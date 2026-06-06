// ============================================================
// ThreePillarsMap.cs  —  v2.0
// NinjaTrader 8  |  Multi-Timeframe Structural Wall Map
// ============================================================
//
// INSTALLATION:
//   1. Copy this file to:
//      Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. In NinjaTrader: Tools > NinjaScript Editor > Compile
//      — or — Tools > Compile NinjaScript
//   3. Apply to any chart via Indicators > ThreePillarsMap
//
// SUPPORTED INSTRUMENTS:
//   YM / MYM  (E-mini / Micro Dow)
//   ES / MES  (E-mini / Micro S&P 500)
//   NQ / MNQ  (E-mini / Micro Nasdaq)
//   UB        (Ultra T-Bond)
//   ZB        (30-Year Treasury Bond)
//   ZC        (Corn Futures)
//
// THREE-TIER SYSTEM:
//   Tier 1  —  Thick solid/dashed lines, 100% opacity, always visible.
//              The most important structural walls for the selected timeframe.
//   Tier 2  —  Thinner lines, 70% opacity, always visible.
//              Context walls that give price the next reference.
//   Tier 3  —  1px dotted lines, 50% opacity.
//              Hidden until price comes within ProximityTicks. Once
//              activated they stay visible for the rest of the session.
//
// GLOBAL DRAW OBJECTS:
//   All levels are created as Global Draw Objects so they appear on
//   every open chart for the same instrument simultaneously.
//
// PARAMETERS (grouped by category in the indicator dialog):
//
//   Chart Configuration
//     ChartRole                — Daily / FourHour / OneHour / FifteenMinute /
//                                ThreeMinute / OneMinute
//     ProximityTicks           — Distance (ticks) to trigger Tier 3 levels
//     ConfluenceProximityTicks — Distance (ticks) to cluster into confluence zone
//
//   Display
//     ShowLabels               — Toggle level name + price labels
//     ShowLegend               — Toggle the legend key panel
//     LegendPosition           — TopLeft / TopRight / BottomLeft / BottomRight
//     LabelFontSize            — 6–16, default 9
//     LineThicknessPrimary     — Tier 1 line weight (1–5, default 2)
//     LineThicknessSecondary   — Tier 2 line weight (1–5, default 1)
//     ShowConfluenceZones      — Draw semi-transparent confluence rectangles
//     ShowVerificationOutput   — Print detailed calculations to Output window
//
//   Visibility Toggles
//     ShowYH / ShowYL          — Yesterday High / Low
//     ShowONH / ShowONL        — Overnight High / Low
//     ShowPOC / ShowVAH / ShowVAL — Daily volume profile levels
//     ShowPivots               — PP, R1, S1
//     ShowR2S2                 — R2, S2 (default false)
//     ShowR3S3                 — R3, S3 (default false)
//     ShowOR                   — Opening Range (equity index only)
//     ShowWeeklyLevels         — Prior week + current week levels
//     ShowMonthlyLevels        — Prior month levels (default false)
//     ShowSwingLevels          — 4H swing high/low levels
//
//   Volume Profile
//     ValueAreaPercent         — Target % of volume in value area (50–90, default 70)
//
//   Swing Detection
//     SwingStrength            — Bars each side a pivot must dominate (1–10, default 3)
//
//   Manual 4H Swing Levels    — Enter manually from your 4H chart if desired.
//                                Value 0 = not drawn.
//
// CHANGELOG:
//   v2.0  2026-06-06  Initial release.
// ============================================================

