using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Quadrunner
{
    public class StableProps : DefModExtension
    {
        public int capacity = 15;
    }

    /// <summary>
    /// Docks robodogs (despawned into <see cref="docked"/>) and runs the dog bills. A dog belongs to the stable its
    /// <see cref="CompRobodog.stable"/> names, whether docked or out; all of them share <see cref="Capacity"/> slots.
    /// </summary>
    public class Building_Stable : Building_WorkTable, IThingHolder
    {
        private ThingOwner<Pawn> docked;

        public Building_Stable() => docked = new ThingOwner<Pawn>(this);

        public int Capacity => def.GetModExtension<StableProps>().capacity;

        public IReadOnlyList<Pawn> Docked => docked.InnerListForReading;

        /// <summary>Every living dog of this stable on its map: docked, out, or carried.</summary>
        public List<Pawn> Dogs() =>
            MapHeld?.mapPawns.AllPawns.Where(p => CompRobodog.Of(p)?.stable == this).ToList() ?? Docked.ToList();

        public bool HasFreeSlot => Dogs().Count < Capacity;

        /// <summary>Docked dogs charge only while the stable has power.</summary>
        public bool CanCharge => this.TryGetComp<CompPowerTrader>()?.PowerOn ?? true;

        /// <summary>Docks <paramref name="dog"/>, assigning it to this stable first if it isn't yet.</summary>
        public bool TryDock(Pawn dog)
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

        /// <summary>A new dog from a bill or a debug action: docked if a slot is free, else left beside the stable.</summary>
        public Pawn AddNewDog(PawnKindDef kind)
        {
            Pawn dog = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true));
            if (dog.Name == null)
                dog.Name = PawnBioAndNameGenerator.GeneratePawnName(dog);
            if (!TryDock(dog))
            {
                GenSpawn.Spawn(dog, InteractionCell, Map);
                Messages.Message($"{dog.LabelShortCap} has no free slot at {LabelCap} and waits beside it.", dog,
                    MessageTypeDefOf.CautionInput);
                QR.Log($"{dog} made at a full {this}; waits unassigned");
            }
            return dog;
        }

        protected override void Tick()
        {
            base.Tick();
            if (docked.Count == 0 || !CanCharge)
                return;
            foreach (Pawn dog in docked.InnerListForReading)
            {
                CompRobodog comp = CompRobodog.Of(dog);
                if (comp != null && comp.Battery < 1f)
                    comp.Charge(comp.Props.stableChargePerTick);
            }
        }

        public ThingOwner GetDirectlyHeldThings() => docked;

        public void GetChildHolders(List<IThingHolder> outChildren) =>
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            // Destroyed or uninstalled: docked dogs come out instead of vanishing with the stable (T3 refines this).
            if (docked.Count > 0)
                docked.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            string s = base.GetInspectString();
            string dogs = $"Robodogs: {Dogs().Count} / {Capacity} ({docked.Count} docked)";
            return s.NullOrEmpty() ? dogs : s + "\n" + dogs;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref docked, "docked", this);
        }
    }
}
