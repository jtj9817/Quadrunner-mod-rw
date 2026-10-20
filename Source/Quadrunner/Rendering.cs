using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>Keep Core's carried-item rendering, but pivot built-in guns on the dog's dorsal hardpoint, not a hand.</summary>
    public class PawnRenderNodeWorker_RobodogMount : PawnRenderNodeWorker_Carried
    {
        public override void PostDraw(PawnRenderNode node, PawnDrawParms parms, Mesh mesh, Matrix4x4 matrix)
        {
            Pawn dog = parms.pawn;
            ThingWithComps gun = dog.equipment?.Primary;
            RobodogPose pose = RobodogPose.Of(dog, parms);
            if (dog.carryTracker?.CarriedThing is Pawn patient && RobodogPose.Props(dog) is PawnRenderNodeProperties_Pose props)
            {
                // On the back, over the dog in every facing; PawnRenderer.BodyAngle (patched) turns it to match.
                Vector3 back = parms.matrix.Position() + pose.offset + props.PatientOffset(dog, patient, parms.facing, out _);
                back.y += PawnRenderUtility.AltitudeForLayer(PawnRenderUtility.Layer_Carried);
                patient.DrawNowAt(back);
                return;
            }
            if (dog.carryTracker?.CarriedThing != null || gun?.TryGetComp<CompBuiltInGun>() == null)
            {
                Vector3 ordinary = parms.matrix.Position() + pose.offset;
                ordinary.y += PawnRenderUtility.AltitudeForLayer(parms.facing == Rot4.North
                    ? PawnRenderUtility.Layer_Carried_Behind : PawnRenderUtility.Layer_Carried);
                if (dog.carryTracker?.CarriedThing is Thing carried)
                    PawnRenderUtility.DrawCarriedThing(dog, ordinary, carried);
                else
                    PawnRenderUtility.DrawEquipmentAndApparelExtras(dog, ordinary, parms.facing, parms.flags);
                return;
            }
            float angle = parms.facing.AsAngle;
            if (!parms.flags.FlagSet(PawnRenderFlags.NeverAimWeapon)
                && dog.stances?.curStance is Stance_Busy busy && !busy.neverAimWeapon && busy.focusTarg.IsValid)
            {
                Vector3 target = busy.focusTarg.HasThing ? busy.focusTarg.Thing.DrawPos : busy.focusTarg.Cell.ToVector3Shifted();
                Vector3 direction = target - dog.DrawPos;
                if (direction.MagnitudeHorizontalSquared() > 0.001f) angle = direction.AngleFlat();
                Verb verb = dog.CurrentEffectiveVerb;
                if (verb?.AimAngleOverride is float aim) angle = aim;
            }
            Vector3 position = parms.matrix.Position() + pose.offset + OffsetFor(node, parms, out _);
            // Core still handles the gun's material, facing, recoil and mesh. The XML tree owns mount offsets/layers.
            PawnRenderUtility.DrawEquipmentAiming(gun, position, angle);
        }
    }

    /// <summary>
    /// One layer of a robodog sprite (<c>Art/</c>: <c>id="part-…"</c>): the kind's body texture path plus
    /// <c>_&lt;part&gt;</c>, at the kind's size and colors, so one tree serves all six kinds.
    /// </summary>
    public class PawnRenderNode_RobodogPart : PawnRenderNode_AnimalPart
    {
        public PawnRenderNode_RobodogPart(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree) : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            Graphic whole = base.GraphicFor(pawn);
            return whole == null ? null : GraphicDatabase.Get<Graphic_Multi>(
                $"{whole.path}_{((PawnRenderNodeProperties_Gait)props).part}", whole.Shader, whole.drawSize, whole.Color, whole.ColorTwo);
        }
    }

    public class PawnRenderNodeProperties_Gait : PawnRenderNodeProperties
    {
        /// <summary>Texture suffix, e.g. <c>FrontNear</c>.</summary>
        public string part;
        /// <summary>Cells walked per full gait cycle; 0 for a layer that never moves (the body).</summary>
        public float stride;
        /// <summary>This leg's offset in the cycle (0–1): diagonal pairs move together.</summary>
        public float phase;
        /// <summary>Side view: degrees swung either way about <see cref="hip"/>.</summary>
        public float swing;
        /// <summary>Front and back view: cells lifted at the top of a step.</summary>
        public float lift;
        /// <summary>Side view: the hip in the east sprite's pixels (128 square, y down).</summary>
        public Vector2 hip;
        /// <summary>Side view: degrees swung at a full stretch (<see cref="RobodogPose.stretch"/>): front legs reach
        /// forward (negative), hind legs push back (positive).</summary>
        public float stretch;
        /// <summary>Cells this layer drops at a full crouch (<see cref="RobodogPose.crouch"/>): the body, over its legs.</summary>
        public float crouch;
    }

    /// <summary>
    /// A walking dog's legs: in profile each swings about its hip, head-on each lifts in turn. The cycle follows the
    /// distance drawn along the dog's facing, so it keeps pace with any speed, runs smoothly between ticks and stops
    /// when the dog does. A ridden dog walks with its rider.
    /// </summary>
    public class PawnRenderNodeWorker_Gait : PawnRenderNodeWorker
    {
        private const float SpritePixels = 128f;

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 offset = base.OffsetFor(node, parms, out pivot);
            var props = (PawnRenderNodeProperties_Gait)node.Props;
            if (!parms.facing.IsHorizontal && TryGetCycle(props, parms, out float t))
                offset.z += props.lift * Mathf.Max(0f, Mathf.Sin(t));
            if (props.crouch != 0f)
                offset.z -= props.crouch * RobodogPose.Of(parms.pawn, parms).crouch;
            return offset;
        }

        protected override Vector3 PivotFor(PawnRenderNode node, PawnDrawParms parms)
        {
            var props = (PawnRenderNodeProperties_Gait)node.Props;
            if (!parms.facing.IsHorizontal || props.stride <= 0f || node.PrimaryGraphic == null)
                return base.PivotFor(node, parms);
            Vector2 size = node.PrimaryGraphic.drawSize;
            float x = (props.hip.x / SpritePixels - 0.5f) * size.x;
            return new Vector3(parms.facing == Rot4.West ? -x : x, 0f, (0.5f - props.hip.y / SpritePixels) * size.y);
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion rotation = base.RotationFor(node, parms);
            var props = (PawnRenderNodeProperties_Gait)node.Props;
            if (!parms.facing.IsHorizontal)
                return rotation;
            float angle = props.stretch * RobodogPose.Of(parms.pawn, parms).stretch;
            if (TryGetCycle(props, parms, out float t))
                angle += props.swing * Mathf.Sin(t);
            return angle == 0f ? rotation
                : rotation * Quaternion.AngleAxis(parms.facing == Rot4.West ? -angle : angle, Vector3.up);
        }

        /// <summary>The leg's cycle angle in radians, when the dog is walking upright on the map.</summary>
        private static bool TryGetCycle(PawnRenderNodeProperties_Gait props, PawnDrawParms parms, out float t)
        {
            t = 0f;
            if (props.stride <= 0f || parms.Portrait || parms.dead || parms.posture != PawnPosture.Standing)
                return false;
            Pawn walker = Riding.RiderOf(parms.pawn) ?? parms.pawn;
            if (walker.pather == null || !walker.pather.Moving)
                return false;
            Vector3 at = parms.matrix.Position();
            IntVec3 ahead = parms.facing.FacingCell;
            t = ((at.x * ahead.x + at.z * ahead.z) / props.stride + props.phase) * 2f * Mathf.PI;
            return true;
        }
    }

    /// <summary>The root of a robodog's render tree: when and how far it moves in action (<see cref="RobodogPose"/>).</summary>
    public class PawnRenderNodeProperties_Pose : PawnRenderNodeProperties_Parent
    {
        /// <summary>A melee strike: ticks, cells lunged toward the target, cells hopped, degrees nose-down at the bite.</summary>
        public int pounceTicks = 24;
        public float pounceReach = 0.3f;
        public float pounceHop = 0.1f;
        public float pounceTilt = 12f;
        /// <summary>Each shot: ticks, cells kicked back from the target, degrees nose-up.</summary>
        public int recoilTicks = 10;
        public float recoilKick = 0.08f;
        public float recoilTilt = 5f;
        /// <summary>Aiming and shooting: the legs' share of a full stretch (a wide stance) and ticks to settle into it.</summary>
        public float braceStretch = 0.35f;
        public int braceTicks = 8;
        /// <summary>Picking up a pawn: ticks, the head dip in degrees, how far in front it lies (cells), the flip's height.</summary>
        public int scoopTicks = 45;
        public float scoopTilt = 14f;
        public float scoopReach = 0.6f;
        public float scoopHeight = 0.55f;
        /// <summary>Where a carried pawn lies on the back, per facing (west mirrors east).</summary>
        public Vector3 patientNorth, patientEast, patientSouth;

        /// <summary>
        /// A carried pawn's offset from the dog and its body angle: lying along the back; flipped up from the ground in
        /// front over the head onto it, in <see cref="scoopTicks"/> from the pickup.
        /// </summary>
        public Vector3 PatientOffset(Pawn dog, Pawn patient, Rot4 facing, out float angle)
        {
            Vector3 rest = facing == Rot4.North ? patientNorth : facing == Rot4.South ? patientSouth
                : facing == Rot4.East ? patientEast : new Vector3(-patientEast.x, patientEast.y, patientEast.z);
            // Head toward the dog's front in profile; across the back head-on. Turning back by 180° over the flip.
            bool clockwise = facing == Rot4.East || facing == Rot4.South;
            angle = clockwise ? 90f : 270f;
            float u = RobodogPose.Since(CompRobodog.Of(dog)?.CarriedSince(patient) ?? int.MinValue / 2) / scoopTicks;
            if (u >= 1f)
                return rest;
            // The first quarter is the head dip under the patient; then it goes up and over.
            float s = RobodogPose.Smooth(Mathf.Clamp01((u - 0.25f) / 0.75f));
            Vector3 front = facing.FacingCell.ToVector3() * scoopReach;
            angle += (clockwise ? 180f : -180f) * (1f - s);
            return Vector3.Lerp(front, rest, s) + new Vector3(0f, 0f, scoopHeight * Mathf.Sin(Mathf.PI * s));
        }
    }

    /// <summary>
    /// A robodog's whole-body action, recomputed every frame from vanilla state (the stance and its start tick) and the
    /// pickup tick: a pounce on each melee strike, a recoil kick on each shot over a braced, crouched stance while it
    /// aims and fires, and a head dip to scoop up a pawn. Tunables on the tree's root, <see cref="PawnRenderNodeProperties_Pose"/>.
    /// </summary>
    public struct RobodogPose
    {
        /// <summary>The whole dog's offset, in cells.</summary>
        public Vector3 offset;
        /// <summary>Side view: degrees the body turns nose-down (negative: nose-up), as seen facing east.</summary>
        public float tilt;
        /// <summary>0–1: the legs' stretch (<see cref="PawnRenderNodeProperties_Gait.stretch"/>).</summary>
        public float stretch;
        /// <summary>0–1: the body's crouch (<see cref="PawnRenderNodeProperties_Gait.crouch"/>).</summary>
        public float crouch;

        public static PawnRenderNodeProperties_Pose Props(Pawn dog) =>
            dog.Drawer?.renderer?.renderTree?.rootNode?.Props as PawnRenderNodeProperties_Pose;

        /// <summary>Ticks since <paramref name="tick"/>, at least 0.</summary>
        public static float Since(int tick) => Mathf.Max(0f, Find.TickManager.TicksGame - tick);

        public static float Smooth(float x) => x * x * (3f - 2f * x);

        /// <summary>0 → 1 at <paramref name="peak"/> → 0 at 1, eased.</summary>
        private static float Pulse(float u, float peak) =>
            u < peak ? Smooth(u / peak) : 1f - Smooth(Mathf.Clamp01((u - peak) / (1f - peak)));

        public static RobodogPose Of(Pawn dog, PawnDrawParms parms)
        {
            var pose = new RobodogPose();
            PawnRenderNodeProperties_Pose p = Props(dog);
            if (p == null || parms.Portrait || parms.dead || parms.posture != PawnPosture.Standing || !dog.Spawned)
                return pose;
            if (dog.stances?.curStance is Stance_Busy busy && busy.verb != null && busy.focusTarg.IsValid)
            {
                Vector3 target = busy.focusTarg.HasThing ? busy.focusTarg.Thing.DrawPos : busy.focusTarg.Cell.ToVector3Shifted();
                Vector3 toward = (target - dog.DrawPos).Yto0().normalized;
                float elapsed = Since(busy.startedTick);
                if (busy.verb is Verb_MeleeAttack)
                {
                    float u = elapsed / p.pounceTicks;
                    if (busy is Stance_Cooldown && u < 1f)
                    {
                        float k = Pulse(u, 0.25f);
                        pose.offset = toward * (p.pounceReach * k) + new Vector3(0f, 0f, p.pounceHop * Mathf.Sin(Mathf.PI * u));
                        pose.tilt = p.pounceTilt * k;
                        pose.stretch = k;
                    }
                }
                else
                {
                    pose.crouch = busy is Stance_Warmup ? Smooth(Mathf.Clamp01(elapsed / p.braceTicks)) : 1f;
                    pose.stretch = p.braceStretch * pose.crouch;
                    float u = elapsed / p.recoilTicks;
                    if (busy is Stance_Cooldown && u < 1f)
                    {
                        float k = (1f - u) * (1f - u);
                        pose.offset = toward * (-p.recoilKick * k);
                        pose.tilt = -p.recoilTilt * k;
                    }
                }
            }
            if (dog.carryTracker?.CarriedThing is Pawn patient)
            {
                float u = Since(CompRobodog.Of(dog)?.CarriedSince(patient) ?? int.MinValue / 2) / p.scoopTicks;
                if (u < 1f)
                    pose.tilt += p.scoopTilt * Pulse(u, 0.2f);
            }
            return pose;
        }
    }

    /// <summary>The root's worker: moves and, in profile, tilts the whole dog by its <see cref="RobodogPose"/>.</summary>
    public class PawnRenderNodeWorker_Pose : PawnRenderNodeWorker
    {
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot) =>
            base.OffsetFor(node, parms, out pivot) + RobodogPose.Of(parms.pawn, parms).offset;

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion rotation = base.RotationFor(node, parms);
            float tilt = parms.facing.IsHorizontal ? RobodogPose.Of(parms.pawn, parms).tilt : 0f;
            return tilt == 0f ? rotation
                : rotation * Quaternion.AngleAxis(parms.facing == Rot4.West ? -tilt : tilt, Vector3.up);
        }
    }

    /// <summary>
    /// A pawn carried by a robodog lies along its back and turns over during the flip onto it
    /// (<see cref="PawnRenderNodeProperties_Pose.PatientOffset"/>). Vanilla fixes a carried pawn's angle for a
    /// humanlike carrier and has no hook short of a Pawn subclass for the dog.
    /// </summary>
    [HarmonyPatch(typeof(PawnRenderer), nameof(PawnRenderer.BodyAngle))]
    public static class Patch_PawnRenderer_BodyAngle
    {
        public static void Postfix(Pawn ___pawn, ref float __result)
        {
            if (___pawn.ParentHolder is Pawn_CarryTracker carrier && RobodogPose.Props(carrier.pawn) is PawnRenderNodeProperties_Pose props)
                props.PatientOffset(carrier.pawn, ___pawn, carrier.pawn.Rotation, out __result);
        }
    }
}
