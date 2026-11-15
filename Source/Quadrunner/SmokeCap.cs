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

        // (map uniqueID, window) -> eligible dog ThingIDs for that window. Old windows are removed when new ones form.
        private static readonly Dictionary<(int, int), HashSet<string>> winners =
            new Dictionary<(int, int), HashSet<string>>();

        // Dog ThingID -> the game window it last popped smoke in. Entries older than two windows are purged alongside.
        private static readonly Dictionary<string, int> lastPopped = new Dictionary<string, int>();

        public static bool CanPop(Pawn dog)
        {
            List<Pawn> cluster = ClusterOf(dog);
            if (cluster == null || cluster.Count < 3)
                return true; // below the threshold the cap is inactive
            int window = Window(dog);
            if (!winners.TryGetValue((dog.Map.uniqueID, window), out HashSet<string> set))
            {
                set = Eligible(cluster).Select(d => d.ThingID).ToHashSet();
                winners[(dog.Map.uniqueID, window)] = set;
                PurgeOld(window);
            }
            return set.Contains(dog.ThingID);
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

        /// <summary>Robodogs with a smoke launcher within the pack radius of <paramref name="dog"/> (itself included).</summary>
        private static List<Pawn> ClusterOf(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier?.smokeGun == null || dog.Map == null)
                return null;
            float range = tier.packRange > 0f ? tier.packRange : 15f;
            var cluster = new List<Pawn> { dog };
            foreach (Pawn other in dog.Map.mapPawns.AllPawnsSpawned)
            {
                if (other == dog || other.Dead || other.Downed)
                    continue;
                if (CombatTier.Of(other)?.smokeGun != null && other.Position.InHorDistOf(dog.Position, range))
                    cluster.Add(other);
            }
            return cluster;
        }

        private static void PurgeOld(int now)
        {
            foreach (var key in winners.Keys.Where(k => k.Item2 < now - 1).ToList())
                winners.Remove(key);
            foreach (var key in lastPopped.Where(kv => kv.Value < now - 2).Select(kv => kv.Key).ToList())
                lastPopped.Remove(key);
        }
    }
}
