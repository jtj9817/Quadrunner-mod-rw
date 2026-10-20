using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>
    /// The stable's Overview tab: one row per assigned dog with its type, battery, health and current job; for
    /// Combatdogs also its tier, what fills each weapon slot (a button picks from the tier's list) and an upgrade button
    /// that orders the next tier's bill (design).
    /// </summary>
    public class ITab_StableOverview : ITab
    {
        private static readonly Vector2 WinSize = new Vector2(840f, 420f);
        private static readonly float[] ColumnWidths = { 120f, 90f, 60f, 60f, 110f, 40f, 0f };
        private static readonly string[] Headers = { "Name", "Type", "Battery", "Health", "Doing", "Tier", "Weapons" };
        private const float UpgradeWidth = 120f;
        private const float RowHeight = 26f;

        private Vector2 scrollPosition;

        public ITab_StableOverview()
        {
            size = WinSize;
            labelKey = "QR_TabOverview";
        }

        private Building_Stable Stable => (Building_Stable)SelThing;

        /// <summary>The tab's row values, docked dogs first.</summary>
        public static List<(Pawn dog, string[] cells)> Rows(Building_Stable stable) =>
            stable.Dogs()
                .OrderBy(d => stable.Docked.Contains(d) ? 0 : 1)
                .ThenBy(d => d.LabelShort)
                .Select(d => (d, new[]
                {
                    d.LabelShortCap,
                    d.kindDef.LabelCap.ToString(),
                    CompRobodog.Of(d).Battery.ToStringPercent(),
                    d.health.summaryHealth.SummaryHealthPercent.ToStringPercent(),
                    stable.Docked.Contains(d) ? "Docked" : d.jobs?.curDriver?.GetReport().CapitalizeFirst() ?? "Idle",
                    CombatTier.Of(d) is CombatTier t ? t.tier.ToString() : "",
                    CombatTier.Of(d) != null ? Tiers.Describe(d) : "",
                }))
                .ToList();

        protected override void FillTab()
        {
            Building_Stable stable = Stable;
            List<(Pawn dog, string[] cells)> rows = Rows(stable);
            Rect inner = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);
            Text.Font = GameFont.Small;
            Rect title = new Rect(inner.x, inner.y, inner.width - 100f, RowHeight);
            Widgets.Label(title,
                $"Robodogs: {rows.Count} / {stable.Capacity} ({stable.Docked.Count} docked)");
            TooltipHandler.TipRegion(title, new TipSignal(stable.def.description, stable.thingIDNumber));
            DrawItemTooltip(new Rect(inner.xMax - 80f, inner.y, 24f, 24f), QR_DefOf.QR_RobodogBattery);
            DrawItemTooltip(new Rect(inner.xMax - 52f, inner.y, 24f, 24f), QR_DefOf.QR_RobodogRepairKit);
            Rect header = new Rect(inner.x, inner.y + RowHeight, inner.width, RowHeight);
            DrawRow(header, Headers, bold: true, dog: null);
            Rect outRect = new Rect(inner.x, header.yMax, inner.width, inner.yMax - header.yMax);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (int i = 0; i < rows.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                Pawn dog = rows[i].dog;
                Rect cells = new Rect(row.x, row.y, row.width - UpgradeWidth, row.height);
                CombatTier tier = CombatTier.Of(dog);
                // A Combatdog's Weapons cell is drawn as its button instead.
                DrawRow(cells, tier?.SlotCount > 0 ? rows[i].cells.Take(6).ToArray() : rows[i].cells, bold: false, dog: dog);
                if (tier != null)
                    DrawCombatdogButtons(stable, dog, tier, rows[i].cells[6], cells,
                        new Rect(cells.xMax, row.y, UpgradeWidth, row.height).ContractedBy(2f));
                else if (dog.Spawned && Widgets.ButtonInvisible(row))
                    CameraJumper.TryJumpAndSelect(dog);
            }
            Widgets.EndScrollView();
        }

        private static void DrawItemTooltip(Rect rect, ThingDef item)
        {
            Widgets.DefIcon(rect, item);
            if (Mouse.IsOver(rect))
                TooltipHandler.TipRegion(rect, item.LabelCap.AsTipTitle() + "\n\n" + item.description);
        }

        /// <summary>The Weapons cell as a button listing each slot's choices, and the upgrade button.</summary>
        private static void DrawCombatdogButtons(Building_Stable stable, Pawn dog, CombatTier tier, string description,
            Rect cells, Rect upgrade)
        {
            float weaponsX = cells.x + ColumnWidths.Take(ColumnWidths.Length - 1).Sum();
            Rect weapons = new Rect(weaponsX, cells.y, cells.xMax - weaponsX, cells.height).ContractedBy(2f);
            if (tier.SlotCount > 0 && Widgets.ButtonText(weapons, description))
            {
                var options = new List<FloatMenuOption>();
                for (int slot = 0; slot < tier.SlotCount; slot++)
                    foreach (ThingDef gun in tier.Slot(slot))
                    {
                        int s = slot;
                        options.Add(new FloatMenuOption($"Slot {slot + 1}: {gun.LabelCap}{(Tiers.Gun(dog, slot) == gun ? " (fitted)" : "")}",
                            () => Tiers.PickGun(dog, s, gun)) { tooltip = new TipSignal(gun.description) });
                    }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (tier.SlotCount > 0 && Mouse.IsOver(weapons))
            {
                string tip = description + "\n\n" + Tiers.Gun(dog, 0).description;
                if (tier.SlotCount > 1)
                    tip += "\n\n" + Tiers.Gun(dog, 1).description;
                TooltipHandler.TipRegion(weapons, tip);
            }
            if (tier.upgrade == null)
                return;
            Bill_UpgradeDog bill = stable.UpgradeBill(dog);
            if (bill != null)
            {
                if (Widgets.ButtonText(upgrade, "Cancel upgrade"))
                    stable.BillStack.Delete(bill);
                TooltipHandler.TipRegion(upgrade, $"{bill.LabelCap}: done at this stable while the dog is docked.");
                return;
            }
            if (Widgets.ButtonText(upgrade, $"Upgrade to Tier {tier.tier + 1}"))
                stable.OrderUpgrade(dog);
            TooltipHandler.TipRegion(upgrade, $"{tier.upgrade.description}\n\nCost: "
                + string.Join(", ", tier.upgrade.ingredients.Select(ing => ing.Summary)));
        }

        private static void DrawRow(Rect rect, string[] cells, bool bold, Pawn dog)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = bold ? Color.gray : Color.white;
            float x = rect.x;
            for (int c = 0; c < cells.Length; c++)
            {
                float w = ColumnWidths[c] > 0f ? ColumnWidths[c] : rect.xMax - x;
                Widgets.Label(new Rect(x + 4f, rect.y, w - 4f, rect.height), cells[c].Truncate(w - 4f));
                Rect cell = new Rect(x, rect.y, w, rect.height);
                string tip = dog != null && c < 2 && Mouse.IsOver(cell)
                    ? cells[c] + "\n\n" + dog.DescriptionFlavor
                    : cells[c];
                TooltipHandler.TipRegion(cell, tip);
                x += w;
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
