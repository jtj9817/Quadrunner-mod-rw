using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using Verse;

namespace Quadrunner
{
    public sealed partial class Survey
    {
        // Save/load clears Map's reference fields before stale networks are pruned. Keep the actual publisher.
        private readonly MapEvents events;
        private Area[] owners, nextOwners;
        private bool[] standable;
        private int[] planned;
        private int planStamp;
        private readonly object homeKey = new object(), outsideKey = new object();
        private readonly Dictionary<(object key, SurveyCategory category), Area> byKey =
            new Dictionary<(object key, SurveyCategory category), Area>();
        private readonly List<Area> areas = new List<Area>();
        private readonly Dictionary<(int tile, Region region), IntVec3> tiles = new Dictionary<(int tile, Region region), IntVec3>();
        private readonly HashSet<IntVec3> candidateSet = new HashSet<IntVec3>();
        private bool dirty = true, built;
        private int refreshAt;
        private static readonly IReadOnlyList<IntVec3> noCandidates = Array.Empty<IntVec3>();

        private void InitializeGeometry()
        {
            int count = map.cellIndices.NumGridCells;
            owners = new Area[count]; nextOwners = new Area[count];
            standable = new bool[count]; planned = new int[count];
            events.RegionsRoomsChanged += Dirty;
            events.RoofChanged += DirtyCell;
            events.CellFogChanged += FogChanged;
            events.MapFogged += MapFogged;
            events.ThingSpawned += ThingChanged;
            events.ThingDespawned += ThingChanged;
            events.TerrainChanged += TerrainChanged;
        }

        public void Dispose()
        {
            events.RegionsRoomsChanged -= Dirty;
            events.RoofChanged -= DirtyCell;
            events.CellFogChanged -= FogChanged;
            events.MapFogged -= MapFogged;
            events.ThingSpawned -= ThingChanged;
            events.ThingDespawned -= ThingChanged;
            events.TerrainChanged -= TerrainChanged;
        }

        private void Dirty() => dirty = true;
        private void DirtyCell(IntVec3 cell) => dirty = true;
        private void ThingChanged(Thing thing)
        {
            if (thing is Building_Storage || thing.def.passability != Traversability.Standable) dirty = true;
        }
        private void TerrainChanged(IntVec3 cell) { dirty = true; Forget(cell); }
        private void FogChanged(IntVec3 cell, bool fogged) { dirty = true; Forget(cell); }
        private void MapFogged() { Clear(); }
        private void Forget(IntVec3 cell)
        {
            int index = map.cellIndices.CellToIndex(cell);
            if (!observed[index]) return;
            observed[index] = false; observedCount--; observationVersion++;
            if (owners[index] != null) owners[index].remaining++;
        }

        public SurveyCategory CategoryAt(IntVec3 cell)
        {
            Refresh();
            return cell.InBounds(map) && owners[map.cellIndices.CellToIndex(cell)] is Area area
                ? area.category : SurveyCategory.Outside;
        }

        public IReadOnlyList<IntVec3> CandidateCells(IntVec3 areaCell)
        {
            Refresh();
            if (!areaCell.InBounds(map) || !(owners[map.cellIndices.CellToIndex(areaCell)] is Area area)) return noCandidates;
            EnsureCandidates(area);
            return area.candidates;
        }

