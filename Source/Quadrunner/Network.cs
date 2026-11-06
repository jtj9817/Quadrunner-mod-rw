using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public enum NetworkTaskKind { Retrieve, Frame, Blueprint, Haul, Clean, Rescue, Combat, Recover, Fire }
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
        /// <summary>Casualty orders: who was downed or killed, the cell that was last reported, and whether the
        /// target is now the real wreck. Never reassigned or clobbered by carrier progress.</summary>
        public string casualtyId;
        public IntVec3 lastKnown = IntVec3.Invalid;
        public bool casualtyDead;
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

        internal void SetTarget(Thing thing, string thingId) { target = thing; targetId = thingId; }

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
            Scribe_Values.Look(ref casualtyId, "casualty");
            Scribe_Values.Look(ref lastKnown, "lastKnown", IntVec3.Invalid);
            Scribe_Values.Look(ref casualtyDead, "casualtyDead");
            Scribe_Values.Look(ref routeCost, "routeCost");
            Scribe_Values.Look(ref toilIndex, "toilIndex", -1);
            Scribe_Values.Look(ref toilTicks, "toilTicks");
        }
    }

    /// <summary>Lives on exactly one stable or active dog. Removing the mod leaves no saved map component.</summary>
    public sealed class NetworkState : IExposable
    {
        public List<NetworkTask> normal = new List<NetworkTask>(), war = new List<NetworkTask>();
        public int nextId = 1;
        public bool warActive;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref normal, "normal", LookMode.Deep);
            Scribe_Collections.Look(ref war, "war", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "nextId", 1);
            Scribe_Values.Look(ref warActive, "warActive");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                normal = normal ?? new List<NetworkTask>();
                war = war ?? new List<NetworkTask>();
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
        private readonly List<NetworkTask> ordered = new List<NetworkTask>();
        private readonly List<string> goneReports = new List<string>();
        private readonly Comparison<NetworkTask> taskComparison;
        private readonly PackCombat packs;
        private List<(int id, int firstTick)> pathAreas;
        private NetworkState state;
        private Thing coordinator;
        private int nextTick, nextScan;
        private bool ticking;

        private DogNetwork(Map map) { Map = map; Survey = new Survey(this); taskComparison = CompareTasks; packs = new PackCombat(this); }
        public Map Map { get; }
        public Survey Survey { get; }
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

        /// <summary>Vanilla 1.6 gathers path grids at most once per tick. A newly assigned allowed area can be
        /// absent from an already-gathered grid, so keep requests pending until a later tick can gather it.
        /// Only transient area IDs are retained; unrestricted requests allocate nothing.</summary>
        internal bool PathInputsReady(Pawn dog)
        {
            Area area = PathUtility.GetAllowedArea(dog);
            if (area == null) return true;
            if (pathAreas == null) pathAreas = new List<(int, int)>(Map.areaManager.AllAreas.Count);
            foreach (var known in pathAreas)
                if (known.id == area.ID)
                    return Now != known.firstTick;
            for (int i = pathAreas.Count - 1; i >= 0; i--)
            {
                bool live = false;
                foreach (Area current in Map.areaManager.AllAreas)
                    if (current.ID == pathAreas[i].id) { live = true; break; }
                if (!live) pathAreas.RemoveAt(i);
            }
            pathAreas.Add((area.ID, Now));
            return false;
        }

        public static DogNetwork For(Map map)
        {
            if (map == null) return null;
            if (!networks.TryGetValue(map, out DogNetwork network))
            {
                foreach (Map gone in networks.Keys.Where(m => !Find.Maps.Contains(m)).ToList())
                {
                    networks[gone].Survey.Dispose();
                    networks.Remove(gone);
                }
                networks[map] = network = new DogNetwork(map);
            }
            return network;
        }

        public static void ClearAll()
        {
            foreach (DogNetwork network in networks.Values) network.Survey.Dispose();
            networks.Clear();
        }
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
            }
            if (elected == coordinator && elected != null) return;
            if (coordinator != null) Store(coordinator, null);
            foreach (Building_Stable stable in stables) stable.networkState = null;
            foreach (Pawn dog in dogs) CompRobodog.Of(dog).networkState = null;
            coordinator = elected;
            if (elected == null)
            {
                state.normal.Clear(); state.war.Clear();
                state.warActive = false;
                assignments.Clear(); reports.Clear(); Survey.Clear();
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
            List<NetworkTask> queue = kind == NetworkTaskKind.Combat || kind == NetworkTaskKind.Rescue || kind == NetworkTaskKind.Fire
                ? State.war : State.normal;
            // ThingID constructs a string; do not rebuild it for every queued task or again on insertion.
            string targetId = target.ThingID;
            foreach (NetworkTask task in queue)
                if (task.kind == kind && task.targetId == targetId) return false;
            var added = new NetworkTask { id = State.nextId++, kind = kind, giver = giver, reportedAt = Now };
            added.SetTarget(target, targetId);
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

        /// <summary>
        /// A downed or powered-down player Combatdog tells the elected node itself, wherever it lies: a local worker
        /// scan would only ever find casualties inside its own radius. One order per dog, however often it reports.
        /// </summary>
        public bool ReportCasualty(Pawn dog)
        {
            if (dog == null || dog.Dead || !dog.Spawned || dog.MapHeld != Map
                || RobodogWork.Of(dog)?.combat != true)
                return false;
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || !dog.Downed && !comp.PoweredDown)
                return false;
            return ReportCasualty(dog, null);
        }

        /// <summary>
        /// A killed Combatdog reports the corpse vanilla just made for it. Nothing is created or revived here: the
        /// order tracks that wreck's identity and stops applying the moment it is destroyed or no longer exists.
        /// </summary>
        public bool ReportDeath(Pawn dog)
        {
            if (dog == null || !dog.Dead || RobodogWork.Of(dog)?.combat != true)
                return false;
            Corpse wreck = dog.Corpse;
            if (wreck == null || wreck.Destroyed || !wreck.Spawned || wreck.Map != Map)
                return false;
            return ReportCasualty(dog, wreck);
        }

        private bool ReportCasualty(Pawn dog, Corpse wreck)
        {
            if (dog.Faction != Faction.OfPlayer)
                return false;
            if (State == null || coordinator == null)
                return false;
            bool dead = wreck != null;
            NetworkTask existing = null, staleRetrieve = null;
            foreach (NetworkTask task in State.normal)
            {
                if (task.kind == NetworkTaskKind.Recover && task.casualtyId == dog.ThingID)
                    existing = existing ?? task;
                else if (task.kind == NetworkTaskKind.Retrieve && task.targetId == dog.ThingID)
                    staleRetrieve = task;
            }
            // Older saves may hold an ordinary retrieval for this Combatdog; it becomes the central order in place,
            // keeping its carrier and id. Any duplicate ordinary order for the same dog is dropped.
            if (existing == null && staleRetrieve != null)
            {
                existing = staleRetrieve;
                existing.kind = NetworkTaskKind.Recover;
                existing.casualtyId = dog.ThingID;
            }
            else if (staleRetrieve != null)
            {
                if (staleRetrieve.assigneeId != null) assignments.Remove(staleRetrieve.assigneeId);
                State.normal.Remove(staleRetrieve);
            }
            if (existing == null)
            {
                var added = new NetworkTask
                {
                    id = State.nextId++, kind = NetworkTaskKind.Recover, casualtyId = dog.ThingID,
                    casualtyDead = dead, reportedAt = Now,
                    lastKnown = dead ? wreck.PositionHeld : dog.PositionHeld,
                };
                Thing target = dead ? (Thing)wreck : dog;
                added.SetTarget(target, target.ThingID);
                State.normal.Add(added);
                QR.Log($"network casualty #{added.id}: {dog} {(dead ? "wreck" : "downed")} at {added.lastKnown}");
                return true;
            }
            if (existing.casualtyDead)
            {
                if (dead && existing.targetId != wreck.ThingID && existing.assigneeId == null)
                {
                    existing.lastKnown = wreck.PositionHeld;
                    existing.SetTarget(wreck, wreck.ThingID);
                    return true;
                }
                return false;
            }
            if (dead)
            {
                existing.casualtyDead = true;
                existing.lastKnown = wreck.PositionHeld;
                existing.SetTarget(wreck, wreck.ThingID);
                QR.Log($"network casualty #{existing.id}: {dog} died; tracking its wreck");
                return true;
            }
            // Still down: identity stays the pawn. The last-known cell is only refreshed while nobody owns the trip.
            if (existing.assigneeId == null)
            {
                existing.lastKnown = dog.PositionHeld;
                existing.SetTarget(dog, dog.ThingID);
            }
            return false;
        }

        public void Ping(Pawn attacker, Pawn victim)
        {
            if (attacker?.MapHeld != Map || !attacker.HostileTo(Faction.OfPlayer)) return;
            Report(NetworkTaskKind.Combat, attacker);
            Raid.Recheck(Map);
            Tick(force: true);
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
                if (force || Now >= nextScan)
                {
                    nextScan = Now + Tuning.scanInterval;
                    foreach (Pawn dog in dogs)
                        if (Active(dog))
                        {
                            DogWork.ReportLocal(dog, this);
                            if (RobodogWork.Of(dog).combat) Combat.Spot(dog, this);
                        }
                    // A casualty reports itself: this is independent of every worker's scan radius and stays in the
                    // central state across war, reload and coordinator transfer.
                    foreach (Pawn dog in dogs)
                        if (RobodogWork.Of(dog)?.combat == true)
                            ReportCasualty(dog);
                    foreach (Pawn patient in Rescue.Patients(Map))
                        if (!patient.Position.Fogged(Map)) Report(NetworkTaskKind.Rescue, patient);
                    // Home-area fires are a colony alarm too: reported map-wide, like the downed (Report skips fog).
                    ReportFires();
                    // A revealed active raid is a colony alert, not undiscovered hauling. Combatdogs must receive
                    // its targets even when every worker has already withdrawn beyond local scan range.
                    if (War)
                        foreach (IAttackTarget threat in Map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))
                            if (threat.Thing is Pawn enemy && GenHostility.IsActiveThreatToPlayer(threat))
                                Report(NetworkTaskKind.Combat, enemy);
                }
                // After this tick's reports, so a fire or patient found now meets a medic already out. Medics deploy
                // only for work assignable this tick: one undocked during a retry backoff has no order, docks again,
                // and its despawn forces another tick that undocks it again, until the backoff ends.
                bool hasCombat = false, hasRescueWork = false;
                foreach (NetworkTask task in State.war)
                {
                    if (task.kind == NetworkTaskKind.Combat && task.assigneeId == null && Now >= task.retryAt)
                        hasCombat = true;
                    else if (task.kind != NetworkTaskKind.Combat && task.assigneeId == null && Now >= task.retryAt)
                        hasRescueWork = true;
                }
                // Casualty recovery is ordinary-queue work: its carriers deploy only outside a war.
                bool hasRecovery = !War && State.normal.Any(t => t.kind == NetworkTaskKind.Recover
                    && t.assigneeId == null && Now >= t.retryAt);
                // Docked workers must step out before vanilla scanners/pathfinders can inspect them. Idle workers
                // survey anyway; medical/combat models leave only for their dedicated work.
                foreach (Pawn dog in dogs)
                {
                    RobodogWork work = RobodogWork.Of(dog);
                    bool deploy = (IsWorker(dog) && !War) || (work.rescue && (hasRescueWork || hasRecovery))
                        || (work.combat && (War || hasCombat));
                    if (deploy && !dog.Spawned && IsAvailable(dog)) CompRobodog.Of(dog).stable.TryUndock(dog);
                }
                // Packs re-form after new reports and undock decisions, before one-assignee assignment: members are
                // then unavailable to ordinary tasks, while a keeper left out for a just-downed target can still take
                // its normal capture order.
                packs.Refresh();
                ordered.Clear(); ordered.AddRange(State.war);
                ordered.Sort(taskComparison);
                if (AssignAll(ordered, hasPatient))
                {
                    // Each deployed medic chooses from the fire backlog, not the oldest fire choosing a medic.
                    foreach (Pawn dog in dogs)
                    {
                        Job fire = TakeFireOrder(dog);
                        if (fire != null) dog.jobs.StartJob(fire, JobCondition.InterruptForced);
                    }
                    if (!War)
                    {
                        ordered.Clear(); ordered.AddRange(State.normal);
                        ordered.Sort(taskComparison);
                        AssignAll(ordered, hasPatient);
                    }
                }
                foreach (Pawn dog in dogs)
                {
                    if (!Active(dog) || !IsAvailable(dog) || assignments.ContainsKey(dog.ThingID) || packs.Has(dog)) continue;
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
            int kind = Rank(a.kind).CompareTo(Rank(b.kind));
            if (kind != 0) return kind;
            // A downed enemy is one ordinary capture trip, not a focus-fire target: dispatch it before standing fights
            // so a pack's keeper can take the capture while the rest of the pack moves on.
            if (a.kind == NetworkTaskKind.Combat && b.kind == NetworkTaskKind.Combat)
            {
                bool x = a.Target(Map) is Pawn left && left.Downed;
                bool y = b.Target(Map) is Pawn right && right.Downed;
                if (x != y) return x ? -1 : 1;
            }
            return a.id.CompareTo(b.id);
        }

        /// <summary>Casualty recovery outranks every ordinary kind once the war is over; war work keeps its own order.</summary>
        private static int Rank(NetworkTaskKind kind) => kind == NetworkTaskKind.Recover ? -1 : (int)kind;

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
                if (Active(dog) && (dog.CurJobDef == QR_DefOf.QR_CarryToStable
                    || IsWorker(dog) && dog.CurJobDef != QR_DefOf.QR_BeingRidden
                        && (dog.CurJobDef == QR_DefOf.QR_Survey || dog.CurJobDef == JobDefOf.HaulToCell
                            || dog.CurJobDef == JobDefOf.HaulToContainer || dog.CurJobDef == JobDefOf.Clean
                            || dog.CurJobDef == QR_DefOf.QR_CleanWithHose)))
                    StopSafely(dog);
        }

        private static void StopSafely(Pawn dog)
        {
            dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            if (dog.carryTracker?.CarriedThing != null)
                dog.carryTracker.TryDropCarriedThing(dog.Position, ThingPlaceMode.Near, out _);
        }

        private bool Finished(NetworkTask task)
        {
            if (task.kind == NetworkTaskKind.Recover)
                return CasualtyFinished(task);
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
                case NetworkTaskKind.Fire: return !Map.areaManager.Home[target.Position];
                default: return false;
            }
        }

        /// <summary>
        /// A living casualty is done when it is no longer downed or when it is docked; a dead one only when its real
        /// wreck is gone. A pawn that died since the report is tracked by the wreck it actually left (if any): no
        /// order ever revives a dog or conjures a replacement wreck.
        /// </summary>
        private bool CasualtyFinished(NetworkTask task)
        {
            if (task.casualtyDead)
            {
                Thing target = task.Target(Map);
                // A cached reference survives after delivery; only a spawned corpse is still recoverable.
                return !(target is Corpse wreck) || !wreck.Spawned || wreck.Map != Map
                    || wreck.InnerPawn == null || wreck.InnerPawn.ThingID != task.casualtyId;
            }
            Pawn pawn = FindPawn(task.casualtyId);
            if (pawn == null || pawn.Dead)
                return !ConvertCasualtyToWreck(task);
            if (pawn.ParentHolder is Building_Stable)
                return true;
            // Carried or otherwise held: an active assignment keeps working; an unowned one stays queued.
            return pawn.Spawned && !pawn.Downed && CompRobodog.Of(pawn)?.PoweredDown != true;
        }

        private Pawn FindPawn(string id)
        {
            foreach (Pawn pawn in Map.mapPawns.AllPawns)
                if (pawn.ThingID == id)
                    return pawn;
            return null;
        }

        /// <summary>The real corpse left by the reported pawn, if vanilla still has it. Never fabricates one.</summary>
        private bool ConvertCasualtyToWreck(NetworkTask task)
        {
            foreach (Corpse wreck in Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>())
                if (wreck.InnerPawn != null && wreck.InnerPawn.ThingID == task.casualtyId)
                {
                    task.casualtyDead = true;
                    task.lastKnown = wreck.PositionHeld;
                    task.SetTarget(wreck, wreck.ThingID);
                    QR.Log($"network casualty #{task.id}: {task.casualtyId} is a wreck at {wreck.PositionHeld}");
                    return true;
                }
            return false;
        }

        private void Reconcile(List<NetworkTask> queue)
        {
            for (int i = queue.Count - 1; i >= 0; i--)
            {
                NetworkTask task = queue[i];
                // Native Goto watches its destination cell, not the fire retained in target B.
                if (task.kind == NetworkTaskKind.Fire && Finished(task))
                {
                    if (task.assigneeId != null && dogById.TryGetValue(task.assigneeId, out Pawn responder)
                        && responder.CurJob?.loadID == task.jobId)
                        StopSafely(responder);
                    queue.RemoveAt(i);
                    continue;
                }
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

        private bool Capable(Pawn dog, NetworkTask task, bool hasPatient)
        {
            RobodogWork work = RobodogWork.Of(dog);
            // Deferred medical emergencies also reserve medics; retry backoff must not send them to a wreck first.
            if (task.kind == NetworkTaskKind.Recover)
                return (IsWorker(dog) || work.rescue && !hasPatient)
                    && !dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any();
            return task.kind == NetworkTaskKind.Rescue || task.kind == NetworkTaskKind.Fire ? work.rescue
                : task.kind == NetworkTaskKind.Combat ? work.combat : IsWorker(dog);
        }

        private static Job JobFor(Pawn dog, NetworkTask task, Map map)
        {
            if (task.kind == NetworkTaskKind.Recover) return Retrieval.RecoveryJobFor(dog, task);
            if (task.kind == NetworkTaskKind.Combat) return Combat.JobFor(dog, task.Target(map) as Pawn);
            if (task.kind == NetworkTaskKind.Rescue) return Rescue.JobFor(dog, task.Target(map) as Pawn);
            if (task.kind == NetworkTaskKind.Fire) return Firefighting.JobFor(dog, task.Target(map) as Fire);
            return DogWork.JobFor(dog, task);
        }

        /// <summary>
        /// A pack refresh takes over a dog's attack. Free its saved combat order (never a noncombat order) so the
        /// one-assignee queue can reuse it this same refresh without a stale assignee blocking the dog; a downed
        /// combat target then becomes an ordinary capture order again.
        /// </summary>
        internal void ReleaseForPack(Pawn dog)
        {
            if (dog == null || !assignments.TryGetValue(dog.ThingID, out NetworkTask task)) return;
            if (task.kind != NetworkTaskKind.Combat) return;
            Reset(task);
            assignments.Remove(dog.ThingID);
        }

        private void ReportFires()
        {
            foreach (Thing thing in Map.listerThings.ThingsOfDef(ThingDefOf.Fire))
                if (thing is Fire fire && Firefighting.Eligible(fire, Map))
                    Report(NetworkTaskKind.Fire, fire);
        }

        /// <summary>Selection only: claim the nearest unowned fire, never start jobs inside native evaluation.</summary>
        private Job TakeFireOrder(Pawn dog, bool refresh = false)
        {
            if (!Active(dog) || RobodogWork.Of(dog)?.rescue != true || dog.Drafted || dog.InMentalState
                || CompRobodog.Of(dog).WantsCharge || dog.CurJob?.playerForced == true
                || dog.carryTracker?.CarriedThing != null || assignments.ContainsKey(dog.ThingID)
                || !IsAvailable(dog) && !Firefighting.AimedAt(dog.CurJob) || !PathInputsReady(dog))
                return null;
            if (refresh) ReportFires();
            // Let the ordinary triage scheduler take a waiting patient before claiming another fire.
            foreach (NetworkTask task in State.war)
                if (task.kind == NetworkTaskKind.Rescue && task.assigneeId == null && Now >= task.retryAt
                    && !Finished(task))
                    return null;
            NetworkTask nearest = null;
            Job selected = null;
            int distance = int.MaxValue, targetId = int.MaxValue;
            foreach (NetworkTask task in State.war)
            {
                if (task.kind != NetworkTaskKind.Fire || task.assigneeId != null || Now < task.retryAt
                    || !(task.Target(Map) is Fire fire) || !Firefighting.Eligible(fire, Map))
                    continue;
                int local = (fire.Position - dog.Position).LengthHorizontalSquared;
                if (local > distance || local == distance && fire.thingIDNumber >= targetId
                    || !dog.CanReach(fire, PathEndMode.Touch, Danger.Deadly))
                    continue;
                Job candidate = Firefighting.JobFor(dog, fire);
                if (candidate == null) continue;
                if (selected != null) JobMaker.ReturnToPool(selected);
                nearest = task; selected = candidate; distance = local; targetId = fire.thingIDNumber;
            }
            if (nearest == null) return null;
            Assign(dog, nearest, selected, 0f);
            CompRobodog.Of(dog).fireStandbyUntil = 0;
            return selected;
        }

        private void Assign(Pawn dog, NetworkTask task, Job job, float cost)
        {
            task.assigneeId = dog.ThingID; task.jobId = job.loadID;
            task.phase = NetworkTaskPhase.Assigned; task.reportedAt = task.progressAt = Now;
            task.lastPosition = dog.Position; task.routeCost = cost;
            assignments[task.assigneeId] = task;
            QR.Log($"network assigned #{task.id} {task.kind} to {dog}; path {cost:F0}, job {task.jobId}");
        }

        private bool AssignAll(List<NetworkTask> tasks, bool hasPatient)
        {
            foreach (NetworkTask task in tasks)
            {
                if (task.kind == NetworkTaskKind.Fire) continue;
                Thing target = task.Target(Map);
                if (task.assigneeId != null || Now < task.retryAt || target == null || !target.Spawned) continue;
                Pawn nearest = null;
                Job selected = null;
                float cost = float.MaxValue;
                bool deferred = false, docked = false;
                foreach (Pawn dog in dogs)
                {
                    if (!IsAvailable(dog) || assignments.ContainsKey(dog.ThingID) || packs.Has(dog) || !Capable(dog, task, hasPatient)) continue;
                    if (!dog.Spawned) { docked = true; continue; }
                    if (!PathInputsReady(dog)) { deferred = true; continue; }
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
                if (deferred)
                {
                    // Do not silently exclude an unranked worker or let a lower-priority task pass this one.
                    if (selected != null) JobMaker.ReturnToPool(selected);
                    return false;
                }
                if (nearest == null)
                {
                    // A capable dog still docked is not a failed attempt: it undocks on the next tick.
                    if (!docked) task.retryAt = Now + Tuning.scanInterval;
                    continue;
                }
                Assign(nearest, task, selected, cost);
                nearest.jobs.StartJob(selected, JobCondition.InterruptForced);
                if (nearest.CurJob?.loadID == task.jobId) task.phase = NetworkTaskPhase.Acknowledged;
            }
            return true;
        }

        /// <summary>Dogs request/acknowledge centrally owned orders, never independently search a global work list.</summary>
        public Job TakeOrder(Pawn dog)
        {
            if (!Active(dog) || CompRobodog.Of(dog).WantsCharge || dog.Drafted) return null;
            // A pack target is a standing enemy only: downed targets fall through to the ordinary capture order.
            Pawn packTarget = packs.Target(dog);
            if (packTarget != null && dog.CurJob?.playerForced != true && PathInputsReady(dog))
            {
                ReleaseForPack(dog);
                Job shared = Combat.JobFor(dog, packTarget);
                if (shared != null)
                {
                    if (dog.CurJob?.targetA.Thing == packTarget && shared.def == dog.CurJobDef
                        && dog.jobs.curDriver != null && dog.jobs.curDriver.IsContinuation(shared))
                    {
                        JobMaker.ReturnToPool(shared);
                        return dog.CurJob;
                    }
                    return shared;
                }
            }
            if (assignments.TryGetValue(dog.ThingID, out NetworkTask task))
            {
                // Ranged orders (fighting, foaming a fire) walk into range, then fire: the next leg is built here.
                if (task.kind == NetworkTaskKind.Combat || task.kind == NetworkTaskKind.Fire)
                {
                    if (dog.CurJob?.loadID == task.jobId
                        && (dog.CurJobDef == JobDefOf.Capture || dog.CurJobDef == QR_DefOf.QR_CarryToRoom))
                        return dog.CurJob;
                    Job next = JobFor(dog, task, Map);
                    if (next != null)
                    {
                        if (task.kind == NetworkTaskKind.Fire) CompRobodog.Of(dog).fireStandbyUntil = 0;
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
            Job fireOrder = TakeFireOrder(dog, refresh: true);
            if (fireOrder != null) return fireOrder;
            // Surveying must not keep an injured worker permanently away from a reachable repair dock.
            if (NeedsRepair(dog) && Idle(dog)) return null;
            // Player orders, riding and pre-cutover productive jobs finish normally; exploration cannot steal them.
            if (dog.CurJob != null && !IsAvailable(dog) && !Firefighting.AimedAt(dog.CurJob)) return dog.CurJob;
            return IdleJob(dog);
        }

        private Job IdleJob(Pawn dog)
        {
            if (IsWorker(dog) && War)
            {
                // Recall workers before standing by; only an unavailable dock means holding position.
                Job dock = JobGiver_Dock.DockJob(dog, allowFallback: true);
                if (dock != null)
                    return dock;
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
                Job survey = Survey.JobFor(dog);
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
            if (RobodogWork.Of(dog).rescue)
                return Firefighting.StandbyJob(dog);
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
