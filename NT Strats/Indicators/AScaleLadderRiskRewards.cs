#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript
{
    public enum ScaleLadderTradeDirection
    {
        Long,
        Short
    }

    public enum ScaleLadderMode
    {
        ScaleInAtLoss,
        ScaleInAtProfit,
        ScaleOutAtLoss,
        ScaleOutAtProfit
    }

    public enum ScaleLadderSpacingUnit
    {
        Ticks,
        Price
    }

    public enum ScaleLadderProtectionKind
    {
        Ticks,
        ATR,
        Dollars
    }
}

namespace NinjaTrader.NinjaScript.Indicators
{
    public class AScaleLadderRiskRewards : Indicator
    {
        private sealed class LadderLevel
        {
            public int LevelNumber { get; set; }
            public double LevelPrice { get; set; }
            public int QuantityAfterLevel { get; set; }
            public int QuantityDelta { get; set; }
            public double AverageEntryAfterLevel { get; set; }
            public double NetPnlAtLevel { get; set; }
            public double TargetPnl { get; set; }
            public double RealizedPnl { get; set; }
            public double MarketTicksFromAverage { get; set; }
            public double TargetTicksFromAverage { get; set; }
            public double PriceUnitsFromEntry { get; set; }
            public double AverageUnitsFromEntry { get; set; }
        }

        private sealed class LadderValidation
        {
            public LadderValidation()
            {
                Warnings = new List<string>();
            }

            public bool CanRenderLevels { get; set; }
            public bool ScaleOnFavorableSide { get; set; }
            public double SpacingPrice { get; set; }
            public int VisibleLevelCount { get; set; }
            public List<string> Warnings { get; private set; }
        }

        private sealed class SimulatedTradeStep
        {
            public int LadderIndex { get; set; }
            public double FillPrice { get; set; }
            public int PreviousQuantity { get; set; }
            public double PreviousAverageEntry { get; set; }
        }

        private sealed class OrderedLadderEntry
        {
            public int LadderIndex { get; set; }
            public double Price { get; set; }
        }

        private sealed class LadderEditorTextBoxTag
        {
            public int LadderIndex { get; set; }
            public LadderEditorFieldKind FieldKind { get; set; }
        }

        private enum LadderEditorFieldKind
        {
            StopDollars,
            TargetDollars,
            BaseContracts,
            Contracts,
            SpacingTicks
        }

        private const float ColumnWidth = 220f;
        private const float LabelVerticalGap = 18f;
        private const float SummaryLineHeight = 16f;
        private const int ProtectionLabelDefaultBarsAgo = 20;
        private const int ProtectionLabelDefaultTickOffset = 35;
        private const int OverlayButtonZIndex = 2000;
        private const int MinScaleLevelContracts = 1;
        private const int MaxScaleLevelContracts = 1000;
        private const int MinRuntimeScaleLevelCount = 1;
        private const int MaxRuntimeScaleLevelCount = 100;
        private const int MinScaleLevelSpacingTicks = 1;
        private const int MaxScaleLevelSpacingTicks = 10000;
        private const float MeasurementArrowSize = 5f;
        private const float MeasurementLineThickness = 1.25f;
        private const float MeasurementLabelWidth = 116f;
        private const float MeasurementLabelXGap = 6f;
        private const float MeasurementLabelPaddingX = 4f;
        private const float MeasurementLabelPaddingY = 2f;
        private const double MeasurementLabelHitPaddingX = 10.0;
        private const double MeasurementLabelHitPaddingY = 6.0;
        private const double OverlayPanelDefaultWidth = 252.0;
        private const double OverlayPanelDefaultHeight = 228.0;
        private const double OverlayPanelMinWidth = 252.0;
        private const double OverlayPanelMinHeight = 168.0;
        private const double OverlayPanelEdgePadding = 16.0;
        private static readonly Brush OriginalEntryMeasurementBrush = Brushes.MediumPurple;
        private static readonly Brush LadderSpacingMeasurementBrush = Brushes.Gainsboro;
        private static readonly Brush ConsumedLadderReferenceBrush = Brushes.YellowGreen;

        private HorizontalLine entryLine;
        private HorizontalLine stopLine;
        private HorizontalLine targetLine;
        private HorizontalLine originalEntryReferenceLine;
        private HorizontalLine[] ladderLines = new HorizontalLine[0];
        private HorizontalLine[] consumedLadderReferenceLines = new HorizontalLine[0];
        private SimpleFont renderFont;
        private ATR baseAtr;

        private NinjaTrader.NinjaScript.DrawingTools.Text stopLabelObject;
        private NinjaTrader.NinjaScript.DrawingTools.Text targetLabelObject;

        private string entryLineTag;
        private string stopLineTag;
        private string targetLineTag;
        private string stopLabelTag;
        private string targetLabelTag;
        private string originalEntryReferenceLineTag;
        private string tagPrefix;
        private string[] ladderLineTags = new string[0];
        private string[] consumedLadderReferenceTags = new string[0];

        private double workingEntryPrice;
        private double workingStopPrice;
        private double workingTargetPrice;
        private bool anchorsSeeded;
        private readonly Stack<SimulatedTradeStep> simulatedTradeHistory = new Stack<SimulatedTradeStep>();
        private readonly HashSet<int> consumedLadderIndices = new HashSet<int>();
        private readonly List<int> runtimeScaleLevelContracts = new List<int>();
        private readonly List<int> runtimeScaleLevelSpacingTicks = new List<int>();
        private int simulatedQuantity;
        private double simulatedAverageEntry;
        private double originalEntryReferencePrice;

        private DateTime stopLabelAnchorTime = DateTime.MinValue;
        private DateTime targetLabelAnchorTime = DateTime.MinValue;
        private double stopLabelPriceOffset = double.NaN;
        private double targetLabelPriceOffset = double.NaN;
        private double stopLabelRenderedPrice;
        private double targetLabelRenderedPrice;
        private string stopLabelRenderedText;
        private string targetLabelRenderedText;

        private Panel chartOverlayHost;
        private Canvas overlayCanvas;
        private Border overlayButtonBorder;
        private Grid overlayChromeGrid;
        private StackPanel overlayButtonPanel;
        private StackPanel ladderEditorRowsPanel;
        private Thumb overlayDragThumb;
        private Thumb overlayTopResizeThumb;
        private Thumb overlayResizeThumb;
        private Button addTradeButton;
        private Button removeTradeButton;
        private Button resetButton;
        private Button addLevelButton;
        private Button removeLevelButton;
        private TextBox stopDollarsTextBox;
        private TextBox targetDollarsTextBox;
        private TextBox baseContractsTextBox;
        private readonly List<TextBox> ladderLevelContractTextBoxes = new List<TextBox>();
        private readonly List<TextBox> ladderLevelSpacingTextBoxes = new List<TextBox>();
        private bool overlayButtonsAdded;
        private bool overlayButtonsInitializing;
        private bool overlayEditorSyncing;
        private bool runtimeScaleLevelsInitialized;
        private bool lastAddTradeEnabled;
        private bool lastRemoveTradeEnabled;
        private bool lastAddLevelEnabled;
        private bool lastRemoveLevelEnabled;
        private int lastRuntimeScaleLevelSignature = int.MinValue;
        private bool overlayPanelPositionInitialized;
        private bool measurementDragHandlersAttached;
        private ChartControl measurementDragHostControl;
        private Rect originalMeasurementLabelBounds = Rect.Empty;
        private readonly List<Rect> spacingMeasurementLabelBounds = new List<Rect>();
        private double originalMeasurementXOffset;
        private double spacingMeasurementXOffset;
        private MeasurementLabelDragTarget activeMeasurementDragTarget = MeasurementLabelDragTarget.None;
        private Point activeMeasurementDragStartPoint;
        private double activeMeasurementDragStartOffset;

        private enum MeasurementLabelDragTarget
        {
            None,
            OriginalMeasurement,
            SpacingMeasurements
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Manual scale ladder planner for visualizing scale-in and scale-out PnL.";
                Name = "AScaleLadderRiskRewards";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsChartOnly = true;
                DisplayInDataBox = false;
                PaintPriceMarkers = false;
                IsSuspendedWhileInactive = true;
                BarsRequiredToPlot = 1;
                ScaleJustification = ScaleJustification.Right;

                TradeDirection = ScaleLadderTradeDirection.Long;
                ScaleMode = ScaleLadderMode.ScaleInAtLoss;
                SpacingUnit = ScaleLadderSpacingUnit.Ticks;
                AtrPeriod = 14;
                StopType = ScaleLadderProtectionKind.Ticks;
                StopValue = 80;
                StopTicks = 80;
                TargetType = ScaleLadderProtectionKind.Ticks;
                TargetValue = 160;
                TargetTicks = 160;
                BaseContracts = 1;
                ContractsPerScaleLevel = 1;
                ScaleLevelCount = 5;
                SpacingValue = 20;
                ShowGuideLines = true;
                ShowLevelLabels = false;
                ShowSummaryPanel = true;
                ResetAnchors = false;

