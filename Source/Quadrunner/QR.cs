using System.Reflection;
using HarmonyLib;
using Verse;

namespace Quadrunner
{
    [StaticConstructorOnStartup]
    public static class QR
    {
        public const string PackageId = "josh9817.quadrunner";

        public static readonly string BuildId =
            typeof(QR).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        static QR()
        {
            // Always printed: tools/autotest.py and tools/status.sh compare it with .devlog/last-build.
            Verse.Log.Message($"[Quadrunner] loaded build {BuildId}");
            new Harmony(PackageId).PatchAll();
            AutoTest.Init();
        }

        /// <summary>Log line with the [Quadrunner] prefix; only in dev mode or an automated test run.</summary>
        public static void Log(string message)
        {
            if (Prefs.DevMode || AutoTest.Enabled)
                Verse.Log.Message($"[Quadrunner] {message}");
        }

        /// <summary>A red error. tools/autotest.py counts every "[Quadrunner] ERROR" line as a new error.</summary>
        public static void Error(string message) => Verse.Log.Error($"[Quadrunner] ERROR: {message}");
    }
}
