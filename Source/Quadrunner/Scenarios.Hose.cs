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
    /// of more than three distinct blood/firefoam tiles, and then keeps spraying the whole patch until it is clean,
    /// one native Verb_ArcSpray-shaped band (about 11 x 7 cells around each aimed tile) at a time. Smaller patches and
    /// unrelated filth stay ordinary Clean work. These fixtures stage real filth, let the normal network discovery and
    /// job construction start the work, and observe actual native jobs and filth removals; they never assert a private
    /// implementation detail such as the saved per-tile work map.
    /// </summary>
    public static partial class Scenarios
    {
        private const string HoseJobDefName = "QR_CleanWithHose";

        private static readonly AccessTools.FieldRef<Filth, int> FilthGrowTick =
            AccessTools.FieldRefAccess<Filth, int>("growTick");

        private static Action<Filth> hoseFilthObserver;

        // Fixture-only observation of every staged filth removal, including several in one spray application.
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
        /// already older than vanilla's 600-tick fresh-filth gate. <paramref name="thickness"/> lets a fixture prove
        /// layer-by-layer thinning. The fixture's flags are restored when it ends.
        /// </summary>
        private static string SpawnHoseFilth(Scenario s, IntVec3 cell, ThingDef def, bool home = true,
            int thickness = 1, bool manageFlags = true)
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
            filth.thickness = thickness;
            FilthGrowTick(filth) = Find.TickManager.TicksGame - 1000;
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

        /// <summary>Stages one filth object per patch cell, alternating blood and firefoam, skipping fixture cells.</summary>
        private static List<string> SpawnHosePatch(Scenario s, CellRect rect, ISet<IntVec3> skip = null)
        {
            var ids = new List<string>();
            int i = 0;
            foreach (IntVec3 cell in rect.Cells)
            {
                if (skip != null && skip.Contains(cell))
                    continue;
                ids.Add(SpawnHoseFilth(s, cell,
                    i++ % 2 == 0 ? ThingDefOf.Filth_Blood : ThingDefOf.Filth_FireFoam));
            }
            return ids;
        }

        /// <summary>
        /// One uniform terrain for the patch cells, so same-def, same-thickness tiles need the same work per 30-tick
        /// application and a fixture can attribute several removals in one tick to one spray.
        /// </summary>
        private static void SetHoseTerrain(Scenario s, IEnumerable<IntVec3> cells)
        {
            Map map = Find.CurrentMap;
            var prior = new List<(IntVec3 cell, TerrainDef terrain)>();
            foreach (IntVec3 cell in cells)
            {
                prior.Add((cell, map.terrainGrid.TerrainAt(cell)));
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
            }
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null)
                    return;
                foreach ((IntVec3 cell, TerrainDef terrain) in prior)
                    now.terrainGrid.SetTerrain(cell, terrain);
            });
        }

        /// <summary>Undocks and teleports the worker to an exact fixture cell, then clears any current order.</summary>
        private static void PlaceHoseWorker(Pawn dog, IntVec3 cell)
        {
            if (!dog.Spawned && !(CompRobodog.Of(dog)?.stable?.TryUndock(dog) ?? false))
                throw new InvalidOperationException("the hose worker could not undock");
            Teleport(dog, cell);
            if (dog.CurJob != null)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        /// <summary>
        /// Puts the worker beside <paramref name="near"/> (fixture placement), clears any survey/order, then performs one
        /// real local network scan and dispatch, exactly as the dog's own scan would.
        /// </summary>
        private static void DispatchHoseWorker(Pawn dog, IntVec3 near, Predicate<IntVec3> extra = null)
        {
            PlaceHoseWorker(dog, OpenCell(dog.MapHeld, near, 2f, 6f, c => c != near && (extra == null || extra(c))));
            DogWork.ReportLocal(dog, Net());
            Net().Tick(force: true);
        }

        /// <summary>
        /// A real drafted colonist reservation on <paramref name="thing"/>: a player-forced wait job the colonist holds
        /// while drafted, releasing it when the fixture ends the job. The hose and ordinary cleaning must leave the
        /// target alone until then.
        /// </summary>
        private static Pawn ReserveWithDraftedColonist(Scenario s, Thing thing)
        {
            Pawn colonist = Find.CurrentMap.mapPawns.FreeColonistsSpawned
                .FirstOrDefault(p => p.drafter != null && !p.Downed);
            if (colonist == null)
                throw new InvalidOperationException("no colonist available to reserve the fixture's unrelated dirt");
            if (!colonist.Drafted)
                colonist.drafter.Drafted = true;
            Job wait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            wait.playerForced = true;
            colonist.jobs.StartJob(wait, JobCondition.InterruptForced);
            if (colonist.CurJob != wait)
                throw new InvalidOperationException("the drafted colonist did not start its reservation job");
            if (!colonist.Reserve(thing, wait, errorOnFailed: true))
                throw new InvalidOperationException("the drafted colonist could not reserve the dirt");
            if (!colonist.HasReserved(thing))
                throw new InvalidOperationException("the drafted colonist's reservation is not visible");
            string colonistId = colonist.ThingID;
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                Pawn current = now?.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == colonistId);
                if (current?.jobs.curJob != null)
                    current.jobs.EndCurrentJob(JobCondition.InterruptForced);
            });
            return colonist;
        }

        /// <summary>Records one staged filth removal under the game tick of the spray application that caused it.</summary>
        private static void RecordHoseBatch(Dictionary<int, List<IntVec3>> byTick, IntVec3 cell)
        {
            int tick = Find.TickManager.TicksGame;
            if (!byTick.TryGetValue(tick, out List<IntVec3> cells))
                byTick[tick] = cells = new List<IntVec3>();
            cells.Add(cell);
        }

        /// <summary>The tick with the most removals, and how far those cells spanned on each axis.</summary>
        private static (int count, int spanX, int spanZ) WidestHoseBatch(Dictionary<int, List<IntVec3>> byTick)
        {
            int count = 0, spanX = 0, spanZ = 0;
            foreach (List<IntVec3> cells in byTick.Values)
            {
                if (cells.Count <= count)
                    continue;
                count = cells.Count;
                spanX = cells.Max(c => c.x) - cells.Min(c => c.x);
                spanZ = cells.Max(c => c.z) - cells.Min(c => c.z);
            }
            return (count, spanX, spanZ);
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

        /// <summary>
        /// The dispatch scheduler takes the oldest discovered task first. A patch discovered behind an older ordinary
        /// dirt task must still get the hose: the dirt task is assigned, but the job it builds is the hose for the
        /// four-tile patch queued behind it. This is asserted from the real task ids and the network's assignee, not
        /// by reordering the task list.
        /// </summary>
        private static void HoseDirtFirstFallback()
        {
            var s = new Scenario("Hose: dirt-first fallback", 60000);
            var patch = new HashSet<string>();
            var hoseRemoved = new HashSet<string>();
            var normalRemoved = new HashSet<string>();
            bool dirtCleanedNormally = false, dirtInHose = false, hoseAtThree = false;
            int hoseQueued = -1, dirtTaskId = -1;
            string dogId = null, dirtId = null, assignedTargetId = null, assignedJob = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            int Remaining() => patch.Count(id => Scenario.ById(id) != null);
            s.Do("a powered stable and worker; Home dirt is discovered before a four-tile patch behind it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect clear = ClearRect(map, stable.InteractionCell, 4, 2, 6f, 14f);
                    CellRect rect = new CellRect(clear.minX, clear.minZ, 2, 2);
                    IntVec3 dirtCell = new IntVec3(rect.maxX + 1, 0, rect.minZ);
                    // The dirt alone is reported by the dog's own local scan. The patch is staged only afterwards, so
                    // the network's id order makes the dirt the first task the scheduler takes.
                    dirtId = SpawnHoseFilth(s, dirtCell, ThingDefOf.Filth_Dirt);
                    IntVec3 stand = OpenCell(map, dirtCell, 1f, 2.5f, c => !rect.ExpandedBy(1).Contains(c));
                    PlaceHoseWorker(dog, stand);
                    DogWork.ReportLocal(dog, Net());
                    NetworkTask dirtTask = Net().State.normal.FirstOrDefault(t => t.targetId == dirtId)
                        ?? throw new InvalidOperationException("the local scan did not discover the staged dirt");
                    if (dirtTask.assigneeId != null)
                        throw new InvalidOperationException("the staged dirt was assigned before the patch existed");
                    dirtTaskId = dirtTask.id;
                    foreach (string id in SpawnHosePatch(s, rect))
                        patch.Add(id);
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId)
                            return;
                        List<string> ids = JobThingIds(job).ToList();
                        if (IsHoseJob(job))
                        {
                            if (hoseQueued < 0)
                                hoseQueued = ids.Count(patch.Contains);
                            if (ids.Contains(dirtId))
                                dirtInHose = true;
                        }
                        else if (job.def == JobDefOf.Clean && ids.Contains(dirtId))
                            dirtCleanedNormally = true;
                    });
                    ObserveHoseFilth(s, filth =>
                    {
                        if (!patch.Contains(filth.ThingID)
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
                    DogWork.ReportLocal(dog, Net());
                    foreach (string id in patch)
                    {
                        NetworkTask task = Net().State.normal.FirstOrDefault(t => t.targetId == id)
                            ?? throw new InvalidOperationException("the patch tile was not discovered: " + id);
                        if (task.id <= dirtTaskId)
                            throw new InvalidOperationException(
                                $"patch task #{task.id} is not newer than the dirt task #{dirtTaskId}");
                    }
                    Net().Tick(force: true);
                    NetworkTask assigned = Net().State.normal.FirstOrDefault(t => t.assigneeId == dogId);
                    assignedTargetId = assigned?.targetId;
                    assignedJob = Dog()?.CurJob?.def?.defName;
                    if (assigned == null || assigned.targetId != dirtId)
                        throw new InvalidOperationException(
                            $"the scheduler assigned {assignedTargetId ?? "nothing"}, not the older dirt task {dirtId}");
                    if (!IsHoseJob(Dog()?.CurJob))
                        throw new InvalidOperationException(
                            $"the dirt task built {assignedJob ?? "no job"}, not the hose for the patch behind it");
                    if (Dog().CurJob.loadID != assigned.jobId)
                        throw new InvalidOperationException("the hose job is not the job recorded for the dirt task");
                    var queued = new HashSet<string>(JobThingIds(Dog().CurJob));
                    if (!queued.SetEquals(patch))
                        throw new InvalidOperationException(
                            $"the hose queued [{string.Join(",", queued)}], not the four-tile patch");
                    if (queued.Contains(dirtId))
                        throw new InvalidOperationException("the dirt was queued as a hose target");
                })
                .Expect("the older dirt task is assigned and carries the hose for the patch behind it", () =>
                {
                    if (assignedTargetId == null)
                        return false;
                    if (assignedTargetId != dirtId)
                        throw new InvalidOperationException($"the assigned task was {assignedTargetId}, not the dirt");
                    if (assignedJob != HoseJobDefName)
                        throw new InvalidOperationException($"the assigned job was {assignedJob}, not the hose");
                    if (hoseQueued != patch.Count)
                        throw new InvalidOperationException($"the hose queued {hoseQueued} of {patch.Count} patch tiles");
                    if (dirtInHose)
                        throw new InvalidOperationException("the dirt was queued as a hose target");
                    return true;
                })
                .Expect("the hose cleans the whole patch, including the last three, and never the dirt", () =>
                {
                    if (Remaining() > 0)
                        return false;
                    if (!patch.SetEquals(hoseRemoved))
                        throw new InvalidOperationException("ordinary Clean removed patch tiles: "
                            + string.Join(", ", normalRemoved));
                    if (Scenario.ById(dirtId) == null)
                        throw new InvalidOperationException("the hose removed the dirt");
                    if (!hoseAtThree)
                        throw new InvalidOperationException("the hose was not firing when the last three patch tiles remained");
                    return true;
                })
                .Expect("the queued dirt is cleaned by an ordinary Clean job afterwards", () =>
                {
                    if (Scenario.ById(dirtId) != null)
                        return false;
                    if (!dirtCleanedNormally)
                        throw new InvalidOperationException("the dirt disappeared without an ordinary Clean job");
                    return true;
                })
                .Summary(() => $"dirt task #{dirtTaskId} took the dispatch; hose queued {hoseQueued}/{patch.Count}; "
                    + $"patch by hose {hoseRemoved.Count}/{patch.Count}; last three: {hoseAtThree}; "
                    + $"dirt cleaned normally after: {dirtCleanedNormally}; {Net().Inspect(Dog())}")
                .Start();
        }

        private static void HoseLargerMixedPatch()
        {
            var s = new Scenario("Hose: larger mixed patch", 120000);
            var staged = new HashSet<string>();
            var outsideSafe = new HashSet<string>();
            var hoseRemoved = new HashSet<string>();
            var normalRemoved = new HashSet<string>();
            var byTick = new Dictionary<int, List<IntVec3>>();
            bool everHose = false, hoseAtThree = false, dirtCleanedNormally = false, dirtInHoseQueue = false;
            bool reloaded = false, savedGameReloaded = false;
            int hoseQueued = -1, remainingAtSave = 0, remainingAfterReload = 0, afterBurstRemaining = -1;
            int hoseResumeStart = -1;
            string dogId = null, dirtId = null, colonistId = null, aimId = null;
            IntVec3 aimCell = default;
            Pawn preReloadDog = null;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            Pawn Colonist() => Scenario.ById(colonistId) as Pawn;
            int Remaining() => staged.Count(id => Scenario.ById(id) != null);
            (int count, int spanX, int spanZ) Batch() => WidestHoseBatch(byTick);
            s.Do("a powered stable and worker; a 4x4 mixed patch and unrelated dirt reserved by a drafted colonist", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect clear = ClearRect(map, stable.InteractionCell, 6, 2, 7f, 16f);
                    CellRect rect = new CellRect(clear.minX + 1, clear.minZ + 1, 4, 4);
                    aimCell = new IntVec3(rect.minX, 0, rect.minZ);
                    SetHoseTerrain(s, rect.Cells);
                    // The aim tile is discovered alone first, so the scheduler's id order gives the fixture a known
                    // spray orientation: the dog stands due west and the 4x4 patch lies inside the first band.
                    aimId = SpawnHoseFilth(s, aimCell, ThingDefOf.Filth_Blood);
                    staged.Add(aimId);
                    PlaceHoseWorker(dog, aimCell + IntVec3.West);
                    DogWork.ReportLocal(dog, Net());
                    NetworkTask aimTask = Net().State.normal.FirstOrDefault(t => t.targetId == aimId)
                        ?? throw new InvalidOperationException("the local scan did not discover the patch's first tile");
                    if (aimTask.assigneeId != null)
                        throw new InvalidOperationException("the first patch tile was assigned before the patch existed");
                    foreach (string id in SpawnHosePatch(s, rect, new HashSet<IntVec3> { aimCell }))
                        staged.Add(id);
                    // Ordinary dirt on the aimed tile itself: a real drafted colonist reserves it for the whole hose
                    // run, so neither the queue nor the spray may touch it. A four-tile eligible patch just outside
                    // Home sits inside the same first band: it is never discovered or queued and must stay untouched.
                    dirtId = SpawnHoseFilth(s, aimCell, ThingDefOf.Filth_Dirt, manageFlags: false);
                    Pawn colonist = ReserveWithDraftedColonist(s, Scenario.ById(dirtId));
                    colonistId = colonist.ThingID;
                    Thing dirt = Scenario.ById(dirtId);
                    if (!colonist.HasReserved(dirt))
                        throw new InvalidOperationException("the colonist's dirt reservation did not stick");
                    if (dog.CanReserve(dirt))
                        throw new InvalidOperationException("the hose worker could still claim the reserved dirt");
                    var outsideRect = new CellRect(rect.minX, rect.maxZ + 1, 4, 1);
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
                                dirtInHoseQueue = true;
                            if (hoseQueued < 0)
                                hoseQueued = ids.Count(staged.Contains);
                        }
                        else if (job.def == JobDefOf.Clean && ids.Contains(dirtId))
                            dirtCleanedNormally = true;
                    });
                    ObserveHoseFilth(s, filth =>
                    {
                        if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null
                            || filth.Map != Find.CurrentMap)
                            return;
                        string id = filth.ThingID;
                        if (id == dirtId)
                            return;
                        if (outsideSafe.Contains(id))
                            return;
                        if (!staged.Contains(id))
                            return;
                        int before = Remaining();
                        if (HoseActive(Dog()))
                        {
                            hoseRemoved.Add(id);
                            if (before <= 3)
                                hoseAtThree = true;
                            RecordHoseBatch(byTick, filth.Position);
                        }
                        else
                            normalRemoved.Add(id);
                    });
                    DogWork.ReportLocal(dog, Net());
                    foreach (string id in staged)
                    {
                        NetworkTask task = Net().State.normal.FirstOrDefault(t => t.targetId == id)
                            ?? throw new InvalidOperationException("the patch tile was not discovered: " + id);
                        if (id != aimId && task.id <= aimTask.id)
                            throw new InvalidOperationException(
                                $"patch task #{task.id} is not newer than the first tile's task #{aimTask.id}");
                    }
                    Net().Tick(force: true);
                    NetworkTask assigned = Net().State.normal.FirstOrDefault(t => t.assigneeId == dogId);
                    if (assigned == null || assigned.targetId != aimId)
                        throw new InvalidOperationException(
                            $"the scheduler assigned {assigned?.targetId ?? "nothing"}, not the first patch tile {aimId}");
                    if (!IsHoseJob(Dog()?.CurJob) || Dog().CurJob.loadID != assigned.jobId)
                        throw new InvalidOperationException($"the assigned task did not produce the hose job: {Dog()?.CurJob}");
                    var queued = new HashSet<string>(JobThingIds(Dog().CurJob));
                    if (!queued.SetEquals(staged) || queued.Contains(dirtId))
                        throw new InvalidOperationException(
                            $"the hose queued [{string.Join(",", queued)}], not exactly the patch without dirt");
                })
                .Expect("the hose starts and queues the whole larger patch", () =>
                {
                    if (!everHose)
                        return false;
                    if (hoseQueued != staged.Count)
                        throw new InvalidOperationException(
                            $"the first hose job queued {hoseQueued} of {staged.Count} staged tiles");
                    if (dirtInHoseQueue)
                        throw new InvalidOperationException("the reserved dirt was queued as a hose target");
                    return true;
                })
                .Do("slow to normal so the mid-patch save point is deterministic", () =>
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("the hose is spraying its first aimed tile", () =>
                {
                    if (!HoseActive(Dog()) || Dog().jobs.curDriver.CurToilString != "SprayFilth")
                        return false;
                    if (Dog().Position != aimCell + IntVec3.West)
                        throw new InvalidOperationException(
                            $"the dog sprays from {Dog().Position}, not {aimCell + IntVec3.West}");
                    return true;
                });
            // Real visible spray for the evidence directory: camera on the dog while QR_HoseSpray fires.
            CaptureArtwork(s, "hose-spray", () =>
                Find.CameraDriver.SetRootPosAndSize(Dog().DrawPos, 5f), delaySeconds: 0.2f);
            s.Expect("one 30-tick application cleans several tiles across the patch", () =>
                {
                    (int count, int spanX, int spanZ) = Batch();
                    if (count < 6 || spanX < 3 || spanZ < 3)
                        return false;
                    if (afterBurstRemaining < 0)
                        afterBurstRemaining = Remaining();
                    return true;
                })
                .Do("verify the save point is mid-patch with more than three tiles still dirty", () =>
                {
                    int now = Remaining();
                    if (now != afterBurstRemaining)
                        throw new InvalidOperationException(
                            $"the patch moved from {afterBurstRemaining} to {now} tiles before the save");
                    remainingAtSave = now;
                    if (remainingAtSave <= 3)
                        throw new InvalidOperationException($"only {remainingAtSave} tiles remained before the save");
                    if (!HoseActive(Dog()))
                        throw new InvalidOperationException("the hose job ended before the mid-patch save");
                    preReloadDog = Dog();
                })
                .SaveAndReload()
                .Do("note the reloaded remainder, the reload and the surviving reservation", () =>
                {
                    reloaded = true;
                    savedGameReloaded = !ReferenceEquals(preReloadDog, Dog());
                    remainingAfterReload = Remaining();
                    if (savedGameReloaded && remainingAfterReload > remainingAtSave)
                        throw new InvalidOperationException(
                            $"the save captured {remainingAfterReload} dirty tiles, more than the {remainingAtSave} at save time");
                    Thing dirt = Scenario.ById(dirtId);
                    if (dirt == null)
                        throw new InvalidOperationException("the reserved dirt disappeared across the save");
                    Pawn colonist = Colonist();
                    if (colonist == null)
                        throw new InvalidOperationException("the reserving colonist disappeared across the save");
                    if (!colonist.HasReserved(dirt))
                        throw new InvalidOperationException("the colonist's dirt reservation did not survive the save");
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
                    if (Scenario.ById(dirtId) == null)
                        throw new InvalidOperationException("the spray removed the reserved dirt");
                    foreach (string id in outsideSafe)
                        if (Scenario.ById(id) == null)
                            throw new InvalidOperationException("the spray crossed the Home boundary");
                    if (!staged.SetEquals(hoseRemoved))
                        throw new InvalidOperationException("ordinary Clean removed patch tiles: "
                            + string.Join(", ", normalRemoved));
                    if (!hoseAtThree)
                        throw new InvalidOperationException("the hose was not firing when the last three tiles remained");
                    return true;
                })
                .Expect("the hose job ends", () => !HoseActive(Dog()))
                .Do("release the colonist's real reservation on the dirt", () =>
                {
                    Pawn colonist = Colonist();
                    if (colonist == null)
                        throw new InvalidOperationException("the reserving colonist disappeared before the release");
                    if (colonist.jobs.curJob != null)
                        colonist.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Thing dirt = Scenario.ById(dirtId);
                    if (colonist.HasReserved(dirt))
                        throw new InvalidOperationException("the dirt reservation was not released");
                    if (!Dog().CanReserve(dirt))
                        throw new InvalidOperationException("the hose worker still cannot claim the released dirt");
                })
                .Wait(150)
                .Do("a later local scan starts ordinary cleaning of the released dirt", () =>
                {
                    Thing dirt = Scenario.ById(dirtId);
                    if (dirt != null)
                        DispatchHoseWorker(Dog(), dirt.Position);
                })
                .Expect("the released dirt is then cleaned by an ordinary Clean job, never the hose", () =>
                {
                    if (Scenario.ById(dirtId) != null)
                        return false;
                    if (!dirtCleanedNormally)
                        throw new InvalidOperationException("dirt disappeared without an ordinary Clean job");
                    return true;
                })
                .Summary(() => $"{hoseRemoved.Count}/{staged.Count} patch tiles by hose, {remainingAtSave} saved dirty "
                    + $"({remainingAfterReload} after reload, reloaded {savedGameReloaded}); last three under hose: "
                    + $"{hoseAtThree}, reserved dirt untouched then cleaned normally: {dirtCleanedNormally}, "
                    + $"{outsideSafe.Count} outside Home untouched; {Net().Inspect(Dog())}")
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

        /// <summary>
        /// The broad spray itself: a 5x13 blood patch, a wall shadow, one two-layer tile and rows beyond the band. A
        /// single 30-tick application must clean the wide visible band in one tick, leave the out-of-range rows, the
        /// line-of-sight shadow and the two-layer tile for later applications, and the hose must finish everything,
        /// including its last three tiles.
        /// </summary>
        private static void HoseBroadSprayFootprint()
        {
            var s = new Scenario("Hose: broad spray footprint", 60000);
            var staged = new HashSet<string>();
            var hoseRemoved = new HashSet<string>();
            var normalRemoved = new HashSet<string>();
            var byTick = new Dictionary<int, List<IntVec3>>();
            var outer = new HashSet<string>();
            bool everHose = false, hoseAtThree = false, burstAssessed = false;
            bool burstShadowAlive = false, burstThickAlive = false, burstEdgeCleaned = false, burstOuterAlive = false;
            int hoseQueued = -1;
            string dogId = null, aimId = null, shadowId = null, thickId = null, edgeId = null;
            IntVec3 aimCell = default;
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            int Remaining() => staged.Count(id => Scenario.ById(id) != null);
            s.Do("a powered stable and worker; a 5 x 13 blood patch with a wall shadow, a thick tile and out-of-band rows", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    ClearStagedFilth(map);
                    (Building_Stable stable, Pawn dog) = SpawnHoseWorker(s);
                    dogId = dog.ThingID;
                    CellRect clear = ClearRect(map, stable.InteractionCell, 13, 2, 9f, 20f);
                    CellRect patch = new CellRect(clear.minX, clear.minZ, 5, 13);
                    aimCell = new IntVec3(patch.minX + 2, 0, patch.minZ + 6);
                    IntVec3 wallCell = aimCell + IntVec3.East;
                    IntVec3 shadowCell = aimCell + IntVec3.East * 2;
                    IntVec3 edgeCell = aimCell + IntVec3.North * 5;
                    IntVec3 thickCell = aimCell + IntVec3.South * 5;
                    SetHoseTerrain(s, patch.Cells);
                    // The aimed tile is discovered alone first, so the dog sprays from its known west cell and the
                    // band's width, depth, wall shadow and two-layer behaviour are all deterministic.
                    aimId = SpawnHoseFilth(s, aimCell, ThingDefOf.Filth_Blood);
                    staged.Add(aimId);
                    PlaceHoseWorker(dog, aimCell + IntVec3.West);
                    DogWork.ReportLocal(dog, Net());
                    NetworkTask aimTask = Net().State.normal.FirstOrDefault(t => t.targetId == aimId)
                        ?? throw new InvalidOperationException("the local scan did not discover the first patch tile");
                    if (aimTask.assigneeId != null)
                        throw new InvalidOperationException("the first patch tile was assigned before the patch existed");
                    foreach (IntVec3 cell in patch.Cells)
                    {
                        if (cell == aimCell || cell == wallCell)
                            continue;
                        int thickness = cell == thickCell ? 2 : 1;
                        string id = SpawnHoseFilth(s, cell, ThingDefOf.Filth_Blood, thickness: thickness);
                        staged.Add(id);
                        if (cell == shadowCell)
                            shadowId = id;
                        if (cell == edgeCell)
                            edgeId = id;
                        if (cell == thickCell)
                            thickId = id;
                        if (cell.z == patch.minZ || cell.z == patch.maxZ)
                            outer.Add(id);
                    }
                    if (shadowId == null || edgeId == null || thickId == null || outer.Count != 10)
                        throw new InvalidOperationException(
                            $"the footprint fixture staged shadow/edge/thick/outer cells wrong ({outer.Count} outer)");
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, wallCell, Rot4.North);
                    ObservePackJobs(s, (pawn, job) =>
                    {
                        if (pawn.ThingID != dogId || !IsHoseJob(job))
                            return;
                        everHose = true;
                        if (hoseQueued < 0)
                            hoseQueued = JobThingIds(job).Count(staged.Contains);
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
                            RecordHoseBatch(byTick, filth.Position);
                        }
                        else
                            normalRemoved.Add(filth.ThingID);
                    });
                    DogWork.ReportLocal(dog, Net());
                    foreach (string id in staged)
                    {
                        NetworkTask task = Net().State.normal.FirstOrDefault(t => t.targetId == id)
                            ?? throw new InvalidOperationException("the footprint tile was not discovered: " + id);
                        if (id != aimId && task.id <= aimTask.id)
                            throw new InvalidOperationException(
                                $"patch task #{task.id} is not newer than the first tile's task #{aimTask.id}");
                    }
                    Net().Tick(force: true);
                    if (!HoseActive(dog))
                        throw new InvalidOperationException($"no hose job started: {dog.CurJob}");
                })
                .Expect("the hose queues the whole broad patch", () =>
                {
                    if (!everHose)
                        return false;
                    if (hoseQueued != staged.Count)
                        throw new InvalidOperationException(
                            $"the hose queued {hoseQueued} of {staged.Count} footprint tiles");
                    return true;
                })
                .Expect("the dog sprays its first tile from the fixture's west cell", () =>
                {
                    if (!HoseActive(Dog()) || Dog().jobs.curDriver.CurToilString != "SprayFilth")
                        return false;
                    if (Dog().CurJob.targetA.Thing?.ThingID != aimId)
                        throw new InvalidOperationException(
                            $"the hose aimed at {Dog().CurJob.targetA.Thing}, not the fixture's first tile");
                    if (Dog().Position != aimCell + IntVec3.West)
                        throw new InvalidOperationException(
                            $"the dog sprays from {Dog().Position}, not {aimCell + IntVec3.West}");
                    return true;
                })
                .Do("slow to normal for a deterministic footprint capture", () =>
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal);
            // Real visible spray for the evidence directory: camera on the dog while QR_HoseSpray fires.
            CaptureArtwork(s, "hose-broad-spray", () =>
                Find.CameraDriver.SetRootPosAndSize(Dog().DrawPos, 7f), delaySeconds: 0.2f);
            s.Expect("one 30-tick application cleans a wide band in one tick, respecting range, LOS and layers", () =>
                {
                    (int count, int spanX, int spanZ) = WidestHoseBatch(byTick);
                    if (count < 20 || spanX < 4 || spanZ < 10)
                        return false;
                    if (burstAssessed)
                        return true;
                    burstAssessed = true;
                    burstShadowAlive = Scenario.ById(shadowId) != null;
                    burstThickAlive = Scenario.ById(thickId) != null;
                    burstEdgeCleaned = Scenario.ById(edgeId) == null;
                    burstOuterAlive = outer.All(id => Scenario.ById(id) != null);
                    if (!burstEdgeCleaned)
                        throw new InvalidOperationException("a visible tile at the band edge was not sprayed");
                    if (!burstShadowAlive)
                        throw new InvalidOperationException("the band sprayed a tile with no line of sight");
                    if (!burstThickAlive)
                        throw new InvalidOperationException("one application destroyed a two-layer tile");
                    if (!burstOuterAlive)
                        throw new InvalidOperationException("the band crossed its lateral edge into the outer rows");
                    return true;
                })
                .Do("resume scenario speed after measuring the first band", () => Scenario.Resume())
                .Expect("the hose finishes the whole footprint, including the last three tiles", () =>
                {
                    if (Remaining() > 0)
                        return false;
                    if (!staged.SetEquals(hoseRemoved))
                        throw new InvalidOperationException("ordinary Clean removed footprint tiles: "
                            + string.Join(", ", normalRemoved));
                    if (!hoseAtThree)
                        throw new InvalidOperationException("the hose was not firing when the last three tiles remained");
                    return true;
                })
                .Summary(() => $"hose queue {hoseQueued}/{staged.Count}; one application removed "
                    + $"{WidestHoseBatch(byTick).count} tiles spanning "
                    + $"{WidestHoseBatch(byTick).spanX}x{WidestHoseBatch(byTick).spanZ}; edge {burstEdgeCleaned}, "
                    + $"shadow alive {burstShadowAlive}, thick alive {burstThickAlive}, outer alive {burstOuterAlive}; "
                    + $"last three: {hoseAtThree}; {Net().Inspect(Dog())}")
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
