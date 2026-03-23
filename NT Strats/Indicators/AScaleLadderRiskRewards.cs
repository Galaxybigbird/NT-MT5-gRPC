#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
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

        private const float ColumnWidth = 220f;
        private const float LabelVerticalGap = 18f;
        private const float SummaryLineHeight = 16f;

        private HorizontalLine entryLine;
        private HorizontalLine stopLine;
        private HorizontalLine targetLine;
        private HorizontalLine[] ladderLines = new HorizontalLine[0];
        private SimpleFont renderFont;

        private string entryLineTag;
        private string stopLineTag;
        private string targetLineTag;
        private string tagPrefix;
        private string[] ladderLineTags = new string[0];

        private double workingEntryPrice;
        private double workingStopPrice;
        private double workingTargetPrice;
        private bool anchorsSeeded;

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
                StopTicks = 80;
                TargetTicks = 160;
                BaseContracts = 1;
                ContractsPerScaleLevel = 1;
                ScaleLevelCount = 5;
                SpacingValue = 20;
                ShowGuideLines = true;
                ShowLevelLabels = true;
                ShowSummaryPanel = true;
                ResetAnchors = false;

                EntryLineBrush = Brushes.DodgerBlue;
                RiskSideBrush = Brushes.OrangeRed;
                RewardSideBrush = Brushes.LimeGreen;
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
            }
            else if (State == State.Historical)
            {
                SetZOrder(1);
            }
            else if (State == State.Terminated)
            {
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

            EnsureAnchorLines();
            SyncWorkingPricesFromAnchors();
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

                DrawBasePriceLabel(textFormat, entryDxBrush, string.Format(CultureInfo.InvariantCulture, "Entry | Base {0}", BaseContracts), lossColumnX, chartScale.GetYByValue(workingEntryPrice) - 16f);
                DrawBasePriceLabel(textFormat, riskDxBrush, BuildStopLineLabel(levels), lossColumnX, chartScale.GetYByValue(workingStopPrice) - 16f);
                DrawBasePriceLabel(textFormat, rewardDxBrush, BuildTargetLineLabel(levels), profitColumnX, chartScale.GetYByValue(workingTargetPrice) - 16f);

                if (ShowLevelLabels)
                {
                    DrawTextBlock(textFormat, mutedDxBrush, "Loss / level state", lossColumnX, Math.Max(summaryBottomY + 6f, panelTop + 62f), ColumnWidth);
                    DrawTextBlock(textFormat, mutedDxBrush, "Target projection", profitColumnX, Math.Max(summaryBottomY + 6f, panelTop + 62f), ColumnWidth);
                }

                if (!validation.CanRenderLevels || levels.Count == 0)
                    return;

                Brush ladderBrush = validation.ScaleOnFavorableSide ? RewardSideBrush : RiskSideBrush;
                using (var ladderDxBrush = ladderBrush.ToDxBrush(RenderTarget))
                {
                    bool suppressedLossLabels = false;
                    bool suppressedProfitLabels = false;
                    float lastLossLabelY = float.MinValue;
                    float lastProfitLabelY = float.MinValue;

                    foreach (var level in levels.OrderBy(l => chartScale.GetYByValue(l.LevelPrice)))
                    {
                        float levelY = chartScale.GetYByValue(level.LevelPrice);
                        if (ShowGuideLines)
                            RenderTarget.DrawLine(new SharpDX.Vector2(lineStartX, levelY), new SharpDX.Vector2(guideLineEndX, levelY), ladderDxBrush, 1f);

                        if (!ShowLevelLabels)
                            continue;

                        string lossText = BuildLossLabel(level);
                        string profitText = BuildProfitLabel(level);

                        if (Math.Abs(levelY - lastLossLabelY) >= LabelVerticalGap)
                        {
                            DrawTextBlock(textFormat, riskDxBrush, lossText, lossColumnX, levelY - 7f, ColumnWidth);
                            lastLossLabelY = levelY;
                        }
                        else
                        {
                            suppressedLossLabels = true;
                        }

                        if (Math.Abs(levelY - lastProfitLabelY) >= LabelVerticalGap)
                        {
                            DrawTextBlock(textFormat, rewardDxBrush, profitText, profitColumnX, levelY - 7f, ColumnWidth);
                            lastProfitLabelY = levelY;
                        }
                        else
                        {
                            suppressedProfitLabels = true;
                        }
                    }

                    if (suppressedLossLabels || suppressedProfitLabels)
                    {
                        DrawTextBlock(
                            textFormat,
                            warningDxBrush,
                            "Some labels were hidden because the levels are too close together.",
                            panelLeft + 10f,
                            Math.Max(summaryBottomY + 6f, panelTop + 78f),
                            panelRight - panelLeft - 20f);
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
                BaseContracts,
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
                int runningQuantity = BaseContracts;
                double runningAverage = workingEntryPrice;

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

            int remainingQuantity = BaseContracts;
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

            if (IsScaleOutMode() && (ContractsPerScaleLevel * ScaleLevelCount) > BaseContracts)
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
                SeedWorkingPrices();

            entryLine = FindHorizontalLine(entryLineTag) ?? entryLine;
            stopLine = FindHorizontalLine(stopLineTag) ?? stopLine;
            targetLine = FindHorizontalLine(targetLineTag) ?? targetLine;

            if (entryLine == null)
                entryLine = Draw.HorizontalLine(this, entryLineTag, workingEntryPrice, EntryLineBrush);
            if (stopLine == null)
                stopLine = Draw.HorizontalLine(this, stopLineTag, workingStopPrice, RiskSideBrush);
            if (targetLine == null)
                targetLine = Draw.HorizontalLine(this, targetLineTag, workingTargetPrice, RewardSideBrush);

            ApplyAnchorStyle(entryLine, EntryLineBrush, DashStyleHelper.Dash, 2);
            ApplyAnchorStyle(stopLine, RiskSideBrush, DashStyleHelper.Solid, 2);
            ApplyAnchorStyle(targetLine, RewardSideBrush, DashStyleHelper.Solid, 2);
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

            Brush ladderBrush = IsScaleOnFavorableSide() ? RewardSideBrush : RiskSideBrush;
            for (int i = 0; i < desiredCount; i++)
            {
                ladderLines[i] = FindHorizontalLine(ladderLineTags[i]) ?? ladderLines[i];
                bool created = false;
                if (ladderLines[i] == null)
                {
                    ladderLines[i] = Draw.HorizontalLine(this, ladderLineTags[i], GetSeededLadderPrice(i + 1), ladderBrush);
                    created = true;
                }

                ApplyAnchorStyle(ladderLines[i], ladderBrush, DashStyleHelper.Dot, 1);
                if (resetRequested || created)
                    SetAnchorPrice(ladderLines[i], GetSeededLadderPrice(i + 1));
            }
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

                prices.Add(RoundToTick(price));
            }

            return prices
                .OrderBy(price => (price - workingEntryPrice) * scaleSign)
                .ToList();
        }

        private void SeedWorkingPrices()
        {
            double tickSize = GetSafeTickSize();
            double closePrice = CurrentBar >= 0 ? Close[0] : 0;
            if (closePrice <= 0)
                closePrice = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.RoundToTickSize(1.0) : 1.0;

            double defaultEntry = RoundToTick(closePrice);
            double stopOffset = Math.Max(1, StopTicks) * tickSize;
            double targetOffset = Math.Max(1, TargetTicks) * tickSize;

            workingEntryPrice = defaultEntry;
            workingStopPrice = RoundToTick(defaultEntry + (TradeDirection == ScaleLadderTradeDirection.Long ? -stopOffset : stopOffset));
            workingTargetPrice = RoundToTick(defaultEntry + (TradeDirection == ScaleLadderTradeDirection.Long ? targetOffset : -targetOffset));
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

            if (entry > 0)
                workingEntryPrice = RoundToTick(entry);
            if (stop > 0)
                workingStopPrice = RoundToTick(stop);
            if (target > 0)
                workingTargetPrice = RoundToTick(target);
        }

        private void ApplyAnchorStyle(HorizontalLine line, Brush brush, DashStyleHelper dashStyle, int width)
        {
            if (line == null)
                return;

            line.IsLocked = false;
            line.Stroke = new Stroke(brush, dashStyle, width);
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

        private void SetAnchorPrice(HorizontalLine line, double price)
        {
            if (line == null || line.StartAnchor == null || price <= 0)
                return;

            line.StartAnchor.Price = RoundToTick(price);
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
            return PriceDeltaToDollars(workingTargetPrice - workingEntryPrice, BaseContracts, directionSign);
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

            return PriceDeltaToDollars(workingStopPrice - workingEntryPrice, BaseContracts, directionSign);
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

        [NinjaScriptProperty]
        [Display(Name = "Trade Direction", GroupName = "Parameters", Order = 0)]
        public ScaleLadderTradeDirection TradeDirection { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Scale Mode", GroupName = "Parameters", Order = 1)]
        public ScaleLadderMode ScaleMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Spacing Unit", GroupName = "Parameters", Order = 2)]
        public ScaleLadderSpacingUnit SpacingUnit { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Stop Ticks", GroupName = "Parameters", Order = 3)]
        public int StopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100000)]
        [Display(Name = "Target Ticks", GroupName = "Parameters", Order = 4)]
        public int TargetTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Base Contracts", GroupName = "Parameters", Order = 5)]
        public int BaseContracts { get; set; }

        [NinjaScriptProperty]
        [Range(1, 1000)]
        [Display(Name = "Contracts Per Scale Level", GroupName = "Parameters", Order = 6)]
        public int ContractsPerScaleLevel { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Scale Level Count", GroupName = "Parameters", Order = 7)]
        public int ScaleLevelCount { get; set; }

        [NinjaScriptProperty]
        [Range(0.0001, 1000000.0)]
        [Display(Name = "Spacing Value", GroupName = "Parameters", Order = 8)]
        public double SpacingValue { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Guide Lines", GroupName = "Display", Order = 9)]
        public bool ShowGuideLines { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Level Labels", GroupName = "Display", Order = 10)]
        public bool ShowLevelLabels { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Summary Panel", GroupName = "Display", Order = 11)]
        public bool ShowSummaryPanel { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Reset Anchors", GroupName = "Display", Order = 12)]
        public bool ResetAnchors { get; set; }

        [XmlIgnore]
        [Display(Name = "Entry Line Brush", GroupName = "Display", Order = 13)]
        public Brush EntryLineBrush { get; set; }

        [Browsable(false)]
        public string EntryLineBrushSerializable
        {
            get { return Serialize.BrushToString(EntryLineBrush); }
            set { EntryLineBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Risk Side Brush", GroupName = "Display", Order = 14)]
        public Brush RiskSideBrush { get; set; }

        [Browsable(false)]
        public string RiskSideBrushSerializable
        {
            get { return Serialize.BrushToString(RiskSideBrush); }
            set { RiskSideBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Reward Side Brush", GroupName = "Display", Order = 15)]
        public Brush RewardSideBrush { get; set; }

        [Browsable(false)]
        public string RewardSideBrushSerializable
        {
            get { return Serialize.BrushToString(RewardSideBrush); }
            set { RewardSideBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Summary Brush", GroupName = "Display", Order = 16)]
        public Brush SummaryBrush { get; set; }

        [Browsable(false)]
        public string SummaryBrushSerializable
        {
            get { return Serialize.BrushToString(SummaryBrush); }
            set { SummaryBrush = Serialize.StringToBrush(value); }
        }
    }
}
