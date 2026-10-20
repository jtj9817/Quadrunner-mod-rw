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
}
