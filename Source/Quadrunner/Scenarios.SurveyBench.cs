using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// QR-0001 matched benchmark: the pre-cutover selector (an exact test-only local copy) and the proposed
    /// <see cref="Survey"/> planner are measured on the same fixture, in the same process, with the same observer,
    /// targets, scan cadence and XML tuning. Physical baseline and proposed phases reuse the identical live geometry;
    /// a geometry signature checked at each phase start proves it.
    ///
    /// Real local scans at the existing 120-tick cadence discover real staged items; nothing is enumerated remotely.
    /// Medirunner observers are spawned and Active (so <see cref="DogWork.ReportLocal"/> runs and feeds scans) but are
    /// not utility workers, so the runtime dispatcher never replaces the benchmark's movement job. Baseline movement
    /// runs the exact historical QR_Survey toils and arrival writer; the fixture temporarily swaps its driver type,
    /// rejects unrelated robodogs and restores the type on success or failure. No unregistered job is saved.
    /// The proposed phase uses the normal QR_Survey driver and completed scan footprints.
    ///
    /// Selection cost/allocation is measured externally around every planner call. New geometry timing and counters
    /// are read from Survey's opt-in Measure counters; the benchmark uses deltas and leaves Measure off outside its
    /// measured phases.
    /// </summary>
    public static partial class Scenarios
    {
        internal static Action<Pawn> HistoricalArrival = null;
        private const int WarmIterations = 120;
        private const int PhysicalTicks = 15000;
        private const int MaxSelections = 80;

        // ---------------------------------------------------------------- registration

        private static void SurveyBenchRoomHeavy() => SurveyBenchRun(SurveyBenchKind.RoomHeavy);
        private static void SurveyBenchLargeZone() => SurveyBenchRun(SurveyBenchKind.LargeZone);
        private static void SurveyBenchWallDetour() => SurveyBenchRun(SurveyBenchKind.WallDetour);
        private static void SurveyBenchOpenFallback() => SurveyBenchRun(SurveyBenchKind.OpenFallback);

        private static void SurveyBenchRun(SurveyBenchKind kind)
        {
            var s = new Scenario($"Survey bench: {KindName(kind)}", 40000);
            SurveyBenchFixture fixture = null;
            SurveyBenchRunner runner = null;
            s.Do($"build the {KindName(kind)} fixture", () =>
                {
                    DraftColonists(s);
                    bool autoHome = Find.PlaySettings.autoHomeArea;
                    Find.PlaySettings.autoHomeArea = false;
                    s.OnFinish(() => Find.PlaySettings.autoHomeArea = autoHome);
                    fixture = SurveyBenchBuild(kind, s);
                    runner = new SurveyBenchRunner(s, fixture);
                })
                .Expect("measure baseline and proposed selection and real walking", () => runner.Step())
                .Summary(() => runner?.Summary() ?? "benchmark did not start")
                .Start();
        }

        private static string KindName(SurveyBenchKind kind)
        {
            switch (kind)
            {
                case SurveyBenchKind.RoomHeavy: return "room-heavy";
                case SurveyBenchKind.LargeZone: return "large zone";
                case SurveyBenchKind.WallDetour: return "wall detour";
                default: return "open fallback";
            }
        }

        // ---------------------------------------------------------------- fixtures

        private enum SurveyBenchKind { RoomHeavy, LargeZone, WallDetour, OpenFallback }

        private sealed class SurveyBenchFixture
        {
            public string Label;
            public IntVec3 DogStart;
            public CellRect Site;
            public long Signature;
            public IntVec3 RefreshCell = IntVec3.Invalid;
            public string GeometryNote;
            public readonly List<IntVec3> Placements = new List<IntVec3>();
            public readonly List<IntVec3> ProbeAreas = new List<IntVec3>();
            public readonly List<IntVec3> TargetCells = new List<IntVec3>();
        }

        private static SurveyBenchFixture SurveyBenchBuild(SurveyBenchKind kind, Scenario s)
        {
            SurveyBenchFixture fixture;
            switch (kind)
            {
                case SurveyBenchKind.RoomHeavy: fixture = BuildRoomHeavy(s); break;
                case SurveyBenchKind.LargeZone: fixture = BuildLargeZone(s); break;
                case SurveyBenchKind.WallDetour: fixture = BuildWallDetour(s); break;
                default: fixture = BuildOpenFallback(s); break;
            }
            EnsureBenchCells(fixture, Find.CurrentMap);
            Find.CurrentMap.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            fixture.Signature = BenchGeometrySignature(Find.CurrentMap, fixture.Site);
            return fixture;
        }

        private static IntVec3 SurveyBenchGround(Scenario s, Map map, int width, int height)
        {
            CellRect site = SurveyFindRect(map, width, height, 120f, waterFixture: s);
            var fogged = site.Cells.Where(c => c.Fogged(map)).ToList();
            foreach (IntVec3 cell in fogged) map.fogGrid.Unfog(cell);
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now != null)
                    foreach (IntVec3 cell in fogged) now.fogGrid.Refog(CellRect.SingleCell(cell));
            });
            return new IntVec3(site.minX, 0, site.minZ);
        }

        private static SurveyBenchFixture BuildRoomHeavy(Scenario s)
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = SurveyBenchGround(s, map, 61, 26);
            var f = new SurveyBenchFixture { Label = "room-heavy", Site = new CellRect(origin.x, origin.z, 61, 26) };
            BenchHome(s, f.Site, true);
            CellRect[] rooms =
            {
                new CellRect(origin.x, origin.z, 13, 13),
                new CellRect(origin.x + 15, origin.z, 13, 13),
                new CellRect(origin.x + 30, origin.z, 13, 13),
                new CellRect(origin.x + 45, origin.z, 13, 13),
            };
            foreach (CellRect room in rooms)
                BenchRoom(s, room, doorNorth: true);

            BenchStockpile(s, rooms[1].ContractedBy(2), ThingDefOf.Gold);
            NetStack(s, ThingDefOf.Gold, 30, rooms[1].ContractedBy(2).CenterCell);
            BenchShelf(s, rooms[2].ContractedBy(1).minX + 2, rooms[2].ContractedBy(1).minZ + 2);
            BenchShelf(s, rooms[2].ContractedBy(1).maxX - 2, rooms[2].ContractedBy(1).minZ + 2);
            BenchStockpile(s, new CellRect(rooms[3].ContractedBy(1).minX + 1, rooms[3].ContractedBy(1).minZ + 1, 4, 4),
                ThingDefOf.Gold);
            BenchShelf(s, rooms[3].ContractedBy(1).maxX - 2, rooms[3].ContractedBy(1).minZ + 2);

            CellRect? grow = BenchFertileRect(map, new CellRect(origin.x + 2, origin.z + 20, 56, 6), 7, 5);
            string growNote = "no fertile growing plot found";
            if (grow.HasValue)
            {
                BenchGrowingZone(s, grow.Value);
                f.ProbeAreas.Add(grow.Value.CenterCell);
                growNote = $"growing plot at {grow.Value.minX},{grow.Value.minZ}";
            }
            f.GeometryNote = $"4 proper roofed rooms, 2 stockpiles, 2 shelves, {growNote}, Home corridor";

            f.DogStart = BenchCell(map, new IntVec3(origin.x + 30, 0, origin.z + 16));
            f.Placements.Add(f.DogStart);
            foreach (CellRect room in rooms)
                f.Placements.Add(BenchCell(map, room.ContractedBy(1).CenterCell));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 58, 0, origin.z + 16)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 3, 0, origin.z + 16)));
            f.ProbeAreas.AddRange(new[]
            {
                rooms[0].ContractedBy(1).CenterCell,
                rooms[1].ContractedBy(1).CenterCell,
                rooms[2].ContractedBy(1).CenterCell,
                rooms[3].ContractedBy(1).CenterCell,
                new IntVec3(origin.x + 58, 0, origin.z + 16),
            });
            f.TargetCells.Add(BenchCell(map, f.DogStart + new IntVec3(3, 0, -2)));
            f.TargetCells.Add(BenchCell(map, new IntVec3(origin.x + 58, 0, origin.z + 17)));
            f.RefreshCell = BenchCell(map, new IntVec3(origin.x + 59, 0, origin.z + 1));
            return f;
        }

        private static SurveyBenchFixture BuildLargeZone(Scenario s)
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = SurveyBenchGround(s, map, 45, 45);
            var f = new SurveyBenchFixture { Label = "large zone", Site = new CellRect(origin.x, origin.z, 45, 45) };
            BenchHome(s, f.Site, true);
            CellRect zoneRect = new CellRect(origin.x + 6, origin.z + 6, 33, 33);
            Zone_Stockpile zone = BenchStockpile(s, zoneRect, ThingDefOf.Gold);
            NetStack(s, ThingDefOf.Gold, 30, zoneRect.CenterCell);
            NetStack(s, ThingDefOf.Gold, 30, new IntVec3(zoneRect.minX + 3, 0, zoneRect.minZ + 3));
            NetStack(s, ThingDefOf.Gold, 30, new IntVec3(zoneRect.maxX - 3, 0, zoneRect.maxZ - 3));
            f.GeometryNote = "one 33x33 stockpile zone inside Home; no rooms";

            f.DogStart = BenchCell(map, new IntVec3(origin.x + 2, 0, origin.z + 2));
            f.Placements.Add(f.DogStart);
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 22, 0, origin.z + 2)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 42, 0, origin.z + 2)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 22, 0, origin.z + 22)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 2, 0, origin.z + 42)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 42, 0, origin.z + 42)));
            f.ProbeAreas.Add(zone.Position);
            f.ProbeAreas.Add(zoneRect.CenterCell);
            f.ProbeAreas.Add(new IntVec3(zoneRect.minX + 1, 0, zoneRect.minZ + 1));
            f.TargetCells.Add(BenchCell(map, f.DogStart + new IntVec3(3, 0, 0)));
            f.TargetCells.Add(BenchCell(map, new IntVec3(origin.x + 42, 0, origin.z + 42)));
            f.RefreshCell = BenchCell(map, new IntVec3(origin.x + 1, 0, origin.z + 44));
            return f;
        }

        private static SurveyBenchFixture BuildWallDetour(Scenario s)
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = SurveyBenchGround(s, map, 45, 30);
            var f = new SurveyBenchFixture { Label = "wall detour", Site = new CellRect(origin.x, origin.z, 45, 30) };
            BenchHome(s, f.Site, true);
            CellRect near = new CellRect(origin.x + 4, origin.z + 2, 11, 11);
            CellRect far = new CellRect(origin.x + 28, origin.z + 6, 11, 11);
            // The dog stands south of both. The near room's only door is on its far south side; the farther room's
            // north door opens toward the dog. Euclidean distance favors the near room; returned route cost does not.
            BenchRoom(s, near, doorNorth: false);
            BenchRoom(s, far, doorNorth: true);
            f.GeometryNote = "near room with only a far south door; farther room with a near north door";

            f.DogStart = BenchCell(map, new IntVec3(origin.x + 18, 0, origin.z + 20));
            f.Placements.Add(f.DogStart);
            f.Placements.Add(BenchCell(map, near.ContractedBy(1).CenterCell));
            f.Placements.Add(BenchCell(map, far.ContractedBy(1).CenterCell));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 2, 0, origin.z + 20)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 42, 0, origin.z + 20)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 18, 0, origin.z + 27)));
            f.ProbeAreas.Add(near.ContractedBy(1).CenterCell);
            f.ProbeAreas.Add(far.ContractedBy(1).CenterCell);
            f.TargetCells.Add(BenchCell(map, near.ContractedBy(1).CenterCell));
            f.TargetCells.Add(BenchCell(map, far.ContractedBy(1).CenterCell));
            f.RefreshCell = BenchCell(map, new IntVec3(origin.x + 1, 0, origin.z + 1));
            return f;
        }

        private static SurveyBenchFixture BuildOpenFallback(Scenario s)
        {
            Map map = Find.CurrentMap;
            IntVec3 origin = SurveyBenchGround(s, map, 51, 51);
            var f = new SurveyBenchFixture { Label = "open fallback", Site = new CellRect(origin.x, origin.z, 51, 51) };
            BenchHome(s, f.Site, false);
            f.GeometryNote = "51x51 open, no rooms/zones, explicitly outside Home";
            f.DogStart = BenchCell(map, new IntVec3(origin.x + 25, 0, origin.z + 25));
            f.Placements.Add(f.DogStart);
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 4, 0, origin.z + 4)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 46, 0, origin.z + 4)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 4, 0, origin.z + 46)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 46, 0, origin.z + 46)));
            f.Placements.Add(BenchCell(map, new IntVec3(origin.x + 25, 0, origin.z + 46)));
            f.ProbeAreas.Add(f.DogStart);
            f.ProbeAreas.Add(new IntVec3(origin.x + 46, 0, origin.z + 46));
            f.TargetCells.Add(BenchCell(map, f.DogStart + new IntVec3(3, 0, 0)));
            f.TargetCells.Add(BenchCell(map, new IntVec3(origin.x + 46, 0, origin.z + 46)));
            f.RefreshCell = BenchCell(map, new IntVec3(origin.x + 1, 0, origin.z + 1));
            return f;
        }

        private static void EnsureBenchCells(SurveyBenchFixture f, Map map)
        {
            f.Placements.Sort((a, b) => a.DistanceToSquared(f.DogStart).CompareTo(b.DistanceToSquared(f.DogStart)));
            var unique = new List<IntVec3>();
            foreach (IntVec3 cell in f.Placements)
                if (cell.IsValid && !unique.Contains(cell))
                    unique.Add(cell);
            f.Placements.Clear();
            f.Placements.AddRange(unique);
            if (f.Placements.Count < 4)
                throw new InvalidOperationException($"{f.Label}: only {f.Placements.Count} distinct observer placements");
            if (f.TargetCells.Count < 2 || f.TargetCells.Any(c => !c.IsValid) || f.TargetCells[0] == f.TargetCells[1])
                throw new InvalidOperationException($"{f.Label}: invalid discovery targets");
            foreach (IntVec3 cell in f.TargetCells)
                if (!cell.InBounds(map) || !cell.Standable(map) || cell.Fogged(map))
                    throw new InvalidOperationException($"{f.Label}: target cell {cell} is not standable and revealed");
        }

        private static IntVec3 BenchCell(Map map, IntVec3 desired, int radius = 8)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(desired, radius, useCenter: true))
                if (cell.InBounds(map) && cell.Standable(map) && !cell.Fogged(map)
                    && cell.GetFirstBuilding(map) == null && cell.GetFirstPawn(map) == null
                    && cell.GetFirstItem(map) == null)
                    return cell;
            throw new InvalidOperationException($"no free standable cell within {radius} of {desired}");
        }

        private static CellRect? BenchFertileRect(Map map, CellRect search, int width, int height)
        {
            for (int x = search.minX; x + width <= search.maxX; x++)
            for (int z = search.minZ; z + height <= search.maxZ; z++)
            {
                var rect = new CellRect(x, z, width, height);
                if (rect.Cells.All(c => c.InBounds(map) && c.GetTerrain(map).fertility > 0.4f
                        && c.GetFirstBuilding(map) == null && c.GetZone(map) == null && c.GetFirstPawn(map) == null
                        && !c.Fogged(map)))
                    return rect;
            }
            return null;
        }

        private static void BenchHome(Scenario s, CellRect rect, bool value)
        {
            Map map = Find.CurrentMap;
            var prior = new List<(IntVec3 cell, bool value)>();
            foreach (IntVec3 cell in rect.Cells)
            {
                prior.Add((cell, map.areaManager.Home[cell]));
                map.areaManager.Home[cell] = value;
            }
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null) return;
                foreach ((IntVec3 cell, bool old) in prior)
                    now.areaManager.Home[cell] = old;
            });
        }

        private static void BenchRoom(Scenario s, CellRect rect, bool doorNorth)
        {
            Map map = Find.CurrentMap;
            IntVec3 door = new IntVec3(rect.CenterCell.x, 0, doorNorth ? rect.maxZ : rect.minZ);
            foreach (IntVec3 cell in rect.EdgeCells)
                if (cell != door)
                    SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North);
            SpawnBuilding(s, ThingDefOf.Door, ThingDefOf.Steel, door, Rot4.North);
            IntVec3[] cells = rect.Cells.ToArray();
            RoofDef[] before = cells.Select(c => map.roofGrid.RoofAt(c)).ToArray();
            foreach (IntVec3 cell in cells)
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now == null) return;
                for (int i = 0; i < cells.Length && i < before.Length; i++)
                    now.roofGrid.SetRoof(cells[i], before[i]);
            });
        }

        private static Zone_Stockpile BenchStockpile(Scenario s, CellRect rect, ThingDef only)
        {
            Map map = Find.CurrentMap;
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in rect)
                zone.AddCell(cell);
            zone.settings.filter.SetDisallowAll();
            zone.settings.filter.SetAllow(only, true);
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now != null && now.zoneManager.AllZones.Contains(zone))
                    zone.Delete();
            });
            return zone;
        }

        private static void BenchGrowingZone(Scenario s, CellRect rect)
        {
            Map map = Find.CurrentMap;
            var zone = new Zone_Growing(map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in rect)
                zone.AddCell(cell);
            zone.allowSow = false;
            zone.allowCut = false;
            s.OnFinish(() =>
            {
                Map now = Find.CurrentMap;
                if (now != null && now.zoneManager.AllZones.Contains(zone))
                    zone.Delete();
            });
        }

        private static void BenchShelf(Scenario s, int x, int z)
        {
            Map map = Find.CurrentMap;
            var shelf = (Building_Storage)ThingMaker.MakeThing(ThingDefOf.Shelf, ThingDefOf.Steel);
            shelf.SetFaction(Faction.OfPlayer);
            s.Track(GenSpawn.Spawn(shelf, new IntVec3(x, 0, z), map));
            shelf.settings.filter.SetDisallowAll();
            shelf.settings.filter.SetAllow(ThingDefOf.Gold, true);
        }

        /// <summary>Stable numeric fingerprint of cells/buildings/zones/Home/rooms/roofs in the fixture. Checked at
        /// each physical phase start so the two modes provably ran on the same live geometry.</summary>
        private static long BenchGeometrySignature(Map map, CellRect site)
        {
            const ulong Offset = 1469598103934665603UL, Prime = 1099511628211UL;
            ulong hash = Offset;
            void Mix(long value)
            {
                hash ^= (ulong)value;
                hash *= Prime;
            }
            foreach (IntVec3 cell in site.Cells)
            {
                Room room = map.regionGrid.GetValidRegionAt(cell)?.Room;
                Mix(cell.x); Mix(cell.z);
                Mix(room?.CellCount ?? -1);
                Mix(room?.IsDoorway == true ? 1 : 0);
                Mix(room?.OutdoorsForWork == true ? 1 : 0);
                Mix(room?.ProperRoom == true ? 1 : 0);
                Mix(cell.GetZone(map)?.ID ?? -1);
                Mix(map.areaManager.Home[cell] ? 1 : 0);
                ThingDef building = cell.GetFirstBuilding(map)?.def;
                RoofDef roof = map.roofGrid.RoofAt(cell);
                Mix(building == null ? -1 : building.shortHash);
                Mix(roof == null ? -1 : roof.shortHash);
                Mix(cell.Standable(map) ? 1 : 0);
            }
            return unchecked((long)hash);
        }


        // ---------------------------------------------------------------- historical selector

        /// <summary>Exact test-only copy of the pre-cutover selector, with benchmark-local arrival history. Never a
        /// production fallback; executable independently of runtime state.</summary>
        private sealed class HistoricalSurvey
        {
            private readonly List<IntVec3> cells = new List<IntVec3>();
            private readonly List<int> ticks = new List<int>();
            private readonly Dictionary<IntVec3, int> surveys = new Dictionary<IntVec3, int>();
            private readonly Dictionary<Pawn, IntVec3> goals;

            public long PathRequests, ReachChecks, Anchors, HistoryScans;

            public HistoricalSurvey(Dictionary<Pawn, IntVec3> goals) { this.goals = goals; }

            public void Clear() { cells.Clear(); ticks.Clear(); surveys.Clear(); }

            public void Record(IntVec3 cell, int tick)
            {
                surveys[cell] = tick;
                int index = cells.IndexOf(cell);
                if (index < 0) { cells.Add(cell); ticks.Add(tick); }
                else ticks[index] = tick;
            }

            public BenchNumbers Numbers() => new BenchNumbers
            {
                PathRequests = PathRequests,
                ReachChecks = ReachChecks,
                Anchors = Anchors,
                HistoryScans = HistoryScans,
            };

            public Job JobFor(Pawn dog, DogNetwork network)
            {
                if (dog == null || !dog.Spawned || network?.Map != dog.Map) return null;
                RobodogWork work = RobodogWork.Utility;
                if (work == null) return null;
                Map map = dog.Map;
                int spacing = work.surveySpacing;
                IntVec3 best = IntVec3.Invalid;
                long bestAge = long.MinValue;
                int bestDist = int.MaxValue;
                float radiusSquared = work.surveySearchRadius * work.surveySearchRadius;
                for (int x = spacing / 2; x < map.Size.x; x += spacing)
                for (int z = spacing / 2; z < map.Size.z; z += spacing)
                {
                    IntVec3 anchor = new IntVec3(x, 0, z);
                    if (anchor.DistanceToSquared(dog.Position) > radiusSquared) continue;
                    Anchors++;
                    IntVec3 cell = GoalNear(dog, anchor, spacing / 2f);
                    if (!cell.IsValid || cell == dog.Position || Taken(network, dog, cell, spacing)) continue;
                    long age = Age(cell, spacing);
                    int dist = cell.DistanceToSquared(dog.Position);
                    if (age < bestAge || age == bestAge && dist >= bestDist) continue;
                    if (!ReachableUnfogged(dog, cell, map)) continue;
                    best = cell; bestAge = age; bestDist = dist;
                }
                if (!best.IsValid) return null;
                Job job = JobMaker.MakeJob(QR_DefOf.QR_Survey, best);
                job.expiryInterval = work.surveyJobTicks;
                job.checkOverrideOnExpire = true;
                return job;
            }

            private IntVec3 GoalNear(Pawn dog, IntVec3 anchor, float radius)
            {
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(anchor, radius, useCenter: true))
                    if (cell.InBounds(dog.Map) && cell.Standable(dog.Map) && !cell.Fogged(dog.Map)
                        && !cell.IsForbidden(dog))
                    {
                        ReachChecks++;
                        if (dog.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)) return cell;
                    }
                return IntVec3.Invalid;
            }

            private bool Taken(DogNetwork network, Pawn dog, IntVec3 cell, int spacing)
            {
                foreach (Pawn other in network.Dogs)
                {
                    if (other == dog || !other.Spawned) continue;
                    if (other.CurJobDef == QR_DefOf.QR_Survey && other.CurJob.targetA.IsValid
                        && other.CurJob.targetA.Cell.DistanceToSquared(cell) < spacing * spacing) return true;
                    if (goals.TryGetValue(other, out IntVec3 goal)
                        && goal.DistanceToSquared(cell) < spacing * spacing) return true;
                }
                return false;
            }

            private long Age(IntVec3 cell, int spacing)
            {
                int newest = int.MinValue;
                int reach = spacing * spacing / 4;
                for (int i = 0; i < cells.Count; i++)
                {
                    HistoryScans++;
                    if (cells[i].DistanceToSquared(cell) <= reach && ticks[i] > newest) newest = ticks[i];
                }
                return newest == int.MinValue ? long.MaxValue : Find.TickManager.TicksGame - newest;
            }

            private bool ReachableUnfogged(Pawn dog, IntVec3 cell, Map map)
            {
                PathRequests++;
                using (PawnPath path = map.pathFinder.FindPathNow(dog.Position, cell, dog, peMode: PathEndMode.OnCell))
                {
                    if (!path.Found) return false;
                    foreach (IntVec3 node in path.NodesReversed)
                        if (map.fogGrid.IsFogged(node)) return false;
                    return true;
                }
            }
        }

        // ---------------------------------------------------------------- measurement primitives

        private struct BenchNumbers
        {
            public long PathRequests, ReachChecks, Anchors, HistoryScans;
            public long GeometryBuilds, GeometryChecks, GeometryElapsedTicks, GeometryAllocatedBytes, ObservedCells;
            public long LocalScanTicks, LocalScanBytes, ObservationTicks, ObservationBytes, ValidationTicks, ValidationBytes;
            public bool HasGeometry => GeometryBuilds >= 0;

            public static BenchNumbers FromSurvey(Survey survey) => new BenchNumbers
            {
                PathRequests = survey.PathRequests,
                ReachChecks = survey.ReachabilityChecks,
                GeometryBuilds = survey.GeometryBuilds,
                GeometryChecks = survey.GeometryChecks,
                GeometryElapsedTicks = survey.GeometryElapsedTicks,
                GeometryAllocatedBytes = survey.GeometryAllocatedBytes,
                ObservedCells = survey.ObservedCellCount,
                LocalScanTicks = survey.LocalScanElapsedTicks, LocalScanBytes = survey.LocalScanAllocatedBytes,
                ObservationTicks = survey.ObservationElapsedTicks, ObservationBytes = survey.ObservationAllocatedBytes,
                ValidationTicks = survey.ValidationElapsedTicks, ValidationBytes = survey.ValidationAllocatedBytes,
            };

            public BenchNumbers Delta(BenchNumbers before) => new BenchNumbers
            {
                PathRequests = Sub(PathRequests, before.PathRequests),
                ReachChecks = Sub(ReachChecks, before.ReachChecks),
                Anchors = Sub(Anchors, before.Anchors),
                HistoryScans = Sub(HistoryScans, before.HistoryScans),
                GeometryBuilds = Sub(GeometryBuilds, before.GeometryBuilds),
                GeometryChecks = Sub(GeometryChecks, before.GeometryChecks),
                GeometryElapsedTicks = Sub(GeometryElapsedTicks, before.GeometryElapsedTicks),
                GeometryAllocatedBytes = Sub(GeometryAllocatedBytes, before.GeometryAllocatedBytes),
                ObservedCells = Sub(ObservedCells, before.ObservedCells),
                LocalScanTicks = Sub(LocalScanTicks, before.LocalScanTicks), LocalScanBytes = Sub(LocalScanBytes, before.LocalScanBytes),
                ObservationTicks = Sub(ObservationTicks, before.ObservationTicks), ObservationBytes = Sub(ObservationBytes, before.ObservationBytes),
                ValidationTicks = Sub(ValidationTicks, before.ValidationTicks), ValidationBytes = Sub(ValidationBytes, before.ValidationBytes),
            };

            private static long Sub(long a, long b) => a < 0 || b < 0 ? -1 : a - b;
        }

        private sealed class BenchStats
        {
            public readonly List<double> Ms = new List<double>();
            public readonly List<long> Alloc = new List<long>();
            public void Add(double ms, long alloc) { Ms.Add(ms); Alloc.Add(alloc); }
            public double MedianMs => Percentile(Ms, 0.50);
            public double P95Ms => Percentile(Ms, 0.95);
            public double TailMs => Ms.Count == 0 ? 0 : Ms.Max();
            public double TotalMs => Ms.Sum();
            public long TotalAlloc => Alloc.Count == 0 || Alloc.Any(a => a < 0) ? -1 : Alloc.Sum();

            public static double Percentile(List<double> values, double fraction)
            {
                if (values.Count == 0) return 0;
                var sorted = values.OrderBy(v => v).ToList();
                int i = Math.Max(0, Math.Min(sorted.Count - 1, (int)Math.Ceiling(fraction * sorted.Count) - 1));
                return sorted[i];
            }
        }

        private sealed class BenchSample
        {
            public double Ms;
            public long Alloc = -1;
            public IntVec3 Goal;
            public string CountText() => Alloc < 0 ? "n/a" : $"{Alloc}B";
        }

        private static IntVec3 TargetCell(Job job)
        {
            if (job == null) return IntVec3.Invalid;
            if (job.targetA.HasThing && job.targetA.Thing != null) return job.targetA.Thing.PositionHeld;
            return job.targetA.Cell;
        }

        private sealed class BenchTarget
        {
            public readonly string Id;
            public readonly IntVec3 Cell;
            public readonly Thing Thing;
            public int EnteredTick = -1, DiscoveredTick = -1;
            public float DistanceAtDiscovery;
            public BenchTarget(string id, IntVec3 cell, Thing thing) { Id = id; Cell = cell; Thing = thing; }
            public int ScanLatency => EnteredTick < 0 || DiscoveredTick < 0 ? -1 : DiscoveredTick - EnteredTick;
        }

        private sealed class BenchPhase
        {
            public bool Baseline;
            public readonly BenchStats Select = new BenchStats();
            public readonly List<BenchTarget> Targets = new List<BenchTarget>();
            public BenchNumbers Before, Counters;
            public BenchNumbers RuntimeBefore, Runtime;
            public double ConstructionMs, ArrivalMs, StartJobMs;
            public long ConstructionAlloc, ArrivalAlloc, StartJobAlloc;
            public double TotalMs => Select.TotalMs + ArrivalMs + StartJobMs + (Baseline ? 0 : ConstructionMs)
                + SurveyBenchRunner.TicksToMs(Runtime.LocalScanTicks - (Baseline ? Runtime.ObservationTicks : 0)
                    + Runtime.ValidationTicks);
            public long TotalAlloc => Select.TotalAlloc < 0 ? -1 : Select.TotalAlloc + ArrivalAlloc + StartJobAlloc
                + (Baseline ? 0 : ConstructionAlloc) + Runtime.LocalScanBytes
                - (Baseline ? Runtime.ObservationBytes : 0) + Runtime.ValidationBytes;
            public int StartTick, EndTick, Selections, WalkedCells, VisitedCells;
            public float WalkedDistance;
            public long Signature;
            public string Selected = "";
            public int Observed = -1;
        }

        // ---------------------------------------------------------------- runner

        private sealed class SurveyBenchRunner
        {
            private static string allocationSource;
            private static readonly Func<long> GcAllocated = FindGcAllocated();
            private static readonly Stopwatch Watch = new Stopwatch();

            private readonly Scenario scenario;
            private readonly SurveyBenchFixture fixture;
            private readonly Map map;
            private readonly HistoricalSurvey historical;
            private readonly Dictionary<Pawn, IntVec3> goals = new Dictionary<Pawn, IntVec3>();
            private readonly List<Pawn> observers = new List<Pawn>();
            private readonly BenchStats baselineWarm = new BenchStats();
            private readonly BenchStats proposedWarm = new BenchStats();
            private readonly List<double> pairedDeltas = new List<double>();
            private readonly HashSet<IntVec3> visited = new HashSet<IntVec3>();

            private DogNetwork network;
            private Survey survey;
            private Func<long> priorAllocHook;
            private BenchSample baselineCold = new BenchSample(), proposedCold = new BenchSample();
            private BenchSample refresh = new BenchSample();
            private BenchNumbers baselineColdNumbers, baselineWarmNumbers;
            private BenchNumbers proposedColdNumbers, proposedWarmNumbers, refreshNumbers;
            private double networkBuildMs;
            private long networkBuildAlloc;
            private string candidateReport = "n/a";
            private string coldObserved = "n/a", proposedWarmObserved = "n/a";

            private Pawn dog;
            private BenchPhase phase;
            private BenchPhase baselinePhase, proposedPhase;
            private IntVec3 goal = IntVec3.Invalid;
            private int nextSelectTick, lastDiscoveryTick = -1;
            private int lastPathConsumed;
            private PawnPath lastPath;
            private IntVec3 lastPos;
            private float walkedDistance;
            private int walkedCells;
            private bool prepared, finished;
            private readonly Type surveyDriver;

            public SurveyBenchRunner(Scenario scenario, SurveyBenchFixture fixture)
            {
                this.scenario = scenario;
                this.fixture = fixture;
                map = Find.CurrentMap;
                if (GcAllocated == null)
                    throw new InvalidOperationException("no allocation counter passed a real 64 KiB allocation probe");
                if (map.mapPawns.AllPawns.Any(p => CompRobodog.Of(p) != null))
                    throw new InvalidOperationException("survey benchmark needs an isolated map without unrelated robodogs");
                surveyDriver = QR_DefOf.QR_Survey.driverClass;
                historical = new HistoricalSurvey(goals);
                priorAllocHook = Survey.AllocatedBytes;
                Survey.AllocatedBytes = GcAllocated;
                scenario.OnFinish(() =>
                {
                    if (survey != null) survey.Measure = false;
                    Survey.AllocatedBytes = priorAllocHook;
                    QR_DefOf.QR_Survey.driverClass = surveyDriver;
                    HistoricalArrival = null;
                });
            }

            public bool Step()
            {
                if (finished) return true;
                if (!prepared) { Prepare(); return false; }
                if (phase == null) { StartPhysical(true); return false; }
                if (!DrivePhysical()) return false;
                EndPhysical();
                if (phase.Baseline)
                {
                    baselinePhase = phase;
                    StartPhysical(false);
                    return false;
                }
                proposedPhase = phase;
                Emit();
                if (proposedPhase.Targets.Any(t => t.DiscoveredTick < 0))
                    throw new InvalidOperationException($"{fixture.Label}: proposed survey missed an eligible staged target");
                finished = true;
                return true;
            }

            private static Func<long> FindGcAllocated()
            {
                try
                {
                    MethodInfo method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread",
                        BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (method != null)
                    {
                        var counter = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
                        if (CounterWorks(counter))
                        {
                            allocationSource = "managed-current-thread";
                            return counter;
                        }
                    }
                }
                catch (NotSupportedException) { }
                // Unity's Boehm build exposes the managed method but returns zero. Its allocator's atomic
                // process-total counter is real GC pressure (including concurrent engine allocations), not a
                // per-thread managed-object-size estimate. The benchmark reports this scope explicitly.
                try
                {
                    if (CounterWorks(BoehmAllocated))
                    {
                        allocationSource = "boehm-process-total";
                        return BoehmAllocated;
                    }
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                return null;
            }

            private static bool CounterWorks(Func<long> counter)
            {
                long before = counter();
                var probe = new byte[64 * 1024];
                long after = counter();
                GC.KeepAlive(probe);
                return after - before >= probe.Length;
            }

            // Boehm's public GC_get_heap_usage_safe contract permits null output pointers. Only benchmark code
            // calls this Linux runtime export; normal gameplay never loads or depends on this profiling API.
            [DllImport("libmonobdwgc-2.0.so", EntryPoint = "GC_get_heap_usage_safe", CallingConvention = CallingConvention.Cdecl)]
            private static extern void HeapUsage(IntPtr heap, IntPtr free, IntPtr unmapped, IntPtr sinceGc, out UIntPtr total);

            private static long BoehmAllocated()
            {
                HeapUsage(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out UIntPtr total);
                return checked((long)total.ToUInt64());
            }

            private static long AllocatedNow() => GcAllocated();

            private void Prepare()
            {
                prepared = true;
                foreach (IntVec3 cell in fixture.Placements)
                {
                    Pawn observer = SpawnPawn(scenario, QR_DefOf.QR_Medirunner, Faction.OfPlayer, cell);
                    CompRobodog.Of(observer)?.SetBattery(1f);
                    observers.Add(observer);
                }
                CreateNetwork();
                // JIT-warm both selectors, then discard the warmed runtime geometry and history.
                ReturnPool(historical.JobFor(observers[0], network));
                ReturnPool(survey.JobFor(observers[0]));
                CreateNetwork();
                historical.Clear();
                RunInterleavedSelection();
                CandidateReport();
                for (int i = 1; i < observers.Count; i++)
                    observers[i].Destroy(DestroyMode.Vanish);
                observers.RemoveRange(1, observers.Count - 1);
            }

            private void CreateNetwork()
            {
                DogNetwork.ClearAll();
                long allocBefore = AllocatedNow();
                Watch.Restart();
                network = DogNetwork.For(map);
                Watch.Stop();
                long allocAfter = AllocatedNow();
                networkBuildMs = Watch.Elapsed.TotalMilliseconds;
                networkBuildAlloc = allocBefore >= 0 && allocAfter >= 0 ? allocAfter - allocBefore : -1;
                survey = network.Survey;
                Survey.AllocatedBytes = GcAllocated;
                _ = network.State;
            }

            private void RunInterleavedSelection()
            {
                survey.Measure = true;
                BenchNumbers baselineAll = historical.Numbers();
                baselineCold = Measure(observers[0], true, out Job baseJob);
                ReturnPool(baseJob);
                baselineColdNumbers = historical.Numbers().Delta(baselineAll);

                BenchNumbers propBefore = BenchNumbers.FromSurvey(survey);
                proposedCold = Measure(observers[0], false, out Job propJob);
                ReturnPool(propJob);
                proposedColdNumbers = BenchNumbers.FromSurvey(survey).Delta(propBefore);
                coldObserved = survey.ObservedCellCount.ToString(CultureInfo.InvariantCulture);

                baselineAll = historical.Numbers();
                propBefore = BenchNumbers.FromSurvey(survey);
                for (int i = 0; i < WarmIterations; i++)
                {
                    Pawn observer = observers[i % observers.Count];
                    BenchSample b = Measure(observer, true, out Job bj);
                    ReturnPool(bj);
                    baselineWarm.Add(b.Ms, b.Alloc);
                    BenchSample p = Measure(observer, false, out Job pj);
                    ReturnPool(pj);
                    proposedWarm.Add(p.Ms, p.Alloc);
                    pairedDeltas.Add(p.Ms - b.Ms);
                }
                baselineWarmNumbers = historical.Numbers().Delta(baselineAll);
                proposedWarmNumbers = BenchNumbers.FromSurvey(survey).Delta(propBefore);
                proposedWarmObserved = survey.ObservedCellCount.ToString(CultureInfo.InvariantCulture);

                // A real topology edit, not a synthetic flag: the wall is spawned after the selector is warm, the
                // runtime's own event marks geometry dirty, and Refresh is timed; then the wall is removed and
                // geometry restored so both physical phases see the original fixture.
                if (fixture.RefreshCell.IsValid)
                {
                    Thing wall = SpawnBuilding(scenario, ThingDefOf.Wall, ThingDefOf.Steel, fixture.RefreshCell, Rot4.North);
                    BenchNumbers editBefore = BenchNumbers.FromSurvey(survey);
                    long allocBefore = AllocatedNow();
                    Watch.Restart();
                    survey.Refresh();
                    Watch.Stop();
                    long allocAfter = AllocatedNow();
                    refresh = new BenchSample
                    {
                        Ms = Watch.Elapsed.TotalMilliseconds,
                        Alloc = allocBefore >= 0 && allocAfter >= 0 ? allocAfter - allocBefore : -1,
                    };
                    refreshNumbers = BenchNumbers.FromSurvey(survey).Delta(editBefore);
                    wall.Destroy();
                    survey.Refresh();
                }
                survey.Measure = false;
            }

            private BenchSample Measure(Pawn observer, bool baseline, out Job job)
            {
                long allocBefore = AllocatedNow();
                Watch.Restart();
                job = baseline ? historical.JobFor(observer, network) : survey.JobFor(observer);
                Watch.Stop();
                long allocAfter = AllocatedNow();
                return new BenchSample
                {
                    Ms = Watch.Elapsed.TotalMilliseconds,
                    Alloc = allocBefore >= 0 && allocAfter >= 0 ? allocAfter - allocBefore : -1,
                    Goal = TargetCell(job),
                };
            }

            private void CandidateReport()
            {
                var parts = new List<string>();
                foreach (IntVec3 probe in fixture.ProbeAreas.Distinct())
                {
                    List<IntVec3> candidates = survey.CandidateCells(probe).ToList();
                    var costs = new List<float>();
                    foreach (IntVec3 candidate in candidates)
                        using (PawnPath path = map.pathFinder.FindPathNow(fixture.DogStart, candidate,
                                   observers[0], peMode: PathEndMode.OnCell))
                            if (path.Found) costs.Add(path.TotalCost);
                    string cost = costs.Count == 0 ? "unreachable"
                        : $"min {costs.Min():F1} med {BenchStats.Percentile(costs.Select(c => (double)c).ToList(), 0.5):F1} max {costs.Max():F1}";
                    parts.Add($"{probe} [{survey.CategoryAt(probe)}]: {candidates.Count} candidates, {cost}");
                }
                candidateReport = string.Join("; ", parts);
            }

            private void StartPhysical(bool baseline)
            {
                if (phase != null)
                    foreach (BenchTarget target in phase.Targets)
                        if (target.Thing != null && !target.Thing.Destroyed)
                            target.Thing.Destroy();
                if (dog != null) dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                dog = observers[0];
                goal = IntVec3.Invalid;
                goals.Clear();
                historical.Clear();
                visited.Clear();
                walkedCells = 0;
                walkedDistance = 0;
                lastPath = null;
                lastPathConsumed = 0;
                lastPos = dog.Position;
                Teleport(dog, fixture.DogStart);
                lastPos = dog.Position;
                lastDiscoveryTick = -1;
                nextSelectTick = 0;

                CreateNetwork();
                survey.Measure = true;
                phase = new BenchPhase { Baseline = baseline, Signature = BenchGeometrySignature(map, fixture.Site),
                    ConstructionMs = networkBuildMs, ConstructionAlloc = networkBuildAlloc };
                HistoricalArrival = baseline ? (Action<Pawn>)RecordHistoricalArrival : null;
                QR_DefOf.QR_Survey.driverClass = baseline ? typeof(JobDriver_HistoricalSurveyBenchmark) : surveyDriver;
                if (phase.Signature != fixture.Signature)
                    throw new InvalidOperationException(
                        $"{fixture.Label}: geometry changed before {(baseline ? "baseline" : "proposed")} phase " +
                        $"({fixture.Signature:X} -> {phase.Signature:X})");
                foreach (IntVec3 cell in fixture.TargetCells)
                {
                    Thing thing = NetStack(scenario, ThingDefOf.Steel, 30, cell);
                    phase.Targets.Add(new BenchTarget(thing.ThingID, cell, thing));
                }
                phase.Before = baseline ? historical.Numbers() : BenchNumbers.FromSurvey(survey);
                phase.RuntimeBefore = BenchNumbers.FromSurvey(survey);
                phase.StartTick = Find.TickManager.TicksGame;
                nextSelectTick = phase.StartTick;
                phase.VisitedCells = 1;
                visited.Add(dog.Position);
            }

            private bool DrivePhysical()
            {
                int now = Find.TickManager.TicksGame;
                // Keep the benchmark observer on station in both phases; battery drain is not the subject and the
                // runtime would otherwise power it down or send it charging during long physical runs.
                CompRobodog.Of(dog)?.SetBattery(1f);
                PollTargets(now);
                TrackWalked();
                if (phase.Baseline) DriveHistorical(now);
                else DriveProposed(now);
                TrackWalked();

                if (phase.Targets.Count > 0 && phase.Targets.All(t => t.DiscoveredTick >= 0))
                {
                    if (lastDiscoveryTick < 0) lastDiscoveryTick = now;
                    if (now - lastDiscoveryTick >= 120) return true;
                }
                if (phase.Selections >= MaxSelections || now - phase.StartTick >= PhysicalTicks) return true;
                return false;
            }

            private void DriveHistorical(int now)
            {
                if (goal.IsValid)
                {
                    bool ended = dog.CurJob == null || dog.CurJobDef != QR_DefOf.QR_Survey
                        || !dog.CurJob.targetA.IsValid || dog.CurJob.targetA.Cell != goal;
                    if (ended)
                    {
                        goal = IntVec3.Invalid;
                        nextSelectTick = now;
                    }
                }
                if (goal.IsValid || now < nextSelectTick || phase.Selections >= MaxSelections) return;
                BenchSample sample = Measure(observers[0], true, out Job job);
                if (job == null || !sample.Goal.IsValid)
                {
                    phase.Select.Add(sample.Ms, sample.Alloc);
                    phase.Selections++;
                    nextSelectTick = now + RobodogWork.Utility.dispatchInterval;
                    return;
                }
                goal = sample.Goal;
                goals[dog] = goal;
                phase.Selected = $"goal {goal}";
                phase.Select.Add(sample.Ms, sample.Alloc);
                phase.Selections++;
                StartMeasuredJob(job);
            }

            private void RecordHistoricalArrival(Pawn observer)
            {
                long bytes = AllocatedNow(), started = Stopwatch.GetTimestamp();
                historical.Record(observer.Position, Find.TickManager.TicksGame);
                phase.ArrivalMs += TicksToMs(Stopwatch.GetTimestamp() - started);
                if (bytes >= 0) phase.ArrivalAlloc += AllocatedNow() - bytes;
            }

            private void DriveProposed(int now)
            {
                if (dog.CurJobDef == QR_DefOf.QR_Survey) return;
                if (now < nextSelectTick || phase.Selections >= MaxSelections) return;
                BenchSample sample = Measure(observers[0], false, out Job job);
                phase.Select.Add(sample.Ms, sample.Alloc);
                phase.Selections++;
                if (job == null || !sample.Goal.IsValid)
                {
                    nextSelectTick = now + RobodogWork.Utility.dispatchInterval;
                    return;
                }
                goal = sample.Goal;
                phase.Selected = $"area {survey.AreaKey(dog)}, category {survey.CategoryAt(goal)}, sweep {survey.Sweep}";
                StartMeasuredJob(job);
            }

            private void StartMeasuredJob(Job job)
            {
                long validation = survey.ValidationElapsedTicks, validationBytes = survey.ValidationAllocatedBytes;
                double arrivalMs = phase.ArrivalMs;
                long arrivalAlloc = phase.ArrivalAlloc;
                long bytes = AllocatedNow();
                Watch.Restart();
                dog.jobs.StartJob(job, JobCondition.InterruptForced);
                Watch.Stop();
                phase.StartJobMs += Watch.Elapsed.TotalMilliseconds - TicksToMs(survey.ValidationElapsedTicks - validation)
                    - (phase.ArrivalMs - arrivalMs);
                if (bytes >= 0) phase.StartJobAlloc += AllocatedNow() - bytes
                    - (survey.ValidationAllocatedBytes - validationBytes) - (phase.ArrivalAlloc - arrivalAlloc);
            }

            private void PollTargets(int now)
            {
                float scanRadius = RobodogWork.Utility.scanRadius;
                foreach (BenchTarget target in phase.Targets)
                {
                    if (target.Thing == null || !target.Thing.Spawned || target.Thing.Destroyed) continue;
                    if (target.EnteredTick < 0 && dog.Position.DistanceTo(target.Cell) <= scanRadius)
                        target.EnteredTick = now;
                    if (target.DiscoveredTick >= 0) continue;
                    if (network.NormalQueue.Any(t => t.targetId == target.Id))
                    {
                        target.DiscoveredTick = now;
                        target.DistanceAtDiscovery = dog.Position.DistanceTo(target.Cell);
                        lastDiscoveryTick = now;
                    }
                }
            }

            private void TrackWalked()
            {
                PawnPath path = dog.pather.curPath;
                if (path != null)
                {
                    if (!ReferenceEquals(path, lastPath))
                    {
                        walkedCells += Math.Max(0, path.NodesConsumedCount);
                        lastPath = path;
                        lastPathConsumed = path.NodesConsumedCount;
                    }
                    else
                    {
                        int delta = path.NodesConsumedCount - lastPathConsumed;
                        if (delta > 0) walkedCells += delta;
                        lastPathConsumed = path.NodesConsumedCount;
                    }
                }
                IntVec3 pos = dog.Position;
                if (lastPos.IsValid && pos != lastPos)
                {
                    walkedDistance += pos.DistanceTo(lastPos);
                    visited.Add(pos);
                }
                lastPos = pos;
            }

            private void EndPhysical()
            {
                phase.EndTick = Find.TickManager.TicksGame;
                phase.Runtime = BenchNumbers.FromSurvey(survey).Delta(phase.RuntimeBefore);
                survey.Measure = false;
                if (phase.Baseline)
                    phase.Counters = historical.Numbers().Delta(phase.Before);
                else
                {
                    survey.Measure = false;
                    phase.Counters = BenchNumbers.FromSurvey(survey).Delta(phase.Before);
                    phase.Observed = survey.ObservedCellCount;
                }
                phase.WalkedCells = walkedCells;
                phase.WalkedDistance = walkedDistance;
                phase.VisitedCells = visited.Count;
            }

            private void Emit()
            {
                string baseTargets = TargetsText(baselinePhase);
                string propTargets = TargetsText(proposedPhase);
                double pairedMedian = BenchStats.Percentile(pairedDeltas, 0.5);
                double pairedP95 = BenchStats.Percentile(pairedDeltas, 0.95);
                if (AutoTest.Enabled)
                    AutoTest.Event("survey_bench",
                        ("fixture", fixture.Label),
                        ("geometry", fixture.GeometryNote),
                        ("allocation_source", allocationSource),
                        ("signature", fixture.Signature.ToString("X", CultureInfo.InvariantCulture)),
                        ("geometry_reused", baselinePhase.Signature == proposedPhase.Signature),
                        ("network_build_ms", Round(networkBuildMs)),
                        ("network_build_alloc", networkBuildAlloc),
                        ("baseline_cold_ms", Round(baselineCold.Ms)),
                        ("baseline_cold_alloc", baselineCold.Alloc),
                        ("proposed_cold_ms", Round(proposedCold.Ms)),
                        ("proposed_cold_alloc", proposedCold.Alloc),
                        ("baseline_warm_n", baselineWarm.Ms.Count),
                        ("baseline_warm_median_ms", Round(baselineWarm.MedianMs)),
                        ("baseline_warm_p95_ms", Round(baselineWarm.P95Ms)),
                        ("baseline_warm_tail_ms", Round(baselineWarm.TailMs)),
                        ("baseline_warm_total_ms", Round(baselineWarm.TotalMs)),
                        ("baseline_warm_alloc", baselineWarm.TotalAlloc),
                        ("proposed_warm_median_ms", Round(proposedWarm.MedianMs)),
                        ("proposed_warm_p95_ms", Round(proposedWarm.P95Ms)),
                        ("proposed_warm_tail_ms", Round(proposedWarm.TailMs)),
                        ("proposed_warm_total_ms", Round(proposedWarm.TotalMs)),
                        ("proposed_warm_alloc", proposedWarm.TotalAlloc),
                        ("paired_delta_median_ms", Round(pairedMedian)),
                        ("paired_delta_p95_ms", Round(pairedP95)),
                        ("refresh_ms", Round(refresh.Ms)),
                        ("refresh_alloc", refresh.Alloc),
                        ("geometry_builds", refreshNumbers.GeometryBuilds),
                        ("geometry_checks", refreshNumbers.GeometryChecks),
                        ("geometry_ms", Round(TicksToMs(refreshNumbers.GeometryElapsedTicks))),
                        ("geometry_alloc", refreshNumbers.GeometryAllocatedBytes),
                        ("cold_observed_cells", coldObserved),
                        ("warm_observed_cells", proposedWarmObserved),
                        ("baseline_paths", baselineColdNumbers.PathRequests),
                        ("baseline_reach", baselineColdNumbers.ReachChecks),
                        ("baseline_warm_paths", baselineWarmNumbers.PathRequests),
                        ("proposed_paths", proposedColdNumbers.PathRequests),
                        ("proposed_reach", proposedColdNumbers.ReachChecks),
                        ("proposed_warm_paths", proposedWarmNumbers.PathRequests),
                        ("baseline_phase", PhaseEvent(baselinePhase)),
                        ("proposed_phase", PhaseEvent(proposedPhase)),
                        ("baseline_total_ms", Round(baselinePhase.TotalMs)), ("baseline_total_alloc", baselinePhase.TotalAlloc),
                        ("proposed_total_ms", Round(proposedPhase.TotalMs)), ("proposed_total_alloc", proposedPhase.TotalAlloc),
                        ("baseline_scan_ms", Round(TicksToMs(baselinePhase.Runtime.LocalScanTicks - baselinePhase.Runtime.ObservationTicks))),
                        ("proposed_scan_ms", Round(TicksToMs(proposedPhase.Runtime.LocalScanTicks))),
                        ("proposed_validation_ms", Round(TicksToMs(proposedPhase.Runtime.ValidationTicks))),
                        ("baseline_targets", baseTargets),
                        ("proposed_targets", propTargets),
                        ("candidates", candidateReport));
                QR.Log($"SURVEY BENCH {fixture.Label}: signed {fixture.Signature:X}, reused " +
                    $"{baselinePhase.Signature == proposedPhase.Signature}; select baseline med {baselineWarm.MedianMs:F2}/" +
                    $"p95 {baselineWarm.P95Ms:F2}/tail {baselineWarm.TailMs:F2} ms vs proposed {proposedWarm.MedianMs:F2}/" +
                    $"{proposedWarm.P95Ms:F2}/{proposedWarm.TailMs:F2} ms (cold {baselineCold.Ms:F2} vs " +
                    $"{proposedCold.Ms:F2} ms); physical baseline {PhaseText(baselinePhase)}; proposed " +
                    $"{PhaseText(proposedPhase)}; targets [{baseTargets}] vs [{propTargets}]; candidates [{candidateReport}]");
            }

            private static string PhaseEvent(BenchPhase phase) =>
                $"selections {phase.Selections}, ticks {phase.EndTick - phase.StartTick}, walked {phase.WalkedCells} " +
                $"({phase.WalkedDistance:F1} straight, {phase.VisitedCells} visited), select " +
                $"{phase.Select.MedianMs:F2}/{phase.Select.P95Ms:F2}/{phase.Select.TailMs:F2} ms total " +
                $"{phase.Select.TotalMs:F1} ms, alloc " +
                $"{phase.Select.TotalAlloc} B, paths {phase.Counters.PathRequests}, reach {phase.Counters.ReachChecks}, " +
                $"geometry {phase.Counters.GeometryBuilds}/{TicksToMs(phase.Counters.GeometryElapsedTicks):F2} ms/" +
                $"{phase.Counters.GeometryAllocatedBytes} B, observed {phase.Observed}, {phase.Selected}";

            private static string PhaseText(BenchPhase phase) =>
                $"{phase.Selections} sel, {phase.EndTick - phase.StartTick} ticks, {phase.WalkedCells} walked, " +
                $"med {phase.Select.MedianMs:F2}/tail {phase.Select.TailMs:F2} ms, {phase.Select.TotalAlloc} B, " +
                $"paths {phase.Counters.PathRequests}, geometry {phase.Counters.GeometryBuilds}/{phase.Counters.GeometryElapsedTicks} ticks";

            private static string TargetsText(BenchPhase phase) => string.Join(" | ", phase.Targets.Select(t =>
                $"{t.Cell}: {(t.DiscoveredTick < 0 ? "missed" : $"d+{t.DiscoveredTick - phase.StartTick}")}" +
                $", enter {(t.EnteredTick < 0 ? "no" : (t.EnteredTick - phase.StartTick).ToString(CultureInfo.InvariantCulture))}" +
                $", latency {(t.ScanLatency < 0 ? "n/a" : t.ScanLatency + "t")}" +
                $", dist {t.DistanceAtDiscovery:F1}"));

            public string Summary()
            {
                string baseTargets = baselinePhase == null ? "n/a" : TargetsText(baselinePhase);
                string propTargets = proposedPhase == null ? "n/a" : TargetsText(proposedPhase);
                return $"{fixture.Label}: {fixture.GeometryNote}; signature reused " +
                    $"{(baselinePhase != null && proposedPhase != null && baselinePhase.Signature == proposedPhase.Signature)}; " +
                    $"cold {baselineCold.Ms:F2}/{baselineCold.Alloc}B vs {proposedCold.Ms:F2}/{proposedCold.Alloc}B; " +
                    $"warm med {baselineWarm.MedianMs:F2}/{proposedWarm.MedianMs:F2} ms, p95 " +
                    $"{baselineWarm.P95Ms:F2}/{proposedWarm.P95Ms:F2}, tail {baselineWarm.TailMs:F2}/{proposedWarm.TailMs:F2}; " +
                    $"refresh {refresh.Ms:F2} ms, geometry {refreshNumbers.GeometryBuilds} builds " +
                    $"{TicksToMs(refreshNumbers.GeometryElapsedTicks):F2} ms/{refreshNumbers.GeometryAllocatedBytes}B; " +
                    $"baseline [{baseTargets}]; proposed [{propTargets}]";
            }

            private static string Round(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
            internal static double TicksToMs(long ticks) => ticks < 0 ? -1 : ticks * 1000.0 / Stopwatch.Frequency;

            private static void ReturnPool(Job job)
            {
                if (job != null) JobMaker.ReturnToPool(job);
            }
        }
    }

    /// <summary>Exact pre-cutover travel/arrival toils; only the isolated benchmark temporarily selects this driver.</summary>
    public sealed class JobDriver_HistoricalSurveyBenchmark : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            go.AddPreTickAction(() =>
            {
                if (pawn.pather.Moving && pawn.Map.fogGrid.IsFogged(pawn.pather.nextCell)) ReadyForNextToil();
            });
            yield return go;
            Toil arrival = ToilMaker.MakeToil("HistoricalSurveyArrival");
            arrival.initAction = () => Scenarios.HistoricalArrival(pawn);
            arrival.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return arrival;
        }
    }
}
