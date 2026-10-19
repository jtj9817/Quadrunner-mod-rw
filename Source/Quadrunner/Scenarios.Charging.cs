using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>T2: battery drain, power-down and charging. Rooms are built in code on clear ground.</summary>
    public static partial class Scenarios
    {
        /// <summary>A walled steel room: conduit under the north wall's middle, door in the south wall's middle.</summary>
        private class TestRoom
        {
            public CellRect rect;
            public string doorId, batteryId;

            public IntVec3 Door => new IntVec3(rect.CenterCell.x, 0, rect.minZ);
            public IntVec3 Conduit => new IntVec3(rect.CenterCell.x, 0, rect.maxZ);
            /// <summary>Two cells in front of the door.</summary>
            public IntVec3 Outside => Door + IntVec3.South * 2;
            public bool Contains(IntVec3 c) => rect.ContractedBy(1).Contains(c);
            public Building_Door DoorThing => (Building_Door)Scenario.ById(doorId);
            public CompPowerBattery Battery => Scenario.ById(batteryId)?.TryGetComp<CompPowerBattery>();

            public bool OnNet => Battery?.PowerNet != null
                && Find.CurrentMap.powerNetGrid.TransmittedPowerNetAt(Conduit) == Battery.PowerNet;

            public ChargeSpot Spot => ChargeSpots.All(Find.CurrentMap).FirstOrDefault(s => Contains(s.cell));
        }

        /// <summary>Open ground with no building or pawn on it; plants and items are cleared away.</summary>
        private static bool ClearCell(IntVec3 c, Map map) => c.InBounds(map) && c.Walkable(map) && !c.Fogged(map)
            && !c.Roofed(map) && !c.GetTerrain(map).IsWater && c.GetFirstBuilding(map) == null && c.GetFirstPawn(map) == null;

        /// <summary>
        /// A cell within <paramref name="maxDist"/> of <paramref name="near"/> whose <paramref name="area"/> is all
        /// <see cref="ClearCell"/>; that area's plants and items are destroyed.
        /// </summary>
        private static IntVec3 FindClearArea(Map map, IntVec3 near, float maxDist, Func<IntVec3, CellRect> area,
            Predicate<IntVec3> extra = null)
        {
            if (!CellFinder.TryFindRandomCellNear(near, map, (int)maxDist,
                    c => area(c).Cells.All(x => ClearCell(x, map)) && (extra == null || extra(c)), out IntVec3 cell, 3000))
                throw new InvalidOperationException($"no clear area within {maxDist} cells of {near}");
            foreach (IntVec3 c in area(cell).Cells)
                foreach (Thing t in c.GetThingList(map).Where(t => t.def.category == ThingCategory.Plant
                             || t.def.category == ThingCategory.Item).ToList())
                    t.Destroy();
            return cell;
        }

        /// <summary>
        /// A size×size rect <paramref name="minDist"/>–<paramref name="maxDist"/> cells from <paramref name="near"/>, clear
        /// for <paramref name="margin"/> cells around, its door side reachable from there.
        /// </summary>
        private static CellRect ClearRect(Map map, IntVec3 near, int size, int margin, float minDist, float maxDist,
            Func<CellRect, bool> extra = null)
        {
            CellRect RectAt(IntVec3 c) => new CellRect(c.x - size / 2, c.z - size / 2, size, size);
            IntVec3 center = FindClearArea(map, near, maxDist, c => RectAt(c).ExpandedBy(margin), c =>
            {
                float d = c.DistanceTo(near);
                CellRect r = RectAt(c);
                return d >= minDist && d <= maxDist && !r.ExpandedBy(margin).Contains(near) && (extra == null || extra(r))
                    && map.reachability.CanReach(near, new IntVec3(c.x, 0, r.minZ - 2), PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors));
            });
            return RectAt(center);
        }

        private static Thing SpawnBuilding(Scenario s, ThingDef def, ThingDef stuff, IntVec3 cell, Rot4 rot)
        {
            Thing thing = ThingMaker.MakeThing(def, stuff);
            thing.SetFaction(Faction.OfPlayer);
            return s.Track(GenSpawn.Spawn(thing, cell, Find.CurrentMap, rot));
        }

        private static Thing SpawnBattery(Scenario s, Map map, IntVec3 cell, float pct)
        {
            Thing battery = SpawnBuilding(s, ThingDefOf.Battery, null, cell, Rot4.North);
            battery.TryGetComp<CompPowerBattery>().SetStoredEnergyPct(pct);
            return battery;
        }

        /// <summary>
        /// The room's walls, door, conduit and roof, plus a battery touching the conduit from outside the north wall (none if
        /// pct &lt; 0): batteries join a net only through a transmitter they touch.
        /// </summary>
        private static TestRoom BuildRoom(Scenario s, CellRect rect, float batteryPct = 1f)
        {
            Map map = Find.CurrentMap;
            var room = new TestRoom { rect = rect };
            // The conduit first, so the wall goes up over it as a colonist would build it.
            SpawnBuilding(s, ThingDefOf.PowerConduit, null, room.Conduit, Rot4.North);
            foreach (IntVec3 c in rect.EdgeCells.Where(c => c != room.Door))
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
            room.doorId = SpawnBuilding(s, ThingDefOf.Door, ThingDefOf.Steel, room.Door, Rot4.North).ThingID;
            if (batteryPct >= 0f)
                room.batteryId = SpawnBattery(s, map, room.Conduit + IntVec3.North, batteryPct).ThingID;
            List<IntVec3> roofed = rect.Cells.ToList();
            foreach (IntVec3 c in roofed)
                map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            s.OnFinish(() =>
            {
                foreach (IntVec3 c in roofed)
                    Find.CurrentMap.roofGrid.SetRoof(c, null);
            });
            return room;
        }

        private static Pawn SpawnDog(Scenario s, IntVec3 cell)
        {
            Pawn dog = PawnGenerator.GeneratePawn(new PawnGenerationRequest(QR_DefOf.QR_Quadrunner, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            return s.Track((Pawn)GenSpawn.Spawn(dog, cell, Find.CurrentMap));
        }

        private static void Teleport(Pawn pawn, IntVec3 cell)
        {
            pawn.Position = cell;
            pawn.Notify_Teleported();
        }

        /// <summary>Sets the battery and makes the dog think again, as the seek check would on its next job.</summary>
        private static void SetBatteryAndRethink(Pawn dog, float level)
        {
            CompRobodog.Of(dog).SetBattery(level);
            dog.jobs.EndCurrentJob(JobCondition.InterruptForced);
        }

        private static bool ChargingIn(Pawn dog, TestRoom room) =>
            dog.CurJobDef == QR_DefOf.QR_ChargeAtConduit && room.Contains(dog.CurJob.targetA.Cell);

        private static IntVec3 ReachableCellAway(Pawn pawn, float minDist, float maxDist)
        {
            Map map = pawn.Map;
            if (!CellFinder.TryFindRandomCellNear(pawn.Position, map, (int)maxDist, c => c.Standable(map)
                    && c.DistanceTo(pawn.Position) >= minDist && c.DistanceTo(pawn.Position) <= maxDist
                    && c.GetFirstPawn(map) == null && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 cell))
                throw new InvalidOperationException($"no reachable cell {minDist}-{maxDist} cells from {pawn.Position}");
            return cell;
        }

        private static void Walk(Pawn pawn, IntVec3 cell) =>
            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, cell));

        /// <summary>Drain per cell walked, the 10% seek threshold, power-down at 0% and power-up, across a reload.</summary>
        private static void DrainAndPowerDown()
        {
            var s = new Scenario("Battery drain and power-down", 8000);
            string dogId = null;
            IntVec3 from = IntVec3.Invalid, to = IntVec3.Invalid;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            s.Do("spawn an unassigned dog", () => dogId = SpawnDog(s, FreeCellNearCenter(Find.CurrentMap)).ThingID)
                .Expect("10% lasts about 150 cells", () => Math.Abs(Comp().Props.seekChargeAt / Comp().Props.drainPerCell - 150f) < 2f)
                .Do("battery 50%, walk 30-40 cells", () =>
                {
                    Comp().SetBattery(0.5f);
                    from = Dog().Position;
                    to = ReachableCellAway(Dog(), 30f, 40f);
                    Walk(Dog(), to);
                })
                .Expect("arrives", () => Dog().Position == to)
                .Expect("drained per cell walked, not below the seek threshold", () =>
                {
                    float used = 0.5f - Comp().Battery, straight = Comp().Props.drainPerCell * from.DistanceTo(to);
                    QR.Log($"walked {from.DistanceTo(to):F1} cells straight-line, used {used:P2} (straight-line {straight:P2})");
                    return used >= straight * 0.95f && used <= straight * 1.8f && !Comp().WantsCharge;
                })
                .Do("battery 10.5%, walk 20 cells", () =>
                {
                    Comp().SetBattery(0.105f);
                    to = ReachableCellAway(Dog(), 18f, 22f);
                    Walk(Dog(), to);
                })
                .Expect("below 10% it wants a charge", () => Comp().Battery <= 0.1f && Comp().WantsCharge)
                .Do("battery 0.3%, walk 20 cells", () =>
                {
                    Comp().SetBattery(0.003f);
                    Walk(Dog(), ReachableCellAway(Dog(), 18f, 22f));
                })
                .Expect("powers down at 0%: downed, can't move", () => Comp().Battery == 0f && Comp().PoweredDown && Dog().Downed
                    && !Dog().health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                .SaveAndReload()
                .Expect("still powered down after the reload, still wanting a charge", () =>
                    Comp().Battery == 0f && Comp().PoweredDown && Dog().Downed && Comp().WantsCharge)
                .Do("charge to 50%", () => Comp().SetBattery(0.5f))
                .Expect("powered up: the condition is gone and it can move", () => !Comp().PoweredDown && !Dog().Downed
                    && Dog().health.capacities.CapableOf(PawnCapacityDefOf.Moving))
                .Summary(() => Dog() == null ? "dog gone" : $"battery {Comp().Battery:P1}")
                .Start();
        }

        /// <summary>A low dog finds the room by the conduit, lies down, draws the battery and gets up when full.</summary>
        private static void ConduitCharging()
        {
            var s = new Scenario("Conduit charging", 8000);
            TestRoom room = null;
            string dogId = null;
            float storedBefore = 0f, batteryBefore = 0f;
            int tickBefore = 0;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            s.Do("a roofed room with a conduit under its wall and a full battery; an unassigned dog outside", () =>
                {
                    Map map = Find.CurrentMap;
                    room = BuildRoom(s, ClearRect(map, map.Center, 7, 4, 0f, 40f));
                    dogId = SpawnDog(s, room.Door + IntVec3.South * 4).ThingID;
                })
                .Expect("the battery is on the conduit's net", () => room.OnNet)
                .Expect("the room has a cached charge spot next to the conduit", () =>
                    room.Spot != null && room.Spot.cell == room.Conduit + IntVec3.South)
                .Do("dump and highlight the spots (debug actions)", () =>
                {
                    DebugActions.DumpSpots();
                    DebugActions.HighlightSpots();
                    storedBefore = room.Battery.StoredEnergy;
                })
                .Do("battery 10%", () => SetBatteryAndRethink(Dog(), 0.1f))
                .Expect("it goes to charge in the room", () => ChargingIn(Dog(), room))
                .Expect("lies down on the spot and charges", () => Dog().Position == room.Spot.cell
                    && Dog().GetPosture() == PawnPosture.LayingOnGroundNormal && Comp().Battery > 0.12f)
                .Do("note the level", () =>
                {
                    batteryBefore = Comp().Battery;
                    tickBefore = Find.TickManager.TicksGame;
                })
                .Wait(500)
                .Expect("charges at the conduit rate", () =>
                {
                    float rate = (Comp().Battery - batteryBefore) / (Find.TickManager.TicksGame - tickBefore);
                    QR.Log($"conduit rate {rate:F6}/tick (setting {Comp().Props.conduitChargePerTick:F6})");
                    return Math.Abs(rate / Comp().Props.conduitChargePerTick - 1f) < 0.1f;
                })
                .SaveAndReload()
                .Expect("still charging after the reload", () => ChargingIn(Dog(), room))
                .Expect("full, then it gets up", () => Comp().Battery == 1f && !Comp().WantsCharge
                    && Dog().CurJobDef != QR_DefOf.QR_ChargeAtConduit)
                .Expect("drew about 90 Wd from the battery", () =>
                {
                    float used = storedBefore - room.Battery.StoredEnergy;
                    return used > 88f && used < 96f;
                })
                .Summary(() => room?.Battery == null ? "" :
                    $"battery {storedBefore:F1} → {room.Battery.StoredEnergy:F1} Wd, dog {Comp()?.Battery:P0}")
                .Start();
        }

        /// <summary>
        /// The search skips a room behind a forbidden door and a room by a wind turbine (the mod setting), and with no
        /// reachable spot left the dog goes to its stable and charges there.
        /// </summary>
        private static void ChargingMaze()
        {
            var s = new Scenario("Charging maze", 12000);
            TestRoom near = null, far = null, windy = null;
            string dogId = null, stableId = null;
            IntVec3 start = IntVec3.Invalid;
            int prevDistance = QuadrunnerMod.Settings.turbineDistance;
            s.OnFinish(() =>
            {
                QuadrunnerMod.Settings.turbineDistance = prevDistance;
                ChargeSpots.ClearAll();
            });
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("a stable; a room by a wind turbine; a near room with a forbidden door, a far one open; a dog", () =>
                {
                    Map map = Find.CurrentMap;
                    stableId = SpawnStable(s, powered: true, battery: true).ThingID;
                    start = FreeCellNearCenter(map);
                    // The turbine stands 6 cells beyond the north wall, turned east so its clear area runs along the
                    // wall, not into the room: the spot is 4 cells from the turbine's area.
                    windy = BuildRoom(s, ClearRect(map, start, 7, 10, 15f, 45f), batteryPct: -1f);
                    Thing turbine = SpawnBuilding(s, ThingDefOf.WindTurbine, null, windy.Conduit + IntVec3.North * 6, Rot4.East);
                    var zone = WindTurbineUtility.CalculateWindCells(turbine.Position, turbine.Rotation, turbine.def.size)
                        .Concat(turbine.OccupiedRect().Cells).ToList();
                    bool FarFromTurbine(CellRect r) => zone.All(z => r.ClosestCellTo(z).DistanceTo(z) > 10f);
                    near = BuildRoom(s, ClearRect(map, start, 7, 3, 7f, 14f, FarFromTurbine));
                    far = BuildRoom(s, ClearRect(map, start, 7, 3, 20f, 35f, FarFromTurbine));
                    near.DoorThing.SetForbidden(true, warnOnFail: false);
                    dogId = SpawnDog(s, start).ThingID;
                })
                .Expect("both batteries are on their conduits' nets", () => near.OnNet && far.OnNet)
                .Expect("cached spots in the near and far rooms, none by the turbine", () =>
                    near.Spot != null && far.Spot != null && windy.Spot == null)
                .Do("turbine distance 0", () =>
                {
                    QuadrunnerMod.Settings.turbineDistance = 0;
                    ChargeSpots.ClearAll();
                })
                .Expect("then the room by the turbine has a spot", () => windy.Spot != null)
                .Do("turbine distance back to 6", () =>
                {
                    QuadrunnerMod.Settings.turbineDistance = 6;
                    ChargeSpots.ClearAll();
                })
                .Expect("and none again", () => windy.Spot == null)
                .Do("the unassigned dog at 10%", () => SetBatteryAndRethink(Dog(), 0.1f))
                .Expect("skips the near room behind the forbidden door, charges in the far one", () => ChargingIn(Dog(), far))
                .Do("back to the start, forbid the far door too, assign the dog to the stable", () =>
                {
                    Teleport(Dog(), start);
                    far.DoorThing.SetForbidden(true, warnOnFail: false);
                    Comp().stable = Stable();
                    SetBatteryAndRethink(Dog(), 0.1f);
                })
                .Expect("no reachable spot: it heads for the stable", () => Dog().CurJobDef == QR_DefOf.QR_Dock)
                .Expect("docks", () => Stable().Docked.Contains(Dog()))
                .SaveAndReload()
                .Expect("charges to full in the stable", () => Comp().Battery == 1f && !Comp().WantsCharge)
                .Summary(() => Dog() == null ? "dog gone" : $"battery {Comp().Battery:P0}")
                .Start();
        }

        /// <summary>
        /// Prison cells never, bedrooms only when nobody sleeps there, freezers allowed, the allowed area respected; a
        /// room that stops qualifying mid-charge (roof opened) makes the dog get up.
        /// </summary>
        private static void ChargingRoomRules()
        {
            var s = new Scenario("Charging room rules", 10000);
            TestRoom room = null;
            string dogId = null, bedId = null, sleeperId = null;
            Area_Allowed area = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            Building_Bed Bed() => (Building_Bed)Scenario.ById(bedId);
            Pawn Sleeper() => (Pawn)Scenario.ById(sleeperId);
            Room TheRoom() => room.Spot.cell.GetRoom(Find.CurrentMap);
            string Why() => ChargeSpots.UsableBy(room.Spot, Dog(), out string why) ? null : why;
            s.Do("a 9x9 room with a bed; a dog outside", () =>
                {
                    Map map = Find.CurrentMap;
                    room = BuildRoom(s, ClearRect(map, map.Center, 9, 4, 0f, 40f));
                    bedId = SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel,
                        new IntVec3(room.rect.minX + 2, 0, room.rect.minZ + 2), Rot4.North).ThingID;
                    dogId = SpawnDog(s, room.Door + IntVec3.South * 4).ThingID;
                    sleeperId = Crafter().ThingID;
                })
                .Expect("a spot, usable", () => room.OnNet && room.Spot != null && Why() == null)
                .Do("the bed for prisoners", () => Bed().ForPrisoners = true)
                .Expect("prison cell: never", () => Why() == "prison cell")
                .Do("the bed for colonists; a tired colonist claims it and goes to sleep", () =>
                {
                    Bed().ForOwnerType = BedOwnerType.Colonist;
                    Sleeper().ownership.ClaimBedIfNonMedical(Bed());
                    Sleeper().needs.rest.CurLevel = 0.05f;
                    Sleeper().jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.LayDown, Bed()));
                })
                .Expect("bedroom with a sleeper: not usable", () => Bed().AnyOccupants && Why() == "someone sleeps there")
                .Do("wake the colonist and send them outside", () =>
                {
                    Sleeper().needs.rest.CurLevel = 1f;
                    Teleport(Sleeper(), room.Door + IntVec3.South * 3 + IntVec3.East * 2);
                })
                .Expect("bedroom nobody sleeps in: usable", () =>
                    !Bed().AnyOccupants && TheRoom().Role == RoomRoleDefOf.Bedroom && Why() == null)
                .Do("remove the bed (nobody else sleeps there later)", () =>
                {
                    Sleeper().ownership.UnclaimBed();
                    Bed().Destroy();
                })
                .Do("freezer: -15 °C", () => TheRoom().Temperature = -15f)
                .Expect("freezer: usable", () => TheRoom().Temperature < -5f && Why() == null)
                .Do("an allowed area without the room", () =>
                {
                    Map map = Find.CurrentMap;
                    if (!map.areaManager.TryMakeNewAllowed(out area))
                        throw new InvalidOperationException("couldn't make an allowed area");
                    area[Dog().Position] = true;
                    Dog().playerSettings.AreaRestrictionInPawnCurrentMap = area;
                })
                .Expect("outside the allowed area: not usable", () => Why() == "outside the allowed area")
                .Do("lift the restriction", () =>
                {
                    Dog().playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    area.Delete();
                })
                .Do("the dog at 10%", () => SetBatteryAndRethink(Dog(), 0.1f))
                .Expect("charges in the room", () => ChargingIn(Dog(), room) && Dog().GetPosture() != PawnPosture.Standing)
                .SaveAndReload()
                .Expect("still charging after the reload", () => ChargingIn(Dog(), room))
                .Do("open the roof over one cell", () =>
                    Find.CurrentMap.roofGrid.SetRoof(new IntVec3(room.rect.minX + 1, 0, room.rect.minZ + 1), null))
                .Expect("the room stops qualifying: the dog gets up, still wanting a charge", () =>
                    Dog().CurJobDef != QR_DefOf.QR_ChargeAtConduit && Comp().WantsCharge && Comp().Battery < 1f)
                .Summary(() => Dog() == null ? "dog gone" : $"battery {Comp().Battery:P0}")
                .Start();
        }

        /// <summary>The near room's battery runs dry mid-charge; the dog searches again and finishes in the far room.</summary>
        private static void BatteriesDrainMidCharge()
        {
            var s = new Scenario("Batteries drain mid-charge", 12000);
            TestRoom near = null, far = null;
            string dogId = null;
            float leftNear = 0f;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            s.Do("a near room whose battery holds 30 Wd, a far one full; a dog by the near door", () =>
                {
                    Map map = Find.CurrentMap;
                    near = BuildRoom(s, ClearRect(map, map.Center, 7, 4, 0f, 40f), batteryPct: 0.05f);
                    IntVec3 start = near.Door + IntVec3.South * 4;
                    far = BuildRoom(s, ClearRect(map, start, 7, 3, 22f, 40f));
                    dogId = SpawnDog(s, start).ThingID;
                })
                .Expect("both batteries are on their conduits' nets", () => near.OnNet && far.OnNet)
                .Do("the dog at 10%", () => SetBatteryAndRethink(Dog(), 0.1f))
                .Expect("charges in the near room first", () => ChargingIn(Dog(), near))
                .Expect("the near battery runs dry; the dog leaves it, still wanting a charge", () =>
                    near.Battery.StoredEnergy < 0.01f && !ChargingIn(Dog(), near) && Comp().WantsCharge)
                .Do("note the level", () => leftNear = Comp().Battery)
                .Expect("about 30% came from the near room", () => leftNear > 0.33f && leftNear < 0.45f)
                .Expect("searches again: charges in the far room", () => ChargingIn(Dog(), far))
                .SaveAndReload()
                .Expect("full in the far room", () => Comp().Battery == 1f && !Comp().WantsCharge)
                .Summary(() => $"left the near room at {leftNear:P0}")
                .Start();
        }

        /// <summary>
        /// A low dog goes wherever it is full soonest, travel included: at 10% the distant stable (3x faster) beats
        /// the conduit beside it; nearly full, the conduit wins. Measures the stable's rate.
        /// </summary>
        private static void StableOrConduit()
        {
            var s = new Scenario("Stable or conduit", 10000);
            TestRoom room = null;
            string dogId = null, stableId = null;
            float batteryBefore = 0f;
            int tickBefore = 0;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            CompRobodog Comp() => CompRobodog.Of(Dog());
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("a battery-powered stable with a dog; a room 18-30 cells away", () =>
                {
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true, battery: true);
                    stableId = stable.ThingID;
                    room = BuildRoom(s, ClearRect(map, stable.InteractionCell, 7, 4, 18f, 30f));
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Expect("the stable has power; the room's battery is on its net", () => Stable().CanCharge && room.OnNet)
                .Do("undock the dog at the room's door, 10%", () =>
                {
                    Stable().TryUndock(Dog());
                    Teleport(Dog(), room.Outside);
                    SetBatteryAndRethink(Dog(), 0.1f);
                })
                .Expect("low: the stable fills it sooner, travel included", () => Dog().CurJobDef == QR_DefOf.QR_Dock)
                .Expect("docks", () => Stable().Docked.Contains(Dog()))
                .Do("note the level", () =>
                {
                    batteryBefore = Comp().Battery;
                    tickBefore = Find.TickManager.TicksGame;
                })
                .Wait(300)
                .Expect("charges at the stable rate, much faster than a conduit", () =>
                {
                    float rate = (Comp().Battery - batteryBefore) / (Find.TickManager.TicksGame - tickBefore);
                    QR.Log($"stable rate {rate:F6}/tick (setting {Comp().Props.stableChargePerTick:F6}, conduit {Comp().Props.conduitChargePerTick:F6})");
                    return Math.Abs(rate / Comp().Props.stableChargePerTick - 1f) < 0.1f
                        && rate >= 2.5f * Comp().Props.conduitChargePerTick;
                })
                .SaveAndReload()
                .Expect("full in the stable", () => Comp().Battery == 1f)
                .Do("undock at the room's door again, at 97% and still wanting a charge", () =>
                {
                    Stable().TryUndock(Dog());
                    Teleport(Dog(), room.Outside);
                    Comp().SetBattery(0.05f);
                    SetBatteryAndRethink(Dog(), 0.97f);
                })
                .Expect("nearly full: the conduit beside it is sooner", () => ChargingIn(Dog(), room))
                .Expect("tops up, then goes home and docks", () => Comp().Battery == 1f && Stable().Docked.Contains(Dog()))
                .Summary(() => Dog() == null ? "dog gone" : $"battery {Comp().Battery:P0}")
                .Start();
        }
    }
}
