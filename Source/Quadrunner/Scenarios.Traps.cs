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
        /// <summary>
        /// A granite box with a single south gap sealed by a player spike trap: the only way in or out crosses the
        /// trap, so any route to something inside enters a known trap cell (plan 0011 fixtures). Returns the trap cell.
        /// </summary>
        private static IntVec3 TrapBox(Scenario s, Map map, CellRect site)
        {
            IntVec3 gap = new IntVec3(site.CenterCell.x, 0, site.minZ);
            foreach (IntVec3 c in site.EdgeCells.Where(c => c != gap))
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.BlocksGranite, c, Rot4.North);
            var trap = (Building_Trap)ThingMaker.MakeThing(ThingDefOf.TrapSpike, ThingDefOf.Steel);
            trap.SetFaction(Faction.OfPlayer);
            s.Track(GenSpawn.Spawn(trap, gap, map));
            return gap;
        }

        /// <summary>Whether <paramref name="pawn"/> stands on one of the fixture's trap cells.</summary>
        private static bool OnTrapCell(Pawn pawn, List<IntVec3> traps) =>
            pawn != null && pawn.Spawned && traps.Contains(pawn.Position);

        /// <summary>Within two cells of a fixture trap (a standoff cell is adjacent to it).</summary>
        private static bool NearTrap(Pawn pawn, List<IntVec3> traps) =>
            pawn != null && pawn.Spawned && traps.Any(t => t.DistanceTo(pawn.Position) <= 2f);

        /// <summary>
        /// A granite box whose only door is a spike trap holds gold; dirt waits outside near the stable. The dirt is
        /// cleaned (its routes cross no trap), the gold inside is never hauled through the trap, and no dog ever
        /// stands on the trap (plan 0011: peacetime dispatch defers trap-locked work and routes around traps).
        /// </summary>
        private static void TrapAvoidanceWork()
        {
            var s = new Scenario("Trap avoidance: peacetime work", 9000);
            string dogId = null, farGoldId = null;
            var filthIds = new List<string>();
            var traps = new List<IntVec3>();
            CellRect pile = default;
            IntVec3 farGoldCell = default;
            int windowStart = 0;
            bool enteredTrap = false;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            void LatchTrap()
            {
                if (OnTrapCell(Dog(), traps))
                    enteredTrap = true;
            }
            s.Do("a stable with one worker beside a trapped box of gold, and dirt near the stable", () =>
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
                    // The worker starts just outside the gap (south of it), so the gold is inside its 15-cell scan.
                    Teleport(Dog(), OpenCell(map, gap, 2f, 4f, c => c.z < gap.z));
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    pile = ClearRect(map, stable.InteractionCell, 3, 1, 6f, 12f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    for (int i = 0; i < 2; i++)
                        filthIds.Add(SpawnFilth(s, OpenCell(map, stable.InteractionCell, 4f, 10f)));
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
                .Expect("no robodog ever stood on the trap", () => !enteredTrap)
                .Expect("the gold inside the box stays put: its haul is deferred, not taken through the trap", () =>
                    CountIn(pile, ThingDefOf.Gold) == 0 && Scenario.ById(farGoldId) != null
                    && Scenario.ById(farGoldId).Position == farGoldCell)
                .Summary(() => $"gold {(Scenario.ById(farGoldId) == null ? "gone" : $"at {Scenario.ById(farGoldId).Position}")}, " +
                    $"pile {CountIn(pile, ThingDefOf.Gold)}, entered trap {enteredTrap}; worker {Dog()?.CurJob?.ToStringSafe()}")
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
