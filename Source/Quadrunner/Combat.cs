using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Combatdogs work their stable's target queue, nearest first: vanilla AttackMelee on a standing target (it ends when
    /// the target goes down), and a downed one captured: vanilla Capture to a free prisoner bed, else carried to the
    /// nearest empty room that isn't a bedroom, barracks or prison (<see cref="JobDriver_CarryToRoom"/>) and left there;
    /// the dead drop out of the queue. Hostiles a Combatdog sees within its engage range join the queue. With nothing
    /// to do during a raid it guards its stable; once the raid is over and the queue empty it docks. Jobs expire after
    /// <see cref="RecheckTicks"/>, so the queue is re-sorted about once a second.
    /// </summary>
    public static class Combat
    {
        private const int RecheckTicks = 60;
        /// <summary>A guarding Combatdog walks back once it is farther than this from its stable.</summary>
        private const float GuardRadius = 4f;

        /// <summary>Out without a target (heading home or guarding), with battery to spare: a ping can redirect it.</summary>
        public static bool Answers(Pawn dog) =>
            CompRobodog.Of(dog) is CompRobodog c && !c.WantsCharge && !c.PoweredDown
            && (dog.CurJob == null || dog.CurJobDef == QR_DefOf.QR_Dock || dog.CurJobDef == JobDefOf.Wait
                || dog.CurJobDef == JobDefOf.Goto);

        public static Job JobFor(Pawn dog, Building_Stable stable)
        {
            Spot(dog, stable);
            foreach (Pawn target in stable.Targets(dog.Position))
            {
                // Carried by someone: not ours to take now.
                if (!target.Spawned)
                    continue;
                Job job = target.Downed ? CaptureJob(dog, stable, target) : AttackJob(dog, target);
                if (job != null)
                    return job;
            }
            return Raid.Active(dog.Map) ? GuardJob(dog, stable) : null;
        }

        /// <summary>Standing hostiles in sight within the engage range join the queue.</summary>
        private static void Spot(Pawn dog, Building_Stable stable)
        {
            Map map = dog.Map;
            float range = RobodogWork.Of(dog).engageRange;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))
            {
                if (t.Thing is Pawn p && !p.Downed && p.Position.InHorDistOf(dog.Position, range) && !p.Position.Fogged(map)
                    && GenSight.LineOfSight(dog.Position, p.Position, map) && stable.Enqueue(p))
                    QR.Log($"{dog} spotted {p} at {p.Position.DistanceTo(dog.Position):F0} cells; queued at {stable}");
            }
        }

        private static Job AttackJob(Pawn dog, Pawn target)
        {
            if (!dog.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return null;
            Job job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.expiryInterval = RecheckTicks;
            job.checkOverrideOnExpire = true;
            return job;
        }

        private static Job CaptureJob(Pawn dog, Building_Stable stable, Pawn target)
        {
            if (!dog.CanReserveAndReach(target, PathEndMode.ClosestTouch, Danger.Deadly))
                return null;
            if (target.RaceProps.Humanlike && target.guest != null)
            {
                Building_Bed bed = RestUtility.FindBedFor(target, dog, checkSocialProperness: false,
                    ignoreOtherReservations: false, GuestStatus.Prisoner);
                if (bed != null)
                {
                    Job capture = JobMaker.MakeJob(JobDefOf.Capture, target, bed);
                    capture.count = 1;
                    return capture;
                }
            }
            if (EmptyRoom(target.GetRoom()))
            {
                QR.Log($"{dog}: {target} lies downed in an empty room already; released from {stable}'s queue");
                stable.Release(target);
                return null;
            }
            IntVec3 cell = JobGiver_WaitInRoom.CellInNearestRoom(dog, target.GetRegion(), EmptyRoom);
            if (!cell.IsValid)
                return null;
            Job carry = JobMaker.MakeJob(QR_DefOf.QR_CarryToRoom, target, cell);
            carry.count = 1;
            return carry;
        }

        /// <summary>Indoors, not a bedroom, barracks or prison, and no colonist in it (agent default, 2026-10-20).</summary>
        public static bool EmptyRoom(Room room) =>
            JobGiver_WaitInRoom.Qualifies(room) && !room.Map.mapPawns.FreeColonistsSpawned.Any(c => c.GetRoom() == room);

        /// <summary>
        /// Raid on, nothing to fight: wait by the stable (agent default, 2026-10-20, until patrol waypoints exist),
        /// looking again every <see cref="RecheckTicks"/>.
        /// </summary>
        private static Job GuardJob(Pawn dog, Building_Stable stable)
        {
            if (!dog.Position.InHorDistOf(stable.InteractionCell, GuardRadius))
                return dog.CanReach(stable.InteractionCell, PathEndMode.OnCell, Danger.Deadly)
                    ? JobMaker.MakeJob(JobDefOf.Goto, stable.InteractionCell) : null;
            Job wait = JobMaker.MakeJob(JobDefOf.Wait);
            wait.expiryInterval = RecheckTicks;
            return wait;
        }
    }

    /// <summary>
    /// Carries a downed hostile (A) to a cell (B) in an empty room and leaves it there downed, not as a prisoner; it then
    /// leaves the stable's queue.
    /// </summary>
    public class JobDriver_CarryToRoom : JobDriver
    {
        private Pawn Takee => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(Takee, job, errorOnFailed: errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch)
                .FailOnDespawnedNullOrForbidden(TargetIndex.A)
                .FailOn(() => !Takee.Downed)
                .FailOnSomeonePhysicallyInteracting(TargetIndex.A);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell).FailOn(() => !pawn.IsCarryingPawn(Takee));
            Toil drop = ToilMaker.MakeToil("DropInRoom");
            drop.initAction = () =>
            {
                Pawn takee = Takee;
                pawn.carryTracker.TryDropCarriedThing(job.targetB.Cell, ThingPlaceMode.Near, out Thing _);
                CompRobodog.Of(pawn)?.stable?.Release(takee);
                QR.Log($"{pawn} left {takee} downed in a room at {takee.PositionHeld}");
            };
            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return drop;
        }
    }
}
