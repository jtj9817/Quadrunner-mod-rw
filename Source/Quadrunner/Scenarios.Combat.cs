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
    /// <summary>T7: Combatdogs answer pings, work the target queue nearest first and capture the downed.</summary>
    public static partial class Scenarios
    {
        /// <summary>
        /// An unarmed raider with its own defend-point lord, held within 3 cells of where it spawned (vanilla's 28-cell
        /// defend radius let raiders chase a dog home) and never fleeing.
        /// </summary>
        private static Pawn SpawnRaider(Scenario s, IntVec3 cell)
        {
            Faction pirates = Pirates();
            Pawn raider = SpawnPawn(s, pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate, pirates, cell);
            raider.equipment?.DestroyAllEquipment();
            raider.inventory?.DestroyAll();
            LordMaker.MakeNewLord(pirates, new LordJob_DefendPoint(raider.Position, wanderRadius: 2f, defendRadius: 3f,
                addFleeToil: false), Find.CurrentMap, new[] { raider });
            return raider;
        }

        /// <summary><paramref name="attacker"/> hits <paramref name="dog"/> for 1 blunt damage: a ping.</summary>
        private static void Hit(Pawn dog, Pawn attacker) =>
            dog.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 1f, instigator: attacker));

        /// <summary>A dog of <paramref name="stable"/> out in the field at <paramref name="cell"/>.</summary>
        private static Pawn DogOut(Scenario s, Building_Stable stable, IntVec3 cell)
        {
            Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
            if (!stable.TryUndock(dog))
                throw new InvalidOperationException($"couldn't undock {dog}");
            Teleport(dog, cell);
            return dog;
        }

        /// <summary>A roofed room with <paramref name="beds"/> beds placed before its walls (so its role is set from the start).</summary>
        private static TestRoom RoomWithBeds(Scenario s, CellRect rect, int beds, bool prisoners)
        {
            for (int i = 0; i < beds; i++)
            {
                var bed = (Building_Bed)SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel,
                    new IntVec3(rect.minX + 1 + 2 * i, 0, rect.minZ + 2), Rot4.North);
                if (prisoners)
                    bed.ForPrisoners = true;
            }
            return BuildRoom(s, rect, batteryPct: -1f);
        }

        private static bool Attacking(Pawn dog, Thing target) =>
            dog.Spawned && dog.CurJobDef == JobDefOf.AttackMelee && dog.CurJob.targetA.Thing == target;

        /// <summary>Dead (or gone), or a prisoner in a bed.</summary>
        private static bool DealtWith(string id) =>
            !(Scenario.ById(id) is Pawn p) || p.Dead || p.IsPrisonerOfColony && p.InBed();

        private static string Fate(string id) =>
            !(Scenario.ById(id) is Pawn p) ? "gone" : p.Dead ? "dead"
            : $"{(p.Downed ? "downed" : "standing")}{(p.IsPrisonerOfColony ? " prisoner" : "")}{(p.InBed() ? " in bed" : "")} at {p.PositionHeld}";

        /// <summary>
        /// The plan's Ping chain: raiders 50–70 cells from the stable (outside its 40-cell radius and the Combatdog's
        /// 15-cell engage range) hit one of its Quadrunners; the docked Combatdog goes after the one nearer to it first,
        /// then the other, each ending dead or a prisoner in bed. Heading home, it turns round for a new ping. Reload
        /// partway.
        /// </summary>
        private static void PingChain()
        {
            var s = new Scenario("Ping chain", 30000);
            string stableId = null, combatId = null, quadId = null, nearId = null, farId = null, thirdId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Combatdog() => (Pawn)Scenario.ById(combatId);
            Pawn ById(string id) => (Pawn)Scenario.ById(id);
            IntVec3 field = IntVec3.Invalid;
            s.Do("a stable with a docked Combatdog and a prison cell of three beds; one of its Quadrunners 50–70 cells out; two unarmed raiders by it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    combatId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    RoomWithBeds(s, ClearRect(map, stable.InteractionCell, 7, 1, 6f, 18f), 3, prisoners: true);
                    field = OpenCell(map, stable.InteractionCell, 50f, 70f);
                    Pawn quad = DogOut(s, stable, field);
                    quadId = quad.ThingID;
                    float d = field.DistanceTo(stable.InteractionCell);
                    nearId = SpawnRaider(s, OpenCell(map, field, 3f, 6f, c => c.DistanceTo(stable.InteractionCell) < d - 2f)).ThingID;
                    farId = SpawnRaider(s, OpenCell(map, field, 3f, 6f, c => c.DistanceTo(stable.InteractionCell) > d + 2f)).ThingID;
                    Raid.Recheck(map);
                })
                .Do("the far raider hits the Quadrunner, then the near one", () =>
                {
                    Hit(ById(quadId), ById(farId));
                    Hit(ById(quadId), ById(nearId));
                })
                .Expect("the Combatdog goes for the near raider first", () => Attacking(Combatdog(), ById(nearId)))
                .Expect("it runs beyond the 40-cell service radius", () =>
                    Combatdog().Position.DistanceTo(Stable().Position) > 40f)
                .SaveAndReload()
                .Expect("the near raider: dead or a prisoner in bed", () => DealtWith(nearId))
                .Expect("then the far one", () => DealtWith(farId) || Attacking(Combatdog(), ById(farId)))
                .Expect("the far raider: dead or a prisoner in bed", () => DealtWith(farId))
                .Expect("the Combatdog heads home", () => Combatdog().CurJobDef == QR_DefOf.QR_Dock)
                .Do("a third raider by another Quadrunner out in the field hits it", () =>
                {
                    Map map = Find.CurrentMap;
                    Pawn quad = DogOut(s, Stable(), OpenCell(map, field, 0f, 8f));
                    Pawn third = SpawnRaider(s, OpenCell(map, quad.Position, 2f, 5f));
                    thirdId = third.ThingID;
                    Raid.Recheck(map);
                    Hit(quad, third);
                })
                .Expect("the Combatdog turns round for it", () => Attacking(Combatdog(), ById(thirdId)))
                .Expect("the third raider: dead or a prisoner in bed", () => DealtWith(thirdId))
                .Expect("the Combatdog docks", () => Stable().Docked.Contains(Combatdog()))
                .Summary(() => $"near {Fate(nearId)}, far {Fate(farId)}, third {Fate(thirdId)}; Combatdog " +
                    (Combatdog() == null ? "gone" : $"{Combatdog().CurJob?.ToStringSafe()}, {Combatdog().GetStatValue(StatDefOf.MoveSpeed):F1} c/s"))
                .Start();
        }

        /// <summary>
        /// Captures: four raiders hit a Quadrunner; three go down, one dies. The Combatdog takes one downed raider to the
        /// only prisoner bed and carries the other two to the nearest empty room, past a nearer bedroom, leaving them
        /// downed and not prisoners; the dead one is skipped. Reload while it carries one. Then the patrol: three
        /// waypoints, a raider walled in out of sight keeps the raid on, another stands past waypoint 2. The Combatdog
        /// reaches waypoint 1, spots the raider on its way to waypoint 2 and fights it, then walks 2, 3 and 1 again
        /// (reload in between); with the walled-in raider dead it docks.
        /// </summary>
        private static void CombatdogSweep()
        {
            var s = new Scenario("Combatdog sweep", 30000);
            string stableId = null, combatId = null, deadId = null, walledId = null, roamerId = null;
            var downIds = new List<string>();
            TestRoom emptyRoom = null, bedroom = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Combatdog() => (Pawn)Scenario.ById(combatId);
            Pawn ById(string id) => (Pawn)Scenario.ById(id);
            List<Pawn> Down() => downIds.Select(id => (Pawn)Scenario.ById(id)).ToList();
            bool InRoom(Pawn p, TestRoom room) => p != null && p.Spawned && room.Contains(p.Position);
            s.Do("a stable with a docked Combatdog and a one-bed prison cell; a field 20–30 cells out, a bedroom near it and an empty room farther; a Quadrunner and four unarmed raiders in the field", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    combatId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    RoomWithBeds(s, ClearRect(map, stable.InteractionCell, 5, 1, 6f, 16f), 1, prisoners: true);
                    IntVec3 field = OpenCell(map, stable.InteractionCell, 20f, 30f);
                    emptyRoom = BuildRoom(s, ClearRect(map, field, 5, 1, 13f, 22f), batteryPct: -1f);
                    float emptyDist = emptyRoom.rect.CenterCell.DistanceTo(field);
                    bedroom = RoomWithBeds(s, ClearRect(map, field, 5, 1, 5f, emptyDist - 4f), 1, prisoners: false);
                    Pawn quad = DogOut(s, stable, field);
                    var raiders = new List<Pawn>();
                    for (int i = 0; i < 4; i++)
                        raiders.Add(SpawnRaider(s, OpenCell(map, field, 1f, 5f)));
                    Raid.Recheck(map);
                    foreach (Pawn r in raiders)
                        Hit(quad, r);
                    for (int i = 0; i < 3; i++)
                        DownBleeding(s, raiders[i], 1f);
                    downIds.AddRange(raiders.Take(3).Select(r => r.ThingID));
                    deadId = raiders[3].ThingID;
                    raiders[3].Kill(null);
                    s.Track(raiders[3].Corpse);
                    if (raiders.Take(3).Any(r => !stable.Queued(r)))
                        throw new InvalidOperationException("the pings didn't queue the raiders");
                })
                .Expect("the bedroom is a bedroom, the empty room qualifies", () =>
                {
                    Room b = Find.CurrentMap.regionGrid.GetValidRegionAt(bedroom.rect.CenterCell)?.Room;
                    Room e = Find.CurrentMap.regionGrid.GetValidRegionAt(emptyRoom.rect.CenterCell)?.Room;
                    if (b?.Role != RoomRoleDefOf.Bedroom || !Combat.EmptyRoom(e))
                        throw new InvalidOperationException($"bedroom role {b?.Role?.defName}, empty room qualifies {Combat.EmptyRoom(e)}");
                    return true;
                })
                .Expect("the Combatdog carries a downed raider", () =>
                    Combatdog().carryTracker?.CarriedThing is Pawn p && downIds.Contains(p.ThingID))
                .SaveAndReload()
                .Expect("one raider a prisoner in the prison bed, two left downed in the empty room", () =>
                    Down().Count(p => p.IsPrisonerOfColony && p.InBed()) == 1
                    && Down().Count(p => InRoom(p, emptyRoom) && p.Downed && !p.IsPrisonerOfColony) == 2)
                .Expect("none in the bedroom; the Combatdog docks with an empty queue", () =>
                {
                    if (Down().Any(p => InRoom(p, bedroom)))
                        throw new InvalidOperationException("a raider was left in the bedroom");
                    return Stable().Docked.Contains(Combatdog()) && Stable().Targets(Stable().InteractionCell).Count == 0;
                })
                .Do("three patrol waypoints; a raider walled in out of sight; another 8–12 cells past waypoint 2, out of sight of waypoint 1 and the stable", () =>
                {
                    Map map = Find.CurrentMap;
                    Building_Stable stable = Stable();
                    IntVec3 home = stable.InteractionCell;
                    IntVec3 wp1 = OpenCell(map, home, 8f, 12f);
                    IntVec3 wp2 = OpenCell(map, wp1, 25f, 30f, c => c.DistanceTo(home) > 25f);
                    IntVec3 wp3 = OpenCell(map, home, 8f, 12f, c => c.DistanceTo(wp1) > 8f && c.DistanceTo(wp2) > 20f);
                    foreach (IntVec3 c in new[] { wp1, wp2, wp3 })
                        stable.AddWaypoint(c);
                    roamerId = SpawnRaider(s, OpenCell(map, wp2, 8f, 12f,
                        c => c.DistanceTo(wp1) > 20f && c.DistanceTo(home) > 20f && c.DistanceTo(wp3) > 20f)).ThingID;
                    CellRect cage = ClearRect(map, home, 3, 1, 25f, 50f, r => r.CenterCell.DistanceTo(wp2) > 20f);
                    foreach (IntVec3 c in cage.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    walledId = SpawnRaider(s, cage.CenterCell).ThingID;
                    CompRobodog.Of(Combatdog()).waypointsReached = 0;
                    Raid.Recheck(map);
                })
                .Expect("the Combatdog deploys and reaches waypoint 1 before it sees the raider", () =>
                {
                    if (Stable().Queued(ById(roamerId)) && CompRobodog.Of(Combatdog()).waypointsReached == 0)
                        throw new InvalidOperationException("spotted the raider before waypoint 1");
                    return CompRobodog.Of(Combatdog()).waypointsReached >= 1;
                })
                .Expect("on its way to waypoint 2 it spots the raider and fights it", () =>
                {
                    if (CompRobodog.Of(Combatdog()).waypointsReached > 2)
                        throw new InvalidOperationException("walked past waypoint 2 without seeing the raider");
                    return Attacking(Combatdog(), ById(roamerId)) || Stable().Queued(ById(roamerId)) && ById(roamerId).Downed;
                })
                .Expect("the raider dead or down and dealt with", () =>
                    !(ById(roamerId) is Pawn p) || p.Dead || p.Downed && !Stable().Queued(p))
                .SaveAndReload()
                .Expect("the raid still on, it walks waypoints 2, 3 and 1 again", () =>
                {
                    if (Stable().Docked.Contains(Combatdog()))
                        throw new InvalidOperationException("docked while the walled-in raider keeps the raid on");
                    return CompRobodog.Of(Combatdog()).waypointsReached >= 4;
                })
                .Do("the walled-in raider dies: raid over", () =>
                {
                    Pawn walled = ById(walledId);
                    walled.Kill(null);
                    s.Track(walled.Corpse);
                    Raid.Recheck(Find.CurrentMap);
                })
                .Expect("the Combatdog docks", () => Stable().Docked.Contains(Combatdog()))
                .Summary(() => "raiders " + string.Join(", ", downIds.Select(Fate)) + $"; dead one {Fate(deadId)}; "
                    + $"roamer {Fate(roamerId)}, walled-in {Fate(walledId)}; waypoints reached {CompRobodog.Of(Combatdog())?.waypointsReached}")
                .Start();
        }

        private static bool Retreating(Pawn dog) =>
            CompRobodog.Of(dog).Retreating && (dog.CurJobDef == QR_DefOf.QR_Dock || dog.CurJobDef == QR_DefOf.QR_ChargeAtConduit);

        /// <summary>
        /// A Combatdog fighting a pinged raider drops to 10%: it breaks off at once and walks home to charge with its
        /// yellow siren flashing (saved to <c>.devlog/screens/siren.png</c> in an automated run); a second raider's ping
        /// doesn't turn it; charged to full, it goes back out for them. Reload during the retreat.
        /// </summary>
        private static void LowBatteryRetreat()
        {
            var s = new Scenario("Low-battery retreat", 20000);
            string stableId = null, combatId = null, quadId = null, firstId = null, secondId = null;
            int frames = 0, retreatTick = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Combatdog() => (Pawn)Scenario.ById(combatId);
            Pawn ById(string id) => (Pawn)Scenario.ById(id);
            s.Do("a stable with a docked Combatdog; one of its Quadrunners 30–40 cells out, hit by an unarmed raider", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    combatId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    Pawn quad = DogOut(s, stable, OpenCell(map, stable.InteractionCell, 30f, 40f));
                    quadId = quad.ThingID;
                    Pawn first = SpawnRaider(s, OpenCell(map, quad.Position, 2f, 5f));
                    firstId = first.ThingID;
                    Raid.Recheck(map);
                    Hit(quad, first);
                    QR.Log($"first raider {first} ({firstId}) at {first.Position}, {first.Position.DistanceTo(stable.Position):F0} cells from the stable");
                })
                .Expect("the Combatdog reaches the raider and fights", () =>
                    Attacking(Combatdog(), ById(firstId)) && Combatdog().Position.InHorDistOf(ById(firstId).Position, 1.5f))
                .Do("its battery drops to 10%", () => CompRobodog.Of(Combatdog()).SetBattery(0.1f))
                .Expect("it breaks off and heads off to charge, siren on", () => Retreating(Combatdog()))
                .Do("pause; camera on the Combatdog", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    CameraMapConfig config = Find.CameraDriver.config;
                    float minSize = config.sizeRange.min;
                    config.sizeRange.min = 4f;
                    s.OnFinish(() => config.sizeRange.min = minSize);
                    Find.CameraDriver.SetRootPosAndSize(Combatdog().DrawPos, 4f);
                    Find.Selector.ClearSelection();
                    frames = 0;
                })
                .Expect("the siren drawn and saved", () =>
                {
                    // Wait for a lit phase, two frames to draw it, then the capture, written at the end of its frame.
                    if (frames == 0 && !CompRobodog.SirenPhaseLit)
                        return false;
                    frames++;
                    if (frames == 3 && AutoTest.Dir != null)
                    {
                        QR.Log($"siren capture: retreating {CompRobodog.Of(Combatdog()).Retreating}, lit {CompRobodog.SirenPhaseLit}, " +
                            $"moving {Combatdog().pather.Moving}, job {Combatdog().CurJob?.ToStringSafe()}");
                        string dir = Path.GetFullPath(Path.Combine(AutoTest.Dir, "..", "screens"));
                        Directory.CreateDirectory(dir);
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, "siren.png"));
                    }
                    return frames >= 6;
                })
                .Do("resume; a second raider by the first hits the Quadrunner", () =>
                {
                    Scenario.Resume();
                    Pawn quad = ById(quadId);
                    // By the first raider, far out: the Quadrunner has gone home by now, where drafted colonists would shoot it.
                    Pawn second = SpawnRaider(s, OpenCell(Find.CurrentMap, ById(firstId).Position, 2f, 5f));
                    secondId = second.ThingID;
                    QR.Log($"second raider {second} ({secondId}) at {second.Position}; first {Fate(firstId)}");
                    Raid.Recheck(Find.CurrentMap);
                    Hit(quad, second);
                    retreatTick = Find.TickManager.TicksGame;
                })
                .Expect("the ping doesn't turn it: it keeps retreating for 200 ticks", () =>
                {
                    if (!Retreating(Combatdog()))
                        throw new InvalidOperationException($"stopped retreating: {Combatdog().CurJob?.ToStringSafe()}");
                    return Find.TickManager.TicksGame - retreatTick >= 200;
                })
                .SaveAndReload()
                .Expect("it docks or charges at a conduit", () =>
                    Stable().Docked.Contains(Combatdog()) || Combatdog().CurJobDef == QR_DefOf.QR_ChargeAtConduit && !Combatdog().pather.Moving)
                .Expect("charged to full, it goes back out for the raiders", () =>
                    CompRobodog.Of(Combatdog()).Battery >= 1f && (Attacking(Combatdog(), ById(firstId)) || Attacking(Combatdog(), ById(secondId))))
                .Summary(() => Combatdog() == null ? "Combatdog gone"
                    : $"battery {CompRobodog.Of(Combatdog()).Battery:P0}, job {Combatdog().CurJob?.ToStringSafe()}; first {Fate(firstId)}, second {Fate(secondId)}")
                .Start();
        }

        /// <summary>
        /// A Combatdog heading for a pinged raider is drafted (the comp's draft toggle) and holds position; vanilla's melee
        /// order sends it at a farther raider that nobody pinged, past the queued one. Still drafted after a reload, it
        /// leaves the queued raider alone; at 10% it is undrafted and retreats; charged, it goes back for the queued one.
        /// </summary>
        private static void DraftedPriorityTarget()
        {
            var s = new Scenario("Drafted priority target", 20000);
            string stableId = null, combatId = null, nearId = null, farId = null;
            int holdTick = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Combatdog() => (Pawn)Scenario.ById(combatId);
            Pawn ById(string id) => (Pawn)Scenario.ById(id);
            bool Down(string id) => !(ById(id) is Pawn p) || p.Dead || p.Downed;
            s.Do("a stable with a docked Combatdog; a Quadrunner 20–25 cells out hit by a raider; another raider 35–45 cells out the other way", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    combatId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    IntVec3 field = OpenCell(map, stable.InteractionCell, 20f, 25f);
                    Pawn quad = DogOut(s, stable, field);
                    Pawn near = SpawnRaider(s, OpenCell(map, field, 2f, 4f));
                    nearId = near.ThingID;
                    farId = SpawnRaider(s, OpenCell(map, stable.InteractionCell, 35f, 45f, c => c.DistanceTo(field) > 30f)).ThingID;
                    Raid.Recheck(map);
                    Hit(quad, near);
                })
                .Expect("the Combatdog heads for the pinged raider", () => Attacking(Combatdog(), ById(nearId)))
                .Do("draft it with its draft toggle", () =>
                {
                    Pawn dog = Combatdog();
                    Command_Toggle toggle = dog.GetGizmos().OfType<Command_Toggle>().FirstOrDefault(c => c.icon == TexCommand.Draft)
                        ?? throw new InvalidOperationException($"no draft toggle (drafter {(dog.drafter == null ? "missing" : "present")})");
                    toggle.toggleAction();
                })
                .Expect("drafted, it holds position and takes orders", () =>
                    Combatdog().Drafted && Combatdog().CurJobDef == JobDefOf.Wait_Combat && Combatdog().CanTakeOrder)
                .Do("order a melee attack on the far raider (vanilla's order)", () =>
                {
                    Action attack = FloatMenuUtility.GetMeleeAttackAction(Combatdog(), ById(farId), out string fail)
                        ?? throw new InvalidOperationException($"melee order refused: {fail}");
                    attack();
                })
                .Expect("it attacks the far raider, past the queued one", () =>
                {
                    if (Attacking(Combatdog(), ById(nearId)))
                        throw new InvalidOperationException("went for the queued raider instead");
                    return Attacking(Combatdog(), ById(farId));
                })
                .SaveAndReload()
                .Expect("still drafted; the far raider dead or down", () =>
                {
                    if (!Combatdog().Drafted)
                        throw new InvalidOperationException("undrafted by the reload");
                    if (Down(farId))
                    {
                        holdTick = Find.TickManager.TicksGame;
                        return true;
                    }
                    return false;
                })
                .Expect("drafted, it holds position for 300 ticks, leaving the queued raider alone", () =>
                {
                    if (!Combatdog().Drafted || Attacking(Combatdog(), ById(nearId)))
                        throw new InvalidOperationException($"drafted {Combatdog().Drafted}, job {Combatdog().CurJob?.ToStringSafe()}");
                    return Find.TickManager.TicksGame - holdTick >= 300;
                })
                .Do("its battery drops to 10%", () => CompRobodog.Of(Combatdog()).SetBattery(0.1f))
                .Expect("undrafted, it retreats to charge", () => !Combatdog().Drafted && Retreating(Combatdog()))
                .Expect("charged to full, it goes back out for the queued raider", () =>
                    CompRobodog.Of(Combatdog()).Battery >= 1f && Attacking(Combatdog(), ById(nearId)))
                .Expect("the queued raider dead or down; the Combatdog docks", () =>
                    Down(nearId) && Stable().Docked.Contains(Combatdog()))
                .Summary(() => $"near {Fate(nearId)}, far {Fate(farId)}; Combatdog " + (Combatdog() == null ? "gone"
                    : $"drafted {Combatdog().Drafted}, {Combatdog().CurJob?.ToStringSafe()}, battery {CompRobodog.Of(Combatdog()).Battery:P0}"))
                .Start();
        }
    }
}
