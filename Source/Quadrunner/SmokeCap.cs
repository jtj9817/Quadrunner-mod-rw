using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// Plan 0012: when ≥3 Combatdogs with a smoke launcher stand within the pack coordination radius (15 cells) of the
    /// dog about to fire, at most 3 of them may pop smoke per smoke interval; the rest wait for a later window.
    /// Eligibility is snapshotted per (map, 900-tick window) on first query: every caller in that window gets the same
    /// answer, so popping can never pull an extra dog over the cap mid-window (the first three pops never flip the pool).
    /// Within a window the three longest waiters (or dogs that never popped) are the eligible ones; ties break by ThingID.
    /// A skipped dog always rotates in on the next window. Tear gas is never capped. Transient state only: after a load
    /// the window table is gone and eligibility re-forms from positions (worst case one extra volley in the reload window).
    /// </summary>
    public static class SmokeCoordinator
    {
        public const int MaxPopsPerWindow = 3;

        private sealed class ClusterSnapshot
        {
            public readonly HashSet<string> members;
            public readonly HashSet<string> winners;

            public ClusterSnapshot(HashSet<string> members, HashSet<string> winners)
            {
                this.members = members;
                this.winners = winners;
            }
        }

        // (map uniqueID, window) -> per-cluster snapshots for that window. Independent squads on the same map
        // each get their own snapshot instead of competing over one map-wide pool.
        private static readonly Dictionary<(int, int), List<ClusterSnapshot>> snapshots =
            new Dictionary<(int, int), List<ClusterSnapshot>>();

        // Dog ThingID -> the game window it last popped smoke in. Entries older than two windows are purged alongside.
        private static readonly Dictionary<string, int> lastPopped = new Dictionary<string, int>();

        public static void ClearAll()
        {
            snapshots.Clear();
            lastPopped.Clear();
        }

        public static bool CanPop(Pawn dog)
        {
            List<Pawn> cluster = ClusterOf(dog);
            if (cluster == null || cluster.Count < 3)
                return true; // below the threshold the cap is inactive
            int window = Window(dog);
            string dogId = dog.ThingID;
            var key = (dog.Map.uniqueID, window);
            if (!snapshots.TryGetValue(key, out List<ClusterSnapshot> list))
            {
                list = new List<ClusterSnapshot>();
                snapshots[key] = list;
                PurgeOld(window);
            }
            for (int i = 0; i < list.Count; i++)
                if (list[i].members.Contains(dogId))
                    return list[i].winners.Contains(dogId);
            var members = new HashSet<string>();
            for (int i = 0; i < cluster.Count; i++)
                members.Add(cluster[i].ThingID);
            for (int i = 0; i < list.Count; i++)
                if (list[i].members.Overlaps(members))
                {
                    list[i].members.UnionWith(members);
                    return list[i].winners.Contains(dogId);
                }
            var winners = new HashSet<string>(Eligible(cluster).Select(d => d.ThingID));
            list.Add(new ClusterSnapshot(members, winners));
            return winners.Contains(dogId);
        }

        public static void NoteFired(Pawn dog)
        {
            List<Pawn> cluster = ClusterOf(dog);
            if (cluster == null || cluster.Count < 3)
                return;
            lastPopped[dog.ThingID] = Window(dog);
            QR.Log($"{dog}: smoke launched (eligibility rotates among the longest waiters in this window)");
        }

        private static int Window(Pawn dog)
        {
            int interval = CombatTier.Of(dog)?.smokeInterval ?? 900;
            return Find.TickManager.TicksGame / (interval > 0 ? interval : 900);
        }

        private static List<Pawn> Eligible(List<Pawn> cluster)
        {
            int now = Find.TickManager.TicksGame / ((CombatTier.Of(cluster[0])?.smokeInterval ?? 900) is int i && i > 0 ? i : 900);
            return cluster
                .OrderByDescending(d => WaitScore(d, now))
                .ThenBy(d => d.ThingID, System.StringComparer.OrdinalIgnoreCase)
                .Take(MaxPopsPerWindow)
                .ToList();
        }

        private static int WaitScore(Pawn dog, int now)
            => lastPopped.TryGetValue(dog.ThingID, out int last) ? now - last : int.MaxValue;

        /// <summary>Connected component of active smoke-equipped same-faction robodogs within pack radius of <paramref name="dog"/>.</summary>
        private static List<Pawn> ClusterOf(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier?.smokeGun == null || dog.Map == null)
                return null;
            float range = tier.packRange > 0f ? tier.packRange : 15f;
            var all = new List<Pawn>();
            foreach (Pawn other in dog.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == dog)
                {
                    all.Add(other);
                    continue;
                }
                if (other.Dead || other.Downed || other.Faction != dog.Faction
                    || CompRobodog.Of(other)?.WantsCharge == true)
                    continue;
                if (CombatTier.Of(other)?.smokeGun != null)
                    all.Add(other);
            }
            var cluster = new List<Pawn> { dog };
            var seen = new HashSet<Pawn> { dog };
            for (int head = 0; head < cluster.Count; head++)
            {
                Pawn cur = cluster[head];
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn other = all[i];
                    if (!seen.Contains(other) && other.Position.InHorDistOf(cur.Position, range))
                    {
                        seen.Add(other);
                        cluster.Add(other);
                    }
                }
            }
            return cluster;
        }

        private static void PurgeOld(int now)
        {
            foreach (var key in snapshots.Keys.Where(k => k.Item2 < now - 1).ToList())
                snapshots.Remove(key);
            foreach (var key in lastPopped.Where(kv => kv.Value < now - 2).Select(kv => kv.Key).ToList())
                lastPopped.Remove(key);
        }
    }
}
