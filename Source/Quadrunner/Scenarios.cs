using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    /// <summary>Every scenario, in Run all order. Each setup builds what it needs on the current (quicktest) map.</summary>
    public static class Scenarios
    {
        public static readonly List<(string name, Action setUp)> All = new List<(string, Action)>
        {
            ("Smoke", Smoke),
            ("Save and reload", SaveAndReload),
            ("Dock and undock", DockAndUndock),
            ("Assemble and dock", AssembleAndDock),
            ("Stable full (16th dog)", StableFull),
        };

        /// <summary>
        /// Runs the scenarios whose names contain any comma-separated part of <paramref name="filter"/>
        /// (case-insensitive; empty = all), then calls <paramref name="done"/> with the PASS count and verdicts.
        /// </summary>
        public static void RunAll(string filter, Action<int, List<string>> done)
        {
            string[] parts = (filter ?? "").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            Scenario.RunAll(All.Where(s => parts.Length == 0 ||
                parts.Any(p => s.name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)), done);
        }

        private static IntVec3 FreeCellNearCenter(Map map)
        {
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 15,
                    c => c.Standable(map) && c.GetFirstItem(map) == null && !c.Fogged(map), out IntVec3 cell))
                throw new InvalidOperationException("no free standable cell near the map center");
            return cell;
        }

        private static Thing SpawnSteel(Scenario s, int count)
        {
            Map map = Find.CurrentMap;
            Thing steel = ThingMaker.MakeThing(ThingDefOf.Steel);
            steel.stackCount = count;
            return s.Track(GenSpawn.Spawn(steel, FreeCellNearCenter(map), map));
        }

        private static Thing SpawnStack(Scenario s, ThingDef def, int count, IntVec3 near)
        {
            Map map = Find.CurrentMap;
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            if (!GenPlace.TryPlaceThing(thing, near, map, ThingPlaceMode.Near))
                throw new InvalidOperationException($"couldn't place {count} {def.defName} near {near}");
            return s.Track(thing);
        }

        /// <summary>
        /// A player stable on clear, unroofed ground near the map center, tracked first so that its docked dogs come
        /// out before they are destroyed. Powered: a conduit under it, with dev unlimited power for the scenario.
        /// </summary>
        private static Building_Stable SpawnStable(Scenario s, bool powered)
        {
            Map map = Find.CurrentMap;
            IntVec2 size = QR_DefOf.QR_Stable.size;
            if (!CellFinder.TryFindRandomCellNear(map.Center, map, 40, c => GenAdj.OccupiedRect(c, Rot4.South, size)
                        .ExpandedBy(4).Cells.All(x => x.InBounds(map) && x.Standable(map) && !x.Fogged(map)
                            && x.GetFirstItem(map) == null && x.GetFirstBuilding(map) == null && x.GetFirstPawn(map) == null),
                    out IntVec3 cell))
                throw new InvalidOperationException("no clear spot for a stable near the map center");
            var stable = (Building_Stable)ThingMaker.MakeThing(QR_DefOf.QR_Stable);
            stable.SetFaction(Faction.OfPlayer);
            s.Track(GenSpawn.Spawn(stable, cell, map, Rot4.South));
            if (powered)
            {
                Thing conduit = ThingMaker.MakeThing(ThingDefOf.PowerConduit);
                conduit.SetFaction(Faction.OfPlayer);
                s.Track(GenSpawn.Spawn(conduit, cell, map));
                bool prev = DebugSettings.unlimitedPower;
                DebugSettings.unlimitedPower = true;
                s.OnFinish(() => DebugSettings.unlimitedPower = prev);
            }
            return stable;
        }

        private static Pawn Crafter() => Find.CurrentMap.mapPawns.FreeColonistsSpawned
            .FirstOrDefault(p => !p.Downed && !p.WorkTypeIsDisabled(WorkTypeDefOf.Crafting));

        private static Job BillJob(Pawn crafter, Building_Stable stable) =>
            QR_DefOf.QR_DoBillsStable.Worker is WorkGiver_Scanner w ? w.JobOnThing(crafter, stable, forced: true) : null;

        private static void SpawnAssembleMaterials(Scenario s, Building_Stable stable)
        {
            SpawnStack(s, ThingDefOf.Steel, 80, stable.InteractionCell);
            SpawnStack(s, ThingDefOf.Plasteel, 30, stable.InteractionCell);
            SpawnStack(s, ThingDefOf.ComponentSpacer, 1, stable.InteractionCell);
        }

        /// <summary>The race's guarantees, a docked dog undocking and walking home to dock, across two reloads.</summary>
        private static void DockAndUndock()
        {
            var s = new Scenario("Dock and undock", 6000);
            string stableId = null, dogId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            Pawn Dog() => (Pawn)Scenario.ById(dogId);
            s.Do("spawn a stable with a new Quadrunner", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: false);
                    stableId = stable.ThingID;
                    dogId = s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID;
                })
                .Expect("the new dog is docked, not spawned", () => Stable().Docked.Contains(Dog()) && !Dog().Spawned)
                .Expect("race: not humanlike, animal, mechanoid or flesh; untrainable", () =>
                {
                    RaceProperties r = Dog().RaceProps;
                    return !r.Humanlike && !r.Animal && !r.IsMechanoid && !r.IsFlesh && r.trainability == TrainabilityDefOf.None;
                })
                .Expect("immune: psychic sensitivity 0, corpse never rots (disease needs flesh)", () =>
                    Dog().GetStatValue(StatDefOf.PsychicSensitivity) == 0f
                    && Dog().RaceProps.corpseDef.GetCompProperties<CompProperties_Rottable>() == null)
                .Expect("no food, rest or mood need", () =>
                    Dog().needs.food == null && Dog().needs.rest == null && Dog().needs.mood == null)
                .Expect("never wild, never manhunter", () => !QR_DefOf.QR_Quadrunner.canArriveManhunter
                    && DefDatabase<BiomeDef>.AllDefs.All(b => b.CommonalityOfAnimal(QR_DefOf.QR_Quadrunner) == 0f))
                .Do("undock", () =>
                {
                    if (!Stable().TryUndock(Dog()))
                        throw new InvalidOperationException("TryUndock failed");
                })
                .Expect("the dog is out, can move and is conscious", () => Dog().Spawned && !Dog().Downed
                    && Dog().health.capacities.CapableOf(PawnCapacityDefOf.Moving)
                    && Dog().health.capacities.CapableOf(PawnCapacityDefOf.Consciousness))
                .Expect("the dog has a name", () => Dog().Name != null && !Dog().LabelShort.NullOrEmpty())
                .Do("move the dog 20 cells away", () =>
                {
                    Map map = Dog().Map;
                    if (!CellFinder.TryFindRandomCellNear(Stable().Position, map, 25, c => c.Standable(map)
                            && c.DistanceTo(Stable().Position) >= 20f && Dog().CanReach(c, PathEndMode.OnCell, Danger.Deadly),
                            out IntVec3 far))
                        throw new InvalidOperationException("no reachable cell 20 cells from the stable");
                    Dog().Position = far;
                    Dog().Notify_Teleported();
                    Dog().jobs.EndCurrentJob(JobCondition.InterruptForced);
                })
                .Expect("it heads home to dock", () => Dog().CurJobDef == QR_DefOf.QR_Dock)
                .SaveAndReload()
                .Expect("the dog walks back and docks", () => Stable().Docked.Contains(Dog()))
                .SaveAndReload()
                .Expect("still docked and assigned after the reload", () =>
                    Stable().Docked.Contains(Dog()) && CompRobodog.Of(Dog())?.stable == Stable())
                .Summary(() => Dog() == null ? "dog gone" :
                    $"moving {Dog().health.capacities.GetLevel(PawnCapacityDefOf.Moving):P0}, " +
                    $"consciousness {Dog().health.capacities.GetLevel(PawnCapacityDefOf.Consciousness):P0}, " +
                    $"needs [{string.Join(", ", Dog().needs.AllNeeds.Select(n => n.def.defName))}], name {Dog().LabelShort}")
                .Start();
        }

        /// <summary>A colonist works the "Assemble Quadrunner" bill; the finished dog docks straight into the stable.</summary>
        private static void AssembleAndDock()
        {
            var s = new Scenario("Assemble and dock", 20000);
            string stableId = null, dogId = null;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("spawn a powered stable, materials and a bill", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    SpawnAssembleMaterials(s, stable);
                    stable.BillStack.AddBill(QR_DefOf.QR_AssembleQuadrunner.MakeNewBill());
                })
                .Expect("a colonist takes the bill", () =>
                {
                    Pawn crafter = Crafter() ?? throw new InvalidOperationException("no colonist can craft");
                    Job job = BillJob(crafter, Stable());
                    return job != null && crafter.jobs.TryTakeOrderedJob(job);
                })
                .Expect("a Quadrunner docks into the stable", () => Stable().Docked.Count == 1)
                .Do("note the dog", () => dogId = s.Track(Stable().Docked[0]).ThingID)
                .Expect("the bill is done", () => Stable().BillStack.Count == 0 || !Stable().BillStack[0].ShouldDoNow())
                .SaveAndReload()
                .Expect("the assembled dog is still docked, assigned to the stable", () =>
                    Stable().Docked.Count == 1 && Stable().Docked[0].ThingID == dogId
                    && CompRobodog.Of(Stable().Docked[0])?.stable == Stable())
                .Summary(() => $"dog {dogId}")
                .Start();
        }

        /// <summary>15 slots: a dog bill can't start at a full stable; a dog made anyway waits outside, unassigned.</summary>
        private static void StableFull()
        {
            var s = new Scenario("Stable full (16th dog)", 8000);
            string stableId = null, extraId = null, outId = null;
            int drawnBefore = 0;
            Building_Stable Stable() => (Building_Stable)Scenario.ById(stableId);
            s.Do("spawn a powered stable with 14 dogs, materials and a bill", () =>
                {
                    Building_Stable stable = SpawnStable(s, powered: true);
                    stableId = stable.ThingID;
                    for (int i = 0; i < 14; i++)
                        s.Track(stable.AddNewDog(QR_DefOf.QR_Quadrunner));
                    Find.ResearchManager.FinishProject(QR_DefOf.MicroelectronicsBasics);
                    SpawnAssembleMaterials(s, stable);
                    stable.BillStack.AddBill(QR_DefOf.QR_AssembleQuadrunner.MakeNewBill());
                })
                .Expect("with a free slot, the bill is doable", () => Stable().HasFreeSlot && BillJob(Crafter(), Stable()) != null)
                .Do("a 15th dog", () => s.Track(Stable().AddNewDog(QR_DefOf.QR_Quadrunner)))
                .Expect("15 docked, no free slot, the bill isn't doable", () =>
                    Stable().Docked.Count == 15 && !Stable().HasFreeSlot && BillJob(Crafter(), Stable()) == null)
                .Do("a 16th dog made anyway", () => extraId = s.Track(Stable().AddNewDog(QR_DefOf.QR_Quadrunner)).ThingID)
                .Expect("the 16th waits beside the stable, unassigned", () =>
                    Scenario.ById(extraId) is Pawn p && p.Spawned && CompRobodog.Of(p).stable == null && Stable().Docked.Count == 15)
                .Do("undock one", () =>
                {
                    Pawn dog = Stable().Docked[0];
                    outId = dog.ThingID;
                    Stable().TryUndock(dog);
                })
                .Expect("an undocked dog keeps its slot", () => Stable().Docked.Count == 14 && !Stable().HasFreeSlot)
                .Expect("the Overview tab has 15 rows, 14 docked", () =>
                {
                    var rows = ITab_StableOverview.Rows(Stable());
                    return rows.Count == 15 && rows.Count(r => r.cells[4] == "Docked") == 14
                        && rows.All(r => r.cells[1] == "Quadrunner" && r.cells[3] == "100%");
                })
                .Do("select the stable, open its Overview tab", () =>
                {
                    drawnBefore = ITab_StableOverview.framesDrawn;
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(Stable(), playSound: false);
                    Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect, playSound: false);
                    ((MainTabWindow_Inspect)MainButtonDefOf.Inspect.TabWindow).OpenTabType = typeof(ITab_StableOverview);
                })
                .Expect("the tab drew", () => ITab_StableOverview.framesDrawn > drawnBefore)
                .Do("deselect", () => Find.Selector.ClearSelection())
                .SaveAndReload()
                .Expect("after the reload: 15 assigned, the 16th still unassigned", () =>
                    Stable().Dogs().Count == 15 && CompRobodog.Of(Scenario.ById(extraId))?.stable == null)
                .Expect("the undocked dog docks again", () => Stable().Docked.Any(d => d.ThingID == outId) && Stable().Docked.Count == 15)
                .Start();
        }

        /// <summary>The harness itself: a scenario starts, ticks, checks a thing and cleans up.</summary>
        private static void Smoke()
        {
            var s = new Scenario("Smoke", 2500);
            string id = null;
            s.Do("spawn 75 steel", () => id = SpawnSteel(s, 75).ThingID)
                .Wait(300)
                .Expect("steel still there", () => Scenario.ById(id)?.stackCount == 75)
                .Start();
        }

        /// <summary>A save/load step partway through a scenario: the pass criterion every later scenario reuses.</summary>
        private static void SaveAndReload()
        {
            var s = new Scenario("Save and reload", 5000);
            string id = null;
            int savedAt = 0;
            s.Do("spawn 40 steel", () => id = SpawnSteel(s, 40).ThingID)
                .Wait(120)
                .Do("note the tick", () => savedAt = Find.TickManager.TicksGame)
                .SaveAndReload()
                .Expect("steel survived the reload", () => Scenario.ById(id)?.stackCount == 40)
                .Expect("the loaded game resumed at the saved tick", () => Find.TickManager.TicksGame >= savedAt)
                .Wait(300)
                .Summary(() => $"saved at tick {savedAt}")
                .Start();
        }
    }
}
