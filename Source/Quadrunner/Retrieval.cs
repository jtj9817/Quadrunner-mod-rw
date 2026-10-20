using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Powered-down dogs lie like downed pawns until someone carries them to the nearest stable with a free slot (its
    /// own stable always has its slot): a healthy dog sent by a stable, or a colonist ordered to. A dog with a field fix
    /// in stock is left to the colonists for <see cref="CompProperties_Robodog.fieldFixWaitTicks"/> first.
    /// </summary>
    public static class Retrieval
    {
        public static IEnumerable<Pawn> DownDogs(Map map) =>
            map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Where(p => CompRobodog.Of(p)?.PoweredDown == true);

        /// <summary>The stable nearest <paramref name="takee"/> that takes it and <paramref name="carrier"/> can reach.</summary>
        public static Building_Stable StableFor(Pawn takee, Pawn carrier) =>
            takee.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Stable>()
                .Where(s => (CompRobodog.Of(takee).stable == s || s.HasFreeSlot)
                    && carrier.CanReach(s, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(s => s.InteractionCell.DistanceToSquared(takee.Position))
                .FirstOrDefault();

        /// <summary>Some stable on the map can reach <paramref name="dog"/>.</summary>
        public static bool Reachable(Pawn dog) =>
            dog.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Stable>().Any(s =>
                dog.Map.reachability.CanReach(s.InteractionCell, dog, PathEndMode.ClosestTouch,
                    TraverseParms.For(TraverseMode.PassDoors)));

        /// <summary>Recently powered down with its field fix in stock: the colonists bring it.</summary>
        public static bool AwaitsFieldFix(Pawn takee)
        {
            Hediff down = takee.health.hediffSet.GetFirstHediffOfDef(QR_DefOf.QR_PoweredDown);
            return down != null && down.ageTicks < CompRobodog.Of(takee).Props.fieldFixWaitTicks
                && FieldFix.InStock(takee.Map, FieldFix.Needed(takee));
        }

        public static Job CarryJob(Pawn carrier, Pawn takee)
        {
            Building_Stable stable = StableFor(takee, carrier);
            if (stable == null)
                return null;
            Job job = JobMaker.MakeJob(QR_DefOf.QR_CarryToStable, takee, stable);
            job.count = 1;
            return job;
        }

        /// <summary>
        /// For a working dog: the nearest powered-down dog it can carry home, unless <paramref name="leftToOthers"/>;
        /// a fire on or next to that dog is beaten out first.
        /// </summary>
        public static Job JobFor(Pawn dog, Predicate<Thing> leftToOthers)
        {
            Thing takee = GenClosest.ClosestThing_Global_Reachable(dog.Position, dog.Map, DownDogs(dog.Map).Where(d => d != dog),
                PathEndMode.ClosestTouch, TraverseParms.For(dog, Danger.Deadly), 9999f,
                t => !t.IsForbidden(dog) && dog.CanReserve(t) && !leftToOthers(t) && !AwaitsFieldFix((Pawn)t)
                    && StableFor((Pawn)t, dog) != null);
            if (takee == null)
                return null;
            Fire fire = GenRadial.RadialCellsAround(takee.Position, 1.5f, useCenter: true)
                .Where(c => c.InBounds(dog.Map))
                .Select(c => c.GetFirstThing<Fire>(dog.Map))
                .FirstOrDefault(f => f != null && dog.CanReach(f, PathEndMode.Touch, Danger.Deadly));
            return fire != null ? JobMaker.MakeJob(JobDefOf.BeatFire, fire) : CarryJob(dog, (Pawn)takee);
        }
    }

    /// <summary>
    /// Carries a powered-down dog (A) to a stable (B) and docks it there. A robodog carrier that drops to its charge
    /// threshold on the way finishes if charging at the stable wins the charge search, else sets the dog down and goes
    /// to the charge spot.
    /// </summary>
    public class JobDriver_CarryToStable : JobDriver
    {
        private bool checkedBattery;

        private Pawn Takee => (Pawn)job.targetA.Thing;

        private Building_Stable Stable => (Building_Stable)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(Takee, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            Toil toStable = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            toStable.AddPreTickIntervalAction(_ => CheckCarrierBattery());
            yield return Toils_Jump.JumpIf(toStable, () => pawn.IsCarryingPawn(Takee));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !CompRobodog.Of(Takee).PoweredDown)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return toStable;
            Toil dock = ToilMaker.MakeToil("DockCarried");
            dock.initAction = () =>
            {
                Pawn takee = Takee;
                pawn.carryTracker.TryDropCarriedThing(Stable.InteractionCell, ThingPlaceMode.Near, out Thing _);
                if (Stable.TryDock(takee))
                    QR.Log($"{pawn} carried {takee} home to {Stable}");
                else
                    QR.Log($"{pawn} carried {takee} to {Stable}, which has no free slot now; set it down");
            };
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }

        private void CheckCarrierBattery()
        {
            CompRobodog comp = CompRobodog.Of(pawn);
            if (checkedBattery || comp == null || !comp.WantsCharge)
                return;
            checkedBattery = true;
            // The charge search's own choice (full soonest, travel included): the stable means finishing on the way.
            Job charge = JobGiver_SeekCharge.ChargeJob(pawn, comp, out string why);
            if (charge == null || charge.def == QR_DefOf.QR_Dock)
            {
                QR.Log($"{pawn} at {comp.Battery:P0} carrying {Takee}: charging at the stable wins ({why}); finishing");
                return;
            }
            QR.Log($"{pawn} at {comp.Battery:P0} carrying {Takee}: a charge spot wins ({why}); setting it down");
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
            // The think tree's charge search comes next and makes the same choice.
            EndJobWith(JobCondition.Incompletable);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref checkedBattery, "checkedBattery");
        }
    }

    /// <summary>Right-click a powered-down dog with a colonist: "Carry to stable".</summary>
    public class FloatMenuOptionProvider_CarryToStable : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override bool RequiresManipulation => true;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn carrier = context.FirstSelectedPawn;
            if (CompRobodog.Of(clickedPawn)?.PoweredDown != true || clickedPawn.Faction != Faction.OfPlayer
                || CompRobodog.IsRobodog(carrier)
                || !carrier.CanReserveAndReach(clickedPawn, PathEndMode.ClosestTouch, Danger.Deadly, ignoreOtherReservations: true))
                return null;
            Job job = Retrieval.CarryJob(carrier, clickedPawn);
            if (job == null)
                return new FloatMenuOption($"Can't carry {clickedPawn.LabelShort} to a stable: none reachable with a free slot", null);
            return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(
                $"Carry {clickedPawn.LabelShort} to {job.targetB.Thing.LabelShort}",
                () => carrier.jobs.TryTakeOrderedJob(job, JobTag.Misc)), carrier, clickedPawn);
        }
    }

    /// <summary>A powered-down dog that no stable can reach.</summary>
    public class Alert_RobodogUnreachable : Alert
    {
        private readonly List<Thing> culprits = new List<Thing>();

        public Alert_RobodogUnreachable()
        {
            defaultLabel = "Robodog out of reach";
            defaultExplanation = "A powered-down robodog lies where no stable can reach it. Open a path, or bring it a robodog battery.";
            defaultPriority = AlertPriority.Medium;
        }

        public List<Thing> Culprits()
        {
            culprits.Clear();
            foreach (Map map in Find.Maps)
                culprits.AddRange(Retrieval.DownDogs(map).Where(d => !Retrieval.Reachable(d)));
            return culprits;
        }

        public override AlertReport GetReport() => AlertReport.CulpritsAre(Culprits());
    }

    /// <summary>
    /// Raiders carry off a powered-down dog as they would a downed colonist; vanilla only looks at humanlikes. Runs
    /// only when vanilla found no victim, so colonists still come first.
    /// </summary>
    [HarmonyPatch(typeof(KidnapAIUtility), nameof(KidnapAIUtility.TryFindGoodKidnapVictim))]
    public static class Patch_KidnapAIUtility_TryFindGoodKidnapVictim
    {
        public static void Postfix(Pawn kidnapper, float maxDist, ref Pawn victim, List<Thing> disallowed, ref bool __result)
        {
            if (__result || kidnapper.Faction == null || !kidnapper.Faction.HostileTo(Faction.OfPlayer)
                || !kidnapper.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)
                || !kidnapper.Map.reachability.CanReachMapEdge(kidnapper.Position, TraverseParms.For(kidnapper, Danger.Some)))
                return;
            victim = (Pawn)GenClosest.ClosestThingReachable(kidnapper.Position, kidnapper.Map,
                ThingRequest.ForGroup(ThingRequestGroup.Pawn), PathEndMode.OnCell,
                TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Some), maxDist,
                t => t is Pawn p && p.Faction == Faction.OfPlayer && CompRobodog.Of(p)?.PoweredDown == true
                    && kidnapper.CanReserve(p) && (disallowed == null || !disallowed.Contains(p)));
            __result = victim != null;
        }
    }
}
