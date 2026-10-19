using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>On a dog recipe: the kind of dog it assembles.</summary>
    public class AssembleProps : DefModExtension
    {
        public PawnKindDef kind;
    }

    /// <summary>A dog recipe makes no item: each finished bill adds a dog to the stable.</summary>
    public class RecipeWorker_AssembleDog : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            if (billDoer.CurJob?.bill?.billStack?.billGiver is Building_Stable stable)
            {
                Pawn dog = stable.AddNewDog(recipe.GetModExtension<AssembleProps>().kind);
                QR.Log($"{billDoer} assembled {dog} at {stable}");
            }
        }
    }

    /// <summary>"Do until you have X" counts this stable's dogs of the recipe's kind.</summary>
    public class RecipeWorkerCounter_Dogs : RecipeWorkerCounter
    {
        public override bool CanCountProducts(Bill_Production bill) => true;

        public override int CountProducts(Bill_Production bill) =>
            bill.billStack?.billGiver is Building_Stable stable
                ? stable.Dogs().Count(d => d.kindDef == recipe.GetModExtension<AssembleProps>().kind)
                : 0;

        public override string ProductsDescription(Bill_Production bill) => "robodogs at this stable";

        public override bool CanPossiblyStore(Bill_Production bill, ISlotGroup slotGroup) => true;
    }

    /// <summary>Stable bills; a dog bill doesn't start while every slot of the stable is taken.</summary>
    public class WorkGiver_DoBillsStable : WorkGiver_DoBill
    {
        public override Job JobOnThing(Pawn pawn, Thing thing, bool forced = false)
        {
            Job job = base.JobOnThing(pawn, thing, forced);
            if (job?.bill?.recipe.HasModExtension<AssembleProps>() == true && thing is Building_Stable stable
                && !stable.HasFreeSlot)
            {
                JobFailReason.Is("stable full");
                return null;
            }
            return job;
        }
    }
}
