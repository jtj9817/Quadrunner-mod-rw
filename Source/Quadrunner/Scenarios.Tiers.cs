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
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    Pawn dog = stable.AddNewDog(QR_DefOf.QR_Combatdog);
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
                    return job != null && crafter.jobs.TryTakeOrderedJob(job);
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
                    Stable().Ping(raider, Dog());
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
                .Expect("downed, it dropped no gun; none lies anywhere", () => Dog().Downed && LooseGuns() == 0)
                .Summary(() => $"dog {Dog()?.def.defName} {Dog()?.LabelShort} ({name}) holding {Held(Dog())?.defName}, "
                    + $"{Dog()?.CurJob.ToStringSafe()}; raider {Fate(raiderId)}; loose guns {LooseGuns()}")
                .Start();
        }

        /// <summary>
        /// Tier 3 and 4 by the debug upgrade: Tier 3 shoots a far raider with its gun and switches to the flamethrower for
        /// one close by; reload between; Tier 4 holds the minigun and runs 0.25 × 2 cells/s slower deployed than Tier 3.
        /// </summary>
        private static void Tier3And4Weapons()
        {
            var s = new Scenario("Tier 3 and 4 weapons", 20000);
            string stableId = null, farId = null, nearId = null, name = null;
            bool rifled = false, flamed = false;
            float speed3 = 0f, speed4 = 0f;
            CellRect field = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => Stable()?.Dogs().FirstOrDefault();
            float Deployed(Pawn dog) => dog.GetStatValue(StatDefOf.MoveSpeed);
            s.Do("a powered stable with a Combatdog upgraded twice to Tier 3; a bare field 35–50 cells out", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn dog = stable.AddNewDog(QR_DefOf.QR_Combatdog);
                    name = dog.LabelShort;
                    dog = stable.Upgrade(stable.Upgrade(dog));
                    s.Track(dog);
                    field = ClearRect(map, stable.InteractionCell, 15, 1, 35f, 50f);
                    // The flamethrower sets things alight: no fire outlives the scenario.
                    s.OnFinish(() =>
                    {
                        foreach (Thing fire in Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())
                            fire.Destroy();
                    });
                })
                .Expect("Tier 3, same name, assault rifle + flamethrower, holding the rifle", () =>
                    Dog() is Pawn dog && dog.def == QR_DefOf.QR_CombatdogThree.race && dog.LabelShort == name
                    && Tiers.Describe(dog) == "assault rifle + flamethrower" && Held(dog) == QR_DefOf.QR_Gun_AssaultRifle)
                .Do("out in the field; a raider 14–22 cells from it, away from the colony, pings the stable", () =>
                {
                    Map map = Find.CurrentMap;
                    Stable().TryUndock(Dog());
                    Teleport(Dog(), field.CenterCell);
                    speed3 = Deployed(Dog());
                    Pawn far = SpawnRaider(s, OpenCell(map, field.CenterCell, 14f, 22f,
                        c => c.DistanceTo(Stable().InteractionCell) > 40f));
                    farId = far.ThingID;
                    Raid.Recheck(map);
                    Stable().Ping(far, Dog());
                })
                .Expect("it shoots the far raider with the rifle", () =>
                {
                    rifled |= Shooting(Dog(), Scenario.ById(farId), QR_DefOf.QR_Gun_AssaultRifle);
                    return rifled;
                })
                .SaveAndReload()
                .Do("a second raider 4–6 cells from the dog, in the field", () =>
                {
                    Map map = Find.CurrentMap;
                    IntVec3 at = field.ContractedBy(1).Cells.Where(c => c.DistanceTo(Dog().Position) >= 4f
                            && c.DistanceTo(Dog().Position) <= 6f && c.Standable(map) && c.GetFirstPawn(map) == null)
                        .RandomElement();
                    Pawn near = SpawnRaider(s, at);
                    nearId = near.ThingID;
                    Raid.Recheck(map);
                    Stable().Ping(near, Dog());
                })
                .Expect("it switches to the flamethrower for the near one", () =>
                {
                    flamed |= Shooting(Dog(), Scenario.ById(nearId), QR_DefOf.QR_Gun_Flamethrower);
                    return flamed;
                })
                .Expect("both raiders down or dead", () => new[] { farId, nearId }
                    .All(id => !(Scenario.ById(id) is Pawn r) || r.Dead || r.Downed))
                .Do("dock it; the debug upgrade to Tier 4", () =>
                {
                    Pawn dog = Dog();
                    if (!Stable().TryDock(dog) || Stable().Upgrade(dog) == null)
                        throw new InvalidOperationException("couldn't upgrade to Tier 4");
                    s.Track(Dog());
                })
                .Expect("Tier 4, same name, holding the minigun", () =>
                    Dog() is Pawn dog && dog.def == QR_DefOf.QR_CombatdogFour.race && dog.LabelShort == name
                    && Held(dog) == QR_DefOf.QR_Gun_Minigun)
                .Do("undock it: its deployed speed", () =>
                {
                    Stable().TryUndock(Dog());
                    speed4 = Deployed(Dog());
                })
                .Expect("the minigun costs 0.25 cells/s, doubled while deployed", () => Math.Abs(speed3 - speed4 - 0.5f) < 0.01f)
                .Do("power it down", () => CompRobodog.Of(Dog()).SetBattery(0f))
                .Expect("downed, it dropped no gun", () => Dog().Downed && LooseGuns() == 0)
                .Summary(() => $"speed T3 {speed3:F2}, T4 {speed4:F2}; far raider {Fate(farId)}, near raider {Fate(nearId)}; "
                    + $"dog {Dog()?.def.defName} holding {Held(Dog())?.defName}; loose guns {LooseGuns()}")
                .Start();
        }
    }
}
