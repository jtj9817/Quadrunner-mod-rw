using System.Collections.Generic;
using System.Linq;
using LudeonTK;
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

        [DebugAction(Category, "Toggle scenario speed", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ToggleSpeed()
        {
            Scenario.fast = !Scenario.fast;
            QR.Log($"scenarios now run at {(Scenario.fast ? "Ultrafast" : "normal speed")}");
        }
    }
}
