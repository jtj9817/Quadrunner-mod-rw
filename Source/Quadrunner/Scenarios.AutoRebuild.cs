using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>W3: a powered stable automatically rebuilds recovered Combatdog wrecks from delivered materials.</summary>
    public static partial class Scenarios
    {
        /// <summary>A stable's physical material delivery area: the interaction cell and its eight neighbours.</summary>
        private static IEnumerable<IntVec3> AutoRebuildDeliveryArea(IntVec3 center, Map map) =>
            new[] { center }.Concat(GenAdj.CellsAdjacent8Way(new TargetInfo(center, map)))
                .Where(c => c.InBounds(map));

        private static int AutoRebuildDelivered(IntVec3 center, Map map, ThingDef def) =>
            AutoRebuildDeliveryArea(center, map).SelectMany(c => c.GetThingList(map)).Where(t => t.def == def)
                .Sum(t => t.stackCount);

        private static int AutoRebuildDelivered(Building_Stable stable, ThingDef def) =>
            stable.RebuildDeliveryCells.SelectMany(c => c.GetThingList(stable.Map)).Where(t => t.def == def)
                .Sum(t => t.stackCount);

        /// <summary>The corpse of <paramref name="pawnId"/> that is lying on the current map.</summary>
        private static Corpse AutoRebuildCorpseOf(string pawnId) =>
            Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                .FirstOrDefault(c => c.InnerPawn?.ThingID == pawnId);

        /// <summary>
        /// A Tier 3 Combatdog wreck waits for its exact delivered steel and plasteel, then a powered stable rebuilds it
        /// with no colonist: the same pawn, name, tier and guns, healthy and charged. A reload mid-build must not pay
        /// again; missing or partial deliveries consume nothing; power loss pauses. Two native captures show the
        /// selected stable's waiting and working inspect status over its drawn delivery area.
        /// </summary>
        private static void StableAutoRebuild()
        {
            var s = new Scenario("Stable auto rebuild", 60000);
            string stableId = null, dogId = null, name = null;
            int pausedAt = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            s.Do("a powered stable, an assigned Tier 3 dog with picked guns, and three wrecks", () =>
                {
                    DraftColonists(s);
                    int width = Screen.width, height = Screen.height;
                    Screen.SetResolution(1280, 720, false);
                    s.OnFinish(() => Screen.SetResolution(width, height, false));
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Map map = stable.Map;
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogThree));
                    dogId = dog.ThingID;
                    name = dog.Name.ToStringShort;
                    CompRobodog comp = CompRobodog.Of(dog);
                    comp.PickGun(0, QR_DefOf.QR_Gun_LMG);
                    comp.PickGun(1, QR_DefOf.QR_Gun_Flamethrower);
                    // A lost mechanical leg must be made whole by the reconstruction, not left as a permanent casualty.
                    BodyPartRecord leg = dog.health.hediffSet.GetNotMissingParts()
                        .FirstOrDefault(p => p.def.defName == "MechanicalLeg");
                    if (leg == null)
                        throw new InvalidOperationException("the Combatdog has no mechanical leg to test");
                    dog.health.AddHediff(HediffDefOf.MissingBodyPart, leg);
                    if (!stable.TryUndock(dog))
                        throw new InvalidOperationException("the Tier 3 dog didn't undock");
                    dog.Kill(null);
                    if (dog.Corpse == null)
                        throw new InvalidOperationException("the Combatdog left no wreck");

                    // Refusals leave the wrecks exactly where they are: another dog type and an enemy Combatdog.
                    Pawn worker = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    if (!stable.TryUndock(worker))
                        throw new InvalidOperationException("the Quadrunner didn't undock");
                    worker.Kill(null);
                    Corpse workerWreck = worker.Corpse;
                    if (workerWreck == null || stable.AcceptWreck(workerWreck) || !workerWreck.Spawned)
                        throw new InvalidOperationException("a Quadrunner wreck was accepted or moved");
                    s.Track(workerWreck).Destroy();

                    Pawn enemy = SpawnPawn(s, QR_DefOf.QR_Combatdog, Pirates(),
                        OpenCell(map, stable.InteractionCell, 8f, 14f));
                    enemy.Kill(null);
                    Corpse enemyWreck = enemy.Corpse;
                    if (enemyWreck == null || stable.AcceptWreck(enemyWreck) || !enemyWreck.Spawned)
                        throw new InvalidOperationException("an enemy Combatdog wreck was accepted or moved");
                    s.Track(enemyWreck).Destroy();

                    if (!stable.AcceptWreck(dog.Corpse))
                        throw new InvalidOperationException("the player Combatdog wreck was refused");
                    if (stable.RecoveredWrecks.Count != 1 || stable.RecoveredWrecks[0].Spawned)
                        throw new InvalidOperationException("the wreck is not safely held in the stable");
                })
                .Expect("the held wreck is part of the stable's recursive contents (removal can find it)", () =>
                    ThingOwnerUtility.GetAllThingsRecursively(Stable()).Contains(Stable().RecoveredWrecks[0]))
                .Expect("no delivery, no progress, and no materials consumed", () =>
                    Stable().RebuildProgress == 0 && !Stable().RebuildPaid
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 0 && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 0)
                .Do("select the stable and pause for a native waiting screenshot", () =>
                {
                    Find.Selector.Select(Stable(), playSound: false);
                    Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 7f);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                });
            CaptureArtwork(s, "auto-rebuild-waiting", () => { });
            s.Do("resume the simulation", () => Scenario.Resume())
                .Do("deliver steel only, short of the recipe", () =>
                    SpawnStack(s, ThingDefOf.Steel, 60, Stable().InteractionCell))
                .Wait(300)
                .Expect("a partial delivery consumes nothing", () =>
                    !Stable().RebuildPaid && Stable().RebuildProgress == 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 60
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 0)
                .Do("deliver the plasteel", () =>
                    SpawnStack(s, ThingDefOf.Plasteel, 30, Stable().InteractionCell))
                .Expect("exactly 50 steel and 25 plasteel are consumed and the work starts", () =>
                    Stable().RebuildPaid && Stable().RebuildProgress > 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 10
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 5)
                .Do("deliver spare materials that must survive the reload untouched", () =>
                    {
                        SpawnStack(s, ThingDefOf.Steel, 50, Stable().InteractionCell);
                        SpawnStack(s, ThingDefOf.Plasteel, 25, Stable().InteractionCell);
                    })
                .Do("switch the stable off", () => Stable().GetComp<CompFlickable>().SwitchIsOn = false)
                .Expect("no power", () => !Stable().CanCharge)
                .Do("note the progress", () => pausedAt = Stable().RebuildProgress)
                .Wait(600)
                .Expect("power loss pauses the rebuild in place", () =>
                    Stable().RebuildProgress == pausedAt && pausedAt > 0 && !Stable().CanCharge)
                .SaveAndReload()
                .Expect("after the load: same progress, already paid, and the spare materials untouched", () =>
                    Stable().RecoveredWrecks.Count == 1 && Stable().RebuildPaid
                    && Stable().RebuildProgress == pausedAt && !Stable().CanCharge
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 60
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 30)
                .Expect("powered again, it progresses from the saved work", () =>
                {
                    if (!Stable().CanCharge)
                        Stable().GetComp<CompFlickable>().SwitchIsOn = true;
                    return Stable().RebuildProgress > pausedAt;
                })
                .Expect("wait until about half the recipe's work amount is done", () =>
                    Stable().RebuildProgress >= Stable().RebuildWork / 2
                    && Stable().RebuildProgress < Stable().RebuildWork)
                .Do("select the stable and pause for a native rebuilding screenshot", () =>
                {
                    Find.Selector.Select(Stable(), playSound: false);
                    Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 7f);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                });
            CaptureArtwork(s, "auto-rebuild-progress", () => { });
            s.Do("resume to completion", () => Scenario.Resume())
                .Expect("the same pawn is rebuilt, docked and assigned to its stable", () =>
                    Dog() is Pawn d && !d.Dead && Stable().Docked.Contains(d)
                    && CompRobodog.Of(d).stable == Stable() && Stable().RecoveredWrecks.Count == 0)
                .Expect("identical name, kind/tier and gun picks; healthy, charged and functional", () =>
                {
                    Pawn d = Dog();
                    CompRobodog comp = CompRobodog.Of(d);
                    return d.Name.ToStringShort == name && d.kindDef == QR_DefOf.QR_CombatdogThree
                        && CombatTier.Of(d)?.tier == 3
                        && comp.Battery == 1f && !d.health.hediffSet.HasHediff(QR_DefOf.QR_PoweredDown)
                        && !d.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any()
                        && !d.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Any()
                        && comp.PickedGun(0) == QR_DefOf.QR_Gun_LMG
                        && comp.PickedGun(1) == QR_DefOf.QR_Gun_Flamethrower
                        && Tiers.Gun(d, 0) == QR_DefOf.QR_Gun_LMG
                        && d.equipment?.Primary?.def == QR_DefOf.QR_Gun_LMG
                        && d.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)
                        && d.health.capacities.CapableOf(PawnCapacityDefOf.Moving);
                })
                .Expect("the spare materials were not charged a second time", () =>
                    AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 60 && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 30)
                .Summary(() => $"rebuilt {name} at {Stable()?.RebuildWork} work")
                .OnFinish(() => Find.Selector.ClearSelection())
                .Start();
        }

        /// <summary>
        /// A full stable rebuilds the wreck but does not overflow: the same dog comes out beside it, unassigned, with its
        /// identity and gun pick intact, while the fifteen existing dogs keep their slots.
        /// </summary>
        private static void AutoRebuildAtCapacity()
        {
            var s = new Scenario("Auto rebuild at capacity", 30000);
            string stableId = null, dogId = null, name = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("a powered stable at capacity, a recovered Tier 2 wreck, and materials", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn combat = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogTwo));
                    dogId = combat.ThingID;
                    name = combat.Name.ToStringShort;
                    CompRobodog.Of(combat).PickGun(0, QR_DefOf.QR_Gun_LMG);
                    for (int i = 0; i < 14; i++)
                        s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    if (!stable.TryUndock(combat))
                        throw new InvalidOperationException("the Tier 2 dog didn't undock");
                    combat.Kill(null);
                    if (!stable.AcceptWreck(combat.Corpse))
                        throw new InvalidOperationException("the Tier 2 wreck was refused");
                    // Refill the vacated slot: at completion the stable is full and must not dock an extra dog.
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    // An active manual bill cannot use a full stable; automatic rebuilding must still proceed.
                    stable.BillStack.AddBill(QR_DefOf.QR_RebuildCombatdog.MakeNewBill());
                    SpawnStack(s, ThingDefOf.Steel, 50, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 25, stable.InteractionCell);
                })
                .Expect("the stable is fully occupied while the rebuild runs", () =>
                    Stable().Dogs().Count == 15 && !Stable().HasFreeSlot && Stable().RebuildPaid)
                .Expect("the rebuilt dog waits beside the full stable, unassigned", () =>
                {
                    Pawn dog = Scenario.ById(dogId) as Pawn;
                    return dog != null && dog.Spawned && !dog.Dead
                        && CompRobodog.Of(dog).stable == null && !Stable().Docked.Contains(dog)
                        && Stable().Dogs().Count == 15 && Stable().RecoveredWrecks.Count == 0;
                })
                .Expect("identity, tier, gun pick, health and charge are still the same dog's", () =>
                {
                    Pawn dog = Scenario.ById(dogId) as Pawn;
                    CompRobodog comp = CompRobodog.Of(dog);
                    return dog.Name.ToStringShort == name && dog.kindDef == QR_DefOf.QR_CombatdogTwo
                        && CombatTier.Of(dog)?.tier == 2
                        && comp.Battery == 1f && comp.PickedGun(0) == QR_DefOf.QR_Gun_LMG
                        && !dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any();
                })
                .Summary(() => $"rebuilt {name} beside 15 occupied slots")
                .Start();
        }

        /// <summary>
        /// A wreck taken from a destroyed stable still carries its dead dog's old affiliation. The full receiving stable
        /// rebuilds it but must leave it outside, unassigned, rather than counting it as an extra dog or keeping the
        /// phantom assignment. After a reload it is still free to adopt that stable, and does so once one slot opens.
        /// </summary>
        private static void CrossStableRebuildAtCapacity()
        {
            var s = new Scenario("Cross-stable rebuild at capacity", 30000);
            string homeId = null, stableId = null, dogId = null, name = null, occupantId = null;
            IntVec3 homeDelivery = IntVec3.Invalid;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => Scenario.ById(dogId) as Pawn;
            s.Do("a powered stable holding the wreck of its assigned Tier 2 Combatdog", () =>
                {
                    DraftColonists(s);
                    Building_Stable home = SpawnStable(s, powered: true);
                    homeId = home.ThingID;
                    homeDelivery = home.InteractionCell;
                    Pawn dog = s.Track(home.AddNewDog(QR_DefOf.QR_CombatdogTwo));
                    dogId = dog.ThingID;
                    name = dog.Name.ToStringShort;
                    CompRobodog.Of(dog).PickGun(0, QR_DefOf.QR_Gun_LMG);
                    if (!home.TryUndock(dog))
                        throw new InvalidOperationException("the Combatdog didn't undock");
                    dog.Kill(null);
                    Corpse wreck = dog.Corpse;
                    if (wreck == null || !home.AcceptWreck(wreck))
                        throw new InvalidOperationException("the Combatdog wreck wasn't taken by its stable");
                    s.Track(wreck);
                })
                .Do("destroy the home stable, releasing the wreck with its dead dog still affiliated to it", () =>
                {
                    Scenario.ById(homeId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f));
                    Corpse wreck = AutoRebuildCorpseOf(dogId);
                    if (Scenario.ById(homeId) != null || wreck == null || !wreck.Spawned)
                        throw new InvalidOperationException("the destroyed stable did not release the wreck");
                })
                .Do("a second powered stable, full with fifteen living dogs, takes the wreck and a full delivery", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true, near: homeDelivery,
                        minDist: 30f, maxDist: 60f);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 15; i++)
                    {
                        Pawn occupant = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                        if (i == 0)
                            occupantId = occupant.ThingID;
                    }
                    Corpse wreck = AutoRebuildCorpseOf(dogId);
                    if (wreck == null || !stable.AcceptWreck(wreck))
                        throw new InvalidOperationException("the full receiving stable refused the wreck");
                    SpawnStack(s, ThingDefOf.Steel, 50, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 25, stable.InteractionCell);
                })
                .Expect("the receiving stable is full and powered while it pays for the rebuild", () =>
                    Stable().CanCharge && Stable().Dogs().Count == 15 && !Stable().HasFreeSlot
                    && Stable().RebuildPaid && Stable().RecoveredWrecks.Count == 1)
                .Expect("the receiving stable finishes rebuilding the same dog", () =>
                    Dog() is Pawn dog && dog.Spawned && !dog.Dead && Stable().RecoveredWrecks.Count == 0)
                .Do("the rebuilt dog waits outside, unassigned, while the receiving stable stays full", () =>
                {
                    Pawn dog = Dog();
                    if (CompRobodog.Of(dog).stable != null || Stable().Docked.Contains(dog)
                        || Stable().Dogs().Count != 15
                        || AutoRebuildDelivered(Stable(), ThingDefOf.Steel) != 0
                        || AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) != 0)
                        throw new InvalidOperationException("the cross-stable rebuild kept an old affiliation or overfilled its receiver");
                })
                .Expect("the same name, tier and gun pick survived the cross-stable rebuild", () =>
                {
                    Pawn dog = Dog();
                    CompRobodog comp = CompRobodog.Of(dog);
                    return dog.Name.ToStringShort == name && dog.kindDef == QR_DefOf.QR_CombatdogTwo
                        && CombatTier.Of(dog)?.tier == 2 && comp.PickedGun(0) == QR_DefOf.QR_Gun_LMG;
                })
                .SaveAndReload()
                .Expect("after the load it is still outside, unassigned and waiting for a slot", () =>
                {
                    Pawn dog = Dog();
                    return dog != null && dog.Spawned && !dog.Dead
                        && CompRobodog.Of(dog).stable == null && !Stable().Docked.Contains(dog)
                        && Stable().Dogs().Count == 15 && !Stable().HasFreeSlot;
                })
                .Do("delete one tracked occupant, opening exactly one slot", () =>
                {
                    Thing occupant = Scenario.ById(occupantId);
                    if (occupant == null || occupant.Destroyed)
                        throw new InvalidOperationException("the tracked occupant is gone");
                    if (occupant is Pawn pawn && !pawn.Spawned && !Stable().TryUndock(pawn))
                        throw new InvalidOperationException("the tracked occupant couldn't leave its dock");
                    occupant.Destroy(DestroyMode.Vanish);
                    if (Stable().Dogs().Count != 14 || !Stable().HasFreeSlot)
                        throw new InvalidOperationException("the freed slot didn't open");
                    Dog()?.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                })
                .Expect("the waiting dog walks in and docks, adopting the receiving stable", () =>
                    Dog() is Pawn dog && Stable().Docked.Contains(dog)
                    && CompRobodog.Of(dog).stable == Stable() && Stable().Dogs().Count == 15)
                .Summary(() => $"rebuilt {name} at a full second stable; docked after a slot opened")
                .Start();
        }

        /// <summary>
        /// Destroying or uninstalling a stable must not swallow its recovered wrecks, even after a partially delivered
        /// or already funded rebuild. The released corpses keep their dead dog, name and tier across a reload.
        /// </summary>
        private static void RecoveredWrecksReleased()
        {
            var s = new Scenario("Recovered wrecks released", 30000);
            string stableId = null, dogId = null, name = null, secondDogId = null, secondName = null, miniId = null;
            IntVec3 firstDelivery = IntVec3.Invalid;
            Map firstMap = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("a powered stable holding a Tier 1 wreck and a partial delivery", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    firstDelivery = stable.InteractionCell;
                    firstMap = stable.Map;
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    dogId = dog.ThingID;
                    name = dog.Name.ToStringShort;
                    stable.TryUndock(dog);
                    dog.Kill(null);
                    if (!stable.AcceptWreck(dog.Corpse))
                        throw new InvalidOperationException("the Tier 1 wreck was refused");
                    SpawnStack(s, ThingDefOf.Steel, 20, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 25, stable.InteractionCell);
                })
                .Wait(300)
                .Expect("a partially delivered rebuild has consumed nothing", () =>
                    !Stable().RebuildPaid && Stable().RebuildProgress == 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 20 && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 25)
                .Do("destroy the stable", () => Scenario.ById(stableId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f)))
                .Expect("the wreck is released on the map with its dead dog intact", () =>
                    Scenario.ById(stableId) == null && AutoRebuildCorpseOf(dogId) is Corpse c && c.Spawned
                    && c.InnerPawn.Name.ToStringShort == name && c.InnerPawn.kindDef == QR_DefOf.QR_Combatdog)
                .Expect("the unconsumed partial delivery stayed on the ground", () =>
                    // Demolition can add salvage to this area; none of the delivered minimum may disappear.
                    AutoRebuildDelivered(firstDelivery, firstMap, ThingDefOf.Steel) >= 20
                    && AutoRebuildDelivered(firstDelivery, firstMap, ThingDefOf.Plasteel) >= 25)
                .Do("track the released corpse", () => s.Track(AutoRebuildCorpseOf(dogId)))
                .Do("a second powered stable, a Tier 4 wreck and a full delivery", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true, near: firstDelivery,
                        minDist: 30f, maxDist: 60f);
                    stableId = stable.ThingID;
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogFour));
                    secondDogId = dog.ThingID;
                    secondName = dog.Name.ToStringShort;
                    stable.TryUndock(dog);
                    dog.Kill(null);
                    if (!stable.AcceptWreck(dog.Corpse))
                        throw new InvalidOperationException("the Tier 4 wreck was refused");
                    SpawnStack(s, ThingDefOf.Steel, 50, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 25, stable.InteractionCell);
                })
                .Expect("the full delivery is consumed and the rebuild is under way", () =>
                    Stable().RebuildPaid && Stable().RebuildProgress > 0)
                .Do("uninstall the stable mid-build", () =>
                {
                    Thing stable = Scenario.ById(stableId);
                    Map map = stable.Map;
                    IntVec3 pos = stable.Position;
                    MinifiedThing mini = stable.MakeMinified();
                    if (!GenPlace.TryPlaceThing(mini, pos, map, ThingPlaceMode.Near))
                        throw new InvalidOperationException("couldn't place the minified stable");
                    miniId = s.Track(mini).ThingID;
                })
                .Expect("the packed stable keeps no wreck, and the released corpse retains its Tier 4 arsenal", () =>
                    Scenario.ById(miniId) is MinifiedThing mini
                    && ((Building_Stable)mini.InnerThing).RecoveredWrecks.Count == 0
                    && AutoRebuildCorpseOf(secondDogId) is Corpse c && c.Spawned
                    && c.InnerPawn.Name.ToStringShort == secondName
                    && c.InnerPawn.kindDef == QR_DefOf.QR_CombatdogFour
                    && Tiers.Gun(c.InnerPawn, 0) == QR_DefOf.QR_Gun_DualChargeRifle)
                .Do("track the second released corpse", () => s.Track(AutoRebuildCorpseOf(secondDogId)))
                .SaveAndReload()
                .Expect("both released wrecks survive the reload; the packed stable is still empty", () =>
                    AutoRebuildCorpseOf(dogId)?.Spawned == true && AutoRebuildCorpseOf(secondDogId)?.Spawned == true
                    && Scenario.ById(miniId) is MinifiedThing mini
                    && ((Building_Stable)mini.InnerThing).RecoveredWrecks.Count == 0
                    && ((Building_Stable)mini.InnerThing).RebuildProgress == 0
                    && !((Building_Stable)mini.InnerThing).RebuildPaid)
                .Summary(() => $"released {name} and {secondName}")
                .Start();
        }
    }
}
