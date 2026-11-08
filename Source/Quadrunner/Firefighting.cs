using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Medirunners foam out fires in the Home area (plan 0008) with their integrated sprayer
    /// (<see cref="RobodogWork.builtInGun"/>): vanilla's foam turret spray on a dog. The network reports every revealed
    /// Home fire to its war queue, after rescues. Deployed medics retain a burning focus and acquire the nearest
    /// remaining fire before considering home. Foam is unlimited; each burst costs battery
    /// (<see cref="Verb_FoamSpray"/>). Between ignitions, responders hold in place, longer while lightning can recur.
    /// </summary>
    public static class Firefighting
    {
        /// <summary>Jobs expire this often, so the medic re-requests its fire (walk into range, then spray).</summary>
        private const int RecheckTicks = 60;
        private const int StandbyTicks = 180; // User-specified three game seconds (60 ticks/second).

        internal static bool Eligible(Fire fire, Map map) => fire != null && !fire.Destroyed && fire.Spawned
            && fire.Map == map && map.areaManager.Home[fire.Position] && !fire.Position.Fogged(map);

        internal static bool AimedAt(Job job) => job != null && !job.playerForced
            && (job.def == JobDefOf.AttackStatic && job.targetA.Thing is Fire
                || job.def == JobDefOf.Goto && job.targetB.Thing is Fire);

        private static bool LightningWeather(WeatherDef weather)
        {
            foreach (WeatherEventMaker maker in weather.eventMakers)
                if (typeof(WeatherEvent_LightningStrike).IsAssignableFrom(maker.eventClass))
                    return true;
            return false;
        }

        private static bool StormActive(Map map) =>
            map.gameConditionManager.GetActiveCondition<GameCondition_Flashstorm>() != null
            || LightningWeather(map.weatherManager.curWeather)
            || map.weatherManager.TransitionLerpFactor < 1f && LightningWeather(map.weatherManager.lastWeather);

        /// <summary>Only a previously working responder holds here; charging is earlier in the think tree.</summary>
        internal static Job StandbyJob(Pawn dog)
        {
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp.fireStandbyUntil < 0)
                return null;
            int now = Find.TickManager.TicksGame;
            if (StormActive(dog.Map))
                comp.fireStandbyUntil = 0;
            else if (comp.fireStandbyUntil == 0)
                comp.fireStandbyUntil = now + StandbyTicks;
            else if (now >= comp.fireStandbyUntil)
            {
                comp.fireStandbyUntil = -1;
                return null;
            }
            if (dog.CurJobDef == JobDefOf.Wait && !dog.CurJob.playerForced)
                return dog.CurJob;
            Job wait = JobMaker.MakeJob(JobDefOf.Wait);
            wait.expiryInterval = RecheckTicks;
            return wait;
        }


        /// <summary>
        /// That one fire only — the network picks which. Vanilla AttackStatic with the sprayer from where the dog
        /// stands if it can hit, else a walk to a cast position; null if it has no sprayer, no position reaches, or
        /// the walk to the spray position crosses a known trap (plan 0011) — the fire stays queued for another dog
        /// or a later attempt, on claim and on every mid-fight rebuild of the walk leg alike.
        /// </summary>
        public static Job JobFor(Pawn dog, Fire fire)
        {
            if (!Eligible(fire, dog.Map))
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
            {
                // A fire whose walk to the spray position crosses a trap is not ours to take (plan 0011).
                if (TrapAvoidance.RouteEntersTrap(dog, cell, PathEndMode.OnCell, out _))
                    return null;
                job = JobMaker.MakeJob(JobDefOf.Goto, cell);
                job.targetB = fire; // Retain fire intent across travel, retargeting and save/load.
            }
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
