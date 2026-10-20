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
        /// <summary>Drafted colonists don't work, so every job in the scenario is a dog's. Undrafted at the end.</summary>
        private static void DraftColonists(Scenario s)
        {
            // ToList: drafting changes the colonist lists.
            foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(p => p.drafter != null && !p.Downed).ToList())
                p.drafter.Drafted = true;
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
        /// Two dogs at a powered stable: the far blueprint gets its steel (map-wide), the near gold goes to the stockpile,
        /// the dirt is cleaned; the gold outside the service radius stays. One dog leaves at a time; a reload mid-work.
        /// </summary>
        private static void HaulAndClean()
        {
            var s = new Scenario("Haul and clean", 30000);
            string stableId = null, farGoldId = null;
            var filthIds = new List<string>();
            CellRect pile = default;
            IntVec3 farGoldCell = default, wallCell = default, frameCell = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            int Out() => Stable().Dogs().Count(d => d.Spawned);
            s.Do("a stable with two dogs, a gold stockpile, gold near and far, dirt, a far wall blueprint and frame", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    IntVec3 home = stable.InteractionCell;
                    pile = ClearRect(map, home, 3, 1, 8f, 14f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    SpawnStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 15f, 25f));
                    Thing farGold = SpawnStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 50f, 60f));
                    farGoldId = farGold.ThingID;
                    farGoldCell = farGold.Position;
                    for (int i = 0; i < 3; i++)
                        filthIds.Add(SpawnFilth(s, OpenCell(map, home, 6f, 20f)));
                    SpawnStack(s, ThingDefOf.Steel, 30, OpenCell(map, home, 4f, 10f));
                    wallCell = OpenCell(map, home, 50f, 70f);
                    s.Track(GenConstruct.PlaceBlueprintForBuild(ThingDefOf.Wall, wallCell, map, Rot4.North,
                        Faction.OfPlayer, ThingDefOf.Steel));
                    frameCell = OpenCell(map, home, 50f, 70f, c => c.DistanceTo(wallCell) >= 5f);
                    SpawnBuilding(s, ThingDefOf.Wall.frameDef, ThingDefOf.Steel, frameCell, Rot4.North);
                    s.OnFinish(() =>
                    {
                        foreach (Thing t in new[] { wallCell, frameCell }.SelectMany(c => c.GetThingList(Find.CurrentMap))
                                     .Where(t => t is Frame || t is Blueprint).ToList())
                            t.Destroy();
                    });
                })
                .Expect("the stable sends one dog out first", () =>
                {
                    int n = Out();
                    if (n > 1)
                        throw new InvalidOperationException($"{n} dogs left at once");
                    return n == 1;
                })
                .Expect("then the second one", () => Out() == 2)
                .SaveAndReload()
                .Expect("the far blueprint and the far frame got their steel (construction is map-wide)", () =>
                    new[] { wallCell, frameCell }.All(c => Find.CurrentMap.thingGrid.ThingsAt(c).OfType<Frame>()
                        .Any(f => f.ThingCountNeeded(ThingDefOf.Steel) == 0)))
                .Expect("the near gold is in the stockpile", () => CountIn(pile, ThingDefOf.Gold) == 30)
                .Expect("the dirt is cleaned", () => filthIds.All(id => Scenario.ById(id) == null))
                .Expect("both dogs dock again", () => Stable().Docked.Count == 2)
                .Expect("the gold outside the radius stayed", () => Scenario.ById(farGoldId)?.Position == farGoldCell)
                .Summary(() =>
                {
                    Thing gold = Scenario.ById(farGoldId);
                    return $"raid {Raid.Active(Find.CurrentMap)}; near gold in the pile {CountIn(pile, ThingDefOf.Gold)}; far gold from {farGoldCell}: {(gold == null ? "gone" : $"{gold.stackCount} at {gold.PositionHeld} in {gold.ParentHolder.ToStringSafe()}")}; "
                        + ColonistStates();
                })
                .Start();
        }

        /// <summary>
        /// Overlapping radii: dirt next to B is B's (A's dog stays docked); later two spots at once, each claimed by one dog
        /// only. A reload in between.
        /// </summary>
        private static void TwoStables()
        {
            var s = new Scenario("Two stables", 20000);
            string aId = null, bId = null, aDogId = null, bDogId = null;
            var filthIds = new List<string>();
            bool aHelped = false, bCleaned = false;
            Building_Stable A() => (Building_Stable)Scenario.ById(aId);
            Building_Stable B() => (Building_Stable)Scenario.ById(bId);
            Pawn ADog() => (Pawn)Scenario.ById(aDogId);
            Pawn BDog() => (Pawn)Scenario.ById(bDogId);
            s.Do("two powered stables 16–22 cells apart, one dog each; no filth but the scenario's", () =>
                {
                    DraftColonists(s);
                    // The map's own filth (rubble, blood) could send B's dog elsewhere, and a stable with no idle dog
                    // rightly leaves its dirt to A.
                    foreach (Thing filth in Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Filth).ToList())
                        filth.Destroy();
                    Building_Stable a = SpawnStable(s, powered: true);
                    Building_Stable b = SpawnStable(s, powered: true, near: a.Position, minDist: 16f, maxDist: 22f);
                    aId = a.ThingID;
                    bId = b.ThingID;
                    aDogId = s.Track(a.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    bDogId = s.Track(b.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Do("dirt 2–4 cells from B", () => filthIds.Add(SpawnFilth(s, OpenCell(Find.CurrentMap, B().InteractionCell, 2f, 4f))))
                .Expect("B's dog cleans it; A's dog never goes for it", () =>
                {
                    Thing dirt = Scenario.ById(filthIds[0]);
                    if (dirt != null && Targets(ADog(), dirt))
                        throw new InvalidOperationException("A sent its dog for B's dirt");
                    bCleaned |= Targets(BDog(), dirt);
                    return dirt == null;
                })
                .Expect("it was B's dog", () => bCleaned)
                .Expect("B's dog docks again", () => B().Docked.Contains(BDog()))
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
                    aHelped |= ADog().Spawned;
                    return filthIds.Skip(1).All(id => Scenario.ById(id) == null);
                })
                .Expect("both dogs docked", () => A().Docked.Contains(ADog()) && B().Docked.Contains(BDog()))
                .Summary(() => $"A's dog {(aHelped ? "took the second spot" : "stayed docked")}; " + ColonistStates())
                .Start();
        }

        /// <summary>
        /// A raid starts while the dog hauls: it finishes at 3x speed and 2x drain, docks, and takes no new work until the
        /// raid is over (across a reload); the stable is unpowered, so dispatch works without power.
        /// </summary>
        private static void RaidFinishAndDock()
        {
            var s = new Scenario("Raid: finish and dock", 20000);
            string stableId = null, dogId = null, raiderId = null, otherGoldId = null;
            var goldIds = new List<string>();
            CellRect pile = default;
            IntVec3 otherGoldCell = default;
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
                .Expect("the dog is sent for gold", () => Dog().Spawned && Dog().CurJobDef == JobDefOf.HaulToCell)
                .Do("a raider appears far away", () =>
                {
                    Map map = Find.CurrentMap;
                    Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate)
                        ?? Find.FactionManager.RandomEnemyFaction();
                    Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction));
                    IntVec3 cell = OpenCell(map, Stable().InteractionCell, 60f, 90f);
                    raiderId = s.Track(GenSpawn.Spawn(raider, cell, map)).ThingID;
                    LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(cell), map, new[] { raider });
                })
                .Expect("raid: the working dog runs faster and drains faster", () =>
                {
                    if (!Raid.Active(Find.CurrentMap) || !Dog().Spawned)
                        return false;
                    raidSpeed = Dog().GetStatValue(StatDefOf.MoveSpeed);
                    raidDrain = CompRobodog.Of(Dog()).DrainFactor;
                    return raidDrain == 2f;
                })
                .Expect("it finishes the haul and docks", () =>
                    Stable().Docked.Contains(Dog()) && CountIn(pile, ThingDefOf.Gold) == 30)
                .Do("note the other stack", () =>
                {
                    Thing other = goldIds.Select(Scenario.ById).First(t => t != null && !pile.Contains(t.Position));
                    otherGoldId = other.ThingID;
                    otherGoldCell = other.Position;
                })
                .Wait(600)
                .Expect("no new work during the raid", () =>
                    Stable().Docked.Contains(Dog()) && Scenario.ById(otherGoldId)?.Position == otherGoldCell)
                .SaveAndReload()
                .Expect("still a raid after the load", () => Raid.Active(Find.CurrentMap))
                .Wait(300)
                .Expect("still docked, the other stack untouched", () =>
                    Stable().Docked.Contains(Dog()) && CountIn(pile, ThingDefOf.Gold) == 30
                    && Scenario.ById(otherGoldId)?.Position == otherGoldCell)
                .Do("the raider leaves during weapon cooldown", () =>
                {
                    Pawn raider = (Pawn)Scenario.ById(raiderId);
                    // Destroy() removes equipment but can leave its verb in a world pawn's saved cooldown.
                    // Stage that boundary explicitly, then use the actual pawn departure lifecycle.
                    raider.stances.SetStance(new Stance_Cooldown(300, LocalTargetInfo.Invalid,
                        raider.equipment.PrimaryEq.PrimaryVerb));
                    raider.ExitMap(allowedToJoinOrCreateCaravan: false, Rot4.North);
                })
                .Expect("raid over: the dog goes out at normal speed and drain", () =>
                {
                    if (Raid.Active(Find.CurrentMap) || !Dog().Spawned)
                        return false;
                    calmSpeed = Dog().GetStatValue(StatDefOf.MoveSpeed);
                    return CompRobodog.Of(Dog()).DrainFactor == 1f;
                })
                .Expect("move speed was 3x during the raid", () => Math.Abs(raidSpeed / calmSpeed - 3f) < 0.01f)
                .SaveAndReload()
                .Expect("all gold stored, the dog docked", () =>
                    CountIn(pile, ThingDefOf.Gold) == 60 && Stable().Docked.Contains(Dog()))
                .Summary(() => $"speed {calmSpeed:F2} -> {raidSpeed:F2} in the raid, drain x{raidDrain}; gold " + string.Join(", ",
                    goldIds.Select(id => Scenario.ById(id) ?? Dog()?.carryTracker.CarriedThing).Where(t => t != null)
                        .Select(t => $"{t.stackCount} at {t.PositionHeld}{(pile.Contains(t.PositionHeld) ? " (pile)" : "")}"))
                    + $", carried {Dog()?.carryTracker.CarriedThing.ToStringSafe()}, docked {Stable().Docked.Contains(Dog())}, "
                    + $"job {Dog()?.CurJob.ToStringSafe()}; " + ColonistStates())
                .Start();
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
            s.Do("a powered stable: an injured dog at 5% and a full one, both docked", () =>
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
                })
                .Expect("the low dog is injured", () => InjurySeverity(Low()) > 0f)
                .Do("switch the stable off", () => Stable().GetComp<CompFlickable>().SwitchIsOn = false)
                .Expect("no power", () => !Stable().CanCharge)
                .Do("dirt near the stable", () => filthId = SpawnFilth(s, OpenCell(Find.CurrentMap, Stable().InteractionCell, 4f, 10f)))
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
        /// The stable burns down with two dogs docked: they come out unassigned and wait in the storeroom, not in the
        /// nearer bedroom; still there after a reload.
        /// </summary>
        private static void StableFire()
        {
            var s = new Scenario("Stable fire with docked dogs", 20000);
            string stableId = null;
            var dogIds = new List<string>();
            TestRoom bedroom = null, store = null;
            IEnumerable<Pawn> Dogs() => dogIds.Select(id => (Pawn)Scenario.ById(id));
            s.Do("a stable with two docked dogs, a bedroom 6–14 cells away, a storeroom 18–32", () =>
                {
                    Map map = Find.CurrentMap;
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID);
                    dogIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID);
                    bedroom = BuildRoom(s, ClearRect(map, stable.InteractionCell, 7, 2, 6f, 14f), batteryPct: -1f);
                    SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel, bedroom.rect.CenterCell, Rot4.North);
                    store = BuildRoom(s, ClearRect(map, stable.InteractionCell, 7, 2, 18f, 32f), batteryPct: -1f);
                })
                .Expect("the bedroom is a bedroom, the storeroom qualifies", () =>
                {
                    Map map = Find.CurrentMap;
                    return bedroom.rect.CenterCell.GetRoom(map)?.Role == RoomRoleDefOf.Bedroom
                        && JobGiver_WaitInRoom.Qualifies(store.rect.CenterCell.GetRoom(map));
                })
                .Do("the stable burns down", () => Scenario.ById(stableId).Kill(new DamageInfo(DamageDefOf.Flame, 99999f)))
                .Expect("both dogs are out and unassigned", () =>
                    Scenario.ById(stableId) == null && Dogs().All(d => d.Spawned && CompRobodog.Of(d).stable == null))
                .Expect("both wait in the storeroom", () => Dogs().All(d => store.Contains(d.Position)))
                .SaveAndReload()
                .Wait(400)
                .Expect("still there, still unassigned", () =>
                    Dogs().All(d => store.Contains(d.Position) && CompRobodog.Of(d).stable == null))
                .Start();
        }

        /// <summary>
        /// Uninstalled, the stable lets its dog out; the dog keeps it, waits in a room (across a reload), and docks once
        /// the stable is installed again elsewhere.
        /// </summary>
        private static void StableUninstall()
        {
            var s = new Scenario("Stable uninstall and reinstall", 20000);
            string stableId = null, dogId = null, miniId = null;
            TestRoom store = null;
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            Building_Stable Assigned() => CompRobodog.Of(Dog()).stable;
            s.Do("a stable with a docked dog and a storeroom 12–28 cells away", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    store = BuildRoom(s, ClearRect(Find.CurrentMap, stable.InteractionCell, 7, 2, 12f, 28f), batteryPct: -1f);
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
                .Expect("it waits in the storeroom", () => store.Contains(Dog().Position))
                .SaveAndReload()
                .Expect("still assigned to the packed stable after the load", () =>
                    Assigned()?.ThingID == stableId && !Assigned().Spawned && store.Contains(Dog().Position))
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
