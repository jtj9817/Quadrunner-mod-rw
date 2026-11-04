using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Transient local focus-fire packs for Tier 3/4 Combatdogs. An eligible leader recruits Combatdogs of any tier
    /// within its XML pack range; new focus is the reachable standing hostile nearest to any member. Selections
    /// derive from live pawns and the existing war queue, at dispatch refreshes and between native combat jobs.
    /// </summary>
    internal sealed class PackCombat
    {
        private static readonly Comparison<Pawn> LeaderOrder = (a, b) => a.thingIDNumber.CompareTo(b.thingIDNumber);

        private readonly DogNetwork network;
        private readonly Dictionary<Pawn, Pawn> targets = new Dictionary<Pawn, Pawn>();
        private readonly Dictionary<Pawn, Pawn> previousTargets = new Dictionary<Pawn, Pawn>();
        private readonly HashSet<Pawn> enemySet = new HashSet<Pawn>();
        private readonly List<Pawn> leaders = new List<Pawn>();
        private readonly List<Pawn> enemies = new List<Pawn>();
        private readonly List<Pawn> candidates = new List<Pawn>();
        private readonly List<Pawn> reserved = new List<Pawn>();
        private int refreshedAt = -1;
        private bool refreshing;

        public PackCombat(DogNetwork network) => this.network = network;

        /// <summary>
        /// Acquire the next queued target at the native job boundary, before idle/guard can send the dog home.
        /// Selection-only refreshes never start/end jobs from inside another job's think-tree evaluation.
        /// </summary>
        public Pawn Target(Pawn dog)
        {
            if (!CanFight(dog))
                return null;
            bool hadTarget = targets.TryGetValue(dog, out Pawn target);
            if (hadTarget && StandingEnemy(target) && dog.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return target;
            if (!refreshing && (hadTarget || refreshedAt != Find.TickManager.TicksGame))
                Refresh(issueOrders: false);
            return targets.TryGetValue(dog, out target) && StandingEnemy(target) ? target : null;
        }

        /// <summary>Whether the scheduler has given this dog a pack target this refresh.</summary>
        public bool Has(Pawn dog) => dog != null && targets.ContainsKey(dog);

        /// <summary>Recomputes packs after reconciliation, or selection-only when an attack ends between ticks.</summary>
        public void Refresh(bool issueOrders = true)
        {
            if (refreshing)
                return;
            refreshing = true;
            try { Rebuild(issueOrders); }
            finally { refreshing = false; }
        }

        private void Rebuild(bool issueOrders)
        {
            refreshedAt = Find.TickManager.TicksGame;
            previousTargets.Clear();
            foreach (KeyValuePair<Pawn, Pawn> pair in targets)
                previousTargets[pair.Key] = pair.Value;
            targets.Clear();
            reserved.Clear();

            if (network == null || !network.War)
            {
                if (issueOrders) ReleasePrevious();
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
                if (issueOrders) ReleasePrevious();
                return;
            }

            leaders.Clear();
            foreach (Pawn dog in network.Dogs)
                if (IsLeader(dog))
                    leaders.Add(dog);
            leaders.Sort(LeaderOrder);

            // Only the scheduled assignment pass reserves carriers. Keep leaders fighting while standing threats
            // remain; a native job-boundary refresh must hand out the next fight, not an unassigned capture wait.
            if (issueOrders && leaders.Count > 0)
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
                candidates.Clear();
                candidates.Add(leader);
                foreach (Pawn dog in network.Dogs)
                {
                    if (dog == leader || targets.ContainsKey(dog) || reserved.Contains(dog))
                        continue;
                    if ((dog.Position - leader.Position).LengthHorizontalSquared > rangeSquared || !CanFight(dog))
                        continue;
                    candidates.Add(dog);
                }
                Pawn target = TargetFor(leader);
                if (target == null)
                    continue;
                foreach (Pawn dog in candidates)
                {
                    if (!dog.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                        continue;
                    targets[dog] = target;
                    if (!issueOrders)
                        continue;
                    network.ReleaseForPack(dog);
                    StartFighting(dog, target);
                }
            }

            if (!issueOrders)
                return;
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
        }

        /// <summary>
        /// Keep a valid focus until it falls; otherwise rank queued enemies by the nearest reachable pack member,
        /// irrespective of tier. Squared tile distance needs no paths/allocations for ranking; reachability rejects
        /// sealed targets. Distant packs remain independent and may naturally choose the same nearest enemy.
        /// </summary>
        private Pawn TargetFor(Pawn leader)
        {
            if (previousTargets.TryGetValue(leader, out Pawn previous) && enemySet.Contains(previous)
                && leader.CanReach(previous, PathEndMode.Touch, Danger.Deadly))
                return previous;
            Pawn best = null;
            int bestDistance = int.MaxValue, bestId = int.MaxValue;
            foreach (Pawn enemy in enemies)
            {
                if (!leader.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))
                    continue;
                int distance = int.MaxValue;
                foreach (Pawn member in candidates)
                {
                    int local = (enemy.Position - member.Position).LengthHorizontalSquared;
                    if (local < distance && member.CanReach(enemy, PathEndMode.Touch, Danger.Deadly))
                        distance = local;
                }
                if (distance > bestDistance || distance == bestDistance && enemy.thingIDNumber >= bestId)
                    continue;
                best = enemy;
                bestDistance = distance;
                bestId = enemy.thingIDNumber;
            }
            return best;
        }

        /// <summary>
        /// One non-leading Combatdog held for a downed target, nearest reachable first, then lowest ID.
        /// Leaders keep the offensive active; once no standing enemies remain all tiers can capture normally.
        /// </summary>
        private Pawn CaptureKeeper(Pawn downed)
        {
            Pawn best = null;
            int bestDistance = int.MaxValue, bestId = int.MaxValue;
            foreach (Pawn dog in network.Dogs)
            {
                if (reserved.Contains(dog) || !CanFight(dog) || (CombatTier.Of(dog)?.packRange ?? 0f) > 0f
                    || !dog.CanReserveAndReach(downed, PathEndMode.ClosestTouch, Danger.Deadly))
                    continue;
                int distance = (dog.Position - downed.Position).LengthHorizontalSquared;
                bool better = best == null || distance < bestDistance
                    || distance == bestDistance && dog.thingIDNumber < bestId;
                if (!better)
                    continue;
                best = dog;
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
            return (job.def == JobDefOf.AttackStatic || job.def == JobDefOf.AttackMelee) && job.targetA.Thing is Pawn
                || job.def == JobDefOf.Goto && job.targetB.Thing is Pawn;
        }

        /// <summary>Keeps an attack already aimed at the shared target; restarts only when the dog has another job.</summary>
        private void StartFighting(Pawn dog, Pawn target)
        {
            if (AimedAt(dog.CurJob, target))
                return;
            // Same deferral as ordinary assignment after an allowed-area change: do not path on a stale grid.
            if (!network.PathInputsReady(dog))
                return;
            Job job = Combat.JobFor(dog, target);
            if (job == null)
            {
                targets.Remove(dog);
                return;
            }
            dog.jobs.StartJob(job, JobCondition.InterruptForced);
        }

        private static bool AimedAt(Job job, Pawn target) => job != null && !job.playerForced
            && ((job.def == JobDefOf.AttackStatic || job.def == JobDefOf.AttackMelee) && job.targetA.Thing == target
                || job.def == JobDefOf.Goto && job.targetB.Thing == target);

        private static void StopFighting(Pawn dog, Pawn target)
        {
            if (dog != null && !dog.Drafted && AimedAt(dog.CurJob, target))
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
                || job.def == JobDefOf.Goto && job.targetB.Thing is Pawn)
                dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
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
