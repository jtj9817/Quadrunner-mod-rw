using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// On a Combatdog race: its tier, the next tier's kind and upgrade bill, and the built-in guns each weapon slot can
    /// hold (the first is the default). Tier 1 has no slots: claws only.
    /// </summary>
    public class CombatTier : DefModExtension
    {
        public int tier = 1;
        public PawnKindDef next;
        public RecipeDef upgrade;
        public List<ThingDef> slot1 = new List<ThingDef>();
        public List<ThingDef> slot2 = new List<ThingDef>();

        public static CombatTier Of(Thing thing) => thing?.def.GetModExtension<CombatTier>();

        public List<ThingDef> Slot(int slot) => slot == 0 ? slot1 : slot2;

        public int SlotCount => slot2.Count > 0 ? 2 : slot1.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Combatdog weapons: built-in guns (never dropped: they are destroyed if anything drops them, and the dog is armed
    /// again before its next attack), one held at a time. Tier 3 holds its second slot's flamethrower while the target is
    /// within its range and its first slot's gun otherwise (user, 2026-10-20).
    /// </summary>
    public static class Tiers
    {
        /// <summary>The gun picked for <paramref name="slot"/>, or the slot's default; null for an empty slot.</summary>
        public static ThingDef Gun(Pawn dog, int slot)
        {
            List<ThingDef> choices = CombatTier.Of(dog)?.Slot(slot);
            if (choices.NullOrEmpty())
                return null;
            ThingDef picked = CompRobodog.Of(dog)?.PickedGun(slot);
            return picked != null && choices.Contains(picked) ? picked : choices[0];
        }

        /// <summary>From the Overview tab: <paramref name="gun"/> fills <paramref name="slot"/> from now on.</summary>
        public static void PickGun(Pawn dog, int slot, ThingDef gun)
        {
            CompRobodog.Of(dog).PickGun(slot, gun);
            QR.Log($"{dog}: slot {slot + 1} set to {gun.defName}");
            Arm(dog, Gun(dog, 0));
        }

        /// <summary>The gun to fight <paramref name="target"/> with: slot 2 within its range, else slot 1; null = claws.</summary>
        public static ThingDef GunFor(Pawn dog, Thing target)
        {
            ThingDef second = Gun(dog, 1);
            if (second != null && target != null
                && dog.Position.InHorDistOf(target.Position, second.Verbs[0].range))
                return second;
            return Gun(dog, 0);
        }

        /// <summary>Makes <paramref name="gun"/> the dog's held weapon (null: none), replacing whatever it held.</summary>
        public static void Arm(Pawn dog, ThingDef gun)
        {
            if (dog.equipment == null)
                return;
            ThingWithComps held = dog.equipment.Primary;
            if (held?.def == gun)
                return;
            if (held != null)
                dog.equipment.DestroyEquipment(held);
            if (gun != null)
                dog.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(gun));
        }

        /// <summary>For the Overview tab and summaries: what fills each slot, or "claws".</summary>
        public static string Describe(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier == null || tier.SlotCount == 0)
                return "claws";
            return string.Join(" + ", Enumerable.Range(0, tier.SlotCount).Select(i => Gun(dog, i).label.Replace("built-in ", "")));
        }
    }

    /// <summary>
    /// On the built-in guns: once one leaves its dog (switched, or destroyed on a drop), a warmup or cooldown that uses
    /// its verb ends too. Vanilla keeps a cooldown, which then references a verb nothing saves.
    /// </summary>
    public class CompBuiltInGun : ThingComp
    {
        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            if (pawn.stances?.curStance is Stance_Busy busy && busy.verb?.EquipmentSource == parent)
                pawn.stances.CancelBusyStanceHard();
        }
    }

    /// <summary>A Combatdog upgrade for one dog; done only while that dog is docked in this stable.</summary>
    public class Bill_UpgradeDog : Bill_Production
    {
        public Pawn dog;

        public Bill_UpgradeDog()
        {
        }

        public Bill_UpgradeDog(RecipeDef recipe, Pawn dog) : base(recipe)
        {
            this.dog = dog;
            repeatMode = BillRepeatModeDefOf.RepeatCount;
            repeatCount = 1;
        }

        public override string LabelCap => $"{recipe.LabelCap}: {dog?.LabelShortCap}";

        protected override bool CanCopy => false;

        /// <summary>The ordered dog is still this tier and docked here (agent default, 2026-10-20: upgrades wait for docking).</summary>
        public bool Pending => repeatCount > 0 && dog != null && !dog.Dead && !dog.Destroyed
            && CombatTier.Of(dog)?.upgrade == recipe && billStack?.billGiver is Building_Stable stable
            && CompRobodog.Of(dog)?.stable == stable;

        public override bool ShouldDoNow() =>
            base.ShouldDoNow() && Pending && ((Building_Stable)billStack.billGiver).Docked.Contains(dog);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref dog, "dog");
        }
    }

    /// <summary>A finished upgrade bill rebuilds its dog as the next tier in the stable.</summary>
    public class RecipeWorker_UpgradeDog : RecipeWorker
    {
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            base.Notify_IterationCompleted(billDoer, ingredients);
            if (!(billDoer.CurJob?.bill is Bill_UpgradeDog bill) || !(bill.billStack?.billGiver is Building_Stable stable))
                return;
            Pawn upgraded = stable.Upgrade(bill.dog);
            if (upgraded == null)
            {
                QR.Error($"{billDoer} finished {bill.LabelCap}, but {bill.dog} couldn't be upgraded");
                return;
            }
            QR.Log($"{billDoer} upgraded {upgraded} to {upgraded.def.defName} at {stable}");
            // The old dog is gone; the bill (done, removed by the stable) mustn't keep a reference to it.
            bill.dog = upgraded;
        }
    }
}
