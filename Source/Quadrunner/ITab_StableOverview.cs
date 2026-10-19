using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Quadrunner
{
    /// <summary>The stable's Overview tab: one row per assigned dog with its type, battery, health and current job.</summary>
    public class ITab_StableOverview : ITab
    {
        private static readonly Vector2 WinSize = new Vector2(560f, 420f);
        private static readonly float[] ColumnWidths = { 140f, 90f, 60f, 60f, 0f };
        private static readonly string[] Headers = { "Name", "Type", "Battery", "Health", "Doing" };
        private const float RowHeight = 26f;

        private Vector2 scrollPosition;

        /// <summary>Frames drawn; scenarios check it went up while the tab was open.</summary>
        public static int framesDrawn;

        public ITab_StableOverview()
        {
            size = WinSize;
            labelKey = "QR_TabOverview";
        }

        private Building_Stable Stable => (Building_Stable)SelThing;

        /// <summary>The tab's rows as text, docked dogs first; scenarios check these without drawing.</summary>
        public static List<(Pawn dog, string[] cells)> Rows(Building_Stable stable) =>
            stable.Dogs()
                .OrderBy(d => stable.Docked.Contains(d) ? 0 : 1)
                .ThenBy(d => d.LabelShort)
                .Select(d => (d, new[]
                {
                    d.LabelShortCap,
                    d.kindDef.LabelCap.ToString(),
                    "—", // battery: T2
                    d.health.summaryHealth.SummaryHealthPercent.ToStringPercent(),
                    stable.Docked.Contains(d) ? "Docked" : d.jobs?.curDriver?.GetReport().CapitalizeFirst() ?? "Idle",
                }))
                .ToList();

        protected override void FillTab()
        {
            framesDrawn++;
            Building_Stable stable = Stable;
            List<(Pawn dog, string[] cells)> rows = Rows(stable);
            Rect inner = new Rect(0f, 0f, WinSize.x, WinSize.y).ContractedBy(10f);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, RowHeight),
                $"Robodogs: {rows.Count} / {stable.Capacity} ({stable.Docked.Count} docked)");
            Rect header = new Rect(inner.x, inner.y + RowHeight, inner.width, RowHeight);
            DrawRow(header, Headers, bold: true);
            Rect outRect = new Rect(inner.x, header.yMax, inner.width, inner.yMax - header.yMax);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, rows.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect);
            for (int i = 0; i < rows.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 1)
                    Widgets.DrawLightHighlight(row);
                DrawRow(row, rows[i].cells, bold: false);
                Pawn dog = rows[i].dog;
                if (dog.Spawned && Widgets.ButtonInvisible(row))
                    CameraJumper.TryJumpAndSelect(dog);
            }
            Widgets.EndScrollView();
        }

        private static void DrawRow(Rect rect, string[] cells, bool bold)
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = bold ? Color.gray : Color.white;
            float x = rect.x;
            for (int c = 0; c < cells.Length; c++)
            {
                float w = ColumnWidths[c] > 0f ? ColumnWidths[c] : rect.xMax - x;
                Widgets.Label(new Rect(x + 4f, rect.y, w - 4f, rect.height), cells[c].Truncate(w - 4f));
                x += w;
            }
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }
    }
}
