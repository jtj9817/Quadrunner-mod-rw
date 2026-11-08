using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Real-foam reproduction of the reported F3 failure: widely separated Home fires remain after a Medirunner's
    /// first burst. Nothing here destroys a burning staged target, reloads before the handoff, or forces a network
    /// tick once the response starts; every later fire must be re-detected and put out by native jobs and actual
    /// battery-paid foam while the responder is deployed.
    /// </summary>
    public static partial class Scenarios
    {
        private static Action<Verb_FoamSpray> foamBurstObserver;

        // Fixture-only observation of real spray bursts, including the battery drain spelled by Verb_FoamSpray.
        private static void FoamBurstStarted(Verb_FoamSpray __instance) => foamBurstObserver?.Invoke(__instance);

        private static void ObserveFoamBursts(Scenario scenario, Action<Verb_FoamSpray> observe)
        {
            var harmony = new Harmony("josh9817.quadrunner.fire-after-foam-scenario");
            var complete = AccessTools.Method(typeof(Verb_FoamSpray), nameof(Verb_FoamSpray.WarmupComplete));
            foamBurstObserver = observe;
            harmony.Patch(complete, postfix: new HarmonyMethod(AccessTools.Method(typeof(Scenarios), nameof(FoamBurstStarted))));
            scenario.OnFinish(() =>
            {
                foamBurstObserver = null;
                harmony.Unpatch(complete, HarmonyPatchType.Postfix, harmony.Id);
            });
        }
        /// <summary>A 5×5 fuel patch with nine simultaneous fires; existing foam is never cleared.</summary>
        private static CellRect SpawnHomeBonfireAt(Scenario s, Map map, IntVec3 center, List<IntVec3> revealed)
        {
            var rect = new CellRect(center.x - 2, center.z - 2, 5, 5);
            foreach (IntVec3 c in rect.ExpandedBy(3).ClipInsideMap(map))
            {
                if (map.fogGrid.IsFogged(c))
                {
                    map.fogGrid.Unfog(c);
                    revealed.Add(c);
                }
                map.areaManager.Home[c] = true;
            }
            // Firefoam natively blocks ignition; a treated spot would not prove anything about a fresh fire.
            foreach (IntVec3 c in rect.ExpandedBy(1).ClipInsideMap(map))
                if (c.GetThingList(map).Any(t => t.def == ThingDefOf.Filth_FireFoam))
                    throw new InvalidOperationException($"fixture: supposed untreated cell {c} already has firefoam");
            foreach (IntVec3 at in rect.Cells)
            {
                Thing stack = SpawnStack(s, ThingDefOf.WoodLog, 75, at);
                if (stack.Position != at)
                    throw new InvalidOperationException($"fixture: log stack for {center} landed at {stack.Position}, not {at}");
            }
            for (int x = -2; x <= 2; x += 2)
                for (int z = -2; z <= 2; z += 2)
                {
                    IntVec3 at = center + new IntVec3(x, 0, z);
                    if (!FireUtility.TryStartFireIn(at, map, 1f, null))
                        throw new InvalidOperationException($"fixture: couldn't start a fire at {at}");
                }
            return rect;
        }

        private static int FoamIn(CellRect rect, int margin)
        {
            Map map = Find.CurrentMap;
            if (map == null || rect.Width <= 0)
                return 0;
            return map.listerThings.ThingsOfDef(ThingDefOf.Filth_FireFoam)
                .Count(t => t.Spawned && !t.Destroyed && rect.ExpandedBy(margin).Contains(t.Position));
        }

        /// <summary>
        /// A wide open pad with four far-apart Home fires staged before the response, then two more lit after the first
        /// real burst. The medic must retarget from its current position, work every surviving fire, and never head home
        /// while one remains. Failures are bounded per step and carry the full native transition/burst record.
        /// </summary>
        private static void MedirunnerFiresAfterFoam() => FiresAfterFoam(hotRoom: false);

        private static void MedirunnerHotFiresAfterFoam() => FiresAfterFoam(hotRoom: true);

        private static void FiresAfterFoam(bool hotRoom)
        {
            var s = new Scenario(hotRoom ? "Medirunner hot fires after foam" : "Medirunner fires after foam", 20000);
            const float SprayRange = 14.9f;
            var staged = new List<(string name, CellRect rect)>();
            var transitions = new List<string>();
            var fireJobs = new List<(int tick, string name)>();
            var burstLog = new List<string>();
            var revealedFog = new List<IntVec3>();
            var seenProjectiles = new HashSet<int>();
            string stableId = null, medicId = null, failure = null, spacing = null;
            int startedAt = 0, firstOutAt = -1, newAt = -1, bursts = 0, projectileCount = 0, foamAfterFirst = 0;
            float secondStartDistance = float.MaxValue;
            bool secondCouldHitFromCurPos = false;
            string firstBurstTarget = null;
            JobDef secondStartDef = null;
            IntVec3 doorAt = IntVec3.Invalid;
            CellRect padAt = default;
            bool[] priorHome = null;
            ThingDef foamProjectile = null;
            Building_Stable Stable() => Scenario.ById(stableId) as Building_Stable;
            Pawn Medic() => Scenario.ById(medicId) as Pawn;
            int Now() => Find.TickManager.TicksGame;
            CellRect Rect(string name) => staged.First(p => p.name == name).rect;
            float Battery() => CompRobodog.Of(Medic())?.Battery ?? -1f;
            bool AnyBurning() => staged.Any(p => p.rect.Width > 0 && FireIn(p.rect));
            string NameOf(Fire fire) => fire == null ? null : StagedRectName(staged, fire.Position);
            string VerbText(Pawn pawn)
            {
                Verb verb = pawn?.equipment?.PrimaryEq?.PrimaryVerb;
                return verb == null ? "no primary verb"
                    : $"{verb.state}/{(verb.Bursting ? "bursting" : "idle")} target={verb.CurrentTarget} available={verb.Available()}";
            }
            string Describe(Job job)
            {
                if (job == null)
                    return "null";
                Fire fire = JobFire(job);
                string target = fire != null ? (NameOf(fire) ?? fire.Position.ToString()) : $"{job.targetA}/{job.targetB}";
                return $"{job.def.defName} id={job.loadID} fire={target} forced={job.playerForced}";
            }
            string Snapshot() =>
                $"medic {(Medic() is Pawn m && m.Spawned ? m.Position.ToString() : "docked/absent")} job {Describe(Medic()?.CurJob)} verb {VerbText(Medic())}; "
                + $"battery {(Battery() < 0f ? "-" : Battery().ToString("P0"))}; burning "
                + $"[{string.Join(",", staged.Where(p => p.rect.Width > 0 && FireIn(p.rect)).Select(p => p.name))}]; "
                + $"bursts {bursts}, first burst target {firstBurstTarget ?? "none"}, foam projectiles {projectileCount} "
                + $"[{string.Join(" | ", burstLog)}]; "
                + $"fire jobs [{string.Join(", ", fireJobs.Select(j => $"+{j.tick - startedAt} {j.name}"))}]; "
                + $"native transitions [{string.Join("; ", transitions.Skip(Math.Max(0, transitions.Count - 18)))}]; "
                + $"spacing {spacing}";
            void Fail()
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
            }
            bool Homeward(Job job)
            {
                Building_Stable stable = Stable();
                if (stable == null || job == null)
                    return false;
                if (job.def == QR_DefOf.QR_Dock)
                    return true;
                if (job.targetA.Thing == stable || job.targetB.Thing == stable)
                    return true;
                if (JobFire(job) != null)
                    return false;
                return job.def == JobDefOf.Goto && !job.targetA.HasThing
                    && job.targetA.Cell.InHorDistOf(stable.InteractionCell, 4f);
            }
            bool Watch()
            {
                Pawn medic = Medic();
                if (medic == null || !medic.Spawned)
                    return true;
                Map map = Find.CurrentMap;
                if (hotRoom && firstOutAt >= 0)
                    Rect("third").CenterCell.GetRoom(map).Temperature = 500f;
                if (map != null && foamProjectile != null)
                    foreach (Thing thing in map.listerThings.ThingsOfDef(foamProjectile))
                        if (thing is Projectile shot && shot.Launcher == medic && seenProjectiles.Add(shot.thingIDNumber))
                            projectileCount++;
                return true;
            }
            void Burst(Verb_FoamSpray verb)
            {
                Pawn caster = verb.CasterPawn;
                if (caster == null || caster.ThingID != medicId)
                    return;
                int now = Now();
                Fire fire = verb.CurrentTarget.Thing as Fire;
                string name = NameOf(fire);
                if (bursts == 0)
                    firstBurstTarget = name;
                bursts++;
                if (burstLog.Count < 16)
                    burstLog.Add($"+{now - startedAt} burst battery {Battery():P0} fire={name ?? verb.CurrentTarget.ToString()} "
                        + $"state={verb.state}/{(verb.Bursting ? "bursting" : "idle")} pos={caster.Position} job={Describe(caster.CurJob)}");
            }
            void Observe(Pawn pawn, Job job)
            {
                if (pawn.ThingID != medicId || job == null)
                    return;
                int now = Now();
                transitions.Add($"+{now - startedAt} next {Describe(job)} prev {Describe(pawn.CurJob)}; "
                    + $"pos {pawn.Position}; verb {VerbText(pawn)}; battery {Battery():P0}");
                Fire fire = JobFire(job);
                if (fire == null)
                {
                    if (Homeward(job) && AnyBurning())
                        failure ??= $"homeward/dock transition while staged Home fires still burn at +{now - startedAt}: "
                            + $"{Describe(job)}; {Snapshot()}";
                    return;
                }
                string name = NameOf(fire);
                fireJobs.Add((now, name));
                if (name == "second" && secondStartDef == null)
                {
                    secondStartDef = job.def;
                    secondStartDistance = pawn.Position.DistanceTo(fire.Position);
                    Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
                    secondCouldHitFromCurPos = verb != null && verb.CanHitTarget(fire);
                }
            }
            // One bounded wait: a phase that makes no progress throws with every native transition and burst recorded.
            Scenario Within(int ticks, string label, Func<bool> check)
            {
                int started = -1;
                return s.Expect($"{label} (within {ticks} ticks)", () =>
                {
                    if (started < 0)
                        started = Now();
                    Watch();
                    Fail();
                    if (Now() - started > ticks)
                        throw new InvalidOperationException($"{label}: no result within {ticks} ticks. {Snapshot()}");
                    return check();
                });
            }
            s.Do("a wide controlled pad, Clear weather, a powered stable, a Medirunner and four separated Home fires", () =>
                {
                    Map map = Find.CurrentMap;
                    WeatherDef priorCur = map.weatherManager.curWeather, priorLast = map.weatherManager.lastWeather;
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                    map.weatherManager.curWeatherAge = 4000;
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        now.weatherManager.curWeather = priorCur;
                        now.weatherManager.lastWeather = priorLast;
                    });
                    DraftColonists(s);
                    ClearFireFixture(s, staged);
                    priorHome = SnapshotHomeArea(map);
                    // Six 5x5 burning patches; extra width accommodates the optional hot room.
                    CellRect pad = padAt = SurveyFindRect(map, 72, 60, 90f, s);
                    foreach (IntVec3 cell in pad.Cells)
                        if (map.fogGrid.IsFogged(cell))
                        {
                            map.fogGrid.Unfog(cell);
                            revealedFog.Add(cell);
                        }
                    // Firefoam left by an earlier fire scenario on the same map blocks ignition inside this pad
                    // (both variants can claim overlapping ground); the pad is ours to control, so sweep it — the
                    // untreated-cell assertion further down still guards against this run's own sprays.
                    foreach (IntVec3 cell in pad.Cells)
                        foreach (Filth filth in cell.GetThingList(map).OfType<Filth>().ToList())
                            if (filth.def == ThingDefOf.Filth_FireFoam)
                                filth.Destroy();
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        foreach (IntVec3 cell in revealedFog)
                            if (cell.InBounds(now) && !now.fogGrid.IsFogged(cell))
                                now.fogGrid.Refog(CellRect.SingleCell(cell));
                    });
                    Building_Stable stable = SpawnStable(s, powered: true,
                        near: new IntVec3(pad.minX + 26, 0, pad.CenterCell.z), minDist: 0f, maxDist: 0f);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    CompRobodog.Of(Medic()).SetBattery(1f);
                    doorAt = stable.InteractionCell;
                    foamProjectile = ThingDef.Named("Proj_Foam");
                    IntVec3 first = doorAt + new IntVec3(8, 0, 0);
                    IntVec3 second = doorAt + new IntVec3(2, 0, -11);
                    IntVec3 third = doorAt + new IntVec3(28, 0, 4);
                    IntVec3 fourth = doorAt + new IntVec3(-22, 0, -4);
                    if (hotRoom)
                    {
                        var interior = new CellRect(third.x - 4, third.z - 4, 9, 9);
                        SurveyBuildRoom(s, interior, new IntVec3(interior.minX - 1, 0, interior.CenterCell.z));
                    }
                    var clusters = new (string name, IntVec3 center)[]
                    {
                        ("first", first), ("second", second), ("third", third), ("fourth", fourth),
                    };
                    foreach ((string name, IntVec3 center) in clusters)
                    {
                        var cluster = new CellRect(center.x - 2, center.z - 2, 5, 5);
                        if (cluster.minX < pad.minX || cluster.maxX > pad.maxX
                            || cluster.minZ < pad.minZ || cluster.maxZ > pad.maxZ)
                            throw new InvalidOperationException($"fixture: the {name} cluster {cluster} leaves the controlled pad {pad}");
                    }
                    if (!first.InHorDistOf(doorAt, SprayRange) || !second.InHorDistOf(doorAt, SprayRange))
                        throw new InvalidOperationException("fixture: the in-range pair is not inside the sprayer's range");
                    foreach ((string name, IntVec3 center) in new[] { ("third", third), ("fourth", fourth) })
                        if (center.InHorDistOf(doorAt, SprayRange))
                            throw new InvalidOperationException($"fixture: the {name} cluster is not beyond sprayer range");
                    for (int i = 0; i < clusters.Length; i++)
                        for (int j = i + 1; j < clusters.Length; j++)
                        {
                            float distance = clusters[i].center.DistanceTo(clusters[j].center);
                            bool inRangePair = clusters[i].name == "first" && clusters[j].name == "second";
                            if (distance < (inRangePair ? 12f : 20f))
                                throw new InvalidOperationException($"fixture: {clusters[i].name}/{clusters[j].name} are {distance:F1} cells apart");
                        }
                    spacing = $"range {SprayRange:F1}; centres from door: first {first.DistanceTo(doorAt):F1}, second {second.DistanceTo(doorAt):F1}, "
                        + $"third {third.DistanceTo(doorAt):F1}, fourth {fourth.DistanceTo(doorAt):F1}; "
                        + $"first-second {first.DistanceTo(second):F1} (in-range retarget pair)";
                    foreach ((string name, IntVec3 center) in clusters)
                        staged.Add((name, SpawnHomeBonfireAt(s, map, center, revealedFog)));
                    s.OnFinish(() => RestoreHomeArea(priorHome));
                    startedAt = Now();
                    ObservePackJobs(s, Observe);
                    ObserveFoamBursts(s, Burst);
                    // No forced pass: the stable's own network tick must deploy and assign the medic.
                });
            Within(600, "the deployed Medirunner has a native fire job on the nearest first cluster", () =>
            {
                if (!Medic().Spawned)
                    return false;
                if (firstOutAt < 0 && fireJobs.Count > 0 && fireJobs[0].name != null && fireJobs[0].name != "first")
                    throw new InvalidOperationException($"the first fire job targeted {fireJobs[0].name}, not the nearest first cluster; {Snapshot()}");
                return fireJobs.Any(j => j.name == "first") || NameOf(JobFire(Medic().CurJob)) == "first";
            });
            Within(1200, "real foam extinguishes the first patch while the distant patches remain burning", () =>
            {
                if (FireIn(Rect("first")))
                    return false;
                if (firstOutAt >= 0)
                    return true;
                firstOutAt = Now();
                if (bursts < 1 || firstBurstTarget != "first")
                    throw new InvalidOperationException($"the first cluster went out without a real foam burst on it "
                        + $"(bursts {bursts}, first burst target {firstBurstTarget ?? "none"}); {Snapshot()}");
                foreach (string survivor in new[] { "third", "fourth" })
                    if (!FireIn(Rect(survivor)))
                        throw new InvalidOperationException($"fixture: the first spray also extinguished the {survivor} cluster; {Snapshot()}");
                foamAfterFirst = FoamIn(Rect("first"), 8);
                if (projectileCount == 0 && foamAfterFirst == 0)
                    throw new InvalidOperationException($"no foam projectile or foam filth was observed for the first spray; {Snapshot()}");
                return true;
            });
            s.Do("light two more Home fires at untreated distant cells while the responder is still out", () =>
                {
                    Watch();
                    Fail();
                    if (firstOutAt < 0 || !Medic().Spawned)
                        throw new InvalidOperationException($"the responder left before the new fires could be staged; {Snapshot()}");
                    if (hotRoom)
                    {
                        Room room = Rect("third").CenterCell.GetRoom(Find.CurrentMap);
                        room.Temperature = 500f;
                        QR.Log($"post-foam hot-room probe: {room.Temperature:F0} C; medic {Medic().Position}; target {Rect("third").CenterCell}");
                    }
                    IntVec3 east = doorAt + new IntVec3(-4, 0, 22);
                    IntVec3 far = doorAt + new IntVec3(32, 0, -24);
                    var clusters = new (string name, IntVec3 center)[] { ("new-east", east), ("new-far", far) };
                    foreach ((string name, IntVec3 center) in clusters)
                    {
                        var cluster = new CellRect(center.x - 2, center.z - 2, 5, 5);
                        if (cluster.minX < padAt.minX || cluster.maxX > padAt.maxX
                            || cluster.minZ < padAt.minZ || cluster.maxZ > padAt.maxZ)
                            throw new InvalidOperationException($"fixture: the {name} cluster leaves the controlled pad");
                        foreach ((string other, CellRect rect) in staged)
                            if (center.DistanceTo(rect.CenterCell) < 20f)
                                throw new InvalidOperationException($"fixture: {name} is only {center.DistanceTo(rect.CenterCell):F1} cells from {other}");
                    }
                    // This run's own sprays scatter firefoam far beyond the targeted clusters (hundreds of filth
                    // cells, 40+ cells out), so the new sites are swept before staging: they must catch fire
                    // fresh (asserted right below), which foam would prevent.
                    foreach ((string _, IntVec3 center) in clusters)
                        foreach (IntVec3 c in new CellRect(center.x - 3, center.z - 3, 7, 7).ClipInsideMap(Find.CurrentMap))
                            foreach (Filth filth in c.GetThingList(Find.CurrentMap).OfType<Filth>().ToList())
                                if (filth.def == ThingDefOf.Filth_FireFoam)
                                    filth.Destroy();
                    foreach ((string name, IntVec3 center) in clusters)
                    {
                        staged.Add((name, SpawnHomeBonfireAt(s, Find.CurrentMap, center, revealedFog)));
                        if (!FireIn(staged[staged.Count - 1].rect))
                            throw new InvalidOperationException($"fixture: the new {name} fire did not catch");
                    }
                })
                .Expect("both new fires are burning and were lit while the responder remained deployed", () =>
                    Medic().Spawned && FireIn(Rect("new-east")) && FireIn(Rect("new-far")));
            Within(900, "the medic takes the second in-range fire from its current position without travelling and puts it out", () =>
            {
                if (secondStartDef == null)
                    return false;
                if (secondStartDef != JobDefOf.AttackStatic || !secondCouldHitFromCurPos || secondStartDistance > SprayRange)
                    throw new InvalidOperationException($"the second fire was not taken with an attack job from spray range: "
                        + $"{secondStartDef.defName} from {secondStartDistance:F1} cells, could hit {secondCouldHitFromCurPos}; {Snapshot()}");
                if (FireIn(Rect("second")))
                    return false;
                if (bursts < 2)
                    throw new InvalidOperationException($"the second cluster went out after {bursts} battery burst(s), not multiple real bursts; {Snapshot()}");
                return true;
            });
            Within(5000, "every remaining pre-existing and newly lit cluster is detected and physically extinguished without going home", () =>
            {
                if (AnyBurning())
                    return false;
                foreach (string name in new[] { "third", "fourth" })
                    if (!fireJobs.Any(j => j.name == name))
                        throw new InvalidOperationException($"the {name} cluster is out without a native fire job; {Snapshot()}");
                foreach (string name in new[] { "new-east", "new-far" })
                    if (!fireJobs.Any(j => j.name == name && j.tick >= newAt - 1))
                        throw new InvalidOperationException($"the {name} cluster is out without a native fire job after it was lit; {Snapshot()}");
                if (bursts < 2)
                    throw new InvalidOperationException($"all clusters went out after {bursts} battery burst(s), not multiple real bursts; {Snapshot()}");
                return true;
            });
            s.Do("the pre-existing foam is still on the first cluster", () =>
                {
                    Fail();
                    if (AnyBurning())
                        throw new InvalidOperationException($"a staged cluster reignited before the foam check; {Snapshot()}");
                    if (foamAfterFirst > 0 && FoamIn(Rect("first"), 8) < foamAfterFirst)
                        throw new InvalidOperationException($"existing firefoam was removed during the response: {foamAfterFirst} -> "
                            + $"{FoamIn(Rect("first"), 8)}; {Snapshot()}");
                })
                .SaveAndReload()
                .Expect("every staged cluster is still physically out after the post-suppression reload", () => !AnyBurning())
                .Expect("the first spray's foam survived the reload", () =>
                    foamAfterFirst == 0 || FoamIn(Rect("first"), 8) >= foamAfterFirst)
                .Summary(() => $"door {doorAt}; {spacing}; first out +{firstOutAt - startedAt}, new fires +{newAt - startedAt}; "
                    + $"bursts {bursts} [{string.Join(" | ", burstLog)}], foam projectiles {projectileCount}, "
                    + $"foam near first {FoamIn(Rect("first"), 8)} (after first spray {foamAfterFirst}); "
                    + $"first burst target {firstBurstTarget ?? "none"}; "
                    + $"second job {secondStartDef?.defName} from {secondStartDistance:F1} cells (could hit {secondCouldHitFromCurPos}); "
                    + $"fire jobs: {string.Join(", ", fireJobs.Select(j => $"+{j.tick - startedAt} {j.name}"))}; "
                    + $"native transitions: {string.Join("; ", transitions)}")
                .Start();
        }
    }
}
