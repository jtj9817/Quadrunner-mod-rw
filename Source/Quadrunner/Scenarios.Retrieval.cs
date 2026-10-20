using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>T4: powered-down dogs carried home, field fixes, carrier on low battery, fire, out of reach, raiders, wrecks.</summary>
    public static partial class Scenarios
    {
        /// <summary>A dog of <paramref name="stable"/>, out in the field <paramref name="minDist"/>–<paramref name="maxDist"/> cells away, at 0%.</summary>
        private static Pawn DownDog(Scenario s, Building_Stable stable, float minDist, float maxDist)
        {
            Pawn dog = SpawnDog(s, OpenCell(Find.CurrentMap, stable.InteractionCell, minDist, maxDist));
            CompRobodog.Of(dog).stable = stable;
            CompRobodog.Of(dog).SetBattery(0f);
            return dog;
        }

        private static bool Carrying(Pawn carrier, Pawn takee) =>
            carrier.Spawned && carrier.CurJobDef == QR_DefOf.QR_CarryToStable && carrier.carryTracker.CarriedThing == takee;

        /// <summary>Scenario colonists use manual priorities with Hauling first; restored at the end.</summary>
        private static void HaulingFirst(Scenario s)
        {
            bool manual = Current.Game.playSettings.useWorkPriorities;
            Current.Game.playSettings.useWorkPriorities = true;
            var before = new Dictionary<Pawn, int>();
            foreach (Pawn p in Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(p => p.workSettings != null))
            {
                before[p] = p.workSettings.GetPriority(WorkTypeDefOf.Hauling);
                if (!p.WorkTypeIsDisabled(WorkTypeDefOf.Hauling))
                    p.workSettings.SetPriority(WorkTypeDefOf.Hauling, 1);
            }
            s.OnFinish(() =>
            {
                foreach (KeyValuePair<Pawn, int> kv in before.Where(kv => !kv.Key.Destroyed && kv.Key.workSettings != null))
                    kv.Key.workSettings.SetPriority(WorkTypeDefOf.Hauling, kv.Value);
                Current.Game.playSettings.useWorkPriorities = manual;
            });
        }

        /// <summary>
        /// The stable sends its healthy dog for a powered-down one and it's carried home and charged, across a reload; then
        /// a colonist's "Carry to stable" (from the real float menu) does the same.
        /// </summary>
        private static void CarryHome()
        {
            var s = new Scenario("Carry home", 20000);
            string stableId = null, carrierId = null, downId = null;
            bool carried = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Carrier() => (Pawn)Scenario.ById(carrierId);
            Pawn Down() => (Pawn)Scenario.ById(downId);
            Pawn Colonist() => Find.CurrentMap.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed
                && p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation));
            s.Do("a powered stable with a docked dog; another of its dogs powered down 15–25 cells out", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    carrierId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    downId = DownDog(s, stable, 15f, 25f).ThingID;
                })
                .Expect("the powered-down dog lies like a downed pawn", () => Down().Downed && CompRobodog.Of(Down()).PoweredDown)
                .Do("the Force dispatch debug action sends the stable's dog to carry it at once", () =>
                {
                    DebugActions.ForceDispatchAt(Stable().Position);
                    if (!Carrier().Spawned || Carrier().CurJobDef != QR_DefOf.QR_CarryToStable || Carrier().CurJob.targetA.Thing != Down())
                        throw new InvalidOperationException($"not sent: {Carrier().CurJob?.ToStringSafe()}");
                })
                .Expect("it picks the dog up", () => Carrying(Carrier(), Down()))
                .SaveAndReload()
                .Expect("still carrying after the load, or already home", () =>
                    carried = Carrying(Carrier(), Down()) || Stable().Docked.Contains(Down()))
                .Expect("the powered-down dog is docked and charging", () =>
                    Stable().Docked.Contains(Down()) && CompRobodog.Of(Down()).Battery > 0f && !CompRobodog.Of(Down()).PoweredDown)
                .Expect("the carrier docks again", () => Stable().Docked.Contains(Carrier()))
                .Do("the docked dog at 1% (charging, so not sent out); the other out again 10–20 cells away at 0%; a colonist right-clicks it", () =>
                {
                    Map map = Find.CurrentMap;
                    CompRobodog.Of(Carrier()).SetBattery(0.01f);
                    Pawn down = Down();
                    Stable().TryUndock(down);
                    Teleport(down, OpenCell(map, Stable().InteractionCell, 10f, 20f));
                    DebugActions.PowerDownAt(down.Position);
                    if (!CompRobodog.Of(down).PoweredDown)
                        throw new InvalidOperationException("the Power down debug action left the dog up");
                    Pawn colonist = Colonist() ?? throw new InvalidOperationException("no colonist can carry");
                    List<FloatMenuOption> options = FloatMenuMakerMap.GetOptions(new List<Pawn> { colonist },
                        down.DrawPos, out FloatMenuContext _);
                    // Exact label: drafted colonists also get vanilla's "Carry <dog>" (CarryDownedPawnDrafted).
                    FloatMenuOption carry = options.FirstOrDefault(o => o.Label == $"Carry {down.LabelShort} to {Stable().LabelShort}")
                        ?? throw new InvalidOperationException("no Carry to stable option: " +
                            string.Join(" | ", options.Select(o => o.Label)));
                    if (carry.Disabled)
                        throw new InvalidOperationException($"option disabled: {carry.Label}");
                    carry.action();
                    if (colonist.CurJobDef != QR_DefOf.QR_CarryToStable)
                        throw new InvalidOperationException($"{colonist} didn't take the job: now {colonist.CurJob?.ToStringSafe()}, " +
                            $"{colonist.jobs.jobQueue.Count} queued, drafted {colonist.Drafted}");
                })
                .Expect("the colonist takes the job", () => Find.CurrentMap.mapPawns.FreeColonistsSpawned.Any(p =>
                    p.CurJobDef == QR_DefOf.QR_CarryToStable && p.CurJob.targetA.Thing == Down()))
                .Expect("the colonist carries it", () =>
                    Find.CurrentMap.mapPawns.FreeColonistsSpawned.Any(p => Carrying(p, Down())))
                .Expect("and docks it", () => Stable().Docked.Contains(Down()))
                .Summary(() => $"carried across the reload: {carried}")
                .Start();
        }

        /// <summary>
        /// A colonist makes a robodog battery and a repair kit at the stable; colonists bring the kit, then the battery,
        /// to a damaged powered-down dog in the field (no dog carries it meanwhile), across a reload.
        /// </summary>
        private static void FieldBatteryAndRepairKit()
        {
            var s = new Scenario("Field battery and repair kit", 30000);
            string stableId = null, dogId = null;
            float healthBefore = 0f;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            int OnMap(ThingDef def) => Find.CurrentMap.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);
            s.Do("a powered stable with materials and battery and kit bills", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    SpawnStack(s, ThingDefOf.Steel, 50, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 10, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.ComponentIndustrial, 1, stable.InteractionCell);
                    stable.BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("QR_MakeRobodogBattery").MakeNewBill());
                    stable.BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("QR_MakeRobodogRepairKit").MakeNewBill());
                    s.OnFinish(() =>
                    {
                        foreach (ThingDef def in new[] { QR_DefOf.QR_RobodogBattery, QR_DefOf.QR_RobodogRepairKit })
                            foreach (Thing t in Find.CurrentMap.listerThings.ThingsOfDef(def).ToList())
                                t.Destroy();
                    });
                })
                .Expect("a colonist makes both", () =>
                {
                    if (OnMap(QR_DefOf.QR_RobodogBattery) >= 1 && OnMap(QR_DefOf.QR_RobodogRepairKit) >= 1)
                        return true;
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    if (crafter.CurJobDef != JobDefOf.DoBill && BillJob(crafter, Stable()) is Job job)
                        crafter.jobs.TryTakeOrderedJob(job);
                    return false;
                })
                .Do("a dog of the stable 15–25 cells out: damaged below 70%, powered down; Hauling first for colonists", () =>
                {
                    Pawn dog = DownDog(s, Stable(), 15f, 25f);
                    dogId = dog.ThingID;
                    // Injuries added directly (armor can deflect damage), half of each part's health, biggest parts
                    // first, never one that would kill the dog or destroy the part, until it's below 65%.
                    foreach (BodyPartRecord part in dog.health.hediffSet.GetNotMissingParts()
                                 .OrderByDescending(p => p.def.GetMaxHealth(dog)).ToList())
                    {
                        if (dog.health.summaryHealth.SummaryHealthPercent < 0.65f)
                            break;
                        Hediff crush = HediffMaker.MakeHediff(DamageDefOf.Blunt.hediff, dog, part);
                        crush.Severity = part.def.GetMaxHealth(dog) * 0.5f;
                        if (!dog.health.WouldDieAfterAddingHediff(crush) && !dog.health.WouldLosePartAfterAddingHediff(crush.def, part, crush.Severity))
                            dog.health.AddHediff(crush, part);
                    }
                    healthBefore = dog.health.summaryHealth.SummaryHealthPercent;
                    HaulingFirst(s);
                })
                .Expect("the dog is below 70% and needs the kit first", () =>
                    healthBefore < 0.7f && FieldFix.Needed(Dog()) == QR_DefOf.QR_RobodogRepairKit && !Dog().Dead)
                .Expect("a colonist brings the kit", () => Find.CurrentMap.mapPawns.FreeColonistsSpawned.Any(p =>
                    p.CurJobDef == QR_DefOf.QR_FieldFixRobodog && p.CurJob.targetB.Thing == Dog()))
                .SaveAndReload()
                .Expect("repaired in the field", () => InjurySeverity(Dog()) == 0f && Dog().Spawned)
                .Expect("then the battery: charged to full on the spot", () =>
                    CompRobodog.Of(Dog()).Battery >= 1f && !CompRobodog.Of(Dog()).PoweredDown)
                .Expect("both items used", () => OnMap(QR_DefOf.QR_RobodogBattery) == 0 && OnMap(QR_DefOf.QR_RobodogRepairKit) == 0)
                .Summary(() => $"health {healthBefore:P0} -> {Dog()?.health.summaryHealth.SummaryHealthPercent:P0}")
                .Start();
        }

        /// <summary>
        /// The carrier drops to 10% on the way: while charging at the (powered) stable wins it finishes; with the stable
        /// switched off and the second dog right beside a charge room, it sets the dog down, charges there, then carries
        /// it home anyway.
        /// </summary>
        private static void CarrierRunsLow()
        {
            var s = new Scenario("Carrier runs low", 30000);
            string stableId = null, carrierId = null, nearId = null, farId = null;
            TestRoom room = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Carrier() => (Pawn)Scenario.ById(carrierId);
            Pawn Near() => (Pawn)Scenario.ById(nearId);
            Pawn Far() => (Pawn)Scenario.ById(farId);
            s.Do("a powered stable with a docked dog; a charge room 22–32 cells out; a dog at 0% 5–10 cells from the stable", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    carrierId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    room = BuildRoom(s, ClearRect(map, stable.InteractionCell, 7, 3, 22f, 32f));
                    nearId = DownDog(s, stable, 5f, 10f).ThingID;
                })
                .Expect("the carrier picks it up", () => Carrying(Carrier(), Near()))
                .Do("the carrier drops to 10%", () => CompRobodog.Of(Carrier()).SetBattery(0.1f))
                .Expect("the stable wins: it finishes and docks the dog", () => Stable().Docked.Contains(Near()))
                .Expect("the carrier docks and charges to full", () =>
                    Stable().Docked.Contains(Carrier()) && CompRobodog.Of(Carrier()).Battery >= 1f)
                .Do("switch the stable off; a second dog at 0% outside the room's door", () =>
                {
                    Stable().GetComp<CompFlickable>().SwitchIsOn = false;
                    Pawn far = SpawnDog(s, room.Outside);
                    CompRobodog.Of(far).stable = Stable();
                    CompRobodog.Of(far).SetBattery(0f);
                    farId = far.ThingID;
                })
                .Expect("the carrier picks it up", () => Carrying(Carrier(), Far()))
                .Do("the carrier drops to 10%", () => CompRobodog.Of(Carrier()).SetBattery(0.1f))
                .Expect("the spot wins: it sets the dog down and charges in the room", () =>
                    Far().Spawned && ChargingIn(Carrier(), room))
                .SaveAndReload()
                .Expect("charged, it carries the dog home after all", () => Stable().Docked.Contains(Far()))
                .Start();
        }

        /// <summary>
        /// A powered-down dog beside a fire: the stable's dog beats the fire out, then carries it home. A dog walled in:
        /// the out-of-reach alert names it and nobody goes; opened up, it is carried home.
        /// </summary>
        private static void RetrievalUnderFire()
        {
            var s = new Scenario("Retrieval under fire", 30000);
            string stableId = null, carrierId = null, burntId = null, boxedId = null, wallId = null;
            bool beat = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Carrier() => (Pawn)Scenario.ById(carrierId);
            Pawn Burnt() => (Pawn)Scenario.ById(burntId);
            Pawn Boxed() => (Pawn)Scenario.ById(boxedId);
            bool FireBy(Pawn dog) => GenRadial.RadialCellsAround(dog.Position, 1.5f, true)
                .Any(c => c.InBounds(dog.Map) && c.GetFirstThing<Fire>(dog.Map) != null);
            s.Do("a powered stable with a docked dog; a dog at 0% 12–20 cells out with a fire beside it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    carrierId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    CellRect clear = ClearRect(map, stable.InteractionCell, 3, 2, 12f, 20f);
                    Pawn burnt = SpawnDog(s, clear.CenterCell);
                    CompRobodog.Of(burnt).stable = stable;
                    CompRobodog.Of(burnt).SetBattery(0f);
                    burntId = burnt.ThingID;
                    // Wood to keep the fire going until the dog gets there.
                    SpawnStack(s, ThingDefOf.WoodLog, 75, clear.CenterCell + IntVec3.East);
                    if (!FireUtility.TryStartFireIn(clear.CenterCell + IntVec3.East, map, 1f, null))
                        throw new InvalidOperationException("couldn't start the fire");
                    s.OnFinish(() =>
                    {
                        foreach (Fire f in Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().ToList())
                            f.Destroy();
                    });
                })
                .Expect("the dog goes to beat the fire", () => beat |= Carrier().CurJobDef == JobDefOf.BeatFire)
                .Expect("the fire is out", () => !FireBy(Burnt()))
                .Expect("then it carries the dog home", () => Stable().Docked.Contains(Burnt()))
                .Expect("and docks", () => Stable().Docked.Contains(Carrier()))
                .Do("a dog at 0% walled in 10–20 cells out", () =>
                {
                    Map map = Find.CurrentMap;
                    CellRect box = ClearRect(map, Stable().InteractionCell, 3, 1, 10f, 20f);
                    foreach (IntVec3 c in box.EdgeCells)
                    {
                        Thing wall = SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                        if (c == new IntVec3(box.CenterCell.x, 0, box.minZ))
                            wallId = wall.ThingID;
                    }
                    Pawn boxed = SpawnDog(s, box.CenterCell);
                    CompRobodog.Of(boxed).stable = Stable();
                    CompRobodog.Of(boxed).SetBattery(0f);
                    boxedId = boxed.ThingID;
                })
                .Expect("the out-of-reach alert names it", () => new Alert_RobodogUnreachable().Culprits().Contains(Boxed()))
                .Wait(600)
                .Expect("nobody went for it", () => Stable().Docked.Contains(Carrier()) && Boxed().Spawned)
                .SaveAndReload()
                .Do("open the wall", () => Scenario.ById(wallId).Destroy())
                .Expect("the alert clears", () => !new Alert_RobodogUnreachable().Culprits().Contains(Boxed()))
                .Expect("it's carried home", () => Stable().Docked.Contains(Boxed()))
                .Summary(() => $"beat the fire: {beat}")
                .Start();
        }

        /// <summary>
        /// A raider finds a powered-down dog as a kidnap victim (Harmony) and carries it off the map; the stable's
        /// own dog stays docked during the raid. A reload while it's carried.
        /// </summary>
        private static void RaiderCarriesOffDog()
        {
            var s = new Scenario("Raider carries off a dog", 20000);
            string stableId = null, keptId = null, downId = null, raiderId = null;
            Pawn down = null;
            int factionId = -1;
            // Re-found by load ID: the Faction object from before the reload is stale.
            Faction RaiderFaction() => Find.FactionManager.AllFactionsListForReading.First(f => f.loadID == factionId);
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Raider() => (Pawn)Scenario.ById(raiderId);
            s.Do("a stable with a docked dog; another of its dogs at 0% 15–25 cells out; a raider 6–10 cells from it", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    keptId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    down = DownDog(s, stable, 15f, 25f);
                    downId = down.ThingID;
                    Faction faction = Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate) ?? Find.FactionManager.RandomEnemyFaction();
                    Pawn raider = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                        faction.def.basicMemberKind ?? PawnKindDefOf.Pirate, faction));
                    factionId = faction.loadID;
                    raiderId = s.Track(GenSpawn.Spawn(raider, OpenCell(map, down.Position, 6f, 10f), map)).ThingID;
                    LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                })
                .Expect("the raider's kidnap search finds the dog", () =>
                    KidnapAIUtility.TryFindGoodKidnapVictim(Raider(), 18f, out Pawn victim) && victim.ThingID == downId)
                .Do("the raider goes to carry it off", () =>
                {
                    if (!RCellFinder.TryFindBestExitSpot(Raider(), out IntVec3 exit))
                        throw new InvalidOperationException("no exit spot");
                    Job job = JobMaker.MakeJob(JobDefOf.Kidnap, Scenario.ById(downId), exit);
                    job.count = 1;
                    Raider().jobs.StartJob(job, JobCondition.InterruptForced);
                })
                .Expect("it picks the dog up; the stable keeps its dog docked", () =>
                {
                    if (!Stable().Docked.Any(d => d.ThingID == keptId))
                        throw new InvalidOperationException("the stable sent its dog out during the raid");
                    return Raider().carryTracker.CarriedThing?.ThingID == downId;
                })
                .SaveAndReload()
                .Expect("the raider leaves the map with it", () => Scenario.ById(raiderId) == null && Scenario.ById(downId) == null)
                .Expect("the dog is kidnapped by the raider's faction", () =>
                    RaiderFaction().kidnapped.KidnappedPawnsListForReading.Any(p => p.ThingID == downId))
                .Start();
        }

        /// <summary>A dog destroyed leaves its wreck; the rebuild bill brings the same dog back docked, across a reload.</summary>
        private static void WreckRebuilt()
        {
            var s = new Scenario("Wreck rebuilt", 20000);
            string stableId = null, dogId = null, name = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("a powered stable whose dog is destroyed beside it; steel and plasteel; a rebuild bill", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    // A stable's own dog (named when it joins), out beside it.
                    Pawn dog = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    stable.TryUndock(dog);
                    dogId = dog.ThingID;
                    name = dog.Name.ToStringShort;
                    dog.Kill(null);
                    if (dog.Corpse == null)
                        throw new InvalidOperationException("no wreck");
                    s.Track(dog.Corpse);
                    SpawnStack(s, ThingDefOf.Steel, 40, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Plasteel, 15, stable.InteractionCell);
                    stable.BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("QR_RebuildQuadrunner").MakeNewBill());
                })
                .Expect("the wreck doesn't count as one of the stable's dogs", () => Stable().Dogs().Count == 0)
                .Expect("a colonist takes the bill", () =>
                {
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    Job job = BillJob(crafter, Stable());
                    return job != null && crafter.jobs.TryTakeOrderedJob(job);
                })
                .Expect("the same dog docks into the stable", () => Stable().Docked.Any(d => d.ThingID == dogId))
                .SaveAndReload()
                .Expect("alive, same name, assigned, after the load", () =>
                    Stable().Docked.FirstOrDefault(d => d.ThingID == dogId) is Pawn d && !d.Dead && d.Name.ToStringShort == name
                    && CompRobodog.Of(d).stable == Stable())
                .Summary(() => $"rebuilt {name}")
                .Start();
        }
    }
}
