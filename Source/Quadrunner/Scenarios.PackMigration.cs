using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        /// <summary>Real legacy equipment serialized in all holders; same pawns return, then fight together after load.</summary>
        private static void CombatTierLegacyMigration()
        {
            var s = new Scenario("Combat tier legacy migration", 30000);
            string stableId = null, patientId = null;
            var ids = new string[8];
            var carriers = new string[2];
            var guns = new string[8];
            var smokeGuns = new string[4];
            int caravanId = -1;
            int[] fieldJobs = new int[2];
            float[] charges = { 0.61f, 0.72f, 0f, 0.83f };
            var enemies = new List<string>();
            var fired = new HashSet<string>();
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            Pawn Dog(int i) => Scenario.ById(ids[i]) as Pawn
                ?? Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId)?.PawnsListForReading.FirstOrDefault(p => p.ThingID == ids[i]);
            s.OnFinish(() =>
            {
                Caravan caravan = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == caravanId);
                if (caravan != null)
                {
                    foreach (Pawn pawn in caravan.PawnsListForReading.ToList())
                    {
                        caravan.RemovePawn(pawn);
                        if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                        else if (!pawn.Destroyed) pawn.Destroy();
                    }
                    caravan.Destroy();
                }
                foreach (Pawn pawn in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.Where(p =>
                             ids.Contains(p.ThingID) || carriers.Contains(p.ThingID) || p.ThingID == patientId).ToList())
                {
                    pawn.carryTracker?.CarriedThing?.Destroy();
                    if (Find.WorldPawns.Contains(pawn)) Find.WorldPawns.RemoveAndDiscardPawnViaGC(pawn);
                }
            });
            s.Do("legacy T3/T4 cohorts docked, deployed, carried and in a caravan, with old selected guns", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    // The preceding fight can leave the 60-tick threat cache true after its last raider is removed.
                    // Reset it before staging docked holders; migration must not race an unrelated deployment.
                    Raid.Recheck(map);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    var travelers = new List<Pawn>();
                    for (int tier = 0; tier < 2; tier++)
                    {
                        for (int holder = 0; holder < 4; holder++)
                        {
                            int index = tier * 4 + holder;
                            Pawn dog = s.Track(stable.AddNewDog(tier == 0 ? QR_DefOf.QR_CombatdogThree : QR_DefOf.QR_CombatdogFour));
                            ids[index] = dog.ThingID;
                            dog.Name = new NameSingle($"Legacy combat {index}");
                            CompRobodog comp = CompRobodog.Of(dog);
                            comp.SetBattery(charges[holder]);
                            ThingDef oldGun = tier == 0 ? QR_DefOf.QR_Gun_LMG
                                : holder == 0 ? QR_DefOf.QR_Gun_Minigun : holder == 1 ? QR_DefOf.QR_Gun_LMG : QR_DefOf.QR_Gun_AssaultRifle;
                            comp.PickGun(0, oldGun);
                            comp.PickGun(1, tier == 0 ? QR_DefOf.QR_Gun_Flamethrower : null);
                            Tiers.Arm(dog, oldGun);
                            foreach (ThingWithComps weapon in dog.equipment.AllEquipmentListForReading.ToList())
                                if (weapon.def == QR_DefOf.QR_Gun_SmokeLauncher) dog.equipment.DestroyEquipment(weapon);
                            if (holder == 0) continue;
                            stable.TryUndock(dog);
                            // Undocking equips the current loadout; replace it again to serialize the actual old gun.
                            Tiers.Arm(dog, oldGun);
                            foreach (ThingWithComps weapon in dog.equipment.AllEquipmentListForReading.ToList())
                                if (weapon.def == QR_DefOf.QR_Gun_SmokeLauncher) dog.equipment.DestroyEquipment(weapon);
                            SurveyHold(dog, 20000);
                            if (holder == 1)
                            {
                                fieldJobs[tier] = dog.CurJob.loadID;
                                Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, dog, dog.RaceProps.body.corePart);
                                wound.Severity = 2f;
                                dog.health.AddHediff(wound);
                            }
                            else if (holder == 2)
                            {
                                Pawn carrier = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                                carriers[tier] = carrier.ThingID;
                                stable.TryUndock(carrier);
                                SurveyHold(carrier, 20000);
                                if (carrier.carryTracker.TryStartCarry(dog, 1, reserve: false) != 1)
                                    throw new InvalidOperationException("couldn't carry the legacy combat casualty");
                            }
                            else travelers.Add(dog);
                        }
                    }
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, stable.InteractionCell);
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 2f);
                    if (Dog(5).carryTracker.TryStartCarry(patient, 1, reserve: false) != 1)
                        throw new InvalidOperationException("couldn't stage saved combat cargo");
                    Pawn traveler = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, stable.InteractionCell);
                    travelers.Add(traveler);
                    caravanId = CaravanMaker.MakeCaravan(travelers, Faction.OfPlayer, map.Tile, true).ID;
                })
                .SaveAndReload(always: true)
                .Do("all eight identities and holder states survive; every T4 has its new arsenal", () =>
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        Pawn dog = Dog(i);
                        CompRobodog comp = CompRobodog.Of(dog);
                        if (dog == null || dog.Name.ToStringFull != $"Legacy combat {i}" || comp.stable?.ThingID != stableId
                            || Math.Abs(comp.Battery - charges[i % 4]) > 0.0001f)
                            throw new InvalidOperationException($"legacy dog {i} lost identity, battery or stable");
                        ThingDef wanted = i < 4 ? QR_DefOf.QR_Gun_LMG : QR_DefOf.QR_Gun_DualChargeRifle;
                        if (dog.equipment.Primary?.def != wanted || dog.equipment.AllEquipmentListForReading.Count != (i < 4 ? 1 : 2))
                            throw new InvalidOperationException($"legacy dog {i} has the wrong migrated equipment");
                        guns[i] = dog.equipment.Primary.ThingID;
                        if (i >= 4) smokeGuns[i - 4] = dog.equipment.AllEquipmentListForReading.Single(w => w.def == QR_DefOf.QR_Gun_SmokeLauncher).ThingID;
                    }
                    for (int tier = 0; tier < 2; tier++)
                    {
                        if (!Stable().Docked.Contains(Dog(tier * 4)))
                            throw new InvalidOperationException($"legacy tier {tier + 3} docked dog deployed during the holder check; war {Net().War}, threat {Raid.Active(Find.CurrentMap)}");
                        if (Dog(tier * 4 + 1).CurJob.loadID != fieldJobs[tier]
                            || Math.Abs(InjurySeverity(Dog(tier * 4 + 1)) - 2f) > 0.001f
                            || ((Pawn)Scenario.ById(carriers[tier])).carryTracker.CarriedThing != Dog(tier * 4 + 2)
                            || !CompRobodog.Of(Dog(tier * 4 + 2)).PoweredDown)
                            throw new InvalidOperationException($"legacy tier {tier + 3}: field job {Dog(tier * 4 + 1).CurJob} (saved {fieldJobs[tier]}), injury {InjurySeverity(Dog(tier * 4 + 1))}, casualty holder {Dog(tier * 4 + 2).ParentHolder}");
                    }
                    if (Dog(5).carryTracker.CarriedThing?.ThingID != patientId)
                        throw new InvalidOperationException("migration lost the saved carried pawn");
                })
                .SaveAndReload(always: true)
                .Do("another load preserves the exact equipment instances without duplicates", () =>
                {
                    for (int i = 0; i < ids.Length; i++)
                        if (Dog(i).equipment.Primary.ThingID != guns[i]
                            || Dog(i).equipment.AllEquipmentListForReading.Count != (i < 4 ? 1 : 2)
                            || i >= 4 && Dog(i).equipment.AllEquipmentListForReading.Single(w => w.def == QR_DefOf.QR_Gun_SmokeLauncher).ThingID != smokeGuns[i - 4])
                            throw new InvalidOperationException($"second load replaced or duplicated dog {i}'s equipment");
                })
                .Do("the migrated field T3 and T4 form a pack against three actual raiders", () =>
                {
                    Map map = Find.CurrentMap;
                    Dog(5).carryTracker.TryDropCarriedThing(Stable().InteractionCell, ThingPlaceMode.Near, out _);
                    CellRect pad = SurveyFindRect(map, 42, 25, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 4, 0, pad.minZ + 10);
                    for (int i = 0; i < 2; i++)
                    {
                        Pawn dog = Dog(i == 0 ? 1 : 5);
                        Teleport(dog, at + new IntVec3(0, 0, i * 4));
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                    for (int i = 0; i < 3; i++) enemies.Add(SpawnRaider(s, at + new IntVec3(24 + i, 0, i * 3)).ThingID);
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both migrated dogs fire real projectiles as a pack", () =>
                {
                    foreach (Projectile shot in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>())
                        if (shot.Launcher == Dog(1) || shot.Launcher == Dog(5)) fired.Add(((Pawn)shot.Launcher).ThingID);
                    return fired.Count == 2;
                })
                .Do("finish the migrated pack's fight", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast)
                .Expect("all three enemies are defeated by the migrated pack", () => enemies.All(id => !Standing(id)))
                .Summary(() => $"migration cohort 8 dogs; observed firing {fired.Count}/2; enemies {string.Join("; ", enemies.Select(Fate))}")
                .Start();
        }
    }
}
