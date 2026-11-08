using System;
using System.Collections.Generic;
using Unity.Collections;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Traps — spike traps, IEDs, razor wire: any <see cref="Building_Trap"/> edifice the player faction is not
    /// hostile to — are passable but harmful ground, and robodogs avoid them (plan 0011). In peacetime every path
    /// request a robodog makes carries a per-cell penalty on known traps
    /// (<see cref="Patch_PathFinder_CreateRequest"/>), so movement, work dispatch and survey route around traps
    /// whenever an alternative exists, and dispatch defers work whose only route crosses one. In wartime, pursuit
    /// never wades in: the dog walks to the last safe cell before the first trap on its route — one tile short of
    /// the wire — and holds there (<see cref="Combat"/>).
    /// </summary>
    public static class TrapAvoidance
    {
        /// <summary>Added per known-trap cell on peacetime robodog paths, on top of vanilla's 800 KnowsOfTrap cost:
        /// at a worker's 12 ticks per cell a dog prefers a detour of up to about 150 cells over one trap tile, and
        /// crosses only when no trap-free route exists at all (plan 0011 fixes the value).</summary>
        public const int PathCost = 2000;

        /// <summary>A cell whose edifice is a trap this dog knows about (a colony trap; VFES razor wire included).</summary>
        public static bool KnownTrapAt(IntVec3 c, Map map, Pawn dog) => PawnUtility.KnownDangerAt(c, map, dog);

        /// <summary>Whether the dog itself stands on a known trap, which it may always walk off.</summary>
        public static bool OnTrap(Pawn dog) =>
            dog != null && dog.Spawned && KnownTrapAt(dog.Position, dog.Map, dog);

        /// <summary>Whether any cell of <paramref name="path"/> after the one the dog stands on is a known trap.</summary>
        public static bool PathEntersTrap(PawnPath path, Pawn dog)
        {
            if (path == null || !path.Found || dog == null)
                return false;
            List<IntVec3> nodes = path.NodesReversed; // destination first; the last node is where the dog stands
            for (int i = 0; i < nodes.Count - 1; i++)
                if (KnownTrapAt(nodes[i], dog.Map, dog))
                    return true;
            return false;
        }

        /// <summary>
        /// The dog's route to <paramref name="dest"/>: whether it enters a known trap, and the last safe cell before
        /// the first one — one tile short of the wire. False when the route stays clear, or when the dog already
        /// stands on a trap, which it may always leave.
        /// </summary>
        public static bool RouteEntersTrap(Pawn dog, LocalTargetInfo dest, PathEndMode mode, out IntVec3 holdCell)
        {
            holdCell = IntVec3.Invalid;
            if (dog == null || !dog.Spawned || OnTrap(dog))
                return false;
            using (PawnPath path = dog.Map.pathFinder.FindPathNow(dog.Position, dest,
                       TraverseParms.For(dog, Danger.Deadly), peMode: mode))
            {
                if (!path.Found)
                    return false;
                List<IntVec3> nodes = path.NodesReversed;
                for (int i = nodes.Count - 2; i >= 0; i--)
                    if (KnownTrapAt(nodes[i], dog.Map, dog))
                    {
                        holdCell = nodes[i + 1];
                        return true;
                    }
            }
            return false;
        }

        // --- the peacetime trap-cost grid ---------------------------------------------------------------------------

        /// <summary>
        /// One grid per map: <see cref="PathCost"/> on every cell of a trap the player faction is not hostile to,
        /// rebuilt when a trap appears or disappears. Shared by every robodog's peacetime requests, so the
        /// pathfinder's per-parameter grid cache sees one stable customizer per map.
        /// </summary>
        private class TrapGrid : PathRequest.IPathGridCustomizer, IDisposable
        {
            private readonly Map map;
            // The publisher instance, not Map.events: a load or removal clears Map's reference fields before the
            // stale network's pruning runs (the Survey.Geometry gotcha), and unsubscribing from the cleared field NREs.
            internal readonly MapEvents events;
            private NativeArray<ushort> grid;
            private bool dirty = true;

            public TrapGrid(Map map)
            {
                this.map = map;
                events = map.events;
            }

            public NativeArray<ushort> GetOffsetGrid()
            {
                if (dirty)
                {
                    if (!grid.IsCreated)
                        grid = new NativeArray<ushort>(map.cellIndices.NumGridCells, Allocator.Persistent);
                    for (int i = 0; i < grid.Length; i++)
                        grid[i] = 0;
                    foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.CostProvider))
                        if (thing is Building_Trap trap
                            && (trap.Faction == null || !trap.Faction.HostileTo(Faction.OfPlayer)))
                            foreach (IntVec3 cell in trap.OccupiedRect())
                                if (cell.InBounds(map))
                                    grid[map.cellIndices.CellToIndex(cell)] = (ushort)PathCost;
                    dirty = false;
                }
                return grid;
            }

            public void MarkDirty(Building building)
            {
                if (building is Building_Trap)
                    dirty = true;
            }

            public void Dispose()
            {
                if (grid.IsCreated)
                    grid.Dispose();
            }
        }

        private static readonly Dictionary<Map, TrapGrid> grids = new Dictionary<Map, TrapGrid>();

        /// <summary>The shared peacetime trap-cost grid of <paramref name="map"/>, or null without a map.</summary>
        internal static PathRequest.IPathGridCustomizer CustomizerFor(Map map)
        {
            if (map == null)
                return null;
            if (!grids.TryGetValue(map, out TrapGrid grid))
            {
                grid = new TrapGrid(map);
                grid.events.BuildingSpawned += grid.MarkDirty;
                grid.events.BuildingDespawned += grid.MarkDirty;
                grids[map] = grid;
            }
            return grid;
        }

        /// <summary>Releases the grid of a map that is gone; called with the network's own map pruning.</summary>
        public static void Forget(Map map)
        {
            if (map == null || !grids.TryGetValue(map, out TrapGrid grid))
                return;
            grid.events.BuildingSpawned -= grid.MarkDirty;
            grid.events.BuildingDespawned -= grid.MarkDirty;
            grid.Dispose();
            grids.Remove(map);
        }
    }

    /// <summary>
    /// Harmony patch 7: every peacetime robodog path request carries the trap-cost grid, so the dog itself, its work
    /// dispatch and its survey all route around known traps. Vanilla has no traverse flag for trap avoidance and no
    /// virtual on CreateRequest, so this postfix on the overload <c>Pawn_PathFollower</c> uses is the one hook that
    /// reaches every path a dog takes (plan 0011). Drafted dogs, wartime, and requests another mod already
    /// customized keep vanilla pathing; wartime behavior is the standoff in <see cref="Combat"/>.
    /// </summary>
    [HarmonyPatch(typeof(PathFinder), nameof(PathFinder.CreateRequest),
        new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(IntVec3?), typeof(TraverseParms),
            typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(Pawn), typeof(PathRequest.IPathGridCustomizer) })]
    public static class Patch_PathFinder_CreateRequest
    {
        public static void Postfix(Pawn pawn, PathRequest __result)
        {
            if (__result.customizer != null || pawn == null || pawn.Map == null || pawn.Drafted
                || __result.TraverseParms.mode != TraverseMode.ByPawn || !CompRobodog.IsRobodog(pawn)
                || DogNetwork.For(pawn.Map).War)
                return;
            __result.customizer = TrapAvoidance.CustomizerFor(pawn.Map);
        }
    }

    /// <summary>
    /// The think tree's last resort, made trap-shy (plan 0011): a wander destination whose route crosses a known
    /// trap is refused — the dog stays where it is and wanders again later, instead of wading through the wire to a
    /// spot that is only region-reachable that way. Idle fallback only; charging and docking may still cross when
    /// nothing else keeps the dog alive.
    /// </summary>
    public class JobGiver_WanderAvoidTraps : JobGiver_WanderAnywhere
    {
        protected override IntVec3 GetExactWanderDest(Pawn pawn)
        {
            IntVec3 dest = base.GetExactWanderDest(pawn);
            return dest.IsValid && TrapAvoidance.RouteEntersTrap(pawn, dest, PathEndMode.OnCell, out _)
                ? IntVec3.Invalid
                : dest;
        }
    }
}
