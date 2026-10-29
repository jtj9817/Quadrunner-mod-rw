using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public static partial class Scenarios
    {
        /// <summary>Five real recovered wrecks of all tiers are rebuilt by five colonist bills, across a reload.</summary>
        private static void CombatdogRebuildBills()
        {
            var s = new Scenario("Combatdog rebuild bills", 60000);
            string stableId = null, crafterId = null;
            var dogIds = new List<string>();
            var names = new List<string>();
            var kinds = new List<PawnKindDef>();
            var materials = new List<string>();
            var priorities = new Dictionary<WorkTypeDef, int>();
            bool previousManualPriorities = false;
            int lastLiving = -1, lastProgress = 0, steelBefore = 0, plasteelBefore = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn CrafterPawn() => (Pawn)Scenario.ById(crafterId);
            int Living() => dogIds.Count(id => Scenario.ById(id) is Pawn dog && !dog.Dead);
            int TotalMaterial(ThingDef def)
            {
                var things = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(Find.CurrentMap, ThingRequest.ForDef(def), things);
                return things.Sum(t => t.stackCount);
            }

            s.Do("destroy five Combatdogs of tiers 1–4 and let a worker recover their wrecks", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    PawnKindDef[] tiers = { QR_DefOf.QR_Combatdog, QR_DefOf.QR_CombatdogTwo,
                        QR_DefOf.QR_CombatdogThree, QR_DefOf.QR_CombatdogFour, QR_DefOf.QR_Combatdog };
                    foreach (PawnKindDef kind in tiers)
                    {
                        Pawn dog = s.Track(stable.AddNewDog(kind));
                        dogIds.Add(dog.ThingID);
                        names.Add(dog.Name.ToStringShort);
                        kinds.Add(kind);
                        if (!stable.TryUndock(dog))
                            throw new InvalidOperationException("could not undock a Combatdog");
                        Teleport(dog, OpenCell(stable.Map, stable.InteractionCell, 8f, 15f));
                        dog.Kill(null);
                        s.Track(dog.Corpse ?? throw new InvalidOperationException("a Combatdog left no wreck"));
                    }
                })
                .Expect("all five real wrecks are physically carried into the stable", () =>
                    Stable().RecoveredWrecks.Count == 5 && Stable().RecoveredWrecks.All(c => !c.Spawned))
                .Expect("newly recovered wrecks are available, including outside-Home wrecks", () =>
                    Stable().RecoveredWrecks.All(c => !c.IsForbidden(Faction.OfPlayer)))
                .Do("simulate forbidden wrecks stored by an older version", () =>
                {
                    if (AutoTest.Enabled)
                        foreach (Corpse wreck in Stable().RecoveredWrecks)
                            wreck.TrySetForbidden(true);
                })
                .SaveAndReload()
                .Expect("saved wrecks remain held and are available after loading", () =>
                    Stable().RecoveredWrecks.Count == 5
                    && Stable().RecoveredWrecks.All(c => !c.IsForbidden(Faction.OfPlayer)))
                .Do("queue five rebuild bills and provide exactly five sets of materials at the stable", () =>
                {
                    Building_Stable stable = Stable();
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    crafterId = crafter.ThingID;
                    previousManualPriorities = Current.Game.playSettings.useWorkPriorities;
                    Current.Game.playSettings.useWorkPriorities = true;
                    foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    {
                        if (crafter.WorkTypeIsDisabled(work))
                            continue;
                        priorities[work] = crafter.workSettings.GetPriority(work);
                        crafter.workSettings.SetPriority(work, work == WorkTypeDefOf.Crafting ? 1 : 0);
                    }
                    s.OnFinish(() =>
                    {
                        Pawn p = CrafterPawn();
                        if (p?.workSettings != null)
                            foreach (KeyValuePair<WorkTypeDef, int> entry in priorities)
                                p.workSettings.SetPriority(entry.Key, entry.Value);
                        Current.Game.playSettings.useWorkPriorities = previousManualPriorities;
                    });
                    for (int i = 0; i < 5; i++)
                    {
                        Bill bill = QR_DefOf.QR_RebuildCombatdog.MakeNewBill();
                        bill.SetPawnRestriction(crafter);
                        stable.BillStack.AddBill(bill);
                    }
                    steelBefore = TotalMaterial(ThingDefOf.Steel);
                    plasteelBefore = TotalMaterial(ThingDefOf.Plasteel);
                    materials.Add(SpawnStack(s, ThingDefOf.Steel, 50, stable.InteractionCell).ThingID);
                    materials.Add(SpawnStack(s, ThingDefOf.Plasteel, 25, stable.InteractionCell).ThingID);
                    foreach (int count in new[] { 75, 75, 50 })
                        materials.Add(SpawnStack(s, ThingDefOf.Steel, count,
                            OpenCell(stable.Map, stable.InteractionCell, 4f, 8f)).ThingID);
                    foreach (int count in new[] { 75, 25 })
                        materials.Add(SpawnStack(s, ThingDefOf.Plasteel, count,
                            OpenCell(stable.Map, stable.InteractionCell, 4f, 8f)).ThingID);
                })
                .Wait(300)
                .Expect("queued bills hold unstarted automatic work even with a full local delivery", () =>
                    Stable().RecoveredWrecks.Count == 5 && !Stable().RebuildPaid && Stable().RebuildProgress == 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) >= 50
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) >= 25);
            if (AutoTest.Enabled)
            {
                int width = UnityEngine.Screen.width, height = UnityEngine.Screen.height;
                s.OnFinish(() =>
                {
                    UnityEngine.Screen.SetResolution(width, height, false);
                    Find.Selector.ClearSelection();
                });
                s.Do("frame the stable's manual-bill status", () =>
                {
                    UnityEngine.Screen.SetResolution(1280, 720, false);
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(Stable(), playSound: false);
                    Find.CameraDriver.SetRootPosAndSize(Stable().DrawPos, 7f);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                });
                CaptureArtwork(s, "rebuild-bill-waiting", () => { });
                s.Do("resume after the capture", () => Scenario.Resume());
            }
            s.Do("assign the undrafted colonist to the first bill", () =>
                {
                    Pawn crafter = CrafterPawn();
                    crafter.drafter.Drafted = false;
                    Job job = BillJob(crafter, Stable()) ?? throw new InvalidOperationException(
                        "the colonist cannot find a bill job with five stored wrecks and all materials");
                    if (!crafter.jobs.TryTakeOrderedJob(job))
                        throw new InvalidOperationException("the colonist refused the first rebuild bill");
                })
                .Expect("the colonist retrieves an actual stored wreck for the first bill", () =>
                    CrafterPawn().CurJob?.bill?.recipe == QR_DefOf.QR_RebuildCombatdog
                    && (CrafterPawn().carryTracker.CarriedThing is Corpse
                        || CrafterPawn().CurJob.placedThings?.Any(t => t.thing is Corpse) == true))
                .SaveAndReload()
                .Expect("the colonist completes the first bill after loading", () => Living() >= 1)
                .Expect("the colonist finishes all five bills and all five actual dogs return", () =>
                {
                    Pawn crafter = CrafterPawn();
                    if (Living() == 5)
                        return true;
                    if (Living() != lastLiving)
                    {
                        lastLiving = Living();
                        lastProgress = Find.TickManager.TicksGame;
                    }
                    if (Find.TickManager.TicksGame - lastProgress > 8000)
                        throw new InvalidOperationException($"stalled: crafter {crafter.CurJob}, "
                            + $"held {Stable().RecoveredWrecks.Count}, paid {Stable().RebuildPaid}, "
                            + $"bills {string.Join(", ", Stable().BillStack.Bills.Select(b => $"{b.ShouldDoNow()}:{b.nextTickToSearchForIngredients}"))}, "
                            + $"materials {string.Join(", ", materials.Select(id => Scenario.ById(id)?.ToStringSafe() ?? "gone"))}");
                    return false;
                })
                .Expect("each original name and tier is restored and each bill is completed", () =>
                    Stable().BillStack.Bills.All(b => !b.ShouldDoNow())
                    && Stable().RecoveredWrecks.Count == 0
                    && dogIds.Select((id, i) => Scenario.ById(id) is Pawn dog && !dog.Dead
                        && dog.Name.ToStringShort == names[i] && dog.kindDef == kinds[i]
                        && CompRobodog.Of(dog).stable == Stable()).All(ok => ok))
                .Expect("all 250 steel and 125 plasteel were consumed exactly once", () =>
                    TotalMaterial(ThingDefOf.Steel) == steelBefore
                    && TotalMaterial(ThingDefOf.Plasteel) == plasteelBefore)
                .Summary(() => $"{Living()} original dogs; {Stable()?.BillStack.Bills.Count(b => b.ShouldDoNow())} bills pending")
                .Start();
        }

        /// <summary>Suspended bills permit automatic work; paid work completes before the next manual bill.</summary>
        private static void RebuildBillArbitration()
        {
            var s = new Scenario("Rebuild bill arbitration", 30000);
            string stableId = null, firstId = null, secondId = null, crafterId = null;
            int progress = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Bill Bill() => Stable().BillStack.Bills.First();
            bool Alive(string id) => Scenario.ById(id) is Pawn dog && !dog.Dead;

            s.Do("store two wrecks, suspend a tier-4 bill and deliver materials for both", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    Pawn first = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogThree));
                    Pawn second = s.Track(stable.AddNewDog(QR_DefOf.QR_CombatdogFour));
                    firstId = first.ThingID;
                    secondId = second.ThingID;
                    foreach (Pawn dog in new[] { first, second })
                    {
                        if (!stable.TryUndock(dog))
                            throw new InvalidOperationException("could not undock a Combatdog");
                        dog.Kill(null);
                        Corpse wreck = s.Track(dog.Corpse ?? throw new InvalidOperationException("no wreck"));
                        if (!stable.AcceptWreck(wreck))
                            throw new InvalidOperationException("the stable refused its wreck");
                    }
                    Bill bill = QR_DefOf.QR_RebuildCombatdog.MakeNewBill();
                    bill.suspended = true;
                    foreach (PawnKindDef kind in new[] { QR_DefOf.QR_Combatdog, QR_DefOf.QR_CombatdogTwo,
                                 QR_DefOf.QR_CombatdogThree })
                        bill.ingredientFilter.SetAllow(kind.race.race.corpseDef, false);
                    stable.BillStack.AddBill(bill);
                    List<IntVec3> cells = stable.RebuildDeliveryCells.Where(c => c.Standable(stable.Map)
                        && c.GetFirstItem(stable.Map) == null && c.GetFirstBuilding(stable.Map) == null).Take(3).ToList();
                    if (cells.Count < 3)
                        throw new InvalidOperationException("not enough free delivery cells");
                    SpawnStack(s, ThingDefOf.Steel, 75, cells[0]);
                    SpawnStack(s, ThingDefOf.Steel, 25, cells[1]);
                    SpawnStack(s, ThingDefOf.Plasteel, 50, cells[2]);
                    crafterId = (Crafter() ?? throw new InvalidOperationException("no colonist can craft")).ThingID;
                })
                .Expect("the suspended bill permits automatic rebuilding and pays only the first wreck", () =>
                    Stable().RebuildPaid && Stable().RebuildProgress > 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 50
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 25)
                .Do("activate the bill after the first wreck has already been paid for", () =>
                {
                    Bill().suspended = false;
                    Pawn crafter = (Pawn)Scenario.ById(crafterId);
                    crafter.drafter.Drafted = false;
                    if (BillJob(crafter, Stable()) != null)
                        throw new InvalidOperationException("a bill took a wreck while automatic rebuilding was paid");
                    crafter.drafter.Drafted = true;
                    progress = Stable().RebuildProgress;
                })
                .Wait(200)
                .Expect("the paid automatic rebuild continues and no second materials are consumed", () =>
                    Stable().RebuildPaid && Stable().RebuildProgress > progress
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 50
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 25)
                .Do("remember the automatic progress", () => progress = Stable().RebuildProgress)
                .SaveAndReload()
                .Expect("the paid work and remaining materials survive the reload", () =>
                    Stable().RebuildPaid && Stable().RebuildProgress >= progress
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 50
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 25)
                .Expect("the already-paid first dog is rebuilt automatically", () => Alive(firstId))
                .Wait(300)
                .Expect("the second wreck and its materials wait for the active bill", () =>
                    !Alive(secondId) && Stable().RecoveredWrecks.Count == 1
                    && !Stable().RebuildPaid && Stable().RebuildProgress == 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 50
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 25)
                .Do("assign the colonist to the filtered tier-4 bill", () =>
                {
                    Pawn crafter = (Pawn)Scenario.ById(crafterId);
                    crafter.drafter.Drafted = false;
                    Job job = BillJob(crafter, Stable()) ?? throw new InvalidOperationException("no tier-4 bill job");
                    if (job.bill != Bill() || !crafter.jobs.TryTakeOrderedJob(job))
                        throw new InvalidOperationException("the colonist did not take the tier-4 bill");
                })
                .Expect("the second original dog is rebuilt by the bill", () =>
                    Alive(secondId) && !Bill().ShouldDoNow() && Stable().RecoveredWrecks.Count == 0)
                .Expect("each path consumed exactly one set of materials", () =>
                    AutoRebuildDelivered(Stable(), ThingDefOf.Steel) == 0
                    && AutoRebuildDelivered(Stable(), ThingDefOf.Plasteel) == 0)
                .Summary(() => $"automatic {Alive(firstId)}, manual {Alive(secondId)}")
                .Start();
        }

        /// <summary>Bill searches still count docked dogs for vanilla's do-until target.</summary>
        private static void StableTargetCountBills()
        {
            var s = new Scenario("Stable target-count bills", 20000);
            string stableId = null, firstId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Bill_Production Bill() => (Bill_Production)Stable().BillStack.Bills.First();

            s.Do("a powered stable with one docked Combatdog and an assemble-until-one bill", () =>
                {
                    DraftColonists(s);
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    firstId = s.Track(stable.AddNewDog(QR_DefOf.QR_Combatdog)).ThingID;
                    var bill = (Bill_Production)QR_DefOf.QR_AssembleCombatdog.MakeNewBill();
                    bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                    bill.targetCount = 1;
                    stable.BillStack.AddBill(bill);
                    SpawnStack(s, ThingDefOf.Steel, 75, stable.InteractionCell);
                    SpawnStack(s, ThingDefOf.Steel, 25, OpenCell(stable.Map, stable.InteractionCell, 3f, 6f));
                    SpawnStack(s, ThingDefOf.Plasteel, 50, OpenCell(stable.Map, stable.InteractionCell, 3f, 6f));
                    SpawnStack(s, ThingDefOf.ComponentSpacer, 2, OpenCell(stable.Map, stable.InteractionCell, 3f, 6f));
                })
                .Expect("the stable is powered and the original dog is docked", () =>
                    Stable().CanCharge && Stable().Docked.Any(d => d.ThingID == firstId))
                .Do("a bill search must not offer another dog when the target is already met", () =>
                {
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    if (Bill().ShouldDoNow() || BillJob(crafter, Stable()) != null)
                        throw new InvalidOperationException("the bill search ignored the docked dog");
                })
                .Do("raise the target to two and assign the colonist", () =>
                {
                    Bill().targetCount = 2;
                    Pawn crafter = Crafter();
                    Job job = BillJob(crafter, Stable()) ?? throw new InvalidOperationException("no assemble bill job");
                    crafter.drafter.Drafted = false;
                    if (!crafter.jobs.TryTakeOrderedJob(job))
                        throw new InvalidOperationException("the colonist refused the assemble bill");
                })
                .Expect("one additional Combatdog is built and the target is satisfied", () =>
                    Stable().Dogs().Count(d => d.kindDef == QR_DefOf.QR_Combatdog) == 2 && !Bill().ShouldDoNow())
                .Do("track the new dog for cleanup", () =>
                    s.Track(Stable().Dogs().First(d => d.ThingID != firstId)))
                .SaveAndReload()
                .Expect("both dogs still count toward the target after loading", () =>
                    Stable().Dogs().Count(d => d.kindDef == QR_DefOf.QR_Combatdog) == 2
                    && !Bill().ShouldDoNow() && BillJob(Crafter(), Stable()) == null)
                .Start();
        }
    }
}
