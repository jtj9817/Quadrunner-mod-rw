using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>Plan 0012 scenarios: tear gas as a supplemental combat mount, and the T4 smoke cluster cap.</summary>
    public static partial class Scenarios
    {
        private const int GasWindow = 900;

        /// <summary>Remove the tear gas cloud things and the hediff from every pawn that ever stood in one.</summary>
        private static void CleanTearGas(Scenario s, CellRect pad)
        {
            s.OnFinish(() =>
            {
                Map map = Find.CurrentMap;
                if (map == null)
                    return;
                foreach (Thing gas in map.listerThings.ThingsOfDef(QR_DefOf.QR_TearGasCloud).ToList())
                    if (!gas.Destroyed)
                        gas.Destroy();
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    Hediff gas2 = pawn.health?.hediffSet?.GetFirstHediffOfDef(QR_DefOf.QR_TearGas);
                    if (gas2 != null)
                        pawn.health.RemoveHediff(gas2);
                }
                foreach (IntVec3 cell in pad.ExpandedBy(10))
                    if (cell.InBounds(map) && map.gasGrid.DensityAt(cell, GasType.BlindSmoke) > 0)
                        map.gasGrid.SetDirect(cell, 0, 0, 0, 0);
            });
        }

        private static bool AnyGasOnMap(Map map) =>
            map.listerThings.ThingsOfDef(QR_DefOf.QR_TearGasCloud).Count > 0;

        /// <summary>
        /// A staged hostile group holding ground like current combat fixtures, colonists drafted but present, one
        /// colonist planted in the gas path. Tier 3s fire the tear gas launcher automatically; raiders and the planted
        /// colonist are blinded; the robots are not; the primary guns keep firing.
        /// </summary>
        private static void TearGasPack()
        {
            var s = new Scenario("Tear gas pack", 40000);
            string stableId = null, colonistId = null;
            string[] dogs = new string[2], enemies = new string[4];
            bool tearShell = false, gasSpread = false, colonistBlinded = false, primaryFired = false;
            int gasCellsSeen = 0;
            CellRect pad = default;
            var blindedRaiders = new HashSet<string>();
            Pawn Colonist() => Scenario.ById(colonistId) as Pawn;
            bool Watch()
            {
                foreach (Projectile shot in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>())
                {
                    if (shot.Launcher is not Pawn launcher || !dogs.Contains(launcher.ThingID))
                        continue;
                    if (shot.EquipmentDef == QR_DefOf.QR_Gun_TearGasLauncher)
                        tearShell = true;
                    else
                        primaryFired = true;
                }
                int gas = Find.CurrentMap.listerThings.ThingsOfDef(QR_DefOf.QR_TearGasCloud).Count;
                gasCellsSeen = Math.Max(gasCellsSeen, gas);
                gasSpread |= gas > 0;
                foreach (string id in enemies)
                    if (Scenario.ById(id) is Pawn enemy && enemy.health.hediffSet.HasHediff(QR_DefOf.QR_TearGas))
                        blindedRaiders.Add(id);
                if (Colonist() is Pawn colonist)
                    colonistBlinded |= colonist.health.hediffSet.HasHediff(QR_DefOf.QR_TearGas);
                return true;
            }
            s.Do("two T3s, an invading hostile group on a controlled field, colonists drafted but present, one colonist in the gas path", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    pad = SurveyFindRect(map, 42, 25, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    CleanTearGas(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 5, 0, pad.minZ + 10);
                    dogs[0] = PackDog(s, stable, QR_DefOf.QR_CombatdogThree, at).ThingID;
                    dogs[1] = PackDog(s, stable, QR_DefOf.QR_CombatdogThree, at + new IntVec3(0, 0, 4)).ThingID;
                    // Close enough for the mid-path shell to cover them: the cloud blooms ~3.5 cells from the
                    // raiders (the Defense lord holds them there), so standing in it blinds them.
                    for (int i = 0; i < enemies.Length; i++)
                        enemies[i] = SpawnRaider(s, at + new IntVec3(7 + (i % 2), 0, i * 2 - 2)).ThingID;
                    Pawn colonist = Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault()
                        ?? throw new InvalidOperationException("no colonist present for the gas-path probe");
                    Teleport(colonist, new IntVec3(pad.minX + 9, 0, pad.minZ + 12));
                    colonistId = colonist.ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both T3s carry the tear gas launcher beside the primary", () =>
                    dogs.All(id => Scenario.ById(id) is Pawn dog
                        && dog.equipment.AllEquipmentListForReading.Any(w => w.def == QR_DefOf.QR_Gun_TearGasLauncher)))
                .Expect("an automatic tear gas shell is launched", () => Watch() && tearShell)
                .Expect("a tear gas cloud blooms", () => Watch() && gasSpread)
                .Expect("raiders standing in the cloud are blinded", () => Watch() && blindedRaiders.Count > 0)
                .Expect("the colonist planted in the gas path is blinded too (drafted, presence is the point)", () => Watch() && colonistBlinded)
                .Expect("the robodogs never gain the tear gas hediff (non-organic flesh)", () =>
                    Watch() && dogs.All(id => Scenario.ById(id) is Pawn dog
                        && !dog.health.hediffSet.HasHediff(QR_DefOf.QR_TearGas)))
                .SaveAndReload()
                .Expect("the primary guns keep firing alongside the launcher after the reload", () =>
                    Watch() && primaryFired)
                .Summary(() => $"tear shell launched: {tearShell}; max cloud cells: {gasCellsSeen}; blinded raiders: {blindedRaiders.Count}/{enemies.Length}; colonist blinded: {colonistBlinded}")
                .Start();
        }

        /// <summary>
        /// Four Tier 4 Combatdogs clustered inside the pack radius facing a staged hostile group, colonists drafted but
        /// present. Per 900-tick window, at most 3 of them pop smoke; the rest fire in later windows (rotation); every
        /// T4 fires the tear gas launcher (no cap), and the smoke cloud sits on the approach path, not on the enemy.
        /// </summary>
        private static void Tier4TearGasSmokeCluster()
        {
            var s = new Scenario("Tier 4 tear gas and smoke cluster", 40000);
            string stableId = null;
            string[] dogs = new string[4], enemies = new string[4];
            var smokeByWindow = new Dictionary<int, HashSet<string>>();
            var tearByWindow = new Dictionary<int, HashSet<string>>();
            bool gasSpread = false;
            bool smokeBetweenChecked = false, smokeBetweenPassed = false;
            CellRect pad = default;
            bool Watch()
            {
                foreach (Projectile shot in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Projectile).OfType<Projectile>())
                {
                    if (shot.Launcher is not Pawn launcher || !dogs.Contains(launcher.ThingID))
                        continue;
                    int window = Find.TickManager.TicksGame / GasWindow;
                    if (shot.EquipmentDef == QR_DefOf.QR_Gun_SmokeLauncher)
                    {
                        if (!smokeByWindow.TryGetValue(window, out HashSet<string> set))
                            smokeByWindow[window] = set = new HashSet<string>();
                        set.Add(launcher.ThingID);
                    }
                    else if (shot.EquipmentDef == QR_DefOf.QR_Gun_TearGasLauncher)
                    {
                        if (!tearByWindow.TryGetValue(window, out HashSet<string> set))
                            tearByWindow[window] = set = new HashSet<string>();
                        set.Add(launcher.ThingID);
                    }
                }
                gasSpread |= AnyGasOnMap(Find.CurrentMap);
                if (!smokeBetweenChecked)
                {
                    Map map = Find.CurrentMap;
                    List<IntVec3> fresh = new List<IntVec3>();
                    for (int x = pad.minX; x <= pad.maxX; x++)
                        for (int z = pad.minZ; z <= pad.maxZ; z++)
                        {
                            var cell = new IntVec3(x, 0, z);
                            if (cell.InBounds(map) && map.gasGrid.DensityAt(cell, GasType.BlindSmoke) > 0)
                                fresh.Add(cell);
                        }
                    if (fresh.Count > 0)
                    {
                        smokeBetweenChecked = true;
                        Vector3 sum = Vector3.zero;
                        foreach (IntVec3 cell in fresh)
                            sum += cell.ToVector3Shifted();
                        Vector3 centroid = sum / fresh.Count;
                        Pawn nearestEnemy = enemies.Select(id => Scenario.ById(id) as Pawn)
                            .Where(p => p != null && !p.Dead && !p.Downed)
                            .OrderBy(p => (p.Position.ToVector3Shifted() - centroid).MagnitudeHorizontalSquared())
                            .FirstOrDefault();
                        if (nearestEnemy != null)
                        {
                            Pawn nearestDog = dogs.Select(id => Scenario.ById(id) as Pawn).Where(p => p != null)
                                .OrderBy(p => (p.Position.ToVector3Shifted() - centroid).MagnitudeHorizontalSquared()).First();
                            IntVec3 mid = Tiers.ScreenCell(nearestDog, nearestEnemy);
                            float dMid = (centroid - mid.ToVector3Shifted()).MagnitudeHorizontal();
                            float dEnemy = (centroid - nearestEnemy.Position.ToVector3Shifted()).MagnitudeHorizontal();
                            smokeBetweenPassed = dMid <= dEnemy + 2f;
                        }
                    }
                }
                return true;
            }
            s.Do("four T4 Combatdogs clustered inside 15 cells, a staged hostile group, colonists drafted but present", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    pad = SurveyFindRect(map, 42, 25, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    CleanTearGas(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 5, 0, pad.minZ + 10);
                    dogs[0] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at).ThingID;
                    dogs[1] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at + new IntVec3(0, 0, 3)).ThingID;
                    dogs[2] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at + new IntVec3(3, 0, 1)).ThingID;
                    dogs[3] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at + new IntVec3(3, 0, 4)).ThingID;
                    for (int i = 0; i < enemies.Length; i++)
                        enemies[i] = ArmedPackEnemy(s, at + new IntVec3(18 + i, 0, (i % 2) * 5)).ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("all four T4s carry both supplemental mounts", () =>
                    dogs.All(id => Scenario.ById(id) is Pawn dog
                        && dog.equipment.AllEquipmentListForReading.Any(w => w.def == QR_DefOf.QR_Gun_SmokeLauncher)
                        && dog.equipment.AllEquipmentListForReading.Any(w => w.def == QR_DefOf.QR_Gun_TearGasLauncher)))
                .Expect("a tear gas cloud blooms", () => Watch() && gasSpread)
                .Expect("every T4 fires the tear gas launcher (no cap), over the fight", () =>
                    Watch() && dogs.All(id => tearByWindow.Values.Any(set => set.Contains(id))))
                .SaveAndReload()
                .Expect("no 900-tick interval pops more than 3 clustered smoke launchers", () =>
                    Watch() && smokeByWindow.Values.All(set => set.Count <= 3))
                .Expect("exactly three of the four clustered T4s may pop per interval; the fourth waits", () =>
                    dogs.Select(id => Scenario.ById(id) as Pawn).Where(p => p != null)
                        .Count(dog => SmokeCoordinator.CanPop(dog)) == 3)
                .Expect("at least three distinct T4s fire smoke during the fight", () =>
                    dogs.Count(id => smokeByWindow.Values.Any(set => set.Contains(id))) >= 3)
                .Expect("smoke shells land on the approach path between the dogs and the enemy", () =>
                    Watch() && smokeBetweenChecked && smokeBetweenPassed)
                .Expect("the hostile group is defeated", () => enemies.All(id => !Standing(id)))
                .Summary(() => $"smoke pops per window: {string.Join("/", smokeByWindow.OrderBy(kv => kv.Key).Select(kv => kv.Value.Count))}; "
                    + $"tear-gas launchers seen: {tearByWindow.Values.SelectMany(set => set).Distinct().Count()}; "
                    + $"smoke between passed={smokeBetweenPassed}")
                .Start();
        }
    }
}
