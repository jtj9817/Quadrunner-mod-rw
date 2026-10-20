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
            if (dog.carryTracker?.CarriedThing != null || gun?.TryGetComp<CompBuiltInGun>() == null)
            {
                Vector3 ordinary = parms.matrix.Position();
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
            Vector3 position = parms.matrix.Position() + OffsetFor(node, parms, out _);
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
            if (!parms.facing.IsHorizontal || !TryGetCycle(props, parms, out float t))
                return rotation;
            float angle = props.swing * Mathf.Sin(t);
            return rotation * Quaternion.AngleAxis(parms.facing == Rot4.West ? -angle : angle, Vector3.up);
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
}
