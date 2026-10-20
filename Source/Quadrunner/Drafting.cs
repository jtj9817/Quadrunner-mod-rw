using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// Combatdogs can be drafted and ordered like a colonist: moved by right-click and sent at a priority target with
    /// vanilla's melee attack order. Vanilla gives a drafter only to humanlikes, colony mechs and colony mutants, and
    /// its control checks (orders, drafted gizmos, attack orders) only pass those, so two patches: a drafter for player
    /// Combatdogs, and the "controllable subhuman" check passing while one is drafted. Undrafted, the draft toggle comes
    /// from <see cref="CompRobodog"/>. The think tree's drafted branch holds it in place between orders.
    /// </summary>
    public static class Drafting
    {
        public static bool Draftable(Pawn pawn) =>
            pawn.Faction == Faction.OfPlayer && !pawn.Dead && RobodogWork.Of(pawn)?.combat == true;

        /// <summary>
        /// The draft toggle while undrafted, as vanilla's (its own is internal); drafted, vanilla shows it with the other
        /// drafted gizmos. Grouped with colonists' toggles, so a mixed selection drafts together.
        /// </summary>
        public static IEnumerable<Gizmo> Gizmos(Pawn pawn)
        {
            if (pawn.drafter == null || pawn.Drafted || !pawn.Spawned)
                yield break;
            var toggle = new Command_Toggle
            {
                hotKey = KeyBindingDefOf.Command_ColonistDraft,
                isActive = () => pawn.Drafted,
                toggleAction = () => pawn.drafter.Drafted = !pawn.Drafted,
                defaultLabel = "CommandDraftLabel".Translate(),
                defaultDesc = "CommandToggleDraftDesc".Translate(),
                icon = TexCommand.Draft,
                turnOnSound = SoundDefOf.DraftOn,
                turnOffSound = SoundDefOf.DraftOff,
                groupKeyIgnoreContent = 81729172,
            };
            if (pawn.Downed)
                toggle.Disable("IsIncapped".Translate(pawn.LabelShort, pawn));
            yield return toggle;
        }
    }

    /// <summary>Harmony patch 4: player Combatdogs get a drafter (vanilla adds one only for humanlikes, colony mechs and mutants).</summary>
    [HarmonyPatch(typeof(PawnComponentsUtility), nameof(PawnComponentsUtility.AddAndRemoveDynamicComponents))]
    public static class Patch_PawnComponentsUtility_AddAndRemoveDynamicComponents
    {
        public static void Postfix(Pawn pawn, bool actAsIfSpawned)
        {
            if (pawn.drafter == null && (pawn.Spawned || actAsIfSpawned) && Drafting.Draftable(pawn))
                pawn.drafter = new Pawn_DraftController(pawn);
        }
    }

    /// <summary>
    /// Harmony patch 5: a drafted Combatdog counts as a controllable colony subhuman, the check vanilla's orders (move,
    /// melee attack), drafted gizmos and attack gizmo pass for non-colonists. Only while drafted, so undrafted it stays
    /// out of the player-controlled pawn lists.
    /// </summary>
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.IsColonySubhumanPlayerControlled), MethodType.Getter)]
    public static class Patch_Pawn_IsColonySubhumanPlayerControlled
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result && __instance.Drafted && __instance.Spawned && Drafting.Draftable(__instance))
                __result = true;
        }
    }
}
