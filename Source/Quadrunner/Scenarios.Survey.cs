using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// QR-0001: area-aware survey routing behavior. Every fixture isolates geometry, Home and fog inside a controlled
    /// rect (the rest of the map is refogged and later restored), drafts the colonists, and asserts actual movement,
    /// local discovery and completion rather than source text or wiring.
    /// </summary>
    public static partial class Scenarios
    {
        // ---------------------------------------------------------------- fixtures and probes

        private static Survey SurveyOf() => Net().Survey;

        private static bool SurveyQueued(string id) =>
            id != null && Net().NormalQueue.Any(t => t.targetId == id);

        private static int SurveyQueueCount(string id) =>
            id == null ? 0 : Net().NormalQueue.Count(t => t.targetId == id);

        private static int SurveyTaskId(string id) =>
            Net().NormalQueue.FirstOrDefault(t => t.targetId == id)?.id ?? 0;

        private static void SurveyHold(Pawn dog, int ticks)
        {
            Job hold = JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture);
            hold.expiryInterval = ticks;
            if (!dog.jobs.TryTakeOrderedJob(hold))
                throw new InvalidOperationException($"couldn't hold {dog}");
        }

        /// <summary>Find a fixture pad without player structures/pawns. Controlled pads use uniform soil, restore
        /// natural rock/roof/terrain footprints, and temporarily move wildlife so long/tied routes do not depend on seed.</summary>
        private static CellRect SurveyFindRect(Map map, int width, int height, float maxDist, Scenario controlledFixture = null)
        {
            foreach (IntVec3 origin in CellRect.CenteredOn(map.Center, (int)maxDist).ClipInsideMap(map).Cells
                         .OrderBy(c => c.DistanceToSquared(map.Center)))
            {
                var rect = new CellRect(origin.x, origin.z, width, height);
                if (rect.minX < 2 || rect.minZ < 2 || rect.maxX > map.Size.x - 3 || rect.maxZ > map.Size.z - 3)
                    continue;
                bool clear = true;
                foreach (IntVec3 cell in rect.Cells)
                {
                    Building building = cell.GetFirstBuilding(map);
                    Pawn pawn = cell.GetFirstPawn(map);
                    bool scenery = building == null || controlledFixture != null && building.Faction != Faction.OfPlayer
                        && (building.def.building.isNaturalRock || building.def.building.isResourceRock);
                    bool ground = controlledFixture != null || cell.Walkable(map) && !cell.GetTerrain(map).IsWater;
                    bool wildlife = pawn == null || controlledFixture != null && pawn.Faction != Faction.OfPlayer && pawn.RaceProps.Animal;
                    if (scenery && ground && wildlife) continue;
                    clear = false;
                    break;
                }
                if (!clear) continue;
                if (controlledFixture != null)
                {
                    var ground = new List<(IntVec3 cell, TerrainDef terrain, RoofDef roof)>();
                    var rocks = new List<(IntVec3 cell, ThingDef def, ThingDef stuff, Rot4 rotation, int hp)>();
                    var visitors = new List<(string id, IntVec3 cell)>();
                    bool[] priorFog = null;
                    foreach (IntVec3 cell in rect.Cells)
                    {
                        ground.Add((cell, cell.GetTerrain(map), map.roofGrid.RoofAt(cell)));
                        map.roofGrid.SetRoof(cell, null);
                        Building rock = cell.GetFirstBuilding(map);
                        if (rock != null)
                        {
                            if (priorFog == null)
                            {
                                priorFog = new bool[map.cellIndices.NumGridCells];
                                for (int i = 0; i < priorFog.Length; i++)
                                    priorFog[i] = map.fogGrid.IsFogged(i);
                            }
                            rocks.Add((cell, rock.def, rock.Stuff, rock.Rotation, rock.HitPoints));
                            rock.Destroy();
                        }
                        Pawn visitor = cell.GetFirstPawn(map);
                        if (visitor != null)
                        {
                            IntVec3 outside = IntVec3.Invalid;
                            foreach (IntVec3 candidate in map.AllCells)
                                if (!rect.Contains(candidate) && candidate.x >= 2 && candidate.z >= 2
                                    && candidate.x < map.Size.x - 2 && candidate.z < map.Size.z - 2
                                    && candidate.Standable(map) && candidate.GetFirstPawn(map) == null)
                                { outside = candidate; break; }
                            if (!outside.IsValid) throw new InvalidOperationException("no temporary wildlife position outside the pad");
                            visitors.Add((visitor.ThingID, cell));
                            Teleport(visitor, outside);
                        }
                        map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
                    }
                    // Removing natural fog blockers can reveal the connected world. Restore it before the caller
                    // captures its isolation mask, otherwise cleanup would preserve a setup side effect.
                    if (priorFog != null)
                        for (int i = 0; i < priorFog.Length; i++)
                        {
                            IntVec3 cell = map.cellIndices.IndexToCell(i);
                            if (priorFog[i] && !map.fogGrid.IsFogged(i))
                                map.fogGrid.Refog(CellRect.SingleCell(cell));
                            else if (!priorFog[i] && map.fogGrid.IsFogged(i))
                                map.fogGrid.Unfog(cell);
                        }
                    controlledFixture.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null) return;
                        foreach (var prior in rocks)
                        {
                            Thing rock = ThingMaker.MakeThing(prior.def, prior.stuff);
                            rock.HitPoints = prior.hp;
                            GenSpawn.Spawn(rock, prior.cell, now, prior.rotation);
                        }
                        foreach (var prior in ground)
                        {
                            now.terrainGrid.SetTerrain(prior.cell, prior.terrain);
                            now.roofGrid.SetRoof(prior.cell, prior.roof);
                        }
                        foreach (var prior in visitors)
                            if (Scenario.ById(prior.id) is Pawn pawn) Teleport(pawn, prior.cell);
                    });
                }
                foreach (IntVec3 cell in rect.Cells)
                {
                    List<Thing> things = cell.GetThingList(map);
                    for (int i = things.Count - 1; i >= 0; i--)
                        if (things[i].def.category == ThingCategory.Plant || things[i].def.category == ThingCategory.Item || things[i] is Filth)
                            things[i].Destroy();
                }
                return rect;
            }
            throw new InvalidOperationException($"no {width}x{height} fixture pad within {maxDist} cells of the map center");
        }

        /// <summary>Reveal the fixture and occupied player-pawn cells; restore original fog on finish.
        /// Hidden player pawns flood-unfog their surroundings after native save/load, so their cells must stay visible.</summary>
        private static void SurveyIsolate(Scenario s, IEnumerable<IntVec3> revealed)
        {
            Map map = Find.CurrentMap;
            IntVec3 size = map.Size;
            var wasFogged = new HashSet<int>();
            foreach (IntVec3 cell in map.AllCells)
                if (map.fogGrid.IsFogged(cell))
                    wasFogged.Add(map.cellIndices.CellToIndex(cell));
            map.fogGrid.Refog(new CellRect(0, 0, size.x, size.z));
            var cells = new HashSet<IntVec3>();
            foreach (IntVec3 cell in revealed)
                if (cell.InBounds(map))
                    cells.Add(cell);
            foreach (IntVec3 cell in cells)
                map.fogGrid.Unfog(cell);
            SurveyRevealPlayerCells(map);
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null || now.Size.x != size.x || now.Size.z != size.z)
                    return;
                for (int i = 0; i < now.cellIndices.NumGridCells; i++)
                {
                    IntVec3 cell = now.cellIndices.IndexToCell(i);
                    bool wantFog = wasFogged.Contains(i);
                    if (wantFog && !now.fogGrid.IsFogged(cell))
                        now.fogGrid.Refog(CellRect.SingleCell(cell));
                    else if (!wantFog && now.fogGrid.IsFogged(cell))
                        now.fogGrid.Unfog(cell);
                }
            });
        }

        /// <summary>Native pawn respawning flood-reveals fog around hidden player pawns. Keep only their occupied
        /// cells visible before saving an isolated fixture; return the exact number of newly revealed cells.</summary>
        private static int SurveyRevealPlayerCells(Map map)
        {
            int revealed = 0;
            foreach (Pawn pawn in map.mapPawns.AllPawns)
            {
                if (!pawn.Spawned || !(pawn.IsPlayerControlled || pawn.Faction == Faction.OfPlayer))
                    continue;
                IntVec3 cell = pawn.PositionHeld;
                if (!cell.InBounds(map) || !map.fogGrid.IsFogged(cell))
                    continue;
                map.fogGrid.Unfog(cell);
                revealed++;
            }
            return revealed;
        }

        /// <summary>Replace the colony Home area with <paramref name="home"/>; every original cell is restored on finish.</summary>
        private static void SurveyHome(Scenario s, CellRect home)
        {
            Map map = Find.CurrentMap;
            bool autoHome = Find.PlaySettings.autoHomeArea;
            Find.PlaySettings.autoHomeArea = false;
            var prior = new List<IntVec3>(map.areaManager.Home.ActiveCells);
            map.areaManager.Home.Clear();
            foreach (IntVec3 cell in home.Cells)
                if (cell.InBounds(map))
                    map.areaManager.Home[cell] = true;
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                Find.PlaySettings.autoHomeArea = autoHome;
                if (now == null)
                    return;
                now.areaManager.Home.Clear();
                foreach (IntVec3 cell in prior)
                    if (cell.InBounds(now))
                        now.areaManager.Home[cell] = true;
            });
        }

        /// <summary>A stable at exactly <paramref name="cell"/> with one tracked Quadrunner; the dog stays docked.</summary>
        private static Pawn SurveyStable(Scenario s, IntVec3 cell, bool powered, out Building_Stable stable)
        {
            stable = SpawnStable(s, powered: powered, near: cell, minDist: 0f, maxDist: 0f);
            if (stable.Position != cell)
                throw new InvalidOperationException($"the stable spawned at {stable.Position}, not {cell}");
            return s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
        }

        private static void SurveyPlace(Pawn dog, Building_Stable stable, IntVec3 cell)
        {
            if (!stable.TryUndock(dog))
                throw new InvalidOperationException($"couldn't undock {dog}");
            Teleport(dog, cell);
            if (dog.CurJob != null)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        /// <summary>A proper roofed room: walls and doors on the expanded edge, roof over the interior.</summary>
        private static void SurveyBuildRoom(Scenario s, CellRect interior, params IntVec3[] doors)
        {
            Map map = Find.CurrentMap;
            CellRect ring = interior.ExpandedBy(1);
            var doorSet = new HashSet<IntVec3>(doors);
            foreach (IntVec3 cell in ring.EdgeCells)
                if (!doorSet.Contains(cell))
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
            foreach (IntVec3 door in doors)
            {
                if (!ring.EdgeCells.Contains(door))
                    throw new InvalidOperationException($"survey room door {door} is not on {interior}");
                SpawnBuilding(s, ThingDefOf.Door, ThingDefOf.Steel, door, Rot4.North);
            }
            var cells = new List<IntVec3>();
            var before = new List<RoofDef>();
            foreach (IntVec3 cell in interior.Cells)
            {
                cells.Add(cell);
                RoofDef roof = map.roofGrid.RoofAt(cell);
                before.Add(roof);
                if (roof != RoofDefOf.RoofConstructed)
                    map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            }
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null)
                    return;
                for (int i = 0; i < cells.Count && i < before.Count; i++)
                    now.roofGrid.SetRoof(cells[i], before[i]);
            });
        }

        private static Zone_Stockpile SurveyStockpile(Scenario s, CellRect rect, ThingDef only)
        {
            Map map = Find.CurrentMap;
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in rect)
                zone.AddCell(cell);
            if (only != null)
            {
                zone.settings.filter.SetDisallowAll();
                zone.settings.filter.SetAllow(only, true);
            }
            int id = zone.ID;
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null)
                    return;
                now.zoneManager.AllZones.FirstOrDefault(z => z.ID == id)?.Delete();
            });
            return zone;
        }

        private static Zone_Growing SurveyGrowing(Scenario s, CellRect rect)
        {
            Map map = Find.CurrentMap;
            var zone = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in rect)
                zone.AddCell(cell);
            zone.allowSow = false;
            zone.allowCut = false;
            int id = zone.ID;
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null)
                    return;
                now.zoneManager.AllZones.FirstOrDefault(z => z.ID == id)?.Delete();
            });
            return zone;
        }

        private static IntVec3 SurveyShelfCell(Map map, CellRect rect, Rot4 rot)
        {
            foreach (IntVec3 cell in rect.Cells.OrderBy(c => c.DistanceToSquared(rect.CenterCell)))
            {
                CellRect occupied = GenAdj.OccupiedRect(cell, rot, ThingDefOf.Shelf.size);
                if (occupied.Cells.All(c => rect.Contains(c) && c.InBounds(map) && c.Standable(map)
                        && c.GetFirstBuilding(map) == null && c.GetFirstPawn(map) == null))
                    return cell;
            }
            throw new InvalidOperationException($"no shelf cell in {rect}");
        }

        private static Building_Storage SurveyShelf(Scenario s, IntVec3 cell, Rot4 rot)
        {
            Map map = Find.CurrentMap;
            var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, ThingDefOf.Steel);
            shelf.SetFaction(Faction.OfPlayer);
            var spawned = (Building_Storage)s.Track(GenSpawn.Spawn(shelf, cell, map, rot));
            spawned.settings.filter.SetDisallowAll();
            spawned.settings.filter.SetAllow(ThingDefOf.Gold, true);
            return spawned;
        }

        private static bool SurveyUsable(Pawn dog, IntVec3 cell) =>
            cell.InBounds(dog.Map) && cell.Standable(dog.Map) && !cell.Fogged(dog.Map) && !cell.IsForbidden(dog);

        private static bool SurveyAccepted(Pawn dog, IntVec3 cell, out float cost)
        {
            cost = 0f;
            Map map = dog.Map;
            if (!SurveyUsable(dog, cell))
                return false;
            using (PawnPath path = map.pathFinder.FindPathNow(dog.Position, cell,
                       TraverseParms.For(dog, Danger.Deadly), peMode: PathEndMode.OnCell))
            {
                if (!path.Found)
                    return false;
                foreach (IntVec3 node in path.NodesReversed)
                    if (node.Fogged(map) || node.IsForbidden(dog) || node.GetDoor(map)?.IsForbidden(dog) == true)
                        return false;
                cost = path.TotalCost;
                return true;
            }
        }

        private static List<IntVec3> SurveyRoute(Pawn dog, IntVec3 cell, out float cost)
        {
            cost = 0f;
            using (PawnPath path = dog.Map.pathFinder.FindPathNow(dog.Position, cell,
                       TraverseParms.For(dog, Danger.Deadly), peMode: PathEndMode.OnCell))
            {
                if (!path.Found)
                    return null;
                cost = path.TotalCost;
                return new List<IntVec3>(path.NodesReversed);
            }
        }

        /// <summary>The declared-cheapest accepted candidate: vanilla returned TotalCost, then lower cell index.</summary>
        private static IntVec3 SurveyExpectedBest(Pawn dog, IEnumerable<IntVec3> candidates, out float cost)
        {
            Map map = dog.Map;
            IntVec3 best = IntVec3.Invalid;
            float bestCost = float.MaxValue;
            int bestIndex = int.MaxValue;
            var seen = new HashSet<IntVec3>();
            foreach (IntVec3 cell in candidates)
            {
                if (!seen.Add(cell) || !SurveyAccepted(dog, cell, out float route))
                    continue;
                int index = map.cellIndices.CellToIndex(cell);
                if (route < bestCost || route == bestCost && index < bestIndex)
                {
                    best = cell;
                    bestCost = route;
                    bestIndex = index;
                }
            }
            cost = bestCost;
            return best;
        }

        private static Job SurveyFreshProbe(Pawn dog, out IntVec3 goal, out object key)
        {
            Survey survey = SurveyOf();
            survey.Clear();
            Job job = survey.JobFor(dog);
            goal = survey.Goal(dog);
            key = survey.AreaKey(dog);
            return job;
        }

        private static void SurveyReturn(Job job)
        {
            if (job != null)
                JobMaker.ReturnToPool(job);
        }

        private static bool SurveyInRoom(Room room, IntVec3 cell) =>
            room != null && room.Cells.Contains(cell);

        /// <summary>Every walkable owned cell of <paramref name="world"/> whose category is above Outside.</summary>
        private static List<(IntVec3 cell, SurveyCategory category)> SurveyOwned(Map map, CellRect world)
        {
            var owned = new List<(IntVec3 cell, SurveyCategory category)>();
            foreach (IntVec3 cell in world.Cells)
            {
                SurveyCategory category = SurveyOf().CategoryAt(cell);
                if (category != SurveyCategory.Outside)
                    owned.Add((cell, category));
            }
            return owned;
        }

        /// <summary>While a dog surveys in <paramref name="sweep"/>, no lower category may be selected before a higher one is observed.</summary>
        private static void SurveyAssertPriority(Survey survey, IntVec3 goal, int sweep,
            List<(IntVec3 cell, SurveyCategory category)> owned)
        {
            if (!goal.IsValid || survey.Sweep != sweep)
                return;
            SurveyCategory selected = survey.CategoryAt(goal);
            foreach ((IntVec3 cell, SurveyCategory category) in owned)
                if (category < selected && !survey.Observed(cell))
                    throw new InvalidOperationException(
                        $"selected {selected} at {goal} while {category} at {cell} is unobserved");
        }

        // ---------------------------------------------------------------- category order and Home gaps

        private static void SurveyCategoryOrder()
        {
            var s = new Scenario("Survey: category order and Home gaps", 40000);
            Map map = Find.CurrentMap;
            CellRect world = default, home = default, inRoom = default, outRoom = default, stock = default,
                grow = default, gap = default, outside = default;
            IntVec3 roomCell = default, outRoomCell = default, stockCell = default, growCell = default,
                gapCell = default, outsideCell = default, probeGoal = default;
            int probeSweep = 1;
            string dogId = null;
            List<(IntVec3 cell, SurveyCategory category)> owned = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a fog-isolated Home with a room, stockpile, growing plot, unzoned Home and an outside-Home room", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 51, 29, 120f);
                    home = new CellRect(world.minX + 7, world.minZ, world.Width - 7, world.Height);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, home);
                    Pawn dog = SurveyStable(s, new IntVec3(home.minX + 3, 0, home.CenterCell.z), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    outRoom = new CellRect(world.minX + 1, world.minZ + 11, 5, 5);
                    SurveyBuildRoom(s, outRoom, new IntVec3(outRoom.CenterCell.x, 0, outRoom.maxZ + 1));
                    inRoom = new CellRect(world.maxX - 11, world.minZ + 10, 9, 7);
                    SurveyBuildRoom(s, inRoom, new IntVec3(inRoom.minX - 1, 0, inRoom.CenterCell.z));
                    stock = new CellRect(home.minX + 12, home.minZ + 4, 3, 3);
                    SurveyStockpile(s, stock, ThingDefOf.Gold);
                    grow = new CellRect(home.minX + 12, home.maxZ - 8, 3, 3);
                    SurveyGrowing(s, grow);
                    gap = new CellRect(home.minX + 24, home.minZ + 2, 2, 2);
                    outside = new CellRect(world.minX + 1, world.maxZ - 3, 2, 2);
                    roomCell = inRoom.CenterCell;
                    outRoomCell = outRoom.CenterCell;
                    stockCell = stock.CenterCell;
                    growCell = grow.CenterCell;
                    gapCell = gap.CenterCell;
                    outsideCell = outside.CenterCell;
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out probeGoal, out _);
                    if (job == null || !probeGoal.IsValid)
                        throw new InvalidOperationException("no first survey destination");
                    SurveyReturn(job);
                    probeSweep = S().Sweep;
                    owned = SurveyOwned(map, world);
                    if (stockCell.DistanceTo(dog.Position) >= probeGoal.DistanceTo(dog.Position))
                        throw new InvalidOperationException("the storage fixture must be geometrically closer than the room");
                    if (S().CategoryAt(roomCell) != SurveyCategory.Room
                        || S().CategoryAt(stockCell) != SurveyCategory.Storage
                        || S().CategoryAt(growCell) != SurveyCategory.Growing
                        || S().CategoryAt(gapCell) != SurveyCategory.Home
                        || S().CategoryAt(outsideCell) != SurveyCategory.Outside
                        || S().CategoryAt(outRoomCell) != SurveyCategory.Outside)
                        throw new InvalidOperationException("fixture cells were classified in the wrong category");
                    Room inRoomRef = roomCell.GetRoom(map);
                    Room outRoomRef = outRoomCell.GetRoom(map);
                    if (inRoomRef == null || !inRoomRef.ProperRoom || inRoomRef.IsDoorway
                        || outRoomRef == null || !outRoomRef.ProperRoom)
                        throw new InvalidOperationException("the proper-room fixtures did not register");
                    if (S().CategoryAt(probeGoal) != SurveyCategory.Room || !inRoom.Contains(probeGoal))
                        throw new InvalidOperationException("the first destination did not honour the room tier");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("priority holds for every destination until the finite world is fully covered", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (survey.Sweep > probeSweep)
                        return true;
                    if (Net().NormalQueue.Count > 0 || Net().WarQueue.Count > 0)
                        throw new InvalidOperationException("the empty fixture queued work");
                    if (dog != null && dog.CurJobDef == QR_DefOf.QR_Survey)
                        SurveyAssertPriority(survey, survey.Goal(dog), probeSweep, owned);
                    return false;
                })
                .Summary(() => $"sweep {probeSweep} -> {S().Sweep}, observed {S().ObservedCellCount}, " +
                    $"queue {Net().NormalQueue.Count}; {Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- route cost ranking

        private static void SurveyRouteRanking()
        {
            var s = new Scenario("Survey: route-cost ranking", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, nearZone = default, farZone = default, sealedRoom = default;
            IntVec3 nearCell = default, farCell = default, sealedCell = default, probeGoal = default;
            float probeCost = 0f, nearCost = 0f;
            string dogId = null;
            bool sawFar = false;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a wall detour and an unreachable room on the way to two same-category stockpiles", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 55, 31, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 15), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    // The wall blocks the geometrically nearer stockpile; the open stockpile is farther by beeline.
                    int wallX = stable.InteractionCell.x + 6;
                    for (int z = world.minZ + 2; z <= world.maxZ - 2; z++)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, new IntVec3(wallX, 0, z), Rot4.North);
                    nearZone = new CellRect(wallX + 5, stable.InteractionCell.z - 1, 2, 2);
                    SurveyStockpile(s, nearZone, ThingDefOf.Gold);
                    nearCell = nearZone.CenterCell;
                    farZone = new CellRect(world.minX + 1, world.maxZ - 1, 2, 2);
                    SurveyStockpile(s, farZone, ThingDefOf.Gold);
                    farCell = farZone.CenterCell;
                    sealedRoom = new CellRect(world.maxX - 9, world.minZ + 2, 5, 5);
                    SurveyBuildRoom(s, sealedRoom);
                    sealedCell = sealedRoom.CenterCell;
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out probeGoal, out _);
                    if (job == null || !probeGoal.IsValid)
                        throw new InvalidOperationException("no route-ranked survey destination");
                    SurveyReturn(job);
                    var candidates = new List<IntVec3>();
                    candidates.AddRange(S().CandidateCells(farCell));
                    candidates.AddRange(S().CandidateCells(nearCell));
                    IntVec3 expected = SurveyExpectedBest(dog, candidates, out probeCost);
                    if (expected != probeGoal)
                        throw new InvalidOperationException(
                            $"declared-candidate ranking picked {probeGoal} (cost {probeCost:F0}), not {expected}");
                    if (!S().CandidateCells(farCell).Contains(probeGoal))
                        throw new InvalidOperationException("the route-ranked goal is not in the farther declared stockpile");
                    if (!SurveyAccepted(dog, nearCell, out nearCost) || nearCost <= probeCost)
                        throw new InvalidOperationException("the walled stockpile must be route-worse than the chosen one");
                    if (nearCell.DistanceTo(dog.Position) >= farCell.DistanceTo(dog.Position))
                        throw new InvalidOperationException("the walled stockpile must be geometrically closer");
                    if (S().CategoryAt(sealedCell) != SurveyCategory.Room)
                        throw new InvalidOperationException("the sealed room was not declared as a room");
                    if (S().CategoryAt(probeGoal) != SurveyCategory.Storage || S().Observed(probeGoal))
                        throw new InvalidOperationException("the probe destination is not a pending storage point");
                    Job again = S().JobFor(dog);
                    if (again == null || S().Goal(dog) != probeGoal)
                        throw new InvalidOperationException("identical selection state produced a different destination");
                    SurveyReturn(again);
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the route-cheap storage area is completed before the nearer walled one is visited", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    if (survey.Observed(farCell))
                        sawFar = true;
                    if (!sawFar && dog.CurJobDef == QR_DefOf.QR_Survey && nearZone.Contains(survey.Goal(dog)))
                        throw new InvalidOperationException("chose the nearer walled stockpile before finishing the open one");
                    return sawFar;
                })
                .Summary(() => $"goal {probeGoal} cost {probeCost:F0}; walled cost {nearCost:F0}; " +
                    $"near {nearCell}, far {farCell}; sweep {S().Sweep}")
                .Start();
        }

        // ---------------------------------------------------------------- deterministic ties

        private static void SurveyDeterministicTies()
        {
            var s = new Scenario("Survey: deterministic ties", 15000);
            Map map = Find.CurrentMap;
            CellRect world = default;
            IntVec3 first = IntVec3.Invalid, second = IntVec3.Invalid, probeGoal = default;
            Survey S() => SurveyOf();
            s.Do("two same-cost stockpile cells mirrored around the worker", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 41, 15, 120f, controlledFixture: s);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 20, 0, world.CenterCell.z), powered: false,
                        out Building_Stable stable);
                    SurveyPlace(dog, stable, new IntVec3(world.CenterCell.x, 0, world.minZ + 2));
                    IntVec3 post = dog.Position;
                    var directions = new[]
                    {
                        new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0), new IntVec3(0, 0, 1), new IntVec3(0, 0, -1),
                    };
                    bool found = false;
                    for (int distance = 16; distance <= 18 && !found; distance++)
                        for (int i = 0; i < directions.Length && !found; i++)
                            for (int j = i + 1; j < directions.Length && !found; j++)
                            {
                                IntVec3 x = post + directions[i] * distance;
                                IntVec3 y = post + directions[j] * distance;
                                if (!world.Contains(x) || !world.Contains(y) || x.GetTerrain(map) != y.GetTerrain(map)
                                    || !SurveyUsable(dog, x) || !SurveyUsable(dog, y))
                                    continue;
                                if (!SurveyAccepted(dog, x, out float cx) || !SurveyAccepted(dog, y, out float cy)
                                    || cx != cy)
                                    continue;
                                first = x;
                                second = y;
                                found = true;
                            }
                    if (!found)
                        throw new InvalidOperationException("no same-cost mirrored stockpile pair found");
                    SurveyStockpile(s, CellRect.SingleCell(first), ThingDefOf.Gold);
                    SurveyStockpile(s, CellRect.SingleCell(second), ThingDefOf.Gold);
                    Job job = SurveyFreshProbe(dog, out probeGoal, out _);
                    if (job == null || !probeGoal.IsValid)
                        throw new InvalidOperationException("no tied survey destination");
                    SurveyReturn(job);
                    IntVec3 expected = map.cellIndices.CellToIndex(first) < map.cellIndices.CellToIndex(second)
                        ? first : second;
                    if (probeGoal != expected)
                        throw new InvalidOperationException(
                            $"tied cells chose {probeGoal}, expected the lower cell index {expected}");
                    if (S().CategoryAt(probeGoal) != SurveyCategory.Storage)
                        throw new InvalidOperationException("the tied destination is not a storage cell");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("both equally costly stockpiles are actually visited", () =>
                {
                    Survey survey = S();
                    return survey.Observed(first) && survey.Observed(second);
                })
                .Summary(() => $"tied cells {first} and {second}; goal {probeGoal}; sweep {S().Sweep}")
                .Start();
        }

        // ---------------------------------------------------------------- doors, fog and alternative entries

        private static void SurveyDoorsAndFog()
        {
            var s = new Scenario("Survey: doors, fog and alternative entries", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default;
            IntVec3 westDoor = default, eastDoor = default, roomFloor = default, openGoal = default;
            string dogId = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a room with a near west door and a far east door", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 41, 27, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 12), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 20, world.minZ + 9, 11, 9);
                    westDoor = new IntVec3(room.minX - 1, 0, room.CenterCell.z);
                    eastDoor = new IntVec3(room.maxX + 1, 0, room.CenterCell.z);
                    SurveyBuildRoom(s, room, westDoor, eastDoor);
                    roomFloor = new IntVec3(room.minX + 1, 0, room.CenterCell.z);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out openGoal, out _);
                    if (job == null || !openGoal.IsValid)
                        throw new InvalidOperationException("no door-ranked destination");
                    SurveyReturn(job);
                    IntVec3 expected = SurveyExpectedBest(dog, S().CandidateCells(roomFloor), out float expectedCost);
                    if (expected != openGoal || expectedCost <= 0f)
                        throw new InvalidOperationException($"best declared entry {expected}, selected {openGoal}");
                    List<IntVec3> route = SurveyRoute(dog, openGoal, out float routeCost);
                    if (route == null || routeCost <= 0f || !route.Contains(westDoor) || route.Contains(eastDoor))
                        throw new InvalidOperationException("the open near door was not the chosen entry");
                    if (!room.Contains(openGoal) || S().CategoryAt(openGoal) != SurveyCategory.Room)
                        throw new InvalidOperationException("the near-door destination is not a room point");
                })
                .Do("forbid the near door: the declared far entry wins", () =>
                {
                    ((Building_Door)map.thingGrid.ThingAt(westDoor, ThingDefOf.Door))
                        .SetForbidden(true, warnOnFail: false);
                    Job job = SurveyFreshProbe(Dog(), out IntVec3 goal, out _);
                    if (job == null || !goal.IsValid)
                        throw new InvalidOperationException("no destination with the near door forbidden");
                    SurveyReturn(job);
                    IntVec3 expected = SurveyExpectedBest(Dog(), S().CandidateCells(roomFloor), out float expectedCost);
                    if (expected != goal || expectedCost <= 0f)
                        throw new InvalidOperationException($"forbidden-door ranking picked {goal}, not {expected}");
                    List<IntVec3> route = SurveyRoute(Dog(), goal, out float routeCost);
                    if (route == null || routeCost <= 0f || !route.Contains(eastDoor) || route.Contains(westDoor))
                        throw new InvalidOperationException("the far door was not used for the alternative entry");
                    if (!room.Contains(goal))
                        throw new InvalidOperationException("the alternative-entry goal left the room");
                    if (S().Observed(goal))
                        throw new InvalidOperationException("a probe destination was already credited as observed");
                })
                .Do("unforbid the near door, refog both doors", () =>
                {
                    ((Building_Door)map.thingGrid.ThingAt(westDoor, ThingDefOf.Door))
                        .SetForbidden(false, warnOnFail: false);
                    map.fogGrid.Refog(CellRect.SingleCell(westDoor));
                    map.fogGrid.Refog(CellRect.SingleCell(eastDoor));
                })
                .Do("probe with every route into the room crossing fog", () =>
                {
                    Job job = SurveyFreshProbe(Dog(), out IntVec3 goal, out _);
                    if (job != null)
                        SurveyReturn(job);
                    if (goal.IsValid && S().CategoryAt(goal) == SurveyCategory.Room)
                        throw new InvalidOperationException($"fogged-door route was accepted for {goal}");
                })
                .Do("unfog the doors: the room is selectable again", () =>
                {
                    map.fogGrid.Unfog(westDoor);
                    map.fogGrid.Unfog(eastDoor);
                    Job job = SurveyFreshProbe(Dog(), out IntVec3 goal, out _);
                    if (job == null || goal != openGoal)
                        throw new InvalidOperationException($"the reopened room selected {goal}, not {openGoal}");
                    SurveyReturn(job);
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the reopened room's chosen point is observed by a real local scan", () =>
                    S().Observed(openGoal))
                .Summary(() => $"open goal {openGoal}; doors {westDoor}/{eastDoor}; sweep {S().Sweep}")
                .Start();
        }

        // ---------------------------------------------------------------- large area completion and resumption

        private static void SurveyAreaCompletion()
        {
            var s = new Scenario("Survey: large area completion and resumption", 40000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default, block = default, pile = default;
            IntVec3 blockFloor = default, goldCell = default, probeGoal = default;
            object key0 = null;
            int sweep0 = 1;
            string dogId = null, goldId = null;
            bool sawFarCorner = false;
            Room roomRef = null;
            List<IntVec3> roomCells = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a large non-convex room with a blocked center and a gold stack in the far corner", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 51, 33, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 15), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 17, world.minZ + 8, 29, 17);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    block = new CellRect(room.CenterCell.x - 3, room.CenterCell.z - 3, 7, 7);
                    foreach (IntVec3 cell in block.Cells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    pile = new CellRect(world.minX + 8, world.minZ + 13, 2, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    goldCell = new IntVec3(room.maxX - 2, 0, room.maxZ - 2);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    blockFloor = new IntVec3(room.minX + 1, 0, room.minZ + 1);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out probeGoal, out key0);
                    if (job == null || !probeGoal.IsValid)
                        throw new InvalidOperationException("no survey destination in the large room");
                    SurveyReturn(job);
                    sweep0 = S().Sweep;
                    if (S().CategoryAt(probeGoal) != SurveyCategory.Room || !room.Contains(probeGoal))
                        throw new InvalidOperationException("the declared first destination is not the large room");
                    var candidates = S().CandidateCells(blockFloor);
                    if (candidates.Count == 0 || candidates.Any(block.Contains))
                        throw new InvalidOperationException("a blocked center produced no usable declared point");
                    roomRef = blockFloor.GetRoom(map);
                    if (roomRef == null)
                        throw new InvalidOperationException("the large room was not registered");
                    roomCells = roomRef.Cells.ToList();
                    if (roomCells.Count < 100)
                        throw new InvalidOperationException("the large-room fixture is too small for multiple legs");
                    foreach (IntVec3 cell in roomCells)
                        if (cell.DistanceTo(candidates.OrderBy(c => c.DistanceToSquared(cell)).First()) > 15f)
                            throw new InvalidOperationException("the blocked-center room lost a declared coverage point");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("every survey goal before the haul stays in the retained room", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    if (dog.CurJobDef == QR_DefOf.QR_Survey)
                    {
                        if (!object.Equals(survey.AreaKey(dog), key0))
                            throw new InvalidOperationException("the chosen area was discarded mid-pass");
                        IntVec3 goal = survey.Goal(dog);
                        if (!goal.IsValid || !room.Contains(goal))
                            throw new InvalidOperationException($"the retained goal left the area: {goal}");
                    }
                    if (survey.Observed(goldCell))
                        sawFarCorner = true;
                    return sawFarCorner && (SurveyQueued(goldId) || dog.CurJobDef == JobDefOf.HaulToCell
                        || dog.carryTracker?.CarriedThing != null);
                })
                .Expect("the productive haul was issued while the area and sweep were retained", () =>
                {
                    Survey survey = S();
                    return sawFarCorner && object.Equals(survey.AreaKey(Dog()), key0) && survey.Sweep == sweep0
                        && (SurveyQueued(goldId) || Dog().CurJobDef == JobDefOf.HaulToCell
                            || Dog().carryTracker?.CarriedThing != null);
                })
                .SaveAndReload()
                .Expect("the productive haul survives the load and is completed", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && !SurveyQueued(goldId))
                .Expect("the rebuilt transient sweep resumes the room after the load", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (CountIn(pile, ThingDefOf.Gold) != 30)
                        return false;
                    if (survey.Sweep > sweep0 || roomCells.All(survey.Observed))
                        return true;
                    return dog != null && dog.CurJobDef == QR_DefOf.QR_Survey
                        && roomCells.Contains(survey.Goal(dog));
                })
                .Summary(() => $"room {room.Width}x{room.Height}, blocked center, gold at {goldCell}, " +
                    $"pile {CountIn(pile, ThingDefOf.Gold)}, sweep {S().Sweep}; {Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- overlap: rooms, stockpiles and shelves

        private static void SurveyOverlapStorage()
        {
            var s = new Scenario("Survey: overlap and storage", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default, indoor = default, outdoor = default;
            IntVec3 indoorCell = default, outdoorCell = default, outdoorShelfCell = default, indoorShelfCell = default;
            string dogId = null;
            Room roomRef = null;
            List<IntVec3> roomCells = null;
            List<(IntVec3 cell, SurveyCategory category)> owned = null;
            bool sawIndoor = false, sawIndoorShelf = false, sawOutdoor = false, sawOutdoorShelf = false;
            int probeSweep = 1;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("an indoor stockpile and shelf, and an outdoor stockpile and shelf in Home", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 45, 27, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 13), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 26, world.minZ + 9, 13, 9);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    indoor = new CellRect(room.minX + 2, room.minZ + 2, 3, 3);
                    SurveyStockpile(s, indoor, ThingDefOf.Gold);
                    indoorCell = indoor.CenterCell;
                    IntVec3 shelfAt = SurveyShelfCell(map, new CellRect(room.maxX - 4, room.minZ + 2, 4, 4), Rot4.North);
                    SurveyShelf(s, shelfAt, Rot4.North);
                    indoorShelfCell = shelfAt;
                    outdoor = new CellRect(world.minX + 6, world.minZ + 4, 3, 3);
                    SurveyStockpile(s, outdoor, ThingDefOf.Gold);
                    outdoorCell = outdoor.CenterCell;
                    outdoorShelfCell = SurveyShelfCell(map, new CellRect(world.minX + 5, world.maxZ - 7, 6, 5), Rot4.North);
                    SurveyShelf(s, outdoorShelfCell, Rot4.North);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out IntVec3 goal, out _);
                    if (job == null || !goal.IsValid)
                        throw new InvalidOperationException("no overlap survey destination");
                    SurveyReturn(job);
                    if (S().CategoryAt(goal) != SurveyCategory.Room)
                        throw new InvalidOperationException("storage proximity won over the room tier");
                    if (outdoorCell.DistanceTo(dog.Position) >= goal.DistanceTo(dog.Position))
                        throw new InvalidOperationException("the storage fixture must be geometrically closer");
                    probeSweep = S().Sweep;
                    roomRef = room.CenterCell.GetRoom(map);
                    roomCells = roomRef.Cells.ToList();
                    owned = SurveyOwned(map, world);
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("indoor stockpile cells are room cells; outdoor storage is the storage tier", () =>
                    S().CategoryAt(room.CenterCell) == SurveyCategory.Room
                    && S().CategoryAt(indoorCell) == SurveyCategory.Room
                    && S().CategoryAt(outdoorCell) == SurveyCategory.Storage
                    && S().CategoryAt(outdoorShelfCell) == SurveyCategory.Storage)
                .Expect("room scans cover indoor storage without a redundant visit and outdoor storage is surveyed", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    bool roomDone = roomCells.All(survey.Observed);
                    if (dog.CurJobDef == QR_DefOf.QR_Survey)
                    {
                        IntVec3 goal = survey.Goal(dog);
                        if (goal.IsValid)
                        {
                            SurveyCategory category = survey.CategoryAt(goal);
                            if (roomDone && category == SurveyCategory.Room)
                                throw new InvalidOperationException($"a redundant room goal {goal} followed room coverage");
                            if (roomDone && category == SurveyCategory.Storage
                                && (indoor.Contains(goal) || goal.DistanceTo(indoorShelfCell) < 2f))
                                throw new InvalidOperationException($"a redundant indoor storage goal {goal} followed room coverage");
                            if (roomDone)
                                SurveyAssertPriority(survey, goal, probeSweep, owned);
                        }
                    }
                    sawIndoor |= survey.Observed(indoorCell);
                    sawIndoorShelf |= survey.Observed(indoorShelfCell);
                    sawOutdoor |= survey.Observed(outdoorCell);
                    sawOutdoorShelf |= survey.Observed(outdoorShelfCell);
                    return roomDone && sawIndoor && sawIndoorShelf && sawOutdoor && sawOutdoorShelf;
                })
                .Summary(() => $"room {room}, indoor {indoorCell}, outdoor {outdoorCell}/{outdoorShelfCell}, " +
                    $"sweep {S().Sweep}; {Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- geometry: non-convex, narrow, separated

        private static void SurveyGeometry()
        {
            var s = new Scenario("Survey: geometry and discovery", 40000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default, block = default, narrow = default, courtyard = default,
                patchA = default, patchB = default, pile = default;
            IntVec3 roomFloor = default, narrowFloor = default, accessible = default;
            string dogId = null, farGoldId = null, narrowGoldId = null, courtyardGoldId = null;
            bool sawNarrow = false, sawPatchA = false, sawPatchB = false, sawAccessible = false;
            Room roomRef = null;
            List<IntVec3> roomCells = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a non-convex room, a narrow room, separated stockpiles and a sealed unroofed courtyard", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 53, 35, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 17), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 20, world.minZ + 4, 21, 13);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z),
                        new IntVec3(room.maxX + 1, 0, room.CenterCell.z));
                    block = new CellRect(room.CenterCell.x - 3, room.CenterCell.z - 2, 7, 5);
                    foreach (IntVec3 cell in block.Cells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    narrow = new CellRect(world.minX + 6, world.minZ + 22, 1, 9);
                    SurveyBuildRoom(s, narrow, new IntVec3(narrow.CenterCell.x, 0, narrow.minZ - 1),
                        new IntVec3(narrow.CenterCell.x, 0, narrow.maxZ + 1));
                    patchA = new CellRect(world.minX + 2, world.minZ + 2, 3, 3);
                    patchB = new CellRect(world.minX + 2, world.minZ + 31, 3, 3);
                    Zone_Stockpile separated = SurveyStockpile(s, patchA, ThingDefOf.Gold);
                    foreach (IntVec3 cell in patchB.Cells)
                        separated.AddCell(cell);
                    courtyard = new CellRect(world.maxX - 6, world.maxZ - 6, 5, 5);
                    CellRect ring = courtyard.ExpandedBy(1);
                    foreach (IntVec3 cell in ring.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    accessible = new IntVec3(courtyard.minX - 2, 0, courtyard.CenterCell.z);
                    pile = new CellRect(world.minX + 3, world.minZ + 8, 2, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    roomFloor = new IntVec3(room.minX + 1, 0, room.minZ + 1);
                    narrowFloor = new IntVec3(narrow.minX, 0, narrow.CenterCell.z);
                    farGoldId = NetStack(s, ThingDefOf.Gold, 30,
                        new IntVec3(room.maxX - 2, 0, room.maxZ - 2)).ThingID;
                    narrowGoldId = NetStack(s, ThingDefOf.Gold, 30,
                        new IntVec3(narrow.minX, 0, narrow.maxZ - 1)).ThingID;
                    courtyardGoldId = NetStack(s, ThingDefOf.Gold, 30, accessible).ThingID;
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    S().Refresh();
                    roomRef = roomFloor.GetRoom(map);
                    if (roomRef == null)
                        throw new InvalidOperationException("the non-convex room was not registered");
                    roomCells = roomRef.Cells.ToList();
                    var candidates = S().CandidateCells(roomFloor);
                    if (candidates.Count == 0 || candidates.Any(block.Contains))
                        throw new InvalidOperationException("the non-convex room declared a blocked candidate");
                    foreach (IntVec3 cell in roomCells)
                        if (cell.DistanceTo(candidates.OrderBy(c => c.DistanceToSquared(cell)).First()) > 15f)
                            throw new InvalidOperationException($"room cell {cell} is outside every declared point");
                    foreach (IntVec3 door in new[]
                             {
                                 new IntVec3(room.minX - 1, 0, room.CenterCell.z),
                                 new IntVec3(room.maxX + 1, 0, room.CenterCell.z),
                             })
                        if (!candidates.Any(c => Math.Abs(c.x - door.x) + Math.Abs(c.z - door.z) == 1))
                            throw new InvalidOperationException($"no door-adjacent declared point for {door}");
                    var narrowCandidates = S().CandidateCells(narrowFloor);
                    if (narrowCandidates.Count == 0 || narrowCandidates.Any(c => !narrow.Contains(c)))
                        throw new InvalidOperationException("the narrow room's declared points leave the corridor");
                    var first = new List<IntVec3>(S().CandidateCells(patchA.CenterCell));
                    var second = new List<IntVec3>(S().CandidateCells(patchB.CenterCell));
                    if (!first.SequenceEqual(second) || !first.Any(patchA.Contains) || !first.Any(patchB.Contains))
                        throw new InvalidOperationException("the separated zone lost one of its patches");
                    var accessibleCandidates = S().CandidateCells(accessible);
                    bool accessibleReachable = false;
                    foreach (IntVec3 candidate in accessibleCandidates)
                        if (candidate.DistanceTo(accessible) <= 15f
                            && SurveyAccepted(dog, candidate, out _))
                        {
                            accessibleReachable = true;
                            break;
                        }
                    if (!accessibleReachable)
                        throw new InvalidOperationException("the accessible Home cell has no reachable declared point");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("far-corner and separated work is found only by real local observation", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    if (courtyard.Contains(dog.Position))
                        throw new InvalidOperationException("the sealed courtyard was entered");
                    foreach (string id in new[] { farGoldId, narrowGoldId, courtyardGoldId })
                    {
                        if (!SurveyQueued(id))
                            continue;
                        Thing target = Scenario.ById(id);
                        if (target != null && !target.IsInValidBestStorage()
                            && dog.Position.DistanceTo(target.Position) > 20f)
                            throw new InvalidOperationException($"remote report of {id}");
                    }
                    if (dog.CurJobDef == QR_DefOf.QR_Survey && courtyard.Contains(survey.Goal(dog)))
                        throw new InvalidOperationException("selected an unreachable courtyard goal");
                    sawNarrow |= survey.Observed(narrowFloor);
                    sawPatchA |= survey.Observed(patchA.CenterCell);
                    sawPatchB |= survey.Observed(patchB.CenterCell);
                    sawAccessible |= survey.Observed(accessible);
                    return CountIn(pile, ThingDefOf.Gold) == 90 && sawNarrow && sawPatchA && sawPatchB && sawAccessible;
                })
                .Summary(() => $"pile {CountIn(pile, ThingDefOf.Gold)}; sweep {S().Sweep}; {Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- scan footprint, cadence and empty coverage

        private static void SurveyScanCadence()
        {
            var s = new Scenario("Survey: scan cadence and empty coverage", 20000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default;
            IntVec3 post = default, secondPost = default, nearCell = default, farCell = default, fogCell = default;
            IntVec3 arrivalGoal = IntVec3.Invalid;
            var scans = new List<int>();
            int beforeTeleport = -1, probeSweep = 1, scanBeforeArrival = -1, arrivedAt = -1, arrivalScan = -1;
            string dogId = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("an ordered worker on a revealed, otherwise empty fixture with one fogged footprint cell", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 25, 17, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 8), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 14, world.minZ + 6, 7, 5);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    post = dog.Position;
                    nearCell = post + new IntVec3(15, 0, 0);
                    farCell = post + new IntVec3(17, 0, 0);
                    fogCell = post + new IntVec3(5, 0, 0);
                    secondPost = post + new IntVec3(18, 0, 0);
                    if (!world.Contains(nearCell) || !world.Contains(farCell) || !world.Contains(fogCell)
                        || !world.Contains(secondPost) || secondPost.Fogged(map))
                        throw new InvalidOperationException("the footprint fixture left the isolated world");
                    map.fogGrid.Refog(CellRect.SingleCell(fogCell));
                    SurveyHold(dog, 6000);
                    probeSweep = S().Sweep;
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the first scheduled scan records only the revealed 15-cell footprint", () =>
                {
                    int last = S().LastScan(Dog());
                    if (last < 0)
                        return false;
                    if (!S().Observed(nearCell) || S().Observed(farCell) || S().Observed(fogCell))
                        throw new InvalidOperationException("the recorded footprint is not the revealed 15-cell radius");
                    return true;
                })
                .Expect("scans keep the existing cadence while the local scanner is active", () =>
                {
                    int last = S().LastScan(Dog());
                    if (last >= 0 && (scans.Count == 0 || scans[scans.Count - 1] != last))
                        scans.Add(last);
                    return scans.Count >= 3;
                })
                .Expect("the observed scan ticks are evenly spaced", () =>
                    scans.Zip(scans.Skip(1), (a, b) => b - a).All(delta => delta == RobodogWork.Utility.scanInterval))
                .Do("pause; a far move between ticks credits nothing", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    beforeTeleport = S().LastScan(Dog());
                    Teleport(Dog(), secondPost);
                })
                .Do("check that no out-of-cycle scan was added on arrival", () =>
                {
                    if (S().LastScan(Dog()) != beforeTeleport
                        || S().Observed(Dog().Position) || S().Observed(secondPost))
                        throw new InvalidOperationException("the teleport position was credited without a scheduled scan");
                })
                .Do("resume while still holding; reveal the fogged footprint cell", () =>
                {
                    map.fogGrid.Unfog(fogCell);
                    Scenario.Resume();
                })
                .Expect("the next scheduled scan credits the new position", () =>
                    S().LastScan(Dog()) > beforeTeleport && S().Observed(secondPost))
                .Do("start a real survey one step from its goal between scheduled scans", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Pawn dog = Dog();
                    scanBeforeArrival = S().LastScan(dog);
                    dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    S().Clear();
                    probeSweep = S().Sweep;
                    Job job = S().JobFor(dog);
                    if (job == null)
                        throw new InvalidOperationException("the empty fixture has no pending survey destination");
                    arrivalGoal = S().Goal(dog);
                    if (job.targetA.Cell != arrivalGoal)
                        throw new InvalidOperationException("the small arrival fixture unexpectedly needed an intermediate leg");
                    IntVec3 start = GenAdj.CardinalDirections.Select(d => arrivalGoal + d)
                        .First(c => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && !c.IsForbidden(dog));
                    Teleport(dog, start);
                    dog.jobs.StartJob(job, JobCondition.InterruptForced);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("physical survey arrival waits without granting coverage", () =>
                {
                    Pawn dog = Dog();
                    if (S().LastScan(dog) >= 0)
                        throw new InvalidOperationException("the next scan occurred before the one-step arrival was observed");
                    if (dog.Position != arrivalGoal)
                        return false;
                    arrivedAt = Find.TickManager.TicksGame;
                    if (dog.CurJobDef != QR_DefOf.QR_Survey || S().Observed(arrivalGoal))
                        throw new InvalidOperationException("arrival completed or credited the survey before its scheduled scan");
                    return true;
                })
                .Expect("the ordinary next scan certifies the arrived goal", () =>
                {
                    arrivalScan = S().LastScan(Dog());
                    if (arrivalScan < arrivedAt)
                        return false;
                    if (arrivalScan - scanBeforeArrival != RobodogWork.Utility.scanInterval)
                        throw new InvalidOperationException("arrival changed the existing scan schedule");
                    return S().Observed(arrivalGoal);
                })
                .Do("resume normal scenario speed", () => Scenario.Resume())
                .Expect("scanning the empty fixture completes coverage without inventing work", () =>
                {
                    Survey survey = S();
                    if (Net().NormalQueue.Count > 0 || Net().WarQueue.Count > 0)
                        throw new InvalidOperationException("the empty fixture queued a task");
                    return survey.Sweep > probeSweep;
                })
                .Summary(() => $"scans {string.Join(",", scans)}; arrival {arrivedAt}, next scan {arrivalScan}; " +
                    $"sweep {probeSweep}->{S().Sweep}")
                .Start();
        }

        // ---------------------------------------------------------------- real progress beyond 160 cells

        private static void SurveyLongProgress()
        {
            var s = new Scenario("Survey: progress beyond 160 cells", 50000);
            Map map = Find.CurrentMap;
            CellRect world = default, hub = default, corridor = default, pile = default;
            IntVec3 goldCell = default, start = default;
            string dogId = null, goldId = null;
            float walked = 0f;
            bool sawCorridor = false, nearDiscovery = false;
            int startSweep = -1;
            IntVec3 last = IntVec3.Invalid;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a 165-cell walled corridor from a Home hub, with gold at the far end", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 180, 13, 120f, controlledFixture: s);
                    hub = new CellRect(world.minX, world.minZ, 15, 13);
                    corridor = new CellRect(world.minX + 15, world.minZ + 5, 165, 3);
                    var revealed = new List<IntVec3>(hub.Cells);
                    revealed.AddRange(corridor.Cells);
                    SurveyIsolate(s, revealed);
                    SurveyHome(s, hub);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 5, 0, world.minZ + 6), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    for (int x = world.minX + 16; x <= world.maxX - 1; x++)
                    {
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                            new IntVec3(x, 0, world.minZ + 4), Rot4.North);
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                            new IntVec3(x, 0, world.minZ + 8), Rot4.North);
                    }
                    for (int z = world.minZ; z <= world.maxZ; z++)
                    {
                        if (z >= world.minZ + 5 && z <= world.minZ + 7)
                            continue;
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                            new IntVec3(world.minX + 15, 0, z), Rot4.North);
                    }
                    for (int z = world.minZ + 4; z <= world.minZ + 8; z++)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                            new IntVec3(world.maxX, 0, z), Rot4.North);
                    pile = new CellRect(world.minX + 1, world.minZ + 10, 2, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    goldCell = new IntVec3(world.maxX - 3, 0, world.minZ + 6);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    start = dog.Position;
                    last = start;
                    if (start.DistanceTo(goldCell) <= 160f)
                        throw new InvalidOperationException("the far target must be more than 160 cells from the placement");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the worker walks more than 160 cells to the far target without a teleport", () =>
                {
                    Pawn dog = Dog();
                    Survey survey = S();
                    if (dog == null || !dog.Spawned)
                        return false;
                    if (startSweep < 0)
                        startSweep = survey.Sweep;
                    if (survey.Sweep < startSweep)
                        throw new InvalidOperationException("the sweep was reset");
                    float moved = dog.Position.DistanceTo(last);
                    if (moved > 32f)
                        throw new InvalidOperationException($"teleported {moved:F1} cells from {last} to {dog.Position}");
                    walked += moved;
                    last = dog.Position;
                    if (dog.CurJobDef == QR_DefOf.QR_Survey)
                    {
                        IntVec3 goal = survey.Goal(dog);
                        if (corridor.Contains(goal))
                            sawCorridor = true;
                    }
                    if (SurveyQueued(goldId))
                    {
                        Thing gold = Scenario.ById(goldId);
                        if (gold != null && !gold.IsInValidBestStorage()
                            && dog.Position.DistanceTo(gold.Position) > 20f)
                            throw new InvalidOperationException("the far target was reported from outside the scan radius");
                        nearDiscovery = true;
                    }
                    if (dog.carryTracker?.CarriedThing?.ThingID == goldId)
                        nearDiscovery = true;
                    if (CountIn(pile, ThingDefOf.Gold) == 30)
                        return true;
                    return false;
                })
                .Expect("the far work was discovered on approach and hauled all the way back", () =>
                {
                    if (CountIn(pile, ThingDefOf.Gold) != 30)
                        return false;
                    if (walked <= 160f)
                        throw new InvalidOperationException($"only {walked:F0} cells walked before delivery");
                    if (!sawCorridor)
                        throw new InvalidOperationException("no survey destination entered the far corridor");
                    if (!nearDiscovery)
                        throw new InvalidOperationException("the far target was never locally discovered");
                    if (S().Sweep < startSweep)
                        throw new InvalidOperationException("the sweep went backwards");
                    return true;
                })
                .Summary(() => $"walked {walked:F0}, start/goal {start}->{goldCell}, sweep {startSweep}; " +
                    $"{Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- finite sweep and revisit

        private static void SurveySweepRevisit()
        {
            var s = new Scenario("Survey: finite sweep and revisit", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default, pile = default;
            IntVec3 goldCell = default;
            string dogId = null, goldId = null;
            int firstSweep = 1, placedSweep = 1;
            bool discovered = false;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a finite Home world with one empty room and a gold stockpile", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 31, 19, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 9), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 17, world.minZ + 6, 7, 5);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    pile = new CellRect(world.minX + 2, world.minZ + 2, 2, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    firstSweep = S().Sweep;
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("one finite pass covers the empty fixture", () =>
                {
                    if (Net().NormalQueue.Count > 0)
                        throw new InvalidOperationException("the empty first pass queued work");
                    return S().Sweep > firstSweep;
                })
                .Do("a new stack appears in the already-covered room", () =>
                {
                    placedSweep = S().Sweep;
                    goldCell = new IntVec3(room.CenterCell.x, 0, room.CenterCell.z);
                    goldId = NetStack(s, ThingDefOf.Gold, 30, goldCell).ThingID;
                    if (SurveyQueued(goldId))
                        throw new InvalidOperationException("the new stack was known without a local revisit");
                })
                .Expect("the new work is only discovered on a later pass, near the room", () =>
                {
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    Survey survey = S();
                    if (!discovered && SurveyQueued(goldId))
                    {
                        Thing gold = Scenario.ById(goldId);
                        if (gold != null && !gold.IsInValidBestStorage()
                            && dog.Position.DistanceTo(gold.Position) > 20f)
                            throw new InvalidOperationException("the new stack was reported remotely");
                        if (survey.Sweep < placedSweep)
                            throw new InvalidOperationException("the sweep went backwards");
                        discovered = true;
                    }
                    return discovered && CountIn(pile, ThingDefOf.Gold) == 30 && !SurveyQueued(goldId);
                })
                .Summary(() => $"sweeps {firstSweep}->{placedSweep}->{S().Sweep}, pile {CountIn(pile, ThingDefOf.Gold)}")
                .Start();
        }

        // ---------------------------------------------------------------- competing claims and death

        private static void SurveyClaimsAndDeath()
        {
            var s = new Scenario("Survey: competing claims and death", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default;
            IntVec3 goalA = default, goalB = default;
            string aId = null;
            int claimSweep = 1;
            Pawn A() => (Pawn)Scenario.ById(aId);
            Survey S() => SurveyOf();
            s.Do("two idle workers claim distinct declared coverage points", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 41, 23, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn a = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 6), powered: false,
                        out Building_Stable stableA);
                    Pawn b = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 16), powered: false,
                        out Building_Stable stableB);
                    aId = a.ThingID;
                    room = new CellRect(world.minX + 25, world.minZ + 8, 11, 7);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    SurveyPlace(a, stableA, stableA.InteractionCell);
                    SurveyPlace(b, stableB, stableB.InteractionCell);
                    Survey survey = S();
                    survey.Clear();
                    Job jobA = survey.JobFor(a);
                    if (jobA == null)
                        throw new InvalidOperationException("worker A has no survey destination");
                    IntVec3 first = survey.Goal(a);
                    object key = survey.AreaKey(a);
                    a.jobs.StartJob(jobA, JobCondition.InterruptForced);
                    if (!object.Equals(survey.AreaKey(a), key))
                        throw new InvalidOperationException("worker A's area was not retained");
                    Job jobB = survey.JobFor(b);
                    if (jobB == null)
                        throw new InvalidOperationException("worker B has no survey destination");
                    goalA = first;
                    goalB = survey.Goal(b);
                    if (goalA == goalB)
                        throw new InvalidOperationException("both workers claimed the same coverage point");
                    if (goalA.DistanceTo(goalB) < 20f)
                        throw new InvalidOperationException($"claims are closer than the declared spacing: {goalA}, {goalB}");
                    if (survey.Observed(goalA) || survey.Observed(goalB))
                        throw new InvalidOperationException("a claim was already treated as observed");
                    b.jobs.StartJob(jobB, JobCondition.InterruptForced);
                    claimSweep = survey.Sweep;
                    A().Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    NetClearCorpses(s, aId);
                    if (survey.Observed(goalA))
                        throw new InvalidOperationException("worker A's death falsely completed its pending coverage");
                    if (survey.AreaKey(b) == null)
                        throw new InvalidOperationException("worker B lost its claimed area");
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the surviving worker covers the dead worker's pending point after its retained area", () =>
                    S().Observed(goalA))
                .Expect("the finite world still completes with one worker", () => S().Sweep > claimSweep)
                .Expect("no queued work was invented by the claim or the death", () =>
                    Net().NormalQueue.Count == 0 && Net().WarQueue.Count == 0)
                .Summary(() => $"A {goalA} died; B {goalB}; sweep {claimSweep}->{S().Sweep}")
                .Start();
        }

        // ---------------------------------------------------------------- interruption, orders, charge, war

        private static void SurveyInterruption()
        {
            var s = new Scenario("Survey: interruption and unavailable workers", 40000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default;
            IntVec3 goal0 = default;
            object key0 = null;
            int sweep0 = 1;
            var observedBefore = new List<IntVec3>();
            string dogId = null, raiderId = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a powered stable and a large room", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 65, 33, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 16), powered: true,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 20, world.minZ + 6, 39, 19);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out goal0, out key0);
                    if (job == null || S().CategoryAt(goal0) != SurveyCategory.Room)
                        throw new InvalidOperationException("the large room was not selected");
                    SurveyReturn(job);
                    sweep0 = S().Sweep;
                })
                .Do("resume", () => Scenario.Resume())
                .Expect("the worker is surveying the retained room", () =>
                    Dog() != null && Dog().CurJobDef == QR_DefOf.QR_Survey
                    && object.Equals(S().AreaKey(Dog()), key0))
                .Do("record partial coverage, then interrupt the survey job", () =>
                {
                    observedBefore = room.Cells.Where(S().Observed).ToList();
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                })
                .Expect("the retained area resumes without resetting progress", () =>
                {
                    Survey survey = S();
                    Pawn dog = Dog();
                    if (dog == null || dog.CurJobDef != QR_DefOf.QR_Survey)
                        return false;
                    if (survey.Sweep != sweep0 || !object.Equals(survey.AreaKey(dog), key0))
                        throw new InvalidOperationException("the interruption reset the sweep or discarded the area");
                    return observedBefore.All(survey.Observed);
                })
                .Do("a player order keeps the worker away", () => SurveyHold(Dog(), 1800))
                .Expect("the ordered worker is unavailable and holds its progress", () =>
                    !DogNetwork.IsAvailable(Dog()) && Dog().CurJobDef != QR_DefOf.QR_Survey
                    && S().Sweep == sweep0)
                .Do("release the order", () => Dog().jobs.EndCurrentJob(JobCondition.InterruptForced))
                .Expect("the retained area resumes after the order", () =>
                    Dog().CurJobDef == QR_DefOf.QR_Survey && S().Sweep == sweep0
                    && object.Equals(S().AreaKey(Dog()), key0))
                .Do("run the battery down", () => CompRobodog.Of(Dog()).SetBattery(0.04f))
                .Expect("a low battery stops survey work before the area is discarded", () =>
                {
                    Pawn dog = Dog();
                    if (CompRobodog.Of(dog).WantsCharge)
                    {
                        if (dog.CurJobDef == QR_DefOf.QR_Survey)
                            return false;
                        if (S().Sweep != sweep0)
                            throw new InvalidOperationException("charging reset the sweep");
                        return true;
                    }
                    return false;
                })
                .Do("charge and resume", () =>
                {
                    CompRobodog.Of(Dog()).SetBattery(1f);
                    if (Dog().CurJob != null && Dog().CurJobDef != QR_DefOf.QR_Survey)
                        Dog().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                })
                .Expect("the retained area resumes after charging", () =>
                    Dog().CurJobDef == QR_DefOf.QR_Survey && S().Sweep == sweep0
                    && object.Equals(S().AreaKey(Dog()), key0))
                .Do("a raider appears far away", () =>
                {
                    raiderId = SpawnRaider(s, new IntVec3(world.maxX - 4, 0, world.minZ + 16)).ThingID;
                    Raid.Recheck(map);
                })
                .Expect("war suspends survey without discarding the area", () =>
                {
                    if (!Net().War)
                        return false;
                    if (Dog().CurJobDef == QR_DefOf.QR_Survey)
                        return false;
                    if (S().Sweep != sweep0 || !object.Equals(S().AreaKey(Dog()), key0))
                        throw new InvalidOperationException("war reset the sweep or discarded the area");
                    return true;
                })
                .Do("the raider dies; the war ends", () =>
                {
                    Scenario.ById(raiderId)?.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(map);
                })
                .Expect("the retained area resumes after the war", () =>
                    !Net().War && Dog().CurJobDef == QR_DefOf.QR_Survey && S().Sweep == sweep0
                    && object.Equals(S().AreaKey(Dog()), key0))
                .Summary(() => $"sweep {S().Sweep}, area retained, observed {observedBefore.Count} before interrupts; " +
                    $"{Net().Inspect(Dog())}; " + ColonistStates())
                .Start();
        }

        // ---------------------------------------------------------------- live room, zone, Home and fog edits

        private static void SurveyLiveEdits()
        {
            var s = new Scenario("Survey: live geometry and zone edits", 50000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default;
            IntVec3 leftCenter = default, rightCenter = default, g = default, homeCell = default;
            IntVec3 fogGoal = IntVec3.Invalid, fogReplacement = IntVec3.Invalid, fogStart = IntVec3.Invalid;
            string dogId = null;
            Zone_Stockpile stock = null;
            CellRect stockRect = default;
            Zone_Growing growing = null;
            Building_Storage shelf = null;
            IntVec3 shelfCell = default;
            bool stockReshaped = false, growingMade = false, shelfMade = false;
            bool sawLeft = false, sawRight = false;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("a large room with two pillar cells", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    world = SurveyFindRect(map, 61, 31, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 15), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 16, world.minZ + 6, 41, 19);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z),
                        new IntVec3(room.minX - 1, 0, room.CenterCell.z + 6));
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                        new IntVec3(room.minX + 5, 0, room.minZ + 5), Rot4.North);
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel,
                        new IntVec3(room.minX + 5, 0, room.minZ + 6), Rot4.North);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    homeCell = new IntVec3(world.minX + 8, 0, world.minZ + 10);
                })
                .Do("resume until a room goal is on the way", () => Scenario.Resume())
                .Expect("a room goal is selected", () =>
                {
                    Pawn dog = Dog();
                    return dog != null && dog.CurJobDef == QR_DefOf.QR_Survey
                        && S().CategoryAt(S().Goal(dog)) == SurveyCategory.Room;
                })
                .Do("split the room with an interior wall and door", () =>
                {
                    int x = room.CenterCell.x;
                    for (int z = room.minZ; z <= room.maxZ; z++)
                    {
                        IntVec3 cell = new IntVec3(x, 0, z);
                        if (z == room.CenterCell.z)
                            SpawnBuilding(s, ThingDefOf.Door, ThingDefOf.Steel, cell, Rot4.North);
                        else
                            SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
                    }
                    leftCenter = new IntVec3(room.minX + 2, 0, room.CenterCell.z);
                    rightCenter = new IntVec3(room.maxX - 2, 0, room.CenterCell.z);
                })
                .Expect("the split is detected as two proper rooms", () =>
                    S().CategoryAt(leftCenter) == SurveyCategory.Room
                    && S().CategoryAt(rightCenter) == SurveyCategory.Room)
                .Expect("both halves are actually surveyed", () =>
                {
                    sawLeft |= S().Observed(leftCenter);
                    sawRight |= S().Observed(rightCenter);
                    return sawLeft && sawRight;
                })
                .Do("merge the halves again", () =>
                {
                    foreach (Thing thing in new CellRect(room.CenterCell.x, room.minZ, 1, room.Height)
                                 .Cells.SelectMany(c => c.GetThingList(map).ToList())
                                 .Where(t => t.def == ThingDefOf.Wall || t.def == ThingDefOf.Door).ToList())
                        thing.Destroy();
                })
                .Expect("the merge is detected as one room", () =>
                {
                    Room left = leftCenter.GetRoom(map), right = rightCenter.GetRoom(map);
                    return left != null && left == right && S().CategoryAt(leftCenter) == SurveyCategory.Room;
                })
                .Do("a same-count wall reshape invalidates a stale goal", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Pawn dog = Dog();
                    Teleport(dog, new IntVec3(world.minX + 2, 0, world.minZ + 2));
                    if (dog.CurJob != null)
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    Job probe = SurveyFreshProbe(dog, out g, out _);
                    if (probe == null || !g.IsValid || g.DistanceTo(dog.Position) < 4f
                        || S().CategoryAt(g) != SurveyCategory.Room)
                    {
                        SurveyReturn(probe);
                        throw new InvalidOperationException("no distant room goal to invalidate");
                    }
                    SurveyReturn(probe);
                    Thing pillar = new CellRect(room.minX + 5, room.minZ + 5, 1, 2).Cells
                        .SelectMany(c => c.GetThingList(map)).FirstOrDefault(t => t.def == ThingDefOf.Wall);
                    if (pillar == null)
                        throw new InvalidOperationException("the pillar fixture is missing");
                    pillar.Destroy();
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, g, Rot4.North);
                    if (S().CategoryAt(g) == SurveyCategory.Room)
                        throw new InvalidOperationException("the reshaped wall cell is still a room cell");
                    Job replacement = S().JobFor(dog);
                    if (replacement == null)
                        throw new InvalidOperationException("no replacement goal after the reshape");
                    IntVec3 next = S().Goal(dog);
                    bool usable = next.IsValid && next != g && !next.Fogged(map)
                        && SurveyAccepted(dog, next, out _);
                    SurveyReturn(replacement);
                    if (!usable)
                        throw new InvalidOperationException($"the stale goal {g} was not replaced by an accepted point");
                })
                .Do("resume after the reshape", () => Scenario.Resume())
                .Expect("no stale goal survives the reshape", () =>
                {
                    if (S().CategoryAt(g) == SurveyCategory.Room)
                        return false;
                    Pawn dog = Dog();
                    if (dog.CurJobDef != QR_DefOf.QR_Survey)
                        return true;
                    IntVec3 goal = S().Goal(dog);
                    return goal != g && goal.IsValid && !goal.Fogged(map)
                        && SurveyAccepted(dog, goal, out _);
                })
                .Do("remove Home from one cell, then restore it", () => map.areaManager.Home[homeCell] = false)
                .Expect("the Home edit is re-read", () => S().CategoryAt(homeCell) == SurveyCategory.Outside)
                .Do("restore Home on that cell", () => map.areaManager.Home[homeCell] = true)
                .Expect("restoring Home is re-read too", () => S().CategoryAt(homeCell) == SurveyCategory.Home)
                .Do("fog a distant active goal while the worker's starting cell stays revealed", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Pawn dog = Dog();
                    if (dog.CurJob != null)
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    dog.pather.StopDead();
                    fogStart = new IntVec3(world.minX + 2, 0, world.minZ + 2);
                    Teleport(dog, fogStart);
                    Job probe = SurveyFreshProbe(dog, out fogGoal, out _);
                    if (probe == null || !fogGoal.IsValid || fogGoal.DistanceTo(fogStart) <= RobodogWork.Utility.scanRadius
                        || S().CategoryAt(fogGoal) != SurveyCategory.Room)
                    {
                        SurveyReturn(probe);
                        throw new InvalidOperationException($"no remote room goal for fog invalidation: {fogStart}->{fogGoal}");
                    }
                    dog.jobs.StartJob(probe, JobCondition.InterruptForced);
                    map.fogGrid.Refog(CellRect.SingleCell(fogGoal));
                    if (dog.Position.Fogged(map) || !fogGoal.Fogged(map))
                        throw new InvalidOperationException($"invalid fog fixture: worker {dog.Position}, goal {fogGoal}");
                    QR.Log($"survey fog edit: worker {fogStart} revealed; active remote goal {fogGoal} fogged");
                })
                .Do("resume after the remote fog edit", () => Scenario.Resume())
                .Expect("a fogged goal is replaced by a revealed one", () =>
                {
                    Pawn dog = Dog();
                    if (dog?.CurJobDef != QR_DefOf.QR_Survey)
                        return false;
                    IntVec3 goal = S().Goal(dog);
                    if (!goal.IsValid || goal == fogGoal || goal.Fogged(map) || !SurveyAccepted(dog, goal, out _))
                        return false;
                    fogReplacement = goal;
                    return true;
                })
                .Expect("the replacement footprint is actually scanned", () => S().Observed(fogReplacement))
                .Do("unfog every fixture cell again", () =>
                {
                    foreach (IntVec3 cell in world.Cells)
                        if (map.fogGrid.IsFogged(cell))
                            map.fogGrid.Unfog(cell);
                })
                .Expect("the restored remote footprint is actually surveyed", () => S().Observed(fogGoal))
                .Do("create and reshape a stockpile, then a growing zone and a shelf", () =>
                {
                    stockRect = new CellRect(world.minX + 4, world.minZ + 2, 3, 3);
                    stock = SurveyStockpile(s, stockRect, ThingDefOf.Gold);
                    stockReshaped = true;
                })
                .Expect("the new stockpile is classified and surveyed", () =>
                    S().CategoryAt(stockRect.CenterCell) == SurveyCategory.Storage && S().Observed(stockRect.CenterCell))
                .Do("reshape the stockpile without changing its cell count", () =>
                {
                    IntVec3 removed = new IntVec3(stockRect.minX, 0, stockRect.minZ);
                    IntVec3 added = new IntVec3(stockRect.maxX + 1, 0, stockRect.minZ);
                    stock.RemoveCell(removed);
                    stock.AddCell(added);
                })
                .Expect("the same-count zone reshape is re-read", () =>
                    S().CategoryAt(new IntVec3(stockRect.maxX + 1, 0, stockRect.minZ)) == SurveyCategory.Storage
                    && S().CategoryAt(new IntVec3(stockRect.minX, 0, stockRect.minZ)) != SurveyCategory.Storage)
                .Do("delete the stockpile and create a growing zone", () =>
                {
                    stock.Delete();
                    growing = SurveyGrowing(s, new CellRect(world.minX + 10, world.minZ + 2, 3, 3));
                    growingMade = true;
                })
                .Expect("the zone deletion and growing creation are re-read", () =>
                    S().CategoryAt(stockRect.CenterCell) != SurveyCategory.Storage
                    && S().CategoryAt(new IntVec3(world.minX + 11, 0, world.minZ + 3)) == SurveyCategory.Growing)
                .Do("reshape the growing zone without changing its cell count", () =>
                {
                    growing.RemoveCell(new IntVec3(world.minX + 10, 0, world.minZ + 2));
                    growing.AddCell(new IntVec3(world.minX + 13, 0, world.minZ + 2));
                })
                .Expect("the same-count growing reshape is re-read", () =>
                    S().CategoryAt(new IntVec3(world.minX + 10, 0, world.minZ + 2)) != SurveyCategory.Growing
                    && S().CategoryAt(new IntVec3(world.minX + 13, 0, world.minZ + 2)) == SurveyCategory.Growing)
                .Do("delete the growing zone and spawn a shelf", () =>
                {
                    growing.Delete();
                    shelfCell = SurveyShelfCell(map, new CellRect(world.minX + 10, world.minZ + 8, 5, 5), Rot4.North);
                    shelf = SurveyShelf(s, shelfCell, Rot4.North);
                    shelfMade = true;
                })
                .Expect("the growing deletion and the storage building spawn are re-read", () =>
                    S().CategoryAt(new IntVec3(world.minX + 11, 0, world.minZ + 3)) != SurveyCategory.Growing
                    && S().CategoryAt(shelfCell) == SurveyCategory.Storage)
                .Do("despawn the shelf", () => shelf.Destroy())
                .Expect("the shelf despawn is re-read", () =>
                    S().CategoryAt(shelfCell) != SurveyCategory.Storage)
                .Summary(() => $"stockpile {stockReshaped}, growing {growingMade}, shelf {shelfMade}; " +
                    $"sweep {S().Sweep}; stale result {g}; fog {fogStart}->{fogGoal}, replacement {fogReplacement}; " +
                    $"worker {Dog()?.Position}; {Net().Inspect(Dog())}")
                .Start();
        }

        private static void SurveyNewAllowedArea()
        {
            var s = new Scenario("Survey: new allowed-area paths", 20000);
            CellRect world = default, pile = default;
            string dogId = null, localId = null;
            var areaIds = new List<int>();
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            void PrimePathData()
            {
                Pawn dog = Dog();
                if (dog.CurJob != null)
                    dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                dog.pather.StopDead();
                using (PawnPath path = dog.Map.pathFinder.FindPathNow(dog.Position, pile.CenterCell,
                           TraverseParms.For(dog, Danger.Deadly), peMode: PathEndMode.OnCell))
                    if (!path.Found)
                        throw new InvalidOperationException("the controlled allowed-area fixture has no native route");
            }
            void RestrictToNewArea()
            {
                Map map = Find.CurrentMap;
                if (!map.areaManager.TryMakeNewAllowed(out Area_Allowed area))
                    throw new InvalidOperationException("couldn't create the new allowed area");
                areaIds.Add(area.ID);
                foreach (IntVec3 cell in world.Cells)
                    area[cell] = true;
                Dog().playerSettings.AreaRestrictionInPawnCurrentMap = area;
            }
            s.Do("stage a distant unreported stack and a worker on revealed controlled ground", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    world = SurveyFindRect(map, 61, 17, 120f, controlledFixture: s);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.CenterCell.z), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    pile = new CellRect(world.minX + 10, world.minZ + 5, 3, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    NetStack(s, ThingDefOf.Gold, 31, new IntVec3(world.maxX - 3, 0, world.CenterCell.z));
                    s.OnFinish(() =>
                    {
                        foreach (Area area in Find.CurrentMap.areaManager.AllAreas.ToList())
                            if (areaIds.Contains(area.ID))
                                area.Delete();
                    });
                })
                .Do("request ordinary surveying immediately after a new restriction in the same cached tick", () =>
                {
                    PrimePathData();
                    RestrictToNewArea();
                    Job job = Net().TakeOrder(Dog());
                    if (job != null)
                        Dog().jobs.StartJob(job, JobCondition.InterruptForced);
                })
                .SaveAndReload()
                .Expect("real surveying discovers and hauls the distant stack after reload", () =>
                    CountIn(pile, ThingDefOf.Gold) == 31)
                .Do("prime the grid, replace the restriction, and report a real nearby stack in that same tick", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    PrimePathData();
                    RestrictToNewArea();
                    Pawn dog = Dog();
                    IntVec3 cell = OpenCell(Find.CurrentMap, dog.Position, 3f, 8f,
                        c => world.Contains(c) && !pile.Contains(c) && !c.Fogged(Find.CurrentMap));
                    localId = NetStack(s, ThingDefOf.Gold, 7, cell).ThingID;
                    Net().Tick(force: true);
                    Scenario.Resume();
                })
                .Expect("productive assignment also delivers without a stale-area path or duplicate task", () =>
                    CountIn(pile, ThingDefOf.Gold) == 38 && !NetQueued(localId))
                .Summary(() => $"stored {CountIn(pile, ThingDefOf.Gold)}, new areas {areaIds.Count}; {Net().Inspect(Dog())}")
                .Start();
        }

        // ---------------------------------------------------------------- legacy save arrivals

        private static void SurveyLegacySave()
        {
            var s = new Scenario("Survey: legacy save arrivals", 30000);
            Map map = Find.CurrentMap;
            CellRect world = default, room = default, pile = default;
            IntVec3 legacyCell = default, queuedCell = default;
            string dogId = null, legacyId = null, queuedId = null;
            int queuedTask = 0;
            bool nearDiscovery = false;
            Game before = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Survey S() => SurveyOf();
            s.Do("save a held worker with a queued task and injected legacy surveyCells tags", () =>
                {
                    DraftColonists(s);
                    world = SurveyFindRect(map, 35, 23, 120f);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, world);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 11), powered: false,
                        out Building_Stable stable);
                    dogId = dog.ThingID;
                    room = new CellRect(world.minX + 23, world.minZ + 7, 7, 5);
                    SurveyBuildRoom(s, room, new IntVec3(room.minX - 1, 0, room.CenterCell.z));
                    pile = new CellRect(world.minX + 1, world.minZ + 1, 2, 2);
                    SurveyStockpile(s, pile, ThingDefOf.Gold);
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    SurveyHold(dog, 12000);
                    legacyCell = new IntVec3(room.maxX - 2, 0, room.maxZ - 2);
                    legacyId = NetStack(s, ThingDefOf.Gold, 30, legacyCell).ThingID;
                    queuedCell = new IntVec3(world.minX + 24, 0, world.minZ + 2);
                    queuedId = NetStack(s, ThingDefOf.Gold, 30, queuedCell).ThingID;
                    map.fogGrid.Unfog(queuedCell);
                    if (!Net().Report(NetworkTaskKind.Haul, Scenario.ById(queuedId), NetHaulGiver))
                        throw new InvalidOperationException("the saved backlog task was refused");
                    queuedTask = SurveyTaskId(queuedId);
                    before = Current.Game;
                    GameDataSaveLoader.SaveGame(Scenario.SaveName);
                    SurveyInjectLegacyArrivals(new[] { legacyCell, queuedCell, world.CenterCell });
                    GameDataSaveLoader.LoadGame(Scenario.SaveName);
                })
                .Expect("loaded", () => Current.Game != before && AutoTest.MapReady)
                .Do("hold the worker again", () => SurveyHold(Dog(), 12000))
                .Expect("resumed", () => Scenario.Resume())
                .Expect("legacy arrival tags neither import coverage nor suppress the unobserved stack", () =>
                {
                    Survey survey = S();
                    if (survey.Observed(legacyCell) && survey.Sweep == 1)
                        throw new InvalidOperationException("legacy arrival history was imported as scanned coverage");
                    return !SurveyQueued(legacyId) && SurveyQueueCount(queuedId) == 1
                        && SurveyTaskId(queuedId) == queuedTask;
                })
                .Do("release the worker", () => Dog().jobs.EndCurrentJob(JobCondition.InterruptForced))
                .Expect("real local travel discovers the legacy-named stack and the saved backlog", () =>
                {
                    Pawn dog = Dog();
                    if (dog == null || !dog.Spawned)
                        return false;
                    if (!nearDiscovery && SurveyQueued(legacyId))
                    {
                        Thing gold = Scenario.ById(legacyId);
                        if (gold != null && !gold.IsInValidBestStorage()
                            && dog.Position.DistanceTo(gold.Position) > 20f)
                            throw new InvalidOperationException("the legacy-named stack was reported remotely");
                        nearDiscovery = true;
                    }
                    if (dog.carryTracker?.CarriedThing?.ThingID == legacyId)
                        nearDiscovery = true;
                    return nearDiscovery && CountIn(pile, ThingDefOf.Gold) == 60
                        && !SurveyQueued(legacyId) && !SurveyQueued(queuedId);
                })
                .Summary(() => $"legacy {legacyCell}, queued {queuedCell}, pile {CountIn(pile, ThingDefOf.Gold)}, " +
                    $"sweep {S().Sweep}; {Net().Inspect(Dog())}")
                .Start();
        }

        private static void SurveyReshapedGoal()
        {
            var s = new Scenario("Survey: reshaped retained goal", 6000);
            Map map = Find.CurrentMap;
            CellRect oldZone = default, movedZone = default;
            Zone_Stockpile stock = null;
            string dogId = null;
            IntVec3 oldGoal = IntVec3.Invalid;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("retain a distant stockpile goal before a manual interruption", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    DraftColonists(s);
                    CellRect world = SurveyFindRect(map, 61, 17, 120f, controlledFixture: s);
                    SurveyIsolate(s, world.Cells);
                    SurveyHome(s, new CellRect(world.minX + 33, world.minZ + 5, 14, 9));
                    oldZone = new CellRect(world.minX + 35, world.minZ + 7, 3, 3);
                    movedZone = new CellRect(oldZone.minX + 6, oldZone.minZ, 3, 3);
                    stock = SurveyStockpile(s, oldZone, ThingDefOf.Gold);
                    Pawn dog = SurveyStable(s, new IntVec3(world.minX + 6, 0, world.minZ + 8),
                        powered: true, out Building_Stable stable);
                    dogId = dog.ThingID;
                    SurveyPlace(dog, stable, stable.InteractionCell);
                    Job job = SurveyFreshProbe(dog, out oldGoal, out object key);
                    if (job == null || !oldZone.Contains(oldGoal) || !object.Equals(key, stock))
                        throw new InvalidOperationException("the distant stockpile was not selected");
                    dog.jobs.StartJob(job, JobCondition.InterruptForced);
                    SurveyHold(dog, 10000);
                    foreach (IntVec3 cell in movedZone.Cells)
                        stock.AddCell(cell);
                    foreach (IntVec3 cell in oldZone.Cells)
                        stock.RemoveCell(cell);
                    NetStack(s, ThingDefOf.Gold, 31,
                        new IntVec3(movedZone.maxX + 2, 0, movedZone.CenterCell.z));
                })
                .Do("resume the held observer through a metadata poll interval", () => Scenario.Resume())
                .Wait(720)
                .Do("release the order without resetting the retained survey", () =>
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false))
                .Expect("real local travel discovers and hauls into the reshaped stockpile", () =>
                    CountIn(movedZone, ThingDefOf.Gold) == 31)
                .Summary(() => $"old goal {oldGoal}, current {SurveyOf().Goal(Dog())}, " +
                    $"worker {Dog()?.Position}, stored {CountIn(movedZone, ThingDefOf.Gold)}; {Net().Inspect(Dog())}")
                .Start();
        }

        /// <summary>Inject the removed pre-cutover arrival tags into the controlled save's elected NetworkState.</summary>
        private static void SurveyInjectLegacyArrivals(IEnumerable<IntVec3> cells)
        {
            string path = GenFilePaths.FilePathForSavedGame(Scenario.SaveName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"the legacy fixture save is missing: {path}");
            XDocument document = XDocument.Load(path);
            List<XElement> states = document.Descendants("network")
                .Where(network => (string)network.Attribute("IsNull") != "True"
                    && (network.Element("nextId") != null || network.Element("normal") != null
                        || network.Element("war") != null)).ToList();
            if (states.Count == 0)
                throw new InvalidOperationException("no elected NetworkState found in the fixture save");
            var positions = cells.ToList();
            foreach (XElement state in states)
            {
                state.Add(new XElement("surveyCells",
                    positions.Select(cell => new XElement("li", $"({cell.x}, 0, {cell.z})"))));
                state.Add(new XElement("surveyTicks",
                    positions.Select(_ => new XElement("li", Find.TickManager.TicksGame.ToString()))));
            }
            document.Save(path);
        }

    }
}