                EntryLineBrush = Brushes.DodgerBlue;
                RiskSideBrush = Brushes.OrangeRed;
                RewardSideBrush = Brushes.DeepSkyBlue;
                SummaryBrush = Brushes.LightGray;
            }
            else if (State == State.DataLoaded)
            {
                renderFont = new SimpleFont("Arial", 12);
                tagPrefix = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}_{2}",
                    Name,
                    Instrument != null ? Instrument.FullName.Replace(' ', '_') : "Instrument",
                    GetHashCode());
                entryLineTag = tagPrefix + "_Entry";
                stopLineTag = tagPrefix + "_Stop";
                targetLineTag = tagPrefix + "_Target";
                stopLabelTag = tagPrefix + "_StopLabel";
                targetLabelTag = tagPrefix + "_TargetLabel";
                originalEntryReferenceLineTag = tagPrefix + "_OriginalEntry";
                NormalizeProtectionInputs();
                simulatedQuantity = Math.Max(1, BaseContracts);
                baseAtr = ATR(Math.Max(2, AtrPeriod));
                ResetRuntimeScaleLevelContractsToDefaults();
            }
            else if (State == State.Historical)
            {
                SetZOrder(1);
            }
            else if (State == State.Terminated)
            {
                RemoveChartOverlayButtons();
                RemoveAnchorLines();
            }
        }

        public override string DisplayName
        {
            get
            {
                return Name;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 0 || ChartControl == null)
                return;

            TryInitializeChartOverlayButtons();
            TryAttachMeasurementDragHandlers();
            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();
            UpdateProtectionVisuals();
            UpdateOverlayButtons();
            UpdateLadderEditorRows();
            ForceRefresh();
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            if (IsInHitTest || RenderTarget == null || ChartPanel == null || ChartBars == null || chartScale == null)
                return;

            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();

            List<LadderLevel> levels;
            LadderValidation validation;
            BuildLadder(out levels, out validation);

            using (var summaryDxBrush = SummaryBrush.ToDxBrush(RenderTarget))
            using (var entryDxBrush = EntryLineBrush.ToDxBrush(RenderTarget))
            using (var riskDxBrush = RiskSideBrush.ToDxBrush(RenderTarget))
            using (var rewardDxBrush = RewardSideBrush.ToDxBrush(RenderTarget))
            using (var mutedDxBrush = Brushes.Gray.ToDxBrush(RenderTarget))
            using (var warningDxBrush = Brushes.Goldenrod.ToDxBrush(RenderTarget))
            using (var originalMeasurementDxBrush = OriginalEntryMeasurementBrush.ToDxBrush(RenderTarget))
            using (var spacingMeasurementDxBrush = LadderSpacingMeasurementBrush.ToDxBrush(RenderTarget))
            using (var measurementLabelBackgroundDxBrush = new SolidColorBrush(Color.FromArgb(236, 8, 12, 18)).ToDxBrush(RenderTarget))
            using (var measurementLabelShadowDxBrush = new SolidColorBrush(Color.FromArgb(220, 0, 0, 0)).ToDxBrush(RenderTarget))
            using (var textFormat = renderFont.ToDirectWriteTextFormat())
            using (var measurementTextFormat = new SimpleFont("Arial", 10) { Bold = true }.ToDirectWriteTextFormat())
            {
                float panelLeft = ChartPanel.X;
                float panelTop = ChartPanel.Y;
                float panelRight = ChartPanel.X + ChartPanel.W;
                float lastVisibleBarX = chartControl.GetXByBarIndex(ChartBars, Math.Max(0, ChartBars.ToIndex));
                float lineStartX = panelLeft + 10f;
                float lossColumnX = Math.Min(panelRight - (ColumnWidth * 2f) - 20f, lastVisibleBarX + 32f);
                if (lossColumnX < panelLeft + 20f)
                    lossColumnX = panelLeft + 20f;

                float profitColumnX = Math.Min(panelRight - ColumnWidth - 10f, lossColumnX + ColumnWidth);
                if (profitColumnX < lossColumnX + 100f)
                    profitColumnX = lossColumnX + 100f;

                float guideLineEndX = Math.Max(lineStartX + 40f, lossColumnX - 12f);
                float summaryBottomY = DrawSummaryPanel(textFormat, summaryDxBrush, warningDxBrush, panelLeft + 10f, panelTop + 10f, validation);

                DrawBasePriceLabel(
                    textFormat,
                    entryDxBrush,
                    string.Format(CultureInfo.InvariantCulture, "Entry | Base {0}", GetEffectiveQuantity()),
                    lossColumnX,
                    chartScale.GetYByValue(workingEntryPrice) - 16f);

                bool hasRenderableLevels = validation.CanRenderLevels && levels.Count > 0;
                if (hasRenderableLevels)
                {
                    Brush ladderBrush = GetLadderLineBrush(validation.ScaleOnFavorableSide);
                    using (var ladderDxBrush = ladderBrush.ToDxBrush(RenderTarget))
                    {
                        foreach (var level in levels.OrderBy(l => chartScale.GetYByValue(l.LevelPrice)))
                        {
                            float levelY = chartScale.GetYByValue(level.LevelPrice);
                            if (ShowGuideLines)
                                RenderTarget.DrawLine(new SharpDX.Vector2(lineStartX, levelY), new SharpDX.Vector2(guideLineEndX, levelY), ladderDxBrush, 1f);
                        }
                    }
                }

                RenderMeasurementOverlays(
                    chartScale,
                    measurementTextFormat,
                    originalMeasurementDxBrush,
                    spacingMeasurementDxBrush,
                    measurementLabelBackgroundDxBrush,
                    measurementLabelShadowDxBrush,
                    levels,
                    panelLeft,
                    panelRight,
                    lastVisibleBarX);
            }
        }

        private float DrawSummaryPanel(TextFormat textFormat, SharpDX.Direct2D1.Brush summaryDxBrush, SharpDX.Direct2D1.Brush warningDxBrush, float x, float y, LadderValidation validation)
        {
            if (!ShowSummaryPanel)
                return y;

            string line1 = string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1}",
                TradeDirection,
                ScaleMode);
            string line2 = string.Format(
                CultureInfo.InvariantCulture,
                "Base {0} | Step Qty {1} | Levels {2} | Spacing {3}",
                GetEffectiveQuantity(),
                GetRuntimeScaleLevelContractsSummary(),
                GetRuntimeScaleLevelCount(),
                GetRuntimeScaleLevelSpacingSummary() + " ticks");
            string line3 = string.Format(
                CultureInfo.InvariantCulture,
                "Entry 0.00 | Stop {0} | Target {1}",
                FormatDistanceFromEntry(workingStopPrice - workingEntryPrice),
                FormatDistanceFromEntry(workingTargetPrice - workingEntryPrice));

            DrawTextBlock(textFormat, summaryDxBrush, line1, x, y, 520f);
            DrawTextBlock(textFormat, summaryDxBrush, line2, x, y + SummaryLineHeight, 520f);
            DrawTextBlock(textFormat, summaryDxBrush, line3, x, y + (SummaryLineHeight * 2f), 520f);

            float currentY = y + (SummaryLineHeight * 3f);
            foreach (string warning in validation.Warnings)
            {
                DrawTextBlock(textFormat, warningDxBrush, "WARN: " + warning, x, currentY, 560f);
                currentY += SummaryLineHeight;
            }

            return currentY;
        }

        private void DrawBasePriceLabel(TextFormat textFormat, SharpDX.Direct2D1.Brush brush, string text, float x, float y)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (y < ChartPanel.Y - 24f || y > ChartPanel.Y + ChartPanel.H)
                return;

            DrawTextBlock(textFormat, brush, text, x, y, ColumnWidth);
        }

        private void DrawTextBlock(TextFormat textFormat, SharpDX.Direct2D1.Brush brush, string text, float x, float y, float width)
        {
            if (string.IsNullOrWhiteSpace(text) || textFormat == null || brush == null)
                return;

            using (var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                text,
                textFormat,
                width,
                textFormat.FontSize * 1.35f))
            {
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(x, y), textLayout, brush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
            }
        }

        private void RenderMeasurementOverlays(
            ChartScale chartScale,
            TextFormat textFormat,
            SharpDX.Direct2D1.Brush originalMeasurementBrush,
            SharpDX.Direct2D1.Brush spacingMeasurementBrush,
            SharpDX.Direct2D1.Brush labelBackgroundBrush,
            SharpDX.Direct2D1.Brush labelShadowBrush,
            IList<LadderLevel> levels,
            float panelLeft,
            float panelRight,
            float lastVisibleBarX)
        {
            if (chartScale == null || textFormat == null || originalMeasurementBrush == null || spacingMeasurementBrush == null || labelBackgroundBrush == null || labelShadowBrush == null)
                return;

            originalMeasurementLabelBounds = Rect.Empty;
            spacingMeasurementLabelBounds.Clear();

            float originalMeasurementX = ResolveMeasurementX(panelLeft, panelRight, lastVisibleBarX, 48f, originalMeasurementXOffset);
            float spacingMeasurementX = ResolveMeasurementX(panelLeft, panelRight, lastVisibleBarX, 104f, spacingMeasurementXOffset);

            RenderOriginalEntryMeasurement(chartScale, textFormat, originalMeasurementBrush, labelBackgroundBrush, labelShadowBrush, originalMeasurementX);
            RenderLadderSpacingMeasurements(chartScale, textFormat, spacingMeasurementBrush, labelBackgroundBrush, labelShadowBrush, spacingMeasurementX, levels);
        }

        private float ResolveMeasurementX(float panelLeft, float panelRight, float lastVisibleBarX, float offsetFromRight, double runtimeOffset)
        {
            float desired = panelRight - offsetFromRight + (float)runtimeOffset;
            float minX = panelLeft + MeasurementLabelWidth + 18f;
            float maxX = Math.Max(minX, panelRight - 18f);
            return Math.Max(minX, Math.Min(maxX, desired));
        }

        private void RenderOriginalEntryMeasurement(
            ChartScale chartScale,
            TextFormat textFormat,
            SharpDX.Direct2D1.Brush brush,
            SharpDX.Direct2D1.Brush labelBackgroundBrush,
            SharpDX.Direct2D1.Brush labelShadowBrush,
            float x)
        {
            if (!HasSimulatedTrades())
                return;

            double originalEntryPrice = GetEffectiveOriginalEntryReferencePrice();
            double liveAveragePrice = GetEffectiveAverageEntry();
            if (originalEntryPrice <= 0 || liveAveragePrice <= 0 || PricesClose(originalEntryPrice, liveAveragePrice))
                return;

            DrawVerticalMeasurement(
                chartScale,
                textFormat,
                brush,
                x,
                originalEntryPrice,
                liveAveragePrice,
                BuildChartMeasurementLabel(originalEntryPrice, liveAveragePrice),
                chartScale.GetYByValue(originalEntryPrice) - (SummaryLineHeight * 1.3f),
                labelBackgroundBrush,
                labelShadowBrush,
                MeasurementLabelDragTarget.OriginalMeasurement);
        }

        private void RenderLadderSpacingMeasurements(
            ChartScale chartScale,
            TextFormat textFormat,
            SharpDX.Direct2D1.Brush brush,
            SharpDX.Direct2D1.Brush labelBackgroundBrush,
            SharpDX.Direct2D1.Brush labelShadowBrush,
            float x,
            IList<LadderLevel> levels)
        {
            if (levels == null || levels.Count == 0 || workingStopPrice <= 0)
                return;

            List<double> measurementNodes = new List<double>();
            measurementNodes.Add(GetEffectiveAverageEntry());
            measurementNodes.AddRange(levels.Select(level => level.LevelPrice));
            measurementNodes.Add(workingStopPrice);

            for (int i = 0; i < measurementNodes.Count - 1; i++)
            {
                double startPrice = measurementNodes[i];
                double endPrice = measurementNodes[i + 1];
                if (startPrice <= 0 || endPrice <= 0 || PricesClose(startPrice, endPrice))
                    continue;

                int? levelContracts = i < levels.Count
                    ? (int?)Math.Max(MinScaleLevelContracts, levels[i].QuantityDelta)
                    : null;

                DrawVerticalMeasurement(
                    chartScale,
                    textFormat,
                    brush,
                    x,
                    startPrice,
                    endPrice,
                    BuildChartMeasurementLabel(startPrice, endPrice, levelContracts),
                    null,
                    labelBackgroundBrush,
                    labelShadowBrush,
                    MeasurementLabelDragTarget.SpacingMeasurements);
            }
        }

        private void DrawVerticalMeasurement(
            ChartScale chartScale,
            TextFormat textFormat,
            SharpDX.Direct2D1.Brush brush,
            float x,
            double startPrice,
            double endPrice,
            string label,
            float? labelYOverride = null,
            SharpDX.Direct2D1.Brush labelBackgroundBrush = null,
            SharpDX.Direct2D1.Brush labelShadowBrush = null,
            MeasurementLabelDragTarget dragTarget = MeasurementLabelDragTarget.None)
        {
            if (chartScale == null || textFormat == null || brush == null || string.IsNullOrWhiteSpace(label))
                return;

            float startY = chartScale.GetYByValue(startPrice);
            float endY = chartScale.GetYByValue(endPrice);
            float topY = Math.Min(startY, endY);
            float bottomY = Math.Max(startY, endY);
            if (bottomY - topY < MeasurementArrowSize * 2f)
                return;

            RenderTarget.DrawLine(
                new SharpDX.Vector2(x, topY),
                new SharpDX.Vector2(x, bottomY),
                brush,
                MeasurementLineThickness);

            DrawMeasurementArrowHead(brush, x, topY, pointsDown: true);
            DrawMeasurementArrowHead(brush, x, bottomY, pointsDown: false);

            float labelY = labelYOverride ?? (((topY + bottomY) * 0.5f) - (SummaryLineHeight * 0.5f));
            labelY = Math.Max(ChartPanel.Y + 2f, Math.Min((ChartPanel.Y + ChartPanel.H) - SummaryLineHeight - 2f, labelY));
            DrawMeasurementLabelBlock(textFormat, brush, labelBackgroundBrush, labelShadowBrush, label, x - MeasurementLabelWidth - MeasurementLabelXGap, labelY, MeasurementLabelWidth, dragTarget);
        }

        private void DrawMeasurementArrowHead(SharpDX.Direct2D1.Brush brush, float x, float y, bool pointsDown)
        {
            if (brush == null)
                return;

            float yOffset = pointsDown ? MeasurementArrowSize : -MeasurementArrowSize;
            RenderTarget.DrawLine(
                new SharpDX.Vector2(x, y),
                new SharpDX.Vector2(x - MeasurementArrowSize, y + yOffset),
                brush,
                MeasurementLineThickness);
            RenderTarget.DrawLine(
                new SharpDX.Vector2(x, y),
                new SharpDX.Vector2(x + MeasurementArrowSize, y + yOffset),
                brush,
                MeasurementLineThickness);
        }

        private string BuildChartMeasurementLabel(double startPrice, double endPrice, int? levelContracts = null)
        {
            double delta = endPrice - startPrice;
            double tickSize = GetSafeTickSize();
            double tickDelta = tickSize > 0 ? delta / tickSize : 0;

            string distanceLabel = string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1})",
                FormatProtectionPriceUnitDelta(delta),
                FormatProtectionTickDelta(tickDelta));

            if (!levelContracts.HasValue || levelContracts.Value <= 0)
                return distanceLabel;

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\n{1}",
                distanceLabel,
                FormatLevelContractsLabel(levelContracts.Value));
        }

        private string FormatLevelContractsLabel(int contracts)
        {
            int safeContracts = Math.Max(MinScaleLevelContracts, contracts);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}",
                safeContracts,
                safeContracts == 1 ? "Entry" : "Entries");
        }

        private void DrawMeasurementLabelBlock(
            TextFormat textFormat,
            SharpDX.Direct2D1.Brush textBrush,
            SharpDX.Direct2D1.Brush backgroundBrush,
            SharpDX.Direct2D1.Brush shadowBrush,
            string text,
            float x,
            float y,
            float width,
            MeasurementLabelDragTarget dragTarget)
        {
            if (string.IsNullOrWhiteSpace(text) || textFormat == null || textBrush == null)
                return;

            float actualX = x;
            using (var textLayout = new TextLayout(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                text,
                textFormat,
                width,
                textFormat.FontSize * 1.45f))
            {
                float backgroundWidth = Math.Min(width, textLayout.Metrics.WidthIncludingTrailingWhitespace) + (MeasurementLabelPaddingX * 2f);
                float backgroundHeight = textLayout.Metrics.Height + (MeasurementLabelPaddingY * 2f);
                float panelMinX = ChartPanel.X + 2f;
                float panelMaxX = (ChartPanel.X + ChartPanel.W) - backgroundWidth - 2f;
                actualX = Math.Max(panelMinX + MeasurementLabelPaddingX, Math.Min(panelMaxX + MeasurementLabelPaddingX, x));

                if (backgroundBrush != null)
                {
                    RenderTarget.FillRectangle(
                        new SharpDX.RectangleF(
                            actualX - MeasurementLabelPaddingX,
                            y - MeasurementLabelPaddingY,
                            backgroundWidth,
                            backgroundHeight),
                        backgroundBrush);
                    RenderTarget.DrawRectangle(
                        new SharpDX.RectangleF(
                            actualX - MeasurementLabelPaddingX,
                            y - MeasurementLabelPaddingY,
                            backgroundWidth,
                            backgroundHeight),
                        textBrush,
                        1f);
                }

                if (shadowBrush != null)
                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(actualX + 1f, y + 1f), textLayout, shadowBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);
                RenderTarget.DrawTextLayout(new SharpDX.Vector2(actualX, y), textLayout, textBrush, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

                RegisterMeasurementLabelBounds(
                    dragTarget,
                    new Rect(
                        actualX - MeasurementLabelPaddingX - MeasurementLabelHitPaddingX,
                        y - MeasurementLabelPaddingY - MeasurementLabelHitPaddingY,
                        backgroundWidth + (MeasurementLabelHitPaddingX * 2.0),
                        backgroundHeight + (MeasurementLabelHitPaddingY * 2.0)));
            }
        }

        private void RegisterMeasurementLabelBounds(MeasurementLabelDragTarget dragTarget, Rect bounds)
        {
            if (bounds.IsEmpty)
                return;

            if (dragTarget == MeasurementLabelDragTarget.OriginalMeasurement)
            {
                originalMeasurementLabelBounds = bounds;
                return;
            }

            if (dragTarget == MeasurementLabelDragTarget.SpacingMeasurements)
                spacingMeasurementLabelBounds.Add(bounds);
        }

        private void TryAttachMeasurementDragHandlers()
        {
            if (measurementDragHandlersAttached || ChartControl == null)
                return;

            Action attach = () =>
            {
                if (measurementDragHandlersAttached || ChartControl == null)
                    return;

                measurementDragHostControl = ChartControl;
                measurementDragHostControl.PreviewMouseLeftButtonDown += ChartControl_PreviewMouseLeftButtonDown;
                measurementDragHostControl.PreviewMouseMove += ChartControl_PreviewMouseMove;
                measurementDragHostControl.PreviewMouseLeftButtonUp += ChartControl_PreviewMouseLeftButtonUp;
                measurementDragHostControl.LostMouseCapture += ChartControl_LostMouseCapture;
                measurementDragHandlersAttached = true;
            };

            if (ChartControl.Dispatcher.CheckAccess())
                attach();
            else
                ChartControl.Dispatcher.InvokeAsync(attach);
        }

        private void ChartControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ChartControl == null)
                return;

            Point point = e.GetPosition(ChartControl);
            MeasurementLabelDragTarget dragTarget = ResolveMeasurementDragTarget(point);
            if (dragTarget == MeasurementLabelDragTarget.None)
                return;

            activeMeasurementDragTarget = dragTarget;
            activeMeasurementDragStartPoint = point;
            activeMeasurementDragStartOffset = dragTarget == MeasurementLabelDragTarget.OriginalMeasurement
                ? originalMeasurementXOffset
                : spacingMeasurementXOffset;

            ChartControl.CaptureMouse();
            e.Handled = true;
        }

        private void ChartControl_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (ChartControl == null || activeMeasurementDragTarget == MeasurementLabelDragTarget.None)
                return;

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndMeasurementLabelDrag();
                return;
            }

            Point point = e.GetPosition(ChartControl);
            double deltaX = point.X - activeMeasurementDragStartPoint.X;
            if (activeMeasurementDragTarget == MeasurementLabelDragTarget.OriginalMeasurement)
                originalMeasurementXOffset = activeMeasurementDragStartOffset + deltaX;
            else if (activeMeasurementDragTarget == MeasurementLabelDragTarget.SpacingMeasurements)
                spacingMeasurementXOffset = activeMeasurementDragStartOffset + deltaX;

            ForceRefresh();
            e.Handled = true;
        }

        private void ChartControl_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (activeMeasurementDragTarget == MeasurementLabelDragTarget.None)
                return;

            EndMeasurementLabelDrag();
            e.Handled = true;
        }

        private void ChartControl_LostMouseCapture(object sender, MouseEventArgs e)
        {
            EndMeasurementLabelDrag();
        }

        private MeasurementLabelDragTarget ResolveMeasurementDragTarget(Point point)
        {
            if (originalMeasurementLabelBounds.Contains(point))
                return MeasurementLabelDragTarget.OriginalMeasurement;

            for (int i = 0; i < spacingMeasurementLabelBounds.Count; i++)
            {
                if (spacingMeasurementLabelBounds[i].Contains(point))
                    return MeasurementLabelDragTarget.SpacingMeasurements;
            }

            return MeasurementLabelDragTarget.None;
        }

        private void EndMeasurementLabelDrag()
        {
            activeMeasurementDragTarget = MeasurementLabelDragTarget.None;

            if (measurementDragHostControl != null && Mouse.Captured == measurementDragHostControl)
                measurementDragHostControl.ReleaseMouseCapture();
        }

        private string BuildLossLabel(LadderLevel level)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | Qty {1} | {2}",
                FormatDistanceFromEntry(level.PriceUnitsFromEntry),
                level.QuantityAfterLevel,
                FormatCurrency(level.NetPnlAtLevel));
        }

        private string BuildProfitLabel(LadderLevel level)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Final {0} | To tgt {1}",
                FormatCurrency(level.TargetPnl),
                FormatDistanceToTarget(level.LevelPrice));
        }

        private string BuildStopLineLabel(IList<LadderLevel> levels)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Stop {0} | Final {1}",
                FormatDistanceFromEntry(workingStopPrice - workingEntryPrice),
                FormatCurrency(GetFinalStopPnl(levels)));
        }

        private string BuildTargetLineLabel(IList<LadderLevel> levels)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Target {0} | Final {1}",
                FormatDistanceFromEntry(workingTargetPrice - workingEntryPrice),
                FormatCurrency(GetFinalTargetPnl(levels)));
        }

        private void BuildLadder(out List<LadderLevel> levels, out LadderValidation validation)
        {
            levels = new List<LadderLevel>();
            validation = ValidateConfiguration();
            if (!validation.CanRenderLevels)
                return;

            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            List<OrderedLadderEntry> orderedEntries = GetOrderedLadderEntries(validation);
            if (orderedEntries.Count == 0)
            {
                validation.Warnings.Add("No active ladder levels are available.");
                validation.CanRenderLevels = false;
                return;
            }

            validation.VisibleLevelCount = orderedEntries.Count;

            if (IsScaleInMode())
            {
                int runningQuantity = GetEffectiveQuantity();
                double runningAverage = GetEffectiveAverageEntry();

                for (int i = 0; i < orderedEntries.Count; i++)
                {
                    OrderedLadderEntry entry = orderedEntries[i];
                    double levelPrice = entry.Price;
                    int quantityDelta = GetRuntimeScaleLevelContractsValue(entry.LadderIndex);
                    int newQuantity = runningQuantity + quantityDelta;
                    double newAverage = ((runningAverage * runningQuantity) + (levelPrice * quantityDelta)) / Math.Max(1, newQuantity);

                    levels.Add(new LadderLevel
                    {
                        LevelNumber = i + 1,
                        LevelPrice = levelPrice,
                        QuantityDelta = quantityDelta,
                        QuantityAfterLevel = newQuantity,
                        AverageEntryAfterLevel = newAverage,
                        NetPnlAtLevel = PriceDeltaToDollars(levelPrice - newAverage, newQuantity, directionSign),
                        TargetPnl = PriceDeltaToDollars(workingTargetPrice - newAverage, newQuantity, directionSign),
                        RealizedPnl = 0,
                        MarketTicksFromAverage = PriceDeltaToSignedTicks(levelPrice - newAverage, directionSign),
                        TargetTicksFromAverage = PriceDeltaToSignedTicks(workingTargetPrice - newAverage, directionSign),
                        PriceUnitsFromEntry = levelPrice - workingEntryPrice,
                        AverageUnitsFromEntry = newAverage - workingEntryPrice
                    });

                    runningQuantity = newQuantity;
                    runningAverage = newAverage;
                }

                return;
            }

            int remainingQuantity = GetEffectiveQuantity();
            double realizedPnl = 0;

            for (int i = 0; i < orderedEntries.Count; i++)
            {
                OrderedLadderEntry entry = orderedEntries[i];
                double levelPrice = entry.Price;
                int quantityDelta = GetRuntimeScaleLevelContractsValue(entry.LadderIndex);
                remainingQuantity -= quantityDelta;
                realizedPnl += PriceDeltaToDollars(levelPrice - workingEntryPrice, quantityDelta, directionSign);
                double netAtLevel = realizedPnl + PriceDeltaToDollars(levelPrice - workingEntryPrice, remainingQuantity, directionSign);
                double targetPnl = realizedPnl + PriceDeltaToDollars(workingTargetPrice - workingEntryPrice, remainingQuantity, directionSign);

                levels.Add(new LadderLevel
                {
                    LevelNumber = i + 1,
                    LevelPrice = levelPrice,
                    QuantityDelta = quantityDelta,
                    QuantityAfterLevel = remainingQuantity,
                    AverageEntryAfterLevel = workingEntryPrice,
                    NetPnlAtLevel = netAtLevel,
                    TargetPnl = targetPnl,
                    RealizedPnl = realizedPnl,
                    MarketTicksFromAverage = PriceDeltaToSignedTicks(levelPrice - workingEntryPrice, directionSign),
                    TargetTicksFromAverage = PriceDeltaToSignedTicks(workingTargetPrice - workingEntryPrice, directionSign),
                    PriceUnitsFromEntry = levelPrice - workingEntryPrice,
                    AverageUnitsFromEntry = 0
                });
            }
        }

        private LadderValidation ValidateConfiguration()
        {
            LadderValidation validation = new LadderValidation();
            double tickSize = GetSafeTickSize();
            EnsureRuntimeScaleLevelContractsSeeded();

            if (tickSize <= 0)
            {
                validation.Warnings.Add("Instrument tick size is not available.");
                validation.CanRenderLevels = false;
                return validation;
            }

            if (BaseContracts <= 0)
                validation.Warnings.Add("BaseContracts must be at least 1.");

            if (runtimeScaleLevelContracts.Any(qty => qty < MinScaleLevelContracts || qty > MaxScaleLevelContracts))
                validation.Warnings.Add("ContractsPerScaleLevel must stay within the allowed range.");

            validation.SpacingPrice = GetConfiguredDefaultSpacingTicks() * tickSize;

            if (runtimeScaleLevelSpacingTicks.Any(ticks => ticks < MinScaleLevelSpacingTicks || ticks > MaxScaleLevelSpacingTicks))
                validation.Warnings.Add("Per-level spacing ticks must stay within the allowed range.");

            if (workingEntryPrice <= 0 || workingStopPrice <= 0 || workingTargetPrice <= 0)
                validation.Warnings.Add("Entry, stop, and target must all be positive prices.");

            if (TradeDirection == ScaleLadderTradeDirection.Long)
            {
                if (workingTargetPrice <= workingEntryPrice)
                    validation.Warnings.Add("For long mode, target must be above entry.");
                if (workingStopPrice >= workingEntryPrice)
                    validation.Warnings.Add("For long mode, stop must be below entry.");
            }
            else
            {
                if (workingTargetPrice >= workingEntryPrice)
                    validation.Warnings.Add("For short mode, target must be below entry.");
                if (workingStopPrice <= workingEntryPrice)
                    validation.Warnings.Add("For short mode, stop must be above entry.");
            }

            validation.ScaleOnFavorableSide = IsScaleOnFavorableSide();
            validation.VisibleLevelCount = GetRuntimeScaleLevelCount();

            if (IsScaleOutMode() && GetRuntimeScaleLevelContractsTotal() > GetEffectiveQuantity())
                validation.Warnings.Add("Scale-out size exceeds BaseContracts.");

            if (validation.Warnings.Count > 0)
            {
                validation.CanRenderLevels = false;
                return validation;
            }

            validation.CanRenderLevels = true;
            return validation;
        }

        private void EnsureAnchorLines()
        {
            if (Instrument == null)
                return;

            bool resetRequested = ResetAnchors || !anchorsSeeded;
            if (resetRequested && !CanSeedFromLatestBar())
                return;

            if (resetRequested)
            {
                ResetSimulationState();
                ResetRuntimeScaleLevelContractsToDefaults();
                SeedWorkingPrices();
            }

            entryLine = FindHorizontalLine(entryLineTag) ?? entryLine;
            stopLine = FindHorizontalLine(stopLineTag) ?? stopLine;
            targetLine = FindHorizontalLine(targetLineTag) ?? targetLine;

            if (entryLine == null)
                entryLine = Draw.HorizontalLine(this, entryLineTag, workingEntryPrice, EntryLineBrush);
            if (stopLine == null)
                stopLine = Draw.HorizontalLine(this, stopLineTag, workingStopPrice, RiskSideBrush);
            if (targetLine == null)
                targetLine = Draw.HorizontalLine(this, targetLineTag, workingTargetPrice, RewardSideBrush);

            ApplyEntryLineStyle(entryLine);
            ApplyProtectionLineStyle(stopLine, Brushes.OrangeRed);
            ApplyProtectionLineStyle(targetLine, Brushes.DeepSkyBlue);
            EnsureLadderLines(resetRequested);

            if (resetRequested)
            {
                SetAnchorPrice(entryLine, workingEntryPrice);
                SetAnchorPrice(stopLine, workingStopPrice);
                SetAnchorPrice(targetLine, workingTargetPrice);
                ResetAnchors = false;
            }
        }

        private void EnsureLadderLines(bool resetRequested)
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            int desiredCount = GetRuntimeScaleLevelCount();
            EnsureConsumedLadderReferenceStorage(desiredCount);
            if (ladderLineTags.Length > desiredCount)
            {
                for (int i = desiredCount; i < ladderLineTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(ladderLineTags[i]))
                        RemoveDrawObject(ladderLineTags[i]);
                }
            }

            if (ladderLines.Length != desiredCount)
            {
                HorizontalLine[] oldLines = ladderLines;
                string[] oldTags = ladderLineTags;
                ladderLines = new HorizontalLine[desiredCount];
                ladderLineTags = new string[desiredCount];

                for (int i = 0; i < desiredCount; i++)
                {
                    string tag = tagPrefix + "_Ladder_" + (i + 1).ToString(CultureInfo.InvariantCulture);
                    ladderLineTags[i] = tag;
                    if (i < oldTags.Length && oldTags[i] == tag)
                        ladderLines[i] = oldLines[i];
                }
            }

            Brush ladderBrush = GetLadderLineBrush(IsScaleOnFavorableSide());
            for (int i = 0; i < desiredCount; i++)
            {
                if (consumedLadderIndices.Contains(i))
                {
                    if (!string.IsNullOrEmpty(ladderLineTags[i]))
                        RemoveDrawObject(ladderLineTags[i]);
                    ladderLines[i] = null;
                    continue;
                }

                ladderLines[i] = FindHorizontalLine(ladderLineTags[i]) ?? ladderLines[i];
                bool created = false;
                if (ladderLines[i] == null)
                {
                    ladderLines[i] = Draw.HorizontalLine(this, ladderLineTags[i], GetSeededLadderPrice(i + 1), ladderBrush);
                    created = true;
                }

                ApplyLadderLineStyle(ladderLines[i], ladderBrush);
                if (resetRequested || created)
                    SetAnchorPrice(ladderLines[i], GetSeededLadderPrice(i + 1));
            }

            for (int i = 0; i < desiredCount; i++)
            {
                consumedLadderReferenceLines[i] = FindHorizontalLine(consumedLadderReferenceTags[i]) ?? consumedLadderReferenceLines[i];

                if (!consumedLadderIndices.Contains(i))
                {
                    RemoveConsumedLadderReferenceLine(i);
                    continue;
                }

                double consumedPrice;
                if (!TryGetConsumedStepPrice(i, out consumedPrice) || consumedPrice <= 0)
                    continue;

                if (consumedLadderReferenceLines[i] == null)
                    consumedLadderReferenceLines[i] = Draw.HorizontalLine(this, consumedLadderReferenceTags[i], consumedPrice, ConsumedLadderReferenceBrush);

                ApplyConsumedLadderReferenceLineStyle(consumedLadderReferenceLines[i]);
                SetAnchorPrice(consumedLadderReferenceLines[i], consumedPrice);
            }
        }

        private void EnsureConsumedLadderReferenceStorage(int desiredCount)
        {
            if (consumedLadderReferenceTags.Length > desiredCount)
            {
                for (int i = desiredCount; i < consumedLadderReferenceTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(consumedLadderReferenceTags[i]))
                        RemoveDrawObject(consumedLadderReferenceTags[i]);
                }
            }

            if (consumedLadderReferenceLines.Length == desiredCount && consumedLadderReferenceTags.Length == desiredCount)
                return;

            HorizontalLine[] oldLines = consumedLadderReferenceLines;
            string[] oldTags = consumedLadderReferenceTags;
            consumedLadderReferenceLines = new HorizontalLine[desiredCount];
            consumedLadderReferenceTags = new string[desiredCount];

            for (int i = 0; i < desiredCount; i++)
            {
                string tag = tagPrefix + "_Consumed_" + (i + 1).ToString(CultureInfo.InvariantCulture);
                consumedLadderReferenceTags[i] = tag;
                if (i < oldTags.Length && oldTags[i] == tag)
                    consumedLadderReferenceLines[i] = oldLines[i];
            }
        }

        private bool TryGetConsumedStepPrice(int ladderIndex, out double price)
        {
            price = 0;
            if (ladderIndex < 0)
                return false;

            foreach (SimulatedTradeStep step in simulatedTradeHistory)
            {
                if (step == null || step.LadderIndex != ladderIndex || step.FillPrice <= 0)
                    continue;

                price = step.FillPrice;
                return true;
            }

            return false;
        }

        private void EnsureConsumedLadderReferenceLine(int ladderIndex, double price)
        {
            if (ladderIndex < 0 || ladderIndex >= consumedLadderReferenceTags.Length || price <= 0)
                return;

            consumedLadderReferenceLines[ladderIndex] = FindHorizontalLine(consumedLadderReferenceTags[ladderIndex]) ?? consumedLadderReferenceLines[ladderIndex];
            if (consumedLadderReferenceLines[ladderIndex] == null)
                consumedLadderReferenceLines[ladderIndex] = Draw.HorizontalLine(this, consumedLadderReferenceTags[ladderIndex], price, ConsumedLadderReferenceBrush);

            ApplyConsumedLadderReferenceLineStyle(consumedLadderReferenceLines[ladderIndex]);
            SetAnchorPrice(consumedLadderReferenceLines[ladderIndex], price);
        }

        private void RemoveConsumedLadderReferenceLine(int ladderIndex)
        {
            if (ladderIndex < 0 || ladderIndex >= consumedLadderReferenceTags.Length)
                return;

            if (!string.IsNullOrEmpty(consumedLadderReferenceTags[ladderIndex]))
                RemoveDrawObject(consumedLadderReferenceTags[ladderIndex]);
            consumedLadderReferenceLines[ladderIndex] = null;
        }

        private void RemoveAllConsumedLadderReferenceLines()
        {
            for (int i = 0; i < consumedLadderReferenceTags.Length; i++)
                RemoveConsumedLadderReferenceLine(i);
        }

        private Brush GetLadderLineBrush(bool scaleOnFavorableSide)
        {
            if (IsScaleInMode())
                return Brushes.Aqua;

            return scaleOnFavorableSide ? RewardSideBrush : RiskSideBrush;
        }

        private double GetSeededLadderPrice(int levelNumber)
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            double tickSize = GetSafeTickSize();
            if (tickSize <= 0)
                return workingEntryPrice;

            int maxLevel = Math.Min(levelNumber, runtimeScaleLevelSpacingTicks.Count);
            int cumulativeTicks = 0;
            for (int i = 0; i < maxLevel; i++)
                cumulativeTicks += GetRuntimeScaleLevelSpacingTicksValue(i);

            double spacingPrice = cumulativeTicks * tickSize;
            if (spacingPrice <= 0)
                return workingEntryPrice;

            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            double scaleSign = GetScaleSideSign(directionSign, IsScaleOnFavorableSide());
            return RoundToTick(workingEntryPrice + (spacingPrice * scaleSign));
        }

        private List<OrderedLadderEntry> GetOrderedLadderEntries(LadderValidation validation)
        {
            List<OrderedLadderEntry> entries = new List<OrderedLadderEntry>();
            if (ladderLines == null || ladderLines.Length == 0)
                return entries;

            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            double scaleSign = GetScaleSideSign(directionSign, validation.ScaleOnFavorableSide);
            double boundaryPrice = validation.ScaleOnFavorableSide ? workingTargetPrice : workingStopPrice;
            double boundaryDistance = (boundaryPrice - workingEntryPrice) * scaleSign;
            bool warnedWrongSide = false;
            bool warnedBoundary = false;

            for (int i = 0; i < ladderLines.Length; i++)
            {
                if (consumedLadderIndices.Contains(i))
                    continue;

                double price = GetAnchorPrice(ladderLines[i]);
                if (price <= 0)
                    continue;

                double distance = (price - workingEntryPrice) * scaleSign;
                if (distance <= 0)
                {
                    if (!warnedWrongSide)
                    {
                        validation.Warnings.Add("One or more ladder levels are on the wrong side of the entry and were ignored.");
                        warnedWrongSide = true;
                    }
                    continue;
                }

                if (!warnedBoundary && boundaryDistance > 0 && distance > boundaryDistance + (GetSafeTickSize() * 0.5))
                {
                    validation.Warnings.Add("One or more ladder levels extend beyond the current stop/target boundary.");
                    warnedBoundary = true;
                    continue;
                }

                entries.Add(new OrderedLadderEntry
                {
                    LadderIndex = i,
                    Price = price
                });
            }

            return entries
                .OrderBy(entry => (entry.Price - workingEntryPrice) * scaleSign)
                .ToList();
        }

        private void SeedWorkingPrices()
        {
            double closePrice = CurrentBar >= 0 ? Close[0] : 0;
            if (closePrice <= 0)
                closePrice = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.RoundToTickSize(1.0) : 1.0;

            double defaultEntry = RoundToTick(closePrice);
            workingEntryPrice = defaultEntry;
            simulatedAverageEntry = workingEntryPrice;
            simulatedQuantity = Math.Max(1, BaseContracts);
            ApplyConfiguredProtectionPrices(workingEntryPrice, simulatedQuantity);
            anchorsSeeded = true;
        }

        private bool CanSeedFromLatestBar()
        {
            return Count > 0 && CurrentBar >= Count - 1;
        }

        private void SyncWorkingPricesFromAnchors()
        {
            double entry = GetAnchorPrice(entryLine);
            double stop = GetAnchorPrice(stopLine);
            double target = GetAnchorPrice(targetLine);

            if (HasSimulatedTrades())
            {
                if (simulatedAverageEntry > 0 && !double.IsNaN(simulatedAverageEntry) && !double.IsInfinity(simulatedAverageEntry))
                {
                    workingEntryPrice = simulatedAverageEntry;
                    if (entryLine != null && !PricesClose(entry, simulatedAverageEntry))
                        SetAnchorPrice(entryLine, simulatedAverageEntry);
                }
            }
            else if (entry > 0)
            {
                workingEntryPrice = entry;
                simulatedAverageEntry = entry;
                simulatedQuantity = Math.Max(1, BaseContracts);
            }

            if (stop > 0)
                workingStopPrice = stop;
            if (target > 0)
                workingTargetPrice = target;
        }

        private void ApplyEntryLineStyle(HorizontalLine line)
        {
            ApplyLineStyle(line, EntryLineBrush ?? Brushes.DodgerBlue, 2, "Dash");
        }

        private void ApplyProtectionLineStyle(HorizontalLine line, Brush brush)
        {
            ApplyLineStyle(line, brush, 3, "Solid");
        }

        private void ApplyOriginalEntryReferenceLineStyle(HorizontalLine line)
        {
            ApplyLineStyle(line, Brushes.MediumPurple, 3, "Solid");
        }

        private void ApplyLadderLineStyle(HorizontalLine line, Brush brush)
        {
            ApplyLineStyle(line, brush, 3, "Dash");
        }

        private void ApplyConsumedLadderReferenceLineStyle(HorizontalLine line)
        {
            ApplyLineStyle(line, ConsumedLadderReferenceBrush, 1, "Solid");
        }

        private void ApplyLineStyle(HorizontalLine line, Brush brush, int width, string dashStyleName)
        {
            if (line == null)
                return;

            if (line.Stroke == null)
                line.Stroke = new Stroke(brush, DashStyleHelper.Solid, width);

            line.IsLocked = false;
            line.Stroke.Brush = brush;
            line.Stroke.Width = width;
            SetLineDashStyle(line.Stroke, dashStyleName);
        }

        private void SetLineDashStyle(object stroke, string styleName)
        {
            if (stroke == null || string.IsNullOrWhiteSpace(styleName))
                return;

            var helperProp = stroke.GetType().GetProperty("DashStyleHelper");
            if (helperProp != null)
            {
                object dashValue = Enum.Parse(helperProp.PropertyType, styleName);
                helperProp.SetValue(stroke, dashValue, null);
                return;
            }

            var dashProp = stroke.GetType().GetProperty("DashStyle");
            if (dashProp == null || dashProp.PropertyType != typeof(DashStyle))
                return;

            DashStyle dashStyle = styleName.Equals("Dash", StringComparison.OrdinalIgnoreCase)
                ? new DashStyle(new double[] { 6, 3 }, 0)
                : DashStyles.Solid;
            dashProp.SetValue(stroke, dashStyle, null);
        }

        private double GetAnchorPrice(HorizontalLine line)
        {
            if (line == null || line.StartAnchor == null)
                return 0;

            double price = line.StartAnchor.Price;
            if (double.IsNaN(price) || double.IsInfinity(price) || price <= 0)
                return 0;

            return price;
        }

        private void SetAnchorPrice(HorizontalLine line, double price, bool roundToTick = false)
        {
            if (line == null || line.StartAnchor == null || price <= 0 || double.IsNaN(price) || double.IsInfinity(price))
                return;

            line.StartAnchor.Price = roundToTick ? RoundToTick(price) : price;
        }

        private HorizontalLine FindHorizontalLine(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || DrawObjects == null)
                return null;

            foreach (var drawObject in DrawObjects)
            {
                if (drawObject == null || !string.Equals(drawObject.Tag, tag, StringComparison.Ordinal))
                    continue;

                return drawObject as HorizontalLine;
            }

            return null;
        }

        private NinjaTrader.NinjaScript.DrawingTools.Text FindTextObject(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag) || DrawObjects == null)
                return null;

            foreach (var drawObject in DrawObjects)
            {
                if (drawObject == null || !string.Equals(drawObject.Tag, tag, StringComparison.Ordinal))
                    continue;

                return drawObject as NinjaTrader.NinjaScript.DrawingTools.Text;
            }

            return null;
        }

        private void RemoveAnchorLines()
        {
            if (!string.IsNullOrEmpty(entryLineTag))
                RemoveDrawObject(entryLineTag);
            if (!string.IsNullOrEmpty(stopLineTag))
                RemoveDrawObject(stopLineTag);
            if (!string.IsNullOrEmpty(targetLineTag))
                RemoveDrawObject(targetLineTag);
            if (ladderLineTags != null)
            {
                for (int i = 0; i < ladderLineTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(ladderLineTags[i]))
                        RemoveDrawObject(ladderLineTags[i]);
                }
            }
            if (consumedLadderReferenceTags != null)
            {
                for (int i = 0; i < consumedLadderReferenceTags.Length; i++)
                {
                    if (!string.IsNullOrEmpty(consumedLadderReferenceTags[i]))
                        RemoveDrawObject(consumedLadderReferenceTags[i]);
                }
            }

            RemoveProtectionLabelDrawObjects();
            RemoveOriginalEntryReferenceLine();

            entryLine = null;
            stopLine = null;
            targetLine = null;
            ladderLines = new HorizontalLine[0];
            ladderLineTags = new string[0];
            consumedLadderReferenceLines = new HorizontalLine[0];
            consumedLadderReferenceTags = new string[0];
        }

        private void RemoveProtectionLabelDrawObjects()
        {
            if (!string.IsNullOrEmpty(stopLabelTag))
                RemoveDrawObject(stopLabelTag);
            if (!string.IsNullOrEmpty(targetLabelTag))
                RemoveDrawObject(targetLabelTag);

            stopLabelObject = null;
            targetLabelObject = null;
            stopLabelAnchorTime = DateTime.MinValue;
            targetLabelAnchorTime = DateTime.MinValue;
            stopLabelPriceOffset = double.NaN;
            targetLabelPriceOffset = double.NaN;
            stopLabelRenderedPrice = 0;
            targetLabelRenderedPrice = 0;
            stopLabelRenderedText = null;
            targetLabelRenderedText = null;
        }

        private void RemoveOriginalEntryReferenceLine()
        {
            if (!string.IsNullOrEmpty(originalEntryReferenceLineTag))
                RemoveDrawObject(originalEntryReferenceLineTag);

            originalEntryReferenceLine = null;
        }

        private double GetSafeTickSize()
        {
            double tickSize = Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.TickSize
                : TickSize;
            return tickSize > 0 ? tickSize : 0.25;
        }

        private double RoundToTick(double price)
        {
            if (price <= 0 || Instrument == null || Instrument.MasterInstrument == null)
                return price;

            return Instrument.MasterInstrument.RoundToTickSize(price);
        }

        private void NormalizeProtectionInputs()
        {
            if (StopType == ScaleLadderProtectionKind.Ticks)
            {
                StopTicks = Math.Max(1, (int)Math.Round(Math.Max(1.0, StopValue)));
                StopValue = StopTicks;
            }

            if (TargetType == ScaleLadderProtectionKind.Ticks)
            {
                TargetTicks = Math.Max(1, (int)Math.Round(Math.Max(1.0, TargetValue)));
                TargetValue = TargetTicks;
            }
        }

        private double GetLatestAtrValue()
        {
            try
            {
                return baseAtr != null ? baseAtr[0] : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private double GetProtectionValue(bool isStop)
        {
            return Math.Max(0.0, isStop ? StopValue : TargetValue);
        }

        private bool IsAtrProtectionMode(bool isStop)
        {
            return isStop ? StopType == ScaleLadderProtectionKind.ATR : TargetType == ScaleLadderProtectionKind.ATR;
        }

        private bool IsDollarProtectionMode(bool isStop)
        {
            return isStop ? StopType == ScaleLadderProtectionKind.Dollars : TargetType == ScaleLadderProtectionKind.Dollars;
        }

        private double ConvertDollarsToPrice(double dollars, int quantityHint)
        {
            if (dollars <= 0)
                return 0;

            double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0.0;
            if (pointValue <= 0 || double.IsNaN(pointValue) || double.IsInfinity(pointValue))
                return 0;

            int quantity = Math.Max(1, quantityHint);
            return dollars / (pointValue * quantity);
        }

        private double ResolveProtectionOffsetPrice(bool isStop, int quantityHint, double atrValue = double.NaN)
        {
            double value = GetProtectionValue(isStop);
            if (value <= 0)
                return 0;

            double tickSize = GetSafeTickSize();
            if (tickSize <= 0)
                return 0;

            if (IsAtrProtectionMode(isStop))
            {
                double effectiveAtr = (!double.IsNaN(atrValue) && atrValue > 0) ? atrValue : GetLatestAtrValue();
                return effectiveAtr > 0 ? effectiveAtr * value : 0;
            }

            if (IsDollarProtectionMode(isStop))
                return ConvertDollarsToPrice(value, quantityHint);

            double ticks = Math.Max(1.0, Math.Round(value));
            return ticks * tickSize;
        }

        private double? ResolveConfiguredProtectionPrice(bool isStop, double entryPrice, int quantityHint, double atrValue = double.NaN)
        {
            if (entryPrice <= 0 || double.IsNaN(entryPrice) || double.IsInfinity(entryPrice))
                return null;

            double offset = ResolveProtectionOffsetPrice(isStop, quantityHint, atrValue);
            if (offset <= 0 || double.IsNaN(offset) || double.IsInfinity(offset))
                return null;

            double desiredPrice;
            if (TradeDirection == ScaleLadderTradeDirection.Long)
                desiredPrice = isStop ? entryPrice - offset : entryPrice + offset;
            else
                desiredPrice = isStop ? entryPrice + offset : entryPrice - offset;

            return RoundToTick(desiredPrice);
        }

        private double ResolveFallbackProtectionPrice(bool isStop, double entryPrice)
        {
            double tickSize = GetSafeTickSize();
            double offset = tickSize > 0 ? tickSize : 0.25;

            if (TradeDirection == ScaleLadderTradeDirection.Long)
                return RoundToTick(isStop ? entryPrice - offset : entryPrice + offset);

            return RoundToTick(isStop ? entryPrice + offset : entryPrice - offset);
        }

        private void ApplyConfiguredProtectionPrices(double entryPrice, int quantityHint)
        {
            if (entryPrice <= 0 || double.IsNaN(entryPrice) || double.IsInfinity(entryPrice))
                return;

            NormalizeProtectionInputs();

            int effectiveQuantity = Math.Max(1, quantityHint);
            double atrValue = GetLatestAtrValue();
            double? desiredStop = ResolveConfiguredProtectionPrice(true, entryPrice, effectiveQuantity, atrValue);
            double? desiredTarget = ResolveConfiguredProtectionPrice(false, entryPrice, effectiveQuantity, atrValue);

            workingStopPrice = desiredStop ?? (workingStopPrice > 0 ? workingStopPrice : ResolveFallbackProtectionPrice(true, entryPrice));
            workingTargetPrice = desiredTarget ?? (workingTargetPrice > 0 ? workingTargetPrice : ResolveFallbackProtectionPrice(false, entryPrice));

            if (stopLine != null && workingStopPrice > 0)
                SetAnchorPrice(stopLine, workingStopPrice);
            if (targetLine != null && workingTargetPrice > 0)
                SetAnchorPrice(targetLine, workingTargetPrice);
        }

        private double PriceDeltaToSignedTicks(double priceDelta, double directionSign)
        {
            return (priceDelta * directionSign) / GetSafeTickSize();
        }

        private double PriceDeltaToDollars(double priceDelta, int quantity, double directionSign)
        {
            double pointValue = Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.PointValue
                : 1.0;

            return priceDelta * directionSign * Math.Max(0, quantity) * pointValue;
        }

        private double GetScaleSideSign(double directionSign, bool scaleOnFavorableSide)
        {
            return scaleOnFavorableSide ? directionSign : -directionSign;
        }

        private bool IsScaleInMode()
        {
            return ScaleMode == ScaleLadderMode.ScaleInAtLoss || ScaleMode == ScaleLadderMode.ScaleInAtProfit;
        }

        private bool IsScaleOutMode()
        {
            return !IsScaleInMode();
        }

        private bool IsScaleOnFavorableSide()
        {
            return ScaleMode == ScaleLadderMode.ScaleInAtProfit || ScaleMode == ScaleLadderMode.ScaleOutAtProfit;
        }

        private double GetFinalTargetPnl(IList<LadderLevel> levels)
        {
            if (levels != null && levels.Count > 0)
                return levels[levels.Count - 1].TargetPnl;

            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            return PriceDeltaToDollars(workingTargetPrice - workingEntryPrice, GetEffectiveQuantity(), directionSign);
        }

        private double GetFinalStopPnl(IList<LadderLevel> levels)
        {
            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            if (levels != null && levels.Count > 0)
            {
                LadderLevel finalLevel = levels[levels.Count - 1];
                if (IsScaleInMode())
                    return PriceDeltaToDollars(workingStopPrice - finalLevel.AverageEntryAfterLevel, finalLevel.QuantityAfterLevel, directionSign);

                return finalLevel.RealizedPnl + PriceDeltaToDollars(workingStopPrice - workingEntryPrice, finalLevel.QuantityAfterLevel, directionSign);
            }

            return PriceDeltaToDollars(workingStopPrice - workingEntryPrice, GetEffectiveQuantity(), directionSign);
        }

        private int GetPriceUnitDecimals()
        {
            string tickText = GetSafeTickSize().ToString("0.########", CultureInfo.InvariantCulture);
            int separatorIndex = tickText.IndexOf('.');
            int decimals = separatorIndex >= 0 ? tickText.Length - separatorIndex - 1 : 0;
            return Math.Max(2, decimals);
        }

        private string FormatPriceUnitDelta(double delta)
        {
            string format = "F" + GetPriceUnitDecimals().ToString(CultureInfo.InvariantCulture);
            if (delta > 0)
                return "+" + Math.Abs(delta).ToString(format, CultureInfo.InvariantCulture);
            if (delta < 0)
                return "-" + Math.Abs(delta).ToString(format, CultureInfo.InvariantCulture);
            return 0.0.ToString(format, CultureInfo.InvariantCulture);
        }

        private string FormatDistanceFromEntry(double priceDelta)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1})",
                FormatPriceUnitDelta(priceDelta),
                FormatTickDelta(PriceDeltaToSignedTicks(priceDelta, GetDirectionSign())));
        }

        private string FormatDistanceToTarget(double levelPrice)
        {
            double priceDelta = workingTargetPrice - levelPrice;
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} ({1})",
                FormatPriceUnitDelta(priceDelta),
                FormatTickDelta(PriceDeltaToSignedTicks(priceDelta, GetDirectionSign())));
        }

        private string FormatTickDelta(double ticks)
        {
            double roundedTicks = Math.Round(ticks, 2, MidpointRounding.AwayFromZero);
            if (roundedTicks > 0)
                return "+" + roundedTicks.ToString("0.##", CultureInfo.InvariantCulture) + "t";
            if (roundedTicks < 0)
                return "-" + Math.Abs(roundedTicks).ToString("0.##", CultureInfo.InvariantCulture) + "t";
            return "0t";
        }

        private string FormatCurrency(double value)
        {
            string prefix = value >= 0 ? "+$" : "-$";
            double roundedValue = Math.Round(Math.Abs(value), 0, MidpointRounding.AwayFromZero);
            return prefix + roundedValue.ToString("N0", CultureInfo.InvariantCulture);
        }

        private double GetDirectionSign()
        {
            return TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
        }

        private bool HasSimulatedTrades()
        {
            return simulatedTradeHistory.Count > 0;
        }

        private int GetEffectiveQuantity()
        {
            return HasSimulatedTrades() ? Math.Max(1, simulatedQuantity) : Math.Max(1, BaseContracts);
        }

        private double GetEffectiveAverageEntry()
        {
            if (HasSimulatedTrades() && simulatedAverageEntry > 0 && !double.IsNaN(simulatedAverageEntry) && !double.IsInfinity(simulatedAverageEntry))
                return simulatedAverageEntry;

            return workingEntryPrice;
        }

        private double GetEffectiveOriginalEntryReferencePrice()
        {
            if (originalEntryReferencePrice > 0 && !double.IsNaN(originalEntryReferencePrice) && !double.IsInfinity(originalEntryReferencePrice))
                return originalEntryReferencePrice;

            return workingEntryPrice;
        }

        private void ResetSimulationState()
        {
            simulatedTradeHistory.Clear();
            consumedLadderIndices.Clear();
            RemoveAllConsumedLadderReferenceLines();
            simulatedQuantity = Math.Max(1, BaseContracts);
            simulatedAverageEntry = workingEntryPrice;
            originalEntryReferencePrice = 0;
            RemoveOriginalEntryReferenceLine();

            if (workingEntryPrice > 0 && !double.IsNaN(workingEntryPrice) && !double.IsInfinity(workingEntryPrice))
                ApplyConfiguredProtectionPrices(workingEntryPrice, simulatedQuantity);

            UpdateOverlayButtons(true);
        }

        private void ResetRuntimeScaleLevelContractsToDefaults()
        {
            runtimeScaleLevelContracts.Clear();
            runtimeScaleLevelSpacingTicks.Clear();

            int levelCount = Math.Min(MaxRuntimeScaleLevelCount, Math.Max(MinRuntimeScaleLevelCount, ScaleLevelCount));
            int defaultContracts = Math.Min(MaxScaleLevelContracts, Math.Max(MinScaleLevelContracts, ContractsPerScaleLevel));
            int defaultSpacingTicks = GetConfiguredDefaultSpacingTicks();
            for (int i = 0; i < levelCount; i++)
            {
                runtimeScaleLevelContracts.Add(defaultContracts);
                runtimeScaleLevelSpacingTicks.Add(defaultSpacingTicks);
            }

            runtimeScaleLevelsInitialized = true;
            lastRuntimeScaleLevelSignature = int.MinValue;
        }

        private void EnsureRuntimeScaleLevelContractsSeeded()
        {
            if (!runtimeScaleLevelsInitialized
                || runtimeScaleLevelSpacingTicks.Count != runtimeScaleLevelContracts.Count)
                ResetRuntimeScaleLevelContractsToDefaults();
        }

        private int GetRuntimeScaleLevelCount()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            return runtimeScaleLevelContracts.Count;
        }

        private int GetRuntimeScaleLevelContractsValue(int ladderIndex)
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (ladderIndex < 0 || ladderIndex >= runtimeScaleLevelContracts.Count)
                return Math.Min(MaxScaleLevelContracts, Math.Max(MinScaleLevelContracts, ContractsPerScaleLevel));

            return runtimeScaleLevelContracts[ladderIndex];
        }

        private int GetRuntimeScaleLevelContractsTotal()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            return runtimeScaleLevelContracts.Sum();
        }

        private int GetConfiguredDefaultSpacingTicks()
        {
            double tickSize = GetSafeTickSize();
            if (tickSize <= 0)
                return MinScaleLevelSpacingTicks;

            double spacingPrice = SpacingUnit == ScaleLadderSpacingUnit.Ticks
                ? SpacingValue * tickSize
                : SpacingValue;
            if (spacingPrice <= 0)
                return MinScaleLevelSpacingTicks;

            int spacingTicks = (int)Math.Round(Math.Abs(spacingPrice) / tickSize, MidpointRounding.AwayFromZero);
            return Math.Min(MaxScaleLevelSpacingTicks, Math.Max(MinScaleLevelSpacingTicks, spacingTicks));
        }

        private int GetRuntimeScaleLevelSpacingTicksValue(int ladderIndex)
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (ladderIndex < 0 || ladderIndex >= runtimeScaleLevelSpacingTicks.Count)
                return GetConfiguredDefaultSpacingTicks();

            return runtimeScaleLevelSpacingTicks[ladderIndex];
        }

        private string GetRuntimeScaleLevelSpacingSummary()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (runtimeScaleLevelSpacingTicks.Count == 0)
                return "n/a";

            if (runtimeScaleLevelSpacingTicks.All(ticks => ticks == runtimeScaleLevelSpacingTicks[0]))
                return runtimeScaleLevelSpacingTicks[0].ToString(CultureInfo.InvariantCulture);

            int previewCount = Math.Min(5, runtimeScaleLevelSpacingTicks.Count);
            string preview = string.Join("/", runtimeScaleLevelSpacingTicks.Take(previewCount).Select(ticks => ticks.ToString(CultureInfo.InvariantCulture)));
            if (runtimeScaleLevelSpacingTicks.Count > previewCount)
                preview += "/...";

            return preview;
        }

        private string GetRuntimeScaleLevelContractsSummary()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (runtimeScaleLevelContracts.Count == 0)
                return "n/a";

            if (runtimeScaleLevelContracts.All(qty => qty == runtimeScaleLevelContracts[0]))
                return runtimeScaleLevelContracts[0].ToString(CultureInfo.InvariantCulture);

            int previewCount = Math.Min(5, runtimeScaleLevelContracts.Count);
            string preview = string.Join("/", runtimeScaleLevelContracts.Take(previewCount).Select(qty => qty.ToString(CultureInfo.InvariantCulture)));
            if (runtimeScaleLevelContracts.Count > previewCount)
                preview += "/...";

            return preview;
        }

        private int GetRuntimeScaleLevelSignature()
        {
            EnsureRuntimeScaleLevelContractsSeeded();

            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Math.Max(1, BaseContracts);
                hash = (hash * 31) + (int)StopType;
                hash = (hash * 31) + (int)TargetType;
                hash = (hash * 31) + (int)Math.Round(GetOverlayDisplayedProtectionDollars(true) * 100.0, MidpointRounding.AwayFromZero);
                hash = (hash * 31) + (int)Math.Round(GetOverlayDisplayedProtectionDollars(false) * 100.0, MidpointRounding.AwayFromZero);
                foreach (int quantity in runtimeScaleLevelContracts)
                    hash = (hash * 31) + quantity;
                foreach (int ticks in runtimeScaleLevelSpacingTicks)
                    hash = (hash * 31) + ticks;
                return hash;
            }
        }

        private double GetOverlayDisplayedProtectionDollars(bool isStop)
        {
            double entryPrice = workingEntryPrice > 0 ? workingEntryPrice : GetAnchorPrice(entryLine);
            double protectionPrice = isStop ? workingStopPrice : workingTargetPrice;
            if (protectionPrice <= 0)
            {
                double? configured = ResolveConfiguredProtectionPrice(isStop, entryPrice, Math.Max(1, BaseContracts), GetLatestAtrValue());
                if (configured.HasValue)
                    protectionPrice = configured.Value;
            }

            if (entryPrice > 0 && protectionPrice > 0)
                return Math.Abs(PriceDeltaToDollars(protectionPrice - entryPrice, Math.Max(1, BaseContracts), GetDirectionSign()));

            return Math.Max(0.0, isStop ? StopValue : TargetValue);
        }

        private static string FormatOverlayEditorDecimal(double value)
        {
            return Math.Max(0.0, value).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private bool TryGetNextEligibleLadderCandidate(out int ladderIndex, out double ladderPrice)
        {
            ladderIndex = -1;
            ladderPrice = 0;

            if (!IsScaleInMode() || ladderLines == null || ladderLines.Length == 0)
                return false;

            double directionSign = GetDirectionSign();
            double scaleSign = GetScaleSideSign(directionSign, IsScaleOnFavorableSide());
            double boundaryPrice = IsScaleOnFavorableSide() ? workingTargetPrice : workingStopPrice;
            double boundaryDistance = (boundaryPrice - workingEntryPrice) * scaleSign;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < ladderLines.Length; i++)
            {
                if (consumedLadderIndices.Contains(i))
                    continue;

                double price = GetAnchorPrice(ladderLines[i]);
                if (price <= 0)
                    continue;

                double distance = (price - workingEntryPrice) * scaleSign;
                if (distance <= 0)
                    continue;

                if (boundaryDistance > 0 && distance > boundaryDistance + (GetSafeTickSize() * 0.5))
                    continue;

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    ladderIndex = i;
                    ladderPrice = price;
                }
            }

            return ladderIndex >= 0 && ladderPrice > 0;
        }

        private void HandleAddTradeRequest()
        {
            if (!IsScaleInMode())
                return;

            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();

            int ladderIndex;
            double fillPrice;
            if (!TryGetNextEligibleLadderCandidate(out ladderIndex, out fillPrice))
                return;

            int previousQuantity = GetEffectiveQuantity();
            double previousAverage = GetEffectiveAverageEntry();
            int quantityDelta = GetRuntimeScaleLevelContractsValue(ladderIndex);

            if (!HasSimulatedTrades())
                originalEntryReferencePrice = previousAverage;

            int newQuantity = previousQuantity + quantityDelta;
            double newAverage = ((previousAverage * previousQuantity) + (fillPrice * quantityDelta)) / Math.Max(1, newQuantity);

            simulatedTradeHistory.Push(new SimulatedTradeStep
            {
                LadderIndex = ladderIndex,
                FillPrice = fillPrice,
                PreviousQuantity = previousQuantity,
                PreviousAverageEntry = previousAverage
            });

            consumedLadderIndices.Add(ladderIndex);
            simulatedQuantity = newQuantity;
            simulatedAverageEntry = newAverage;
            workingEntryPrice = newAverage;
            ApplyConfiguredProtectionPrices(simulatedAverageEntry, simulatedQuantity);

            if (ladderIndex >= 0 && ladderIndex < ladderLines.Length)
                ladderLines[ladderIndex] = null;
            if (ladderIndex >= 0 && ladderIndex < ladderLineTags.Length && !string.IsNullOrEmpty(ladderLineTags[ladderIndex]))
                RemoveDrawObject(ladderLineTags[ladderIndex]);
            EnsureConsumedLadderReferenceLine(ladderIndex, fillPrice);

            if (entryLine != null)
                SetAnchorPrice(entryLine, simulatedAverageEntry);

            UpdateProtectionVisuals();
            UpdateOverlayButtons(true);
            ForceRefresh();
        }

        private void HandleRemoveTradeRequest()
        {
            if (!HasSimulatedTrades())
                return;

            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();

            SimulatedTradeStep step = simulatedTradeHistory.Pop();
            consumedLadderIndices.Remove(step.LadderIndex);
            RestoreConsumedLadderLine(step);

            simulatedQuantity = Math.Max(1, step.PreviousQuantity);
            simulatedAverageEntry = step.PreviousAverageEntry;
            workingEntryPrice = simulatedAverageEntry;
            ApplyConfiguredProtectionPrices(simulatedAverageEntry, simulatedQuantity);

            if (entryLine != null)
                SetAnchorPrice(entryLine, simulatedAverageEntry);

            if (!HasSimulatedTrades())
            {
                originalEntryReferencePrice = 0;
                RemoveOriginalEntryReferenceLine();
            }

            UpdateProtectionVisuals();
            UpdateOverlayButtons(true);
            ForceRefresh();
        }

        private void HandleResetRequest()
        {
            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();

            double restoredEntryPrice = originalEntryReferencePrice;
            if (restoredEntryPrice > 0 && !double.IsNaN(restoredEntryPrice) && !double.IsInfinity(restoredEntryPrice))
            {
                RestoreAllConsumedLadderLinesFromHistory();
                ResetSimulationState();

                workingEntryPrice = restoredEntryPrice;
                simulatedAverageEntry = restoredEntryPrice;
                simulatedQuantity = Math.Max(1, BaseContracts);
                ApplyConfiguredProtectionPrices(restoredEntryPrice, simulatedQuantity);

                if (entryLine != null)
                    SetAnchorPrice(entryLine, restoredEntryPrice);

                EnsureLadderLines(false);
            }
            else
            {
                EnsureLadderLines(false);
            }

            UpdateProtectionVisuals();
            UpdateOverlayButtons(true);
            UpdateLadderEditorRows(true);
            ForceRefresh();
        }

        private void RestoreAllConsumedLadderLinesFromHistory()
        {
            foreach (SimulatedTradeStep step in simulatedTradeHistory.Reverse())
            {
                if (step == null || step.LadderIndex < 0)
                    continue;

                RestoreConsumedLadderLine(step);
            }
        }

        private void RestoreConsumedLadderLine(SimulatedTradeStep step)
        {
            if (step == null || step.LadderIndex < 0 || step.LadderIndex >= ladderLineTags.Length)
                return;

            string tag = ladderLineTags[step.LadderIndex];
            if (string.IsNullOrWhiteSpace(tag))
                return;

            RemoveConsumedLadderReferenceLine(step.LadderIndex);
            Brush ladderBrush = GetLadderLineBrush(IsScaleOnFavorableSide());
            HorizontalLine line = Draw.HorizontalLine(this, tag, step.FillPrice, ladderBrush);
            ApplyLadderLineStyle(line, ladderBrush);
            SetAnchorPrice(line, step.FillPrice);
            ladderLines[step.LadderIndex] = line;
        }

        private void TryInitializeChartOverlayButtons()
        {
            if (overlayButtonsAdded || overlayButtonsInitializing || ChartControl == null)
                return;

            overlayButtonsInitializing = true;

            try
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (overlayButtonsAdded || ChartControl == null)
                            return;

                        chartOverlayHost = FindOverlayHostPanel(ChartControl);
                        if (chartOverlayHost == null)
                            return;

                        overlayCanvas = new Canvas
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        overlayCanvas.SizeChanged += OverlayCanvas_SizeChanged;

                        overlayButtonPanel = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            Margin = new Thickness(0),
                            MinWidth = 148,
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };

                        addTradeButton = CreateOverlayButton("Add Trade", Brushes.SteelBlue, "Simulate filling the nearest scale-in level.");
                        removeTradeButton = CreateOverlayButton("Remove Trade", Brushes.IndianRed, "Undo the last simulated add.");
                        resetButton = CreateOverlayButton("Reset", Brushes.MediumPurple, "Restore the original entry and refresh the ladder without changing current ladder prices or per-level contract counts.");
                        addLevelButton = CreateOverlayButton("Add Level", Brushes.SeaGreen, "Append one more scale level using the configured spacing.");
                        removeLevelButton = CreateOverlayButton("Remove Level", Brushes.IndianRed, "Remove the farthest scale level.");

                        addTradeButton.Margin = new Thickness(0, 0, 0, 4);
                        addTradeButton.Click += AddTradeButton_Click;
                        removeTradeButton.Click += RemoveTradeButton_Click;
                        resetButton.Margin = new Thickness(0, 0, 0, 6);
                        resetButton.Click += ResetButton_Click;
                        addLevelButton.Margin = new Thickness(0, 0, 4, 0);
                        addLevelButton.Click += AddLevelButton_Click;
                        removeLevelButton.Click += RemoveLevelButton_Click;

                        overlayDragThumb = new Thumb
                        {
                            Height = 18,
                            Cursor = Cursors.SizeAll,
                            Background = Brushes.Transparent,
                            Margin = new Thickness(0)
                        };
                        overlayDragThumb.DragDelta += OverlayDragThumb_DragDelta;

                        overlayTopResizeThumb = new Thumb
                        {
                            Height = 8,
                            Cursor = Cursors.SizeNS,
                            Background = new SolidColorBrush(Color.FromArgb(95, 74, 90, 110)),
                            Margin = new Thickness(8, 8, 8, 4),
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Top
                        };
                        overlayTopResizeThumb.DragDelta += OverlayTopResizeThumb_DragDelta;

                        overlayButtonPanel.Children.Add(addTradeButton);
                        overlayButtonPanel.Children.Add(removeTradeButton);
                        overlayButtonPanel.Children.Add(resetButton);
                        stopDollarsTextBox = CreateOverlayEditorTextBox(LadderEditorFieldKind.StopDollars, "Configured stop loss in dollars. Editing this switches stop mode to Dollars.");
                        targetDollarsTextBox = CreateOverlayEditorTextBox(LadderEditorFieldKind.TargetDollars, "Configured take profit in dollars. Editing this switches target mode to Dollars.");
                        baseContractsTextBox = CreateOverlayEditorTextBox(LadderEditorFieldKind.BaseContracts, "Base contract quantity used for the ladder plan.");
                        overlayButtonPanel.Children.Add(CreateOverlayEditorInputRow("Stop ($)", stopDollarsTextBox, new Thickness(0, 0, 0, 4)));
                        overlayButtonPanel.Children.Add(CreateOverlayEditorInputRow("Target ($)", targetDollarsTextBox, new Thickness(0, 0, 0, 4)));
                        overlayButtonPanel.Children.Add(CreateOverlayEditorInputRow("Base Qty", baseContractsTextBox, new Thickness(0, 0, 0, 6)));
                        overlayButtonPanel.Children.Add(new TextBlock
                        {
                            Text = "Ladder Levels",
                            Margin = new Thickness(0, 6, 0, 4),
                            Foreground = Brushes.Gainsboro,
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 11
                        });

                        ladderEditorRowsPanel = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            Margin = new Thickness(0, 0, 0, 4)
                        };
                        overlayButtonPanel.Children.Add(ladderEditorRowsPanel);

                        var ladderButtonPanel = new Grid
                        {
                            HorizontalAlignment = HorizontalAlignment.Stretch
                        };
                        ladderButtonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        ladderButtonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        addLevelButton.Margin = new Thickness(0, 0, 4, 0);
                        removeLevelButton.Margin = new Thickness(4, 0, 0, 0);
                        ladderButtonPanel.Children.Add(addLevelButton);
                        ladderButtonPanel.Children.Add(removeLevelButton);
                        Grid.SetColumn(addLevelButton, 0);
                        Grid.SetColumn(removeLevelButton, 1);
                        overlayButtonPanel.Children.Add(ladderButtonPanel);

                        var overlayContentScroller = new ScrollViewer
                        {
                            Content = overlayButtonPanel,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch,
                            Margin = new Thickness(0)
                        };

                        overlayResizeThumb = new Thumb
                        {
                            Width = 16,
                            Height = 16,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Thickness(0, 0, 2, 2),
                            Cursor = Cursors.SizeNWSE,
                            Background = new SolidColorBrush(Color.FromArgb(110, 74, 90, 110)),
                            BorderBrush = Brushes.SlateGray,
                            BorderThickness = new Thickness(1)
                        };
                        overlayResizeThumb.DragDelta += OverlayResizeThumb_DragDelta;

                        var overlayHeaderBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(155, 48, 58, 74)),
                            CornerRadius = new CornerRadius(4, 4, 0, 0),
                            Padding = new Thickness(8, 3, 8, 3),
                            Margin = new Thickness(0, 0, 0, 8)
                        };

                        var overlayHeaderGrid = new Grid();
                        overlayHeaderGrid.Children.Add(new TextBlock
                        {
                            Text = "Ladder Tools",
                            Foreground = Brushes.Gainsboro,
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 11,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        overlayHeaderGrid.Children.Add(overlayDragThumb);
                        overlayHeaderBorder.Child = overlayHeaderGrid;

                        var overlayBottomGripPanel = new Grid
                        {
                            Height = 18,
                            Margin = new Thickness(0, 6, 0, 0)
                        };
                        overlayBottomGripPanel.Children.Add(overlayResizeThumb);

                        overlayChromeGrid = new Grid();
                        overlayChromeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        overlayChromeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        overlayChromeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                        overlayChromeGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        overlayChromeGrid.Children.Add(overlayTopResizeThumb);
                        Grid.SetRow(overlayTopResizeThumb, 0);
                        overlayChromeGrid.Children.Add(overlayHeaderBorder);
                        Grid.SetRow(overlayHeaderBorder, 1);
                        overlayChromeGrid.Children.Add(overlayContentScroller);
                        Grid.SetRow(overlayContentScroller, 2);
                        overlayChromeGrid.Children.Add(overlayBottomGripPanel);
                        Grid.SetRow(overlayBottomGripPanel, 3);

                        overlayButtonBorder = new Border
                        {
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            Width = OverlayPanelDefaultWidth,
                            Height = OverlayPanelDefaultHeight,
                            MinWidth = OverlayPanelMinWidth,
                            MinHeight = OverlayPanelMinHeight,
                            Padding = new Thickness(10),
                            CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Color.FromArgb(150, 12, 18, 26)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 74, 90, 110)),
                            BorderThickness = new Thickness(1),
                            Child = overlayChromeGrid
                        };
                        overlayButtonBorder.Loaded += OverlayButtonBorder_Loaded;

                        if (chartOverlayHost is Grid grid)
                        {
                            Grid.SetRow(overlayCanvas, Grid.GetRow(ChartControl));
                            Grid.SetColumn(overlayCanvas, Grid.GetColumn(ChartControl));
                            Grid.SetRowSpan(overlayCanvas, Math.Max(1, Grid.GetRowSpan(ChartControl)));
                            Grid.SetColumnSpan(overlayCanvas, Math.Max(1, Grid.GetColumnSpan(ChartControl)));
                        }

                        System.Windows.Controls.Panel.SetZIndex(overlayCanvas, OverlayButtonZIndex);
                        chartOverlayHost.Children.Add(overlayCanvas);
                        overlayCanvas.Children.Add(overlayButtonBorder);

                        overlayButtonsAdded = true;
                        UpdateOverlayButtons(true);
                        UpdateLadderEditorRows(true);
                    }
                    finally
                    {
                        overlayButtonsInitializing = false;
                    }
                });
            }
            catch
            {
                overlayButtonsInitializing = false;
            }
        }

        private Button CreateOverlayButton(string content, Brush background, string toolTip)
        {
            return new Button
            {
                Content = content,
                MinWidth = 84,
                Margin = new Thickness(0, 0, 0, 3),
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                Background = background,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ToolTip = toolTip
            };
        }

        private TextBox CreateOverlayEditorTextBox(LadderEditorFieldKind fieldKind, string toolTip)
        {
            var textBox = new TextBox
            {
                MinWidth = 72,
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Tag = new LadderEditorTextBoxTag { LadderIndex = -1, FieldKind = fieldKind },
                FontSize = 11,
                ToolTip = toolTip
            };
            AttachOverlayEditorTextBoxHandlers(textBox);
            return textBox;
        }

        private Grid CreateOverlayEditorInputRow(string label, TextBox textBox, Thickness margin)
        {
            var row = new Grid
            {
                Margin = margin,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = Brushes.Gainsboro,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            row.Children.Add(labelBlock);
            Grid.SetColumn(labelBlock, 0);
            row.Children.Add(textBox);
            Grid.SetColumn(textBox, 2);
            return row;
        }

        private void AttachOverlayEditorTextBoxHandlers(TextBox textBox)
        {
            if (textBox == null)
                return;

            textBox.PreviewMouseDown += LadderLevelContractsTextBox_PreviewMouseDown;
            textBox.PreviewTextInput += LadderLevelContractsTextBox_PreviewTextInput;
            textBox.PreviewKeyDown += LadderLevelContractsTextBox_PreviewKeyDown;
            textBox.LostFocus += LadderLevelContractsTextBox_LostFocus;
        }

        private void DetachOverlayEditorTextBoxHandlers(TextBox textBox)
        {
            if (textBox == null)
                return;

            textBox.PreviewMouseDown -= LadderLevelContractsTextBox_PreviewMouseDown;
            textBox.PreviewTextInput -= LadderLevelContractsTextBox_PreviewTextInput;
            textBox.PreviewKeyDown -= LadderLevelContractsTextBox_PreviewKeyDown;
            textBox.LostFocus -= LadderLevelContractsTextBox_LostFocus;
        }

        private void OverlayButtonBorder_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeOverlayPanelPosition();
        }

        private void OverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            InitializeOverlayPanelPosition();
            ClampOverlayPanelToCanvasBounds();
        }

        private void OverlayDragThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (overlayCanvas == null || overlayButtonBorder == null)
                return;

            overlayPanelPositionInitialized = true;
            SetOverlayPanelPosition(
                GetOverlayPanelLeft() + e.HorizontalChange,
                GetOverlayPanelTop() + e.VerticalChange);
        }

        private void OverlayResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (overlayCanvas == null || overlayButtonBorder == null)
                return;

            overlayPanelPositionInitialized = true;
            double canvasWidth = Math.Max(OverlayPanelMinWidth, overlayCanvas.ActualWidth);
            double canvasHeight = Math.Max(OverlayPanelMinHeight, overlayCanvas.ActualHeight);
            double currentWidth = overlayButtonBorder.ActualWidth > 0 ? overlayButtonBorder.ActualWidth : overlayButtonBorder.Width;
            double currentHeight = overlayButtonBorder.ActualHeight > 0 ? overlayButtonBorder.ActualHeight : overlayButtonBorder.Height;

            double maxWidth = Math.Max(OverlayPanelMinWidth, canvasWidth - OverlayPanelEdgePadding);
            double maxHeight = Math.Max(OverlayPanelMinHeight, canvasHeight - OverlayPanelEdgePadding);
            overlayButtonBorder.Width = Math.Max(OverlayPanelMinWidth, Math.Min(maxWidth, currentWidth + e.HorizontalChange));
            overlayButtonBorder.Height = Math.Max(OverlayPanelMinHeight, Math.Min(maxHeight, currentHeight + e.VerticalChange));
            ClampOverlayPanelToCanvasBounds();
        }

        private void OverlayTopResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (overlayCanvas == null || overlayButtonBorder == null)
                return;

            overlayPanelPositionInitialized = true;

            double canvasHeight = Math.Max(OverlayPanelMinHeight, overlayCanvas.ActualHeight);
            double currentTop = GetOverlayPanelTop();
            double currentHeight = overlayButtonBorder.ActualHeight > 0 ? overlayButtonBorder.ActualHeight : overlayButtonBorder.Height;
            double currentBottom = currentTop + currentHeight;
            double maxBottom = Math.Max(
                OverlayPanelEdgePadding + OverlayPanelMinHeight,
                Math.Min(canvasHeight - OverlayPanelEdgePadding, currentBottom));

            double proposedTop = currentTop + e.VerticalChange;
            double maxTop = maxBottom - OverlayPanelMinHeight;
            double newTop = Math.Max(OverlayPanelEdgePadding, Math.Min(maxTop, proposedTop));
            double newHeight = Math.Max(OverlayPanelMinHeight, maxBottom - newTop);

            overlayButtonBorder.Height = newHeight;
            Canvas.SetTop(overlayButtonBorder, newTop);
            ClampOverlayPanelToCanvasBounds();
        }

        private void InitializeOverlayPanelPosition()
        {
            if (overlayPanelPositionInitialized || overlayCanvas == null || overlayButtonBorder == null)
                return;

            double canvasWidth = overlayCanvas.ActualWidth;
            double canvasHeight = overlayCanvas.ActualHeight;
            if (canvasWidth <= 0 || canvasHeight <= 0)
                return;

            double panelWidth = !double.IsNaN(overlayButtonBorder.Width) && overlayButtonBorder.Width > 0
                ? overlayButtonBorder.Width
                : OverlayPanelDefaultWidth;
            double panelHeight = !double.IsNaN(overlayButtonBorder.Height) && overlayButtonBorder.Height > 0
                ? overlayButtonBorder.Height
                : OverlayPanelDefaultHeight;

            double left = Math.Max(OverlayPanelEdgePadding, canvasWidth - panelWidth - OverlayPanelEdgePadding);
            double top = Math.Max(OverlayPanelEdgePadding, canvasHeight - panelHeight - OverlayPanelEdgePadding);
            Canvas.SetLeft(overlayButtonBorder, left);
            Canvas.SetTop(overlayButtonBorder, top);
            overlayPanelPositionInitialized = true;
        }

        private double GetOverlayPanelLeft()
        {
            if (overlayButtonBorder == null)
                return OverlayPanelEdgePadding;

            double left = Canvas.GetLeft(overlayButtonBorder);
            return double.IsNaN(left) ? OverlayPanelEdgePadding : left;
        }

        private double GetOverlayPanelTop()
        {
            if (overlayButtonBorder == null)
                return OverlayPanelEdgePadding;

            double top = Canvas.GetTop(overlayButtonBorder);
            return double.IsNaN(top) ? OverlayPanelEdgePadding : top;
        }

        private void SetOverlayPanelPosition(double left, double top)
        {
            if (overlayCanvas == null || overlayButtonBorder == null)
                return;

            double panelWidth = overlayButtonBorder.ActualWidth > 0 ? overlayButtonBorder.ActualWidth : overlayButtonBorder.Width;
            double panelHeight = overlayButtonBorder.ActualHeight > 0 ? overlayButtonBorder.ActualHeight : overlayButtonBorder.Height;
            double maxLeft = Math.Max(OverlayPanelEdgePadding, overlayCanvas.ActualWidth - panelWidth - OverlayPanelEdgePadding);
            double maxTop = Math.Max(OverlayPanelEdgePadding, overlayCanvas.ActualHeight - panelHeight - OverlayPanelEdgePadding);

            Canvas.SetLeft(overlayButtonBorder, Math.Max(OverlayPanelEdgePadding, Math.Min(maxLeft, left)));
            Canvas.SetTop(overlayButtonBorder, Math.Max(OverlayPanelEdgePadding, Math.Min(maxTop, top)));
        }

        private void ClampOverlayPanelToCanvasBounds()
        {
            if (overlayCanvas == null || overlayButtonBorder == null)
                return;

            SetOverlayPanelPosition(GetOverlayPanelLeft(), GetOverlayPanelTop());
        }

        private void UpdateOverlayButtons(bool force = false)
        {
            if (ChartControl == null || addTradeButton == null || removeTradeButton == null)
                return;

            EnsureRuntimeScaleLevelContractsSeeded();
            bool addEnabled = IsScaleInMode() && TryGetNextEligibleLadderCandidate(out _, out _);
            bool removeEnabled = IsScaleInMode() && HasSimulatedTrades();
            bool addLevelEnabled = runtimeScaleLevelContracts.Count < MaxRuntimeScaleLevelCount;
            bool removeLevelEnabled = runtimeScaleLevelContracts.Count > 0;

            if (!force
                && addEnabled == lastAddTradeEnabled
                && removeEnabled == lastRemoveTradeEnabled
                && addLevelEnabled == lastAddLevelEnabled
                && removeLevelEnabled == lastRemoveLevelEnabled)
                return;

            lastAddTradeEnabled = addEnabled;
            lastRemoveTradeEnabled = removeEnabled;
            lastAddLevelEnabled = addLevelEnabled;
            lastRemoveLevelEnabled = removeLevelEnabled;

            Action apply = () =>
            {
                ApplyOverlayButtonState(addTradeButton, addEnabled, Brushes.SteelBlue);
                ApplyOverlayButtonState(removeTradeButton, removeEnabled, Brushes.IndianRed);
                ApplyOverlayButtonState(resetButton, true, Brushes.MediumPurple);
                ApplyOverlayButtonState(addLevelButton, addLevelEnabled, Brushes.SeaGreen);
                ApplyOverlayButtonState(removeLevelButton, removeLevelEnabled, Brushes.IndianRed);
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void ApplyOverlayButtonState(Button button, bool enabled, Brush activeBrush)
        {
            if (button == null)
                return;

            button.IsEnabled = enabled;
            button.Background = enabled ? activeBrush : Brushes.DimGray;
            button.Foreground = enabled ? Brushes.White : Brushes.LightGray;
            button.Opacity = enabled ? 1.0 : 0.7;
        }

        private void RemoveChartOverlayButtons()
        {
            overlayButtonsInitializing = false;

            if (!overlayButtonsAdded
                && overlayButtonBorder == null
                && !measurementDragHandlersAttached)
                return;

            Action remove = () =>
            {
                if (addTradeButton != null)
                    addTradeButton.Click -= AddTradeButton_Click;
                if (removeTradeButton != null)
                    removeTradeButton.Click -= RemoveTradeButton_Click;
                if (resetButton != null)
                    resetButton.Click -= ResetButton_Click;
                if (addLevelButton != null)
                    addLevelButton.Click -= AddLevelButton_Click;
                if (removeLevelButton != null)
                    removeLevelButton.Click -= RemoveLevelButton_Click;
                DetachOverlayEditorTextBoxHandlers(stopDollarsTextBox);
                DetachOverlayEditorTextBoxHandlers(targetDollarsTextBox);
                DetachOverlayEditorTextBoxHandlers(baseContractsTextBox);
                if (overlayDragThumb != null)
                    overlayDragThumb.DragDelta -= OverlayDragThumb_DragDelta;
                if (overlayTopResizeThumb != null)
                    overlayTopResizeThumb.DragDelta -= OverlayTopResizeThumb_DragDelta;
                if (overlayResizeThumb != null)
                    overlayResizeThumb.DragDelta -= OverlayResizeThumb_DragDelta;
                if (overlayCanvas != null)
                    overlayCanvas.SizeChanged -= OverlayCanvas_SizeChanged;
                if (overlayButtonBorder != null)
                    overlayButtonBorder.Loaded -= OverlayButtonBorder_Loaded;
                if (measurementDragHostControl != null && measurementDragHandlersAttached)
                {
                    measurementDragHostControl.PreviewMouseLeftButtonDown -= ChartControl_PreviewMouseLeftButtonDown;
                    measurementDragHostControl.PreviewMouseMove -= ChartControl_PreviewMouseMove;
                    measurementDragHostControl.PreviewMouseLeftButtonUp -= ChartControl_PreviewMouseLeftButtonUp;
                    measurementDragHostControl.LostMouseCapture -= ChartControl_LostMouseCapture;
                }

                EndMeasurementLabelDrag();
                measurementDragHandlersAttached = false;
                measurementDragHostControl = null;
                originalMeasurementLabelBounds = Rect.Empty;
                spacingMeasurementLabelBounds.Clear();

                ClearLadderEditorRows();

                if (overlayCanvas != null && overlayButtonBorder != null)
                    overlayCanvas.Children.Remove(overlayButtonBorder);
                if (overlayCanvas != null && chartOverlayHost != null)
                    chartOverlayHost.Children.Remove(overlayCanvas);

                chartOverlayHost = null;
                overlayCanvas = null;
                overlayButtonBorder = null;
                overlayChromeGrid = null;
                overlayButtonPanel = null;
                ladderEditorRowsPanel = null;
                overlayDragThumb = null;
                overlayTopResizeThumb = null;
                overlayResizeThumb = null;
                addTradeButton = null;
                removeTradeButton = null;
                resetButton = null;
                addLevelButton = null;
                removeLevelButton = null;
                stopDollarsTextBox = null;
                targetDollarsTextBox = null;
                baseContractsTextBox = null;
                overlayButtonsAdded = false;
                lastAddTradeEnabled = false;
                lastRemoveTradeEnabled = false;
                lastAddLevelEnabled = false;
                lastRemoveLevelEnabled = false;
                lastRuntimeScaleLevelSignature = int.MinValue;
                overlayPanelPositionInitialized = false;
            };

            if (ChartControl != null && !ChartControl.Dispatcher.CheckAccess())
                ChartControl.Dispatcher.InvokeAsync(remove);
            else
                remove();
        }

        private Panel FindOverlayHostPanel(DependencyObject child)
        {
            DependencyObject current = child;
            while (current != null)
            {
                current = VisualTreeHelper.GetParent(current);
                if (current is Panel panel)
                    return panel;
            }

            return null;
        }

        private void AddTradeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(_ => HandleAddTradeRequest(), null);
        }

        private void RemoveTradeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(_ => HandleRemoveTradeRequest(), null);
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(_ => HandleResetRequest(), null);
        }

        private void AddLevelButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(_ => HandleAddLevelRequest(), null);
        }

        private void RemoveLevelButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(_ => HandleRemoveLevelRequest(), null);
        }

        private void LadderLevelContractsTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null || textBox.IsKeyboardFocusWithin)
                return;

            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
            e.Handled = true;
        }

        private void LadderLevelContractsTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = true;
        }

        private void LadderLevelContractsTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null)
                return;

            LadderEditorTextBoxTag tag = textBox.Tag as LadderEditorTextBoxTag;
            LadderEditorFieldKind fieldKind = tag != null ? tag.FieldKind : LadderEditorFieldKind.Contracts;

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                CommitLadderLevelContractsTextBox(textBox);
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                UpdateLadderEditorRows(true);
                textBox.SelectAll();
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
                return;

            if (e.Key == Key.Back
                || e.Key == Key.Delete)
            {
                ApplyLadderLevelTextDelete(textBox, deleteBackward: e.Key == Key.Back);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Tab
                || e.Key == Key.Left
                || e.Key == Key.Right
                || e.Key == Key.Home
                || e.Key == Key.End)
                return;

            if (IsDecimalEditorField(fieldKind)
                && (e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.Separator))
            {
                if (!string.IsNullOrEmpty(textBox.Text) && textBox.Text.Contains("."))
                {
                    e.Handled = true;
                    return;
                }

                ApplyLadderLevelTextEdit(textBox, ".");
                e.Handled = true;
                return;
            }

            string insertText;
            if (TryGetLadderLevelInsertText(e.Key, out insertText))
            {
                ApplyLadderLevelTextEdit(textBox, insertText);
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private void LadderLevelContractsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (overlayEditorSyncing)
                return;

            CommitLadderLevelContractsTextBox(sender as TextBox);
        }

        private void CommitLadderLevelContractsTextBox(TextBox textBox)
        {
            if (textBox == null)
                return;

            LadderEditorTextBoxTag tag = textBox.Tag as LadderEditorTextBoxTag;
            if (tag == null)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            string requestedText = textBox.Text;
            switch (tag.FieldKind)
            {
                case LadderEditorFieldKind.StopDollars:
                    TriggerCustomEvent(_ => HandleOverlayStopDollarsEdit(requestedText), null);
                    break;
                case LadderEditorFieldKind.TargetDollars:
                    TriggerCustomEvent(_ => HandleOverlayTargetDollarsEdit(requestedText), null);
                    break;
                case LadderEditorFieldKind.BaseContracts:
                    TriggerCustomEvent(_ => HandleOverlayBaseContractsEdit(requestedText), null);
                    break;
                case LadderEditorFieldKind.Contracts:
                    TriggerCustomEvent(_ => HandleRuntimeLevelContractsEdit(tag.LadderIndex, requestedText), null);
                    break;
                default:
                    TriggerCustomEvent(_ => HandleRuntimeLevelSpacingEdit(tag.LadderIndex, requestedText), null);
                    break;
            }
        }

        private bool IsDecimalEditorField(LadderEditorFieldKind fieldKind)
        {
            return fieldKind == LadderEditorFieldKind.StopDollars
                || fieldKind == LadderEditorFieldKind.TargetDollars;
        }

        private bool TryGetLadderLevelInsertText(Key key, out string insertText)
        {
            insertText = null;

            char digit;
            if (!TryGetDigitFromKey(key, out digit))
                return false;

            insertText = digit.ToString();
            return true;
        }

        private static bool TryGetDigitFromKey(Key key, out char digit)
        {
            digit = '\0';

            if (key >= Key.D0 && key <= Key.D9)
            {
                digit = (char)('0' + (key - Key.D0));
                return true;
            }

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
            {
                digit = (char)('0' + (key - Key.NumPad0));
                return true;
            }

            return false;
        }

        private void ApplyLadderLevelTextDelete(TextBox textBox, bool deleteBackward)
        {
            if (textBox == null)
                return;

            string current = textBox.Text ?? string.Empty;
            int start = textBox.SelectionStart;
            int length = textBox.SelectionLength;
            if (start < 0)
                start = 0;
            if (start > current.Length)
                start = current.Length;
            if (length < 0)
                length = 0;
            if (start + length > current.Length)
                length = current.Length - start;

            if (length > 0)
            {
                current = current.Remove(start, length);
            }
            else if (deleteBackward)
            {
                if (start <= 0 || current.Length <= 0)
                    return;

                current = current.Remove(start - 1, 1);
                start--;
            }
            else
            {
                if (start >= current.Length)
                    return;

                current = current.Remove(start, 1);
            }

            textBox.Text = current;
            textBox.SelectionStart = Math.Max(0, Math.Min(start, current.Length));
            textBox.SelectionLength = 0;
        }

        private void ApplyLadderLevelTextEdit(TextBox textBox, string insertText)
        {
            if (textBox == null || string.IsNullOrEmpty(insertText))
                return;

            string current = textBox.Text ?? string.Empty;
            int start = textBox.SelectionStart;
            int length = textBox.SelectionLength;
            if (start < 0)
                start = 0;
            if (start > current.Length)
                start = current.Length;
            if (length < 0)
                length = 0;
            if (start + length > current.Length)
                length = current.Length - start;

            string updated = current.Remove(start, length).Insert(start, insertText);
            textBox.Text = updated;
            textBox.SelectionStart = Math.Min(updated.Length, start + insertText.Length);
            textBox.SelectionLength = 0;
        }

        private void HandleRuntimeLevelContractsEdit(int ladderIndex, string requestedText)
        {
            EnsureRuntimeScaleLevelContractsSeeded();

            if (ladderIndex < 0 || ladderIndex >= runtimeScaleLevelContracts.Count)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            int parsedValue;
            if (!int.TryParse(requestedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue)
                || parsedValue < MinScaleLevelContracts
                || parsedValue > MaxScaleLevelContracts)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            if (runtimeScaleLevelContracts[ladderIndex] == parsedValue)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            runtimeScaleLevelContracts[ladderIndex] = parsedValue;
            HandleRuntimeScaleLevelConfigurationChanged();
        }

        private void HandleRuntimeLevelSpacingEdit(int ladderIndex, string requestedText)
        {
            EnsureRuntimeScaleLevelContractsSeeded();

            if (ladderIndex < 0 || ladderIndex >= runtimeScaleLevelSpacingTicks.Count)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            int parsedValue;
            if (!int.TryParse(requestedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue)
                || parsedValue < MinScaleLevelSpacingTicks
                || parsedValue > MaxScaleLevelSpacingTicks)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            if (runtimeScaleLevelSpacingTicks[ladderIndex] == parsedValue)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            runtimeScaleLevelSpacingTicks[ladderIndex] = parsedValue;
            HandleRuntimeScaleLevelConfigurationChanged(true);
        }

        private void HandleOverlayBaseContractsEdit(string requestedText)
        {
            int parsedValue;
            if (!int.TryParse(requestedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue)
                || parsedValue < 1
                || parsedValue > MaxScaleLevelContracts)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            if (BaseContracts == parsedValue)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            BaseContracts = parsedValue;
            HandleOverlayConfigurationEdit(IsDollarProtectionMode(true) || IsDollarProtectionMode(false));
        }

        private void HandleOverlayStopDollarsEdit(string requestedText)
        {
            double parsedValue;
            if (!double.TryParse(requestedText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue)
                || parsedValue <= 0)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            parsedValue = Math.Round(parsedValue, 2, MidpointRounding.AwayFromZero);
            if (StopType == ScaleLadderProtectionKind.Dollars && Math.Abs(StopValue - parsedValue) < 0.0001)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            StopType = ScaleLadderProtectionKind.Dollars;
            StopValue = parsedValue;
            HandleOverlayConfigurationEdit(true);
        }

        private void HandleOverlayTargetDollarsEdit(string requestedText)
        {
            double parsedValue;
            if (!double.TryParse(requestedText, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedValue)
                || parsedValue <= 0)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            parsedValue = Math.Round(parsedValue, 2, MidpointRounding.AwayFromZero);
            if (TargetType == ScaleLadderProtectionKind.Dollars && Math.Abs(TargetValue - parsedValue) < 0.0001)
            {
                UpdateLadderEditorRows(true);
                return;
            }

            TargetType = ScaleLadderProtectionKind.Dollars;
            TargetValue = parsedValue;
            HandleOverlayConfigurationEdit(true);
        }

        private void HandleOverlayConfigurationEdit(bool recalculateProtectionFromConfig)
        {
            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();
            ResetSimulationState();

            if (recalculateProtectionFromConfig && workingEntryPrice > 0 && !double.IsNaN(workingEntryPrice) && !double.IsInfinity(workingEntryPrice))
                ApplyConfiguredProtectionPrices(workingEntryPrice, Math.Max(1, BaseContracts));

            EnsureLadderLines(false);
            UpdateProtectionVisuals();
            UpdateOverlayButtons(true);
            UpdateLadderEditorRows(true);
            ForceRefresh();
        }

        private void HandleAddLevelRequest()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (runtimeScaleLevelContracts.Count >= MaxRuntimeScaleLevelCount)
                return;

            int templateIndex = runtimeScaleLevelContracts.Count - 1;
            int nextContracts = templateIndex >= 0
                ? Math.Min(MaxScaleLevelContracts, Math.Max(MinScaleLevelContracts, runtimeScaleLevelContracts[templateIndex]))
                : Math.Min(MaxScaleLevelContracts, Math.Max(MinScaleLevelContracts, ContractsPerScaleLevel));
            int nextSpacingTicks = templateIndex >= 0
                ? Math.Min(MaxScaleLevelSpacingTicks, Math.Max(MinScaleLevelSpacingTicks, runtimeScaleLevelSpacingTicks[templateIndex]))
                : GetConfiguredDefaultSpacingTicks();

            runtimeScaleLevelContracts.Add(nextContracts);
            runtimeScaleLevelSpacingTicks.Add(nextSpacingTicks);
            HandleRuntimeScaleLevelConfigurationChanged(true);
        }

        private void HandleRemoveLevelRequest()
        {
            EnsureRuntimeScaleLevelContractsSeeded();
            if (runtimeScaleLevelContracts.Count <= 0)
                return;

            runtimeScaleLevelContracts.RemoveAt(runtimeScaleLevelContracts.Count - 1);
            runtimeScaleLevelSpacingTicks.RemoveAt(runtimeScaleLevelSpacingTicks.Count - 1);
            HandleRuntimeScaleLevelConfigurationChanged();
        }

        private void HandleRuntimeScaleLevelConfigurationChanged(bool reseedLadderPrices = false)
        {
            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();
            ResetSimulationState();
            EnsureLadderLines(false);
            if (reseedLadderPrices)
                ApplyRuntimeScaleLevelSpacingToLadderLines();
            UpdateProtectionVisuals();
            UpdateOverlayButtons(true);
            UpdateLadderEditorRows(true);
            ForceRefresh();
        }

        private void ApplyRuntimeScaleLevelSpacingToLadderLines()
        {
            if (ladderLines == null || ladderLineTags == null)
                return;

            Brush ladderBrush = GetLadderLineBrush(IsScaleOnFavorableSide());
            for (int i = 0; i < ladderLines.Length; i++)
            {
                if (consumedLadderIndices.Contains(i))
                    continue;

                ladderLines[i] = FindHorizontalLine(ladderLineTags[i]) ?? ladderLines[i];
                if (ladderLines[i] == null)
                    continue;

                ApplyLadderLineStyle(ladderLines[i], ladderBrush);
                SetAnchorPrice(ladderLines[i], GetSeededLadderPrice(i + 1));
            }
        }

        private void ClearLadderEditorRows()
        {
            foreach (TextBox textBox in ladderLevelContractTextBoxes)
            {
                if (textBox == null)
                    continue;

                textBox.PreviewMouseDown -= LadderLevelContractsTextBox_PreviewMouseDown;
                textBox.PreviewTextInput -= LadderLevelContractsTextBox_PreviewTextInput;
                textBox.PreviewKeyDown -= LadderLevelContractsTextBox_PreviewKeyDown;
                textBox.LostFocus -= LadderLevelContractsTextBox_LostFocus;
            }

            ladderLevelContractTextBoxes.Clear();
            foreach (TextBox textBox in ladderLevelSpacingTextBoxes)
            {
                if (textBox == null)
                    continue;

                textBox.PreviewMouseDown -= LadderLevelContractsTextBox_PreviewMouseDown;
                textBox.PreviewTextInput -= LadderLevelContractsTextBox_PreviewTextInput;
                textBox.PreviewKeyDown -= LadderLevelContractsTextBox_PreviewKeyDown;
                textBox.LostFocus -= LadderLevelContractsTextBox_LostFocus;
            }

            ladderLevelSpacingTextBoxes.Clear();
            if (ladderEditorRowsPanel != null)
                ladderEditorRowsPanel.Children.Clear();
        }

        private void UpdateLadderEditorRows(bool force = false)
        {
            if (ChartControl == null || ladderEditorRowsPanel == null)
                return;

            EnsureRuntimeScaleLevelContractsSeeded();
            int signature = GetRuntimeScaleLevelSignature();

            Action apply = () =>
            {
                if (ladderEditorRowsPanel == null)
                    return;

                overlayEditorSyncing = true;
                try
                {
                    if (stopDollarsTextBox != null)
                    {
                        stopDollarsTextBox.Tag = new LadderEditorTextBoxTag { LadderIndex = -1, FieldKind = LadderEditorFieldKind.StopDollars };
                        string desiredStopText = FormatOverlayEditorDecimal(GetOverlayDisplayedProtectionDollars(true));
                        if (!stopDollarsTextBox.IsKeyboardFocusWithin && !string.Equals(stopDollarsTextBox.Text, desiredStopText, StringComparison.Ordinal))
                            stopDollarsTextBox.Text = desiredStopText;
                    }

                    if (targetDollarsTextBox != null)
                    {
                        targetDollarsTextBox.Tag = new LadderEditorTextBoxTag { LadderIndex = -1, FieldKind = LadderEditorFieldKind.TargetDollars };
                        string desiredTargetText = FormatOverlayEditorDecimal(GetOverlayDisplayedProtectionDollars(false));
                        if (!targetDollarsTextBox.IsKeyboardFocusWithin && !string.Equals(targetDollarsTextBox.Text, desiredTargetText, StringComparison.Ordinal))
                            targetDollarsTextBox.Text = desiredTargetText;
                    }

                    if (baseContractsTextBox != null)
                    {
                        baseContractsTextBox.Tag = new LadderEditorTextBoxTag { LadderIndex = -1, FieldKind = LadderEditorFieldKind.BaseContracts };
                        string desiredBaseContractsText = Math.Max(1, BaseContracts).ToString(CultureInfo.InvariantCulture);
                        if (!baseContractsTextBox.IsKeyboardFocusWithin && !string.Equals(baseContractsTextBox.Text, desiredBaseContractsText, StringComparison.Ordinal))
                            baseContractsTextBox.Text = desiredBaseContractsText;
                    }

                    if (force
                        || ladderLevelContractTextBoxes.Count != runtimeScaleLevelContracts.Count
                        || ladderLevelSpacingTextBoxes.Count != runtimeScaleLevelSpacingTicks.Count)
                    {
                        ClearLadderEditorRows();

                        var headerRow = new Grid
                        {
                            Margin = new Thickness(0, 0, 0, 3)
                        };
                        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        headerRow.Children.Add(new TextBlock
                        {
                            Text = "Qty",
                            Margin = new Thickness(24, 0, 0, 0),
                            Foreground = Brushes.Gainsboro,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center
                        });
                        Grid.SetColumn(headerRow.Children[headerRow.Children.Count - 1], 1);
                        headerRow.Children.Add(new TextBlock
                        {
                            Text = "Ticks",
                            Foreground = Brushes.Gainsboro,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        });
                        Grid.SetColumn(headerRow.Children[headerRow.Children.Count - 1], 3);
                        ladderEditorRowsPanel.Children.Add(headerRow);

                        for (int i = 0; i < runtimeScaleLevelContracts.Count; i++)
                        {
                            var rowPanel = new Grid
                            {
                                Margin = new Thickness(0, 0, 0, 2)
                            };
                            rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                            rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                            rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                            rowPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                            var label = new TextBlock
                            {
                                Text = string.Format(CultureInfo.InvariantCulture, "L{0}", i + 1),
                                Width = 22,
                                Margin = new Thickness(0, 0, 6, 0),
                                VerticalAlignment = VerticalAlignment.Center,
                                Foreground = Brushes.White,
                                FontSize = 11
                            };

                            var contractTextBox = new TextBox
                            {
                                MinWidth = 46,
                                Padding = new Thickness(4, 1, 4, 1),
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                Tag = new LadderEditorTextBoxTag { LadderIndex = i, FieldKind = LadderEditorFieldKind.Contracts },
                                FontSize = 11,
                                ToolTip = string.Format(CultureInfo.InvariantCulture, "Contracts for scale level L{0}.", i + 1)
                            };
                            contractTextBox.PreviewMouseDown += LadderLevelContractsTextBox_PreviewMouseDown;
                            contractTextBox.PreviewTextInput += LadderLevelContractsTextBox_PreviewTextInput;
                            contractTextBox.PreviewKeyDown += LadderLevelContractsTextBox_PreviewKeyDown;
                            contractTextBox.LostFocus += LadderLevelContractsTextBox_LostFocus;

                            var spacingTextBox = new TextBox
                            {
                                MinWidth = 56,
                                Padding = new Thickness(4, 1, 4, 1),
                                HorizontalContentAlignment = HorizontalAlignment.Center,
                                HorizontalAlignment = HorizontalAlignment.Stretch,
                                Tag = new LadderEditorTextBoxTag { LadderIndex = i, FieldKind = LadderEditorFieldKind.SpacingTicks },
                                FontSize = 11,
                                ToolTip = string.Format(CultureInfo.InvariantCulture, "Ticks from {0} to L{1}.", i == 0 ? "entry" : "L" + i.ToString(CultureInfo.InvariantCulture), i + 1)
                            };
                            spacingTextBox.PreviewMouseDown += LadderLevelContractsTextBox_PreviewMouseDown;
                            spacingTextBox.PreviewTextInput += LadderLevelContractsTextBox_PreviewTextInput;
                            spacingTextBox.PreviewKeyDown += LadderLevelContractsTextBox_PreviewKeyDown;
                            spacingTextBox.LostFocus += LadderLevelContractsTextBox_LostFocus;

                            rowPanel.Children.Add(label);
                            Grid.SetColumn(label, 0);
                            rowPanel.Children.Add(contractTextBox);
                            Grid.SetColumn(contractTextBox, 1);
                            rowPanel.Children.Add(spacingTextBox);
                            Grid.SetColumn(spacingTextBox, 3);
                            ladderEditorRowsPanel.Children.Add(rowPanel);
                            ladderLevelContractTextBoxes.Add(contractTextBox);
                            ladderLevelSpacingTextBoxes.Add(spacingTextBox);
                        }
                    }

                    for (int i = 0; i < runtimeScaleLevelContracts.Count && i < ladderLevelContractTextBoxes.Count; i++)
                    {
                        TextBox contractTextBox = ladderLevelContractTextBoxes[i];
                        if (contractTextBox == null)
                            continue;

                        contractTextBox.Tag = new LadderEditorTextBoxTag { LadderIndex = i, FieldKind = LadderEditorFieldKind.Contracts };
                        string desiredContractsText = runtimeScaleLevelContracts[i].ToString(CultureInfo.InvariantCulture);
                        if (!contractTextBox.IsKeyboardFocusWithin && !string.Equals(contractTextBox.Text, desiredContractsText, StringComparison.Ordinal))
                            contractTextBox.Text = desiredContractsText;
                    }

                    for (int i = 0; i < runtimeScaleLevelSpacingTicks.Count && i < ladderLevelSpacingTextBoxes.Count; i++)
                    {
                        TextBox spacingTextBox = ladderLevelSpacingTextBoxes[i];
                        if (spacingTextBox == null)
                            continue;

                        spacingTextBox.Tag = new LadderEditorTextBoxTag { LadderIndex = i, FieldKind = LadderEditorFieldKind.SpacingTicks };
                        string desiredSpacingText = runtimeScaleLevelSpacingTicks[i].ToString(CultureInfo.InvariantCulture);
                        if (!spacingTextBox.IsKeyboardFocusWithin && !string.Equals(spacingTextBox.Text, desiredSpacingText, StringComparison.Ordinal))
                            spacingTextBox.Text = desiredSpacingText;
                    }

                    lastRuntimeScaleLevelSignature = signature;
                }
                finally
                {
                    overlayEditorSyncing = false;
                }
            };

            if (!force
                && signature == lastRuntimeScaleLevelSignature
                && ladderLevelContractTextBoxes.Count == runtimeScaleLevelContracts.Count
                && ladderLevelSpacingTextBoxes.Count == runtimeScaleLevelSpacingTicks.Count)
                return;

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateProtectionVisuals()
        {
            if (ChartControl == null)
            {
                RemoveProtectionLabelDrawObjects();
                RemoveOriginalEntryReferenceLine();
                return;
            }

            stopLabelObject = FindTextObject(stopLabelTag);
            targetLabelObject = FindTextObject(targetLabelTag);
            originalEntryReferenceLine = FindHorizontalLine(originalEntryReferenceLineTag);

            double originalEntryPrice = GetEffectiveOriginalEntryReferencePrice();
            double liveAveragePrice = GetEffectiveAverageEntry();
            int liveQuantity = GetEffectiveQuantity();

            if (HasSimulatedTrades() && !PricesClose(originalEntryPrice, liveAveragePrice))
            {
                originalEntryReferenceLine = EnsureOriginalEntryReferenceLine(originalEntryPrice);
            }
            else
            {
                RemoveOriginalEntryReferenceLine();
            }

            if (workingStopPrice > 0)
            {
                double defaultStopLabelOffset = GetProtectionDefaultLabelPriceOffset(true);
                SyncProtectionLabelPlacementFromChart(stopLabelObject, stopLabelRenderedPrice, defaultStopLabelOffset, ref stopLabelAnchorTime, ref stopLabelPriceOffset);
                string stopLabel = BuildProtectionLabel(workingStopPrice, originalEntryPrice, liveAveragePrice, liveQuantity);
                if (!string.IsNullOrWhiteSpace(stopLabel))
                {
                    if (ShouldRedrawProtectionLabel(stopLabelObject, stopLabelRenderedText, stopLabel, stopLabelRenderedPrice, workingStopPrice))
                    {
                        stopLabelObject = DrawProtectionLabel(
                            stopLabelObject,
                            stopLabelTag,
                            stopLabel,
                            workingStopPrice,
                            Brushes.OrangeRed,
                            defaultStopLabelOffset,
                            ref stopLabelAnchorTime,
                            ref stopLabelPriceOffset);
                        stopLabelRenderedPrice = workingStopPrice;
                        stopLabelRenderedText = stopLabel;
                    }
                }
                else
                {
                    RemoveDrawObject(stopLabelTag);
                    stopLabelObject = null;
                    stopLabelAnchorTime = DateTime.MinValue;
                    stopLabelPriceOffset = double.NaN;
                    stopLabelRenderedPrice = 0;
                    stopLabelRenderedText = null;
                }
            }
            else
            {
                RemoveDrawObject(stopLabelTag);
                stopLabelObject = null;
                stopLabelAnchorTime = DateTime.MinValue;
                stopLabelPriceOffset = double.NaN;
                stopLabelRenderedPrice = 0;
                stopLabelRenderedText = null;
            }

            if (workingTargetPrice > 0)
            {
                double defaultTargetLabelOffset = GetProtectionDefaultLabelPriceOffset(false);
                SyncProtectionLabelPlacementFromChart(targetLabelObject, targetLabelRenderedPrice, defaultTargetLabelOffset, ref targetLabelAnchorTime, ref targetLabelPriceOffset);
                string targetLabel = BuildProtectionLabel(workingTargetPrice, originalEntryPrice, liveAveragePrice, liveQuantity);
                if (!string.IsNullOrWhiteSpace(targetLabel))
                {
                    if (ShouldRedrawProtectionLabel(targetLabelObject, targetLabelRenderedText, targetLabel, targetLabelRenderedPrice, workingTargetPrice))
                    {
                        targetLabelObject = DrawProtectionLabel(
                            targetLabelObject,
                            targetLabelTag,
                            targetLabel,
                            workingTargetPrice,
                            Brushes.DeepSkyBlue,
                            defaultTargetLabelOffset,
                            ref targetLabelAnchorTime,
                            ref targetLabelPriceOffset);
                        targetLabelRenderedPrice = workingTargetPrice;
                        targetLabelRenderedText = targetLabel;
                    }
                }
                else
                {
                    RemoveDrawObject(targetLabelTag);
                    targetLabelObject = null;
                    targetLabelAnchorTime = DateTime.MinValue;
                    targetLabelPriceOffset = double.NaN;
                    targetLabelRenderedPrice = 0;
                    targetLabelRenderedText = null;
                }
            }
            else
            {
                RemoveDrawObject(targetLabelTag);
                targetLabelObject = null;
                targetLabelAnchorTime = DateTime.MinValue;
                targetLabelPriceOffset = double.NaN;
                targetLabelRenderedPrice = 0;
                targetLabelRenderedText = null;
            }
        }

        private string BuildProtectionLabel(double linePrice, double originalEntryPrice, double liveAveragePrice, int quantity)
        {
            if (originalEntryPrice <= 0 || liveAveragePrice <= 0 || quantity <= 0)
                return string.Empty;

            double originalEntryDelta = linePrice - originalEntryPrice;
            double liveAverageDelta = linePrice - liveAveragePrice;
            double liveDollars = PriceDeltaToDollars(liveAverageDelta, quantity, GetDirectionSign());
            double originalTicks = PriceDeltaToSignedTicks(originalEntryDelta, GetDirectionSign());

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1} ({2})",
                FormatProtectionCurrency(liveDollars),
                FormatProtectionPriceUnitDelta(originalEntryDelta),
                FormatProtectionTickDelta(originalTicks));
        }

        private string FormatProtectionPriceUnitDelta(double delta)
        {
            string format = "F" + GetPriceUnitDecimals().ToString(CultureInfo.InvariantCulture);
            if (delta > 0)
                return "+" + Math.Abs(delta).ToString(format, CultureInfo.InvariantCulture);
            if (delta < 0)
                return "-" + Math.Abs(delta).ToString(format, CultureInfo.InvariantCulture);
            return 0.0.ToString(format, CultureInfo.InvariantCulture);
        }

        private string FormatProtectionTickDelta(double ticks)
        {
            double roundedTicks = Math.Round(ticks, 2, MidpointRounding.AwayFromZero);
            if (roundedTicks > 0)
                return "+" + roundedTicks.ToString("0.##", CultureInfo.InvariantCulture) + "t";
            if (roundedTicks < 0)
                return "-" + Math.Abs(roundedTicks).ToString("0.##", CultureInfo.InvariantCulture) + "t";
            return "0t";
        }

        private string FormatProtectionCurrency(double value)
        {
            string prefix = value >= 0 ? "+$" : "-$";
            return prefix + Math.Abs(value).ToString("N2", CultureInfo.InvariantCulture);
        }

        private double GetProtectionDefaultLabelPriceOffset(bool isStop)
        {
            double tickSize = GetSafeTickSize();
            double direction = isStop ? 1.0 : -1.0;
            return direction * (tickSize * ProtectionLabelDefaultTickOffset);
        }

        private double NormalizeProtectionLabelPriceOffset(double priceOffset, double defaultPriceOffset)
        {
            if (double.IsNaN(priceOffset) || double.IsInfinity(priceOffset))
                return defaultPriceOffset;

            double tickSize = GetSafeTickSize();
            if (tickSize <= 0)
                return priceOffset;

            return Math.Round(priceOffset / tickSize) * tickSize;
        }

        private void SyncProtectionLabelPlacementFromChart(
            NinjaTrader.NinjaScript.DrawingTools.Text label,
            double renderedLinePrice,
            double defaultPriceOffset,
            ref DateTime cachedTime,
            ref double cachedPriceOffset)
        {
            if (label == null || label.Anchor == null)
                return;

            if (label.Anchor.Time != DateTime.MinValue)
                cachedTime = label.Anchor.Time;

            double anchorPrice = label.Anchor.Price;
            if (anchorPrice <= 0 || double.IsNaN(anchorPrice) || double.IsInfinity(anchorPrice))
                return;

            double baselinePrice = renderedLinePrice > 0
                ? renderedLinePrice
                : anchorPrice - defaultPriceOffset;
            cachedPriceOffset = NormalizeProtectionLabelPriceOffset(anchorPrice - baselinePrice, defaultPriceOffset);
        }

        private DateTime ResolveProtectionLabelTime(NinjaTrader.NinjaScript.DrawingTools.Text label, DateTime cachedTime)
        {
            if (label != null && label.Anchor != null && label.Anchor.Time != DateTime.MinValue)
                return label.Anchor.Time;

            if (cachedTime != DateTime.MinValue)
                return cachedTime;

            if (Time != null && Time.Count > 0)
            {
                int barsAgo = Math.Min(CurrentBar, ProtectionLabelDefaultBarsAgo);
                if (barsAgo >= 0 && barsAgo < Time.Count)
                    return Time[barsAgo];

                return Time[0];
            }

            return DateTime.UtcNow;
        }

        private int ResolveProtectionLabelBarsAgo(NinjaTrader.NinjaScript.DrawingTools.Text label, DateTime cachedTime)
        {
            DateTime anchorTime = ResolveProtectionLabelTime(label, cachedTime);
            if (Time == null || Time.Count == 0)
                return 0;

            if (anchorTime != DateTime.MinValue && Bars != null)
            {
                int barIndex = Bars.GetBar(anchorTime);
                if (barIndex >= 0)
                    return Math.Max(0, Math.Min(CurrentBar, CurrentBar - barIndex));
            }

            return Math.Max(0, Math.Min(CurrentBar, ProtectionLabelDefaultBarsAgo));
        }

        private NinjaTrader.NinjaScript.DrawingTools.Text DrawProtectionLabel(
            NinjaTrader.NinjaScript.DrawingTools.Text label,
            string tag,
            string text,
            double linePrice,
            Brush brush,
            double defaultPriceOffset,
            ref DateTime cachedTime,
            ref double cachedPriceOffset)
        {
            int barsAgo = ResolveProtectionLabelBarsAgo(label, cachedTime);
            double priceOffset = NormalizeProtectionLabelPriceOffset(cachedPriceOffset, defaultPriceOffset);
            double anchorPrice = RoundToTick(linePrice + priceOffset);
            var font = new SimpleFont("Arial", 11) { Bold = true };
            label = Draw.Text(
                this,
                tag,
                false,
                text,
                barsAgo,
                anchorPrice,
                0,
                brush,
                font,
                System.Windows.TextAlignment.Right,
                null,
                null,
                0);
            if (label != null)
            {
                label.IsLocked = false;
                if (Time != null && Time.Count > 0)
                {
                    int clampedBarsAgo = Math.Max(0, Math.Min(CurrentBar, barsAgo));
                    if (clampedBarsAgo >= 0 && clampedBarsAgo < Time.Count)
                        cachedTime = Time[clampedBarsAgo];
                }

                if (label.Anchor != null)
                {
                    if (label.Anchor.Time != DateTime.MinValue)
                        cachedTime = label.Anchor.Time;

                    double actualAnchorPrice = label.Anchor.Price;
                    if (actualAnchorPrice > 0 && !double.IsNaN(actualAnchorPrice) && !double.IsInfinity(actualAnchorPrice))
                        cachedPriceOffset = NormalizeProtectionLabelPriceOffset(actualAnchorPrice - linePrice, defaultPriceOffset);
                }
            }
            return label;
        }

        private bool ShouldRedrawProtectionLabel(
            NinjaTrader.NinjaScript.DrawingTools.Text label,
            string renderedText,
            string desiredText,
            double renderedLinePrice,
            double desiredLinePrice)
        {
            if (label == null)
                return true;

            if (string.IsNullOrWhiteSpace(desiredText))
                return false;

            if (!string.Equals(renderedText ?? string.Empty, desiredText ?? string.Empty, StringComparison.Ordinal))
                return true;

            if (renderedLinePrice <= 0 || desiredLinePrice <= 0)
                return true;

            return !PricesClose(renderedLinePrice, desiredLinePrice);
        }

        private HorizontalLine EnsureOriginalEntryReferenceLine(double desiredPrice)
        {
            if (desiredPrice <= 0)
                return null;

            originalEntryReferenceLine = FindHorizontalLine(originalEntryReferenceLineTag) ?? originalEntryReferenceLine;
            if (originalEntryReferenceLine == null)
                originalEntryReferenceLine = Draw.HorizontalLine(this, originalEntryReferenceLineTag, desiredPrice, Brushes.MediumPurple);

            if (originalEntryReferenceLine == null)
                return null;

            ApplyOriginalEntryReferenceLineStyle(originalEntryReferenceLine);

            double currentPrice = GetAnchorPrice(originalEntryReferenceLine);
            if (currentPrice <= 0 || !PricesClose(currentPrice, desiredPrice))
                SetAnchorPrice(originalEntryReferenceLine, desiredPrice);

            return originalEntryReferenceLine;
        }

        private bool PricesClose(double left, double right)
        {
            double tolerance = Math.Max(1e-6, GetSafeTickSize() * 0.25);
            return Math.Abs(left - right) <= tolerance;
        }

        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", GroupName = "05 - Parameters", Order = 0)]
        public ScaleLadderTradeDirection TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Scale Mode", GroupName = "05 - Parameters", Order = 1)]
        public ScaleLadderMode ScaleMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Spacing Unit", GroupName = "05 - Parameters", Order = 2)]
        public ScaleLadderSpacingUnit SpacingUnit { get; set; }

        [NinjaScriptProperty]
        [Range(2, 100)]
        [Display(Name = "Base Atr Period", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 0)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "StopType", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 1)]
        public ScaleLadderProtectionKind StopType { get; set; }

        [Browsable(false)]
        public int StopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100000.0)]
        [Display(Name = "StopValue", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 2)]
        public double StopValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TargetType", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 3)]
        public ScaleLadderProtectionKind TargetType { get; set; }

        [Browsable(false)]
        public int TargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 100000.0)]
        [Display(Name = "TargetValue", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 4)]
        public double TargetValue { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Base Contracts", GroupName = "05 - Parameters", Order = 5)]
        public int BaseContracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Contracts Per Scale Level", GroupName = "05 - Parameters", Order = 6)]
        public int ContractsPerScaleLevel { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Scale Level Count", GroupName = "05 - Parameters", Order = 7)]
        public int ScaleLevelCount { get; set; }

        [NinjaScriptProperty]
        [Range(0.0001, 1000000.0)]
        [Display(Name = "Spacing Value", GroupName = "05 - Parameters", Order = 8)]
        public double SpacingValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Guide Lines", GroupName = "06 - Display", Order = 9)]
        public bool ShowGuideLines { get; set; }

        [Browsable(false)]
        public bool ShowLevelLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Summary Panel", GroupName = "06 - Display", Order = 10)]
        public bool ShowSummaryPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset Anchors", GroupName = "06 - Display", Order = 11)]
        public bool ResetAnchors { get; set; }

        [XmlIgnore]
        [Display(Name = "Entry Line Brush", GroupName = "06 - Display", Order = 12)]
        public Brush EntryLineBrush { get; set; }

        [Browsable(false)]
        public string EntryLineBrushSerializable
        {
            get { return Serialize.BrushToString(EntryLineBrush); }
            set { EntryLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Risk Side Brush", GroupName = "06 - Display", Order = 13)]
        public Brush RiskSideBrush { get; set; }

        [Browsable(false)]
        public string RiskSideBrushSerializable
        {
            get { return Serialize.BrushToString(RiskSideBrush); }
            set { RiskSideBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Reward Side Brush", GroupName = "06 - Display", Order = 14)]
        public Brush RewardSideBrush { get; set; }

        [Browsable(false)]
        public string RewardSideBrushSerializable
        {
            get { return Serialize.BrushToString(RewardSideBrush); }
            set { RewardSideBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Summary Brush", GroupName = "06 - Display", Order = 15)]
        public Brush SummaryBrush { get; set; }

        [Browsable(false)]
        public string SummaryBrushSerializable
        {
            get { return Serialize.BrushToString(SummaryBrush); }
            set { SummaryBrush = Serialize.StringToBrush(value); }
        }
    }
}
