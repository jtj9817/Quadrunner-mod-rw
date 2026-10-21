using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Quadrunner
{
    public class CompProperties_Robodog : CompProperties
    {
        public float drainPerCell = 0.0002857143f;
        public float seekChargeAt = 0.1f;
        public float conduitChargePerTick = 0.0004f;
        public float stableChargePerTick = 0.0012f;
        public float energyPerFullCharge = 100f;
        /// <summary>A powered-down dog is left this long to colonists bringing a robodog battery, when one is in stock.</summary>
        public int fieldFixWaitTicks = 2500;
        /// <summary>Colonists bring a repair kit to a dog in the field below this health.</summary>
        public float fieldRepairBelow = 0.7f;

        public CompProperties_Robodog() => compClass = typeof(CompRobodog);
    }

    /// <summary>
    /// On every robodog: its stable (the only record of it; the stable finds its dogs through this) and its battery,
    /// drained per cell walked. At <see cref="CompProperties_Robodog.seekChargeAt"/> the dog wants a charge until full;
    /// at 0% it powers down.
    /// </summary>
    public class CompRobodog : ThingComp
    {
        public Building_Stable stable;
        public NetworkState networkState;
        public int builtOrder;
        public int BuildOrder => builtOrder == 0 ? Dog.thingIDNumber : builtOrder;
        private float battery = 1f;
        private bool wantsCharge;
        private IntVec3 lastCell = IntVec3.Invalid;
        /// <summary>Combatdogs: patrol waypoints reached so far; the next one is this modulo the stable's count.</summary>
        public int waypointsReached;
        // Combatdogs: the built-in gun picked for each weapon slot (null: the slot's default), see Tiers.
        private ThingDef gun1, gun2;
        // What the dog carries and since when, for the flip onto its back (RobodogPose). Not saved: a dog loaded or
        // spawned with a pawn on its back has had it there all along.
        private Thing carried;
        private int carriedSince = int.MinValue / 2;

        public CompProperties_Robodog Props => (CompProperties_Robodog)props;

        public Pawn Dog => (Pawn)parent;

        public float Battery => battery;

        /// <summary>Set at the seek threshold, cleared once full.</summary>
        public bool WantsCharge => wantsCharge;

        public bool PoweredDown => Dog.health.hediffSet.HasHediff(QR_DefOf.QR_PoweredDown);

        /// <summary>Per-cell drain multiplier: <see cref="RobodogWork.raidDrainFactor"/> for a working dog during a raid (not while ridden).</summary>
        public float DrainFactor
        {
            get
            {
                RobodogWork work = RobodogWork.Of(parent);
                return work != null && parent.Spawned && Riding.RiderOf(Dog) == null && Raid.Active(parent.Map)
                    ? work.raidDrainFactor : 1f;
            }
        }

        public static CompRobodog Of(Thing thing) => (thing as Pawn)?.TryGetComp<CompRobodog>();

        public static bool IsRobodog(Thing thing) => Of(thing) != null;

        /// <summary>A Combatdog out of its stable walking off to charge: it flashes its yellow siren (design).</summary>
        public bool Retreating => wantsCharge && parent.Spawned && !PoweredDown
            && RobodogWork.Of(parent)?.combat == true && Dog.pather.Moving;

        /// <summary>The siren's flash phase (2 Hz, real time, so it shows while paused too).</summary>
        public static bool SirenPhaseLit => (int)(Time.realtimeSinceStartup * 4f) % 2 == 0;

        public void SetBattery(float level)
        {
            battery = Mathf.Clamp01(level);
            if (battery >= 0.9999f)
                battery = 1f;
            if (battery <= Props.seekChargeAt && !wantsCharge)
            {
                wantsCharge = true;
                // Mid-fight or guarding: retreat now, not when the job next expires. A capture already under way finishes.
                // Drafted: undrafted, so it retreats rather than powering down where the player left it (agent, 2026-10-20).
                JobDef job = Dog.CurJobDef;
                if (Dog.Drafted)
                {
                    QR.Log($"{Dog} at {battery:P0}: undrafted to charge");
                    Dog.drafter.Drafted = false;
                }
                else if (RobodogWork.Of(parent)?.combat == true && parent.Spawned
                    && (job == JobDefOf.AttackMelee || job == JobDefOf.AttackStatic || job == JobDefOf.Wait
                        || job == JobDefOf.Goto))
                {
                    QR.Log($"{Dog} at {battery:P0}: retreats from {job.defName} to charge");
                    Dog.jobs.EndCurrentJob(JobCondition.InterruptForced);
                }
            }
            if (battery >= 1f)
                wantsCharge = false;
            bool poweredDown = PoweredDown;
            if (battery <= 0f && !poweredDown)
            {
                Dog.health.AddHediff(QR_DefOf.QR_PoweredDown);
                QR.Log($"{Dog} powered down");
                DogNetwork.For(parent.MapHeld)?.NodeLeaving(Dog);
            }
            else if (battery > 0f && poweredDown)
            {
                Dog.health.RemoveHediff(Dog.health.hediffSet.GetFirstHediffOfDef(QR_DefOf.QR_PoweredDown));
                QR.Log($"{Dog} powered up at {battery:P0}");
            }
        }

        public ThingDef PickedGun(int slot) => slot == 0 ? gun1 : gun2;

        /// <summary>
        /// The tick the dog picked up <paramref name="thing"/>, which it carries: now, when not yet seen (comps tick before
        /// the job that picks it up).
        /// </summary>
        public int CarriedSince(Thing thing) => thing == carried ? carriedSince : Find.TickManager.TicksGame;

        public void PickGun(int slot, ThingDef gun)
        {
            if (slot == 0)
                gun1 = gun;
            else
                gun2 = gun;
        }

        /// <summary>Adds <paramref name="amount"/> (0–1) of charge.</summary>
        public void Charge(float amount) => SetBattery(battery + amount);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            carried = Dog.carryTracker?.CarriedThing;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            DogNetwork.For(map)?.NodeLeaving(Dog);
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            DogNetwork.For(previousMap)?.NodeLeaving(Dog);
        }

        public override void CompTick()
        {
            if (!parent.Spawned)
            {
                lastCell = IntVec3.Invalid;
                return;
            }
            DogNetwork.For(parent.Map).Tick();
            Thing nowCarried = Dog.carryTracker?.CarriedThing;
            if (nowCarried != carried)
            {
                carried = nowCarried;
                carriedSince = Find.TickManager.TicksGame;
            }
            IntVec3 cell = parent.Position;
            // One step at a time is walking; a longer jump is a teleport (dev tools, scenarios) and costs nothing.
            float moved = lastCell.IsValid ? cell.DistanceTo(lastCell) : 0f;
            if (moved > 0f && moved < 1.5f && battery > 0f)
                SetBattery(battery - Props.drainPerCell * moved * DrainFactor);
            lastCell = cell;
        }

        /// <summary>Hit by a hostile pawn: alert the current network node, including a dog acting without a stable.</summary>
        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            if (dinfo.Instigator is Pawn attacker && attacker.HostileTo(Faction.OfPlayer)
                && parent.Spawned && !Dog.Dead)
                DogNetwork.For(parent.Map).Ping(attacker, Dog);
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            DogNetwork.For(prevMap)?.NodeLeaving(Dog);
            QR.Log($"{Dog} killed by {dinfo?.Instigator.ToStringSafe()} ({dinfo?.Def?.defName}, {dinfo?.Weapon?.defName}, {dinfo?.HitPart?.LabelShort}); "
                + $"destroyed: {string.Join(", ", Dog.health.hediffSet.hediffs.OfType<Hediff_MissingPart>().Select(h => h.Part.LabelShort))}");
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
                yield return g;
            foreach (Gizmo g in Drafting.Gizmos(Dog))
                yield return g;
        }

        /// <summary>A Medirunner on a rescue run, to the patient and on to the bed: its tail siren flashes.</summary>
        public bool Rescuing => parent.Spawned && !PoweredDown
            && (Dog.CurJobDef == JobDefOf.Rescue || Dog.CurJobDef == JobDefOf.TakeWoundedPrisonerToBed);

        public override void PostDraw()
        {
            base.PostDraw();
            if (!SirenPhaseLit)
                return;
            if (Retreating)
                Siren.Draw(Dog.DrawPos + new Vector3(0f, 0f, 0.35f), Siren.Core, Siren.Glow, 0.5f);
            else if (parent.def.GetModExtension<TailSiren>() is TailSiren tail && Rescuing)
                Siren.Draw(Dog.DrawPos + tail.OffsetFor(Dog.Rotation), Siren.RescueCore, Siren.RescueGlow, tail.size);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref stable, "stable");
            Scribe_Deep.Look(ref networkState, "network");
            Scribe_Values.Look(ref builtOrder, "builtOrder");
            Scribe_Values.Look(ref battery, "battery", 1f);
            Scribe_Values.Look(ref wantsCharge, "wantsCharge");
            Scribe_Values.Look(ref waypointsReached, "waypointsReached");
            Scribe_Defs.Look(ref gun1, "gun1");
            Scribe_Defs.Look(ref gun2, "gun2");
        }

        public override string CompInspectStringExtra() =>
            $"Battery: {battery.ToStringPercent()}{(wantsCharge ? " (wants a charge)" : "")}\n" +
            (stable == null ? "No stable" : $"Stable: {stable.LabelCap}")
            + (parent.MapHeld == null ? "" : "\n" + DogNetwork.For(parent.MapHeld).Inspect(Dog));
    }

    /// <summary>
    /// The retreat siren's light: a yellow lamp (<c>Quadrunner/Things/Siren</c>, with alpha) under vanilla's lightning
    /// glow tinted yellow, drawn additive around it (the glow alone hardly shows on bright ground). The Medirunner's
    /// tail siren is the same lamp in red.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Siren
    {
        public static readonly Material Core = MaterialPool.MatFrom("Quadrunner/Things/Siren", ShaderDatabase.Transparent);
        public static readonly Material Glow =
            MaterialPool.MatFrom("Things/Mote/LightningGlow", ShaderDatabase.MoteGlow, new Color(1f, 0.85f, 0.1f));
        public static readonly Material RescueCore =
            MaterialPool.MatFrom("Quadrunner/Things/Siren", ShaderDatabase.Transparent, new Color(1f, 0.25f, 0.3f));
        public static readonly Material RescueGlow =
            MaterialPool.MatFrom("Things/Mote/LightningGlow", ShaderDatabase.MoteGlow, new Color(1f, 0.1f, 0.15f));

        /// <summary>The lamp <paramref name="size"/> cells wide at <paramref name="at"/>, its glow 2.4 times that, over everything.</summary>
        public static void Draw(Vector3 at, Material core, Material glow, float size)
        {
            at.y = AltitudeLayer.MetaOverlays.AltitudeFor();
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(at, Quaternion.identity, new Vector3(size, 1f, size)), core, 0);
            at.y += 0.01f;
            Graphics.DrawMesh(MeshPool.plane10,
                Matrix4x4.TRS(at, Quaternion.identity, new Vector3(size * 2.4f, 1f, size * 2.4f)), glow, 0);
        }
    }

    /// <summary>
    /// A siren lamp on the dog's tail (<c>Art/</c>: the Medirunner's red dome), flashing while it rescues
    /// (<see cref="CompRobodog.Rescuing"/>). Offsets in cells from the dog's center, per facing; west mirrors east.
    /// </summary>
    public class TailSiren : DefModExtension
    {
        public Vector3 north, east, south;
        public float size = 0.3f;

        public Vector3 OffsetFor(Rot4 facing) =>
            facing == Rot4.North ? north : facing == Rot4.South ? south
            : facing == Rot4.East ? east : new Vector3(-east.x, east.y, east.z);
    }
}
