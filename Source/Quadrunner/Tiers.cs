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
        public float packRange;
        public ThingDef smokeGun;
        public ThingDef tearGasGun;
        public int smokeInterval = 900;
        public int tearGasInterval = 900;

        public bool FixedLoadout => smokeGun != null;

        public static CombatTier Of(Thing thing) => thing?.def.GetModExtension<CombatTier>();

        public List<ThingDef> Slot(int slot) => slot == 0 ? slot1 : slot2;

        public int SlotCount => slot2.Count > 0 ? 2 : slot1.Count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Built-in damaging mounts switch by range: the secondary within its range, otherwise the primary.
    /// T2/T3 retain selectable guns; T4 has fixed paired mounts and a separately equipped smoke launcher.
    /// All built-in weapons are destroyed on drop, never loot.
    /// </summary>
    public static class Tiers
    {
        /// <summary>
        /// The gun picked for <paramref name="slot"/>, or the slot's default; null for an empty slot. A dog without
        /// a Combatdog tier holds its race's <see cref="RobodogWork.builtInGun"/> in slot 1 (the Medirunner's sprayer).
        /// </summary>
        public static ThingDef Gun(Pawn dog, int slot)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier == null)
                return slot == 0 ? RobodogWork.Of(dog)?.builtInGun : null;
            List<ThingDef> choices = tier.Slot(slot);
            if (choices.NullOrEmpty())
                return null;
            ThingDef picked = CompRobodog.Of(dog)?.PickedGun(slot);
            return !tier.FixedLoadout && picked != null && choices.Contains(picked) ? picked : choices[0];
        }

        /// <summary>From the Overview tab: <paramref name="gun"/> fills <paramref name="slot"/> from now on.</summary>
        public static void PickGun(Pawn dog, int slot, ThingDef gun)
        {
            if (CombatTier.Of(dog)?.FixedLoadout == true)
                return;
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
            EnsureSupplementals(dog);
            ThingWithComps held = dog.equipment.Primary;
            if (held?.def == gun)
                return;
            if (held != null)
                dog.equipment.DestroyEquipment(held);
            if (gun != null)
                dog.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(gun));
        }

        /// <summary>Supplemental mounts are real, saved equipment, but never the primary attack verb.</summary>
        internal static void EnsureSupplementals(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            EnsureSupplementary(dog, tier?.smokeGun);
            EnsureSupplementary(dog, tier?.tearGasGun);
        }

        private static void EnsureSupplementary(Pawn dog, ThingDef gun)
        {
            if (gun == null || dog.equipment == null)
                return;
            foreach (ThingWithComps weapon in dog.equipment.AllEquipmentListForReading)
                if (weapon.def == gun)
                    return;
            dog.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(gun));
        }

        private static void RemoveBuiltIn(Pawn dog, ThingDef gun)
        {
            if (gun == null || dog.equipment == null)
                return;
            foreach (ThingWithComps weapon in dog.equipment.AllEquipmentListForReading.ToList())
                if (weapon.def == gun)
                    dog.equipment.DestroyEquipment(weapon);
        }

        /// <summary>In-place load migration, including docked, carried and world pawns; no pawn is replaced.</summary>
        public static void MigrateLoadout(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier == null || dog.Dead)
                return;
            if (tier.FixedLoadout)
            {
                CompRobodog.Of(dog).PickGun(0, null);
                CompRobodog.Of(dog).PickGun(1, null);
                ThingDef held = dog.equipment?.Primary?.def;
                // Modern saves retain their exact equipment and in-flight verb/stance state.
                if (held != Gun(dog, 0) && held != Gun(dog, 1))
                    Arm(dog, Gun(dog, 0));
            }
            else if (tier.tearGasGun != null && dog.equipment?.Primary?.def == QR_DefOf.QR_Gun_Flamethrower)
            {
                // Plan 0012: the flamethrower is gone; a T3 holding one is armed with its primary instead.
                Arm(dog, Gun(dog, 0));
            }
            // Pre-0012 saves may still hold a flamethrower instance; destroy it. Stale slot picks naming it are inert,
            // since no slot lists it anymore. Every tier with an automatic side mount carries it from now on.
            RemoveBuiltIn(dog, QR_DefOf.QR_Gun_Flamethrower);
            EnsureSupplementals(dog);
        }

        /// <summary>
        /// The screen cell between <paramref name="dog"/> and <paramref name="target"/>: their midpoint, where both
        /// launchers shell (user, 2026-11-13). Any cell works for the cast; the explosion spreads the cloud around it.
        /// </summary>
        public static IntVec3 ScreenCell(Pawn dog, Thing target)
            => new IntVec3((dog.Position.x + target.Position.x) / 2, 0, (dog.Position.z + target.Position.z) / 2);

        /// <summary>Independent supplemental fire never takes the damaging gun's warmup, cooldown or job. Smoke: at most
        /// 3 pops per 900-tick window per cluster of 3+ T4s within the pack radius, others wait; tear gas has no cap.</summary>
        public static void TickArsenal(Pawn dog, ref int nextSmokeAt, ref int nextTearGasAt)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier == null || (tier.smokeGun == null && tier.tearGasGun == null) || !dog.Spawned || dog.Dead || dog.Downed
                || CompRobodog.Of(dog).WantsCharge
                || dog.CurJobDef != JobDefOf.AttackStatic
                || !(dog.CurJob.targetA.Thing is Pawn target) || target.Dead || target.Downed
                || !target.HostileTo(dog))
                return;
            // Range switching also applies to a player's drafted ranged order, not only network-created jobs.
            Arm(dog, GunFor(dog, target));
            IntVec3 screen = ScreenCell(dog, target);
            FireSupplemental(dog, tier.smokeGun, tier.smokeInterval, ref nextSmokeAt, screen, isSmoke: true);
            FireSupplemental(dog, tier.tearGasGun, tier.tearGasInterval, ref nextTearGasAt, screen, isSmoke: false);
        }

        private static void FireSupplemental(Pawn dog, ThingDef gun, int interval, ref int nextAt, IntVec3 screen, bool isSmoke)
        {
            if (gun == null)
                return;
            int now = Find.TickManager.TicksGame;
            if (now < nextAt)
                return;
            if (isSmoke && !SmokeCoordinator.CanPop(dog))
                return; // cluster cap: wait for this or a later window; no stamp, so the cluster retries next tick
            foreach (ThingWithComps weapon in dog.equipment.AllEquipmentListForReading)
            {
                if (weapon.def != gun)
                    continue;
                Verb verb = weapon.TryGetComp<CompEquippable>().AllVerbs[0];
                if (verb.Available() && verb.TryStartCastOn(screen, nonInterruptingSelfCast: true))
                {
                    if (isSmoke)
                        SmokeCoordinator.NoteFired(dog);
                    nextAt = now + interval;
                }
                return;
            }
        }

        /// <summary>For the Overview tab and summaries: what fills each slot, or "claws".</summary>
        public static string Describe(Pawn dog)
        {
            CombatTier tier = CombatTier.Of(dog);
            if (tier == null || tier.SlotCount == 0)
                return "claws";
            string guns = string.Join(" + ", Enumerable.Range(0, tier.SlotCount).Select(i => Gun(dog, i).label.Replace("built-in ", "")));
            if (tier.smokeGun != null)
                guns += " + smoke launcher";
            if (tier.tearGasGun != null)
                guns += " + tear gas launcher";
            return guns;
        }
    }

    /// <summary>Each barrel gets a full vanilla accuracy/cover roll and launches a real projectile on the same tick.</summary>
    public class Verb_PairedShoot : Verb_Shoot
    {
        protected override bool TryCastShot()
        {
            if (currentTarget.Thing is Pawn target && (target.Dead || target.Downed))
                return false;
            bool left = base.TryCastShot();
            bool right = left && base.TryCastShot();
            return left || right;
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
