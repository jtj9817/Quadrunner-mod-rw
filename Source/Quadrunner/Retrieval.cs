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
    /// own stable always has its slot): a healthy dog sent by the network, or a colonist ordered to. A dog with a field
    /// fix in stock is left to the colonists for <see cref="CompProperties_Robodog.fieldFixWaitTicks"/> first.
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

        /// <summary>A living Combatdog casualty: injured down or out of battery.</summary>
        public static bool Casualty(Pawn pawn) =>
            pawn != null && !pawn.Dead && (pawn.Downed || CompRobodog.Of(pawn)?.PoweredDown == true);

        /// <summary>
        /// The job for one central casualty order: the real living dog, or the real wreck it left. Null when that
        /// body is gone, forbidden, reserved or out of reach, or when no stable will receive it; the order stays queued.
        /// </summary>
        public static Job RecoveryJobFor(Pawn carrier, NetworkTask task)
        {
            if (carrier == null || task == null)
                return null;
            if (task.casualtyDead)
                return task.Target(carrier.Map) is Corpse wreck ? WreckJobFor(carrier, wreck) : null;
            return task.Target(carrier.Map) is Pawn casualty ? CasualtyJobFor(carrier, casualty) : null;
        }

        public static Job CasualtyJobFor(Pawn carrier, Pawn casualty)
        {
            if (carrier == null || casualty == null || casualty == carrier || casualty.Dead || !casualty.Spawned
                || casualty.Map != carrier.Map || RobodogWork.Of(casualty)?.combat != true || !Casualty(casualty)
                || casualty.IsForbidden(carrier) || !carrier.CanReserve(casualty)
                || !carrier.CanReach(casualty, PathEndMode.ClosestTouch, Danger.Deadly) || AwaitsFieldFix(casualty)
                || StableFor(casualty, carrier) == null)
                return null;
            Fire fire = FireNear(carrier, casualty);
            return fire != null ? JobMaker.MakeJob(JobDefOf.BeatFire, fire) : CarryJob(carrier, casualty);
        }

        /// <summary>The stable that should receive <paramref name="wreck"/>: the dog's own if reachable, else the nearest.</summary>
        public static Building_Stable StableForWreck(Corpse wreck, Pawn carrier)
        {
            Building_Stable home = CompRobodog.Of(wreck.InnerPawn)?.stable;
            if (home != null && home.Spawned && home.Map == wreck.Map
                && carrier.CanReach(home, PathEndMode.InteractionCell, Danger.Deadly))
                return home;
            return wreck.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Stable>()
                .Where(s => carrier.CanReach(s, PathEndMode.InteractionCell, Danger.Deadly))
                .OrderBy(s => s.InteractionCell.DistanceToSquared(wreck.Position))
                .FirstOrDefault();
        }

        public static Job WreckJobFor(Pawn carrier, Corpse wreck)
        {
            Pawn dog = wreck?.InnerPawn;
            if (carrier == null || wreck == null || wreck.Destroyed || !wreck.Spawned || wreck.Map != carrier.Map
                || dog == null || dog.Faction != Faction.OfPlayer || RobodogWork.Of(dog)?.combat != true
                || !carrier.CanReserve(wreck)
                || !carrier.CanReach(wreck, PathEndMode.ClosestTouch, Danger.Deadly))
                return null;
            // Vanilla forbids corpses dropped outside the home area by default; a centrally known casualty is
            // still ours to recover, so the wreck is not gated on that automatic flag (living dogs still are).
            Building_Stable stable = StableForWreck(wreck, carrier);
            if (stable == null)
                return null;
            // The wreck burns like anything else: the same fire-first step as an ordinary retrieval.
            Fire fire = FireNear(carrier, wreck);
            if (fire != null)
                return JobMaker.MakeJob(JobDefOf.BeatFire, fire);
            Job job = JobMaker.MakeJob(QR_DefOf.QR_CarryToStable, wreck, stable);
            job.count = 1;
            return job;
        }

        private static Fire FireNear(Pawn carrier, Thing target) =>
            GenRadial.RadialCellsAround(target.Position, 1.5f, useCenter: true)
                .Where(c => c.InBounds(carrier.Map))
                .Select(c => c.GetFirstThing<Fire>(carrier.Map))
                .FirstOrDefault(f => f != null && carrier.CanReach(f, PathEndMode.Touch, Danger.Deadly));

        /// <summary>
        /// For a working dog given one powered-down dog: a fire on or next to it is beaten out first, else it is
        /// carried home — unless it awaits its field fix or is not ours to take (forbidden, reserved, unreachable,
        /// or no stable will receive it: null, and the network keeps it queued).
        /// </summary>
        public static Job JobFor(Pawn dog, Pawn takee)
        {
            if (takee == null || takee == dog || takee.Dead || !takee.Spawned
                || CompRobodog.Of(takee)?.PoweredDown != true || takee.IsForbidden(dog) || !dog.CanReserve(takee)
                || !dog.CanReach(takee, PathEndMode.ClosestTouch, Danger.Deadly) || AwaitsFieldFix(takee)
                || StableFor(takee, dog) == null)
                return null;
            Fire fire = FireNear(dog, takee);
            return fire != null ? JobMaker.MakeJob(JobDefOf.BeatFire, fire) : CarryJob(dog, takee);
        }
    }

    /// <summary>
    /// Carries a living downed dog or a Combatdog wreck (A) to a stable (B): a living dog is docked, a wreck offered
    /// to <see cref="Building_Stable.AcceptWreck"/> and, if refused, safely set down beside it. A robodog carrier that
    /// drops to its charge threshold on the way finishes if charging at the stable wins, else sets the cargo down and
    /// goes to the charge spot.
    /// </summary>
    public class JobDriver_CarryToStable : JobDriver
    {
        private bool checkedBattery;

        private Thing Cargo => job.targetA.Thing;

        private Building_Stable Stable => (Building_Stable)job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            Cargo != null && pawn.Reserve(Cargo, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnDespawnedOrNull(TargetIndex.B);
            // A failed trip must never eat the dog or its wreck: set real cargo down where the carrier stands.
            this.AddFinishAction(_ => DropCargo());
            Toil toStable = Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.InteractionCell);
            toStable.AddPreTickIntervalAction(_ => CheckCarrierBattery());
            yield return Toils_Jump.JumpIf(toStable, () => Cargo != null && pawn.carryTracker.CarriedThing == Cargo);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedOrNull(TargetIndex.A)
                .FailOn(() => Cargo is Pawn cargo && cargo.IsForbidden(this.pawn))
                .FailOn(() => !CanCarry(Cargo))
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return toStable;
            Toil dock = ToilMaker.MakeToil("DockCarried");
            dock.initAction = () =>
            {
                Thing cargo = Cargo;
                if (!pawn.carryTracker.TryDropCarriedThing(Stable.InteractionCell, ThingPlaceMode.Near, out Thing dropped))
                {
                    QR.Log($"{pawn} couldn't set {cargo} down at {Stable}");
                    return;
                }
                if (dropped is Pawn takee)
                {
                    if (Stable.TryDock(takee))
                        QR.Log($"{pawn} carried {takee} home to {Stable}");
                    else
                        QR.Log($"{pawn} carried {takee} to {Stable}, which has no free slot now; set it down");
                }
                else if (dropped is Corpse wreck)
                {
                    // Adjacent to the stable. False leaves the real wreck untouched on the ground; the order retries.
                    if (Stable.AcceptWreck(wreck))
                        QR.Log($"{pawn} delivered {wreck} to {Stable}");
                    else
                        QR.Log($"{pawn} set {wreck} down beside {Stable}; it did not take the wreck");
                }
            };
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }

        private static bool CanCarry(Thing cargo) =>
            cargo is Pawn pawn ? !pawn.Dead && Retrieval.Casualty(pawn)
                : cargo is Corpse wreck && !wreck.Destroyed;

        private void DropCargo()
        {
            Thing cargo = pawn.carryTracker?.CarriedThing;
            if (!pawn.Spawned || !(cargo is Pawn || cargo is Corpse))
                return;
            pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out Thing _);
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
                QR.Log($"{pawn} at {comp.Battery:P0} carrying {Cargo}: charging at the stable wins ({why}); finishing");
                return;
            }
            QR.Log($"{pawn} at {comp.Battery:P0} carrying {Cargo}: a charge spot wins ({why}); setting it down");
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
