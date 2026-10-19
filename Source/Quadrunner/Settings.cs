using UnityEngine;
using Verse;

namespace Quadrunner
{
    public class QuadrunnerSettings : ModSettings
    {
        /// <summary>Charge spots closer than this to a wind turbine or its clear area don't count.</summary>
        public int turbineDistance = 6;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref turbineDistance, "turbineDistance", 6);
        }
    }

    public class QuadrunnerMod : Mod
    {
        public static QuadrunnerSettings Settings;

        public QuadrunnerMod(ModContentPack content) : base(content) => Settings = GetSettings<QuadrunnerSettings>();

        public override string SettingsCategory() => "Quadrunner";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label($"Keep charge spots at least {Settings.turbineDistance} cells from wind turbines and their clear area");
            Settings.turbineDistance = Mathf.RoundToInt(list.Slider(Settings.turbineDistance, 0f, 15f));
            list.End();
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            ChargeSpots.ClearAll();
        }
    }
}
