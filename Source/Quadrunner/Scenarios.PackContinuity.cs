using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        private static Action<Pawn, Job> packJobObserver;

        // Scenario-only observation of actual native job transitions, including ones between rendered frames.
        private static void PackJobStarting(Pawn ___pawn, Job newJob) => packJobObserver?.Invoke(___pawn, newJob);

        private static void ObservePackJobs(Scenario scenario, Action<Pawn, Job> observe)
        {
            var harmony = new Harmony("josh9817.quadrunner.pack-continuity-scenario");
            var start = AccessTools.Method(typeof(Pawn_JobTracker), "StartJob");
            packJobObserver = observe;
            harmony.Patch(start, prefix: new HarmonyMethod(AccessTools.Method(typeof(Scenarios), nameof(PackJobStarting))));
            scenario.OnFinish(() =>
            {
                packJobObserver = null;
                harmony.Unpatch(start, HarmonyPatchType.Prefix, harmony.Id);
            });
        }

        private static void PackTargetHandoff()
        {
            var s = new Scenario("Pack target handoff", 20000);
            string stableId = null;
            string[] dogs = new string[2], enemies = new string[3];
            int fellAt = -1;
            string wrongReturn = null;
            var transitions = new List<string>();
            Pawn D(int i) => Scenario.ById(dogs[i]) as Pawn;
            Pawn E(int i) => Scenario.ById(enemies[i]) as Pawn;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            void CheckReturn()
            {
                if (wrongReturn != null) throw new InvalidOperationException(wrongReturn);
            }
            s.Do("a mixed pack with two queued enemies beyond its first engagement", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    CellRect pad = SurveyFindRect(map, 72, 27, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 5, 0, pad.minZ + 12);
                    dogs[0] = PackDog(s, stable, QR_DefOf.QR_CombatdogThree, at).ThingID;
                    dogs[1] = PackDog(s, stable, QR_DefOf.QR_CombatdogTwo, at + new IntVec3(0, 0, 3)).ThingID;
                    enemies[0] = SpawnRaider(s, at + new IntVec3(12, 0, 0)).ThingID;
                    enemies[1] = SpawnRaider(s, at + new IntVec3(40, 0, 0)).ThingID;
                    enemies[2] = SpawnRaider(s, at + new IntVec3(58, 0, 0)).ThingID;
                    ObservePackJobs(s, (dog, job) =>
                    {
                        if (fellAt < 0 || dog.ThingID != dogs[0] && dog.ThingID != dogs[1]) return;
                        int delay = Find.TickManager.TicksGame - fellAt;
                        transitions.Add($"+{delay} {dog.ThingID}: {job}");
                        if (Standing(enemies[1]) || Standing(enemies[2]))
                            if (job.def == QR_DefOf.QR_Dock || job.def == JobDefOf.Goto && !job.targetA.HasThing
                                && !(job.targetB.Thing is Pawn)
                                && job.targetA.Cell.InHorDistOf(Stable().InteractionCell, 4f))
                                wrongReturn = $"returned toward stable with queued standing enemies at +{delay} ticks: {dog} {job}";
                    });
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both dogs begin on the first enemy", () => PackAttacks(D(0), E(0)) && PackAttacks(D(1), E(0)))
                .Do("first enemy dies immediately after a scheduler refresh", () =>
                {
                    Net().Tick(force: true);
                    fellAt = Find.TickManager.TicksGame;
                    Pawn enemy = E(0);
                    enemy.Kill(null);
                    s.Track(enemy.Corpse ?? throw new InvalidOperationException("first target left no corpse"));
                })
                .Wait(3)
                .Do("no native job turns home between target death and the next scheduler tick", CheckReturn)
                .SaveAndReload()
                .Expect("the pack reaches and defeats the remaining distant enemies without returning home", () =>
                {
                    CheckReturn();
                    return !Standing(enemies[1]) && !Standing(enemies[2]);
                })
                .Summary(() => $"native transitions: {string.Join("; ", transitions)}")
                .Start();
        }

        private static void PackNearestMember()
        {
            var s = new Scenario("Pack nearest member", 16000);
            string[] dogs = new string[2], enemies = new string[3];
            Pawn D(int i) => Scenario.ById(dogs[i]) as Pawn;
            Pawn E(int i) => Scenario.ById(enemies[i]) as Pawn;
            s.Do("a T2 follower sees a closer enemy than its T4 leader does", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    CellRect pad = SurveyFindRect(map, 52, 32, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 27, 0, pad.minZ + 12);
                    dogs[0] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at).ThingID;
                    dogs[1] = PackDog(s, stable, QR_DefOf.QR_CombatdogTwo, at + new IntVec3(-14, 0, 0)).ThingID;
                    enemies[0] = SpawnRaider(s, at + new IntVec3(10, 0, 0)).ThingID;
                    enemies[1] = SpawnRaider(s, at + new IntVec3(-14, 0, 5)).ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    if (!PackAttacks(D(0), E(1)) || !PackAttacks(D(1), E(1)))
                        throw new InvalidOperationException($"pack ignored the nearest follower's enemy: leader {D(0).CurJob}, follower {D(1).CurJob}");
                })
                .Do("an even closer newcomer must not restart an already valid focus", () =>
                {
                    int leaderJob = D(0).CurJob.loadID, followerJob = D(1).CurJob.loadID;
                    enemies[2] = SpawnRaider(s, D(0).Position + new IntVec3(2, 0, -4)).ThingID;
                    Net().Tick(force: true);
                    if (!PackAttacks(D(0), E(1)) || !PackAttacks(D(1), E(1))
                        || D(0).CurJob.loadID != leaderJob || D(1).CurJob.loadID != followerJob)
                        throw new InvalidOperationException("new discovery thrashed the valid pack focus or restarted its firing job");
                })
                .Expect("all three threats fall to the coordinated pack", () => enemies.All(id => !Standing(id)))
                .Summary(() => $"leader {D(0)?.CurJob}; follower {D(1)?.CurJob}")
                .Start();
        }

        private static bool PackPursues(Pawn dog, Pawn enemy) => PackAttacks(dog, enemy)
            || dog?.CurJobDef == JobDefOf.Goto && dog.CurJob.targetB.Thing == enemy;

        private static void PackPursuitHandoff()
        {
            var s = new Scenario("Pack pursuit handoff", 20000);
            string[] dogs = new string[2], enemies = new string[3];
            string stableId = null;
            int[] oldJobs = new int[2];
            int retargetAt = 0;
            Pawn D(int i) => Scenario.ById(dogs[i]) as Pawn;
            Pawn E(int i) => Scenario.ById(enemies[i]) as Pawn;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            s.Do("T3 and T4 pursue a distant enemy ahead of two more targets", () =>
                {
                    DraftColonists(s);
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    RoomWithBeds(s, ClearRect(map, stable.InteractionCell, 9, 1, 12f, 25f), 3, prisoners: true);
                    CellRect pad = SurveyFindRect(map, 88, 28, 90f, s);
                    foreach (IntVec3 cell in pad) map.fogGrid.Unfog(cell);
                    PackCleanup(s, pad);
                    IntVec3 at = new IntVec3(pad.minX + 5, 0, pad.minZ + 12);
                    dogs[0] = PackDog(s, stable, QR_DefOf.QR_CombatdogThree, at).ThingID;
                    dogs[1] = PackDog(s, stable, QR_DefOf.QR_CombatdogFour, at + new IntVec3(0, 0, 3)).ThingID;
                    enemies[0] = SpawnRaider(s, at + new IntVec3(48, 0, 0)).ThingID;
                    enemies[1] = SpawnRaider(s, at + new IntVec3(63, 0, 0)).ThingID;
                    enemies[2] = SpawnRaider(s, at + new IntVec3(74, 0, 0)).ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                })
                .Expect("both dogs walk into firing range", () => D(0).CurJobDef == JobDefOf.Goto
                    && D(1).CurJobDef == JobDefOf.Goto && PackPursues(D(0), E(0)) && PackPursues(D(1), E(0)))
                .SaveAndReload()
                .Do("down two queued targets mid-pursuit and refresh without ending the dogs' jobs", () =>
                {
                    oldJobs[0] = D(0).CurJob.loadID;
                    oldJobs[1] = D(1).CurJob.loadID;
                    DownBleeding(s, E(0), 1f);
                    DownBleeding(s, E(1), 1f);
                    retargetAt = Find.TickManager.TicksGame;
                    Net().Tick(force: true);
                    for (int i = 0; i < 2; i++)
                        if (!PackPursues(D(i), E(2)) || D(i).CurJob.loadID == oldJobs[i])
                            throw new InvalidOperationException($"leader {i} kept a stale pursuit or withdrew to capture: {D(i).CurJob}");
                    if (Find.TickManager.TicksGame != retargetAt)
                        throw new InvalidOperationException("retargeting unexpectedly advanced the simulation");
                })
                .Expect("the leaders defeat the last standing enemy before taking capture jobs", () =>
                {
                    if (Standing(enemies[2]) && (D(0).CurJobDef == JobDefOf.Capture || D(0).CurJobDef == QR_DefOf.QR_CarryToRoom
                        || D(1).CurJobDef == JobDefOf.Capture || D(1).CurJobDef == QR_DefOf.QR_CarryToRoom))
                        throw new InvalidOperationException("capture backlog withdrew a combat leader with a standing enemy left");
                    return !Standing(enemies[2]);
                })
                .Expect("all downed survivors then reach prisoner beds", () => DealtWith(enemies[0])
                    && DealtWith(enemies[1]) && DealtWith(enemies[2]))
                .Expect("after the threats and captures clear both dogs dock", () =>
                    Stable().Docked.Contains(D(0)) && Stable().Docked.Contains(D(1)))
                .Summary(() => $"pursuit replacement at tick {retargetAt}, zero-tick dispatch; {Fate(enemies[0])}; {Fate(enemies[1])}; {Fate(enemies[2])}")
                .Start();
        }
    }
}
