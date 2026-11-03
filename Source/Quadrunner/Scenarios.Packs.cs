using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        private static Pawn PackDog(Scenario s, Building_Stable stable, PawnKindDef kind, IntVec3 at)
        {
            Pawn dog = s.Track(stable.AddNewDog(kind));
            if (!stable.TryUndock(dog)) throw new InvalidOperationException("couldn't deploy pack dog");
            Teleport(dog, at);
            return dog;
        }

        private static bool PackAttacks(Pawn dog, Pawn target) => dog?.Spawned == true && target != null
            && (dog.CurJobDef == JobDefOf.AttackStatic || dog.CurJobDef == JobDefOf.AttackMelee)
            && dog.CurJob.targetA.Thing == target;

        private static bool PairedProjectiles(Pawn dog, ThingDef gun)
        {
            // Only count the opening flight window, shorter than the gap between burst shots. Two ordinary
            // sequential rounds in the air must not masquerade as simultaneous paired fire.
            VerbProperties verb = gun.Verbs[0];
            float window = Math.Min(8, verb.ticksBetweenBurstShots - 2) * verb.defaultProjectile.projectile.SpeedTilesPerTick;
            return Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>()
                .Count(p => p.Launcher == dog && p.EquipmentDef == gun
                    && (p.ExactPosition - dog.DrawPos).MagnitudeHorizontalSquared() <= window * window) >= 2;
        }

        private static Pawn ArmedPackEnemy(Scenario s, IntVec3 at)
        {
            Pawn enemy = SpawnRaider(s, at);
            enemy.equipment.AddEquipment(s.Track((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_AssaultRifle"))));
            return enemy;
        }

        private static void PackCleanup(Scenario s, CellRect pad)
        {
            s.OnFinish(() =>
            {
                Map map = Find.CurrentMap;
                foreach (Thing thing in map.listerThings.ThingsOfDef(ThingDefOf.Fire).ToList())
                    if (pad.ExpandedBy(8).Contains(thing.Position)) thing.Destroy();
                foreach (IntVec3 cell in pad.ExpandedBy(8).ClipInsideMap(map))
                    map.gasGrid.SetDirect(cell, 0, 0, 0, 0);
            });
        }

        /// <summary>Two local mixed packs fight separate nearest enemies, then retarget; player orders and retreat still win.</summary>
        private static void Tier3PackCombat()
        {
            var s = new Scenario("Tier 3 pack combat", 30000);
            string stableId = null;
            string[] dogs = new string[4], enemies = new string[4];
            var fired = new HashSet<string>();
            CellRect pad = default;
            Pawn D(int i) => Scenario.ById(dogs[i]) as Pawn;
            Pawn E(int i) => Scenario.ById(enemies[i]) as Pawn;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            bool WatchFire()
            {
                foreach (Projectile shot in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>())
                    if (shot.Launcher is Pawn dog && dogs.Contains(dog.ThingID)) fired.Add(dog.ThingID);
                return true;
            }
            s.Do("two T3-led mixed packs, each with two nearby enemies on a controlled field", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    RoomWithBeds(s, ClearRect(map, stable.InteractionCell, 11, 1, 12f, 25f), 4, prisoners: true);
                    pad = SurveyFindRect(map, 76, 32, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    for (int group = 0; group < 2; group++)
                    {
                        IntVec3 at = new IntVec3(pad.minX + 5 + group * 40, 0, pad.minZ + 10);
                        dogs[group * 2] = PackDog(s, stable, QR_DefOf.QR_CombatdogThree, at).ThingID;
                        dogs[group * 2 + 1] = PackDog(s, stable, QR_DefOf.QR_CombatdogTwo, at + new IntVec3(0, 0, 4)).ThingID;
                        enemies[group * 2] = SpawnRaider(s, at + new IntVec3(14, 0, 0)).ThingID;
                        enemies[group * 2 + 1] = SpawnRaider(s, at + new IntVec3(23, 0, 5)).ThingID;
                    }
                    float baseSpeed = QR_DefOf.QR_Combatdog.race.GetStatValueAbstract(StatDefOf.MoveSpeed);
                    if (Math.Abs(D(0).GetStatValue(StatDefOf.MoveSpeed) - baseSpeed * 1.5f * 2f) > 0.01f
                        || Math.Abs(D(1).GetStatValue(StatDefOf.MoveSpeed) - baseSpeed * 2f) > 0.01f)
                        throw new InvalidOperationException("T3 chassis speed or lower-tier deployed movement changed incorrectly");
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("each mixed pack shares its closest enemy, not the other pack's target", () =>
                    WatchFire() && PackAttacks(D(0), E(0)) && PackAttacks(D(1), E(0))
                    && PackAttacks(D(2), E(2)) && PackAttacks(D(3), E(2)))
                .Expect("all four dogs fire real projectiles at their local enemies", () => WatchFire() && fired.Count == 4)
                .SaveAndReload()
                .Do("resume faster after the observed opening volleys", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast)
                .Expect("both nearest enemies fall to actual attacks", () => !Standing(enemies[0]) && !Standing(enemies[2]))
                .Expect("each pack advances to its next enemy", () =>
                    (!Standing(enemies[1]) || PackAttacks(D(0), E(1)) || PackAttacks(D(1), E(1)))
                    && (!Standing(enemies[3]) || PackAttacks(D(2), E(3)) || PackAttacks(D(3), E(3))))
                .Expect("both enemy packs are defeated", () => enemies.All(id => !Standing(id)))
                .Expect("downed survivors are captured through the existing single-carrier path", () => enemies.All(DealtWith))
                .Do("two distant packs see the same closest enemy and a farther decoy", () =>
                {
                    foreach (string id in enemies)
                        if (Scenario.ById(id) is Pawn enemy && !enemy.Destroyed) enemy.Destroy();
                    for (int i = 0; i < dogs.Length; i++)
                    {
                        Pawn dog = D(i);
                        Teleport(dog, new IntVec3(pad.minX + 5 + i / 2 * 40, 0, pad.minZ + 10 + i % 2 * 4));
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }
                    enemies[0] = SpawnRaider(s, new IntVec3(pad.minX + 25, 0, pad.minZ + 10)).ThingID;
                    enemies[1] = SpawnRaider(s, new IntVec3(pad.minX + 69, 0, pad.minZ + 10)).ThingID;
                    Raid.Recheck(Find.CurrentMap);
                    Net().Tick(force: true);
                })
                .Expect("both independent packs choose their closest enemy, even when another pack targets it", () =>
                    dogs.All(id => PackAttacks(Scenario.ById(id) as Pawn, E(0))))
                .Expect("the shared closest enemy and the decoy fall", () => !Standing(enemies[0]) && !Standing(enemies[1]))
                .Expect("survivors of the second fight are captured too", () => enemies.All(DealtWith))
                .Do("a new enemy while one leader is drafted and the other reaches retreat battery", () =>
                {
                    foreach (string id in enemies)
                        if (Scenario.ById(id) is Pawn enemy && !enemy.Destroyed) enemy.Destroy();
                    foreach (Pawn dog in dogs.Select(id => Scenario.ById(id) as Pawn))
                    {
                        if (!dog.Spawned) Stable().TryUndock(dog);
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    }
                    D(0).drafter.Drafted = true;
                    D(0).drafter.FireAtWill = false;
                    SurveyHold(D(0), 6000);
                    CompRobodog.Of(D(2)).SetBattery(0.09f);
                    Pawn enemy2 = SpawnRaider(s, D(0).Position + new IntVec3(10, 0, 0));
                    enemies[0] = enemy2.ThingID;
                    Raid.Recheck(Find.CurrentMap);
                    Net().Tick(force: true);
                })
                .Expect("the drafted leader keeps its order and the low-battery leader leaves the fight", () =>
                    D(0).Drafted && D(0).CurJobDef == JobDefOf.Wait_MaintainPosture
                    && !PackAttacks(D(2), E(0)) && (D(2).CurJobDef == QR_DefOf.QR_Dock || !D(2).Spawned))
                .Summary(() => $"observed firing from {string.Join(",", fired)}; current T3 speed {D(0)?.GetStatValue(StatDefOf.MoveSpeed):F2} c/s; war {Net().War}")
                .Start();
        }

        /// <summary>Actual paired volleys, injuries and smoke from a two-T4 pack, at rifle and extended shotgun range.</summary>
        private static void Tier4PackArsenal()
        {
            var s = new Scenario("Tier 4 pack arsenal", 30000);
            string[] dogs = new string[2], enemies = new string[3];
            var rifled = new HashSet<string>();
            var shotgunned = new HashSet<string>();
            bool smokeShell = false, smoke = false;
            int closeWaveAt = 0;
            var firstShotDelays = new List<int>();
            var allEnemyIds = new HashSet<string>();
            CellRect pad = default;
            IntVec3 start = IntVec3.Invalid;
            Pawn D(int i) => Scenario.ById(dogs[i]) as Pawn;
            Pawn E(int i) => Scenario.ById(enemies[i]) as Pawn;
            Pawn EnemyAt(IntVec3 at)
            {
                Pawn enemy = ArmedPackEnemy(s, at);
                allEnemyIds.Add(enemy.ThingID);
                return enemy;
            }
            s.OnFinish(() =>
            {
                // A fixture's explicitly destroyed vanilla rifle must not leave a retained pawn's cooldown
                // pointing at its unsaved verb. Keep vanilla world-pawn/history ownership intact.
                foreach (Pawn enemy in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead
                             .Where(p => allEnemyIds.Contains(p.ThingID)).ToList())
                {
                    enemy.stances?.CancelBusyStanceHard();
                }
            });
            bool Watch()
            {
                foreach (Pawn dog in dogs.Select(id => Scenario.ById(id) as Pawn))
                {
                    if (PairedProjectiles(dog, QR_DefOf.QR_Gun_DualChargeRifle)) rifled.Add(dog.ThingID);
                    if (PairedProjectiles(dog, QR_DefOf.QR_Gun_DualChainShotgun) && shotgunned.Add(dog.ThingID))
                    {
                        Verb verb = dog.equipment.PrimaryEq.PrimaryVerb;
                        int delay = verb.LastShotTick - closeWaveAt;
                        firstShotDelays.Add(delay);
                    }
                }
                smokeShell |= Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>()
                    .Any(p => p.Launcher is Pawn dog && dogs.Contains(dog.ThingID) && p.EquipmentDef == QR_DefOf.QR_Gun_SmokeLauncher);
                smoke |= pad.Cells.Any(c => Find.CurrentMap.gasGrid.DensityAt(c, GasType.BlindSmoke) > 0);
                return true;
            }
            s.Do("two T4s and a hostile pack beyond shotgun range", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    pad = SurveyFindRect(map, 45, 30, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    start = new IntVec3(pad.minX + 5, 0, pad.minZ + 12);
                    for (int i = 0; i < 2; i++)
                        dogs[i] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, start + new IntVec3(0, 0, i * 4)).ThingID;
                    for (int i = 0; i < 3; i++)
                        // The second T4 is closer to enemy 1: coordination must override its independent nearest choice.
                        enemies[i] = EnemyAt(start + new IntVec3(26, 0, i * 6)).ThingID;
                    float baseSpeed = QR_DefOf.QR_Combatdog.race.GetStatValueAbstract(StatDefOf.MoveSpeed);
                    if (Math.Abs(D(0).GetStatValue(StatDefOf.MoveSpeed) - baseSpeed * 1.75f * 2f) > 0.01f)
                        throw new InvalidOperationException("T4 speed has an incorrect chassis multiplier or a leftover minigun penalty");
                    CloseUpCaptures(s);
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both T4s focus the closest standing enemy", () => Watch() && PackAttacks(D(0), E(0)) && PackAttacks(D(1), E(0)))
                .Expect("each T4 launches two charge-rifle projectiles together", () => Watch() && rifled.Count == 2)
                .Expect("the rifle volley causes a real wound", () => Watch() && (!Standing(enemies[0]) || InjurySeverity(E(0)) > 0f))
                .SaveAndReload()
                .Do("replace the defeated/distant wave with three enemies 17–19 cells out", () =>
                {
                    foreach (string id in enemies)
                        if (Scenario.ById(id) is Pawn enemy && !enemy.Destroyed)
                        {
                            enemy.stances.CancelBusyStanceHard();
                            enemy.Destroy();
                        }
                    for (int i = 0; i < 2; i++)
                    {
                        Teleport(D(i), start + new IntVec3(0, 0, i * 4));
                        D(i).jobs.EndCurrentJob(JobCondition.InterruptForced);
                        D(i).stances.CancelBusyStanceHard();
                    }
                    for (int i = 0; i < 3; i++)
                        enemies[i] = EnemyAt(start + new IntVec3(17 + i, 0, i * 3)).ThingID;
                    closeWaveAt = Find.TickManager.TicksGame;
                    Raid.Recheck(Find.CurrentMap);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both T4s fire paired shotgun projectiles beyond vanilla chain-shotgun range", () => Watch() && shotgunned.Count == 2)
                .Expect("an automatic smoke shell is actually launched", () => Watch() && smokeShell)
                .Expect("the smoke shell creates real smoke while the damaging guns keep firing", () => Watch() && smoke
                    && enemies.Any(id => !Standing(id) || InjurySeverity(Scenario.ById(id) as Pawn) > 0f));
            CaptureArtwork(s, "combat-t4-pack-smoke", () =>
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Frame(D(0), E(1));
            }, delaySeconds: 1f);
            s.Do("finish the pack battle", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast)
                .Expect("all three close enemies are defeated", () => Watch() && enemies.All(id => !Standing(id)))
                .Do("power down both T4s", () =>
                {
                    foreach (Pawn dog in dogs.Select(id => Scenario.ById(id) as Pawn)) CompRobodog.Of(dog).SetBattery(0f);
                })
                .Expect("no guns drop and no stance refers to a removed weapon", () => LooseGuns() == 0
                    && dogs.All(id => Scenario.ById(id) is Pawn dog && dog.Downed && !StaleStance(dog)))
                .Summary(() => $"paired rifles {rifled.Count}/2; paired extended shotguns {shotgunned.Count}/2; first paired shotgun delays {string.Join("/", firstShotDelays)} ticks; smoke shell {smokeShell}, cloud {smoke}; enemies {string.Join("; ", enemies.Select(Fate))}; loose guns {LooseGuns()}")
                .Start();
        }
    }
}
