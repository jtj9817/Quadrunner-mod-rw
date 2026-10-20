using System;
using System.Collections.Generic;
using System.IO;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        /// <summary>Capture real pawn, building and UI rendering; a PASS is not a substitute for inspecting the images.</summary>
        private static void ArtworkShowcase()
        {
            var s = new Scenario("Artwork showcase", 12000);
            string stableId = null;
            var dogIds = new List<string>();
            IntVec3 center = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog(int index) => (Pawn)Scenario.ById(dogIds[index]);
            s.Do("a stable and every dog model on clear ground", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    CellRect field = ClearRect(map, map.Center, 17, 1, 0f, 80f);
                    center = field.CenterCell;
                    Building_Stable stable = SpawnStable(s, powered: true, near: center, maxDist: 6f);
                    stableId = stable.ThingID;
                    foreach (PawnKindDef kind in new[] { QR_DefOf.QR_Quadrunner, QR_DefOf.QR_Medirunner,
                        QR_DefOf.QR_Combatdog, QR_DefOf.QR_CombatdogTwo, QR_DefOf.QR_CombatdogThree, QR_DefOf.QR_CombatdogFour })
                    {
                        Pawn dog = s.Track(stable.AddNewDog(kind));
                        dogIds.Add(dog.ThingID);
                        stable.TryUndock(dog);
                    }
                    stable.AddWaypoint(center + new IntVec3(4, 0, 4));
                })
                .SaveAndReload()
                .Do("pause and frame the model lineup", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    CameraMapConfig config = Find.CameraDriver.config;
                    float minSize = config.sizeRange.min;
                    int width = Screen.width, height = Screen.height;
                    config.sizeRange.min = 2f;
                    Screen.SetResolution(1280, 720, false);
                    s.OnFinish(() =>
                    {
                        config.sizeRange.min = minSize;
                        Find.Selector.ClearSelection();
                        Screen.SetResolution(width, height, false);
                    });
                    for (int i = 0; i < dogIds.Count; i++)
                    {
                        Pawn dog = Dog(i);
                        if (!dog.Spawned)
                            Stable().TryUndock(dog);
                        Teleport(dog, center + new IntVec3(-5 + i * 2, 0, -4));
                        Tiers.Arm(dog, Tiers.Gun(dog, 0));
                        dog.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    }
                    Find.Selector.ClearSelection();
                });
            foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                CaptureArtwork(s, "art-dogs-" + rot.ToStringHuman().ToLowerInvariant(), () =>
                {
                    for (int i = 0; i < dogIds.Count; i++)
                        Dog(i).Rotation = rot;
                    Find.CameraDriver.SetRootPosAndSize((center + new IntVec3(0, 0, -4)).ToVector3Shifted(), 4f);
                });
            }
            CaptureArtwork(s, "art-normal-zoom", () =>
                Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 11f));
            foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                CaptureArtwork(s, "art-stable-" + rot.ToStringHuman().ToLowerInvariant(), () =>
                {
                    Building_Stable stable = Stable();
                    IntVec3 pos = stable.Position;
                    stable.DeSpawn();
                    GenSpawn.Spawn(stable, pos, Find.CurrentMap, rot);
                    Find.CameraDriver.SetRootPosAndSize(stable.DrawPos, 3f);
                });
            }
            foreach (ThingDef gun in new[] { QR_DefOf.QR_Gun_AssaultRifle, QR_DefOf.QR_Gun_LMG,
                QR_DefOf.QR_Gun_Flamethrower, QR_DefOf.QR_Gun_Minigun })
            {
                CaptureArtwork(s, "art-mounted-" + gun.defName, () =>
                {
                    Pawn dog = Dog(4);
                    Tiers.Arm(dog, gun);
                    dog.Rotation = Rot4.East;
                    dog.drafter.Drafted = true;
                    dog.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_Combat), JobCondition.InterruptForced);
                    Find.CameraDriver.SetRootPosAndSize(dog.DrawPos, 2f);
                });
            }
            CaptureArtwork(s, "art-field-items", () =>
            {
                IntVec3 pos = center + new IntVec3(0, 0, -1);
                SpawnStack(s, QR_DefOf.QR_RobodogBattery, 1, pos + IntVec3.West);
                SpawnStack(s, QR_DefOf.QR_RobodogRepairKit, 1, pos + IntVec3.East);
                Find.CameraDriver.SetRootPosAndSize(pos.ToVector3Shifted(), 2f);
            });
            CaptureArtwork(s, "art-carry-battery", () =>
            {
                Pawn dog = Dog(0);
                dog.Rotation = Rot4.East;
                Thing battery = ThingMaker.MakeThing(QR_DefOf.QR_RobodogBattery);
                dog.carryTracker.TryStartCarry(battery);
                Find.CameraDriver.SetRootPosAndSize(dog.DrawPos, 2f);
            });
            CaptureArtwork(s, "art-stable-ui", () =>
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(Stable(), playSound: false);
                Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 5f);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = typeof(ITab_StableOverview);
            });
            Dialog_ModSettings settings = null;
            Window warning = null;
            s.OnFinish(() =>
            {
                warning?.Close(doCloseSound: false);
                settings?.Close(doCloseSound: false);
            });
            CaptureArtwork(s, "art-removal-settings", () =>
            {
                // Keep these already-paused visual fixtures open through the harness's blocking-window sweep.
                settings = new Dialog_ModSettings(LoadedModManager.GetMod<QuadrunnerMod>()) { forcePause = false };
                Find.WindowStack.Add(settings);
            });
            CaptureArtwork(s, "art-removal-warning", () =>
            {
                Removal.Confirm();
                warning = Find.WindowStack.Windows[Find.WindowStack.Windows.Count - 1];
                warning.forcePause = false;
            });
            s.Summary(() => "six dog models, four facings, four stable rotations, four mounted guns, items, carry and Overview; inspect .devlog/screens/art-*.png")
                .Start();
        }

        /// <summary>The runner advances once per rendered frame even while paused; capture only after the new scene drew.</summary>
        private static void CaptureArtwork(Scenario scenario, string name, Action pose)
        {
            int frames = 0;
            scenario.Do(name, pose).Expect(name + " captured", () =>
            {
                frames++;
                if (frames == 4 && AutoTest.Dir != null)
                {
                    string dir = Path.GetFullPath(Path.Combine(AutoTest.Dir, "..", "screens"));
                    Directory.CreateDirectory(dir);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                }
                return frames >= 8;
            });
        }
    }
}
