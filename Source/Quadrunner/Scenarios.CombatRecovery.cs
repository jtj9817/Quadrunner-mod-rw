using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>
    /// Plan 0007 / W2: Combatdog casualties report themselves to the elected node and are recovered after a war,
    /// ahead of ordinary work, by healthy workers and Medirunners.
    /// </summary>
    public static partial class Scenarios
    {
        /// <summary>The central casualty order for this dog's identity (living or wreck), if any.</summary>
        private static NetworkTask CasualtyTask(string casualtyId) =>
            Net().NormalQueue.FirstOrDefault(t => t.kind == NetworkTaskKind.Recover && t.casualtyId == casualtyId);

        private static int CasualtyTaskCount(string casualtyId) =>
            Net().NormalQueue.Count(t => t.kind == NetworkTaskKind.Recover && t.casualtyId == casualtyId);

        private static bool CarryingCasualty(Pawn carrier, string casualtyId)
        {
            if (carrier == null || !carrier.Spawned)
                return false;
            Thing cargo = carrier.carryTracker?.CarriedThing;
            return cargo is Pawn pawn && pawn.ThingID == casualtyId
                || cargo is Corpse corpse && corpse.InnerPawn?.ThingID == casualtyId;
        }

        private static bool CarryingAnyCasualty(Pawn carrier) =>
            carrier != null && carrier.Spawned
            && (carrier.carryTracker?.CarriedThing is Pawn
                || carrier.carryTracker?.CarriedThing is Corpse corpse && CompRobodog.IsRobodog(corpse.InnerPawn));

        /// <summary>A guaranteed downing by real injuries (armor would deflect TakeDamage).</summary>
        private static void DownByInjury(Pawn dog)
        {
            foreach (BodyPartRecord leg in dog.health.hediffSet.GetNotMissingParts()
                         .Where(p => p.def.defName == "MechanicalLeg").ToList())
            {
                Hediff wound = HediffMaker.MakeHediff(DamageDefOf.Blunt.hediff, dog, leg);
                wound.Severity = leg.def.GetMaxHealth(dog) * 0.95f;
                dog.health.AddHediff(wound, leg);
            }
            if (dog.Dead || !dog.Downed || InjurySeverity(dog) <= 0f)
                throw new InvalidOperationException($"{dog} wasn't downed by injuries");
        }

        /// <summary>A deferred medical emergency keeps its medic even if a fresh wreck is ready for recovery.</summary>
        private static void CombatRecoveryMedicalPriority()
        {
            var s = new Scenario("Combat recovery: deferred rescue priority", 30000);
            string stableId = null, medicId = null, patientId = null, casualtyId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Patient() => (Pawn)Scenario.ById(patientId);
            s.Do("defer a rescue while the only medic charges, then report a fresh Combatdog wreck", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Pawn medic = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner));
                    medicId = medic.ThingID;
                    CompRobodog.Of(medic).SetBattery(0.04f);
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer,
                        OpenCell(stable.Map, stable.InteractionCell, 16f, 24f));
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 2f);
                    SpawnMedicalBed(s, stable.InteractionCell, 4f, 10f);
                    Net().Tick(force: true);
                    NetworkTask rescue = Net().WarQueue.Single(t =>
                        t.kind == NetworkTaskKind.Rescue && t.targetId == patientId);
                    if (rescue.assigneeId != null || rescue.retryAt <= Find.TickManager.TicksGame)
                        throw new InvalidOperationException("the charging medic did not defer the rescue");
                    CompRobodog.Of(medic).SetBattery(1f);
                    Pawn casualty = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    casualtyId = casualty.ThingID;
                    stable.TryUndock(casualty);
                    Teleport(casualty, OpenCell(stable.Map, stable.InteractionCell, 28f, 38f));
                    casualty.Kill(null);
                    s.Track(casualty.Corpse ?? throw new InvalidOperationException("no casualty wreck"));
                    Net().Tick(force: true);
                    if (CasualtyTask(casualtyId) == null)
                        throw new InvalidOperationException("the fresh wreck was not reported");
                    if (CasualtyTask(casualtyId).assigneeId != null
                        || medic.CurJobDef == QR_DefOf.QR_CarryToStable)
                        throw new InvalidOperationException("wreck recovery took the only medic from a deferred emergency");
                })
                .Expect("automatic rescue retries before the medic takes any casualty recovery", () =>
                {
                    if (CasualtyTask(casualtyId)?.assigneeId != null)
                        throw new InvalidOperationException("recovery preempted the waiting medical emergency");
                    return Rescuing(Medic());
                })
                .Expect("the medic physically picks up the patient", () =>
                    Medic().carryTracker.CarriedThing == Patient())
                .SaveAndReload()
                .Expect("the patient reaches a bed before recovery", () => Patient().InBed())
                .Expect("the medic then carries the Combatdog wreck", () =>
                    CarryingCasualty(Medic(), casualtyId))
                .Expect("the same wreck reaches the stable", () =>
                    Stable().RecoveredWrecks.Any(c => c.InnerPawn?.ThingID == casualtyId))
                .Summary(() => $"medical rescue before recovery of {casualtyId}")
                .Start();
        }

        /// <summary>
        /// A lone worker, a dead Combatdog and an injured one far beyond its scan: both report to the elected node
        /// (which transfers twice), the backlog survives a wartime reload, a destroyed wreck leaves no phantom, and
        /// the living order is still carried to the actual stable afterwards.
        /// </summary>
        private static void CombatRecoveryReports()
        {
            var s = new Scenario("Combat recovery: reports", 50000);
            string workerId = null, injuredId = null, deadId = null, corpseId = null, raiderId = null, stableId = null;
            IntVec3 injuredAt = default, wreckAt = default;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            Pawn Injured() => (Pawn)Scenario.ById(injuredId);
            Pawn Raider() => (Pawn)Scenario.ById(raiderId);
            s.Do("a worker, then a dead Combatdog and a newer injured one, all beyond its scan radius", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    workerId = SpawnDog(s, OpenCell(map, map.Center, 25f, 30f)).ThingID;
                    Pawn dead = SpawnPawn(s, QR_DefOf.QR_Combatdog, Faction.OfPlayer,
                        OpenCell(map, Worker().Position, 25f, 35f));
                    deadId = dead.ThingID;
                    Pawn injured = SpawnPawn(s, QR_DefOf.QR_Combatdog, Faction.OfPlayer,
                        OpenCell(map, Worker().Position, 25f, 35f, c => c.DistanceTo(dead.Position) > 10f));
                    injuredId = injured.ThingID;
                    float radius = RobodogWork.Utility.scanRadius;
                    if (injured.Position.DistanceTo(Worker().Position) <= radius
                        || dead.Position.DistanceTo(Worker().Position) <= radius)
                        throw new InvalidOperationException("a casualty started inside the worker's scan radius");
                    // Elect the newest dog (the injured one) before it goes down, so the reports cross owners.
                    Net().Tick(force: true);
                    if (Net().Coordinator?.ThingID != injuredId)
                        throw new InvalidOperationException($"the newest dog should coordinate at first, not {Net().Coordinator}");
                    DownByInjury(injured);
                    injuredAt = injured.PositionHeld;
                    dead.Kill(null);
                    Corpse wreck = dead.Corpse ?? throw new InvalidOperationException("the dead Combatdog left no wreck");
                    s.Track(wreck);
                    corpseId = wreck.ThingID;
                    wreckAt = wreck.PositionHeld;
                })
                .Expect("both casualties reported beyond the scan: one order each, the last-known cells kept", () =>
                    CasualtyTaskCount(injuredId) == 1 && CasualtyTaskCount(deadId) == 1
                    && CasualtyTask(injuredId).lastKnown == injuredAt
                    && CasualtyTask(deadId).lastKnown == wreckAt
                    && !CasualtyTask(injuredId).casualtyDead && CasualtyTask(deadId).casualtyDead
                    && CasualtyTask(deadId).targetId == corpseId)
                .Expect("the downed coordinator handed the state to the only active dog", () =>
                    Net().Coordinator?.ThingID == workerId)
                .Do("a contained hostile starts a war", () =>
                {
                    Map map = Find.CurrentMap;
                    raiderId = SpawnContainedRaid(s, OpenCell(map, Worker().Position, 40f, 55f)).ThingID;
                    Raid.Recheck(map);
                    Net().Tick(force: true);
                    if (!Net().War)
                        throw new InvalidOperationException("the contained hostile did not start a war");
                })
                .Expect("the war holds both orders, with no carrier deployed", () =>
                    Net().War && CasualtyTask(injuredId)?.assigneeId == null
                    && CasualtyTask(deadId)?.assigneeId == null && !CarryingAnyCasualty(Worker()))
                .SaveAndReload()
                .Expect("the wartime backlog and the real identities survive the load", () =>
                    Net().War && CasualtyTaskCount(injuredId) == 1 && CasualtyTaskCount(deadId) == 1
                    && CasualtyTask(injuredId).lastKnown == injuredAt
                    && CasualtyTask(deadId).lastKnown == wreckAt
                    && CasualtyTask(deadId).casualtyDead && Injured()?.Downed == true
                    && Scenario.ById(corpseId) is Corpse saved
                    && saved.InnerPawn != null && saved.InnerPawn.ThingID == deadId)
                .Do("the hostile dies and the war ends", () =>
                {
                    Raider()?.Kill(null);
                    NetClearCorpses(s, raiderId);
                    Raid.Recheck(Find.CurrentMap);
                    Net().Tick(force: true);
                    if (Net().War)
                        throw new InvalidOperationException("the war outlived its hostile");
                })
                .Expect("without a receiving stable the orders are retained, never assigned", () =>
                    CasualtyTaskCount(injuredId) == 1 && CasualtyTaskCount(deadId) == 1
                    && CasualtyTask(injuredId).assigneeId == null && CasualtyTask(deadId).assigneeId == null)
                .Do("destroy the wreck while no carrier owns it, then refresh", () =>
                    {
                        Scenario.ById(corpseId)?.Destroy();
                        Net().Tick(force: true);
                    })
                .Expect("the dead order is gone with the real wreck; nothing revived or fabricated", () =>
                    CasualtyTaskCount(deadId) == 0
                    && !Find.CurrentMap.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>()
                        .Any(c => c.InnerPawn != null && c.InnerPawn.ThingID == deadId)
                    && Find.CurrentMap.mapPawns.AllPawns.All(p => p.ThingID != deadId)
                    && Find.CurrentMap.mapPawns.AllPawns.Count(p => !p.Dead
                        && RobodogWork.Of(p)?.combat == true) == 1)
                .Do("build the actual receiving stable", () =>
                {
                    stableId = SpawnStable(s, powered: true, near: Worker().Position, minDist: 8f, maxDist: 25f).ThingID;
                    Net().Tick(force: true);
                    if (Net().Coordinator?.ThingID != stableId)
                        throw new InvalidOperationException($"the stable did not take over the state: {Net().Coordinator}");
                })
                .Expect("the living order survived the transfer to the stable", () => CasualtyTaskCount(injuredId) == 1)
                .Expect("the injured Combatdog is carried to the actual stable and docked", () =>
                    Stable().Docked.Contains(Injured()))
                .Expect("its order is gone once docked", () => CasualtyTaskCount(injuredId) == 0)
                .Summary(() => $"wreck {wreckAt} -> destroyed; injured at {injuredAt}; " + Net().Inspect(Worker()))
                .Start();
        }

        /// <summary>
        /// After a war: a worker and a Medirunner recover an injured, an empty-battery and a dead Combatdog ahead of
        /// an adjacent gold haul, one carrier each, while the Medirunner keeps a downed colonist first. Delivery is
        /// to the actual stable; the wreck is stored, not rebuilt (no ingredients are supplied).
        /// </summary>
        private static void CombatRecoveryTransport()
        {
            var s = new Scenario("Combat recovery: transport", 80000);
            string stableId = null, workerId = null, medicId = null, injuredId = null, batteryId = null, deadId = null;
            string patientId = null, goldId = null, raiderId = null;
            CellRect pile = default;
            bool workerCarried = false, medicCarried = false;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            Pawn Medic() => (Pawn)Scenario.ById(medicId);
            Pawn Injured() => (Pawn)Scenario.ById(injuredId);
            Pawn Battery() => (Pawn)Scenario.ById(batteryId);
            Pawn Patient() => (Pawn)Scenario.ById(patientId);
            s.Do("a powered stable with a worker and a Medirunner; three far Combatdogs, a near gold haul and a patient", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    workerId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    medicId = s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    Pawn injured = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    stable.TryUndock(injured);
                    Teleport(injured, OpenCell(map, home, 25f, 35f));
                    injuredId = injured.ThingID;
                    Pawn battery = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    stable.TryUndock(battery);
                    Teleport(battery, OpenCell(map, home, 25f, 35f, c => c.DistanceTo(injured.Position) > 10f));
                    batteryId = battery.ThingID;
                    Pawn dead = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    stable.TryUndock(dead);
                    Teleport(dead, OpenCell(map, home, 25f, 35f, c =>
                        c.DistanceTo(injured.Position) > 10f && c.DistanceTo(battery.Position) > 10f));
                    deadId = dead.ThingID;
                    // The gold is beside the stable, far nearer than every casualty: only the kind priority can send
                    // an available carrier to a casualty first.
                    Thing gold = NetStack(s, ThingDefOf.Gold, 30, OpenCell(map, home, 1.5f, 4f));
                    goldId = gold.ThingID;
                    gold.SetForbidden(false);
                    pile = ClearRect(map, home, 3, 1, 6f, 10f);
                    Stockpile(s, pile, ThingDefOf.Gold);
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, OpenCell(map, home, 12f, 18f));
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 2f);
                    SpawnMedicalBed(s, home, 5f, 10f);

                    DownByInjury(injured);
                    CompRobodog.Of(battery).SetBattery(0f);
                    dead.Kill(null);
                    s.Track(dead.Corpse ?? throw new InvalidOperationException("no wreck"));
                    if (!Net().Report(NetworkTaskKind.Haul, gold, NetHaulGiver))
                        throw new InvalidOperationException("the near gold report was refused");
                })
                .Do("a contained hostile starts the war, which suspends every recovery and the haul", () =>
                    {
                        Map map = Find.CurrentMap;
                        raiderId = SpawnContainedRaid(s, OpenCell(map, Stable().InteractionCell, 40f, 55f)).ThingID;
                        Raid.Recheck(map);
                        Net().Tick(force: true);
                        if (!Net().War)
                            throw new InvalidOperationException("the contained hostile did not start a war");
                    })
                .Expect("war: all four ordinary orders wait and nobody carries a casualty", () =>
                    Net().War && CasualtyTask(injuredId)?.assigneeId == null
                    && CasualtyTask(batteryId)?.assigneeId == null && CasualtyTask(deadId)?.assigneeId == null
                    && NetTask(goldId)?.assigneeId == null && !CarryingAnyCasualty(Worker()))
                .Do("the war ends while the Medirunner is still answering the medical emergency", () =>
                    {
                        Scenario.ById(raiderId)?.Kill(null);
                        NetClearCorpses(s, raiderId);
                        Raid.Recheck(Find.CurrentMap);
                        Net().Tick(force: true);
                    })
                .Do("the medical emergency keeps the Medirunner; recovery goes to the worker before the near haul", () =>
                    {
                        if (Net().War)
                            throw new InvalidOperationException("the war outlived its hostile");
                        if (Medic() == null || !Rescuing(Medic()))
                            throw new InvalidOperationException($"{Medic()} isn't on the rescue: {Medic()?.CurJob.ToStringSafe()}");
                        List<NetworkTask> tasks = Net().NormalQueue
                            .Where(t => t.kind == NetworkTaskKind.Recover).ToList();
                        if (tasks.Any(t => t.assigneeId == medicId))
                            throw new InvalidOperationException("a recovery order was given to the rescuing Medirunner");
                        foreach (IGrouping<string, NetworkTask> group in tasks.Where(t => t.casualtyId != null)
                                     .GroupBy(t => t.casualtyId))
                            if (group.Count() > 1)
                                throw new InvalidOperationException($"duplicate casualty orders for {group.Key}");
                        List<string> assignees = tasks.Where(t => t.assigneeId != null).Select(t => t.assigneeId).ToList();
                        if (assignees.Count != assignees.Distinct().Count())
                            throw new InvalidOperationException("one carrier holds two casualty orders");
                        if (NetTask(goldId)?.assigneeId != null)
                            throw new InvalidOperationException("the near gold haul was assigned before the casualties");
                        if (tasks.All(t => t.assigneeId != workerId))
                            throw new InvalidOperationException("the worker didn't take a casualty ahead of the near haul");
                        if (CountIn(pile, ThingDefOf.Gold) != 0)
                            throw new InvalidOperationException("gold was delivered before the casualties");
                    })
                .SaveAndReload()
                .Expect("after the load: no war, the orders and the rescue still owned", () =>
                    !Net().War && Patient()?.Downed == true
                    && CasualtyTaskCount(injuredId) + CasualtyTaskCount(batteryId) + CasualtyTaskCount(deadId) >= 1
                    && Net().NormalQueue.Any(t => t.kind == NetworkTaskKind.Recover && t.assigneeId != null)
                    && NetTask(goldId)?.assigneeId == null)
                .Expect("the worker carries a Combatdog casualty", () =>
                {
                    workerCarried |= CarryingCasualty(Worker(), injuredId) || CarryingCasualty(Worker(), batteryId)
                        || CarryingCasualty(Worker(), deadId);
                    return workerCarried;
                })
                .Expect("once the patient is safe, the Medirunner carries a Combatdog casualty too", () =>
                {
                    medicCarried |= CarryingCasualty(Medic(), injuredId) || CarryingCasualty(Medic(), batteryId)
                        || CarryingCasualty(Medic(), deadId);
                    return medicCarried;
                })
                .Expect("both living Combatdogs are docked at the actual stable", () =>
                    Stable().Docked.Contains(Injured()) && Stable().Docked.Contains(Battery()))
                .Expect("the real wreck is in the stable's recovered wrecks, not rebuilt without materials", () =>
                    Stable().RecoveredWrecks.Any(c => c.InnerPawn != null && c.InnerPawn.ThingID == deadId)
                    && !Find.CurrentMap.mapPawns.AllPawns.Any(p => p.ThingID == deadId && !p.Dead))
                .Expect("every casualty order resolved exactly once", () =>
                    CasualtyTaskCount(injuredId) == 0 && CasualtyTaskCount(batteryId) == 0
                    && CasualtyTaskCount(deadId) == 0)
                .Expect("the ordinary haul resumes only after the casualties", () =>
                    CountIn(pile, ThingDefOf.Gold) == 30 && NetTask(goldId) == null)
                .Summary(() => $"worker carried {workerCarried}, medic carried {medicCarried}; " +
                    $"wrecks {Stable().RecoveredWrecks.Count}; {Net().Inspect(Worker())}")
                .Start();
        }

        /// <summary>
        /// A blocked casualty stays queued while a reachable one is recovered; with every carrier unhealthy the next
        /// order is retained; an interruption re-issues it; opening the route recovers the blocked dog.
        /// </summary>
        private static void CombatRecoveryFallback()
        {
            var s = new Scenario("Combat recovery: fallback", 60000);
            string stableId = null, workerId = null, openId = null, boxedId = null, wallId = null;
            string waitingId = null, interruptedId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Worker() => (Pawn)Scenario.ById(workerId);
            Pawn Open() => (Pawn)Scenario.ById(openId);
            Pawn Boxed() => (Pawn)Scenario.ById(boxedId);
            Pawn Waiting() => (Pawn)Scenario.ById(waitingId);
            Pawn Interrupted() => (Pawn)Scenario.ById(interruptedId);
            s.Do("a powered stable with one worker; a reachable downed Combatdog and one walled in", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    workerId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                    IntVec3 home = stable.InteractionCell;
                    Pawn open = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    stable.TryUndock(open);
                    Teleport(open, OpenCell(map, home, 15f, 25f));
                    openId = open.ThingID;
                    CellRect box = ClearRect(map, home, 3, 1, 20f, 30f);
                    foreach (IntVec3 c in box.EdgeCells)
                    {
                        Thing wall = SpawnBuilding(s, ThingDefOf.Wall, ThingDefOf.Steel, c, Rot4.North);
                        if (c == new IntVec3(box.CenterCell.x, 0, box.minZ))
                            wallId = wall.ThingID;
                    }
                    Pawn boxed = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    stable.TryUndock(boxed);
                    Teleport(boxed, box.CenterCell);
                    boxedId = boxed.ThingID;
                    DownByInjury(open);
                    DownByInjury(boxed);
                    Net().Tick(force: true);
                })
                .Expect("the reachable casualty is assigned; the blocked one stays queued", () =>
                    CasualtyTask(openId)?.assigneeId == workerId
                    && CasualtyTask(boxedId) != null && CasualtyTask(boxedId).assigneeId == null)
                .Expect("the reachable dog is docked", () => Stable().Docked.Contains(Open()))
                .Do("hurt the only worker, then report another casualty in the same frame", () =>
                    {
                        BodyPartRecord part = Worker().health.hediffSet.GetNotMissingParts()
                            .OrderByDescending(p => p.def.GetMaxHealth(Worker())).First();
                        Hediff wound = HediffMaker.MakeHediff(DamageDefOf.Blunt.hediff, Worker(), part);
                        wound.Severity = part.def.GetMaxHealth(Worker()) * 0.1f;
                        Worker().health.AddHediff(wound, part);
                        Pawn waiting = s.Track(Stable().AddNewDog(QR_DefOf.QR_Combatdog));
                        Stable().TryUndock(waiting);
                        Teleport(waiting, OpenCell(Find.CurrentMap, Stable().InteractionCell, 15f, 25f,
                            c => c.DistanceTo(Boxed().Position) > 10f));
                        waitingId = waiting.ThingID;
                        CompRobodog.Of(waiting).SetBattery(0f);
                        Net().Tick(force: true);
                    })
                .Expect("no healthy carrier: the new order is retained, not lost or given to a hurt dog", () =>
                    CasualtyTask(waitingId) != null && CasualtyTask(waitingId).assigneeId == null
                    && !CarryingAnyCasualty(Worker()) && InjurySeverity(Worker()) > 0f)
                .Do("heal the worker", () =>
                    {
                        foreach (Hediff injury in Worker().health.hediffSet.hediffs
                                     .Where(h => h is Hediff_Injury).ToList())
                            Worker().health.RemoveHediff(injury);
                        if (Worker().Spawned)
                            Worker().jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
                        Net().Tick(force: true);
                    })
                .Expect("healthy again, it takes the waiting casualty", () =>
                    CasualtyTask(waitingId)?.assigneeId == workerId || CarryingCasualty(Worker(), waitingId))
                .Expect("the empty-battery dog is docked and charging", () => Stable().Docked.Contains(Waiting()))
                .Do("a further reachable casualty is reported", () =>
                    {
                        Pawn interrupted = s.Track(Stable().AddNewDog(QR_DefOf.QR_Combatdog));
                        Stable().TryUndock(interrupted);
                        Teleport(interrupted, OpenCell(Find.CurrentMap, Stable().InteractionCell, 15f, 25f,
                            c => c.DistanceTo(Boxed().Position) > 10f));
                        interruptedId = interrupted.ThingID;
                        DownByInjury(interrupted);
                        Net().Tick(force: true);
                    })
                .Expect("the worker picks the free casualty up or heads for it", () =>
                    CarryingCasualty(Worker(), interruptedId) || Targets(Worker(), Interrupted()))
                .Do("interrupt the trip", () => Worker().jobs.EndCurrentJob(JobCondition.InterruptForced))
                .Expect("the network re-issues the interrupted order and the dog is docked", () =>
                    Stable().Docked.Contains(Interrupted()))
                .SaveAndReload()
                .Expect("the blocked order survives the load, still without a carrier", () =>
                    CasualtyTask(boxedId) != null && CasualtyTask(boxedId).assigneeId == null && Boxed()?.Downed == true)
                .Do("open the wall", () => Scenario.ById(wallId)?.Destroy())
                .Expect("the blocked casualty is recovered once a route exists", () => Stable().Docked.Contains(Boxed()))
                .Summary(() => $"open {Open()?.PositionHeld}; boxed {Boxed()?.PositionHeld}; " + Net().Inspect(Worker()))
                .Start();
        }
    }
}
