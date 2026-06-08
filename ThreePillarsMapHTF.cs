// ============================================================
// ThreePillarsMapHTF.cs  -  v1.0
// NinjaTrader 8  |  Daily / Higher-Timeframe Structural Wall Map
// ============================================================
//
// PURPOSE:
//   Companion to ThreePillarsMap (the intraday session map). This one
//   is built for DAILY (and other higher-timeframe) charts, where there
//   is no intraday detail to compute overnight / value-area / pivots.
//
//   It builds structural walls purely from SWING HIGHS / LOWS detected
//   on the chart's own bars. When price has pivoted at the same price
//   more than once, those swings cluster into a single wall whose
//   strength = the number of touches. A level that acted as BOTH support
//   and resistance (a polarity flip) is flagged "S/R" - the strongest.
//
// INSTALLATION:
//   1. Copy to: Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. Tools > NinjaScript Editor > Compile (F5)
//   3. Apply to a Daily (or Weekly / higher-TF) chart via
//      Indicators > ThreePillarsMapHTF
//
// VISUAL SYSTEM (shared with ThreePillarsMap):
//   Band  = a price tested repeatedly; thicker + brighter = more touches
//   **    = 2 touches    ***  = 3 touches    **** = 4+ touches
//   R     = resistance (above price)   S = support (below)
//   S/R   = level has flipped roles (strongest)
//   Dotted line = most recent swing high / low (immediate structure)
//
// CHANGELOG:
//   v1.0  2026-06-08  Initial release - swing-based daily wall map.
// ============================================================

