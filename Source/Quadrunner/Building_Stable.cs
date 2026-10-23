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
    public class Building_Stable : Building_WorkTable, IThingHolder
    {
        private static readonly Texture2D PatrolIcon = ContentFinder<Texture2D>.Get("Quadrunner/UI/Patrol");
        private static readonly Texture2D ClearPatrolIcon = ContentFinder<Texture2D>.Get("Quadrunner/UI/ClearPatrol");
        private static readonly Material[,] DockIcons = MakeDockIcons();
        private static readonly Material EmptyDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(0.22f, 0.29f, 0.31f));
        private static readonly Material OccupiedDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(0.4f, 1f, 0.6f));
        private static readonly Material UnpoweredDockLamp = MaterialPool.MatFrom("Quadrunner/Buildings/DockLamp",
            ShaderDatabase.Cutout, new Color(1f, 0.65f, 0.2f));

        private ThingOwner<Pawn> docked;
        public NetworkState networkState;
        private List<IntVec3> waypoints = new List<IntVec3>();
        // Set by a click while placing waypoints, so the targeter starts again once vanilla stops it after the click.
        private bool placingMore;
        private bool destroying;

        public Building_Stable() => docked = new ThingOwner<Pawn>(this);

        private StableProps Props => def.GetModExtension<StableProps>();

        public int Capacity => Props.capacity;

        public IReadOnlyList<Pawn> Docked => docked.InnerListForReading;

        /// <summary>Every living dog of this stable on its map: docked, out, or carried.</summary>
        public List<Pawn> Dogs() =>
            MapHeld?.mapPawns.AllPawns.Where(p => CompRobodog.Of(p)?.stable == this).ToList() ?? Docked.ToList();

        public bool HasFreeSlot => Dogs().Count < Capacity;

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
            QR.Log($"{dog} undocked from {this}");
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

        /// <summary>An unspawned dog, new or rebuilt: docked if a slot is free, else left beside the stable.</summary>
        public void TakeNewDog(Pawn dog)
        {
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
            if (waypoints.Count == 0)
                return;
            GenDraw.DrawFieldEdges(waypoints, Color.yellow);
            for (int i = 0; i < waypoints.Count; i++)
                GenDraw.DrawLineBetween(waypoints[i].ToVector3Shifted(), waypoints[(i + 1) % waypoints.Count].ToVector3Shifted(),
                    SimpleColor.Yellow);
        }

        /// <summary>The debug dispatch action refreshes the shared network, not a stable-local job search.</summary>
        public void TryDispatch(bool force = false) => DogNetwork.For(Map)?.Tick(force);

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

        public ThingOwner GetDirectlyHeldThings() => docked;

        public void GetChildHolders(List<IThingHolder> outChildren) =>
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

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
            string s = base.GetInspectString();
            string dogs = $"Robodogs: {Dogs().Count} / {Capacity} ({docked.Count} docked)";
            return (s.NullOrEmpty() ? dogs : s + "\n" + dogs) + "\n" + DogNetwork.For(Map)?.Inspect();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref docked, "docked", this);
            Scribe_Deep.Look(ref networkState, "network");
            Scribe_Collections.Look(ref waypoints, "waypoints", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                waypoints = waypoints ?? new List<IntVec3>();
            }
        }
    }
}
