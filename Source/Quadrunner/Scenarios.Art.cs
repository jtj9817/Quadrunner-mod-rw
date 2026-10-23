using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            var kinds = new[] { QR_DefOf.QR_Quadrunner, QR_DefOf.QR_Medirunner, QR_DefOf.QR_Combatdog,
                QR_DefOf.QR_CombatdogTwo, QR_DefOf.QR_CombatdogThree, QR_DefOf.QR_CombatdogFour };
            IntVec3 center = default;
            Dialog_InfoCard info = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog(int index) => (Pawn)Scenario.ById(dogIds[index]);
            void CloseWeaponMenu()
            {
                foreach (Window window in Find.WindowStack.Windows)
                    if (window is FloatMenu)
                    {
                        window.Close(doCloseSound: false);
                        break;
                    }
            }
            void OpenOverview()
            {
                CloseWeaponMenu();
                Find.Selector.ClearSelection();
                Find.Selector.Select(Stable(), playSound: false);
                Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 5f);
                Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = typeof(ITab_StableOverview);
            }
            s.OnFinish(() =>
            {
                info?.Close(doCloseSound: false);
                CloseWeaponMenu();
            });
            s.Do("a stable and every dog model on clear ground", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    // A gallery needs clear land, not the room helper's reachable doorway and off-center plot.
                    center = FindClearArea(map, map.Center, Math.Max(map.Size.x, map.Size.z),
                        c => CellRect.CenteredOn(c, 9));
                    Building_Stable stable = SpawnStable(s, powered: true, near: center, maxDist: 6f);
                    stableId = stable.ThingID;
                    foreach (PawnKindDef kind in kinds)
                    {
                        Pawn dog = s.Track(stable.AddNewDog(kind));
                        dog.Name = new NameSingle($"{dogIds.Count + 1}: {kind.LabelCap}");
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
            s.Do("fit the native information cards on screen", () => Screen.SetResolution(1280, 900, false));
            foreach (ThingDef def in new[] { QR_DefOf.QR_Stable, QR_DefOf.QR_Quadrunner.race, QR_DefOf.QR_Medirunner.race,
                QR_DefOf.QR_Combatdog.race, QR_DefOf.QR_CombatdogTwo.race, QR_DefOf.QR_CombatdogThree.race,
                QR_DefOf.QR_CombatdogFour.race, QR_DefOf.QR_RobodogBattery, QR_DefOf.QR_RobodogRepairKit,
                QR_DefOf.QR_Gun_AssaultRifle, QR_DefOf.QR_Gun_LMG, QR_DefOf.QR_Gun_Flamethrower, QR_DefOf.QR_Gun_Minigun,
                DefDatabase<ThingDef>.GetNamed("QR_Bullet_Flame") })
                CaptureArtwork(s, "art-lore-" + def.defName, () =>
                {
                    info?.Close(doCloseSound: false);
                    info = new Dialog_InfoCard(def) { forcePause = false };
                    Find.WindowStack.Add(info);
                });
            foreach (RecipeDef recipe in DefDatabase<RecipeDef>.AllDefs)
                if (recipe.defName.StartsWith("QR_", StringComparison.Ordinal))
                    CaptureArtwork(s, "art-lore-" + recipe.defName, () =>
                    {
                        info?.Close(doCloseSound: false);
                        info = new Dialog_InfoCard(recipe) { forcePause = false };
                        Find.WindowStack.Add(info);
                    });
            s.Do("close lore information cards", () => info?.Close(doCloseSound: false));
            s.Do("frame Overview", () => Screen.SetResolution(1280, 720, false));
            CaptureArtwork(s, "art-stable-ui", OpenOverview);
            // Real pointer input on the private display exercises the controls, not a duplicate tooltip renderer.
            CaptureArtwork(s, "art-lore-stable-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-battery-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-kit-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-dog-name-hover", OpenOverview, delaySeconds: 2f);
            for (int i = 0; i < kinds.Length; i++)
                CaptureArtwork(s, $"art-lore-dog-{i}-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-fitted-weapons-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-weapon-choice-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-minigun-choice-hover", OpenOverview, delaySeconds: 2f);
            CaptureArtwork(s, "art-lore-upgrade-hover", OpenOverview, delaySeconds: 2f);
            s.Do("close weapon choices", CloseWeaponMenu);
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
            s.Summary(() => "six models, facings, stable rotations, mounted guns, items, carry, lore cards and Overview/weapon hovers; inspect .devlog/screens/art-*.png")
                .Start();
        }

        private static void StableDockIndicators()
        {
            var s = new Scenario("Stable dock indicators", 12000);
            string stableId = null;
            var dogIds = new List<string>();
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog(int index) => (Pawn)Scenario.ById(dogIds[index]);
            void Frame(float size = 2.2f)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Find.CameraDriver.config.sizeRange.min = 2f;
                Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, size);
                Find.Selector.ClearSelection();
            }
            void Add(PawnKindDef kind)
            {
                Pawn dog = s.Track(Stable().AddNewDog(kind));
                dogIds.Add(dog.ThingID);
                // Remain docked through the save/load and power-switch ticks, without changing dispatch.
                CompRobodog.Of(dog).SetBattery(0.01f);
            }
            void Undock(int index)
            {
                if (!Stable().TryUndock(Dog(index)))
                    throw new InvalidOperationException("indicator fixture could not undock dog " + index);
                Teleport(Dog(index), Stable().Position + new IntVec3(6 + index, 0, 0));
            }
            s.Do("stage a powered stable", () =>
                {
                    DraftColonists(s);
                    stableId = SpawnStable(s, powered: true).ThingID;
                    int width = Screen.width, height = Screen.height;
                    float minSize = Find.CameraDriver.config.sizeRange.min;
                    Screen.SetResolution(1280, 720, false);
                    s.OnFinish(() =>
                    {
                        Find.CameraDriver.config.sizeRange.min = minSize;
                        Screen.SetResolution(width, height, false);
                    });
                })
                .Expect("stable power is ready", () => Stable().CanCharge);
            CaptureArtwork(s, "stable-docks-empty", () => Frame());
            CaptureArtwork(s, "stable-docks-worker", () => Add(QR_DefOf.QR_Quadrunner));
            CaptureArtwork(s, "stable-docks-worker-combat", () => Add(QR_DefOf.QR_CombatdogFour));
            CaptureArtwork(s, "stable-docks-full", () => Add(QR_DefOf.QR_Medirunner));
            s.SaveAndReload()
                .Expect("three types remain physically docked after reload", () =>
                    Stable().Docked.Count == 3 && dogIds.TrueForAll(id => Stable().Docked.Contains((Pawn)Scenario.ById(id))));
            CaptureArtwork(s, "stable-docks-reloaded", () => Frame());
            foreach (Rot4 rot in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                s.Do("rotate the stable " + rot, () =>
                {
                    Building_Stable stable = Stable();
                    IntVec3 pos = stable.Position;
                    stable.DeSpawn();
                    GenSpawn.Spawn(stable, pos, Find.CurrentMap, rot);
                    foreach (string id in dogIds)
                    {
                        Pawn dog = (Pawn)Scenario.ById(id);
                        if (!stable.TryDock(dog))
                            throw new InvalidOperationException("indicator fixture could not redock " + id);
                        CompRobodog.Of(dog).SetBattery(0.01f);
                    }
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                }).Wait(250).Expect("rotated stable reconnects to power", () => Stable().CanCharge);
                CaptureArtwork(s, "stable-docks-" + rot.ToStringHuman().ToLowerInvariant(), () => Frame());
            }
            CaptureArtwork(s, "stable-docks-normal-zoom", () => Frame(11f));
            CaptureArtwork(s, "stable-docks-two-workers", () => { Frame(); Add(QR_DefOf.QR_Quadrunner); });
            CaptureArtwork(s, "stable-docks-one-worker-left", () => Undock(0));
            CaptureArtwork(s, "stable-docks-workers-out", () => Undock(3));
            CaptureArtwork(s, "stable-docks-combat-out", () => Undock(1));
            CaptureArtwork(s, "stable-docks-all-out", () => Undock(2));
            s.Expect("assigned dogs outside do not occupy the dock", () =>
                    Stable().Docked.Count == 0 && Stable().Dogs().Count == 4)
                .Do("redock the dogs and switch power off", () =>
                {
                    foreach (string id in dogIds)
                        if (!Stable().TryDock((Pawn)Scenario.ById(id)))
                            throw new InvalidOperationException("indicator fixture could not redock " + id);
                    Stable().GetComp<CompFlickable>().SwitchIsOn = false;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("occupied stable has no charging power", () => !Stable().CanCharge);
            CaptureArtwork(s, "stable-docks-unpowered", () => Frame());
            s.Summary(() => "three type icons, Tier-4 combat, duplicate worker, undock/redock, power loss, reload and four rotations; inspect .devlog/screens/stable-docks-*.png")
                .Start();
        }

        /// <summary>The runner advances once per rendered frame even while paused; capture only after the new scene drew.</summary>
        private static void CaptureArtwork(Scenario scenario, string name, Action pose, float delaySeconds = 0f)
        {
            int frames = 0;
            float started = 0f;
            bool captured = false;
            scenario.Do(name, () =>
            {
                pose();
                started = Time.realtimeSinceStartup;
            }).Expect(name + " captured", () =>
            {
                frames++;
                if (!captured && frames >= 4 && Time.realtimeSinceStartup - started >= delaySeconds)
                {
                    captured = true;
                    frames = 0;
                    if (AutoTest.Dir != null)
                    {
                        string dir = Path.GetFullPath(Path.Combine(AutoTest.Dir, "..", "screens"));
                        Directory.CreateDirectory(dir);
                        ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
                    }
                }
                return captured && frames >= 4;
            });
        }
    }
}
