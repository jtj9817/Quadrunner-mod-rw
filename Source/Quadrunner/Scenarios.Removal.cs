using System;
using System.IO;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>T9: confirmed removal export and the tools/autotest.py removal-check no-assembly proof.</summary>
    public static partial class Scenarios
    {
        /// <summary>
        /// The prepared export loaded by the independent no-Quadrunner process. The scenario also keeps a raw
        /// QR_removal_raw save so missing-class/Def behavior can be observed without confusing it with clean removal.
        /// </summary>
        public const string RemovalSaveName = "QR_removal";

        /// <summary>
        /// Stages bills, held and deployed dogs, a powered-down dog and an actual mid-ride colonist across reload.
        /// The same production removal action must preserve a carried patient, vanilla cargo and an existing wound.
        /// </summary>
        private static void RemovalSave()
        {
            var s = new Scenario("Prepare removal", 12000);
            string stableId = null, riderId = null, rideDogId = null, downDogId = null, walkDogId = null, crafterId = null;
            string patientId = null, cargoId = null, dockCargoId = null, travelerId = null, worldCargoId = null, backup = null;
            string[] removedIds = null;
            float woundSeverity = 0f;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Rider() => (Pawn)Scenario.ById(riderId);
            Pawn RideDog() => (Pawn)Scenario.ById(rideDogId);
            Pawn DownDog() => (Pawn)Scenario.ById(downDogId);
            Pawn WalkDog() => (Pawn)Scenario.ById(walkDogId);
            s.Do("a powered stable with bills, docked dogs, a powered-down dog, items and a mid-ride colonist", () =>
                {
                    Map map = Find.CurrentMap;
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    // Bills: an assemble bill (worked on below), the two field-fix bills, a Combatdog upgrade.
                    stable.BillStack.AddBill(QR_DefOf.QR_AssembleQuadrunner.MakeNewBill());
                    stable.BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("QR_MakeRobodogBattery").MakeNewBill());
                    stable.BillStack.AddBill(DefDatabase<RecipeDef>.GetNamed("QR_MakeRobodogRepairKit").MakeNewBill());
                    // Docked dogs of every kind; a pending upgrade bill on the Combatdog.
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Medirunner));
                    Pawn combat = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog));
                    if (stable.OrderUpgrade(combat) == null)
                        throw new InvalidOperationException("the Combatdog upgrade order failed");
                    // A dog walking home to dock: a mod JobDef on a pawn.
                    Pawn walkDog = stable.AddNewDog(QR_DefOf.QR_Quadrunner);
                    walkDogId = s.Track(walkDog).ThingID;
                    if (!stable.TryUndock(walkDog))
                        throw new InvalidOperationException("TryUndock failed");
                    IntVec3 far;
                    if (!CellFinder.TryFindRandomCellNear(stable.Position, map, 25,
                            c => c.Standable(map) && c.DistanceTo(stable.Position) >= 20f
                                && walkDog.CanReach(c, PathEndMode.OnCell, Danger.Deadly), out far))
                        throw new InvalidOperationException("no reachable cell 20 cells from the stable");
                    walkDog.Position = far;
                    walkDog.Notify_Teleported();
                    walkDog.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    // A colonist mid-bill on the stable (a vanilla DoBill job whose bill lives on the stable).
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no crafter");
                    crafterId = crafter.ThingID;
                    crafter.drafter.Drafted = false;
                    SpawnAssembleMaterials(s, stable, QR_DefOf.QR_AssembleQuadrunner);
                    // A powered-down dog lying out in the open, its QR_PoweredDown hediff on, with the items beside it.
                    Pawn downDog = stable.AddNewDog(QR_DefOf.QR_Quadrunner);
                    downDogId = s.Track(downDog).ThingID;
                    if (!stable.TryUndock(downDog))
                        throw new InvalidOperationException("TryUndock failed");
                    IntVec3 downAt = OpenCell(map, stable.InteractionCell, 15f, 25f);
                    Teleport(downDog, downAt);
                    CompRobodog.Of(downDog).SetBattery(0f);
                    SpawnStack(s, QR_DefOf.QR_RobodogBattery, 1, downAt);
                    SpawnStack(s, QR_DefOf.QR_RobodogRepairKit, 1, downAt);
                    // A colonist mid-ride on a fresh Quadrunner, far enough to still be riding at the save.
                    Pawn rideDog = stable.AddNewDog(QR_DefOf.QR_Quadrunner);
                    rideDogId = s.Track(rideDog).ThingID;
                    Pawn rider = map.mapPawns.FreeColonistsSpawned
                        .FirstOrDefault(p => p != crafter && !p.Downed && p.drafter != null)
                        ?? SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, stable.InteractionCell);
                    rider.drafter.Drafted = false;
                    Teleport(rider, OpenCell(map, stable.InteractionCell, 3f, 8f));
                    riderId = rider.ThingID;
                    IntVec3 dest = OpenCell(map, stable.InteractionCell, 40f, 60f);
                    if (!Riding.Ride(rider, rideDog, stable, dest))
                        throw new InvalidOperationException("the ride order failed");
                })
                .Expect("a colonist takes the bill after the power net connects", () =>
                {
                    Pawn crafter = (Pawn)Scenario.ById(crafterId);
                    Job job = BillJob(crafter, Stable());
                    return job != null && crafter.jobs.TryTakeOrderedJob(job);
                })
                .Expect("the rider is mounted, mid-ride", () => Riding.MountOf(Rider()) == RideDog())
                .Expect("the walked-out dog heads home to dock", () => WalkDog().CurJobDef == QR_DefOf.QR_Dock)
                .Expect("the powered-down dog is downed with the hediff", () =>
                    DownDog().Downed && DownDog().health.hediffSet.GetFirstHediffOfDef(QR_DefOf.QR_PoweredDown) != null)
                .Do("save the raw pre-removal colony", () =>
                    GameDataSaveLoader.SaveGame("QR_removal_raw"))
                .SaveAndReload()
                .Expect("still riding after the reload", () => Riding.MountOf(Rider()) == RideDog())
                .Do("prepare removal with a carried patient, vanilla cargo and a lasting wound", () =>
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Map map = Find.CurrentMap;
                    Pawn medic = s.Track(Stable().AddNewDog(QR_DefOf.QR_Medirunner));
                    Stable().TryUndock(medic);
                    medic.jobs.StopAll();
                    Pawn patient = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, medic.Position);
                    patientId = patient.ThingID;
                    DownBleeding(s, patient, 2f);
                    if (medic.carryTracker.TryStartCarry(patient, 1, reserve: false) != 1)
                        throw new InvalidOperationException("could not stage the carried patient");
                    Pawn hauler = s.Track(Stable().AddNewDog(QR_DefOf.QR_Quadrunner));
                    Stable().TryUndock(hauler);
                    hauler.jobs.StopAll();
                    Thing cargo = SpawnStack(s, ThingDefOf.Gold, 20, hauler.Position);
                    cargoId = cargo.ThingID;
                    if (hauler.carryTracker.TryStartCarry(cargo, 20, reserve: false) != 20)
                        throw new InvalidOperationException("could not stage the vanilla cargo");
                    Thing dockCargo = ThingMaker.MakeThing(ThingDefOf.Silver);
                    dockCargo.stackCount = 7;
                    dockCargoId = dockCargo.ThingID;
                    if (!Stable().Docked[0].inventory.innerContainer.TryAdd(dockCargo))
                        throw new InvalidOperationException("could not stage the docked cargo");
                    Pawn traveler = SpawnPawn(s, PawnKindDefOf.Colonist, Faction.OfPlayer, Stable().InteractionCell);
                    travelerId = traveler.ThingID;
                    Pawn worldDog = s.Track(Stable().AddNewDog(QR_DefOf.QR_CombatdogFour));
                    Stable().TryUndock(worldDog);
                    Thing worldCargo = ThingMaker.MakeThing(ThingDefOf.Uranium);
                    worldCargo.stackCount = 9;
                    worldCargoId = worldCargo.ThingID;
                    if (!worldDog.inventory.innerContainer.TryAdd(worldCargo))
                        throw new InvalidOperationException("could not stage the caravan cargo");
                    CaravanMaker.MakeCaravan(new[] { traveler, worldDog }, Faction.OfPlayer, map.Tile, true);
                    Pawn wreckDog = s.Track(Stable().AddNewDog(QR_DefOf.QR_CombatdogThree));
                    Stable().TryUndock(wreckDog);
                    wreckDog.Kill(null);
                    Corpse wreck = wreckDog.Corpse ?? throw new InvalidOperationException("no wreck to remove");
                    s.Track(wreck);
                    CellRect field = ClearRect(map, Stable().Position, 13, 1, 12f, 35f);
                    Building_Stable packed = SpawnStable(s, powered: false, near: field.CenterCell, maxDist: 0f);
                    IntVec3 packedAt = packed.Position;
                    MinifiedThing mini = packed.MakeMinified();
                    if (!GenPlace.TryPlaceThing(mini, packedAt, map, ThingPlaceMode.Near))
                        throw new InvalidOperationException("could not place the packed stable");
                    s.Track(mini);
                    Blueprint blueprint = s.Track(GenConstruct.PlaceBlueprintForBuild(QR_DefOf.QR_Stable,
                        field.CenterCell + new IntVec3(-4, 0, -4), map, Rot4.North, Faction.OfPlayer, null));
                    Thing frame = SpawnBuilding(s, QR_DefOf.QR_Stable.frameDef, null,
                        field.CenterCell + new IntVec3(4, 0, -4), Rot4.North);
                    removedIds = new[] { wreck.ThingID, mini.ThingID, blueprint.ThingID, frame.ThingID };
                    Hediff wound = Rider().health.AddHediff(HediffDefOf.Cut, Rider().RaceProps.body.corePart);
                    wound.Severity = woundSeverity = 2f;
                    wound.sourceDef = QR_DefOf.QR_Gun_AssaultRifle;
                    backup = Removal.PrepareSave(RemovalSaveName);
                    if (AutoTest.Dir != null)
                        File.WriteAllLines(Path.Combine(AutoTest.Dir, "removal-expected.txt"),
                            map.mapPawns.FreeColonistsSpawned.Select(p => p.ThingID)
                                .Concat(new[] { cargoId, dockCargoId, travelerId, worldCargoId }));
                })
                .Expect("rider and carried patient survive, cargo is kept, mod objects and jobs are gone", () =>
                {
                    var remaining = Removal.AllThings();
                    return Rider().Spawned && Riding.MountOf(Rider()) == null
                        && Scenario.ById(patientId) is Pawn patient && patient.Spawned && patient.Downed
                        && Scenario.ById(cargoId) is Thing cargo && cargo.Spawned && cargo.stackCount == 20
                        && Scenario.ById(dockCargoId) is Thing dockCargo && dockCargo.Spawned && dockCargo.stackCount == 7
                        && remaining.OfType<Pawn>().Any(p => p.ThingID == travelerId
                            && p.GetCaravan() != null && p.inventory.innerContainer.Any(t =>
                                t.ThingID == worldCargoId && t.stackCount == 9))
                        && Rider().health.hediffSet.hediffs.Any(h => h.def == HediffDefOf.Cut
                            && h.Severity >= woundSeverity && h.sourceDef == null)
                        && !remaining.Any(t => CompRobodog.IsRobodog(t) || removedIds.Contains(t.ThingID))
                        && !Find.CurrentMap.mapPawns.AllPawns.Any(p =>
                            p.jobs?.AllJobs().Any(j => j.def.defName.StartsWith("QR_", StringComparison.Ordinal)) == true);
                })
                .Summary(() => backup == null ? "removal preparation not reached; no clean export claimed"
                    : $"export {RemovalSaveName}, backup {backup}; rider {riderId}, patient {patientId}, cargo {cargoId}/{dockCargoId}/{worldCargoId}")
                .Start();
        }
    }
}
