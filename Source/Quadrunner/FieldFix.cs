using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>On a field-fix item: what it does to the dog it's used on.</summary>
    public class RobodogFixProps : DefModExtension
    {
        /// <summary>Charges the dog to full (the robodog battery).</summary>
        public bool charges;
        /// <summary>Removes every injury (the repair kit).</summary>
        public bool repairs;
    }

    /// <summary>
    /// Colonists bring a repair kit to a dog in the field below <see cref="CompProperties_Robodog.fieldRepairBelow"/>
    /// health, then a robodog battery if it's powered down, when one is in stock.
    /// </summary>
    public static class FieldFix
    {
        /// <summary>The item <paramref name="dog"/> needs now, or null.</summary>
        public static ThingDef Needed(Pawn dog)
        {
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || !dog.Spawned || dog.Dead)
                return null;
            // Repair first: a dog that's powered up walks home, where the stable repairs it anyway.
            if (dog.health.hediffSet.hediffs.Any(h => h is Hediff_Injury)
                && dog.health.summaryHealth.SummaryHealthPercent < comp.Props.fieldRepairBelow)
                return QR_DefOf.QR_RobodogRepairKit;
            return comp.PoweredDown ? QR_DefOf.QR_RobodogBattery : null;
        }

        public static bool InStock(Map map, ThingDef item) => item != null &&
            map.listerThings.ThingsOfDef(item).Any(t => t.Spawned && !t.IsForbidden(Faction.OfPlayer));

        public static void Apply(Thing item, Pawn dog)
        {
            RobodogFixProps props = item.def.GetModExtension<RobodogFixProps>();
            if (props.charges)
                CompRobodog.Of(dog).SetBattery(1f);
            if (props.repairs)
                foreach (Hediff_Injury injury in dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                    dog.health.RemoveHediff(injury);
        }
    }

    public class WorkGiver_FieldFixRobodog : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            CompRobodog.IsRobodog(pawn)
            || !FieldFix.InStock(pawn.Map, QR_DefOf.QR_RobodogBattery) && !FieldFix.InStock(pawn.Map, QR_DefOf.QR_RobodogRepairKit);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn) =>
            pawn.Map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Where(p => FieldFix.Needed(p) != null);

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Pawn dog) || dog.IsForbidden(pawn) || !pawn.CanReserve(dog, ignoreOtherReservations: forced))
                return null;
            ThingDef need = FieldFix.Needed(dog);
            if (need == null)
                return null;
            Thing item = GenClosest.ClosestThingReachable(pawn.Position, pawn.Map, ThingRequest.ForDef(need),
                PathEndMode.ClosestTouch, TraverseParms.For(pawn), 9999f, x => !x.IsForbidden(pawn) && pawn.CanReserve(x));
            if (item == null)
            {
                JobFailReason.Is($"no {need.label} in stock");
                return null;
            }
            Job job = JobMaker.MakeJob(QR_DefOf.QR_FieldFixRobodog, item, dog);
            job.count = 1;
            return job;
        }
    }

    /// <summary>Picks up one field-fix item (A), takes it to the dog (B) and uses it there.</summary>
    public class JobDriver_FieldFixRobodog : JobDriver
    {
        private const int UseTicks = 120;

        private Pawn Dog => (Pawn)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, 1, 1, errorOnFailed: errorOnFailed)
            && pawn.Reserve(job.targetB, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.B);
            this.FailOn(() => FieldFix.Needed(Dog) == null);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch);
            yield return Toils_General.Wait(UseTicks, TargetIndex.B).WithProgressBarToilDelay(TargetIndex.B);
            Toil use = ToilMaker.MakeToil("UseFieldFix");
            use.initAction = () =>
            {
                Thing item = pawn.carryTracker.CarriedThing;
                if (item?.def.GetModExtension<RobodogFixProps>() == null)
                    return;
                Thing one = item.stackCount > 1 ? item.SplitOff(1) : item;
                FieldFix.Apply(one, Dog);
                QR.Log($"{pawn} used a {one.def.label} on {Dog}: battery {CompRobodog.Of(Dog).Battery:P0}, " +
                    $"health {Dog.health.summaryHealth.SummaryHealthPercent:P0}");
                one.Destroy();
            };
            use.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return use;
        }
    }
}
