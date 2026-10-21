using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Medirunners carry the downed to a bed: colonists, then guests, prisoners and colony animals, never enemies;
    /// within a rank the fastest-bleeding first (the network sorts <see cref="Patients"/> and sends the nearest
    /// available medic). Map-wide and under fire (no enemy-near check, unlike vanilla). The patient's reservation
    /// keeps it to one Medirunner per patient. Vanilla's own jobs do the carrying: Rescue, or
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

        /// <summary>
        /// The job for that one patient only — the network picks which (rank, then bleeding, then the nearest
        /// available medic). Null if it is not eligible, not ours to take (forbidden, reserved or unreachable), or
        /// no bed will have it.
        /// </summary>
        public static Job JobFor(Pawn dog, Pawn patient)
        {
            if (patient == null || patient.Dead || !patient.Spawned || !patient.Downed || Rank(patient) < 0
                || !HealthAIUtility.WantsToBeRescued(patient)
                || patient.playerSettings?.medCare == MedicalCareCategory.NoCare
                || patient.IsForbidden(dog) || !dog.CanReserveAndReach(patient, PathEndMode.OnCell, Danger.Deadly))
                return null;
            Building_Bed bed = BedFor(patient, dog);
            if (bed == null)
                return null;
            Job job = JobMaker.MakeJob(patient.IsPrisonerOfColony ? JobDefOf.TakeWoundedPrisonerToBed : JobDefOf.Rescue,
                patient, bed);
            job.count = 1;
            return job;
        }
    }
}
