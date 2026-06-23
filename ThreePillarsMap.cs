// ============================================================
// ThreePillarsMap.cs  â€”  v2.0
// NinjaTrader 8  |  Multi-Timeframe Structural Wall Map
// ============================================================
//
// INSTALLATION:
//   1. Copy this file to:
//      Documents\NinjaTrader 8\bin\Custom\Indicators\
//   2. In NinjaTrader: Tools > NinjaScript Editor > Compile
//      â€” or â€” Tools > Compile NinjaScript
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
// VISUAL SYSTEM:
//   Wall   - 2+ overlapping levels clustered together. Thick band + center
//            line, opacity/thickness scales with touch count (** / *** / ****).
//   Anchor - a single reference level shown alone (prior-day H/L/C,
//            today's POC/VAH/VAL, key HTF refs near price). Thin dotted
//            line - visually distinct from a confirmed multi-touch wall.
//
// GLOBAL DRAW OBJECTS:
//   All levels are created as Global Draw Objects so they appear on
//   every open chart for the same instrument simultaneously.
//
// PARAMETERS (grouped by category in the indicator dialog):
//
//   Chart Configuration
//     ChartRole                â€” Daily / FourHour / OneHour / FifteenMinute /
//                                ThreeMinute / OneMinute
//
//   Display
//     ShowLabels               â€” Toggle level name + price labels
//     ShowLegend               â€” Toggle the legend key panel
//     LegendPosition           â€” TopLeft / TopRight / BottomLeft / BottomRight
//     LabelFontSize            â€” 6â€“16, default 9
//     ShowVerificationOutput   â€” Print detailed calculations to Output window
//
//   Visibility Toggles
//     ShowYH / ShowYL          â€” Yesterday High / Low
//     ShowONH / ShowONL        â€” Overnight High / Low
//     ShowPOC / ShowVAH / ShowVAL â€” Daily volume profile levels
//     ShowPivots               â€” PP, R1, S1
//     ShowR2S2                 â€” R2, S2 (default false)
//     ShowR3S3                 â€” R3, S3 (default false)
//     ShowOR                   â€” Opening Range (equity index only)
//     ShowWeeklyLevels         â€” Prior week + current week levels
//     ShowMonthlyLevels        â€” Prior month levels (default false)
//     ShowSwingLevels          â€” 4H swing high/low levels
//
//   Structural Walls
//     MaxWallsPerSide          â€” Confluence walls drawn above/below price (default 5)
//     MinConfluence            â€” Min overlapping levels to count as a wall (default 2)
//     ClusterTolerancePercent  â€” % of price within which levels merge (default 0.05)
//     WallRangePercent         â€” % of price defining the visible wall range (default 1.5)
//     ShowAnchors              â€” Always show prior-day H/L/C + today's POC/VAH/VAL
//     MinRRRatio               â€” R/R threshold used by the context panel (default 1.5)
//     ShowApproachBands        â€” Wider transparent zone around 3+/4+ touch walls
//     ApproachBandWidth        â€” Approach zone width in ticks (default 100)
//
//   Volume Profile
//     ValueAreaPercent         â€” Target % of volume in value area (50â€“90, default 70)
//
//   Swing Detection
//     SwingStrength            â€” Bars each side a pivot must dominate (1â€“30, default 3;
//                                role defaults auto-apply per ChartRole, see ApplyRoleDefaults)
//     ShowSwingMarkers         â€” Dot + dotted line at every confirmed swing pivot
//                                (all bars, including overnight). Off by default on 3M/1M.
//
//   Manual 4H Swing Levels    â€” Enter manually from your 4H chart if desired.
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
    [CategoryOrder("Colors â€” Daily Levels",     6)]
    [CategoryOrder("Colors â€” Overnight",        7)]
    [CategoryOrder("Colors â€” Volume Profile",   8)]
    [CategoryOrder("Colors â€” Pivots",           9)]
    [CategoryOrder("Colors â€” Opening Range",   10)]
    [CategoryOrder("Colors â€” Weekly Levels",   11)]
    [CategoryOrder("Colors â€” Monthly Levels",  12)]
    [CategoryOrder("Colors â€” Swing Levels",    13)]
    [CategoryOrder("Manual 4H Swing Levels",   15)]
    public class ThreePillarsMap : Indicator
    {
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Constants
        private const int    MAX_LOOKBACK_DAILY   = 500;
        private const int    MAX_LOOKBACK_WEEKLY  = 2000;
        private const int    MAX_LOOKBACK_MONTHLY = 5000;
        private const string TAG_PREFIX           = "3PM_";
        private const string VERSION              = "v2.0";
        private const string LOG_PREFIX           = "[ThreePillarsMap v2.0]";
        private const double VALUE_AREA_TOLERANCE = 0.10; // 10% band for VA% warning
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Instrument Config Fields
        private TimeSpan rthOpen;
        private TimeSpan rthClose;
        private TimeSpan orStart;
        private TimeSpan orEnd;
        private bool     instrumentHasOR;
        private string   instrumentKey = "YM";
        private double   defaultConfluenceTicks = 8;
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region State / Session Fields
        private DateTime                   lastDrawnDate   = DateTime.MinValue;
        private string                     dateTag         = "";
        private bool                       orComplete      = false;
        private Dictionary<double, double> dayProfile      = new Dictionary<double, double>();
        private Dictionary<double, double> weekProfile     = new Dictionary<double, double>();
        private List<string>               drawnTags       = new List<string>();
        private DateTime                   priorRTHDate    = DateTime.MinValue;
        private int                        priorDayBars    = 0;
        private int                        overnightBars   = 0;
        private double                     dayTotalVol     = 0;
        private double                     dayProfileAvg   = 0;  // avg vol/level for confluence grading
        private HashSet<string>            swingMarkerTags = new HashSet<string>();
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Wall Engine State
        private readonly List<string> wallTags           = new List<string>();
        private double                lastWallBuildPrice = 0;
        private List<Wall>            lastKeptWalls      = new List<Wall>();
        private double                sesHi              = 0;
        private double                sesLo              = 0;
        private bool                         prevAbovePP    = false;
        private List<double>                 nakedPOCPrices = new List<double>();
        private HashSet<string>              nakedPOCTags   = new HashSet<string>();

        private class LevelCandidate
        {
            public double Price;
            public string Code;
            public double Weight;
            public bool   IsAnchor;   // prior-day refs: always shown
            public bool   IsKey;      // HTF refs: shown when within range even if lone
        }

        private class Wall
        {
            public double Hi, Lo, Center, Score;
            public List<LevelCandidate> Members = new List<LevelCandidate>();
            public int Count { get { return Members.Count; } }
        }
        #endregion

        // =================================================================
        #region Parameters â€” Chart Configuration

        [Display(Name = "Chart Role", GroupName = "Chart Configuration", Order = 1,
            Description = "Select the timeframe role for this chart. Controls which levels are drawn and at which tier.")]
        public ChartRole ChartRole { get; set; }

        [Browsable(false)]
        public bool RoleDefaultsApplied { get; set; }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Display

        [Display(Name = "Show Labels", GroupName = "Display", Order = 1)]
        public bool ShowLabels { get; set; }

        [Display(Name = "Show Legend", GroupName = "Display", Order = 2)]
        public bool ShowLegend { get; set; }

        [Display(Name = "Legend Position", GroupName = "Display", Order = 3)]
        public TPMLegendPosition LegendPosition { get; set; }

        [Range(6, 16)]
        [Display(Name = "Label Font Size", GroupName = "Display", Order = 4)]
        public int LabelFontSize { get; set; }

        [Display(Name = "Show Verification Output", GroupName = "Display", Order = 8,
            Description = "Print all calculated level values to the Output window each session.")]
        public bool ShowVerificationOutput { get; set; }

        [Display(Name = "Alert On Bias Flip", GroupName = "Display", Order = 9,
            Description = "Audible alert + vertical line when price crosses PP.")]
        public bool AlertOnBiasFlip { get; set; }

        [Display(Name = "Show Bias Flip Line", GroupName = "Display", Order = 10)]
        public bool ShowBiasFlipLine { get; set; }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Structural Walls

        [Range(1, 15)]
        [Display(Name = "Max Walls Per Side", GroupName = "Structural Walls", Order = 1,
            Description = "Maximum confluence walls drawn above and below price.")]
        public int MaxWallsPerSide { get; set; }

        [Range(1, 5)]
        [Display(Name = "Min Confluence", GroupName = "Structural Walls", Order = 2,
            Description = "Minimum overlapping levels to qualify as a wall. 2 = only show real confluence.")]
        public int MinConfluence { get; set; }

        [Range(0.01, 0.50)]
        [Display(Name = "Cluster Tolerance %", GroupName = "Structural Walls", Order = 3,
            Description = "Levels within this percent of price merge into one wall. 0.05 = ~25 YM pts at 51000.")]
        public double ClusterTolerancePercent { get; set; }

        [Range(0.25, 10.0)]
        [Display(Name = "Wall Range %", GroupName = "Structural Walls", Order = 4,
            Description = "Only draw walls within this percent of current price (anchors always show).")]
        public double WallRangePercent { get; set; }

        [Display(Name = "Show Anchor Levels", GroupName = "Structural Walls", Order = 5,
            Description = "Always show prior-day H/L/C and today's POC/VAH/VAL even when not in a wall.")]
        public bool ShowAnchors { get; set; }

        [Range(0.5, 5.0)]
        [Display(Name = "Min R/R Ratio", GroupName = "Structural Walls", Order = 9,
            Description = "R/R ratio in context panel. Check>=ratio, ~>=70%, X=below.")]
        public double MinRRRatio { get; set; }

        [Display(Name = "Show Approach Bands", GroupName = "Structural Walls", Order = 10,
            Description = "Wider transparent zone around *** and **** walls.")]
        public bool ShowApproachBands { get; set; }

        [Range(10, 500)]
        [Display(Name = "Approach Band Width (ticks)", GroupName = "Structural Walls", Order = 11,
            Description = "Approach zone width beyond wall edge in ticks.")]
        public int ApproachBandWidth { get; set; }

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
        [Display(Name = "Anchor Level Color", GroupName = "Structural Walls", Order = 8)]
        public Brush AnchorColor { get; set; }
        [Browsable(false)]
        public string AnchorColorSerializable
        { get { return Serialize.BrushToString(AnchorColor); } set { AnchorColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Swing Detection

        [Range(1, 30)]
        [Display(Name = "Swing Strength", GroupName = "Swing Detection", Order = 1,
            Description = "Number of bars on each side a bar must dominate to qualify as a swing high/low. Default 3.")]
        public int SwingStrength { get; set; }

        [Display(Name = "Show Swing Markers", GroupName = "Swing Detection", Order = 2,
            Description = "Dot + short dotted line at each confirmed swing pivot, scanned on every bar (including overnight). Replaces a standalone Swing indicator.")]
        public bool ShowSwingMarkers { get; set; }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Visibility Toggles

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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Volume Profile

        [Range(50, 90)]
        [Display(Name = "Value Area %", GroupName = "Volume Profile", Order = 1,
            Description = "Percentage of total volume to include in the value area. Default 70.")]
        public double ValueAreaPercent { get; set; }

        [Display(Name = "Show Naked POCs", GroupName = "Volume Profile", Order = 2,
            Description = "Dashed magenta lines at prior session POCs not yet visited.")]
        public bool ShowNakedPOC { get; set; }

        [Range(1, 20)]
        [Display(Name = "Naked POC Lookback (days)", GroupName = "Volume Profile", Order = 3)]
        public int NakedPOCLookback { get; set; }

        [Range(1, 100)]
        [Display(Name = "Naked POC Visit Threshold (ticks)", GroupName = "Volume Profile", Order = 4,
            Description = "Ticks from a naked POC to count as visited.")]
        public int NakedPOCVisitThreshold { get; set; }

        [Display(Name = "Show HVOL Bands", GroupName = "Volume Profile", Order = 5,
            Description = "Shade high-volume zones as bands instead of HVOL text label.")]
        public bool ShowHVOLBands { get; set; }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Daily Levels

        [XmlIgnore]
        [Display(Name = "Yesterday High Color", GroupName = "Colors â€” Daily Levels", Order = 1)]
        public Brush YHColor { get; set; }
        [Browsable(false)]
        public string YHColorSerializable
        { get { return Serialize.BrushToString(YHColor); } set { YHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Yesterday Low Color", GroupName = "Colors â€” Daily Levels", Order = 2)]
        public Brush YLColor { get; set; }
        [Browsable(false)]
        public string YLColorSerializable
        { get { return Serialize.BrushToString(YLColor); } set { YLColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Yesterday Close Color", GroupName = "Colors â€” Daily Levels", Order = 3)]
        public Brush YCColor { get; set; }
        [Browsable(false)]
        public string YCColorSerializable
        { get { return Serialize.BrushToString(YCColor); } set { YCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Current Week High Color", GroupName = "Colors â€” Daily Levels", Order = 4)]
        public Brush CWHColor { get; set; }
        [Browsable(false)]
        public string CWHColorSerializable
        { get { return Serialize.BrushToString(CWHColor); } set { CWHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Current Week Low Color", GroupName = "Colors â€” Daily Levels", Order = 5)]
        public Brush CWLColor { get; set; }
        [Browsable(false)]
        public string CWLColorSerializable
        { get { return Serialize.BrushToString(CWLColor); } set { CWLColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Overnight

        [XmlIgnore]
        [Display(Name = "Overnight High Color", GroupName = "Colors â€” Overnight", Order = 1)]
        public Brush ONHColor { get; set; }
        [Browsable(false)]
        public string ONHColorSerializable
        { get { return Serialize.BrushToString(ONHColor); } set { ONHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Overnight Low Color", GroupName = "Colors â€” Overnight", Order = 2)]
        public Brush ONLColor { get; set; }
        [Browsable(false)]
        public string ONLColorSerializable
        { get { return Serialize.BrushToString(ONLColor); } set { ONLColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Volume Profile

        [XmlIgnore]
        [Display(Name = "Prior Day POC Color", GroupName = "Colors â€” Volume Profile", Order = 1)]
        public Brush POCColor { get; set; }
        [Browsable(false)]
        public string POCColorSerializable
        { get { return Serialize.BrushToString(POCColor); } set { POCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Day VAH Color", GroupName = "Colors â€” Volume Profile", Order = 2)]
        public Brush VAHColor { get; set; }
        [Browsable(false)]
        public string VAHColorSerializable
        { get { return Serialize.BrushToString(VAHColor); } set { VAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Day VAL Color", GroupName = "Colors â€” Volume Profile", Order = 3)]
        public Brush VALColor { get; set; }
        [Browsable(false)]
        public string VALColorSerializable
        { get { return Serialize.BrushToString(VALColor); } set { VALColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly POC Color", GroupName = "Colors â€” Volume Profile", Order = 4)]
        public Brush WeeklyPOCColor { get; set; }
        [Browsable(false)]
        public string WeeklyPOCColorSerializable
        { get { return Serialize.BrushToString(WeeklyPOCColor); } set { WeeklyPOCColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly VAH Color", GroupName = "Colors â€” Volume Profile", Order = 5)]
        public Brush WeeklyVAHColor { get; set; }
        [Browsable(false)]
        public string WeeklyVAHColorSerializable
        { get { return Serialize.BrushToString(WeeklyVAHColor); } set { WeeklyVAHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Weekly VAL Color", GroupName = "Colors â€” Volume Profile", Order = 6)]
        public Brush WeeklyVALColor { get; set; }
        [Browsable(false)]
        public string WeeklyVALColorSerializable
        { get { return Serialize.BrushToString(WeeklyVALColor); } set { WeeklyVALColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Pivots

        [XmlIgnore]
        [Display(Name = "Pivot Point (PP) Color", GroupName = "Colors â€” Pivots", Order = 1)]
        public Brush PPColor { get; set; }
        [Browsable(false)]
        public string PPColorSerializable
        { get { return Serialize.BrushToString(PPColor); } set { PPColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R1 Color", GroupName = "Colors â€” Pivots", Order = 2)]
        public Brush R1Color { get; set; }
        [Browsable(false)]
        public string R1ColorSerializable
        { get { return Serialize.BrushToString(R1Color); } set { R1Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R2 Color", GroupName = "Colors â€” Pivots", Order = 3)]
        public Brush R2Color { get; set; }
        [Browsable(false)]
        public string R2ColorSerializable
        { get { return Serialize.BrushToString(R2Color); } set { R2Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "R3 Color", GroupName = "Colors â€” Pivots", Order = 4)]
        public Brush R3Color { get; set; }
        [Browsable(false)]
        public string R3ColorSerializable
        { get { return Serialize.BrushToString(R3Color); } set { R3Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S1 Color", GroupName = "Colors â€” Pivots", Order = 5)]
        public Brush S1Color { get; set; }
        [Browsable(false)]
        public string S1ColorSerializable
        { get { return Serialize.BrushToString(S1Color); } set { S1Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S2 Color", GroupName = "Colors â€” Pivots", Order = 6)]
        public Brush S2Color { get; set; }
        [Browsable(false)]
        public string S2ColorSerializable
        { get { return Serialize.BrushToString(S2Color); } set { S2Color = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "S3 Color", GroupName = "Colors â€” Pivots", Order = 7)]
        public Brush S3Color { get; set; }
        [Browsable(false)]
        public string S3ColorSerializable
        { get { return Serialize.BrushToString(S3Color); } set { S3Color = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Opening Range

        [XmlIgnore]
        [Display(Name = "Opening Range High Color", GroupName = "Colors â€” Opening Range", Order = 1)]
        public Brush ORHighColor { get; set; }
        [Browsable(false)]
        public string ORHighColorSerializable
        { get { return Serialize.BrushToString(ORHighColor); } set { ORHighColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Opening Range Low Color", GroupName = "Colors â€” Opening Range", Order = 2)]
        public Brush ORLowColor { get; set; }
        [Browsable(false)]
        public string ORLowColorSerializable
        { get { return Serialize.BrushToString(ORLowColor); } set { ORLowColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Weekly Levels

        [XmlIgnore]
        [Display(Name = "Prior Week High Color", GroupName = "Colors â€” Weekly Levels", Order = 1)]
        public Brush PWHColor { get; set; }
        [Browsable(false)]
        public string PWHColorSerializable
        { get { return Serialize.BrushToString(PWHColor); } set { PWHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Week Low Color", GroupName = "Colors â€” Weekly Levels", Order = 2)]
        public Brush PWLColor { get; set; }
        [Browsable(false)]
        public string PWLColorSerializable
        { get { return Serialize.BrushToString(PWLColor); } set { PWLColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Monthly Levels

        [XmlIgnore]
        [Display(Name = "Prior Month High Color", GroupName = "Colors â€” Monthly Levels", Order = 1)]
        public Brush PMHColor { get; set; }
        [Browsable(false)]
        public string PMHColorSerializable
        { get { return Serialize.BrushToString(PMHColor); } set { PMHColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Prior Month Low Color", GroupName = "Colors â€” Monthly Levels", Order = 2)]
        public Brush PMLColor { get; set; }
        [Browsable(false)]
        public string PMLColorSerializable
        { get { return Serialize.BrushToString(PMLColor); } set { PMLColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Colors: Swing Levels

        [XmlIgnore]
        [Display(Name = "Swing High Color", GroupName = "Colors â€” Swing Levels", Order = 1)]
        public Brush SwingHighColor { get; set; }
        [Browsable(false)]
        public string SwingHighColorSerializable
        { get { return Serialize.BrushToString(SwingHighColor); } set { SwingHighColor = Serialize.StringToBrush(value); } }

        [XmlIgnore]
        [Display(Name = "Swing Low Color", GroupName = "Colors â€” Swing Levels", Order = 2)]
        public Brush SwingLowColor { get; set; }
        [Browsable(false)]
        public string SwingLowColorSerializable
        { get { return Serialize.BrushToString(SwingLowColor); } set { SwingLowColor = Serialize.StringToBrush(value); } }
        #endregion

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        #region Parameters â€” Manual 4H Swing Levels

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

                // Display
                ShowLabels                  = true;
                ShowLegend                  = true;
                LegendPosition              = TPMLegendPosition.TopLeft;
                LabelFontSize               = 9;
                ShowVerificationOutput      = false;

                // Structural Walls
                MaxWallsPerSide             = 5;
                MinConfluence               = 2;
                ClusterTolerancePercent     = 0.05;
                WallRangePercent            = 1.5;
                ShowAnchors                 = true;

                // Swing
                SwingStrength               = 3;
                ShowSwingMarkers            = true;
                RoleDefaultsApplied         = false;

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

                // Default colors â€” Daily
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
                // Structural Walls
                ResistanceColor             = Brushes.Crimson;
                SupportColor                = Brushes.LimeGreen;
                AnchorColor                 = Brushes.Silver;
                MinRRRatio                  = 1.5;
                ShowApproachBands           = true;
                ApproachBandWidth           = 100;
                AlertOnBiasFlip             = true;
                ShowBiasFlipLine            = true;
                ShowNakedPOC                = true;
                NakedPOCLookback            = 5;
                NakedPOCVisitThreshold      = 10;
                ShowHVOLBands               = true;
            }
            else if (State == State.Configure)
            {
                // Detect instrument and configure session times
                ConfigureInstrument();

                if (!RoleDefaultsApplied)
                {
                    ApplyRoleDefaults();
                    RoleDefaultsApplied = true;
                }
            }
            else if (State == State.DataLoaded)
            {
                lastDrawnDate  = DateTime.MinValue;
                dateTag        = "";
                orComplete     = false;
                dayProfile     = new Dictionary<double, double>();
                weekProfile    = new Dictionary<double, double>();
                drawnTags      = new List<string>();
                wallTags.Clear();
                lastWallBuildPrice = 0;
                prevAbovePP        = false;
                nakedPOCPrices.Clear();
                nakedPOCTags.Clear();
                swingMarkerTags.Clear();
                ResetLevels();
            }
            else if (State == State.Terminated)
            {
                ClearWalls();
                foreach (string t in drawnTags)
                    try { RemoveDrawObject(t); } catch { }
                try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { }
                foreach (string t in nakedPOCTags)
                    try { RemoveDrawObject(t); } catch { }
                foreach (string t in swingMarkerTags)
                    try { RemoveDrawObject(t); } catch { }
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

        // Applied once per instance (State.Configure) so saved templates keep any later manual edits.
        private void ApplyRoleDefaults()
        {
            switch (ChartRole)
            {
                case ChartRole.FourHour:
                    SwingStrength    = 3;
                    break;
                case ChartRole.OneHour:
                    SwingStrength    = 10;
                    break;
                case ChartRole.FifteenMinute:
                    SwingStrength    = 20;
                    break;
                case ChartRole.ThreeMinute:
                case ChartRole.OneMinute:
                    ShowSwingMarkers = false;
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

            // Runs on every bar (including overnight) so it replaces a standalone Swing indicator.
            CheckSwingMarkers();

            TimeSpan barTime = Time[0].TimeOfDay;
            DateTime barDate = Time[0].Date;

            bool isRTH = IsRTHBar(barTime);
            if (!isRTH) return;

            // â”€â”€ SESSION START: draw all levels once per calendar date â”€â”€
            if (barDate != lastDrawnDate)
            {
                lastDrawnDate = barDate;
                dateTag       = barDate.ToString("yyyyMMdd");

                // Clear any stale draw objects from a prior run of today's session
                ClearTagsForDate(dateTag);

                // Reset dynamic OR state
                sesHi      = 0;
                sesLo      = 0;
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

                // Seed session extremes before drawing so slow timeframes
                // (4H OnBarClose) show correct values on the first draw
                if (High[0] > sesHi) sesHi = High[0];
                if (sesLo == 0 || Low[0] < sesLo) sesLo = Low[0];
                if (lvlPP > 0) prevAbovePP = Close[0] >= lvlPP;
                CalcNakedPOCs(barDate);
                BuildAndDrawWalls();
            }

            // Track today's RTH session extremes for the context panel
            if (High[0] > sesHi) sesHi = High[0];
            if (sesLo == 0 || Low[0] < sesLo) sesLo = Low[0];
            UpdateCurrentWeekExtremes();

            // â”€â”€ Rebuild walls when price drifts, so R/S sides stay correct as price moves â”€â”€
            if (lastWallBuildPrice > 0
                && Math.Abs(Close[0] - lastWallBuildPrice) > GetClusterTolerance() * 0.5)
                BuildAndDrawWalls();

            // â”€â”€ OPENING RANGE TRACKING â”€â”€
            // Bias flip detection (improvement 8)
            if (AlertOnBiasFlip && lvlPP > 0)
            {
                bool abovePP = Close[0] >= lvlPP;
                if (abovePP != prevAbovePP)
                {
                    Alert("BiasFlip", Priority.High,
                        "BIAS FLIP: " + (abovePP ? "ABOVE PP " : "BELOW PP ") + FormatPrice(lvlPP),
                        "Alert2.wav", 60, Brushes.Yellow, Brushes.Black);
                    if (ShowBiasFlipLine)
                    {
                        string flipTag = TAG_PREFIX + "FLIP_" + CurrentBar;
                        try
                        {
                            Draw.VerticalLine(this, flipTag, 0,
                                abovePP ? Brushes.Cyan : Brushes.OrangeRed, DashStyleHelper.Dash, 2);
                            if (!drawnTags.Contains(flipTag)) drawnTags.Add(flipTag);
                        }
                        catch { }
                    }
                }
                prevAbovePP = abovePP;
            }

            if (instrumentHasOR && !orComplete)
            {
                if (barTime >= orStart && barTime < orEnd)
                {
                    if (lvlORH == 0 || High[0] > lvlORH) lvlORH = High[0];
                    if (lvlORL == 0 || Low[0]  < lvlORL) lvlORL = Low[0];
                }
                if (barTime >= orEnd && lvlORH > 0 && lvlORL > 0)
                {
                    orComplete = true;
                    BuildAndDrawWalls();
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

        // â”€â”€ Prior Day RTH Levels + Volume Profile â”€â”€
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

        // â”€â”€ Pivot Points (standard floor formula) â”€â”€
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

        // â”€â”€ Overnight High / Low â”€â”€
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
                    break; // reached yesterday's RTH â€” stop
                else if (bd < priorRTHDate)
                    break;
            }
        }

        // â”€â”€ Weekly Levels (prior complete week + current week) â”€â”€
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

        // â”€â”€ Monthly Levels (prior complete calendar month) â”€â”€
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

        // â”€â”€ Auto-detect 4H Swing Highs/Lows using SwingStrength â”€â”€
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

        // â”€â”€ Update CWH / CWL dynamically each bar â”€â”€
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
        #region Swing Markers

        // Confirms a pivot once SwingStrength bars exist dominantly on both sides.
        // Independent of the wall engine's lvlSwingH/lvlSwingL (top-3 confluence inputs) — 
        // this marks every historical pivot, purely as a visual replacement for a standalone Swing indicator.
        private void CheckSwingMarkers()
        {
            if (!ShowSwingMarkers) return;
            int strength = SwingStrength;
            if (CurrentBar < strength * 2) return;

            bool isSwingH = true, isSwingL = true;
            for (int k = 1; k <= strength; k++)
            {
                if (High[strength] <= High[strength - k] || High[strength] <= High[strength + k]) isSwingH = false;
                if (Low[strength]  >= Low[strength - k]  || Low[strength]  >= Low[strength + k])  isSwingL = false;
            }

            if (isSwingH) DrawSwingMarker(true,  strength, High[strength]);
            if (isSwingL) DrawSwingMarker(false, strength, Low[strength]);
        }

        private void DrawSwingMarker(bool isHigh, int barsAgo, double price)
        {
            string tag = TAG_PREFIX + (isHigh ? "SWH_" : "SWL_") + (CurrentBar - barsAgo);
            if (swingMarkerTags.Contains(tag)) return;

            Brush color   = isHigh ? Brushes.DodgerBlue : Brushes.HotPink;
            int    leftBA  = barsAgo + 2;
            int    rightBA = Math.Max(0, barsAgo - 2);
            try
            {
                Draw.Dot(this, tag, false, barsAgo, price, color);
                Draw.Line(this, tag + "_L", false, leftBA, price, rightBA, price,
                    color, DashStyleHelper.Dot, 1);
                swingMarkerTags.Add(tag);
                swingMarkerTags.Add(tag + "_L");
            }
            catch (Exception ex) { Print(LOG_PREFIX + " Swing marker error: " + ex.Message); }
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
        #region Drawing Primitives

        // Core draw method
        private void DrawTier(string tag, double price, Brush color, DashStyleHelper dash,
                               int width, string lbl, int tier)
        {
            if (price == 0) return;

            Brush lineColor  = tier == 1 ? color : WithOpacity(color, tier == 2 ? 0.70 : 0.50);
            Brush labelColor = lineColor;

            try
            {
                Draw.HorizontalLine(this, tag, false, price, lineColor, dash, width);
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
                    // White text on a semi-transparent dark background for readability
                    Draw.Text(this, labelTag, true, text, 0, price, 0,
                        Brushes.White,
                        new SimpleFont("Arial", fontSize),
                        System.Windows.TextAlignment.Left,
                        Brushes.Transparent,
                        WithOpacity(Brushes.Black, 0.65),
                        60);
                    if (!drawnTags.Contains(labelTag)) drawnTags.Add(labelTag);
                }
                catch (Exception ex)
                {
                    Print(LOG_PREFIX + " DrawText error [" + labelTag + "]: " + ex.Message);
                }
            }
        }

        // Opacity helper â€” clones a SolidColorBrush at given alpha
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
        #region Structural Wall Engine

        private double GetClusterTolerance()
        {
            double basePrice = Close[0] > 0 ? Close[0] : (lvlPOC > 0 ? lvlPOC : 1);
            double tol = basePrice * (ClusterTolerancePercent / 100.0);
            return Math.Max(tol, TickSize * 4);
        }

        private void ClearWalls()
        {
            foreach (string t in wallTags)
                try { RemoveDrawObject(t); } catch { }
            wallTags.Clear();
        }

        private void RegWall(string tag) { if (!wallTags.Contains(tag)) wallTags.Add(tag); }

        private void GatherCandidates(List<LevelCandidate> c)
        {
            void Add(double p, string code, double w, bool anchor, bool key)
            { if (p > 0) c.Add(new LevelCandidate { Price = p, Code = code, Weight = w, IsAnchor = anchor, IsKey = key }); }

            // Anchors â€” prior-day refs, always shown (YC has no toggle, always on)
            if (ShowYH)  Add(lvlYH,  "YH",  2.5, true,  false);
            if (ShowYL)  Add(lvlYL,  "YL",  2.5, true,  false);
            Add(lvlYC,  "YC",  1.5, true,  false);
            if (ShowPOC) Add(lvlPOC, "POC", 3.0, true,  false);
            if (ShowVAH) Add(lvlVAH, "VAH", 2.0, true,  false);
            if (ShowVAL) Add(lvlVAL, "VAL", 2.0, true,  false);

            // Overnight
            if (ShowONH) Add(lvlONH, "ONH", 1.5, false, false);
            if (ShowONL) Add(lvlONL, "ONL", 1.5, false, false);
            // Pivots
            if (ShowPivots)
            {
                Add(lvlPP, "PP", 1.5, false, false);
                Add(lvlR1, "R1", 1.2, false, false);
                Add(lvlS1, "S1", 1.2, false, false);
            }
            if (ShowR2S2)
            {
                Add(lvlR2, "R2", 1.0, false, true);
                Add(lvlS2, "S2", 1.0, false, true);
            }
            if (ShowR3S3)
            {
                Add(lvlR3, "R3", 0.8, false, true);
                Add(lvlS3, "S3", 0.8, false, true);
            }
            // Weekly â€” PWH/PWL/WPOC are key (show lone when near price)
            if (ShowWeeklyLevels)
            {
                Add(lvlPWH,     "PWH",  2.5, false, true);
                Add(lvlPWL,     "PWL",  2.5, false, true);
                Add(lvlWeekPOC, "WPOC", 2.5, false, true);
                Add(lvlWeekVAH, "WVAH", 2.0, false, false);
                Add(lvlWeekVAL, "WVAL", 2.0, false, false);
            }
            // Monthly â€” key
            if (ShowMonthlyLevels)
            {
                Add(lvlPMH, "PMH", 2.0, false, true);
                Add(lvlPML, "PML", 2.0, false, true);
            }
            // Opening range (once complete)
            if (orComplete && ShowOR) { Add(lvlORH, "ORH", 1.2, false, false); Add(lvlORL, "ORL", 1.2, false, false); }
            // 4H swings
            if (ShowSwingLevels)
                for (int k = 0; k < 3; k++)
                {
                    Add(lvlSwingH[k], "SH" + (k + 1), 1.5, false, false);
                    Add(lvlSwingL[k], "SL" + (k + 1), 1.5, false, false);
                }
            // Range extremes: overall RTH high/low across the lookback window.
            // Ensures a reference wall exists below AND above price on big breakout days
            // when all session structure has been breached (e.g. flash crash below S3/YL).
            {
                int    span = Math.Min(CurrentBar, MAX_LOOKBACK_DAILY);
                double rHi  = double.MinValue;
                double rLo  = double.MaxValue;
                for (int i = 0; i < span; i++)
                {
                    if (!IsRTHBar(Time[i].TimeOfDay)) continue;
                    if (High[i] > rHi) rHi = High[i];
                    if (Low[i]  < rLo) rLo = Low[i];
                }
                if (rHi > double.MinValue) Add(rHi, "RH", 1.5, true,  false);
                if (rLo < double.MaxValue) Add(rLo, "RL", 1.5, true,  false);
            }
        }

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

        private bool WallHasAnchor(Wall w) => ShowAnchors && w.Members.Any(m => m.IsAnchor);
        private bool WallHasKey(Wall w)    => w.Members.Any(m => m.IsKey);
        private bool WallAlwaysKeep(Wall w) => WallHasAnchor(w) || WallHasKey(w);

        // Keep all anchor/key zones; cap pure confluence walls at MaxWallsPerSide by score
        private List<Wall> SelectSide(List<Wall> side)
        {
            var result = new List<Wall>();
            result.AddRange(side.Where(WallAlwaysKeep));
            result.AddRange(side.Where(w => !WallAlwaysKeep(w) && w.Count >= MinConfluence)
                                .OrderByDescending(w => w.Score)
                                .Take(MaxWallsPerSide));
            return result;
        }

        private void BuildAndDrawWalls()
        {
            if (CurrentBar < 20) return;
            ClearWalls();
            lastWallBuildPrice = Close[0];

            var cands = new List<LevelCandidate>();
            GatherCandidates(cands);
            if (cands.Count == 0) return;

            double tol      = GetClusterTolerance();
            double price    = Close[0];
            double rangeAbs = price * (WallRangePercent / 100.0);

            var walls = ClusterCandidates(cands, tol);

            var above = new List<Wall>();
            var below = new List<Wall>();
            foreach (var w in walls)
            {
                bool hasAnchor  = WallHasAnchor(w);
                bool hasKey     = WallHasKey(w);
                bool inRange    = Math.Abs(w.Center - price) <= rangeAbs;
                // IsKey walls (S2/S3/R2/R3/PWH/PWL/etc.) get 3x range so they remain
                // visible on volatile days where S2 can be 900+ pts from price.
                bool keyInRange = hasKey && Math.Abs(w.Center - price) <= rangeAbs * 3.0;
                bool keepLone   = hasAnchor || keyInRange;
                if (w.Count < MinConfluence && !keepLone) continue;
                if (!inRange && !hasAnchor && !keyInRange) continue;
                if (w.Center >= price) above.Add(w); else below.Add(w);
            }

            var keep = new List<Wall>();
            keep.AddRange(SelectSide(above));
            keep.AddRange(SelectSide(below));

            int idx = 0;
            foreach (var w in keep)
                DrawWall(w, price, idx++);
            lastKeptWalls = keep;
            DrawLegend();
            DrawNakedPOCs();
        }

        private void DrawWall(Wall w, double price, int idx)
        {
            bool  isResistance = w.Center >= price;
            bool  isWall       = w.Count >= 2;
            Brush baseColor    = isWall ? (isResistance ? ResistanceColor : SupportColor) : AnchorColor;

            string tagBand = TAG_PREFIX + "WALL" + idx;
            string tagLine = tagBand + "_C";
            string tagLbl  = tagBand + "_L";

            // Codes sorted by reference priority (improvement 3)
            var codes = w.Members.Select(m => m.Code).ToList();
            var prio = new[] {"WPOC","WVAH","WVAL","PWH","PWL","PMH","PML","ONH","ONL","YH","YL","YC","SH1","SH2","SH3","SL1","SL2","SL3","POC","VAH","VAL","R1","S1","R2","S2","R3","S3","PP","RH","RL","ORH","ORL"};
            codes = codes.OrderBy(c => { int pi = Array.IndexOf(prio, c); return pi < 0 ? 99 : pi; }).ToList();
            string codeStr = codes.Count <= 4
                ? string.Join("+", codes)
                : string.Join("+", codes.Take(4)) + "+" + (codes.Count - 4);

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

                if (ShowApproachBands && w.Count >= 3)
                {
                    double bandPts     = ApproachBandWidth * TickSize;
                    string tagApproach = tagBand + "_A";
                    Brush  aFill       = WithOpacity(baseColor, 0.08);
                    try
                    {
                        Draw.RegionHighlightY(this, tagApproach, false,
                            w.Hi + bandPts, w.Lo - bandPts, Brushes.Transparent, aFill, 8);
                        RegWall(tagApproach);
                    }
                    catch (Exception ex) { Print(LOG_PREFIX + " Approach band error: " + ex.Message); }
                }
            }
            else
            {
                // Lone anchor â€” quiet dotted reference line
                try
                {
                    Draw.HorizontalLine(this, tagLine, false, w.Center,
                        WithOpacity(baseColor, 0.55), DashStyleHelper.Dot, 1);
                    RegWall(tagLine);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Anchor line error: " + ex.Message); }
            }

            // HVOL band (improvement 11)
            bool isHvol = false;
            if (isWall && dayProfileAvg > 0)
            {
                double zv  = GetZoneVolume(w.Lo, w.Hi);
                int    n   = (int)Math.Round(Math.Abs(w.Hi - w.Lo) / TickSize) + 1;
                double za  = n > 0 ? zv / n : 0;
                isHvol     = (zv > 0 && za >= dayProfileAvg);
            }
            if (ShowHVOLBands && isHvol)
            {
                string tagHvol = tagBand + "_HV";
                Brush  hFill   = WithOpacity(Brushes.Gold, 0.12);
                Brush  hBorder = WithOpacity(Brushes.Gold, 0.55);
                try
                {
                    Draw.RegionHighlightY(this, tagHvol, false,
                        w.Hi + TickSize, w.Lo - TickSize, hBorder, hFill, 12);
                    RegWall(tagHvol);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " HVOL band error: " + ex.Message); }
            }

            if (ShowLabels)
            {
                string stars   = w.Count >= 4 ? " ****" : w.Count == 3 ? " ***" : w.Count == 2 ? " **" : "";
                string side    = isResistance ? "R" : "S";
                string hvolTxt = (!ShowHVOLBands && isHvol) ? "  HVOL" : "";
                string text    = side + " " + FormatPrice(w.Center) + stars + "  " + codeStr + hvolTxt;
                Brush  txt  = isWall ? Brushes.White : WithOpacity(Brushes.White, 0.75);
                int    fs   = isWall ? Math.Max(8, LabelFontSize) : Math.Max(7, LabelFontSize - 2);
                try
                {
                    Draw.Text(this, tagLbl, true, text, -5, w.Center, 0,
                        txt, new SimpleFont("Arial", fs),
                        System.Windows.TextAlignment.Left,
                        Brushes.Transparent, WithOpacity(Brushes.Black, 0.70), 70);
                    RegWall(tagLbl);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " Wall label error: " + ex.Message); }
            }
        }

        private void CalcNakedPOCs(DateTime today)
        {
            nakedPOCPrices.Clear();
            if (!ShowNakedPOC || NakedPOCLookback <= 0) return;
            int maxBars = Math.Min(CurrentBar, MAX_LOOKBACK_DAILY);
            double visitThresh = NakedPOCVisitThreshold * TickSize;
            var sessions = new List<(DateTime date, double poc)>();
            DateTime curDate = DateTime.MinValue;
            var sesProfile = new Dictionary<double, double>();
            double sesTotalVol = 0;
            int daysFound = 0;
            for (int i = 1; i < maxBars && daysFound < NakedPOCLookback; i++)
            {
                DateTime bd = Time[i].Date;
                if (bd >= today) continue;
                if (!IsRTHBar(Time[i].TimeOfDay)) continue;
                if (bd != curDate)
                {
                    if (curDate != DateTime.MinValue && sesProfile.Count > 0)
                    {
                        double poc, vah, val;
                        CalcValueArea(sesProfile, sesTotalVol, out poc, out vah, out val);
                        if (poc > 0) { sessions.Add((curDate, poc)); daysFound++; }
                    }
                    curDate = bd; sesProfile.Clear(); sesTotalVol = 0;
                }
                AddToProfile(sesProfile, High[i], Low[i], Volume[i]);
                sesTotalVol += Volume[i];
            }
            if (curDate != DateTime.MinValue && sesProfile.Count > 0 && daysFound < NakedPOCLookback)
            {
                double poc, vah, val;
                CalcValueArea(sesProfile, sesTotalVol, out poc, out vah, out val);
                if (poc > 0) sessions.Add((curDate, poc));
            }
            foreach (var (sDate, poc) in sessions)
            {
                bool visited = false;
                if (sesHi > 0 && sesLo > 0 && sesLo <= poc + visitThresh && sesHi >= poc - visitThresh)
                    visited = true;
                else
                {
                    for (int i = 1; i < maxBars && !visited; i++)
                    {
                        DateTime bd = Time[i].Date;
                        if (bd >= today || bd <= sDate) continue;
                        if (!IsRTHBar(Time[i].TimeOfDay)) continue;
                        if (Low[i] <= poc + visitThresh && High[i] >= poc - visitThresh) visited = true;
                    }
                }
                if (!visited) nakedPOCPrices.Add(poc);
            }
        }

        private void DrawNakedPOCs()
        {
            foreach (string t in nakedPOCTags.ToList())
                try { RemoveDrawObject(t); } catch { }
            nakedPOCTags.Clear();
            if (!ShowNakedPOC || nakedPOCPrices.Count == 0) return;
            double visitThresh = NakedPOCVisitThreshold * TickSize;
            for (int idx = nakedPOCPrices.Count - 1; idx >= 0; idx--)
            {
                double poc = nakedPOCPrices[idx];
                if (sesHi > 0 && sesLo > 0 && sesLo <= poc + visitThresh && sesHi >= poc - visitThresh)
                { nakedPOCPrices.RemoveAt(idx); continue; }
                string tag = TAG_PREFIX + "NPOC_" + idx;
                try
                {
                    Draw.HorizontalLine(this, tag, false, poc,
                        WithOpacity(Brushes.Magenta, 0.80), DashStyleHelper.Dash, 1);
                    nakedPOCTags.Add(tag);
                }
                catch (Exception ex) { Print(LOG_PREFIX + " NakedPOC error: " + ex.Message); }
            }
        }

        #endregion

        // =================================================================
        #region Legend

        private void DrawLegend()
        {
            if (!ShowLegend) { try { RemoveDrawObject(TAG_PREFIX + "LEGEND"); } catch { } return; }

            double price   = Close[0];
            string ppBias  = lvlPP > 0 ? (price >= lvlPP ? "ABOVE PP" : "BELOW PP") : "PP n/a";
            string ycBias  = lvlYC > 0 ? (price >= lvlYC ? "ABOVE YC" : "BELOW YC") : "YC n/a";
            string biasArr = price >= lvlPP ? "↑" : "↓";

            string[] prio = new string[] {"WPOC","WVAH","WVAL","PWH","PWL","PMH","PML","ONH","ONL","YH","YL","YC","SH1","SH2","SH3","SL1","SL2","SL3","POC","VAH","VAL","R1","S1","R2","S2","R3","S3","PP","RH","RL","ORH","ORL"};

            var aboveWalls = lastKeptWalls.Where(w => w.Center >= price).OrderBy(w => w.Center).Take(3).ToList();
            var belowWalls = lastKeptWalls.Where(w => w.Center <  price).OrderByDescending(w => w.Center).Take(3).ToList();
            int rCount     = lastKeptWalls.Count(w => w.Center >= price);
            int sCount     = lastKeptWalls.Count(w => w.Center <  price);

            double rangePts  = 500.0;
            int    rCount500 = lastKeptWalls.Count(w => w.Center >= price && w.Center <= price + rangePts);
            int    sCount500 = lastKeptWalls.Count(w => w.Center <  price && w.Center >= price - rangePts);
            string rDensity  = rCount500 == 0 ? "CLEAR" : rCount500 >= 4 ? "STACKED" : rCount500 == 1 ? "WIDE" : "NORMAL";
            string sDensity  = sCount500 == 0 ? "CLEAR" : sCount500 >= 4 ? "STACKED" : sCount500 == 1 ? "WIDE" : "NORMAL";

            string rrLine;
            if (aboveWalls.Count > 0 && belowWalls.Count > 0)
            {
                double rDist = aboveWalls[0].Center - price;
                double sDist = price - belowWalls[0].Center;
                double rr    = sDist > 0 ? rDist / sDist : 0;
                string rrSym = rr >= MinRRRatio ? "[OK]" : rr >= MinRRRatio - 0.5 ? "[~]" : "[X]";
                rrLine = string.Format("  R/R {0:F2}x {1}  (R:+{2}  S:-{3} pts)", rr, rrSym, FormatPrice(rDist), FormatPrice(sDist));
            }
            else rrLine = "  R/R  n/a (no walls)";

            double sesRange = (sesHi > 0 && sesLo > 0) ? sesHi - sesLo : 0;
            string hiStr    = sesHi > 0 ? FormatPrice(sesHi) : "---";
            string loStr    = sesLo > 0 ? FormatPrice(sesLo) : "---";
            string rangeStr = sesRange > 0 ? FormatPrice(sesRange) + " pts" : "---";

            TimeSpan now = Time[0].TimeOfDay;
            string phase =
                now < rthOpen                            ? "PRE-MARKET"    :
                now < rthOpen  + new TimeSpan(0, 30, 0) ? "OPENING (30m)" :
                now < rthOpen  + new TimeSpan(2,  0, 0) ? "MORNING"       :
                now < rthClose - new TimeSpan(1,  0, 0) ? "MID-SESSION"   :
                                                          "CLOSING";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("═══ MARKET CONTEXT ═════════════════════");
            sb.AppendLine("  Bias: " + biasArr + " " + ppBias + "  ·  " + ycBias);
            sb.AppendLine("  ────────────────────────────────────");
            if (aboveWalls.Count == 0)
            {
                sb.AppendLine("  (no resistance walls)");
            }
            else
            {
                for (int ri = 0; ri < aboveWalls.Count; ri++)
                {
                    var wR  = aboveWalls[ri];
                    var csR = wR.Members.Select(m => m.Code).OrderBy(c => { int pi = Array.IndexOf(prio, c); return pi < 0 ? 99 : pi; }).Take(3);
                    sb.AppendLine("  R" + (ri+1) + " " + FormatPrice(wR.Center) + "  [+" + FormatPrice(wR.Center - price) + " pts]  " + string.Join("+", csR));
                }
            }
            sb.AppendLine("  ────────────────────────────────────");
            if (belowWalls.Count == 0)
            {
                sb.AppendLine("  (no support walls)");
            }
            else
            {
                for (int si = 0; si < belowWalls.Count; si++)
                {
                    var wS  = belowWalls[si];
                    var csS = wS.Members.Select(m => m.Code).OrderBy(c => { int pi = Array.IndexOf(prio, c); return pi < 0 ? 99 : pi; }).Take(3);
                    sb.AppendLine("  S" + (si+1) + " " + FormatPrice(wS.Center) + "  [-" + FormatPrice(price - wS.Center) + " pts]  " + string.Join("+", csS));
                }
            }
            sb.AppendLine("  ────────────────────────────────────");
            sb.AppendLine(rrLine);
            sb.AppendLine("  Density: R " + rDensity + "  S " + sDensity);
            sb.AppendLine("  ────────────────────────────────────");
            sb.AppendLine("  Session  Hi:" + hiStr + "  Lo:" + loStr);
            sb.AppendLine("  Range:   " + rangeStr);
            sb.AppendLine("  Phase:   " + phase);
            sb.AppendLine("  ────────────────────────────────────");
            sb.Append    ("  **=cluster  ***=wall  ****=4+stacked");

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
                    Brushes.SteelBlue, WithOpacity(Brushes.Black, 0.85), 90);
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
                Print(LOG_PREFIX + " WARNING: PP=" + lvlPP + " is outside YH-YL range â€” calculation error");

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
                    Print(string.Format("{0} WARNING: Value area is {1:F1}% of total â€” target was {2}%",
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
                    Print(LOG_PREFIX + " WARNING: " + names[n] + " is 0 â€” calculation may have failed");
        }

        private void PrintVerificationOutput(DateTime today)
        {
            Print(string.Format("{0} â•â•â• SESSION: {1} â•â•â•", LOG_PREFIX, today.ToString("yyyy-MM-dd")));
            Print(string.Format("{0} Instrument: {1} | Chart Role: {2} | TickSize: {3}",
                LOG_PREFIX, instrumentKey, ChartRole, TickSize));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ PRIOR DAY LEVELS â”€â”€");
            Print(string.Format("{0}   Prior Session Date: {1}", LOG_PREFIX, priorRTHDate.ToString("yyyy-MM-dd")));
            Print(string.Format("{0}   RTH Bars Found: {1}", LOG_PREFIX, priorDayBars));
            Print(string.Format("{0}   YH = {1}", LOG_PREFIX, lvlYH));
            Print(string.Format("{0}   YL = {1}", LOG_PREFIX, lvlYL));
            Print(string.Format("{0}   YC = {1} (close of last RTH bar)", LOG_PREFIX, lvlYC));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ PIVOT VERIFICATION â”€â”€");
            Print(string.Format("{0}   PP = ({1} + {2} + {3}) / 3 = {4:F2}",
                LOG_PREFIX, lvlYH, lvlYL, lvlYC, lvlPP));
            Print(string.Format("{0}   R1 = (2 Ã— {1:F2}) âˆ’ {2} = {3:F2}", LOG_PREFIX, lvlPP, lvlYL, lvlR1));
            Print(string.Format("{0}   S1 = (2 Ã— {1:F2}) âˆ’ {2} = {3:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlS1));
            Print(string.Format("{0}   R2 = {1:F2} + ({2} âˆ’ {3}) = {4:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlYL, lvlR2));
            Print(string.Format("{0}   S2 = {1:F2} âˆ’ ({2} âˆ’ {3}) = {4:F2}", LOG_PREFIX, lvlPP, lvlYH, lvlYL, lvlS2));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ OVERNIGHT LEVELS â”€â”€");
            Print(string.Format("{0}   Overnight bars scanned: {1}", LOG_PREFIX, overnightBars));
            Print(string.Format("{0}   ONH = {1} | vs YH: {2}", LOG_PREFIX, lvlONH,
                lvlONH > lvlYH ? "ONH > YH â†’ PROMOTED to Tier 1" : "ONH <= YH â†’ standard Tier 2"));
            Print(string.Format("{0}   ONL = {1} | vs YL: {2}", LOG_PREFIX, lvlONL,
                lvlONL < lvlYL ? "ONL < YL â†’ PROMOTED to Tier 1" : "ONL >= YL â†’ standard Tier 2"));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ VOLUME PROFILE (Prior Day) â”€â”€");
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
            Print(LOG_PREFIX + " â”€â”€ WEEKLY LEVELS â”€â”€");
            Print(string.Format("{0}   PWH = {1} | PWL = {2}", LOG_PREFIX, lvlPWH, lvlPWL));
            Print(string.Format("{0}   Weekly POC = {1} | Weekly VAH = {2} | Weekly VAL = {3}",
                LOG_PREFIX, lvlWeekPOC, lvlWeekVAH, lvlWeekVAL));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ MONTHLY LEVELS â”€â”€");
            Print(string.Format("{0}   PMH = {1} | PML = {2}", LOG_PREFIX, lvlPMH, lvlPML));
            Print("");
            Print(LOG_PREFIX + " â”€â”€ TIER ASSIGNMENTS (ChartRole: " + ChartRole + ") â”€â”€");
            PrintTierAssignments();
            Print("");
            Print(LOG_PREFIX + " â•â•â• VERIFICATION COMPLETE â•â•â•");
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

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private ThreePillarsMap[] cacheThreePillarsMap;
		public ThreePillarsMap ThreePillarsMap()
		{
			return ThreePillarsMap(Input);
		}

		public ThreePillarsMap ThreePillarsMap(ISeries<double> input)
		{
			if (cacheThreePillarsMap != null)
				for (int idx = 0; idx < cacheThreePillarsMap.Length; idx++)
					if (cacheThreePillarsMap[idx] != null &&  cacheThreePillarsMap[idx].EqualsInput(input))
						return cacheThreePillarsMap[idx];
			return CacheIndicator<ThreePillarsMap>(new ThreePillarsMap(), input, ref cacheThreePillarsMap);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.ThreePillarsMap ThreePillarsMap()
		{
			return indicator.ThreePillarsMap(Input);
		}

		public Indicators.ThreePillarsMap ThreePillarsMap(ISeries<double> input )
		{
			return indicator.ThreePillarsMap(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.ThreePillarsMap ThreePillarsMap()
		{
			return indicator.ThreePillarsMap(Input);
		}

		public Indicators.ThreePillarsMap ThreePillarsMap(ISeries<double> input )
		{
			return indicator.ThreePillarsMap(input);
		}
	}
}

#endregion
