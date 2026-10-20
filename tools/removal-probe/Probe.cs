using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace QuadrunnerRemovalProbe
{
    // Test-only mod, built and enabled by autotest.py removal-check. No reference to Quadrunner.dll and nothing saved.
    [StaticConstructorOnStartup]
    public static class Boot
    {
        static Boot()
        {
            if (!GenCommandLine.TryGetCommandLineArg("quadrunner-removal-probe", out string dir)) return;
            var go = new GameObject("QuadrunnerRemovalProbe");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Probe>().Dir = dir;
        }
    }

    public class Probe : MonoBehaviour
    {
        public string Dir;
        private int stage, startTick, captureFrames;
        private float nextBeat;
        private Game before;
        private bool finished;
        private const string Resave = "QR_removed_resaved";

        private void Update()
        {
            if (finished || Dir == null) return;
            try
            {
                if (Time.realtimeSinceStartup >= nextBeat)
                {
                    nextBeat = Time.realtimeSinceStartup + 1f;
                    File.WriteAllText(Path.Combine(Dir, "heartbeat"), stage.ToString());
                }
                if (Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null
                    || LongEventHandler.ShouldWaitForEvent || !Current.Game.PlayerHasControl) return;
                foreach (Window window in Find.WindowStack.Windows.Where(w => w.forcePause).ToList()) window.Close(false);
                Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                if (stage == 0)
                {
                    CheckColony();
                    startTick = Find.TickManager.TicksGame;
                    stage = 1;
                    Log.Message("[QuadrunnerRemovalProbe] colony loaded without Quadrunner; observing 600 ticks");
                }
                else if (stage == 1 && Find.TickManager.TicksGame - startTick >= 600)
                {
                    CheckColony();
                    string path = GenFilePaths.FilePathForSavedGame(Resave);
                    if (File.Exists(path)) File.Delete(path);
                    GameDataSaveLoader.SaveGame(Resave);
                    if (!File.Exists(path)) throw new IOException("The no-mod resave was not written.");
                    before = Current.Game;
                    stage = 2;
                    GameDataSaveLoader.LoadGame(Resave);
                }
                else if (stage == 2 && Current.Game != before)
                {
                    CheckColony();
                    startTick = Find.TickManager.TicksGame;
                    stage = 3;
                }
                else if (stage == 3 && Find.TickManager.TicksGame - startTick >= 600)
                {
                    CheckColony();
                    Find.CameraDriver.SetRootPosAndSize(Find.CurrentMap.Center.ToVector3Shifted(), 16f);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    ScreenCapture.CaptureScreenshot(Path.Combine(Dir, "removed-colony.png"));
                    stage = 4;
                }
                else if (stage == 4 && ++captureFrames >= 10)
                {
                    Complete("PASS: loaded, ticked 600, saved, reloaded and ticked 600; expected vanilla contents retained; Quadrunner assembly absent");
                }
            }
            catch (Exception e)
            {
                Log.Error("[QuadrunnerRemovalProbe] " + e);
                Complete("FAIL: " + e);
            }
        }

        private void CheckColony()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Quadrunner")
                || LoadedModManager.RunningMods.Any(m => m.PackageId == "josh9817.quadrunner"))
                throw new InvalidOperationException("Quadrunner is still loaded; not a removal proof.");
            var ids = new HashSet<string>();
            var contents = new List<Thing>();
            foreach (Map map in Find.Maps)
            {
                ThingOwnerUtility.GetAllThingsRecursively(map, ThingRequest.ForGroup(ThingRequestGroup.Everything), contents);
                foreach (Thing thing in contents)
                {
                    if (thing.def.defName.StartsWith("QR_", StringComparison.Ordinal)
                        || thing.def.defName.StartsWith("Corpse_QR_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Quadrunner content remains: " + thing);
                    ids.Add(thing.ThingID);
                }
            }
            foreach (IThingHolder holder in Find.WorldObjects.AllWorldObjects.OfType<IThingHolder>())
            {
                ThingOwnerUtility.GetAllThingsRecursively(holder, contents);
                foreach (Thing thing in contents)
                {
                    if (thing.def.defName.StartsWith("QR_", StringComparison.Ordinal)
                        || thing.def.defName.StartsWith("Corpse_QR_", StringComparison.Ordinal))
                        throw new InvalidOperationException("Quadrunner world content remains: " + thing);
                    ids.Add(thing.ThingID);
                }
            }
            foreach (string id in File.ReadAllLines(Path.Combine(Dir, "expected.txt")))
                if (!ids.Contains(id)) throw new InvalidOperationException("Vanilla content was lost: " + id);
        }

        private void Complete(string text)
        {
            File.WriteAllText(Path.Combine(Dir, "result"), text + "\n");
            Log.Message("[QuadrunnerRemovalProbe] " + text);
            finished = true;
        }
    }
}