#region Using Declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    // NOTE: TPMLegendPosition is already defined by ThreePillarsMap.cs in this
    // same namespace - we reuse it here rather than redefining it.

    public class ThreePillarsMapHTF : Indicator
    {
        #region Constants
        private const string TAG_PREFIX = "3PMH_";
        private const string VERSION    = "v1.0";
        private const string LOG_PREFIX = "[ThreePillarsMapHTF v1.0]";
        #endregion

        #region Wall Engine State
        private readonly List<string> wallTags = new List<string>();

        private class LevelCandidate
        {
            public double Price;
            public string Code;       // "H" or "L"
            public double Weight;
            public bool   IsAnchor;   // most-recent swings: always shown
        }

        private class Wall
        {
            public double Hi, Lo, Center, Score;
            public List<LevelCandidate> Members = new List<LevelCandidate>();
            public int Count { get { return Members.Count; } }
        }
        #endregion

        // =================================================================
        #region Parameters - Display

        [Display(Name = "Show Labels", GroupName = "Display", Order = 1)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Show Legend", GroupName = "Display", Order = 2)]
        public bool ShowLegend { get; set; }

        [Display(Name = "Legend Position", GroupName = "Display", Order = 3)]
        public TPMLegendPosition LegendPosition { get; set; }

        [Range(6, 16)]
        [Display(Name = "Label Font Size", GroupName = "Display", Order = 4)]
        public int LabelFontSize { get; set; }
        #endregion

        // =================================================================
        #region Parameters - Structural Walls

        [Range(1, 15)]
        [Display(Name = "Max Walls Per Side", GroupName = "Structural Walls", Order = 1,
            Description = "Maximum walls drawn above and below price.")]
        public int MaxWallsPerSide { get; set; }

        [Range(1, 5)]
        [Display(Name = "Min Touches", GroupName = "Structural Walls", Order = 2,
            Description = "Minimum swing touches at one level to qualify as a wall. 2 = only show levels tested at least twice.")]
        public int MinConfluence { get; set; }

        [Range(0.01, 0.50)]
        [Display(Name = "Cluster Tolerance %", GroupName = "Structural Walls", Order = 3,
            Description = "Swings within this percent of price merge into one wall. 0.06 = ~30 YM pts at 51000.")]
        public double ClusterTolerancePercent { get; set; }

        [Range(0.5, 20.0)]
        [Display(Name = "Wall Range %", GroupName = "Structural Walls", Order = 4,
            Description = "Only draw walls within this percent of current price (recent swings always show).")]
        public double WallRangePercent { get; set; }

        [Range(0, 5)]
        [Display(Name = "Recent Swings Always Shown", GroupName = "Structural Walls", Order = 5,
            Description = "How many of the most recent swing highs/lows to always draw, even if tested only once.")]
        public int RecentSwingAnchors { get; set; }

        [XmlIgnore]
        [Display(Name = "Resistance Wall Color", GroupName = "Structural Walls", Order = 6)]
        public Brush ResistanceColor { get; set; }
        [Browsable(false)]
        public string ResistanceColorSerializable
        { get { return Serialize.BrushToString(ResistanceColor); } set { ResistanceColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Support Wall Color", GroupName = "Structural Walls", Order = 7)]
        public Brush SupportColor { get; set; }
        [Browsable(false)]
        public string SupportColorSerializable
        { get { return Serialize.BrushToString(SupportColor); } set { SupportColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Recent Swing Color", GroupName = "Structural Walls", Order = 8)]
        public Brush AnchorColor { get; set; }
        [Browsable(false)]
        public string AnchorColorSerializable
        { get { return Serialize.BrushToString(AnchorColor); } set { AnchorColor = Serialize.StringToBrush(value); } }
        #endregion

        // =================================================================
        #region Parameters - Swing Detection

        [Range(1, 20)]
        [Display(Name = "Swing Strength", GroupName = "Swing Detection", Order = 1,
            Description = "Bars on each side a pivot must dominate to count as a swing high/low. Higher = fewer, more significant swings.")]
        public int SwingStrength { get; set; }

        [Range(20, 2000)]
        [Display(Name = "Swing Lookback Bars", GroupName = "Swing Detection", Order = 2,
            Description = "How many bars back to scan for swing pivots.")]
        public int SwingLookbackBars { get; set; }
        #endregion

        // =================================================================
        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                     = "ThreePillarsMapHTF";
                Description              = "Daily / Higher-Timeframe Structural Wall Map (swing-based) v1.0";
                IsOverlay                = true;
                IsSuspendedWhileInactive = true;
                Calculate                = Calculate.OnBarClose;
                DisplayInDataBox         = false;

                ShowLabels               = true;
                ShowLegend               = true;
                LegendPosition           = TPMLegendPosition.TopLeft;
                LabelFontSize            = 9;

                MaxWallsPerSide          = 6;
                MinConfluence            = 2;
                ClusterTolerancePercent  = 0.06;
                WallRangePercent         = 5.0;
                RecentSwingAnchors       = 2;

                ResistanceColor          = Brushes.Crimson;
                SupportColor             = Brushes.LimeGreen;
                AnchorColor              = Brushes.Silver;

                SwingStrength            = 3;
                SwingLookbackBars        = 300;
            }
            else if (State == State.DataLoaded)
            {
                wallTags.Clear();
            }
            else if (State == State.Terminated)
            {
                ClearWalls();
                try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { }
            }
        }
        #endregion

        // =================================================================
        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < SwingStrength * 2 + 5) return;

            // Daily bars are few; recompute the wall map each closed bar.
            BuildAndDrawWalls();
            DrawLegend();
        }
        #endregion

        // =================================================================
        #region Swing Detection

        private void GatherSwings(List<LevelCandidate> c)
        {
            int strength = SwingStrength;
            int limit    = Math.Min(CurrentBar - strength, SwingLookbackBars);

            var highs = new List<int>();   // bars ago, most-recent first
            var lows  = new List<int>();

            for (int i = strength; i <= limit; i++)
            {
                bool isH = true, isL = true;
                for (int k = 1; k <= strength; k++)
                {
                    if (High[i] <= High[i - k] || High[i] <= High[i + k]) isH = false;
                    if (Low[i]  >= Low[i - k]  || Low[i]  >= Low[i + k])  isL = false;
                }
                if (isH) highs.Add(i);
                if (isL) lows.Add(i);
            }

            for (int idx = 0; idx < highs.Count; idx++)
                c.Add(new LevelCandidate
                {
                    Price    = High[highs[idx]],
                    Code     = "H",
                    Weight   = 1.0,
                    IsAnchor = idx < RecentSwingAnchors
                });

            for (int idx = 0; idx < lows.Count; idx++)
                c.Add(new LevelCandidate
                {
                    Price    = Low[lows[idx]],
                    Code     = "L",
                    Weight   = 1.0,
                    IsAnchor = idx < RecentSwingAnchors
                });
        }
        #endregion

        // =================================================================
        #region Structural Wall Engine

        private double GetClusterTolerance()
        {
            double basePrice = Close[0] > 0 ? Close[0] : 1;
            return Math.Max(basePrice * (ClusterTolerancePercent / 100.0), TickSize * 4);
        }

        private void ClearWalls()
        {
            foreach (string t in wallTags)
                try { RemoveDrawObject(t); } catch { }
            wallTags.Clear();
        }

        private void RegWall(string tag) { if (!wallTags.Contains(tag)) wallTags.Add(tag); }

        private List<Wall> ClusterCandidates(List<LevelCandidate> cands, double tol)
        {
            cands.Sort((a, b) => a.Price.CompareTo(b.Price));
            var walls = new List<Wall>();
            int i = 0;
            while (i < cands.Count)
            {
                var w = new Wall();
                w.Lo = w.Hi = cands[i].Price;
                w.Members.Add(cands[i]);
                int j = i + 1;
                while (j < cands.Count
                       && cands[j].Price - w.Hi <= tol
                       && cands[j].Price - w.Lo <= tol * 2.0)
                {
                    w.Hi = cands[j].Price;
                    w.Members.Add(cands[j]);
                    j++;
                }
                double wsum = 0, psum = 0;
                foreach (var m in w.Members) { wsum += m.Weight; psum += m.Price * m.Weight; }
                w.Score  = wsum;
                w.Center = wsum > 0 ? psum / wsum : (w.Hi + w.Lo) / 2.0;
                walls.Add(w);
                i = j;
            }
            return walls;
        }

        private bool WallHasAnchor(Wall w) => w.Members.Any(m => m.IsAnchor);

        private List<Wall> SelectSide(List<Wall> side)
        {
            var result = new List<Wall>();
            result.AddRange(side.Where(WallHasAnchor));
            result.AddRange(side.Where(w => !WallHasAnchor(w) && w.Count >= MinConfluence)
                                .OrderByDescending(w => w.Score)
                                .Take(MaxWallsPerSide));
            return result;
        }

        private void BuildAndDrawWalls()
        {
            ClearWalls();

            var cands = new List<LevelCandidate>();
            GatherSwings(cands);
            if (cands.Count == 0) return;

            double tol      = GetClusterTolerance();
            double price    = Close[0];
            double rangeAbs = price * (WallRangePercent / 100.0);

            var walls = ClusterCandidates(cands, tol);

            var above = new List<Wall>();
            var below = new List<Wall>();
            foreach (var w in walls)
            {
                bool hasAnchor = WallHasAnchor(w);
                bool inRange   = Math.Abs(w.Center - price) <= rangeAbs;
                if (w.Count < MinConfluence && !hasAnchor) continue;
                if (!inRange && !hasAnchor) continue;
                if (w.Center >= price) above.Add(w); else below.Add(w);
            }

            var keep = new List<Wall>();
            keep.AddRange(SelectSide(above));
            keep.AddRange(SelectSide(below));

            int idx = 0;
            foreach (var w in keep)
                DrawWall(w, price, idx++);
        }

        private void DrawWall(Wall w, double price, int idx)
        {
            bool  isResistance = w.Center >= price;
            bool  isWall       = w.Count >= 2;
            Brush baseColor    = isWall ? (isResistance ? ResistanceColor : SupportColor) : AnchorColor;

            string tagBand = TAG_PREFIX + "WALL" + idx;
            string tagLine = tagBand + "_C";
            string tagLbl  = tagBand + "_L";

            bool hasH = w.Members.Any(m => m.Code == "H");
            bool hasL = w.Members.Any(m => m.Code == "L");
            string kind = hasH && hasL ? "S/R" : hasH ? "H" : "L";

            if (isWall)
            {
                int   cnt     = w.Count;
                int   opacity = cnt >= 4 ? 32 : cnt == 3 ? 22 : 12;
                int   thick   = Math.Min(cnt, 4);
                Brush fill    = WithOpacity(baseColor, opacity / 100.0);
                Brush outline = WithOpacity(baseColor, 0.55);

                double hi = w.Hi, lo = w.Lo;
                if (hi - lo < TickSize) { hi += TickSize * 0.5; lo -= TickSize * 0.5; }

                try
                {
                    Draw.RegionHighlightY(this, tagBand, false, hi, lo, outline, fill, opacity);
                    RegWall(tagBand);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Wall band error: " + ex.Message); }

                try
                {
                    Draw.HorizontalLine(this, tagLine, false, w.Center, baseColor, DashStyleHelper.Solid, thick);
                    RegWall(tagLine);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Wall line error: " + ex.Message); }
            }
            else
            {
                try
                {
                    Draw.HorizontalLine(this, tagLine, false, w.Center,
                        WithOpacity(baseColor, 0.55), DashStyleHelper.Dot, 1);
                    RegWall(tagLine);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Recent-swing line error: " + ex.Message); }
            }

            if (ShowLabels)
            {
                string side  = isResistance ? "R" : "S";
                string stars = w.Count >= 4 ? " ****" : w.Count == 3 ? " ***" : w.Count == 2 ? " **" : "";
                string text  = isWall
                    ? side + " " + FormatPrice(w.Center) + stars + "  " + w.Count + "x " + kind
                    : side + " " + FormatPrice(w.Center) + "  swing " + kind;
                Brush  txt   = isWall ? Brushes.White : WithOpacity(Brushes.White, 0.75);
                int    fs    = isWall ? Math.Max(8, LabelFontSize) : Math.Max(7, LabelFontSize - 2);
                try
                {
                    Draw.Text(this, tagLbl, true, text, 0, w.Center, 0,
                        txt, new SimpleFont("Arial", fs),
                        System.Windows.TextAlignment.Left,
                        Brushes.Transparent, WithOpacity(Brushes.Black, 0.70), 70);
                    RegWall(tagLbl);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Wall label error: " + ex.Message); }
            }
        }

        private Brush WithOpacity(Brush brush, double opacity)
        {
            try
            {
                var scb = brush as SolidColorBrush;
                if (scb == null) return brush;
                var c = scb.Color;
                var nb = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
                nb.Freeze();
                return nb;
            }
            catch { return brush; }
        }

        private string FormatPrice(double price) => price.ToString("N0");
        #endregion

        // =================================================================
        #region Legend

        private void DrawLegend()
        {
            if (!ShowLegend) { try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { } return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("THREE PILLARS - DAILY WALLS " + VERSION);
            sb.AppendLine("Band = price tested repeatedly; thicker = more touches");
            sb.AppendLine("  **  2x    ***  3x    ****  4+ touches");
            sb.AppendLine("R = resistance (above)   S = support (below)");
            sb.AppendLine("S/R = level flipped roles (strongest)");
            sb.AppendLine("Dotted = most recent swing high / low");

            TextPosition tp;
            switch (LegendPosition)
            {
                case TPMLegendPosition.TopRight:    tp = TextPosition.TopRight;    break;
                case TPMLegendPosition.BottomLeft:  tp = TextPosition.BottomLeft;  break;
                case TPMLegendPosition.BottomRight: tp = TextPosition.BottomRight; break;
                default:                            tp = TextPosition.TopLeft;     break;
            }

            try
            {
                Draw.TextFixed(this, TAG_PREFIX + "LEGEND", sb.ToString(), tp,
                    Brushes.White, new SimpleFont("Courier New", 11),
                    Brushes.Gray, WithOpacity(Brushes.Black, 0.80), 85);
            }
            catch (Exception ex) { Print(LOG_PREFIX + " Legend error: " + ex.Message); }
        }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ThreePillarsMapHTF[] cacheThreePillarsMapHTF;
		public ThreePillarsMapHTF ThreePillarsMapHTF()
		{
			return ThreePillarsMapHTF(Input);
		}

		public ThreePillarsMapHTF ThreePillarsMapHTF(ISeries<double> input)
		{
			if (cacheThreePillarsMapHTF != null)
				for (int idx = 0; idx < cacheThreePillarsMapHTF.Length; idx++)
					if (cacheThreePillarsMapHTF[idx] != null &&  cacheThreePillarsMapHTF[idx].EqualsInput(input))
						return cacheThreePillarsMapHTF[idx];
			return CacheIndicator<ThreePillarsMapHTF>(new ThreePillarsMapHTF(), input, ref cacheThreePillarsMapHTF);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ThreePillarsMapHTF ThreePillarsMapHTF()
		{
			return indicator.ThreePillarsMapHTF(Input);
		}

		public Indicators.ThreePillarsMapHTF ThreePillarsMapHTF(ISeries<double> input )
		{
			return indicator.ThreePillarsMapHTF(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ThreePillarsMapHTF ThreePillarsMapHTF()
		{
			return indicator.ThreePillarsMapHTF(Input);
		}

		public Indicators.ThreePillarsMapHTF ThreePillarsMapHTF(ISeries<double> input )
		{
			return indicator.ThreePillarsMapHTF(input);
		}
	}
}

#endregion
