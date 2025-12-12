#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Shared;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BaseOptStrategyManual : Strategy, ITradeSyncParticipant, IRunUpParticipant
    {
        // --- indicator refs
        private ATR atr;

        // --- internal
        private bool stopSet, targetSet;
        private bool demaTrailingActive;
        private double demaHighWater;
        private double demaLowWater;

        private static long tradeSequence;
        private Dictionary<string, TradeRuntimeState> tradeStates;
        private string activeTradeId;

        private Button manualBuyButton;
        private Button manualSellButton;
        private Button manualFlattenButton;
        private bool manualBuyClickArmed;
        private bool manualSellClickArmed;
        private bool manualFlattenClickArmed;
        private bool manualProcessingReady;
        private Grid manualButtonPanel;
        private Panel manualButtonHost;
        private Grid manualButtonGrid;
        private int manualButtonRowIndex = -1;
        private RowDefinition manualButtonRowDefinition;
        private Button manualButtonStyleSource;
        private readonly List<string> openTradeOrder = new List<string>();
        private const int ManualMinEntriesPerDirection = 8;
        private const int ManualOrderSeriesIndex = 1;
        private const double ExposureFlatTolerance = 1e-6;
        private readonly string manualPanelTag = $"BaseOptManualButtons_{Guid.NewGuid():N}";
        private Chart attachedChart;
        private ChartTrader attachedChartTrader;
        private bool chartTraderVisibilityHooked;
        private bool manualTreeLogged;
        private bool desyncHoldActive;
        private DateTime desyncHoldActivatedAt = DateTime.MinValue;

        private class TradeRuntimeState
        {
            public string TradeId;
            public MarketPosition EntrySide;
            public int OriginalQuantity;
            public int RemainingQuantity;
            public bool OpenPublished;
            public bool IsSynthetic;
            public string InstrumentName;
            public string AccountName;
            public double NtPointsPer1kLoss;
            public double EntryPrice;
            public bool ManualStopOverride;
            public bool ManualTargetOverride;
            public bool PendingAutoStopUpdate;
            public bool PendingAutoTargetUpdate;
            public double PendingAutoStopPrice;
            public double LastStopPrice;
            public double LastTargetPrice;
            public bool RunUpActive;
            public double RunUpAnchorPrice;
            public double RunUpInitialDistance;
            public double RunUpIncrement;
            public double? RunUpLastStopPrice;
            public double RunUpHighWater;
            public double RunUpLowWater;
        }


        #region Defaults
        protected override void OnStateChange()
        {
            try
            {
                if (State == State.SetDefaults)
                {
                    Name = "BaseOptStrategyManual";
                    Calculate = Calculate.OnBarClose;
                    IsOverlay = false;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                // Allow stacked entries so manual Chart Trader buttons can add multiple units.
                EntriesPerDirection = 10;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 60;
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;

                AtrPeriod = 14;

                // Risk params
                StopType = StopKind.ATR;
                AtrStopMult = 2.0;
                StopTicks = 40;
                TargetType = TargetKind.ATR;
                AtrTargetMult = 3.0;
                TargetTicks = 60;

                // Legacy ATR trailing inputs retained for reference only; strategy now always
                // uses shared DEMA-ATR trailing logic that mirrors the AddOn configuration.
                // TrailType = TrailKind.None;
                // AtrTrailMult = 1.5;
                // TrailTicks = 20;

                UseBreakEven = true;
                BreakEvenTriggerTicks = 30;
                BreakEvenPlusTicks = 2;

                Debug = false;
                DemaAtrPeriod = 14;
                DemaAtrMultiplier = 1.5;
                UseDemaAtrTrailing = true;
                DemaAtrActivationMode = TrailingActivationType.Percent;
                DemaAtrActivationValue = 1.0;

                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                activeTradeId = null;
            }
            else if (State == State.Configure)
            {

                if (BarsArray.Length == 1)
                {
                    AddDataSeries(BarsPeriodType.Tick, 1);
                }


                    // optional: log parameters once per iteration for diagnostics
                if (Debug)
                {
                    StrategyLogDebug($"PARAMS: AtrPeriod={AtrPeriod}, StopType={StopType}, StopTicks={StopTicks}, AtrStopMult={AtrStopMult}, TargetType={TargetType}, TargetTicks={TargetTicks}, AtrTargetMult={AtrTargetMult}, UseDemaAtrTrailing={UseDemaAtrTrailing}, DemaAtrPeriod={DemaAtrPeriod}, DemaAtrMultiplier={DemaAtrMultiplier}, DemaAtrActivationMode={DemaAtrActivationMode}, DemaAtrActivationValue={DemaAtrActivationValue}, UseBreakEven={UseBreakEven}, BETriggerTicks={BreakEvenTriggerTicks}, BEPlusTicks={BreakEvenPlusTicks}");
                }

                if (tradeStates == null)
                    tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                else
                    tradeStates.Clear();

                activeTradeId = null;
                openTradeOrder.Clear();

                if (EntriesPerDirection < ManualMinEntriesPerDirection)
                {
                    Print(string.Format("[MANUAL][CONFIG] Raising EntriesPerDirection from {0} to {1} to allow manual stacking.",
                        EntriesPerDirection, ManualMinEntriesPerDirection));
                    EntriesPerDirection = ManualMinEntriesPerDirection;
                }

                if (EntryHandling != EntryHandling.AllEntries)
                {
                    Print(string.Format("[MANUAL][CONFIG] Switching EntryHandling from {0} to AllEntries to support manual button flow.", EntryHandling));
                    EntryHandling = EntryHandling.AllEntries;
                }
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(AtrPeriod);
                AddChartIndicator(atr);

                ResetManualState();
                manualProcessingReady = false;


                if (EntriesPerDirection < ManualMinEntriesPerDirection)
                {
                    Print(string.Format("[MANUAL][CONFIG] (DataLoaded) Raising EntriesPerDirection from {0} to {1}", EntriesPerDirection, ManualMinEntriesPerDirection));
                    EntriesPerDirection = ManualMinEntriesPerDirection;
                }

                if (EntryHandling != EntryHandling.AllEntries)
                {
                    Print(string.Format("[MANUAL][CONFIG] (DataLoaded) Switching EntryHandling from {0} to AllEntries", EntryHandling));
                    EntryHandling = EntryHandling.AllEntries;
                }

                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.RegisterStrategy(this);

                if (ChartControl != null)
                    ChartControl.Dispatcher.BeginInvoke(new Action(AttachManualChartTraderControls));

            }
            else if (State == State.Realtime)
            {
                // Flush any historical bookkeeping so live executions start from a clean slate.
                ResetManualState();
                manualProcessingReady = true;
                StrategyLogInfo("[MANUAL] Strategy entered realtime; manual buttons enabled");

                if (ChartControl != null)
                    ChartControl.Dispatcher.BeginInvoke(new Action(AttachManualChartTraderControls));

                BootstrapExistingPositionState();
            }
            else if (State == State.Terminated)
            {
                manualProcessingReady = false;
                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.UnregisterStrategy(this);

                activeTradeId = null;
                if (tradeStates != null)
                    tradeStates.Clear();
                openTradeOrder.Clear();

                if (ChartControl != null)
                    ChartControl.Dispatcher.BeginInvoke(new Action(RemoveManualChartTraderControls));
                else
                    RemoveManualChartTraderControls();
            }
        }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[STATE] OnStateChange({0}) exception: {1}", State, ex));
                throw;
            }
        }
        #endregion

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 1)
            {
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0)
                    UpdateStopsTargets(GetRealtimePrice());
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            bool isFlatPosition = Position == null
                || Position.MarketPosition == MarketPosition.Flat
                || Position.Quantity == 0;
            bool hasTrackedTrades = tradeStates != null && tradeStates.Count > 0;
            bool accountHasExposure = AccountHasInstrumentExposure();

            if (!isFlatPosition && !hasTrackedTrades)
            {
                if (accountHasExposure)
                {
                    // We have live exposure but no runtime state, so rebuild synthetic tracking.
                    BootstrapExistingPositionState();
                    hasTrackedTrades = tradeStates != null && tradeStates.Count > 0;
                }
                else if (!desyncHoldActive)
                {
                    desyncHoldActive = true;
                    desyncHoldActivatedAt = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                    StrategyLogInfo(string.Format("[MANUAL][DESYNC] Holding risk management because NT reports {0} qty={1} while account exposure and trade state are empty.",
                        Position != null ? Position.MarketPosition.ToString() : "Flat",
                        Position != null ? Position.Quantity : 0));
                }
            }

            if (desyncHoldActive)
            {
                if (isFlatPosition && !accountHasExposure)
                {
                    desyncHoldActive = false;
                    StrategyLogInfo("[MANUAL][DESYNC] Platform/account mismatch resolved; risk controls resumed.");
                }
                else
                {
                    if (Debug)
                    {
                        StrategyLogDebug(string.Format("[MANUAL][DESYNC] Waiting for platform flatten (pos={0} qty={1}).",
                            Position != null ? Position.MarketPosition.ToString() : "Flat",
                            Position != null ? Position.Quantity : 0));
                    }
                    return;
                }
            }

            if (isFlatPosition)
            {
                stopSet = false;
                targetSet = false;
                activeTradeId = null;
                ResetDemaTrailingState();
                return;
            }

            if (hasTrackedTrades)
                UpdateStopsTargets(GetRealtimePrice());
        }

        private void UpdateStopsTargets(double? priceOverride = null)
        {
            if (string.IsNullOrEmpty(activeTradeId))
                return;

            if (!TryGetTradeState(activeTradeId, out var activeState))
                return;

            if (activeState.IsSynthetic)
            {
                if (Debug)
                    StrategyLogDebug($"[STOPS] Skipping stop/target setup for synthetic trade {activeTradeId} while waiting for live fill.");
                return;
            }

            double currentPrice = priceOverride ?? GetRealtimePrice();

            if (activeState.RunUpActive)
            {
                ApplyRunUpTrailing(activeState, currentPrice);
                return;
            }

            bool demaApplied = false;
            if (UseDemaAtrTrailing)
                demaApplied = TryApplyDemaAtrTrailingStop(currentPrice);

            if (!demaApplied && !stopSet)
            {
                // Stop
                if (StopType == StopKind.ATR)
                {
                    int ticks = (int)Math.Max(1, Math.Round((atr[0] * AtrStopMult) / TickSize));
                    if (IssueStopLoss(activeTradeId, CalculationMode.Ticks, ticks, false))
                    {
                        if (Debug) StrategyLogDebug($"{Time[0]} Init Stop (ATR): {ticks} ticks");
                        stopSet = true;
                    }
                }
                else
                {
                    if (IssueStopLoss(activeTradeId, CalculationMode.Ticks, StopTicks, false))
                    {
                        if (Debug) StrategyLogDebug($"{Time[0]} Init Stop (Ticks): {StopTicks}");
                        stopSet = true;
                    }
                }
            }

            if (!targetSet)
            {
                // Profit Target
                if (TargetType == TargetKind.ATR)
                {
                    int ticks = (int)Math.Max(1, Math.Round((atr[0] * AtrTargetMult) / TickSize));
                    if (IssueProfitTarget(activeTradeId, CalculationMode.Ticks, ticks))
                    {
                        if (Debug) StrategyLogDebug($"{Time[0]} Init Target (ATR): {ticks} ticks");
                        targetSet = true;
                    }
                }
                else
                {
                    if (IssueProfitTarget(activeTradeId, CalculationMode.Ticks, TargetTicks))
                    {
                        if (Debug) StrategyLogDebug($"{Time[0]} Init Target (Ticks): {TargetTicks}");
                        targetSet = true;
                    }
                }
            }

            // BreakEven
            if (!demaApplied && UseBreakEven && Position.MarketPosition != MarketPosition.Flat)
            {
                try
                {
                    double entry = Position.AveragePrice;
                    if (Position.MarketPosition == MarketPosition.Long &&
                        currentPrice >= entry + BreakEvenTriggerTicks * TickSize)
                    {
                        double be = entry + BreakEvenPlusTicks * TickSize;
                        if (Debug) StrategyLogDebug($"{Time[0]} BE LONG trigger: entry={entry:F2} price={currentPrice:F2} be={be:F2}");
                        double? lastAccepted = activeState.RunUpLastStopPrice ?? activeState.LastStopPrice;
                        var clamped = ClampStopPrice(be, currentPrice, true, lastAccepted);
                        if (clamped.HasValue)
                            IssueStopLoss(activeTradeId, CalculationMode.Price, clamped.Value, false);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short &&
                             currentPrice <= entry - BreakEvenTriggerTicks * TickSize)
                    {
                        double be = entry - BreakEvenPlusTicks * TickSize;
                        if (Debug) StrategyLogDebug($"{Time[0]} BE SHORT trigger: entry={entry:F2} price={currentPrice:F2} be={be:F2}");
                        double? lastAccepted = activeState.RunUpLastStopPrice ?? activeState.LastStopPrice;
                        var clamped = ClampStopPrice(be, currentPrice, false, lastAccepted);
                        if (clamped.HasValue)
                            IssueStopLoss(activeTradeId, CalculationMode.Price, clamped.Value, false);
                    }
                }
                catch (Exception ex)
                {
                    StrategyLogError($"[ERROR] BreakEven block: {ex.Message} at {Time[0]}");
                }
            }
        }

        private bool IssueStopLoss(string tradeId, CalculationMode mode, double value, bool simulated = false)
        {
            if (string.IsNullOrEmpty(tradeId))
                return false;
            if (!TryGetTradeState(tradeId, out var state))
                return false;
            if (state.ManualStopOverride)
            {
                if (Debug)
                    StrategyLogDebug($"[MANUAL][STOP] Skipping auto stop update for {tradeId} due to manual adjustment.");
                return false;
            }

            double targetValue = value;
            if (mode == CalculationMode.Price)
            {
                // Align to instrument tick size so downstream order updates match the pending value.
                double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                if (tickSize > 0)
                    targetValue = Instrument?.MasterInstrument?.RoundToTickSize(value) ?? Math.Round(value / tickSize) * tickSize;
            }

            state.PendingAutoStopUpdate = true;
            state.PendingAutoStopPrice = targetValue;
            try
            {
                SetStopLoss(tradeId, mode, targetValue, simulated);
                return true;
            }
            catch (Exception ex)
            {
                state.PendingAutoStopUpdate = false;
                StrategyLogError($"[ERROR] SetStopLoss failed for {tradeId}: {ex.Message}");
                return false;
            }
        }

        private bool IssueProfitTarget(string tradeId, CalculationMode mode, double value)
        {
            if (string.IsNullOrEmpty(tradeId))
                return false;
            if (!TryGetTradeState(tradeId, out var state))
                return false;
            if (state.ManualTargetOverride)
            {
                if (Debug)
                    StrategyLogDebug($"[MANUAL][TARGET] Skipping auto target update for {tradeId} due to manual adjustment.");
                return false;
            }

            state.PendingAutoTargetUpdate = true;
            state.PendingAutoStopPrice = 0;
            try
            {
                SetProfitTarget(tradeId, mode, value);
                return true;
            }
            catch (Exception ex)
            {
                state.PendingAutoTargetUpdate = false;
                StrategyLogError($"[ERROR] SetProfitTarget failed for {tradeId}: {ex.Message}");
                return false;
            }
        }

        private bool PricesClose(double a, double b)
        {
            double tolerance = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tolerance <= 0)
                tolerance = 1e-6;
            return Math.Abs(a - b) <= tolerance * 0.25;
        }

        private static bool IsStopUpdateFinal(OrderState orderState)
        {
            switch (orderState)
            {
                case OrderState.Working:
                case OrderState.Accepted:
                case OrderState.Submitted:
                case OrderState.PartFilled:
                case OrderState.Filled:
                    return true;
                default:
                    return false;
            }
        }

        /*
        private void ApplyTrailing()
        {
            // Legacy ATR trailing logic retained for historical reference only.
            if (Position.MarketPosition == MarketPosition.Flat || string.IsNullOrEmpty(activeTradeId))
                return;

            if (TrailType == TrailKind.Ticks)
            {
                SetTrailStop(activeTradeId, CalculationMode.Ticks, TrailTicks, false);
            }
            else if (TrailType == TrailKind.ATR)
            {
                int ticks = (int)Math.Round((atr[0] * AtrTrailMult) / TickSize);
                SetTrailStop(activeTradeId, CalculationMode.Ticks, Math.Max(1, ticks), false);
            }
        }
        */

        private bool TryApplyDemaAtrTrailingStop(double currentPrice)
        {
            if (!UseDemaAtrTrailing || Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (!TryGetTradeState(activeTradeId, out var state))
                return false;
            if (state.IsSynthetic)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR] Skipping synthetic trade {activeTradeId} until live order executes.");
                return false;
            }
            if (state.ManualStopOverride)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR] Manual stop override active for {activeTradeId}; skipping trail update.");
                return false;
            }

            double entryPrice = Position.AveragePrice;
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                return false;

            double activationPrice = GetActivationProbePrice(currentPrice, demaTrailingActive);
            if (!EnsureDemaAtrActivation(entryPrice, activationPrice))
                return false;

            UpdateDemaAtrWatermarks(currentPrice);

            int availableBars = CurrentBar + 1;
            int lookback = Math.Max(DemaAtrPeriod * 2 + 10, 50);
            int barsNeeded = Math.Max(DemaAtrPeriod, lookback);
            if (availableBars < barsNeeded)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR] Waiting for {barsNeeded} bars (have {availableBars}) before trailing.");
                return false;
            }

            var quotes = BuildQuoteHistory(Math.Min(lookback, availableBars));
            if (quotes.Count < DemaAtrPeriod)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR] Need {DemaAtrPeriod} quotes but only have {quotes.Count}.");
                return false;
            }

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double? stopPrice = SharedDemaAtrTrailing.CalculateTrailingStop(quotes, DemaAtrPeriod, DemaAtrMultiplier, isLong, currentPrice);
            if (!stopPrice.HasValue)
                return false;

            double rounded = Instrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(stopPrice.Value)
                : stopPrice.Value;

            double? lastAccepted = state != null ? (state.RunUpLastStopPrice ?? state.LastStopPrice) : (double?)null;
            double? safePrice = ClampStopPrice(rounded, currentPrice, isLong, lastAccepted);
            if (!safePrice.HasValue)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[DEMA-ATR] Skipped stop update because desired price {0:F2} violates market constraints (current={1:F2}).", rounded, currentPrice));
                return false;
            }

            if (!IssueStopLoss(activeTradeId, CalculationMode.Price, safePrice.Value, false))
                return false;

            stopSet = true;
            if (Debug)
                StrategyLogDebug(string.Format("[DEMA-ATR] Applied trailing stop @ {0:F2} (isLong={1})", rounded, isLong));
            return true;
        }

        private List<Quote> BuildQuoteHistory(int maxBars)
        {
            var quotes = new List<Quote>();
            if (maxBars <= 0 || Instrument == null)
                return quotes;

            int primaryCount = BarsArray.Length > 0 ? BarsArray[0].Count : 0;
            int count = Math.Min(maxBars, primaryCount);
            for (int barsAgo = count - 1; barsAgo >= 0; barsAgo--)
            {
                quotes.Add(new Quote
                {
                    Date = Times[0][barsAgo],
                    Open = Opens[0][barsAgo],
                    High = Highs[0][barsAgo],
                    Low = Lows[0][barsAgo],
                    Close = Closes[0][barsAgo],
                    Volume = (long)Math.Max(0, Volumes[0][barsAgo])
                });
            }

            return quotes;
        }

        private double GetActivationProbePrice(double currentPrice, bool trailActive)
        {
            if (trailActive || Position == null || Position.MarketPosition == MarketPosition.Flat || BarsArray[0].Count == 0)
                return currentPrice;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                double barHigh = Highs[0].Count > 0 ? Highs[0][0] : currentPrice;
                return Math.Max(currentPrice, barHigh);
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double barLow = Lows[0].Count > 0 ? Lows[0][0] : currentPrice;
                return Math.Min(currentPrice, barLow);
            }

            return currentPrice;
        }

        private double CalculateUnrealizedPnlAtPrice(double price)
        {
            if (Position == null || Position.Quantity == 0)
                return 0;

            try
            {
                return Math.Abs(Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price));
            }
            catch
            {
                double direction = Position.MarketPosition == MarketPosition.Long ? 1.0 : -1.0;
                double entry = Position.AveragePrice;
                if (entry <= 0 || double.IsNaN(entry))
                    return 0;
                double pointValue = Instrument?.MasterInstrument?.PointValue ?? 1.0;
                double diff = (price - entry) * direction;
                return Math.Abs(diff * pointValue);
            }
        }

        private double? ClampStopPrice(double desiredPrice, double currentPrice, bool isLong, double? lastAcceptedPrice = null)
        {
            if (desiredPrice <= 0 || currentPrice <= 0 || double.IsNaN(desiredPrice) || double.IsNaN(currentPrice))
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = Math.Max(Math.Abs(currentPrice) * 1e-6, 1e-6);

            double tolerance = tickSize * 0.1;
            if (isLong)
            {
                double maxAllowed = currentPrice - tickSize;
                double clamped = Math.Min(desiredPrice, maxAllowed);
                if (lastAcceptedPrice.HasValue && clamped < lastAcceptedPrice.Value - tolerance)
                    return null;
                if (clamped <= 0 || clamped >= currentPrice)
                    return null;
                return clamped;
            }
            else
            {
                double minAllowed = currentPrice + tickSize;
                double clamped = Math.Max(desiredPrice, minAllowed);
                if (lastAcceptedPrice.HasValue && clamped > lastAcceptedPrice.Value + tolerance)
                    return null;
                if (clamped <= currentPrice)
                    return null;
                return clamped;
            }
        }

        private double ConvertRunUpValueToPrice(RunUpUnits units, double value)
        {
            if (value <= 0)
                return 0;
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            switch (units)
            {
                case RunUpUnits.Ticks:
                    return value * tickSize;
                case RunUpUnits.Dollars:
                    double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0;
                    if (pointValue <= 0)
                        return 0;
                    return value / pointValue;
                default:
                    return 0;
            }
        }

        private void ApplyRunUpTrailing(TradeRuntimeState state, double currentPrice)
        {
            if (state == null || !state.RunUpActive || Position == null || Position.MarketPosition == MarketPosition.Flat)
                return;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double anchor = state.RunUpAnchorPrice > 0 ? state.RunUpAnchorPrice : currentPrice;
            double distance = state.RunUpInitialDistance;
            double increment = state.RunUpIncrement;
            if (distance <= 0)
                return;

            // Track high/low water to avoid trailing retractions when price pulls back.
            if (isLong)
            {
                if (currentPrice > state.RunUpHighWater || state.RunUpHighWater <= 0)
                    state.RunUpHighWater = currentPrice;
                if (state.RunUpLowWater <= 0)
                    state.RunUpLowWater = currentPrice;
            }
            else
            {
                if (currentPrice < state.RunUpLowWater || state.RunUpLowWater <= 0)
                    state.RunUpLowWater = currentPrice;
                if (state.RunUpHighWater <= 0)
                    state.RunUpHighWater = currentPrice;
            }

            double progress = isLong ? (state.RunUpHighWater - anchor) : (anchor - state.RunUpLowWater);
            double steps = 0;
            if (increment > 0)
                steps = Math.Floor(Math.Max(0.0, progress) / increment);

            double desiredStop = isLong
                ? anchor - distance + steps * increment
                : anchor + distance - steps * increment;

            double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
            var clamped = ClampStopPrice(desiredStop, currentPrice, isLong, lastAccepted);
            if (!clamped.HasValue)
            {
                StrategyLogInfo(string.Format("[RUN_UP_TRACE] Clamp blocked stop update | base={0} side={1} price={2:F2} anchor={3:F2} desired={4:F2} dist={5:F4} inc={6:F4} steps={7:F2}",
                    state.TradeId ?? "<unknown>",
                    isLong ? "LONG" : "SHORT",
                    currentPrice,
                    anchor,
                    desiredStop,
                    distance,
                    increment,
                    steps));
                return;
            }

            if (state.RunUpLastStopPrice.HasValue && PricesClose(state.RunUpLastStopPrice.Value, clamped.Value))
            {
                if (Debug)
                {
                    StrategyLogDebug(string.Format("[RUN_UP_TRACE] No move (unchanged) | base={0} price={1:F2} last={2:F2} desired={3:F2} steps={4:F2}",
                        state.TradeId ?? "<unknown>",
                        currentPrice,
                        state.RunUpLastStopPrice.Value,
                        clamped.Value,
                        steps));
                }
                return;
            }

            if (IssueStopLoss(activeTradeId, CalculationMode.Price, clamped.Value, false))
            {
                state.RunUpLastStopPrice = clamped.Value;
                stopSet = true;
                if (Debug)
                    StrategyLogDebug(string.Format("[RUN_UP] Updated stop to {0:F2} (anchor={1:F2}, dist={2:F4}, inc={3:F4})", clamped.Value, anchor, distance, increment));
            }
            else
            {
                StrategyLogInfo(string.Format("[RUN_UP_WARN] Stop update failed | base={0} price={1:F2} desired={2:F2} steps={3:F2}",
                    state.TradeId ?? "<unknown>",
                    currentPrice,
                    clamped.Value,
                    steps));
            }
        }

        private double GetRealtimePrice()
        {
            if (BarsArray.Length > 1 && Closes[1].Count > 0)
                return Closes[1][0];
            return Closes[0].Count > 0 ? Closes[0][0] : Close[0];
        }

        private bool EnsureDemaAtrActivation(double entryPrice, double activationPrice)
        {
            if (demaTrailingActive)
                return true;

            double threshold = Math.Max(0, DemaAtrActivationValue);
            if (threshold <= 0)
            {
                ActivateDemaAtr(activationPrice);
                return true;
            }

            double metric = CalculateDemaAtrActivationMetric(entryPrice, activationPrice);
            if (metric >= threshold)
            {
                ActivateDemaAtr(activationPrice);
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR] Activation threshold met ({metric:F2} >= {threshold:F2}) using {DemaAtrActivationMode}.");
                return true;
            }

            if (Debug)
                StrategyLogDebug($"[DEMA-ATR] Waiting for activation: {metric:F2}/{threshold:F2} ({DemaAtrActivationMode}).");
            return false;
        }

        private void ActivateDemaAtr(double price)
        {
            demaTrailingActive = true;
            demaHighWater = price;
            demaLowWater = price;
        }

        private void UpdateDemaAtrWatermarks(double price)
        {
            if (!demaTrailingActive || Position == null)
                return;

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (price > demaHighWater || demaHighWater == 0)
                    demaHighWater = price;
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (demaLowWater == 0 || price < demaLowWater)
                    demaLowWater = price;
            }
        }

        private void ResetDemaTrailingState()
        {
            demaTrailingActive = false;
            demaHighWater = 0;
            demaLowWater = 0;
        }

        private double CalculateDemaAtrActivationMetric(double entryPrice, double activationPrice)
        {
            double diff = Math.Abs(activationPrice - entryPrice);
            switch (DemaAtrActivationMode)
            {
                case TrailingActivationType.Ticks:
                    double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                    return tickSize > 0 ? diff / tickSize : 0;
                case TrailingActivationType.Pips:
                    double pip = GetPipValueForInstrument();
                    return pip > 0 ? diff / pip : 0;
                case TrailingActivationType.Dollars:
                    return CalculateUnrealizedPnlAtPrice(activationPrice);
                case TrailingActivationType.Percent:
                default:
                    return entryPrice != 0 ? Math.Abs(diff / entryPrice) * 100.0 : 0;
            }
        }

        private double GetPipValueForInstrument()
        {
            try
            {
                if (Instrument?.MasterInstrument == null)
                    return TickSize;

                if (Instrument.MasterInstrument.InstrumentType == InstrumentType.Forex)
                {
                    if (!string.IsNullOrEmpty(Instrument.FullName) && Instrument.FullName.IndexOf("JPY", StringComparison.OrdinalIgnoreCase) >= 0)
                        return 0.01;
                    return 0.0001;
                }

                return Instrument.MasterInstrument.TickSize;
            }
            catch
            {
                return TickSize;
            }
        }

        private string CreateTradeId(MarketPosition side)
        {
            long seq = Interlocked.Increment(ref tradeSequence);
            string direction = side == MarketPosition.Short ? "S" : "L";
            string instrumentName = Instrument != null
                ? (Instrument.MasterInstrument != null ? Instrument.MasterInstrument.Name : Instrument.FullName)
                : "SYM";
            instrumentName = SanitizeSymbol(instrumentName, 16);
            string tradeId = string.Format("{0}{1:X}-{2}", direction, seq, instrumentName);
            if (tradeId.Length > 50)
                tradeId = tradeId.Substring(0, 50);
            return tradeId;
        }

        private static string SanitizeSymbol(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "SYM";

            var builder = new StringBuilder(Math.Min(value.Length, maxLength));
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToUpperInvariant(c));
                    if (builder.Length >= maxLength)
                        break;
                }
            }

            if (builder.Length == 0)
                return "SYM";

            return builder.ToString();
        }

        private bool AccountHasInstrumentExposure()
        {
            if (Account == null || Instrument == null)
                return false;

            try
            {
                foreach (var accountPosition in Account.Positions)
                {
                    if (accountPosition?.Instrument == null)
                        continue;
                    if (!string.Equals(accountPosition.Instrument.FullName, Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (accountPosition.Quantity != 0)
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[MANUAL][DESYNC] Unable to inspect account positions: {0}", ex.Message));
            }

            return false;
        }

        private bool ShouldTreatAsCleanupOrder(Order order)
        {
            if (order == null)
                return false;

            if (desyncHoldActive)
                return true;

            bool hasKnownTrades = (tradeStates != null && tradeStates.Count > 0) || openTradeOrder.Count > 0;
            if (hasKnownTrades)
                return false;

            string orderName = order.Name ?? string.Empty;
            if (orderName.IndexOf("Close position", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (orderName.IndexOf("Close", StringComparison.OrdinalIgnoreCase) >= 0 &&
                string.IsNullOrEmpty(order.FromEntrySignal))
                return true;

            return false;
        }

        private void BootstrapExistingPositionState()
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
                return;

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            if (tradeStates.Count > 0 || openTradeOrder.Count > 0)
                return;

            if (Account != null)
            {
                double acctQty = 0;
                foreach (var acctPos in Account.Positions)
                {
                    if (acctPos.Instrument != null && Instrument != null && acctPos.Instrument.FullName == Instrument.FullName)
                    {
                        acctQty = acctPos.Quantity;
                        break;
                    }
                }
                if (acctQty == 0)
                {
                    StrategyLogInfo(string.Format("[MANUAL][BOOTSTRAP] Account is flat; skipping synthetic position seed despite NinjaTrader position reporting {0}", Position.MarketPosition));
                    return;
                }
            }
            MarketPosition side = Position.MarketPosition;
            int qty = Math.Abs(Position.Quantity);
            string tradeId = CreateTradeId(side);

            var state = new TradeRuntimeState
            {
                TradeId = tradeId,
                EntrySide = side,
                OriginalQuantity = qty,
                RemainingQuantity = qty,
                InstrumentName = Instrument != null ? Instrument.FullName : string.Empty,
                AccountName = Account != null ? Account.Name : string.Empty,
                EntryPrice = Position.AveragePrice,
                OpenPublished = true,
                IsSynthetic = true,
                ManualStopOverride = false,
                ManualTargetOverride = false,
                PendingAutoStopUpdate = false,
                PendingAutoTargetUpdate = false,
                LastStopPrice = 0,
                LastTargetPrice = 0,
                RunUpActive = false,
                RunUpAnchorPrice = 0,
                RunUpInitialDistance = 0,
                RunUpIncrement = 0,
                RunUpLastStopPrice = null,
                RunUpHighWater = 0,
                RunUpLowWater = 0
            };

            try
            {
                double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0.0;
                state.NtPointsPer1kLoss = pointValue > 0 ? 1000.0 / pointValue : 0.0;
            }
            catch
            {
                state.NtPointsPer1kLoss = 0.0;
            }

            tradeStates[tradeId] = state;
            openTradeOrder.Add(tradeId);
            activeTradeId = tradeId;
            StrategyLogInfo(string.Format("[MANUAL][BOOTSTRAP] Seeded synthetic trade {0} for existing position {1} qty={2}", tradeId, side, qty));

        }

        private void ResetManualState()
        {
            if (tradeStates != null && tradeStates.Count > 0)
            {
                foreach (var state in tradeStates.Values.ToList())
                {
                    if (state.ManualStopOverride || state.ManualTargetOverride)
                        NotifyAddonManualOverride(state.TradeId,
                            state.ManualStopOverride ? false : (bool?)null,
                            state.ManualTargetOverride ? false : (bool?)null);
                }
            }

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            else
                tradeStates.Clear();

            openTradeOrder.Clear();
            activeTradeId = null;
            stopSet = false;
            targetSet = false;
            ResetDemaTrailingState();
            manualBuyClickArmed = false;
            manualSellClickArmed = false;
            manualFlattenClickArmed = false;

        }

        private TradeRuntimeState PrepareTradeState(string tradeId, MarketPosition side, int quantityHint)
        {
            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            var state = new TradeRuntimeState
            {
                TradeId = tradeId,
                EntrySide = side,
                OriginalQuantity = Math.Max(1, quantityHint),
                RemainingQuantity = Math.Max(1, quantityHint),
                InstrumentName = Instrument != null ? Instrument.FullName : string.Empty,
                AccountName = Account != null ? Account.Name : string.Empty,
                OpenPublished = false,
                ManualStopOverride = false,
                ManualTargetOverride = false,
                PendingAutoStopUpdate = false,
                PendingAutoTargetUpdate = false,
                LastStopPrice = 0,
                LastTargetPrice = 0,
                IsSynthetic = State != State.Realtime,
                RunUpActive = false,
                RunUpAnchorPrice = 0,
                RunUpInitialDistance = 0,
                RunUpIncrement = 0,
                RunUpLastStopPrice = null,
                RunUpHighWater = 0,
                RunUpLowWater = 0
            };

            tradeStates[tradeId] = state;
            activeTradeId = tradeId;
            if (!openTradeOrder.Contains(tradeId))
                openTradeOrder.Add(tradeId);
            return state;
        }

        private bool TryGetTradeState(string tradeId, out TradeRuntimeState state)
        {
            state = null;
            if (string.IsNullOrEmpty(tradeId) || tradeStates == null)
                return false;
            return tradeStates.TryGetValue(tradeId, out state);
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null)
                return;

            OrderAction action = execution.Order.OrderAction;
            bool isEntryAction = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExitAction = action == OrderAction.Sell || action == OrderAction.BuyToCover;

            // Core NT accounting (Position, performance stats, etc.) still lives in the base implementation.
            // During optimizer sweeps the base method can throw IndexOutOfRangeException when our custom
            // trade_id is used as the signal name. We let the base run but swallow that specific fault so
            // strategy-driven trade_id flow keeps working across live, replay, and analyzer contexts.
            bool isBacktestAccount = false;
            string executionAccountName = execution.Account != null ? execution.Account.Name : (Account != null ? Account.Name : string.Empty);
            if (!string.IsNullOrEmpty(executionAccountName))
            {
                string trimmed = executionAccountName.Trim();
                if (trimmed.Equals("Backtest", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Backtest ", StringComparison.OrdinalIgnoreCase))
                    isBacktestAccount = true;
            }

            if (isBacktestAccount)
            {
                try
                {
                    base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);
                }
                catch (Exception ex) when (ex is IndexOutOfRangeException || ex is NullReferenceException)
                {
                    if (Debug)
                        StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Ignored optimizer {1} in base.OnExecutionUpdate for trade {2}", time, ex.GetType().Name, execution != null && execution.Order != null ? execution.Order.Name : "<unknown>"));
                }
                // Strategy Analyzer runs do not need trade sync or state tracking; bail out after base housekeeping.
                return;
            }
            else
            {
                base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);
            }

            bool exitOnClose = false;
            if (!exitOnClose)
            {
                string orderName = execution.Order.Name;
                if (!string.IsNullOrEmpty(orderName))
                {
                    if (orderName.IndexOf("Exit on session close", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        orderName.IndexOf("Exit on close", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        exitOnClose = true;
                    }
                }
            }

            if (ShouldTreatAsCleanupOrder(execution.Order))
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[MANUAL][DESYNC] Ignoring cleanup execution for order '{0}'", execution.Order != null ? execution.Order.Name : "<unknown>"));
                return;
            }

            string tradeId = !string.IsNullOrEmpty(execution.Order.FromEntrySignal)
                ? execution.Order.FromEntrySignal
                : execution.Order.Name;

            bool isLiveExecution = IsLiveExecutionContext(execution);
            if (!isLiveExecution)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Ignoring non-realtime execution for trade {1}", time, tradeId ?? "<unknown>"));
                return;
            }

            if (exitOnClose && tradeStates != null && tradeStates.Count > 0)
            {
                TradeRuntimeState resolvedState = null;

                if (!string.IsNullOrEmpty(activeTradeId) && tradeStates.TryGetValue(activeTradeId, out var activeState))
                {
                    if (activeState != null && activeState.RemainingQuantity > 0)
                        resolvedState = activeState;
                }

                if (resolvedState == null)
                {
                    OrderAction exitAction = execution.Order.OrderAction;
                    resolvedState = tradeStates.Values.FirstOrDefault(state =>
                    {
                        if (state == null || state.RemainingQuantity <= 0)
                            return false;

                        if (exitAction == OrderAction.Sell || exitAction == OrderAction.SellShort)
                            return state.EntrySide == MarketPosition.Long;
                        if (exitAction == OrderAction.Buy || exitAction == OrderAction.BuyToCover)
                            return state.EntrySide == MarketPosition.Short;
                        return true;
                    });
                }

                if (resolvedState != null)
                    tradeId = resolvedState.TradeId;
            }

            if (string.IsNullOrEmpty(tradeId))
                return;

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            TradeRuntimeState state;
            if (!tradeStates.TryGetValue(tradeId, out state))
            {
                // Ensure we always have a runtime state so we can draw P/L labels even if the trade was
                // opened externally or the state was dropped.
                if (exitOnClose && !isExitAction)
                {
                    if (Debug)
                        StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Exit-on-close execution received without matching trade state for order '{1}'", time, execution.Order.Name ?? "<unknown>"));
                    return;
                }

                if (isEntryAction)
                {
                    MarketPosition inferredSide = (action == OrderAction.SellShort || action == OrderAction.Sell)
                        ? MarketPosition.Short
                        : MarketPosition.Long;
                    state = PrepareTradeState(tradeId, inferredSide, Math.Max(1, Math.Abs((int)execution.Order.Quantity)));
                }
                else if (isExitAction)
                {
                    MarketPosition inferredSide = (action == OrderAction.Sell || action == OrderAction.SellShort)
                        ? MarketPosition.Long
                        : MarketPosition.Short;
                    int qtyHint = Math.Max(1, Math.Abs((int)execution.Order.Quantity));
                    state = PrepareTradeState(tradeId, inferredSide, qtyHint);
                    state.IsSynthetic = true; // avoid lifecycle publishes for recovered trades
                    state.OpenPublished = true;

                    // Best-effort entry price so we can compute/display P&L on the chart.
                    double entryPrice = Position != null && Position.MarketPosition != MarketPosition.Flat
                        ? Position.AveragePrice
                        : 0;
                    if (entryPrice <= 0 && execution.Order != null)
                        entryPrice = execution.Order.AverageFillPrice;
                    if (entryPrice <= 0)
                        entryPrice = execution.Price;
                    state.EntryPrice = entryPrice;
                    state.OriginalQuantity = qtyHint;
                    state.RemainingQuantity = qtyHint;
                }
                else
                {
                    return;
                }
            }

            if (string.IsNullOrEmpty(state.InstrumentName))
                state.InstrumentName = execution.Instrument != null ? execution.Instrument.FullName : state.InstrumentName;
            if (string.IsNullOrEmpty(state.AccountName))
                state.AccountName = execution.Account != null ? execution.Account.Name : state.AccountName;

            // Treat Buy/SellShort as entries; BuyToCover/Sell as exits. Ninja leaves FromEntrySignal empty for many manual actions,
            // so rely on OrderAction instead of FromEntrySignal alone to avoid misclassifying exits as new entries.
            bool isEntry = isEntryAction && !exitOnClose;

            if (isEntry)
                HandleEntryExecution(execution, state);
            else
                HandleExitExecution(execution, state);

        }

        private bool IsLiveExecutionContext(Execution execution)
        {
            if (execution == null)
                return false;

            string executionAccountName = execution.Account != null ? execution.Account.Name : (Account != null ? Account.Name : string.Empty);
            bool isBacktestAccount = false;
            if (!string.IsNullOrEmpty(executionAccountName))
            {
                string trimmed = executionAccountName.Trim();
                if (trimmed.Equals("Backtest", StringComparison.OrdinalIgnoreCase) || trimmed.StartsWith("Backtest ", StringComparison.OrdinalIgnoreCase))
                    isBacktestAccount = true;
            }

            if (isBacktestAccount)
                return false;

            try
            {
                var isLiveProp = execution.GetType().GetProperty("IsLive");
                if (isLiveProp != null)
                {
                    object value = isLiveProp.GetValue(execution, null);
                    if (value is bool flag)
                        return flag;
                }
            }
            catch
            {
                // Swallow - we'll fall back to state-based heuristics below.
            }

            if (State == State.Realtime)
                return true;

            return false;
        }

        protected override void OnPositionUpdate(Position position, double averagePrice, int quantity, MarketPosition marketPosition)
        {
            base.OnPositionUpdate(position, averagePrice, quantity, marketPosition);

            if (position == null)
                return;

            if (Account != null && position.Account != null && !string.Equals(Account.Name, position.Account.Name, StringComparison.OrdinalIgnoreCase))
                return;

            if (Instrument != null && position.Instrument != null && !string.Equals(Instrument.FullName, position.Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                return;

        }

        private void HandleEntryExecution(Execution execution, TradeRuntimeState state)
        {
            if (state != null)
                state.IsSynthetic = false;

            int orderQty = Math.Max(1, Math.Abs((int)execution.Order.Quantity));
            int filledQty = Math.Max(1, Math.Abs((int)execution.Order.Filled));

            state.OriginalQuantity = orderQty;
            state.RemainingQuantity = Math.Max(orderQty, filledQty);
            state.EntrySide = (execution.Order.OrderAction == OrderAction.SellShort || execution.Order.OrderAction == OrderAction.Sell)
                ? MarketPosition.Short
                : MarketPosition.Long;
            state.EntryPrice = execution.Price;

            try
            {
                double pointValue = execution.Instrument?.MasterInstrument?.PointValue ?? 0.0;
                state.NtPointsPer1kLoss = pointValue > 0 ? 1000.0 / pointValue : 0.0;
            }
            catch
            {
                state.NtPointsPer1kLoss = 0.0;
            }

            activeTradeId = state.TradeId;

            if (!state.OpenPublished && !state.IsSynthetic)
            {
                PublishOpenEvent(state);
                state.OpenPublished = true;
            }
        }

        private void HandleExitExecution(Execution execution, TradeRuntimeState state)
        {
            int execQty = Math.Max(1, Math.Abs((int)execution.Quantity));
            if (execQty > state.RemainingQuantity)
                execQty = state.RemainingQuantity;

            state.RemainingQuantity = Math.Max(0, state.RemainingQuantity - execQty);

            if (state.RemainingQuantity > 0)
            {
                if (!state.IsSynthetic)
                    PublishPartialEvent(state.TradeId, state.RemainingQuantity);
            }
            else
            {
                // Draw P/L label on exit marker for this completed trade
                if (execution != null)
                {
                    double entryPrice = state.EntryPrice;
                    if (entryPrice <= 0 && execution.Order != null)
                        entryPrice = execution.Order.AverageFillPrice;
                    if (entryPrice <= 0)
                        entryPrice = execution.Price; // last resort; avoids missing label entirely

                    int qty = state.OriginalQuantity > 0
                        ? state.OriginalQuantity
                        : Math.Max(1, Math.Abs((int)(execution.Order?.Quantity ?? execution.Quantity)));

                    double execPnl = 0;
                    double pointValue = Instrument?.MasterInstrument?.PointValue ?? (execution.Instrument?.MasterInstrument?.PointValue ?? 0);
                    if (pointValue <= 0 && state.NtPointsPer1kLoss > 0)
                        pointValue = 1000.0 / state.NtPointsPer1kLoss;
                    if (pointValue > 0 && entryPrice > 0)
                    {
                        double signed = (state.EntrySide == MarketPosition.Long)
                            ? (execution.Price - entryPrice)
                            : (entryPrice - execution.Price);
                        execPnl = signed * pointValue * qty;
                    }

                    DrawExitPnlLabel(execution, execPnl, state, entryPrice, qty);
                }

                if (!state.IsSynthetic)
                    PublishClosedEvent(state.TradeId);
                if (state.ManualStopOverride || state.ManualTargetOverride)
                    NotifyAddonManualOverride(state.TradeId,
                        state.ManualStopOverride ? false : (bool?)null,
                        state.ManualTargetOverride ? false : (bool?)null);
                state.RunUpActive = false;
                state.RunUpLastStopPrice = null;
                tradeStates.Remove(state.TradeId);
                openTradeOrder.Remove(state.TradeId);

                if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                    activeTradeId = null;

                if (string.IsNullOrEmpty(activeTradeId) && openTradeOrder.Count > 0)
                    activeTradeId = openTradeOrder[openTradeOrder.Count - 1];

                stopSet = false;
                targetSet = false;
                ResetDemaTrailingState();
            }
        }

        private void DrawExitPnlLabel(Execution execution, double pnl, TradeRuntimeState state, double entryPrice, int qty)
        {
            if (execution == null)
                return;

            string tag = $"exit_pnl_{execution.ExecutionId}_{execution.Order?.OrderId}";
            double rounded = Math.Round(pnl, 0);
            string tradeId = state != null && !string.IsNullOrEmpty(state.TradeId)
                ? state.TradeId
                : (execution.Order != null ? execution.Order.Name : string.Empty);
            qty = Math.Max(1, qty);
            double entry = entryPrice > 0 ? entryPrice : execution.Price;
            string tradeDescriptor = !string.IsNullOrEmpty(tradeId)
                ? $"{tradeId} {qty}@{entry:F2}"
                : $"{qty}@{entry:F2}";

            string pnlText = (rounded >= 0 ? "+" : "-") + "$" + Math.Abs(rounded).ToString("0");
            string label = $"{tradeDescriptor} | {pnlText}";
            var brush = rounded >= 0 ? Brushes.LimeGreen : Brushes.Red;

            // Place label near the execution bar/price
            int barIndex = Bars.GetBar(execution.Time);
            if (barIndex < 0)
                barIndex = CurrentBar;
            int barsAgo = Math.Max(0, CurrentBar - barIndex);
            Draw.Text(this, tag, false, label, barsAgo, execution.Price, 0, brush, new SimpleFont("Arial", 12), TextAlignment.Center, null, null, 0);
        }

        private void AttachManualChartTraderControls()
        {
            if (ChartControl == null)
                return;

            var chart = ChartControl.OwnerChart;
            if (chart == null)
                return;

            attachedChart = chart;

            var chartTrader = chart.ChartTrader;
            if (chartTrader == null)
                return;

            attachedChartTrader = chartTrader;

            if (!chartTraderVisibilityHooked)
            {
                chartTrader.IsVisibleChanged += ChartTrader_IsVisibleChanged;
                chartTraderVisibilityHooked = true;
            }

            if (!chartTrader.IsLoaded)
            {
                RoutedEventHandler loadedHandler = null;
                loadedHandler = (s, e) =>
                {
                    chartTrader.Loaded -= loadedHandler;
                    AttachManualChartTraderControlsCore(chartTrader);
                };
                chartTrader.Loaded += loadedHandler;
                return;
            }

            AttachManualChartTraderControlsCore(chartTrader);
        }

        private void AttachManualChartTraderControlsCore(ChartTrader chartTrader)
        {
            if (chartTrader == null || manualBuyButton != null)
                return;

            if (Debug && !manualTreeLogged)
            {
                DumpVisualTree(chartTrader, 0, 12);
                manualTreeLogged = true;
            }

            FrameworkElement positionDisplayElement = FindPositionDisplay(chartTrader);
            FrameworkElement priceElement = FindPriceDisplayElement(chartTrader);
            FrameworkElement instrumentElement = FindInstrumentSelector(chartTrader);

            Grid traderGrid = null;
            int positionRow = -1;
            if (positionDisplayElement != null)
            {
                traderGrid = FindAncestorGrid(positionDisplayElement);
                if (traderGrid != null)
                {
                    positionRow = Grid.GetRow(positionDisplayElement);
                    StrategyLogDebug(string.Format("[MANUAL] Found position display in grid '{0}' row {1}", traderGrid.Name ?? "<grid>", positionRow));
                }
            }

            int priceRow = -1;
            Grid priceGrid = priceElement != null ? FindAncestorGrid(priceElement) : null;
            if (traderGrid == null && priceGrid != null)
                traderGrid = priceGrid;
            if (priceGrid != null && traderGrid == priceGrid)
            {
                priceRow = Grid.GetRow(priceElement);
                StrategyLogDebug(string.Format("[MANUAL] Found price element in grid '{0}' row {1}", traderGrid.Name ?? "<grid>", priceRow));
            }

            Grid instrumentGrid = instrumentElement != null ? FindAncestorGrid(instrumentElement) : null;
            if (traderGrid == null && instrumentGrid != null)
                traderGrid = instrumentGrid;

            if (traderGrid == null)
            {
                StrategyLogDebug("[MANUAL] Unable to locate Chart Trader grid; skipping manual button insertion");
                return;
            }

            manualButtonGrid = traderGrid;
            manualButtonHost = traderGrid;

            RemoveExistingManualPanel(traderGrid);

            int insertRow = -1;
            if (positionRow >= 0)
            {
                insertRow = positionRow + 1;
            }
            else if (priceRow >= 0)
            {
                insertRow = priceRow + 1;
            }
            else if (traderGrid.RowDefinitions != null)
            {
                insertRow = traderGrid.RowDefinitions.Count;
            }
            else
            {
                insertRow = 0;
            }

            if (insertRow < 0)
                insertRow = 0;

            if (traderGrid.RowDefinitions == null)
            {
                StrategyLogDebug("[MANUAL] Chart Trader grid has no row definitions; aborting manual button insertion");
                return;
            }

            insertRow = Math.Min(insertRow, traderGrid.RowDefinitions.Count);

            RowDefinition newRow = new RowDefinition { Height = GridLength.Auto };
            if (insertRow >= traderGrid.RowDefinitions.Count)
            {
                traderGrid.RowDefinitions.Add(newRow);
            }
            else
            {
                traderGrid.RowDefinitions.Insert(insertRow, newRow);
                foreach (UIElement child in traderGrid.Children.OfType<UIElement>().ToList())
                {
                    int currentRow = Grid.GetRow(child);
                    if (currentRow >= insertRow)
                        Grid.SetRow(child, currentRow + 1);
                }
            }

            manualButtonRowIndex = insertRow;
            manualButtonRowDefinition = newRow;

            manualButtonPanel = new Grid
            {
                Margin = new Thickness(8, 4, 8, 8),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 36,
                Tag = manualPanelTag
            };
            manualButtonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            manualButtonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            manualButtonPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            manualButtonStyleSource = LocateDefaultChartTraderButton(chartTrader, "Buy Market");

            manualBuyButton = CreateManualButton("Manual\nBuy", manualButtonStyleSource, Brushes.DarkGreen, ManualBuyButton_Click);
            manualSellButton = CreateManualButton("Manual\nSell", manualButtonStyleSource, Brushes.DarkRed, ManualSellButton_Click);
            manualFlattenButton = CreateManualButton("Manual\nFlatten", manualButtonStyleSource, Brushes.DimGray, ManualFlattenButton_Click);

            manualBuyButton.PreviewMouseLeftButtonDown += ManualBuyButton_PreviewMouseLeftButtonDown;
            manualBuyButton.PreviewKeyDown += ManualBuyButton_PreviewKeyDown;
            manualSellButton.PreviewMouseLeftButtonDown += ManualSellButton_PreviewMouseLeftButtonDown;
            manualSellButton.PreviewKeyDown += ManualSellButton_PreviewKeyDown;
            manualFlattenButton.PreviewMouseLeftButtonDown += ManualFlattenButton_PreviewMouseLeftButtonDown;
            manualFlattenButton.PreviewKeyDown += ManualFlattenButton_PreviewKeyDown;

            manualButtonPanel.Children.Add(manualBuyButton);
            manualButtonPanel.Children.Add(manualSellButton);
            manualButtonPanel.Children.Add(manualFlattenButton);
            Grid.SetColumn(manualBuyButton, 0);
            Grid.SetColumn(manualSellButton, 1);
            Grid.SetColumn(manualFlattenButton, 2);

            traderGrid.Children.Add(manualButtonPanel);
            Grid.SetRow(manualButtonPanel, insertRow);
            Grid.SetColumn(manualButtonPanel, 0);
            Grid.SetColumnSpan(manualButtonPanel, traderGrid.ColumnDefinitions != null && traderGrid.ColumnDefinitions.Count > 0 ? traderGrid.ColumnDefinitions.Count : 1);

            StrategyLogDebug(string.Format("[MANUAL] Chart Trader controls attached at row {0}", insertRow));
            LogGridChildren(traderGrid, "post-insert");
        }

        private void ChartTrader_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (attachedChartTrader != null && attachedChartTrader.IsVisible && manualBuyButton == null)
            {
                AttachManualChartTraderControlsCore(attachedChartTrader);
            }
        }

        private void LogGridChildren(Grid grid, string context)
        {
            if (!Debug || grid == null)
                return;

            foreach (UIElement child in grid.Children)
            {
                int row = Grid.GetRow(child);
                int col = Grid.GetColumn(child);
                string childName = (child as FrameworkElement)?.Name ?? child.GetType().Name;
                StrategyLogDebug(string.Format("[MANUAL][GRID] {0} -> row={1} col={2} type={3} name={4}", context, row, col, child.GetType().Name, childName));
            }
        }

        private Button LocateDefaultChartTraderButton(ChartTrader chartTrader, string partialContent)
        {
            return FindVisualDescendant<Button>(chartTrader, btn =>
            {
                if (btn?.Content is string text)
                    return text.IndexOf(partialContent, StringComparison.OrdinalIgnoreCase) >= 0;
                return false;
            });
        }

        private FrameworkElement FindPriceDisplayElement(ChartTrader chartTrader)
        {
            return FindVisualDescendant<FrameworkElement>(chartTrader, elem =>
            {
                if (elem == null)
                    return false;

                if (!string.IsNullOrEmpty(elem.Name) && elem.Name.IndexOf("price", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (!string.IsNullOrEmpty(elem.Name) && (elem.Name.IndexOf("bid", StringComparison.OrdinalIgnoreCase) >= 0 || elem.Name.IndexOf("ask", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;

                if (elem is TextBlock textBlock)
                {
                    string text = textBlock.Text;
                    if (!string.IsNullOrEmpty(text) && (text.IndexOf("Bid", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Ask", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }

                return false;
            });
        }

        private FrameworkElement FindInstrumentSelector(ChartTrader chartTrader)
        {
            return FindVisualDescendant<ComboBox>(chartTrader, combo =>
            {
                if (combo == null)
                    return false;

                string name = combo.Name;
                if (!string.IsNullOrEmpty(name) && name.IndexOf("instrument", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                return false;
            });
        }

        private FrameworkElement FindPositionDisplay(ChartTrader chartTrader)
        {
            return FindVisualDescendant<FrameworkElement>(chartTrader, element =>
            {
                if (element == null)
                    return false;

                return element.GetType().Name.IndexOf("PositionDisplay", StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        private void DumpVisualTree(DependencyObject obj, int depth, int maxDepth)
        {
            if (obj == null || depth > maxDepth)
                return;

            string indent = new string(' ', depth * 2);
            if (obj is FrameworkElement fe)
            {
                StrategyLogDebug(string.Format("[MANUAL][TREE]{0}{1} Name={2} Type={3}", indent, depth, fe.Name ?? "<none>", fe.GetType().Name));
            }
            else
            {
                StrategyLogDebug(string.Format("[MANUAL][TREE]{0}{1} Type={2}", indent, depth, obj.GetType().Name));
            }

            int childCount = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < childCount; i++)
            {
                DumpVisualTree(VisualTreeHelper.GetChild(obj, i), depth + 1, maxDepth);
            }
        }

        private Panel FindAncestorPanel(DependencyObject element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                if (current is Grid gridPanel)
                    return gridPanel;
                if (current is StackPanel stack)
                    return stack;
                if (current is WrapPanel wrap)
                    return wrap;
                if (current is UniformGrid grid)
                    return grid;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private Grid FindAncestorGrid(DependencyObject element)
        {
            DependencyObject current = element;
            while (current != null)
            {
                if (current is Grid grid)
                    return grid;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static T FindVisualDescendant<T>(DependencyObject parent, Func<T, bool> predicate = null) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int childCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    if (predicate == null || predicate(typedChild))
                        return typedChild;
                }

                T result = FindVisualDescendant<T>(child, predicate);
                if (result != null)
                    return result;
            }

            return null;
        }

        private Button CreateManualButton(string label, Button styleSource, Brush fallbackBrush, RoutedEventHandler handler)
        {
            var button = new Button
            {
                Margin = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 32,
                MinWidth = 90,
                Focusable = false,
                IsTabStop = false,
                UseLayoutRounding = true,
                SnapsToDevicePixels = true
            };

            button.Content = label;

            if (styleSource != null)
            {
                button.Style = styleSource.Style;
                button.FontSize = styleSource.FontSize;
                button.FontFamily = styleSource.FontFamily;
                button.Padding = styleSource.Padding;
                button.Foreground = styleSource.Foreground;
                button.Background = styleSource.Background;
                button.BorderBrush = styleSource.BorderBrush;
                button.Command = null;
                button.CommandParameter = null;
                button.Content = label;
            }
            else
            {
                button.Background = fallbackBrush;
                button.Foreground = Brushes.White;
                button.Padding = new Thickness(10, 4, 10, 4);
            }

            button.Click += handler;
            return button;
        }

        private void ManualBuyButton_Click(object sender, RoutedEventArgs e)
        {
            if (!manualBuyClickArmed)
            {
                StrategyLogDebug("[MANUAL] Buy click ignored because it was not armed by a user interaction");
                return;
            }
            if (!IsManualOrderWindowReady())
            {
                StrategyLogInfo("[MANUAL] Buy click ignored because strategy is not in realtime yet");
                manualBuyClickArmed = false;
                manualSellClickArmed = false;
                manualFlattenClickArmed = false;
                return;
            }
            manualBuyClickArmed = false;
            manualSellClickArmed = false;
            manualFlattenClickArmed = false;
            if (!IsPointerOverElement(manualBuyButton) && !(manualBuyButton?.IsKeyboardFocusWithin ?? false))
            {
                StrategyLogDebug("[MANUAL] Buy click ignored because pointer/focus is not on the manual Buy button");
                return;
            }
            if (!IsChartTraderContextActive())
            {
                StrategyLogDebug("[MANUAL] Buy click ignored because Chart Trader is not hosting this strategy's chart context");
                return;
            }
            StrategyLogDebug("[MANUAL] Buy button clicked");
            TriggerCustomEvent(_ => SubmitManualOrder(MarketPosition.Long), null);
        }

        private void ManualSellButton_Click(object sender, RoutedEventArgs e)
        {
            if (!manualSellClickArmed)
            {
                StrategyLogDebug("[MANUAL] Sell click ignored because it was not armed by a user interaction");
                return;
            }
            if (!IsManualOrderWindowReady())
            {
                StrategyLogInfo("[MANUAL] Sell click ignored because strategy is not in realtime yet");
                manualSellClickArmed = false;
                manualBuyClickArmed = false;
                manualFlattenClickArmed = false;
                return;
            }
            manualSellClickArmed = false;
            manualBuyClickArmed = false;
            manualFlattenClickArmed = false;
            if (!IsPointerOverElement(manualSellButton) && !(manualSellButton?.IsKeyboardFocusWithin ?? false))
            {
                StrategyLogDebug("[MANUAL] Sell click ignored because pointer/focus is not on the manual Sell button");
                return;
            }
            if (!IsChartTraderContextActive())
            {
                StrategyLogDebug("[MANUAL] Sell click ignored because Chart Trader is not hosting this strategy's chart context");
                return;
            }
            StrategyLogDebug("[MANUAL] Sell button clicked");
            TriggerCustomEvent(_ => SubmitManualOrder(MarketPosition.Short), null);
        }

        private void ManualFlattenButton_Click(object sender, RoutedEventArgs e)
        {
            if (!manualFlattenClickArmed)
            {
                StrategyLogDebug("[MANUAL] Flatten click ignored because it was not armed by a user interaction");
                return;
            }
            if (!IsManualOrderWindowReady())
            {
                StrategyLogInfo("[MANUAL] Flatten click ignored because strategy is not in realtime yet");
                manualFlattenClickArmed = false;
                manualBuyClickArmed = false;
                manualSellClickArmed = false;
                return;
            }
            manualFlattenClickArmed = false;
            manualBuyClickArmed = false;
            manualSellClickArmed = false;
            if (!IsPointerOverElement(manualFlattenButton) && !(manualFlattenButton?.IsKeyboardFocusWithin ?? false))
            {
                StrategyLogDebug("[MANUAL] Flatten click ignored because pointer/focus is not on the manual Flatten button");
                return;
            }
            if (!IsChartTraderContextActive())
            {
                StrategyLogDebug("[MANUAL] Flatten click ignored because Chart Trader is not hosting this strategy's chart context");
                return;
            }
            StrategyLogDebug("[MANUAL] Flatten button clicked");
            TriggerCustomEvent(_ => SubmitManualFlatten(), null);
        }

        private void ManualBuyButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            manualBuyClickArmed = true;
        }

        private void ManualBuyButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter)
                manualBuyClickArmed = true;
        }

        private void ManualSellButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            manualSellClickArmed = true;
        }

        private void ManualSellButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter)
                manualSellClickArmed = true;
        }

        private void ManualFlattenButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            manualFlattenClickArmed = true;
        }

        private void ManualFlattenButton_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Enter)
                manualFlattenClickArmed = true;
        }

        private bool IsPointerOverElement(FrameworkElement element)
        {
            if (element == null)
                return false;

            DependencyObject current = Mouse.DirectlyOver as DependencyObject;
            while (current != null)
            {
                if (ReferenceEquals(current, element))
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private bool IsManualOrderWindowReady()
        {
            return State == State.Realtime && manualProcessingReady;
        }

        private bool IsChartTraderContextActive()
        {
            if (ChartControl == null || attachedChart == null)
                return false;

            try
            {
                return ReferenceEquals(ChartControl.OwnerChart, attachedChart);
            }
            catch
            {
                return false;
            }
        }

        private double GetOtherStrategyExposure()
        {
            if (Account == null || Instrument == null)
                return 0;

            double fallback = GetOtherExposureFallback();
            if (Math.Abs(fallback) > ExposureFlatTolerance)
                return fallback;

            var manager = MultiStratManager.Instance;
            if (manager == null)
                return 0;

            bool hasData;
            double net = manager.GetNetExposure(Account.Name, Instrument.FullName, this, out hasData);
            if (!hasData)
                return 0;

            double normalizedNet = Math.Abs(net) > ExposureFlatTolerance ? net : 0.0;
            bool accountFlat = !AccountHasInstrumentExposure();
            bool strategyFlat = tradeStates == null || tradeStates.Count == 0;

            if (normalizedNet != 0 && accountFlat && strategyFlat)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[MANUAL][EXPOSURE] Ignoring stale cross-strategy exposure report ({0:F2}) because account/trade state are flat.", normalizedNet));
                return 0;
            }

            return normalizedNet;
        }

        private double GetOtherExposureFallback()
        {
            if (Account == null || Instrument == null)
                return 0;

            double accountQty = 0;
            try
            {
                foreach (var acctPos in Account.Positions)
                {
                    if (acctPos?.Instrument != null && acctPos.Instrument.FullName == Instrument.FullName)
                    {
                        accountQty = GetSignedQuantity(acctPos.MarketPosition, acctPos.Quantity);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[MANUAL][GUARD] Failed to read account exposure fallback: {0}", ex.Message));
            }

            return accountQty - GetSignedStrategyPosition();
        }

        private bool HasOpposingExternalExposure(MarketPosition desiredDirection, out double otherExposure)
        {
            otherExposure = GetOtherStrategyExposure();
            if (desiredDirection == MarketPosition.Long)
                return otherExposure < -ExposureFlatTolerance;
            if (desiredDirection == MarketPosition.Short)
                return otherExposure > ExposureFlatTolerance;
            return false;
        }

        private double GetSignedStrategyPosition()
        {
            if (Position == null)
                return 0;
            return GetSignedQuantity(Position.MarketPosition, Position.Quantity);
        }

        private static double GetSignedQuantity(MarketPosition marketPosition, double quantity)
        {
            double absQty = Math.Abs(quantity);
            switch (marketPosition)
            {
                case MarketPosition.Long:
                    return absQty;
                case MarketPosition.Short:
                    return -absQty;
                default:
                    return 0;
            }
        }

        private bool IsAccountOpposedPosition(MarketPosition desiredDirection)
        {
            return HasOpposingExternalExposure(desiredDirection, out _);
        }

        private void SubmitManualOrder(MarketPosition direction)
        {
            if (!IsManualOrderWindowReady())
            {
                StrategyLogInfo("[MANUAL][ORDER] Ignored manual order request because strategy is not realtime ready");
                return;
            }
            int requestedQuantity = Math.Max(1, DefaultQuantity);
            int remainingToOpen = requestedQuantity;
            double otherExposureSnapshot;
            HasOpposingExternalExposure(direction, out otherExposureSnapshot);

            StrategyLogDebug(string.Format("[MANUAL][EXPOSURE] other strategies net {0} on {1}", otherExposureSnapshot,
                Instrument != null ? Instrument.FullName : "<unknown>"));

            StrategyLogInfo(string.Format("[MANUAL][ORDER] direction={0} qty={1} currentPos={2} posQty={3} activeTradeId={4} openTrades={5} entriesPerDir={6} entryHandling={7}",
                direction,
                requestedQuantity,
                Position.MarketPosition,
                Position.Quantity,
                activeTradeId ?? "<none>",
                openTradeOrder.Count,
                EntriesPerDirection,
                EntryHandling));

            if (direction == MarketPosition.Long)
            {
                if (Position.MarketPosition != MarketPosition.Short && HasOpposingExternalExposure(MarketPosition.Long, out otherExposureSnapshot))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Ignored long entry because other strategies are net short ({0}). Flatten opposing positions first.", otherExposureSnapshot));
                    return;
                }

                if (Position.MarketPosition == MarketPosition.Short && Position.Quantity != 0)
                {
                    int qtyToCover = Math.Min(remainingToOpen, Math.Abs(Position.Quantity));
                    if (qtyToCover > 0)
                    {
                        SubmitManualExit(MarketPosition.Short, qtyToCover);
                        remainingToOpen = Math.Max(0, remainingToOpen - qtyToCover);
                    }
                }

                if (HasOpposingExternalExposure(MarketPosition.Long, out otherExposureSnapshot))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Ignored long entry because other strategies remain net short ({0}).", otherExposureSnapshot));
                    return;
                }

                if (remainingToOpen > 0)
                    OpenManualEntry(MarketPosition.Long, remainingToOpen);
            }
            else if (direction == MarketPosition.Short)
            {
                if (Position.MarketPosition != MarketPosition.Long && HasOpposingExternalExposure(MarketPosition.Short, out otherExposureSnapshot))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Ignored short entry because other strategies are net long ({0}). Flatten opposing positions first.", otherExposureSnapshot));
                    return;
                }
                if (Position.MarketPosition == MarketPosition.Long && Position.Quantity != 0)
                {
                    int qtyToClose = Math.Min(remainingToOpen, Math.Abs(Position.Quantity));
                    if (qtyToClose > 0)
                    {
                        SubmitManualExit(MarketPosition.Long, qtyToClose);
                        remainingToOpen = Math.Max(0, remainingToOpen - qtyToClose);
                    }
                }

                if (HasOpposingExternalExposure(MarketPosition.Short, out otherExposureSnapshot))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Ignored short entry because other strategies remain net long ({0}).", otherExposureSnapshot));
                    return;
                }

                if (remainingToOpen > 0)
                    OpenManualEntry(MarketPosition.Short, remainingToOpen);
            }
        }

        private void SubmitManualFlatten()
        {
            if (!IsManualOrderWindowReady())
            {
                StrategyLogInfo("[MANUAL] Flatten ignored because strategy is not realtime ready");
                return;
            }
            if (Position.MarketPosition == MarketPosition.Long && Position.Quantity > 0)
            {
                StrategyLogInfo("[MANUAL] Flatten requested: closing long position via manual buttons");
                SubmitManualExit(MarketPosition.Long, Math.Abs(Position.Quantity));
            }
            else if (Position.MarketPosition == MarketPosition.Short && Position.Quantity > 0)
            {
                StrategyLogInfo("[MANUAL] Flatten requested: closing short position via manual buttons");
                SubmitManualExit(MarketPosition.Short, Math.Abs(Position.Quantity));
            }
            else
            {
                StrategyLogDebug("[MANUAL] Flatten requested but strategy is already flat.");
            }
        }

        private void SubmitManualExit(MarketPosition side, int quantity)
        {
            if (quantity <= 0)
                return;

            StrategyLogInfo(string.Format("[MANUAL][EXIT] side={0} qty={1} tradeStates={2} openTrades={3}",
                side,
                quantity,
                tradeStates != null ? tradeStates.Count : 0,
                openTradeOrder.Count));

            int remaining = quantity;
            int exitSeriesIndex = BarsArray.Length > ManualOrderSeriesIndex ? ManualOrderSeriesIndex : 0;
            foreach (var state in EnumerateOpenTrades(side))
            {
                int available = Math.Max(0, state.RemainingQuantity);
                if (available <= 0)
                    continue;

                StrategyLogInfo(string.Format("[MANUAL][EXIT] considering trade={0} entrySide={1} remaining={2}", state.TradeId, state.EntrySide, state.RemainingQuantity));

                int qtyToExit = Math.Min(available, remaining);
                if (qtyToExit <= 0)
                    continue;

                string exitSignal = BuildExitSignalName(state.TradeId, side == MarketPosition.Long ? "MANSELL" : "MANBUY");
                if (side == MarketPosition.Long)
                {
                    if (exitSeriesIndex > 0)
                        ExitLong(exitSeriesIndex, qtyToExit, exitSignal, state.TradeId);
                    else
                        ExitLong(qtyToExit, exitSignal, state.TradeId);
                    StrategyLogInfo(string.Format("[MANUAL][EXIT] submitted ExitLong qty={0} fromEntry={1}", qtyToExit, state.TradeId));
                }
                else
                {
                    if (exitSeriesIndex > 0)
                        ExitShort(exitSeriesIndex, qtyToExit, exitSignal, state.TradeId);
                    else
                        ExitShort(qtyToExit, exitSignal, state.TradeId);
                    StrategyLogInfo(string.Format("[MANUAL][EXIT] submitted ExitShort qty={0} fromEntry={1}", qtyToExit, state.TradeId));
                }

                remaining -= qtyToExit;
                if (remaining <= 0)
                    break;
            }

            int exited = quantity - remaining;

            if (remaining > 0)
            {
                StrategyLogDebug(string.Format("[MANUAL] Requested exit of {0} contracts on {1}, but only {2} were available.", quantity, side, quantity - remaining));
                if (quantity - remaining <= 0)
                {
                    StrategyLogInfo(string.Format("[MANUAL][EXIT] fallback exit triggered side={0} qty={1}", side, quantity));
                    string exitSignal = BuildExitSignalName("FALLBACK", side == MarketPosition.Long ? "MANSELL" : "MANBUY");
                    if (side == MarketPosition.Long)
                    {
                        ExitLong(exitSignal);
                    }
                    else
                    {
                        ExitShort(exitSignal);
                    }
                    StrategyLogInfo(string.Format("[MANUAL][WARN] Fallback exit triggered for {0} contracts on side {1}; trade state mapping missing.", quantity, side));
                    exited = quantity;
                    remaining = 0;
                }
            }
            else if (exited > 0)
            {
                StrategyLogInfo(string.Format("[MANUAL] Submitted exit for {0} contracts on {1} via Chart Trader buttons.", exited, side));
            }
        }

        private IEnumerable<TradeRuntimeState> EnumerateOpenTrades(MarketPosition side)
        {
            for (int i = openTradeOrder.Count - 1; i >= 0; i--)
            {
                string tradeId = openTradeOrder[i];
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out var state))
                {
                    if (state != null && state.EntrySide == side && state.RemainingQuantity > 0)
                        yield return state;
                }
            }
        }

        private void OpenManualEntry(MarketPosition direction, int quantity)
        {
            if (quantity <= 0)
            {
                StrategyLogInfo(string.Format("[MANUAL][ENTRY] skipping entry direction={0} because qty={1}", direction, quantity));
                return;
            }

            if (HasOpposingExternalExposure(direction, out double blockingExposure))
            {
                StrategyLogInfo(string.Format("[MANUAL][ENTRY] blocked entry direction={0} because other strategies are net {1} on this instrument.", direction, blockingExposure));
                return;
            }

            StrategyLogInfo(string.Format("[MANUAL][ENTRY] preparing entry direction={0} qty={1} currentPos={2} posQty={3}", direction, quantity, Position.MarketPosition, Position.Quantity));

            string tradeId = CreateTradeId(direction);
            StrategyLogInfo(string.Format("[MANUAL][ENTRY] created tradeId={0}", tradeId));

            PrepareTradeState(tradeId, direction, quantity);
            ConfigureStopsAndTargetsForTrade(tradeId);
            stopSet = true;
            targetSet = true;

            StrategyLogInfo(string.Format("[MANUAL][ENTRY] submitting order direction={0} qty={1} tradeId={2}", direction, quantity, tradeId));

            try
            {
                int entrySeriesIndex = BarsArray.Length > ManualOrderSeriesIndex ? ManualOrderSeriesIndex : 0;
                if (direction == MarketPosition.Long)
                {
                    if (entrySeriesIndex > 0)
                        EnterLong(entrySeriesIndex, quantity, tradeId);
                    else
                        EnterLong(quantity, tradeId);
                    StrategyLogInfo(string.Format("[MANUAL] Submitted Long order via Chart Trader (trade_id={0}, qty={1})", tradeId, quantity));
                }
                else
                {
                    if (entrySeriesIndex > 0)
                        EnterShort(entrySeriesIndex, quantity, tradeId);
                    else
                        EnterShort(quantity, tradeId);
                    StrategyLogInfo(string.Format("[MANUAL] Submitted Short order via Chart Trader (trade_id={0}, qty={1})", tradeId, quantity));
                }
            }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[MANUAL] Failed to submit {0} order (trade_id={1}): {2}", direction, tradeId, ex.Message));
                tradeStates.Remove(tradeId);
                openTradeOrder.Remove(tradeId);
                if (string.Equals(activeTradeId, tradeId, StringComparison.OrdinalIgnoreCase))
                    activeTradeId = null;
                stopSet = false;
                targetSet = false;
                ResetDemaTrailingState();
            }
        }

        private void ConfigureStopsAndTargetsForTrade(string tradeId)
        {
            if (string.IsNullOrEmpty(tradeId))
                return;

            int stopTicks = 0;
            int targetTicks = 0;

            double atrValue = 0;
            try
            {
                if (atr != null)
                    atrValue = atr[0];
            }
            catch
            {
                atrValue = 0;
            }

            switch (StopType)
            {
                case StopKind.ATR when atrValue > 0 && TickSize > 0:
                    stopTicks = (int)Math.Max(1, Math.Round((atrValue * AtrStopMult) / TickSize));
                    break;
                case StopKind.Ticks:
                default:
                    stopTicks = Math.Max(1, StopTicks);
                    break;
            }

            if (stopTicks <= 0)
                stopTicks = Math.Max(1, StopTicks);

            switch (TargetType)
            {
                case TargetKind.ATR when atrValue > 0 && TickSize > 0:
                    targetTicks = (int)Math.Max(1, Math.Round((atrValue * AtrTargetMult) / TickSize));
                    break;
                case TargetKind.Ticks:
                default:
                    targetTicks = Math.Max(1, TargetTicks);
                    break;
            }

            if (targetTicks <= 0)
                targetTicks = Math.Max(1, TargetTicks);

            if (stopTicks > 0)
                IssueStopLoss(tradeId, CalculationMode.Ticks, stopTicks, false);

            if (targetTicks > 0)
                IssueProfitTarget(tradeId, CalculationMode.Ticks, targetTicks);
        }

        private void RemoveManualChartTraderControls()
        {
            if (attachedChartTrader != null && chartTraderVisibilityHooked)
            {
                attachedChartTrader.IsVisibleChanged -= ChartTrader_IsVisibleChanged;
                chartTraderVisibilityHooked = false;
            }

            if (manualBuyButton != null)
            {
                manualBuyButton.PreviewMouseLeftButtonDown -= ManualBuyButton_PreviewMouseLeftButtonDown;
                manualBuyButton.PreviewKeyDown -= ManualBuyButton_PreviewKeyDown;
                manualBuyButton.Click -= ManualBuyButton_Click;
            }
            if (manualSellButton != null)
            {
                manualSellButton.PreviewMouseLeftButtonDown -= ManualSellButton_PreviewMouseLeftButtonDown;
                manualSellButton.PreviewKeyDown -= ManualSellButton_PreviewKeyDown;
                manualSellButton.Click -= ManualSellButton_Click;
            }
            if (manualFlattenButton != null)
            {
                manualFlattenButton.PreviewMouseLeftButtonDown -= ManualFlattenButton_PreviewMouseLeftButtonDown;
                manualFlattenButton.PreviewKeyDown -= ManualFlattenButton_PreviewKeyDown;
                manualFlattenButton.Click -= ManualFlattenButton_Click;
            }

            if (manualButtonPanel != null)
            {
                if (manualButtonGrid != null)
                {
                    int panelRow = manualButtonRowIndex >= 0 ? manualButtonRowIndex : Grid.GetRow(manualButtonPanel);

                    if (manualButtonGrid.Children.Contains(manualButtonPanel))
                        manualButtonGrid.Children.Remove(manualButtonPanel);

                    if (manualButtonGrid.RowDefinitions != null && panelRow >= 0 && panelRow < manualButtonGrid.RowDefinitions.Count)
                    {
                        manualButtonGrid.RowDefinitions.RemoveAt(panelRow);
                        foreach (UIElement child in manualButtonGrid.Children.OfType<UIElement>().ToList())
                        {
                            int currentRow = Grid.GetRow(child);
                            if (currentRow > panelRow)
                                Grid.SetRow(child, currentRow - 1);
                        }
                    }
                }
                else if (manualButtonPanel.Parent is Panel parentPanel)
                {
                    parentPanel.Children.Remove(manualButtonPanel);
                }
                else if (manualButtonPanel.Parent is ContentControl contentControl)
                {
                    contentControl.Content = null;
                }
            }

            manualBuyButton = null;
            manualSellButton = null;
            manualFlattenButton = null;
            manualButtonPanel = null;
            manualButtonGrid = null;
            manualButtonRowDefinition = null;
            manualButtonRowIndex = -1;
            manualButtonStyleSource = null;
            openTradeOrder.Clear();
            manualButtonHost = null;
            attachedChartTrader = null;
            attachedChart = null;
            manualTreeLogged = false;
            manualBuyClickArmed = false;
            manualSellClickArmed = false;
            manualFlattenClickArmed = false;
        }

        private void RemoveExistingManualPanel(Grid traderGrid)
        {
            if (traderGrid == null || traderGrid.Children == null)
                return;

            foreach (var existingPanel in traderGrid.Children.OfType<Grid>().Where(g => manualPanelTag.Equals(g.Tag as string, StringComparison.Ordinal)).ToList())
            {
                int row = Grid.GetRow(existingPanel);
                traderGrid.Children.Remove(existingPanel);

                if (traderGrid.RowDefinitions != null && row >= 0 && row < traderGrid.RowDefinitions.Count)
                {
                    traderGrid.RowDefinitions.RemoveAt(row);
                    foreach (UIElement child in traderGrid.Children.OfType<UIElement>().ToList())
                    {
                        int currentRow = Grid.GetRow(child);
                        if (currentRow > row)
                            Grid.SetRow(child, currentRow - 1);
                    }
                }
            }
        }

        private void PublishOpenEvent(TradeRuntimeState state)
        {
            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return;

            manager.TradeSync.PublishOpen(this, state.TradeId, state.InstrumentName, state.EntrySide, state.OriginalQuantity, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice);
        }

        private void PublishPartialEvent(string tradeId, int remainingQuantity)
        {
            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return;

            manager.TradeSync.PublishPartial(this, tradeId, remainingQuantity);
        }

        private void PublishClosedEvent(string tradeId)
        {
            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return;

            manager.TradeSync.PublishClosed(this, tradeId);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            base.OnOrderUpdate(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, nativeError);

            if (order == null)
                return;

            string name = order.Name ?? "<null>";
            bool looksManual = false;
            if (!string.IsNullOrEmpty(name))
            {
                looksManual = name.StartsWith("L", StringComparison.OrdinalIgnoreCase) ||
                              name.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
                              name.IndexOf("MAN", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            if (!looksManual && !Debug)
                return;

            StrategyLogInfo(string.Format("[MANUAL][ORDERUPD] name={0} fromEntry={1} action={2} state={3} qty={4} filled={5} oco={6} stop={7} limit={8} error={9} native='{10}'",
                name,
                order.FromEntrySignal ?? "<null>",
                order.OrderAction,
                orderState,
                order.Quantity,
                order.Filled,
                order.Oco ?? "<none>",
                stopPrice,
                limitPrice,
                error,
                string.IsNullOrEmpty(nativeError) ? "<none>" : nativeError));

            DetectManualStopTargetAdjustments(order, limitPrice, stopPrice, orderState);
            HandleStopUpdateErrors(order, stopPrice, orderState, error, nativeError);
        }

        private void DetectManualStopTargetAdjustments(Order order, double limitPrice, double stopPrice, OrderState orderState)
        {
            if (order == null)
                return;
            string tradeId = order.FromEntrySignal;
            if (string.IsNullOrEmpty(tradeId))
                return;
            if (!TryGetTradeState(tradeId, out var state))
                return;

            string orderName = order.Name ?? string.Empty;
            bool isStopOrder = (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit) &&
                orderName.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isTargetOrder = order.OrderType == OrderType.Limit &&
                (orderName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 orderName.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isStopOrder)
            {
                double effectivePrice = stopPrice;
                if (effectivePrice <= 0 && order.StopPrice > 0)
                    effectivePrice = order.StopPrice;
                if (effectivePrice <= 0)
                    return;

                if (state.PendingAutoStopUpdate)
                {
                    if (!IsStopUpdateFinal(orderState))
                        return;

                    if (PricesClose(state.PendingAutoStopPrice, effectivePrice))
                    {
                        state.PendingAutoStopUpdate = false;
                        state.PendingAutoStopPrice = 0;
                        state.LastStopPrice = effectivePrice;
                        return;
                    }
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                }

                if (state.LastStopPrice <= 0)
                {
                    state.LastStopPrice = effectivePrice;
                    return;
                }

                if (!PricesClose(state.LastStopPrice, effectivePrice))
                {
                    state.LastStopPrice = effectivePrice;
                    bool wasLocked = state.ManualStopOverride;
                    state.ManualStopOverride = true;
                    StrategyLogInfo(string.Format("[MANUAL][STOP] Detected manual stop move for {0} -> {1:F2}; auto trailing disabled for this trade.", tradeId, effectivePrice));
                    if (!wasLocked)
                        NotifyAddonManualOverride(tradeId, true, null);
                    AlignManagedStopWithManual(tradeId, effectivePrice);
                }
            }
            else if (isTargetOrder)
            {
                double effectivePrice = limitPrice;
                if (effectivePrice <= 0 && order.LimitPrice > 0)
                    effectivePrice = order.LimitPrice;
                if (effectivePrice <= 0)
                    return;

                if (state.PendingAutoTargetUpdate)
                {
                    if (!IsStopUpdateFinal(orderState))
                        return;

                    state.PendingAutoTargetUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    state.LastTargetPrice = effectivePrice;
                    return;
                }

                if (state.LastTargetPrice <= 0)
                {
                    state.LastTargetPrice = effectivePrice;
                    return;
                }

                if (!PricesClose(state.LastTargetPrice, effectivePrice))
                {
                    state.LastTargetPrice = effectivePrice;
                    bool wasLocked = state.ManualTargetOverride;
                    state.ManualTargetOverride = true;
                    StrategyLogInfo(string.Format("[MANUAL][TARGET] Detected manual target move for {0} -> {1:F2}; auto target locked for this trade.", tradeId, effectivePrice));
                    if (!wasLocked)
                        NotifyAddonManualOverride(tradeId, null, true);
                    AlignManagedTargetWithManual(tradeId, effectivePrice);
                }
            }
        }

        private void HandleStopUpdateErrors(Order order, double stopPrice, OrderState orderState, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            string tradeId = order.FromEntrySignal;
            if (string.IsNullOrEmpty(tradeId))
                return;
            if (!TryGetTradeState(tradeId, out var state))
                return;
            if (state.ManualStopOverride)
                return;

            string orderName = order.Name ?? string.Empty;
            bool isStopOrder = (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit) &&
                orderName.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isStopOrder)
                return;

            bool errorState = error != ErrorCode.NoError || orderState == OrderState.Rejected || orderState == OrderState.Cancelled;
            if (!errorState)
                return;

            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return;

            double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
            if (!lastAccepted.HasValue || lastAccepted.Value <= 0)
                return;

            double currentPrice = GetRealtimePrice();
            bool isLong = Position.MarketPosition == MarketPosition.Long;
            var safe = ClampStopPrice(lastAccepted.Value, currentPrice, isLong, lastAccepted);
            if (!safe.HasValue)
            {
                StrategyLogInfo(string.Format("[STOP_ROLLBACK] Skipped rollback for {0} | last={1:F2} price={2:F2} state={3} err={4} native='{5}'",
                    tradeId,
                    lastAccepted.Value,
                    currentPrice,
                    orderState,
                    error,
                    string.IsNullOrEmpty(nativeError) ? "<none>" : nativeError));
                return;
            }

            state.PendingAutoStopUpdate = true;
            state.PendingAutoStopPrice = safe.Value;
            try
            {
                SetStopLoss(tradeId, CalculationMode.Price, safe.Value, false);
                state.LastStopPrice = safe.Value;
                if (state.RunUpActive)
                    state.RunUpLastStopPrice = safe.Value;
                StrategyLogInfo(string.Format("[STOP_ROLLBACK] Reapplied stop for {0} at {1:F2} after rejected update (state={2}, err={3})",
                    tradeId,
                    safe.Value,
                    orderState,
                    error));
            }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[STOP_ROLLBACK] Failed to reapply stop for {0} at {1:F2}: {2}",
                    tradeId,
                    safe.Value,
                    ex.Message));
            }
            finally
            {
                state.PendingAutoStopUpdate = false;
                state.PendingAutoStopPrice = 0;
            }
        }

        private void NotifyAddonManualOverride(string tradeId, bool? stopLocked, bool? targetLocked)
        {
            if (string.IsNullOrEmpty(tradeId))
                return;

            var manager = MultiStratManager.Instance;
            manager?.TradeSync?.PublishManualOverride(this, tradeId, stopLocked, targetLocked);
        }

        private void AlignManagedStopWithManual(string tradeId, double price)
        {
            if (string.IsNullOrEmpty(tradeId) || price <= 0)
                return;

            if (!TryGetTradeState(tradeId, out var state))
                return;

            if (state.IsSynthetic)
                return;

            state.PendingAutoStopUpdate = true;
            try
            {
                SetStopLoss(tradeId, CalculationMode.Price, price, false);
            }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[MANUAL][STOP] Failed to align managed stop for {0}: {1}", tradeId, ex.Message));
            }
            finally
            {
                state.PendingAutoStopUpdate = false;
            }
        }

        private void AlignManagedTargetWithManual(string tradeId, double price)
        {
            if (string.IsNullOrEmpty(tradeId) || price <= 0)
                return;

            if (!TryGetTradeState(tradeId, out var state))
                return;

            if (state.IsSynthetic)
                return;

            state.PendingAutoTargetUpdate = true;
            try
            {
                SetProfitTarget(tradeId, CalculationMode.Price, price);
            }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[MANUAL][TARGET] Failed to align managed target for {0}: {1}", tradeId, ex.Message));
            }
            finally
            {
                state.PendingAutoTargetUpdate = false;
            }
        }

        private static string BuildExitSignalName(string tradeId, string suffix)
        {
            string signal = string.Format("{0}_{1}", tradeId, suffix);
            if (signal.Length <= 50)
                return signal;
            return signal.Substring(0, 50);
        }

        private void StrategyLogInfo(string message)
        {
            Print(message);
            var manager = MultiStratManager.Instance;
            if (manager != null)
            {
                string tradeRef = activeTradeId ?? string.Empty;
                manager.LogInfo("STRATEGY", message, tradeRef, tradeRef);
            }
        }

        private void StrategyLogDebug(string message)
        {
            Print(message);
            var manager = MultiStratManager.Instance;
            if (manager != null)
            {
                string tradeRef = activeTradeId ?? string.Empty;
                manager.LogDebug("STRATEGY", message, tradeRef, tradeRef);
            }
        }

        private void StrategyLogError(string message)
        {
            Print(message);
            var manager = MultiStratManager.Instance;
            if (manager != null)
            {
                string tradeRef = activeTradeId ?? string.Empty;
                manager.LogError("STRATEGY", message, 0, tradeRef, tradeRef);
            }
        }

        void ITradeSyncParticipant.HandleTradeSyncPartial(string tradeId, int quantityToExit)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || quantityToExit <= 0)
                return;

            if (tradeStates == null)
                return;

            TradeRuntimeState state;
            if (!tradeStates.TryGetValue(tradeId, out state))
                return;

            int qty = Math.Min(quantityToExit, Math.Max(0, state.RemainingQuantity));
            if (qty <= 0)
                return;

            string exitSignal = BuildExitSignalName(tradeId, "EXT");
            if (state.EntrySide == MarketPosition.Long)
                ExitLong(qty, exitSignal, tradeId);
            else
                ExitShort(qty, exitSignal, tradeId);
        }

        void ITradeSyncParticipant.HandleTradeSyncClose(string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            if (tradeStates == null)
                return;

            TradeRuntimeState state;
            if (!tradeStates.TryGetValue(tradeId, out state))
                return;

            int qty = Math.Max(0, state.RemainingQuantity);
            if (qty <= 0)
                return;

            string exitSignal = BuildExitSignalName(tradeId, "CLS");
            if (state.EntrySide == MarketPosition.Long)
                ExitLong(qty, exitSignal, tradeId);
            else
                ExitShort(qty, exitSignal, tradeId);
        }

        void IRunUpParticipant.HandleRunUpStart(string tradeId, double anchorPrice, RunUpConfig config)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || config == null || !config.Enabled)
                return;
            if (!TryGetTradeState(tradeId, out var state))
                return;

            double distance = ConvertRunUpValueToPrice(config.DistanceUnits, config.DistanceValue);
            double increment = ConvertRunUpValueToPrice(config.IncrementUnits, config.IncrementValue);
            if (distance <= 0)
            {
                StrategyLogInfo(string.Format("[RUN_UP] Skip activation for {0}: distance must be > 0 (got {1:F4})", tradeId, distance));
                return;
            }

            state.RunUpActive = true;
            state.RunUpAnchorPrice = anchorPrice;
            state.RunUpInitialDistance = distance;
            state.RunUpIncrement = increment;
            state.RunUpLastStopPrice = null;
            state.RunUpHighWater = anchorPrice;
            state.RunUpLowWater = anchorPrice;

            if (string.IsNullOrEmpty(activeTradeId))
                activeTradeId = tradeId;

            bool isLong = Position != null && Position.MarketPosition == MarketPosition.Long;
            double desiredStop = isLong ? anchorPrice - distance : anchorPrice + distance;
            double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
            var clamped = ClampStopPrice(desiredStop, anchorPrice, isLong, lastAccepted);
            if (clamped.HasValue)
            {
                if (IssueStopLoss(tradeId, CalculationMode.Price, clamped.Value, false))
                {
                    state.RunUpLastStopPrice = clamped.Value;
                    stopSet = true;
                    StrategyLogInfo(string.Format("[RUN_UP] Activated for {0}: anchor={1:F2}, stop={2:F2}, dist={3:F4}, inc={4:F4}", tradeId, anchorPrice, clamped.Value, distance, increment));
                }
            }
        }

        #region Params

        public enum StopKind { Ticks, ATR }
        public enum TargetKind { Ticks, ATR }
        // Legacy ATR trailing enum retained for documentation reference.
        // public enum TrailKind { None, Ticks, ATR }

        [NinjaScriptProperty, Range(2, 100), Display(Name = "AtrPeriod", GroupName = "03 - Indicator Periods", Order = 10)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty, Display(Name = "StopType", GroupName = "04 - Stops & Targets", Order = 0)]
        public StopKind StopType { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "StopTicks", GroupName = "04 - Stops & Targets", Order = 1)]
        public int StopTicks { get; set; }

        [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "AtrStopMult", GroupName = "04 - Stops & Targets", Order = 2)]
        public double AtrStopMult { get; set; }

        [NinjaScriptProperty, Display(Name = "TargetType", GroupName = "04 - Stops & Targets", Order = 3)]
        public TargetKind TargetType { get; set; }

        [NinjaScriptProperty, Range(1, 400), Display(Name = "TargetTicks", GroupName = "04 - Stops & Targets", Order = 4)]
        public int TargetTicks { get; set; }

        [NinjaScriptProperty, Range(0.5, 20.0), Display(Name = "AtrTargetMult", GroupName = "04 - Stops & Targets", Order = 5)]
        public double AtrTargetMult { get; set; }

        // [NinjaScriptProperty, Display(Name = "TrailType", GroupName = "04 - Stops & Targets", Order = 6)]
        // public TrailKind TrailType { get; set; }

        // [NinjaScriptProperty, Range(1, 200), Display(Name = "TrailTicks", GroupName = "04 - Stops & Targets", Order = 7)]
        // public int TrailTicks { get; set; }

        // [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "AtrTrailMult", GroupName = "04 - Stops & Targets", Order = 8)]
        // public double AtrTrailMult { get; set; }

        [NinjaScriptProperty, Display(Name = "Use DEMA ATR Trailing", GroupName = "05 - DEMA ATR Trailing", Order = 0)]
        public bool UseDemaAtrTrailing { get; set; }

        [NinjaScriptProperty, Display(Name = "DEMA ATR Activation Mode", GroupName = "05 - DEMA ATR Trailing", Order = 1)]
        public TrailingActivationType DemaAtrActivationMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 10000.0), Display(Name = "DEMA ATR Activation Value", GroupName = "05 - DEMA ATR Trailing", Order = 2)]
        public double DemaAtrActivationValue { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "DEMA ATR Period", GroupName = "05 - DEMA ATR Trailing", Order = 3)]
        public int DemaAtrPeriod { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name = "DEMA ATR Multiplier", GroupName = "05 - DEMA ATR Trailing", Order = 4)]
        public double DemaAtrMultiplier { get; set; }

        [NinjaScriptProperty, Display(Name = "UseBreakEven", GroupName = "06 - BreakEven", Order = 0)]
        public bool UseBreakEven { get; set; }

        [NinjaScriptProperty, Range(1, 400), Display(Name = "BreakEvenTriggerTicks", GroupName = "06 - BreakEven", Order = 1)]
        public int BreakEvenTriggerTicks { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "BreakEvenPlusTicks", GroupName = "06 - BreakEven", Order = 2)]
        public int BreakEvenPlusTicks { get; set; }

        [NinjaScriptProperty, Display(Name = "Debug", GroupName = "07 - Misc", Order = 0)]
        public bool Debug { get; set; }

        #endregion
    }
}
