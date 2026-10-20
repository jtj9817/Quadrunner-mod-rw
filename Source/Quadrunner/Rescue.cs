using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Medirunners carry the downed to a bed: colonists, then guests, prisoners and colony animals, never enemies;
    /// within a rank the fastest-bleeding first. Map-wide and under fire (no enemy-near check, unlike vanilla). The
    /// patient's reservation keeps it to one Medirunner per patient. Vanilla's own jobs do the carrying: Rescue, or
    /// TakeWoundedPrisonerToBed for a prisoner.
    /// </summary>
    public static class Rescue
    {
        /// <summary>0 colonist, 1 guest, 2 prisoner, 3 colony animal; -1 for anyone else (enemies, robodogs, wild animals).</summary>
        public static int Rank(Pawn p)
        {
            // Prisoners first: their own faction is often hostile.
            if (p.IsPrisonerOfColony)
                return 2;
            if (CompRobodog.IsRobodog(p) || p.HostileTo(Faction.OfPlayer))
                return -1;
            if (p.Faction == Faction.OfPlayer)
                return p.RaceProps.Humanlike ? 0 : p.RaceProps.Animal ? 3 : -1;
            return p.RaceProps.Humanlike ? 1 : -1;
        }

        /// <summary>Downed, out of bed, and ours to rescue (vanilla's float menu also skips "no medical care").</summary>
        public static IEnumerable<Pawn> Patients(Map map) =>
            map.mapPawns.SpawnedDownedPawns.Where(p => Rank(p) >= 0 && HealthAIUtility.WantsToBeRescued(p)
                && p.playerSettings?.medCare != MedicalCareCategory.NoCare);

        public static bool AnyPatient(Map map) => Patients(map).Any();

        public static Building_Bed BedFor(Pawn patient, Pawn rescuer) =>
            RestUtility.FindBedFor(patient, rescuer, checkSocialProperness: false, ignoreOtherReservations: false,
                patient.GuestStatus);

        /// <summary>The first patient by rank, then bleed rate, then distance, that <paramref name="dog"/> can take to a bed.</summary>
        public static Job JobFor(Pawn dog)
        {
            (Pawn patient, Building_Bed bed) = Patients(dog.Map)
                .Where(p => !p.IsForbidden(dog) && dog.CanReserveAndReach(p, PathEndMode.OnCell, Danger.Deadly))
                .OrderBy(Rank)
                .ThenByDescending(p => p.health.hediffSet.BleedRateTotal)
                .ThenBy(p => p.Position.DistanceToSquared(dog.Position))
                .Select(p => (p, BedFor(p, dog)))
                .FirstOrDefault(pb => pb.Item2 != null);
            if (patient == null)
                return null;
            Job job = JobMaker.MakeJob(patient.IsPrisonerOfColony ? JobDefOf.TakeWoundedPrisonerToBed : JobDefOf.Rescue,
                patient, bed);
            job.count = 1;
            return job;
        }
    }
}
