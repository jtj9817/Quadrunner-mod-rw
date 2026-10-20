using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public class StableProps : DefModExtension
    {
        public int capacity = 15;
        public float serviceRadius = 40f;
        public int dispatchInterval = 250;
        /// <summary>Ticks to wait after a dispatch found no work.</summary>
        public int idleBackoff = 1000;
        /// <summary>Hit points healed per tick on a docked dog's injuries while powered.</summary>
        public float repairPerTick = 0.02f;
    }

    /// <summary>
    /// Docks robodogs (despawned into <see cref="docked"/>), sends them out to work, repairs and charges them while
    /// powered, and runs the dog bills. A dog belongs to the stable its <see cref="CompRobodog.stable"/> names, whether
    /// docked or out; all of them share <see cref="Capacity"/> slots.
    /// </summary>
    public class Building_Stable : Building_WorkTable, IThingHolder
    {
        private ThingOwner<Pawn> docked;
        // Not saved: after a load the stable just checks again.
        private int nextDispatchTick;
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

        /// <summary>Within the service radius (construction delivery ignores it).</summary>
        public bool Covers(IntVec3 cell) => cell.InHorDistOf(Position, Props.serviceRadius);

        /// <summary>A docked worker (not a medic) that could be sent out now, not waiting for a charge.</summary>
        public bool HasIdleDog => IdleDog(medic: false) != null;

        /// <summary>The docked dog a colonist would ride: rideable, the fullest not waiting for a charge.</summary>
        public Pawn IdleRideable => IdleDog(d => Rideable.Of(d) != null);

        private Pawn IdleDog(bool medic) => IdleDog(d => RobodogWork.Of(d) is RobodogWork w && w.rescue == medic);

        private Pawn IdleDog(Func<Pawn, bool> kind) => docked.InnerListForReading
            .Where(d => kind(d) && CompRobodog.Of(d) is CompRobodog c && !c.WantsCharge && !c.PoweredDown)
            .OrderByDescending(d => CompRobodog.Of(d).Battery)
            .FirstOrDefault();

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

        /// <summary>
        /// Sends out one idle dog if there is work for it: a medic while someone is down (raid or not), else a worker
        /// (never during a raid). The work search needs a spawned dog, so the dog steps out and, if it finds nothing,
        /// docks again in the same tick; once a dog tried and found nothing, the stable waits <see cref="StableProps.idleBackoff"/>.
        /// </summary>
        public void TryDispatch(bool force = false)
        {
            int now = Find.TickManager.TicksGame;
            if (!force && now < nextDispatchTick)
                return;
            bool tried = false;
            if (Rescue.AnyPatient(Map) && TrySend(IdleDog(medic: true), ref tried)
                || !Raid.Active(Map) && TrySend(IdleDog(medic: false), ref tried))
                return;
            if (tried)
                nextDispatchTick = now + Props.idleBackoff;
        }

        private bool TrySend(Pawn dog, ref bool tried)
        {
            if (dog == null || !docked.TryDrop(dog, InteractionCell, Map, ThingPlaceMode.Near, out Thing _))
                return false;
            tried = true;
            Job job = DogWork.FindJob(dog);
            if (job == null)
            {
                TryDock(dog, log: false);
                return false;
            }
            dog.jobs.StartJob(job, JobCondition.InterruptForced);
            QR.Log($"{this} dispatched {dog} ({CompRobodog.Of(dog).Battery:P0}): {job.def.defName} {job.targetA}");
            return true;
        }

        protected override void Tick()
        {
            base.Tick();
            // A minified stable still ticks inside its MinifiedThing, with no map.
            if (!Spawned)
                return;
            if (this.IsHashIntervalTick(Props.dispatchInterval))
                TryDispatch();
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
            // Docked dogs come out. Destroyed: they lose their stable and wait in the nearest room (or adopt another
            // stable); uninstalled: they keep it, wait, and walk to it once it is installed again.
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
