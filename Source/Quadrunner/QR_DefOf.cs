using RimWorld;
using Verse;

namespace Quadrunner
{
    [DefOf]
    public static class QR_DefOf
    {
        public static ThingDef QR_Stable;
        public static PawnKindDef QR_Quadrunner;
        public static PawnKindDef QR_Medirunner;
        public static PawnKindDef QR_Combatdog;
        public static PawnKindDef QR_CombatdogTwo;
        public static PawnKindDef QR_CombatdogThree;
        public static PawnKindDef QR_CombatdogFour;
        public static ThingDef QR_Gun_AssaultRifle;
        public static ThingDef QR_Gun_LMG;
        public static ThingDef QR_Gun_Minigun;
        public static ThingDef QR_Gun_Flamethrower;
        public static ThingDef QR_Gun_DualChargeRifle;
        public static ThingDef QR_Gun_DualChainShotgun;
        public static ThingDef QR_Gun_SmokeLauncher;
        public static JobDef QR_Dock;
        public static JobDef QR_Survey;
        public static JobDef QR_ChargeAtConduit;
        public static JobDef QR_CarryToStable;
        public static JobDef QR_FieldFixRobodog;
        public static JobDef QR_RideTo;
        public static JobDef QR_BeingRidden;
        public static JobDef QR_CarryToRoom;
        public static JobDef QR_CleanWithHose;
        public static EffecterDef QR_HoseSpray;
        public static EffecterDef QR_HoseSprayBurst;
        public static ThingDef QR_RobodogBattery;
        public static ThingDef QR_RobodogRepairKit;
        public static HediffDef QR_PoweredDown;
        public static RecipeDef QR_AssembleQuadrunner;
        public static RecipeDef QR_AssembleMedirunner;
        public static RecipeDef QR_AssembleCombatdog;
        public static RecipeDef QR_RebuildCombatdog;
        public static WorkGiverDef QR_DoBillsStable;
        public static ResearchProjectDef MicroelectronicsBasics;

        static QR_DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(QR_DefOf));
    }
}
