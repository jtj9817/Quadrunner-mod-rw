using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Medirunners foam out fires in the Home area (plan 0008) with their integrated sprayer
    /// (<see cref="RobodogWork.builtInGun"/>): vanilla's foam turret spray on a dog. The network reports every revealed
    /// Home fire to its war queue, after rescues, so it goes on during raids, and sends the nearest idle medic; one
    /// burst usually takes neighbouring fires with it. Foam is unlimited; each burst costs battery
    /// (<see cref="Verb_FoamSpray"/>).
    /// </summary>
    public static class Firefighting
    {
        /// <summary>Jobs expire this often, so the medic re-requests its fire (walk into range, then spray).</summary>
        private const int RecheckTicks = 60;

        /// <summary>Burning, on a revealed Home cell (attached fires included: they sit on their parent's cell).</summary>
        public static IEnumerable<Fire> Fires(Map map) =>
            map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>()
                .Where(f => f.Spawned && map.areaManager.Home[f.Position] && !f.Position.Fogged(map));

        /// <summary>
        /// That one fire only — the network picks which. Vanilla AttackStatic with the sprayer from where the dog
        /// stands if it can hit, else a walk to a cast position; null if it has no sprayer or no position reaches.
        /// </summary>
        public static Job JobFor(Pawn dog, Fire fire)
        {
            if (fire == null || fire.Destroyed || !fire.Spawned || fire.Map != dog.Map)
                return null;
            Tiers.Arm(dog, Tiers.Gun(dog, 0));
            Verb verb = dog.equipment?.PrimaryEq?.PrimaryVerb;
            if (!(verb is Verb_FoamSpray) || !verb.Available())
                return null;
            Job job;
            if (verb.CanHitTarget(fire))
            {
                job = JobMaker.MakeJob(JobDefOf.AttackStatic, fire);
                job.endIfCantShootTargetFromCurPos = true;
            }
            else if (CastPositionFinder.TryFindCastPosition(new CastPositionRequest
                     {
                         caster = dog, target = fire, verb = verb, maxRangeFromTarget = verb.verbProps.range,
                         wantCoverFromTarget = false,
                     }, out IntVec3 cell))
                job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            else
                return null;
            job.expiryInterval = RecheckTicks;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }

    /// <summary>Vanilla's foam turret spray, paid for from the dog's battery instead of a fuel tank, once per burst.</summary>
    public class Verb_FoamSpray : Verb_ArcSprayProjectile
    {
        public override void WarmupComplete()
        {
            base.WarmupComplete();
            CompRobodog comp = CompRobodog.Of(CasterPawn);
            float drain = RobodogWork.Of(CasterPawn)?.foamDrainPerBurst ?? 0f;
            if (comp != null && drain > 0f)
                comp.SetBattery(comp.Battery - drain);
        }
    }
}
