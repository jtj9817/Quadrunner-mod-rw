using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public enum NetworkTaskKind { Retrieve, Frame, Blueprint, Haul, Clean, Rescue, Combat }
    public enum NetworkTaskPhase { Queued, Assigned, Acknowledged, Working }

    /// <summary>A discovered intention, not a saved vanilla job. Jobs are constructed only when assigned.</summary>
    public sealed class NetworkTask : IExposable
    {
        public int id;
        public NetworkTaskKind kind;
        public string targetId, assigneeId;
        public WorkGiverDef giver;
        public NetworkTaskPhase phase;
        public int jobId = -1, reportedAt, progressAt, retryAt;
        public IntVec3 lastPosition = IntVec3.Invalid;
        public float routeCost;
        public int toilIndex = -1, toilTicks;
        private Thing target;

        public Thing Target(Map map)
        {
            if (target == null && targetId != null)
                target = map.listerThings.AllThings.FirstOrDefault(t => t.ThingID == targetId)
                    ?? map.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == targetId);
            return target != null && !target.Destroyed ? target : null;
        }

        internal void SetTarget(Thing thing) { target = thing; targetId = thing.ThingID; }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref targetId, "target");
            Scribe_Values.Look(ref assigneeId, "assignee");
            Scribe_Defs.Look(ref giver, "giver");
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref jobId, "job", -1);
            Scribe_Values.Look(ref reportedAt, "reportedAt");
            Scribe_Values.Look(ref progressAt, "progressAt");
            Scribe_Values.Look(ref retryAt, "retryAt");
            Scribe_Values.Look(ref lastPosition, "lastPosition", IntVec3.Invalid);
            Scribe_Values.Look(ref routeCost, "routeCost");
            Scribe_Values.Look(ref toilIndex, "toilIndex", -1);
            Scribe_Values.Look(ref toilTicks, "toilTicks");
        }
    }

    /// <summary>Lives on exactly one stable or active dog. Removing the mod leaves no saved map component.</summary>
    public sealed class NetworkState : IExposable
    {
        public List<NetworkTask> normal = new List<NetworkTask>(), war = new List<NetworkTask>();
        public List<IntVec3> surveyCells = new List<IntVec3>();
        public List<int> surveyTicks = new List<int>();
        public int nextId = 1;
        public bool warActive;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref normal, "normal", LookMode.Deep);
            Scribe_Collections.Look(ref war, "war", LookMode.Deep);
            Scribe_Collections.Look(ref surveyCells, "surveyCells", LookMode.Value);
            Scribe_Collections.Look(ref surveyTicks, "surveyTicks", LookMode.Value);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref warActive, "warActive");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                normal = normal ?? new List<NetworkTask>();
                war = war ?? new List<NetworkTask>();
                surveyCells = surveyCells ?? new List<IntVec3>();
                surveyTicks = surveyTicks ?? new List<int>();
            }
        }
    }

    /// <summary>The most recent dog-to-coordinator report; rebuilt from real pawns after loading.</summary>
    public sealed class DogReport
    {
        public IntVec3 position;
        public int receivedAt;
        public int taskId;
        public NetworkTaskPhase phase;
    }

    /// <summary>
    /// One dispatcher per map, driven by existing stable/dog ticks. The elected physical node owns all persistent
    /// state. Local observations enqueue intentions; only this dispatcher matches them to available dogs.
    /// </summary>
    public sealed class DogNetwork
    {
        private static readonly Dictionary<Map, DogNetwork> networks = new Dictionary<Map, DogNetwork>();
        private readonly List<Building_Stable> stables = new List<Building_Stable>();
        private readonly List<Pawn> dogs = new List<Pawn>();
        private readonly Dictionary<string, Pawn> dogById = new Dictionary<string, Pawn>();
        private readonly Dictionary<string, NetworkTask> assignments = new Dictionary<string, NetworkTask>();
        private readonly Dictionary<string, DogReport> reports = new Dictionary<string, DogReport>();
        private readonly Dictionary<IntVec3, int> surveys = new Dictionary<IntVec3, int>();
        private readonly List<NetworkTask> ordered = new List<NetworkTask>();
        private readonly List<string> goneReports = new List<string>();
        private readonly Comparison<NetworkTask> taskComparison;
        private NetworkState state;
        private Thing coordinator;
        private int nextTick, nextScan;
        private bool ticking;

        private DogNetwork(Map map) { Map = map; taskComparison = CompareTasks; }
        public Map Map { get; }
        public Thing Coordinator { get { _ = State; return coordinator; } }
        public NetworkState State
        {
            get
            {
                if (state == null || coordinator == null || coordinator.Destroyed || !coordinator.Spawned)
                {
                    // A node wipe leaves a cleared-but-non-null state with a dead coordinator: re-elect, or every
                    // report is silently refused and Coordinator stays null for callers.
                    Roster();
                    Elect();
                    RebuildAssignments();
                }
                return state;
            }
        }
        public bool War => State.warActive;
        public IReadOnlyList<NetworkTask> NormalQueue => State.normal;
        public IReadOnlyList<NetworkTask> WarQueue => State.war;
        public IReadOnlyList<Pawn> Dogs => dogs;
        private static RobodogWork Tuning => QR_DefOf.QR_Quadrunner.race.GetModExtension<RobodogWork>();
        private static int Now => Find.TickManager.TicksGame;

        public static DogNetwork For(Map map)
        {
            if (map == null) return null;
            if (!networks.TryGetValue(map, out DogNetwork network))
            {
                foreach (Map gone in networks.Keys.Where(m => !Find.Maps.Contains(m)).ToList())
                    networks.Remove(gone);
                networks[map] = network = new DogNetwork(map);
            }
            return network;
        }

        public static void ClearAll() => networks.Clear();
        public static bool IsWorker(Pawn dog) => RobodogWork.Of(dog) is RobodogWork w && !w.rescue && !w.combat;

        private static bool Active(Pawn dog) => dog.Spawned && !dog.Dead && !dog.Downed
            && dog.Faction == Faction.OfPlayer && CompRobodog.Of(dog) is CompRobodog c && !c.PoweredDown;

        /// <summary>Only idle/survey/return travel can be preempted. Productive and player-ordered jobs cannot.</summary>
        public static bool IsAvailable(Pawn dog)
        {
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || dog.Dead || dog.Downed || comp.PoweredDown || comp.WantsCharge || dog.Drafted
                || dog.InMentalState || dog.Faction != Faction.OfPlayer || dog.CurJob?.playerForced == true)
                return false;
            if (!dog.Spawned)
                return comp.stable?.Spawned == true && comp.stable.Docked.Contains(dog)
                    && !comp.stable.BeingUpgraded(dog) && !dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any();
            return !NeedsRepair(dog) && Idle(dog);
        }

        private static bool Idle(Pawn dog)
        {
            JobDef job = dog.CurJobDef;
            return job == null || job == QR_DefOf.QR_Survey || job == QR_DefOf.QR_Dock || job == JobDefOf.Wait
                || job == JobDefOf.Wait_MaintainPosture || job == JobDefOf.Goto || job == JobDefOf.GotoWander
                || job == JobDefOf.Wait_Wander;
        }

        private static bool NeedsRepair(Pawn dog)
        {
            Building_Stable home = CompRobodog.Of(dog).stable;
            return IsWorker(dog) && home?.Spawned == true && home.Map == dog.Map
                && dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any()
                && dog.CanReach(home, PathEndMode.InteractionCell, Danger.Deadly);
        }

        private void Roster(Thing excluding = null)
        {
            stables.Clear(); dogs.Clear(); dogById.Clear();
            foreach (Building building in Map.listerBuildings.allBuildingsColonist)
                if (building is Building_Stable stable && stable != excluding && stable.Spawned)
                    stables.Add(stable);
            foreach (Pawn dog in Map.mapPawns.AllPawns)
                if (dog != excluding && !dog.Dead && !dog.Destroyed && dog.Faction == Faction.OfPlayer
                    && CompRobodog.IsRobodog(dog) && dog.MapHeld == Map && !dogById.ContainsKey(dog.ThingID))
                {
                    dogs.Add(dog);
                    dogById.Add(dog.ThingID, dog);
                }
        }

        private static NetworkState Stored(Thing node) => node is Building_Stable s ? s.networkState : CompRobodog.Of(node)?.networkState;
        private static void Store(Thing node, NetworkState value)
        {
            if (node is Building_Stable s) s.networkState = value;
            else if (CompRobodog.Of(node) is CompRobodog c) c.networkState = value;
        }

        private void Elect()
        {
            Thing elected = null;
            foreach (Building_Stable stable in stables)
                if (elected == null || stable.thingIDNumber < elected.thingIDNumber) elected = stable;
            if (elected == null)
                foreach (Pawn dog in dogs)
                    if (Active(dog) && (elected == null || CompRobodog.Of(dog).BuildOrder > CompRobodog.Of(elected).BuildOrder))
                        elected = dog;
            // First access after loading: recover the sole physical owner's state before changing owner.
            if (state == null)
            {
                state = Stored(elected);
                foreach (Building_Stable stable in stables) if (state == null) state = stable.networkState;
                foreach (Pawn dog in dogs) if (state == null) state = CompRobodog.Of(dog).networkState;
                if (state == null) state = new NetworkState();
                for (int i = 0; i < Math.Min(state.surveyCells.Count, state.surveyTicks.Count); i++)
                    surveys[state.surveyCells[i]] = state.surveyTicks[i];
            }
            if (elected == coordinator && elected != null) return;
            if (coordinator != null) Store(coordinator, null);
            foreach (Building_Stable stable in stables) stable.networkState = null;
            foreach (Pawn dog in dogs) CompRobodog.Of(dog).networkState = null;
            coordinator = elected;
            if (elected == null)
            {
                state.normal.Clear(); state.war.Clear(); state.surveyCells.Clear(); state.surveyTicks.Clear();
                state.warActive = false;
                assignments.Clear(); reports.Clear(); surveys.Clear();
                return;
            }
            Store(elected, state);
            QR.Log($"network coordinator: {elected}; normal {state.normal.Count}, war {state.war.Count}");
        }

        public void NodeLeaving(Thing node)
        {
            if (ticking) return; // The current refresh will elect from the final roster on its next tick.
            Roster(node);
            Elect();
            nextTick = 0;
        }

        public bool Report(NetworkTaskKind kind, Thing target, WorkGiverDef giver = null)
        {
            if (target == null || target.Destroyed || target.MapHeld != Map || target.PositionHeld.Fogged(Map)) return false;
            if (State == null || coordinator == null) return false;
            List<NetworkTask> queue = kind == NetworkTaskKind.Combat || kind == NetworkTaskKind.Rescue ? State.war : State.normal;
            foreach (NetworkTask task in queue)
                if (task.kind == kind && task.targetId == target.ThingID) return false;
            var added = new NetworkTask { id = State.nextId++, kind = kind, giver = giver, reportedAt = Now };
            added.SetTarget(target);
            queue.Add(added);
            QR.Log($"network discovered #{added.id} {kind}: {target}");
            return true;
        }

        public bool Queued(Thing target)
        {
            if (target == null) return false;
            foreach (NetworkTask task in State.war) if (task.targetId == target.ThingID) return true;
            return false;
        }

        public void Release(Thing target)
        {
            if (target == null) return;
            for (int i = State.war.Count - 1; i >= 0; i--)
                if (State.war[i].targetId == target.ThingID)
                {
                    NetworkTask task = State.war[i];
                    if (task.assigneeId != null) assignments.Remove(task.assigneeId);
                    State.war.RemoveAt(i);
                }
        }

        public void Ping(Pawn attacker, Pawn victim)
        {
            if (attacker?.MapHeld != Map || !attacker.HostileTo(Faction.OfPlayer)) return;
            Report(NetworkTaskKind.Combat, attacker);
            Raid.Recheck(Map);
            Tick(force: true);
        }

        public int LastSurvey(IntVec3 cell) => surveys.TryGetValue(cell, out int tick) ? tick : int.MinValue / 2;

        public void Surveyed(IntVec3 cell)
        {
            surveys[cell] = Now;
            int index = State.surveyCells.IndexOf(cell);
            if (index < 0) { State.surveyCells.Add(cell); State.surveyTicks.Add(Now); }
            else State.surveyTicks[index] = Now;
        }

        public void Tick(bool force = false)
        {
            if (ticking || !force && Now < nextTick) return;
            ticking = true;
            try
            {
                nextTick = Now + Tuning.dispatchInterval;
                Roster(); Elect();
                if (coordinator == null) return;
                bool war = Raid.Active(Map) || State.war.Any(t => t.kind == NetworkTaskKind.Combat
                    && t.Target(Map) is Pawn p && p.Spawned && !p.Dead && !p.Downed && p.HostileTo(Faction.OfPlayer));
                if (war && !State.warActive) SuspendNormal();
                if (war != State.warActive)
                    QR.Log($"network mode: {(war ? "war; normal queue suspended" : "normal; revalidating backlog")}");
                State.warActive = war;
                Reconcile(State.normal);
                Reconcile(State.war);
                RebuildAssignments();
                bool hasPatient = Rescue.AnyPatient(Map);
                bool hasCombat = false;
                foreach (NetworkTask task in State.war)
                    if (task.kind == NetworkTaskKind.Combat) { hasCombat = true; break; }
                // Docked workers must step out before vanilla scanners/pathfinders can inspect them. Idle workers
                // survey anyway; medical/combat models leave only for their dedicated work.
                foreach (Pawn dog in dogs)
                {
                    RobodogWork work = RobodogWork.Of(dog);
                    bool deploy = IsWorker(dog) && !War || work.rescue && hasPatient
                        || work.combat && (War || hasCombat);
                    if (deploy && !dog.Spawned && IsAvailable(dog)) CompRobodog.Of(dog).stable.TryUndock(dog);
                }
                if (force || Now >= nextScan)
                {
                    nextScan = Now + Tuning.scanInterval;
                    foreach (Pawn dog in dogs)
                        if (Active(dog))
                        {
                            DogWork.ReportLocal(dog, this);
                            if (RobodogWork.Of(dog).combat) Combat.Spot(dog, this);
                        }
                    foreach (Pawn patient in Rescue.Patients(Map))
                        if (!patient.Position.Fogged(Map)) Report(NetworkTaskKind.Rescue, patient);
                    // A revealed active raid is a colony alert, not undiscovered hauling. Combatdogs must receive
                    // its targets even when every worker has already withdrawn beyond local scan range.
                    if (War)
                        foreach (IAttackTarget threat in Map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))
                            if (threat.Thing is Pawn enemy && GenHostility.IsActiveThreatToPlayer(threat))
                                Report(NetworkTaskKind.Combat, enemy);
                }
                ordered.Clear(); ordered.AddRange(State.war);
                ordered.Sort(taskComparison);
                AssignAll(ordered);
                if (!War)
                {
                    ordered.Clear(); ordered.AddRange(State.normal);
                    ordered.Sort(taskComparison);
                    AssignAll(ordered);
                }
                foreach (Pawn dog in dogs)
                {
                    if (!Active(dog) || !IsAvailable(dog) || assignments.ContainsKey(dog.ThingID)) continue;
                    Job idle = IdleJob(dog);
                    if (idle != null && idle != dog.CurJob) dog.jobs.StartJob(idle, JobCondition.InterruptForced);
                }
                SendReports();
            }
            finally { ticking = false; }
        }

        private int CompareTasks(NetworkTask a, NetworkTask b)
        {
            if (a.kind == NetworkTaskKind.Rescue && b.kind == NetworkTaskKind.Rescue)
            {
                Pawn x = a.Target(Map) as Pawn, y = b.Target(Map) as Pawn;
                if (x != null && y != null)
                {
                    int rank = Rescue.Rank(x).CompareTo(Rescue.Rank(y));
                    if (rank != 0) return rank;
                    int bleeding = y.health.hediffSet.BleedRateTotal.CompareTo(x.health.hediffSet.BleedRateTotal);
                    if (bleeding != 0) return bleeding;
                }
            }
            int kind = a.kind.CompareTo(b.kind);
            return kind != 0 ? kind : a.id.CompareTo(b.id);
        }

        private static void Reset(NetworkTask task)
        {
            task.assigneeId = null; task.jobId = -1; task.phase = NetworkTaskPhase.Queued;
            task.lastPosition = IntVec3.Invalid; task.toilIndex = -1; task.routeCost = 0f;
        }

        private void SuspendNormal()
        {
            foreach (NetworkTask task in State.normal)
            {
                if (task.assigneeId != null && dogById.TryGetValue(task.assigneeId, out Pawn dog)
                    && dog.CurJob?.loadID == task.jobId)
                    StopSafely(dog);
                Reset(task);
            }
            foreach (Pawn dog in dogs)
                if (Active(dog) && IsWorker(dog) && dog.CurJobDef != QR_DefOf.QR_BeingRidden
                    && (dog.CurJobDef == QR_DefOf.QR_Survey || dog.CurJobDef == JobDefOf.HaulToCell
                        || dog.CurJobDef == JobDefOf.HaulToContainer || dog.CurJobDef == JobDefOf.Clean
                        || dog.CurJobDef == QR_DefOf.QR_CarryToStable)) StopSafely(dog);
        }

        private static void StopSafely(Pawn dog)
        {
            dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            if (dog.carryTracker?.CarriedThing != null)
                dog.carryTracker.TryDropCarriedThing(dog.Position, ThingPlaceMode.Near, out _);
        }

        private bool Finished(NetworkTask task)
        {
            Thing target = task.Target(Map);
            if (target == null || !target.Spawned || target.Map != Map) return true;
            switch (task.kind)
            {
                case NetworkTaskKind.Haul: return target.IsInValidBestStorage();
                case NetworkTaskKind.Frame:
                case NetworkTaskKind.Blueprint:
                    if (!(target is IConstructible c)) return true;
                    foreach (ThingDefCountClass cost in c.TotalMaterialCost())
                        if (c.ThingCountNeeded(cost.thingDef) > 0) return false;
                    return true;
                case NetworkTaskKind.Retrieve: return !(target is Pawn down) || !CompRobodog.Of(down).PoweredDown;
                case NetworkTaskKind.Rescue: return !(target is Pawn patient) || !Rescue.Patients(Map).Contains(patient);
                case NetworkTaskKind.Combat:
                    return !(target is Pawn hostile) || hostile.Dead || hostile.IsPrisonerOfColony || !hostile.HostileTo(Faction.OfPlayer);
                case NetworkTaskKind.Clean: return !Map.areaManager.Home[target.Position];
                default: return false;
            }
        }

        private void Reconcile(List<NetworkTask> queue)
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                NetworkTask task = queue[i];
                if (task.assigneeId != null)
                {
                    if (dogById.TryGetValue(task.assigneeId, out Pawn dog) && Active(dog)
                        && dog.CurJob?.loadID == task.jobId && !dog.Drafted)
                    {
                        if (task.phase == NetworkTaskPhase.Assigned) task.phase = NetworkTaskPhase.Acknowledged;
                        else task.phase = NetworkTaskPhase.Working;
                        int toil = dog.jobs.curDriver?.CurToilIndex ?? -1;
                        int ticks = dog.jobs.curDriver?.ticksLeftThisToil ?? 0;
                        if (dog.Position != task.lastPosition || toil != task.toilIndex || ticks != task.toilTicks)
                        {
                            task.progressAt = Now; task.lastPosition = dog.Position; task.toilIndex = toil; task.toilTicks = ticks;
                        }
                        if (Now - task.progressAt <= Tuning.progressTimeout) continue;
                        QR.Log($"network #{task.id}: no progress from {dog}; released for reassignment");
                        StopSafely(dog);
                        task.retryAt = Now + Tuning.scanInterval;
                    }
                    else QR.Log($"network #{task.id}: assignment ended; revalidating unfinished work");
                    Reset(task);
                }
                if (Finished(task))
                {
                    QR.Log($"network #{task.id} {task.kind}: complete or no longer applicable");
                    queue.RemoveAt(i);
                }
            }
        }

        private void RebuildAssignments()
        {
            assignments.Clear();
            foreach (NetworkTask task in state.normal)
                if (task.assigneeId != null) assignments[task.assigneeId] = task;
            foreach (NetworkTask task in state.war)
                if (task.assigneeId != null) assignments[task.assigneeId] = task;
        }

        private bool Capable(Pawn dog, NetworkTask task)
        {
            RobodogWork work = RobodogWork.Of(dog);
            return task.kind == NetworkTaskKind.Rescue ? work.rescue
                : task.kind == NetworkTaskKind.Combat ? work.combat : IsWorker(dog);
        }

        private static Job JobFor(Pawn dog, NetworkTask task, Map map)
        {
            if (task.kind == NetworkTaskKind.Combat) return Combat.JobFor(dog, task.Target(map) as Pawn);
            if (task.kind == NetworkTaskKind.Rescue) return Rescue.JobFor(dog, task.Target(map) as Pawn);
            return DogWork.JobFor(dog, task);
        }

        private void AssignAll(List<NetworkTask> tasks)
        {
            foreach (NetworkTask task in tasks)
            {
                Thing target = task.Target(Map);
                if (task.assigneeId != null || Now < task.retryAt || target == null || !target.Spawned) continue;
                Pawn nearest = null;
                Job selected = null;
                float cost = float.MaxValue;
                foreach (Pawn dog in dogs)
                {
                    if (!dog.Spawned || !IsAvailable(dog) || assignments.ContainsKey(dog.ThingID) || !Capable(dog, task)) continue;
                    Job candidate = JobFor(dog, task, Map);
                    if (candidate == null) continue;
                    LocalTargetInfo destination = target;
                    PathEndMode mode = DogWork.EndMode(task);
                    if ((task.kind == NetworkTaskKind.Frame || task.kind == NetworkTaskKind.Blueprint)
                        && candidate.targetA.HasThing) { destination = candidate.targetA; mode = PathEndMode.ClosestTouch; }
                    float route;
                    using (PawnPath path = Map.pathFinder.FindPathNow(dog.Position, destination, TraverseParms.For(dog, Danger.Deadly), peMode: mode))
                        route = path.Found ? path.TotalCost : float.MaxValue;
                    if (route < cost || route == cost && nearest != null && dog.thingIDNumber < nearest.thingIDNumber)
                    {
                        if (selected != null) JobMaker.ReturnToPool(selected);
                        nearest = dog; selected = candidate; cost = route;
                    }
                    else JobMaker.ReturnToPool(candidate);
                }
                if (nearest == null)
                {
                    task.retryAt = Now + Tuning.scanInterval;
                    continue;
                }
                task.assigneeId = nearest.ThingID; task.jobId = selected.loadID;
                task.phase = NetworkTaskPhase.Assigned; task.reportedAt = task.progressAt = Now;
                task.lastPosition = nearest.Position; task.routeCost = cost;
                assignments[nearest.ThingID] = task;
                nearest.jobs.StartJob(selected, JobCondition.InterruptForced);
                if (nearest.CurJob?.loadID == task.jobId) task.phase = NetworkTaskPhase.Acknowledged;
                QR.Log($"network assigned #{task.id} {task.kind} to {nearest}; path {cost:F0}, job {task.jobId}");
            }
        }

        /// <summary>Dogs request/acknowledge centrally owned orders, never independently search a global work list.</summary>
        public Job TakeOrder(Pawn dog)
        {
            if (!Active(dog) || CompRobodog.Of(dog).WantsCharge || dog.Drafted) return null;
            if (assignments.TryGetValue(dog.ThingID, out NetworkTask task))
            {
                if (task.kind == NetworkTaskKind.Combat)
                {
                    if (dog.CurJob?.loadID == task.jobId
                        && (dog.CurJobDef == JobDefOf.Capture || dog.CurJobDef == QR_DefOf.QR_CarryToRoom))
                        return dog.CurJob;
                    Job next = Combat.JobFor(dog, task.Target(Map) as Pawn);
                    if (next != null)
                    {
                        if (dog.CurJob?.loadID == task.jobId && next.def == dog.CurJobDef
                            && dog.jobs.curDriver.IsContinuation(next))
                        {
                            JobMaker.ReturnToPool(next);
                            return dog.CurJob;
                        }
                        task.jobId = next.loadID; task.phase = NetworkTaskPhase.Assigned; task.progressAt = Now;
                        return next;
                    }
                }
                else if (dog.CurJob?.loadID == task.jobId) return dog.CurJob;
                Reset(task);
                assignments.Remove(dog.ThingID);
            }
            // Surveying must not keep an injured worker permanently away from a reachable repair dock.
            if (NeedsRepair(dog) && Idle(dog)) return null;
            // Player orders, riding and pre-cutover productive jobs finish normally; exploration cannot steal them.
            if (dog.CurJob != null && !IsAvailable(dog)) return dog.CurJob;
            return IdleJob(dog);
        }

        private Job IdleJob(Pawn dog)
        {
            if (IsWorker(dog) && War)
            {
                // War standby: hold its ground, never vanilla wandering. Docked/maintenance paths are elsewhere.
                if (dog.CurJobDef == JobDefOf.Wait && !dog.CurJob.playerForced)
                    return dog.CurJob;
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = Tuning.dispatchInterval;
                return wait;
            }
            if (IsWorker(dog))
            {
                if (dog.CurJobDef == QR_DefOf.QR_Survey)
                    return dog.CurJob;
                Job survey = Survey.JobFor(dog, this);
                if (survey != null)
                    return survey;
                // No reachable revealed survey area: do not fall through to vanilla wandering into fog.
                if (dog.CurJobDef == JobDefOf.Wait && !dog.CurJob.playerForced)
                    return dog.CurJob;
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = Tuning.dispatchInterval;
                return wait;
            }
            if (RobodogWork.Of(dog).combat && (War || State.war.Exists(t => t.kind == NetworkTaskKind.Combat)))
                return Combat.IdleJob(dog, this);
            return null; // The existing dock/nearest-room branches implement standby and maintenance.
        }

        private void SendReports()
        {
            foreach (Pawn dog in dogs)
            {
                if (!reports.TryGetValue(dog.ThingID, out DogReport report)) reports[dog.ThingID] = report = new DogReport();
                report.position = dog.PositionHeld; report.receivedAt = Now;
                assignments.TryGetValue(dog.ThingID, out NetworkTask task);
                report.taskId = task?.id ?? 0; report.phase = task?.phase ?? NetworkTaskPhase.Queued;
                if (task != null) task.reportedAt = Now;
            }
            goneReports.Clear();
            foreach (string id in reports.Keys) if (!dogById.ContainsKey(id)) goneReports.Add(id);
            foreach (string id in goneReports) reports.Remove(id);
        }

        public string Inspect(Pawn dog = null)
        {
            string node = coordinator?.LabelShortCap ?? "none";
            string summary = $"Network: {(War ? "war (normal suspended)" : "normal")}; coordinator: {node}\n"
                + $"Normal queue: {State.normal.Count}; war/emergency queue: {State.war.Count}";
            if (dog != null && assignments.TryGetValue(dog.ThingID, out NetworkTask task))
                summary += $"\nOrder #{task.id}: {task.kind}, {task.phase}; path cost {task.routeCost:F0}; last progress {Now - task.progressAt} ticks ago";
            else if (dog != null && reports.TryGetValue(dog.ThingID, out DogReport report))
                summary += $"\nReported position: {report.position}; {Now - report.receivedAt} ticks ago";
            return summary;
        }
    }
}
