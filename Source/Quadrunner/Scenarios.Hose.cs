using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Plan 0010 / H1: the worker Quadrunner's integrated water hose is used only for a contiguous Home-area patch
    /// of more than three distinct blood/firefoam tiles, and then keeps spraying the whole patch until it is clean.
    /// Smaller patches and unrelated filth stay ordinary Clean work. These fixtures stage real filth, let the normal
    /// network discovery and job construction start the work, and observe actual native jobs and filth removals.
    /// </summary>
    public static partial class Scenarios
    {
        private const string HoseJobDefName = "QR_CleanWithHose";

        private static readonly AccessTools.FieldRef<Filth, int> FilthGrowTick =
            AccessTools.FieldRefAccess<Filth, int>("growTick");

        private static Action<Filth> hoseFilthObserver;

        // Fixture-only observation of every staged filth removal, including several in one rendered frame.
        private static void HoseFilthDespawning(Filth __instance) => hoseFilthObserver?.Invoke(__instance);

        private static void ObserveHoseFilth(Scenario scenario, Action<Filth> observe)
        {
            var harmony = new Harmony("josh9817.quadrunner.hose-scenario");
            var despawn = AccessTools.Method(typeof(Filth), nameof(Filth.DeSpawn));
            hoseFilthObserver = observe;
            harmony.Patch(despawn, prefix: new HarmonyMethod(AccessTools.Method(typeof(Scenarios), nameof(HoseFilthDespawning))));
            scenario.OnFinish(() =>
            {
                hoseFilthObserver = null;
                harmony.Unpatch(despawn, HarmonyPatchType.Prefix, harmony.Id);
            });
        }

        private static bool IsHoseJob(Job job) => job?.def != null && job.def.defName == HoseJobDefName;

        private static bool HoseActive(Pawn dog) => dog != null && dog.Spawned && IsHoseJob(dog.CurJob);

        /// <summary>Every ThingID a job targets, whether as A/B or as a Clean-style queue under A.</summary>
        private static IEnumerable<string> JobThingIds(Job job)
        {
            if (job == null)
                yield break;
            if (job.targetA.HasThing && job.targetA.Thing != null)
                yield return job.targetA.Thing.ThingID;
            if (job.targetB.HasThing && job.targetB.Thing != null)
                yield return job.targetB.Thing.ThingID;
            List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.A);
            if (queue == null)
                yield break;
            foreach (LocalTargetInfo target in queue)
                if (target.HasThing && target.Thing != null)
                    yield return target.Thing.ThingID;
        }

        /// <summary>Removes every existing filth so only the fixture's own cells can decide the patch and its work.</summary>
        private static void ClearStagedFilth(Map map)
        {
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Filth).ToList())
                thing.Destroy();
        }

        /// <summary>
        /// A real stationary worker in a powered stable: its own Home/roof fixture bookkeeping, then a teleport to the
        /// fixture area only, so all discovery and work below is the ordinary network and native job path.
        /// </summary>
        private static (Building_Stable stable, Pawn dog) SpawnHoseWorker(Scenario s)
        {
            Building_Stable stable = SpawnStable(s, powered: true);
            Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
            CompRobodog.Of(dog).SetBattery(1f);
            return (stable, dog);
        }

        /// <summary>
        /// Filth on <paramref name="cell"/> in the Home area by default, roofed (rain washes blood and foam away) and
        /// already older than vanilla's 600-tick fresh-filth gate. The fixture's flags are restored when it ends.
        /// </summary>
        private static string SpawnHoseFilth(Scenario s, IntVec3 cell, ThingDef def, bool home = true,
            bool forbidden = false, bool manageFlags = true)
        {
            Map map = Find.CurrentMap;
            bool wasHome = false;
            RoofDef roof = null;
            if (manageFlags)
            {
                wasHome = map.areaManager.Home[cell];
                roof = map.roofGrid.RoofAt(cell);
                map.areaManager.Home[cell] = home;
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
            var filth = (Filth)GenSpawn.Spawn(def, cell, map);
            FilthGrowTick(filth) = Find.TickManager.TicksGame - 1000;
            if (forbidden)
                filth.SetForbidden(true, warnOnFail: false);
            if (manageFlags)
                s.OnFinish(() =>
                {
                    Map now = Find.CurrentMap;
                    if (now == null)
                        return;
                    now.areaManager.Home[cell] = wasHome;
                    now.roofGrid.SetRoof(cell, roof);
                });
            return s.Track(filth).ThingID;
        }

        /// <summary>Stages one filth object per patch cell, alternating blood and firefoam.</summary>
        private static List<string> SpawnHosePatch(Scenario s, CellRect rect)
        {
            var ids = new List<string>();
            int i = 0;
            foreach (IntVec3 cell in rect.Cells)
                ids.Add(SpawnHoseFilth(s, cell,
                    i++ % 2 == 0 ? ThingDefOf.Filth_Blood : ThingDefOf.Filth_FireFoam));
            return ids;
        }

        /// <summary>
        /// Puts the worker beside <paramref name="near"/> (fixture placement), clears any survey/order, then performs one
        /// real local network scan and dispatch, exactly as the dog's own scan would.
        /// </summary>
        private static void DispatchHoseWorker(Pawn dog, IntVec3 near, Predicate<IntVec3> extra = null,
            string firstTargetId = null)
        {
            if (!dog.Spawned && !(CompRobodog.Of(dog)?.stable?.TryUndock(dog) ?? false))
                throw new InvalidOperationException("the hose worker could not undock");
            Teleport(dog, OpenCell(dog.Map, near, 2f, 6f,
                c => c != near && (extra == null || extra(c))));
            if (dog.CurJob != null)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            DogWork.ReportLocal(dog, Net());
            if (firstTargetId != null)
            {
                NetworkTask first = Net().State.normal.FirstOrDefault(t => t.targetId == firstTargetId)
                    ?? throw new InvalidOperationException("the requested first cleaning task was not discovered");
                Net().State.normal.Remove(first);
                Net().State.normal.Insert(0, first);
            }
            Net().Tick(force: true);
        }

        private static void HoseSingleBloodAndFoam()
        {
            var s = new Scenario("Hose: single blood and foam", 30000);
            var staged = new HashSet<string>();
            var normalTargets = new HashSet<string>();
            bool everHose = false;
            string dogId = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            s.Do("a powered stable and worker, one blood tile and one foam tile apart in Home", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    IntVec3 blood = OpenCell(map, stable.InteractionCell, 5f, 12f);
                    IntVec3 foam = OpenCell(map, stable.InteractionCell, 5f, 12f,
                        c => c.DistanceTo(blood) >= 8f && c.DistanceTo(blood) <= 10f);
                    staged.Add(SpawnHoseFilth(s, blood, ThingDefOf.Filth_Blood));
                    staged.Add(SpawnHoseFilth(s, foam, ThingDefOf.Filth_FireFoam));
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        if (IsHoseJob(job))
                            everHose = true;
                        if (job.def == JobDefOf.Clean)
                            foreach (string id in JobThingIds(job))
                                if (staged.Contains(id))
                                    normalTargets.Add(id);
                    });
                    DispatchHoseWorker(dog, blood, c => c.DistanceTo(foam) <= 7f);
                })
                .Expect("single blood and foam are cleaned by ordinary Clean jobs, never by the hose", () =>
                {
                    if (everHose)
                        throw new InvalidOperationException("the hose started for single-tile filth");
                    if (staged.Any(id => Scenario.ById(id) != null))
                        return false;
                    if (!staged.SetEquals(normalTargets))
                        throw new InvalidOperationException("ordinary Clean did not target: "
                            + string.Join(", ", staged.Except(normalTargets)));
                    return true;
                })
                .Summary(() => $"2 single tiles cleaned normally; hose used: {everHose}; "
                    + $"{Net().Inspect(Dog())}")
                .Start();
        }

        private static void HoseThreeTilePatch()
        {
            var s = new Scenario("Hose: three-tile patch", 30000);
            var staged = new HashSet<string>();
            var normalTargets = new HashSet<string>();
            bool everHose = false;
            string dogId = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            s.Do("a powered stable and worker, a compact three-tile blood/foam patch in Home", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect rect = ClearRect(map, stable.InteractionCell, 3, 1, 6f, 14f);
                    var corner = new IntVec3(rect.minX, 0, rect.minZ);
                    // Three distinct tiles: one holds both blood and foam, so four filth objects must still count
                    // as three tiles and stay on ordinary cleaning.
                    staged.Add(SpawnHoseFilth(s, corner, ThingDefOf.Filth_Blood));
                    staged.Add(SpawnHoseFilth(s, corner, ThingDefOf.Filth_FireFoam, manageFlags: false));
                    staged.Add(SpawnHoseFilth(s, corner + IntVec3.East, ThingDefOf.Filth_Blood));
                    staged.Add(SpawnHoseFilth(s, corner + IntVec3.North, ThingDefOf.Filth_FireFoam));
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        if (IsHoseJob(job))
                            everHose = true;
                        if (job.def == JobDefOf.Clean)
                            foreach (string id in JobThingIds(job))
                                if (staged.Contains(id))
                                    normalTargets.Add(id);
                    });
                    DispatchHoseWorker(dog, corner);
                })
                .Expect("a three-tile patch (four objects) is cleaned by ordinary Clean, never by the hose", () =>
                {
                    if (everHose)
                        throw new InvalidOperationException("the hose started for a three-tile patch");
                    if (staged.Any(id => Scenario.ById(id) != null))
                        return false;
                    if (!staged.SetEquals(normalTargets))
                        throw new InvalidOperationException("ordinary Clean did not target: "
                            + string.Join(", ", staged.Except(normalTargets)));
                    return true;
                })
                .Summary(() => $"4 objects on 3 tiles cleaned normally; hose used: {everHose}; "
                    + $"{Net().Inspect(Dog())}")
                .Start();
        }

        private static void HoseFourTilePatch()
        {
            var s = new Scenario("Hose: four-tile patch", 60000);
            var staged = new HashSet<string>();
            var hoseRemoved = new HashSet<string>();
            var normalRemoved = new HashSet<string>();
            bool everHose = false, hoseAtThree = false;
            int hoseQueued = -1;
            string dogId = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            int Remaining() => staged.Count(id => Scenario.ById(id) != null);
            s.Do("a powered stable and worker, a four-tile mixed 2x2 patch in Home", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect rect = ClearRect(map, stable.InteractionCell, 2, 1, 6f, 14f);
                    foreach (string id in SpawnHosePatch(s, rect))
                        staged.Add(id);
                    if (staged.Count != 4)
                        throw new InvalidOperationException($"fixture staged {staged.Count} tiles, not four");
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        if (!IsHoseJob(job))
                            return;
                        everHose = true;
                        if (hoseQueued < 0)
                            hoseQueued = JobThingIds(job).Where(staged.Contains).Distinct().Count();
                    });
                    ObserveHoseFilth(s, filth =>
                    {
                        if (!staged.Contains(filth.ThingID)
                            || Current.ProgramState != ProgramState.Playing
                            || Find.CurrentMap == null || filth.Map != Find.CurrentMap)
                            return;
                        int before = Remaining(); // still includes the filth being removed
                        if (HoseActive(Dog()))
                        {
                            hoseRemoved.Add(filth.ThingID);
                            if (before <= 3)
                                hoseAtThree = true;
                        }
                        else
                            normalRemoved.Add(filth.ThingID);
                    });
                    DispatchHoseWorker(dog, rect.CenterCell);
                })
                .Expect("the hose starts for four tiles and queues every tile, including the last three", () =>
                {
                    if (!everHose)
                        return false;
                    if (hoseQueued != staged.Count)
                        throw new InvalidOperationException(
                            $"the first hose job queued {hoseQueued} of {staged.Count} staged tiles");
                    return true;
                })
                .Expect("the four-tile patch is cleaned completely by the hose, including the last three", () =>
                {
                    if (Remaining() > 0)
                        return false;
                    if (!staged.SetEquals(hoseRemoved))
                        throw new InvalidOperationException("ordinary Clean removed patch tiles: "
                            + string.Join(", ", normalRemoved));
                    if (!hoseAtThree)
                        throw new InvalidOperationException("the hose was not firing when the last three tiles remained");
                    return true;
                })
                .Summary(() => $"{hoseRemoved.Count}/{staged.Count} removed by the hose; last three under hose: "
                    + $"{hoseAtThree}; queued {hoseQueued}; {Net().Inspect(Dog())}")
                .Start();
        }

        private static void HoseLargerMixedPatch()
        {
            var s = new Scenario("Hose: larger mixed patch", 120000);
            var staged = new HashSet<string>();
            var outsideSafe = new HashSet<string>();
            var hoseRemoved = new HashSet<string>();
            var normalRemoved = new HashSet<string>();
            bool everHose = false, hoseAtThree = false, dirtCleanedNormally = false, dirtTouchedByHose = false;
            bool outsideTouchedByHose = false;
            bool reloaded = false;
            int hoseQueued = -1, remainingAtSave = 0, remainingAfterReload = 0, hoseResumeStart = -1;
            string dogId = null, dirtId = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            int Remaining() => staged.Count(id => Scenario.ById(id) != null);
            s.Do("a powered stable and worker, a 4x4 mixed patch with forbidden dirt beside it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect rect = ClearRect(map, stable.InteractionCell, 4, 2, 7f, 16f);
                    foreach (string id in SpawnHosePatch(s, rect))
                        staged.Add(id);
                    // Unrelated filth inside the spray's working area: forbidden so only the hose could remove it
                    // before it is released below.
                    dirtId = SpawnHoseFilth(s, new IntVec3(rect.maxX + 1, 0, rect.CenterCell.z),
                        ThingDefOf.Filth_Dirt, forbidden: true);
                    // A four-tile eligible patch just outside Home, inside the working area: it must never join the
                    // hose job or be sprayed, even while the worker stands beside it.
                    var outsideRect = new CellRect(rect.minX - 2, rect.CenterCell.z, 2, 2);
                    int o = 0;
                    foreach (IntVec3 cell in outsideRect.Cells)
                        outsideSafe.Add(SpawnHoseFilth(s, cell,
                            o++ % 2 == 0 ? ThingDefOf.Filth_Blood : ThingDefOf.Filth_FireFoam, home: false));
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        List<string> ids = JobThingIds(job).ToList();
                        if (IsHoseJob(job))
                        {
                            everHose = true;
                            if (ids.Contains(dirtId))
                                dirtTouchedByHose = true;
                            if (ids.Any(outsideSafe.Contains))
                                outsideTouchedByHose = true;
                            if (hoseQueued < 0)
                                hoseQueued = ids.Where(staged.Contains).Distinct().Count();
                        }
                        else if (job.def == JobDefOf.Clean && ids.Contains(dirtId))
                            dirtCleanedNormally = true;
                    });
                    ObserveHoseFilth(s, filth =>
                    {
                        if (!staged.Contains(filth.ThingID)
                            || Current.ProgramState != ProgramState.Playing
                            || Find.CurrentMap == null || filth.Map != Find.CurrentMap)
                            return;
                        int before = Remaining();
                        if (HoseActive(Dog()))
                        {
                            hoseRemoved.Add(filth.ThingID);
                            if (before <= 3)
                                hoseAtThree = true;
                        }
                        else
                            normalRemoved.Add(filth.ThingID);
                    });
                    DispatchHoseWorker(dog, rect.CenterCell, firstTargetId: dirtId);
                })
                .Expect("the hose starts and queues the whole larger patch", () =>
                {
                    if (!everHose)
                        return false;
                    if (hoseQueued != staged.Count)
                        throw new InvalidOperationException(
                            $"the first hose job queued {hoseQueued} of {staged.Count} staged tiles");
                    return true;
                })
                .Expect("at least one patch tile is already cleaned while the hose job continues", () =>
                    Remaining() < staged.Count)
                .Do("slow to normal and note the dirty remainder for the save", () =>
                {
                    remainingAtSave = Remaining();
                    if (remainingAtSave <= 0)
                        throw new InvalidOperationException("the patch was already clean before the save");
                    if (!HoseActive(Dog()))
                        throw new InvalidOperationException("the hose job ended before the mid-patch save");
                    // Normal speed makes the save's mid-job point deterministic; live runs (no automatic reload)
                    // simply continue at this speed until the scenario ends.
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("the hose is actively spraying", () =>
                    HoseActive(Dog()) && Dog().jobs.curDriver.CurToilString == "SprayFilth");
            CaptureArtwork(s, "hose-spray", () =>
            {
                Find.CameraDriver.SetRootPosAndSize(Dog().DrawPos, 5f);
            }, delaySeconds: 0.2f);
            s.SaveAndReload()
                .Do("note the reloaded remainder", () =>
                {
                    reloaded = true;
                    remainingAfterReload = Remaining();
                    if (remainingAfterReload <= 0)
                        throw new InvalidOperationException("no dirty patch tiles survived the reload");
                })
                .Expect("the loaded worker resumes the hose on the remaining tiles", () =>
                {
                    if (Remaining() == 0 || HoseActive(Dog()))
                        return true;
                    if (hoseResumeStart < 0)
                        hoseResumeStart = Find.TickManager.TicksGame;
                    if (Find.TickManager.TicksGame - hoseResumeStart <= 600)
                        return false;
                    throw new InvalidOperationException(
                        $"the loaded worker did not resume the hose with {Remaining()} tiles left: {Dog()?.CurJob}");
                })
                .Expect("the reloaded hose finishes the patch, including its last three tiles", () =>
                {
                    if (!reloaded)
                        throw new InvalidOperationException("the patch completed before the save/load step");
                    if (Remaining() > 0)
                        return false;
                    if (dirtTouchedByHose)
                        throw new InvalidOperationException("the hose targeted unrelated dirt");
                    if (outsideTouchedByHose)
                        throw new InvalidOperationException("the hose queued blood/foam outside Home");
                    if (Scenario.ById(dirtId) == null)
                        throw new InvalidOperationException("the spray removed unrelated dirt before it was released");
                    foreach (string id in outsideSafe)
                        if (Scenario.ById(id) == null)
                            throw new InvalidOperationException("the hose crossed the Home boundary");
                    if (!staged.SetEquals(hoseRemoved))
                        throw new InvalidOperationException("ordinary Clean removed patch tiles: "
                            + string.Join(", ", normalRemoved));
                    if (!hoseAtThree)
                        throw new InvalidOperationException("the hose was not firing when the last three tiles remained");
                    return true;
                })
                .Expect("the hose job ends", () => !HoseActive(Dog()))
                .Do("release the untouched dirt", () =>
                {
                    Scenario.ById(dirtId).SetForbidden(false, warnOnFail: false);
                    DispatchHoseWorker(Dog(), Scenario.ById(dirtId).Position);
                })
                .Expect("the released dirt is then cleaned by an ordinary Clean job", () =>
                {
                    if (Scenario.ById(dirtId) != null)
                        return false;
                    if (!dirtCleanedNormally)
                        throw new InvalidOperationException("dirt disappeared without an ordinary Clean job");
                    return true;
                })
                .Summary(() => $"{hoseRemoved.Count}/{staged.Count} patch tiles by hose, {remainingAtSave} saved dirty "
                    + $"({remainingAfterReload} after reload); last three under hose: {hoseAtThree}, dirt untouched by "
                    + $"spray then cleaned normally: {dirtCleanedNormally}, {outsideSafe.Count} outside Home untouched; "
                    + $"{Net().Inspect(Dog())}")
                .Start();
        }

        private static void HoseTwoWorkerBoundaries()
        {
            var s = new Scenario("Hose: two-worker boundaries", 90000);
            var patch = new HashSet<string>();
            var reserved = new HashSet<string>();
            var relevant = new HashSet<string>();
            var patchRemovedByHose = new HashSet<string>();
            var reservedRemovedByOther = new HashSet<string>();
            var unexpected = new HashSet<string>();
            var owners = new Dictionary<string, string>();
            string duplicateOwnership = null;
            bool undiscoveredCleanedNormally = false;
            string hoseQueue = "none";
            string aId = null, bId = null, xId = null, yId = null, uId = null, oId = null;
            IntVec3 uCell = default;
            Pawn A() => Scenario.ById(aId) as Pawn;
            Pawn B() => Scenario.ById(bId) as Pawn;
            HashSet<string> Ids(Job job) => new HashSet<string>(JobThingIds(job).Where(relevant.Contains));
            s.Do("a powered stable with two workers, a four-tile patch, reserved neighbors, fogged blood and outside-Home foam", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn a) = SpawnHoseWorker(s);
                    aId = a.ThingID;
                    Pawn b = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    bId = b.ThingID;
                    CompRobodog.Of(b).SetBattery(1f);
                    CellRect rect = ClearRect(map, stable.InteractionCell, 4, 2, 7f, 16f);
                    IntVec3 p0 = new IntVec3(rect.minX, 0, rect.minZ);
                    foreach (IntVec3 cell in new[]
                             {
                                 p0, p0 + IntVec3.East, p0 + IntVec3.North, p0 + IntVec3.East + IntVec3.North
                             })
                        patch.Add(SpawnHoseFilth(s, cell,
                            patch.Count % 2 == 0 ? ThingDefOf.Filth_Blood : ThingDefOf.Filth_FireFoam));
                    IntVec3 xCell = p0 + IntVec3.East * 2, yCell = p0 + IntVec3.North * 2;
                    xId = SpawnHoseFilth(s, xCell, ThingDefOf.Filth_Blood);
                    yId = SpawnHoseFilth(s, yCell, ThingDefOf.Filth_FireFoam);
                    reserved.Add(xId);
                    reserved.Add(yId);
                    uCell = p0 + IntVec3.East * 2 + IntVec3.North;
                    uId = SpawnHoseFilth(s, uCell, ThingDefOf.Filth_FireFoam);
                    map.fogGrid.Refog(CellRect.SingleCell(uCell));
                    s.OnFinish(() => Find.CurrentMap?.fogGrid.Unfog(uCell));
                    oId = SpawnHoseFilth(s, p0 + IntVec3.East + IntVec3.North * 2,
                        ThingDefOf.Filth_Blood, home: false);
                    relevant.UnionWith(patch);
                    relevant.UnionWith(reserved);
                    relevant.Add(uId);
                    relevant.Add(oId);
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != aId && pawn.ThingID != bId)
                            return;
                        HashSet<string> ids = Ids(job);
                        if (IsHoseJob(job))
                        {
                            if (hoseQueue == "none")
                                hoseQueue = string.Join(",", ids.OrderBy(id => id));
                        }
                        if (job.def == JobDefOf.Clean && ids.Contains(uId))
                            undiscoveredCleanedNormally = true;
                        foreach (string id in ids)
                        {
                            if (owners.TryGetValue(id, out string prior) && prior != pawn.ThingID)
                                duplicateOwnership ??= $"{id} claimed by {prior} and {pawn.ThingID}: {job}";
                            else
                                owners[id] = pawn.ThingID;
                        }
                    });
                    ObserveHoseFilth(s, filth =>
                    {
                        if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null
                            || filth.Map != Find.CurrentMap)
                            return;
                        string id = filth.ThingID;
                        if (patch.Contains(id))
                        {
                            bool byHose = HoseActive(A()) && JobThingIds(A().CurJob).Contains(id);
                            if (byHose)
                                patchRemovedByHose.Add(id);
                            else
                                unexpected.Add(id);
                        }
                        else if (reserved.Contains(id))
                        {
                            bool byOther = B()?.CurJobDef == JobDefOf.Clean
                                && JobThingIds(B().CurJob).Contains(id);
                            if (byOther)
                                reservedRemovedByOther.Add(id);
                            else
                                unexpected.Add(id);
                        }
                        else if (id == oId)
                            unexpected.Add(id);
                    });
                    IntVec3 aCell = OpenCell(map, p0 + IntVec3.East + IntVec3.North, 2f, 6f);
                    IntVec3 bCell = OpenCell(map, xCell, 2f, 6f, c => c != aCell);
                    Teleport(a, aCell);
                    if (a.CurJob != null)
                        a.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    Teleport(b, bCell);
                    if (b.CurJob != null)
                        b.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    // The other worker's real vanilla Clean job is already assigned and has reserved both neighbors
                    // before the hose worker's network dispatch can consider the patch.
                    Job clean = JobMaker.MakeJob(JobDefOf.Clean);
                    clean.AddQueuedTarget(TargetIndex.A, Scenario.ById(xId));
                    clean.AddQueuedTarget(TargetIndex.A, Scenario.ById(yId));
                    clean.workGiverDef = RobodogWork.Utility.workGivers.First(g => g.Worker is WorkGiver_CleanFilth);
                    b.jobs.StartJob(clean, JobCondition.InterruptForced);
                    Thing x = Scenario.ById(xId);
                    Thing y = Scenario.ById(yId);
                    if (b.CurJobDef != JobDefOf.Clean || !b.HasReserved(x) || !b.HasReserved(y))
                        throw new InvalidOperationException("the other worker did not take and reserve both neighbors");
                    if (a.CanReserve(x) || a.CanReserve(y))
                        throw new InvalidOperationException("the hose worker could still claim a neighbor reserved by the other");
                    DogWork.ReportLocal(a, Net());
                    Net().Tick(force: true);
                    if (!HoseActive(a))
                        throw new InvalidOperationException($"no hose job started: {a.CurJob}; other worker: {b.CurJob}");
                    HashSet<string> hoseIds = Ids(a.CurJob);
                    if (!hoseIds.SetEquals(patch) || hoseIds.Overlaps(reserved))
                        throw new InvalidOperationException(
                            $"the hose job owns [{string.Join(",", hoseIds)}], not only its four-tile patch");
                    HashSet<string> otherIds = Ids(b.CurJob);
                    if (!otherIds.SetEquals(reserved))
                        throw new InvalidOperationException(
                            $"the other worker owns [{string.Join(",", otherIds)}], not exactly its two reserved neighbors");
                })
                .Expect("the hose sprays only its patch while the other worker cleans its reserved neighbors", () =>
                {
                    if (duplicateOwnership != null)
                        throw new InvalidOperationException(duplicateOwnership);
                    if (patch.Any(id => Scenario.ById(id) != null)
                        || reserved.Any(id => Scenario.ById(id) != null))
                        return false;
                    if (!patch.SetEquals(patchRemovedByHose))
                        throw new InvalidOperationException("patch tiles were not all removed by the hose: "
                            + string.Join(", ", unexpected));
                    if (!reserved.SetEquals(reservedRemovedByOther))
                        throw new InvalidOperationException("reserved neighbors were not all removed by the other worker: "
                            + string.Join(", ", unexpected));
                    if (unexpected.Count > 0)
                        throw new InvalidOperationException("unexpected filth removal: "
                            + string.Join(", ", unexpected));
                    if (Scenario.ById(uId) == null)
                        throw new InvalidOperationException("the undiscovered fogged tile was cleaned");
                    if (Scenario.ById(oId) == null)
                        throw new InvalidOperationException("the outside-Home tile was cleaned");
                    return true;
                })
                .Expect("the hose job ends", () => !HoseActive(A()))
                .Expect("the other worker's clean job ends", () => B()?.CurJobDef != JobDefOf.Clean)
                .Do("unfog the undiscovered tile", () => Find.CurrentMap.fogGrid.Unfog(uCell))
                .Do("a later local scan finds it and starts ordinary cleaning", () => DispatchHoseWorker(A(), uCell))
                .Expect("the newly discovered tile is cleaned by ordinary cleaning, not the hose", () =>
                {
                    if (Scenario.ById(uId) != null)
                        return false;
                    if (!undiscoveredCleanedNormally)
                        throw new InvalidOperationException("the discovered tile was not cleaned by a vanilla Clean job");
                    return true;
                })
                .Do("the outside-Home tile is still untouched and ownership stayed unique", () =>
                {
                    if (Scenario.ById(oId) == null)
                        throw new InvalidOperationException("the outside-Home tile was cleaned");
                    if (duplicateOwnership != null)
                        throw new InvalidOperationException(duplicateOwnership);
                    if (unexpected.Count > 0)
                        throw new InvalidOperationException("unexpected filth removal: "
                            + string.Join(", ", unexpected));
                })
                .Summary(() => $"hose queue [{hoseQueue}]; patch {patchRemovedByHose.Count}/{patch.Count}, "
                    + $"reserved {reservedRemovedByOther.Count}/{reserved.Count}; outside Home and fog untouched; "
                    + $"later normal clean: {undiscoveredCleanedNormally}; {Net().Inspect(A())}")
                .Start();
        }

        private static void HoseNoEligibleFilth()
        {
            var s = new Scenario("Hose: no eligible filth", 60000);
            var dirt = new HashSet<string>();
            var outside = new HashSet<string>();
            var normalDirtTargets = new HashSet<string>();
            bool everHose = false;
            string dogId = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            s.Do("a powered stable and worker, Home dirt only, and blood/foam outside Home in scan range", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect dirtRect = ClearRect(map, stable.InteractionCell, 2, 1, 6f, 14f);
                    foreach (IntVec3 cell in dirtRect.Cells)
                        dirt.Add(SpawnHoseFilth(s, cell, ThingDefOf.Filth_Dirt));
                    IntVec3 beside = OpenCell(map, dirtRect.CenterCell, 2f, 6f, c => !dirtRect.Contains(c));
                    CellRect outsideRect = ClearRect(map, beside, 2, 1, 3f, 10f,
                        r => !r.ExpandedBy(1).Overlaps(dirtRect));
                    int i = 0;
                    foreach (IntVec3 cell in outsideRect.Cells)
                        outside.Add(SpawnHoseFilth(s, cell,
                            i++ % 2 == 0 ? ThingDefOf.Filth_Blood : ThingDefOf.Filth_FireFoam, home: false));
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        List<string> ids = JobThingIds(job).ToList();
                        if (IsHoseJob(job))
                            everHose = true;
                        if (job.def == JobDefOf.Clean)
                            foreach (string id in ids)
                                if (dirt.Contains(id))
                                    normalDirtTargets.Add(id);
                    });
                    Teleport(dog, beside);
                    if (dog.CurJob != null)
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    DogWork.ReportLocal(dog, Net());
                    Net().Tick(force: true);
                })
                .Expect("the Home dirt is cleaned by ordinary Clean, with no hose job", () =>
                {
                    if (everHose)
                        throw new InvalidOperationException("the hose started without eligible filth");
                    if (dirt.Any(id => Scenario.ById(id) != null))
                        return false;
                    if (!dirt.SetEquals(normalDirtTargets))
                        throw new InvalidOperationException("ordinary Clean did not target: "
                            + string.Join(", ", dirt.Except(normalDirtTargets)));
                    return true;
                })
                .Wait(300)
                .Do("later scans still start no hose and leave the out-of-Home blood/foam untouched", () =>
                {
                    if (everHose)
                        throw new InvalidOperationException("a later scan started the hose without eligible filth");
                    foreach (string id in outside)
                        if (Scenario.ById(id) == null)
                            throw new InvalidOperationException("blood/foam outside Home was cleaned");
                })
                .Summary(() => $"dirt cleaned normally; {outside.Count} out-of-Home eligible tiles untouched; "
                    + $"hose used: {everHose}; {Net().Inspect(Dog())}")
                .Start();
        }
    }
}