        /// <summary>Topology events invalidate immediately. Bounded polling compares exact effective Home/zone/slot
        /// ownership without rebuilding unchanged geometry or relying on counts, hashes or saved room IDs. Live route
        /// checks handle temporary occupancy/forbidden changes; rejected routes are reconsidered on each poll.</summary>
        public void Refresh()
        {
            if (!dirty && built && Now < refreshAt) return;
            long start = Measure ? Stopwatch.GetTimestamp() : 0;
            long bytes = Measure && AllocatedBytes != null ? AllocatedBytes() : 0;
            if (Measure) GeometryChecks++;
            RevalidateObservers();
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            if (!dirty && built && !EditableInputsChanged())
            {
                refreshAt = Now + Tuning.surveyJobTicks;
                RecordGeometryCost(start, bytes);
                return;
            }
            dirty = false; refreshAt = Now + Tuning.surveyJobTicks;
            Array.Clear(nextOwners, 0, nextOwners.Length);
            foreach (Area area in areas) area.live = false;

            foreach (Room room in map.regionGrid.AllRooms)
            {
                if (!room.ProperRoom || room.IsDoorway || room.TouchesMapEdge || room.OutdoorsForWork) continue;
                Area area = GetArea(room, SurveyCategory.Room);
                foreach (IntVec3 cell in room.Cells) AddCell(area, cell, homeOnly: true);
            }
            foreach (SlotGroup group in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (!IsStorage(group.parent)) continue;
                Area area = GetArea(group.parent, SurveyCategory.Storage);
                foreach (IntVec3 cell in group.CellsList) AddCell(area, cell, homeOnly: true);
            }
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                if (zone is Zone_Stockpile) continue;
                Area area = GetArea(zone, zone is Zone_Growing ? SurveyCategory.Growing : SurveyCategory.OtherZone);
                foreach (IntVec3 cell in zone.cells) AddCell(area, cell, homeOnly: true);
            }
            // Residual Home/exterior footprints stay separated by native room topology. An observer outside a sealed
            // courtyard cannot be its only declared candidate just because its scan disk geometrically overlaps it.
            foreach (Room room in map.regionGrid.AllRooms)
            {
                Area residualHome = null, residualOutside = null;
                foreach (IntVec3 cell in room.Cells)
                {
                    int index = map.cellIndices.CellToIndex(cell);
                    if (nextOwners[index] != null || cell.Fogged(map) || !cell.Walkable(map)) continue;
                    nextOwners[index] = map.areaManager.Home[index]
                        ? residualHome ?? (residualHome = GetArea(room, SurveyCategory.Home))
                        : residualOutside ?? (residualOutside = GetArea(room, SurveyCategory.Outside));
                }
            }
            bool changed = !built;
            Area fallbackHome = null, fallbackOutside = null;
            for (int i = 0; i < nextOwners.Length; i++)
            {
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                bool visible = !cell.Fogged(map);
                if (nextOwners[i] == null && visible && cell.Walkable(map))
                    nextOwners[i] = map.areaManager.Home[i]
                        ? fallbackHome ?? (fallbackHome = GetArea(homeKey, SurveyCategory.Home))
                        : fallbackOutside ?? (fallbackOutside = GetArea(outsideKey, SurveyCategory.Outside));
                bool canStand = visible && cell.Standable(map);
                if (owners[i] != nextOwners[i] || standable[i] != canStand) changed = true;
                standable[i] = canStand;
                // A newly opened/revealed cell has not been seen in its current accessible form. Initial construction
                // is different: scans may legitimately precede the first geometry query and must be retained.
                if (built && owners[i] == null && nextOwners[i] != null) Forget(cell);
            }
            if (changed)
            {
                if (Measure) GeometryBuilds++;
                revision++;
                Area[] previous = owners; owners = nextOwners; nextOwners = previous;
                foreach (Area area in areas)
                {
                    area.cells.Clear(); area.candidates.Clear(); area.remaining = 0; area.candidatesReady = false;
                }
                for (int i = 0; i < owners.Length; i++)
                {
                    Area area = owners[i];
                    if (area == null) continue;
                    area.live = true; area.cells.Add(i);
                    if (!observed[i]) area.remaining++;
                }
                for (int i = areas.Count - 1; i >= 0; i--)
                    if (!areas[i].live)
                    {
                        byKey.Remove((areas[i].key, areas[i].category));
                        areas.RemoveAt(i);
                    }
            }
            else
                foreach (Area area in areas) area.live = area.cells.Count != 0;
            built = true;
            RecordGeometryCost(start, bytes);
        }

        private void RecordGeometryCost(long start, long bytes)
        {
            if (!Measure) return;
            GeometryElapsedTicks += Stopwatch.GetTimestamp() - start;
            if (AllocatedBytes != null) GeometryAllocatedBytes += AllocatedBytes() - bytes;
        }

        private void RevalidateObservers()
        {
            foreach (var pair in explorers)
                if (pair.Key.Dead || pair.Key.Destroyed || pair.Key.MapHeld != map) retiredObservers.Add(pair.Key);
                else pair.Value.rejected.Clear();
            foreach (Pawn pawn in retiredObservers) explorers.Remove(pawn);
            retiredObservers.Clear();
        }

        private static bool IsStorage(object parent) =>
            parent is Zone_Stockpile || parent is Building_Storage storage && storage.Faction == Faction.OfPlayer;

