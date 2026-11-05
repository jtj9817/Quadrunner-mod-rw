using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Plan 0008 / F3: a deployed Medirunner keeps working the Home fire queue it was sent out for. It starts on the
    /// nearest revealed fire, keeps a still-burning focus even when a closer one is lit, and at every native job
    /// boundary moves straight to the nearest remaining fire instead of walking home. With nothing burning it stands
    /// by for three seconds before docking, stays out while real weather can throw lightning, and only then returns.
    /// </summary>
    public static partial class Scenarios
    {
        /// <summary>Firefighting.StandbyJob's hold at normal speed (3 game seconds).</summary>
        private const int FireStandbyTicks = 180;

        private static Fire JobFire(Job job)
        {
            if (job == null)
                return null;
            if (job.targetA.Thing is Fire a && !a.Destroyed && a.Spawned)
                return a;
            if (job.targetB.Thing is Fire b && !b.Destroyed && b.Spawned)
                return b;
            return null;
        }

        /// <summary>Burning fires on and around <paramref name="rect"/> (spread included).</summary>
        private static List<Fire> FiresIn(CellRect rect, int margin = 3)
        {
            Map map = Find.CurrentMap;
            if (map == null || rect.Width <= 0)
                return new List<Fire>();
            return map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>()
                .Where(f => f.Spawned && !f.Destroyed && rect.ExpandedBy(margin).Contains(f.Position)).ToList();
        }

        private static void DestroyFiresIn(CellRect rect, int margin = 3)
        {
            foreach (Fire fire in FiresIn(rect, margin))
                fire.Destroy();
        }

        private static string StagedRectName(List<(string name, CellRect rect)> staged, IntVec3 cell)
        {
            string name = null;
            float nearest = float.MaxValue;
            foreach ((string candidate, CellRect rect) in staged)
            {
                if (rect.Width <= 0 || !rect.ExpandedBy(3).Contains(cell))
                    continue;
                float distance = rect.CenterCell.DistanceToSquared(cell);
                if (name == null || distance < nearest)
                {
                    name = candidate;
                    nearest = distance;
                }
            }
            return name;
        }

        /// <summary>
        /// The staged fire nearest <paramref name="pawn"/> by squared distance, ties broken by ThingID (the network's
        /// documented choice), reported as its fixture name. Null when no staged fire burns.
        /// </summary>
        private static string NearestStagedFireName(Pawn pawn, List<(string name, CellRect rect)> staged)
        {
            if (pawn == null || !pawn.Spawned)
                return null;
            Fire nearest = null;
            foreach ((string _, CellRect rect) in staged)
            {
                foreach (Fire fire in FiresIn(rect))
                {
                    if (nearest == null
                        || fire.Position.DistanceToSquared(pawn.Position) < nearest.Position.DistanceToSquared(pawn.Position)
                        || fire.Position.DistanceToSquared(pawn.Position) == nearest.Position.DistanceToSquared(pawn.Position)
                        && fire.thingIDNumber < nearest.thingIDNumber)
                        nearest = fire;
                }
            }
            return nearest == null ? null : StagedRectName(staged, nearest.Position);
        }

        /// <summary>
        /// Stops the Core weather's random event makers while keeping the weather def itself (and its lightning event
        /// classes) in place, so a fixture is deterministic without ceasing to be real lightning weather. Restored on
        /// the scenario's cleanup; this only touches timing, never the current weather or its detection.
        /// </summary>
        private static Action SilenceRandomWeatherEvents(WeatherDef weather)
        {
            List<WeatherEventMaker> makers = weather.eventMakers;
            float[] intervals = makers.Select(m => m.averageInterval).ToArray();
            for (int i = 0; i < makers.Count; i++)
                makers[i].averageInterval = 1e9f;
            return () =>
            {
                for (int i = 0; i < makers.Count && i < intervals.Length; i++)
                    makers[i].averageInterval = intervals[i];
            };
        }

        /// <summary>
        /// The Home grid before a fixture set its flags; restoring the whole snapshot after the bonfires' own cell
        /// cleanups keeps overlapping fixtures from leaving Home cells set.
        /// </summary>
        private static bool[] SnapshotHomeArea(Map map)
        {
            var prior = new bool[map.cellIndices.NumGridCells];
            for (int i = 0; i < prior.Length; i++)
                prior[i] = map.areaManager.Home[map.cellIndices.IndexToCell(i)];
            return prior;
        }

        private static void RestoreHomeArea(bool[] prior)
        {
            Map now = Find.CurrentMap;
            if (now == null || now.cellIndices.NumGridCells != prior.Length)
                return;
            for (int i = 0; i < prior.Length; i++)
                now.areaManager.Home[now.cellIndices.IndexToCell(i)] = prior[i];
        }

        /// <summary>No stray fire or foam from an earlier fixture may decide this one.</summary>
        private static void ClearFireFixture(Scenario s, List<(string name, CellRect rect)> staged)
        {
            Map map = Find.CurrentMap;
            foreach (Fire fire in map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().ToList())
                fire.Destroy();
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null)
                    return;
                foreach ((string _, CellRect rect) in staged)
                {
                    if (rect.Width <= 0)
                        continue;
                    foreach (Fire fire in now.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>()
                                 .Where(f => rect.ExpandedBy(8).Contains(f.Position)).ToList())
                        fire.Destroy();
                    foreach (Thing foam in now.listerThings.ThingsOfDef(ThingDefOf.Filth_FireFoam)
                                 .Where(t => rect.ExpandedBy(8).Contains(t.Position)).ToList())
                        foam.Destroy();
                }
            });
        }

        private static void MedirunnerFireContinuity()
        {
            var s = new Scenario("Medirunner fire continuity", 50000);
            const int Hold = FireStandbyTicks, RetargetLimit = 120;
            var staged = new List<(string name, CellRect rect)>();
            var transitions = new List<string>();
            var fireJobs = new List<(int tick, string name)>();
            string stableId = null, medicId = null, failure = null, initialTarget = null, lastFireTarget = null;
            int startedAt = 0, closeSpawnedAt = -1, focusDestroyedAt = -1, retargetAt = -1;
            int standbyStart = -1, addedAt = -1, finalAt = -1;
            bool[] priorHome = null;
            bool holdValidFocus = false, firstPursuitRetainsFire = true;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            Pawn Medic() => Scenario.ById(medicId) as Pawn;
            int Now() => Find.TickManager.TicksGame;
            bool East(IntVec3 cell, IntVec3 from) => cell.x - from.x > Math.Abs(cell.z - from.z);
            bool West(IntVec3 cell, IntVec3 from) => from.x - cell.x > Math.Abs(cell.z - from.z);
            CellRect Rect(string name) => staged.First(p => p.name == name).rect;
            bool AnyBurning() => staged.Any(p => p.rect.Width > 0 && FireIn(p.rect));
            string RectOf(IntVec3 cell) => StagedRectName(staged, cell);
            void Fail()
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
            }
            bool Homeward(Job job)
            {
                Building_Stable stable = Stable();
                if (stable == null)
                    return false;
                if (job.def == QR_DefOf.QR_Dock)
                    return true;
                if (job.targetA.Thing == stable || job.targetB.Thing == stable)
                    return true;
                if (JobFire(job) != null)
                    return false;
                return job.def == JobDefOf.Goto && !job.targetA.HasThing
                    && job.targetA.Cell.InHorDistOf(stable.InteractionCell, 4f);
            }
            void Observe(Pawn pawn, Job job)
            {
                if (pawn.ThingID != medicId)
                    return;
                int now = Now();
                transitions.Add($"+{now - startedAt} {job}");
                Fire fire = JobFire(job);
                if (fire != null)
                {
                    string rect = RectOf(fire.Position);
                    fireJobs.Add((now, rect));
                    lastFireTarget = rect;
                    if (initialTarget == null)
                    {
                        initialTarget = rect;
                        firstPursuitRetainsFire = job.def != JobDefOf.Goto || job.targetB.Thing == fire;
                    }
                    if (holdValidFocus && rect == "close" && FireIn(Rect("near")))
                        failure ??= $"a newly lit closer fire took over the valid near focus at +{now - startedAt}: {job}";
                    if (focusDestroyedAt >= 0 && retargetAt < 0)
                    {
                        string expected = NearestStagedFireName(pawn, staged);
                        if (expected != rect)
                            failure ??= $"at +{now - focusDestroyedAt} ticks after the focus died the Medirunner started on {rect ?? fire.Position.ToString()}, not the nearest remaining {expected ?? "none"}: {job}";
                        else
                            retargetAt = now;
                    }
                    // A native fire job is work: the standby clock restarts once it ends.
                    standbyStart = -1;
                    return;
                }
                if (job.def == JobDefOf.Goto && !job.playerForced && AnyBurning())
                    failure ??= $"fire pursuit lost its Fire intent at +{now - startedAt}: {job}";
                if (Homeward(job))
                {
                    if (AnyBurning())
                        failure ??= $"homeward order with staged fires still burning at +{now - startedAt}: {job}";
                    else if (standbyStart < 0)
                        failure ??= $"homeward order without a standby hold at +{now - startedAt}: {job}";
                    else if (now < standbyStart + Hold)
                        failure ??= $"homeward order {now - standbyStart} ticks into a {Hold}-tick standby at +{now - startedAt}: {job}";
                }
                if (!AnyBurning() && standbyStart < 0)
                    standbyStart = now;
            }
            s.Do("clear weather, a powered stable, and a far Home fire queued before a nearer one", () =>
                {
                    Map map = Find.CurrentMap;
                    WeatherDef priorCur = map.weatherManager.curWeather, priorLast = map.weatherManager.lastWeather;
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                    map.weatherManager.curWeatherAge = 4000;
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        now.weatherManager.curWeather = priorCur;
                        now.weatherManager.lastWeather = priorLast;
                    });
                    DraftColonists(s);
                    ClearFireFixture(s, staged);
                    priorHome = SnapshotHomeArea(map);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    CompRobodog.Of(Medic()).SetBattery(1f);
                    IntVec3 door = stable.InteractionCell;
                    // The farther west fire is lit and queued first; the nearer east one second. The network must let
                    // the deployed Medirunner choose the nearer of the two, not simply take the older order.
                    CellRect far = SpawnBonfire(s, door, 26f, 36f, home: true, r => West(r.CenterCell, door));
                    staged.Add(("far", far));
                    CellRect near = SpawnBonfire(s, door, 18f, 24f, home: true,
                        r => East(r.CenterCell, door) && r.CenterCell.DistanceTo(far.CenterCell) >= 18f);
                    staged.Add(("near", near));
                    // Registered after the bonfires' own Home cleanups, so their overlapping edge cells restore last.
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    if (far.CenterCell.DistanceTo(door) <= near.CenterCell.DistanceTo(door))
                        throw new InvalidOperationException("fixture: the first-queued fire is not the farther one");
                    startedAt = Now();
                    ObservePackJobs(s, Observe);
                    Net().Tick(force: true);
                    List<NetworkTask> fires = Net().WarQueue.Where(t => t.kind == NetworkTaskKind.Fire).ToList();
                    NetworkTask farTask = fires.FirstOrDefault(t => t.Target(map) is Fire f
                        && far.ExpandedBy(3).Contains(f.Position));
                    NetworkTask nearTask = fires.FirstOrDefault(t => t.Target(map) is Fire f
                        && near.ExpandedBy(3).Contains(f.Position));
                    if (farTask == null || nearTask == null || farTask.id >= nearTask.id)
                        throw new InvalidOperationException("fixture: the farther fire was not queued before the nearer one");
                })
                .Expect("the deployed Medirunner starts on the nearest Home fire, not the first-queued far one", () =>
                {
                    Fail();
                    if (initialTarget == null)
                        return false;
                    if (initialTarget != "near")
                        throw new InvalidOperationException($"the Medirunner chose the {initialTarget} fire instead of the nearer one; "
                            + string.Join("; ", transitions));
                    if (!firstPursuitRetainsFire)
                        throw new InvalidOperationException("the fire pursuit Goto did not retain the chosen fire as its intent; "
                            + string.Join("; ", transitions));
                    return true;
                })
                .Do("a new Home fire is lit right beside the pursuing Medirunner", () =>
                {
                    Fail();
                    Pawn medic = Medic();
                    IntVec3 ahead = Rect("near").CenterCell - medic.Position;
                    CellRect close = SpawnBonfire(s, medic.Position, 5f, 9f, home: true, r =>
                    {
                        IntVec3 offset = r.CenterCell - medic.Position;
                        long dot = (long)offset.x * ahead.x + (long)offset.z * ahead.z;
                        long here = (long)offset.x * offset.x + (long)offset.z * offset.z;
                        long path = (long)ahead.x * ahead.x + (long)ahead.z * ahead.z;
                        return dot < 0 && 2L * dot * dot >= here * path
                            && r.CenterCell.DistanceTo(Rect("near").CenterCell) >= 8f
                            && r.CenterCell.DistanceTo(Rect("far").CenterCell) >= 18f;
                    });
                    staged.Add(("close", close));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    closeSpawnedAt = Now();
                    holdValidFocus = true;
                    Net().Tick(force: true);
                    Fire target = JobFire(medic.CurJob);
                    if (target == null || RectOf(target.Position) != "near")
                        throw new InvalidOperationException($"the closer fire replaced the still-valid near focus: {medic.CurJob}");
                })
                .Expect("the Medirunner keeps its valid focus until that fire is destroyed just after a network pass", () =>
                {
                    Fail();
                    Pawn medic = Medic();
                    Fire spraying = JobFire(medic.CurJob);
                    bool arrived = medic.CurJobDef == JobDefOf.AttackStatic && spraying != null
                        && RectOf(spraying.Position) == "near";
                    if (!arrived && Now() - closeSpawnedAt < 40)
                        return false;
                    CellRect near = Rect("near");
                    if (!FireIn(near))
                        throw new InvalidOperationException("the focus fire went out before it could be destroyed");
                    Net().Tick(force: true);
                    focusDestroyedAt = Now();
                    holdValidFocus = false;
                    DestroyFiresIn(near);
                    if (FireIn(near))
                        throw new InvalidOperationException("the focus fire survived destruction");
                    string expected = NearestStagedFireName(medic, staged);
                    if (expected != "close")
                        throw new InvalidOperationException($"fixture: the nearest remaining fire is {expected}, not the close one");
                    return true;
                })
                .Expect("at its next native job boundary the Medirunner takes the nearest remaining fire without going home", () =>
                {
                    Fail();
                    if (retargetAt >= 0)
                        return true;
                    if (Now() - focusDestroyedAt > RetargetLimit)
                        throw new InvalidOperationException($"no replacement fire job within {RetargetLimit} ticks "
                            + $"(current {Medic().CurJob}, last fire job {lastFireTarget}); " + string.Join("; ", transitions));
                    return false;
                })
                .SaveAndReload()
                .Expect("the remaining separated bonfires are physically out after the reload", () =>
                    !FireIn(Rect("close")) && !FireIn(Rect("far")) && !FireIn(Rect("near")))
                .Expect("a full 180-tick standby follows the last fire with no homeward order", () =>
                {
                    Fail();
                    return standbyStart >= 0 && Now() - standbyStart >= Hold;
                })
                .Expect("the Medirunner docks after that standby because the weather is clear", () =>
                    Stable().Docked.Contains(Medic()))
                .Do("a new Home fire appears while the Medirunner is docked", () =>
                {
                    Fail();
                    addedAt = Now();
                    CellRect added = SpawnBonfire(s, Stable().InteractionCell, 8f, 14f, home: true,
                        r => r.CenterCell.DistanceTo(Rect("far").CenterCell) >= 18f
                            && r.CenterCell.DistanceTo(Rect("near").CenterCell) >= 12f
                            && r.CenterCell.DistanceTo(Rect("close").CenterCell) >= 14f);
                    staged.Add(("added", added));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    Net().Tick(force: true);
                })
                .Expect("the redeployed Medirunner takes the new fire with a native fire job", () =>
                {
                    Fail();
                    return fireJobs.Any(j => j.tick >= addedAt && j.name == "added");
                })
                .Expect("the new fire is physically out", () => !FireIn(Rect("added")))
                .Expect("the Medirunner then stands by instead of docking", () =>
                {
                    Fail();
                    return standbyStart >= 0 && Now() - standbyStart >= 60;
                })
                // The positive quiet deadline must survive a reload, not only an engaged or storm alert.
                .SaveAndReload()
                .Do("another Home fire appears during that standby", () =>
                {
                    Fail();
                    CellRect final = SpawnBonfire(s, Stable().InteractionCell, 8f, 14f, home: true,
                        r => r.CenterCell.DistanceTo(Rect("far").CenterCell) >= 18f
                            && r.CenterCell.DistanceTo(Rect("near").CenterCell) >= 12f
                            && r.CenterCell.DistanceTo(Rect("close").CenterCell) >= 14f
                            && r.CenterCell.DistanceTo(Rect("added").CenterCell) >= 14f);
                    finalAt = Now();
                    staged.Add(("final", final));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    Net().Tick(force: true);
                })
                .Expect("the Medirunner resumes from standby and takes the new fire", () =>
                {
                    Fail();
                    return fireJobs.Any(j => j.tick >= finalAt && j.name == "final");
                })
                .Expect("the resumed fire is physically out", () => !FireIn(Rect("final")))
                .Expect("the resumed fire gets a fresh full 180-tick standby before docking", () =>
                {
                    Fail();
                    return standbyStart > finalAt && Now() - standbyStart >= Hold;
                })
                .Expect("the Medirunner docks after the fresh standby", () => Stable().Docked.Contains(Medic()))
                .Summary(() =>
                    $"standby started at {standbyStart}; fire jobs: {string.Join(", ", fireJobs.Select(j => $"+{j.tick - startedAt} {j.name}"))}; "
                    + $"native transitions: {string.Join("; ", transitions)}")
                .Start();
        }

        private static void MedirunnerStormWeatherStandby() => MedirunnerStormStandby(flashstorm: false);

        private static void MedirunnerStormConditionStandby() => MedirunnerStormStandby(flashstorm: true);

        private static void MedirunnerStormStandby(bool flashstorm)
        {
            var s = new Scenario(flashstorm ? "Medirunner storm condition standby" : "Medirunner storm weather standby", 50000);
            const int Hold = FireStandbyTicks;
            var staged = new List<(string name, CellRect rect)>();
            var transitions = new List<string>();
            var fireJobs = new List<(int tick, string name)>();
            string stableId = null, medicId = null, failure = null;
            int startedAt = 0, secondAt = -1, reloadedAt = -1, stormEndedAt = -1, standbyStart = -1, conditionId = -1;
            int noHomeUntil = 0;
            bool[] priorHome = null;
            WeatherDef dry = null, priorCur = null, priorLast = null;
            string storm = flashstorm ? "flashstorm" : "lightning";
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            Pawn Medic() => Scenario.ById(medicId) as Pawn;
            int Now() => Find.TickManager.TicksGame;
            CellRect Rect(string name) => staged.First(p => p.name == name).rect;
            bool East(IntVec3 cell, IntVec3 from) => cell.x - from.x > Math.Abs(cell.z - from.z);
            bool West(IntVec3 cell, IntVec3 from) => from.x - cell.x > Math.Abs(cell.z - from.z);
            bool AnyBurning() => staged.Any(p => p.rect.Width > 0 && FireIn(p.rect));
            GameCondition_Flashstorm ActiveCondition()
            {
                Map map = Find.CurrentMap;
                return map?.gameConditionManager == null ? null : map.gameConditionManager.ActiveConditions
                    .OfType<GameCondition_Flashstorm>().FirstOrDefault(c => c.uniqueID == conditionId);
            }
            void Fail()
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
            }
            bool Homeward(Job job)
            {
                Building_Stable stable = Stable();
                if (stable == null)
                    return false;
                if (job.def == QR_DefOf.QR_Dock)
                    return true;
                if (job.targetA.Thing == stable || job.targetB.Thing == stable)
                    return true;
                if (JobFire(job) != null)
                    return false;
                return job.def == JobDefOf.Goto && !job.targetA.HasThing
                    && job.targetA.Cell.InHorDistOf(stable.InteractionCell, 4f);
            }
            void Observe(Pawn pawn, Job job)
            {
                if (pawn.ThingID != medicId)
                    return;
                int now = Now();
                transitions.Add($"+{now - startedAt} {job}");
                if (JobFire(job) != null)
                {
                    string rect = StagedRectName(staged, JobFire(job).Position);
                    fireJobs.Add((now, rect));
                    standbyStart = -1;
                    return;
                }
                if (job.def == JobDefOf.Goto && !job.playerForced && AnyBurning())
                    failure ??= $"fire pursuit lost its Fire intent at +{now - startedAt}: {job}";
                if (Homeward(job))
                {
                    if (AnyBurning())
                        failure ??= $"homeward order with staged fires burning at +{now - startedAt}: {job}";
                    else if (now < noHomeUntil)
                        failure ??= $"the {storm} alert ended early with a homeward order at +{now - startedAt}: {job}";
                    else if (standbyStart < 0)
                        failure ??= $"homeward order without a standby hold at +{now - startedAt}: {job}";
                    else if (now < standbyStart + Hold)
                        failure ??= $"homeward order {now - standbyStart} ticks into a {Hold}-tick standby at +{now - startedAt}: {job}";
                }
                if (!AnyBurning() && standbyStart < 0)
                    standbyStart = now;
            }
            s.Do(flashstorm
                    ? "clear weather with an independent Core flashstorm and a Home fire"
                    : "a real Core dry thunderstorm and a Home fire", () =>
                {
                    Map map = Find.CurrentMap;
                    priorCur = map.weatherManager.curWeather;
                    priorLast = map.weatherManager.lastWeather;
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    if (flashstorm)
                    {
                        map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                        map.weatherManager.curWeatherAge = 4000;
                        var condition = (GameCondition_Flashstorm)GameConditionMaker.MakeCondition(
                            GameConditionDefOf.Flashstorm, 200000);
                        condition.suppressEndMessage = true;
                        // Defer every strike past the fixture while leaving the real condition active for persistence.
                        condition.initialStrikeDelay = new IntRange(120000, 120000);
                        condition.areaRadiusOverride = new IntRange(1, 1);
                        condition.centerLocation = new IntVec2(8, 8);
                        condition.avoidConditionCauser = false;
                        condition.ambientSound = false;
                        map.gameConditionManager.RegisterCondition(condition);
                        conditionId = condition.uniqueID;
                        s.OnFinish(() =>
                        {
                            Map now = Find.CurrentMap;
                            if (now == null)
                                return;
                            GameCondition active = now.gameConditionManager.ActiveConditions
                                .FirstOrDefault(c => c.uniqueID == conditionId);
                            if (active != null)
                                active.End();
                        });
                    }
                    else
                    {
                        dry = WeatherDef.Named("DryThunderstorm");
                        if (dry == null || dry.eventMakers == null || dry.eventMakers.Count == 0)
                            throw new InvalidOperationException("fixture: Core dry thunderstorm weather is unavailable");
                        // The def stays the real lightning weather; only its random event cadence is muted for the fixture.
                        s.OnFinish(SilenceRandomWeatherEvents(dry));
                        map.weatherManager.curWeather = map.weatherManager.lastWeather = dry;
                        map.weatherManager.curWeatherAge = 4000;
                    }
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        now.weatherManager.curWeather = priorCur;
                        now.weatherManager.lastWeather = priorLast;
                    });
                    DraftColonists(s);
                    ClearFireFixture(s, staged);
                    priorHome = SnapshotHomeArea(map);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    CompRobodog.Of(Medic()).SetBattery(1f);
                    IntVec3 door = stable.InteractionCell;
                    CellRect first = SpawnBonfire(s, door, 10f, 16f, home: true, r => East(r.CenterCell, door));
                    staged.Add(("first", first));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    startedAt = Now();
                    // No docking is lawful until the scenario clears this after the weather ends.
                    noHomeUntil = int.MaxValue;
                    ObservePackJobs(s, Observe);
                    Net().Tick(force: true);
                })
                .Expect("the Medirunner deploys and takes the storm fire with a fire job", () =>
                {
                    Fail();
                    return fireJobs.Any(j => j.name == "first");
                })
                .Expect("the storm fire is physically out", () => !FireIn(Rect("first")))
                .Expect("the storm alert keeps the Medirunner deployed well beyond the 180-tick hold", () =>
                {
                    Fail();
                    return Medic().Spawned && !Stable().Docked.Contains(Medic())
                        && standbyStart >= 0 && Now() - standbyStart >= 300;
                })
                .Do("a second Home fire appears across the colony", () =>
                {
                    Fail();
                    secondAt = Now();
                    CellRect second = SpawnBonfire(s, Stable().InteractionCell, 16f, 24f, home: true,
                        r => West(r.CenterCell, Stable().InteractionCell)
                            && r.CenterCell.DistanceTo(Rect("first").CenterCell) >= 18f);
                    staged.Add(("second", second));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    Net().Tick(force: true);
                })
                .Expect("the deployed Medirunner takes the second fire without returning home", () =>
                {
                    Fail();
                    return fireJobs.Any(j => j.tick >= secondAt && j.name == "second");
                })
                .Expect("the second fire is physically out", () => !FireIn(Rect("second")))
                .Expect("the storm alert holds the Medirunner out past another 180 ticks", () =>
                {
                    Fail();
                    return Medic().Spawned && !Stable().Docked.Contains(Medic())
                        && standbyStart >= 0 && Now() - standbyStart >= 300;
                })
                .SaveAndReload()
                .Expect("the reload keeps the real " + (flashstorm ? "flashstorm condition" : "lightning weather") + " and the deployed alert", () =>
                {
                    Fail();
                    if (flashstorm)
                    {
                        GameCondition_Flashstorm active = ActiveCondition();
                        return active != null && active.def == GameConditionDefOf.Flashstorm
                            && Find.CurrentMap.gameConditionManager.ConditionIsActive(GameConditionDefOf.Flashstorm)
                            && Medic().Spawned && !Stable().Docked.Contains(Medic());
                    }
                    Map map = Find.CurrentMap;
                    return map.weatherManager.curWeather == dry && map.weatherManager.lastWeather == dry
                        && Medic().Spawned && !Stable().Docked.Contains(Medic());
                })
                .Do("mark the reloaded alert", () => reloadedAt = Now())
                .Expect("the saved alert persists for at least another 120 ticks", () =>
                {
                    Fail();
                    return reloadedAt >= 0 && Now() - reloadedAt >= 120
                        && (!flashstorm || ActiveCondition() != null)
                        && Medic().Spawned && !Stable().Docked.Contains(Medic());
                })
                .Do(flashstorm ? "the flashstorm condition ends" : "the dry thunderstorm passes and the sky clears", () =>
                {
                    Fail();
                    if (flashstorm)
                    {
                        GameCondition_Flashstorm active = ActiveCondition();
                        if (active == null)
                            throw new InvalidOperationException("fixture: the flashstorm condition was lost before it could end");
                        active.End();
                    }
                    else
                    {
                        Map map = Find.CurrentMap;
                        map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                        map.weatherManager.curWeatherAge = 4000;
                    }
                    stormEndedAt = Now();
                    noHomeUntil = Now() + Hold;
                })
                .Expect("the alert ends with an ordinary 180-tick hold and no early homeward order", () =>
                {
                    Fail();
                    return Now() - stormEndedAt >= Hold;
                })
                .Expect("the Medirunner docks after the ordinary hold", () => Stable().Docked.Contains(Medic()))
                .Summary(() => $"weather {Find.CurrentMap.weatherManager.curWeather.defName}; "
                    + $"fire jobs: {string.Join(", ", fireJobs.Select(j => $"+{j.tick - startedAt} {j.name}"))}; "
                    + $"native transitions: {string.Join("; ", transitions)}")
                .Start();
        }

        private static void MedirunnerStormChargePriority()
        {
            var s = new Scenario("Medirunner storm charge priority", 40000);
            var staged = new List<(string name, CellRect rect)>();
            var transitions = new List<string>();
            string stableId = null, medicId = null, failure = null;
            int startedAt = 0, standbyStart = -1;
            float chargeThreshold = -1f, chargeStartBattery = 1f;
            bool allowHomeward = false, chargeJobSeen = false;
            bool[] priorHome = null;
            WeatherDef dry = null, priorCur = null, priorLast = null;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            Pawn Medic() => Scenario.ById(medicId) as Pawn;
            int Now() => Find.TickManager.TicksGame;
            CellRect Rect(string name) => staged.First(p => p.name == name).rect;
            bool East(IntVec3 cell, IntVec3 from) => cell.x - from.x > Math.Abs(cell.z - from.z);
            bool AnyBurning() => staged.Any(p => p.rect.Width > 0 && FireIn(p.rect));
            void Fail()
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
            }
            bool Homeward(Job job)
            {
                Building_Stable stable = Stable();
                if (stable == null)
                    return false;
                if (job.def == QR_DefOf.QR_Dock)
                    return true;
                if (job.targetA.Thing == stable || job.targetB.Thing == stable)
                    return true;
                if (JobFire(job) != null)
                    return false;
                return job.def == JobDefOf.Goto && !job.targetA.HasThing
                    && job.targetA.Cell.InHorDistOf(stable.InteractionCell, 4f);
            }
            void Observe(Pawn pawn, Job job)
            {
                if (pawn.ThingID != medicId)
                    return;
                int now = Now();
                transitions.Add($"+{now - startedAt} {job}");
                if (JobFire(job) != null)
                {
                    standbyStart = -1;
                    return;
                }
                if (allowHomeward && !chargeJobSeen
                    && (job.def == QR_DefOf.QR_Dock || job.def == QR_DefOf.QR_ChargeAtConduit))
                {
                    // Record the native charge order and the battery it was issued from, not the later frame state.
                    chargeJobSeen = true;
                    chargeStartBattery = CompRobodog.Of(pawn)?.Battery ?? 1f;
                }
                if (!allowHomeward && Homeward(job))
                {
                    if (AnyBurning())
                        failure ??= $"homeward order with the fire burning at +{now - startedAt}: {job}";
                    else if (standbyStart < 0)
                        failure ??= $"homeward order without a standby hold at +{now - startedAt}: {job}";
                    else if (now < standbyStart + FireStandbyTicks)
                        failure ??= $"homeward order {now - standbyStart} ticks into standby at +{now - startedAt}: {job}";
                }
                if (!AnyBurning() && standbyStart < 0)
                    standbyStart = now;
            }
            s.Do("a dry thunderstorm, a full battery and a Home fire", () =>
                {
                    Map map = Find.CurrentMap;
                    priorCur = map.weatherManager.curWeather;
                    priorLast = map.weatherManager.lastWeather;
                    dry = WeatherDef.Named("DryThunderstorm");
                    if (dry == null)
                        throw new InvalidOperationException("fixture: Core dry thunderstorm weather is unavailable");
                    s.OnFinish(SilenceRandomWeatherEvents(dry));
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = dry;
                    map.weatherManager.curWeatherAge = 4000;
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        now.weatherManager.curWeather = priorCur;
                        now.weatherManager.lastWeather = priorLast;
                    });
                    DraftColonists(s);
                    ClearFireFixture(s, staged);
                    priorHome = SnapshotHomeArea(map);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    CompRobodog.Of(Medic()).SetBattery(1f);
                    IntVec3 door = stable.InteractionCell;
                    CellRect first = SpawnBonfire(s, door, 10f, 16f, home: true, r => East(r.CenterCell, door));
                    staged.Add(("first", first));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    startedAt = Now();
                    ObservePackJobs(s, Observe);
                    Net().Tick(force: true);
                })
                .Expect("the Medirunner deploys and puts the storm fire out", () =>
                    Medic().Spawned && !FireIn(Rect("first")))
                .Expect("the storm alert has it waiting out beyond 180 ticks", () =>
                {
                    Fail();
                    return Medic().Spawned && !Stable().Docked.Contains(Medic())
                        && standbyStart >= 0 && Now() - standbyStart >= 300;
                })
                .Do("the battery falls to the charge threshold while the alert is active", () =>
                {
                    Fail();
                    CompRobodog comp = CompRobodog.Of(Medic());
                    if (comp == null)
                        throw new InvalidOperationException("fixture: the deployed Medirunner lost its battery component");
                    allowHomeward = true;
                    chargeThreshold = comp.Props.seekChargeAt;
                    comp.SetBattery(comp.Props.seekChargeAt - 0.02f);
                    Net().Tick(force: true);
                })
                .Expect("low battery overrides the alert with a native charge order from below the threshold", () =>
                {
                    Fail();
                    return chargeJobSeen && chargeStartBattery < chargeThreshold;
                })
                .Expect("the battery recovers above the seek threshold", () =>
                {
                    Fail();
                    CompRobodog comp = CompRobodog.Of(Medic());
                    return comp != null && comp.Battery > comp.Props.seekChargeAt + 0.05f;
                })
                .Summary(() => $"battery {CompRobodog.Of(Medic())?.Battery:P0}; "
                    + $"native transitions: {string.Join("; ", transitions)}")
                .Start();
        }
    }
}
