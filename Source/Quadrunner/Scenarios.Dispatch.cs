using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>T3: dispatch and work, raids, and a stable that loses power, burns or is moved.</summary>
    public static partial class Scenarios
    {
        /// <summary>Drafted colonists neither work nor shoot, so only the dogs act. Undrafted at the end.</summary>
        private static void DraftColonists(Scenario s)
        {
            // ToList: drafting changes the colonist lists.
            foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(p => p.drafter != null && !p.Downed).ToList())
            {
                p.drafter.Drafted = true;
                p.drafter.FireAtWill = false;
            }
            s.OnFinish(() =>
            {
                foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(p => p.drafter != null).ToList())
                    p.drafter.Drafted = false;
            });
        }

        /// <summary>For summaries: each colonist's job, drafted and mental state, and every bleeding pawn.</summary>
        private static string ColonistStates()
        {
            Map map = Find.CurrentMap;
            return "colonists: " + string.Join(", ", map.mapPawns.FreeColonistsSpawned.Select(p =>
                    $"{p.LabelShort} {p.CurJob?.ToStringSafe()}{(p.Drafted ? " drafted" : "")}{(p.InMentalState ? " " + p.MentalStateDef.defName : "")}"))
                + "; bleeding: " + string.Join(", ", map.mapPawns.AllPawnsSpawned.Where(p => p.health.hediffSet.BleedRateTotal > 0f)
                    .Select(p => $"{p} at {p.Position}"));
        }

        /// <summary>A free open cell <paramref name="minDist"/>–<paramref name="maxDist"/> from <paramref name="from"/>, reachable from it.</summary>
        private static IntVec3 OpenCell(Map map, IntVec3 from, float minDist, float maxDist, Predicate<IntVec3> extra = null)
        {
            if (!CellFinder.TryFindRandomCellNear(from, map, (int)maxDist, c => c.Standable(map) && !c.Roofed(map)
                    && !c.Fogged(map) && c.GetFirstItem(map) == null && c.GetFirstBuilding(map) == null
                    && c.GetFirstPawn(map) == null && c.GetZone(map) == null
                    && c.DistanceTo(from) >= minDist && c.DistanceTo(from) <= maxDist && (extra == null || extra(c))
                    && map.reachability.CanReach(from, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)),
                    out IntVec3 cell, 3000))
                throw new InvalidOperationException($"no open cell {minDist}-{maxDist} cells from {from}");
            return cell;
        }

        /// <summary>A stockpile over <paramref name="rect"/> that takes only <paramref name="only"/>; deleted at the end.</summary>
        private static void Stockpile(Scenario s, CellRect rect, ThingDef only)
        {
            Map map = Find.CurrentMap;
            var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
            map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 c in rect)
                zone.AddCell(c);
            zone.settings.filter.SetDisallowAll();
            zone.settings.filter.SetAllow(only, true);
            string label = zone.label;
            s.OnFinish(() =>
            {
                Map m = Find.CurrentMap;
                foreach (Thing t in rect.Cells.SelectMany(c => c.GetThingList(m)).Where(t => t.def == only).ToList())
                    t.Destroy();
                m.zoneManager.AllZones.FirstOrDefault(z => z.label == label)?.Delete();
            });
        }

        private static int CountIn(CellRect rect, ThingDef def)
        {
            Map map = Find.CurrentMap;
            return rect.Cells.SelectMany(c => c.GetThingList(map)).Where(t => t.def == def).Sum(t => t.stackCount);
        }

        /// <summary>
        /// Dirt on <paramref name="cell"/>, made part of the home area (where colonists and dogs clean) and roofed, since
        /// rain washes outdoor filth away.
        /// </summary>
        private static string SpawnFilth(Scenario s, IntVec3 cell)
        {
            Map map = Find.CurrentMap;
            bool wasHome = map.areaManager.Home[cell];
            map.areaManager.Home[cell] = true;
            map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
            s.OnFinish(() =>
            {
                Find.CurrentMap.areaManager.Home[cell] = wasHome;
                Find.CurrentMap.roofGrid.SetRoof(cell, null);
            });
            // Spawned directly: FilthMaker refuses dirt on natural ground, and cleaning doesn't care.
            return s.Track(GenSpawn.Spawn(ThingDefOf.Filth_Dirt, cell, map)).ThingID;
        }

        /// <summary>The dog is out on a job whose target (or target queue) includes <paramref name="thing"/>.</summary>
        private static bool Targets(Pawn dog, Thing thing) =>
            thing != null && dog.Spawned && dog.CurJob != null && (dog.CurJob.targetA.Thing == thing
                || dog.CurJob.GetTargetQueue(TargetIndex.A).Any(t => t.Thing == thing));

        private static float InjurySeverity(Pawn pawn) =>
            pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);

        /// <summary>
        /// Two dogs at a stable: the near blueprint and frame get their steel, the near gold goes to the stockpile,
        /// the dirt is cleaned; then the idle workers survey, and the gold beyond the old 40-cell boundary is
        /// discovered out there and hauled too, across a reload in between.
        /// </summary>
        private static void HaulAndClean()
        {
            var s = new Scenario("Haul and clean", 90000);
            string farGoldId = null;
            var dogIds = new List<string>();
            var filthIds = new List<string>();
            CellRect pile = default;
            IntVec3 farGoldCell = default, wallCell = default, frameCell = default;
            IEnumerable<Pawn> Dogs() => dogIds.Select(id => (Pawn)Scenario.ById(id));
            s.Do("a stable with two dogs, a gold stockpile, near gold, dirt, a near blueprint and frame, and gold 50–60 out", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    dogIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID);
                    dogIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID);
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 12f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    SpawnStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 8f, 14f));
                    Thing farGold = SpawnStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 50f, 60f));
                    farGoldId = farGold.ThingID;
                    farGoldCell = farGold.Position;
                    for (int i = 0; i < 3; i++)
                        filthIds.Add(SpawnFilth(s, OpenCell(map, home, 5f, 12f)));
                    SpawnStack(s, ThingDefOf.Steel, 30, OpenCell(map, home, 4f, 10f));
                    wallCell = OpenCell(map, home, 8f, 14f);
                    s.Track(GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, wallCell, map, Rot4.North,
                        Faction.OfPlayer, ThingDefOf.Steel));
                    frameCell = OpenCell(map, home, 8f, 14f, c => c.DistanceTo(wallCell) >= 4f);
                    SpawnBuilding(s, ThingDefOf.Wall.frameDef, ThingDefOf.Steel, frameCell, Rot4.North);
                    s.OnFinish(() =>
                    {
                        foreach (Thing t in new[] { wallCell, frameCell }.SelectMany(c => c.GetThingList(Find.CurrentMap))
                                     .Where(t => t is Frame || t is Blueprint).ToList())
                            t.Destroy();
                    });
                })
                .Expect("the near blueprint and the near frame got their steel", () =>
                    new[] { wallCell, frameCell }.All(c => Find.CurrentMap.thingGrid.ThingsAt(c).OfType<Frame>()
                        .Any(f => f.ThingCountNeeded(ThingDefOf.Steel) == 0)))
                .Expect("the near gold is in the stockpile", () => CountIn(pile, ThingDefOf.Gold) == 30)
                .Expect("the dirt is cleaned", () => filthIds.All(id => Scenario.ById(id) == null))
                .Expect("idle workers survey instead of docking", () =>
                {
                    foreach (Pawn dog in Dogs())
                        CompRobodog.Of(dog)?.SetBattery(1f);
                    return Dogs().All(NetSurveying);
                })
                .Expect("a survey reaches the far gold", () =>
                {
                    if (!NetQueued(farGoldId))
                        foreach (Pawn dog in Dogs().Where(d => d.Spawned && d.carryTracker?.CarriedThing == null))
                        {
                            Teleport(dog, CellFinder.StandableCellNear(farGoldCell, Find.CurrentMap, 3));
                            break;
                        }
                    return NetQueued(farGoldId);
                })
                .SaveAndReload()
                .Expect("the far gold is hauled across the old 40-cell boundary", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && !NetQueued(farGoldId))
                .Expect("and the workers go back to surveying", () =>
                {
                    foreach (Pawn dog in Dogs())
                        CompRobodog.Of(dog)?.SetBattery(1f);
                    return Dogs().All(NetSurveying);
                })
                .Summary(() =>
                {
                    Thing gold = Scenario.ById(farGoldId);
                    return $"far gold from {farGoldCell}: {(gold == null ? "stored" : $"{gold.stackCount} at {gold.PositionHeld}")}; " +
                        $"jobs {string.Join(", ", Dogs().Select(d => d?.CurJob?.ToStringSafe()))}; " + ColonistStates();
                })
                .Start();
        }

        /// <summary>
        /// Two stables share one queue across a reload. Separate cleaning targets must never be claimed by both
        /// dogs at once; home-stable affiliation does not reserve nearby work. Idle workers resume surveying.
        /// </summary>
        private static void TwoStables()
        {
            var s = new Scenario("Two stables", 30000);
            string bId = null, aDogId = null, bDogId = null;
            var filthIds = new List<string>();
            bool aHelped = false;
            Building_Stable B() => (Building_Stable)Scenario.ById(bId);
            Pawn ADog() => (Pawn)Scenario.ById(aDogId);
            Pawn BDog() => (Pawn)Scenario.ById(bDogId);
            s.Do("two unpowered stables 16–22 cells apart, one dog each; no filth but the scenario's", () =>
                {
                    DraftColonists(s);
                    // The map's own filth (rubble, blood) could distract the dogs; only the staged dirt counts.
                    foreach (Thing filth in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Filth).ToList())
                        filth.Destroy();
                    Building_Stable a = SpawnStable(s, powered: false);
                    Building_Stable b = SpawnStable(s, powered: false, near: a.Position, minDist: 16f, maxDist: 22f);
                    bId = b.ThingID;
                    aDogId = s.Track(a.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    bDogId = s.Track(b.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .SaveAndReload()
                .Do("two dirt spots near B, 12 cells apart", () =>
                {
                    Map map = Find.CurrentMap;
                    IntVec3 first = OpenCell(map, B().InteractionCell, 3f, 8f);
                    filthIds.Add(SpawnFilth(s, first));
                    filthIds.Add(SpawnFilth(s, OpenCell(map, B().InteractionCell, 3f, 12f, c => c.DistanceTo(first) >= 12f)));
                })
                .Expect("both cleaned, never by two dogs at once", () =>
                {
                    var claimed = new Dictionary<Thing, Pawn>();
                    foreach (Pawn dog in new[] { ADog(), BDog() }.Where(d => d.Spawned && d.CurJobDef == JobDefOf.Clean))
                        foreach (LocalTargetInfo t in dog.CurJob.GetTargetQueue(TargetIndex.A).Append(dog.CurJob.targetA))
                        {
                            if (!t.HasThing)
                                continue;
                            if (claimed.TryGetValue(t.Thing, out Pawn other) && other != dog)
                                throw new InvalidOperationException($"{t.Thing} claimed by {other} and {dog}");
                            claimed[t.Thing] = dog;
                        }
                    aHelped |= claimed.Values.Any(v => v == ADog());
                    return filthIds.All(id => Scenario.ById(id) == null);
                })
                .Expect("both dogs idle again: surveying, not docked", () =>
                {
                    CompRobodog.Of(ADog())?.SetBattery(1f);
                    CompRobodog.Of(BDog())?.SetBattery(1f);
                    return NetSurveying(ADog()) && NetSurveying(BDog());
                })
                .Summary(() => $"A's dog {(aHelped ? "helped with the second pair" : "stayed away")}; " + ColonistStates())
                .Start();
        }

        /// <summary>
        /// A raid starts while the dog carries gold: the haul is suspended at once — the cargo dropped safely, the
        /// dog running at raid speed and raid drain as it stands by — and no normal work until the raid is over,
        /// across a reload. The raider leaving hands the backlog back at normal speed and drain.
        /// </summary>
        private static void RaidSuspendAndResume()
        {
            var s = new Scenario("Raid: suspend and resume", 40000);
            string stableId = null, dogId = null, raiderId = null, carriedId = null, otherGoldId = null;
            var goldIds = new List<string>();
            CellRect pile = default;
            IntVec3 otherGoldCell = default, dropCell = default;
            bool dropped = false;
            float raidSpeed = 0f, calmSpeed = 0f, raidDrain = 0f;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("an unpowered stable with a dog, a gold stockpile, two gold stacks 22–30 cells out and 12+ apart", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    IntVec3 first = OpenCell(map, home, 22f, 30f);
                    goldIds.Add(SpawnStack(s, ThingDefOf.Gold, 30, first).ThingID);
                    // Vanilla HaulToCell adds duplicate stacks within 8 cells of the pickup, in the same job.
                    // Leave room for an adjacent pickup cell too: the second stack must be a separate job.
                    goldIds.Add(SpawnStack(s, ThingDefOf.Gold, 30,
                        OpenCell(map, home, 22f, 30f, c => c.DistanceTo(first) >= 12f)).ThingID);
                })
                .Do("stage the two discovered reports before hauling, to exercise wartime backlog handling", () =>
                {
                    foreach (string id in goldIds)
                        Net().Report(NetworkTaskKind.Haul, Scenario.ById(id), NetHaulGiver);
                    Net().Tick(force: true);
                })
                .Expect("the dog is sent for gold", () => Dog().Spawned && Dog().CurJobDef == JobDefOf.HaulToCell)
                .Do("note its calm walking speed", () => calmSpeed = Dog().GetStatValue(StatDefOf.MoveSpeed))
                .Expect("it carries a stack", () => Dog().carryTracker.CarriedThing != null)
                .Do("note the cargo, then a raider appears far away; watch at normal speed", () =>
                    {
                        Map map = Find.CurrentMap;
                        carriedId = Dog().carryTracker.CarriedThing.ThingID;
                        Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate)
                            ?? Find.FactionManager.RandomEnemyFaction();
                        Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                            faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction));
                        EquipRaidCooldownWeapon(raider);
                        IntVec3 cell = OpenCell(map, Stable().InteractionCell, 60f, 90f);
                        raiderId = s.Track(GenSpawn.Spawn(raider, cell, map)).ThingID;
                        LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(cell), map, new[] { raider });
                        Raid.Recheck(map);
                        Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    })
                .Expect("raid: the haul suspends at once, the cargo dropped safely, never delivered", () =>
                    {
                        if (!Net().War || !Dog().Spawned)
                            return false;
                        if (Scenario.ById(carriedId) is Thing cargo && cargo.Spawned && cargo.stackCount == 30)
                        {
                            dropped = true;
                            dropCell = cargo.Position;
                        }
                        if (dropped && Dog().CurJobDef != JobDefOf.HaulToCell && Dog().carryTracker.CarriedThing == null
                            && CountIn(pile, ThingDefOf.Gold) == 0)
                        {
                            raidSpeed = Dog().GetStatValue(StatDefOf.MoveSpeed);
                            raidDrain = CompRobodog.Of(Dog()).DrainFactor;
                        }
                        return dropped && raidDrain == 2f;
                    })
                .Do("back to ultrafast, and note the other stack", () =>
                    {
                        Scenario.Resume();
                        Thing other = goldIds.Select(Scenario.ById).First(t => t != null && t.ThingID != carriedId);
                        otherGoldId = other.ThingID;
                        otherGoldCell = other.Position;
                    })
                .Wait(600)
                .Expect("no normal work during the raid: both stacks untouched and unassigned", () =>
                    NetTask(otherGoldId)?.assigneeId == null && NetTask(carriedId)?.assigneeId == null
                    && Dog().carryTracker.CarriedThing == null && Scenario.ById(otherGoldId)?.Position == otherGoldCell
                    && Scenario.ById(carriedId)?.Position == dropCell && CountIn(pile, ThingDefOf.Gold) == 0)
                .SaveAndReload()
                .Expect("still a raid after the load", () => Raid.Active(Find.CurrentMap) && Net().War)
                .Wait(300)
                .Expect("after reload the normal queue still waits, with both stacks untouched", () =>
                    NetTask(otherGoldId)?.assigneeId == null && NetTask(carriedId)?.assigneeId == null
                    && Dog().carryTracker.CarriedThing == null && Scenario.ById(otherGoldId)?.Position == otherGoldCell
                    && Scenario.ById(carriedId)?.Position == dropCell && CountIn(pile, ThingDefOf.Gold) == 0)
                .Do("the raider leaves during weapon cooldown", () =>
                    {
                        Pawn raider = (Pawn)Scenario.ById(raiderId);
                        // Destroy() removes equipment but can leave its verb in a world pawn's saved cooldown.
                        // Stage that boundary explicitly, then use the actual pawn departure lifecycle.
                        raider.stances.SetStance(new Stance_Cooldown(300, LocalTargetInfo.Invalid,
                            raider.equipment.PrimaryEq.PrimaryVerb));
                        raider.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.North);
                        Raid.Recheck(Find.CurrentMap);
                    })
                .Expect("raid over: the dog goes back to work at normal speed and drain", () =>
                    {
                        if (Net().War || Raid.Active(Find.CurrentMap) || !Dog().Spawned)
                            return false;
                        calmSpeed = Dog().GetStatValue(StatDefOf.MoveSpeed);
                        return CompRobodog.Of(Dog()).DrainFactor == 1f;
                    })
                .Expect("move speed was 3x during the raid", () => Math.Abs(raidSpeed / calmSpeed - 3f) < 0.01f)
                .Expect("all gold stored, the dropped cargo included", () => CountIn(pile, ThingDefOf.Gold) == 60)
                .Summary(() => $"speed {calmSpeed:F2} -> {raidSpeed:F2} in the raid, drain x{raidDrain}; cargo dropped at {dropCell}; gold " +
                    string.Join(", ", goldIds.Select(Scenario.ById).Where(t => t != null)
                        .Select(t => $"{t.stackCount} at {t.PositionHeld}{(pile.Contains(t.PositionHeld) ? " (pile)" : "")}"))
                    + $", docked {Stable().Docked.Contains(Dog())}, job {Dog()?.CurJob.ToStringSafe()}; " + ColonistStates())
                .Start();
        }

        /// <summary>The cooldown/departure fixtures require an actual equipped verb, not random pawn-kind gear.</summary>
        private static void EquipRaidCooldownWeapon(Pawn raider)
        {
            if (raider?.equipment == null)
                throw new InvalidOperationException("the cooldown fixture needs an equipable raider");
            raider.equipment.DestroyAllEquipment();
            raider.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(
                DefDatabase<ThingDef>.GetNamed("Gun_Autopistol")));
        }

        /// <summary>
        /// Switched off, the stable still dispatches, but a docked dog neither charges nor gets repaired; switched back on
        /// (after a reload), it does both.
        /// </summary>
        private static void StablePowerLoss()
        {
            var s = new Scenario("Stable power loss", 15000);
            string stableId = null, lowId = null, fullId = null, filthId = null;
            float battery = 0f, severity = 0f;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Low() => (Pawn)Scenario.ById(lowId);
            Pawn Full() => (Pawn)Scenario.ById(fullId);
            s.Do("a powered stable: an injured dog at 5% and a worker held for setup, both docked", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn low = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    lowId = low.ThingID;
                    // The injury directly: damage can be deflected by armor.
                    BodyPartRecord leg = low.health.hediffSet.GetNotMissingParts().First(p => p.def.defName == "MechanicalLeg");
                    Hediff crush = HediffMaker.MakeHediff(DamageDefOf.Blunt.hediff, low, leg);
                    crush.Severity = 8f;
                    low.health.AddHediff(crush, leg);
                    CompRobodog.Of(low).SetBattery(0.05f);
                    fullId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    CompRobodog.Of(Full()).SetBattery(0.01f);
                })
                .Expect("the low dog is injured", () => InjurySeverity(Low()) > 0f)
                .Do("switch the stable off", () => Stable().GetComp<CompFlickable>().SwitchIsOn = false)
                .Expect("no power", () => !Stable().CanCharge)
                .Do("dirt near the stable", () => filthId = SpawnFilth(s, OpenCell(Find.CurrentMap, Stable().InteractionCell, 4f, 10f)))
                .Wait(700) // Vanilla does not clean fresh filth; make it eligible before the worker leaves its scan area.
                .Do("release the full worker", () => CompRobodog.Of(Full()).SetBattery(1f))
                .Expect("dispatch goes on: the full dog goes out to clean", () => Full().Spawned)
                .Expect("the dirt is cleaned", () => Scenario.ById(filthId) == null)
                .Do("note the low dog", () =>
                {
                    battery = CompRobodog.Of(Low()).Battery;
                    severity = InjurySeverity(Low());
                })
                .Wait(600)
                .Expect("unpowered: the docked dog neither charged nor got repaired", () =>
                    Stable().Docked.Contains(Low()) && CompRobodog.Of(Low()).Battery == battery && InjurySeverity(Low()) == severity)
                .SaveAndReload()
                .Do("switch it back on", () => Stable().GetComp<CompFlickable>().SwitchIsOn = true)
                .Expect("powered: it charges and gets repaired", () =>
                    CompRobodog.Of(Low()).Battery > battery + 0.1f && InjurySeverity(Low()) < severity)
                .Summary(() => $"battery {battery:P0} -> {CompRobodog.Of(Low())?.Battery:P0}, injury {severity:F1} -> {(Low() == null ? 0f : InjurySeverity(Low())):F1}")
                .Start();
        }

        /// <summary>
        /// The stable burns down with two dogs docked: they come out unassigned, the newest of them coordinates the
        /// network, and with no work at hand they survey rather than wait indoors. Still true after a reload.
        /// </summary>
        private static void StableFire()
        {
            var s = new Scenario("Stable fire with docked dogs", 20000);
            string stableId = null, d1Id = null, d2Id = null;
            Pawn D1() => (Pawn)Scenario.ById(d1Id);
            Pawn D2() => (Pawn)Scenario.ById(d2Id);
            s.Do("an unpowered stable with two dogs", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    d1Id = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    d2Id = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Do("the stable burns down", () => Scenario.ById(stableId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f)))
                .Expect("both dogs are out and unassigned", () =>
                    Scenario.ById(stableId) == null && new[] { D1(), D2() }.All(d => d.Spawned && CompRobodog.Of(d).stable == null))
                .Do("settle the network", () => Net().Tick(force: true))
                .Expect("the newest dog coordinates", () => Net().Coordinator.ThingID == d2Id)
                .Expect("surveying replaces waiting: both dogs explore", () =>
                {
                    CompRobodog.Of(D1())?.SetBattery(1f);
                    CompRobodog.Of(D2())?.SetBattery(1f);
                    return NetSurveying(D1()) && NetSurveying(D2());
                })
                .SaveAndReload()
                .Wait(400)
                .Expect("still out, unassigned and exploring after the reload", () =>
                {
                    CompRobodog.Of(D1())?.SetBattery(1f);
                    CompRobodog.Of(D2())?.SetBattery(1f);
                    return new[] { D1(), D2() }.All(d => d.Spawned && CompRobodog.Of(d).stable == null && NetSurveying(d));
                })
                .Summary(() => $"coordinator {Net().Coordinator?.LabelShortCap ?? "none"}; jobs " +
                    string.Join(", ", new[] { D1(), D2() }.Select(d => d?.CurJob?.ToStringSafe())))
                .Start();
        }

        /// <summary>
        /// Uninstalled, the stable lets its dog out; the dog keeps its affiliation, itself coordinates while the
        /// stable is packed, surveys until its battery runs low and then waits; installed again elsewhere, it walks
        /// over and docks.
        /// </summary>
        private static void StableUninstall()
        {
            var s = new Scenario("Stable uninstall and reinstall", 25000);
            string stableId = null, dogId = null, miniId = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Building_Stable Assigned() => CompRobodog.Of(Dog()).stable;
            s.Do("an unpowered stable with a dog", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Do("uninstall the stable", () =>
                {
                    Thing stable = Scenario.ById(stableId);
                    Map map = stable.Map;
                    IntVec3 pos = stable.Position;
                    MinifiedThing mini = stable.MakeMinified();
                    if (!GenPlace.TryPlaceThing(mini, pos, map, ThingPlaceMode.Near))
                        throw new InvalidOperationException("couldn't place the minified stable");
                    miniId = s.Track(mini).ThingID;
                })
                .Expect("the dog is out and still belongs to it", () => Dog().Spawned && Assigned()?.ThingID == stableId)
                .Do("settle the network", () => Net().Tick(force: true))
                .Expect("with the stable packed, the dog itself coordinates", () => Net().Coordinator.ThingID == dogId)
                .Expect("it surveys while the stable is packed", () =>
                {
                    CompRobodog.Of(Dog())?.SetBattery(1f);
                    return NetSurveying(Dog());
                })
                .Do("its battery runs low", () => CompRobodog.Of(Dog()).SetBattery(0.04f))
                .Expect("no surveying on a low battery: it waits", () => Dog().Spawned && !NetSurveying(Dog()) && !Dog().Downed)
                .SaveAndReload()
                .Expect("still assigned to the packed stable after the load", () =>
                    Assigned()?.ThingID == stableId && !Assigned().Spawned && Dog().Spawned)
                .Do("install it again 10+ cells away", () =>
                {
                    Map map = Find.CurrentMap;
                    var mini = (MinifiedThing)Scenario.ById(miniId);
                    IntVec3 from = mini.Position;
                    IntVec3 at = FindClearArea(map, from, 40f,
                        c => GenAdj.OccupiedRect(c, Rot4.South, QR_DefOf.QR_Stable.size).ExpandedBy(3),
                        c => c.DistanceTo(from) >= 10f);
                    Thing inner = mini.InnerThing;
                    mini.InnerThing = null;
                    mini.Destroy();
                    GenSpawn.Spawn(inner, at, map, Rot4.South);
                })
                .Expect("the dog walks to it and docks", () => ((Building_Stable)Scenario.ById(stableId))?.Docked.Contains(Dog()) == true)
                .Start();
        }
    }
}
