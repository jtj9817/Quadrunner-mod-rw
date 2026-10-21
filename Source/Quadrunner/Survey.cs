using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// What an otherwise idle worker does: survey the map's revealed areas for the shared network. Goals lie on or
    /// near a <see cref="RobodogWork.surveySpacing"/> grid, using revealed reachable cells when a center is blocked.
    /// Explorers keep that spacing from each other's goals and follow fog-free paths; the least-recently-surveyed
    /// area wins (never-surveyed first), spreading coverage outward and revisiting old areas by age. On arrival the driver
    /// records it with the network (<see cref="DogNetwork.Surveyed"/>) — the record that drives the preference.
    /// </summary>
    public static class Survey
    {

        /// <summary>A QR_Survey job to the goal cell, or null when no revealed reachable one was found.</summary>
        public static Job JobFor(Pawn dog, DogNetwork network)
        {
            if (dog == null || !dog.Spawned || network?.Map != dog.Map)
                return null;
            RobodogWork work = RobodogWork.Utility;
            if (work == null)
                return null;
            Map map = dog.Map;
            int spacing = work.surveySpacing;
            IntVec3 best = IntVec3.Invalid;
            long bestAge = long.MinValue;
            int bestDist = int.MaxValue;
            float radiusSquared = work.surveySearchRadius * work.surveySearchRadius;
            // Enumerate the lattice directly: vanilla's radial table ends below 80 cells, and scanning every
            // intervening cell would do needless work just to discard the non-grid points.
            for (int x = spacing / 2; x < map.Size.x; x += spacing)
            for (int z = spacing / 2; z < map.Size.z; z += spacing)
            {
                IntVec3 anchor = new IntVec3(x, 0, z);
                if (anchor.DistanceToSquared(dog.Position) > radiusSquared)
                    continue;
                IntVec3 cell = GoalNear(dog, anchor, spacing / 2f);
                if (!cell.IsValid || cell == dog.Position || Taken(network, dog, cell, spacing))
                    continue;
                long age = Age(network, cell, spacing);
                int dist = cell.DistanceToSquared(dog.Position);
                if (age < bestAge || age == bestAge && dist >= bestDist)
                    continue;
                if (!ReachableUnfogged(dog, cell, map))
                    continue;
                best = cell;
                bestAge = age;
                bestDist = dist;
            }
            if (!best.IsValid)
                return null;
            // Expire often, with the think tree re-asked, so a task found meanwhile preempts the travel.
            Job job = JobMaker.MakeJob(QR_DefOf.QR_Survey, best);
            job.expiryInterval = work.surveyJobTicks;
            job.checkOverrideOnExpire = true;
            return job;
        }


        // A lattice center may be inside a wall or just outside a narrow revealed corridor. Survey the accessible
        // part of that tile instead of silently leaving a whole corridor/room unvisited.
        private static IntVec3 GoalNear(Pawn dog, IntVec3 anchor, float radius)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(anchor, radius, useCenter: true))
                if (cell.InBounds(dog.Map) && cell.Standable(dog.Map) && !cell.Fogged(dog.Map)
                    && !cell.IsForbidden(dog) && dog.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                    return cell;
            return IntVec3.Invalid;
        }

        private static bool Taken(DogNetwork network, Pawn dog, IntVec3 cell, int spacing)
        {
            foreach (Pawn other in network.Dogs)
                if (other != dog && other.Spawned && other.CurJobDef == QR_DefOf.QR_Survey
                    && other.CurJob.targetA.Cell.DistanceToSquared(cell) < spacing * spacing)
                    return true;
            return false;
        }

        /// <summary>How long ago this tile (within half a spacing) was surveyed. Marking adjacent tile centers as
        /// visited would leave gaps outside the scan radius; a never-surveyed tile counts as oldest.</summary>
        private static long Age(DogNetwork network, IntVec3 cell, int spacing)
        {
            int newest = int.MinValue;
            List<IntVec3> cells = network.State.surveyCells;
            List<int> ticks = network.State.surveyTicks;
            int reach = spacing * spacing / 4;
            for (int i = 0; i < cells.Count; i++)
                if (cells[i].DistanceToSquared(cell) <= reach && ticks[i] > newest)
                    newest = ticks[i];
            return newest == int.MinValue ? long.MaxValue : Find.TickManager.TicksGame - newest;
        }

        /// <summary>A path the dog could walk that crosses no fog; the pooled path is always released.</summary>
        private static bool ReachableUnfogged(Pawn dog, IntVec3 cell, Map map)
        {
            using (PawnPath path = map.pathFinder.FindPathNow(dog.Position, cell, dog, peMode: PathEndMode.OnCell))
            {
                if (!path.Found)
                    return false;
                foreach (IntVec3 node in path.NodesReversed)
                    if (map.fogGrid.IsFogged(node))
                        return false;
                return true;
            }
        }
    }

    /// <summary>Walks to the survey goal and records it with the network; the job expires so found work preempts the travel.</summary>
    public class JobDriver_Survey : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            // A dynamic obstruction can make vanilla repath through fog; that never surveys, it wanders blind.
            go.AddPreTickAction(() =>
            {
                if (pawn.pather.Moving && pawn.Map.fogGrid.IsFogged(pawn.pather.nextCell))
                    ReadyForNextToil();
            });
            yield return go;
            Toil survey = ToilMaker.MakeToil("Survey");
            survey.initAction = () => DogNetwork.For(pawn.Map)?.Surveyed(pawn.Position);
            survey.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return survey;
        }
    }
}
