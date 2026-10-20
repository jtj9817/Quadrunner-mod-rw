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
        public List<WorkGiverDef> workGivers = new List<WorkGiverDef>();
        public List<WorkGiverDef> mapWide = new List<WorkGiverDef>();
        public List<JobDef> jobs = new List<JobDef>();
        public float raidSpeedFactor = 3f;
        public float raidDrainFactor = 2f;

        public static RobodogWork Of(Thing thing) => thing?.def.GetModExtension<RobodogWork>();
    }

    /// <summary>
    /// Finds work for a dog of a stable: the first work giver (in <see cref="RobodogWork.workGivers"/> order) with a
    /// target in the stable's service radius (or anywhere, for <see cref="RobodogWork.mapWide"/> ones), closest to the
    /// dog first, after carrying powered-down dogs home (<see cref="Retrieval"/>). A target closer to another stable that
    /// has an idle dog is left to that stable. None during a raid. A medic only rescues, raid or not.
    /// </summary>
    public static class DogWork
    {
        public static Job FindJob(Pawn dog)
        {
            RobodogWork work = RobodogWork.Of(dog);
            CompRobodog comp = CompRobodog.Of(dog);
            Building_Stable stable = comp?.stable;
            if (work == null || stable == null || !stable.Spawned || stable.Map != dog.Map || !dog.Spawned
                || comp.WantsCharge || comp.PoweredDown)
                return null;
            if (work.rescue)
                return Rescue.JobFor(dog);
            if (Raid.Active(dog.Map))
                return null;
            List<Building_Stable> others = dog.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Stable>()
                .Where(s => s != stable && s.HasIdleDog).ToList();
            // Powered-down dogs first, map-wide.
            Job carry = Retrieval.JobFor(dog, t => others.Any(s =>
                s.InteractionCell.DistanceToSquared(t.Position) < dog.Position.DistanceToSquared(t.Position)));
            if (carry != null)
                return carry;
            foreach (WorkGiverDef giver in work.workGivers)
            {
                if (!(giver.Worker is WorkGiver_Scanner scanner) || scanner.ShouldSkip(dog))
                    continue;
                bool mapWide = work.mapWide.Contains(giver);
                bool Valid(Thing t)
                {
                    if (!mapWide && !stable.Covers(t.Position) || t.IsForbidden(dog))
                        return false;
                    float mine = dog.Position.DistanceToSquared(t.Position);
                    if (others.Any(s => (mapWide || s.Covers(t.Position)) && s.InteractionCell.DistanceToSquared(t.Position) < mine))
                        return false;
                    return scanner.HasJobOnThing(dog, t);
                }
                IEnumerable<Thing> things = scanner.PotentialWorkThingsGlobal(dog)
                    ?? dog.Map.listerThings.ThingsMatching(scanner.PotentialWorkThingRequest);
                Thing target = GenClosest.ClosestThing_Global_Reachable(dog.Position, dog.Map, things, scanner.PathEndMode,
                    TraverseParms.For(dog, scanner.MaxPathDanger(dog)), 9999f, Valid);
                if (target == null)
                    continue;
                Job job = scanner.JobOnThing(dog, target);
                if (job == null || !work.jobs.Contains(job.def))
                    continue;
                // Vanilla queues more targets near the first one (Clean: all filth within ~5 cells); keep only ours.
                job.GetTargetQueue(TargetIndex.A).RemoveAll(t => t.HasThing && t.Thing != target && !Valid(t.Thing));
                job.workGiverDef = giver;
                return job;
            }
            return null;
        }
    }

    /// <summary>A dog that is out keeps working in its stable's radius until nothing is left, then docks.</summary>
    public class JobGiver_RobodogWork : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn) => DogWork.FindJob(pawn);
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

    /// <summary>Move speed × <see cref="RobodogWork.raidSpeedFactor"/> for a working dog with a stable during a raid.</summary>
    public class StatPart_RobodogRaid : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (Factor(req, out float f))
                val *= f;
        }

        public override string ExplanationPart(StatRequest req) =>
            Factor(req, out float f) ? $"Raid, finishing up: x{f.ToStringPercent()}" : null;

        private static bool Factor(StatRequest req, out float factor)
        {
            factor = 1f;
            if (!(req.Thing is Pawn pawn) || !pawn.Spawned || CompRobodog.Of(pawn)?.stable == null)
                return false;
            RobodogWork work = RobodogWork.Of(pawn);
            if (work == null || !Raid.Active(pawn.Map))
                return false;
            factor = work.raidSpeedFactor;
            return true;
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
