using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>T6: riding a Quadrunner: the ride order, ridden speed, the dismount rules, power-down mid-ride, the draw offsets.</summary>
    public static partial class Scenarios
    {
        /// <summary>An undrafted colonist 3–8 cells from <paramref name="near"/>; the others stay drafted (<see cref="DraftColonists"/>).</summary>
        private static Pawn UndraftedRider(Scenario s, IntVec3 near)
        {
            Map map = Find.CurrentMap;
            Pawn rider = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed && p.drafter != null)
                ?? SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, near);
            rider.drafter.Drafted = false;
            Teleport(rider, OpenCell(map, near, 3f, 8f));
            return rider;
        }

        /// <summary>Checks that right-clicking <paramref name="clicked"/> with <paramref name="rider"/> selected offers <paramref name="label"/>, enabled.</summary>
        private static void ExpectOption(Pawn rider, Thing clicked, string label)
        {
            List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(new List<Pawn> { rider }, clicked.DrawPos, out FloatMenuContext _);
            FloatMenuOption option = options.FirstOrDefault(o => o.Label == label)
                ?? throw new InvalidOperationException($"no '{label}' option: " + string.Join(" | ", options.Select(o => o.Label))
                    + $"; {rider} at {rider.Position}, menu: {FloatMenuMakerMap.ShouldGenerateFloatMenuForPawn(rider).Reason ?? "ok"}, "
                    + $"job {rider.CurJob.ToStringSafe()}, lord {rider.GetLord().ToStringSafe()}");
            if (option.Disabled)
                throw new InvalidOperationException($"option disabled: {label}");
        }

        private static bool Free(Pawn dog) => dog.CurJobDef != QR_DefOf.QR_BeingRidden && Riding.RiderOf(dog) == null;

        /// <summary>Cells within <paramref name="radius"/> of the supplied pawn's actual vanilla route; a disconnected
        /// route is a fixture failure, not grounds to invent a straight-line substitute.</summary>
        private static List<IntVec3> RideCorridor(Pawn walker, IntVec3 from, IntVec3 to, int radius)
        {
            Map map = Find.CurrentMap;
            var cells = new HashSet<IntVec3>();
            // The pawn overload, with the pather's own cost tuning, is the route Toils_Goto would request; a
            // default-tuned path can differ around water, frames or door costs and leave the real ride outside the
            // revealed corridor.
            using (PawnPath path = map.pathFinder.FindPathNow(from, to, walker,
                       PathFinderCostTuning.For(walker), PathEndMode.OnCell))
            {
                if (!path.Found)
                    throw new InvalidOperationException($"no real ride-fixture route for {walker}: {from}->{to}");
                foreach (IntVec3 node in path.NodesReversed)
                    foreach (IntVec3 cell in GenRadial.RadialCellsAround(node, radius, useCenter: true))
                        if (cell.InBounds(map))
                            cells.Add(cell);
            }
            return cells.ToList();
        }

        /// <summary>
        /// Reveals cells after <see cref="SurveyIsolate"/> installed its full-map fog restore: the corridor added
        /// later in a scenario is still restored because that callback compares every cell with the original fog.
        /// </summary>
        private static void RideReveal(IEnumerable<IntVec3> cells)
        {
            Map map = Find.CurrentMap;
            int count = 0;
            foreach (IntVec3 cell in cells)
                if (cell.InBounds(map) && map.fogGrid.IsFogged(cell))
                {
                    map.fogGrid.Unfog(cell);
                    count++;
                }
            QR.Log($"ride corridor: revealed {count} cells for the worker's local scan");
        }


        /// <summary>
        /// The plan's scenario: a colonist takes a 1.5% Quadrunner from its stable for a ride longer than its battery, at
        /// the ridden speed, across a reload; at 0% the dog powers down, the rider steps off and walks on, and the stable's
        /// other dog carries it home. The rest of the shared map is fogged out for the test and the worker gets a corridor
        /// along the real ride route, so the stop is found by its normal scheduled scan rather than exploration order.
        /// <see cref="SurveyIsolate"/> must keep every player pawn's cell revealed: native post-load handling floods the
        /// whole world around a player-controlled pawn left in fog, which would make the worker survey the entire map.
        /// The two corridor/fog checks below make that precondition and its failure modes explicit.
        /// </summary>
        private static void RideUntilEmpty()
        {
            var s = new Scenario("Ride until 0%", 20000);
            string stableId = null, dogId = null, riderId = null, carrierId = null;
            IntVec3 dest = IntVec3.Invalid, downAt = IntVec3.Invalid;
            float ridden = 0f, walking = 0f, nearest = float.MaxValue;
            bool retrieveQueued = false;
            int revealedAtSetup = -1;
            int revealedBeforeLoad = -1;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Pawn Rider() => (Pawn)Scenario.ById(riderId);
            Pawn Other() => Stable()?.Dogs().FirstOrDefault(d => d.ThingID != dogId);
            int Revealed()
            {
                Map map = Find.CurrentMap;
                int count = 0;
                foreach (IntVec3 cell in map.AllCells)
                    if (!map.fogGrid.IsFogged(cell))
                        count++;
                return count;
            }
            void Track()
            {
                Pawn dog = Dog(), other = Other();
                if (dog != null && other != null && other.Spawned)
                    nearest = Mathf.Min(nearest, other.Position.DistanceTo(dog.Position));
                if (dog != null)
                    foreach (Pawn pawn in Find.CurrentMap.mapPawns.AllPawns)
                        if (Carrying(pawn, dog)) { carrierId = pawn.ThingID; break; }
                retrieveQueued |= Net().NormalQueue.Any(t => t.kind == NetworkTaskKind.Retrieve && t.targetId == dogId);
            }
            s.Do("a powered stable with two Quadrunners; a colonist rides one from the stable 70–90 cells out, the dog at 1.5%", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 2; i++)
                        s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    Pawn rider = UndraftedRider(s, stable.InteractionCell);
                    riderId = rider.ThingID;
                    Pawn dog = stable.IdleRideable;
                    dogId = dog.ThingID;
                    ExpectOption(rider, stable, FloatMenuOptionProvider_Ride.RideLabel(dog, stable));
                    dest = OpenCell(map, stable.InteractionCell, 70f, 90f);
                    // The other dog finds field work only through its real 15-cell scan. This scenario runs on the shared
                    // test map, whose colony, Home and zone geometry can consume a finite priority sweep before the open
                    // ride stretch; reveal a controlled corridor along the planned ride so the observation is a
                    // deterministic local scan rather than leftover exploration order.
                    List<IntVec3> corridor = RideCorridor(rider, stable.InteractionCell, dest, 10);
                    QR.Log($"Ride until 0%: worker corridor {corridor.Count} cells, {stable.InteractionCell} -> {dest}");
                    SurveyIsolate(s, corridor);
                    revealedAtSetup = Revealed();
                    QR.Log($"Ride until 0%: {revealedAtSetup} cells revealed after isolation");
                    if (!Riding.Ride(rider, dog, stable, dest))
                        throw new InvalidOperationException("the ride order failed");
                    CompRobodog.Of(dog).SetBattery(0.015f);
                })
                .Expect("the colonist mounts", () => Riding.MountOf(Rider()) == Dog())
                .Expect("the rider moves at the ridden speed, the dog under them", () =>
                {
                    ridden = Rider().GetStatValue(StatDefOf.MoveSpeed);
                    return Mathf.Abs(ridden - Rideable.Of(Dog()).riddenMoveSpeed) < 0.01f && Riding.RiderOf(Dog()) == Rider()
                        && Dog().Position.DistanceTo(Rider().Position) < 1.5f;
                })
                .Expect("the dog's battery drains as it carries", () =>
                    Riding.MountOf(Rider()) == Dog() && CompRobodog.Of(Dog()).Battery < 0.0125f)
                .Do("keep occupied player-pawn cells revealed across the save", () =>
                {
                    // Pawns can move after initial isolation. Reveal their actual cells without refogging anything
                    // or clearing coverage; any other growth indicates a native flood before the snapshot.
                    int occupiedCells = SurveyRevealPlayerCells(Find.CurrentMap);
                    revealedBeforeLoad = Revealed();
                    int growth = revealedBeforeLoad - revealedAtSetup;
                    if (growth != occupiedCells)
                        throw new InvalidOperationException(
                            $"{revealedAtSetup} cells were revealed after isolation, {revealedBeforeLoad} before the save; "
                            + $"{occupiedCells} occupied cells were newly revealed: a native flood broke the fixture's scope");
                    QR.Log($"Ride until 0%: preserved {occupiedCells} newly occupied cells before the save");
                })
                .SaveAndReload()
                .Expect("still riding after the load", () => Riding.MountOf(Rider()) == Dog())
                .Do("the load kept the rest of the world fogged", () =>
                {
                    // Native Pawn.SpawnSetup floods fog around every player-controlled pawn still standing in it after
                    // a load, even respawningAfterLoad. Catching that here distinguishes a broken fixture precondition
                    // (the whole world became surveyable) from a genuine local-scan liveness failure later.
                    int now = Revealed();
                    if (now != revealedBeforeLoad)
                        throw new InvalidOperationException(
                            $"{revealedBeforeLoad} cells were revealed before the load, {now} after it: "
                            + "a native flood (a player pawn left in fog, a door or a removed fog blocker) broke the fixture's scope");
                    QR.Log($"Ride until 0%: {now} cells revealed across the load (unchanged)");
                })
                .Expect("at 0% the dog powers down and the rider steps off", () =>
                    CompRobodog.Of(Dog()).PoweredDown && Riding.MountOf(Rider()) == null)
                .Do("the rider steps off where the dog stopped and continues the trip on foot", () =>
                {
                    downAt = Dog().Position;
                    walking = Rider().GetStatValue(StatDefOf.MoveSpeed);
                    (string id, IntVec3 cell, string why) = Riding.lastDismount;
                    if (id != riderId || cell.DistanceTo(downAt) > 1.5f)
                        throw new InvalidOperationException($"last dismount {id} at {cell}: {why}; dog at {downAt}");
                    if (Rider().CurJobDef != QR_DefOf.QR_RideTo)
                        throw new InvalidOperationException($"rider {Rider().CurJob?.ToStringSafe()}");
                    // A detour can stop the ride outside the planned corridor: reveal the real leg to the stop too, so
                    // the worker's scheduled 15-cell scan can still reach it.
                    Building_Stable home = Stable();
                    Pawn other = home?.Dogs().FirstOrDefault(d => d.ThingID != dogId);
                    if (other != null)
                        RideReveal(RideCorridor(other, home.InteractionCell, downAt, 10));
                    string fromHome = home == null ? "no stable" : $"{downAt.DistanceTo(home.InteractionCell):F0} cells from the stable";
                    QR.Log($"Ride until 0%: {Dog()} powered down at {downAt}, {fromHome}");
                })
                .Expect("the rider arrives on foot", () =>
                {
                    Track();
                    return Rider().Position == dest && Rider().CurJobDef != QR_DefOf.QR_RideTo;
                })
                .Expect("the worker's local scan discovers the powered-down dog", () =>
                {
                    Track();
                    return retrieveQueued || Stable().Docked.Contains(Dog());
                })
                .Expect("the stable's other dog carries the powered-down dog home", () =>
                {
                    Track();
                    return Stable().Docked.Contains(Dog());
                })
                .Summary(() =>
                {
                    Pawn other = Other();
                    Survey survey = Net().Survey;
                    string near = nearest < float.MaxValue ? $"{nearest:F0}" : "never seen spawned";
                    string coverage = downAt.IsValid
                        ? $"stop area {survey.CategoryAt(downAt)}, observed {survey.Observed(downAt)}, sweep {survey.Sweep}"
                        : "no stop";
                    IntVec3 goal = other == null ? IntVec3.Invalid : survey.Goal(other);
                    string workerGoal = goal.IsValid && downAt.IsValid
                        ? $"{goal} {survey.CategoryAt(goal)}, {goal.DistanceTo(downAt):F0} cells from the stop, "
                            + $"observed {survey.Observed(goal)}"
                        : "none";
                    string fog = $"{revealedAtSetup} cells revealed after isolation";
                    if (revealedBeforeLoad >= 0) fog += $", {revealedBeforeLoad} before the load";
                    fog += $", {Revealed()} at the end";
                    return $"ridden {ridden:F1} c/s, walking {walking:F1} c/s; powered down {downAt.DistanceTo(dest):F0} cells short"
                        + $"; stop {downAt}, {downAt.DistanceTo(Stable()?.InteractionCell ?? downAt):F0} cells from the stable; "
                        + $"other dog {(other == null ? "gone" : other.PositionHeld + " " + other.CurJob?.ToStringSafe())}, "
                        + $"nearest {near} cells to the stop, worker goal {workerGoal}; retrieve queued {retrieveQueued}; "
                        + $"carried by {carrierId ?? "unseen"}; {coverage}; {fog}; "
                        + Net().Inspect().Replace('\n', ' ');
                })
                .Start();
        }

        /// <summary>
        /// The dismount rules: at the destination (the dog goes free), before an indoor room's door (the rider walks in),
        /// when the rider is drafted and when the rider is downed; the ride offered on a dog that is out; a reload mid-ride.
        /// </summary>
        private static void RideAndDismount()
        {
            var s = new Scenario("Ride and dismount", 20000);
            string stableId = null, dogId = null, riderId = null;
            IntVec3 dest = IntVec3.Invalid;
            TestRoom room = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Pawn Rider() => (Pawn)Scenario.ById(riderId);
            bool Mounted() => Riding.MountOf(Rider()) == Dog() && Riding.RiderOf(Dog()) == Rider();
            void Ride(IntVec3 cell)
            {
                // Exact arrival cells are brief states; sample them at normal speed, not once per ultrafast frame.
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                dest = cell;
                Riding.lastDismount = default;
                if (!Riding.Ride(Rider(), Dog(), Stable(), cell))
                    throw new InvalidOperationException("the ride order failed");
            }
            s.Do("a powered stable with a Quadrunner standing out; a colonist rides it 20–30 cells out", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    dogId = dog.ThingID;
                    stable.TryUndock(dog);
                    Pawn rider = UndraftedRider(s, stable.InteractionCell);
                    riderId = rider.ThingID;
                    ExpectOption(rider, dog, FloatMenuOptionProvider_Ride.RideLabel(dog, null));
                    Ride(OpenCell(map, rider.Position, 20f, 30f));
                })
                .Expect("mounted", Mounted)
                .SaveAndReload()
                .Expect("still mounted after the load", Mounted)
                .Do("observe exact arrival at normal speed after reload", () =>
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("at the destination the rider gets off; the dog is free again", () =>
                    Rider().Position == dest && Riding.MountOf(Rider()) == null && Free(Dog()))
                .Do("dismounted at the destination", () =>
                {
                    if (Riding.lastDismount.cell != dest)
                        throw new InvalidOperationException($"last dismount at {Riding.lastDismount.cell}, destination {dest}");
                })
                .Do("a closed room 15–25 cells out; ride into it", () =>
                {
                    room = BuildRoom(s, ClearRect(Find.CurrentMap, Rider().Position, 5, 1, 15f, 25f), batteryPct: -1f);
                    Ride(room.rect.CenterCell);
                })
                .Expect("mounted", Mounted)
                .Expect("the rider gets off before the door and walks in", () =>
                    Riding.MountOf(Rider()) == null && Rider().Position == dest && Free(Dog()))
                .Do("the rider got off outside, by the door", () =>
                {
                    IntVec3 at = Riding.lastDismount.cell;
                    if (Riding.Indoors(at, Find.CurrentMap) || at.DistanceTo(room.Door) > 1.5f)
                        throw new InvalidOperationException($"dismounted at {at}, door {room.Door}");
                })
                .Do("ride out 20–30 cells", () => Ride(OpenCell(Find.CurrentMap, room.Outside, 20f, 30f)))
                .Expect("mounted", Mounted)
                .Wait(30)
                .Do("draft the rider", () => Rider().drafter.Drafted = true)
                .Expect("drafted, the rider gets off; the dog is free", () =>
                    Rider().Drafted && Riding.MountOf(Rider()) == null && Free(Dog()))
                .Do("undraft; ride 20–30 cells again", () =>
                {
                    Rider().drafter.Drafted = false;
                    Ride(OpenCell(Find.CurrentMap, Rider().Position, 20f, 30f));
                })
                .Expect("mounted", Mounted)
                .Wait(30)
                .Do("the rider goes down (anesthetic)", () =>
                {
                    Rider().health.AddHediff(HediffDefOf.Anesthetic);
                    string id = riderId;
                    s.OnFinish(() =>
                    {
                        if (Scenario.ById(id) is Pawn p && p.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Anesthetic) is Hediff h)
                            p.health.RemoveHediff(h);
                    });
                })
                .Expect("downed, the rider is off; the dog is free", () =>
                    Rider().Downed && Riding.MountOf(Rider()) == null && Free(Dog()))
                .Summary(() => $"rider {Rider().Position}, destination {dest}, job {Rider().CurJob?.ToStringSafe()}; last dismount: {Riding.lastDismount.why}")
                .Start();
        }

        /// <summary>
        /// The draw offsets: a mounted rider, the game paused and the camera close, turned to face each way; in an
        /// automated run each pose is saved to <c>.devlog/screens/ride-&lt;facing&gt;.png</c>.
        /// </summary>
        private static void RiderPose()
        {
            var s = new Scenario("Rider pose", 6000);
            string dogId = null, riderId = null;
            int frames = 0;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Pawn Rider() => (Pawn)Scenario.ById(riderId);
            s.Do("a colonist rides a Quadrunner 20–30 cells out", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    dogId = dog.ThingID;
                    Pawn rider = UndraftedRider(s, stable.InteractionCell);
                    riderId = rider.ThingID;
                    if (!Riding.Ride(rider, dog, stable, OpenCell(Find.CurrentMap, stable.InteractionCell, 20f, 30f)))
                        throw new InvalidOperationException("the ride order failed");
                })
                .Expect("mounted", () => Riding.MountOf(Rider()) == Dog())
                .Wait(20)
                .Do("pause; camera on the rider", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    // Closer than the camera allows (min size 11): the test window is only 360 px tall.
                    CameraMapConfig config = Find.CameraDriver.config;
                    float minSize = config.sizeRange.min;
                    config.sizeRange.min = 3f;
                    s.OnFinish(() => config.sizeRange.min = minSize);
                    Find.CameraDriver.SetRootPosAndSize(Rider().DrawPos, 3f);
                    Find.Selector.ClearSelection();
                });
            foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                s.Do($"face {rot.ToStringHuman()}", () =>
                    {
                        Rider().Rotation = rot;
                        Dog().Rotation = rot;
                        frames = 0;
                    })
                    .Expect($"pose {rot.ToStringHuman()} drawn and saved", () =>
                    {
                        // Two frames to draw the pose, then the capture, written at the end of its frame.
                        frames++;
                        if (frames == 3 && AutoTest.Dir != null)
                        {
                            string dir = Path.GetFullPath(Path.Combine(AutoTest.Dir, "..", "screens"));
                            Directory.CreateDirectory(dir);
                            ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"ride-{rot.ToStringHuman().ToLower()}.png"));
                        }
                        return frames >= 6;
                    });
            }
            s.Summary(() => $"offsets N {Riding.Offset(Dog(), Rot4.North)}, E {Riding.Offset(Dog(), Rot4.East)}, " +
                    $"S {Riding.Offset(Dog(), Rot4.South)}, W {Riding.Offset(Dog(), Rot4.West)}")
                .Start();
        }
    }
}
