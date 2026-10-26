using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
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
