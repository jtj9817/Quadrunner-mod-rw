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
    /// that walks the patch and sprays a native <see cref="Verb_ArcSpray"/>-shaped band around each aimed tile, so
    /// one application cleans every eligible queued tile in that band and the last few tiles keep the hose instead of
    /// falling back to normal cleaning. The hose never targets any other filth (plan 0010: blood and firefoam only).
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
        /// Native <c>Verb_ArcSpray</c> band around <paramref name="aim"/>, oriented from the dog to it: half-width
        /// across that line, half-depth = spray thickness/2 + arching along it. The Medirunner's foam sprayer
        /// (5.5 x 3.5 cells at the XML defaults) has the same shape; no random sampling is needed for cleaning.
        /// </summary>
        internal static bool InSprayBand(Pawn dog, Filth aim, Filth filth, RobodogWork work)
        {
            if (dog == null || aim == null || filth == null)
                return false;
            Vector3 direction = (aim.Position.ToVector3Shifted() - dog.Position.ToVector3Shifted()).Yto0();
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector3.forward;
            else
                direction.Normalize();
            Vector3 offset = filth.Position.ToVector3Shifted() - aim.Position.ToVector3Shifted();
            float halfWidth = work != null && work.hoseSprayWidth > 0f ? work.hoseSprayWidth : 5.5f;
            float halfDepth = work != null
                ? work.hoseSprayThicknessCells * 0.5f + work.hoseSprayArching : 3.5f;
            Vector3 tangent = direction.RotatedBy(90f);
            return Mathf.Abs(Vector3.Dot(offset, tangent)) <= halfWidth
                && Mathf.Abs(Vector3.Dot(offset, direction)) <= halfDepth;
        }

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
    /// Sprays a queued patch: walks to each filth, then streams the native hose effecter over the native
    /// <see cref="Hose.InSprayBand"/> around it while applying the same cleaning work and thinning as vanilla to every
    /// reserved queued target in that band, so water alone removes the filth. All patch targets were reserved when
    /// the job started; the queue and each tile's partial layer work are saved with the job, which is how the last
    /// tiles keep the hose.
    /// </summary>
    public class JobDriver_CleanWithHose : JobDriver
    {
        private const TargetIndex FilthInd = TargetIndex.A;

        // Work done toward the current thickness layer, keyed by the numeric Thing ID to avoid formatting strings
        // on every spray application. Partial progress for each affected tile survives a mid-patch save.
        private Dictionary<int, float> layerWork = new Dictionary<int, float>();
        private float totalCleaningWorkDone;
        private float totalCleaningWorkRequired;
        private readonly List<Filth> affected = new List<Filth>();
        private readonly HashSet<IntVec3> burstCells = new HashSet<IntVec3>();
        private Effecter sprayBurst;

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
                totalCleaningWorkDone = 0f;
                if (filth != null && layerWork.TryGetValue(filth.thingIDNumber, out float done))
                    totalCleaningWorkDone = done;
                totalCleaningWorkRequired = filth == null ? 0f
                    : filth.def.filth.cleaningWorkToReduceThickness * filth.thickness;
            };
            spray.tickIntervalAction = Spray;
            spray.defaultCompleteMode = ToilCompleteMode.Never;
            // Native continuous spray: vanilla's foam-spray fleck, tinted pale blue, from the dog to the filth.
            // Deliberately not EffecterDefOf.Clean, whose bubbles are the ordinary mop's effect.
            spray.WithEffect(QR_DefOf.QR_HoseSpray, FilthInd);
            spray.AddFinishAction(() =>
            {
                sprayBurst?.Cleanup();
                sprayBurst = null;
            });
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

        /// <summary>
        /// One vanilla cleaning interval over the native spray band: the aimed filth and every other queued target
        /// this dog reserved inside <see cref="Hose.InSprayBand"/> (about 11 x 7 cells, matching the Medirunner's
        /// foam sprayer). Each affected filth gets the same per-cell work and thins exactly like vanilla, so several
        /// tiles can be cleaned by one application and every thickness layer keeps its own progress.
        /// </summary>
        private void Spray(int delta)
        {
            Filth aim = Filth;
            Map map = pawn.Map;
            if (aim == null || aim.Destroyed || !Hose.StillEligible(aim, pawn)
                || !GenSight.LineOfSight(pawn.Position, aim.Position, map, skipFirstCell: true))
            {
                ReadyForNextToil();
                return;
            }
            RobodogWork hose = RobodogWork.Of(pawn);
            float work = pawn.GetStatValue(StatDefOf.CleaningSpeed) * delta;
            if (hose != null && hose.hoseCleaningSpeedFactor > 0f)
                work *= hose.hoseCleaningSpeedFactor;
            affected.Clear();
            affected.Add(aim);
            foreach (LocalTargetInfo target in job.GetTargetQueue(FilthInd))
            {
                if (!(target.Thing is Filth filth) || filth == aim || !Hose.StillEligible(filth, pawn)
                    || !pawn.HasReserved(filth) || !Hose.InSprayBand(pawn, aim, filth, hose)
                    || !GenSight.LineOfSight(pawn.Position, filth.Position, map, skipFirstCell: true))
                    continue;
                affected.Add(filth);
            }
            burstCells.Clear();
            foreach (Filth filth in affected)
            {
                Apply(filth, work);
                burstCells.Add(filth.Position);
            }
            if (burstCells.Count > 0)
            {
                sprayBurst ??= QR_DefOf.QR_HoseSprayBurst.Spawn();
                foreach (IntVec3 cell in burstCells)
                    sprayBurst.Trigger(pawn, new TargetInfo(cell, map));
            }
            if (aim.Destroyed || !Hose.StillEligible(aim, pawn))
                ReadyForNextToil();
        }

        /// <summary>Vanilla's layer work and <see cref="Filth.ThinFilth"/> for one filth in the spray band.</summary>
        private void Apply(Filth filth, float work)
        {
            float factor = filth.Position.GetTerrain(filth.Map)
                .GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
            if (factor != 0f)
                work /= factor;
            int id = filth.thingIDNumber;
            layerWork.TryGetValue(id, out float done);
            done += work;
            if (filth == Filth)
                totalCleaningWorkDone += work;
            if (done <= filth.def.filth.cleaningWorkToReduceThickness)
            {
                layerWork[id] = done;
                return;
            }
            filth.ThinFilth();
            if (filth.Destroyed)
            {
                layerWork.Remove(id);
                pawn.records.Increment(RecordDefOf.MessesCleaned);
                return;
            }
            layerWork[id] = 0f;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref layerWork, "hoseLayerWork", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref totalCleaningWorkDone, "hoseTotalCleaningWorkDone", 0f);
            Scribe_Values.Look(ref totalCleaningWorkRequired, "hoseTotalCleaningWorkRequired", 0f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && layerWork == null)
                layerWork = new Dictionary<int, float>();
        }
    }
}
