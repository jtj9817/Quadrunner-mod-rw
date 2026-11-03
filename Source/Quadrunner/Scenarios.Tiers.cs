using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>T8: Combatdog tiers, upgrade bills and built-in guns.</summary>
    public static partial class Scenarios
    {
        private static ThingDef Held(Pawn dog) => dog?.equipment?.Primary?.def;

        /// <summary>Built-in guns lying anywhere on the map (there should never be any).</summary>
        private static int LooseGuns() => Find.CurrentMap.listerThings.AllThings
            .Count(t => t.def.IsWeapon && t.def.destroyOnDrop && t.def.defName.StartsWith("QR_Gun"));

        /// <summary>A warmup or cooldown that uses a gun the dog no longer holds (its verb would be saved unresolvable).</summary>
        private static bool StaleStance(Pawn dog) =>
            dog.stances.curStance is Stance_Busy busy && busy.verb?.EquipmentSource is Thing gun && gun != dog.equipment.Primary;

        private static bool Shooting(Pawn dog, Thing target, ThingDef gun) =>
            dog.Spawned && dog.CurJobDef == JobDefOf.AttackStatic && dog.CurJob.targetA.Thing == target && Held(dog) == gun;

        /// <summary>
        /// The plan's Tier upgrade: a Tier 1 Combatdog's upgrade ordered from the Overview tab, a colonist does the bill,
        /// the dog comes back as Tier 2 with its name and the built-in assault rifle; the LMG picked for its slot stays
        /// across a reload; pinged, it shoots the raider with it; powered down, it drops nothing.
        /// </summary>
        private static void TierUpgrade()
        {
            var s = new Scenario("Tier upgrade", 20000);
            string stableId = null, oldId = null, raiderId = null, name = null;
            bool shot = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => Stable()?.Dogs().FirstOrDefault();
            s.Do("a powered stable with a docked Tier 1 Combatdog and the Tier 2 upgrade's materials", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    oldId = dog.ThingID;
                    name = dog.LabelShort;
                    SpawnAssembleMaterials(s, stable, CombatTier.Of(dog).upgrade);
                })
                .Expect("the Overview tab shows it as Tier 1 with claws", () =>
                {
                    string[] cells = ITab_StableOverview.Rows(Stable()).Single().cells;
                    return cells[5] == "1" && cells[6] == "claws" && Held(Dog()) == null;
                })
                .Do("order the upgrade (the tab's button)", () =>
                {
                    if (Stable().OrderUpgrade(Dog()) == null || Stable().UpgradeBill(Dog()) == null)
                        throw new InvalidOperationException("no upgrade bill");
                    if (Stable().OrderUpgrade(Dog()) != null)
                        throw new InvalidOperationException("a second bill for the same dog");
                })
                .Expect("a colonist takes the bill", () =>
                {
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    Verse.AI.Job job = BillJob(crafter, Stable());
                    if (job == null)
                        return false;
                    crafter.drafter.Drafted = false;
                    return crafter.jobs.TryTakeOrderedJob(job);
                })
                .Expect("the dog is docked as Tier 2, with its name and the built-in assault rifle", () =>
                    Dog() is Pawn dog && dog.def == QR_DefOf.QR_CombatdogTwo.race && Stable().Docked.Contains(dog)
                    && dog.LabelShort == name && Held(dog) == QR_DefOf.QR_Gun_AssaultRifle)
                .Expect("the Tier 1 dog is gone, the bill removed; still one dog at the stable", () =>
                    Scenario.ById(oldId) == null && !Stable().BillStack.Bills.OfType<Bill_UpgradeDog>().Any()
                    && Stable().Dogs().Count == 1)
                .Do("note it; pick the LMG for its slot (the tab's Weapons button)", () =>
                {
                    s.Track(Dog());
                    Tiers.PickGun(Dog(), 0, QR_DefOf.QR_Gun_LMG);
                })
                .Expect("it holds the LMG; the row says Tier 2, LMG", () =>
                {
                    string[] cells = ITab_StableOverview.Rows(Stable()).Single().cells;
                    return Held(Dog()) == QR_DefOf.QR_Gun_LMG && cells[5] == "2" && cells[6] == "LMG";
                })
                .SaveAndReload()
                .Expect("after the load: Tier 2, same name, the LMG held and still picked", () =>
                    Dog() is Pawn dog && dog.def == QR_DefOf.QR_CombatdogTwo.race && dog.LabelShort == name
                    && Held(dog) == QR_DefOf.QR_Gun_LMG && Tiers.Gun(dog, 0) == QR_DefOf.QR_Gun_LMG)
                .Do("draft the colonists; a raider 32–40 cells out pings the stable", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Pawn raider = SpawnRaider(s, OpenCell(map, Stable().InteractionCell, 32f, 40f));
                    raiderId = raider.ThingID;
                    Raid.Recheck(map);
                    Net().Ping(raider, Dog());
                })
                .Expect("it shoots the raider with the LMG", () =>
                {
                    shot |= Shooting(Dog(), Scenario.ById(raiderId), QR_DefOf.QR_Gun_LMG);
                    return shot;
                })
                .Expect("the raider is down or dead", () => !(Scenario.ById(raiderId) is Pawn r) || r.Dead || r.Downed)
                .Do("power the dog down where it is", () =>
                {
                    if (!Dog().Spawned)
                        Stable().TryUndock(Dog());
                    CompRobodog.Of(Dog()).SetBattery(0f);
                })
                .Expect("downed, it dropped no gun; none lies anywhere; no stance on a gone gun", () =>
                    Dog().Downed && LooseGuns() == 0 && !StaleStance(Dog()))
                .Summary(() => $"dog {Dog()?.def.defName} {Dog()?.LabelShort} ({name}) holding {Held(Dog())?.defName}, "
                    + $"{Dog()?.CurJob.ToStringSafe()}; raider {Fate(raiderId)}; loose guns {LooseGuns()}")
                .Start();
        }

    }
}
