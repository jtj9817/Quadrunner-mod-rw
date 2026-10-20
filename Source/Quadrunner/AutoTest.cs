using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// The game side of tools/autotest.py. Active only when the game was launched with
    /// <c>-quadrunner-autotest=&lt;dir&gt;</c>: it touches <c>&lt;dir&gt;/heartbeat</c> every second, announces
    /// <c>ready</c> once the quicktest map is up, and runs one command at a time from <c>&lt;dir&gt;/command</c>
    /// (<c>&lt;id&gt; &lt;verb&gt; [arg]</c>), answering in <c>&lt;dir&gt;/events.jsonl</c>.
    /// A MonoBehaviour rather than a GameComponent, so it survives save/load and never ends up in a save file.
    /// It also drives <see cref="Scenario"/> every frame, with or without the launch argument.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        private const string Arg = "quadrunner-autotest";
        internal const string MapSizeArg = "quadrunner-mapsize";

        private static string dir;
        private static readonly int Pid = Process.GetCurrentProcess().Id;

        /// <summary>One id per process: an in-game restart starts a new process, so a new launch.</summary>
        public static readonly string LaunchId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Pid}";

        private bool announced;
        private const int IdleFrameRate = 5;
        private float nextBeat, nextPoll;
        private int fullFrameRate;

        public static bool Enabled => dir != null;

        /// <summary>The harness directory (<c>.devlog/autotest</c>), or null outside an automated run.</summary>
        public static string Dir => dir;

        public static bool MapReady =>
            Current.ProgramState == ProgramState.Playing && Find.CurrentMap != null && !LongEventHandler.ShouldWaitForEvent;

        public static void Init()
        {
            var go = new GameObject("Quadrunner.AutoTest");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoTest>();
            if (!GenCommandLine.TryGetCommandLineArg(Arg, out string value))
                return;
            dir = value;
            Directory.CreateDirectory(dir);
            QR.Log($"autotest: launch {LaunchId}, control dir {dir}");
            Event("launched", ("build", QR.BuildId));
        }

        private void Update()
        {
            Scenario.Update();
            if (dir == null)
                return;
            // Software rendering costs ~370% CPU even while idle: render 5 frames a second between scenarios.
            if (fullFrameRate == 0)
                fullFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = Scenario.Running ? fullFrameRate : IdleFrameRate;
            float now = Time.realtimeSinceStartup;
            if (now >= nextBeat)
            {
                nextBeat = now + 1f;
                File.WriteAllText(Path.Combine(dir, "heartbeat"), LaunchId + "\n");
            }
            if (!MapReady)
                return;
            if (!announced)
            {
                announced = true;
                Event("ready", ("build", QR.BuildId), ("game", VersionControl.CurrentVersionStringWithRev),
                    ("map", Find.CurrentMap.Size.ToString()),
                    ("modRoot", LoadedModManager.GetMod<QuadrunnerMod>().Content.RootDir));
            }
            CloseBlockingWindows();
            if (now >= nextPoll)
            {
                nextPoll = now + 0.5f;
                Poll();
            }
        }

        /// <summary>Nobody clicks in a test run: a window that pauses the game would stall every scenario.</summary>
        private static void CloseBlockingWindows()
        {
            foreach (Window w in Find.WindowStack.Windows.Where(w => w.forcePause).ToList())
            {
                QR.Log($"autotest: closing blocking window {w.GetType().FullName}");
                Event("closed_window", ("type", w.GetType().FullName));
                w.Close(false);
            }
        }

        private static void Poll()
        {
            string path = Path.Combine(dir, "command");
            if (!File.Exists(path))
                return;
            string text = File.ReadAllText(path).Trim();
            File.Delete(path);
            string[] parts = text.Split(new[] { ' ' }, 3);
            string id = parts[0];
            string verb = parts.Length > 1 ? parts[1] : "";
            string arg = parts.Length > 2 ? parts[2] : "";
            QR.Log($"autotest: command {text}");
            switch (verb)
            {
                case "run":
                    Scenarios.RunAll(arg, (passed, verdicts) =>
                        Event("result", ("id", id), ("passed", passed), ("total", verdicts.Count), ("verdicts", verdicts)));
                    break;
                case "restart":
                    // The same restart the Options dialog does after toggling texture compression:
                    // relaunch with the same command line, then shut this process down.
                    Event("restarting", ("id", id));
                    GenCommandLine.Restart();
                    break;
                case "quit":
                    Event("quitting", ("id", id));
                    Root.Shutdown();
                    break;
                case "hang":
                    Event("hanging", ("id", id));
                    Freeze();
                    break;
                case "ping":
                    Event("pong", ("id", id));
                    break;
                default:
                    QR.Error($"autotest: unknown command '{text}'");
                    Event("unknown", ("id", id), ("command", text));
                    break;
            }
        }

        /// <summary>Watchdog self-test (autotest.py hang): blocks the main thread for good.</summary>
        private static void Freeze()
        {
            while (true)
                Thread.Sleep(1000);
        }

        /// <summary>Appends one JSON line to events.jsonl. No-op without the launch argument.</summary>
        public static void Event(string ev, params (string key, object value)[] fields)
        {
            if (dir == null)
                return;
            var sb = new StringBuilder();
            sb.Append("{\"ev\":").Append(Json(ev)).Append(",\"launch\":").Append(Json(LaunchId)).Append(",\"pid\":").Append(Pid);
            foreach ((string key, object value) in fields)
                sb.Append(',').Append(Json(key)).Append(':').Append(Json(value));
            sb.Append("}\n");
            File.AppendAllText(Path.Combine(dir, "events.jsonl"), sb.ToString());
        }

        private static string Json(object value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case bool b:
                    return b ? "true" : "false";
                case int or long or float or double:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
                case string s:
                    return Quote(s);
                case IEnumerable list:
                    return "[" + string.Join(",", list.Cast<object>().Select(Json)) + "]";
                default:
                    return Quote(value.ToString());
            }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    /// <summary>
    /// Test instance only: <c>-quadrunner-mapsize=N</c> sets the quicktest map's size, which vanilla fixes at 250
    /// (<c>Root_Play.SetupForQuickTestPlay</c>). A smaller map costs less per tick, so scenarios run faster.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    public static class Patch_Root_Play_SetupForQuickTestPlay
    {
        public static void Postfix()
        {
            if (AutoTest.Enabled && GenCommandLine.TryGetCommandLineArg(AutoTest.MapSizeArg, out string value)
                && int.TryParse(value, out int size) && size >= 50)
                Find.GameInitData.mapSize = size;
        }
    }
}