        private bool EditableInputsChanged()
        {
            var home = map.areaManager.Home;
            for (int i = 0; i < owners.Length; i++)
            {
                Area prior = owners[i];
                bool inHome = home[i];
                if (prior != null && inHome != (prior.category != SurveyCategory.Outside)) return true;
                if (!inHome || prior?.category == SurveyCategory.Room) continue;
                IntVec3 cell = map.cellIndices.IndexToCell(i);
                if (cell.Fogged(map)) continue;
                object key;
                SurveyCategory category;
                SlotGroup storage = map.haulDestinationManager.SlotGroupAt(cell);
                if (IsStorage(storage?.parent))
                {
                    key = storage.parent; category = SurveyCategory.Storage;
                }
                else if (cell.GetZone(map) is Zone zone && !(zone is Zone_Stockpile))
                {
                    key = zone; category = zone is Zone_Growing ? SurveyCategory.Growing : SurveyCategory.OtherZone;
                }
                else
                {
                    if (prior == null) continue;
                    key = prior.key; category = SurveyCategory.Home;
                }
                if (prior == null || prior.key != key || prior.category != category) return true;
            }
            return false;
        }

        private Area GetArea(object key, SurveyCategory category)
        {
            if (!byKey.TryGetValue((key, category), out Area area))
            {
                area = new Area { key = key, category = category };
                byKey.Add((key, category), area); areas.Add(area);
            }
            return area;
        }

        private void AddCell(Area area, IntVec3 cell, bool homeOnly)
        {
            if (!cell.InBounds(map) || cell.Fogged(map) || homeOnly && !map.areaManager.Home[cell]) return;
            int index = map.cellIndices.CellToIndex(cell);
            if (nextOwners[index] == null) nextOwners[index] = area;
        }

        private void EnsureCandidates(Area area)
        {
            if (area.candidatesReady) return;
            long started = Measure ? Stopwatch.GetTimestamp() : 0;
            long bytes = Measure && AllocatedBytes != null ? AllocatedBytes() : 0;
            BuildCandidates(area);
            area.candidatesReady = true;
            if (!Measure) return;
            CandidateBuilds++;
            CandidateElapsedTicks += Stopwatch.GetTimestamp() - started;
            if (AllocatedBytes != null) CandidateAllocatedBytes += AllocatedBytes() - bytes;
        }

        private void BuildCandidates(Area area)
        {
            tiles.Clear(); candidateSet.Clear();
            int spacing = Tuning.surveySpacing;
            int columns = (map.Size.x + spacing - 1) / spacing;
            Region[] regions = map.regionGrid.DirectGrid;
            foreach (int index in area.cells)
            {
                if (!standable[index]) continue;
                IntVec3 cell = map.cellIndices.IndexToCell(index);
                int tile = cell.x / spacing + cell.z / spacing * columns;
                IntVec3 center = new IntVec3(cell.x / spacing * spacing + spacing / 2, 0, cell.z / spacing * spacing + spacing / 2);
                var bucket = (tile, regions[index]);
                if (!tiles.TryGetValue(bucket, out IntVec3 prior) || cell.DistanceToSquared(center) < prior.DistanceToSquared(center))
                    tiles[bucket] = cell;
                // Actual door-adjacent interior positions are declared alternatives, not just one centroid that could
                // hide the cheaper entrance on the other side of an irregular room.
                foreach (IntVec3 offset in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacent = cell + offset;
                    if (adjacent.InBounds(map) && adjacent.GetDoor(map) != null) { candidateSet.Add(cell); break; }
                }
            }
            foreach (IntVec3 point in tiles.Values) candidateSet.Add(point);
            if (++planStamp == int.MaxValue) { Array.Clear(planned, 0, planned.Length); planStamp = 1; }
            foreach (IntVec3 point in candidateSet) MarkPlanned(point);
            foreach (int index in area.cells)
            {
                if (planned[index] == planStamp) continue;
                IntVec3 cell = map.cellIndices.IndexToCell(index), point = IntVec3.Invalid;
                if (standable[index]) point = cell;
                else
                {
                    int count = GenRadial.NumCellsInRadius(Tuning.scanRadius);
                    for (int n = 0; n < count; n++)
                    {
                        IntVec3 nearby = cell + GenRadial.RadialPattern[n];
                        if (nearby.InBounds(map) && standable[map.cellIndices.CellToIndex(nearby)]) { point = nearby; break; }
                    }
                }
                // A completely sealed solid footprint has no observation position. It is not credited as scanned;
                // native geometry changes will regenerate candidates when a position becomes available.
                if (!point.IsValid) continue;
                if (candidateSet.Add(point)) MarkPlanned(point);
            }
            area.candidates.AddRange(candidateSet);
            area.candidates.Sort(cellComparison);
        }

        private void MarkPlanned(IntVec3 point)
        {
            int count = GenRadial.NumCellsInRadius(Tuning.scanRadius);
            for (int n = 0; n < count; n++)
            {
                IntVec3 cell = point + GenRadial.RadialPattern[n];
                if (cell.InBounds(map)) planned[map.cellIndices.CellToIndex(cell)] = planStamp;
            }
        }
    }
}
