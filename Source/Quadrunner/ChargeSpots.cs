using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>A cell where a dog lies to charge from <see cref="conduit"/>'s power net.</summary>
    public class ChargeSpot
    {
        public IntVec3 cell;
        public Thing conduit;

        public float StoredEnergy => conduit.TryGetComp<CompPower>()?.PowerNet?.CurrentStoredEnergy() ?? 0f;

        public override string ToString() => $"{cell} by {conduit}";
    }

    /// <summary>
    /// The conduit charge spots of each map (design § Conduit charging). The cache holds the spots that pass the
    /// room-shape and wind-turbine rules and is rebuilt when the colony's buildings change or it is
    /// <see cref="MaxAge"/> ticks old. The rules that change without a building changing (roof, prisoners, sleepers,
    /// stored energy, fire, hostiles, allowed area, reservations) are checked per dog in <see cref="UsableBy"/>.
    /// Static and never saved, so nothing lands in save files.
    /// </summary>
    public static class ChargeSpots
    {
        private const int MaxAge = 2500;
        private const float SearchRadius = 10f;
        private const int Candidates = 5;

        private class Cache
        {
            public MapEvents events;
            public int builtAt;
            public bool dirty = true;
            public List<ChargeSpot> spots;

            public void MarkDirty(Building _) => dirty = true;
            public void MarkDirty() => dirty = true;
            public void MarkDirtyCell(IntVec3 _) => dirty = true;

            public void Subscribe(Map map)
            {
                events = map.events;
                events.BuildingSpawned += MarkDirty;
                events.BuildingDespawned += MarkDirty;
                events.RegionsRoomsChanged += MarkDirty;
                events.RoofChanged += MarkDirtyCell;
            }

            public void Unsubscribe()
            {
                if (events == null) return;
                events.BuildingSpawned -= MarkDirty;
                events.BuildingDespawned -= MarkDirty;
                events.RegionsRoomsChanged -= MarkDirty;
                events.RoofChanged -= MarkDirtyCell;
                events = null;
            }
        }

        private static readonly Dictionary<Map, Cache> caches = new Dictionary<Map, Cache>();

        public static void ClearAll()
        {
            foreach (Cache cache in caches.Values)
                cache.Unsubscribe();
            caches.Clear();
        }

        public static void Forget(Map map)
        {
            if (map == null || !caches.TryGetValue(map, out Cache cache))
                return;
            cache.Unsubscribe();
            caches.Remove(map);
        }

        /// <summary>Every spot that passes the cached rules, rebuilt first if stale.</summary>
        public static List<ChargeSpot> All(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (!caches.TryGetValue(map, out Cache cache))
            {
                foreach (Map gone in caches.Keys.Where(m => !Find.Maps.Contains(m)).ToList())
                    Forget(gone);
                cache = new Cache();
                cache.Subscribe(map);
                caches[map] = cache;
            }
            if (cache.dirty || cache.spots == null || now - cache.builtAt >= MaxAge || now < cache.builtAt)
            {
                cache.builtAt = now;
                cache.dirty = false;
                cache.spots = Build(map);
                QR.Log($"charge spots rebuilt on {map}: {cache.spots.Count}");
            }
            return cache.spots;
        }

        private static List<ChargeSpot> Build(Map map)
        {
            int distance = QuadrunnerMod.Settings.turbineDistance;
            HashSet<IntVec3> turbineBuffer = TurbineBuffer(map, distance);
            var spots = new List<ChargeSpot>();
            var taken = new HashSet<IntVec3>();
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b.GetComp<CompPowerTransmitter>() == null)
                    continue;
                foreach (Room room in RoomsOf(b.Position, map).Where(RoomQualifies))
                {
                    // The nearest free cell of that room to the conduit, dropped if too close to a turbine.
                    foreach (IntVec3 c in GenRadial.RadialCellsAround(b.Position, SearchRadius, useCenter: true))
                    {
                        if (!c.InBounds(map) || c.GetRoom(map) != room || !FreeCell(c, map) || taken.Contains(c))
                            continue;
                        if (!turbineBuffer.Contains(c))
                        {
                            taken.Add(c);
                            spots.Add(new ChargeSpot { cell = c, conduit = b });
                        }
                        break;
                    }
                }
            }
            return spots;
        }

        /// <summary>The conduit's room, or under a wall the rooms on its four sides.</summary>
        private static IEnumerable<Room> RoomsOf(IntVec3 cell, Map map)
        {
            Room own = cell.GetRoom(map);
            if (own != null)
                return new[] { own };
            return GenAdj.CardinalDirections.Select(d => (cell + d).InBounds(map) ? (cell + d).GetRoom(map) : null)
                .Where(r => r != null).Distinct();
        }

        /// <summary>Roofed, indoors, not a doorway and not touching the map edge.</summary>
        public static bool RoomQualifies(Room room) =>
            room != null && !room.IsDoorway && !room.TouchesMapEdge && !room.PsychologicallyOutdoors && room.OpenRoofCount == 0;

        /// <summary>
        /// <see cref="Room.IsPrisonCell"/> is only recomputed when the room's shape changes; a bed switched to prisoners
        /// updates the role.
        /// </summary>
        private static bool IsPrison(Room room) => room.IsPrisonCell || room.Role == RoomRoleDefOf.PrisonCell
            || room.Role == RoomRoleDefOf.PrisonBarracks;

        private static bool FreeCell(IntVec3 c, Map map) => c.Standable(map) && c.GetEdifice(map) == null;

        /// <summary>Each colony wind turbine's cells and clear area, pre-expanded by <paramref name="distance"/>.</summary>
        private static HashSet<IntVec3> TurbineBuffer(Map map, int distance)
        {
            var zone = new HashSet<IntVec3>();
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b.GetComp<CompPowerPlantWind>() == null)
                    continue;
                zone.AddRange(b.OccupiedRect().Cells);
                zone.AddRange(WindTurbineUtility.CalculateWindCells(b.Position, b.Rotation, b.def.size));
            }
            if (zone.Count == 0 || distance <= 0)
                return zone;
            var buffer = new HashSet<IntVec3>(zone);
            int radialCount = GenRadial.NumCellsInRadius(distance);
            foreach (IntVec3 cell in zone)
                for (int i = 0; i < radialCount; i++)
                {
                    IntVec3 c = cell + GenRadial.RadialPattern[i];
                    if (c.InBounds(map))
                        buffer.Add(c);
                }
            return buffer;
        }

        /// <summary>The rules that can change without a building changing, plus the cached ones re-checked.</summary>
        public static bool UsableBy(ChargeSpot spot, Pawn dog, out string why)
        {
            Map map = dog.Map;
            Room room = spot.cell.GetRoom(map);
            why = !spot.conduit.Spawned || spot.conduit.Map != map ? "conduit gone"
                : !RoomQualifies(room) || !FreeCell(spot.cell, map) ? "room doesn't qualify"
                : IsPrison(room) ? "prison cell"
                : (room.Role == RoomRoleDefOf.Bedroom || room.Role == RoomRoleDefOf.Barracks)
                  && room.ContainedBeds.Any(b => b.AnyOccupants) ? "someone sleeps there"
                : spot.StoredEnergy <= 0f ? "no stored energy"
                : !spot.cell.InAllowedArea(dog) ? "outside the allowed area"
                : room.ContainsThing(ThingDefOf.Fire) ? "fire"
                : map.attackTargetsCache.TargetsHostileToColony.Any(t => t.Thing.Spawned && t.Thing.GetRoom() == room) ? "hostiles"
                : !dog.CanReserve(spot.cell) ? "taken"
                : null;
            return why == null;
        }

        /// <summary>
        /// The best usable spot for <paramref name="dog"/>: the first <see cref="Candidates"/> found searching outward
        /// through the regions it may enter (forbidden doors block), ranked by <see cref="Score"/>; null if none.
        /// </summary>
        public static ChargeSpot Best(Pawn dog)
        {
            Map map = dog.Map;
            List<ChargeSpot> all = All(map);
            if (all.Count == 0)
                return null;
            var byRegion = new Dictionary<Region, List<ChargeSpot>>();
            for (int i = 0; i < all.Count; i++)
            {
                ChargeSpot spot = all[i];
                Region region = spot.cell.GetRegion(map);
                if (region == null)
                    continue;
                if (!byRegion.TryGetValue(region, out List<ChargeSpot> list))
                    byRegion[region] = list = new List<ChargeSpot>();
                list.Add(spot);
            }
            if (byRegion.Count == 0)
                return null;
            var found = new List<ChargeSpot>(Candidates);
            TraverseParms parms = TraverseParms.For(dog);
            RegionTraverser.BreadthFirstTraverse(dog.Position, map, (from, to) => to.Allows(parms, isDestination: false),
                r =>
                {
                    if (byRegion.TryGetValue(r, out List<ChargeSpot> list))
                    {
                        for (int i = 0; i < list.Count; i++)
                            if (UsableBy(list[i], dog, out _))
                                found.Add(list[i]);
                    }
                    return found.Count >= Candidates;
                });
            ChargeSpot best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < found.Count; i++)
            {
                float s = Score(found[i], dog);
                if (s < bestScore)
                {
                    bestScore = s;
                    best = found[i];
                }
            }
            return best;
        }

        /// <summary>Lower is better: distance first, then dogs already charging in the room, then stored energy.</summary>
        public static float Score(ChargeSpot spot, Pawn dog)
        {
            Room room = spot.cell.GetRoom(dog.Map);
            int charging = dog.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Count(p => p != dog
                && p.CurJobDef == QR_DefOf.QR_ChargeAtConduit && p.CurJob.targetA.Cell.GetRoom(dog.Map) == room);
            return dog.Position.DistanceTo(spot.cell) + 15f * charging - 10f * UnityEngine.Mathf.Min(spot.StoredEnergy / 1000f, 1f);
        }
    }
}
