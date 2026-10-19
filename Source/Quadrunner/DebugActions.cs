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
        private static void SpawnDocked()
        {
            if (StableAt(UI.MouseCell()) is Building_Stable stable)
                stable.AddNewDog(QR_DefOf.QR_Quadrunner);
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

        [DebugAction(Category, "Toggle scenario speed", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleSpeed()
        {
            Scenario.fast = !Scenario.fast;
            QR.Log($"scenarios now run at {(Scenario.fast ? "Ultrafast" : "normal speed")}");
        }
    }
}
