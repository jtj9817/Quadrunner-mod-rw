using RimWorld;
using Verse;

namespace Quadrunner
{
    [DefOf]
    public static class QR_DefOf
    {
        public static ThingDef QR_Stable;
        public static PawnKindDef QR_Quadrunner;
        public static JobDef QR_Dock;
        public static JobDef QR_ChargeAtConduit;
        public static JobDef QR_CarryToStable;
        public static JobDef QR_FieldFixRobodog;
        public static ThingDef QR_RobodogBattery;
        public static ThingDef QR_RobodogRepairKit;
        public static HediffDef QR_PoweredDown;
        public static RecipeDef QR_AssembleQuadrunner;
        public static WorkGiverDef QR_DoBillsStable;
        public static ResearchProjectDef MicroelectronicsBasics;

        static QR_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(QR_DefOf));
    }
}
