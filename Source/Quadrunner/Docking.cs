using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>A dog with nothing else to do walks to its stable and docks.</summary>
    public class JobGiver_Dock : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            Building_Stable stable = CompRobodog.Of(pawn)?.stable;
            if (stable == null || !stable.Spawned || stable.Map != pawn.Map
                || !pawn.CanReach(stable, PathEndMode.InteractionCell, Danger.Deadly))
                return null;
            return JobMaker.MakeJob(QR_DefOf.QR_Dock, stable);
        }
    }

    public class JobDriver_Dock : JobDriver
    {
        private Building_Stable Stable => (Building_Stable)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
            Toil dock = ToilMaker.MakeToil("Dock");
            dock.initAction = () => Stable.TryDock(pawn);
            dock.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return dock;
        }
    }
}
