using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
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

        /// <summary>
        /// The plan's scenario: a colonist takes a 1.5% Quadrunner from its stable for a ride longer than its battery, at
        /// the ridden speed, across a reload; at 0% the dog powers down, the rider steps off and walks on, and the stable's
        /// other dog carries it home.
        /// </summary>
        private static void RideUntilEmpty()
        {
            var s = new Scenario("Ride until 0%", 20000);
            string stableId = null, dogId = null, riderId = null;
            IntVec3 dest = IntVec3.Invalid, downAt = IntVec3.Invalid;
            float ridden = 0f, walking = 0f, unmounted = 0f;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Pawn Rider() => (Pawn)Scenario.ById(riderId);
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
                    unmounted = rider.GetStatValue(StatDefOf.MoveSpeed);
                    Pawn dog = stable.IdleRideable;
                    dogId = dog.ThingID;
                    ExpectOption(rider, stable, FloatMenuOptionProvider_Ride.RideLabel(dog, stable));
                    dest = OpenCell(map, stable.InteractionCell, 70f, 90f);
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
                .SaveAndReload()
                .Expect("still riding after the load", () => Riding.MountOf(Rider()) == Dog())
                .Expect("at 0% the dog powers down and the rider steps off", () =>
                    CompRobodog.Of(Dog()).PoweredDown && Riding.MountOf(Rider()) == null)
                .Do("the rider steps off where the dog stopped and walks on at their own speed", () =>
                {
                    downAt = Dog().Position;
                    walking = Rider().GetStatValue(StatDefOf.MoveSpeed);
                    (string id, IntVec3 cell, string why) = Riding.lastDismount;
                    if (id != riderId || cell.DistanceTo(downAt) > 1.5f)
                        throw new InvalidOperationException($"last dismount {id} at {cell}: {why}; dog at {downAt}");
                    if (Rider().CurJobDef != QR_DefOf.QR_RideTo || Mathf.Abs(walking - unmounted) > 0.01f)
                        throw new InvalidOperationException($"rider {Rider().CurJob?.ToStringSafe()}, speed {walking}, unmounted {unmounted}");
                })
                .Expect("the rider arrives on foot", () => Rider().Position == dest && Rider().CurJobDef != QR_DefOf.QR_RideTo)
                .Expect("the stable's other dog carries the powered-down dog home", () => Stable().Docked.Contains(Dog()))
                .Summary(() => $"ridden {ridden:F1} c/s, walking {walking:F1} c/s; powered down {downAt.DistanceTo(dest):F0} cells short")
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
