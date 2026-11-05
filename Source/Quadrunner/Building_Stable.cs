using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public class StableProps : DefModExtension
    {
        public int capacity = 15;
        /// <summary>Hit points healed per tick on a docked dog's injuries while powered.</summary>
        public float repairPerTick = 0.02f;
    }

    /// <summary>
    /// Docks, repairs and charges its assigned dogs, runs fabrication bills, and can own the map's shared network
    /// queues. Stable affiliation controls docking capacity, not which dog may receive a network assignment.
    /// </summary>
    [StaticConstructorOnStartup]
    public class Building_Stable : Building_WorkTable, IThingHolder, IHaulSource
    {
        private static readonly Texture2D PatrolIcon = ContentFinder<Texture2D>.Get("Quadrunner/UI/Patrol");
        private static readonly Texture2D ClearPatrolIcon = ContentFinder<Texture2D>.Get("Quadrunner/UI/ClearPatrol");
        private static readonly Texture2D RescueIcon = ContentFinder<Texture2D>.Get("Quadrunner/Buildings/DockMedirunner");
        private static readonly Material[,] DockIcons = MakeDockIcons();
        private static readonly Material EmptyDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(0.22f, 0.29f, 0.31f));
        private static readonly Material OccupiedDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(0.4f, 1f, 0.6f));
        private static readonly Material UnpoweredDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(1f, 0.65f, 0.2f));

        private ThingOwner<Pawn> docked;
        // Recovered Combatdog wrecks (corpses) waiting for, or under, automatic rebuilding. Wrapped in a holder so
        // vanilla's recursive content walk (saving helpers, removal) sees the wrecks like any other contained content.
        private RecoveredHolder recoveredHolder;
        private int rebuildProgress;
        private bool rebuildPaid;
        // Only the stable's own bill search may see recovered wrecks as ingredients.
        internal bool searchingBillIngredients;
        private readonly StorageSettings haulSourceSettings;
        public NetworkState networkState;
        private List<IntVec3> waypoints = new List<IntVec3>();
        // Set by a click while placing waypoints, so the targeter starts again once vanilla stops it after the click.
        private bool placingMore;
        private bool destroying;

        private sealed class RecoveredHolder : IThingHolder
        {
            private readonly Building_Stable stable;
            public ThingOwner<Corpse> Contents;

            public RecoveredHolder(Building_Stable stable)
            {
                this.stable = stable;
                Contents = new ThingOwner<Corpse>(this);
            }

            public IThingHolder ParentHolder => stable;

            public ThingOwner GetDirectlyHeldThings() => Contents;

            public void GetChildHolders(List<IThingHolder> outChildren) =>
                ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, Contents);
        }

        public Building_Stable()
        {
            docked = new ThingOwner<Pawn>(this);
            recoveredHolder = new RecoveredHolder(this);
            haulSourceSettings = new StorageSettings(this);
        }

        private ThingOwner<Corpse> recovered => recoveredHolder.Contents;

        private StableProps Props => def.GetModExtension<StableProps>();

        public int Capacity => Props.capacity;

        public IReadOnlyList<Pawn> Docked => docked.InnerListForReading;

        /// <summary>Wrecks taken from recovery carriers, waiting for materials or rebuilding, oldest first.</summary>
        public IReadOnlyList<Corpse> RecoveredWrecks => recovered.InnerListForReading;

        /// <summary>Work done on <see cref="RecoveredWrecks"/>[0], in ticks; the recipe's work amount is the total.</summary>
        public int RebuildProgress => rebuildProgress;

        /// <summary>True once this wreck's recipe ingredients were physically consumed; a reload must not charge again.</summary>
        public bool RebuildPaid => rebuildPaid;

        /// <summary>The existing Combatdog rebuild recipe supplies both cost and duration; there is no local tuning.</summary>
        public RecipeDef RebuildRecipe => QR_DefOf.QR_RebuildCombatdog;

        public int RebuildWork
        {
            get
            {
                RecipeDef recipe = RebuildRecipe;
                return recipe == null ? 0 : Mathf.CeilToInt(recipe.WorkAmountTotal(null));
            }
        }

        bool IHaulSource.HaulSourceEnabled => searchingBillIngredients && !rebuildPaid && recovered.Count > 0;
        bool IStoreSettingsParent.StorageTabVisible => false;
        StorageSettings IStoreSettingsParent.GetStoreSettings() => haulSourceSettings;
        StorageSettings IStoreSettingsParent.GetParentStoreSettings() => null;
        void IStoreSettingsParent.Notify_SettingsChanged()
        {
            if (Spawned)
                Map.listerHaulables.Notify_HaulSourceChanged(this);
        }

        /// <summary>The physical 3x3 delivery area: the interaction cell and its eight neighbours.</summary>
        public CellRect RebuildDeliveryCells => CellRect.CenteredOn(InteractionCell, 1).ClipInsideMap(Map);

        public string RebuildDeliveryDescription =>
            $"the 3x3 area around ({InteractionCell.x}, {InteractionCell.z})";

        /// <summary>Every living dog of this stable on its map: docked, out, or carried.</summary>
        public List<Pawn> Dogs()
        {
            Map map = MapHeld;
            if (map == null)
                return Docked.ToList();
            List<Pawn> dogs = map.mapPawns.AllPawns.Where(p => CompRobodog.Of(p)?.stable == this).ToList();
            // The temporary haul-source view hides docked pawns from MapPawns during a bill's target-count check.
            if (searchingBillIngredients)
                foreach (Pawn dog in docked.InnerListForReading)
                    if (CompRobodog.Of(dog)?.stable == this)
                        dogs.Add(dog);
            return dogs;
        }

        public bool HasFreeSlot
        {
            get
            {
                int remaining = Capacity;
                Map map = MapHeld;
                if (map == null)
                    return docked.Count < remaining;
                foreach (Pawn pawn in map.mapPawns.AllPawns)
                    if (CompRobodog.Of(pawn)?.stable == this && --remaining <= 0)
                        return false;
                return remaining > 0;
            }
        }

        /// <summary>Docked dogs charge and get repaired only while the stable has power.</summary>
        public bool CanCharge => this.TryGetComp<CompPowerTrader>()?.PowerOn ?? true;

        /// <summary>A free rideable dog of this stable, docked or surveying; charging and productive jobs stay busy.</summary>
        public Pawn IdleRideable => Dogs().Where(d => Rideable.Of(d) != null && DogNetwork.IsAvailable(d))
            .OrderByDescending(d => CompRobodog.Of(d).Battery).FirstOrDefault();

        /// <summary>Docks <paramref name="dog"/>, assigning it to this stable first if it isn't yet.</summary>
        public bool TryDock(Pawn dog) => TryDock(dog, log: true);

        private bool TryDock(Pawn dog, bool log)
        {
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp == null || docked.Contains(dog))
                return false;
            if (comp.stable != this)
            {
                if (!HasFreeSlot)
                    return false;
                comp.stable = this;
            }
            bool wasSelected = dog.Spawned && dog.DeSpawnOrDeselect();
            if (!docked.TryAdd(dog))
            {
                QR.Error($"{this} couldn't take {dog} into its dock");
                return false;
            }
            comp.fireStandbyUntil = -1;
            if (wasSelected)
                Find.Selector.Select(this, playSound: false);
            if (log)
                QR.Log($"{dog} docked at {this} ({docked.Count} docked, {Dogs().Count}/{Capacity} assigned)");
            return true;
        }

        public bool TryUndock(Pawn dog)
        {
            if (!docked.Contains(dog) || !docked.TryDrop(dog, InteractionCell, Map, ThingPlaceMode.Near, out Thing _))
                return false;
            // Held in the dock, vanilla gives the dog JobGiver_Carried; left current, it would not read as idle.
            dog.jobs?.StopAll();
            // Integrated equipment (the Medirunner's foam sprayer) is fitted on the way out, older saves included.
            if (RobodogWork.Of(dog)?.builtInGun != null)
                Tiers.Arm(dog, Tiers.Gun(dog, 0));
            QR.Log($"{dog} undocked from {this}");
            return true;
        }

        /// <summary>
        /// Takes a recovered player Combatdog wreck, including one still carried by its rescuer. A refusal changes
        /// nothing, so the carrier can keep or drop the corpse itself. The wreck waits here for materials and power.
        /// </summary>
        public bool AcceptWreck(Corpse corpse)
        {
            if (corpse == null || corpse.Destroyed)
                return false;
            Pawn dog = corpse.InnerPawn;
            if (dog == null || !dog.Dead || dog.Faction != Faction.OfPlayer || RobodogWork.Of(dog)?.combat != true)
                return false;
            if (recovered.Contains(corpse))
            {
                corpse.TrySetForbidden(false);
                return true;
            }
            if (!recovered.CanAcceptAnyOf(corpse, canMergeWithExistingStacks: false))
                return false;
            if (corpse.Spawned)
                corpse.DeSpawnOrDeselect();
            bool taken = corpse.holdingOwner != null
                ? corpse.holdingOwner.TryTransferToContainer(corpse, recovered, canMergeWithExistingStacks: false)
                : recovered.TryAdd(corpse, canMergeWithExistingStacks: false);
            if (!taken)
            {
                // Never strand a wreck that was already lifted off the map: put it back for its carrier or a retry.
                if (!corpse.Spawned && corpse.holdingOwner == null)
                    GenPlace.TryPlaceThing(corpse, InteractionCell, Map, ThingPlaceMode.Near);
                return false;
            }
            // Death outside Home automatically forbids a corpse; a recovered wreck is available for service.
            corpse.TrySetForbidden(false);
            QR.Log($"{this} took the wreck of {dog}; {recovered.Count} recovered, waiting for rebuilding");
            return true;
        }

        /// <summary>A new dog from a bill or a debug action.</summary>
        public Pawn AddNewDog(PawnKindDef kind)
        {
            Pawn dog = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            if (dog.Name == null)
                dog.Name = PawnBioAndNameGenerator.GeneratePawnName(dog);
            TakeNewDog(dog);
            return dog;
        }

        /// <summary>An unspawned dog, new or rebuilt: docked if a slot is free, else left beside the stable, unassigned.</summary>
        public void TakeNewDog(Pawn dog)
        {
            // A wreck may have come from a different, even destroyed stable. Receiving it establishes a fresh
            // assignment; if this stable is full, the rebuilt dog must remain free to adopt a future slot.
            CompRobodog comp = CompRobodog.Of(dog);
            if (comp != null)
                comp.stable = null;
            if (!TryDock(dog))
            {
                GenSpawn.Spawn(dog, InteractionCell, Map);
                Messages.Message($"{dog.LabelShortCap} has no free slot at {LabelCap} and waits beside it.", dog,
                    MessageTypeDefOf.CautionInput);
                QR.Log($"{dog} made at a full {this}; waits unassigned");
            }
        }

        /// <summary>The upgrade bill waiting for <paramref name="dog"/>, if one was ordered.</summary>
        public Bill_UpgradeDog UpgradeBill(Pawn dog) =>
            BillStack.Bills.OfType<Bill_UpgradeDog>().FirstOrDefault(b => b.dog == dog && b.Pending);

        /// <summary>From the Overview tab: a bill to upgrade <paramref name="dog"/> to its next tier.</summary>
        public Bill_UpgradeDog OrderUpgrade(Pawn dog)
        {
            RecipeDef recipe = CombatTier.Of(dog)?.upgrade;
            if (recipe == null || CompRobodog.Of(dog)?.stable != this || UpgradeBill(dog) != null)
                return null;
            var bill = new Bill_UpgradeDog(recipe, dog);
            BillStack.AddBill(bill);
            QR.Log($"{this}: {bill.LabelCap} ordered");
            return bill;
        }

        /// <summary>A dog being upgraded stays docked and cannot accept network assignments.</summary>
        public bool BeingUpgraded(Pawn dog) =>
            Map.mapPawns.FreeColonistsSpawned.Any(p => p.CurJob?.bill is Bill_UpgradeDog b && b.dog == dog);

        /// <summary>
        /// Rebuilds a docked Combatdog as its next tier (design: tiers are separate models): a new dog of the next kind
        /// takes its name, stable, battery and gun picks, holds its first slot's gun, and replaces it in the dock.
        /// </summary>
        public Pawn Upgrade(Pawn dog)
        {
            PawnKindDef next = CombatTier.Of(dog)?.next;
            if (next == null || !docked.Contains(dog))
                return null;
            Pawn upgraded = PawnGenerator.GeneratePawn(new PawnGenerationRequest(next, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            upgraded.Name = dog.Name;
            CompRobodog from = CompRobodog.Of(dog), to = CompRobodog.Of(upgraded);
            to.stable = this;
            to.SetBattery(from.Battery);
            to.builtOrder = from.BuildOrder;
            for (int slot = 0; slot < 2; slot++)
                if (from.PickedGun(slot) != null)
                    to.PickGun(slot, from.PickedGun(slot));
            Tiers.Arm(upgraded, Tiers.Gun(upgraded, 0));
            docked.Remove(dog);
            dog.Destroy();
            if (!docked.TryAdd(upgraded))
                QR.Error($"{this} couldn't dock the upgraded {upgraded}");
            return upgraded;
        }

        /// <summary>The patrol route its deployed Combatdogs walk in this order, looping.</summary>
        public IReadOnlyList<IntVec3> Waypoints => waypoints;

        public void AddWaypoint(IntVec3 cell)
        {
            waypoints.Add(cell);
            QR.Log($"{this}: patrol waypoint {waypoints.Count} at {cell}");
        }

        public void ClearWaypoints() => waypoints.Clear();

        /// <summary>Waypoints are placed one click after another until right-click or Escape.</summary>
        private void PlaceWaypoints()
        {
            var parms = new TargetingParameters { canTargetLocations = true, canTargetPawns = false, canTargetBuildings = false,
                validator = t => t.Cell.InBounds(Map) && t.Cell.Standable(Map) };
            Find.Targeter.BeginTargeting(parms, t =>
                {
                    AddWaypoint(t.Cell);
                    placingMore = true;
                }, actionWhenFinished: () =>
                {
                    if (placingMore && Spawned)
                    {
                        placingMore = false;
                        PlaceWaypoints();
                    }
                }, requiresCastedSelected: false);
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo g in base.GetGizmos())
                yield return g;
            if (Spawned && Faction == Faction.OfPlayer)
            {
                var dispatch = new Command_Action
                {
                    defaultLabel = "Dispatch Rescuedogs",
                    defaultDesc = "Immediately refresh the shared dispatch network to send available Medirunners to "
                        + "revealed downed colonists, guests, prisoners and colony animals. Normal rescue priorities, "
                        + "bed requirements and battery limits still apply. Automatic rescue remains active.",
                    icon = RescueIcon,
                    action = () => TryDispatch(force: true),
                };
                bool available = false;
                foreach (Pawn dog in Map.mapPawns.AllPawns)
                    if (RobodogWork.Of(dog)?.rescue == true && DogNetwork.IsAvailable(dog))
                    {
                        available = true;
                        break;
                    }
                if (!available)
                    dispatch.Disable("No Medirunner is available. Busy, charging and powered-down dogs cannot be dispatched.");
                else if (!Rescue.Patients(Map).Any(p => !p.Position.Fogged(Map)))
                    dispatch.Disable("No revealed downed pawn needs rescue.");
                yield return dispatch;
            }
            yield return new Command_Action
            {
                defaultLabel = "Add patrol waypoints",
                defaultDesc = "Click to place waypoints one after another; right-click to stop. Deployed Combatdogs walk "
                    + "them in order, in a loop, while a raid is on or targets remain.",
                icon = PatrolIcon,
                action = PlaceWaypoints,
            };
            if (waypoints.Count > 0)
                yield return new Command_Action
                {
                    defaultLabel = "Clear patrol",
                    defaultDesc = $"Remove all {waypoints.Count} patrol waypoints.",
                    icon = ClearPatrolIcon,
                    action = ClearWaypoints,
                };
        }

        private static Material[,] MakeDockIcons()
        {
            var icons = new Material[3, 2];
            string[] types = { "Quadrunner", "Combatdog", "Medirunner" };
            for (int i = 0; i < types.Length; i++)
            {
                string path = "Quadrunner/Buildings/Dock" + types[i];
                icons[i, 0] = MaterialPool.MatFrom(path, ShaderDatabase.Cutout, new Color(0.3f, 0.34f, 0.36f));
                icons[i, 1] = MaterialPool.MatFrom(path, ShaderDatabase.Cutout);
            }
            return icons;
        }

        /// <summary>Live occupancy, not assignment: dog emblems on the floor and lamps on the service gantry.</summary>
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            if (!Spawned)
                return;
            int occupied = 0;
            for (int i = 0; i < docked.Count; i++)
            {
                Pawn dog = docked[i];
                if (dog.Dead)
                    continue;
                RobodogWork work = RobodogWork.Of(dog);
                int type = work?.combat == true ? 1 : work?.rescue == true ? 2 : 0;
                occupied |= 1 << type;
            }
            // The south SVG is the layout origin: 384 pixels across a three-cell canvas.
            Quaternion rotation = Quaternion.AngleAxis(Rotation.AsAngle - 180f, Vector3.up);
            drawLoc.y += Altitudes.AltInc * 2f;
            bool powered = CanCharge;
            for (int type = 0; type < 3; type++)
            {
                bool present = (occupied & (1 << type)) != 0;
                float x = (type - 1) * 0.84375f;
                Vector3 icon = drawLoc + rotation * new Vector3(x, 0f, -0.18f);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(icon, rotation, new Vector3(0.7f, 1f, 0.7f)),
                    DockIcons[type, present ? 1 : 0], 0);
                Vector3 lamp = drawLoc + rotation * new Vector3(x, 0f, 0.758f);
                Material light = !present ? EmptyDockLamp : powered ? OccupiedDockLamp : UnpoweredDockLamp;
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(lamp, rotation, new Vector3(0.54f, 1f, 0.1f)),
                    light, 0);
            }
        }

        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();
            if (Spawned && recovered.Count > 0)
                GenDraw.DrawFieldEdges(RebuildDeliveryCells.ToList(), new Color(0.25f, 0.8f, 1f));
            if (waypoints.Count == 0)
                return;
            GenDraw.DrawFieldEdges(waypoints, Color.yellow);
            for (int i = 0; i < waypoints.Count; i++)
                GenDraw.DrawLineBetween(waypoints[i].ToVector3Shifted(), waypoints[(i + 1) % waypoints.Count].ToVector3Shifted(),
                    SimpleColor.Yellow);
        }

        /// <summary>Explicit forced dispatch retries pending rescues before refreshing the shared network.</summary>
        public void TryDispatch(bool force = false)
        {
            DogNetwork network = DogNetwork.For(Map);
            if (network == null)
                return;
            if (force)
            {
                IReadOnlyList<NetworkTask> tasks = network.WarQueue;
                for (int i = 0; i < tasks.Count; i++)
                {
                    NetworkTask task = tasks[i];
                    if (task.kind == NetworkTaskKind.Rescue && task.assigneeId == null)
                        task.retryAt = 0;
                }
            }
            network.Tick(force);
        }

        protected override void Tick()
        {
            base.Tick();
            // A minified stable still ticks inside its MinifiedThing, with no map.
            if (!Spawned)
                return;
            DogNetwork.For(Map).Tick();
            if (this.IsHashIntervalTick(250))
            {
                // Done, or its dog gone or reassigned.
                foreach (Bill_UpgradeDog bill in BillStack.Bills.OfType<Bill_UpgradeDog>().Where(b => !b.Pending).ToList())
                    BillStack.Delete(bill);
            }
            TickRebuild();
            if (docked.Count == 0 || !CanCharge)
                return;
            foreach (Pawn dog in docked.InnerListForReading)
            {
                CompRobodog comp = CompRobodog.Of(dog);
                if (comp != null && comp.Battery < 1f)
                    comp.Charge(comp.Props.stableChargePerTick);
                Hediff_Injury injury = dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().FirstOrDefault();
                injury?.Heal(Props.repairPerTick);
            }
        }

        /// <summary>
        /// No colonist labor: a powered stable pays the existing recipe's ingredients from the physical delivery area,
        /// then works through its work amount one tick at a time. No power or no ingredients pauses it in place.
        /// </summary>
        private void TickRebuild()
        {
            if (recovered.Count == 0)
            {
                if (rebuildProgress != 0 || rebuildPaid)
                    ResetRebuild();
                return;
            }
            RecipeDef recipe = RebuildRecipe;
            if (recipe == null)
                return;
            Corpse wreck = recovered[0];
            Pawn dog = wreck?.InnerPawn;
            if (wreck == null || wreck.Destroyed || dog == null)
            {
                QR.Error($"{this} had an invalid recovered wreck; releasing it");
                if (wreck != null)
                    recovered.Remove(wreck);
                ResetRebuild();
                return;
            }
            if (!rebuildPaid)
            {
                // An active manual bill gets first choice; a reserved wreck cannot be rebuilt by both paths.
                if (Map.reservationManager.IsReserved(wreck) || ManualRebuildPending(recipe))
                    return;
                if (!CanCharge || !ConsumeRebuildMaterials(recipe))
                    return;
                rebuildPaid = true;
                QR.Log($"{this}: delivered rebuild materials consumed for {dog.LabelShortCap}");
            }
            if (!CanCharge)
                return;
            int work = RebuildWork;
            if (work <= 0)
                return;
            if (rebuildProgress < work)
                rebuildProgress++;
            if (rebuildProgress >= work)
                FinishRebuild(wreck);
        }

        private bool ManualRebuildPending(RecipeDef recipe)
        {
            foreach (Bill bill in BillStack.Bills)
            {
                if (bill.recipe != recipe || bill.suspended
                    || bill is Bill_Production production && production.repeatMode == BillRepeatModeDefOf.RepeatCount
                    && production.repeatCount <= 0)
                    continue;
                // A full roster scan is expensive. Until its next periodic check, conservatively give the bill priority.
                if (!this.IsHashIntervalTick(250) || bill.ShouldDoNow() && HasFreeSlot)
                    return true;
            }
            return false;
        }

        private void FinishRebuild(Corpse wreck)
        {
            Pawn dog = wreck.InnerPawn;
            if (dog == null || !ResurrectionUtility.TryResurrect(dog, new ResurrectionParams { dontSpawn = true }))
            {
                // Never silently lose the wreck or work forever on a wreck that cannot be rebuilt: put it outside.
                QR.Error($"{this} couldn't automatically rebuild {dog?.LabelShortCap.ToStringSafe() ?? "a wreck"}");
                if (!wreck.Destroyed)
                    recovered.TryDrop(wreck, InteractionCell, Map, ThingPlaceMode.Near, out Thing _);
                else if (recovered.Contains(wreck))
                    recovered.Remove(wreck);
                ResetRebuild();
                return;
            }
            if (recovered.Contains(wreck))
                recovered.Remove(wreck);
            ResetRebuild();
            // The same pawn returns: its name, tier and gun picks are its own saved state. The reconstruction makes it
            // whole and charged; Tiers re-equips the chosen default so it is ready the moment it docks.
            foreach (Hediff_Injury injury in dog.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                dog.health.RemoveHediff(injury);
            if (dog.Drafted)
                dog.drafter.Drafted = false;
            CompRobodog.Of(dog)?.SetBattery(1f);
            Tiers.Arm(dog, Tiers.Gun(dog, 0));
            TakeNewDog(dog);
            QR.Log($"{this} automatically rebuilt {dog} from its wreck");
        }

        private void ResetRebuild()
        {
            rebuildProgress = 0;
            rebuildPaid = false;
        }

        private static bool IsWreckIngredient(IngredientCount ingredient)
        {
            foreach (ThingDef def in ingredient.filter.AllowedThingDefs)
                if (typeof(Corpse).IsAssignableFrom(def.thingClass))
                    return true;
            return false;
        }

        private static IEnumerable<IngredientCount> MaterialIngredients(RecipeDef recipe) =>
            recipe.ingredients.Where(i => !IsWreckIngredient(i));

        private static bool MatchesIngredient(RecipeDef recipe, IngredientCount ingredient, ThingDef def) =>
            ingredient.filter.Allows(def)
            && (ingredient.IsFixedIngredient || recipe.fixedIngredientFilter == null
                || recipe.fixedIngredientFilter.Allows(def));

        private static string MaterialLabel(IngredientCount ingredient) =>
            string.Join(" or ", ingredient.filter.AllowedThingDefs.Select(d => d.label));

        private static int Required(IngredientCount ingredient) => Mathf.RoundToInt(ingredient.GetBaseCount());

        private static int Available(RecipeDef recipe, IngredientCount ingredient, List<Thing> stacks) =>
            stacks.Where(t => MatchesIngredient(recipe, ingredient, t.def)).Sum(t => t.stackCount);

        /// <summary>Item stacks physically on the interaction cell or its eight neighbours, excluding forbidden ones.</summary>
        private List<Thing> DeliveryStacks()
        {
            var stacks = new List<Thing>();
            foreach (IntVec3 cell in RebuildDeliveryCells)
                foreach (Thing thing in cell.GetThingList(Map))
                    if (IsDeliverable(thing))
                        stacks.Add(thing);
            return stacks;
        }

        private static bool IsDeliverable(Thing thing) =>
            thing.def.category == ThingCategory.Item && thing.stackCount > 0
            && !thing.IsForbidden(Faction.OfPlayer)
            // Never steal ingredients a colonist, bill or hauler already reserved.
            && thing.MapHeld?.reservationManager.IsReserved(thing) != true;

        /// <summary>
        /// Checks every material ingredient first, so a short delivery consumes nothing; then takes exactly the recipe's
        /// counts from those stacks. The wreck is already held separately and is not part of this consumption.
        /// </summary>
        private bool ConsumeRebuildMaterials(RecipeDef recipe)
        {
            // Check all ingredients before consuming any partial delivery.
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (IsWreckIngredient(ingredient))
                    continue;
                int left = Required(ingredient);
                if (left <= 0)
                    continue;
                foreach (IntVec3 cell in RebuildDeliveryCells)
                {
                    if (left <= 0)
                        break;
                    foreach (Thing thing in cell.GetThingList(Map))
                    {
                        if (left <= 0)
                            break;
                        if (IsDeliverable(thing) && MatchesIngredient(recipe, ingredient, thing.def))
                            left -= thing.stackCount;
                    }
                }
                if (left > 0)
                    return false;
            }
            // Everything is here: take it. Only a successful start allocates the bookkeeping.
            var taken = new Dictionary<Thing, int>();
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ingredient = recipe.ingredients[i];
                if (IsWreckIngredient(ingredient))
                    continue;
                int left = Required(ingredient);
                foreach (IntVec3 cell in RebuildDeliveryCells)
                {
                    if (left <= 0)
                        break;
                    foreach (Thing thing in cell.GetThingList(Map))
                    {
                        if (left <= 0)
                            break;
                        if (!IsDeliverable(thing) || !MatchesIngredient(recipe, ingredient, thing.def))
                            continue;
                        int already = taken.TryGetValue(thing, out int count) ? count : 0;
                        int use = Mathf.Min(left, thing.stackCount - already);
                        if (use <= 0)
                            continue;
                        taken[thing] = already + use;
                        left -= use;
                    }
                }
            }
            foreach (KeyValuePair<Thing, int> pair in taken)
            {
                Thing split = pair.Key.SplitOff(pair.Value);
                split.Destroy(DestroyMode.Vanish);
            }
            return true;
        }

        private string RebuildInspect(RecipeDef recipe)
        {
            Pawn dog = recovered.Count > 0 ? recovered[0]?.InnerPawn : null;
            if (dog == null)
                return "Rebuilding: no dog in the recovered wreck";
            string who = $"{dog.LabelShortCap} (Tier {CombatTier.Of(dog)?.tier ?? 0})";
            string power = CanCharge ? "" : "; paused: no power";
            if (!rebuildPaid)
            {
                if (ManualRebuildPending(recipe))
                    return $"Rebuilding {who}: waiting for an active colonist rebuild bill{power}";
                List<Thing> stacks = DeliveryStacks();
                string demand = string.Join(", ", MaterialIngredients(recipe).Select(i =>
                    $"{MaterialLabel(i)} {Available(recipe, i, stacks)}/{Required(i)}"));
                return $"Rebuilding {who}: waiting for materials at {RebuildDeliveryDescription}: {demand}{power}";
            }
            int work = RebuildWork;
            int percent = work <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(rebuildProgress * 100f / work), 0, 100);
            return $"Rebuilding {who}: {percent}% ({rebuildProgress}/{work} work), materials delivered at "
                + $"{RebuildDeliveryDescription}{power}";
        }

        public ThingOwner GetDirectlyHeldThings() => searchingBillIngredients ? recovered : docked;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            // Vanilla recursively searches a haul source: expose only wrecks, not docked dogs or their cargo.
            if (searchingBillIngredients)
                return;
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
            // Removal walks child holders; the wrecks must be reachable there like any other held content.
            if (recoveredHolder != null)
                outChildren.Add(recoveredHolder);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Minifying despawns with the same mode as a destroy but never calls Destroy, so this tells them apart.
            destroying = true;
            base.Destroy(mode);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Docked dogs come out; the shared queues transfer to another stable or the newest active dog.
            // Uninstalling preserves docking affiliation for when this stable is reinstalled.
            Map map = Map;
            if (docked.Count > 0)
                docked.TryDropAll(InteractionCell, map, ThingPlaceMode.Near);
            if (recovered.Count > 0)
            {
                int count = recovered.Count;
                // Wrecks are never lost to a destroy or an uninstall: their corpses and contents go back onto the map.
                // Materials already consumed are not refunded when the stable is removed.
                recovered.TryDropAll(InteractionCell, map, ThingPlaceMode.Near);
                QR.Log($"{this} released {count - recovered.Count} recovered wreck(s)");
                if (recovered.Count > 0)
                    QR.Error($"{this} couldn't release {recovered.Count} recovered wreck(s)");
            }
            ResetRebuild();
            if (destroying)
            {
                foreach (Pawn dog in map.mapPawns.AllPawns.Where(p => CompRobodog.Of(p)?.stable == this))
                    CompRobodog.Of(dog).stable = null;
                QR.Log($"{this} destroyed; its dogs are unassigned");
            }
            else
                QR.Log($"{this} despawned (uninstalled?); its {Dogs().Count} dogs wait for it");
            DogNetwork.For(map).NodeLeaving(this);
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            var lines = new List<string>();
            string s = base.GetInspectString();
            if (!s.NullOrEmpty())
                lines.Add(s);
            lines.Add($"Robodogs: {Dogs().Count} / {Capacity} ({docked.Count} docked)");
            if (Spawned && recovered.Count > 0)
            {
                lines.Add($"Recovered wrecks: {recovered.Count}");
                RecipeDef recipe = RebuildRecipe;
                lines.Add(recipe == null ? "Rebuilding: recipe missing; wrecks wait safely" : RebuildInspect(recipe));
            }
            string network = DogNetwork.For(Map)?.Inspect();
            if (!network.NullOrEmpty())
                lines.Add(network);
            return string.Join("\n", lines);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref docked, "docked", this);
            if (recoveredHolder == null)
                recoveredHolder = new RecoveredHolder(this);
            ThingOwner<Corpse> wrecks = recoveredHolder.Contents;
            Scribe_Deep.Look(ref wrecks, "recoveredWrecks", recoveredHolder);
            recoveredHolder.Contents = wrecks;
            Scribe_Values.Look(ref rebuildProgress, "rebuildProgress", 0);
            Scribe_Values.Look(ref rebuildPaid, "rebuildPaid", false);
            Scribe_Deep.Look(ref networkState, "network");
            Scribe_Collections.Look(ref waypoints, "waypoints", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                waypoints = waypoints ?? new List<IntVec3>();
                if (recoveredHolder.Contents == null)
                    recoveredHolder.Contents = new ThingOwner<Corpse>(recoveredHolder);
                // Older saves may already hold wrecks that retained vanilla's outside-Home forbid flag.
                foreach (Corpse wreck in recovered.InnerListForReading)
                    wreck.TrySetForbidden(false);
                if (recovered.Count == 0)
                    ResetRebuild();
            }
        }
    }
}
