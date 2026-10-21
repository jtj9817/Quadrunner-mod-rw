using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>On a dog race that works: which vanilla work givers it runs, and how a raid changes it.</summary>
    public class RobodogWork : DefModExtension
    {
        /// <summary>A medic (Medirunner): it only rescues the downed (<see cref="Rescue"/>), raid or not.</summary>
        public bool rescue;
        /// <summary>A Combatdog: it only fights and captures hostiles (<see cref="Combat"/>).</summary>
        public bool combat;
        public List<WorkGiverDef> workGivers = new List<WorkGiverDef>();
        public List<JobDef> jobs = new List<JobDef>();
        public float raidSpeedFactor = 3f;
        public float raidDrainFactor = 2f;
        /// <summary>Move speed factor whenever the dog is out (docked dogs are despawned; Combatdogs: 200% deployed).</summary>
        public float deployedSpeedFactor = 1f;
        /// <summary>Combatdogs: hostiles seen within this many cells join the network's war queue.</summary>
        public float engageRange = 15f;
        /// <summary>Discovery: radius of the local scan around an active dog that reports work to the network.</summary>
        public float scanRadius = 15f;
        /// <summary>Discovery: ticks between local scans (and retries of a task no dog could take).</summary>
        public int scanInterval = 120;
        /// <summary>Dispatch: ticks between the network's scheduler refreshes.</summary>
        public int dispatchInterval = 60;
        /// <summary>Dispatch: reassign a task whose assignee has made no progress for this many ticks.</summary>
        public int progressTimeout = 2500;
        /// <summary>Exploration: distance between survey goals, and kept from another explorer's goal.</summary>
        public int surveySpacing = 20;
        /// <summary>Exploration: a survey job expires this often, so found work can preempt the travel.</summary>
        public int surveyJobTicks = 600;
        /// <summary>Exploration: maximum distance from the dog considered for its next survey destination.</summary>
        public float surveySearchRadius = 80f;

        public static RobodogWork Of(Thing thing) => thing?.def.GetModExtension<RobodogWork>();

        /// <summary>
        /// The one utility-work configuration: the QR_Quadrunner race's, which every dog discovers with — a Medirunner
        /// or Combatdog has no work list of its own, but still reports utility work it passes
        /// (<see cref="DogWork.ReportLocal"/>).
        /// </summary>
        public static RobodogWork Utility => QR_DefOf.QR_Quadrunner.race.GetModExtension<RobodogWork>();
    }

    /// <summary>
    /// A dog's share of the shared dispatch network (<see cref="DogNetwork"/>): the local scan that reports known-area
    /// work around it (<see cref="ReportLocal"/>) and the vanilla job it builds for one specific queued target
    /// (<see cref="JobFor"/>). No dog ever searches the whole map for work on its own.
    /// </summary>
    public static class DogWork
    {
        /// <summary>
        /// Reports utility work and powered-down dogs within the scan radius of <paramref name="observer"/>: cells are
        /// enumerated, so nothing outside the radius (or in fog) is ever seen. Eligibility for a particular dog,
        /// forbidden targets and reservations are left to assignment (<see cref="JobFor"/>), so a report never hides
        /// work another dog could do, and the network deduplicates (<see cref="DogNetwork.Report"/>).
        /// </summary>
        public static void ReportLocal(Pawn observer, DogNetwork network)
        {
            if (observer == null || !observer.Spawned || network?.Map != observer.Map)
                return;
            RobodogWork work = RobodogWork.Utility;
            if (work == null)
                return;
            Map map = observer.Map;
            ICollection<Thing> haulables = map.listerHaulables.ThingsPotentiallyNeedingHauling();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(observer.Position, work.scanRadius, useCenter: true))
            {
                if (!cell.InBounds(map) || map.fogGrid.IsFogged(cell))
                    continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing.IsForbidden(Faction.OfPlayer))
                        continue;
                    if (CompRobodog.Of(thing)?.PoweredDown == true && thing != observer)
                    {
                        // Recently powered down with its field fix in stock: the colonists bring it.
                        if (!Retrieval.AwaitsFieldFix((Pawn)thing))
                            network.Report(NetworkTaskKind.Retrieve, thing);
                        continue;
                    }
                    foreach (WorkGiverDef giver in work.workGivers)
                        if (giver.Worker is WorkGiver_Scanner scanner && Known(scanner, thing, haulables))
                        {
                            network.Report(KindOf(giver), thing, giver);
                            break;
                        }
                }
            }
        }

        /// <summary>The queue kind a giver's targets are reported as; any other hauling scanner reports as hauling.</summary>
        private static NetworkTaskKind KindOf(WorkGiverDef giver)
        {
            if (giver.Worker is WorkGiver_ConstructDeliverResourcesToFrames)
                return NetworkTaskKind.Frame;
            if (giver.Worker is WorkGiver_ConstructDeliverResourcesToBlueprints)
                return NetworkTaskKind.Blueprint;
            if (giver.Worker is WorkGiver_CleanFilth)
                return NetworkTaskKind.Clean;
            return NetworkTaskKind.Haul;
        }

        /// <summary>Whether <paramref name="thing"/> is work a scan could know about, without asking any particular dog.</summary>
        private static bool Known(WorkGiver_Scanner scanner, Thing thing, ICollection<Thing> haulables)
        {
            switch (scanner)
            {
                case WorkGiver_ConstructDeliverResourcesToFrames _:
                    return thing is Frame frame && frame.Faction == Faction.OfPlayer && NeedsMaterials(frame);
                case WorkGiver_ConstructDeliverResourcesToBlueprints _:
                    return thing is Blueprint print && print.Faction == Faction.OfPlayer
                        && !(print.def.entityDefToBuild is ThingDef { plant: not null }) && NeedsMaterials(print);
                case WorkGiver_CleanFilth _:
                    // Vanilla also skips fresh filth (600 ticks) and filth outside the Home area.
                    return thing is Filth filth && filth.Map.areaManager.Home[filth.Position]
                        && filth.TicksSinceThickened >= 600;
                default:
                    // Vanilla's own "potentially needs hauling" list, consulted only about enumerated things.
                    return thing.def.category == ThingCategory.Item && !(thing is Corpse) && haulables.Contains(thing);
            }
        }

        private static bool NeedsMaterials(IConstructible target)
        {
            foreach (ThingDefCountClass cost in target.TotalMaterialCost())
                if (target.ThingCountNeeded(cost.thingDef) > 0)
                    return true;
            return false;
        }

        /// <summary>
        /// The vanilla job a dog takes for one specific network task's target — never a search for other work. The
        /// target must be live, allowed to this dog and free to take; the task's giver decides what the job is, and any
        /// extra targets vanilla queued next to it (Clean batches nearby filth) are trimmed to what the network
        /// discovered and nobody else is assigned, so no dog works targets it never saw.
        /// </summary>
        public static Job JobFor(Pawn dog, NetworkTask task)
        {
            if (dog == null || !dog.Spawned || task == null)
                return null;
            Thing target = task.Target(dog.Map);
            if (target == null || !target.Spawned || target.Map != dog.Map)
                return null;
            if (task.kind == NetworkTaskKind.Retrieve)
                return target is Pawn takee && takee != dog ? Retrieval.JobFor(dog, takee) : null;
            if (!(task.giver?.Worker is WorkGiver_Scanner scanner))
                return null;
            if (target.IsForbidden(dog) || scanner.ShouldSkip(dog)
                || !dog.CanReserveAndReach(target, scanner.PathEndMode, scanner.MaxPathDanger(dog))
                || !scanner.HasJobOnThing(dog, target))
                return null;
            Job job = scanner.JobOnThing(dog, target);
            if (job == null || !RobodogWork.Utility.jobs.Contains(job.def))
                return null;
            // Only what the network handed out: a job must not opportunistically pick up extra unreported stacks
            // (vanilla sets this for its own haul jobs, HaulAIUtility.HaulToStorageJob).
            job.haulOpportunisticDuplicates = false;
            job.workGiverDef = task.giver;
            TrimQueue(dog, task, job);
            return job;
        }

        /// <summary>
        /// Vanilla queues more targets near the first one (Clean: all filth in its room); keep only ones within one
        /// scan radius of the target, discovered by the network and not assigned to another dog.
        /// </summary>
        private static void TrimQueue(Pawn dog, NetworkTask task, Job job)
        {
            List<LocalTargetInfo> queue = job.GetTargetQueue(TargetIndex.A);
            if (queue == null)
                return;
            DogNetwork network = DogNetwork.For(dog.Map);
            RobodogWork work = RobodogWork.Utility;
            Thing target = task.Target(dog.Map);
            queue.RemoveAll(queued => queued.HasThing && queued.Thing != target
                && (queued.Thing.Position.DistanceTo(target.Position) > work.scanRadius
                    || !network.State.normal.Any(t => t.targetId == queued.Thing.ThingID)
                    || network.State.normal.Any(t => t.targetId == queued.Thing.ThingID
                        && t.assigneeId != null && t.assigneeId != dog.ThingID)));
        }

        /// <summary>How a dog would have to reach a task's target, for ranking routes to it.</summary>
        public static PathEndMode EndMode(NetworkTask task)
        {
            switch (task.kind)
            {
                case NetworkTaskKind.Retrieve:
                    return PathEndMode.ClosestTouch;
                case NetworkTaskKind.Rescue:
                    return PathEndMode.OnCell;
                case NetworkTaskKind.Combat:
                    return PathEndMode.Touch;
                default:
                    return task.giver?.Worker is WorkGiver_Scanner scanner ? scanner.PathEndMode : PathEndMode.Touch;
            }
        }
    }

    /// <summary>A dog's orders come from the shared dispatch network (<see cref="DogNetwork.TakeOrder"/>): assigned
    /// work, exploration or standby — never a private search of the map's work.</summary>
    public class JobGiver_RobodogWork : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn) =>
            pawn.Map == null ? null : DogNetwork.For(pawn.Map)?.TakeOrder(pawn);
    }

    /// <summary>"A raid is on": a hostile active threat to the player on the map, checked at most once a second.</summary>
    public static class Raid
    {
        // By map uniqueID, so a removed map isn't kept alive.
        private static readonly Dictionary<int, (int tick, bool active)> cache = new Dictionary<int, (int, bool)>();

        public static bool Active(Map map)
        {
            if (map == null)
                return false;
            int now = Find.TickManager.TicksGame;
            if (cache.TryGetValue(map.uniqueID, out (int tick, bool active) c) && now - c.tick < 60 && now >= c.tick)
                return c.active;
            bool active = GenHostility.AnyHostileActiveThreatToPlayer(map);
            cache[map.uniqueID] = (now, active);
            return active;
        }

        /// <summary>Forgets the cached answer, so the next check sees hostiles spawned this tick (scenarios).</summary>
        public static void Recheck(Map map) => cache.Remove(map.uniqueID);
    }

    /// <summary>
    /// Move speed × <see cref="RobodogWork.deployedSpeedFactor"/> for a dog that is out (docked dogs are despawned),
    /// with or without a stable, and also × <see cref="RobodogWork.raidSpeedFactor"/> during a raid.
    /// </summary>
    public class StatPart_RobodogRaid : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Factor(req, out float f))
                val *= f;
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!Factor(req, out float f))
                return null;
            RobodogWork work = RobodogWork.Of(req.Thing);
            return work.deployedSpeedFactor != 1f && f == work.deployedSpeedFactor
                ? $"Deployed: x{f.ToStringPercent()}" : $"Raid, standby: x{f.ToStringPercent()}";
        }

        private static bool Factor(StatRequest req, out float factor)
        {
            factor = 1f;
            if (!(req.Thing is Pawn pawn) || !pawn.Spawned || CompRobodog.Of(pawn) == null)
                return false;
            RobodogWork work = RobodogWork.Of(pawn);
            if (work == null)
                return false;
            factor = work.deployedSpeedFactor * (Raid.Active(pawn.Map) ? work.raidSpeedFactor : 1f);
            return factor != 1f;
        }
    }

    /// <summary>
    /// Vanilla construction work givers ask <c>pawn.workSettings</c> (null on a dog: NRE) and the worker's skills. A
    /// robodog only ever delivers resources, so it takes the check without work settings or skills.
    /// </summary>
    [HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.CanConstruct),
        new[] { typeof(Thing), typeof(Pawn), typeof(WorkTypeDef), typeof(bool), typeof(JobDef) })]
    public static class Patch_GenConstruct_CanConstruct
    {
        public static bool Prefix(Thing t, Pawn pawn, bool forced, JobDef jobForReservation, ref bool __result)
        {
            if (!CompRobodog.IsRobodog(pawn))
                return true;
            __result = GenConstruct.CanConstruct(t, pawn, checkSkills: false, forced, jobForReservation);
            return false;
        }
    }
}
