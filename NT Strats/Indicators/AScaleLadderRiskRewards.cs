#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

        private const float ColumnWidth = 220f;
        private const float LabelVerticalGap = 18f;
        private const float SummaryLineHeight = 16f;
        private const int ProtectionLabelDefaultBarsAgo = 20;
        private const int ProtectionLabelDefaultTickOffset = 35;
        private const int OverlayButtonZIndex = 2000;

        private HorizontalLine entryLine;
        private HorizontalLine stopLine;
        private HorizontalLine targetLine;
        private HorizontalLine originalEntryReferenceLine;
        private HorizontalLine[] ladderLines = new HorizontalLine[0];
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

        private double workingEntryPrice;
        private double workingStopPrice;
        private double workingTargetPrice;
        private bool anchorsSeeded;
        private readonly Stack<SimulatedTradeStep> simulatedTradeHistory = new Stack<SimulatedTradeStep>();
        private readonly HashSet<int> consumedLadderIndices = new HashSet<int>();
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
        private Border overlayButtonBorder;
        private StackPanel overlayButtonPanel;
        private Button addTradeButton;
        private Button removeTradeButton;
        private bool overlayButtonsAdded;
        private bool overlayButtonsInitializing;
        private bool lastAddTradeEnabled;
        private bool lastRemoveTradeEnabled;

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
            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();
            UpdateProtectionVisuals();
            UpdateOverlayButtons();
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
            using (var textFormat = renderFont.ToDirectWriteTextFormat())
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

                if (!validation.CanRenderLevels || levels.Count == 0)
                    return;

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
        }

        private float DrawSummaryPanel(TextFormat textFormat, SharpDX.Direct2D1.Brush summaryDxBrush, SharpDX.Direct2D1.Brush warningDxBrush, float x, float y, LadderValidation validation)
        {
            if (!ShowSummaryPanel)
                return y;

            string spacingLabel = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}",
                SpacingValue.ToString("0.####", CultureInfo.InvariantCulture),
                SpacingUnit == ScaleLadderSpacingUnit.Ticks ? "ticks" : "price");

            string line1 = string.Format(
                CultureInfo.InvariantCulture,
                "{0} | {1}",
                TradeDirection,
                ScaleMode);
            string line2 = string.Format(
                CultureInfo.InvariantCulture,
                "Base {0} | Step {1} x {2} | Spacing {3}",
                GetEffectiveQuantity(),
                ContractsPerScaleLevel,
                ScaleLevelCount,
                spacingLabel);
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
            List<double> orderedLevelPrices = GetOrderedLadderPrices(validation);
            if (orderedLevelPrices.Count == 0)
            {
                validation.Warnings.Add("No active ladder levels are available.");
                validation.CanRenderLevels = false;
                return;
            }

            validation.VisibleLevelCount = orderedLevelPrices.Count;

            if (IsScaleInMode())
            {
                int runningQuantity = GetEffectiveQuantity();
                double runningAverage = GetEffectiveAverageEntry();

                for (int i = 0; i < orderedLevelPrices.Count; i++)
                {
                    double levelPrice = orderedLevelPrices[i];
                    int newQuantity = runningQuantity + ContractsPerScaleLevel;
                    double newAverage = ((runningAverage * runningQuantity) + (levelPrice * ContractsPerScaleLevel)) / Math.Max(1, newQuantity);

                    levels.Add(new LadderLevel
                    {
                        LevelNumber = i + 1,
                        LevelPrice = levelPrice,
                        QuantityDelta = ContractsPerScaleLevel,
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

            for (int i = 0; i < orderedLevelPrices.Count; i++)
            {
                double levelPrice = orderedLevelPrices[i];
                remainingQuantity -= ContractsPerScaleLevel;
                realizedPnl += PriceDeltaToDollars(levelPrice - workingEntryPrice, ContractsPerScaleLevel, directionSign);
                double netAtLevel = realizedPnl + PriceDeltaToDollars(levelPrice - workingEntryPrice, remainingQuantity, directionSign);
                double targetPnl = realizedPnl + PriceDeltaToDollars(workingTargetPrice - workingEntryPrice, remainingQuantity, directionSign);

                levels.Add(new LadderLevel
                {
                    LevelNumber = i + 1,
                    LevelPrice = levelPrice,
                    QuantityDelta = ContractsPerScaleLevel,
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

            if (tickSize <= 0)
            {
                validation.Warnings.Add("Instrument tick size is not available.");
                validation.CanRenderLevels = false;
                return validation;
            }

            if (BaseContracts <= 0)
                validation.Warnings.Add("BaseContracts must be at least 1.");

            if (ContractsPerScaleLevel <= 0)
                validation.Warnings.Add("ContractsPerScaleLevel must be at least 1.");

            if (ScaleLevelCount <= 0)
                validation.Warnings.Add("ScaleLevelCount must be at least 1.");

            validation.SpacingPrice = SpacingUnit == ScaleLadderSpacingUnit.Ticks
                ? SpacingValue * tickSize
                : SpacingValue;

            if (validation.SpacingPrice <= 0)
                validation.Warnings.Add("SpacingValue must be greater than 0.");

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
            validation.VisibleLevelCount = ScaleLevelCount;

            if (IsScaleOutMode() && (ContractsPerScaleLevel * ScaleLevelCount) > GetEffectiveQuantity())
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
            int desiredCount = Math.Max(0, ScaleLevelCount);
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
        }

        private Brush GetLadderLineBrush(bool scaleOnFavorableSide)
        {
            if (IsScaleInMode())
                return Brushes.Aqua;

            return scaleOnFavorableSide ? RewardSideBrush : RiskSideBrush;
        }

        private double GetSeededLadderPrice(int levelNumber)
        {
            double spacingPrice = SpacingUnit == ScaleLadderSpacingUnit.Ticks
                ? SpacingValue * GetSafeTickSize()
                : SpacingValue;

            if (spacingPrice <= 0)
                return workingEntryPrice;

            double directionSign = TradeDirection == ScaleLadderTradeDirection.Long ? 1.0 : -1.0;
            double scaleSign = GetScaleSideSign(directionSign, IsScaleOnFavorableSide());
            return RoundToTick(workingEntryPrice + (spacingPrice * scaleSign * levelNumber));
        }

        private List<double> GetOrderedLadderPrices(LadderValidation validation)
        {
            List<double> prices = new List<double>();
            if (ladderLines == null || ladderLines.Length == 0)
                return prices;

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

                prices.Add(price);
            }

            return prices
                .OrderBy(price => (price - workingEntryPrice) * scaleSign)
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

            RemoveProtectionLabelDrawObjects();
            RemoveOriginalEntryReferenceLine();

            entryLine = null;
            stopLine = null;
            targetLine = null;
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
            simulatedQuantity = Math.Max(1, BaseContracts);
            simulatedAverageEntry = workingEntryPrice;
            originalEntryReferencePrice = 0;
            RemoveOriginalEntryReferenceLine();
            UpdateOverlayButtons(true);
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

            if (!HasSimulatedTrades())
                originalEntryReferencePrice = previousAverage;

            int newQuantity = previousQuantity + ContractsPerScaleLevel;
            double newAverage = ((previousAverage * previousQuantity) + (fillPrice * ContractsPerScaleLevel)) / Math.Max(1, newQuantity);

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

        private void RestoreConsumedLadderLine(SimulatedTradeStep step)
        {
            if (step == null || step.LadderIndex < 0 || step.LadderIndex >= ladderLineTags.Length)
                return;

            string tag = ladderLineTags[step.LadderIndex];
            if (string.IsNullOrWhiteSpace(tag))
                return;

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

                        overlayButtonPanel = new StackPanel
                        {
                            Orientation = Orientation.Vertical
                        };

                        addTradeButton = CreateOverlayButton("Add Trade", Brushes.SteelBlue, "Simulate filling the nearest scale-in level.");
                        removeTradeButton = CreateOverlayButton("Remove Trade", Brushes.IndianRed, "Undo the last simulated add.");

                        addTradeButton.Margin = new Thickness(0, 0, 0, 4);
                        addTradeButton.Click += AddTradeButton_Click;
                        removeTradeButton.Click += RemoveTradeButton_Click;

                        overlayButtonPanel.Children.Add(addTradeButton);
                        overlayButtonPanel.Children.Add(removeTradeButton);

                        overlayButtonBorder = new Border
                        {
                            HorizontalAlignment = HorizontalAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Bottom,
                            Margin = new Thickness(0, 0, 14, 18),
                            Padding = new Thickness(4),
                            CornerRadius = new CornerRadius(4),
                            Background = new SolidColorBrush(Color.FromArgb(150, 12, 18, 26)),
                            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 74, 90, 110)),
                            BorderThickness = new Thickness(1),
                            Child = overlayButtonPanel
                        };

                        if (chartOverlayHost is Grid grid)
                        {
                            Grid.SetRow(overlayButtonBorder, Grid.GetRow(ChartControl));
                            Grid.SetColumn(overlayButtonBorder, Grid.GetColumn(ChartControl));
                            Grid.SetRowSpan(overlayButtonBorder, Math.Max(1, Grid.GetRowSpan(ChartControl)));
                            Grid.SetColumnSpan(overlayButtonBorder, Math.Max(1, Grid.GetColumnSpan(ChartControl)));
                        }

                        System.Windows.Controls.Panel.SetZIndex(overlayButtonBorder, OverlayButtonZIndex);
                        chartOverlayHost.Children.Add(overlayButtonBorder);

                        overlayButtonsAdded = true;
                        UpdateOverlayButtons(true);
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
                MinWidth = 96,
                Margin = new Thickness(0),
                Padding = new Thickness(10, 4, 10, 4),
                Background = background,
                Foreground = Brushes.White,
                ToolTip = toolTip
            };
        }

        private void UpdateOverlayButtons(bool force = false)
        {
            if (ChartControl == null || addTradeButton == null || removeTradeButton == null)
                return;

            bool addEnabled = IsScaleInMode() && TryGetNextEligibleLadderCandidate(out _, out _);
            bool removeEnabled = IsScaleInMode() && HasSimulatedTrades();

            if (!force && addEnabled == lastAddTradeEnabled && removeEnabled == lastRemoveTradeEnabled)
                return;

            lastAddTradeEnabled = addEnabled;
            lastRemoveTradeEnabled = removeEnabled;

            Action apply = () =>
            {
                ApplyOverlayButtonState(addTradeButton, addEnabled, Brushes.SteelBlue);
                ApplyOverlayButtonState(removeTradeButton, removeEnabled, Brushes.IndianRed);
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

            if (!overlayButtonsAdded && overlayButtonBorder == null)
                return;

            Action remove = () =>
            {
                if (addTradeButton != null)
                    addTradeButton.Click -= AddTradeButton_Click;
                if (removeTradeButton != null)
                    removeTradeButton.Click -= RemoveTradeButton_Click;

                if (overlayButtonBorder != null && chartOverlayHost != null)
                    chartOverlayHost.Children.Remove(overlayButtonBorder);

                chartOverlayHost = null;
                overlayButtonBorder = null;
                overlayButtonPanel = null;
                addTradeButton = null;
                removeTradeButton = null;
                overlayButtonsAdded = false;
                lastAddTradeEnabled = false;
                lastRemoveTradeEnabled = false;
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
