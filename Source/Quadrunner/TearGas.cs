using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// Standalone tear gas cloud (copied from Vanilla Weapons Expanded - Non-Lethal, user 2026-11-13). One cloud thing
    /// per walkable cell, spawned by the projectile's explosion; unlike the map-grid BlindSmoke the smoke launcher uses.
    /// Every 120 ticks, organic breathing pawns standing in its cell get <see cref="QR_DefOf.QR_TearGas"/> applied or
    /// refreshed. Non-organic flesh (every robodog: ToxicResistance and the robots) is immune; Biotech's gas mask, if
    /// some other mod provides one, blocks it; it never injures, it can only blind.
    /// </summary>
    public sealed class Gas_TearGas : Gas
    {
        public const int RefreshTicks = 120;

        protected override void Tick()
        {
            base.Tick();
            if (Find.TickManager.TicksGame % RefreshTicks != 0 || Map == null || Destroyed)
                return;
            foreach (Thing thing in GridsUtility.GetThingList(Position, Map))
            {
                if (thing is not Pawn pawn || pawn.Dead || !pawn.RaceProps.FleshType.isOrganic)
                    continue;
                if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Breathing) || HasGasMask(pawn))
                    continue;
                Refresh(pawn);
            }
        }

        private void Refresh(Pawn pawn)
        {
            float resist = 1f - pawn.GetStatValue(StatDefOf.ToxicResistance);
            float amount = Rand.Range(0.01f * resist, resist); // copied from the VWE: Rand.Range(0.01f, 1.0f) × resistance
            Hediff first = pawn.health.hediffSet.GetFirstHediffOfDef(QR_DefOf.QR_TearGas);
            if (first != null)
            {
                if (amount > 0f)
                    first.Severity += amount;
            }
            else
            {
                Hediff hediff = HediffMaker.MakeHediff(QR_DefOf.QR_TearGas, pawn);
                hediff.Severity = amount;
                pawn.health.AddHediff(hediff);
            }
        }

        // Biotech's mask doesn't exist in Core; checked null-safely so a mask from anywhere else still works.
        private static bool HasGasMask(Pawn pawn)
        {
            ThingDef mask = DefDatabase<ThingDef>.GetNamed("Apparel_GasMask", errorOnFail: false);
            return mask != null && pawn.apparel?.WornApparel != null && pawn.apparel.WornApparel.Any(a => a.def == mask);
        }
    }
}
