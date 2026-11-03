using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Transient local focus-fire packs for Tier 3/4 Combatdogs. An eligible leader keeps the nearest Combatdogs of any
    /// tier within its XML pack range on the closest reachable standing hostile. Everything here is derived on each
    /// scheduler refresh from live pawns and the war queue: packs are never saved and reloads rebuild them.
    /// </summary>
    internal sealed class PackCombat
    {
        private static readonly Comparison<Pawn> LeaderOrder = (a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber);
        private readonly Comparison<Pawn> candidateOrder;

        private readonly DogNetwork network;
        private readonly Dictionary<Pawn, Pawn> targets = new Dictionary<Pawn, Pawn>();
        private readonly Dictionary<Pawn, Pawn> previousTargets = new Dictionary<Pawn, Pawn>();
        private readonly Dictionary<Pawn, int> startedJobs = new Dictionary<Pawn, int>();
        private readonly HashSet<Pawn> enemySet = new HashSet<Pawn>();
        private readonly List<Pawn> leaders = new List<Pawn>();
        private readonly List<Pawn> enemies = new List<Pawn>();
        private readonly List<Pawn> candidates = new List<Pawn>();
        private readonly List<Pawn> reserved = new List<Pawn>();
        private readonly List<Pawn> released = new List<Pawn>();
        private Pawn sortedAround;

        public PackCombat(DogNetwork network)
        {
            this.network = network;
            candidateOrder = CompareCandidates;
        }

        /// <summary>The standing hostile this dog's local pack currently shares, if it still qualifies.</summary>
        public Pawn Target(Pawn dog) =>
            dog != null && CanFight(dog) && targets.TryGetValue(dog, out Pawn target) && StandingEnemy(target) ? target : null;

        /// <summary>Whether the scheduler has given this dog a pack target this refresh.</summary>
        public bool Has(Pawn dog) => dog != null && targets.ContainsKey(dog);

        /// <summary>Remembers a job the dog took for its pack target, so the next refresh keeps an equivalent attack.</summary>
        public void Ordered(Pawn dog, Job job)
        {
            if (dog != null && job != null)
                startedJobs[dog] = job.loadID;
        }

        /// <summary>Recomputes packs, targets and attack jobs after the network's queues have been reconciled.</summary>
        public void Refresh()
        {
            previousTargets.Clear();
            foreach (KeyValuePair<Pawn, Pawn> pair in targets)
                previousTargets[pair.Key] = pair.Value;
            targets.Clear();
            reserved.Clear();

            if (network == null || !network.War)
            {
                ReleasePrevious();
                startedJobs.Clear();
                return;
            }

            enemies.Clear();
            enemySet.Clear();
            foreach (NetworkTask task in network.WarQueue)
                if (task.kind == NetworkTaskKind.Combat && task.Target(network.Map) is Pawn enemy
                    && StandingEnemy(enemy) && enemySet.Add(enemy))
                    enemies.Add(enemy);
            if (enemies.Count == 0)
            {
                ReleasePrevious();
                startedJobs.Clear();
                return;
            }

            leaders.Clear();
            foreach (Pawn dog in network.Dogs)
                if (IsLeader(dog))
                    leaders.Add(dog);
            leaders.Sort(LeaderOrder);

            // Without a leader there is no pack to keep dogs from the ordinary queue, so captures need no keeper.
            // With packs, a downed enemy stops being a focus-fire target: each due, unassigned capture order keeps
            // one nearby Combatdog free so the one-assignee queue can take it while the rest of the pack retargets.
            if (leaders.Count > 0)
            {
                int tick = Find.TickManager.TicksGame;
                foreach (NetworkTask task in network.WarQueue)
                    if (task.kind == NetworkTaskKind.Combat && task.assigneeId == null && tick >= task.retryAt
                        && task.Target(network.Map) is Pawn downed && DownedEnemy(downed))
                    {
                        Pawn keeper = CaptureKeeper(downed);
                        if (keeper != null)
                            reserved.Add(keeper);
                    }
            }
            foreach (Pawn leader in leaders)
            {
                // The keeper sits out one refresh so the normal capture order can take it.
                if (targets.ContainsKey(leader) || reserved.Contains(leader) || !CanFight(leader))
                    continue;
                float range = CombatTier.Of(leader)?.packRange ?? 0f;
                float rangeSquared = range * range;
                Pawn target = TargetFor(leader, rangeSquared);
                if (target == null)
                    continue;
                candidates.Clear();
                foreach (Pawn dog in network.Dogs)
                {
                    if (dog == leader || targets.ContainsKey(dog) || reserved.Contains(dog))
                        continue;
                    if ((dog.Position - leader.Position).LengthHorizontalSquared > rangeSquared || !CanFight(dog))
                        continue;
                    candidates.Add(dog);
                }
                sortedAround = leader;
                candidates.Sort(candidateOrder);
                targets[leader] = target;
                network.ReleaseForPack(leader);
                StartFighting(leader, target);
                foreach (Pawn dog in candidates)
                {
                    if (targets.ContainsKey(dog) || !dog.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                        continue;
                    targets[dog] = target;
                    network.ReleaseForPack(dog);
                    StartFighting(dog, target);
                }
            }

            // Pack refresh owns these attacks. Stop fire aimed at a target that died, went down or became unreachable,
            // and free any stale combat order from a dog that left its old pack.
            foreach (Pawn dog in reserved)
            {
                network.ReleaseForPack(dog);
                StopAttack(dog);
            }
            foreach (KeyValuePair<Pawn, Pawn> pair in previousTargets)
            {
                if (targets.TryGetValue(pair.Key, out Pawn current) && current == pair.Value)
                    continue;
                network.ReleaseForPack(pair.Key);
                StopFighting(pair.Key, pair.Value);
            }
            released.Clear();
            foreach (Pawn dog in startedJobs.Keys)
                if (!targets.ContainsKey(dog))
                    released.Add(dog);
            foreach (Pawn dog in released)
                startedJobs.Remove(dog);
        }

        /// <summary>
        /// The leader keeps its current enemy while it stays valid, else takes the nearest reachable enemy that no
        /// pack beyond its own coordination range is already fighting. Nearby leaders therefore share a focus target,
        /// while distant packs pick their own local enemies instead of converging across the map.
        /// </summary>
        private Pawn TargetFor(Pawn leader, float rangeSquared)
        {
            if (previousTargets.TryGetValue(leader, out Pawn previous) && enemySet.Contains(previous)
                && leader.CanReach(previous, PathEndMode.Touch, Danger.Deadly))
                return previous;
            return NearestReachable(leader, rangeSquared);
        }

        private Pawn NearestReachable(Pawn leader, float rangeSquared)
        {
            Pawn best = null, fallback = null;
            int bestDistance = int.MaxValue, bestId = int.MaxValue;
            int fallbackDistance = int.MaxValue, fallbackId = int.MaxValue;
            foreach (Pawn enemy in enemies)
            {
                if (!leader.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))
                    continue;
                int distance = (enemy.Position - leader.Position).LengthHorizontalSquared;
                if (ClaimedBeyond(leader, enemy, rangeSquared))
                {
                    if (distance > fallbackDistance || distance == fallbackDistance && enemy.thingIDNumber >= fallbackId)
                        continue;
                    fallback = enemy;
                    fallbackDistance = distance;
                    fallbackId = enemy.thingIDNumber;
                    continue;
                }
                if (distance > bestDistance || distance == bestDistance && enemy.thingIDNumber >= bestId)
                    continue;
                best = enemy;
                bestDistance = distance;
                bestId = enemy.thingIDNumber;
            }
            return best ?? fallback;
        }

        /// <summary>True when only dogs farther than the leader's pack range are already fighting this enemy.</summary>
        private bool ClaimedBeyond(Pawn leader, Pawn enemy, float rangeSquared)
        {
            bool targeted = false;
            foreach (KeyValuePair<Pawn, Pawn> pair in targets)
            {
                if (pair.Value != enemy)
                    continue;
                targeted = true;
                if ((pair.Key.Position - leader.Position).LengthHorizontalSquared <= rangeSquared)
                    return false;
            }
            return targeted;
        }

        /// <summary>
        /// The one free Combatdog held for a downed target: a follower if possible, nearest reachable first, then
        /// lowest ID. The network's normal capture assignment decides whether it can actually make the carry.
        /// </summary>
        private Pawn CaptureKeeper(Pawn downed)
        {
            Pawn best = null;
            bool bestLeads = false;
            int bestDistance = int.MaxValue, bestId = int.MaxValue;
            foreach (Pawn dog in network.Dogs)
            {
                if (reserved.Contains(dog) || !CanFight(dog)
                    || !dog.CanReserveAndReach(downed, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;
                bool leads = IsLeader(dog);
                int distance = (dog.Position - downed.Position).LengthHorizontalSquared;
                bool better;
                if (best == null)
                    better = true;
                else if (!bestLeads && leads)
                    better = false;
                else if (bestLeads && !leads)
                    better = true;
                else
                    better = distance < bestDistance || distance == bestDistance && dog.thingIDNumber < bestId;
                if (!better)
                    continue;
                best = dog;
                bestLeads = leads;
                bestDistance = distance;
                bestId = dog.thingIDNumber;
            }
            return best;
        }

        /// <summary>
        /// A Combatdog may join a pack while truly available, or while already fighting: the latter keeps a pack
        /// together across refreshes instead of collapsing every time its attack job is not an "idle" job.
        /// </summary>
        private bool CanFight(Pawn dog)
        {
            if (dog == null || !dog.Spawned || dog.Dead || dog.Downed || dog.Drafted || dog.InMentalState
                || dog.Faction != Faction.OfPlayer || dog.CurJob?.playerForced == true
                || dog.carryTracker?.CarriedThing != null || dog.ParentHolder is Pawn_CarryTracker)
                return false;
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || comp.PoweredDown || comp.WantsCharge || RobodogWork.Of(dog)?.combat != true
                || comp.stable != null && comp.stable.Spawned && comp.stable.BeingUpgraded(dog))
                return false;
            return DogNetwork.IsAvailable(dog) || Fighting(dog);
        }

        private bool IsLeader(Pawn dog) => CanFight(dog) && (CombatTier.Of(dog)?.packRange ?? 0f) > 0f;

        private static bool Fighting(Pawn dog)
        {
            Job job = dog.CurJob;
            if (job == null || job.playerForced)
                return false;
            return (job.def == JobDefOf.AttackStatic || job.def == JobDefOf.AttackMelee || job.def == JobDefOf.Goto)
                && job.targetA.Thing is Pawn;
        }

        /// <summary>Keeps an attack already aimed at the shared target; restarts only when the dog has another job.</summary>
        private void StartFighting(Pawn dog, Pawn target)
        {
            if (startedJobs.TryGetValue(dog, out int loadId) && dog.CurJob?.loadID == loadId)
                return;
            Job current = dog.CurJob;
            if (current != null && !current.playerForced && current.targetA.Thing == target
                && (current.def == JobDefOf.AttackStatic || current.def == JobDefOf.AttackMelee))
            {
                startedJobs[dog] = current.loadID;
                return;
            }
            // Same deferral as ordinary assignment after an allowed-area change: do not path on a stale grid.
            if (!network.PathInputsReady(dog))
                return;
            Job job = Combat.JobFor(dog, target);
            if (job == null)
            {
                targets.Remove(dog);
                startedJobs.Remove(dog);
                return;
            }
            startedJobs[dog] = job.loadID;
            dog.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private static void StopFighting(Pawn dog, Pawn target)
        {
            Job job = dog?.CurJob;
            if (job == null || job.playerForced || dog.Drafted || job.targetA.Thing != target)
                return;
            if (job.def == JobDefOf.AttackStatic || job.def == JobDefOf.AttackMelee || job.def == JobDefOf.Goto)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        private void ReleasePrevious()
        {
            foreach (KeyValuePair<Pawn, Pawn> pair in previousTargets)
            {
                network?.ReleaseForPack(pair.Key);
                StopFighting(pair.Key, pair.Value);
            }
            foreach (Pawn dog in reserved)
            {
                network?.ReleaseForPack(dog);
                StopAttack(dog);
            }
        }

        /// <summary>Frees a keeper from any ordinary attack so the network's capture assignment can take it this tick.</summary>
        private static void StopAttack(Pawn dog)
        {
            Job job = dog?.CurJob;
            if (job == null || job.playerForced || dog.Drafted)
                return;
            if (job.def == JobDefOf.AttackStatic || job.def == JobDefOf.AttackMelee
                || job.def == JobDefOf.Goto && job.targetA.Thing is Pawn)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
        }

        private int CompareCandidates(Pawn a, Pawn b)
        {
            int distance = (a.Position - sortedAround.Position).LengthHorizontalSquared
                .CompareTo((b.Position - sortedAround.Position).LengthHorizontalSquared);
            return distance != 0 ? distance : a.thingIDNumber.CompareTo(b.thingIDNumber);
        }

        private bool StandingEnemy(Pawn pawn) =>
            pawn != null && pawn.Spawned && pawn.Map == network.Map && !pawn.Dead && !pawn.Downed
            && !pawn.IsPrisonerOfColony && pawn.HostileTo(Faction.OfPlayer)
            && !pawn.Position.Fogged(pawn.Map);

        private bool DownedEnemy(Pawn pawn) =>
            pawn != null && pawn.Spawned && pawn.Map == network.Map && !pawn.Dead && pawn.Downed
            && !(pawn.ParentHolder is Pawn_CarryTracker) && !pawn.IsPrisonerOfColony
            && pawn.HostileTo(Faction.OfPlayer);
    }
}
