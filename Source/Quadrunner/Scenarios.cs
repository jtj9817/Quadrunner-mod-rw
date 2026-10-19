using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>Every scenario, in Run all order. Each setup builds what it needs on the current (quicktest) map.</summary>
    public static class Scenarios
    {
        public static readonly List<(string name, Action setUp)> All = new List<(string, Action)>
        {
            ("Smoke", Smoke),
            ("Save and reload", SaveAndReload),
        };

        /// <summary>
        /// Runs the scenarios whose names contain any comma-separated part of <paramref name="filter"/>
        /// (case-insensitive; empty = all), then calls <paramref name="done"/> with the PASS count and verdicts.
        /// </summary>
        public static void RunAll(string filter, Action<int, List<string>> done)
        {
            string[] parts = (filter ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            Scenario.RunAll(All.Where(s => parts.Length == 0 ||
                parts.Any(p => s.name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)), done);
        }

        private static IntVec3 FreeCellNearCenter(Map map)
        {
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15,
                    c => c.Standable(map) && c.GetFirstItem(map) == null && !c.Fogged(map), out IntVec3 cell))
                throw new InvalidOperationException("no free standable cell near the map center");
            return cell;
        }

        private static Thing SpawnSteel(Scenario s, int count)
        {
            Map map = Find.CurrentMap;
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = count;
            return s.Track(GenSpawn.Spawn(steel, FreeCellNearCenter(map), map));
        }

        /// <summary>The harness itself: a scenario starts, ticks, checks a thing and cleans up.</summary>
        private static void Smoke()
        {
            var s = new Scenario("Smoke", 2500);
            string id = null;
            s.Do("spawn 75 steel", () => id = SpawnSteel(s, 75).ThingID)
                .Wait(300)
                .Expect("steel still there", () => Scenario.ById(id)?.stackCount == 75)
                .Start();
        }

        /// <summary>A save/load step partway through a scenario: the pass criterion every later scenario reuses.</summary>
        private static void SaveAndReload()
        {
            var s = new Scenario("Save and reload", 5000);
            string id = null;
            int savedAt = 0;
            s.Do("spawn 40 steel", () => id = SpawnSteel(s, 40).ThingID)
                .Wait(120)
                .Do("note the tick", () => savedAt = Find.TickManager.TicksGame)
                .SaveAndReload()
                .Expect("steel survived the reload", () => Scenario.ById(id)?.stackCount == 40)
                .Expect("the loaded game resumed at the saved tick", () => Find.TickManager.TicksGame >= savedAt)
                .Wait(300)
                .Summary(() => $"saved at tick {savedAt}")
                .Start();
        }
    }
}
