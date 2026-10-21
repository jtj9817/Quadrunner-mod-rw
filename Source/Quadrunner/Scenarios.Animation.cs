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
    /// <summary>Action animations (<see cref="RobodogPose"/>): the Medirunner's flip and siren, the Combatdog's attacks.</summary>
    public static partial class Scenarios
    {
        /// <summary>Pauses once <paramref name="ready"/> holds, then saves a screenshot, as <see cref="CaptureArtwork"/>.</summary>
        private static void CaptureWhen(Scenario s, string name, Func<bool> ready, Func<Pawn> framed) =>
            CaptureWhen(s, name, ready, () => Find.CameraDriver.SetRootPosAndSize(framed().DrawPos, 4f));

        /// <summary>As above, with the camera placed by <paramref name="pose"/>.</summary>
        private static void CaptureWhen(Scenario s, string name, Func<bool> ready, Action pose)
        {
            s.Expect("pause for " + name, () =>
            {
                if (!ready())
                    return false;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                return true;
            });
            CaptureArtwork(s, name, pose);
            s.Do("resume", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal);
        }

        /// <summary>Larger captures and a closer zoom than the private instance's default, until the scenario ends.</summary>
        private static void CloseUpCaptures(Scenario s)
        {
            float minSize = Find.CameraDriver.config.sizeRange.min;
            int width = Screen.width, height = Screen.height;
            CloseUp();
            Screen.SetResolution(1280, 720, false);
            s.OnFinish(() =>
            {
                Find.CameraDriver.config.sizeRange.min = minSize;
                Screen.SetResolution(width, height, false);
            });
        }

        /// <summary>Lets the camera zoom in for <see cref="CloseUpCaptures"/>; again after a reload, which makes a new camera.</summary>
        private static void CloseUp() => Find.CameraDriver.config.sizeRange.min = 2f;

        /// <summary>The distance of <paramref name="label"/>'s drawn center from the pawn's draw position, toward <paramref name="target"/>.</summary>
        private static float DrawnToward(Pawn dog, string label, Thing target) =>
            Drawn(dog, label) is Matrix4x4 m
                ? Vector3.Dot((m.Position() - dog.DrawPos).Yto0(), (target.DrawPos - dog.DrawPos).Yto0().normalized) : 0f;

        /// <summary>
        /// A Medirunner sent to a downed colonist, at normal speed: its tail siren flashes on the run, it dips its head (ducks, head-on),
        /// flips the colonist up over it onto its back (the patient's drawn angle turns half a circle, then lies along the
        /// back) and takes it to bed. After a reload mid-carry the patient stays put, with no second flip.
        /// </summary>
        private static void RescueAnimation()
        {
            var s = new Scenario("Rescue animation", 8000);
            string medicId = null, patientId = null;
            int sirenFrames = 0, flipFrames = 0;
            float maxTurn = 0f, maxDip = 0f, maxDuck = 0f;
            bool profile = false;
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Patient() => (Pawn)Scenario.ById(patientId);
            PawnRenderNodeProperties_Pose Props() => RobodogPose.Props(Medic());
            float Rest() => Medic().Rotation == Rot4.East || Medic().Rotation == Rot4.South ? 90f : 270f;
            float Turn() => Mathf.DeltaAngle(Rest(), Patient().Drawer.renderer.BodyAngle(PawnRenderFlags.None));
            bool Carrying() => Medic().carryTracker.CarriedThing == Patient() && Patient() != null;
            float SincePickup() => RobodogPose.Since(CompRobodog.Of(Medic()).CarriedSince(Patient()));
            void Sample()
            {
                Pawn medic = Medic();
                // Off screen a pawn isn't pre-drawn and its draw requests keep the last on-screen matrices.
                Find.CameraDriver.JumpToCurrentMapLoc(medic.DrawPos);
                if (CompRobodog.Of(medic).Rescuing && CompRobodog.SirenPhaseLit)
                    sirenFrames++;
                if (!Carrying())
                    return;
                float sincePickup = SincePickup();
                if (sincePickup > Props().scoopTicks)
                    return;
                flipFrames++;
                maxTurn = Mathf.Max(maxTurn, Mathf.Abs(Turn()));
            }
            void SamplePose()
            {
                // Observe only settled screenshots, not a pre-pickup matrix left over from a moving frame.
                Pawn medic = Medic();
                if (Drawn(medic, "Body") is not Matrix4x4 body || (body.Position() - medic.DrawPos).Yto0().magnitude > 0.5f)
                    throw new InvalidOperationException("the Medirunner's body was not drawn in the captured pose");
                maxDuck = Mathf.Max(maxDuck, medic.DrawPos.z - body.Position().z);
                if (DrawParmsOf(medic.Drawer.renderer.renderTree).facing.IsHorizontal && SincePickup() <= Props().scoopTicks * 0.5f)
                {
                    profile = true;
                    maxDip = Mathf.Max(maxDip, Mathf.Abs(Yaw(body)));
                }
            }
            s.Do("a stable with a Medirunner and a medical bed; a colonist down 20–26 cells out", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    IntVec3 center = FindClearArea(map, map.Center, Math.Max(map.Size.x, map.Size.z), c => CellRect.CenteredOn(c, 9));
                    Building_Stable stable = SpawnStable(s, powered: true, near: center, maxDist: 3f);
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    SpawnMedicalBed(s, stable.InteractionCell, 3f, 8f);
                    Pawn colonist = map.mapPawns.FreeColonistsSpawned.First(p => !p.Downed);
                    Teleport(colonist, OpenCell(map, stable.InteractionCell, 20f, 26f));
                    DownBleeding(s, colonist, 1f);
                    patientId = colonist.ThingID;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    CloseUpCaptures(s);
                })
                .Expect("it runs out to the patient, tail siren flashing", () =>
                {
                    Sample();
                    return Rescuing(Medic()) && CompRobodog.Of(Medic()).Rescuing && Medic().pather.Moving && sirenFrames >= 3;
                });
            s.Expect("pause on a lit siren", () =>
            {
                if (!CompRobodog.SirenPhaseLit)
                    return false;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                return true;
            });
            CaptureArtwork(s, "art-rescue-siren", () => Find.CameraDriver.SetRootPosAndSize(Medic().DrawPos, 4f));
            s.Do("resume", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Normal)
                .Expect("it picks the patient up", () =>
                {
                    Sample();
                    return Carrying();
                });
            foreach (int tick in new[] { 4, 9, 16, 26, 36 })
            {
                CaptureWhen(s, $"art-rescue-flip-{tick}", () =>
                {
                    Sample();
                    return SincePickup() >= tick;
                }, Medic);
                s.Do("measure the captured pickup pose", SamplePose);
            }
            s.Expect("the flip ends with the patient lying along the back", () =>
                {
                    Sample();
                    return SincePickup() > Props().scoopTicks;
                })
                .Do("checked: head dipped, the patient turned half a circle and came to rest", () =>
                {
                    if (flipFrames < 8 || maxTurn < 150f || Mathf.Abs(Turn()) > 0.01f)
                        throw new InvalidOperationException($"flip over {flipFrames} frames: turned up to {maxTurn:F0}°, " +
                            $"now {Turn():F1}° from resting");
                    if (maxDuck < 0.03f || profile && maxDip < 5f)
                        throw new InvalidOperationException($"ducked {maxDuck:F3} cells, head dip {maxDip:F1}° (profile seen: {profile})");
                })
                .SaveAndReload()
                .Do("normal speed, close-ups again", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    CloseUp();
                })
                .Expect("still on its back after the reload, no second flip", () =>
                {
                    if (!Carrying())
                        throw new InvalidOperationException($"not carried after the reload: {Medic().carryTracker.CarriedThing.ToStringSafe()}");
                    if (Mathf.Abs(Turn()) > 0.01f || SincePickup() < Props().scoopTicks)
                        throw new InvalidOperationException($"flipping again: {Turn():F1}°, {SincePickup()} ticks since pickup");
                    return true;
                });
            CaptureWhen(s, "art-rescue-carry", () => Carrying() && Medic().pather.Moving, Medic);
            s.Expect("the patient in bed; the siren off", () => Patient().InBed() && !CompRobodog.Of(Medic()).Rescuing)
                .Summary(() => $"siren lit on {sirenFrames} frames; flip over {flipFrames} frames, patient turned up to " +
                    $"{maxTurn:F0}°, ducked {maxDuck:F3} cells, head dip {maxDip:F1}° (profile seen: {profile}); inspect .devlog/screens/art-rescue-*.png")
                .Start();
        }

        /// <summary>
        /// Drafted Combatdogs at normal speed, against a raider each: a Tier 4 shoots (crouched, legs braced, kicked back by
        /// every shot) and a Tier 1 bites (it pounces at the raider, nose down, front legs reaching). Checks the matrices
        /// the game drew each frame.
        /// </summary>
        private static void CombatAnimation()
        {
            var s = new Scenario("Combat animation", 8000);
            string gunnerId = null, biterId = null, farId = null, nearId = null;
            int shots = 0, strikes = 0, lastShot = -1, lastStrike = -1;
            float recoil = 0f, crouch = 0f, brace = 0f, pounce = 0f, bite = 0f;
            Pawn ById(string id) => (Pawn)Scenario.ById(id);
            void Order(Pawn dog, JobDef def, Pawn target)
            {
                dog.drafter.Drafted = true;
                Job job = JobMaker.MakeJob(def, target);
                job.playerForced = true;
                dog.jobs.StartJob(job, JobCondition.InterruptForced);
            }
            void Sample()
            {
                Pawn gunner = ById(gunnerId), biter = ById(biterId), far = ById(farId), near = ById(nearId);
                if (gunner?.Spawned == true && far != null && gunner.stances.curStance is Stance_Busy aim && aim.verb != null)
                {
                    if (aim is Stance_Cooldown && aim.startedTick != lastShot)
                    {
                        shots++;
                        lastShot = aim.startedTick;
                    }
                    recoil = Mathf.Min(recoil, DrawnToward(gunner, "Body", far));
                    if (Drawn(gunner, "Body") is Matrix4x4 body)
                        crouch = Mathf.Min(crouch, body.Position().z - gunner.DrawPos.z);
                    if (gunner.Rotation.IsHorizontal && Drawn(gunner, "Leg FrontNear") is Matrix4x4 leg)
                        brace = Mathf.Max(brace, Mathf.Abs(Yaw(leg)));
                }
                if (biter?.Spawned == true && near != null && biter.stances.curStance is Stance_Cooldown hit
                    && hit.verb is Verb_MeleeAttack)
                {
                    if (hit.startedTick != lastStrike)
                    {
                        strikes++;
                        lastStrike = hit.startedTick;
                    }
                    pounce = Mathf.Max(pounce, DrawnToward(biter, "Body", near));
                    if (biter.Rotation.IsHorizontal && Drawn(biter, "Body") is Matrix4x4 body)
                        bite = Mathf.Max(bite, Mathf.Abs(Yaw(body)));
                }
            }
            s.Do("a Tier 4 and a Tier 1 Combatdog out on clear ground; a raider 10 cells east of one, 1 cell east of the other", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    IntVec3 center = FindClearArea(map, map.Center, Math.Max(map.Size.x, map.Size.z), c => CellRect.CenteredOn(c, 9));
                    Building_Stable stable = SpawnStable(s, powered: true, near: center, maxDist: 3f);
                    Pawn gunner = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogFour));
                    Pawn biter = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    foreach (Pawn dog in new[] { gunner, biter })
                        if (!stable.TryUndock(dog))
                            throw new InvalidOperationException($"couldn't undock {dog}");
                    Tiers.Arm(gunner, Tiers.Gun(gunner, 0));
                    if (Held(gunner) == null)
                        throw new InvalidOperationException("the Tier 4 holds no gun");
                    gunnerId = gunner.ThingID;
                    biterId = biter.ThingID;
                    Teleport(gunner, center + new IntVec3(-5, 0, 3));
                    Teleport(biter, center + new IntVec3(-5, 0, -3));
                    farId = SpawnRaider(s, center + new IntVec3(5, 0, 3)).ThingID;
                    nearId = SpawnRaider(s, center + new IntVec3(-4, 0, -3)).ThingID;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 12f);
                    CloseUpCaptures(s);
                })
                .Do("drafted: one ordered to shoot, the other to bite", () =>
                {
                    Order(ById(gunnerId), JobDefOf.AttackStatic, ById(farId));
                    Order(ById(biterId), JobDefOf.AttackMelee, ById(nearId));
                });
            CaptureWhen(s, "art-combat-shot", () =>
            {
                Sample();
                return shots >= 3 && ById(gunnerId).stances.curStance is Stance_Cooldown c && RobodogPose.Since(c.startedTick) <= 1f;
            }, () => ById(gunnerId));
            CaptureWhen(s, "art-combat-pounce", () =>
            {
                Sample();
                return strikes >= 1 && ById(biterId).stances.curStance is Stance_Cooldown c && c.verb is Verb_MeleeAttack
                    && RobodogPose.Since(c.startedTick) >= 5f;
            }, () => ById(biterId));
            s.Expect("several shots and two strikes, sampled every frame", () =>
                {
                    Sample();
                    return shots >= 6 && strikes >= 2 || ById(farId).Downed && ById(nearId).Downed;
                })
                .Do("checked: crouched, braced and kicked back while shooting; pounced nose-down at each strike", () =>
                {
                    if (shots < 2 || recoil > -0.04f || crouch > -0.03f || brace < 5f)
                        throw new InvalidOperationException($"shooting: {shots} shots, kicked {recoil:F3} cells, " +
                            $"crouched {crouch:F3} cells, legs braced {brace:F1}°");
                    if (strikes < 1 || pounce < 0.15f || bite < 5f)
                        throw new InvalidOperationException($"melee: {strikes} strikes, lunged {pounce:F3} cells, nose down {bite:F1}°");
                })
                .Summary(() => $"{shots} shots: kicked back {-recoil:F3} cells, crouched {-crouch:F3}, legs braced {brace:F1}°; " +
                    $"{strikes} strikes: lunged {pounce:F3} cells, nose down {bite:F1}°; inspect .devlog/screens/art-combat-*.png")
                .Start();
        }
    }
}
