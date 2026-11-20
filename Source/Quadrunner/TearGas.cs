using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// Standalone tear gas cloud (copied from Vanilla Weapons Expanded - Non-Lethal, user 2026-11-13). One cloud thing
    /// per walkable cell, spawned by the projectile's explosion; unlike the map-grid BlindSmoke the smoke launcher uses.
    /// Renders with Smokepop Pack's soft cloud texture (<c>Things/Gas/GasCloudThickA</c>), shifting from musk green at
    /// the fresh core to light yellow toward the edges and as the cloud dissipates. Every 120 ticks, organic breathing
    /// pawns standing in its cell get <see cref="QR_DefOf.QR_TearGas"/> applied or refreshed.
    /// </summary>
    public sealed class Gas_TearGas : Gas
    {
        public const int RefreshTicks = 120;

        internal static readonly Color MuskGreen = new Color(0.32f, 0.54f, 0.28f);
        internal static readonly Color LightYellow = new Color(0.98f, 0.95f, 0.58f);

        private int spawnTick;
        private float edgeFactor;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                spawnTick = Find.TickManager.TicksGame;
                edgeFactor = DamageWorker_TearGas.CurrentEdgeFactor(map, Position);
            }
            else if (spawnTick <= 0)
            {
                spawnTick = Math.Max(0, destroyTick - 25 * 60);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
            Scribe_Values.Look(ref edgeFactor, "edgeFactor", 0f);
        }

        public Color CurrentColor()
        {
            int now = Find.TickManager.TicksGame;
            int total = Math.Max(1, destroyTick - spawnTick);
            float ageFrac = Mathf.Clamp01((float)(now - spawnTick) / total);
            float colorT = Mathf.Clamp01(edgeFactor * 0.60f + ageFrac * 0.65f);
            Color c = Color.Lerp(MuskGreen, LightYellow, colorT);
            float fadeIn = Mathf.Clamp01((now - spawnTick) / 15f);
            float fadeOut = Mathf.Clamp01((destroyTick - now) / 180f);
            c.a = 0.55f * (1f - 0.22f * edgeFactor) * fadeIn * fadeOut;
            return c;
        }

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

        // Biotech's mask doesn't exist in Core; checked null-safely and cached once so a mask from anywhere else still works.
        private static bool gasMaskChecked;
        private static ThingDef gasMaskDef;

        private static bool HasGasMask(Pawn pawn)
        {
            if (!gasMaskChecked)
            {
                gasMaskDef = DefDatabase<ThingDef>.GetNamed("Apparel_GasMask", errorOnFail: false);
                gasMaskChecked = true;
            }
            return gasMaskDef != null && pawn.apparel?.WornApparel != null && pawn.apparel.WornApparel.Any(a => a.def == gasMaskDef);
        }
    }

    /// <summary>
    /// Draws <see cref="Gas_TearGas"/> with <see cref="Graphic_Gas"/>'s random cell jitter/rotation and a per-cloud
    /// musk-green-to-light-yellow tint via <see cref="MaterialPropertyBlock"/>.
    /// </summary>
    public sealed class Graphic_TearGas : Graphic_Gas
    {
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
        {
            Color c = (thing as Gas_TearGas)?.CurrentColor() ?? Color;
            if (c.a <= 0f)
                return;
            Rand.PushState();
            Rand.Seed = thing.thingIDNumber.GetHashCode();
            float angle = Rand.Range(0, 360) + ((thing as Gas)?.graphicRotation ?? 0f);
            Vector3 pos = thing.TrueCenter() + new Vector3(Rand.Range(-0.45f, 0.45f), 0f, Rand.Range(-0.45f, 0.45f));
            Vector3 s = new Vector3(Rand.Range(0.8f, 1.2f) * drawSize.x, 0f, Rand.Range(0.8f, 1.2f) * drawSize.y);
            Matrix4x4 matrix = default;
            matrix.SetTRS(pos, Quaternion.AngleAxis(angle, Vector3.up), s);
            propertyBlock.SetColor(ShaderPropertyIDs.Color, c);
            Graphics.DrawMesh(MeshPool.plane10, matrix, MatSingle, 0, null, 0, propertyBlock);
            Rand.PopState();
        }
    }

    /// <summary>
    /// Emits Smokepop-style detonation smoke wisps and cloud puffs tinted from musk green at the center to light yellow
    /// at the outer edge, and passes each affected cell's normalized distance to <see cref="Gas_TearGas"/> on spawn.
    /// </summary>
    public sealed class DamageWorker_TearGas : DamageWorker
    {
        private static Map activeMap;
        private static IntVec3 activeOrigin;
        private static float activeRadius;

        internal static float CurrentEdgeFactor(Map map, IntVec3 cell)
        {
            if (map == null || map != activeMap || activeRadius <= 0f)
                return 0f;
            return Mathf.Clamp01(cell.DistanceTo(activeOrigin) / activeRadius);
        }

        protected override void ExplosionVisualEffectCenter(Explosion explosion)
        {
            Vector3 center = explosion.Position.ToVector3Shifted();
            Map map = explosion.Map;
            float r = explosion.radius;
            for (int i = 0; i < 6; i++)
            {
                Vector3 offset = Gen.RandomHorizontalVector(r * 0.7f);
                Vector3 loc = center + offset;
                if (!loc.ShouldSpawnMotesAt(map))
                    continue;
                float t = Mathf.Clamp01(offset.MagnitudeHorizontal() / Mathf.Max(0.1f, r));
                FleckCreationData smoke = FleckMaker.GetDataStatic(loc, map, FleckDefOf.Smoke, Rand.Range(1.5f, 2.5f) * r * 0.6f);
                smoke.rotationRate = Rand.Range(-30f, 30f);
                smoke.velocityAngle = Rand.Range(30f, 40f);
                smoke.velocitySpeed = Rand.Range(0.5f, 0.7f);
                smoke.instanceColor = Color.Lerp(Gas_TearGas.MuskGreen, Gas_TearGas.LightYellow, t);
                map.flecks.CreateFleck(smoke);
            }
            for (int i = 0; i < 6; i++)
            {
                Vector3 offset = Gen.RandomHorizontalVector(r * 0.55f);
                Vector3 loc = center + offset;
                if (!loc.ShouldSpawnMotesAt(map))
                    continue;
                float t = Mathf.Clamp01(offset.MagnitudeHorizontal() / Mathf.Max(0.1f, r));
                FleckCreationData puff = FleckMaker.GetDataStatic(loc, map, QR_DefOf.QR_TearGasPuff, Rand.Range(1.4f, 2.2f));
                puff.rotation = Rand.Range(0f, 360f);
                puff.rotationRate = Rand.Range(-20f, 20f);
                puff.velocityAngle = offset.MagnitudeHorizontal() > 0.05f ? offset.AngleFlat() : Rand.Range(0f, 360f);
                puff.velocitySpeed = Rand.Range(0.4f, 1.1f);
                Color c = Color.Lerp(Gas_TearGas.MuskGreen, Gas_TearGas.LightYellow, t);
                c.a = 0.72f;
                puff.instanceColor = c;
                map.flecks.CreateFleck(puff);
            }
        }

        public override void ExplosionAffectCell(Explosion explosion, IntVec3 c, List<Thing> damagedThings, List<Thing> ignoredThings, bool canThrowMotes)
        {
            activeMap = explosion.Map;
            activeOrigin = explosion.Position;
            activeRadius = explosion.radius;
            try
            {
                base.ExplosionAffectCell(explosion, c, damagedThings, ignoredThings, canThrowMotes);
            }
            finally
            {
                activeMap = null;
                activeRadius = 0f;
            }
        }
    }
}