#region Using Declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
    #region Supporting Enums
    public enum ChartRole
    {
        Daily,
        FourHour,
        OneHour,
        FifteenMinute,
        ThreeMinute,
        OneMinute
    }

    public enum TPMLegendPosition
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
    #endregion

    [CategoryOrder("Chart Configuration",       1)]
    [CategoryOrder("Display",                   2)]
    [CategoryOrder("Swing Detection",           3)]
    [CategoryOrder("Visibility Toggles",        4)]
    [CategoryOrder("Volume Profile",            5)]
    [CategoryOrder("Colors — Daily Levels",     6)]
    [CategoryOrder("Colors — Overnight",        7)]
    [CategoryOrder("Colors — Volume Profile",   8)]
    [CategoryOrder("Colors — Pivots",           9)]
    [CategoryOrder("Colors — Opening Range",   10)]
    [CategoryOrder("Colors — Weekly Levels",   11)]
    [CategoryOrder("Colors — Monthly Levels",  12)]
    [CategoryOrder("Colors — Swing Levels",    13)]
    [CategoryOrder("Colors — Confluence",      14)]
    [CategoryOrder("Manual 4H Swing Levels",   15)]
    public class ThreePillarsMap : Indicator
    {
        // ─────────────────────────────────────────────────────────────────
        #region Constants
        private const int    MAX_LOOKBACK_DAILY   = 500;
        private const int    MAX_LOOKBACK_WEEKLY  = 2000;
        private const int    MAX_LOOKBACK_MONTHLY = 5000;
        private const string TAG_PREFIX           = "3PM_";
        private const string VERSION              = "v2.0";
        private const string LOG_PREFIX           = "[ThreePillarsMap v2.0]";
        private const double VALUE_AREA_TOLERANCE = 0.10; // 10% band for VA% warning
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Instrument Config Fields
        private TimeSpan rthOpen;
        private TimeSpan rthClose;
        private TimeSpan orStart;
        private TimeSpan orEnd;
        private bool     instrumentHasOR;
        private string   instrumentKey = "YM";
        private double   defaultConfluenceTicks = 8;
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Level Price Variables
        private double lvlYH,  lvlYL,  lvlYC;
        private double lvlONH, lvlONL;
        private double lvlPP,  lvlR1,  lvlR2,  lvlR3;
        private double lvlS1,  lvlS2,  lvlS3;
        private double lvlPOC, lvlVAH, lvlVAL;
        private double lvlPWH, lvlPWL, lvlWeekPOC, lvlWeekVAH, lvlWeekVAL;
        private double lvlCWH, lvlCWL;
        private double lvlPMH, lvlPML;
        private double lvlORH, lvlORL;
        private double[] lvlSwingH = new double[3];
        private double[] lvlSwingL = new double[3];
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region State / Session Fields
        private DateTime                   lastDrawnDate   = DateTime.MinValue;
        private string                     dateTag         = "";
        private bool                       orComplete      = false;
        private Dictionary<double, double> dayProfile      = new Dictionary<double, double>();
        private Dictionary<double, double> weekProfile     = new Dictionary<double, double>();
        private HashSet<string>            tier3Activated  = new HashSet<string>();
        private List<string>               drawnTags       = new List<string>();
        private DateTime                   priorRTHDate    = DateTime.MinValue;
        private int                        priorDayBars    = 0;
        private int                        overnightBars   = 0;
        private double                     dayTotalVol     = 0;
        private double                     dayProfileAvg   = 0;  // avg vol/level for confluence grading
        #endregion

        // =================================================================
        #region Parameters — Chart Configuration

        [Display(Name = "Chart Role", GroupName = "Chart Configuration", Order = 1,
            Description = "Select the timeframe role for this chart. Controls which levels are drawn and at which tier.")]
        public ChartRole ChartRole { get; set; }

        [Range(10, 100)]
        [Display(Name = "Proximity Ticks (Tier 3)", GroupName = "Chart Configuration", Order = 2,
            Description = "Price must be within this many ticks to activate a Tier 3 level.")]
        public int ProximityTicks { get; set; }

        [Range(1, 100)]
        [Display(Name = "Confluence Proximity Ticks", GroupName = "Chart Configuration", Order = 3,
            Description = "Two levels within this many ticks form a confluence zone.")]
        public int ConfluenceProximityTicks { get; set; }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Display

        [Display(Name = "Show Labels", GroupName = "Display", Order = 1)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Show Legend", GroupName = "Display", Order = 2)]
        public bool ShowLegend { get; set; }

        [Display(Name = "Legend Position", GroupName = "Display", Order = 3)]
        public TPMLegendPosition LegendPosition { get; set; }

        [Range(6, 16)]
        [Display(Name = "Label Font Size", GroupName = "Display", Order = 4)]
        public int LabelFontSize { get; set; }

        [Range(1, 5)]
        [Display(Name = "Tier 1 Line Thickness", GroupName = "Display", Order = 5)]
        public int LineThicknessPrimary { get; set; }

        [Range(1, 5)]
        [Display(Name = "Tier 2 Line Thickness", GroupName = "Display", Order = 6)]
        public int LineThicknessSecondary { get; set; }

        [Display(Name = "Show Confluence Zones", GroupName = "Display", Order = 7)]
        public bool ShowConfluenceZones { get; set; }

        [Display(Name = "Show Verification Output", GroupName = "Display", Order = 8,
            Description = "Print all calculated level values to the Output window each session.")]
        public bool ShowVerificationOutput { get; set; }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Swing Detection

        [Range(1, 10)]
        [Display(Name = "Swing Strength", GroupName = "Swing Detection", Order = 1,
            Description = "Number of bars on each side a bar must dominate to qualify as a swing high/low. Default 3.")]
        public int SwingStrength { get; set; }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Visibility Toggles

        [Display(Name = "Show Yesterday High",      GroupName = "Visibility Toggles", Order = 1)]
        public bool ShowYH { get; set; }

        [Display(Name = "Show Yesterday Low",       GroupName = "Visibility Toggles", Order = 2)]
        public bool ShowYL { get; set; }

        [Display(Name = "Show Overnight High",      GroupName = "Visibility Toggles", Order = 3)]
        public bool ShowONH { get; set; }

        [Display(Name = "Show Overnight Low",       GroupName = "Visibility Toggles", Order = 4)]
        public bool ShowONL { get; set; }

        [Display(Name = "Show POC",                 GroupName = "Visibility Toggles", Order = 5)]
        public bool ShowPOC { get; set; }

        [Display(Name = "Show VAH",                 GroupName = "Visibility Toggles", Order = 6)]
        public bool ShowVAH { get; set; }

        [Display(Name = "Show VAL",                 GroupName = "Visibility Toggles", Order = 7)]
        public bool ShowVAL { get; set; }

        [Display(Name = "Show Pivots (PP, R1, S1)", GroupName = "Visibility Toggles", Order = 8)]
        public bool ShowPivots { get; set; }

        [Display(Name = "Show R2 / S2",             GroupName = "Visibility Toggles", Order = 9)]
        public bool ShowR2S2 { get; set; }

        [Display(Name = "Show R3 / S3",             GroupName = "Visibility Toggles", Order = 10)]
        public bool ShowR3S3 { get; set; }

        [Display(Name = "Show Opening Range",       GroupName = "Visibility Toggles", Order = 11)]
        public bool ShowOR { get; set; }

        [Display(Name = "Show Weekly Levels",       GroupName = "Visibility Toggles", Order = 12)]
        public bool ShowWeeklyLevels { get; set; }

        [Display(Name = "Show Monthly Levels",      GroupName = "Visibility Toggles", Order = 13)]
        public bool ShowMonthlyLevels { get; set; }

        [Display(Name = "Show Swing Levels",        GroupName = "Visibility Toggles", Order = 14)]
        public bool ShowSwingLevels { get; set; }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Volume Profile

        [Range(50, 90)]
        [Display(Name = "Value Area %", GroupName = "Volume Profile", Order = 1,
            Description = "Percentage of total volume to include in the value area. Default 70.")]
        public double ValueAreaPercent { get; set; }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Daily Levels

        [XmlIgnore]
        [Display(Name = "Yesterday High Color", GroupName = "Colors — Daily Levels", Order = 1)]
        public Brush YHColor { get; set; }
        [Browsable(false)]
        public string YHColorSerializable
        { get { return Serialize.BrushToString(YHColor); } set { YHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Yesterday Low Color", GroupName = "Colors — Daily Levels", Order = 2)]
        public Brush YLColor { get; set; }
        [Browsable(false)]
        public string YLColorSerializable
        { get { return Serialize.BrushToString(YLColor); } set { YLColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Yesterday Close Color", GroupName = "Colors — Daily Levels", Order = 3)]
        public Brush YCColor { get; set; }
        [Browsable(false)]
        public string YCColorSerializable
        { get { return Serialize.BrushToString(YCColor); } set { YCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Current Week High Color", GroupName = "Colors — Daily Levels", Order = 4)]
        public Brush CWHColor { get; set; }
        [Browsable(false)]
        public string CWHColorSerializable
        { get { return Serialize.BrushToString(CWHColor); } set { CWHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Current Week Low Color", GroupName = "Colors — Daily Levels", Order = 5)]
        public Brush CWLColor { get; set; }
        [Browsable(false)]
        public string CWLColorSerializable
        { get { return Serialize.BrushToString(CWLColor); } set { CWLColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Overnight

        [XmlIgnore]
        [Display(Name = "Overnight High Color", GroupName = "Colors — Overnight", Order = 1)]
        public Brush ONHColor { get; set; }
        [Browsable(false)]
        public string ONHColorSerializable
        { get { return Serialize.BrushToString(ONHColor); } set { ONHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Overnight Low Color", GroupName = "Colors — Overnight", Order = 2)]
        public Brush ONLColor { get; set; }
        [Browsable(false)]
        public string ONLColorSerializable
        { get { return Serialize.BrushToString(ONLColor); } set { ONLColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Volume Profile

        [XmlIgnore]
        [Display(Name = "Prior Day POC Color", GroupName = "Colors — Volume Profile", Order = 1)]
        public Brush POCColor { get; set; }
        [Browsable(false)]
        public string POCColorSerializable
        { get { return Serialize.BrushToString(POCColor); } set { POCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Day VAH Color", GroupName = "Colors — Volume Profile", Order = 2)]
        public Brush VAHColor { get; set; }
        [Browsable(false)]
        public string VAHColorSerializable
        { get { return Serialize.BrushToString(VAHColor); } set { VAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Day VAL Color", GroupName = "Colors — Volume Profile", Order = 3)]
        public Brush VALColor { get; set; }
        [Browsable(false)]
        public string VALColorSerializable
        { get { return Serialize.BrushToString(VALColor); } set { VALColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly POC Color", GroupName = "Colors — Volume Profile", Order = 4)]
        public Brush WeeklyPOCColor { get; set; }
        [Browsable(false)]
        public string WeeklyPOCColorSerializable
        { get { return Serialize.BrushToString(WeeklyPOCColor); } set { WeeklyPOCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly VAH Color", GroupName = "Colors — Volume Profile", Order = 5)]
        public Brush WeeklyVAHColor { get; set; }
        [Browsable(false)]
        public string WeeklyVAHColorSerializable
        { get { return Serialize.BrushToString(WeeklyVAHColor); } set { WeeklyVAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly VAL Color", GroupName = "Colors — Volume Profile", Order = 6)]
        public Brush WeeklyVALColor { get; set; }
        [Browsable(false)]
        public string WeeklyVALColorSerializable
        { get { return Serialize.BrushToString(WeeklyVALColor); } set { WeeklyVALColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Pivots

        [XmlIgnore]
        [Display(Name = "Pivot Point (PP) Color", GroupName = "Colors — Pivots", Order = 1)]
        public Brush PPColor { get; set; }
        [Browsable(false)]
        public string PPColorSerializable
        { get { return Serialize.BrushToString(PPColor); } set { PPColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R1 Color", GroupName = "Colors — Pivots", Order = 2)]
        public Brush R1Color { get; set; }
        [Browsable(false)]
        public string R1ColorSerializable
        { get { return Serialize.BrushToString(R1Color); } set { R1Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R2 Color", GroupName = "Colors — Pivots", Order = 3)]
        public Brush R2Color { get; set; }
        [Browsable(false)]
        public string R2ColorSerializable
        { get { return Serialize.BrushToString(R2Color); } set { R2Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R3 Color", GroupName = "Colors — Pivots", Order = 4)]
        public Brush R3Color { get; set; }
        [Browsable(false)]
        public string R3ColorSerializable
        { get { return Serialize.BrushToString(R3Color); } set { R3Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S1 Color", GroupName = "Colors — Pivots", Order = 5)]
        public Brush S1Color { get; set; }
        [Browsable(false)]
        public string S1ColorSerializable
        { get { return Serialize.BrushToString(S1Color); } set { S1Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S2 Color", GroupName = "Colors — Pivots", Order = 6)]
        public Brush S2Color { get; set; }
        [Browsable(false)]
        public string S2ColorSerializable
        { get { return Serialize.BrushToString(S2Color); } set { S2Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S3 Color", GroupName = "Colors — Pivots", Order = 7)]
        public Brush S3Color { get; set; }
        [Browsable(false)]
        public string S3ColorSerializable
        { get { return Serialize.BrushToString(S3Color); } set { S3Color = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Opening Range

        [XmlIgnore]
        [Display(Name = "Opening Range High Color", GroupName = "Colors — Opening Range", Order = 1)]
        public Brush ORHighColor { get; set; }
        [Browsable(false)]
        public string ORHighColorSerializable
        { get { return Serialize.BrushToString(ORHighColor); } set { ORHighColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Opening Range Low Color", GroupName = "Colors — Opening Range", Order = 2)]
        public Brush ORLowColor { get; set; }
        [Browsable(false)]
        public string ORLowColorSerializable
        { get { return Serialize.BrushToString(ORLowColor); } set { ORLowColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Weekly Levels

        [XmlIgnore]
        [Display(Name = "Prior Week High Color", GroupName = "Colors — Weekly Levels", Order = 1)]
        public Brush PWHColor { get; set; }
        [Browsable(false)]
        public string PWHColorSerializable
        { get { return Serialize.BrushToString(PWHColor); } set { PWHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Week Low Color", GroupName = "Colors — Weekly Levels", Order = 2)]
        public Brush PWLColor { get; set; }
        [Browsable(false)]
        public string PWLColorSerializable
        { get { return Serialize.BrushToString(PWLColor); } set { PWLColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Monthly Levels

        [XmlIgnore]
        [Display(Name = "Prior Month High Color", GroupName = "Colors — Monthly Levels", Order = 1)]
        public Brush PMHColor { get; set; }
        [Browsable(false)]
        public string PMHColorSerializable
        { get { return Serialize.BrushToString(PMHColor); } set { PMHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Month Low Color", GroupName = "Colors — Monthly Levels", Order = 2)]
        public Brush PMLColor { get; set; }
        [Browsable(false)]
        public string PMLColorSerializable
        { get { return Serialize.BrushToString(PMLColor); } set { PMLColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Swing Levels

        [XmlIgnore]
        [Display(Name = "Swing High Color", GroupName = "Colors — Swing Levels", Order = 1)]
        public Brush SwingHighColor { get; set; }
        [Browsable(false)]
        public string SwingHighColorSerializable
        { get { return Serialize.BrushToString(SwingHighColor); } set { SwingHighColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Swing Low Color", GroupName = "Colors — Swing Levels", Order = 2)]
        public Brush SwingLowColor { get; set; }
        [Browsable(false)]
        public string SwingLowColorSerializable
        { get { return Serialize.BrushToString(SwingLowColor); } set { SwingLowColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Colors: Confluence

        [XmlIgnore]
        [Display(Name = "Confluence Zone Color", GroupName = "Colors — Confluence", Order = 1)]
        public Brush ConfluenceZoneColor { get; set; }
        [Browsable(false)]
        public string ConfluenceZoneColorSerializable
        { get { return Serialize.BrushToString(ConfluenceZoneColor); } set { ConfluenceZoneColor = Serialize.StringToBrush(value); } }
        #endregion

        // ─────────────────────────────────────────────────────────────────
        #region Parameters — Manual 4H Swing Levels

        [Display(Name = "4H Swing High 1", GroupName = "Manual 4H Swing Levels", Order = 1,
            Description = "Enter from your 4H chart. Leave 0 to skip.")]
        public double SwingHigh4H_1 { get; set; }

        [Display(Name = "4H Swing High 2", GroupName = "Manual 4H Swing Levels", Order = 2)]
        public double SwingHigh4H_2 { get; set; }

        [Display(Name = "4H Swing High 3", GroupName = "Manual 4H Swing Levels", Order = 3)]
        public double SwingHigh4H_3 { get; set; }

        [Display(Name = "4H Swing Low 1", GroupName = "Manual 4H Swing Levels", Order = 4)]
        public double SwingLow4H_1 { get; set; }

        [Display(Name = "4H Swing Low 2", GroupName = "Manual 4H Swing Levels", Order = 5)]
        public double SwingLow4H_2 { get; set; }

        [Display(Name = "4H Swing Low 3", GroupName = "Manual 4H Swing Levels", Order = 6)]
        public double SwingLow4H_3 { get; set; }
        #endregion

        // =================================================================
        #region OnStateChange

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                        = "ThreePillarsMap";
                Description                 = "Three Pillars Structural Wall Map v2.0";
                IsOverlay                   = true;
                IsSuspendedWhileInactive    = true;
                Calculate                   = Calculate.OnBarClose;
                DisplayInDataBox            = false;

                // Chart configuration
                ChartRole                   = ChartRole.FifteenMinute;
                ProximityTicks              = 40;
                ConfluenceProximityTicks    = 8;

                // Display
                ShowLabels                  = true;
                ShowLegend                  = true;
                LegendPosition              = TPMLegendPosition.TopLeft;
                LabelFontSize               = 9;
                LineThicknessPrimary        = 2;
                LineThicknessSecondary      = 1;
                ShowConfluenceZones         = true;
                ShowVerificationOutput      = true;

                // Swing
                SwingStrength               = 3;

                // Visibility
                ShowYH                      = true;
                ShowYL                      = true;
                ShowONH                     = true;
                ShowONL                     = true;
                ShowPOC                     = true;
                ShowVAH                     = true;
                ShowVAL                     = true;
                ShowPivots                  = true;
                ShowR2S2                    = false;
                ShowR3S3                    = false;
                ShowOR                      = true;
                ShowWeeklyLevels            = true;
                ShowMonthlyLevels           = false;
                ShowSwingLevels             = true;

                // Volume profile
                ValueAreaPercent            = 70;

                // Manual swings
                SwingHigh4H_1 = SwingHigh4H_2 = SwingHigh4H_3 = 0;
                SwingLow4H_1  = SwingLow4H_2  = SwingLow4H_3  = 0;

                // Default colors — Daily
                YHColor                     = Brushes.Crimson;
                YLColor                     = Brushes.LimeGreen;
                YCColor                     = Brushes.Gray;
                CWHColor                    = Brushes.Orange;
                CWLColor                    = Brushes.Orange;
                // Overnight
                ONHColor                    = Brushes.DarkOrange;
                ONLColor                    = Brushes.MediumPurple;
                // Volume Profile
                POCColor                    = Brushes.Gold;
                VAHColor                    = Brushes.DodgerBlue;
                VALColor                    = Brushes.DodgerBlue;
                WeeklyPOCColor              = Brushes.DarkGoldenrod;
                WeeklyVAHColor              = Brushes.SteelBlue;
                WeeklyVALColor              = Brushes.SteelBlue;
                // Pivots
                PPColor                     = Brushes.White;
                R1Color                     = Brushes.Tomato;
                R2Color                     = Brushes.Tomato;
                R3Color                     = Brushes.Tomato;
                S1Color                     = Brushes.MediumSpringGreen;
                S2Color                     = Brushes.MediumSpringGreen;
                S3Color                     = Brushes.MediumSpringGreen;
                // Opening Range
                ORHighColor                 = Brushes.Cyan;
                ORLowColor                  = Brushes.Magenta;
                // Weekly
                PWHColor                    = Brushes.Orange;
                PWLColor                    = Brushes.Orange;
                // Monthly
                PMHColor                    = Brushes.Coral;
                PMLColor                    = Brushes.Coral;
                // Swings
                SwingHighColor              = Brushes.DeepSkyBlue;
                SwingLowColor               = Brushes.HotPink;
                // Confluence
                ConfluenceZoneColor         = Brushes.Yellow;
            }
            else if (State == State.Configure)
            {
                // Detect instrument and configure session times
                ConfigureInstrument();
            }
            else if (State == State.DataLoaded)
            {
                lastDrawnDate  = DateTime.MinValue;
                dateTag        = "";
                orComplete     = false;
                dayProfile     = new Dictionary<double, double>();
                weekProfile    = new Dictionary<double, double>();
                tier3Activated = new HashSet<string>();
                drawnTags      = new List<string>();
                ResetLevels();
            }
            else if (State == State.Terminated)
            {
                // Global draw objects persist by design; remove today's session tags
                foreach (string t in drawnTags)
                    try { RemoveDrawObject(t); } catch { }
                if (ShowLegend)
                    try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { }
            }
        }

        private void ConfigureInstrument()
        {
            string name = Instrument.MasterInstrument.Name.ToUpper();

            if      (name.StartsWith("MYM"))  instrumentKey = "MYM";
            else if (name.StartsWith("YM"))   instrumentKey = "YM";
            else if (name.StartsWith("MES"))  instrumentKey = "MES";
            else if (name.StartsWith("ES"))   instrumentKey = "ES";
            else if (name.StartsWith("MNQ"))  instrumentKey = "MNQ";
            else if (name.StartsWith("NQ"))   instrumentKey = "NQ";
            else if (name.StartsWith("UB"))   instrumentKey = "UB";
            else if (name.StartsWith("ZB"))   instrumentKey = "ZB";
            else if (name.StartsWith("ZC"))   instrumentKey = "ZC";
            else
            {
                instrumentKey = "YM";
                Print(LOG_PREFIX + " WARNING: Unrecognized instrument '" + name + "'. Using YM defaults.");
            }

            switch (instrumentKey)
            {
                case "UB":
                case "ZB":
                    rthOpen          = new TimeSpan(8, 20, 0);
                    rthClose         = new TimeSpan(14, 0, 0);
                    orStart          = TimeSpan.Zero;
                    orEnd            = TimeSpan.Zero;
                    instrumentHasOR  = false;
                    defaultConfluenceTicks = 6;
                    break;

                case "ZC":
                    rthOpen          = new TimeSpan(9, 30, 0);
                    rthClose         = new TimeSpan(14, 20, 0);
                    orStart          = new TimeSpan(9, 30, 0);
                    orEnd            = new TimeSpan(9, 45, 0);
                    instrumentHasOR  = true;
                    defaultConfluenceTicks = 8;
                    break;

                default: // YM, MYM, ES, MES, NQ, MNQ
                    rthOpen          = new TimeSpan(9, 30, 0);
                    rthClose         = new TimeSpan(16, 15, 0);
                    orStart          = new TimeSpan(9, 30, 0);
                    orEnd            = new TimeSpan(9, 45, 0);
                    instrumentHasOR  = true;
                    defaultConfluenceTicks = 8;
                    break;
            }
        }

        private void ResetLevels()
        {
            lvlYH  = lvlYL  = lvlYC  = 0;
            lvlONH = lvlONL = 0;
            lvlPP  = lvlR1  = lvlR2  = lvlR3 = 0;
            lvlS1  = lvlS2  = lvlS3  = 0;
            lvlPOC = lvlVAH = lvlVAL = 0;
            lvlPWH = lvlPWL = lvlWeekPOC = lvlWeekVAH = lvlWeekVAL = 0;
            lvlCWH = lvlCWL = 0;
            lvlPMH = lvlPML = 0;
            lvlORH = lvlORL = 0;
            for (int i = 0; i < 3; i++) { lvlSwingH[i] = 0; lvlSwingL[i] = 0; }
        }
        #endregion

        // =================================================================
        #region OnBarUpdate

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (CurrentBar < 20)     return;

            TimeSpan barTime = Time[0].TimeOfDay;
            DateTime barDate = Time[0].Date;

            bool isRTH = IsRTHBar(barTime);
            if (!isRTH) return;

            // ── SESSION START: draw all levels once per calendar date ──
            if (barDate != lastDrawnDate)
            {
                lastDrawnDate = barDate;
                dateTag       = barDate.ToString("yyyyMMdd");

                // Clear any stale draw objects from a prior run of today's session
                ClearTagsForDate(dateTag);
                tier3Activated.Clear();

                // Reset dynamic OR state
                orComplete = false;
                lvlORH     = 0;
                lvlORL     = 0;

                try { CalculateAllLevels(barDate); }
                catch (Exception ex) { Print(LOG_PREFIX + " ERROR in CalculateAllLevels: " + ex.Message); }

                try { RunVerificationChecks(); }
                catch (Exception ex) { Print(LOG_PREFIX + " ERROR in RunVerificationChecks: " + ex.Message); }

                if (ShowVerificationOutput)
                    try { PrintVerificationOutput(barDate); }
                    catch (Exception ex) { Print(LOG_PREFIX + " ERROR in PrintVerificationOutput: " + ex.Message); }

                DrawLevelsForRole();
                DetectAndDrawConfluence();
                DrawLegend();
            }

            // ── UPDATE DYNAMIC CWH / CWL every bar ──
            UpdateCurrentWeekExtremes();

            // ── TIER 3 PROXIMITY CHECK every bar ──
            CheckProximityActivation();

            // ── OPENING RANGE TRACKING ──
            if (instrumentHasOR && !orComplete)
            {
                if (barTime >= orStart && barTime < orEnd)
                {
                    if (lvlORH == 0 || High[0] > lvlORH) lvlORH = High[0];
                    if (lvlORL == 0 || Low[0]  < lvlORL) lvlORL = Low[0];
                }
                if (barTime >= orEnd && lvlORH > 0 && lvlORL > 0)
                {
                    DrawOpeningRangeLevels();
                    DetectAndDrawConfluence();
                    orComplete = true;
                    Print(string.Format("{0} OR Complete: High={1} Low={2} Range={3} ticks",
                        LOG_PREFIX, lvlORH, lvlORL,
                        Math.Round(Math.Abs(lvlORH - lvlORL) / TickSize)));
                }
            }
        }
        #endregion

        // =================================================================
        #region Level Calculations

        private void CalculateAllLevels(DateTime today)
        {
            ResetLevels();
            CalcPriorDayLevels(today);
            CalcPivots();
            CalcOvernightLevels(today);
            CalcWeeklyLevels(today);
            CalcMonthlyLevels(today);
            CalcSwingLevels();
        }

        // ── Prior Day RTH Levels + Volume Profile ──
        private void CalcPriorDayLevels(DateTime today)
        {
            dayProfile.Clear();
            priorDayBars    = 0;
            priorRTHDate    = DateTime.MinValue;
            dayTotalVol     = 0;
            dayProfileAvg   = 0;

            int limit = Math.Min(CurrentBar, MAX_LOOKBACK_DAILY);
            bool ycSet = false;

            for (int i = 1; i < limit; i++)
            {
                DateTime bd = Time[i].Date;
                if (bd >= today) continue;
                if (!IsRTHBar(Time[i].TimeOfDay)) continue;

                if (priorRTHDate == DateTime.MinValue)
                {
                    priorRTHDate = bd;
                    lvlYC = Close[i]; // first found = last bar of that session
                    ycSet = true;
                }
                if (bd != priorRTHDate) break;

                priorDayBars++;
                if (High[i] > lvlYH) lvlYH = High[i];
                if (lvlYL == 0 || Low[i] < lvlYL) lvlYL = Low[i];
                AddToProfile(dayProfile, High[i], Low[i], Volume[i]);
            }

            if (!ycSet) lvlYC = lvlYH > 0 ? lvlYH : 0;

            if (dayProfile.Count > 0)
            {
                dayTotalVol   = dayProfile.Values.Sum();
                dayProfileAvg = dayProfile.Count > 0 ? dayTotalVol / dayProfile.Count : 0;
                CalcValueArea(dayProfile, dayTotalVol, out lvlPOC, out lvlVAH, out lvlVAL);
            }
        }

        // ── Pivot Points (standard floor formula) ──
        private void CalcPivots()
        {
            if (lvlYH == 0 || lvlYL == 0) return;
            lvlPP = (lvlYH + lvlYL + lvlYC) / 3.0;
            lvlR1 = 2.0 * lvlPP - lvlYL;
            lvlR2 = lvlPP + (lvlYH - lvlYL);
            lvlR3 = lvlYH + 2.0 * (lvlPP - lvlYL);
            lvlS1 = 2.0 * lvlPP - lvlYH;
            lvlS2 = lvlPP - (lvlYH - lvlYL);
            lvlS3 = lvlYL - 2.0 * (lvlYH - lvlPP);
        }

        // ── Overnight High / Low ──
        private void CalcOvernightLevels(DateTime today)
        {
            if (priorRTHDate == DateTime.MinValue) return;
            overnightBars = 0;
            int limit = Math.Min(CurrentBar, 400);

            for (int i = 1; i < limit; i++)
            {
                DateTime bd = Time[i].Date;
                TimeSpan bt = Time[i].TimeOfDay;
                bool todayPreMarket    = (bd == today     && bt < rthOpen);
                bool priorPostMarket   = (bd == priorRTHDate && bt > rthClose);

                if (todayPreMarket || priorPostMarket)
                {
                    overnightBars++;
                    if (lvlONH == 0 || High[i] > lvlONH) lvlONH = High[i];
                    if (lvlONL == 0 || Low[i]  < lvlONL) lvlONL = Low[i];
                }
                else if (bd == priorRTHDate && IsRTHBar(bt))
                    break; // reached yesterday's RTH — stop
                else if (bd < priorRTHDate)
                    break;
            }
        }

        // ── Weekly Levels (prior complete week + current week) ──
        private void CalcWeeklyLevels(DateTime today)
        {
            weekProfile.Clear();
            lvlPWH = lvlPWL = lvlWeekPOC = lvlWeekVAH = lvlWeekVAL = 0;
            lvlCWH = lvlCWL = 0;

            // Calculate Monday of the current week
            int dFromMon = (int)today.DayOfWeek == 0 ? 6 : (int)today.DayOfWeek - 1;
            DateTime curWeekStart = today.AddDays(-dFromMon);
            DateTime priorWeekEnd = curWeekStart.AddDays(-1);        // Last Fri (or Mon-1)
            DateTime priorWeekStart = curWeekStart.AddDays(-7);       // Prior Mon

            int limit = Math.Min(CurrentBar, MAX_LOOKBACK_WEEKLY);
            int priorWeekBarCount = 0;

            for (int i = 1; i < limit; i++)
            {
                DateTime bd = Time[i].Date;
                if (bd < priorWeekStart) break;
                if (!IsRTHBar(Time[i].TimeOfDay)) continue;

                if (bd >= priorWeekStart && bd <= priorWeekEnd)
                {
                    priorWeekBarCount++;
                    if (High[i] > lvlPWH) lvlPWH = High[i];
                    if (lvlPWL == 0 || Low[i] < lvlPWL) lvlPWL = Low[i];
                    AddToProfile(weekProfile, High[i], Low[i], Volume[i]);
                }
                else if (bd >= curWeekStart && bd < today)
                {
                    if (High[i] > lvlCWH) lvlCWH = High[i];
                    if (lvlCWL == 0 || Low[i] < lvlCWL) lvlCWL = Low[i];
                }
            }

            if (weekProfile.Count > 0)
            {
                double weekTotalVol = weekProfile.Values.Sum();
                CalcValueArea(weekProfile, weekTotalVol, out lvlWeekPOC, out lvlWeekVAH, out lvlWeekVAL);
            }
        }

        // ── Monthly Levels (prior complete calendar month) ──
        private void CalcMonthlyLevels(DateTime today)
        {
            lvlPMH = lvlPML = 0;
            int priorMonth = today.Month == 1 ? 12 : today.Month - 1;
            int priorYear  = today.Month == 1 ? today.Year - 1 : today.Year;
            int limit      = Math.Min(CurrentBar, MAX_LOOKBACK_MONTHLY);

            for (int i = 1; i < limit; i++)
            {
                DateTime bd = Time[i].Date;
                // Stop if we've gone past the prior month
                if (bd.Year < priorYear || (bd.Year == priorYear && bd.Month < priorMonth)) break;
                if (bd.Year != priorYear || bd.Month != priorMonth) continue;
                if (!IsRTHBar(Time[i].TimeOfDay)) continue;

                if (High[i] > lvlPMH) lvlPMH = High[i];
                if (lvlPML == 0 || Low[i] < lvlPML) lvlPML = Low[i];
            }
        }

        // ── Auto-detect 4H Swing Highs/Lows using SwingStrength ──
        private void CalcSwingLevels()
        {
            // Use manual inputs if provided; else auto-detect on current chart bars
            lvlSwingH[0] = SwingHigh4H_1;
            lvlSwingH[1] = SwingHigh4H_2;
            lvlSwingH[2] = SwingHigh4H_3;
            lvlSwingL[0] = SwingLow4H_1;
            lvlSwingL[1] = SwingLow4H_2;
            lvlSwingL[2] = SwingLow4H_3;

            // Auto-detection only if all manual values are zero
            bool allManualZero = (SwingHigh4H_1 == 0 && SwingHigh4H_2 == 0 && SwingHigh4H_3 == 0 &&
                                  SwingLow4H_1  == 0 && SwingLow4H_2  == 0 && SwingLow4H_3  == 0);
            if (!allManualZero) return;

            int swHCount = 0, swLCount = 0;
            int strength = SwingStrength;
            int limit    = Math.Min(CurrentBar - strength, MAX_LOOKBACK_WEEKLY);

            for (int i = strength; i < limit && (swHCount < 3 || swLCount < 3); i++)
            {
                bool isSwingH = true, isSwingL = true;
                for (int k = 1; k <= strength; k++)
                {
                    if (High[i] <= High[i - k] || High[i] <= High[i + k]) isSwingH = false;
                    if (Low[i]  >= Low[i - k]  || Low[i]  >= Low[i + k])  isSwingL = false;
                }
                if (isSwingH && swHCount < 3) { lvlSwingH[swHCount++] = High[i]; }
                if (isSwingL && swLCount < 3) { lvlSwingL[swLCount++] = Low[i];  }
            }
        }

        // ── Update CWH / CWL dynamically each bar ──
        private void UpdateCurrentWeekExtremes()
        {
            if (!ShowWeeklyLevels) return;
            bool changed = false;
            if (High[0] > lvlCWH) { lvlCWH = High[0]; changed = true; }
            if (lvlCWL == 0 || Low[0] < lvlCWL) { lvlCWL = Low[0]; changed = true; }

            if (changed && dateTag != "")
            {
                // Redraw CWH/CWL lines with updated values
                if (ChartRole == ChartRole.Daily)
                {
                    DrawTier(TAG_PREFIX + "CWH_" + dateTag, lvlCWH, CWHColor, DashStyleHelper.Dot, 1, "CWH", 2);
                    DrawTier(TAG_PREFIX + "CWL_" + dateTag, lvlCWL, CWLColor, DashStyleHelper.Dot, 1, "CWL", 2);
                }
            }
        }
        #endregion

        // =================================================================
        #region Volume Profile Helpers

        private void AddToProfile(Dictionary<double, double> profile, double high, double low, double volume)
        {
            double ts = TickSize;
            double rHigh = Math.Round(high / ts) * ts;
            double rLow  = Math.Round(low  / ts) * ts;
            int    nLvls = Math.Max(1, (int)Math.Round((rHigh - rLow) / ts) + 1);
            double vpl   = volume / nLvls;

            for (int t = 0; t < nLvls; t++)
            {
                double p = Math.Round((rLow + t * ts) / ts) * ts;
                if (!profile.ContainsKey(p)) profile[p] = 0;
                profile[p] += vpl;
            }
        }

        private void CalcValueArea(Dictionary<double, double> profile, double totalVol,
                                   out double poc, out double vah, out double val)
        {
            poc = vah = val = 0;
            if (profile.Count == 0) return;

            // Find POC
            double maxV = 0;
            foreach (var kv in profile)
                if (kv.Value > maxV) { maxV = kv.Value; poc = kv.Key; }

            var sorted = profile.Keys.OrderBy(k => k).ToList();
            double targetVol = totalVol * (ValueAreaPercent / 100.0);

            int pocIdx = sorted.IndexOf(poc);
            int upIdx  = pocIdx + 1;
            int dnIdx  = pocIdx - 1;
            double vaVol = profile.ContainsKey(poc) ? profile[poc] : 0;
            vah = val = poc;

            while (vaVol < targetVol && (upIdx < sorted.Count || dnIdx >= 0))
            {
                double upVol = upIdx < sorted.Count ? profile[sorted[upIdx]] : 0;
                double dnVol = dnIdx >= 0           ? profile[sorted[dnIdx]] : 0;

                if (upVol == 0 && dnVol == 0) break;

                if (upVol >= dnVol)
                { vaVol += upVol; vah = sorted[upIdx]; upIdx++; }
                else
                { vaVol += dnVol; val = sorted[dnIdx]; dnIdx--; }
            }
        }
        #endregion

        // =================================================================
        #region Draw Levels For Role

        private void DrawLevelsForRole()
        {
            switch (ChartRole)
            {
                case ChartRole.Daily:         DrawDaily();         break;
                case ChartRole.FourHour:      DrawFourHour();      break;
                case ChartRole.OneHour:       DrawOneHour();       break;
                case ChartRole.FifteenMinute: DrawFifteenMinute(); break;
                case ChartRole.ThreeMinute:   DrawThreeMinute();   break;
                case ChartRole.OneMinute:     DrawOneMinute();     break;
            }
        }

        // ── Daily Chart ──
        private void DrawDaily()
        {
            // Tier 1: PMH, PML, PWH, PWL
            if (ShowMonthlyLevels)
            {
                DrawT1(TAG_PREFIX + "PMH_" + dateTag, lvlPMH, PMHColor, DashStyleHelper.Solid, "PMH");
                DrawT1(TAG_PREFIX + "PML_" + dateTag, lvlPML, PMLColor, DashStyleHelper.Solid, "PML");
            }
            if (ShowWeeklyLevels)
            {
                DrawT1(TAG_PREFIX + "PWH_" + dateTag, lvlPWH, PWHColor, DashStyleHelper.Solid, "PWH");
                DrawT1(TAG_PREFIX + "PWL_" + dateTag, lvlPWL, PWLColor, DashStyleHelper.Solid, "PWL");
                // Tier 2: week POC, VAH, VAL, CWH, CWL
                DrawT2(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC, WeeklyPOCColor, DashStyleHelper.Solid,  "WPOC");
                DrawT2(TAG_PREFIX + "WVAH_" + dateTag, lvlWeekVAH, WeeklyVAHColor, DashStyleHelper.Solid,  "WVAH");
                DrawT2(TAG_PREFIX + "WVAL_" + dateTag, lvlWeekVAL, WeeklyVALColor, DashStyleHelper.Solid,  "WVAL");
                DrawT2(TAG_PREFIX + "CWH_"  + dateTag, lvlCWH,     CWHColor,       DashStyleHelper.Dot,    "CWH");
                DrawT2(TAG_PREFIX + "CWL_"  + dateTag, lvlCWL,     CWLColor,       DashStyleHelper.Dot,    "CWL");
            }
        }

        // ── 4H Chart ──
        private void DrawFourHour()
        {
            if (ShowWeeklyLevels)
            {
                DrawT1(TAG_PREFIX + "PWH_"  + dateTag, lvlPWH,    PWHColor,     DashStyleHelper.Solid, "PWH");
                DrawT1(TAG_PREFIX + "PWL_"  + dateTag, lvlPWL,    PWLColor,     DashStyleHelper.Solid, "PWL");
                DrawT1(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC,WeeklyPOCColor,DashStyleHelper.Solid,"WPOC");
                DrawT2(TAG_PREFIX + "WVAH_" + dateTag, lvlWeekVAH,WeeklyVAHColor,DashStyleHelper.Solid,"WVAH");
                DrawT2(TAG_PREFIX + "WVAL_" + dateTag, lvlWeekVAL,WeeklyVALColor,DashStyleHelper.Solid,"WVAL");
            }
            if (ShowYH) DrawT2(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  DashStyleHelper.Dash, "YH");
            if (ShowYL) DrawT2(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  DashStyleHelper.Dash, "YL");
            if (ShowPOC) DrawT2(TAG_PREFIX + "POC_"  + dateTag, lvlPOC, POCColor, DashStyleHelper.Solid,"POC");
            if (ShowSwingLevels)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (lvlSwingH[k] > 0) DrawT2(TAG_PREFIX + "SH" + k + "_" + dateTag, lvlSwingH[k], SwingHighColor, DashStyleHelper.Dash, "SH" + (k+1));
                    if (lvlSwingL[k] > 0) DrawT2(TAG_PREFIX + "SL" + k + "_" + dateTag, lvlSwingL[k], SwingLowColor,  DashStyleHelper.Dash, "SL" + (k+1));
                }
            }
            // Tier 3
            if (ShowMonthlyLevels)
            {
                DrawT3(TAG_PREFIX + "PMH_" + dateTag, lvlPMH, PMHColor, "PMH");
                DrawT3(TAG_PREFIX + "PML_" + dateTag, lvlPML, PMLColor, "PML");
            }
        }

        // ── 1H Chart ──
        private void DrawOneHour()
        {
            if (ShowYH) DrawT1(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  DashStyleHelper.Dash, "YH");
            if (ShowYL) DrawT1(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  DashStyleHelper.Dash, "YL");
            if (ShowPOC) DrawT1(TAG_PREFIX + "POC_"  + dateTag, lvlPOC, POCColor, DashStyleHelper.Solid,"POC");
            if (ShowPivots)
            {
                DrawT1(TAG_PREFIX + "PP_"  + dateTag, lvlPP, PPColor, DashStyleHelper.Dash, "PP");
                DrawT2(TAG_PREFIX + "R1_"  + dateTag, lvlR1, R1Color, DashStyleHelper.Dash, "R1");
                DrawT2(TAG_PREFIX + "S1_"  + dateTag, lvlS1, S1Color, DashStyleHelper.Dash, "S1");
            }
            if (ShowONH)
            {
                int ow = lvlONH > lvlYH ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONH_" + dateTag, lvlONH, ONHColor, DashStyleHelper.Dot, ow, "ONH", lvlONH > lvlYH ? 1 : 2);
            }
            if (ShowONL)
            {
                int ow = lvlONL < lvlYL ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONL_" + dateTag, lvlONL, ONLColor, DashStyleHelper.Dot, ow, "ONL", lvlONL < lvlYL ? 1 : 2);
            }
            if (ShowVAH) DrawT2(TAG_PREFIX + "VAH_" + dateTag, lvlVAH, VAHColor, DashStyleHelper.Solid, "VAH");
            if (ShowVAL) DrawT2(TAG_PREFIX + "VAL_" + dateTag, lvlVAL, VALColor, DashStyleHelper.Solid, "VAL");
            // Tier 3
            if (ShowR2S2)
            {
                DrawT3(TAG_PREFIX + "R2_" + dateTag, lvlR2, R2Color, "R2");
                DrawT3(TAG_PREFIX + "S2_" + dateTag, lvlS2, S2Color, "S2");
            }
            if (ShowR3S3)
            {
                DrawT3(TAG_PREFIX + "R3_" + dateTag, lvlR3, R3Color, "R3");
                DrawT3(TAG_PREFIX + "S3_" + dateTag, lvlS3, S3Color, "S3");
            }
            if (ShowWeeklyLevels)
            {
                DrawT3(TAG_PREFIX + "PWH_"  + dateTag, lvlPWH,     PWHColor,     "PWH");
                DrawT3(TAG_PREFIX + "PWL_"  + dateTag, lvlPWL,     PWLColor,     "PWL");
                DrawT3(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC, WeeklyPOCColor,"WPOC");
            }
            if (ShowSwingLevels)
                for (int k = 0; k < 3; k++)
                {
                    if (lvlSwingH[k] > 0) DrawT3(TAG_PREFIX + "SH" + k + "_" + dateTag, lvlSwingH[k], SwingHighColor, "SH" + (k+1));
                    if (lvlSwingL[k] > 0) DrawT3(TAG_PREFIX + "SL" + k + "_" + dateTag, lvlSwingL[k], SwingLowColor,  "SL" + (k+1));
                }
        }

        // ── 15M Chart ──
        private void DrawFifteenMinute()
        {
            if (ShowYH) DrawT1(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  DashStyleHelper.Dash, "YH");
            if (ShowYL) DrawT1(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  DashStyleHelper.Dash, "YL");
            if (ShowPOC) DrawT1(TAG_PREFIX + "POC_"  + dateTag, lvlPOC, POCColor, DashStyleHelper.Solid,"POC");
            if (ShowPivots)
            {
                DrawT1(TAG_PREFIX + "PP_"  + dateTag, lvlPP, PPColor, DashStyleHelper.Dash, "PP");
                DrawT2(TAG_PREFIX + "R1_"  + dateTag, lvlR1, R1Color, DashStyleHelper.Dash, "R1");
                DrawT2(TAG_PREFIX + "S1_"  + dateTag, lvlS1, S1Color, DashStyleHelper.Dash, "S1");
            }
            if (ShowONH)
            {
                int ow = lvlONH > lvlYH ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONH_" + dateTag, lvlONH, ONHColor, DashStyleHelper.Dot, ow, "ONH", lvlONH > lvlYH ? 1 : 2);
            }
            if (ShowONL)
            {
                int ow = lvlONL < lvlYL ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONL_" + dateTag, lvlONL, ONLColor, DashStyleHelper.Dot, ow, "ONL", lvlONL < lvlYL ? 1 : 2);
            }
            if (ShowVAH) DrawT2(TAG_PREFIX + "VAH_" + dateTag, lvlVAH, VAHColor, DashStyleHelper.Solid, "VAH");
            if (ShowVAL) DrawT2(TAG_PREFIX + "VAL_" + dateTag, lvlVAL, VALColor, DashStyleHelper.Solid, "VAL");
            // OR drawn by DrawOpeningRangeLevels() after 9:45
            // Tier 3
            if (ShowR2S2)
            {
                DrawT3(TAG_PREFIX + "R2_" + dateTag, lvlR2, R2Color, "R2");
                DrawT3(TAG_PREFIX + "S2_" + dateTag, lvlS2, S2Color, "S2");
            }
            if (ShowR3S3)
            {
                DrawT3(TAG_PREFIX + "R3_" + dateTag, lvlR3, R3Color, "R3");
                DrawT3(TAG_PREFIX + "S3_" + dateTag, lvlS3, S3Color, "S3");
            }
            if (ShowWeeklyLevels)
            {
                DrawT3(TAG_PREFIX + "PWH_"  + dateTag, lvlPWH,     PWHColor,      "PWH");
                DrawT3(TAG_PREFIX + "PWL_"  + dateTag, lvlPWL,     PWLColor,      "PWL");
                DrawT3(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC, WeeklyPOCColor,"WPOC");
            }
            if (ShowSwingLevels)
                for (int k = 0; k < 3; k++)
                {
                    if (lvlSwingH[k] > 0) DrawT3(TAG_PREFIX + "SH" + k + "_" + dateTag, lvlSwingH[k], SwingHighColor, "SH" + (k+1));
                    if (lvlSwingL[k] > 0) DrawT3(TAG_PREFIX + "SL" + k + "_" + dateTag, lvlSwingL[k], SwingLowColor,  "SL" + (k+1));
                }
        }

        // ── 3M Chart ──
        private void DrawThreeMinute()
        {
            if (ShowYH) DrawT1(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  DashStyleHelper.Dash, "YH");
            if (ShowYL) DrawT1(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  DashStyleHelper.Dash, "YL");
            if (ShowPOC) DrawT1(TAG_PREFIX + "POC_"  + dateTag, lvlPOC, POCColor, DashStyleHelper.Solid,"POC");
            if (ShowPivots) DrawT1(TAG_PREFIX + "PP_"   + dateTag, lvlPP, PPColor, DashStyleHelper.Dash, "PP");
            // OR added after 9:45 via DrawOpeningRangeLevels()
            if (ShowPivots)
            {
                DrawT2(TAG_PREFIX + "R1_"  + dateTag, lvlR1, R1Color, DashStyleHelper.Dash, "R1");
                DrawT2(TAG_PREFIX + "S1_"  + dateTag, lvlS1, S1Color, DashStyleHelper.Dash, "S1");
            }
            if (ShowONH)
            {
                int ow = lvlONH > lvlYH ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONH_" + dateTag, lvlONH, ONHColor, DashStyleHelper.Dot, ow, "ONH", lvlONH > lvlYH ? 1 : 2);
            }
            if (ShowONL)
            {
                int ow = lvlONL < lvlYL ? LineThicknessPrimary : LineThicknessSecondary;
                DrawTier(TAG_PREFIX + "ONL_" + dateTag, lvlONL, ONLColor, DashStyleHelper.Dot, ow, "ONL", lvlONL < lvlYL ? 1 : 2);
            }
            // Tier 3
            if (ShowR2S2)
            {
                DrawT3(TAG_PREFIX + "R2_" + dateTag, lvlR2, R2Color, "R2");
                DrawT3(TAG_PREFIX + "S2_" + dateTag, lvlS2, S2Color, "S2");
            }
            if (ShowR3S3)
            {
                DrawT3(TAG_PREFIX + "R3_" + dateTag, lvlR3, R3Color, "R3");
                DrawT3(TAG_PREFIX + "S3_" + dateTag, lvlS3, S3Color, "S3");
            }
            if (ShowWeeklyLevels)
            {
                DrawT3(TAG_PREFIX + "PWH_"  + dateTag, lvlPWH,     PWHColor,      "PWH");
                DrawT3(TAG_PREFIX + "PWL_"  + dateTag, lvlPWL,     PWLColor,      "PWL");
                DrawT3(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC, WeeklyPOCColor,"WPOC");
            }
            if (ShowVAH) DrawT3(TAG_PREFIX + "VAH_" + dateTag, lvlVAH, VAHColor, "VAH");
            if (ShowVAL) DrawT3(TAG_PREFIX + "VAL_" + dateTag, lvlVAL, VALColor, "VAL");
            if (ShowSwingLevels)
                for (int k = 0; k < 3; k++)
                {
                    if (lvlSwingH[k] > 0) DrawT3(TAG_PREFIX + "SH" + k + "_" + dateTag, lvlSwingH[k], SwingHighColor, "SH" + (k+1));
                    if (lvlSwingL[k] > 0) DrawT3(TAG_PREFIX + "SL" + k + "_" + dateTag, lvlSwingL[k], SwingLowColor,  "SL" + (k+1));
                }
        }

        // ── 1M Chart ──
        private void DrawOneMinute()
        {
            // Tier 1: PP only
            if (ShowPivots) DrawT1(TAG_PREFIX + "PP_" + dateTag, lvlPP, PPColor, DashStyleHelper.Dash, "PP");

            // Everything else is Tier 3 with tight threshold
            if (ShowYH)    DrawT3(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  "YH");
            if (ShowYL)    DrawT3(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  "YL");
            if (ShowPOC)   DrawT3(TAG_PREFIX + "POC_"  + dateTag, lvlPOC, POCColor, "POC");
            if (ShowONH)   DrawT3(TAG_PREFIX + "ONH_"  + dateTag, lvlONH, ONHColor, "ONH");
            if (ShowONL)   DrawT3(TAG_PREFIX + "ONL_"  + dateTag, lvlONL, ONLColor, "ONL");
            if (ShowPivots)
            {
                DrawT3(TAG_PREFIX + "R1_" + dateTag, lvlR1, R1Color, "R1");
                DrawT3(TAG_PREFIX + "S1_" + dateTag, lvlS1, S1Color, "S1");
            }
        }

        // ── Opening Range (called after 9:45 AM) ──
        private void DrawOpeningRangeLevels()
        {
            if (!instrumentHasOR || !ShowOR) return;
            if (lvlORH == 0 || lvlORL == 0) return;

            // OR is Tier 1 on 3M, Tier 2 on 15M
            if (ChartRole == ChartRole.ThreeMinute || ChartRole == ChartRole.OneMinute)
            {
                DrawT1(TAG_PREFIX + "ORH_" + dateTag, lvlORH, ORHighColor, DashStyleHelper.Dash, "ORH");
                DrawT1(TAG_PREFIX + "ORL_" + dateTag, lvlORL, ORLowColor,  DashStyleHelper.Dash, "ORL");
            }
            else if (ChartRole == ChartRole.FifteenMinute || ChartRole == ChartRole.OneHour)
            {
                DrawT2(TAG_PREFIX + "ORH_" + dateTag, lvlORH, ORHighColor, DashStyleHelper.Dash, "ORH");
                DrawT2(TAG_PREFIX + "ORL_" + dateTag, lvlORL, ORLowColor,  DashStyleHelper.Dash, "ORL");
            }
        }
        #endregion

        // =================================================================
        #region Drawing Primitives

        // Tier-specific draw helpers
        private void DrawT1(string tag, double price, Brush color, DashStyleHelper dash, string lbl)
            => DrawTier(tag, price, color, dash, LineThicknessPrimary, lbl, 1);

        private void DrawT2(string tag, double price, Brush color, DashStyleHelper dash, string lbl)
            => DrawTier(tag, price, color, dash, LineThicknessSecondary, lbl, 2);

        private void DrawT3(string tag, double price, Brush color, string lbl)
            => DrawTier(tag, price, color, DashStyleHelper.Dot, 1, lbl, 3);

        // Core draw method
        private void DrawTier(string tag, double price, Brush color, DashStyleHelper dash,
                               int width, string lbl, int tier)
        {
            if (price == 0) return;

            Brush lineColor  = tier == 1 ? color : WithOpacity(color, tier == 2 ? 0.70 : 0.50);
            Brush labelColor = lineColor;

            // For Tier 3 — initially hidden; drawn only when proximity-activated
            if (tier == 3)
            {
                if (!tier3Activated.Contains(tag))
                    return; // will be drawn by CheckProximityActivation()
            }

            try
            {
                var hl = Draw.HorizontalLine(this, tag, false, price, true, "");
                if (hl != null)
                    hl.Stroke = new Stroke(lineColor, dash, (float)width);
                if (!drawnTags.Contains(tag)) drawnTags.Add(tag);
            }
            catch (Exception ex)
            {
                Print(LOG_PREFIX + " DrawHL error [" + tag + "]: " + ex.Message);
            }

            if (ShowLabels && lbl.Length > 0)
            {
                string labelTag = tag + "_L";
                int fontSize = tier == 1 ? LabelFontSize
                             : tier == 2 ? LabelFontSize - 1
                             : LabelFontSize - 2;
                fontSize = Math.Max(6, fontSize);
                string text = lbl + " " + FormatPrice(price);
                try
                {
                    Draw.Text(this, labelTag, false, text, 0, price, 4,
                              labelColor, new SimpleFont("Courier New", fontSize),
                              TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0, true, "");
                    if (!drawnTags.Contains(labelTag)) drawnTags.Add(labelTag);
                }
                catch (Exception ex)
                {
                    Print(LOG_PREFIX + " DrawText error [" + labelTag + "]: " + ex.Message);
                }
            }
        }

        // Opacity helper — clones a SolidColorBrush at given alpha
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
        #region Proximity Activation (Tier 3)

        private void CheckProximityActivation()
        {
            if (CurrentBar < 1) return;
            double cur    = Close[0];
            double thresh = ProximityTicks * TickSize;

            // Special 1M threshold
            if (ChartRole == ChartRole.OneMinute) thresh = 20 * TickSize;

            CheckAndActivate(TAG_PREFIX + "YH_"   + dateTag, lvlYH,  YHColor,  DashStyleHelper.Dash, "YH",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "YL_"   + dateTag, lvlYL,  YLColor,  DashStyleHelper.Dash, "YL",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "ONH_"  + dateTag, lvlONH, ONHColor, DashStyleHelper.Dot,  "ONH",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "ONL_"  + dateTag, lvlONL, ONLColor, DashStyleHelper.Dot,  "ONL",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "R2_"   + dateTag, lvlR2,  R2Color,  DashStyleHelper.Dot,  "R2",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "R3_"   + dateTag, lvlR3,  R3Color,  DashStyleHelper.Dot,  "R3",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "S2_"   + dateTag, lvlS2,  S2Color,  DashStyleHelper.Dot,  "S2",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "S3_"   + dateTag, lvlS3,  S3Color,  DashStyleHelper.Dot,  "S3",   cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "PWH_"  + dateTag, lvlPWH, PWHColor, DashStyleHelper.Dot,  "PWH",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "PWL_"  + dateTag, lvlPWL, PWLColor, DashStyleHelper.Dot,  "PWL",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "WPOC_" + dateTag, lvlWeekPOC,WeeklyPOCColor,DashStyleHelper.Dot,"WPOC",cur,thresh,3);
            CheckAndActivate(TAG_PREFIX + "VAH_"  + dateTag, lvlVAH, VAHColor, DashStyleHelper.Dot,  "VAH",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "VAL_"  + dateTag, lvlVAL, VALColor, DashStyleHelper.Dot,  "VAL",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "PMH_"  + dateTag, lvlPMH, PMHColor, DashStyleHelper.Dot,  "PMH",  cur, thresh, 3);
            CheckAndActivate(TAG_PREFIX + "PML_"  + dateTag, lvlPML, PMLColor, DashStyleHelper.Dot,  "PML",  cur, thresh, 3);
            for (int k = 0; k < 3; k++)
            {
                CheckAndActivate(TAG_PREFIX + "SH" + k + "_" + dateTag, lvlSwingH[k], SwingHighColor, DashStyleHelper.Dot, "SH" + (k+1), cur, thresh, 3);
                CheckAndActivate(TAG_PREFIX + "SL" + k + "_" + dateTag, lvlSwingL[k], SwingLowColor,  DashStyleHelper.Dot, "SL" + (k+1), cur, thresh, 3);
            }
        }

        private void CheckAndActivate(string tag, double price, Brush color, DashStyleHelper dash,
                                       string lbl, double cur, double thresh, int tier)
        {
            if (price == 0) return;
            if (tier3Activated.Contains(tag)) return; // already visible — stays visible
            if (Math.Abs(cur - price) <= thresh)
            {
                tier3Activated.Add(tag);
                DrawTier(tag, price, color, dash, 1, lbl, tier);
                DetectAndDrawConfluence(); // re-run when new level becomes visible
            }
        }
        #endregion

        // =================================================================
        #region Confluence Zone Detection

        private void DetectAndDrawConfluence()
        {
            if (!ShowConfluenceZones) return;

            // Remove old confluence rectangles for today
            var oldCZ = drawnTags.Where(t => t.StartsWith(TAG_PREFIX + "CZ") && t.EndsWith("_" + dateTag)).ToList();
            foreach (string t in oldCZ)
            {
                try { RemoveDrawObject(t); } catch { }
                drawnTags.Remove(t);
                try { RemoveDrawObject(t + "_L"); } catch { }
                drawnTags.Remove(t + "_L");
            }

            // Collect all currently VISIBLE levels (Tier 1 and Tier 2)
            var visible = CollectVisibleLevels();
            visible.Sort();

            double czThresh = ConfluenceProximityTicks * TickSize;
            var zones = new List<(double low, double high, List<string> names)>();

            int i = 0;
            while (i < visible.Count)
            {
                double zLow   = visible[i].price;
                double zHigh  = visible[i].price;
                var    zNames = new List<string> { visible[i].name };
                int    j = i + 1;
                while (j < visible.Count && visible[j].price - zLow <= czThresh)
                {
                    zHigh = visible[j].price;
                    zNames.Add(visible[j].name);
                    j++;
                }
                if (zNames.Count >= 2) zones.Add((zLow, zHigh, zNames));
                i = j;
            }

            // Draw each confluence zone
            for (int z = 0; z < zones.Count; z++)
            {
                var zone     = zones[z];
                string czTag = TAG_PREFIX + "CZ" + z + "_" + dateTag;

                string grade  = GradeConfluenceZone(zone.low, zone.high);
                double opacity = grade.Contains("HIGH") ? 0.20 : grade.Contains("LOW") ? 0.10 : 0.15;

                Brush fillBrush    = WithOpacity(ConfluenceZoneColor, opacity);
                Brush borderBrush  = WithOpacity(ConfluenceZoneColor, 0.40);

                try
                {
                    var rect = Draw.Rectangle(this, czTag, false,
                        CurrentBar, zone.high, 0, zone.low,
                        borderBrush, fillBrush, opacity * 100.0, true, "");
                    if (!drawnTags.Contains(czTag)) drawnTags.Add(czTag);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " DrawRect error: " + ex.Message); }

                string lbl     = "CONFLUENCE — " + grade;
                string lblTag  = czTag + "_L";
                double midPrice = (zone.low + zone.high) / 2.0;
                Brush  lblColor = WithOpacity(ConfluenceZoneColor, 0.80);
                try
                {
                    Draw.Text(this, lblTag, false, lbl, 0, midPrice, 0,
                              lblColor, new SimpleFont("Courier New", 8),
                              TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0, true, "");
                    if (!drawnTags.Contains(lblTag)) drawnTags.Add(lblTag);
                }
                catch { }

                if (ShowVerificationOutput)
                {
                    double zoneVol = GetZoneVolume(zone.low, zone.high);
                    int    nLevels = (int)Math.Round(Math.Abs(zone.high - zone.low) / TickSize) + 1;
                    double zoneAvg = nLevels > 0 ? zoneVol / nLevels : 0;
                    Print(string.Format("{0} ── CONFLUENCE ZONE {1} ──", LOG_PREFIX, z + 1));
                    Print(string.Format("{0}   Levels: {1}", LOG_PREFIX, string.Join(" + ", zone.names)));
                    Print(string.Format("{0}   Zone range: {1} to {2}", LOG_PREFIX, zone.low, zone.high));
                    Print(string.Format("{0}   Profile volume in zone: {1:N0}", LOG_PREFIX, zoneVol));
                    Print(string.Format("{0}   Profile average per level: {1:F1}", LOG_PREFIX, dayProfileAvg));
                    Print(string.Format("{0}   Zone average per level: {1:F1}", LOG_PREFIX, zoneAvg));
                    Print(string.Format("{0}   Grade: {1}", LOG_PREFIX, grade +
                          (dayProfileAvg > 0 ? string.Format(" ({0:F1}x average)", zoneAvg / dayProfileAvg) : "")));
                }
            }
        }

        private string GradeConfluenceZone(double low, double high)
        {
            if (dayProfile.Count == 0 || dayProfileAvg == 0)
                return "NO PRIOR VOL";

            double zoneVol = GetZoneVolume(low, high);
            int    nLevels = (int)Math.Round(Math.Abs(high - low) / TickSize) + 1;
            double zoneAvg = nLevels > 0 ? zoneVol / nLevels : 0;

            if (zoneVol == 0) return "NO PRIOR VOL";
            return zoneAvg >= dayProfileAvg ? "HIGH VOL" : "LOW VOL";
        }

        private double GetZoneVolume(double low, double high)
        {
            if (dayProfile.Count == 0) return 0;
            double vol = 0;
            foreach (var kv in dayProfile)
                if (kv.Key >= low - TickSize * 0.01 && kv.Key <= high + TickSize * 0.01)
                    vol += kv.Value;
            return vol;
        }

        // Returns sorted list of currently visible (T1/T2) level prices
        private List<(double price, string name)> CollectVisibleLevels()
        {
            var list = new List<(double, string)>();

            void Add(double p, string n) { if (p > 0) list.Add((p, n)); }

            switch (ChartRole)
            {
                case ChartRole.Daily:
                    if (ShowMonthlyLevels) { Add(lvlPMH,"PMH"); Add(lvlPML,"PML"); }
                    if (ShowWeeklyLevels)  { Add(lvlPWH,"PWH"); Add(lvlPWL,"PWL"); Add(lvlWeekPOC,"WPOC"); Add(lvlWeekVAH,"WVAH"); Add(lvlWeekVAL,"WVAL"); Add(lvlCWH,"CWH"); Add(lvlCWL,"CWL"); }
                    break;
                case ChartRole.FourHour:
                    if (ShowWeeklyLevels) { Add(lvlPWH,"PWH"); Add(lvlPWL,"PWL"); Add(lvlWeekPOC,"WPOC"); Add(lvlWeekVAH,"WVAH"); Add(lvlWeekVAL,"WVAL"); }
                    if (ShowYH)  Add(lvlYH,"YH"); if (ShowYL) Add(lvlYL,"YL");
                    if (ShowPOC) Add(lvlPOC,"POC");
                    break;
                case ChartRole.OneHour:
                case ChartRole.FifteenMinute:
                case ChartRole.ThreeMinute:
                    if (ShowYH)     Add(lvlYH,"YH");    if (ShowYL)   Add(lvlYL,"YL");
                    if (ShowPOC)    Add(lvlPOC,"POC");
                    if (ShowPivots) { Add(lvlPP,"PP"); Add(lvlR1,"R1"); Add(lvlS1,"S1"); }
                    if (ShowONH)    Add(lvlONH,"ONH");  if (ShowONL)  Add(lvlONL,"ONL");
                    if (ShowVAH)    Add(lvlVAH,"VAH");  if (ShowVAL)  Add(lvlVAL,"VAL");
                    if (orComplete && ShowOR) { Add(lvlORH,"ORH"); Add(lvlORL,"ORL"); }
                    break;
                case ChartRole.OneMinute:
                    if (ShowPivots) Add(lvlPP,"PP");
                    break;
            }

            // Also add any tier3 levels that have been activated
            foreach (string t in tier3Activated)
            {
                // Extract level name from tag for labeling
                string n = t.Replace(TAG_PREFIX,"").Replace("_" + dateTag,"");
                double p = LevelFromTag(n);
                if (p > 0) list.Add((p, n));
            }

            return list.Distinct().ToList();
        }

        private double LevelFromTag(string shortName)
        {
            switch (shortName)
            {
                case "YH": return lvlYH; case "YL": return lvlYL;
                case "ONH": return lvlONH; case "ONL": return lvlONL;
                case "PP": return lvlPP; case "R1": return lvlR1; case "S1": return lvlS1;
                case "R2": return lvlR2; case "S2": return lvlS2;
                case "R3": return lvlR3; case "S3": return lvlS3;
                case "POC": return lvlPOC; case "VAH": return lvlVAH; case "VAL": return lvlVAL;
                case "PWH": return lvlPWH; case "PWL": return lvlPWL;
                case "WPOC": return lvlWeekPOC; case "WVAH": return lvlWeekVAH; case "WVAL": return lvlWeekVAL;
                case "PMH": return lvlPMH; case "PML": return lvlPML;
                case "ORH": return lvlORH; case "ORL": return lvlORL;
                case "SH0": return lvlSwingH[0]; case "SH1": return lvlSwingH[1]; case "SH2": return lvlSwingH[2];
                case "SL0": return lvlSwingL[0]; case "SL1": return lvlSwingL[1]; case "SL2": return lvlSwingL[2];
                default: return 0;
            }
        }
        #endregion

        // =================================================================
        #region Legend

        private void DrawLegend()
        {
            if (!ShowLegend) { try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { } return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("── THREE PILLARS MAP " + VERSION + " ──");

            // Only show lines for visible levels
            if (ShowYH || ShowYL)
                sb.AppendLine("YH = Yesterday High    YL = Yesterday Low");
            if (ShowONH || ShowONL)
                sb.AppendLine("ONH= Overnight High    ONL= Overnight Low");
            if (ShowPOC || ShowVAH || ShowVAL)
                sb.AppendLine("POC= Point of Control  VAH= Value Area High  VAL= Value Area Low");
            if (ShowPivots)
                sb.AppendLine("PP = Pivot Point       R1 = Resistance 1     S1 = Support 1");
            if (ShowR2S2)
                sb.AppendLine("R2 = Resistance 2      S2 = Support 2");
            if (ShowR3S3)
                sb.AppendLine("R3 = Resistance 3      S3 = Support 3");
            if (ShowOR && instrumentHasOR)
                sb.AppendLine("ORH= Opening Range Hi  ORL= Opening Range Lo");
            if (ShowWeeklyLevels)
                sb.AppendLine("PWH= Prior Week High   PWL= Prior Week Low   WPOC= Weekly POC");
            if (ShowMonthlyLevels)
                sb.AppendLine("PMH= Prior Month High  PML= Prior Month Low");
            if (ShowSwingLevels)
                sb.AppendLine("SH = Swing High (4H)   SL = Swing Low (4H)");
            sb.AppendLine("── TIERS ──");
            sb.AppendLine("THICK SOLID  = Tier 1 (always visible)");
            sb.AppendLine("THIN DASHED  = Tier 2 (context)");
            sb.AppendLine("THIN DOTTED  = Tier 3 (proximity activated)");
            if (ShowConfluenceZones)
            {
                sb.AppendLine("── CONFLUENCE ──");
                sb.AppendLine("HIGH VOL = Strong zone (volume confirmed)");
                sb.AppendLine("LOW VOL  = Weaker zone (thin volume)");
            }

            TextPosition tp;
            switch (LegendPosition)
            {
                case TPMLegendPosition.TopRight:   tp = TextPosition.TopRight;    break;
                case TPMLegendPosition.BottomLeft: tp = TextPosition.BottomLeft;  break;
                case TPMLegendPosition.BottomRight:tp = TextPosition.BottomRight; break;
                default:                           tp = TextPosition.TopLeft;     break;
            }

            Brush textBrush = WithOpacity(Brushes.LightGray, 0.80);
            Brush bgBrush   = WithOpacity(Brushes.Black, 0.40);

            try
            {
                Draw.TextFixed(this, TAG_PREFIX + "LEGEND", sb.ToString(), tp,
                    textBrush, new SimpleFont("Courier New", 8),
                    Brushes.Transparent, bgBrush, 40.0);
            }
            catch (Exception ex) { Print(LOG_PREFIX + " Legend error: " + ex.Message); }
        }
        #endregion

        // =================================================================
        #region Verification

        private void RunVerificationChecks()
        {
            // 1. PP between YH and YL
            if (lvlPP > 0 && (lvlPP > lvlYH || lvlPP < lvlYL))
                Print(LOG_PREFIX + " WARNING: PP=" + lvlPP + " is outside YH-YL range — calculation error");

            // 2. R1 between PP and R2
            if (lvlR1 > 0 && lvlR2 > 0 && !(lvlR1 > lvlPP && lvlR1 < lvlR2))
                Print(LOG_PREFIX + " WARNING: R1 ordering error: PP=" + lvlPP + " R1=" + lvlR1 + " R2=" + lvlR2);

            // 3. S1 between PP and S2
            if (lvlS1 > 0 && lvlS2 > 0 && !(lvlS1 < lvlPP && lvlS1 > lvlS2))
                Print(LOG_PREFIX + " WARNING: S1 ordering error: PP=" + lvlPP + " S1=" + lvlS1 + " S2=" + lvlS2);

            // 4. VAH > POC > VAL
            if (lvlPOC > 0 && lvlVAH > 0 && lvlVAL > 0 && !(lvlVAH > lvlPOC && lvlPOC > lvlVAL))
                Print(LOG_PREFIX + " WARNING: Profile ordering error: VAH=" + lvlVAH + " POC=" + lvlPOC + " VAL=" + lvlVAL);

            // 5. Value area %
            if (dayTotalVol > 0 && lvlVAH > 0 && lvlVAL > 0)
            {
                double vaVol = GetZoneVolume(lvlVAL, lvlVAH);
                double pct   = vaVol / dayTotalVol * 100.0;
                if (pct < (ValueAreaPercent - 5) || pct > (ValueAreaPercent + 5))
                    Print(string.Format("{0} WARNING: Value area is {1:F1}% of total — target was {2}%",
                        LOG_PREFIX, pct, ValueAreaPercent));
            }

            // 6. ONH/ONL sanity
            if (lvlONH > 0 && lvlYH > 0 && lvlONH > lvlYH * 1.10)
                Print(LOG_PREFIX + " WARNING: ONH=" + lvlONH + " seems unusually far above YH=" + lvlYH);

            // 7. Auto-calculated level zero checks
            string[] names  = { "YH","YL","YC","PP","R1","S1","POC","VAH","VAL","PWH","PWL" };
            double[] values = { lvlYH,lvlYL,lvlYC,lvlPP,lvlR1,lvlS1,lvlPOC,lvlVAH,lvlVAL,lvlPWH,lvlPWL };
            for (int n = 0; n < names.Length; n++)
                if (values[n] == 0)
                    Print(LOG_PREFIX + " WARNING: " + names[n] + " is 0 — calculation may have failed");
        }

        private void PrintVerificationOutput(DateTime today)
        {
            Print(string.Format("{0} ═══ SESSION: {1} ═══", LOG_PREFIX, today.ToString("yyyy-MM-dd")));
            Print(string.Format("{0} Instrument: {1} | Chart Role: {2} | TickSize: {3}",
                LOG_PREFIX, instrumentKey, ChartRole, TickSize));
            Print("");
            Print(LOG_PREFIX + " ── PRIOR DAY LEVELS ──");
            Print(string.Format("{0}   Prior Session Date: {1}", LOG_PREFIX, priorRTHDate.ToString("yyyy-MM-dd")));
            Print(string.Format("{0}   RTH Bars Found: {1}", LOG_PREFIX, priorDayBars));
            Print(string.Format("{0}   YH = {1}", LOG_PREFIX, lvlYH));
            Print(string.Format("{0}   YL = {1}", LOG_PREFIX, lvlYL));
            Print(string.Format("{0}   YC = {1} (close of last RTH bar)", LOG_PREFIX, lvlYC));
            Print("");
            Print(LOG_PREFIX + " ── PIVOT VERIFICATION ──");
            Print(string.Format("{0}   PP = ({1} + {2} + {3}) / 3 = {4:F2}",
                LOG_PREFIX, lvlYH, lvlYL, lvlYC, lvlPP));
            Print(string.Format("{0}   R1 = (2 × {1:F2}) − {2} = {3:F2}", LOG_PREFIX, lvlPP, lvlYL, lvlR1));
            Print(string.Format("{0}   S1 = (2 × {1:F2}) − {2} = {3:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlS1));
            Print(string.Format("{0}   R2 = {1:F2} + ({2} − {3}) = {4:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlYL, lvlR2));
            Print(string.Format("{0}   S2 = {1:F2} − ({2} − {3}) = {4:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlYL, lvlS2));
            Print("");
            Print(LOG_PREFIX + " ── OVERNIGHT LEVELS ──");
            Print(string.Format("{0}   Overnight bars scanned: {1}", LOG_PREFIX, overnightBars));
            Print(string.Format("{0}   ONH = {1} | vs YH: {2}", LOG_PREFIX, lvlONH,
                lvlONH > lvlYH ? "ONH > YH → PROMOTED to Tier 1" : "ONH <= YH → standard Tier 2"));
            Print(string.Format("{0}   ONL = {1} | vs YL: {2}", LOG_PREFIX, lvlONL,
                lvlONL < lvlYL ? "ONL < YL → PROMOTED to Tier 1" : "ONL >= YL → standard Tier 2"));
            Print("");
            Print(LOG_PREFIX + " ── VOLUME PROFILE (Prior Day) ──");
            Print(string.Format("{0}   Price levels in profile: {1}", LOG_PREFIX, dayProfile.Count));
            Print(string.Format("{0}   Total volume: {1:N0}", LOG_PREFIX, dayTotalVol));
            Print(string.Format("{0}   POC = {1}", LOG_PREFIX, lvlPOC));
            if (dayTotalVol > 0)
            {
                double vaVol = GetZoneVolume(lvlVAL, lvlVAH);
                Print(string.Format("{0}   Value Area {1}%: target vol = {2:N0}", LOG_PREFIX, ValueAreaPercent, dayTotalVol * ValueAreaPercent / 100.0));
                Print(string.Format("{0}   VAH = {1} | VAL = {2}", LOG_PREFIX, lvlVAH, lvlVAL));
                Print(string.Format("{0}   Actual VA volume: {1:N0} ({2:F1}% of total)", LOG_PREFIX, vaVol, vaVol / dayTotalVol * 100.0));
            }
            Print("");
            Print(LOG_PREFIX + " ── WEEKLY LEVELS ──");
            Print(string.Format("{0}   PWH = {1} | PWL = {2}", LOG_PREFIX, lvlPWH, lvlPWL));
            Print(string.Format("{0}   Weekly POC = {1} | Weekly VAH = {2} | Weekly VAL = {3}",
                LOG_PREFIX, lvlWeekPOC, lvlWeekVAH, lvlWeekVAL));
            Print("");
            Print(LOG_PREFIX + " ── MONTHLY LEVELS ──");
            Print(string.Format("{0}   PMH = {1} | PML = {2}", LOG_PREFIX, lvlPMH, lvlPML));
            Print("");
            Print(LOG_PREFIX + " ── TIER ASSIGNMENTS (ChartRole: " + ChartRole + ") ──");
            PrintTierAssignments();
            Print("");
            Print(LOG_PREFIX + " ═══ VERIFICATION COMPLETE ═══");
        }

        private void PrintTierAssignments()
        {
            // Print which levels are at which tier for the current role
            switch (ChartRole)
            {
                case ChartRole.Daily:
                    Print(string.Format("{0}   Tier 1: PMH={1}, PML={2}, PWH={3}, PWL={4}", LOG_PREFIX, lvlPMH, lvlPML, lvlPWH, lvlPWL));
                    Print(string.Format("{0}   Tier 2: WPOC={1}, WVAH={2}, WVAL={3}, CWH={4}, CWL={5}", LOG_PREFIX, lvlWeekPOC, lvlWeekVAH, lvlWeekVAL, lvlCWH, lvlCWL));
                    break;
                case ChartRole.FourHour:
                    Print(string.Format("{0}   Tier 1: PWH={1}, PWL={2}, WPOC={3}", LOG_PREFIX, lvlPWH, lvlPWL, lvlWeekPOC));
                    Print(string.Format("{0}   Tier 2: YH={1}, YL={2}, POC={3}", LOG_PREFIX, lvlYH, lvlYL, lvlPOC));
                    Print(string.Format("{0}   Tier 3: PMH={1}, PML={2}", LOG_PREFIX, lvlPMH, lvlPML));
                    break;
                default:
                    Print(string.Format("{0}   Tier 1: YH={1}, YL={2}, POC={3}, PP={4}", LOG_PREFIX, lvlYH, lvlYL, lvlPOC, lvlPP));
                    Print(string.Format("{0}   Tier 2: R1={1}, S1={2}, ONH={3}({4}), ONL={5}",
                        LOG_PREFIX, lvlR1, lvlS1, lvlONH, lvlONH > lvlYH ? "promoted" : "standard", lvlONL));
                    Print(string.Format("{0}   Tier 3: R2={1}, S2={2}, R3={3}, S3={4}, PWH={5}, PWL={6}, VAH={7}, VAL={8}",
                        LOG_PREFIX, lvlR2, lvlS2, lvlR3, lvlS3, lvlPWH, lvlPWL, lvlVAH, lvlVAL));
                    break;
            }
        }
        #endregion

        // =================================================================
        #region Utilities

        private bool IsRTHBar(TimeSpan t) => t >= rthOpen && t <= rthClose;

        private void ClearTagsForDate(string dtag)
        {
            var toRemove = drawnTags.Where(t => t.Contains("_" + dtag)).ToList();
            foreach (string t in toRemove)
            {
                try { RemoveDrawObject(t); } catch { }
                drawnTags.Remove(t);
            }
        }
        #endregion
    }
}
