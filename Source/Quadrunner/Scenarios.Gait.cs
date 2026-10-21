using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> DrawRequestsOf =
            AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");
        private static readonly AccessTools.FieldRef<PawnRenderTree, PawnDrawParms> DrawParmsOf =
            AccessTools.FieldRefAccess<PawnRenderTree, PawnDrawParms>("oldParms");

        /// <summary>The matrix the game computed for <paramref name="label"/>'s layer this frame, or null when it isn't drawn.</summary>
        private static Matrix4x4? Drawn(Pawn dog, string label) => DrawRequestsOf(dog.Drawer.renderer.renderTree)
            .Where(r => r.node.Props.debugLabel == label).Select(r => (Matrix4x4?)r.preDrawnComputedMatrix).FirstOrDefault();

        private static float Yaw(Matrix4x4 m) => Mathf.DeltaAngle(0f, m.rotation.eulerAngles.y);

        /// <summary>
        /// A Quadrunner walks east (legs swing about the hips) and a Tier 4 Combatdog south (legs lift in turn), at
        /// normal speed. Checks the matrices the game drew each frame, and that the legs rest once the dogs stop.
        /// </summary>
        private static void WalkAnimation()
        {
            var s = new Scenario("Walk animation", 6000);
            string sideId = null, frontId = null;
            IntVec3 center = default;
            Pawn Side() => (Pawn)Scenario.ById(sideId);
            Pawn Front() => (Pawn)Scenario.ById(frontId);
            int frames = 0, movedFrames = 0, swungFrames = 0;
            float minSwing = 0f, maxSwing = 0f, maxLift = 0f;
            bool hindHeadOn = false;
            Vector3 lastPos = default;
            float lastSwing = 0f;
            void Walk(Pawn dog, IntVec3 to)
            {
                // Then stand there, not dock: the rest check needs the dogs stopped and on screen.
                dog.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, to));
                Job wait = JobMaker.MakeJob(JobDefOf.Wait, 5000);
                wait.playerForced = true;
                dog.jobs.jobQueue.EnqueueFirst(wait);
            }
            bool Arrived(Pawn dog, IntVec3 at) => dog.Position == at && !dog.pather.Moving;
            s.Do("a Quadrunner and a Tier 4 Combatdog out on clear ground", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    center = FindClearArea(map, map.Center, Math.Max(map.Size.x, map.Size.z), c => CellRect.CenteredOn(c, 9));
                    Building_Stable stable = SpawnStable(s, powered: true, near: center, maxDist: 3f);
                    foreach (PawnKindDef kind in new[] { QR_DefOf.QR_Quadrunner, QR_DefOf.QR_CombatdogFour })
                    {
                        Pawn dog = s.Track(stable.AddNewDog(kind));
                        if (!stable.TryUndock(dog))
                            throw new InvalidOperationException($"couldn't undock {dog}");
                        if (sideId == null) sideId = dog.ThingID;
                        else frontId = dog.ThingID;
                    }
                    Teleport(Side(), center + new IntVec3(-8, 0, -6));
                    Teleport(Front(), center + new IntVec3(6, 0, 8));
                })
                .Expect("layers: whole sprite for icons, separate body and leg textures", () =>
                {
                    Pawn dog = Side();
                    string whole = QR_DefOf.QR_Quadrunner.lifeStages[0].bodyGraphicData.texPath;
                    dog.Drawer.renderer.EnsureGraphicsInitialized();
                    if (dog.Drawer.renderer.BodyGraphic?.path != whole)
                        throw new InvalidOperationException($"icon graphic {dog.Drawer.renderer.BodyGraphic?.path}, expected {whole}");
                    foreach (PawnRenderNode node in dog.Drawer.renderer.renderTree.rootNode.children)
                        if (node.Props is PawnRenderNodeProperties_Gait gait)
                        {
                            Graphic g = node.PrimaryGraphic;
                            if (g?.path != whole + "_" + gait.part || g.MatEast.mainTexture == BaseContent.BadTex)
                                throw new InvalidOperationException($"{node.Props.debugLabel}: graphic {g?.path}");
                        }
                    return true;
                })
                .Do("both walk 16 cells at normal speed", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 20f);
                    Walk(Side(), center + new IntVec3(8, 0, -6));
                    Walk(Front(), center + new IntVec3(6, 0, -8));
                })
                .Expect("both arrive; each frame's drawn legs recorded", () =>
                {
                    Pawn side = Side(), front = Front();
                    if (side.pather.Moving && side.Rotation == Rot4.East && Drawn(side, "Leg FrontNear") is Matrix4x4 leg)
                    {
                        float swing = Yaw(leg);
                        frames++;
                        if (frames > 1 && (side.DrawPos - lastPos).MagnitudeHorizontal() > 0.001f)
                        {
                            movedFrames++;
                            if (Mathf.Abs(swing - lastSwing) > 0.01f) swungFrames++;
                        }
                        minSwing = Mathf.Min(minSwing, swing);
                        maxSwing = Mathf.Max(maxSwing, swing);
                        lastPos = side.DrawPos;
                        lastSwing = swing;
                    }
                    if (front.pather.Moving && front.Rotation == Rot4.South
                        && Drawn(front, "Leg FrontNear") is Matrix4x4 near && Drawn(front, "Leg FrontFar") is Matrix4x4 far)
                    {
                        maxLift = Mathf.Max(maxLift, Mathf.Abs(near.Position().z - far.Position().z));
                        hindHeadOn |= Drawn(front, "Leg HindNear") != null;
                    }
                    return Arrived(side, center + new IntVec3(8, 0, -6)) && Arrived(front, center + new IntVec3(6, 0, -8));
                })
                .Wait(10)
                .Do("legs swung and lifted while walking, and rest once stopped", () =>
                {
                    if (frames < 20 || maxSwing - minSwing < 30f || swungFrames < movedFrames * 0.8f)
                        throw new InvalidOperationException($"side swing over {frames} frames: {minSwing:F1}° to {maxSwing:F1}°, " +
                            $"changed on {swungFrames} of {movedFrames} frames that moved");
                    if (maxLift < 0.05f || hindHeadOn)
                        throw new InvalidOperationException($"head-on: lift {maxLift:F3} cells, hind legs drawn {hindHeadOn}");
                    foreach (Pawn dog in new[] { Side(), Front() })
                        foreach (string label in new[] { "Leg FrontNear", "Leg FrontFar" })
                        {
                            Matrix4x4 m = Drawn(dog, label) ?? throw new InvalidOperationException($"{dog} {label} not drawn");
                            Matrix4x4 body = Drawn(dog, "Body") ?? throw new InvalidOperationException($"{dog} body not drawn");
                            if (Mathf.Abs(Yaw(m)) > 0.01f || Mathf.Abs(m.Position().z - body.Position().z) > 0.001f)
                                throw new InvalidOperationException($"{dog} {label} not at rest: {Yaw(m):F2}°, " +
                                    $"z {m.Position().z - body.Position().z:F4}");
                        }
                });
            // Paused mid-stride stills to inspect: the Quadrunner walking back west, the Combatdog north.
            s.Do("walk back", () =>
            {
                Walk(Side(), center + new IntVec3(-8, 0, -6));
                Walk(Front(), center + new IntVec3(6, 0, 8));
                CameraMapConfig config = Find.CameraDriver.config;
                float minSize = config.sizeRange.min;
                int width = Screen.width, height = Screen.height;
                config.sizeRange.min = 2f;
                Screen.SetResolution(1280, 720, false);
                s.OnFinish(() =>
                {
                    config.sizeRange.min = minSize;
                    Screen.SetResolution(width, height, false);
                });
            });
            for (int i = 0; i < 3; i++)
            {
                int step = i;
                s.Do("advance", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal).Wait(5 + step * 4);
                CaptureArtwork(s, $"art-walk-{step}", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.CameraDriver.SetRootPosAndSize((Side().DrawPos + Front().DrawPos) / 2f, 6f);
                });
            }
            s.Summary(() => $"side swing {minSwing:F1}° to {maxSwing:F1}° over {frames} frames ({swungFrames}/{movedFrames} " +
                    $"moving frames changed), head-on lift {maxLift:F3} cells; inspect .devlog/screens/art-walk-*.png")
                .Start();
        }
    }
}
