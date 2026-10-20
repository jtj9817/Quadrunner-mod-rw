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

    /// <summary>
    /// Rebuilds a dog from its wreck (the dog's corpse, an ingredient): the same dog, name and all, comes back docked
    /// in the stable, or beside it when every slot is taken.
    /// </summary>
    public class RecipeWorker_RebuildDog : RecipeWorker
    {
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map)
        {
            // The wreck stays until Notify_IterationCompleted, which brings its dog back.
            if (!(ingredient is Corpse corpse && CompRobodog.IsRobodog(corpse.InnerPawn)))
                base.ConsumeIngredient(ingredient, recipe, map);
        }

        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            Corpse wreck = ingredients.OfType<Corpse>().FirstOrDefault(c => CompRobodog.IsRobodog(c.InnerPawn));
            if (!(billDoer.CurJob?.bill?.billStack?.billGiver is Building_Stable stable) || wreck == null)
                return;
            Pawn dog = wreck.InnerPawn;
            if (!ResurrectionUtility.TryResurrect(dog, new ResurrectionParams { dontSpawn = true }))
            {
                QR.Error($"couldn't rebuild {dog} from its wreck");
                return;
            }
            CompRobodog comp = CompRobodog.Of(dog);
            comp.SetBattery(comp.Battery);
            stable.TakeNewDog(dog);
            QR.Log($"{billDoer} rebuilt {dog} at {stable}");
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
