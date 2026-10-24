using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        // Enclose armed alert fixtures: their purpose is hostile presence and cooldown persistence, not shooting workers.
        private static Pawn SpawnContainedRaid(Scenario s, IntVec3 cell)
        {
            foreach (IntVec3 edge in new CellRect(cell.x - 1, cell.z - 1, 3, 3).EdgeCells)
                SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, edge, Rot4.North);
            Pawn raider = SpawnRaider(s, cell);
            EquipRaidCooldownWeapon(raider);
            return raider;
        }

        private static void WartimeRecall()
        {
            var s = new Scenario("Wartime return and dock", 30000);
            string stableId = null, firstId = null, blockedId = null, raiderId = null;
            var walls = new List<string>();
            IntVec3 held = IntVec3.Invalid;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn First() => (Pawn)Scenario.ById(firstId);
            Pawn Blocked() => (Pawn)Scenario.ById(blockedId);
            s.Do("two field workers, one enclosed; an enclosed distant hostile alerts the network", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn first = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    Pawn blocked = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    firstId = first.ThingID; blockedId = blocked.ThingID;
                    stable.TryUndock(first); stable.TryUndock(blocked);
                    Teleport(first, OpenCell(map, stable.InteractionCell, 18f, 24f));
                    held = OpenCell(map, stable.InteractionCell, 28f, 34f);
                    Teleport(blocked, held);
                    first.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    blocked.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    foreach (IntVec3 edge in new CellRect(held.x - 1, held.z - 1, 3, 3).EdgeCells)
                        walls.Add(SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, edge, Rot4.North).ThingID);
                    raiderId = SpawnContainedRaid(s, OpenCell(map, stable.InteractionCell, 60f, 80f)).ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    if (first.CurJobDef != QR_DefOf.QR_Dock || blocked.CurJobDef != JobDefOf.Wait)
                        throw new InvalidOperationException($"recall jobs: {first.CurJobDef}, blocked {blocked.CurJobDef}");
                })
                .Expect("reachable worker physically docks, blocked worker holds its exact field cell", () =>
                    Net().War && Stable().Docked.Contains(First()) && Blocked().Spawned
                    && Blocked().Position == held && Blocked().CurJobDef == JobDefOf.Wait)
                .SaveAndReload()
                .Wait(300)
                .Expect("reload keeps docked worker inside and blocked worker stationary", () =>
                    Net().War && Stable().Docked.Contains(First()) && Blocked().Position == held
                    && Blocked().CurJobDef == JobDefOf.Wait)
                .Do("open the blocked worker's route", () =>
                {
                    foreach (string wall in walls) Scenario.ById(wall)?.Destroy();
                })
                .Expect("restored route recalls the second worker while the threat remains", () =>
                    Net().War && Stable().Docked.Contains(First()) && Stable().Docked.Contains(Blocked()))
                .Do("the hostile event ends", () =>
                {
                    Scenario.ById(raiderId).Kill(null);
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(Find.CurrentMap);
                })
                .Expect("both workers redeploy after the threat clears", () =>
                    !Net().War && First().Spawned && Blocked().Spawned)
                .Summary(() => $"war {Net().War}; first {First()?.CurJobDef}; second {Blocked()?.CurJobDef}")
                .Start();
        }
    }
}
