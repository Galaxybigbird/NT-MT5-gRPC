#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Shared;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.AddOns;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class BaseOptStrategyAuto : Strategy, ITradeSyncParticipant, IRunUpParticipant
    {
        // --- indicator refs
        private SMA sma;
        private EMA emaFast, emaSlow;
        private RSI rsi;
        private MACD macd;
        private ATR atr;

        // --- internal
        private bool stopSet, targetSet;
        private bool demaTrailingActive;
        private double demaHighWater;
        private double demaLowWater;
        private int maxSignalSlots; // number of enabled indicator families (SMA/EMA/RSI/MACD)

        private static long tradeSequence;
        private readonly List<string> openTradeOrder = new List<string>();
        private Dictionary<string, TradeRuntimeState> tradeStates;
        private string activeTradeId;

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
                    Name = "BaseOptStrategyAuto";
                    Calculate = Calculate.OnBarClose;
                    IsOverlay = false;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 60;
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;

                // Toggles
                UseSMA = true;
                UseEMA = true;
                UseRSI = true;
                UseMACD = true;

                // Signal control
                Bias = TradeBias.Both;
                MinSignalsToEnterLong = 2;
                MinSignalsToEnterShort = 2;

                // Indicator params
                SmaPeriod = 50;
                EmaFast = 12;
                EmaSlow = 26;
                RsiPeriod = 14;
                RsiSmooth = 3;
                RsiLongThreshold = 55;
                RsiShortThreshold = 45;
                MacdFast = 12;
                MacdSlow = 26;
                MacdSmooth = 9;
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

                    // optional: log parameters once per iteration for diagnostics
                if (Debug)
                {
                    StrategyLogDebug($"PARAMS: Bias={Bias}, MinLong={MinSignalsToEnterLong}, MinShort={MinSignalsToEnterShort}, UseSMA={UseSMA}, SmaPeriod={SmaPeriod}, UseEMA={UseEMA}, EmaFast={EmaFast}, EmaSlow={EmaSlow}, UseRSI={UseRSI}, RsiPeriod={RsiPeriod}, RsiSmooth={RsiSmooth}, RsiLong={RsiLongThreshold}, RsiShort={RsiShortThreshold}, UseMACD={UseMACD}, MacdFast={MacdFast}, MacdSlow={MacdSlow}, MacdSmooth={MacdSmooth}, AtrPeriod={AtrPeriod}, StopType={StopType}, StopTicks={StopTicks}, AtrStopMult={AtrStopMult}, TargetType={TargetType}, TargetTicks={TargetTicks}, AtrTargetMult={AtrTargetMult}, UseDemaAtrTrailing={UseDemaAtrTrailing}, DemaAtrPeriod={DemaAtrPeriod}, DemaAtrMultiplier={DemaAtrMultiplier}, DemaAtrActivationMode={DemaAtrActivationMode}, DemaAtrActivationValue={DemaAtrActivationValue}, UseBreakEven={UseBreakEven}, BETriggerTicks={BreakEvenTriggerTicks}, BEPlusTicks={BreakEvenPlusTicks}");
                }

                if (tradeStates == null)
                    tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                else
                    tradeStates.Clear();

                activeTradeId = null;
                openTradeOrder.Clear();

            }
            else if (State == State.DataLoaded)
            {
                if (UseSMA) sma = SMA(Close, SmaPeriod);
                if (UseEMA) { emaFast = EMA(Close, EmaFast); emaSlow = EMA(Close, EmaSlow); }
                if (UseRSI) rsi = RSI(Close, RsiPeriod, RsiSmooth);
                if (UseMACD) macd = MACD(Close, MacdFast, MacdSlow, MacdSmooth);
                atr = ATR(AtrPeriod);

                if (UseSMA) AddChartIndicator(sma);
                if (UseEMA) { AddChartIndicator(emaFast); AddChartIndicator(emaSlow); }
                if (UseRSI) AddChartIndicator(rsi);
                if (UseMACD) AddChartIndicator(macd);
                AddChartIndicator(atr);

				// Compute how many indicator families are enabled so we can cap required votes safely
				maxSignalSlots = (UseSMA ? 1 : 0) + (UseEMA ? 1 : 0) + (UseRSI ? 1 : 0) + (UseMACD ? 1 : 0);
				if (maxSignalSlots <= 0) maxSignalSlots = 1; // prevent zero causing impossible thresholds
					if (Debug && (MinSignalsToEnterLong > maxSignalSlots || MinSignalsToEnterShort > maxSignalSlots))
						StrategyLogInfo($"WARN: MinSignals exceeds enabled indicators; capping in runtime. slots={maxSignalSlots} effMinL={Math.Min(MinSignalsToEnterLong, maxSignalSlots)} effMinS={Math.Min(MinSignalsToEnterShort, maxSignalSlots)}");

                ResetTradeState();

                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.RegisterStrategy(this);
            }
            else if (State == State.Realtime)
            {
                // Flush any historical bookkeeping so live executions start from a clean slate.
                ResetTradeState();
                StrategyLogInfo("[AUTO] Strategy entered realtime; automation enabled");

                BootstrapExistingPositionState();
            }
            else if (State == State.Terminated)
            {
                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.UnregisterStrategy(this);

                ResetTradeState();
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

            // Build signals
            int longVotes = 0, shortVotes = 0;

            if (UseSMA)
            {
                bool longCond = Close[0] > sma[0] && sma[0] > sma[1];
                bool shortCond = Close[0] < sma[0] && sma[0] < sma[1];
                if (longCond) longVotes++;
                if (shortCond) shortVotes++;
            }

            if (UseEMA)
            {
                bool longCond = emaFast[0] > emaSlow[0];
                bool shortCond = emaFast[0] < emaSlow[0];
                if (longCond) longVotes++;
                if (shortCond) shortVotes++;
            }

            if (UseRSI)
            {
                bool longCond = CrossAbove(rsi.Avg, RsiLongThreshold, 1);
                bool shortCond = CrossBelow(rsi.Avg, RsiShortThreshold, 1);
                if (longCond) longVotes++;
                if (shortCond) shortVotes++;
            }

            if (UseMACD)
            {
                double hist = macd.Default[0] - macd.Avg[0];
                if (hist > 0) longVotes++;
                if (hist < 0) shortVotes++;
            }

            int effMinLong = Math.Max(1, Math.Min(MinSignalsToEnterLong, maxSignalSlots));
            int effMinShort = Math.Max(1, Math.Min(MinSignalsToEnterShort, maxSignalSlots));
            bool canLong = (Bias == TradeBias.Both || Bias == TradeBias.LongOnly) && longVotes >= effMinLong;
            bool canShort = (Bias == TradeBias.Both || Bias == TradeBias.ShortOnly) && shortVotes >= effMinShort;

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
                else
                {
                    if (!desyncHoldActive)
                    {
                        desyncHoldActive = true;
                        desyncHoldActivatedAt = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                        StrategyLogInfo(string.Format("[AUTO][DESYNC] Holding automation because NT reports {0} qty={1} while account exposure and trade state are empty.",
                            Position != null ? Position.MarketPosition.ToString() : "Flat",
                            Position != null ? Position.Quantity : 0));
                    }
                }
            }

            if (desyncHoldActive)
            {
                if (isFlatPosition && !accountHasExposure)
                {
                    desyncHoldActive = false;
                    StrategyLogInfo("[AUTO][DESYNC] Platform/account mismatch resolved; automation resumed.");
                }
                else
                {
                    if (Debug)
                    {
                        StrategyLogDebug(string.Format("[AUTO][DESYNC] Waiting for platform flatten (pos={0} qty={1}).",
                            Position != null ? Position.MarketPosition.ToString() : "Flat",
                            Position != null ? Position.Quantity : 0));
                    }
                    return;
                }
            }

            // Manage orders
            if (isFlatPosition)
            {
                stopSet = targetSet = false;
                activeTradeId = null;
                ResetDemaTrailingState();

                if (canLong)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Long))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterLong because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                    }
                    else
                    {
                        string tradeId = CreateTradeId(MarketPosition.Long);
                        PrepareTradeState(tradeId, MarketPosition.Long, Math.Max(1, DefaultQuantity));
                        if (Debug) StrategyLogDebug($"{Time[0]} EnterLong({tradeId}) votes={longVotes} effMin={effMinLong}");
                        EnterLong(tradeId);
                    }
                }
                else if (canShort)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Short))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterShort because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                    }
                    else
                    {
                        string tradeId = CreateTradeId(MarketPosition.Short);
                        PrepareTradeState(tradeId, MarketPosition.Short, Math.Max(1, DefaultQuantity));
                        if (Debug) StrategyLogDebug($"{Time[0]} EnterShort({tradeId}) votes={shortVotes} effMin={effMinShort}");
                        EnterShort(tradeId);
                    }
                }
            }
            else if (hasTrackedTrades)
            {
                UpdateStopsTargets(GetRealtimePrice());
            }

            if (Debug)
                StrategyLogDebug($"{Time[0]} votes L/S: {longVotes}/{shortVotes} canL={canLong} canS={canShort} bias={Bias} minL={MinSignalsToEnterLong}->{effMinLong} minS={MinSignalsToEnterShort}->{effMinShort} Pos:{Position.MarketPosition}");
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
                        var clamped = ClampStopPrice(be, currentPrice, true);
                        if (clamped.HasValue)
                            IssueStopLoss(activeTradeId, CalculationMode.Price, clamped.Value, false);
                    }
                    else if (Position.MarketPosition == MarketPosition.Short &&
                             currentPrice <= entry - BreakEvenTriggerTicks * TickSize)
                    {
                        double be = entry - BreakEvenPlusTicks * TickSize;
                        if (Debug) StrategyLogDebug($"{Time[0]} BE SHORT trigger: entry={entry:F2} price={currentPrice:F2} be={be:F2}");
                        var clamped = ClampStopPrice(be, currentPrice, false);
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

            double? safePrice = ClampStopPrice(rounded, currentPrice, isLong);
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
                double entry = Position.AveragePrice;
                if (entry <= 0 || double.IsNaN(entry))
                    return 0;
                double direction = Position.MarketPosition == MarketPosition.Long ? 1.0 : -1.0;
                double pointValue = Instrument?.MasterInstrument?.PointValue ?? 1.0;
                double diff = (price - entry) * direction;
                return Math.Abs(diff * pointValue);
            }
        }

        private double? ClampStopPrice(double desiredPrice, double currentPrice, bool isLong)
        {
            if (desiredPrice <= 0 || currentPrice <= 0 || double.IsNaN(desiredPrice) || double.IsNaN(currentPrice))
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = Math.Max(Math.Abs(currentPrice) * 1e-6, 1e-6);

            if (isLong)
            {
                double maxAllowed = currentPrice - tickSize;
                double clamped = Math.Min(desiredPrice, maxAllowed);
                if (clamped <= 0 || clamped >= currentPrice)
                    return null;
                return clamped;
            }
            else
            {
                double minAllowed = currentPrice + tickSize;
                double clamped = Math.Max(desiredPrice, minAllowed);
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

            // Track high/low water to keep run-up monotonic (no stop loosening on pullbacks)
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

            var clamped = ClampStopPrice(desiredStop, currentPrice, isLong);
            if (!clamped.HasValue)
                return;

            if (state.RunUpLastStopPrice.HasValue && PricesClose(state.RunUpLastStopPrice.Value, clamped.Value))
                return;

            if (IssueStopLoss(activeTradeId, CalculationMode.Price, clamped.Value, false))
            {
                state.RunUpLastStopPrice = clamped.Value;
                stopSet = true;
                if (Debug)
                    StrategyLogDebug(string.Format("[RUN_UP] Updated stop to {0:F2} (anchor={1:F2}, dist={2:F4}, inc={3:F4})", clamped.Value, anchor, distance, increment));
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
                    StrategyLogDebug(string.Format("[AUTO][DESYNC] Unable to inspect account positions: {0}", ex.Message));
            }

            return false;
        }

        private double GetOtherStrategyExposure()
        {
            if (Account == null || Instrument == null)
                return 0;

            var manager = MultiStratManager.Instance;
            if (manager == null)
                return GetOtherExposureFallback();

            bool hasData;
            double net = manager.GetNetExposure(Account.Name, Instrument.FullName, this, out hasData);
            return hasData ? net : GetOtherExposureFallback();
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
                    StrategyLogDebug(string.Format("[AUTO][GUARD] Failed to read account exposure fallback: {0}", ex.Message));
            }

            return accountQty - GetSignedStrategyPosition();
        }

        private bool HasOpposingExternalExposure(MarketPosition desiredDirection, out double otherExposure)
        {
            otherExposure = GetOtherStrategyExposure();
            const double tolerance = 1e-6;
            if (desiredDirection == MarketPosition.Long)
                return otherExposure < -tolerance;
            if (desiredDirection == MarketPosition.Short)
                return otherExposure > tolerance;
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
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Account is flat; skipping synthetic position seed despite NinjaTrader position reporting {0}", Position.MarketPosition));
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
            StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Seeded synthetic trade {0} for existing position {1} qty={2}", tradeId, side, qty));

        }

        private void ResetTradeState()
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
                RunUpLastStopPrice = null
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
                    StrategyLogDebug(string.Format("[AUTO][DESYNC] Ignoring cleanup execution for order '{0}'", execution.Order != null ? execution.Order.Name : "<unknown>"));
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
                if (exitOnClose)
                {
                    if (Debug)
                        StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Exit-on-close execution received without matching trade state for order '{1}'", time, execution.Order.Name ?? "<unknown>"));
                    return;
                }

                if (string.IsNullOrEmpty(execution.Order.FromEntrySignal))
                {
                    MarketPosition inferredSide = (execution.Order.OrderAction == OrderAction.SellShort || execution.Order.OrderAction == OrderAction.Sell)
                        ? MarketPosition.Short
                        : MarketPosition.Long;
                    state = PrepareTradeState(tradeId, inferredSide, Math.Max(1, Math.Abs((int)execution.Order.Quantity)));
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

            bool isEntry = string.IsNullOrEmpty(execution.Order.FromEntrySignal) && !exitOnClose;
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
            var clamped = ClampStopPrice(desiredStop, anchorPrice, isLong);
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

        public enum TradeBias { Both, LongOnly, ShortOnly }
        public enum StopKind { Ticks, ATR }
        public enum TargetKind { Ticks, ATR }
        // Legacy ATR trailing enum retained for documentation reference.
        // public enum TrailKind { None, Ticks, ATR }

        [NinjaScriptProperty, Display(Name = "Bias", GroupName = "01 - Bias & Voting", Order = 0)]
        public TradeBias Bias { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "MinSignalsToEnterLong", GroupName = "01 - Bias & Voting", Order = 1)]
        public int MinSignalsToEnterLong { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "MinSignalsToEnterShort", GroupName = "01 - Bias & Voting", Order = 2)]
        public int MinSignalsToEnterShort { get; set; }

        [NinjaScriptProperty, Display(Name = "UseSMA", GroupName = "02 - Indicator Toggles", Order = 0)]
        public bool UseSMA { get; set; }

        [NinjaScriptProperty, Display(Name = "UseEMA", GroupName = "02 - Indicator Toggles", Order = 1)]
        public bool UseEMA { get; set; }

        [NinjaScriptProperty, Display(Name = "UseRSI", GroupName = "02 - Indicator Toggles", Order = 2)]
        public bool UseRSI { get; set; }

        [NinjaScriptProperty, Display(Name = "UseMACD", GroupName = "02 - Indicator Toggles", Order = 3)]
        public bool UseMACD { get; set; }

        [NinjaScriptProperty, Range(2, 400), Display(Name = "SmaPeriod", GroupName = "03 - Indicator Periods", Order = 0)]
        public int SmaPeriod { get; set; }

        [NinjaScriptProperty, Range(2, 200), Display(Name = "EmaFast", GroupName = "03 - Indicator Periods", Order = 1)]
        public int EmaFast { get; set; }

        [NinjaScriptProperty, Range(2, 400), Display(Name = "EmaSlow", GroupName = "03 - Indicator Periods", Order = 2)]
        public int EmaSlow { get; set; }

        [NinjaScriptProperty, Range(2, 100), Display(Name = "RsiPeriod", GroupName = "03 - Indicator Periods", Order = 3)]
        public int RsiPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "RsiSmooth", GroupName = "03 - Indicator Periods", Order = 4)]
        public int RsiSmooth { get; set; }

        [NinjaScriptProperty, Range(50, 90), Display(Name = "RsiLongThreshold", GroupName = "03 - Indicator Periods", Order = 5)]
        public int RsiLongThreshold { get; set; }

        [NinjaScriptProperty, Range(10, 50), Display(Name = "RsiShortThreshold", GroupName = "03 - Indicator Periods", Order = 6)]
        public int RsiShortThreshold { get; set; }

        [NinjaScriptProperty, Range(2, 50), Display(Name = "MacdFast", GroupName = "03 - Indicator Periods", Order = 7)]
        public int MacdFast { get; set; }

        [NinjaScriptProperty, Range(5, 100), Display(Name = "MacdSlow", GroupName = "03 - Indicator Periods", Order = 8)]
        public int MacdSlow { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "MacdSmooth", GroupName = "03 - Indicator Periods", Order = 9)]
        public int MacdSmooth { get; set; }

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
