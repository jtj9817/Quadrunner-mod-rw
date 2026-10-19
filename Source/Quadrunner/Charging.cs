using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// A dog that wants a charge goes wherever it will be full soonest, travel included: its stable (docked, fast) or
    /// the best conduit spot (slow). No usable spot: the stable, powered or not.
    /// </summary>
    public class JobGiver_SeekCharge : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            CompRobodog comp = CompRobodog.Of(pawn);
            return comp != null && comp.WantsCharge ? ChargeJob(pawn, comp, out _) : null;
        }

        /// <summary>The charge job for <paramref name="dog"/>, or null; <paramref name="why"/> explains the choice.</summary>
        public static Job ChargeJob(Pawn dog, CompRobodog comp, out string why)
        {
            Building_Stable stable = comp.stable;
            if (stable != null && (!stable.Spawned || stable.Map != dog.Map
                    || !dog.CanReach(stable, PathEndMode.InteractionCell, Danger.Deadly)))
                stable = null;
            ChargeSpot spot = ChargeSpots.Best(dog);
            float missing = 1f - comp.Battery;
            float stableTicks = stable == null ? float.MaxValue
                : stable.CanCharge ? TicksToFull(dog, stable.InteractionCell, missing, comp.Props.stableChargePerTick)
                : float.MaxValue / 2f; // unpowered: only when there's no spot
            float spotTicks = spot == null ? float.MaxValue
                : TicksToFull(dog, spot.cell, missing, comp.Props.conduitChargePerTick);
            why = $"stable {Ticks(stableTicks)}, best spot {(spot == null ? "none" : $"{spot} {Ticks(spotTicks)}")}";
            if (stable == null && spot == null)
                return null;
            QR.Log($"{dog} at {comp.Battery:P0} seeks a charge: {why}");
            return stableTicks <= spotTicks
                ? JobMaker.MakeJob(QR_DefOf.QR_Dock, stable)
                : JobMaker.MakeJob(QR_DefOf.QR_ChargeAtConduit, spot.cell, spot.conduit);
        }

        /// <summary>Travel (straight-line cells at the dog's speed) plus charging time.</summary>
        public static float TicksToFull(Pawn dog, IntVec3 cell, float missing, float perTick) =>
            dog.Position.DistanceTo(cell) * dog.TicksPerMoveCardinal + missing / perTick;

        private static string Ticks(float t) => t >= float.MaxValue / 4f ? "n/a" : $"{t:F0} ticks";
    }

    /// <summary>
    /// Walks to a charge spot, lies down and draws the conduit's net batteries into the dog's own. Ends when full,
    /// when the batteries run dry, or when the spot stops being usable (checked every <see cref="RecheckTicks"/>);
    /// the dog then searches again while it still wants a charge.
    /// </summary>
    public class JobDriver_ChargeAtConduit : JobDriver
    {
        private const int RecheckTicks = 250;

        private int sinceCheck;

        private Thing Conduit => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            Toil charge = ToilMaker.MakeToil("Charge");
            charge.initAction = () => pawn.jobs.posture = PawnPosture.LayingOnGroundNormal;
            charge.tickIntervalAction = ChargeTick;
            charge.defaultCompleteMode = ToilCompleteMode.Never;
            yield return charge;
        }

        private void ChargeTick(int delta)
        {
            CompRobodog comp = CompRobodog.Of(pawn);
            float want = Mathf.Min(comp.Props.conduitChargePerTick * delta, 1f - comp.Battery);
            PowerNet net = Conduit.TryGetComp<CompPower>()?.PowerNet;
            float got = net == null ? 0f : Draw(net, want * comp.Props.energyPerFullCharge) / comp.Props.energyPerFullCharge;
            comp.Charge(got);
            if (!comp.WantsCharge)
            {
                QR.Log($"{pawn} charged to full at {job.targetA.Cell}");
                EndJobWith(JobCondition.Succeeded);
                return;
            }
            if (got < want * 0.999f)
            {
                QR.Log($"{pawn} at {comp.Battery:P0}: the batteries at {job.targetA.Cell} ran dry; searching again");
                EndJobWith(JobCondition.Incompletable);
                return;
            }
            sinceCheck += delta;
            if (sinceCheck >= RecheckTicks)
            {
                sinceCheck = 0;
                if (!ChargeSpots.UsableBy(new ChargeSpot { cell = job.targetA.Cell, conduit = Conduit }, pawn, out string why))
                {
                    QR.Log($"{pawn} at {comp.Battery:P0}: spot {job.targetA.Cell} no longer usable ({why}); searching again");
                    EndJobWith(JobCondition.Incompletable);
                }
            }
        }

        /// <summary>Takes up to <paramref name="wattDays"/> from the net's batteries, fullest first; returns what it got.</summary>
        private static float Draw(PowerNet net, float wattDays)
        {
            float got = 0f;
            foreach (CompPowerBattery battery in net.batteryComps.OrderByDescending(b => b.StoredEnergy))
            {
                float take = Mathf.Min(wattDays - got, battery.StoredEnergy);
                if (take <= 0f)
                    break;
                battery.DrawPower(take);
                got += take;
            }
            return got;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref sinceCheck, "sinceCheck");
        }
    }
}
