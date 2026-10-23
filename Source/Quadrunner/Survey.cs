using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public enum SurveyCategory { Room, Storage, Growing, OtherZone, Home, Outside }

    /// <summary>Transient, map-shared exploration data owned by the existing dispatcher. Only completed local scans
    /// certify coverage; native area inventories contain geometry, never remotely discovered work.</summary>
    public sealed partial class Survey : IDisposable
    {
        private sealed class Area
        {
            internal object key;
            internal SurveyCategory category;
            internal readonly List<int> cells = new List<int>();
            internal readonly List<IntVec3> candidates = new List<IntVec3>();
            internal int remaining;
            internal bool live, candidatesReady;
        }

        private sealed class Explorer
        {
            internal int checkedObservation = -1, checkedRevision = -1;
            internal bool goalUseful;
            internal Area area;
            internal IntVec3 goal = IntVec3.Invalid;
            internal int jobId = -1, sweep, revision, scanTick = -1;
            internal readonly HashSet<IntVec3> rejected = new HashSet<IntVec3>();
        }

        private struct Choice
        {
            internal Area area;
            internal IntVec3 cell;
            internal int bound, index;
        }

        private readonly DogNetwork network;
        private readonly Map map;
        private readonly Dictionary<Pawn, Explorer> explorers = new Dictionary<Pawn, Explorer>();
        private readonly List<Pawn> retiredObservers = new List<Pawn>();
        private readonly List<Choice> choices = new List<Choice>();
        private static readonly Comparison<Choice> choiceComparison = (a, b) =>
            a.bound != b.bound ? a.bound.CompareTo(b.bound) : a.index != b.index ? a.index.CompareTo(b.index)
                : a.area.cells[0].CompareTo(b.area.cells[0]);
        private static readonly Comparison<IntVec3> cellComparison = (a, b) =>
            a.z != b.z ? a.z.CompareTo(b.z) : a.x.CompareTo(b.x);
        private readonly bool[] observed;
        private int observedCount, observationVersion;
        private int sweep = 1, revision;
        private static int Now => Find.TickManager.TicksGame;
        private static RobodogWork Tuning => RobodogWork.Utility;

        // Opt-in measurements for the controlled in-game benchmark; no timing/allocation instrumentation in play.
        internal bool Measure = false;
        internal long PathRequests, GeometryBuilds, GeometryChecks;
        internal long ReachabilityChecks = 0;
        internal long GeometryElapsedTicks, GeometryAllocatedBytes;
        internal long CandidateBuilds, CandidateElapsedTicks, CandidateAllocatedBytes;
        internal long ObservationElapsedTicks, ObservationAllocatedBytes, ValidationElapsedTicks, ValidationAllocatedBytes;
        internal long LocalScanElapsedTicks, LocalScanAllocatedBytes;
        internal static Func<long> AllocatedBytes = null;

        public Survey(DogNetwork network)
        {
            this.network = network;
            map = network.Map;
            events = map.events;
            observed = new bool[map.cellIndices.NumGridCells];
            InitializeGeometry();
        }

        public int Sweep => sweep;
        public int ObservedCellCount => observedCount;
        public bool Observed(IntVec3 cell) => cell.InBounds(map) && observed[map.cellIndices.CellToIndex(cell)];
        public int LastScan(Pawn dog) => explorers.TryGetValue(dog, out Explorer e) ? e.scanTick : -1;
        public IntVec3 Goal(Pawn dog) => explorers.TryGetValue(dog, out Explorer e) ? e.goal : IntVec3.Invalid;
        public object AreaKey(Pawn dog) => explorers.TryGetValue(dog, out Explorer e) ? e.area?.key : null;

        private Explorer ExplorerFor(Pawn dog)
        {
            if (!explorers.TryGetValue(dog, out Explorer e)) explorers.Add(dog, e = new Explorer());
            if (e.sweep != sweep || e.revision != revision)
            {
                e.rejected.Clear();
                e.sweep = sweep; e.revision = revision;
                if (e.area?.live != true) { e.area = null; e.goal = IntVec3.Invalid; }
            }
            return e;
        }

        /// <summary>Called once, after ReportLocal has completed, even if it reported no tasks. The footprint is the
        /// same revealed radial cells as that scanner, with no room/LOS restriction and no arrival credit.</summary>
        public void Scanned(Pawn observer)
        {
            if (observer?.Spawned != true || observer.Map != map) return;
            long started = Measure ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            long bytes = Measure && AllocatedBytes != null ? AllocatedBytes() : 0;
            ExplorerFor(observer).scanTick = Now;
            observationVersion++;
            int count = GenRadial.NumCellsInRadius(Tuning.scanRadius);
            for (int n = 0; n < count; n++)
            {
                IntVec3 cell = observer.Position + GenRadial.RadialPattern[n];
                if (!cell.InBounds(map) || cell.Fogged(map)) continue;
                int index = map.cellIndices.CellToIndex(cell);
                if (observed[index]) continue;
                observed[index] = true; observedCount++;
                Area area = owners[index];
                if (area != null) area.remaining--;
            }
            if (Measure)
            {
                ObservationElapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
                if (AllocatedBytes != null) ObservationAllocatedBytes += AllocatedBytes() - bytes;
            }
        }

        /// <summary>Build one idle movement job. Retained coverage targets survive expiry, productive work and bounded
        /// travel legs. Failed returned routes are provisional for this sweep/revision, not certified observations.</summary>
        public Job JobFor(Pawn dog)
        {
            if (dog?.Spawned != true || dog.Map != map || !DogNetwork.IsAvailable(dog) || network.War
                || !network.PathInputsReady(dog)) return null;
            Refresh();
            Explorer e = ExplorerFor(dog);
            for (int pass = 0; pass < 2; pass++)
            {
                if (e.area?.live == true && e.area.remaining > 0)
                {
                    EnsureCandidates(e.area);
                    if (e.goal.IsValid && e.area.candidates.Contains(e.goal) && !e.rejected.Contains(e.goal)
                        && Useful(e.area, e.goal) && !Taken(dog, e.goal))
                    {
                        Job continued = RouteJob(dog, e, e.area, e.goal);
                        if (continued != null) return continued;
                        e.rejected.Add(e.goal);
                    }
                    Job within = Select(dog, e, e.area.category, e.area);
                    if (within != null) return within;
                    // Another worker's claims keep the area pending, but need not idle this worker beside it.
                }
                e.area = null; e.goal = IntVec3.Invalid;
                for (SurveyCategory category = SurveyCategory.Room; category <= SurveyCategory.Outside; category++)
                {
                    Job selected = Select(dog, e, category, null);
                    if (selected != null) return selected;
                }
                // Never reset on temporary claims. All remaining candidate routes must have been attempted or
                // observed before this dog can close a pass; local horizon exhaustion is not a condition here.
                if (HasClaims(dog) || !CanAdvanceSweep(dog, e)) return null;
                BeginSweep();
                e = ExplorerFor(dog);
            }
            return null;
        }

        private Job Select(Pawn dog, Explorer e, SurveyCategory category, Area only)
        {
            choices.Clear();
            // In vanilla 1.6 PathFinderJob.IndexCost is >= 0 (max with a ushort provider penalty). Every step adds
            // rounded pawn movement ticks. Chebyshev distance * the lesser step cost is therefore a lower bound on
            // EVERY returned route, even with weighted A*. Strict > pruning preserves equal-cost cell-index ties.
            int step = Math.Max(0, Math.Min(Mathf.RoundToInt(dog.TicksPerMoveCardinal), Mathf.RoundToInt(dog.TicksPerMoveDiagonal)));
            foreach (Area area in areas)
            {
                if (!area.live || area.category != category || area.remaining == 0 || only != null && area != only) continue;
                EnsureCandidates(area);
                foreach (IntVec3 cell in area.candidates)
                {
                    if (e.rejected.Contains(cell) || !Useful(area, cell) || Taken(dog, cell)) continue;
                    int distance = Math.Max(Math.Abs(cell.x - dog.Position.x), Math.Abs(cell.z - dog.Position.z));
                    choices.Add(new Choice { area = area, cell = cell, bound = distance * step, index = map.cellIndices.CellToIndex(cell) });
                }
            }
            choices.Sort(choiceComparison);
            float bestCost = float.MaxValue;
            int bestIndex = int.MaxValue;
            Area bestArea = null;
            IntVec3 best = IntVec3.Invalid, bestLeg = IntVec3.Invalid;
            foreach (Choice choice in choices)
            {
                if (choice.bound > bestCost) break;
                if (!Usable(dog, choice.cell)) { e.rejected.Add(choice.cell); continue; }
                using (PawnPath path = FindPath(dog, choice.cell))
                {
                    if (!Accepted(dog, path)) { e.rejected.Add(choice.cell); continue; }
                    if (path.TotalCost < bestCost || path.TotalCost == bestCost && (choice.index < bestIndex
                        || choice.index == bestIndex && choice.area.cells[0] < bestArea.cells[0]))
                    {
                        bestCost = path.TotalCost; bestIndex = choice.index;
                        bestArea = choice.area; best = choice.cell; bestLeg = Leg(dog, path);
                    }
                }
            }
            return best.IsValid ? MakeJob(e, bestArea, best, bestLeg) : null;
        }

        private Job RouteJob(Pawn dog, Explorer e, Area area, IntVec3 cell)
        {
            if (!Usable(dog, cell)) return null;
            using (PawnPath path = FindPath(dog, cell))
                return Accepted(dog, path) ? MakeJob(e, area, cell, Leg(dog, path)) : null;
        }

        private PawnPath FindPath(Pawn dog, IntVec3 cell)
        {
            if (Measure) PathRequests++;
            return map.pathFinder.FindPathNow(dog.Position, cell, TraverseParms.For(dog, Danger.Deadly), peMode: PathEndMode.OnCell);
        }

        private bool Usable(Pawn dog, IntVec3 cell) => cell.InBounds(map) && cell.Standable(map)
            && !cell.Fogged(map) && !cell.IsForbidden(dog);

        private bool Accepted(Pawn dog, PawnPath path)
        {
            if (!path.Found) return false;
            foreach (IntVec3 node in path.NodesReversed)
                if (node.Fogged(map) || node.IsForbidden(dog) || node.GetDoor(map)?.IsForbidden(dog) == true) return false;
            return true;
        }

        private IntVec3 Leg(Pawn dog, PawnPath path)
        {
            // The path is reversed (destination first). Bound both walked steps and Euclidean displacement. Retain
            // the ultimate target, so a leg cannot turn into a new local sweep around its endpoint.
            IntVec3 result = dog.Position;
            float walked = 0f;
            for (int i = path.NodesReversed.Count - 2; i >= 0; i--)
            {
                IntVec3 next = path.NodesReversed[i];
                walked += next.x != result.x && next.z != result.z ? 1.414214f : 1f;
                if (walked > Tuning.surveySearchRadius) break;
                result = next;
            }
            return result;
        }

        private static Job MakeJob(Explorer e, Area area, IntVec3 goal, IntVec3 leg)
        {
            Job job = JobMaker.MakeJob(QR_DefOf.QR_Survey, leg);
            e.checkedObservation = -1;
            job.expiryInterval = Tuning.surveyJobTicks;
            job.checkOverrideOnExpire = true;
            e.area = area; e.goal = goal; e.jobId = job.loadID;
            return job;
        }

        private bool Useful(Area area, IntVec3 point)
        {
            if (area.remaining == 0) return false;
            int count = GenRadial.NumCellsInRadius(Tuning.scanRadius);
            for (int n = 0; n < count; n++)
            {
                IntVec3 cell = point + GenRadial.RadialPattern[n];
                if (!cell.InBounds(map)) continue;
                int index = map.cellIndices.CellToIndex(cell);
                if (owners[index] == area && !observed[index]) return true;
            }
            return false;
        }


        private bool Claimed(Pawn dog, Explorer e) => dog.Spawned && !dog.Dead && !dog.Downed && !dog.Drafted
            && dog.CurJobDef == QR_DefOf.QR_Survey && dog.CurJob.loadID == e.jobId
            && CompRobodog.Of(dog) is CompRobodog c && !c.PoweredDown && !c.WantsCharge && !network.War;

        private bool Taken(Pawn dog, IntVec3 cell)
        {
            int spacing = Tuning.surveySpacing;
            foreach (var pair in explorers)
                if (pair.Key != dog && Claimed(pair.Key, pair.Value)
                    && pair.Value.goal.DistanceToSquared(cell) < spacing * spacing) return true;
            return false;
        }

        private bool HasClaims(Pawn except)
        {
            foreach (var pair in explorers) if (pair.Key != except && Claimed(pair.Key, pair.Value)) return true;
            return false;
        }

        private bool CanAdvanceSweep(Pawn dog, Explorer e)
        {
            foreach (Area area in areas)
            {
                if (!area.live || area.remaining == 0) continue;
                EnsureCandidates(area);
                foreach (IntVec3 cell in area.candidates)
                    if (Useful(area, cell) && !e.rejected.Contains(cell)) return false;
            }
            return true;
        }

        private void BeginSweep()
        {
            if (++sweep == int.MaxValue) sweep = 1;
            Array.Clear(observed, 0, observed.Length);
            observedCount = 0; observationVersion++;
            foreach (Area area in areas) if (area.live) area.remaining = area.cells.Count;
            foreach (Explorer e in explorers.Values) { e.area = null; e.goal = IntVec3.Invalid; e.jobId = -1; }
        }

        public void Clear()
        {
            Array.Clear(observed, 0, observed.Length); observedCount = 0; observationVersion++;
            explorers.Clear(); dirty = true; built = false;
            retiredObservers.Clear();
            byKey.Clear(); areas.Clear(); choices.Clear(); tiles.Clear(); candidateSet.Clear();
            Array.Clear(owners, 0, owners.Length);
            Array.Clear(nextOwners, 0, nextOwners.Length);
        }

        /// <summary>Live validation for an active leg, including goals rebuilt after save/load. No saved runtime IDs.</summary>
        internal bool Valid(Pawn dog, Job job)
        {
            if (!Measure) return CheckGoal(dog, job);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long bytes = AllocatedBytes != null ? AllocatedBytes() : 0;
            bool valid = CheckGoal(dog, job);
            RecordDriverCost(started, bytes);
            return valid;
        }

        internal void RecordDriverCost(long started, long bytes)
        {
            if (!Measure) return;
            ValidationElapsedTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
            if (AllocatedBytes != null) ValidationAllocatedBytes += AllocatedBytes() - bytes;
        }

        internal bool DoneWaiting(Pawn dog, int arrived)
        {
            if (!Measure) return ScanComplete(dog, arrived);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            long bytes = AllocatedBytes != null ? AllocatedBytes() : 0;
            bool complete = ScanComplete(dog, arrived);
            RecordDriverCost(started, bytes);
            return complete;
        }

        private bool ScanComplete(Pawn dog, int arrived) =>
            !explorers.TryGetValue(dog, out Explorer e) || dog.Position != e.goal || e.scanTick >= arrived;

        private bool CheckGoal(Pawn dog, Job job)
        {
            Refresh();
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || comp.PoweredDown || comp.WantsCharge || dog.Drafted || dog.Downed
                || network.War || !Usable(dog, job.targetA.Cell)) return false;
            if (dog.pather.Moving && (dog.pather.nextCell.Fogged(map) || dog.pather.nextCell.IsForbidden(dog)
                || dog.pather.nextCell.GetDoor(map)?.IsForbidden(dog) == true)) return false;
            if (!explorers.TryGetValue(dog, out Explorer e) || e.jobId != job.loadID || e.area?.live != true) return false;
            if (e.checkedObservation != observationVersion || e.checkedRevision != revision)
            {
                EnsureCandidates(e.area);
                e.goalUseful = e.area.candidates.Contains(e.goal) && Useful(e.area, e.goal);
                e.checkedObservation = observationVersion; e.checkedRevision = revision;
            }
            return e.goalUseful;
        }
    }

    /// <summary>Interruptible travel followed by waiting for the existing scheduled scanner; never credits arrival.</summary>
    public class JobDriver_Survey : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            pawn.Map.pawnDestinationReservationManager.Reserve(pawn, job, job.targetA.Cell);
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !DogNetwork.For(pawn.Map).Survey.Valid(pawn, job));
            Toil go = Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return go;
            Toil observe = ToilMaker.MakeToil("AwaitScheduledSurveyScan");
            int arrived = -1;
            observe.initAction = () => arrived = Find.TickManager.TicksGame;
            observe.tickAction = () =>
            {
                Survey survey = DogNetwork.For(pawn.Map).Survey;
                // Intermediate travel legs need no artificial scan stop; the final goal waits for a real scan.
                if (survey.DoneWaiting(pawn, arrived)) ReadyForNextToil();
            };
            observe.defaultCompleteMode = ToilCompleteMode.Never;
            yield return observe;
        }
    }
}
