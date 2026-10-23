using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>N1–N3: the shared dispatch network — discovery, dedup, persistence, nearest-dog ranking, coordinator
    /// failover, reassignment after death or interruption, war suspension, no-stable combat and wartime medics.</summary>
    public static partial class Scenarios
    {
        /// <summary>Whether the normal queue holds a task for this ThingID.</summary>
        private static bool NetQueued(string targetId) => NetQueueCount(targetId) > 0;

        /// <summary>How many normal-queue tasks name this ThingID (a duplicate report would make it 2).</summary>
        private static int NetQueueCount(string targetId) => Net().NormalQueue.Count(t => t.targetId == targetId);

        /// <summary>The normal-queue task for this ThingID, or null.</summary>
        private static NetworkTask NetTask(string targetId) =>
            Net().NormalQueue.FirstOrDefault(t => t.targetId == targetId);

        /// <summary>The haul work giver every utility dog scans with, for staging tasks by hand.</summary>
        private static WorkGiverDef NetHaulGiver => RobodogWork.Utility.workGivers.First(g => g.defName == "HaulGeneral");

        /// <summary>An idle utility dog explores: its current job is a survey.</summary>
        private static bool NetSurveying(Pawn dog) => dog != null && dog.Spawned && dog.CurJobDef == QR_DefOf.QR_Survey;

        /// <summary>A dog doing normal work, exploring or fighting — never true for a dog standing by in a war.</summary>
        private static bool NetBusy(Pawn dog) => dog != null && dog.Spawned
            && (dog.CurJobDef == JobDefOf.HaulToCell || dog.CurJobDef == JobDefOf.HaulToContainer
                || dog.CurJobDef == JobDefOf.Clean || dog.CurJobDef == QR_DefOf.QR_Survey
                || dog.CurJobDef == JobDefOf.AttackMelee || dog.CurJobDef == JobDefOf.AttackStatic);

        /// <summary>Destroys the corpses of tracked pawns that died during the scenario.</summary>
        private static void NetClearCorpses(Scenario s, params string[] pawnIds)
        {
            s.OnFinish(() =>
            {
                foreach (Corpse corpse in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
                             .OfType<Corpse>()
                             .Where(c => c.InnerPawn != null && pawnIds.Contains(c.InnerPawn.ThingID)).ToList())
                    corpse.Destroy();
            });
        }

        /// <summary>Exactly <paramref name="count"/> of a stackable at an exact cell — the placement helpers drop
        /// things "near" a cell, which would blur distance-sensitive assertions.</summary>
        private static Thing NetStack(Scenario s, ThingDef def, int count, IntVec3 cell)
        {
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            return s.Track(GenSpawn.Spawn(thing, cell, Find.CurrentMap));
        }

        /// <summary>Walk a narrow revealed corridor, discover distant work locally, and return it without a teleport.</summary>
        private static void NetDiscovery()
        {
            var s = new Scenario("Network: discovery beyond the boundary", 15000);
            string dogId = null, farGoldId = null;
            CellRect pile = default;
            IntVec3 farGoldCell = default;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("a stable and a revealed narrow corridor, with gold sixty cells from the worker", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    int spacing = RobodogWork.Utility.surveySpacing;
                    IntVec3 corner = FindClearArea(map, map.Center, 120f,
                        c => new CellRect(c.x, c.z, 67, 3), c => (c.z + 1) % spacing == spacing / 4);
                    CellRect corridor = new CellRect(corner.x, corner.z, 67, 3);
                    foreach (IntVec3 cell in corridor.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    IntVec3 start = corner + new IntVec3(2, 0, 1);
                    Building_Stable stable = SpawnStable(s, powered: false, near: start, minDist: 4f, maxDist: 12f);
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    dogId = dog.ThingID;
                    Teleport(dog, start); // Fixture placement only; every subsequent cell is walked by real survey/work.
                    pile = new CellRect(corner.x + 1, corner.z + 1, 2, 1);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    farGoldCell = corner + new IntVec3(62, 0, 1);
                    farGoldId = NetStack(s, ThingDefOf.Gold, 30, farGoldCell).ThingID;
                    if (farGoldCell.DistanceTo(stable.Position) <= 40f)
                        throw new InvalidOperationException("the distant work must be outside the old stable boundary");
                })
                .Expect("the idle worker surveys the known corridor", () => NetSurveying(Dog()))
                .Expect("walking its survey discovers the far stack exactly once", () =>
                {
                    if (NetQueued(farGoldId) && Dog().Position.DistanceTo(farGoldCell) > 20f)
                        throw new InvalidOperationException("work was reported before any dog reached its scan area");
                    return NetQueueCount(farGoldId) == 1;
                })
                .Expect("the dog brings the gold back across the old boundary", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && !NetQueued(farGoldId))
                .SaveAndReload()
                .Expect("still stored after reload; idle work resumes surveying", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && NetSurveying(Dog()))
                .Summary(() => $"surveyed and hauled from {farGoldCell}; pile {CountIn(pile, ThingDefOf.Gold)}; " +
                    $"at {Dog()?.Position}, job {Dog()?.CurJobDef?.defName}:{Dog()?.CurJob?.targetA}, " +
                    $"goal {Net().Survey.Goal(Dog())}, area {Net().Survey.AreaKey(Dog())}, " +
                    $"scan {Net().Survey.LastScan(Dog())}, sweep {Net().Survey.Sweep}; {Net().Inspect(Dog())}")
                .Start();
        }

        /// <summary>Local scans respect fog, forbidden items and Home cleaning; allowing them enables real work.</summary>
        private static void NetScanEligibility()
        {
            var s = new Scenario("Network: scan eligibility", 12000);
            string dogId = null, forbiddenId = null, foggedId = null, filthId = null;
            CellRect pile = default;
            IntVec3 foggedCell = default, filthCell = default;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("an ordered stationary worker beside forbidden gold, fogged gold and dirt outside Home", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    IntVec3 corner = FindClearArea(map, map.Center, 100f, c => new CellRect(c.x, c.z, 17, 5));
                    pile = new CellRect(corner.x + 1, corner.z + 1, 2, 1);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    Pawn dog = SpawnDog(s, corner + new IntVec3(3, 0, 2));
                    dogId = dog.ThingID;
                    Job hold = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
                    hold.expiryInterval = 2400;
                    dog.jobs.TryTakeOrderedJob(hold);
                    Thing forbidden = NetStack(s, ThingDefOf.Gold, 30, corner + new IntVec3(7, 0, 2));
                    forbidden.SetForbidden(true, warnOnFail: false);
                    forbiddenId = forbidden.ThingID;
                    foggedCell = corner + new IntVec3(12, 0, 2);
                    foggedId = NetStack(s, ThingDefOf.Gold, 30, foggedCell).ThingID;
                    map.fogGrid.Refog(CellRect.SingleCell(foggedCell));
                    s.OnFinish(() => Find.CurrentMap.fogGrid.Unfog(foggedCell));
                    filthCell = corner + new IntVec3(9, 0, 2);
                    filthId = SpawnFilth(s, filthCell);
                    map.areaManager.Home[filthCell] = false;
                })
                .Wait(700)
                .Do("several real scans have ignored all three ineligible targets", () =>
                {
                    if (NetQueued(forbiddenId) || NetQueued(foggedId) || NetQueued(filthId)
                        || Scenario.ById(forbiddenId) == null || Scenario.ById(foggedId) == null
                        || Scenario.ById(filthId) == null || CountIn(pile, ThingDefOf.Gold) != 0
                        || !Find.CurrentMap.fogGrid.IsFogged(foggedCell))
                        throw new InvalidOperationException("a scan exposed or executed ineligible work");
                })
                .Do("allow the gold, reveal the cell, put dirt in Home, release the manual order", () =>
                {
                    Scenario.ById(forbiddenId).SetForbidden(false, warnOnFail: false);
                    Find.CurrentMap.fogGrid.Unfog(foggedCell);
                    Find.CurrentMap.areaManager.Home[filthCell] = true;
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                })
                .Expect("the same targets now execute: both stacks stored and dirt cleaned", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && Scenario.ById(filthId) == null)
                .SaveAndReload()
                .Expect("permitted work remains complete after reload", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && Scenario.ById(filthId) == null)
                .Summary(() => $"gold {CountIn(pile, ThingDefOf.Gold)}; fog/forbidden/Home boundaries respected; {Net().Inspect(Dog())}")
                .Start();
        }

        /// <summary>
        /// N1/N2 dedup and persistence: two far stacks — one open, one walled into a pocket no job can reach. Repeated
        /// rescans and a manual duplicate report never queue the pocket stack twice; the open one is hauled; a reload
        /// keeps the blocked task queued exactly once with its identity; opening the pocket lets it complete.
        /// </summary>
        private static void NetDedupPersistence()
        {
            var s = new Scenario("Network: dedup and queue persistence", 60000);
            string dogId = null, openId = null, pocketId = null;
            var pocketWallIds = new List<string>();
            CellRect pile = default;
            IntVec3 pocketCell = default;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("an unpowered stable with a dog, a stockpile, an open stack 30–40 out and a walled one by it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    IntVec3 open = OpenCell(map, home, 30f, 40f);
                    openId = NetStack(s, ThingDefOf.Gold, 30, open).ThingID;
                    IntVec3 pocket = OpenCell(map, open, 6f, 12f, c => new CellRect(c.x - 1, c.z - 1, 3, 3).Cells
                        .All(n => n.InBounds(map) && n.Standable(map) && !n.Fogged(map)
                            && n.GetFirstBuilding(map) == null && n.GetFirstItem(map) == null));
                    pocketCell = pocket;
                    pocketId = NetStack(s, ThingDefOf.Gold, 30, pocket).ThingID;
                    foreach (IntVec3 c in new CellRect(pocket.x - 1, pocket.z - 1, 3, 3).Cells.Where(c => c != pocket))
                        pocketWallIds.Add(SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North).ThingID);
                })
                .Expect("the dog surveys out to the stacks; both queued exactly once", () =>
                {
                    string missing = !NetQueued(openId) ? openId : !NetQueued(pocketId) ? pocketId : null;
                    if (missing != null && Dog().carryTracker.CarriedThing == null)
                        Teleport(Dog(), CellFinder.StandableCellNear(Scenario.ById(missing).Position, Find.CurrentMap, 3));
                    return NetQueueCount(openId) == 1 && NetQueueCount(pocketId) == 1;
                })
                .Do("a duplicate manual report changes nothing", () =>
                {
                    if (Net().Report(NetworkTaskKind.Haul, Scenario.ById(pocketId), NetHaulGiver))
                        throw new InvalidOperationException("a duplicate report queued a second task");
                })
                .Expect("rescans never duplicate it while the open stack is hauled", () =>
                    NetQueueCount(pocketId) <= 1 && CountIn(pile, ThingDefOf.Gold) == 30)
                .Expect("the pocket task is still there, blocked, exactly once", () =>
                    NetQueueCount(pocketId) == 1 && Scenario.ById(pocketId)?.Position == pocketCell)
                .SaveAndReload()
                .Expect("the queued task survived: one Haul task, still queued, still walled in", () =>
                {
                    NetworkTask task = NetTask(pocketId);
                    return task != null && NetQueueCount(pocketId) == 1 && task.kind == NetworkTaskKind.Haul
                        && task.phase == NetworkTaskPhase.Queued && Scenario.ById(pocketId)?.Position == pocketCell;
                })
                .Do("tear the pocket open", () =>
                {
                    foreach (string id in pocketWallIds)
                        Scenario.ById(id)?.Destroy();
                })
                .Expect("the dog takes it and the pile is complete", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && !NetQueued(pocketId) && !NetQueued(openId))
                .Summary(() => $"pocket task {(NetTask(pocketId) == null ? "done" : "still queued")}, " +
                    $"pile {CountIn(pile, ThingDefOf.Gold)}; {Net().Inspect(Dog())}")
                .Start();
        }
        /// <summary>
        /// N1 nearest assignment across stables by real path: a 41-cell wall between two stables. The north dog is
        /// closer to the south gold as the crow flies but must detour around the wall; the south dog wins. For the
        /// north gold the comparison flips. Both hauls complete physically.
        /// </summary>
        private static void NetNearestDog()
        {
            var s = new Scenario("Network: nearest dog wins", 40000);
            string aId = null, bId = null, aDogId = null, bDogId = null, southId = null, northId = null, blockedId = null;
            CellRect pile = default, wall = default;
            IntVec3 mid = default;
            string southAssignee = null, northAssignee = null, blockedAssignee = null;
            Building_Stable A() => (Building_Stable)Scenario.ById(aId);
            Building_Stable B() => (Building_Stable)Scenario.ById(bId);
            Pawn ADog() => (Pawn)Scenario.ById(aDogId);
            Pawn BDog() => (Pawn)Scenario.ById(bDogId);
            void Station(IntVec3 cell, ref string targetId, ref string assignee)
            {
                // Stage the item only after fixing both posts: discovery during the preceding frame must not
                // let a worker pick it up before path ranking is observed.
                A().TryUndock(ADog());
                B().TryUndock(BDog());
                Teleport(ADog(), mid + new IntVec3(0, 0, 5));
                Teleport(BDog(), mid + new IntVec3(0, 0, -14));
                ADog().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                BDog().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                Thing target = NetStack(s, ThingDefOf.Gold, 30, cell);
                targetId = target.ThingID;
                Find.CurrentMap.fogGrid.Unfog(cell);
                Net().Report(NetworkTaskKind.Haul, target, NetHaulGiver);
                Net().Tick(force: true);
                assignee = NetTask(targetId)?.assigneeId;
            }
            s.Do("a 41-cell wall, a stable north and one south, gold by each side, a stockpile south", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    IntVec3 left = FindClearArea(map, map.Center, 120f, c => new CellRect(c.x, c.z, 41, 1).ExpandedBy(3));
                    wall = new CellRect(left.x, left.z, 41, 1);
                    foreach (IntVec3 c in wall.Cells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    mid = wall.CenterCell;
                    IntVec3 south = mid + new IntVec3(0, 0, -3);
                    pile = ClearRect(map, south, 3, 1, 6f, 12f, r => r.Cells.All(c => c.z < wall.minZ));
                    Stockpile(s, pile, ThingDefOf.Gold);
                    Building_Stable a = SpawnStable(s, powered: false, near: mid + new IntVec3(0, 0, 3), minDist: 10f, maxDist: 16f);
                    Building_Stable b = SpawnStable(s, powered: false, near: south, minDist: 22f, maxDist: 34f);
                    aId = a.ThingID;
                    bId = b.ThingID;
                    aDogId = s.Track(a.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    bDogId = s.Track(b.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Do("both dogs at their posts; the south gold is staged", () =>
                    Station(mid + new IntVec3(0, 0, -3), ref southId, ref southAssignee))
                .Expect("the south dog wins despite the north dog's shorter beeline", () => southAssignee == bDogId)
                .Expect("it hauls the south gold; the north dog never touches it", () =>
                {
                    if (Targets(ADog(), Scenario.ById(southId)))
                        throw new InvalidOperationException("the wall-detoured dog took the south gold");
                    return CountIn(pile, ThingDefOf.Gold) == 30;
                })
                .Do("restage at the posts; now the north gold", () =>
                    Station(mid + new IntVec3(0, 0, 3), ref northId, ref northAssignee))
                .Expect("this time the north dog wins", () => northAssignee == aDogId)
                .Expect("both sides delivered", () =>
                {
                    if (Targets(BDog(), Scenario.ById(northId)))
                        throw new InvalidOperationException("the south dog took the north gold");
                    return CountIn(pile, ThingDefOf.Gold) == 60;
                })
                .Do("wall in the nearer north dog, then stage another north-side stack", () =>
                {
                    foreach (IntVec3 cell in CellRect.CenteredOn(mid + new IntVec3(0, 0, 5), 1).EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    Station(mid + new IntVec3(0, 0, 3), ref blockedId, ref blockedAssignee);
                })
                .Expect("the unreachable near dog cannot win; the reachable south dog takes it", () => blockedAssignee == bDogId)
                .Expect("the reachable dog delivers the third stack", () => CountIn(pile, ThingDefOf.Gold) == 90)
                .Summary(() => $"south -> {(southAssignee == bDogId ? "B" : southAssignee)}, " +
                    $"north -> {(northAssignee == aDogId ? "A" : northAssignee)}, caged A -> B; {Net().Inspect()}")
                .Start();
        }

        /// <summary>
        /// N2 coordination: stable A hands the queue to stable B, B to the newest-built dog, the dog to itself again
        /// after a reload — same task identity all the way — and then, with no stable at all, dispatches the work to
        /// completion. One selected-node inspection screenshot of the fallback coordinator for the changed surface.
        /// </summary>
        private static void NetCoordinatorFailover()
        {
            var s = new Scenario("Network: coordinator failover", 50000);
            string aId = null, bId = null, d1Id = null, d2Id = null, goldId = null;
            CellRect pile = default;
            int taskId = 0;
            Pawn D1() => (Pawn)Scenario.ById(d1Id);
            Pawn D2() => (Pawn)Scenario.ById(d2Id);
            s.Do("stables A then B with a dog each kept charging; a staged haul waits for a free dog", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable a = SpawnStable(s, powered: false);
                    Building_Stable b = SpawnStable(s, powered: false, near: a.Position, minDist: 16f, maxDist: 30f);
                    aId = a.ThingID;
                    bId = b.ThingID;
                    d1Id = s.Track(a.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    d2Id = s.Track(b.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    // Charging dogs are unavailable, so the staged task has to wait for the failover to play out.
                    CompRobodog.Of(D1()).SetBattery(0.04f);
                    CompRobodog.Of(D2()).SetBattery(0.04f);
                    IntVec3 home = a.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 25f, 40f)).ThingID;
                    // Fogged staging would be refused by Report: reveal the target cell as a scout would have.
                    map.fogGrid.Unfog(Scenario.ById(goldId).Position);
                    Net().Report(NetworkTaskKind.Haul, Scenario.ById(goldId), NetHaulGiver);
                    Net().Tick(force: true);
                    taskId = NetTask(goldId)?.id ?? 0;
                })
                .Expect("the oldest stable coordinates; the task waits", () =>
                    Net().Coordinator.ThingID == aId && NetTask(goldId) != null && NetTask(goldId).assigneeId == null)
                .Do("stable A burns down", () => Scenario.ById(aId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f)))
                .Do("settle the network", () => Net().Tick(force: true))
                .Expect("stable B takes over with the same task", () =>
                    Net().Coordinator.ThingID == bId && NetTask(goldId)?.id == taskId && NetTask(goldId).assigneeId == null)
                .Do("stable B burns down too", () => Scenario.ById(bId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f)))
                .Do("settle the network", () => Net().Tick(force: true))
                .Expect("the newest-built dog coordinates, queue intact", () =>
                    Net().Coordinator.ThingID == d2Id && NetTask(goldId)?.id == taskId)
                .SaveAndReload()
                .Do("settle the network", () => Net().Tick(force: true))
                .Expect("after the reload the same dog still coordinates the same task", () =>
                    Net().Coordinator.ThingID == d2Id && NetTask(goldId)?.id == taskId && NetQueueCount(goldId) == 1)
                .Do("free the fallback coordinator to take the pending haul", () => CompRobodog.Of(D2()).SetBattery(1f))
                .Expect("the fallback coordinator acknowledges a real haul", () =>
                    NetTask(goldId)?.assigneeId == d2Id && NetTask(goldId).jobId == D2().CurJob?.loadID)
                .Do("pause and inspect the fallback coordinator's route", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    foreach (Window window in Find.WindowStack.Windows.ToList())
                        window.Close(doCloseSound: false);
                    int width = Screen.width, height = Screen.height;
                    Screen.SetResolution(1280, 720, false);
                    s.OnFinish(() => Screen.SetResolution(width, height, false));
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(D2(), playSound: false);
                    Find.CameraDriver.SetRootPosAndSize(D2().DrawPos, 6f);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                    s.OnFinish(() => Find.Selector.ClearSelection());
                });
            // One real selected-node capture of the changed fallback-coordinator inspection surface (frames, not ticks).
            CaptureArtwork(s, "network-inspection", () => { });
            s.Do("resume", () => Scenario.Resume())
                .Do("the newest dog is lost; fail over again to the older active dog", () =>
                {
                    D2().Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    NetClearCorpses(s, d2Id);
                    Net().Tick(force: true);
                })
                .Expect("the older dog coordinates the same task", () =>
                    Net().Coordinator?.ThingID == d1Id && NetTask(goldId)?.id == taskId)
                .Do("the surviving worker is freed up", () => CompRobodog.Of(D1()).SetBattery(1f))
                .Expect("with no stable at all, a dog still dispatches the work and finishes it", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && !NetQueued(goldId))
                .Summary(() => $"coordinator {Net().Coordinator?.LabelShortCap ?? "none"}; {Net().Inspect(D1())}")
                .Start();
        }

        /// <summary>
        /// N2 finality: the last dog dies and the last stable burns — both queues clear, a reload
        /// resurrects nothing, and a brand-new stable and dog never pick up the discarded backlog.
        /// </summary>
        private static void NetLastNodeClears()
        {
            var s = new Scenario("Network: last node clears the backlog", 30000);
            string stableId = null, dogId = null, goldId = null;
            CellRect pile = default;
            IntVec3 goldCell = default;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("a stable, its dog surveying far away", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    goldCell = OpenCell(map, home, 12f, 20f);
                    map.fogGrid.Unfog(goldCell);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    Teleport(Dog(), OpenCell(map, home, 60f, 80f));
                    Net().Tick(force: true);
                })
                .Expect("the dog surveys", () => NetSurveying(Dog()))
                .Do("stage a haul it cannot reach quickly", () =>
                {
                    Net().Report(NetworkTaskKind.Haul, Scenario.ById(goldId), NetHaulGiver);
                    Net().Tick(force: true);
                })
                .Expect("the task is queued", () => NetQueued(goldId))
                .Do("the dog is killed, then the stable burns", () =>
                {
                    Dog().Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    NetClearCorpses(s, dogId);
                    Scenario.ById(stableId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f));
                    Net().Tick(force: true);
                })
                .Expect("no eligible node: both queues are cleared", () =>
                    Net().Coordinator == null && Net().NormalQueue.Count == 0 && Net().WarQueue.Count == 0)
                .SaveAndReload()
                .Expect("nothing resurrected by the load", () =>
                    Net().NormalQueue.Count == 0 && !NetQueued(goldId))
                .Do("a new stable far away with a new dog", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: false, near: goldCell, minDist: 45f, maxDist: 70f);
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    Net().Tick(force: true);
                })
                .Expect("the discarded backlog stays discarded", () =>
                    !NetQueued(goldId) && Scenario.ById(goldId)?.Position == goldCell && Net().Coordinator != null)
                .Wait(300)
                .Expect("the old stack was never touched", () => Scenario.ById(goldId)?.Position == goldCell)
                .Summary(() => $"queue {Net().NormalQueue.Count}; coordinator {Net().Coordinator?.LabelShortCap ?? "none"}")
                .Start();
        }

        /// <summary>
        /// N2 reassignment: the dog walking a stack home is killed — the other dog takes the same work and delivers it.
        /// Then a manual interruption of the second haul: the network re-issues it, nothing is duplicated, all delivered.
        /// </summary>
        private static void NetDeathReassignment()
        {
            var s = new Scenario("Network: death and interruption reassignment", 50000);
            string stableId = null, d1Id = null, d2Id = null, gold1Id = null, gold2Id = null;
            CellRect pile = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn D1() => (Pawn)Scenario.ById(d1Id);
            Pawn D2() => (Pawn)Scenario.ById(d2Id);
            s.Do("an unpowered stable with two dogs, a stockpile and two stacks 30–40 out", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    d1Id = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    d2Id = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    IntVec3 one = OpenCell(map, home, 30f, 40f);
                    gold1Id = NetStack(s, ThingDefOf.Gold, 30, one).ThingID;
                    gold2Id = NetStack(s, ThingDefOf.Gold, 30,
                        OpenCell(map, home, 30f, 40f, c => c.DistanceTo(one) >= 15f)).ThingID;
                })
                .Do("D1 stationed near stack 1, D2 home; the task is staged and D1 wins it", () =>
                {
                    Stable().TryUndock(D1());
                    Stable().TryUndock(D2());
                    Teleport(D1(), OpenCell(Find.CurrentMap, Scenario.ById(gold1Id).Position, 10f, 15f));
                    Teleport(D2(), Stable().InteractionCell);
                    D1().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    D2().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Net().Report(NetworkTaskKind.Haul, Scenario.ById(gold1Id), NetHaulGiver);
                    Net().Tick(force: true);
                    if (NetTask(gold1Id)?.assigneeId != d1Id)
                        throw new InvalidOperationException("the nearer dog didn't get the task");
                })
                .Expect("D1 walks the stack home, carrying nothing yet", () =>
                    Targets(D1(), Scenario.ById(gold1Id)) && D1().carryTracker.CarriedThing == null)
                .Do("D1 is killed mid-task", () =>
                {
                    D1().Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    NetClearCorpses(s, d1Id);
                })
                .Expect("the unfinished work is reassigned to D2", () =>
                    NetQueueCount(gold1Id) <= 1 && (NetTask(gold1Id)?.assigneeId == d2Id
                        || Targets(D2(), Scenario.ById(gold1Id))))
                .Expect("stack 1 delivered anyway", () => CountIn(pile, ThingDefOf.Gold) == 30 && !NetQueued(gold1Id))
                .Do("D2 stationed near stack 2; the task is staged", () =>
                {
                    Stable().TryUndock(D2());
                    Teleport(D2(), OpenCell(Find.CurrentMap, Scenario.ById(gold2Id).Position, 10f, 15f));
                    D2().jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Net().Report(NetworkTaskKind.Haul, Scenario.ById(gold2Id), NetHaulGiver);
                    Net().Tick(force: true);
                    if (NetTask(gold2Id)?.assigneeId != d2Id)
                        throw new InvalidOperationException("D2 didn't get the second task");
                })
                .Expect("D2 is on its way with the second stack, carrying nothing yet", () =>
                    Targets(D2(), Scenario.ById(gold2Id)) && D2().carryTracker.CarriedThing == null)
                .SaveAndReload()
                .Expect("the same acknowledged assignment and job survive reload without duplication", () =>
                    NetQueueCount(gold2Id) == 1 && NetTask(gold2Id)?.assigneeId == d2Id
                    && NetTask(gold2Id).jobId == D2().CurJob?.loadID)
                .Do("a manual interruption ends the job", () =>
                    D2().jobs.EndCurrentJob(JobCondition.InterruptForced))
                .Expect("the network re-issues the unfinished work", () =>
                    NetQueueCount(gold2Id) <= 1 && Targets(D2(), Scenario.ById(gold2Id)))
                .Expect("stack 2 delivered; nothing duplicated", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && !NetQueued(gold1Id) && !NetQueued(gold2Id))
                .Summary(() => $"pile {CountIn(pile, ThingDefOf.Gold)}; {Net().Inspect(D2())}")
                .Start();
        }

        /// <summary>
        /// N3 war: a raider appears while the dog carries the far stack — the haul suspends at once (not finished
        /// first), the cargo is dropped safely and stays put, the worker stands by, and the backlog survives a reload.
        /// The raider leaving resumes the work; both stacks end up stored.
        /// </summary>
        private static void NetWarSuspension()
        {
            var s = new Scenario("Network: war suspends hauls", 60000);
            string stableId = null, dogId = null, nearId = null, farId = null, carriedId = null, raiderId = null;
            CellRect pile = default;
            IntVec3 farCell = default, dropCell = default;
            bool dropped = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("an unpowered stable with a dog, a stockpile, a near stack and a far one", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    nearId = NetStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 8f, 12f)).ThingID;
                    Thing far = NetStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 22f, 30f));
                    farId = far.ThingID;
                    farCell = far.Position;
                })
                .Expect("the near stack is found by the local scan", () => NetQueueCount(nearId) == 1)
                .Expect("the survey reaches the far stack", () =>
                {
                    if (!NetQueued(farId) && Dog().carryTracker.CarriedThing == null)
                        Teleport(Dog(), CellFinder.StandableCellNear(farCell, Find.CurrentMap, 2));
                    return NetQueueCount(farId) == 1;
                })
                .Expect("the near stack is stored first", () => CountIn(pile, ThingDefOf.Gold) == 30 && !NetQueued(nearId))
                .Do("watch at normal speed from here", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("the dog carries the far stack", () => Dog().carryTracker.CarriedThing != null)
                .Do("note the cargo, then a raider appears far away", () =>
                {
                    Map map = Find.CurrentMap;
                    carriedId = Dog().carryTracker.CarriedThing.ThingID;
                    Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate)
                        ?? Find.FactionManager.RandomEnemyFaction();
                    Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction));
                    raiderId = s.Track(GenSpawn.Spawn(raider, OpenCell(map, Stable().InteractionCell, 60f, 90f), map)).ThingID;
                    LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                    Raid.Recheck(map);
                })
                .Expect("war: the haul is suspended at once, not finished first", () =>
                    Net().War && Dog().CurJobDef != JobDefOf.HaulToCell && Dog().carryTracker.CarriedThing == null
                    && CountIn(pile, ThingDefOf.Gold) == 30)
                .Expect("the cargo was dropped safely, not lost or delivered", () =>
                {
                    if (Scenario.ById(carriedId) is Thing cargo && cargo.Spawned && cargo.stackCount == 30)
                    {
                        dropped = true;
                        dropCell = cargo.Position;
                    }
                    return dropped;
                })
                .Do("back to ultrafast", () => Scenario.Resume())
                .Wait(600)
                .Expect("no new work during the war: the dropped cargo stays put, the dog stands by", () =>
                {
                    if (NetBusy(Dog()))
                        throw new InvalidOperationException($"the worker is busy during the war: {Dog().CurJobDef}");
                    return Scenario.ById(carriedId)?.Position == dropCell && CountIn(pile, ThingDefOf.Gold) == 30;
                })
                .SaveAndReload()
                .Expect("war and backlog survive the load", () =>
                {
                    if (NetBusy(Dog()))
                        throw new InvalidOperationException($"busy after the reload: {Dog().CurJobDef}");
                    return Net().War && NetQueueCount(carriedId) == 1
                        && Scenario.ById(carriedId)?.Position == dropCell && CountIn(pile, ThingDefOf.Gold) == 30;
                })
                .Do("the raider leaves during weapon cooldown", () =>
                {
                    Pawn raider = (Pawn)Scenario.ById(raiderId);
                    raider.stances.SetStance(new Stance_Cooldown(300, LocalTargetInfo.Invalid,
                        raider.equipment.PrimaryEq.PrimaryVerb));
                    raider.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.North);
                    Raid.Recheck(Find.CurrentMap);
                })
                .Expect("the threat is gone, the backlog resumes", () =>
                    !Net().War && !Raid.Active(Find.CurrentMap) && Dog().CurJobDef == JobDefOf.HaulToCell)
                .Expect("the dropped stack and the rest delivered after the war", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && !NetQueued(farId))
                .Summary(() => $"cargo from {farCell} dropped at {dropCell}; pile {CountIn(pile, ThingDefOf.Gold)}; " +
                    $"{Net().Inspect(Dog())}; " + ColonistStates())
                .Start();
        }

        /// <summary>
        /// N3 without a stable: a free worker and a free Combatdog. The worker's backlog waits out the war while the
        /// Combatdog — coordinator among the dogs — takes the central war order, keeps it across a reload, deals with
        /// the raider, and the worker delivers the gold once the war ends.
        /// </summary>
        private static void NetNoStableCombat()
        {
            var s = new Scenario("Network: combatdog without a stable", 50000);
            string workerId = null, combatId = null, raiderId = null, goldId = null;
            CellRect pile = default;
            IntVec3 goldCell = default;
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            Pawn Combatdog() => (Pawn)Scenario.ById(combatId);
            s.Do("a free worker and a free Combatdog, a stockpile and no stable", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    IntVec3 home = FindClearArea(map, map.Center, 100f, c => CellRect.CenteredOn(c, 8));
                    pile = ClearRect(map, home, 3, 1, 0f, 4f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    workerId = SpawnDog(s, home).ThingID;
                    combatId = SpawnPawn(s, QR_DefOf.QR_Combatdog, Faction.OfPlayer,
                        OpenCell(map, home, 25f, 40f)).ThingID;
                })
                .Expect("the worker surveys; no war yet", () => !Net().War && NetSurveying(Worker()))
                .Do("a raider near the worker, then the war-time backlog: gold by the pile", () =>
                {
                    Map map = Find.CurrentMap;
                    raiderId = SpawnRaider(s, OpenCell(map, Worker().Position, 8f, 15f)).ThingID;
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    goldCell = OpenCell(map, pile.CenterCell, 4f, 8f);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    map.fogGrid.Unfog(goldCell);
                    if (!Net().Report(NetworkTaskKind.Haul, Scenario.ById(goldId), NetHaulGiver))
                        throw new InvalidOperationException("the war-time haul report was refused");
                })
                .Expect("war: with no stable, the newest dog coordinates", () =>
                {
                    Net().Tick(force: true);
                    return Net().War && Net().Coordinator.ThingID == combatId;
                })
                .Expect("the Combatdog engages; the worker stands by and the gold waits", () =>
                {
                    if (Net().War && NetBusy(Worker()))
                        throw new InvalidOperationException($"the worker is busy during the war: {Worker().CurJobDef}");
                    return Attacking(Combatdog(), Scenario.ById(raiderId))
                        && Scenario.ById(goldId)?.Position == goldCell;
                })
                .SaveAndReload()
                .Expect("still war, still the same centrally-owned target", () =>
                {
                    if (NetBusy(Worker()))
                        throw new InvalidOperationException($"busy after the reload: {Worker().CurJobDef}");
                    return Net().War && (Attacking(Combatdog(), Scenario.ById(raiderId)) || DealtWith(raiderId))
                        && Scenario.ById(goldId)?.Position == goldCell;
                })
                .Expect("the raider is dealt with", () => DealtWith(raiderId))
                .Expect("war over", () => !Net().War && !Raid.Active(Find.CurrentMap))
                .Expect("the worker resumes and delivers the gold", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && !NetQueued(goldId))
                .Summary(() => $"coordinator {Net().Coordinator?.LabelShortCap ?? "none"}; " +
                    $"worker {Worker()?.CurJob?.ToStringSafe()}; {Net().Inspect(Worker())}")
                .Start();
        }

        /// <summary>
        /// N3 medics: from the first tick of a raid (raider walled in by the casualty) the worker stands by and its
        /// queued haul waits, while the Medirunner keeps rescuing — across a reload mid-carry. The raider's death ends
        /// the war and the worker delivers the gold.
        /// </summary>
        private static void NetMedicWartime()
        {
            var s = new Scenario("Network: medic keeps rescuing in war", 40000);
            string stableId = null, medicId = null, workerId = null, patientId = null, raiderId = null, goldId = null;
            CellRect pile = default, box = default;
            IntVec3 goldCell = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            s.Do("a powered stable with a Medirunner and a worker, a medical bed, gold, a casualty and a walled raider", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    workerId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    SpawnMedicalBed(s, home, 3f, 10f);
                    pile = ClearRect(map, home, 3, 1, 4f, 8f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    goldCell = OpenCell(map, home, 8f, 12f);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    IntVec3 field = OpenCell(map, home, 18f, 28f);
                    Pawn patient = map.mapPawns.FreeColonistsSpawned
                        .FirstOrDefault(p => !p.Downed && !p.IsPrisonerOfColony)
                        ?? SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, field);
                    Teleport(patient, field);
                    DownBleeding(s, patient, 6f);
                    patientId = patient.ThingID;
                    box = ClearRect(map, field, 3, 1, 6f, 14f);
                    foreach (IntVec3 c in box.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    Faction pirates = Pirates();
                    Pawn raider = SpawnPawn(s, pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate, pirates, box.CenterCell);
                    raider.equipment?.DestroyAllEquipment();
                    raider.inventory?.DestroyAll();
                    LordMaker.MakeNewLord(pirates, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                    raiderId = raider.ThingID;
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(map);
                    // The worker is docked for the war, so its backlog has to be staged: a known task waiting it out.
                    Net().Report(NetworkTaskKind.Haul, Scenario.ById(goldId), NetHaulGiver);
                    Net().Tick(force: true);
                })
                .Expect("war from the start: the worker stands by docked, its queued haul waiting", () =>
                    Net().War && Stable().Docked.Contains(Worker()) && NetQueued(goldId)
                    && Scenario.ById(goldId)?.Position == goldCell)
                .Expect("the Medirunner keeps rescuing through the war", () =>
                    Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Scenario.ById(patientId))
                .Expect("it carries the casualty", () => Medic().carryTracker.CarriedThing == Scenario.ById(patientId))
                .SaveAndReload()
                .Expect("the casualty is in bed, the worker still standing by", () =>
                {
                    if (NetBusy(Worker()))
                        throw new InvalidOperationException($"busy after the reload: {Worker().CurJobDef}");
                    return Scenario.ById(patientId) is Pawn p && p.InBed()
                        && Scenario.ById(goldId)?.Position == goldCell && CountIn(pile, ThingDefOf.Gold) == 0;
                })
                .Do("the walled raider is killed", () =>
                {
                    Scenario.ById(raiderId).Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    Raid.Recheck(Find.CurrentMap);
                })
                .Expect("war over", () => !Net().War && !Raid.Active(Find.CurrentMap))
                .Expect("the worker finally hauls the gold", () => CountIn(pile, ThingDefOf.Gold) == 30)
                .Summary(() => $"{Net().Inspect()}; patient {(Scenario.ById(patientId) is Pawn p ? (p.InBed() ? "in bed" : p.PositionHeld.ToString()) : "gone")}; " + ColonistStates())
                .Start();
        }
    }
}
