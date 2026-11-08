using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>Plan 0011: robodogs avoid known traps — peacetime dispatch routes and targets around them, and
    /// wartime pursuit stops one tile short of the first trap and holds there.</summary>
    public static partial class Scenarios
    {
        /// <summary>A player spike trap at <paramref name="cell"/> (plan 0011 fixtures).</summary>
        private static void SpawnTrap(Scenario s, Map map, IntVec3 cell)
        {
            var trap = (Building_Trap)ThingMaker.MakeThing(ThingDefOf.TrapSpike, ThingDefOf.Steel);
            trap.SetFaction(Faction.OfPlayer);
            s.Track(GenSpawn.Spawn(trap, cell, map));
        }

        /// <summary>
        /// A granite box with a single south gap sealed by a player spike trap: the only way in or out crosses the
        /// trap, so any route to something inside enters a known trap cell (plan 0011 fixtures). Returns the trap cell.
        /// </summary>
        private static IntVec3 TrapBox(Scenario s, Map map, CellRect site)
        {
            IntVec3 gap = new IntVec3(site.CenterCell.x, 0, site.minZ);
            foreach (IntVec3 c in site.EdgeCells.Where(c => c != gap))
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, c, Rot4.North);
            SpawnTrap(s, map, gap);
            return gap;
        }

        /// <summary>Whether <paramref name="pawn"/> stands on one of the fixture's trap cells.</summary>
        private static bool OnTrapCell(Pawn pawn, List<IntVec3> traps) =>
            pawn != null && pawn.Spawned && traps.Contains(pawn.Position);

        /// <summary>Within two cells of a fixture trap (a standoff cell is adjacent to it).</summary>
        private static bool NearTrap(Pawn pawn, List<IntVec3> traps) =>
            pawn != null && pawn.Spawned && traps.Any(t => t.DistanceTo(pawn.Position) <= 2f);

        /// <summary>
        /// A 1-wide sealed granite corridor (plan 0011 fire fixture): a wood-fed fire burns at the north end, 16
        /// cells north of a spike trap — farther than the sprayer's 14.9-cell range from the south end — so every
        /// cast position a medic could walk to lies across the trap while none exists on its own side. Returns the
        /// fire's cell; the trap's comes back in <paramref name="trapCell"/>.
        /// </summary>
        private static IntVec3 TrapCorridor(Scenario s, Map map, CellRect site, out IntVec3 trapCell)
        {
            int x = site.CenterCell.x;
            for (int z = site.minZ; z <= site.maxZ; z++)
            {
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, new IntVec3(x - 1, 0, z), Rot4.North);
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, new IntVec3(x + 1, 0, z), Rot4.North);
            }
            SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, new IntVec3(x, 0, site.minZ), Rot4.North);
            SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, new IntVec3(x, 0, site.maxZ), Rot4.North);
            trapCell = new IntVec3(x, 0, site.maxZ - 17);
            SpawnTrap(s, map, trapCell);
            IntVec3 fireCell = new IntVec3(x, 0, site.maxZ - 1);
            var wood = ThingMaker.MakeThing(ThingDefOf.WoodLog);
            wood.stackCount = 75;
            s.Track(GenSpawn.Spawn(wood, fireCell, map));
            if (!FireUtility.TryStartFireIn(fireCell, map, 1f, null))
                throw new InvalidOperationException($"couldn't start a corridor fire at {fireCell}");
            bool wasHome = map.areaManager.Home[fireCell];
            map.areaManager.Home[fireCell] = true;
            s.OnFinish(() => Find.CurrentMap.areaManager.Home[fireCell] = wasHome);
            return fireCell;
        }

        /// <summary>The first node of <paramref name="root"/>'s tree, depth-first, matching <paramref name="match"/>.</summary>
        private static ThinkNode FindNode(ThinkNode root, Func<ThinkNode, bool> match)
        {
            if (match(root)) return root;
            foreach (ThinkNode child in root.subNodes)
            {
                ThinkNode found = FindNode(child, match);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// A granite box whose only door is a spike trap holds gold; dirt waits outside near the stable. The dirt is
        /// cleaned (its routes cross no trap), the gold inside is never hauled through the trap, and no dog ever
        /// stands on the trap (plan 0011: peacetime dispatch defers trap-locked work and routes around traps). Two
        /// fallbacks are proven directly in the same fixture: the idle wander node, offered only trap-crossing
        /// destinations from a pocket sealed by the trap, must answer with its wait job — never no job at all (which
        /// the think tree answers with JobGiver_IdleError) and never a wander into the wire — and a medic's job
        /// builder must refuse a fire whose every cast position is across a trap, on claim and on every mid-fight
        /// walk rebuild alike.
        /// </summary>
        private static void TrapAvoidanceWork()
        {
            var s = new Scenario("Trap avoidance: peacetime work", 9000);
            string dogId = null, medicId = null, farGoldId = null;
            var filthIds = new List<string>();
            var traps = new List<IntVec3>();
            CellRect pile = default;
            IntVec3 farGoldCell = default, workerStart = default;
            int windowStart = 0;
            bool enteredTrap = false, wanderBad = false, firePlumbingOk = false, fireWalkRefused = false;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            void LatchTrap()
            {
                if (OnTrapCell(Dog(), traps) || OnTrapCell(Medic(), traps))
                    enteredTrap = true;
            }
            s.Do("a stable with one worker beside a trapped box of gold, one medic, a trapped fire corridor, and dirt near the stable", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    CellRect box = ClearRect(map, stable.InteractionCell, 9, 2, 14f, 28f);
                    IntVec3 gap = TrapBox(s, map, box);
                    traps.Add(gap);
                    var gold = ThingMaker.MakeThing(ThingDefOf.Gold);
                    gold.stackCount = 30;
                    farGoldId = s.Track(GenSpawn.Spawn(gold, box.CenterCell, map)).ThingID;
                    farGoldCell = box.CenterCell;
                    // The worker starts just outside the gap (3–4 cells south, clear of the wander pocket), so the
                    // gold is inside its 15-cell scan.
                    workerStart = OpenCell(map, gap, 3f, 4f, c => c.z < gap.z - 2);
                    Teleport(Dog(), workerStart);
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    // The idle wander fallback, exercised directly on the loaded tree's own node (the network's Wait
                    // standby usually shadows it in the tree): sealed in a pocket whose only exit is the trap, every
                    // destination it can be offered crosses the trap.
                    IntVec3 pocket = new IntVec3(gap.x, 0, gap.z - 1);
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, pocket + IntVec3.West, Rot4.North);
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, pocket + IntVec3.East, Rot4.North);
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, pocket + IntVec3.South, Rot4.North);
                    Teleport(Dog(), pocket);
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    var wander = (JobGiver_WanderAvoidTraps)FindNode(
                        DefDatabase<ThinkTreeDef>.GetNamed("QR_Robodog").thinkRoot, n => n is JobGiver_WanderAvoidTraps);
                    for (int i = 0; i < 40; i++)
                    {
                        Job offered = wander.TryIssueJobPackage(Dog(), default).Job;
                        if (offered == null || offered.def == JobDefOf.GotoWander) wanderBad = true;
                        if (offered != null) JobMaker.ReturnToPool(offered);
                    }
                    Teleport(Dog(), workerStart);
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    pile = ClearRect(map, stable.InteractionCell, 3, 1, 6f, 12f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    for (int i = 0; i < 2; i++)
                        filthIds.Add(SpawnFilth(s, OpenCell(map, stable.InteractionCell, 4f, 10f)));
                    // The fire corridor: in sprayer range the medic is handed the fire (the plumbing works); from
                    // the far side of the trap every cast position is across it, so the walk must be refused.
                    CellRect corridor = ClearRect(map, stable.InteractionCell, 24, 2, 16f, 60f);
                    IntVec3 fireCell = TrapCorridor(s, map, corridor, out IntVec3 corridorTrap);
                    traps.Add(corridorTrap);
                    Pawn medic = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner));
                    medicId = medic.ThingID;
                    CompRobodog.Of(medic).SetBattery(1f);
                    Fire fire = fireCell.GetFirstThing<Fire>(map);
                    Teleport(medic, new IntVec3(fireCell.x, 0, fireCell.z - 4));
                    Job near = Firefighting.JobFor(medic, fire);
                    firePlumbingOk = near != null;
                    string nearDef = near?.def.defName;
                    if (near != null) JobMaker.ReturnToPool(near);
                    Teleport(medic, new IntVec3(corridorTrap.x, 0, corridorTrap.z - 5));
                    bool enters = TrapAvoidance.RouteEntersTrap(medic, fire, PathEndMode.Touch, out IntVec3 hold);
                    Job far = Firefighting.JobFor(medic, fire);
                    fireWalkRefused = far == null;
                    string farDef = far?.def.defName;
                    IntVec3 farA = far?.targetA.Cell ?? IntVec3.Invalid;
                    QR.Log($"probe: far={farDef} A={farA} entersFire={enters} hold={hold} medic={medic.Position} " +
                        $"trap={corridorTrap} fire={fireCell} knowTrap={TrapAvoidance.KnownTrapAt(corridorTrap, map, medic)} nearDef={nearDef}");
                    if (far != null)
                        QR.Log($"probe TAKEN: entersCastCell={TrapAvoidance.RouteEntersTrap(medic, farA, PathEndMode.OnCell, out IntVec3 hold2)}" +
                            $" hold2={hold2} trapEdifice={corridorTrap.GetEdifice(map)}");
                    if (far != null) JobMaker.ReturnToPool(far);
                    Teleport(medic, OpenCell(map, stable.InteractionCell, 3f, 8f));
                })
                .Do("observe at normal speed", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("the dirt near the stable is cleaned (its routes cross no trap)", () =>
                {
                    LatchTrap();
                    return filthIds.All(id => Scenario.ById(id) == null);
                })
                .Do("note the tick", () => windowStart = Find.TickManager.TicksGame)
                .Expect("600 ticks pass with the worker around the trapped box", () =>
                {
                    LatchTrap();
                    return Find.TickManager.TicksGame - windowStart >= 600;
                })
                .Expect("no robodog ever stood on a trap", () => !enteredTrap)
                .Expect("the idle wander fallback, offered only trap-crossing destinations, waits instead of erroring or wandering in", () => !wanderBad)
                .Expect("a fire whose every cast position is across a trap is refused; one in sprayer range is taken", () => fireWalkRefused && firePlumbingOk)
                .Expect("the gold inside the box stays put: its haul is deferred, not taken through the trap", () =>
                    CountIn(pile, ThingDefOf.Gold) == 0 && Scenario.ById(farGoldId) != null
                    && Scenario.ById(farGoldId).Position == farGoldCell)
                .Summary(() => $"gold {(Scenario.ById(farGoldId) == null ? "gone" : $"at {Scenario.ById(farGoldId).Position}")}, " +
                    $"pile {CountIn(pile, ThingDefOf.Gold)}, entered trap {enteredTrap}; wander {(wanderBad ? "BAD" : "ok")}, " +
                    $"fire walk {(fireWalkRefused ? "refused" : "TAKEN")}, plumbing {(firePlumbingOk ? "ok" : "BROKEN")}; " +
                    $"worker {Dog()?.CurJob?.ToStringSafe()}, medic {Medic()?.CurJob?.ToStringSafe()}")
                .Start();
        }

        /// <summary>
        /// An unarmed raider waits inside the trapped box, around the corner from the gap so it never sees (and
        /// never charges) the dog outside. The deployed Combatdog never touches the trap: it stops one tile short
        /// of the gap, holds the standoff calmly for 600+ ticks while the raider lives, and leaves once the raider
        /// dies (plan 0011: wartime pursuit holds before traps instead of wading in).
        /// </summary>
        private static void TrapStandoffWar()
        {
            var s = new Scenario("Trap standoff: wartime pursuit", 9000);
            string combatId = null, raiderId = null;
            var traps = new List<IntVec3>();
            int holdStart = 0;
            bool enteredTrap = false;
            Pawn Dog() => (Pawn)Scenario.ById(combatId);
            void LatchTrap()
            {
                if (OnTrapCell(Dog(), traps))
                    enteredTrap = true;
            }
            s.Do("a stable with a docked Combatdog; a raider inside a trapped box", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    combatId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    CellRect box = ClearRect(map, stable.InteractionCell, 9, 2, 14f, 28f);
                    traps.Add(TrapBox(s, map, box));
                    // The north-west corner: its defend-point lord keeps it there, out of sight of the gap, so it
                    // never charges out through the trap on its own.
                    IntVec3 corner = new IntVec3(box.minX + 2, 0, box.maxZ - 2);
                    raiderId = SpawnRaider(s, corner).ThingID;
                    Raid.Recheck(map);
                })
                .Do("observe at normal speed", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("the Combatdog deploys for the raid", () => Dog() != null && Dog().Spawned)
                .Expect("it stops one tile short of the trapped gap and waits there", () =>
                {
                    LatchTrap();
                    return NearTrap(Dog(), traps) && !OnTrapCell(Dog(), traps)
                        && Dog().CurJobDef == JobDefOf.Wait;
                })
                .Do("note the hold tick", () => holdStart = Find.TickManager.TicksGame)
                .Expect("it holds the standoff calmly for 600 ticks while the raider lives", () =>
                {
                    LatchTrap();
                    return Find.TickManager.TicksGame - holdStart >= 600
                        && NearTrap(Dog(), traps) && !OnTrapCell(Dog(), traps)
                        && Dog().CurJobDef == JobDefOf.Wait && Standing(raiderId);
                })
                .Do("the raider dies", () =>
                {
                    if (Scenario.ById(raiderId) is Pawn raider && !raider.Dead)
                        raider.Kill(null);
                })
                .Expect("no robodog ever stood on the trap", () => !enteredTrap)
                .Expect("with the threat gone it leaves the standoff", () =>
                    Dog() == null || !Dog().Spawned || !NearTrap(Dog(), traps) || Dog().CurJobDef != JobDefOf.Wait)
                .Summary(() => Dog() == null ? "dog gone" :
                    $"dog {Dog().CurJob?.ToStringSafe()} at {Dog().Position}, trap {traps[0]}, " +
                    $"gap distance {traps[0].DistanceTo(Dog().Position):F1}, entered trap {enteredTrap}; raider {Fate(raiderId)}")
                .Start();
        }
    }
}
