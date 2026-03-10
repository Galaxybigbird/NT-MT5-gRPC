#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.AddOns;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Shared;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class PineScriptalgoBridgeStrategy : Strategy, ITradeSyncParticipant, IRunUpParticipant
    {
        private const int MaxTradesPerEntry = 10;
        private const int TickSeriesIndex = 1;
        private const double DailyProfitConfirmSeconds = 0.75;
        private const string StatusTag = "PINE79_STATUS";
        private const string StatusPnlTag = "PINE79_STATUS_PNL";
        private const string StatusLimitsTag = "PINE79_STATUS_LIMITS";
        private const string ChecklistGreenTag = "PINE79_CHECKLIST_GREEN";
        private const string ChecklistRedTag = "PINE79_CHECKLIST_RED";
        private const string ChecklistNeutralTag = "PINE79_CHECKLIST_NEUTRAL";
        private const string RibbonTopTag = "PINE79_RIB_TOP";
        private const string RibbonBottomTag = "PINE79_RIB_BOT";
        private const string EntryLineTag = "PINE79_ENTRY";
        private const string StopLineTag = "PINE79_STOP";
        private const string Tp1LineTag = "PINE79_TP1";
        private const string Tp2LineTag = "PINE79_TP2";
        private const string Tp3LineTag = "PINE79_TP3";

        private static long tradeSequence;

        private Dictionary<string, PineTradeRuntimeState> tradeStates;
        private Dictionary<string, string> entrySignalToTradeId;
        private Dictionary<string, PineTradeSyncGroup> syncGroups;
        private Dictionary<string, Order> workingEntryOrders;
        private readonly List<string> openTradeOrder = new List<string>();

        private double pineCondition;
        private double pineEntryLine;
        private double pineSlLine;
        private double pineTp1Line;
        private double pineTp2Line;
        private double pineTp3Line;
        private DateTime pineEntryStartTime;
        private int atrEntryQuantity;
        private MarketPosition atrEntrySide;

        private int signalSeriesIndex;
        private BarsPeriodType signalBarsType;
        private int signalBarsValue;
        private bool signalSeriesUsesPrimary;

        private int tradesPerEntryOverride;
        private double? runtimeDailyLossLimit;
        private double? runtimeDailyProfitLimit;
        private int lastTradesPerEntryDisplay = -1;
        private double lastDllDisplay = double.NaN;
        private double lastDplDisplay = double.NaN;

        private bool manualHaltActive;
        private DateTime manualHaltActivatedAt = DateTime.MinValue;
        private string haltReason = string.Empty;

        private bool dailyLimitHalted;
        private string dailyLimitType = string.Empty;
        private double dailyLimitTriggeredPnl;
        private DateTime dailyLimitTriggeredAt = DateTime.MinValue;
        private DateTime dailyProfitCandidateAt = DateTime.MinValue;
        private double dailyProfitCandidatePnl;
        private DateTime trackedSessionDate = Core.Globals.MinDate;

        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private RowDefinition chartTraderButtonsRow;
        private StackPanel chartTraderButtonPanel;
        private Button manualFlattenButton;
        private Button manualResumeButton;
        private Button manualBuyButton;
        private Button manualSellButton;
        private TextBlock tradesPerEntryLabel;
        private TextBox tradesPerEntryTextBox;
        private TextBlock dllLabel;
        private TextBox dllTextBox;
        private TextBlock dplLabel;
        private TextBox dplTextBox;
        private bool chartTraderButtonsAdded;
        private bool lastManualButtonsEnabled;
        private bool lastResumeEnabled;
        private PineEvalState lastUiEvalState;
        private string lastStatusText = string.Empty;
        private bool lastStatusHealthy;
        private bool lastStatusHasPnLLines;
        private bool lastStatusPnlNegative;
        private string lastChecklistText = string.Empty;
        private bool lastChecklistReady;
        private DateTime lastSignalDiagnosticsBarTime = Core.Globals.MinDate;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "PineScriptalgoBridgeStrategy";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                StartBehavior = StartBehavior.ImmediatelySubmit;
                EntryHandling = EntryHandling.AllEntries;
                EntriesPerDirection = MaxTradesPerEntry;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 60;
                IsInstantiatedOnEachOptimizationIteration = true;
                BarsRequiredToTrade = 250;

                TPSType = PineTpSType.Trailing;
                SetupType = PineSetupType.OpenClose;
                TimeframeMultiplier = 18;
                UseLookaheadApproximation = true;

                SidewaysFilterType = PineSidewaysFilterType.NoFilter;
                RsiPeriod = 7;
                TopLimitRsi = 45;
                BottomLimitRsi = 10;
                AtrFilterLength = 5;
                AtrMaLength = 5;
                ReplicateAtrMaTypo = true;
                AtrMaUseEmaWhenTypoDisabled = false;

                RenkoUseAtr = true;
                RenkoAtrLength = 3;
                RenkoTraditionalTicks = 1000;
                RenkoFastEma = 2;
                RenkoSlowEma = 10;
                RenkoSourceBars = 500;

                AtrLength = 20;
                ProfitFactor = 2.5;
                AtrQtyTp1 = 50;
                AtrQtyTp2 = 30;
                AtrQtyTp3 = 20;

                EnableTrailingEngine = true;
                TrailingMode = PineTrailingMode.Ticks;
                AtrTrailBehavior = PineAtrTrailBehavior.Intrabar;
                AtrTrailSource = PineAtrTrailSource.Traditional;
                TrailingAtrPeriod = 14;
                TrailingDemaLength = 14;
                AtrUseExternalActivationThreshold = false;
                AtrExternalActivationType = PineExternalActivationType.Ticks;
                AtrTrailActivation = 0;
                AtrTrailStep = 0;
                AtrTrailStop = 1;
                TicksTrailActivation = 0;
                TicksTrailStep = 0;
                TicksTrailStop = 0;
                DollarsTrailActivation = 0;
                DollarsTrailStep = 0;
                DollarsTrailStop = 0;

                EnableEntryStopLoss = false;
                EntryStopLossType = PineEntryStopLossType.Atr;
                StopFactor = 1.0;
                EntryStopAtrPeriod = 14;
                EntryStopDemaLength = 14;
                StructureStopModel = PineStructureStopModel.ChartSwingPivot;
                BosChochEngine = PineBosChochEngine.SimplifiedMql;
                StructurePivotStrength = 3;
                StructureBufferType = PineStructureBufferType.Ticks;
                StructureTicksBuffer = 0;
                StructureAtrBufferMultiple = 0;

                TradesPerEntry = 1;
                TreatMultiEntryAsSingleTrade = false;
                StartHaltedOnEnable = false;
                EnableDailyPnLLimits = false;
                DailyLossLimit = -2000;
                DailyProfitLimit = 840;

                ShowRibbon = true;
                ShowRiskLines = true;
                ShowEventLabels = true;
                ShowStatusPanel = true;
                ShowChecklistPanel = true;
                RiskLineRightBars = 10;
                Debug = false;
                EnableSignalDiagnostics = false;
                EnableTradeStoryLogging = false;
            }
            else if (State == State.Configure)
            {
                EntriesPerDirection = MaxTradesPerEntry;
                AddDataSeries(BarsPeriodType.Tick, 1);

                ResolveSignalSeriesSpecification(out signalBarsType, out signalBarsValue, out signalSeriesUsesPrimary);
                if (!signalSeriesUsesPrimary)
                    AddDataSeries(signalBarsType, signalBarsValue);

                signalSeriesIndex = signalSeriesUsesPrimary ? 0 : 2;

                if (Debug)
                {
                    StrategyLogDebug(string.Format(CultureInfo.InvariantCulture,
                        "PARAMS: TPSType={0} SetupType={1} TFx={2} Lookahead={3} Filter={4} TradesPerEntry={5} TreatMulti={6} StartHalted={7} DailyLimits={8} DLL={9} DPL={10} EntryStop={11}/{12} Trailing={13}/{14}",
                        TPSType,
                        SetupType,
                        TimeframeMultiplier,
                        UseLookaheadApproximation,
                        SidewaysFilterType,
                        TradesPerEntry,
                        TreatMultiEntryAsSingleTrade,
                        StartHaltedOnEnable,
                        EnableDailyPnLLimits,
                        DailyLossLimit,
                        DailyProfitLimit,
                        EnableEntryStopLoss,
                        EntryStopLossType,
                        EnableTrailingEngine,
                        TrailingMode));
                }
            }
            else if (State == State.DataLoaded)
            {
                ResetRuntimeState();
                trackedSessionDate = Core.Globals.MinDate;

                try
                {
                    MultiStratManager.Instance?.TradeSync?.RegisterStrategy(this);
                }
                catch { }
            }
            else if (State == State.Historical)
            {
                TryInitializeChartTraderButtons();
            }
            else if (State == State.Realtime)
            {
                ResetDailyLimitState("realtime_start");
                manualHaltActive = false;
                manualHaltActivatedAt = DateTime.MinValue;
                haltReason = string.Empty;
                ResetRuntimeState();
                StrategyLogInfo("[AUTO] Strategy entered realtime; automation enabled");

                TryInitializeChartTraderButtons();

                if (StartHaltedOnEnable)
                {
                    HandleManualHaltRequest(false);
                    StrategyLogInfo("[MANUAL_HALT] Strategy started halted (config).");
                }
                else
                {
                    BootstrapExistingPositionState();
                    UpdateManualTradeButtons(true);
                }
            }
            else if (State == State.Terminated)
            {
                try
                {
                    MultiStratManager.Instance?.TradeSync?.UnregisterStrategy(this);
                    MultiStratManager.Instance?.ClearManualHaltOverride(Account != null ? Account.Name : string.Empty, "strategy_terminated");
                }
                catch { }

                if (ChartControl != null)
                    ChartControl.Dispatcher.BeginInvoke(new Action(RemoveChartTraderButtons));
                else
                    RemoveChartTraderButtons();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == TickSeriesIndex)
            {
                if (ShouldRunTrailingIntrabar())
                    RunTrailingEngine(true);
                return;
            }

            if (BarsInProgress != 0)
                return;

            if (CurrentBar < BarsRequiredToTrade)
                return;

            UpdateTradeExcursions(High[0], Low[0]);

            if (!IsFirstTickOfBar)
            {
                UpdateStatusOverlay(Time[0]);
                UpdateChecklistOverlay();
                return;
            }

            DateTime signalDate = Time[1].Date;
            MaybeResetDailyLimitForNewDay(signalDate);

            PineEvalState st;
            if (!EvaluateBar(out st) || !st.Valid)
            {
                lastUiEvalState = null;
                if (EnableSignalDiagnostics && State == State.Realtime)
                    StrategyLogInfo(string.Format(CultureInfo.InvariantCulture, "[SIGNAL] time={0:yyyy-MM-dd HH:mm:ss} decision=INVALID_EVAL pos={1} halted={2}", Time[0], FormatPositionSummary(), IsExecutionHalted() ? haltReason : "OFF"));
                UpdateStatusOverlay(Time[0]);
                UpdateChecklistOverlay();
                return;
            }

            lastUiEvalState = st;
            LogSignalDiagnostics(st);
            pineCondition = st.ConditionNow;
            pineEntryLine = st.EntryLine;
            pineSlLine = st.SlLine;
            pineTp1Line = st.Tp1Line;
            pineTp2Line = st.Tp2Line;
            pineTp3Line = st.Tp3Line;
            if (st.LongE || st.ShortE)
                pineEntryStartTime = st.BarTime;

            if (EnableDailyPnLLimits && CheckDailyLimit(st.BarTime.Date))
                EnforceFlatWhenHalted("daily_limit");

            if (!IsExecutionHalted())
            {
                switch (TPSType)
                {
                    case PineTpSType.Trailing:
                        ExecuteTrailingMode(st);
                        break;
                    case PineTpSType.Options:
                        ExecuteOptionsMode(st);
                        break;
                    case PineTpSType.Atr:
                        ExecuteAtrMode(st);
                        break;
                }
            }
            else
            {
                EnforceFlatWhenHalted(haltReason);
            }

            if (ShouldUseMarketStructureStopUpdater())
                UpdateMarketStructureStops();

            if (ShouldRunTrailingBarClose())
                RunTrailingEngine(false);

            UpdateSignalDrawings(st);
            UpdateStatusOverlay(st.BarTime);
            UpdateChecklistOverlay();
            UpdateManualTradeButtons(false);
            UpdateRuntimeInputBoxes(false);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled, double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            base.OnOrderUpdate(order, limitPrice, stopPrice, quantity, filled, averageFillPrice, orderState, time, error, nativeError);

            if (order == null)
                return;

            string name = order.Name ?? "<null>";
            bool looksManual = !string.IsNullOrEmpty(name) &&
                (name.IndexOf("MAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 name.IndexOf("MHLT", StringComparison.OrdinalIgnoreCase) >= 0);
            if (looksManual || Debug)
            {
                StrategyLogInfo(string.Format("[ORDER] name={0} fromEntry={1} action={2} state={3} qty={4} filled={5} avg={6:F2} oco={7} stop={8:F2} limit={9:F2} error={10} native='{11}'",
                    name,
                    order.FromEntrySignal ?? "<null>",
                    order.OrderAction,
                    orderState,
                    order.Quantity,
                    order.Filled,
                    averageFillPrice,
                    order.Oco ?? "<none>",
                    stopPrice,
                    limitPrice,
                    error,
                    string.IsNullOrEmpty(nativeError) ? "<none>" : nativeError));
            }

            if (string.IsNullOrEmpty(order.Name) || workingEntryOrders == null)
                return;

            if (!entrySignalToTradeId.ContainsKey(order.Name))
                return;

            switch (orderState)
            {
                case OrderState.Accepted:
                case OrderState.Working:
                case OrderState.Submitted:
                case OrderState.PartFilled:
                    workingEntryOrders[order.OrderId] = order;
                    break;
                default:
                    workingEntryOrders.Remove(order.OrderId);
                    break;
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            base.OnExecutionUpdate(execution, executionId, price, quantity, marketPosition, orderId, time);

            if (execution == null || execution.Order == null || quantity <= 0)
                return;

            OrderAction action = execution.Order.OrderAction;
            bool isEntryAction = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExitAction = action == OrderAction.Sell || action == OrderAction.BuyToCover;
            if (!isEntryAction && !isExitAction)
                return;

            if (isEntryAction)
            {
                string signal = execution.Order.Name ?? string.Empty;
                string tradeId;
                if (string.IsNullOrEmpty(signal) || !entrySignalToTradeId.TryGetValue(signal, out tradeId))
                    return;

                PineTradeRuntimeState state;
                if (!tradeStates.TryGetValue(tradeId, out state) || state == null)
                    return;

                state.IsSynthetic = false;
                state.EntryPrice = execution.Price;
                state.EntryTimeUtc = time.ToUniversalTime();
                state.RemainingQuantity = Math.Max(state.RemainingQuantity, Math.Abs(quantity));
                state.OriginalQuantity = Math.Max(state.OriginalQuantity, Math.Abs(quantity));
                state.EntrySide = action == OrderAction.SellShort ? MarketPosition.Short : MarketPosition.Long;
                state.InstrumentName = execution.Instrument != null ? execution.Instrument.FullName : Instrument.FullName;
                state.AccountName = execution.Account != null ? execution.Account.Name : (Account != null ? Account.Name : string.Empty);
                double pointValue = execution.Instrument != null && execution.Instrument.MasterInstrument != null ? execution.Instrument.MasterInstrument.PointValue : Instrument.MasterInstrument.PointValue;
                state.NtPointsPer1kLoss = pointValue > 0 ? 1000.0 / pointValue : 0.0;
                if (state.MaxFavorablePrice <= 0.0)
                    state.MaxFavorablePrice = execution.Price;
                if (state.MaxAdversePrice <= 0.0)
                    state.MaxAdversePrice = execution.Price;

                if (Debug || EnableSignalDiagnostics || state.IsManualEntry)
                {
                    StrategyLogInfo(string.Format("[EXEC] ENTRY trade={0} side={1} qty={2} price={3:F2} ctx={4} reason={5}",
                        state.TradeId,
                        state.EntrySide,
                        quantity,
                        execution.Price,
                        string.IsNullOrWhiteSpace(state.EntryContext) ? "AUTO" : state.EntryContext,
                        string.IsNullOrWhiteSpace(state.EntryReason) ? signal : state.EntryReason));
                }

                PublishOpenEvent(state);
                return;
            }

            int remainingToAllocate = Math.Abs(quantity);
            foreach (var state in ResolveExitStates(execution, remainingToAllocate))
            {
                if (state == null || remainingToAllocate <= 0)
                    continue;

                int closeQty = Math.Min(remainingToAllocate, Math.Max(0, state.RemainingQuantity));
                if (closeQty <= 0)
                    continue;

                double exitPrice = execution.Price > 0 ? execution.Price : (execution.Order.AverageFillPrice > 0 ? execution.Order.AverageFillPrice : price);
                double execPnl = ComputeExecutionPnl(state, closeQty, exitPrice);
                string exitReason = ResolveExitReason(execution, state);

                state.RemainingQuantity = Math.Max(0, state.RemainingQuantity - closeQty);
                remainingToAllocate -= closeQty;

                if (Debug || EnableTradeStoryLogging || state.IsManualEntry)
                {
                    StrategyLogInfo(string.Format("[EXEC] EXIT trade={0} side={1} qty={2} exit={3:F2} pnl={4:C2} remaining={5} reason={6}",
                        state.TradeId,
                        state.EntrySide,
                        closeQty,
                        exitPrice,
                        execPnl,
                        state.RemainingQuantity,
                        exitReason));
                }

                LogTradeOutcomeStory(execution, state, closeQty, execPnl, exitReason);

                if (state.RemainingQuantity > 0)
                    PublishPartialEvent(state);
                else
                    PublishClosedEvent(state);
            }

            CleanupClosedTradeState();
        }

        private IEnumerable<PineTradeRuntimeState> ResolveExitStates(Execution execution, int quantity)
        {
            var results = new List<PineTradeRuntimeState>();
            if (execution == null || quantity <= 0 || tradeStates == null || tradeStates.Count == 0)
                return results;

            string fromEntry = execution.Order != null ? execution.Order.FromEntrySignal : string.Empty;
            string tradeId;
            if (!string.IsNullOrEmpty(fromEntry) && entrySignalToTradeId.TryGetValue(fromEntry, out tradeId))
            {
                PineTradeRuntimeState direct;
                if (tradeStates.TryGetValue(tradeId, out direct) && direct != null)
                {
                    results.Add(direct);
                    return results;
                }
            }

            MarketPosition targetSide = execution.Order != null && execution.Order.OrderAction == OrderAction.BuyToCover
                ? MarketPosition.Short
                : MarketPosition.Long;

            foreach (var state in tradeStates.Values
                .Where(x => x != null && x.RemainingQuantity > 0 && x.EntrySide == targetSide)
                .OrderBy(x => x.EntryTimeUtc))
            {
                results.Add(state);
                quantity -= Math.Max(1, state.RemainingQuantity);
                if (quantity <= 0)
                    break;
            }

            return results;
        }

        private void CleanupClosedTradeState()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            foreach (var closedId in tradeStates.Values.Where(x => x != null && x.RemainingQuantity <= 0).Select(x => x.TradeId).ToList())
            {
                PineTradeRuntimeState state;
                if (!tradeStates.TryGetValue(closedId, out state) || state == null)
                    continue;

                if (!string.IsNullOrEmpty(state.EntrySignal))
                    entrySignalToTradeId.Remove(state.EntrySignal);

                openTradeOrder.Remove(closedId);
                tradeStates.Remove(closedId);
            }

            foreach (var groupId in syncGroups.Values.Where(x => x != null && GetSyncGroupRemainingQuantity(x.TradeId) <= 0).Select(x => x.TradeId).ToList())
                syncGroups.Remove(groupId);
        }

        private void PublishOpenEvent(PineTradeRuntimeState state)
        {
            if (state == null || state.OpenPublished)
                return;

            state.OpenPublished = true;
            if (State != State.Realtime)
                return;

            var manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null || !manager.TradeSync.IsReady)
                return;

            if (!string.IsNullOrEmpty(state.SyncTradeId))
            {
                PineTradeSyncGroup group;
                if (syncGroups.TryGetValue(state.SyncTradeId, out group) && group != null)
                {
                    if (group.OpenPublished)
                        return;

                    group.OpenPublished = true;
                    group.LastPublishedRemaining = group.TotalQuantity;
                    manager.TradeSync.PublishOpen(this, group.TradeId, state.InstrumentName, state.EntrySide, group.TotalQuantity, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice, true);
                    return;
                }
            }

            manager.TradeSync.PublishOpen(this, state.TradeId, state.InstrumentName, state.EntrySide, state.OriginalQuantity, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice, false);
            StrategyLogDebug(string.Format("[SYNC] Published OPEN trade={0} qty={1} side={2} price={3:F2}", state.TradeId, state.OriginalQuantity, state.EntrySide, state.EntryPrice));
        }

        private void PublishPartialEvent(PineTradeRuntimeState state)
        {
            if (state == null || State != State.Realtime)
                return;

            var manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null || !manager.TradeSync.IsReady)
                return;

            if (!string.IsNullOrEmpty(state.SyncTradeId))
            {
                PineTradeSyncGroup group;
                if (syncGroups.TryGetValue(state.SyncTradeId, out group) && group != null)
                {
                    int remaining = GetSyncGroupRemainingQuantity(group.TradeId);
                    if (remaining > 0 && remaining != group.LastPublishedRemaining)
                    {
                        group.LastPublishedRemaining = remaining;
                        manager.TradeSync.PublishPartial(this, group.TradeId, remaining);
                    }
                    return;
                }
            }

            manager.TradeSync.PublishPartial(this, state.TradeId, state.RemainingQuantity);
            StrategyLogDebug(string.Format("[SYNC] Published PARTIAL trade={0} remaining={1}", state.TradeId, state.RemainingQuantity));
        }

        private void PublishClosedEvent(PineTradeRuntimeState state)
        {
            if (state == null || State != State.Realtime)
                return;

            var manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null || !manager.TradeSync.IsReady)
                return;

            if (!string.IsNullOrEmpty(state.SyncTradeId))
            {
                PineTradeSyncGroup group;
                if (syncGroups.TryGetValue(state.SyncTradeId, out group) && group != null)
                {
                    int remaining = GetSyncGroupRemainingQuantity(group.TradeId);
                    if (remaining > 0)
                    {
                        PublishPartialEvent(state);
                        return;
                    }

                    if (!group.ClosedPublished)
                    {
                        group.ClosedPublished = true;
                        manager.TradeSync.PublishClosed(this, group.TradeId);
                    }
                    return;
                }
            }

            manager.TradeSync.PublishClosed(this, state.TradeId);
            StrategyLogDebug(string.Format("[SYNC] Published CLOSED trade={0}", state.TradeId));
        }

        private int GetSyncGroupRemainingQuantity(string syncTradeId)
        {
            if (string.IsNullOrEmpty(syncTradeId) || tradeStates == null)
                return 0;

            int remaining = 0;
            foreach (var state in tradeStates.Values)
            {
                if (state == null || !string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                remaining += Math.Max(0, state.RemainingQuantity);
            }
            return remaining;
        }

        void ITradeSyncParticipant.HandleTradeSyncPartial(string tradeId, int quantityToExit)
        {
            TriggerCustomEvent(o =>
            {
                var payload = o as Tuple<string, int>;
                if (payload == null)
                    return;
                HandleTradeSyncPartialCore(payload.Item1, payload.Item2);
            }, Tuple.Create(tradeId, quantityToExit));
        }

        void ITradeSyncParticipant.HandleTradeSyncClose(string tradeId)
        {
            TriggerCustomEvent(o => HandleTradeSyncCloseCore(o as string), tradeId);
        }

        void IRunUpParticipant.HandleRunUpStart(string tradeId, double anchorPrice, RunUpConfig config)
        {
        }

        private void HandleTradeSyncPartialCore(string tradeId, int quantityToExit)
        {
            if (string.IsNullOrEmpty(tradeId) || quantityToExit <= 0)
                return;

            PineTradeSyncGroup group;
            if (syncGroups != null && syncGroups.TryGetValue(tradeId, out group) && group != null)
            {
                SubmitExit(group.Side, quantityToExit, "TSYNC_PART");
                return;
            }

            PineTradeRuntimeState state;
            if (tradeStates != null && tradeStates.TryGetValue(tradeId, out state) && state != null)
                SubmitExit(state.EntrySide, Math.Min(quantityToExit, Math.Max(1, state.RemainingQuantity)), "TSYNC_PART");
        }

        private void HandleTradeSyncCloseCore(string tradeId)
        {
            if (string.IsNullOrEmpty(tradeId))
                return;

            PineTradeSyncGroup group;
            if (syncGroups != null && syncGroups.TryGetValue(tradeId, out group) && group != null)
            {
                SubmitExit(group.Side, GetSyncGroupRemainingQuantity(group.TradeId), "TSYNC_CLOSE");
                return;
            }

            PineTradeRuntimeState state;
            if (tradeStates != null && tradeStates.TryGetValue(tradeId, out state) && state != null)
                SubmitExit(state.EntrySide, state.RemainingQuantity, "TSYNC_CLOSE");
        }

        private bool EvaluateBar(out PineEvalState st)
        {
            st = new PineEvalState();
            if (CurrentBar < 2)
                return false;

            st.Valid = false;
            st.BarTime = Time[1];
            st.BarOpen = Open[1];
            st.BarHigh = High[1];
            st.BarLow = Low[1];
            st.BarClose = Close[1];
            st.TradeDateAllowed = true;

            bool trendType;
            double filterRsi;
            double atrNow;
            double atrMa;
            if (!ComputeTrendType(out trendType, out filterRsi, out atrNow, out atrMa))
                return false;

            bool buySig;
            bool sellSig;
            bool buyColor;
            double ribbonTop;
            double ribbonBottom;
            bool ok = SetupType == PineSetupType.OpenClose
                ? ComputeOpenCloseSignal(st.BarTime, out buySig, out sellSig, out buyColor, out ribbonTop, out ribbonBottom)
                : ComputeRenkoSignal(st.BarTime, out buySig, out sellSig, out buyColor, out ribbonTop, out ribbonBottom);
            if (!ok)
                return false;

            st.TrendAllowed = trendType;
            st.RawBuySignal = buySig;
            st.RawSellSignal = sellSig;
            st.RsiValue = filterRsi;
            st.AtrFilterValue = atrNow;
            st.AtrMaValue = atrMa;
            st.BuyEntry = buySig && trendType;
            st.SellEntry = sellSig && trendType;
            st.LeTrigger = st.BuyEntry;
            st.SeTrigger = st.SellEntry;
            st.BuyColor = buyColor;
            st.RibbonValid = true;
            st.RibbonTop = ribbonTop;
            st.RibbonBottom = ribbonBottom;

            double atrTp;
            if (!TryReadAtrForSeries(0, AtrLength, 1, out atrTp))
                return false;

            double takeProfit1Buy = 1.0 * ProfitFactor * atrTp;
            double takeProfit2Buy = 2.0 * ProfitFactor * atrTp;
            double takeProfit3Buy = 3.0 * ProfitFactor * atrTp;
            double takeProfit1Sell = 1.0 * ProfitFactor * atrTp;
            double takeProfit2Sell = 2.0 * ProfitFactor * atrTp;
            double takeProfit3Sell = 3.0 * ProfitFactor * atrTp;

            double iLxLvlTp1 = st.LeTrigger ? takeProfit1Buy : (st.SeTrigger ? takeProfit1Sell : double.NaN);
            double iLxLvlTp2 = st.LeTrigger ? takeProfit2Buy : (st.SeTrigger ? takeProfit2Sell : double.NaN);
            double iLxLvlTp3 = st.LeTrigger ? takeProfit3Buy : (st.SeTrigger ? takeProfit3Sell : double.NaN);
            double iLxLvlSl = st.LeTrigger ? takeProfit1Buy : (st.SeTrigger ? takeProfit1Sell : double.NaN);

            double prevCondition = pineCondition;
            st.ConditionPrev = prevCondition;
            st.EntryLine = st.LeTrigger && prevCondition <= 0.0 ? st.BarClose : (st.SeTrigger && prevCondition >= 0.0 ? st.BarClose : pineEntryLine);

            double slTop = st.BarClose + iLxLvlSl;
            double slBottom = st.BarClose - iLxLvlSl;
            st.SlLine = prevCondition <= 0.0 && st.LeTrigger ? slBottom : (prevCondition >= 0.0 && st.SeTrigger ? slTop : pineSlLine);

            st.Tp1Line = pineTp1Line;
            if (!PineAlgoMath.EqCond(prevCondition, 1.0) && st.LeTrigger)
                st.Tp1Line = st.BarClose + iLxLvlTp1;
            else if (!PineAlgoMath.EqCond(prevCondition, -1.0) && st.SeTrigger)
                st.Tp1Line = st.BarClose - iLxLvlTp1;

            st.Tp2Line = pineTp2Line;
            if (!PineAlgoMath.EqCond(prevCondition, 1.1) && st.LeTrigger)
                st.Tp2Line = st.BarClose + iLxLvlTp2;
            else if (!PineAlgoMath.EqCond(prevCondition, -1.1) && st.SeTrigger)
                st.Tp2Line = st.BarClose - iLxLvlTp2;

            st.Tp3Line = pineTp3Line;
            if (!PineAlgoMath.EqCond(prevCondition, 1.2) && st.LeTrigger)
                st.Tp3Line = st.BarClose + iLxLvlTp3;
            else if (!PineAlgoMath.EqCond(prevCondition, -1.2) && st.SeTrigger)
                st.Tp3Line = st.BarClose - iLxLvlTp3;

            bool slLong = PineAlgoMath.CrossSeries(st.BarLow, st.SlLine, Low[2], pineSlLine, false);
            bool slShort = PineAlgoMath.CrossSeries(st.BarHigh, st.SlLine, High[2], pineSlLine, true);
            bool tp1Long = PineAlgoMath.CrossSeries(st.BarHigh, st.Tp1Line, High[2], pineTp1Line, true);
            bool tp1Short = PineAlgoMath.CrossSeries(st.BarLow, st.Tp1Line, Low[2], pineTp1Line, false);
            bool tp2Long = PineAlgoMath.CrossSeries(st.BarHigh, st.Tp2Line, High[2], pineTp2Line, true);
            bool tp2Short = PineAlgoMath.CrossSeries(st.BarLow, st.Tp2Line, Low[2], pineTp2Line, false);
            bool tp3Long = PineAlgoMath.CrossSeries(st.BarHigh, st.Tp3Line, High[2], pineTp3Line, true);
            bool tp3Short = PineAlgoMath.CrossSeries(st.BarLow, st.Tp3Line, Low[2], pineTp3Line, false);

            double condition = prevCondition;
            if (st.LeTrigger && prevCondition <= 0.0)
                condition = 1.0;
            else if (st.SeTrigger && prevCondition >= 0.0)
                condition = -1.0;
            else if (tp3Long && PineAlgoMath.EqCond(prevCondition, 1.2))
                condition = 1.3;
            else if (tp3Short && PineAlgoMath.EqCond(prevCondition, -1.2))
                condition = -1.3;
            else if (tp2Long && PineAlgoMath.EqCond(prevCondition, 1.1))
                condition = 1.2;
            else if (tp2Short && PineAlgoMath.EqCond(prevCondition, -1.1))
                condition = -1.2;
            else if (tp1Long && PineAlgoMath.EqCond(prevCondition, 1.0))
                condition = 1.1;
            else if (tp1Short && PineAlgoMath.EqCond(prevCondition, -1.0))
                condition = -1.1;
            else if (slLong && prevCondition >= 1.0)
                condition = 0.0;
            else if (slShort && prevCondition <= -1.0)
                condition = 0.0;

            st.ConditionNow = condition;
            st.LongE = st.LeTrigger && prevCondition <= 0.0 && PineAlgoMath.EqCond(condition, 1.0);
            st.ShortE = st.SeTrigger && prevCondition >= 0.0 && PineAlgoMath.EqCond(condition, -1.0);
            st.LongX = false;
            st.ShortX = false;
            st.LongSL = slLong && prevCondition >= 1.0 && PineAlgoMath.EqCond(condition, 0.0);
            st.ShortSL = slShort && prevCondition <= -1.0 && PineAlgoMath.EqCond(condition, 0.0);
            st.LongTP1 = tp1Long && PineAlgoMath.EqCond(prevCondition, 1.0) && PineAlgoMath.EqCond(condition, 1.1);
            st.ShortTP1 = tp1Short && PineAlgoMath.EqCond(prevCondition, -1.0) && PineAlgoMath.EqCond(condition, -1.1);
            st.LongTP2 = tp2Long && PineAlgoMath.EqCond(prevCondition, 1.1) && PineAlgoMath.EqCond(condition, 1.2);
            st.ShortTP2 = tp2Short && PineAlgoMath.EqCond(prevCondition, -1.1) && PineAlgoMath.EqCond(condition, -1.2);
            st.LongTP3 = tp3Long && PineAlgoMath.EqCond(prevCondition, 1.2) && PineAlgoMath.EqCond(condition, 1.3);
            st.ShortTP3 = tp3Short && PineAlgoMath.EqCond(prevCondition, -1.2) && PineAlgoMath.EqCond(condition, -1.3);
            st.Valid = true;
            return true;
        }

        private void ExecuteTrailingMode(PineEvalState st)
        {

            if (st.BuyEntry)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    SubmitExit(MarketPosition.Short, Position.Quantity, "TRAIL_REV");
                if (Position.MarketPosition != MarketPosition.Long)
                    SubmitEntryBatch(MarketPosition.Long, "LE", st, false);
            }

            if (st.SellEntry)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    SubmitExit(MarketPosition.Long, Position.Quantity, "TRAIL_REV");
                if (Position.MarketPosition != MarketPosition.Short)
                    SubmitEntryBatch(MarketPosition.Short, "SE", st, false);
            }
        }

        private void ExecuteOptionsMode(PineEvalState st)
        {

            if (st.BuyEntry)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    SubmitExit(MarketPosition.Short, Position.Quantity, "OPT_REV");
                if (Position.MarketPosition != MarketPosition.Long)
                    SubmitEntryBatch(MarketPosition.Long, "LE", st, false);
            }

            if (st.SellEntry && Position.MarketPosition == MarketPosition.Long)
                SubmitExit(MarketPosition.Long, Position.Quantity, "OPT_CLOSE");
        }

        private void ExecuteAtrMode(PineEvalState st)
        {

            if (st.LongE)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    SubmitExit(MarketPosition.Short, Position.Quantity, "ATR_REV");
                if (Position.MarketPosition != MarketPosition.Long)
                {
                    SubmitEntryBatch(MarketPosition.Long, "LE", st, false);
                    atrEntryQuantity = GetOpenQuantity(MarketPosition.Long);
                    atrEntrySide = MarketPosition.Long;
                }
            }

            if (st.ShortE)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    SubmitExit(MarketPosition.Long, Position.Quantity, "ATR_REV");
                if (Position.MarketPosition != MarketPosition.Short)
                {
                    SubmitEntryBatch(MarketPosition.Short, "SE", st, false);
                    atrEntryQuantity = GetOpenQuantity(MarketPosition.Short);
                    atrEntrySide = MarketPosition.Short;
                }
            }

            if (Position.MarketPosition == MarketPosition.Long)
            {
                if (st.LongSL)
                    SubmitExit(MarketPosition.Long, Position.Quantity, "ATR_SL");
                else if (st.LongTP1)
                    SubmitExit(MarketPosition.Long, ResolveAtrPartialQuantity(AtrQtyTp1), "ATR_TP1");
                else if (st.LongTP2)
                    SubmitExit(MarketPosition.Long, ResolveAtrPartialQuantity(AtrQtyTp2), "ATR_TP2");
                else if (st.LongTP3)
                    SubmitExit(MarketPosition.Long, ResolveAtrPartialQuantity(AtrQtyTp3), "ATR_TP3");
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                if (st.ShortSL)
                    SubmitExit(MarketPosition.Short, Position.Quantity, "ATR_SL");
                else if (st.ShortTP1)
                    SubmitExit(MarketPosition.Short, ResolveAtrPartialQuantity(AtrQtyTp1), "ATR_TP1");
                else if (st.ShortTP2)
                    SubmitExit(MarketPosition.Short, ResolveAtrPartialQuantity(AtrQtyTp2), "ATR_TP2");
                else if (st.ShortTP3)
                    SubmitExit(MarketPosition.Short, ResolveAtrPartialQuantity(AtrQtyTp3), "ATR_TP3");
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                atrEntryQuantity = 0;
                atrEntrySide = MarketPosition.Flat;
            }
        }

        private int ResolveAtrPartialQuantity(double percent)
        {
            int baseQty = atrEntryQuantity > 0 ? atrEntryQuantity : Math.Abs(Position.Quantity);
            if (baseQty <= 0)
                return 0;

            int qty = (int)Math.Round(baseQty * (percent / 100.0), MidpointRounding.AwayFromZero);
            qty = Math.Max(1, qty);
            return Math.Min(qty, Math.Abs(Position.Quantity));
        }

        private void SubmitEntryBatch(MarketPosition side, string tag, PineEvalState st, bool manual)
        {
            int entries = GetEffectiveTradesPerEntry();
            string syncTradeId = TreatMultiEntryAsSingleTrade && entries > 1 ? CreateTradeId(tag + "_SYNC") : null;
            PineTradeSyncGroup group = null;
            if (!string.IsNullOrEmpty(syncTradeId))
            {
                group = new PineTradeSyncGroup
                {
                    TradeId = syncTradeId,
                    Side = side,
                    TotalQuantity = entries,
                    LastPublishedRemaining = entries,
                    CreatedAtUtc = DateTime.UtcNow
                };
                syncGroups[syncTradeId] = group;
            }

            for (int i = 0; i < entries; i++)
            {
                string tradeId = CreateTradeId(tag);
                string entrySignal = tradeId;
                var state = new PineTradeRuntimeState
                {
                    TradeId = tradeId,
                    SyncTradeId = syncTradeId,
                    EntrySignal = entrySignal,
                    EntrySide = side,
                    OriginalQuantity = 1,
                    RemainingQuantity = 1,
                    InstrumentName = Instrument.FullName,
                    AccountName = Account != null ? Account.Name : string.Empty,
                    EntryTimeUtc = DateTime.UtcNow
                };
                CaptureEntryDiagnostics(state, st, tag, manual);
                tradeStates[tradeId] = state;
                entrySignalToTradeId[entrySignal] = tradeId;
                openTradeOrder.Add(tradeId);
                if (group != null)
                    group.MemberTradeIds.Add(tradeId);

                double entryStop;
                if (ShouldApplyEntryStopLoss() && ComputeEntryStopLossPrice(side == MarketPosition.Long ? 1 : -1, 0.0, out entryStop))
                    SetStopLoss(entrySignal, CalculationMode.Price, entryStop, false);
                else if (TPSType == PineTpSType.Atr && st != null && st.Valid)
                    SetStopLoss(entrySignal, CalculationMode.Price, RoundToTickSize(st.SlLine), false);

                if (Debug || EnableSignalDiagnostics || manual)
                {
                    StrategyLogInfo(string.Format("[ENTRY_SUBMIT] trade={0} side={1} tag={2} manual={3} sync={4} ctx={5} reason={6}",
                        tradeId,
                        side,
                        tag,
                        manual,
                        string.IsNullOrEmpty(syncTradeId) ? "OFF" : syncTradeId,
                        string.IsNullOrWhiteSpace(state.EntryContext) ? "AUTO" : state.EntryContext,
                        string.IsNullOrWhiteSpace(state.EntryReason) ? tag : state.EntryReason));
                }

                if (side == MarketPosition.Long)
                    EnterLong(1, entrySignal);
                else
                    EnterShort(1, entrySignal);
            }

            UpdateManualTradeButtons(false);
        }

        private void SubmitExit(MarketPosition side, int quantity, string signalName)
        {
            if (quantity <= 0)
                return;

            TrackPendingExitReason(side, quantity, signalName);
            if (Debug || EnableTradeStoryLogging)
                StrategyLogInfo(string.Format("[EXIT_SUBMIT] side={0} qty={1} reason={2} pos={3} posQty={4}", side, quantity, signalName, Position.MarketPosition, Position.Quantity));

            if (side == MarketPosition.Long)
                ExitLong(quantity, BuildExitSignalName(signalName), string.Empty);
            else if (side == MarketPosition.Short)
                ExitShort(quantity, BuildExitSignalName(signalName), string.Empty);
        }

        private void CancelWorkingEntryOrders(string reason)
        {
            if (workingEntryOrders == null || workingEntryOrders.Count == 0)
                return;

            foreach (var order in workingEntryOrders.Values.ToList())
            {
                try
                {
                    if (order != null)
                        CancelOrder(order);
                }
                catch (Exception ex)
                {
                    StrategyLogDebug($"[ORDERS] Cancel failed ({reason}): {ex.Message}");
                }
            }
        }

        private void EnforceFlatWhenHalted(string reason)
        {
            CancelWorkingEntryOrders(reason);
            if (Position.MarketPosition == MarketPosition.Long && Position.Quantity > 0)
                ExitLong(Position.Quantity, BuildExitSignalName(reason), string.Empty);
            else if (Position.MarketPosition == MarketPosition.Short && Position.Quantity > 0)
                ExitShort(Position.Quantity, BuildExitSignalName(reason), string.Empty);
        }

        private void RunTrailingEngine(bool intrabarPass)
        {
            if (!EnableTrailingEngine)
                return;
            if (intrabarPass && !ShouldRunTrailingIntrabar())
                return;
            if (!intrabarPass && !ShouldRunTrailingBarClose())
                return;
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            double currentPrice = intrabarPass && BarsInProgress == TickSeriesIndex && CurrentBars.Length > TickSeriesIndex && CurrentBars[TickSeriesIndex] > 0
                ? Closes[TickSeriesIndex][0]
                : Close[0];
            if (currentPrice <= 0)
                currentPrice = Close[0];

            double atrValue = 0.0;
            if (TrailingMode == PineTrailingMode.Atr && !TryReadTrailingAtrValue(intrabarPass ? 0 : 1, out atrValue))
                return;

            foreach (var state in tradeStates.Values.Where(x => x != null && x.RemainingQuantity > 0).ToList())
            {
                double activationMetric = 0.0;
                double activationThreshold = 0.0;
                double stepUnits = 0.0;
                double stopUnits = 0.0;
                double dist = 0.0;
                double favorableMove = state.EntrySide == MarketPosition.Long ? currentPrice - state.EntryPrice : state.EntryPrice - currentPrice;
                if (favorableMove <= 0)
                    continue;

                switch (TrailingMode)
                {
                    case PineTrailingMode.Ticks:
                        activationMetric = favorableMove / TickSize;
                        activationThreshold = TicksTrailActivation;
                        stepUnits = TicksTrailStep;
                        stopUnits = TicksTrailStop;
                        break;
                    case PineTrailingMode.Dollars:
                        activationMetric = favorableMove * (Instrument.MasterInstrument.PointValue * state.RemainingQuantity);
                        activationThreshold = DollarsTrailActivation;
                        stepUnits = DollarsTrailStep;
                        stopUnits = DollarsTrailStop;
                        break;
                    case PineTrailingMode.Atr:
                        double atrProgress = atrValue > 0 ? favorableMove / atrValue : 0;
                        if (AtrUseExternalActivationThreshold)
                        {
                            activationMetric = AtrExternalActivationType == PineExternalActivationType.Dollars
                                ? favorableMove * (Instrument.MasterInstrument.PointValue * state.RemainingQuantity)
                                : favorableMove / TickSize;
                            activationThreshold = AtrExternalActivationType == PineExternalActivationType.Dollars ? DollarsTrailActivation : TicksTrailActivation;
                        }
                        else
                        {
                            activationMetric = atrProgress;
                            activationThreshold = AtrTrailActivation;
                        }
                        stepUnits = AtrTrailStep;
                        stopUnits = AtrTrailStop;
                        break;
                }

                if (activationMetric < activationThreshold)
                    continue;

                if (TrailingMode == PineTrailingMode.Atr)
                {
                    double atrProgress = atrValue > 0 ? favorableMove / atrValue : 0;
                    double anchor = AtrUseExternalActivationThreshold ? 0.0 : AtrTrailActivation;
                    double steps = stepUnits > 0.0 && atrProgress > anchor ? Math.Floor((atrProgress - anchor) / stepUnits + 1e-9) : 0.0;
                    dist = (stopUnits + steps * stepUnits) * atrValue;
                }
                else if (TrailingMode == PineTrailingMode.Ticks)
                {
                    double steps = stepUnits > 0.0 ? Math.Floor((activationMetric - activationThreshold) / stepUnits + 1e-9) : 0.0;
                    dist = (stopUnits + steps * stepUnits) * TickSize;
                }
                else
                {
                    double steps = stepUnits > 0.0 ? Math.Floor((activationMetric - activationThreshold) / stepUnits + 1e-9) : 0.0;
                    double targetDollars = stopUnits + steps * stepUnits;
                    double pointValue = Instrument.MasterInstrument.PointValue * Math.Max(1, state.RemainingQuantity);
                    if (pointValue <= 0.0)
                        continue;
                    dist = targetDollars / pointValue;
                }

                if (dist <= 0.0)
                    continue;

                double proposedStop = state.EntrySide == MarketPosition.Long ? state.EntryPrice + dist : state.EntryPrice - dist;
                ApplyStopToState(state, proposedStop);
            }
        }

        private void UpdateMarketStructureStops()
        {
            if (!ShouldUseMarketStructureStopUpdater())
                return;

            foreach (var state in tradeStates.Values.Where(x => x != null && x.RemainingQuantity > 0).ToList())
            {
                double stopPrice;
                if (!ComputeEntryStopLossPrice(state.EntrySide == MarketPosition.Long ? 1 : -1, state.EntryPrice, out stopPrice))
                    continue;
                ApplyStopToState(state, stopPrice);
            }
        }

        private void ApplyStopToState(PineTradeRuntimeState state, double proposedStop)
        {
            if (state == null || state.RemainingQuantity <= 0)
                return;

            proposedStop = RoundToTickSize(proposedStop);
            if (proposedStop <= 0.0)
                return;

            bool improve = state.LastStopPrice <= 0.0
                || (state.EntrySide == MarketPosition.Long && proposedStop > state.LastStopPrice + TickSize * 0.5)
                || (state.EntrySide == MarketPosition.Short && proposedStop < state.LastStopPrice - TickSize * 0.5);
            if (!improve)
                return;

            if (state.EntrySide == MarketPosition.Long && proposedStop >= Close[0])
                return;
            if (state.EntrySide == MarketPosition.Short && proposedStop <= Close[0])
                return;

            SetStopLoss(state.EntrySignal, CalculationMode.Price, proposedStop, false);
            state.LastStopPrice = proposedStop;
            StrategyLogDebug(string.Format(CultureInfo.InvariantCulture, "[STOP] trade={0} side={1} stop={2:F2}", state.TradeId, state.EntrySide, proposedStop));
        }

        private bool ComputeTrendType(out bool trendType, out double rsiValue, out double atrNow, out double atrMa)
        {
            trendType = true;
            rsiValue = 0.0;
            atrNow = 0.0;
            atrMa = 0.0;

            if (!TryReadRsiForSeries(0, RsiPeriod, 1, out rsiValue))
                return false;
            rsiValue = PineAlgoMath.Truncate2(rsiValue);

            int count = Math.Max(AtrMaLength + 20, 80);
            var bars = BuildBarsOldestFirst(0, 1, count);
            if (bars.Count <= AtrMaLength)
                return false;

            List<double> atrSeries = ComputeAtrSeriesOldestFirst(bars, AtrFilterLength);
            atrNow = atrSeries[atrSeries.Count - 1];
            if (atrNow <= 0.0)
                return false;

            int windowCount = Math.Min(AtrMaLength, atrSeries.Count);
            var atrWindow = atrSeries.Skip(atrSeries.Count - windowCount).ToList();
            atrMa = ReplicateAtrMaTypo
                ? atrWindow.Average()
                : (AtrMaUseEmaWhenTypoDisabled ? ComputeEmaTailOldestFirst(atrWindow, AtrMaLength) : atrWindow.Average());

            bool cndSidwayss1 = atrNow >= atrMa;
            bool cndSidwayss2 = rsiValue > TopLimitRsi || rsiValue < BottomLimitRsi;
            bool cndSidways = cndSidwayss1 || cndSidwayss2;
            bool cndSidways1 = cndSidwayss1 && cndSidwayss2;
            bool sideways1 = atrNow <= atrMa;
            bool sideways2 = rsiValue < TopLimitRsi && rsiValue > BottomLimitRsi;
            bool sideways = sideways1 || sideways2;
            bool sidewaysAnd = sideways1 && sideways2;

            switch (SidewaysFilterType)
            {
                case PineSidewaysFilterType.Atr:
                    trendType = cndSidwayss1;
                    break;
                case PineSidewaysFilterType.Rsi:
                    trendType = cndSidwayss2;
                    break;
                case PineSidewaysFilterType.AtrOrRsi:
                    trendType = cndSidways;
                    break;
                case PineSidewaysFilterType.AtrAndRsi:
                    trendType = cndSidways1;
                    break;
                case PineSidewaysFilterType.NoFilter:
                    trendType = rsiValue > 0.0;
                    break;
                case PineSidewaysFilterType.SidewaysAtrOrRsi:
                    trendType = sideways;
                    break;
                case PineSidewaysFilterType.SidewaysAtrAndRsi:
                    trendType = sidewaysAnd;
                    break;
            }

            return true;
        }

        private bool ComputeOpenCloseSignal(DateTime signalBarTime, out bool buyOc, out bool sellOc, out bool buyColor, out double lineTop, out double lineBottom)
        {
            buyOc = false;
            sellOc = false;
            buyColor = false;
            lineTop = 0.0;
            lineBottom = 0.0;

            int curBarsAgo;
            int prevBarsAgo;
            if (!ResolveSignalShifts(signalBarTime, out curBarsAgo, out prevBarsAgo))
                return false;

            var bars = BuildBarsOldestFirst(signalSeriesIndex, curBarsAgo, Math.Min(CurrentBars[signalSeriesIndex] - curBarsAgo + 1, 2000));
            if (bars.Count < 2)
                return false;

            var haOpen = new List<double>(bars.Count);
            var haClose = new List<double>(bars.Count);
            for (int i = 0; i < bars.Count; i++)
            {
                PinePriceBar bar = bars[i];
                double close = (bar.Open + bar.High + bar.Low + bar.Close) / 4.0;
                double open = i == 0 ? (bar.Open + bar.Close) / 2.0 : (haOpen[i - 1] + haClose[i - 1]) / 2.0;
                haOpen.Add(open);
                haClose.Add(close);
            }

            double openCur = haOpen[haOpen.Count - 1];
            double openPrev = haOpen[haOpen.Count - 2];
            double closeCur = haClose[haClose.Count - 1];
            double closePrev = haClose[haClose.Count - 2];

            buyOc = closeCur > openCur && closePrev <= openPrev;
            sellOc = closeCur < openCur && closePrev >= openPrev;
            buyColor = closeCur > openCur;
            lineTop = closeCur;
            lineBottom = openCur;
            return true;
        }

        private bool ComputeRenkoSignal(DateTime signalBarTime, out bool buyR, out bool sellR, out bool buyColor, out double lineTop, out double lineBottom)
        {
            buyR = false;
            sellR = false;
            buyColor = false;
            lineTop = 0.0;
            lineBottom = 0.0;

            int curBarsAgo;
            int prevBarsAgo;
            if (!ResolveSignalShifts(signalBarTime, out curBarsAgo, out prevBarsAgo))
                return false;

            int barsNeeded = Math.Max(120, RenkoSourceBars);
            var bars = BuildBarsOldestFirst(signalSeriesIndex, curBarsAgo, barsNeeded);
            if (bars.Count < 30)
                return false;

            List<double> atrSeries = RenkoUseAtr ? ComputeAtrSeriesOldestFirst(bars, RenkoAtrLength) : null;
            var renkoOpen = new List<double>(bars.Count);
            var renkoClose = new List<double>(bars.Count);
            double rc = bars[0].Close;
            double ro = rc;

            for (int i = 0; i < bars.Count; i++)
            {
                double brick = RenkoUseAtr ? atrSeries[i] : RenkoTraditionalTicks * TickSize;
                if (brick <= TickSize)
                    brick = TickSize;

                double price = bars[i].Close;
                int guard = 0;
                while (price >= rc + brick && guard < 200)
                {
                    ro = rc;
                    rc += brick;
                    guard++;
                }
                while (price <= rc - brick && guard < 400)
                {
                    ro = rc;
                    rc -= brick;
                    guard++;
                }
                renkoOpen.Add(ro);
                renkoClose.Add(rc);
            }

            var emaFast = ComputeEmaSeriesOldestFirst(renkoClose, RenkoFastEma);
            var emaSlow = ComputeEmaSeriesOldestFirst(renkoClose, RenkoSlowEma);
            if (emaFast.Count < 2 || emaSlow.Count < 2)
                return false;

            int last = emaFast.Count - 1;
            int prev = last - 1;
            buyR = emaFast[last] > emaSlow[last] && emaFast[prev] <= emaSlow[prev];
            sellR = emaFast[last] < emaSlow[last] && emaFast[prev] >= emaSlow[prev];
            buyColor = renkoClose[last] > renkoOpen[last];
            lineTop = renkoClose[last];
            lineBottom = renkoOpen[last];
            return true;
        }

        private bool ResolveSignalShifts(DateTime signalBarTime, out int curBarsAgo, out int prevBarsAgo)
        {
            curBarsAgo = -1;
            prevBarsAgo = -1;
            int baseBarsAgo = FindBarsAgoContainingTime(signalSeriesIndex, signalBarTime);
            if (baseBarsAgo < 0)
                return false;

            curBarsAgo = baseBarsAgo + (UseLookaheadApproximation ? 0 : 1);
            prevBarsAgo = curBarsAgo + 1;
            return CurrentBars[signalSeriesIndex] >= prevBarsAgo;
        }

        private int FindBarsAgoContainingTime(int seriesIndex, DateTime targetTime)
        {
            int maxBarsAgo = Math.Min(CurrentBars[seriesIndex], 5000);
            for (int barsAgo = 0; barsAgo <= maxBarsAgo; barsAgo++)
            {
                if (Times[seriesIndex][barsAgo] <= targetTime)
                    return barsAgo;
            }
            return -1;
        }

        private List<PinePriceBar> BuildBarsOldestFirst(int seriesIndex, int startBarsAgo, int count)
        {
            var bars = new List<PinePriceBar>();
            if (count <= 0 || CurrentBars[seriesIndex] < startBarsAgo)
                return bars;

            int maxBarsAgo = Math.Min(CurrentBars[seriesIndex], startBarsAgo + count - 1);
            for (int barsAgo = maxBarsAgo; barsAgo >= startBarsAgo; barsAgo--)
            {
                bars.Add(new PinePriceBar
                {
                    Time = Times[seriesIndex][barsAgo],
                    Open = Opens[seriesIndex][barsAgo],
                    High = Highs[seriesIndex][barsAgo],
                    Low = Lows[seriesIndex][barsAgo],
                    Close = Closes[seriesIndex][barsAgo],
                    Volume = Volumes[seriesIndex][barsAgo]
                });
            }
            return bars;
        }

        private List<PinePriceBar> BuildBarsNewestFirst(int seriesIndex, int count)
        {
            var bars = new List<PinePriceBar>();
            int maxBarsAgo = Math.Min(CurrentBars[seriesIndex], Math.Max(0, count - 1));
            for (int barsAgo = 0; barsAgo <= maxBarsAgo; barsAgo++)
            {
                bars.Add(new PinePriceBar
                {
                    Time = Times[seriesIndex][barsAgo],
                    Open = Opens[seriesIndex][barsAgo],
                    High = Highs[seriesIndex][barsAgo],
                    Low = Lows[seriesIndex][barsAgo],
                    Close = Closes[seriesIndex][barsAgo],
                    Volume = Volumes[seriesIndex][barsAgo]
                });
            }
            return bars;
        }

        private List<double> ComputeAtrSeriesOldestFirst(IList<PinePriceBar> bars, int length)
        {
            var result = new List<double>();
            if (bars == null || bars.Count == 0)
                return result;

            length = Math.Max(1, length);
            var trValues = new List<double>(bars.Count);
            for (int i = 0; i < bars.Count; i++)
            {
                PinePriceBar bar = bars[i];
                double tr = i == 0 ? bar.High - bar.Low : Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - bars[i - 1].Close), Math.Abs(bar.Low - bars[i - 1].Close)));
                trValues.Add(Math.Max(0.0, tr));
            }

            double runningAtr = 0.0;
            for (int i = 0; i < trValues.Count; i++)
            {
                if (i < length)
                {
                    runningAtr += trValues[i];
                    result.Add(i == length - 1 ? runningAtr / length : 0.0);
                    if (i == length - 1)
                        runningAtr /= length;
                    continue;
                }

                runningAtr = ((runningAtr * (length - 1)) + trValues[i]) / length;
                result.Add(runningAtr);
            }

            return result;
        }

        private List<double> ComputeEmaSeriesOldestFirst(IList<double> values, int length)
        {
            var result = new List<double>();
            if (values == null || values.Count == 0)
                return result;

            length = Math.Max(1, length);
            double alpha = 2.0 / (length + 1.0);
            double ema = values[0];
            result.Add(ema);
            for (int i = 1; i < values.Count; i++)
            {
                ema = alpha * values[i] + (1.0 - alpha) * ema;
                result.Add(ema);
            }
            return result;
        }

        private double ComputeEmaTailOldestFirst(IList<double> values, int length)
        {
            var series = ComputeEmaSeriesOldestFirst(values, length);
            return series.Count == 0 ? 0.0 : series[series.Count - 1];
        }

        private double ComputeDemaTailOldestFirst(IList<double> values, int length)
        {
            var ema1 = ComputeEmaSeriesOldestFirst(values, length);
            var ema2 = ComputeEmaSeriesOldestFirst(ema1, length);
            if (ema1.Count == 0 || ema2.Count == 0)
                return 0.0;
            return (2.0 * ema1[ema1.Count - 1]) - ema2[ema2.Count - 1];
        }

        private bool TryReadAtrForSeries(int seriesIndex, int period, int shift, out double atrValue)
        {
            atrValue = 0.0;
            var bars = BuildBarsOldestFirst(seriesIndex, shift, Math.Max(200, period * 8 + shift + 5));
            if (bars.Count <= period)
                return false;
            var atrSeries = ComputeAtrSeriesOldestFirst(bars, period);
            atrValue = atrSeries[atrSeries.Count - 1];
            return atrValue > 0.0;
        }

        private bool TryReadTrailingAtrValue(int shift, out double atrValue)
        {
            atrValue = 0.0;
            if (AtrTrailSource == PineAtrTrailSource.Traditional)
                return TryReadAtrForSeries(0, TrailingAtrPeriod, shift, out atrValue);

            var bars = BuildBarsOldestFirst(0, shift, Math.Max(200, TrailingAtrPeriod * 8 + shift + 5));
            if (bars.Count <= TrailingAtrPeriod)
                return false;
            var atrSeries = ComputeAtrSeriesOldestFirst(bars, TrailingAtrPeriod);
            atrValue = ComputeDemaTailOldestFirst(atrSeries, TrailingDemaLength);
            return atrValue > 0.0;
        }

        private bool TryReadDemaAtrForSeries(int seriesIndex, int atrPeriod, int demaLength, int shift, out double atrValue)
        {
            atrValue = 0.0;
            var bars = BuildBarsOldestFirst(seriesIndex, shift, Math.Max(200, atrPeriod * 8 + shift + 5));
            if (bars.Count <= atrPeriod)
                return false;
            var atrSeries = ComputeAtrSeriesOldestFirst(bars, atrPeriod);
            atrValue = ComputeDemaTailOldestFirst(atrSeries, demaLength);
            return atrValue > 0.0;
        }

        private bool TryReadRsiForSeries(int seriesIndex, int period, int shift, out double rsi)
        {
            rsi = 0.0;
            period = Math.Max(1, period);
            var bars = BuildBarsOldestFirst(seriesIndex, shift, Math.Max(200, period * 8 + shift + 5));
            if (bars.Count <= period)
                return false;

            double avgGain = 0.0;
            double avgLoss = 0.0;
            for (int i = 1; i <= period; i++)
            {
                double delta = bars[i].Close - bars[i - 1].Close;
                avgGain += Math.Max(delta, 0.0);
                avgLoss += Math.Max(-delta, 0.0);
            }
            avgGain /= period;
            avgLoss /= period;

            for (int i = period + 1; i < bars.Count; i++)
            {
                double delta = bars[i].Close - bars[i - 1].Close;
                double gain = Math.Max(delta, 0.0);
                double loss = Math.Max(-delta, 0.0);
                avgGain = ((avgGain * (period - 1)) + gain) / period;
                avgLoss = ((avgLoss * (period - 1)) + loss) / period;
            }

            if (avgLoss <= 0.0)
            {
                rsi = 100.0;
                return true;
            }

            double rs = avgGain / avgLoss;
            rsi = 100.0 - (100.0 / (1.0 + rs));
            return true;
        }

        private bool ShouldRunTrailingIntrabar()
        {
            if (!EnableTrailingEngine)
                return false;
            if (TrailingMode == PineTrailingMode.Atr)
                return AtrTrailBehavior == PineAtrTrailBehavior.Intrabar;
            return true;
        }

        private bool ShouldRunTrailingBarClose()
        {
            return EnableTrailingEngine && TrailingMode == PineTrailingMode.Atr && AtrTrailBehavior == PineAtrTrailBehavior.BarClose;
        }

        private bool ShouldApplyEntryStopLoss()
        {
            return EnableEntryStopLoss && (TPSType == PineTpSType.Trailing || TPSType == PineTpSType.Options);
        }

        private bool ShouldUseMarketStructureStopUpdater()
        {
            return ShouldApplyEntryStopLoss() && EntryStopLossType == PineEntryStopLossType.MarketStructure;
        }

        private bool ComputeEntryStopLossPrice(int direction, double referenceOpenPrice, out double stopLoss)
        {
            stopLoss = 0.0;
            if (!ShouldApplyEntryStopLoss())
                return false;

            double refPrice = referenceOpenPrice > 0.0 ? referenceOpenPrice : Close[0];
            switch (EntryStopLossType)
            {
                case PineEntryStopLossType.Atr:
                    double atrValue;
                    if (!TryReadAtrForSeries(0, EntryStopAtrPeriod, 1, out atrValue))
                        return false;
                    stopLoss = direction > 0 ? refPrice - (Math.Max(0.0, StopFactor) * atrValue) : refPrice + (Math.Max(0.0, StopFactor) * atrValue);
                    break;
                case PineEntryStopLossType.DemaAtr:
                    double demaAtr;
                    if (!TryReadDemaAtrForSeries(0, EntryStopAtrPeriod, EntryStopDemaLength, 1, out demaAtr))
                        return false;
                    stopLoss = direction > 0 ? refPrice - (Math.Max(0.0, StopFactor) * demaAtr) : refPrice + (Math.Max(0.0, StopFactor) * demaAtr);
                    break;
                case PineEntryStopLossType.MarketStructure:
                    double anchorPrice;
                    if (!ComputeStructureStopAnchor(direction, out anchorPrice))
                        return false;
                    double bufferDistance = StructureBufferType == PineStructureBufferType.Ticks
                        ? Math.Max(0.0, StructureTicksBuffer) * TickSize
                        : ResolveStructureAtrBufferDistance();
                    if (bufferDistance < 0.0)
                        bufferDistance = 0.0;
                    stopLoss = direction > 0 ? anchorPrice - bufferDistance : anchorPrice + bufferDistance;
                    break;
            }

            stopLoss = RoundToTickSize(stopLoss);
            return stopLoss > 0.0;
        }

        private double ResolveStructureAtrBufferDistance()
        {
            double atrValue;
            return TryReadAtrForSeries(EntryStopLossType == PineEntryStopLossType.MarketStructure && StructureStopModel == PineStructureStopModel.SignalTimeframeSwing ? signalSeriesIndex : 0, EntryStopAtrPeriod, 1, out atrValue)
                ? Math.Max(0.0, StructureAtrBufferMultiple) * atrValue
                : 0.0;
        }

        private bool ComputeStructureStopAnchor(int direction, out double anchorPrice)
        {
            anchorPrice = 0.0;
            bool wantHigh = direction < 0;
            int seriesIndex = StructureStopModel == PineStructureStopModel.SignalTimeframeSwing ? signalSeriesIndex : 0;
            var bars = BuildBarsNewestFirst(seriesIndex, Math.Max(200, StructurePivotStrength * 50));
            if (bars.Count <= StructurePivotStrength * 2 + 10)
                return false;

            switch (StructureStopModel)
            {
                case PineStructureStopModel.SignalTimeframeSwing:
                case PineStructureStopModel.ChartSwingPivot:
                    int pivotShift;
                    return FindLatestConfirmedPivot(bars, StructurePivotStrength, wantHigh, out pivotShift, out anchorPrice);
                case PineStructureStopModel.BosChoch:
                    bool ok = direction > 0
                        ? FindBullishBosChochAnchor(bars, StructurePivotStrength, BosChochEngine == PineBosChochEngine.ClosePineParity, out anchorPrice)
                        : FindBearishBosChochAnchor(bars, StructurePivotStrength, BosChochEngine == PineBosChochEngine.ClosePineParity, out anchorPrice);
                    if (ok)
                        return true;
                    return FindLatestConfirmedPivot(bars, StructurePivotStrength, wantHigh, out pivotShift, out anchorPrice);
                default:
                    return false;
            }
        }

        private static bool IsPivotHighAt(IList<PinePriceBar> bars, int idx, int strength)
        {
            int total = bars.Count;
            if (idx < strength + 1 || idx + strength >= total)
                return false;
            double candidate = bars[idx].High;
            for (int k = 1; k <= strength; k++)
            {
                if (candidate < bars[idx - k].High)
                    return false;
                if (candidate <= bars[idx + k].High)
                    return false;
            }
            return true;
        }

        private static bool IsPivotLowAt(IList<PinePriceBar> bars, int idx, int strength)
        {
            int total = bars.Count;
            if (idx < strength + 1 || idx + strength >= total)
                return false;
            double candidate = bars[idx].Low;
            for (int k = 1; k <= strength; k++)
            {
                if (candidate > bars[idx - k].Low)
                    return false;
                if (candidate >= bars[idx + k].Low)
                    return false;
            }
            return true;
        }

        private static bool FindLatestConfirmedPivot(IList<PinePriceBar> bars, int strength, bool wantHigh, out int pivotShift, out double pivotPrice)
        {
            pivotShift = -1;
            pivotPrice = 0.0;
            int total = bars.Count;
            int maxShift = total - strength - 1;
            for (int i = strength + 1; i <= maxShift; i++)
            {
                bool isPivot = wantHigh ? IsPivotHighAt(bars, i, strength) : IsPivotLowAt(bars, i, strength);
                if (!isPivot)
                    continue;
                pivotShift = i;
                pivotPrice = wantHigh ? bars[i].High : bars[i].Low;
                return true;
            }
            return false;
        }

        private static bool FindConfirmedPivotInRange(IList<PinePriceBar> bars, int strength, bool wantHigh, int minShift, int maxShift, out int pivotShift, out double pivotPrice)
        {
            pivotShift = -1;
            pivotPrice = 0.0;
            int total = bars.Count;
            int start = Math.Max(strength + 1, minShift);
            int finish = Math.Min(maxShift, total - strength - 1);
            if (start > finish)
                return false;
            for (int i = start; i <= finish; i++)
            {
                bool isPivot = wantHigh ? IsPivotHighAt(bars, i, strength) : IsPivotLowAt(bars, i, strength);
                if (!isPivot)
                    continue;
                pivotShift = i;
                pivotPrice = wantHigh ? bars[i].High : bars[i].Low;
                return true;
            }
            return false;
        }

        private static bool FindExtremePriceInRange(IList<PinePriceBar> bars, int fromShift, int toShift, bool wantHigh, out double extremePrice)
        {
            extremePrice = 0.0;
            int start = Math.Max(1, Math.Min(fromShift, toShift));
            int finish = Math.Min(bars.Count - 1, Math.Max(fromShift, toShift));
            if (start > finish)
                return false;
            extremePrice = wantHigh ? bars[start].High : bars[start].Low;
            for (int i = start + 1; i <= finish; i++)
            {
                double candidate = wantHigh ? bars[i].High : bars[i].Low;
                extremePrice = wantHigh ? Math.Max(extremePrice, candidate) : Math.Min(extremePrice, candidate);
            }
            return true;
        }

        private static int FindCloseBreakAbove(IList<PinePriceBar> bars, int pivotShift, double level)
        {
            for (int i = pivotShift - 1; i >= 1; i--)
            {
                if (bars[i].Close > level)
                    return i;
            }
            return -1;
        }

        private static int FindCloseBreakBelow(IList<PinePriceBar> bars, int pivotShift, double level)
        {
            for (int i = pivotShift - 1; i >= 1; i--)
            {
                if (bars[i].Close < level)
                    return i;
            }
            return -1;
        }

        private static bool FindBullishBosChochAnchor(IList<PinePriceBar> bars, int strength, bool parityMode, out double anchorPrice)
        {
            anchorPrice = 0.0;
            int total = bars.Count;
            int maxShift = total - strength - 1;
            for (int pivotShift = strength + 1; pivotShift <= maxShift; pivotShift++)
            {
                if (!IsPivotHighAt(bars, pivotShift, strength))
                    continue;
                double pivotPrice = bars[pivotShift].High;
                int breakShift = FindCloseBreakAbove(bars, pivotShift, pivotPrice);
                if (breakShift < 1)
                    continue;
                int anchorShift;
                if (!parityMode && FindConfirmedPivotInRange(bars, strength, false, breakShift + 1, pivotShift - 1, out anchorShift, out anchorPrice))
                    return true;
                if (FindExtremePriceInRange(bars, breakShift, pivotShift, false, out anchorPrice))
                    return true;
            }
            return false;
        }

        private static bool FindBearishBosChochAnchor(IList<PinePriceBar> bars, int strength, bool parityMode, out double anchorPrice)
        {
            anchorPrice = 0.0;
            int total = bars.Count;
            int maxShift = total - strength - 1;
            for (int pivotShift = strength + 1; pivotShift <= maxShift; pivotShift++)
            {
                if (!IsPivotLowAt(bars, pivotShift, strength))
                    continue;
                double pivotPrice = bars[pivotShift].Low;
                int breakShift = FindCloseBreakBelow(bars, pivotShift, pivotPrice);
                if (breakShift < 1)
                    continue;
                int anchorShift;
                if (!parityMode && FindConfirmedPivotInRange(bars, strength, true, breakShift + 1, pivotShift - 1, out anchorShift, out anchorPrice))
                    return true;
                if (FindExtremePriceInRange(bars, breakShift, pivotShift, true, out anchorPrice))
                    return true;
            }
            return false;
        }

        private bool CheckDailyLimit(DateTime sessionDate)
        {
            double totalPnl;
            if (!TryGetStrategyDailyTotalPnl(sessionDate, out totalPnl))
                return false;

            double lossLimit = GetEffectiveDailyLossLimit();
            double profitLimit = GetEffectiveDailyProfitLimit();
            bool hasLoss = Math.Abs(lossLimit) > 1e-9;
            bool hasProfit = Math.Abs(profitLimit) > 1e-9;
            if (!hasLoss && !hasProfit)
                return false;

            if (hasLoss && totalPnl <= lossLimit + 1e-9)
            {
                TriggerDailyLimit("DLL", totalPnl);
                return true;
            }

            if (hasProfit && totalPnl >= profitLimit - 1e-9)
            {
                if (dailyProfitCandidateAt == DateTime.MinValue)
                {
                    dailyProfitCandidateAt = DateTime.UtcNow;
                    dailyProfitCandidatePnl = totalPnl;
                    return false;
                }
                if (totalPnl > dailyProfitCandidatePnl)
                    dailyProfitCandidatePnl = totalPnl;
                if ((DateTime.UtcNow - dailyProfitCandidateAt).TotalSeconds < DailyProfitConfirmSeconds)
                    return false;

                TriggerDailyLimit("DPL", totalPnl);
                return true;
            }

            dailyProfitCandidateAt = DateTime.MinValue;
            dailyProfitCandidatePnl = 0.0;
            return false;
        }

        private void TriggerDailyLimit(string limitType, double totalPnl)
        {
            dailyLimitHalted = true;
            dailyLimitType = limitType ?? string.Empty;
            dailyLimitTriggeredPnl = totalPnl;
            dailyLimitTriggeredAt = DateTime.UtcNow;
            haltReason = "daily_limit_" + dailyLimitType;

            if (State == State.Realtime)
            {
                try
                {
                    MultiStratManager.Instance?.ActivateDailyLimitOverride(Account != null ? Account.Name : string.Empty, dailyLimitType, totalPnl, Name);
                }
                catch { }
            }
        }

        private void ResetDailyLimitState(string reason)
        {
            dailyLimitHalted = false;
            dailyLimitType = string.Empty;
            dailyLimitTriggeredPnl = 0.0;
            dailyLimitTriggeredAt = DateTime.MinValue;
            dailyProfitCandidateAt = DateTime.MinValue;
            dailyProfitCandidatePnl = 0.0;
            if (!manualHaltActive)
                haltReason = string.Empty;

            if (State == State.Realtime)
            {
                try
                {
                    MultiStratManager.Instance?.ClearDailyLimitOverrideForAccount(Account != null ? Account.Name : string.Empty, reason);
                }
                catch { }
            }
        }

        private void MaybeResetDailyLimitForNewDay(DateTime sessionDate)
        {
            if (trackedSessionDate == Core.Globals.MinDate)
            {
                trackedSessionDate = sessionDate.Date;
                return;
            }

            if (trackedSessionDate.Date == sessionDate.Date)
                return;

            trackedSessionDate = sessionDate.Date;
            ResetDailyLimitState("new_day");
        }

        private bool TryGetStrategyDailyTotalPnl(DateTime sessionDate, out double totalPnl)
        {
            totalPnl = 0.0;
            DateTime targetDate = sessionDate.Date;
            foreach (Trade trade in SystemPerformance.AllTrades)
            {
                if (trade == null || trade.Exit == null)
                    continue;
                if (trade.Exit.Time.Date != targetDate)
                    continue;
                totalPnl += trade.ProfitCurrency;
            }

            if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                totalPnl += Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]);

            return true;
        }

        private bool IsExecutionHalted()
        {
            return manualHaltActive || dailyLimitHalted;
        }

        private int GetOpenQuantity(MarketPosition side)
        {
            if (tradeStates == null)
                return 0;
            return tradeStates.Values.Where(x => x != null && x.EntrySide == side).Sum(x => Math.Max(0, x.RemainingQuantity));
        }

        private int GetEffectiveTradesPerEntry()
        {
            int effective = tradesPerEntryOverride > 0 ? tradesPerEntryOverride : TradesPerEntry;
            effective = Math.Max(1, effective);
            return Math.Min(MaxTradesPerEntry, effective);
        }

        private double GetEffectiveDailyLossLimit()
        {
            double value = runtimeDailyLossLimit ?? DailyLossLimit;
            return -Math.Abs(value);
        }

        private double GetEffectiveDailyProfitLimit()
        {
            double value = runtimeDailyProfitLimit ?? DailyProfitLimit;
            return Math.Abs(value);
        }

        private void ResolveSignalSeriesSpecification(out BarsPeriodType barsType, out int barsValue, out bool usePrimary)
        {
            usePrimary = false;
            int baseMinutes = GetPrimaryFrameMinutes();
            int targetMinutes = Math.Max(1, baseMinutes * Math.Max(1, TimeframeMultiplier));

            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute && BarsPeriod.Value == targetMinutes)
            {
                barsType = BarsPeriod.BarsPeriodType;
                barsValue = BarsPeriod.Value;
                usePrimary = true;
                return;
            }

            if (targetMinutes < 1440)
            {
                barsType = BarsPeriodType.Minute;
                barsValue = targetMinutes;
            }
            else if (targetMinutes < 10080)
            {
                barsType = BarsPeriodType.Day;
                barsValue = Math.Max(1, targetMinutes / 1440);
            }
            else if (targetMinutes < 43200)
            {
                barsType = BarsPeriodType.Week;
                barsValue = Math.Max(1, targetMinutes / 10080);
            }
            else
            {
                barsType = BarsPeriodType.Month;
                barsValue = Math.Max(1, targetMinutes / 43200);
            }
        }

        private int GetPrimaryFrameMinutes()
        {
            switch (BarsPeriod.BarsPeriodType)
            {
                case BarsPeriodType.Second:
                    return Math.Max(1, (int)Math.Round(BarsPeriod.Value / 60.0));
                case BarsPeriodType.Minute:
                    return Math.Max(1, BarsPeriod.Value);
                case BarsPeriodType.Day:
                    return Math.Max(1, BarsPeriod.Value * 1440);
                case BarsPeriodType.Week:
                    return Math.Max(1, BarsPeriod.Value * 10080);
                case BarsPeriodType.Month:
                    return Math.Max(1, BarsPeriod.Value * 43200);
                default:
                    return 1;
            }
        }

        private double RoundToTickSize(double price)
        {
            return Instrument != null && Instrument.MasterInstrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(price)
                : price;
        }

        private string CreateTradeId(string prefix)
        {
            string token = string.IsNullOrWhiteSpace(prefix) ? "SIG" : prefix.Trim().ToUpperInvariant();
            if (token.Length > 12)
                token = token.Substring(0, 12);

            return string.Format(CultureInfo.InvariantCulture,
                "P_{0}_{1}_{2}",
                token,
                DateTime.UtcNow.ToString("HHmmssfff", CultureInfo.InvariantCulture),
                Interlocked.Increment(ref tradeSequence));
        }

        private string BuildExitSignalName(string reason)
        {
            string token = string.IsNullOrWhiteSpace(reason) ? "EXIT" : reason.Trim().ToUpperInvariant();
            if (token.Length > 12)
                token = token.Substring(0, 12);

            return string.Format(CultureInfo.InvariantCulture, "PX_{0}_{1}", token, Interlocked.Increment(ref tradeSequence));
        }

        private string ResolvePrimaryTradeRef()
        {
            if (openTradeOrder != null)
            {
                foreach (string tradeId in openTradeOrder)
                {
                    if (!string.IsNullOrWhiteSpace(tradeId))
                        return tradeId;
                }
            }

            if (tradeStates != null)
            {
                PineTradeRuntimeState state = tradeStates.Values.FirstOrDefault(x => x != null && x.RemainingQuantity > 0 && !string.IsNullOrWhiteSpace(x.TradeId));
                if (state != null)
                    return state.TradeId;
            }

            return string.Empty;
        }

        private void StrategyLogInfo(string message)
        {
            Print("[" + Name + "] " + message);
            var manager = MultiStratManager.Instance;
            if (manager != null)
            {
                string tradeRef = ResolvePrimaryTradeRef();
                manager.LogInfo("STRATEGY", message, tradeRef, tradeRef);
            }
        }

        private void StrategyLogDebug(string message)
        {
            if (!Debug)
                return;

            Print("[" + Name + "][DEBUG] " + message);
            var manager = MultiStratManager.Instance;
            if (manager != null)
            {
                string tradeRef = ResolvePrimaryTradeRef();
                manager.LogDebug("STRATEGY", message, tradeRef, tradeRef);
            }
        }

        private void CaptureEntryDiagnostics(PineTradeRuntimeState state, PineEvalState st, string tag, bool manual)
        {
            if (state == null)
                return;

            state.IsManualEntry = manual;
            state.EntryContext = manual
                ? "MANUAL"
                : string.Format(CultureInfo.InvariantCulture, "AUTO:{0}/{1}/{2}", TPSType, SetupType, tag);
            state.EntryReason = BuildEntryReason(tag, st, state.EntrySide, manual);
            state.EntrySignalTime = st != null && st.BarTime != DateTime.MinValue
                ? st.BarTime
                : (Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow);

            if (st == null)
                return;

            state.EntryConditionPrev = st.ConditionPrev;
            state.EntryConditionNow = st.ConditionNow;
            state.EntryTrendAllowed = st.TrendAllowed;
            state.EntryRawBuySignal = st.RawBuySignal;
            state.EntryRawSellSignal = st.RawSellSignal;
            state.EntryRsiValue = st.RsiValue;
            state.EntryAtrFilterValue = st.AtrFilterValue;
            state.EntryAtrMaValue = st.AtrMaValue;
            state.EntryLine = st.EntryLine;
            state.EntrySlLine = st.SlLine;
            state.EntryTp1Line = st.Tp1Line;
            state.EntryTp2Line = st.Tp2Line;
            state.EntryTp3Line = st.Tp3Line;
        }

        private string BuildEntryReason(string tag, PineEvalState st, MarketPosition side, bool manual)
        {
            if (manual)
                return string.Format(CultureInfo.InvariantCulture, "Manual {0}", side);

            if (st == null)
                return string.IsNullOrWhiteSpace(tag) ? "signal" : tag;

            return string.Format(CultureInfo.InvariantCulture,
                "{0} {1} rawBuy={2} rawSell={3} trend={4} cond={5}->{6} entry={7:F2} sl={8:F2} tp1={9:F2}",
                TPSType,
                string.IsNullOrWhiteSpace(tag) ? "signal" : tag,
                st.RawBuySignal,
                st.RawSellSignal,
                st.TrendAllowed,
                DescribeCondition(st.ConditionPrev),
                DescribeCondition(st.ConditionNow),
                st.EntryLine,
                st.SlLine,
                st.Tp1Line);
        }

        private void LogSignalDiagnostics(PineEvalState st)
        {
            if (!EnableSignalDiagnostics || State != State.Realtime || st == null || !st.Valid)
                return;

            DateTime diagTime = st.BarTime != DateTime.MinValue ? st.BarTime : (Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow);
            if (diagTime == lastSignalDiagnosticsBarTime)
                return;
            lastSignalDiagnosticsBarTime = diagTime;

            StrategyLogInfo(string.Format(CultureInfo.InvariantCulture,
                "[SIGNAL] time={0:yyyy-MM-dd HH:mm:ss} tp={1} setup={2} tfx={3} filter={4} rawBuy={5} rawSell={6} trend={7} buyEntry={8} sellEntry={9} cond={10}->{11} pos={12} halted={13} decision={14} rsi={15:F2} atr={16:F2} atrMa={17:F2} entry={18:F2} sl={19:F2} tp1={20:F2} tp2={21:F2} tp3={22:F2} flags={23}",
                diagTime,
                TPSType,
                SetupType,
                TimeframeMultiplier,
                SidewaysFilterType,
                st.RawBuySignal,
                st.RawSellSignal,
                st.TrendAllowed,
                st.BuyEntry,
                st.SellEntry,
                DescribeCondition(st.ConditionPrev),
                DescribeCondition(st.ConditionNow),
                FormatPositionSummary(),
                IsExecutionHalted() ? haltReason : "OFF",
                ComputeDiagnosticDecision(st),
                st.RsiValue,
                st.AtrFilterValue,
                st.AtrMaValue,
                st.EntryLine,
                st.SlLine,
                st.Tp1Line,
                st.Tp2Line,
                st.Tp3Line,
                FormatSignalFlags(st)));
        }

        private string ComputeDiagnosticDecision(PineEvalState st)
        {
            if (st == null)
                return "NONE";
            if (IsExecutionHalted())
                return "HALTED:" + haltReason;

            switch (TPSType)
            {
                case PineTpSType.Atr:
                    if (st.LongE)
                        return Position.MarketPosition == MarketPosition.Long ? "SKIP_ALREADY_LONG" : (Position.MarketPosition == MarketPosition.Short ? "REV_TO_LONG" : "ENTER_LONG");
                    if (st.ShortE)
                        return Position.MarketPosition == MarketPosition.Short ? "SKIP_ALREADY_SHORT" : (Position.MarketPosition == MarketPosition.Long ? "REV_TO_SHORT" : "ENTER_SHORT");
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        if (st.LongSL) return "EXIT_LONG_SL";
                        if (st.LongTP1) return "EXIT_LONG_TP1";
                        if (st.LongTP2) return "EXIT_LONG_TP2";
                        if (st.LongTP3) return "EXIT_LONG_TP3";
                    }
                    if (Position.MarketPosition == MarketPosition.Short)
                    {
                        if (st.ShortSL) return "EXIT_SHORT_SL";
                        if (st.ShortTP1) return "EXIT_SHORT_TP1";
                        if (st.ShortTP2) return "EXIT_SHORT_TP2";
                        if (st.ShortTP3) return "EXIT_SHORT_TP3";
                    }
                    break;
                case PineTpSType.Options:
                    if (st.BuyEntry)
                        return Position.MarketPosition == MarketPosition.Long ? "SKIP_ALREADY_LONG" : (Position.MarketPosition == MarketPosition.Short ? "REV_TO_LONG" : "ENTER_LONG");
                    if (st.SellEntry && Position.MarketPosition == MarketPosition.Long)
                        return "EXIT_LONG_OPTIONS";
                    if (st.SellEntry)
                        return "SELL_SIGNAL_NO_ACTION";
                    break;
                default:
                    if (st.BuyEntry)
                        return Position.MarketPosition == MarketPosition.Long ? "SKIP_ALREADY_LONG" : (Position.MarketPosition == MarketPosition.Short ? "REV_TO_LONG" : "ENTER_LONG");
                    if (st.SellEntry)
                        return Position.MarketPosition == MarketPosition.Short ? "SKIP_ALREADY_SHORT" : (Position.MarketPosition == MarketPosition.Long ? "REV_TO_SHORT" : "ENTER_SHORT");
                    break;
            }

            return "NONE";
        }

        private string FormatSignalFlags(PineEvalState st)
        {
            if (st == null)
                return "none";

            var flags = new List<string>();
            if (st.LongE) flags.Add("LongE");
            if (st.ShortE) flags.Add("ShortE");
            if (st.LongSL) flags.Add("LongSL");
            if (st.ShortSL) flags.Add("ShortSL");
            if (st.LongTP1) flags.Add("LongTP1");
            if (st.ShortTP1) flags.Add("ShortTP1");
            if (st.LongTP2) flags.Add("LongTP2");
            if (st.ShortTP2) flags.Add("ShortTP2");
            if (st.LongTP3) flags.Add("LongTP3");
            if (st.ShortTP3) flags.Add("ShortTP3");
            return flags.Count == 0 ? "none" : string.Join(",", flags);
        }

        private void UpdateTradeExcursions(double high, double low)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            double useHigh = high > 0 ? high : Close[0];
            double useLow = low > 0 ? low : Close[0];
            foreach (PineTradeRuntimeState state in tradeStates.Values.Where(x => x != null && x.RemainingQuantity > 0 && x.EntryPrice > 0))
            {
                if (state.MaxFavorablePrice <= 0.0)
                    state.MaxFavorablePrice = state.EntryPrice;
                if (state.MaxAdversePrice <= 0.0)
                    state.MaxAdversePrice = state.EntryPrice;

                if (state.EntrySide == MarketPosition.Long)
                {
                    state.MaxFavorablePrice = Math.Max(state.MaxFavorablePrice, useHigh);
                    state.MaxAdversePrice = Math.Min(state.MaxAdversePrice, useLow);
                }
                else if (state.EntrySide == MarketPosition.Short)
                {
                    state.MaxFavorablePrice = Math.Min(state.MaxFavorablePrice, useLow);
                    state.MaxAdversePrice = Math.Max(state.MaxAdversePrice, useHigh);
                }
            }
        }

        private double ComputeExecutionPnl(PineTradeRuntimeState state, int quantity, double exitPrice)
        {
            if (state == null || quantity <= 0 || state.EntryPrice <= 0.0 || exitPrice <= 0.0)
                return 0.0;

            double pointValue = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.PointValue : 0.0;
            if (pointValue <= 0.0 && state.NtPointsPer1kLoss > 0.0)
                pointValue = 1000.0 / state.NtPointsPer1kLoss;
            if (pointValue <= 0.0)
                return 0.0;

            double points = state.EntrySide == MarketPosition.Long
                ? exitPrice - state.EntryPrice
                : state.EntryPrice - exitPrice;
            return points * pointValue * Math.Max(1, quantity);
        }

        private string ResolveExitReason(Execution execution, PineTradeRuntimeState state)
        {
            if (state != null && !string.IsNullOrWhiteSpace(state.LastExitReason))
                return state.LastExitReason;
            if (execution != null && execution.Order != null && !string.IsNullOrWhiteSpace(execution.Order.Name))
                return execution.Order.Name;
            return "EXIT";
        }

        private void LogTradeOutcomeStory(Execution execution, PineTradeRuntimeState state, int quantity, double execPnl, string exitReason)
        {
            if (!EnableTradeStoryLogging || execution == null || state == null)
                return;

            double exitPrice = execution.Price > 0 ? execution.Price : (execution.Order != null && execution.Order.AverageFillPrice > 0 ? execution.Order.AverageFillPrice : 0.0);
            string outcome = execPnl >= 0.0 ? "PROFIT" : "LOSS";
            double pointValue = Instrument != null && Instrument.MasterInstrument != null ? Instrument.MasterInstrument.PointValue : 0.0;
            if (pointValue <= 0.0 && state.NtPointsPer1kLoss > 0.0)
                pointValue = 1000.0 / state.NtPointsPer1kLoss;

            double favorable = state.MaxFavorablePrice > 0.0 ? state.MaxFavorablePrice : state.EntryPrice;
            double adverse = state.MaxAdversePrice > 0.0 ? state.MaxAdversePrice : state.EntryPrice;
            double mfePoints = 0.0;
            double maePoints = 0.0;
            if (state.EntryPrice > 0.0)
            {
                if (state.EntrySide == MarketPosition.Long)
                {
                    mfePoints = Math.Max(0.0, favorable - state.EntryPrice);
                    maePoints = Math.Max(0.0, state.EntryPrice - adverse);
                }
                else if (state.EntrySide == MarketPosition.Short)
                {
                    mfePoints = Math.Max(0.0, state.EntryPrice - favorable);
                    maePoints = Math.Max(0.0, adverse - state.EntryPrice);
                }
            }

            StrategyLogInfo(string.Format(CultureInfo.InvariantCulture,
                "[TRADE_STORY] trade={0} side={1} qty={2} entry={3:F2} exit={4:F2} pnl={5:C2} {6} reason={7} ctx={8} entryReason={9} rawBuy={10} rawSell={11} trend={12} cond={13}->{14} rsi={15:F2} atr={16:F2} atrMa={17:F2} entryLine={18:F2} sl={19:F2} tp1={20:F2} tp2={21:F2} tp3={22:F2} mfe={23} mae={24} entryTime={25:yyyy-MM-dd HH:mm:ss} exitTime={26:yyyy-MM-dd HH:mm:ss}",
                state.TradeId,
                state.EntrySide,
                Math.Max(1, quantity),
                state.EntryPrice,
                exitPrice,
                execPnl,
                outcome,
                string.IsNullOrWhiteSpace(exitReason) ? "EXIT" : exitReason,
                string.IsNullOrWhiteSpace(state.EntryContext) ? "AUTO" : state.EntryContext,
                string.IsNullOrWhiteSpace(state.EntryReason) ? "n/a" : state.EntryReason,
                state.EntryRawBuySignal,
                state.EntryRawSellSignal,
                state.EntryTrendAllowed,
                DescribeCondition(state.EntryConditionPrev),
                DescribeCondition(state.EntryConditionNow),
                state.EntryRsiValue,
                state.EntryAtrFilterValue,
                state.EntryAtrMaValue,
                state.EntryLine,
                state.EntrySlLine,
                state.EntryTp1Line,
                state.EntryTp2Line,
                state.EntryTp3Line,
                FormatExcursionText(mfePoints, pointValue, quantity),
                FormatExcursionText(maePoints, pointValue, quantity),
                state.EntrySignalTime != DateTime.MinValue ? state.EntrySignalTime : state.EntryTimeUtc.ToLocalTime(),
                execution.Time != DateTime.MinValue ? execution.Time : DateTime.Now));
        }

        private string FormatExcursionText(double points, double pointValue, int quantity)
        {
            if (points <= 0.0)
                return "$0.00 (0.00pt)";

            double cash = pointValue > 0.0 ? points * pointValue * Math.Max(1, quantity) : 0.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:C2} ({1:F2}pt)", cash, points);
        }

        private void TrackPendingExitReason(MarketPosition side, int quantity, string signalName)
        {
            if (tradeStates == null || tradeStates.Count == 0 || quantity <= 0)
                return;

            int remaining = Math.Abs(quantity);
            foreach (PineTradeRuntimeState state in tradeStates.Values.Where(x => x != null && x.RemainingQuantity > 0 && x.EntrySide == side).OrderBy(x => x.EntryTimeUtc))
            {
                if (remaining <= 0)
                    break;

                int applied = Math.Min(remaining, Math.Max(1, state.RemainingQuantity));
                state.LastExitReason = signalName;
                remaining -= applied;
            }
        }

        private void UpdateSignalDrawings(PineEvalState st)
        {
            if (ShowRibbon && st.RibbonValid)
            {
                int leftBarsAgo = Math.Min(CurrentBar, 20);
                Draw.Line(this, RibbonTopTag, false, leftBarsAgo, st.RibbonTop, 0, st.RibbonTop, st.BuyColor ? Brushes.LimeGreen : Brushes.Tomato, DashStyleHelper.Dot, 1);
                Draw.Line(this, RibbonBottomTag, false, leftBarsAgo, st.RibbonBottom, 0, st.RibbonBottom, st.BuyColor ? Brushes.LimeGreen : Brushes.Tomato, DashStyleHelper.Dot, 1);
            }
            else
            {
                RemoveDrawObject(RibbonTopTag);
                RemoveDrawObject(RibbonBottomTag);
            }

            bool riskActive = TPSType == PineTpSType.Atr && ShowRiskLines && Math.Abs(st.ConditionNow) >= 1.0 && !st.LeTrigger && !st.SeTrigger;
            if (!riskActive)
            {
                RemoveDrawObject(EntryLineTag);
                RemoveDrawObject(StopLineTag);
                RemoveDrawObject(Tp1LineTag);
                RemoveDrawObject(Tp2LineTag);
                RemoveDrawObject(Tp3LineTag);
                return;
            }

            int left = Math.Min(CurrentBar, Math.Max(1, RiskLineRightBars));
            Draw.Line(this, EntryLineTag, false, left, st.EntryLine, 0, st.EntryLine, Brushes.DodgerBlue, DashStyleHelper.Solid, 1);
            Draw.Line(this, StopLineTag, false, left, st.SlLine, 0, st.SlLine, Brushes.Red, DashStyleHelper.Solid, 1);
            Draw.Line(this, Tp1LineTag, false, left, st.Tp1Line, 0, st.Tp1Line, Brushes.LimeGreen, DashStyleHelper.Solid, 1);
            Draw.Line(this, Tp2LineTag, false, left, st.Tp2Line, 0, st.Tp2Line, Brushes.LimeGreen, DashStyleHelper.Solid, 1);
            Draw.Line(this, Tp3LineTag, false, left, st.Tp3Line, 0, st.Tp3Line, Brushes.LimeGreen, DashStyleHelper.Solid, 1);
        }
        private void UpdateStatusOverlay(DateTime barTime)
        {
            if (!ShowStatusPanel)
            {
                RemoveDrawObject(StatusTag);
                RemoveDrawObject(StatusPnlTag);
                RemoveDrawObject(StatusLimitsTag);
                lastStatusText = string.Empty;
                lastStatusHealthy = false;
                lastStatusHasPnLLines = false;
                lastStatusPnlNegative = false;
                return;
            }

            string line1 = "AUTO: " + BuildStatusMessage();
            string pnlLine;
            string limitsLine;
            bool pnlNegative;
            bool hasPnLLines = TryBuildDailyPnlLines(barTime.Date, out pnlLine, out pnlNegative, out limitsLine);
            bool healthy = !manualHaltActive && !dailyLimitHalted;
            string composite = hasPnLLines ? line1 + "\n" + pnlLine + "\n" + limitsLine : line1;
            if (string.Equals(composite, lastStatusText, StringComparison.Ordinal)
                && healthy == lastStatusHealthy
                && hasPnLLines == lastStatusHasPnLLines
                && pnlNegative == lastStatusPnlNegative)
            {
                return;
            }

            lastStatusText = composite;
            lastStatusHealthy = healthy;
            lastStatusHasPnLLines = hasPnLLines;
            lastStatusPnlNegative = pnlNegative;

            var font = new SimpleFont("Arial", 13) { Bold = true };
            var line1Brush = healthy ? Brushes.LimeGreen : Brushes.OrangeRed;
            try
            {
                string line1Text = hasPnLLines ? line1 + "\n\n" : line1;
                Draw.TextFixed(this, StatusTag, line1Text, TextPosition.BottomLeft, line1Brush, font, Brushes.Black, Brushes.Transparent, 45);

                if (hasPnLLines)
                {
                    var pnlBrush = pnlNegative ? Brushes.Red : Brushes.LimeGreen;
                    var limitsBrush = Brushes.LimeGreen;
                    var transparent = Brushes.Transparent;
                    Draw.TextFixed(this, StatusPnlTag, pnlLine + "\n", TextPosition.BottomLeft, pnlBrush, font, transparent, transparent, 0);
                    Draw.TextFixed(this, StatusLimitsTag, limitsLine, TextPosition.BottomLeft, limitsBrush, font, transparent, transparent, 0);
                }
                else
                {
                    RemoveDrawObject(StatusPnlTag);
                    RemoveDrawObject(StatusLimitsTag);
                }
            }
            catch (Exception ex)
            {
                StrategyLogDebug("[UI] Status overlay failed: " + ex.Message);
            }
        }

        private string BuildStatusMessage()
        {
            if (dailyLimitHalted)
                return string.IsNullOrWhiteSpace(dailyLimitType) ? "HALTED: daily limit" : "HALTED: " + dailyLimitType + " reached";

            if (manualHaltActive)
                return "HALTED: manual flatten (awaiting resume)";

            if (lastUiEvalState != null && lastUiEvalState.Valid)
            {
                if (lastUiEvalState.BuyEntry && lastUiEvalState.SellEntry)
                    return "READY LONG/SHORT";
                if (lastUiEvalState.BuyEntry)
                    return "READY LONG";
                if (lastUiEvalState.SellEntry)
                    return "READY SHORT";
            }

            return "RUNNING";
        }

        private bool TryBuildDailyPnlLines(DateTime sessionDate, out string pnlLine, out bool pnlNegative, out string limitsLine)
        {
            double dailyPnl;
            if (!TryGetStrategyDailyTotalPnl(sessionDate, out dailyPnl))
                dailyPnl = 0.0;

            pnlNegative = dailyPnl < -1e-9;
            pnlLine = "TotalPnL: " + dailyPnl.ToString("C2");
            limitsLine = "DLL: " + FormatLossLimitText(Math.Abs(GetEffectiveDailyLossLimit())) + " | DPL: " + GetEffectiveDailyProfitLimit().ToString("C2");
            return true;
        }

        private string FormatLossLimitText(double absoluteLossLimit)
        {
            return "(" + absoluteLossLimit.ToString("C2") + ")";
        }

        private void AppendChecklistLine(List<string> lines, List<bool?> states, string label, bool? passed)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            string text = passed.HasValue
                ? string.Format("[{0}] {1}", passed.Value ? "x" : " ", label)
                : label;
            lines.Add(text);
            states.Add(passed);
        }

        private void UpdateChecklistOverlay()
        {
            if (!ShowChecklistPanel || ChartControl == null || lastUiEvalState == null || !lastUiEvalState.Valid)
            {
                RemoveDrawObject(ChecklistGreenTag);
                RemoveDrawObject(ChecklistRedTag);
                RemoveDrawObject(ChecklistNeutralTag);
                lastChecklistText = string.Empty;
                lastChecklistReady = false;
                return;
            }

            bool readyLong = lastUiEvalState.BuyEntry;
            bool readyShort = lastUiEvalState.SellEntry;
            string readiness = readyLong && readyShort
                ? "READY LONG/SHORT"
                : readyLong
                    ? "READY LONG"
                    : readyShort
                        ? "READY SHORT"
                        : "READY NO";

            var lines = new List<string>();
            var states = new List<bool?>();
            AppendChecklistLine(lines, states, "CHECK: " + readiness, readyLong || readyShort);
            AppendChecklistLine(lines, states, "TPS " + TPSType, null);
            AppendChecklistLine(lines, states, "Setup " + SetupType + " x" + TimeframeMultiplier, null);
            AppendChecklistLine(lines, states, "Lookahead " + (UseLookaheadApproximation ? "ON" : "OFF"), null);
            AppendChecklistLine(lines, states, "Trend Filter " + (lastUiEvalState.TrendAllowed ? "OK" : "NO"), lastUiEvalState.TrendAllowed);
            AppendChecklistLine(lines, states, "Raw Buy " + (lastUiEvalState.RawBuySignal ? "YES" : "NO"), lastUiEvalState.RawBuySignal);
            AppendChecklistLine(lines, states, "Raw Sell " + (lastUiEvalState.RawSellSignal ? "YES" : "NO"), lastUiEvalState.RawSellSignal);
            AppendChecklistLine(lines, states, "Entry Buy " + (lastUiEvalState.BuyEntry ? "READY" : "NO"), lastUiEvalState.BuyEntry);
            AppendChecklistLine(lines, states, "Entry Sell " + (lastUiEvalState.SellEntry ? "READY" : "NO"), lastUiEvalState.SellEntry);
            AppendChecklistLine(lines, states, "Ribbon " + (lastUiEvalState.BuyColor ? "LONG" : "SHORT"), null);
            AppendChecklistLine(lines, states, "Condition " + DescribeCondition(lastUiEvalState.ConditionNow), null);
            AppendChecklistLine(lines, states, string.Format("RSI {0:0.00}", lastUiEvalState.RsiValue), null);
            AppendChecklistLine(lines, states, string.Format("ATR {0:0.00} MA {1:0.00}", lastUiEvalState.AtrFilterValue, lastUiEvalState.AtrMaValue), null);
            AppendChecklistLine(lines, states, "Entry SL " + (EnableEntryStopLoss ? EntryStopLossType.ToString() : "OFF"), null);
            AppendChecklistLine(lines, states, "Trail " + (EnableTrailingEngine ? TrailingMode.ToString() : "OFF"), null);
            AppendChecklistLine(lines, states, "Pos " + FormatPositionSummary(), null);
            AppendChecklistLine(lines, states, "Trades/Entry " + GetEffectiveTradesPerEntry(), null);
            AppendChecklistLine(lines, states, "Daily Limits " + (EnableDailyPnLLimits ? "ON" : "OFF"), null);
            if (manualHaltActive)
                AppendChecklistLine(lines, states, "Manual Halt ACTIVE", false);
            if (dailyLimitHalted)
                AppendChecklistLine(lines, states, "Daily Limit Halt " + dailyLimitType, false);

            string compositeKey = string.Join("\n", lines);
            bool highlight = readyLong || readyShort;
            if (string.Equals(compositeKey, lastChecklistText, StringComparison.Ordinal) && highlight == lastChecklistReady)
                return;

            lastChecklistText = compositeKey;
            lastChecklistReady = highlight;

            string pnlLine;
            string limitsLine;
            bool pnlNegative;
            bool hasPnLLines = ShowStatusPanel && TryBuildDailyPnlLines(Time[0].Date, out pnlLine, out pnlNegative, out limitsLine);
            int statusLines = hasPnLLines ? 3 : (ShowStatusPanel ? 1 : 0);
            string padding = new string('\n', statusLines + 1);

            var green = new System.Text.StringBuilder();
            var red = new System.Text.StringBuilder();
            var neutral = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                bool? passed = states[i];
                if (!passed.HasValue)
                {
                    neutral.AppendLine(line);
                    green.AppendLine(" ");
                    red.AppendLine(" ");
                }
                else if (passed.Value)
                {
                    green.AppendLine(line);
                    red.AppendLine(" ");
                    neutral.AppendLine(" ");
                }
                else
                {
                    red.AppendLine(line);
                    green.AppendLine(" ");
                    neutral.AppendLine(" ");
                }
            }

            green.Append(padding);
            red.Append(padding);
            neutral.Append(padding);

            var font = new SimpleFont("Arial", 12) { Bold = true };
            try
            {
                Draw.TextFixed(this, ChecklistGreenTag, green.ToString(), TextPosition.BottomLeft, Brushes.LimeGreen, font, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.TextFixed(this, ChecklistRedTag, red.ToString(), TextPosition.BottomLeft, Brushes.OrangeRed, font, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.TextFixed(this, ChecklistNeutralTag, neutral.ToString(), TextPosition.BottomLeft, Brushes.LightGray, font, Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                StrategyLogDebug("[UI] Checklist overlay failed: " + ex.Message);
            }
        }

        private string DescribeCondition(double condition)
        {
            if (condition >= 1.3)
                return "LONG TP3";
            if (condition >= 1.2)
                return "LONG TP2";
            if (condition >= 1.1)
                return "LONG TP1";
            if (condition >= 1.0)
                return "LONG LIVE";
            if (condition <= -1.3)
                return "SHORT TP3";
            if (condition <= -1.2)
                return "SHORT TP2";
            if (condition <= -1.1)
                return "SHORT TP1";
            if (condition <= -1.0)
                return "SHORT LIVE";
            return "FLAT";
        }

        private string FormatPositionSummary()
        {
            return Position.MarketPosition == MarketPosition.Flat
                ? "FLAT"
                : Position.MarketPosition + " x" + Math.Abs(Position.Quantity).ToString(CultureInfo.InvariantCulture);
        }

        private void TryInitializeChartTraderButtons()
        {
            if (chartTraderButtonsAdded || ChartControl == null)
                return;

            try
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (chartTraderButtonsAdded || ChartControl == null)
                        return;

                    chartWindow = Window.GetWindow(ChartControl) as Chart;
                    if (chartWindow == null)
                        return;

                    chartTrader = FindFirstChild<ChartTrader>(chartWindow);
                    if (chartTrader == null)
                        return;

                    chartTraderGrid = chartTrader.Content as Grid;
                    if (chartTraderGrid == null)
                        chartTraderGrid = FindFirstChild<Grid>(chartTrader);
                    if (chartTraderGrid == null)
                        return;

                    chartTraderButtonsRow = new RowDefinition { Height = GridLength.Auto };
                    chartTraderGrid.RowDefinitions.Add(chartTraderButtonsRow);
                    chartTraderButtonPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(2, 4, 2, 2)
                    };

                    var buttonRow1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var buttonRow2 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch };
                    var inputRow1 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) };
                    var inputRow2 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) };
                    var inputRow3 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 2, 0, 0) };

                    manualFlattenButton = new Button { Content = "Flatten + Halt", Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
                    manualResumeButton = new Button { Content = "Resume", Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2) };
                    manualBuyButton = new Button { Content = "Manual Buy", Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2), Background = Brushes.DarkGreen, Foreground = Brushes.White };
                    manualSellButton = new Button { Content = "Manual Sell", Margin = new Thickness(2), Padding = new Thickness(6, 2, 6, 2), Background = Brushes.DarkRed, Foreground = Brushes.White };

                    manualFlattenButton.Click += ManualFlattenButton_Click;
                    manualResumeButton.Click += ManualResumeButton_Click;
                    manualBuyButton.Click += ManualBuyButton_Click;
                    manualSellButton.Click += ManualSellButton_Click;

                    tradesPerEntryLabel = new TextBlock { Text = "Trades/Entry", Width = 78, Margin = new Thickness(2, 4, 6, 2), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
                    tradesPerEntryTextBox = CreateRuntimeTextBox();
                    tradesPerEntryTextBox.ToolTip = "Overrides TradesPerEntry while strategy is running.";
                    tradesPerEntryTextBox.PreviewMouseDown += TradesPerEntryTextBox_PreviewMouseDown;
                    tradesPerEntryTextBox.PreviewKeyDown += TradesPerEntryTextBox_PreviewKeyDown;
                    tradesPerEntryTextBox.LostFocus += TradesPerEntryTextBox_LostFocus;

                    dllLabel = new TextBlock { Text = "DLL", Width = 78, Margin = new Thickness(2, 4, 6, 2), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
                    dllTextBox = CreateRuntimeTextBox();
                    dllTextBox.ToolTip = "Runtime daily loss limit override (positive dollars).";
                    dllTextBox.PreviewMouseDown += DllTextBox_PreviewMouseDown;
                    dllTextBox.PreviewKeyDown += DllTextBox_PreviewKeyDown;
                    dllTextBox.LostFocus += DllTextBox_LostFocus;

                    dplLabel = new TextBlock { Text = "DPL", Width = 78, Margin = new Thickness(2, 4, 6, 2), VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
                    dplTextBox = CreateRuntimeTextBox();
                    dplTextBox.ToolTip = "Runtime daily profit limit override (positive dollars).";
                    dplTextBox.PreviewMouseDown += DplTextBox_PreviewMouseDown;
                    dplTextBox.PreviewKeyDown += DplTextBox_PreviewKeyDown;
                    dplTextBox.LostFocus += DplTextBox_LostFocus;

                    buttonRow1.Children.Add(manualFlattenButton);
                    buttonRow1.Children.Add(manualResumeButton);
                    buttonRow2.Children.Add(manualBuyButton);
                    buttonRow2.Children.Add(manualSellButton);
                    inputRow1.Children.Add(tradesPerEntryLabel);
                    inputRow1.Children.Add(tradesPerEntryTextBox);
                    inputRow2.Children.Add(dllLabel);
                    inputRow2.Children.Add(dllTextBox);
                    inputRow3.Children.Add(dplLabel);
                    inputRow3.Children.Add(dplTextBox);

                    chartTraderButtonPanel.Children.Add(buttonRow1);
                    chartTraderButtonPanel.Children.Add(buttonRow2);
                    chartTraderButtonPanel.Children.Add(inputRow1);
                    chartTraderButtonPanel.Children.Add(inputRow2);
                    chartTraderButtonPanel.Children.Add(inputRow3);
                    Grid.SetRow(chartTraderButtonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                    Grid.SetColumnSpan(chartTraderButtonPanel, Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                    chartTraderGrid.Children.Add(chartTraderButtonPanel);

                    chartTraderButtonsAdded = true;
                    UpdateManualTradeButtons(true);
                    UpdateRuntimeInputBoxes(true);
                });
            }
            catch (Exception ex)
            {
                StrategyLogDebug("[UI] Init failed: " + ex.Message);
            }
        }

        private TextBox CreateRuntimeTextBox()
        {
            return new TextBox
            {
                Width = 72,
                Margin = new Thickness(2),
                Padding = new Thickness(4, 1, 4, 1),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Focusable = true,
                IsTabStop = true
            };
        }

        private void RemoveChartTraderButtons()
        {
            try
            {
                if (ChartControl != null)
                {
                    ChartControl.Dispatcher.InvokeAsync(() => RemoveChartTraderButtonsCore());
                    return;
                }
            }
            catch { }

            RemoveChartTraderButtonsCore();
        }

        private void RemoveChartTraderButtonsCore()
        {
            if (!chartTraderButtonsAdded)
                return;

            if (manualFlattenButton != null)
                manualFlattenButton.Click -= ManualFlattenButton_Click;
            if (manualResumeButton != null)
                manualResumeButton.Click -= ManualResumeButton_Click;
            if (manualBuyButton != null)
                manualBuyButton.Click -= ManualBuyButton_Click;
            if (manualSellButton != null)
                manualSellButton.Click -= ManualSellButton_Click;
            if (tradesPerEntryTextBox != null)
            {
                tradesPerEntryTextBox.PreviewMouseDown -= TradesPerEntryTextBox_PreviewMouseDown;
                tradesPerEntryTextBox.LostFocus -= TradesPerEntryTextBox_LostFocus;
                tradesPerEntryTextBox.PreviewKeyDown -= TradesPerEntryTextBox_PreviewKeyDown;
            }
            if (dllTextBox != null)
            {
                dllTextBox.PreviewMouseDown -= DllTextBox_PreviewMouseDown;
                dllTextBox.LostFocus -= DllTextBox_LostFocus;
                dllTextBox.PreviewKeyDown -= DllTextBox_PreviewKeyDown;
            }
            if (dplTextBox != null)
            {
                dplTextBox.PreviewMouseDown -= DplTextBox_PreviewMouseDown;
                dplTextBox.LostFocus -= DplTextBox_LostFocus;
                dplTextBox.PreviewKeyDown -= DplTextBox_PreviewKeyDown;
            }

            if (chartTraderGrid != null && chartTraderButtonPanel != null)
                chartTraderGrid.Children.Remove(chartTraderButtonPanel);
            if (chartTraderGrid != null && chartTraderButtonsRow != null)
                chartTraderGrid.RowDefinitions.Remove(chartTraderButtonsRow);

            chartTraderButtonsAdded = false;
            chartTraderButtonPanel = null;
            chartTraderButtonsRow = null;
            chartTraderGrid = null;
            chartTrader = null;
            chartWindow = null;
            manualFlattenButton = null;
            manualResumeButton = null;
            manualBuyButton = null;
            manualSellButton = null;
            tradesPerEntryTextBox = null;
            dllTextBox = null;
            dplTextBox = null;
        }

        private void UpdateManualTradeButtons(bool force)
        {
            if ((manualFlattenButton == null && manualResumeButton == null && manualBuyButton == null && manualSellButton == null) || ChartControl == null)
                return;

            bool manualEnabled = manualHaltActive && State == State.Realtime;
            bool resumeEnabled = manualHaltActive && State == State.Realtime;
            if (!force && manualEnabled == lastManualButtonsEnabled && resumeEnabled == lastResumeEnabled)
                return;

            lastManualButtonsEnabled = manualEnabled;
            lastResumeEnabled = resumeEnabled;

            Action apply = () =>
            {
                if (manualBuyButton != null)
                {
                    manualBuyButton.IsEnabled = manualEnabled;
                    manualBuyButton.Background = manualEnabled ? Brushes.DarkGreen : Brushes.DimGray;
                    manualBuyButton.Foreground = manualEnabled ? Brushes.White : Brushes.LightGray;
                    manualBuyButton.Opacity = manualEnabled ? 1.0 : 0.6;
                    manualBuyButton.ToolTip = manualEnabled
                        ? "Manual trades enabled while strategy is halted."
                        : "Enable by clicking Flatten + Halt.";
                }

                if (manualSellButton != null)
                {
                    manualSellButton.IsEnabled = manualEnabled;
                    manualSellButton.Background = manualEnabled ? Brushes.DarkRed : Brushes.DimGray;
                    manualSellButton.Foreground = manualEnabled ? Brushes.White : Brushes.LightGray;
                    manualSellButton.Opacity = manualEnabled ? 1.0 : 0.6;
                    manualSellButton.ToolTip = manualEnabled
                        ? "Manual trades enabled while strategy is halted."
                        : "Enable by clicking Flatten + Halt.";
                }

                if (manualResumeButton != null)
                    manualResumeButton.IsEnabled = resumeEnabled;
                if (manualFlattenButton != null)
                    manualFlattenButton.IsEnabled = State == State.Realtime;
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateRuntimeInputBoxes(bool force)
        {
            if ((tradesPerEntryTextBox == null && dllTextBox == null && dplTextBox == null) || ChartControl == null)
                return;

            int tradesValue = GetEffectiveTradesPerEntry();
            double dllValue = Math.Abs(GetEffectiveDailyLossLimit());
            double dplValue = GetEffectiveDailyProfitLimit();

            Action apply = () =>
            {
                if (tradesPerEntryTextBox != null && (force || lastTradesPerEntryDisplay != tradesValue) && !tradesPerEntryTextBox.IsKeyboardFocusWithin)
                {
                    tradesPerEntryTextBox.Text = tradesValue.ToString(CultureInfo.InvariantCulture);
                    lastTradesPerEntryDisplay = tradesValue;
                }
                if (dllTextBox != null && (force || Math.Abs(lastDllDisplay - dllValue) > 1e-9) && !dllTextBox.IsKeyboardFocusWithin)
                {
                    dllTextBox.Text = dllValue.ToString("0.##", CultureInfo.InvariantCulture);
                    lastDllDisplay = dllValue;
                }
                if (dplTextBox != null && (force || Math.Abs(lastDplDisplay - dplValue) > 1e-9) && !dplTextBox.IsKeyboardFocusWithin)
                {
                    dplTextBox.Text = dplValue.ToString("0.##", CultureInfo.InvariantCulture);
                    lastDplDisplay = dplValue;
                }
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void ManualFlattenButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualHaltRequest(), null);
        }

        private void ManualResumeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualResumeRequest(), null);
        }

        private void ManualBuyButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOrderRequest(MarketPosition.Long), null);
        }

        private void ManualSellButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOrderRequest(MarketPosition.Short), null);
        }

        private void TradesPerEntryTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            FocusRuntimeTextBox(tradesPerEntryTextBox, e);
        }

        private void TradesPerEntryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleIntegerTextBoxKey(tradesPerEntryTextBox, e, SubmitTradesPerEntryInput);
        }

        private void TradesPerEntryTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SubmitTradesPerEntryInput();
        }

        private void SubmitTradesPerEntryInput()
        {
            if (tradesPerEntryTextBox == null)
                return;
            string text = tradesPerEntryTextBox.Text;
            TriggerCustomEvent(o => HandleTradesPerEntryOverrideRequest(o as string), text);
        }

        private void DllTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            FocusRuntimeTextBox(dllTextBox, e);
        }

        private void DllTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleDecimalTextBoxKey(dllTextBox, e, SubmitDllInput);
        }

        private void DllTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SubmitDllInput();
        }

        private void SubmitDllInput()
        {
            if (dllTextBox == null)
                return;
            string text = dllTextBox.Text;
            TriggerCustomEvent(o => HandleDllOverrideRequest(o as string), text);
        }

        private void DplTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            FocusRuntimeTextBox(dplTextBox, e);
        }

        private void DplTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            HandleDecimalTextBoxKey(dplTextBox, e, SubmitDplInput);
        }

        private void DplTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SubmitDplInput();
        }

        private void SubmitDplInput()
        {
            if (dplTextBox == null)
                return;
            string text = dplTextBox.Text;
            TriggerCustomEvent(o => HandleDplOverrideRequest(o as string), text);
        }

        private void HandleManualHaltRequest()
        {
            HandleManualHaltRequest(true);
        }

        private void HandleManualHaltRequest(bool allowBootstrap)
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[MANUAL_HALT] Flatten ignored (strategy not realtime).");
                return;
            }

            if (allowBootstrap)
                EnsureManualTradeStateForPosition();
            manualHaltActive = true;
            manualHaltActivatedAt = DateTime.UtcNow;
            haltReason = "manual_halt";

            try
            {
                MultiStratManager.Instance?.ActivateManualHaltOverride(Account != null ? Account.Name : string.Empty, Name);
            }
            catch { }

            EnforceFlatWhenHalted("manual_halt");
            StrategyLogInfo("[MANUAL_HALT] Flatten requested.");
            UpdateManualTradeButtons(true);
            UpdateStatusOverlay(CurrentBar > 0 ? Time[0] : DateTime.Now);
            UpdateChecklistOverlay();
        }

        private void HandleManualResumeRequest()
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[MANUAL_HALT] Resume ignored (strategy not realtime).");
                return;
            }

            if (!manualHaltActive)
            {
                UpdateStatusOverlay(CurrentBar > 0 ? Time[0] : DateTime.Now);
                return;
            }

            manualHaltActive = false;
            manualHaltActivatedAt = DateTime.MinValue;
            haltReason = string.Empty;
            try
            {
                MultiStratManager.Instance?.ClearManualHaltOverride(Account != null ? Account.Name : string.Empty, "manual_resume");
            }
            catch { }
            StrategyLogInfo("[MANUAL_HALT] Strategy resumed by user.");
            UpdateManualTradeButtons(true);
            UpdateStatusOverlay(CurrentBar > 0 ? Time[0] : DateTime.Now);
            UpdateChecklistOverlay();
        }

        private void HandleManualOrderRequest(MarketPosition direction)
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[MANUAL] Manual trade ignored (strategy not realtime).");
                return;
            }

            if (!manualHaltActive)
            {
                StrategyLogInfo("[MANUAL] Manual trade ignored (manual halt not active).");
                return;
            }

            EnsureManualTradeStateForPosition();

            if (direction == MarketPosition.Long)
            {
                if (Position.MarketPosition == MarketPosition.Short)
                    SubmitExit(MarketPosition.Short, Position.Quantity, "MANUAL_REV");
                SubmitEntryBatch(MarketPosition.Long, "MANBUY", null, true);
            }
            else if (direction == MarketPosition.Short)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    SubmitExit(MarketPosition.Long, Position.Quantity, "MANUAL_REV");
                SubmitEntryBatch(MarketPosition.Short, "MANSELL", null, true);
            }
        }

        private void EnsureManualTradeStateForPosition()
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
                return;

            if (tradeStates != null && tradeStates.Count > 0)
                return;

            if (TryRestoreTradeStateFromTradeSync())
                return;

            BootstrapExistingPositionState(true);
        }

        private bool TryRestoreTradeStateFromTradeSync()
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
                return false;

            var manager = MultiStratManager.Instance;
            var tradeSync = manager != null ? manager.TradeSync : null;
            if (tradeSync == null || Account == null || Instrument == null)
                return false;

            string acct = Account.Name ?? string.Empty;
            string inst = Instrument.FullName ?? string.Empty;
            MarketPosition side = Position.MarketPosition;
            var openTrades = tradeSync.GetOpenTradesSnapshot();
            if (openTrades == null || openTrades.Count == 0)
                return false;

            var record = openTrades.FirstOrDefault(r =>
                r != null &&
                r.Side == side &&
                string.Equals((r.AccountName ?? string.Empty).Trim(), acct.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals((r.Instrument ?? string.Empty).Trim(), inst.Trim(), StringComparison.OrdinalIgnoreCase));
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return false;

            if (tradeStates == null)
                tradeStates = new Dictionary<string, PineTradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            if (tradeStates.Count > 0 || openTradeOrder.Count > 0)
                return false;

            int posQty = Math.Abs(Position.Quantity);
            int qty = record.RemainingQuantity > 0 ? record.RemainingQuantity : Math.Max(1, posQty);
            int originalQty = record.NtQuantity > 0 ? record.NtQuantity : Math.Max(1, posQty);
            double entryPrice = record.EntryPrice > 0 ? record.EntryPrice : Position.AveragePrice;

            var state = new PineTradeRuntimeState
            {
                TradeId = record.TradeId,
                SyncTradeId = record.AggregateEntry ? record.TradeId : null,
                EntrySignal = record.TradeId,
                EntrySide = record.Side,
                OriginalQuantity = originalQty,
                RemainingQuantity = qty,
                InstrumentName = string.IsNullOrWhiteSpace(record.Instrument) ? inst : record.Instrument,
                AccountName = string.IsNullOrWhiteSpace(record.AccountName) ? acct : record.AccountName,
                EntryPrice = entryPrice,
                OpenPublished = true,
                IsSynthetic = false,
                EntryTimeUtc = DateTime.UtcNow,
                NtPointsPer1kLoss = record.NtPointsPer1kLoss
            };

            if (state.NtPointsPer1kLoss <= 0)
            {
                try
                {
                    double pointValue = Instrument.MasterInstrument.PointValue;
                    state.NtPointsPer1kLoss = pointValue > 0 ? 1000.0 / pointValue : 0.0;
                }
                catch
                {
                    state.NtPointsPer1kLoss = 0.0;
                }
            }

            tradeStates[record.TradeId] = state;
            openTradeOrder.Add(record.TradeId);
            entrySignalToTradeId[record.TradeId] = record.TradeId;
            if (!string.IsNullOrEmpty(state.SyncTradeId))
            {
                syncGroups[state.SyncTradeId] = new PineTradeSyncGroup
                {
                    TradeId = state.SyncTradeId,
                    Side = state.EntrySide,
                    TotalQuantity = originalQty,
                    LastPublishedRemaining = qty,
                    OpenPublished = true,
                    CreatedAtUtc = DateTime.UtcNow
                };
                syncGroups[state.SyncTradeId].MemberTradeIds.Add(record.TradeId);
            }

            StrategyLogDebug("[MANUAL][SYNC] Rehydrated trade state from TradeSync for " + record.TradeId + ".");
            return true;
        }

        private void BootstrapExistingPositionState(bool allowWhileHalted = false)
        {
            if (manualHaltActive && !allowWhileHalted)
                return;
            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
                return;

            int accountQty = GetAccountInstrumentSignedQuantity();
            if (StartBehavior == StartBehavior.ImmediatelySubmitSynchronizeAccount && accountQty == 0)
            {
                StrategyLogDebug("[AUTO][BOOTSTRAP] Deferring bootstrap while waiting for start-behavior sync entry (account flat).");
                return;
            }

            if (tradeStates == null)
                tradeStates = new Dictionary<string, PineTradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            if (tradeStates.Count > 0 || openTradeOrder.Count > 0)
                return;

            MarketPosition side = Position.MarketPosition;
            int qty = Math.Abs(Position.Quantity);
            string tradeId = CreateTradeId(side == MarketPosition.Long ? "LONG" : "SHORT");
            var state = new PineTradeRuntimeState
            {
                TradeId = tradeId,
                SyncTradeId = null,
                EntrySignal = tradeId,
                EntrySide = side,
                OriginalQuantity = qty,
                RemainingQuantity = qty,
                InstrumentName = Instrument != null ? Instrument.FullName : string.Empty,
                AccountName = Account != null ? Account.Name : string.Empty,
                EntryPrice = Position.AveragePrice,
                OpenPublished = false,
                IsSynthetic = false,
                EntryTimeUtc = DateTime.UtcNow
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
            entrySignalToTradeId[tradeId] = tradeId;
            StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Seeded trade {0} for existing position {1} qty={2}", tradeId, side, qty));

            if (State == State.Realtime)
                PublishOpenEvent(state);
        }

        private int GetAccountInstrumentSignedQuantity()
        {
            if (Account == null || Instrument == null)
                return 0;

            try
            {
                foreach (var accountPosition in Account.Positions)
                {
                    if (accountPosition?.Instrument == null)
                        continue;
                    if (!string.Equals(accountPosition.Instrument.FullName, Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    return (int)GetSignedQuantity(accountPosition.MarketPosition, accountPosition.Quantity);
                }
            }
            catch (Exception ex)
            {
                StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] Unable to read account quantity: {0}", ex.Message));
            }

            return 0;
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

        private void ResetRuntimeState()
        {
            if (tradeStates == null)
                tradeStates = new Dictionary<string, PineTradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            else
                tradeStates.Clear();

            if (entrySignalToTradeId == null)
                entrySignalToTradeId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            else
                entrySignalToTradeId.Clear();

            if (syncGroups == null)
                syncGroups = new Dictionary<string, PineTradeSyncGroup>(StringComparer.OrdinalIgnoreCase);
            else
                syncGroups.Clear();

            if (workingEntryOrders == null)
                workingEntryOrders = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
            else
                workingEntryOrders.Clear();

            openTradeOrder.Clear();
            atrEntryQuantity = 0;
            atrEntrySide = MarketPosition.Flat;
            lastUiEvalState = null;
            lastStatusText = string.Empty;
            lastStatusHealthy = false;
            lastStatusHasPnLLines = false;
            lastStatusPnlNegative = false;
            lastChecklistText = string.Empty;
            lastChecklistReady = false;
            lastSignalDiagnosticsBarTime = Core.Globals.MinDate;
        }

        private void FocusRuntimeTextBox(TextBox textBox, MouseButtonEventArgs e)
        {
            if (textBox == null)
                return;

            textBox.Focus();
            Keyboard.Focus(textBox);
            textBox.SelectAll();
            e.Handled = true;
        }

        private void HandleIntegerTextBoxKey(TextBox textBox, KeyEventArgs e, Action submitAction)
        {
            HandleRuntimeTextBoxKey(textBox, e, submitAction, false);
        }

        private void HandleDecimalTextBoxKey(TextBox textBox, KeyEventArgs e, Action submitAction)
        {
            HandleRuntimeTextBoxKey(textBox, e, submitAction, true);
        }

        private void HandleRuntimeTextBoxKey(TextBox textBox, KeyEventArgs e, Action submitAction, bool allowDecimal)
        {
            if (textBox == null)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                submitAction?.Invoke();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
                return;

            if (e.Key == Key.Back)
            {
                ApplyTextBoxDeletion(textBox, true);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                ApplyTextBoxDeletion(textBox, false);
                e.Handled = true;
                return;
            }

            if (IsNavigationKey(e.Key))
                return;

            string insertText;
            if (TryGetTextInputFromKey(textBox, e.Key, allowDecimal, out insertText))
            {
                ApplyTextBoxEdit(textBox, insertText);
                e.Handled = true;
                return;
            }

            e.Handled = true;
        }

        private static bool IsNavigationKey(Key key)
        {
            return key == Key.Left
                || key == Key.Right
                || key == Key.Tab
                || key == Key.Home
                || key == Key.End;
        }

        private static bool TryGetTextInputFromKey(TextBox textBox, Key key, bool allowDecimal, out string insertText)
        {
            insertText = null;

            char digit;
            if (TryGetDigitFromKey(key, out digit))
            {
                insertText = digit.ToString();
                return true;
            }

            if (!allowDecimal)
                return false;

            if (key != Key.Decimal && key != Key.OemPeriod)
                return false;

            if (textBox == null)
            {
                insertText = ".";
                return true;
            }

            string current = textBox.Text ?? string.Empty;
            int start = Math.Max(0, textBox.SelectionStart);
            int length = Math.Max(0, textBox.SelectionLength);
            if (start > current.Length)
                start = current.Length;
            if (start + length > current.Length)
                length = current.Length - start;

            string remaining = current.Remove(start, length);
            if (remaining.Contains("."))
                return false;

            insertText = ".";
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

        private void ApplyTextBoxDeletion(TextBox textBox, bool backspace)
        {
            if (textBox == null)
                return;

            string current = textBox.Text ?? string.Empty;
            int start = Math.Max(0, textBox.SelectionStart);
            int length = Math.Max(0, textBox.SelectionLength);
            if (start > current.Length)
                start = current.Length;
            if (start + length > current.Length)
                length = current.Length - start;

            if (length > 0)
            {
                current = current.Remove(start, length);
            }
            else if (backspace)
            {
                if (start <= 0 || current.Length == 0)
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

        private void ApplyTextBoxEdit(TextBox textBox, string insertText)
        {
            if (textBox == null || string.IsNullOrEmpty(insertText))
                return;

            string current = textBox.Text ?? string.Empty;
            int start = Math.Max(0, textBox.SelectionStart);
            int length = Math.Max(0, textBox.SelectionLength);
            if (start > current.Length)
                start = current.Length;
            if (start + length > current.Length)
                length = current.Length - start;

            string updated = current.Remove(start, length).Insert(start, insertText);
            textBox.Text = updated;
            textBox.SelectionStart = Math.Min(updated.Length, start + insertText.Length);
            textBox.SelectionLength = 0;
        }

        private void HandleTradesPerEntryOverrideRequest(string text)
        {
            int parsed;
            if (string.IsNullOrWhiteSpace(text))
            {
                tradesPerEntryOverride = 0;
            }
            else if (int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) && parsed > 0)
            {
                tradesPerEntryOverride = Math.Max(1, Math.Min(MaxTradesPerEntry, parsed));
            }
            UpdateRuntimeInputBoxes(true);
            UpdateStatusOverlay(Time[0]);
            UpdateChecklistOverlay();
        }

        private void HandleDllOverrideRequest(string text)
        {
            double parsed;
            if (string.IsNullOrWhiteSpace(text))
                runtimeDailyLossLimit = null;
            else if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                runtimeDailyLossLimit = Math.Max(0.0, parsed);
            UpdateRuntimeInputBoxes(true);
            UpdateStatusOverlay(Time[0]);
            UpdateChecklistOverlay();
        }

        private void HandleDplOverrideRequest(string text)
        {
            double parsed;
            if (string.IsNullOrWhiteSpace(text))
                runtimeDailyProfitLimit = null;
            else if (double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                runtimeDailyProfitLimit = Math.Max(0.0, parsed);
            UpdateRuntimeInputBoxes(true);
            UpdateStatusOverlay(Time[0]);
            UpdateChecklistOverlay();
        }

        private static T FindFirstChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed)
                    return typed;
                T descendant = FindFirstChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            return null;
        }

        [NinjaScriptProperty, Display(Name = "TPS Type", GroupName = "01 - Core", Order = 0)]
        public PineTpSType TPSType { get; set; }

        [NinjaScriptProperty, Display(Name = "Setup Type", GroupName = "01 - Core", Order = 1)]
        public PineSetupType SetupType { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "Timeframe Multiplier", GroupName = "01 - Core", Order = 2)]
        public int TimeframeMultiplier { get; set; }

        [NinjaScriptProperty, Display(Name = "Use Lookahead Approximation", GroupName = "01 - Core", Order = 3)]
        public bool UseLookaheadApproximation { get; set; }

        [NinjaScriptProperty, Display(Name = "Sideways Filter", GroupName = "02 - Filters", Order = 0)]
        public PineSidewaysFilterType SidewaysFilterType { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "RSI Period", GroupName = "02 - Filters", Order = 1)]
        public int RsiPeriod { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "Top Limit RSI", GroupName = "02 - Filters", Order = 2)]
        public int TopLimitRsi { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "Bottom Limit RSI", GroupName = "02 - Filters", Order = 3)]
        public int BottomLimitRsi { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "ATR Filter Length", GroupName = "02 - Filters", Order = 4)]
        public int AtrFilterLength { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "ATR MA Length", GroupName = "02 - Filters", Order = 5)]
        public int AtrMaLength { get; set; }

        [NinjaScriptProperty, Display(Name = "Replicate ATR MA Typo", GroupName = "02 - Filters", Order = 6)]
        public bool ReplicateAtrMaTypo { get; set; }

        [NinjaScriptProperty, Display(Name = "Use EMA When Typo Disabled", GroupName = "02 - Filters", Order = 7)]
        public bool AtrMaUseEmaWhenTypoDisabled { get; set; }

        [NinjaScriptProperty, Display(Name = "Renko Use ATR", GroupName = "03 - Renko", Order = 0)]
        public bool RenkoUseAtr { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "Renko ATR Length", GroupName = "03 - Renko", Order = 1)]
        public int RenkoAtrLength { get; set; }

        [NinjaScriptProperty, Range(1, 100000), Display(Name = "Renko Traditional Ticks", GroupName = "03 - Renko", Order = 2)]
        public int RenkoTraditionalTicks { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Renko Fast EMA", GroupName = "03 - Renko", Order = 3)]
        public int RenkoFastEma { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Renko Slow EMA", GroupName = "03 - Renko", Order = 4)]
        public int RenkoSlowEma { get; set; }

        [NinjaScriptProperty, Range(50, 5000), Display(Name = "Renko Source Bars", GroupName = "03 - Renko", Order = 5)]
        public int RenkoSourceBars { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "ATR Length", GroupName = "04 - ATR Mode", Order = 0)]
        public int AtrLength { get; set; }

        [NinjaScriptProperty, Range(0.1, 100.0), Display(Name = "Profit Factor", GroupName = "04 - ATR Mode", Order = 1)]
        public double ProfitFactor { get; set; }

        [NinjaScriptProperty, Range(0.0, 100.0), Display(Name = "ATR Qty TP1", GroupName = "04 - ATR Mode", Order = 2)]
        public double AtrQtyTp1 { get; set; }

        [NinjaScriptProperty, Range(0.0, 100.0), Display(Name = "ATR Qty TP2", GroupName = "04 - ATR Mode", Order = 3)]
        public double AtrQtyTp2 { get; set; }

        [NinjaScriptProperty, Range(0.0, 100.0), Display(Name = "ATR Qty TP3", GroupName = "04 - ATR Mode", Order = 4)]
        public double AtrQtyTp3 { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Trailing Engine", GroupName = "05 - Trailing", Order = 0)]
        public bool EnableTrailingEngine { get; set; }

        [NinjaScriptProperty, Display(Name = "Trailing Mode", GroupName = "05 - Trailing", Order = 1)]
        public PineTrailingMode TrailingMode { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Trail Behavior", GroupName = "05 - Trailing", Order = 2)]
        public PineAtrTrailBehavior AtrTrailBehavior { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Trail Source", GroupName = "05 - Trailing", Order = 3)]
        public PineAtrTrailSource AtrTrailSource { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Trailing ATR Period", GroupName = "05 - Trailing", Order = 4)]
        public int TrailingAtrPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Trailing DEMA Length", GroupName = "05 - Trailing", Order = 5)]
        public int TrailingDemaLength { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Use External Activation", GroupName = "05 - Trailing", Order = 6)]
        public bool AtrUseExternalActivationThreshold { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR External Activation Type", GroupName = "05 - Trailing", Order = 7)]
        public PineExternalActivationType AtrExternalActivationType { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Trail Activation", GroupName = "05 - Trailing", Order = 8)]
        public double AtrTrailActivation { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Trail Step", GroupName = "05 - Trailing", Order = 9)]
        public double AtrTrailStep { get; set; }

        [NinjaScriptProperty, Display(Name = "ATR Trail Stop", GroupName = "05 - Trailing", Order = 10)]
        public double AtrTrailStop { get; set; }

        [NinjaScriptProperty, Display(Name = "Ticks Trail Activation", GroupName = "05 - Trailing", Order = 11)]
        public double TicksTrailActivation { get; set; }

        [NinjaScriptProperty, Display(Name = "Ticks Trail Step", GroupName = "05 - Trailing", Order = 12)]
        public double TicksTrailStep { get; set; }

        [NinjaScriptProperty, Display(Name = "Ticks Trail Stop", GroupName = "05 - Trailing", Order = 13)]
        public double TicksTrailStop { get; set; }

        [NinjaScriptProperty, Display(Name = "Dollars Trail Activation", GroupName = "05 - Trailing", Order = 14)]
        public double DollarsTrailActivation { get; set; }

        [NinjaScriptProperty, Display(Name = "Dollars Trail Step", GroupName = "05 - Trailing", Order = 15)]
        public double DollarsTrailStep { get; set; }

        [NinjaScriptProperty, Display(Name = "Dollars Trail Stop", GroupName = "05 - Trailing", Order = 16)]
        public double DollarsTrailStop { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Entry Stop Loss", GroupName = "06 - Entry Stops", Order = 0)]
        public bool EnableEntryStopLoss { get; set; }

        [NinjaScriptProperty, Display(Name = "Entry Stop Loss Type", GroupName = "06 - Entry Stops", Order = 1)]
        public PineEntryStopLossType EntryStopLossType { get; set; }

        [NinjaScriptProperty, Display(Name = "Stop Factor", GroupName = "06 - Entry Stops", Order = 2)]
        public double StopFactor { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Entry Stop ATR Period", GroupName = "06 - Entry Stops", Order = 3)]
        public int EntryStopAtrPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Entry Stop DEMA Length", GroupName = "06 - Entry Stops", Order = 4)]
        public int EntryStopDemaLength { get; set; }

        [NinjaScriptProperty, Display(Name = "Structure Stop Model", GroupName = "06 - Entry Stops", Order = 5)]
        public PineStructureStopModel StructureStopModel { get; set; }

        [NinjaScriptProperty, Display(Name = "BOS/CHOCH Engine", GroupName = "06 - Entry Stops", Order = 6)]
        public PineBosChochEngine BosChochEngine { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "Structure Pivot Strength", GroupName = "06 - Entry Stops", Order = 7)]
        public int StructurePivotStrength { get; set; }

        [NinjaScriptProperty, Display(Name = "Structure Buffer Type", GroupName = "06 - Entry Stops", Order = 8)]
        public PineStructureBufferType StructureBufferType { get; set; }

        [NinjaScriptProperty, Display(Name = "Structure Ticks Buffer", GroupName = "06 - Entry Stops", Order = 9)]
        public double StructureTicksBuffer { get; set; }

        [NinjaScriptProperty, Display(Name = "Structure ATR Buffer Multiple", GroupName = "06 - Entry Stops", Order = 10)]
        public double StructureAtrBufferMultiple { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "TradesPerEntry", GroupName = "07 - Runtime", Order = 0)]
        public int TradesPerEntry { get; set; }

        [NinjaScriptProperty, Display(Name = "Treat Multi-Entry As Single Trade", GroupName = "07 - Runtime", Order = 1)]
        public bool TreatMultiEntryAsSingleTrade { get; set; }

        [NinjaScriptProperty, Display(Name = "Start Halted On Enable", GroupName = "07 - Runtime", Order = 2)]
        public bool StartHaltedOnEnable { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Daily PnL Limits", GroupName = "07 - Runtime", Order = 3)]
        public bool EnableDailyPnLLimits { get; set; }

        [NinjaScriptProperty, Display(Name = "Daily Loss Limit", GroupName = "07 - Runtime", Order = 4)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty, Display(Name = "Daily Profit Limit", GroupName = "07 - Runtime", Order = 5)]
        public double DailyProfitLimit { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Ribbon", GroupName = "08 - Visuals", Order = 0)]
        public bool ShowRibbon { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Risk Lines", GroupName = "08 - Visuals", Order = 1)]
        public bool ShowRiskLines { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Event Labels", GroupName = "08 - Visuals", Order = 2)]
        public bool ShowEventLabels { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Status Panel", GroupName = "08 - Visuals", Order = 3)]
        public bool ShowStatusPanel { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Checklist Panel", GroupName = "08 - Visuals", Order = 4)]
        public bool ShowChecklistPanel { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "Risk Line Right Bars", GroupName = "08 - Visuals", Order = 5)]
        public int RiskLineRightBars { get; set; }

        [NinjaScriptProperty, Display(Name = "Debug", GroupName = "09 - Diagnostics", Order = 0)]
        public bool Debug { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Signal Diagnostics", GroupName = "09 - Diagnostics", Order = 1)]
        public bool EnableSignalDiagnostics { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Trade Story Logging", GroupName = "09 - Diagnostics", Order = 2)]
        public bool EnableTradeStoryLogging { get; set; }
    }
}
