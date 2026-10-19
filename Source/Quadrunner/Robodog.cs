using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    public class CompProperties_Robodog : CompProperties
    {
        public float drainPerCell = 0.000667f;
        public float seekChargeAt = 0.1f;
        public float conduitChargePerTick = 0.0001f;
        public float stableChargePerTick = 0.0004f;
        public float energyPerFullCharge = 100f;

        public CompProperties_Robodog() => compClass = typeof(CompRobodog);
    }

    /// <summary>
    /// On every robodog: its stable (the only record of it; the stable finds its dogs through this) and its battery,
    /// drained per cell walked. At <see cref="CompProperties_Robodog.seekChargeAt"/> the dog wants a charge until full;
    /// at 0% it powers down.
    /// </summary>
    public class CompRobodog : ThingComp
    {
        public Building_Stable stable;
        private float battery = 1f;
        private bool wantsCharge;
        private IntVec3 lastCell = IntVec3.Invalid;

        public CompProperties_Robodog Props => (CompProperties_Robodog)props;

        public Pawn Dog => (Pawn)parent;

        public float Battery => battery;

        /// <summary>Set at the seek threshold, cleared once full.</summary>
        public bool WantsCharge => wantsCharge;

        public bool PoweredDown => Dog.health.hediffSet.HasHediff(QR_DefOf.QR_PoweredDown);

        public static CompRobodog Of(Thing thing) => (thing as Pawn)?.TryGetComp<CompRobodog>();

        public static bool IsRobodog(Thing thing) => Of(thing) != null;

        public void SetBattery(float level)
        {
            battery = Mathf.Clamp01(level);
            if (battery <= Props.seekChargeAt)
                wantsCharge = true;
            if (battery >= 1f)
                wantsCharge = false;
            bool poweredDown = PoweredDown;
            if (battery <= 0f && !poweredDown)
            {
                Dog.health.AddHediff(QR_DefOf.QR_PoweredDown);
                QR.Log($"{Dog} powered down");
            }
            else if (battery > 0f && poweredDown)
            {
                Dog.health.RemoveHediff(Dog.health.hediffSet.GetFirstHediffOfDef(QR_DefOf.QR_PoweredDown));
                QR.Log($"{Dog} powered up at {battery:P0}");
            }
        }

        /// <summary>Adds <paramref name="amount"/> (0–1) of charge.</summary>
        public void Charge(float amount) => SetBattery(battery + amount);

        public override void CompTick()
        {
            if (!parent.Spawned)
            {
                lastCell = IntVec3.Invalid;
                return;
            }
            IntVec3 cell = parent.Position;
            if (lastCell.IsValid && cell != lastCell && battery > 0f)
                SetBattery(battery - Props.drainPerCell * cell.DistanceTo(lastCell));
            lastCell = cell;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref stable, "stable");
            Scribe_Values.Look(ref battery, "battery", 1f);
            Scribe_Values.Look(ref wantsCharge, "wantsCharge");
        }

        public override string CompInspectStringExtra() =>
            $"Battery: {battery.ToStringPercent()}{(wantsCharge ? " (wants a charge)" : "")}\n" +
            (stable == null ? "No stable" : $"Stable: {stable.LabelCap}");
    }
}
