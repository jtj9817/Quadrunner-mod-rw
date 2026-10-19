using RimWorld;
using Verse;

namespace Quadrunner
{
    public class CompProperties_Robodog : CompProperties
    {
        public CompProperties_Robodog() => compClass = typeof(CompRobodog);
    }

    /// <summary>On every robodog. The dog's stable is recorded only here; the stable finds its dogs through it.</summary>
    public class CompRobodog : ThingComp
    {
        public Building_Stable stable;

        public Pawn Dog => (Pawn)parent;

        public static CompRobodog Of(Thing thing) => (thing as Pawn)?.TryGetComp<CompRobodog>();

        public static bool IsRobodog(Thing thing) => Of(thing) != null;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref stable, "stable");
        }

        public override string CompInspectStringExtra() =>
            stable == null ? "No stable" : $"Stable: {stable.LabelCap}";
    }
}
