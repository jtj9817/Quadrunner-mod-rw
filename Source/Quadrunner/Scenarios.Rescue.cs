using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Quadrunner
{
    /// <summary>T5: Medirunners rescue the downed: rank order, fastest-bleeding first, one per patient, under fire.</summary>
    public static partial class Scenarios
    {
        /// <summary>
        /// Downs <paramref name="p"/> with anesthetic and a torso cut of severity <paramref name="cut"/>, which sets its
        /// bleeding; the cut, anesthetic and blood loss are taken off again at the end.
        /// </summary>
        private static void DownBleeding(Scenario s, Pawn p, float cut)
        {
            Hediff wound = HediffMaker.MakeHediff(HediffDefOf.Cut, p, p.RaceProps.body.corePart);
            wound.Severity = cut;
            p.health.AddHediff(wound);
            p.health.AddHediff(HediffDefOf.Anesthetic);
            if (!p.Downed)
                throw new InvalidOperationException($"{p} isn't downed");
            string id = p.ThingID;
            s.OnFinish(() =>
            {
                if (!(Scenario.ById(id) is Pawn q) || q.Destroyed)
                    return;
                foreach (Hediff h in q.health.hediffSet.hediffs.Where(h => h.def == HediffDefOf.Cut
                             || h.def == HediffDefOf.Anesthetic || h.def == HediffDefOf.BloodLoss).ToList())
                    q.health.RemoveHediff(h);
            });
        }

        private static Pawn SpawnPawn(Scenario s, PawnKindDef kind, Faction faction, IntVec3 cell) =>
            s.Track((Pawn)GenSpawn.Spawn(PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction,
                forceGenerateNewPawn: true)), cell, Find.CurrentMap));

        private static Faction Pirates() =>
            Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate) ?? Find.FactionManager.RandomEnemyFaction();

        /// <summary>A medical bed on clear ground <paramref name="minDist"/>–<paramref name="maxDist"/> cells from <paramref name="near"/>.</summary>
        private static Building_Bed SpawnMedicalBed(Scenario s, IntVec3 near, float minDist, float maxDist)
        {
            CellRect r = ClearRect(Find.CurrentMap, near, 3, 0, minDist, maxDist);
            var bed = (Building_Bed)SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel, r.CenterCell + IntVec3.South, Rot4.North);
            bed.Medical = true;
            return bed;
        }

        private static bool Rescuing(Pawn medic) => medic.Spawned
            && (medic.CurJobDef == JobDefOf.Rescue || medic.CurJobDef == JobDefOf.TakeWoundedPrisonerToBed);

        private static void ManualRescueDispatch()
        {
            var s = new Scenario("Manual rescue dispatch", 12000);
            string stableId = null;
            int pausedTick = 0;
            var medicIds = new List<string>();
            var patientIds = new List<string>();
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic(int i) => (Pawn)Scenario.ById(medicIds[i]);
            Pawn Patient(int i) => (Pawn)Scenario.ById(patientIds[i]);
            Command_Action Dispatch() => Stable().GetGizmos().OfType<Command_Action>()
                .Single(c => c.defaultLabel == "Dispatch Rescuedogs");
            s.Do("pause with an unpowered stable and no medics", () =>
                {
                    DraftColonists(s);
                    int width = UnityEngine.Screen.width, height = UnityEngine.Screen.height;
                    UnityEngine.Screen.SetResolution(1280, 720, false);
                    s.OnFinish(() => UnityEngine.Screen.SetResolution(width, height, false));
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    stableId = SpawnStable(s, powered: false).ThingID;
                    if (!Dispatch().Disabled)
                        throw new InvalidOperationException("Dispatch enabled without a Medirunner");
                    medicIds.Add(s.Track(Stable().AddNewDog(QR_DefOf.QR_Medirunner)).ThingID);
                    CompRobodog.Of(Medic(0)).SetBattery(0.04f);
                    if (!Dispatch().Disabled)
                        throw new InvalidOperationException("Dispatch enabled with only a charging Medirunner");
                    CompRobodog.Of(Medic(0)).SetBattery(1f);
                    if (!Dispatch().Disabled)
                        throw new InvalidOperationException("Dispatch enabled without patients");
                    medicIds.Add(s.Track(Stable().AddNewDog(QR_DefOf.QR_Medirunner)).ThingID);
                    for (int i = 0; i < 2; i++)
                    {
                        SpawnMedicalBed(s, Stable().InteractionCell, 3f, 12f);
                        Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer,
                            OpenCell(Find.CurrentMap, Stable().InteractionCell, 20f, 28f));
                        patientIds.Add(patient.ThingID);
                        DownBleeding(s, patient, 2f + i);
                    }
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(Stable(), playSound: false);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                    Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 12f);
                });
            s.Do("queue patients while medics charge; automatic retry still waits after recharging", () =>
                {
                    pausedTick = Find.TickManager.TicksGame;
                    for (int i = 0; i < 2; i++)
                        CompRobodog.Of(Medic(i)).SetBattery(0.04f);
                    DogNetwork network = DogNetwork.For(Find.CurrentMap);
                    network.Tick(force: true);
                    for (int i = 0; i < 2; i++)
                    {
                        string patientId = patientIds[i];
                        NetworkTask task = network.WarQueue.Single(t =>
                            t.kind == NetworkTaskKind.Rescue && t.targetId == patientId);
                        if (task.assigneeId != null || task.retryAt <= pausedTick || Rescuing(Medic(i)))
                            throw new InvalidOperationException("Unavailable medics did not leave a deferred rescue");
                        CompRobodog.Of(Medic(i)).SetBattery(1f);
                    }
                    network.Tick(force: true);
                    if (Rescuing(Medic(0)) || Rescuing(Medic(1)))
                        throw new InvalidOperationException("Automatic dispatch bypassed rescue retry backoff");
                });
            CaptureArtwork(s, "manual-rescue-ready", () => { });
            s.Do("dispatch immediately without advancing simulation", () =>
                {
                    Command_Action command = Dispatch();
                    if (command.Disabled)
                        throw new InvalidOperationException(command.disabledReason);
                    command.ProcessInput(new UnityEngine.Event());
                    if (Find.TickManager.TicksGame != pausedTick || !Rescuing(Medic(0)) || !Rescuing(Medic(1))
                        || Medic(0).CurJob.targetA.Thing == Medic(1).CurJob.targetA.Thing)
                        throw new InvalidOperationException("Manual dispatch did not immediately assign distinct patients");
                    int first = Medic(0).CurJob.loadID, second = Medic(1).CurJob.loadID;
                    command.action();
                    if (Medic(0).CurJob.loadID != first || Medic(1).CurJob.loadID != second)
                        throw new InvalidOperationException("Repeated dispatch interrupted an active rescue");
                });
            CaptureArtwork(s, "manual-rescue-dispatched", () => { });
            s.Do("resume the rescue trips", () => Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast)
                .Expect("a Medirunner carries a patient", () =>
                    Medic(0).carryTracker.CarriedThing == Patient(0) || Medic(0).carryTracker.CarriedThing == Patient(1)
                    || Medic(1).carryTracker.CarriedThing == Patient(0) || Medic(1).carryTracker.CarriedThing == Patient(1))
                .SaveAndReload()
                .Expect("both manually dispatched patients reach beds", () => Patient(0).InBed() && Patient(1).InBed())
                .Expect("both Medirunners return to the unpowered stable", () =>
                    Stable().Docked.Contains(Medic(0)) && Stable().Docked.Contains(Medic(1)))
                .OnFinish(() => Find.Selector.ClearSelection())
                .Start();
        }

        /// <summary>
        /// The plan's scenario: three colonists down, bleeding at different rates, by a live raider (walled in, so it
        /// can't hurt anyone, yet the raid is on and the enemy near); one Medirunner takes them to bed fastest-bleeding
        /// first, across a reload while it carries the first.
        /// </summary>
        private static void MassCasualtyRaid()
        {
            var s = new Scenario("Mass-casualty raid", 30000);
            string stableId = null, medicId = null;
            var patientIds = new List<string>();
            string bleeds = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Patient(int i) => (Pawn)Scenario.ById(patientIds[i]);
            s.Do("a stable with a Medirunner, three medical beds; three colonists down 18–28 cells out, a raider walled in by them", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    for (int i = 0; i < 3; i++)
                        SpawnMedicalBed(s, stable.InteractionCell, 3f, 12f);
                    IntVec3 field = OpenCell(map, stable.InteractionCell, 18f, 28f);
                    List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).Take(3).ToList();
                    while (colonists.Count < 3)
                        colonists.Add(SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, field));
                    // Cut severities 3, 9, 6: the second bleeds fastest, then the third, then the first.
                    float[] cuts = { 3f, 9f, 6f };
                    for (int i = 0; i < 3; i++)
                    {
                        Teleport(colonists[i], OpenCell(map, field, 0f, 4f));
                        DownBleeding(s, colonists[i], cuts[i]);
                    }
                    patientIds.AddRange(colonists.OrderByDescending(p => p.health.hediffSet.BleedRateTotal).Select(p => p.ThingID));
                    bleeds = string.Join(", ", colonists.OrderByDescending(p => p.health.hediffSet.BleedRateTotal)
                        .Select(p => $"{p.LabelShort} {p.health.hediffSet.BleedRateTotal:F2}"));
                    CellRect box = ClearRect(map, field, 3, 1, 6f, 14f);
                    foreach (IntVec3 c in box.EdgeCells)
                        SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                    Faction pirates = Pirates();
                    Pawn raider = SpawnPawn(s, pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate, pirates, box.CenterCell);
                    raider.equipment?.DestroyAllEquipment();
                    raider.inventory?.DestroyAll();
                    LordMaker.MakeNewLord(pirates, new LordJob_DefendPoint(raider.Position), map, new[] { raider });
                })
                .Expect("a raid is on, the enemy near the patients", () =>
                    Raid.Active(Find.CurrentMap) && GenAI.EnemyIsNear(Patient(0), 25f))
                .Expect("the Medirunner goes for the fastest-bleeding first", () =>
                    Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(0))
                .Expect("it carries them", () => Medic().carryTracker.CarriedThing == Patient(0))
                .SaveAndReload()
                .Expect("first patient in bed", () => Patient(0).InBed())
                .Expect("then the next-fastest", () => Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(1))
                .Expect("second patient in bed", () => Patient(1).InBed())
                .Expect("then the last", () => Rescuing(Medic()) && Medic().CurJob.targetA.Thing == Patient(2))
                .Expect("third patient in bed", () => Patient(2).InBed())
                .Expect("the Medirunner docks", () => Stable().Docked.Contains(Medic()))
                .Summary(() => $"bleed rates {bleeds}")
                .Start();
        }

        /// <summary>
        /// A colonist, a guest, a prisoner, a colony animal and an enemy down at once, the prisoner bleeding fastest and
        /// the colonist slowest; two Medirunners: two patients at once, rescues started in rank order regardless of
        /// bleeding, the prisoner to the prison cell, the enemy never; a reload partway.
        /// </summary>
        private static void RescueOrder()
        {
            var s = new Scenario("Rescue order", 30000);
            string stableId = null, enemyId = null;
            var medicIds = new List<string>();
            var rankIds = new List<string>(); // colonist, guest, prisoner, animal
            var started = new Dictionary<string, int>();
            bool together = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            List<Pawn> Medics() => medicIds.Select(id => (Pawn)Scenario.ById(id)).ToList();
            Pawn Ranked(int i) => (Pawn)Scenario.ById(rankIds[i]);
            Pawn Enemy() => (Pawn)Scenario.ById(enemyId);
            void Watch()
            {
                List<Thing> targets = Medics().Where(Rescuing).Select(m => m.CurJob.targetA.Thing).ToList();
                foreach (Thing t in targets.Where(t => !started.ContainsKey(t.ThingID)))
                    started[t.ThingID] = Find.TickManager.TicksGame;
                together |= targets.Count == 2 && targets[0] != targets[1];
            }
            s.Do("a stable with two Medirunners; two medical beds, a prison cell, an animal spot; five pawns down 20–30 cells out", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 2; i++)
                        medicIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID);
                    for (int i = 0; i < 2; i++)
                        SpawnMedicalBed(s, stable.InteractionCell, 3f, 12f);
                    // The prisoner bed goes in before the walls, so the new room is a prison cell from the start.
                    CellRect cell = ClearRect(map, stable.InteractionCell, 5, 1, 6f, 16f);
                    var prisonBed = (Building_Bed)SpawnBuilding(s, ThingDefOf.Bed, ThingDefOf.Steel,
                        new IntVec3(cell.minX + 1, 0, cell.minZ + 1), Rot4.North);
                    prisonBed.ForPrisoners = true;
                    BuildRoom(s, cell, batteryPct: -1f);
                    SpawnBuilding(s, DefDatabase<ThingDef>.GetNamed("AnimalSleepingSpot"), null,
                        OpenCell(map, stable.InteractionCell, 3f, 10f), Rot4.North);

                    IntVec3 field = OpenCell(map, stable.InteractionCell, 20f, 30f);
                    IntVec3 Near() => OpenCell(map, field, 0f, 5f);
                    Pawn colonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p => !p.Downed)
                        ?? SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, field);
                    Teleport(colonist, Near());
                    Faction friendly = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => !f.IsPlayer
                        && !f.Hidden && f.def.humanlikeFaction && f.def.basicMemberKind != null && !f.HostileTo(Faction.OfPlayer));
                    Pawn guest = SpawnPawn(s, friendly?.def.basicMemberKind ?? PawnKindDefOf.Colonist, friendly, Near());
                    Faction pirates = Pirates();
                    PawnKindDef pirate = pirates.def.basicMemberKind ?? PawnKindDefOf.Pirate;
                    Pawn prisoner = SpawnPawn(s, pirate, pirates, Near());
                    prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                    Pawn animal = SpawnPawn(s, DefDatabase<PawnKindDef>.GetNamed("Husky"), Faction.OfPlayer, Near());
                    Pawn enemy = SpawnPawn(s, pirate, pirates, Near());
                    enemy.equipment?.DestroyAllEquipment();
                    // Bleeding against the rank order: the prisoner fastest, the colonist slowest.
                    DownBleeding(s, colonist, 2f);
                    DownBleeding(s, guest, 6f);
                    DownBleeding(s, prisoner, 10f);
                    DownBleeding(s, animal, 4f);
                    DownBleeding(s, enemy, 8f);
                    rankIds.AddRange(new[] { colonist, guest, prisoner, animal }.Select(p => p.ThingID));
                    enemyId = enemy.ThingID;
                    if (new[] { colonist, guest, prisoner, animal }.Select(Rescue.Rank).SequenceEqual(new[] { 0, 1, 2, 3 }) == false
                        || Rescue.Rank(enemy) != -1)
                        throw new InvalidOperationException("ranks: " + string.Join(", ",
                            new[] { colonist, guest, prisoner, animal, enemy }.Select(p => $"{p.LabelShort} {Rescue.Rank(p)}")));
                })
                .Expect("both Medirunners out at once, on two different patients", () =>
                {
                    Watch();
                    return together;
                })
                .SaveAndReload()
                .Expect("colonist, guest, prisoner and animal all in bed", () =>
                {
                    Watch();
                    return Enumerable.Range(0, 4).All(i => Ranked(i).InBed());
                })
                .Expect("rescues started in rank order, bleeding aside", () =>
                {
                    List<int> ticks = rankIds.Select(id => started.TryGetValue(id, out int t) ? t : -1).ToList();
                    if (ticks.Contains(-1) || Enumerable.Range(1, 3).Any(i => ticks[i] < ticks[i - 1]))
                        throw new InvalidOperationException("start ticks (colonist, guest, prisoner, animal): " + string.Join(", ", ticks));
                    return true;
                })
                .Expect("the prisoner lies in the prison cell", () => Ranked(2).CurrentBed() is Building_Bed b && b.ForPrisoners)
                .Expect("both Medirunners dock", () => Medics().All(m => Stable().Docked.Contains(m)))
                .Expect("the enemy is left where it fell", () => !started.ContainsKey(enemyId) && Enemy().Spawned && !Enemy().InBed())
                .Summary(() => "start ticks " + string.Join(", ", rankIds.Select(id => started[id])))
                .Start();
        }

        /// <summary>
        /// Regression: unassignable queue entries (a downed colony animal with no bed, a powered-down Combatdog
        /// awaiting an in-stock field battery, and a walled-in Home fire) must never repeatedly deploy docked
        /// Medirunners in front of the stable every retry cycle; once one task becomes actionable, only the single
        /// needed Medirunner deploys while surplus docked Medirunners stay inside.
        /// </summary>
        private static void MedirunnerIdleDeploymentGuard()
        {
            var s = new Scenario("Medirunner idle deployment guard", 35000);
            string stableId = null, huskyId = null, combatId = null, wallToOpenId = null, failure = null;
            var medicIds = new List<string>();
            var jobStarts = new List<string>();
            CellRect fireBox = default;
            int startedAt = 0;
            bool zeroSpawnPhase = true, allowSingleDeploy = false, allowFireDeploy = false;
            string deployedForAnimal = null, deployedForFire = null, forbiddenDoorId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic(int i) => (Pawn)Scenario.ById(medicIds[i]);
            Pawn Husky() => (Pawn)Scenario.ById(huskyId);
            Pawn Combat() => (Pawn)Scenario.ById(combatId);
            void CheckGuard()
            {
                if (failure != null)
                    throw new InvalidOperationException(failure);
                for (int i = 0; i < medicIds.Count; i++)
                {
                    Pawn m = Medic(i);
                    if (m == null)
                        continue;
                    int dt = Find.TickManager.TicksGame - startedAt;
                    if (!allowSingleDeploy && !allowFireDeploy && (m.Spawned || zeroSpawnPhase && m.TickSpawned >= 0))
                        throw new InvalidOperationException(
                            $"spurious Medirunner deployment at +{dt} ticks with no actionable emergency: {m} spawned={m.Spawned} tickSpawned={m.TickSpawned} job={m.CurJob}");
                    if (allowSingleDeploy && !allowFireDeploy && m.Spawned)
                    {
                        deployedForAnimal ??= m.ThingID;
                        if (m.ThingID != deployedForAnimal)
                            throw new InvalidOperationException(
                                $"surplus Medirunner {m} also deployed for a single rescue (already handled by {deployedForAnimal}) at +{dt} ticks");
                    }
                    if (allowFireDeploy && m.Spawned)
                    {
                        deployedForFire ??= m.ThingID;
                        if (m.ThingID != deployedForFire)
                            throw new InvalidOperationException(
                                $"surplus Medirunner {m} also deployed for a single fire (already handled by {deployedForFire}) at +{dt} ticks");
                    }
                }
            }
            void AttachObserver()
            {
                ObservePackJobs(s, (pawn, job) =>
                {
                    // Docked pawns inside the stable receive vanilla's Despawned Carried job; observe spawned field jobs.
                    if (!pawn.Spawned || !medicIds.Contains(pawn.ThingID))
                        return;
                    int dt = Find.TickManager.TicksGame - startedAt;
                    jobStarts.Add($"+{dt} {pawn.ThingID}:{job.def.defName}");
                    if (!allowSingleDeploy && !allowFireDeploy)
                        failure ??= $"spurious Medirunner job at +{dt} ticks with no actionable emergency: {pawn} -> {job}";
                    else if (allowSingleDeploy && !allowFireDeploy)
                    {
                        deployedForAnimal ??= pawn.ThingID;
                        if (pawn.ThingID != deployedForAnimal)
                            failure ??= $"surplus Medirunner {pawn} took job {job} during single animal rescue by {deployedForAnimal} at +{dt} ticks";
                    }
                    else if (allowFireDeploy)
                    {
                        deployedForFire ??= pawn.ThingID;
                        if (pawn.ThingID != deployedForFire)
                            failure ??= $"surplus Medirunner {pawn} took job {job} during single fire response by {deployedForFire} at +{dt} ticks";
                    }
                });
            }
            s.Do("two docked Medirunners, a no-bed downed animal, a battery-awaiting Combatdog, and a sealed Home fire", () =>
                {
                    Map map = Find.CurrentMap;
                    WeatherDef priorCur = map.weatherManager.curWeather, priorLast = map.weatherManager.lastWeather;
                    map.weatherDecider.DisableRainFor(GenDate.TicksPerDay);
                    map.weatherManager.curWeather = map.weatherManager.lastWeather = WeatherDefOf.Clear;
                    s.OnFinish(() =>
                    {
                        Map now = Find.CurrentMap;
                        if (now == null)
                            return;
                        now.weatherManager.curWeather = priorCur;
                        now.weatherManager.lastWeather = priorLast;
                        foreach (Fire f in now.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().ToList())
                            f.Destroy();
                        foreach (Thing foam in now.listerThings.ThingsOfDef(ThingDefOf.Filth_FireFoam).ToList())
                            foam.Destroy();
                    });
                    DraftColonists(s);
                    // Ensure no stray animal beds exist on the quicktest map.
                    foreach (Building_Bed bed in map.listerThings.ThingsInGroup(ThingRequestGroup.Bed)
                                 .OfType<Building_Bed>().Where(b => !b.def.building.bed_humanlike).ToList())
                        bed.Destroy();
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 2; i++)
                        medicIds.Add(s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID);
                    IntVec3 door = stable.InteractionCell;
                    // 1) Downed colony animal with no animal bed anywhere on the map.
                    Pawn husky = SpawnPawn(s, DefDatabase<PawnKindDef>.GetNamed("Husky"), Faction.OfPlayer,
                        OpenCell(map, door, 16f, 24f));
                    husky.health.AddHediff(HediffDefOf.Anesthetic);
                    huskyId = husky.ThingID;
                    // 2) Powered-down Combatdog while a robodog battery is in stock (AwaitsFieldFix == true).
                    IntVec3 stockCell = OpenCell(map, door, 6f, 10f);
                    Zone_Stockpile stock = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(stock);
                    stock.AddCell(stockCell);
                    s.OnFinish(() => { if (Find.CurrentMap?.zoneManager.AllZones.Contains(stock) == true) stock.Delete(); });
                    SpawnStack(s, QR_DefOf.QR_RobodogBattery, 1, stockCell);
                    Pawn combat = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    combatId = combat.ThingID;
                    stable.TryUndock(combat);
                    Teleport(combat, OpenCell(map, door, 20f, 28f));
                    CompRobodog.Of(combat).SetBattery(0f);
                    if (!Retrieval.AwaitsFieldFix(combat))
                        throw new InvalidOperationException("fixture: powered-down Combatdog is not awaiting a field battery");
                    // 3) Burning Home fire sealed inside a non-flammable 5x5 stone room so flames cannot spread to walls.
                    ThingDef granite = DefDatabase<ThingDef>.GetNamed("BlocksGranite");
                    fireBox = ClearRect(map, door, 5, 1, 12f, 20f);
                    foreach (IntVec3 c in fireBox.ExpandedBy(2).ClipInsideMap(map))
                    {
                        bool was = map.areaManager.Home[c];
                        map.areaManager.Home[c] = true;
                        s.OnFinish(() => { if (Find.CurrentMap != null) Find.CurrentMap.areaManager.Home[c] = was; });
                    }
                    foreach (IntVec3 c in fireBox)
                        foreach (Thing plant in c.GetThingList(map).Where(t => t.def.category == ThingCategory.Plant).ToList())
                            plant.Destroy();
                    foreach (IntVec3 c in fireBox.EdgeCells)
                    {
                        Thing wall = SpawnBuilding(s, ThingDefOf.Wall, granite, c, Rot4.North);
                        if (wallToOpenId == null && (c.x == fireBox.CenterCell.x || c.z == fireBox.CenterCell.z))
                            wallToOpenId = wall.ThingID;
                    }
                    SpawnStack(s, ThingDefOf.WoodLog, 75, fireBox.CenterCell);
                    if (!FireUtility.TryStartFireIn(fireBox.CenterCell, map, 1f, null))
                        throw new InvalidOperationException("fixture: could not start the sealed Home fire");
                    startedAt = Find.TickManager.TicksGame;
                    AttachObserver();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    Net().Tick(force: true);
                    if (!Net().WarQueue.Any(t => t.kind == NetworkTaskKind.Rescue && t.targetId == huskyId)
                        || !Net().WarQueue.Any(t => t.kind == NetworkTaskKind.Fire)
                        || !Net().NormalQueue.Any(t => t.kind == NetworkTaskKind.Recover && t.casualtyId == combatId))
                        throw new InvalidOperationException("fixture: expected queued unassignable rescue, fire, and recovery tasks");
                    CheckGuard();
                })
                .Wait(450)
                .Do("check neither Medirunner deployed across multiple retry cycles", CheckGuard)
                .SaveAndReload()
                .Do("keep normal speed and guard checks after reload", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    CheckGuard();
                })
                .Wait(240)
                .Do("still docked after reload; now place one animal sleeping spot", () =>
                {
                    CheckGuard();
                    zeroSpawnPhase = false;
                    allowSingleDeploy = true;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                    SpawnBuilding(s, DefDatabase<ThingDef>.GetNamed("AnimalSleepingSpot"), null,
                        OpenCell(Find.CurrentMap, Stable().InteractionCell, 4f, 9f), Rot4.North);
                })
                .Expect("exactly one Medirunner deploys and rescues the Husky while the second stays docked", () =>
                {
                    CheckGuard();
                    Pawn surplus = medicIds.Select(id => (Pawn)Scenario.ById(id)).FirstOrDefault(m => m.ThingID != deployedForAnimal);
                    if (deployedForAnimal != null && surplus != null && surplus.TickSpawned >= 0)
                        throw new InvalidOperationException($"surplus Medirunner {surplus} spawned at tick {surplus.TickSpawned}");
                    return Husky().InBed() && Stable().Docked.Contains(Medic(0)) && Stable().Docked.Contains(Medic(1));
                })
                .Do("clear the battery-awaiting Combatdog before its 2500-tick field-fix window expires, then test a forbidden door", () =>
                {
                    allowSingleDeploy = false;
                    CompRobodog.Of(Combat()).SetBattery(1f);
                    Stable().TryDock(Combat());
                    Thing wall = Scenario.ById(wallToOpenId);
                    IntVec3 doorCell = wall.Position;
                    wall.Destroy();
                    Building_Door door = (Building_Door)SpawnBuilding(s, ThingDefOf.Door,
                        DefDatabase<ThingDef>.GetNamed("BlocksGranite"), doorCell, Rot4.North);
                    door.SetForbidden(true);
                    forbiddenDoorId = door.ThingID;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    Net().Tick(force: true);
                    CheckGuard();
                })
                .Wait(200)
                .Do("unforbid the door to make the Home fire reachable", () =>
                {
                    CheckGuard();
                    allowFireDeploy = true;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Ultrafast;
                    ((Building_Door)Scenario.ById(forbiddenDoorId)).SetForbidden(false);
                })
                .Expect("one Medirunner extinguishes the newly reachable fire and redocks while the other stays docked", () =>
                {
                    CheckGuard();
                    return !FireIn(fireBox) && Stable().Docked.Contains(Medic(0)) && Stable().Docked.Contains(Medic(1));
                })
                .Do("restore Combatdog battery for clean teardown", () => CompRobodog.Of(Combat())?.SetBattery(1f))
                .Summary(() => $"animal medic={deployedForAnimal ?? "none"}, fire medic={deployedForFire ?? "none"}, jobs=[{string.Join(", ", jobStarts)}]")
                .Start();
        }
    }
}
