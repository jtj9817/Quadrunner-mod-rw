using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// A dog with nothing else to do walks to its stable and docks. A dog without a stable (its stable was destroyed)
    /// adopts the nearest reachable one with a free slot.
    /// </summary>
    public class JobGiver_Dock : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompRobodog comp = CompRobodog.Of(pawn);
            if (comp == null)
                return null;
            Building_Stable stable = comp.stable ?? NearestFreeStable(pawn);
            if (stable == null || !stable.Spawned || stable.Map != pawn.Map
                || !pawn.CanReach(stable, PathEndMode.InteractionCell, Danger.Deadly))
                return null;
            return JobMaker.MakeJob(QR_DefOf.QR_Dock, stable);
        }

        private static Building_Stable NearestFreeStable(Pawn pawn) =>
            pawn.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Stable>()
                .Where(s => s.HasFreeSlot && pawn.CanReach(s, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(s => s.Position.DistanceToSquared(pawn.Position))
                .FirstOrDefault();
    }

    public class JobDriver_Dock : JobDriver
    {
        private Building_Stable Stable => (Building_Stable)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil dock = ToilMaker.MakeToil("Dock");
            dock.initAction = () => Stable.TryDock(pawn);
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }
    }

    /// <summary>
    /// No stable to go to (destroyed, uninstalled, unreachable): wait in the nearest indoor room that isn't a bedroom,
    /// barracks or prison, walking there first.
    /// </summary>
    public class JobGiver_WaitInRoom : ThinkNode_JobGiver
    {
        private const int MaxRegions = 600;
        private const int WaitTicks = 300;

        public static bool Qualifies(Room room) =>
            room != null && !room.PsychologicallyOutdoors && !room.IsDoorway && !room.IsPrisonCell
            && room.Role != RoomRoleDefOf.Bedroom && room.Role != RoomRoleDefOf.Barracks
            && room.Role != RoomRoleDefOf.PrisonCell && room.Role != RoomRoleDefOf.PrisonBarracks;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (CompRobodog.Of(pawn) == null)
                return null;
            if (Qualifies(pawn.GetRoom()))
            {
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = WaitTicks;
                return wait;
            }
            IntVec3 cell = CellInNearestRoom(pawn, pawn.GetRegion(), Qualifies);
            return cell.IsValid ? JobMaker.MakeJob(JobDefOf.Goto, cell) : null;
        }

        /// <summary>
        /// A standable cell <paramref name="pawn"/> can reach in the room nearest <paramref name="start"/> (by regions it
        /// can pass) that passes <paramref name="ok"/>; invalid if none.
        /// </summary>
        public static IntVec3 CellInNearestRoom(Pawn pawn, Region start, Predicate<Room> ok)
        {
            Region found = null;
            TraverseParms parms = TraverseParms.For(pawn);
            RegionTraverser.BreadthFirstTraverse(start, (from, to) => to.Allows(parms, isDestination: false),
                r =>
                {
                    if (r.IsDoorway || !ok(r.Room))
                        return false;
                    found = r;
                    return true;
                }, MaxRegions);
            if (found == null || !found.TryFindRandomCellInRegion(
                    c => c.Standable(pawn.Map) && pawn.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out IntVec3 cell))
                return IntVec3.Invalid;
            return cell;
        }
    }
}
