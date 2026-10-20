using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>T5: Medirunners rescue the downed: rank order, fastest-bleeding first, one per patient, under fire.</summary>
    public static partial class Scenarios
    {
        /// <summary>
        /// Downs <paramref name="p"/> with anesthetic and a torso cut of severity <paramref name="cut"/>, which sets its
        /// bleeding; the cut, anesthetic and blood loss are taken off again at the end.
        /// </summary>
        private static void DownBleeding(Scenario s, Pawn p, float cut)
        {
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, p, p.RaceProps.body.corePart);
            wound.Severity = cut;
            p.health.AddHediff(wound);
            p.health.AddHediff(HediffDefOf.Anesthetic);
            if (!p.Downed)
                throw new InvalidOperationException($"{p} isn't downed");
            string id = p.ThingID;
            s.OnFinish(() =>
            {
                if (!(Scenario.ById(id) is Pawn q) || q.Destroyed)
                    return;
                foreach (Hediff h in q.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Cut
                             || h.def == HediffDefOf.Anesthetic || h.def == HediffDefOf.BloodLoss).ToList())
                    q.health.RemoveHediff(h);
            });
        }

        private static Pawn SpawnPawn(Scenario s, PawnKindDef kind, Faction faction, IntVec3 cell) =>
            s.Track((Pawn)GenSpawn.Spawn(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
                forceGenerateNewPawn: true)), cell, Find.CurrentMap));

        private static Faction Pirates() =>
            Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate) ?? Find.FactionManager.RandomEnemyFaction();

        /// <summary>A medical bed on clear ground <paramref name="minDist"/>–<paramref name="maxDist"/> cells from <paramref name="near"/>.</summary>
        private static Building_Bed SpawnMedicalBed(Scenario s, IntVec3 near, float minDist, float maxDist)
        {
            CellRect r = ClearRect(Find.CurrentMap, near, 3, 0, minDist, maxDist);
            var bed = (Building_Bed)SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel, r.CenterCell + IntVec3.South, Rot4.North);
            bed.Medical = true;
            return bed;
        }

        private static bool Rescuing(Pawn medic) => medic.Spawned
            && (medic.CurJobDef == JobDefOf.Rescue || medic.CurJobDef == JobDefOf.TakeWoundedPrisonerToBed);

        /// <summary>
        /// The plan's scenario: three colonists down, bleeding at different rates, by a live raider (walled in, so it
        /// can't hurt anyone, yet the raid is on and the enemy near); one Medirunner takes them to bed fastest-bleeding
        /// first, across a reload while it carries the first.
        /// </summary>
        private static void MassCasualtyRaid()
        {
            var s = new Scenario("Mass-casualty raid", 30000);
            string stableId = null, medicId = null;
            var patientIds = new List<string>();
            string bleeds = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Patient(int i) => (Pawn)Scenario.ById(patientIds[i]);
            s.Do("a stable with a Medirunner, three medical beds; three colonists down 18–28 cells out, a raider walled in by them", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    for (int i = 0; i < 3; i++)
                        SpawnMedicalBed(s, stable.InteractionCell, 3f, 12f);
                    IntVec3 field = OpenCell(map, stable.InteractionCell, 18f, 28f);
                    List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).Take(3).ToList();
                    while (colonists.Count < 3)
                        colonists.Add(SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, field));
                    // Cut severities 3, 9, 6: the second bleeds fastest, then the third, then the first.
                    float[] cuts = { 3f, 9f, 6f };
                    for (int i = 0; i < 3; i++)
                    {
                        Teleport(colonists[i], OpenCell(map, field, 0f, 4f));
                        DownBleeding(s, colonists[i], cuts[i]);
                    }
                    patientIds.AddRange(colonists.OrderByDescending(p => p.health.hediffSet.BleedRateTotal).Select(p => p.ThingID));
                    bleeds = string.Join(", ", colonists.OrderByDescending(p => p.health.hediffSet.BleedRateTotal)
                        .Select(p => $"{p.LabelShort} {p.health.hediffSet.BleedRateTotal:F2}"));
                    CellRect box = ClearRect(map, field, 3, 1, 6f, 14f);
                    foreach (IntVec3 c in box.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    Faction pirates = Pirates();
                    Pawn raider = SpawnPawn(s, pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate, pirates, box.CenterCell);
                    raider.equipment?.DestroyAllEquipment();
                    raider.inventory?.DestroyAll();
                    LordMaker.MakeNewLord(pirates, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                })
                .Expect("a raid is on, the enemy near the patients", () =>
                    Raid.Active(Find.CurrentMap) && GenAI.EnemyIsNear(Patient(0), 25f))
                .Expect("the Medirunner goes for the fastest-bleeding first", () =>
                    Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(0))
                .Expect("it carries them", () => Medic().carryTracker.CarriedThing == Patient(0))
                .SaveAndReload()
                .Expect("first patient in bed", () => Patient(0).InBed())
                .Expect("then the next-fastest", () => Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(1))
                .Expect("second patient in bed", () => Patient(1).InBed())
                .Expect("then the last", () => Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(2))
                .Expect("third patient in bed", () => Patient(2).InBed())
                .Expect("the Medirunner docks", () => Stable().Docked.Contains(Medic()))
                .Summary(() => $"bleed rates {bleeds}")
                .Start();
        }

        /// <summary>
        /// A colonist, a guest, a prisoner, a colony animal and an enemy down at once, the prisoner bleeding fastest and
        /// the colonist slowest; two Medirunners: two patients at once, rescues started in rank order regardless of
        /// bleeding, the prisoner to the prison cell, the enemy never; a reload partway.
        /// </summary>
        private static void RescueOrder()
        {
            var s = new Scenario("Rescue order", 30000);
            string stableId = null, enemyId = null;
            var medicIds = new List<string>();
            var rankIds = new List<string>(); // colonist, guest, prisoner, animal
            var started = new Dictionary<string, int>();
            bool together = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            List<Pawn> Medics() => medicIds.Select(id => (Pawn)Scenario.ById(id)).ToList();
            Pawn Ranked(int i) => (Pawn)Scenario.ById(rankIds[i]);
            Pawn Enemy() => (Pawn)Scenario.ById(enemyId);
            void Watch()
            {
                List<Thing> targets = Medics().Where(Rescuing).Select(m => m.CurJob.targetA.Thing).ToList();
                foreach (Thing t in targets.Where(t => !started.ContainsKey(t.ThingID)))
                    started[t.ThingID] = Find.TickManager.TicksGame;
                together |= targets.Count == 2 && targets[0] != targets[1];
            }
            s.Do("a stable with two Medirunners; two medical beds, a prison cell, an animal spot; five pawns down 20–30 cells out", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 2; i++)
                        medicIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID);
                    for (int i = 0; i < 2; i++)
                        SpawnMedicalBed(s, stable.InteractionCell, 3f, 12f);
                    // The prisoner bed goes in before the walls, so the new room is a prison cell from the start.
                    CellRect cell = ClearRect(map, stable.InteractionCell, 5, 1, 6f, 16f);
                    var prisonBed = (Building_Bed)SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel,
                        new IntVec3(cell.minX + 1, 0, cell.minZ + 1), Rot4.North);
                    prisonBed.ForPrisoners = true;
                    BuildRoom(s, cell, batteryPct: -1f);
                    SpawnBuilding(s, DefDatabase<ThingDef>.GetNamed("AnimalSleepingSpot"), null,
                        OpenCell(map, stable.InteractionCell, 3f, 10f), Rot4.North);

                    IntVec3 field = OpenCell(map, stable.InteractionCell, 20f, 30f);
                    IntVec3 Near() => OpenCell(map, field, 0f, 5f);
                    Pawn colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed)
                        ?? SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, field);
                    Teleport(colonist, Near());
                    Faction friendly = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer
                        && !f.Hidden && f.def.humanlikeFaction && f.def.basicMemberKind != null && !f.HostileTo(Faction.OfPlayer));
                    Pawn guest = SpawnPawn(s, friendly?.def.basicMemberKind ?? PawnKindDefOf.Colonist, friendly, Near());
                    Faction pirates = Pirates();
                    PawnKindDef pirate = pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate;
                    Pawn prisoner = SpawnPawn(s, pirate, pirates, Near());
                    prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                    Pawn animal = SpawnPawn(s, DefDatabase<PawnKindDef>.GetNamed("Husky"), Faction.OfPlayer, Near());
                    Pawn enemy = SpawnPawn(s, pirate, pirates, Near());
                    enemy.equipment?.DestroyAllEquipment();
                    // Bleeding against the rank order: the prisoner fastest, the colonist slowest.
                    DownBleeding(s, colonist, 2f);
                    DownBleeding(s, guest, 6f);
                    DownBleeding(s, prisoner, 10f);
                    DownBleeding(s, animal, 4f);
                    DownBleeding(s, enemy, 8f);
                    rankIds.AddRange(new[] { colonist, guest, prisoner, animal }.Select(p => p.ThingID));
                    enemyId = enemy.ThingID;
                    if (new[] { colonist, guest, prisoner, animal }.Select(Rescue.Rank).SequenceEqual(new[] { 0, 1, 2, 3 }) == false
                        || Rescue.Rank(enemy) != -1)
                        throw new InvalidOperationException("ranks: " + string.Join(", ",
                            new[] { colonist, guest, prisoner, animal, enemy }.Select(p => $"{p.LabelShort} {Rescue.Rank(p)}")));
                })
                .Expect("both Medirunners out at once, on two different patients", () =>
                {
                    Watch();
                    return together;
                })
                .SaveAndReload()
                .Expect("colonist, guest, prisoner and animal all in bed", () =>
                {
                    Watch();
                    return Enumerable.Range(0, 4).All(i => Ranked(i).InBed());
                })
                .Expect("rescues started in rank order, bleeding aside", () =>
                {
                    List<int> ticks = rankIds.Select(id => started.TryGetValue(id, out int t) ? t : -1).ToList();
                    if (ticks.Contains(-1) || Enumerable.Range(1, 3).Any(i => ticks[i] < ticks[i - 1]))
                        throw new InvalidOperationException("start ticks (colonist, guest, prisoner, animal): " + string.Join(", ", ticks));
                    return true;
                })
                .Expect("the prisoner lies in the prison cell", () => Ranked(2).CurrentBed() is Building_Bed b && b.ForPrisoners)
                .Expect("both Medirunners dock", () => Medics().All(m => Stable().Docked.Contains(m)))
                .Expect("the enemy is left where it fell", () => !started.ContainsKey(enemyId) && Enemy().Spawned && !Enemy().InBed())
                .Summary(() => "start ticks " + string.Join(", ", rankIds.Select(id => started[id])))
                .Start();
        }
    }
}
