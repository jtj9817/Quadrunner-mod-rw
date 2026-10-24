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

        /// <summary>
        /// A field worker's own stable is sealed off, then uninstalled, while a reachable alternate stable has a free
        /// slot. The wartime recall must physically dock it in the alternate, transferring the assignment, and both
        /// transfers must survive a reload. Sealing and uninstalling cover the two ways home can be unreachable.
        /// </summary>
        private static void WartimeAlternateStable()
        {
            var s = new Scenario("Wartime alternate stable", 45000);
            string homeId = null, alternateId = null, workerId = null, raiderId = null, miniId = null;
            var homeWalls = new List<string>();
            Building_Stable Home() => (Building_Stable)Scenario.ById(homeId);
            Building_Stable Alternate() => (Building_Stable)Scenario.ById(alternateId);
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            s.Do("a field worker, its home stable sealed, and a reachable alternate stable with a free slot", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable home = SpawnStable(s, powered: true);
                    homeId = home.ThingID;
                    // The field is reachable from home now, so reopening the sealed home later restores that route.
                    IntVec3 field = OpenCell(map, home.InteractionCell, 25f, 35f);
                    Building_Stable alternate = SpawnStable(s, powered: true, near: field,
                        minDist: 8f, maxDist: 14f);
                    alternateId = alternate.ThingID;
                    Pawn worker = s.Track(home.AddNewDog(QR_DefOf.QR_Quadrunner));
                    workerId = worker.ThingID;
                    if (!home.TryUndock(worker))
                        throw new InvalidOperationException("the worker didn't undock");
                    Teleport(worker, field);
                    worker.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                    IntVec3 raiderAt = OpenCell(map, home.InteractionCell, 60f, 80f,
                        c => new CellRect(c.x - 1, c.z - 1, 3, 3).Cells
                            .All(n => n.InBounds(map) && n.GetFirstBuilding(map) == null));
                    // Seal the footprint and its interaction cell: walls all around, no door.
                    CellRect footprint = home.OccupiedRect();
                    IntVec3 spot = home.InteractionCell;
                    int minX = Math.Min(footprint.minX, spot.x), minZ = Math.Min(footprint.minZ, spot.z);
                    int maxX = Math.Max(footprint.maxX, spot.x), maxZ = Math.Max(footprint.maxZ, spot.z);
                    CellRect sealedOff = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1).ExpandedBy(1);
                    foreach (IntVec3 cell in sealedOff.EdgeCells)
                        homeWalls.Add(SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, cell, Rot4.North).ThingID);
                    raiderId = SpawnContainedRaid(s, raiderAt).ThingID;
                    if (worker.CanReach(home, PathEndMode.InteractionCell, Danger.Deadly))
                        throw new InvalidOperationException("fixture: the home stable is still reachable");
                    if (!worker.CanReach(alternate, PathEndMode.InteractionCell, Danger.Deadly))
                        throw new InvalidOperationException("fixture: the alternate stable is not reachable");
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    if (!Net().War)
                        throw new InvalidOperationException("the contained hostile did not start a war");
                })
                .Wait(60)
                .Do("the wartime recall is under way to the reachable alternate stable", () =>
                {
                    Pawn worker = Worker();
                    if (worker != null && worker.CurJobDef == QR_DefOf.QR_Dock
                        && worker.CurJob.targetA.Thing == Alternate())
                        return;
                    if (worker != null && Alternate().Docked.Contains(worker))
                        return;
                    throw new InvalidOperationException($"wartime recall: {worker?.CurJob?.ToStringSafe() ?? "none"}, "
                        + "not the alternate stable");
                })
                .Expect("the worker physically docks in the alternate stable and its assignment transfers", () =>
                    Net().War && Alternate().Docked.Contains(Worker()) && !Worker().Spawned
                    && CompRobodog.Of(Worker())?.stable == Alternate() && !Home().Docked.Contains(Worker()))
                .SaveAndReload()
                .Wait(300)
                .Expect("after the reload it is still docked in and assigned to the alternate stable", () =>
                    Net().War && Alternate().Docked.Contains(Worker()) && !Worker().Spawned
                    && CompRobodog.Of(Worker())?.stable == Alternate())
                .Do("open the home stable and uninstall the alternate one, blocking its worker's home", () =>
                {
                    foreach (string wall in homeWalls)
                        Scenario.ById(wall)?.Destroy();
                    Building_Stable alternate = Alternate();
                    Map map = alternate.Map;
                    IntVec3 at = alternate.Position;
                    MinifiedThing mini = alternate.MakeMinified();
                    if (!GenPlace.TryPlaceThing(mini, at, map, ThingPlaceMode.Near))
                        throw new InvalidOperationException("couldn't place the minified alternate stable");
                    miniId = s.Track(mini).ThingID;
                    if (!Worker().Spawned)
                        throw new InvalidOperationException("the uninstalled stable didn't release its worker");
                    if (CompRobodog.Of(Worker()).stable != alternate || alternate.Spawned)
                        throw new InvalidOperationException("uninstalling changed the worker's affiliation");
                })
                .Expect("the worker docks in the reopened home stable and the assignment transfers again", () =>
                    Net().War && Home().Docked.Contains(Worker()) && !Worker().Spawned
                    && CompRobodog.Of(Worker())?.stable == Home())
                .SaveAndReload()
                .Expect("after the reload it is docked in and assigned to the reopened home stable", () =>
                    Net().War && Home().Docked.Contains(Worker()) && !Worker().Spawned
                    && CompRobodog.Of(Worker())?.stable == Home() && Scenario.ById(miniId) is MinifiedThing)
                .Do("the hostile event ends", () =>
                {
                    Scenario.ById(raiderId).Kill(null);
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(Find.CurrentMap);
                })
                .Expect("with the war over the worker redeploys from its reopened stable", () =>
                    !Net().War && Worker().Spawned)
                .Summary(() => $"war {Net().War}; job {Worker()?.CurJobDef}; home docked "
                    + $"{Home()?.Docked.Contains(Worker())}; alternate {Alternate()?.ThingID ?? "packed"}")
                .Start();
        }
    }
}
