using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// The worker Quadrunner's integrated water hose (plan 0010). A network Clean task whose target belongs to a
    /// contiguous patch of more than <see cref="RobodogWork.hoseMinTiles"/> blood/firefoam tiles becomes one hose job
    /// that walks the patch and sprays every eligible filth object in it, so the last few tiles keep the hose instead
    /// of falling back to normal cleaning. The hose never targets any other filth (plan 0010: blood and firefoam only).
    /// </summary>
    public static class Hose
    {
        /// <summary>Vanilla's own fresh-filth gate (<c>WorkGiver_CleanFilth</c>); the hose keeps cleaning rules.</summary>
        internal const int MinTicksSinceThickened = 600;

        /// <summary>
        /// One hose job for <paramref name="target"/>'s patch, or null when the patch is too small (normal cleaning
        /// then handles it) or the target is not a hose filth. Only discovered network Clean targets that are
        /// unassigned or assigned to this dog are queued, and only if they are reservable and reachable; other filth
        /// on the same cells is never queued. The tile threshold counts those queueable tiles, not filth objects.
        /// </summary>
        public static Job JobFor(Pawn dog, Thing target)
        {
            RobodogWork work = RobodogWork.Of(dog);
            if (dog == null || !dog.Spawned || dog.Map == null || work == null || work.hoseFilth.NullOrEmpty()
                || !(target is Filth first) || !StillEligible(first, dog) || !Mature(first))
                return null;
            List<IntVec3> patch = PatchCells(dog.Map, first.Position, work);
            int minimum = work.hoseMinTiles > 0 ? work.hoseMinTiles : 4;
            HashSet<string> discovered = Discovered(DogNetwork.For(dog.Map), dog);
            if (discovered.Count == 0)
                return null;
            List<Filth> targets = new List<Filth>(patch.Count);
            if (Queueable(first, dog, discovered))
                targets.Add(first);
            foreach (IntVec3 cell in patch
                         .OrderBy(c => (c - first.Position).LengthHorizontalSquared)
                         .ThenBy(c => c.x).ThenBy(c => c.z))
                foreach (Thing thing in cell.GetThingList(dog.Map))
                    if (thing is Filth filth && filth != first && Queueable(filth, dog, discovered))
                        targets.Add(filth);
            // The threshold counts only distinct tiles the network actually discovered and assigned or left free for
            // this dog — the same rules TrimQueue applies to a vanilla Clean job's batch.
            if (targets.Count == 0 || targets.Select(f => f.Position).Distinct().Count() < minimum)
                return null;
            Job job = JobMaker.MakeJob(QR_DefOf.QR_CleanWithHose);
            foreach (Filth filth in targets)
                job.AddQueuedTarget(TargetIndex.A, filth);
            return job;
        }

        /// <summary>Still a hose filth this dog may spray here: right def, spawned, Home, revealed, not forbidden.</summary>
        internal static bool StillEligible(Filth filth, Pawn dog)
        {
            if (filth == null || dog == null || !filth.Spawned || filth.Destroyed || filth.Map != dog.Map)
                return false;
            RobodogWork work = RobodogWork.Of(dog);
            return work != null && work.hoseFilth != null && work.hoseFilth.Contains(filth.def)
                && filth.Map.areaManager.Home[filth.Position] && !filth.Position.Fogged(filth.Map)
                && !filth.IsForbidden(dog);
        }

        private static bool Mature(Filth filth) => filth.TicksSinceThickened >= MinTicksSinceThickened;

        /// <summary>
        /// Target IDs the network has discovered in its normal queue and that this dog may still take: unassigned,
        /// or already assigned to it. Checked before reservations, because assignment is recorded first.
        /// </summary>
        private static HashSet<string> Discovered(DogNetwork network, Pawn dog)
        {
            HashSet<string> ids = new HashSet<string>();
            NetworkState state = network?.State;
            if (state?.normal == null)
                return ids;
            string dogId = dog.ThingID;
            foreach (NetworkTask task in state.normal)
                if (task.assigneeId == null || task.assigneeId == dogId)
                    ids.Add(task.targetId);
            return ids;
        }

        private static bool Queueable(Filth filth, Pawn dog, HashSet<string> discovered) =>
            discovered.Contains(filth.ThingID) && StillEligible(filth, dog) && Mature(filth)
            && dog.CanReserve(filth) && Reachable(filth, dog);

        internal static bool Reachable(Filth filth, Pawn dog) =>
            filth != null && dog.CanReach(filth, PathEndMode.Touch, Danger.Deadly);

        /// <summary>
        /// Distinct affected cells of the contiguous 8-neighbour patch containing <paramref name="origin"/>. A cell
        /// belongs when it is revealed, in Home and holds at least one mature, unforbidden hose filth. The search is
        /// bounded by <see cref="RobodogWork.hosePatchRadius"/> and <see cref="RobodogWork.hoseMaxTiles"/> so a
        /// pathological map-wide spill cannot turn every dispatch into a whole-map walk.
        /// </summary>
        private static List<IntVec3> PatchCells(Map map, IntVec3 origin, RobodogWork work)
        {
            List<IntVec3> patch = new List<IntVec3>();
            if (!Affected(map, origin, work))
                return patch;
            int cap = work.hoseMaxTiles > 0 ? work.hoseMaxTiles : 256;
            float radius = work.hosePatchRadius > 0 ? work.hosePatchRadius : 12f;
            Queue<IntVec3> open = new Queue<IntVec3>();
            HashSet<IntVec3> seen = new HashSet<IntVec3> { origin };
            open.Enqueue(origin);
            while (open.Count > 0 && patch.Count < cap)
            {
                IntVec3 cell = open.Dequeue();
                patch.Add(cell);
                foreach (IntVec3 offset in GenAdj.AdjacentCells)
                {
                    if (patch.Count >= cap)
                        break;
                    IntVec3 neighbour = cell + offset;
                    if (!neighbour.InBounds(map) || seen.Contains(neighbour)
                        || !neighbour.InHorDistOf(origin, radius))
                        continue;
                    seen.Add(neighbour);
                    if (Affected(map, neighbour, work))
                        open.Enqueue(neighbour);
                }
            }
            return patch;
        }

        private static bool Affected(Map map, IntVec3 cell, RobodogWork work)
        {
            if (!cell.InBounds(map) || cell.Fogged(map) || !map.areaManager.Home[cell])
                return false;
            foreach (Thing thing in cell.GetThingList(map))
                if (thing is Filth filth && filth.Spawned && work.hoseFilth.Contains(filth.def)
                    && Mature(filth) && !filth.IsForbidden(Faction.OfPlayer))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// Sprays a queued patch: walks to each filth, then streams the native hose effecter at it while applying the
    /// same cleaning work and thinning as vanilla, so water alone removes the filth. All patch targets were reserved
    /// when the job started; the queue is saved with the job, which is how the last tiles keep the hose.
    /// </summary>
    public class JobDriver_CleanWithHose : JobDriver
    {
        private const TargetIndex FilthInd = TargetIndex.A;

        private float cleaningWorkDone;
        private float totalCleaningWorkDone;
        private float totalCleaningWorkRequired;

        private Filth Filth => job.GetTarget(FilthInd).Thing as Filth;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.ReserveAsManyAsPossible(job.GetTargetQueue(FilthInd), job);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil extract = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(
                FilthInd, thing => Hose.StillEligible(thing as Filth, pawn));
            yield return extract;
            yield return Toils_JobTransforms.SucceedOnNoTargetInQueue(FilthInd);
            yield return Toils_JobTransforms.ExtractNextTargetFromQueue(FilthInd);
            yield return Toils_Goto.GotoThing(FilthInd, PathEndMode.Touch)
                .JumpIfDespawnedOrNullOrForbidden(FilthInd, extract)
                .JumpIfOutsideHomeArea(FilthInd, extract)
                .JumpIf(() => !Hose.StillEligible(Filth, pawn) || !Hose.Reachable(Filth, pawn), extract);

            Toil spray = ToilMaker.MakeToil("SprayFilth");
            spray.initAction = () =>
            {
                Filth filth = Filth;
                cleaningWorkDone = 0f;
                totalCleaningWorkDone = 0f;
                totalCleaningWorkRequired = filth == null ? 0f
                    : filth.def.filth.cleaningWorkToReduceThickness * filth.thickness;
            };
            spray.tickIntervalAction = Spray;
            spray.defaultCompleteMode = ToilCompleteMode.Never;
            // Native continuous spray: vanilla's foam-spray fleck, tinted pale blue, from the dog to the filth.
            // Deliberately not EffecterDefOf.Clean, whose bubbles are the ordinary mop's effect.
            spray.WithEffect(QR_DefOf.QR_HoseSpray, FilthInd);
            spray.WithProgressBar(FilthInd, Progress, interpolateBetweenActorAndTarget: true);
            spray.PlaySustainerOrSound(() => Filth?.def.filth.cleaningSound ?? SoundDefOf.Interact_CleanFilth);
            spray.JumpIfDespawnedOrNullOrForbidden(FilthInd, extract);
            spray.JumpIfOutsideHomeArea(FilthInd, extract);
            spray.JumpIf(() => !Hose.StillEligible(Filth, pawn), extract);
            yield return spray;
            yield return Toils_Jump.Jump(extract);
        }

        private float Progress() => totalCleaningWorkRequired <= 0f
            ? 0f : Mathf.Clamp01(totalCleaningWorkDone / totalCleaningWorkRequired);

        /// <summary>Vanilla's cleaning work and <see cref="Filth.ThinFilth"/>, with the hose effect instead of a mop.</summary>
        private void Spray(int delta)
        {
            Filth filth = Filth;
            if (filth == null || filth.Destroyed || !Hose.StillEligible(filth, pawn))
            {
                ReadyForNextToil();
                return;
            }
            float factor = filth.Position.GetTerrain(filth.Map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
            float work = pawn.GetStatValue(StatDefOf.CleaningSpeed) * delta;
            RobodogWork hose = RobodogWork.Of(pawn);
            if (hose != null && hose.hoseCleaningSpeedFactor > 0f)
                work *= hose.hoseCleaningSpeedFactor;
            if (factor != 0f)
                work /= factor;
            cleaningWorkDone += work;
            totalCleaningWorkDone += work;
            if (cleaningWorkDone <= filth.def.filth.cleaningWorkToReduceThickness)
                return;
            filth.ThinFilth();
            cleaningWorkDone = 0f;
            if (!filth.Destroyed)
                return;
            pawn.records.Increment(RecordDefOf.MessesCleaned);
            ReadyForNextToil();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref cleaningWorkDone, "hoseCleaningWorkDone", 0f);
            Scribe_Values.Look(ref totalCleaningWorkDone, "hoseTotalCleaningWorkDone", 0f);
            Scribe_Values.Look(ref totalCleaningWorkRequired, "hoseTotalCleaningWorkRequired", 0f);
        }
    }
}
