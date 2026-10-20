using System.Collections.Generic;
using System.Linq;
using LudeonTK;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>Debug actions → Quadrunner. Dev mode only; the same scenarios tools/autotest.py runs.</summary>
    public static class DebugActions
    {
        private const string Category = "Quadrunner";

        [DebugAction(Category, "Scenario: Run all", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void RunAll() => Scenarios.RunAll("", null);

        [DebugAction(Category, "Scenario...", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PickScenario()
        {
            List<FloatMenuOption> options = Scenarios.All
                .Select(s => new FloatMenuOption(s.name, () => s.setUp()))
                .ToList();
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private static Building_Stable StableAt(IntVec3 cell) =>
            cell.GetThingList(Find.CurrentMap).OfType<Building_Stable>().FirstOrDefault();

        [DebugAction(Category, "Spawn Quadrunner docked", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnDocked() => SpawnDocked(QR_DefOf.QR_Quadrunner);

        [DebugAction(Category, "Spawn Combatdog docked", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnCombatdogDocked() => SpawnDocked(QR_DefOf.QR_Combatdog);

        private static void SpawnDocked(PawnKindDef kind)
        {
            if (StableAt(UI.MouseCell()) is Building_Stable stable)
                stable.AddNewDog(kind);
            else
                Messages.Message("Click a Quadrunner stable.", MessageTypeDefOf.RejectInput, historical: false);
        }

        [DebugAction(Category, "Undock all", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void UndockAll()
        {
            if (StableAt(UI.MouseCell()) is Building_Stable stable)
                foreach (Pawn dog in stable.Docked.ToList())
                    stable.TryUndock(dog);
        }

        /// <summary>The dogs on the clicked cell, or docked in the stable there.</summary>
        private static List<Pawn> DogsAt(IntVec3 cell) =>
            StableAt(cell)?.Docked.ToList()
            ?? cell.GetThingList(Find.CurrentMap).OfType<Pawn>().Where(CompRobodog.IsRobodog).ToList();

        [DebugAction(Category, "Set battery %...", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SetBattery()
        {
            List<Pawn> dogs = DogsAt(UI.MouseCell());
            if (dogs.Count == 0)
                return;
            Find.WindowStack.Add(new FloatMenu(new[] { 0f, 0.05f, 0.1f, 0.5f, 1f }.Select(level =>
                new FloatMenuOption(level.ToStringPercent(), () =>
                {
                    foreach (Pawn dog in dogs)
                    {
                        CompRobodog.Of(dog).SetBattery(level);
                        if (dog.Spawned)
                            dog.jobs?.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);
                    }
                })).ToList()));
        }

        [DebugAction(Category, "Power down", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void PowerDown() => PowerDownAt(UI.MouseCell());

        /// <summary>The Power down action on <paramref name="cell"/>; scenarios call it too.</summary>
        internal static void PowerDownAt(IntVec3 cell)
        {
            foreach (Pawn dog in cell.GetThingList(Find.CurrentMap).OfType<Pawn>().Where(CompRobodog.IsRobodog).ToList())
                CompRobodog.Of(dog).SetBattery(0f);
        }

        [DebugAction(Category, "Force dispatch", actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceDispatch() => ForceDispatchAt(UI.MouseCell());

        /// <summary>The Force dispatch action on <paramref name="cell"/>; scenarios call it too.</summary>
        internal static void ForceDispatchAt(IntVec3 cell) => StableAt(cell)?.TryDispatch(force: true);

        [DebugAction(Category, "Highlight charge spots", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void HighlightSpots()
        {
            // Green: batteries on the spot's net hold energy; red: they're empty (the spot doesn't count now).
            foreach (ChargeSpot spot in ChargeSpots.All(Find.CurrentMap))
                Find.CurrentMap.debugDrawer.FlashCell(spot.cell, spot.StoredEnergy > 0f ? 0.5f : 0f,
                    $"{spot.StoredEnergy:F0}", 600);
        }

        [DebugAction(Category, "Dump charge-spot cache", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        internal static void DumpSpots()
        {
            List<ChargeSpot> spots = ChargeSpots.All(Find.CurrentMap);
            Verse.Log.Message($"[Quadrunner] {spots.Count} charge spots on {Find.CurrentMap}:\n" + string.Join("\n",
                spots.Select(s => $"  {s}, room {s.cell.GetRoom(Find.CurrentMap)?.ID} ({s.cell.GetRoom(Find.CurrentMap)?.Role?.label}), " +
                    $"stored {s.StoredEnergy:F0} Wd")));
        }

        [DebugAction(Category, "Toggle scenario speed", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleSpeed()
        {
            Scenario.fast = !Scenario.fast;
            QR.Log($"scenarios now run at {(Scenario.fast ? "Ultrafast" : "normal speed")}");
        }
    }
}
