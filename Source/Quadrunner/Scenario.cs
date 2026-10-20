using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// A dev-mode test run (ported from Queen Bee): the setup spawns things, then the steps are checked in order every
    /// frame until all pass (PASS) or the tick timeout hits (FAIL), and the game pauses. Static and never saved, so it
    /// leaves nothing in save files and survives a <see cref="SaveAndReload"/> step: things are tracked by ThingID and
    /// looked up again with <see cref="ById"/>, because references from before a load are stale.
    /// </summary>
    public class Scenario
    {
        public const string SaveName = "QR_autotest";

        /// <summary>Ultrafast when true (the default), otherwise normal speed.</summary>
        public static bool fast = true;

        private static Scenario current;

        /// <summary>A scenario is running (Run all included, between its scenarios).</summary>
        public static bool Running => current != null;

        // Run all: the scenarios still to start and each one's verdict so far.
        private static readonly Queue<(string name, Action setUp)> runAllQueue = new Queue<(string, Action)>();
        private static readonly List<string> runAllVerdicts = new List<string>();
        private static Action<int, List<string>> runAllDone;
        private static bool runningAll;

        private readonly string name;
        private readonly int timeoutTicks;
        private readonly List<(string label, Func<bool> done)> steps = new List<(string, Func<bool>)>();
        private readonly List<string> tracked = new List<string>();
        private readonly List<Action> cleanups = new List<Action>();
        private Func<string> summary;
        private int stepIndex;
        private int startTick;
        private float startRealtime;
        private int startFrame;
        private int stepStartTick;
        private bool prevNeverForceNormalSpeed;
        private bool prevStoryteller;

        public Scenario(string name, int timeoutTicks)
        {
            this.name = name;
            this.timeoutTicks = timeoutTicks;
        }

        private static int Now => Find.TickManager.TicksGame;

        /// <summary>The spawned thing with this ThingID on the current map, or a pawn held there (docked, carried), or null.</summary>
        public static Thing ById(string thingId) =>
            Find.CurrentMap?.listerThings.AllThings.FirstOrDefault(t => t.ThingID == thingId)
            ?? Find.CurrentMap?.mapPawns.AllPawns.FirstOrDefault(p => p.ThingID == thingId);

        /// <summary>Destroyed when the scenario ends, also after a reload.</summary>
        public T Track<T>(T thing) where T : Thing
        {
            tracked.Add(thing.ThingID);
            return thing;
        }

        /// <summary>Runs when the scenario ends, PASS or FAIL, after the tracked things are destroyed.</summary>
        public Scenario OnFinish(Action cleanup)
        {
            cleanups.Add(cleanup);
            return this;
        }

        /// <summary>Waits until <paramref name="done"/> returns true.</summary>
        public Scenario Expect(string label, Func<bool> done)
        {
            steps.Add((label, done));
            return this;
        }

        /// <summary>Runs <paramref name="action"/> once, then moves on in the same frame.</summary>
        public Scenario Do(string label, Action action) => Expect(label, () => { action(); return true; });

        public Scenario Wait(int ticks) => Expect($"wait {ticks} ticks", () => Now - stepStartTick >= ticks);

        /// <summary>Saves the game, loads that save, and continues once the loaded map is up.</summary>
        public Scenario SaveAndReload()
        {
            Game before = null;
            return Do("save and load", () =>
                {
                    before = Current.Game;
                    GameDataSaveLoader.SaveGame(SaveName);
                    GameDataSaveLoader.LoadGame(SaveName);
                })
                .Expect("loaded", () => Current.Game != before && AutoTest.MapReady)
                .Do("resume", Resume);
        }

        /// <summary>Appended to the PASS/FAIL line.</summary>
        public Scenario Summary(Func<string> summary)
        {
            this.summary = summary;
            return this;
        }

        /// <summary>Starts each scenario when the previous one ends, then calls <paramref name="done"/>.</summary>
        public static void RunAll(IEnumerable<(string name, Action setUp)> scenarios, Action<int, List<string>> done)
        {
            runningAll = false;
            runAllQueue.Clear();
            current?.Finish("aborted: Run all started");
            runAllVerdicts.Clear();
            foreach ((string, Action) s in scenarios)
                runAllQueue.Enqueue(s);
            runAllDone = done;
            runningAll = true;
            QR.Log($"SCENARIO Run all: start, {runAllQueue.Count} scenarios");
            StartNext();
        }

        /// <summary>Sets up the next queued scenario, skipping (as FAIL) any whose setup didn't start it.</summary>
        private static void StartNext()
        {
            while (runAllQueue.Count > 0)
            {
                (string name, Action setUp) = runAllQueue.Dequeue();
                try
                {
                    setUp();
                }
                catch (Exception e)
                {
                    QR.Error($"SCENARIO {name}: setup threw {e}");
                }
                if (current != null)
                    return;
                runAllVerdicts.Add($"{name} FAIL (didn't start)");
                AutoTest.Event("scenario", ("name", name), ("verdict", "FAIL"), ("detail", "didn't start"));
            }
            runningAll = false;
            int passed = runAllVerdicts.Count(v => v.EndsWith(" PASS"));
            QR.Log($"SCENARIO Run all: {passed}/{runAllVerdicts.Count} PASS: {string.Join(", ", runAllVerdicts)}");
            Action<int, List<string>> done = runAllDone;
            runAllDone = null;
            done?.Invoke(passed, new List<string>(runAllVerdicts));
        }

        public void Start()
        {
            if (current != null && runningAll)
            {
                // Started by hand in the middle of Run all (Run all itself starts each one after the last finished).
                runningAll = false;
                runAllQueue.Clear();
                QR.Log("SCENARIO Run all: cancelled, a scenario was started by hand");
            }
            current?.Finish("aborted: another scenario started");
            current = this;
            startTick = stepStartTick = Now;
            startRealtime = UnityEngine.Time.realtimeSinceStartup;
            startFrame = UnityEngine.Time.frameCount;
            // Hostiles make vanilla force normal speed; this dev setting turns that off for the run.
            prevNeverForceNormalSpeed = DebugViewSettings.neverForceNormalSpeed;
            DebugViewSettings.neverForceNormalSpeed = true;
            // No incidents during a run: scenarios stage their own events, and a random one (a short circuit, a quest
            // whose pawns get discarded) would make results depend on luck.
            prevStoryteller = DebugSettings.enableStoryteller;
            DebugSettings.enableStoryteller = false;
            RefreshColonists();
            Resume();
            QR.Log($"SCENARIO {name}: start ({(fast ? "Ultrafast" : "normal speed")}), {steps.Count} steps, timeout {timeoutTicks} ticks");
        }

        /// <summary>
        /// Every colonist starts a scenario fed, rested and unhurt: scenarios keep them drafted, so in a long Run all they
        /// would never eat or sleep, and malnutrition slowed later ones.
        /// </summary>
        private static void RefreshColonists()
        {
            foreach (Pawn p in Find.CurrentMap?.mapPawns.FreeColonistsSpawned ?? new List<Pawn>())
            {
                foreach (Need need in p.needs?.AllNeeds ?? new List<Need>())
                    need.CurLevelPercentage = 1f;
                foreach (Hediff h in p.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Malnutrition
                             || h.def == HediffDefOf.BloodLoss || h is Hediff_Injury injury && !injury.IsPermanent()).ToList())
                    p.health.RemoveHediff(h);
            }
        }

        private static void Resume() => Find.TickManager.CurTimeSpeed = fast ? TimeSpeed.Ultrafast : TimeSpeed.Normal;

        /// <summary>Called every frame by <see cref="AutoTest"/>; steps only run while a map is up.</summary>
        public static void Update()
        {
            if (current != null && AutoTest.MapReady)
                current.Check();
        }

        private void Check()
        {
            try
            {
                // Several steps can pass in the same frame; a load in progress stops the run until the map is back.
                while (stepIndex < steps.Count && AutoTest.MapReady && steps[stepIndex].done())
                {
                    QR.Log($"SCENARIO {name}: t={Now - startTick} step {stepIndex + 1}/{steps.Count} ok: {steps[stepIndex].label} (+{Now - stepStartTick}, {UnityEngine.Time.realtimeSinceStartup - startRealtime:F0} s)");
                    stepIndex++;
                    stepStartTick = Now;
                }
            }
            catch (Exception e)
            {
                Finish($"FAIL at step {stepIndex + 1}/{steps.Count} '{steps[stepIndex].label}': {e}");
                return;
            }
            if (!AutoTest.MapReady)
                return;
            if (stepIndex == steps.Count)
                Finish($"PASS in {Now - startTick} ticks");
            else if (Now - startTick >= timeoutTicks)
                Finish($"FAIL at step {stepIndex + 1}/{steps.Count} '{steps[stepIndex].label}' after {Now - startTick} ticks");
        }

        private void Finish(string verdict)
        {
            string extra = summary != null ? $"; {summary()}" : "";
            // Wall time, to compare test-instance speed (map size, load beside the user's game); includes reloads.
            float secs = UnityEngine.Time.realtimeSinceStartup - startRealtime;
            extra += $"; {secs:F0} s, {(Now - startTick) / Math.Max(secs, 0.001f):F0} ticks/s, " +
                $"{(UnityEngine.Time.frameCount - startFrame) / Math.Max(secs, 0.001f):F0} fps, tick {Find.TickManager.MeanTickTime:F1} ms, " +
                $"{Find.TickManager.CurTimeSpeed} x{Find.TickManager.TickRateMultiplier}";
            QR.Log($"SCENARIO {name}: {verdict}{extra}");
            foreach (string id in tracked)
                ById(id)?.Destroy();
            foreach (Action cleanup in cleanups)
            {
                try
                {
                    cleanup();
                }
                catch (Exception e)
                {
                    QR.Error($"SCENARIO {name}: cleanup failed: {e}");
                }
            }
            DebugViewSettings.neverForceNormalSpeed = prevNeverForceNormalSpeed;
            DebugSettings.enableStoryteller = prevStoryteller;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            current = null;
            bool pass = verdict.StartsWith("PASS");
            AutoTest.Event("scenario", ("name", name), ("verdict", pass ? "PASS" : "FAIL"), ("detail", verdict + extra));
            if (runningAll)
            {
                runAllVerdicts.Add($"{name} {(pass ? "PASS" : "FAIL")}");
                StartNext();
            }
        }
    }
}
