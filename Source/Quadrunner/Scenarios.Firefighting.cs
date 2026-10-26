using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>Plan 0008 / F1: Medirunners foam out Home-area fires, after rescues, during raids, across a reload.</summary>
    public static partial class Scenarios
    {
        /// <summary>Burning log stacks on a cleared 3×3 rect <paramref name="minDist"/>–<paramref name="maxDist"/> out, its Home flag set to <paramref name="home"/>.</summary>
        private static CellRect SpawnBonfire(Scenario s, IntVec3 near, float minDist, float maxDist, bool home,
            Func<CellRect, bool> extra = null)
        {
            Map map = Find.CurrentMap;
            CellRect rect = ClearRect(map, near, 3, 1, minDist, maxDist, extra);
            // The flag over the rect and a margin, so spreading flames stay on the same side of the boundary.
            foreach (IntVec3 c in rect.ExpandedBy(3).ClipInsideMap(map))
            {
                bool was = map.areaManager.Home[c];
                map.areaManager.Home[c] = home;
                s.OnFinish(() => Find.CurrentMap.areaManager.Home[c] = was);
            }
            // Firefoam left by an earlier phase keeps a cell from igniting.
            foreach (IntVec3 c in rect.ExpandedBy(1).ClipInsideMap(map))
                foreach (Filth filth in c.GetThingList(map).OfType<Filth>().ToList())
                    filth.Destroy();
            for (int x = rect.minX; x <= rect.maxX; x++)
                SpawnStack(s, ThingDefOf.WoodLog, 75, new IntVec3(x, 0, rect.CenterCell.z));
            if (!FireUtility.TryStartFireIn(rect.CenterCell, map, 1f, null))
                throw new InvalidOperationException($"couldn't start a fire at {rect.CenterCell}");
            return rect;
        }

        private static bool FireIn(CellRect rect)
        {
            Map map = Find.CurrentMap;
            return rect.ExpandedBy(3).ClipInsideMap(map).Cells.Any(c => c.GetFirstThing<Fire>(map) != null);
        }

        private static bool Foaming(Pawn medic) => medic.Spawned && medic.CurJobDef == JobDefOf.AttackStatic
            && medic.CurJob.targetA.Thing is Fire && medic.equipment?.PrimaryEq?.PrimaryVerb is Verb_FoamSpray;

        private static void MedirunnerLegacyMigration()
        {
            var s = new Scenario("Medirunner legacy migration", 20000);
            string stableId = null, carrierId = null, patientId = null, modernId = null, modernGunId = null;
            string[] legacyIds = new string[4], gunIds = new string[4];
            float[] charges = { 0.61f, 0.72f, 0f, 0.83f };
            int caravanId = -1, heldJobId = -1;
            CellRect fire = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Legacy(int i) => Scenario.ById(legacyIds[i]) as Pawn
                ?? Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId)?.PawnsListForReading
                    .FirstOrDefault(p => p.ThingID == legacyIds[i]);
            s.OnFinish(() =>
            {
                Caravan caravan = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId);
                if (caravan != null)
                {
                    foreach (Pawn pawn in caravan.PawnsListForReading.ToList())
                    {
                        caravan.RemovePawn(pawn);
                        if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                        else if (!pawn.Destroyed) pawn.Destroy();
                    }
                    caravan.Destroy();
                }
                // Destroying a fixture pawn can retain it in WorldPawns with its carried patient.
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Where(p =>
                             legacyIds.Contains(p.ThingID) || p.ThingID == carrierId || p.ThingID == modernId
                             || p.ThingID == patientId).ToList())
                {
                    pawn.carryTracker?.CarriedThing?.Destroy();
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                }
            });
            s.Do("save pre-sprayer medics docked, in the field, carried and in a caravan", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    for (int i = 0; i < legacyIds.Length; i++)
                    {
                        Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner));
                        legacyIds[i] = dog.ThingID;
                        dog.Name = new NameSingle($"Legacy medic {i}");
                        CompRobodog.Of(dog).SetBattery(charges[i]);
                    }
                    stable.TryUndock(Legacy(1));
                    SurveyHold(Legacy(1), 20000);
                    heldJobId = Legacy(1).CurJob.loadID;
                    Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, Legacy(1), Legacy(1).RaceProps.body.corePart);
                    wound.Severity = 2f;
                    Legacy(1).health.AddHediff(wound);
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, Legacy(1).Position);
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 2f);
                    if (Legacy(1).carryTracker.TryStartCarry(patient, 1, reserve: false) != 1)
                        throw new InvalidOperationException("could not stage the medic's carried patient");
                    Pawn carrier = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    carrierId = carrier.ThingID;
                    stable.TryUndock(carrier);
                    SurveyHold(carrier, 20000);
                    stable.TryUndock(Legacy(2));
                    if (carrier.carryTracker.TryStartCarry(Legacy(2), 1, reserve: false) != 1)
                        throw new InvalidOperationException("could not stage the carried medic");
                    stable.TryUndock(Legacy(3));
                    Pawn traveler = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, stable.InteractionCell);
                    caravanId = CaravanMaker.MakeCaravan(new[] { traveler, Legacy(3) }, Faction.OfPlayer, map.Tile, true).ID;
                    for (int i = 0; i < legacyIds.Length; i++)
                    {
                        Pawn dog = Legacy(i);
                        dog.equipment.DestroyAllEquipment();
                        dog.equipment = null;
                        dog.apparel = null;
                    }
                    Pawn modern = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner));
                    modernId = modern.ThingID;
                    Tiers.Arm(modern, Tiers.Gun(modern, 0));
                    modernGunId = modern.equipment.Primary.ThingID;
                    CompRobodog.Of(modern).SetBattery(0.01f);
                })
                .SaveAndReload(always: true)
                .Do("all saved medics gain the sprayer without replacing their identity or state", () =>
                {
                    for (int i = 0; i < legacyIds.Length; i++)
                    {
                        Pawn dog = Legacy(i);
                        if (dog == null || dog.Name.ToStringFull != $"Legacy medic {i}"
                            || CompRobodog.Of(dog).stable?.ThingID != stableId
                            || Math.Abs(CompRobodog.Of(dog).Battery - charges[i]) > 0.0001f)
                            throw new InvalidOperationException($"legacy medic {i} lost identity, affiliation or battery");
                        if (dog.equipment == null || dog.apparel == null
                            || dog.equipment.Primary?.def != RobodogWork.Of(dog).builtInGun
                            || dog.equipment.AllEquipmentListForReading.Count != 1)
                            throw new InvalidOperationException($"legacy medic {i} has no ToolUser trackers and single sprayer");
                        gunIds[i] = dog.equipment.Primary.ThingID;
                    }
                    if (!Stable().Docked.Contains(Legacy(0)) || Legacy(1).CurJob?.loadID != heldJobId
                        || Math.Abs(InjurySeverity(Legacy(1)) - 2f) > 0.001f
                        || Legacy(1).carryTracker.CarriedThing?.ThingID != patientId
                        || ((Pawn)Scenario.ById(carrierId)).carryTracker.CarriedThing != Legacy(2)
                        || !CompRobodog.Of(Legacy(2)).PoweredDown)
                        throw new InvalidOperationException("migration changed docking, a field order, injury or carried pawn");
                    if (((Pawn)Scenario.ById(modernId)).equipment.Primary.ThingID != modernGunId
                        || ((Pawn)Scenario.ById(carrierId)).equipment != null)
                        throw new InvalidOperationException("migration changed a modern medic or a utility dog");
                })
                .SaveAndReload(always: true)
                .Do("a second load keeps the same saved sprayers instead of migrating again", () =>
                {
                    for (int i = 0; i < legacyIds.Length; i++)
                        if (Legacy(i).equipment?.Primary?.ThingID != gunIds[i]
                            || Legacy(i).equipment.AllEquipmentListForReading.Count != 1)
                            throw new InvalidOperationException($"legacy medic {i}'s sprayer was replaced or duplicated");
                    if (((Pawn)Scenario.ById(modernId)).equipment.Primary.ThingID != modernGunId)
                        throw new InvalidOperationException("the modern medic's sprayer was replaced");
                })
                .Do("light a Home fire for the migrated docked medic", () =>
                {
                    Map map = Find.CurrentMap;
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                    fire = SpawnBonfire(s, Stable().InteractionCell, 8f, 14f, home: true);
                    s.OnFinish(() =>
                    {
                        foreach (Fire f in Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>()
                                     .Where(f => fire.ExpandedBy(3).Contains(f.Position)).ToList())
                            f.Destroy();
                    });
                })
                .Expect("the migrated medic undocks and sprays the Home fire", () => Foaming(Legacy(0)))
                .Expect("the Home fire is extinguished", () => !FireIn(fire))
                .Expect("the same migrated medic docks again", () => Stable().Docked.Contains(Legacy(0)))
                .Start();
        }

        private static void MedirunnerFirefighting()
        {
            var s = new Scenario("Medirunner firefighting", 40000);
            string stableId = null, medicId = null, patientId = null;
            CellRect home = default, outside = default, second = default, third = default;
            float lowest = 1f, drain = 0f;
            int queuedAt = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Patient() => (Pawn)Scenario.ById(patientId);
            bool Watch()
            {
                lowest = Math.Min(lowest, CompRobodog.Of(Medic())?.Battery ?? 1f);
                return true;
            }
            s.Do("a powered stable with a full Medirunner; log fires inside and outside the Home area", () =>
                {
                    Map map = Find.CurrentMap;
                    drain = QR_DefOf.QR_Medirunner.race.GetModExtension<RobodogWork>().foamDrainPerBurst;
                    // Rain puts out unroofed fires (Fire.Tick): keep the sky clear, or a bonfire may die before it is seen.
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    CompRobodog.Of(Medic()).SetBattery(1f);
                    home = SpawnBonfire(s, stable.InteractionCell, 8f, 14f, home: true);
                    outside = SpawnBonfire(s, stable.InteractionCell, 26f, 36f, home: false);
                    s.OnFinish(() =>
                    {
                        foreach (Fire f in Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().ToList())
                            f.Destroy();
                    });
                })
                .Expect("the Home fire is queued and the outside one is not", () =>
                {
                    Map map = Find.CurrentMap;
                    List<NetworkTask> fires = Net().WarQueue.Where(t => t.kind == NetworkTaskKind.Fire).ToList();
                    if (fires.Any(t => t.Target(map) is Thing f && !map.areaManager.Home[f.Position]))
                        throw new InvalidOperationException("a fire outside the Home area was queued");
                    queuedAt = Find.TickManager.TicksGame;
                    return fires.Any(t => t.Target(map) is Thing f && home.ExpandedBy(3).Contains(f.Position));
                })
                .Expect("the Medirunner takes it without a retry backoff or undock/dock churn", () =>
                {
                    Map map = Find.CurrentMap;
                    NetworkTask task = Net().WarQueue.FirstOrDefault(t => t.kind == NetworkTaskKind.Fire
                        && t.Target(map) is Thing f && home.ExpandedBy(3).Contains(f.Position));
                    if (task == null)
                        throw new InvalidOperationException("the Home fire left the queue unassigned");
                    if (task.assigneeId == medicId)
                        return true;
                    if (task.retryAt > Find.TickManager.TicksGame)
                        throw new InvalidOperationException($"the Home fire got a retry backoff to {task.retryAt}");
                    int waited = Find.TickManager.TicksGame - queuedAt;
                    if (waited > 2 * QR_DefOf.QR_Medirunner.race.GetModExtension<RobodogWork>().dispatchInterval)
                        throw new InvalidOperationException($"no medic took the Home fire after {waited} ticks");
                    return false;
                })
                .Expect("the Medirunner undocks and foams it with its built-in sprayer", () => Watch() && Foaming(Medic()))
                .Do("put out the fire outside Home (never queued)", () =>
                {
                    Map map = Find.CurrentMap;
                    if (!FireIn(outside))
                        throw new InvalidOperationException("the fire outside Home went out before the check");
                    foreach (Fire f in map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>()
                                 .Where(f => !map.areaManager.Home[f.Position]).ToList())
                        f.Destroy();
                })
                .SaveAndReload()
                .Expect("the Home fire is out and the logs survive", () => Watch() && !FireIn(home)
                    && home.Cells.Any(c => c.GetFirstThing(Find.CurrentMap, ThingDefOf.WoodLog) != null))
                .Expect("spraying cost at least one burst of battery", () =>
                    Watch() && lowest <= 1f - drain + 0.0001f)
                .Expect("the Medirunner docks again", () => Stable().Docked.Contains(Medic()))
                .Do("a bleeding colonist down and a new Home fire at once", () =>
                {
                    Map map = Find.CurrentMap;
                    SpawnMedicalBed(s, Stable().InteractionCell, 3f, 10f);
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer,
                        OpenCell(map, Stable().InteractionCell, 16f, 24f));
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 3f);
                    second = SpawnBonfire(s, Stable().InteractionCell, 8f, 16f, home: true);
                    Net().Tick(force: true);
                })
                .Expect("the rescue comes first while the fire waits", () =>
                    Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient()
                    && Net().WarQueue.Any(t => t.kind == NetworkTaskKind.Fire && t.assigneeId == null))
                .Expect("the patient reaches a bed", () => Patient().InBed())
                .Expect("then the Medirunner foams the fire", () => Foaming(Medic()) || !FireIn(second))
                .Expect("the second fire is out", () => !FireIn(second))
                // The rest of the burst is still in flight: foam landing on a new bonfire would put it out unseen.
                .Wait(300)
                .Do("a raider walled in nearby and a third Home fire", () =>
                {
                    Map map = Find.CurrentMap;
                    CellRect box = ClearRect(map, Stable().InteractionCell, 3, 1, 18f, 30f);
                    foreach (IntVec3 c in box.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    Faction pirates = Pirates();
                    Pawn raider = SpawnPawn(s, pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate, pirates, box.CenterCell);
                    raider.equipment?.DestroyAllEquipment();
                    raider.inventory?.DestroyAll();
                    LordMaker.MakeNewLord(pirates, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                    Raid.Recheck(map);
                    third = SpawnBonfire(s, Stable().InteractionCell, 8f, 16f, home: true,
                        r => r.CenterCell.DistanceTo(second.CenterCell) >= 10f);
                })
                .Expect("a raid is on", () => Raid.Active(Find.CurrentMap) && Net().War)
                .Expect("the Medirunner foams the fire under the raid", () => Foaming(Medic()));
            CaptureArtwork(s, "medirunner-foam", () =>
            {
                int width = UnityEngine.Screen.width, height = UnityEngine.Screen.height;
                UnityEngine.Screen.SetResolution(1280, 720, false);
                s.OnFinish(() => UnityEngine.Screen.SetResolution(width, height, false));
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Find.CameraDriver.SetRootPosAndSize(Medic().DrawPos, 8f);
            }, delaySeconds: 1f);
            s.Do("resume", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast)
                .Expect("the third fire is out", () => !FireIn(third))
                .Summary(() => $"lowest battery {lowest:P1} (burst {drain:P0}); weather {Find.CurrentMap.weatherManager.curWeather.defName}; third fire burning {FireIn(third)}; {Net().Inspect(Medic())}")
                .Start();
        }
    }
}
