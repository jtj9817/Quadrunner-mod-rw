using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// Plan 0012: when ≥3 Combatdogs with a smoke launcher stand within the pack coordination radius (15 cells) of the
    /// dog about to fire, at most 3 of them may pop smoke per smoke interval; the rest hold until the next window and
    /// pop with the next scheduled rotation. Tear gas is never capped. Stateless: each window rotates which dogs are
    /// eligible (FNV-1a over <see cref="Thing.ThingID"/> mixed with the window index), so the same three dogs never
    /// monopolize the cap and there is nothing to reload.
    /// </summary>
    public static class SmokeCoordinator
    {
        public const int MaxPopsPerWindow = 3;

        public static bool CanPop(Pawn dog)
        {
            List<Pawn> cluster = ClusterOf(dog);
            if (cluster == null || cluster.Count < 3)
                return true;
            int window = Find.TickManager.TicksGame / IntervalOf(dog);
            return cluster
                .OrderBy(p => RotateKey(p.ThingID, window))
                .Take(MaxPopsPerWindow)
                .Contains(dog);
        }

        public static void NoteFired(Pawn dog)
            => QR.Log($"{dog}: smoke launched (cluster of {ClusterOf(dog)?.Count ?? 1}, ≤{MaxPopsPerWindow} per interval)");

        private static int IntervalOf(Pawn dog)
        {
            int interval = CombatTier.Of(dog)?.smokeInterval ?? 900;
            return interval > 0 ? interval : 900;
        }

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

        private static uint RotateKey(string thingId, int window)
            => Fnv(window + ":" + thingId); // hash the pair, so eligibility shuffles order every window

        private static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619;
            }
            return h;
        }
    }
}
