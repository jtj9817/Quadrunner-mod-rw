using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>On a dog race a colonist can ride (the Quadrunner): the ridden speed and where the rider sits.</summary>
    public class Rideable : DefModExtension
    {
        /// <summary>The rider's move speed while mounted, in cells per second.</summary>
        public float riddenMoveSpeed = 7f;
        // The rider's draw offset from the dog, per facing (x right, z up the screen).
        public Vector3 offsetNorth;
        public Vector3 offsetEast;
        public Vector3 offsetSouth;
        public Vector3 offsetWest;

        public static Rideable Of(Thing thing) => thing?.def.GetModExtension<Rideable>();
    }

    /// <summary>
    /// "Rider drives, dog carries": the rider's <see cref="JobDriver_RideTo"/> walks to the dog, mounts and goes to the
    /// destination at the dog's ridden speed, while the dog's <see cref="JobDriver_BeingRidden"/> keeps it in the
    /// rider's cell. The ride ends at the destination or when the rider's job ends for any reason (drafted, downed,
    /// another order); the rider steps off early before an indoor room's door or when the dog powers down.
    /// </summary>
    public static class Riding
    {
        // Dev mode: added to the XML offsets to tune them live (Dev tools → Tweak values), then copied into the XML.
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float northX = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float northZ = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float eastX = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float eastZ = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float southX = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float southZ = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float westX = 0f;
        [TweakValue("Quadrunner riding", -1f, 1f)] private static float westZ = 0f;

        /// <summary>The latest dismount, for scenarios; never saved.</summary>
        public static (string riderId, IntVec3 cell, string why) lastDismount;

        /// <summary>The dog <paramref name="rider"/> sits on now, or null.</summary>
        public static Pawn MountOf(Pawn rider) =>
            rider.jobs?.curDriver is JobDriver_RideTo ride && ride.Mounted ? ride.Dog : null;

        /// <summary>The pawn sitting on <paramref name="dog"/> now, or null.</summary>
        public static Pawn RiderOf(Pawn dog) =>
            dog.jobs?.curDriver is JobDriver_BeingRidden ridden && MountOf(ridden.Rider) == dog ? ridden.Rider : null;

        /// <summary>The rider's draw offset from <paramref name="dog"/> when facing <paramref name="rot"/>.</summary>
        public static Vector3 Offset(Pawn dog, Rot4 rot)
        {
            Rideable r = Rideable.Of(dog);
            if (rot == Rot4.North)
                return r.offsetNorth + new Vector3(northX, 0f, northZ);
            if (rot == Rot4.East)
                return r.offsetEast + new Vector3(eastX, 0f, eastZ);
            if (rot == Rot4.South)
                return r.offsetSouth + new Vector3(southX, 0f, southZ);
            return r.offsetWest + new Vector3(westX, 0f, westZ);
        }

        /// <summary>A player dog that can take a rider now: rideable, out, up, and not already on a ride.</summary>
        public static bool CanBeRidden(Pawn dog) =>
            Rideable.Of(dog) != null && dog.Spawned && dog.Faction == Faction.OfPlayer && !dog.Downed
            && dog.CurJobDef != QR_DefOf.QR_BeingRidden;

        public static bool CanRide(Pawn rider) => rider.RaceProps.Humanlike && !CompRobodog.IsRobodog(rider) && !rider.Downed;

        /// <summary>Orders <paramref name="rider"/> to ride <paramref name="dog"/> to <paramref name="cell"/>, undocking it from <paramref name="stable"/> first.</summary>
        public static bool Ride(Pawn rider, Pawn dog, Building_Stable stable, IntVec3 cell)
        {
            if (!rider.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
            {
                Messages.Message($"{rider.LabelShortCap} can't reach that spot.", MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }
            if (stable != null && stable.Docked.Contains(dog) && !stable.TryUndock(dog))
                return false;
            QR.Log($"{rider} rides {dog} ({CompRobodog.Of(dog).Battery:P0}) to {cell}");
            return rider.jobs.TryTakeOrderedJob(JobMaker.MakeJob(QR_DefOf.QR_RideTo, dog, cell), JobTag.Misc);
        }

        /// <summary>Lets the player pick the destination, then orders the ride; <paramref name="dog"/> is asked for only then.</summary>
        public static void ChooseDestination(Pawn rider, System.Func<Pawn> dog, Building_Stable stable)
        {
            var parms = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false };
            Find.Targeter.BeginTargeting(parms, t =>
            {
                Pawn d = dog();
                if (d != null)
                    Ride(rider, d, stable, t.Cell);
            }, rider);
        }

        /// <summary>A cell of a room that isn't outdoors (doors aside).</summary>
        public static bool Indoors(IntVec3 c, Map map) =>
            c.InBounds(map) && c.GetRoom(map) is Room r && !r.PsychologicallyOutdoors && !r.IsDoorway;

        /// <summary>An outdoor rider whose next step is into an indoor room, or into a door that leads into one.</summary>
        public static bool EnteringIndoors(Pawn rider)
        {
            Map map = rider.Map;
            if (!rider.pather.Moving || Indoors(rider.Position, map))
                return false;
            IntVec3 next = rider.pather.nextCell;
            if (next.GetDoor(map) != null)
                next += next - rider.Position;
            return Indoors(next, map);
        }
    }

    /// <summary>
    /// The rider's job: walk to the dog (target A), mount, ride to the destination (target B), dismount. Dismounts
    /// early when the next step goes indoors or the dog stops carrying (powered down), then walks the rest.
    /// </summary>
    public class JobDriver_RideTo : JobDriver
    {
        private bool mounted;
        private bool dismounted;

        public Pawn Dog => (Pawn)job.targetA.Thing;

        public bool Mounted => mounted;

        /// <summary>Was mounted and got off: the dog is free again.</summary>
        public bool Dismounted => dismounted;

        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            pawn.Reserve(job.targetA, job, errorOnFailed: errorOnFailed);

        // The dog waits for its rider. Here, not in a toil: the fail conditions are checked before the first toil starts.
        public override void Notify_Starting()
        {
            base.Notify_Starting();
            Dog.jobs.StartJob(JobMaker.MakeJob(QR_DefOf.QR_BeingRidden, pawn), JobCondition.InterruptForced);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            AddFailCondition(() => pawn.Drafted);
            // Before mounting: the dog must still be up and waiting for this rider.
            AddFailCondition(() => !mounted && !dismounted && (Dog.DestroyedOrNull() || !Dog.Spawned || Dog.Downed
                || Dog.CurJobDef != QR_DefOf.QR_BeingRidden || Dog.CurJob.targetA.Thing != pawn));
            AddFinishAction(c =>
            {
                if (!mounted && !dismounted)
                    QR.Log($"{pawn}'s ride on {Dog} ended before mounting ({c})");
                Dismount($"ride ended ({c})");
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            Toil mount = ToilMaker.MakeToil("Mount");
            mount.initAction = () =>
            {
                mounted = true;
                QR.Log($"{pawn} mounted {Dog} ({CompRobodog.Of(Dog).Battery:P0})");
            };
            mount.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return mount;
            Toil ride = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            ride.AddPreTickAction(() =>
            {
                if (!mounted)
                    return;
                if (Dog.Downed || !Dog.Spawned || Dog.CurJobDef != QR_DefOf.QR_BeingRidden)
                    Dismount(CompRobodog.Of(Dog)?.PoweredDown == true ? "the dog powered down" : "the dog stopped");
                else if (Riding.EnteringIndoors(pawn))
                    Dismount("going indoors");
            });
            yield return ride;
        }

        private void Dismount(string why)
        {
            if (!mounted)
                return;
            mounted = false;
            dismounted = true;
            Riding.lastDismount = (pawn.ThingID, pawn.Position, why);
            QR.Log($"{pawn} dismounted {Dog} at {pawn.Position}: {why}");
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref mounted, "mounted");
            Scribe_Values.Look(ref dismounted, "dismounted");
        }
    }

    /// <summary>The dog's job while called for and ridden: wait for the rider (target A), then stay in its cell, facing its way.</summary>
    public class JobDriver_BeingRidden : JobDriver
    {
        public Pawn Rider => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Toil carry = ToilMaker.MakeToil("BeingRidden");
            carry.initAction = () => pawn.pather.StopDead();
            carry.handlingFacing = true;
            carry.tickAction = () =>
            {
                Pawn rider = Rider;
                if (rider == null || !rider.Spawned || rider.Map != pawn.Map
                    || !(rider.jobs.curDriver is JobDriver_RideTo ride) || ride.Dog != pawn || ride.Dismounted)
                {
                    QR.Log($"{pawn} no longer carries {rider}: rider's job {rider?.CurJob.ToStringSafe()}");
                    EndJobWith(JobCondition.Succeeded);
                    return;
                }
                if (!ride.Mounted)
                    return;
                pawn.Position = rider.Position;
                pawn.Rotation = rider.Rotation;
            };
            carry.defaultCompleteMode = ToilCompleteMode.Never;
            yield return carry;
        }
    }

    /// <summary>The rider moves at the dog's ridden speed.</summary>
    public class StatPart_Riding : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            if (req.Thing is Pawn rider && Riding.MountOf(rider) is Pawn dog)
                val = Rideable.Of(dog).riddenMoveSpeed;
        }

        public override string ExplanationPart(StatRequest req) =>
            req.Thing is Pawn rider && Riding.MountOf(rider) is Pawn dog
                ? $"Riding {dog.LabelShort}: {Rideable.Of(dog).riddenMoveSpeed:0.##}"
                : null;
    }

    /// <summary>"Ride X to…" on a Quadrunner that is out, or on its stable for the fullest docked one.</summary>
    public class FloatMenuOptionProvider_Ride : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;

        protected override bool Undrafted => true;

        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
        {
            Pawn rider = context.FirstSelectedPawn;
            if (!Riding.CanBeRidden(clickedPawn) || !Riding.CanRide(rider))
                return null;
            if (!rider.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
                return new FloatMenuOption($"Can't ride {clickedPawn.LabelShort}: unreachable or taken", null);
            return new FloatMenuOption(RideLabel(clickedPawn, null), () => Riding.ChooseDestination(rider, () => clickedPawn, null));
        }

        protected override FloatMenuOption GetSingleOptionFor(Thing clickedThing, FloatMenuContext context)
        {
            Pawn rider = context.FirstSelectedPawn;
            if (!(clickedThing is Building_Stable stable) || stable.Faction != Faction.OfPlayer || !Riding.CanRide(rider))
                return null;
            Pawn dog = stable.IdleRideable;
            if (dog == null)
                return null;
            if (!rider.CanReach(stable, PathEndMode.InteractionCell, Danger.Deadly))
                return new FloatMenuOption($"Can't ride {dog.LabelShort}: {stable.LabelShort} unreachable", null);
            return new FloatMenuOption(RideLabel(dog, stable),
                () => Riding.ChooseDestination(rider, () => stable.IdleRideable, stable));
        }

        public static string RideLabel(Pawn dog, Building_Stable stable) =>
            stable == null ? $"Ride {dog.LabelShort} to…" : $"Ride {dog.LabelShort} from {stable.LabelShort} to…";
    }

    /// <summary>The rider is drawn on the dog's back, above it; the dog under the rider, wherever the rider's tween is.</summary>
    [HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.DrawPos), MethodType.Getter)]
    public static class Patch_Pawn_DrawTracker_DrawPos
    {
        // Half an altitude step above the pawn layer's top, so above any pawn's seeded offset.
        private static readonly float RiderAltitude = AltitudeLayer.PawnUnused.AltitudeFor() + Altitudes.AltInc / 2f;

        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (Riding.MountOf(___pawn) is Pawn dog)
            {
                __result += Riding.Offset(dog, ___pawn.Rotation);
                __result.y = RiderAltitude;
            }
            else if (Riding.RiderOf(___pawn) is Pawn rider)
            {
                Vector3 under = rider.DrawPos - Riding.Offset(___pawn, rider.Rotation);
                __result = new Vector3(under.x, __result.y, under.z);
            }
        }
    }
}
