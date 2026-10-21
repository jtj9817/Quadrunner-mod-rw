using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Combatdogs fight the dog network's war queue: vanilla AttackMelee on a standing target (it ends when the
    /// target goes down), and a downed one captured: vanilla Capture to a free prisoner bed, else carried to the
    /// nearest empty room that isn't a bedroom, barracks or prison (<see cref="JobDriver_CarryToRoom"/>) and left
    /// there; the dead drop out of the queue. Hostiles a Combatdog sees within its engage range join it
    /// (<see cref="Spot"/>), as do attackers that hit one of the player's dogs (<see cref="DogNetwork.Ping"/>).
    /// With nothing assigned while a raid is on or targets remain, it walks its stable's patrol waypoints in order,
    /// in a loop — the coordinator stable's without one of its own, and without any stable it guards the
    /// coordinator's position, itself included (guarding then means holding its ground, never chasing its own moving
    /// position); without waypoints it guards the stable during a raid; once the raid is over and the queue empty it
    /// docks. Jobs expire after <see cref="RecheckTicks"/>, so the dog re-requests its assigned target about once a
    /// second.
    /// </summary>
    public static class Combat
    {
        private const int RecheckTicks = 60;
        /// <summary>A guarding Combatdog walks back once it is farther than this from its post.</summary>
        private const float GuardRadius = 4f;

        /// <summary>Standing hostiles in sight within the engage range join the network's war queue.</summary>
        public static void Spot(Pawn dog, DogNetwork network)
        {
            Map map = dog.Map;
            float range = RobodogWork.Of(dog).engageRange;
            foreach (IAttackTarget t in map.attackTargetsCache.TargetsHostileToFaction(Faction.OfPlayer))
            {
                if (t.Thing is Pawn p && !p.Downed && p.Position.InHorDistOf(dog.Position, range) && !p.Position.Fogged(map)
                    && GenSight.LineOfSight(dog.Position, p.Position, map) && network.Report(NetworkTaskKind.Combat, p))
                    QR.Log($"{dog} spotted {p} at {p.Position.DistanceTo(dog.Position):F0} cells; queued on the network");
            }
        }

        /// <summary>
        /// That one target only — the network picks which, never this. A standing one is attacked, a downed one
        /// captured. Done with it — dead, gone from the map, a prisoner, or no longer hostile — it leaves the war
        /// queue; one carried by someone else stays queued but is not ours to take now. Works without a stable.
        /// </summary>
        public static Job JobFor(Pawn dog, Pawn target)
        {
            DogNetwork network = DogNetwork.For(dog.Map);
            if (target == null || target.Destroyed || target.Dead || target.MapHeld != dog.Map
                || target.IsPrisonerOfColony || !target.HostileTo(Faction.OfPlayer))
            {
                network?.Release(target);
                return null;
            }
            return target.Downed ? CaptureJob(dog, network, target) : AttackJob(dog, target);
        }

        /// <summary>
        /// With nothing assigned to fight: the patrol, else guarding — its own stable's waypoints (the coordinator
        /// stable's without one), and without any stable the coordinator's position, which may be this very dog: it
        /// then holds its ground rather than chasing its own moving position. An equivalent walk or wait already
        /// under way is returned as-is, so a scheduler refresh does not restart it.
        /// </summary>
        public static Job IdleJob(Pawn dog, DogNetwork network)
        {
            if (network == null || !network.War && network.WarQueue.Count == 0)
                return null;
            Building_Stable home = CompRobodog.Of(dog)?.stable;
            if (home == null || !home.Spawned || home.Map != dog.Map)
                home = network.Coordinator as Building_Stable;
            if (home != null && (!home.Spawned || home.Map != dog.Map))
                home = null;
            if (home != null)
                return PatrolJob(dog, home.Waypoints)
                    ?? (network.War ? GuardJob(dog, home.InteractionCell) : null);
            Thing node = network.Coordinator;
            return GuardJob(dog, node != null && node.Spawned && node.MapHeld == dog.Map ? node.Position : dog.Position);
        }

        /// <summary>
        /// Claws: vanilla AttackMelee. A gun (Tier 2 on, the slot for the range, see <see cref="Tiers"/>): vanilla
        /// AttackStatic from where it stands if it can hit, else a walk to a cast position as vanilla's fighting AI
        /// finds one, else the claws.
        /// </summary>
        private static Job AttackJob(Pawn dog, Pawn target)
        {
            if (!dog.CanReach(target, PathEndMode.Touch, Danger.Deadly))
                return null;
            Tiers.Arm(dog, Tiers.GunFor(dog, target));
            Verb verb = dog.equipment?.PrimaryEq?.PrimaryVerb;
            Job job = null;
            if (verb != null && verb.Available())
            {
                if (verb.CanHitTarget(target))
                {
                    job = JobMaker.MakeJob(JobDefOf.AttackStatic, target);
                    job.endIfCantShootTargetFromCurPos = true;
                }
                else if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest
                         {
                             caster = dog, target = target, verb = verb, maxRangeFromTarget = verb.verbProps.range,
                             wantCoverFromTarget = false,
                         }, out IntVec3 cell))
                    job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            }
            job = job ?? JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.expiryInterval = RecheckTicks;
            job.checkOverrideOnExpire = true;
            return job;
        }

        private static Job CaptureJob(Pawn dog, DogNetwork network, Pawn target)
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
                QR.Log($"{dog}: {target} lies downed in an empty room already; released from the network's war queue");
                network.Release(target);
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
        /// To the next of the waypoints (user, 2026-10-20: patrol in a loop while a raid is on or targets remain);
        /// one reached or out of reach counts as passed. Null without waypoints or with none reachable. The job
        /// expires like the others so the dog keeps looking; a Goto already walking there is kept — vanilla keeps
        /// the same Goto running rather than repathing, and the scheduler re-asks every refresh.
        /// </summary>
        private static Job PatrolJob(Pawn dog, IReadOnlyList<IntVec3> points)
        {
            if (points == null || points.Count == 0)
                return null;
            CompRobodog comp = CompRobodog.Of(dog);
            for (int tries = 0; tries < points.Count; tries++)
            {
                int i = comp.waypointsReached % points.Count;
                IntVec3 cell = points[i];
                if (dog.Position.InHorDistOf(cell, 1.5f) || !dog.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
                {
                    QR.Log($"{dog} {(dog.Position.InHorDistOf(cell, 1.5f) ? "reached" : "can't reach")} patrol waypoint {i + 1} at {cell}");
                    comp.waypointsReached++;
                    continue;
                }
                if (WalkingTo(dog, cell, out Job running))
                    return running;
                Job job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                job.expiryInterval = RecheckTicks;
                job.checkOverrideOnExpire = true;
                return job;
            }
            return null;
        }

        /// <summary>
        /// Raid on, nothing to fight, no patrol: wait by the post (agent default, 2026-10-20), looking again every
        /// <see cref="RecheckTicks"/>. An invalid post — no stable, the coordinator gone, or the coordinator this
        /// very dog — means holding its ground where it stands.
        /// </summary>
        private static Job GuardJob(Pawn dog, IntVec3 post)
        {
            if (!post.IsValid || dog.Position.InHorDistOf(post, GuardRadius))
            {
                if (dog.CurJobDef == JobDefOf.Wait && !dog.CurJob.playerForced)
                    return dog.CurJob;
                Job wait = JobMaker.MakeJob(JobDefOf.Wait);
                wait.expiryInterval = RecheckTicks;
                return wait;
            }
            if (WalkingTo(dog, post, out Job walking))
                return walking;
            return dog.CanReach(post, PathEndMode.OnCell, Danger.Deadly)
                ? JobMaker.MakeJob(JobDefOf.Goto, post) : null;
        }

        /// <summary>A walk to <paramref name="cell"/> already under way (not player-ordered): keep it, don't restart it.</summary>
        private static bool WalkingTo(Pawn dog, IntVec3 cell, out Job job)
        {
            job = dog.CurJob;
            return job != null && job.def == JobDefOf.Goto && !job.playerForced && !job.targetA.HasThing
                && job.targetA.Cell == cell;
        }
    }

    /// <summary>
    /// Carries a downed hostile (A) to a cell (B) in an empty room and leaves it there downed, not as a prisoner; it
    /// then leaves the network's war queue.
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
                DogNetwork.For(pawn.Map)?.Release(takee);
                QR.Log($"{pawn} left {takee} downed in a room at {takee.PositionHeld}");
            };
            drop.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return drop;
        }
    }
}
