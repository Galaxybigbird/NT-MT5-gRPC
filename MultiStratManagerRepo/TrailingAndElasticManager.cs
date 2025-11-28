#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Shared;
using NTGrpcClient;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    // Helper enums retained for legacy settings compatibility
    public enum InitialStopPlacementType { StartTrailingImmediately }
    public enum ContinuousTrailingType { None, DollarAmountTrail, PipTrail, TickTrail, DEMAAtrTrail, StepTrail }

    // Trackers exposed to UI/Manager
        public class ElasticPositionTracker
        {
            public string BaseId { get; set; }
            public string InstrumentFullName { get; set; }
            public string InstrumentName { get; set; }
            public MarketPosition MarketPosition { get; set; }
            public double EntryPrice { get; set; }
            public double CurrentTrailedStopPrice { get; set; }
            public double HighWaterMarkPrice { get; set; }
            public double LowWaterMarkPrice { get; set; }
            public bool IsTrailingActive { get; set; }
            public int ProfitUpdatesSent { get; set; }
            public double LastReportedProfitLevel { get; set; }
            public int LastReportedIncrement { get; set; }
            public DateTime LastUpdateTime { get; set; }
            public bool IsSLTPLogicCompleteForEntry { get; set; }
            public List<Quote> QuoteBuffer { get; } = new List<Quote>();
            public double CurrentAtrValue { get; set; }
            public double CurrentDemaValue { get; set; }
            public DateTime LastBarTimeProcessed { get; set; }
            public DateTime PendingBarStart { get; set; } = DateTime.MinValue;
            public double PendingBarOpen { get; set; }
            public double PendingBarHigh { get; set; }
            public double PendingBarLow { get; set; }
            public double PendingBarClose { get; set; }
            public Order ManagedStopOrder { get; set; }
            // Elastic trigger/increment runtime
            public bool Triggered { get; set; }
            public double IncrementUnitsAtTrigger { get; set; }
            public int OriginalQuantity { get; set; }
            public int RemainingQuantity { get; set; }
            public bool ManualStopOverride { get; set; }
            public bool ManualTargetOverride { get; set; }
        }

    public class TraditionalTrailingStop
    {
        public string BaseId { get; set; }
        public Order CurrentStopOrder { get; set; }
        public double LastStopPrice { get; set; }
        public DateTime LastModificationTime { get; set; }
        public int ModificationCount { get; set; }
        public Position TrackedPosition { get; set; }
        public double EntryPrice { get; set; }
        public bool IsActive { get; set; }
        public DateTime ActivationTime { get; set; }
        public double ActivationPrice { get; set; }
        public double MaxProfit { get; set; }
        public double InitialStopDistance { get; set; }
        public OrderState LastKnownOrderState { get; set; }
        public DateTime LastOrderStateUpdate { get; set; }
        public bool IsPendingModification { get; set; }
        public int FailedModificationAttempts { get; set; }
    }

    public class TrailingAndElasticManager : INotifyPropertyChanged
    {
        private readonly MultiStratManager parentManager;
        public TrailingAndElasticManager(MultiStratManager parent) { parentManager = parent; }

        // Timers and state
        private DispatcherTimer elasticMonitorTimer;

        // Tracking collections
        private readonly Dictionary<string, ElasticPositionTracker> elasticPositions = new Dictionary<string, ElasticPositionTracker>();
        private readonly Dictionary<string, TraditionalTrailingStop> traditionalTrailingStops = new Dictionary<string, TraditionalTrailingStop>();
        private readonly Dictionary<string, BarsRequest> barsRequests = new Dictionary<string, BarsRequest>();
        private static readonly TimeSpan IndicatorAggregationPeriod = TimeSpan.FromMilliseconds(250);
        private const int MaxIndicatorQuotes = 600;


        // Flags
        public bool EnableElasticHedging { get; set; } = true;
        public bool EnableTrailing { get; set; } = true;
        public bool UseTraditionalTrailing { get; set; } = false;

        // Elastic hedging trigger/increment settings
        public TrailingActivationType ElasticTriggerType { get; set; } = TrailingActivationType.Dollars;
        public double ProfitUpdateThreshold { get; set; } = 100.0;
        public int ElasticUpdateIntervalSeconds { get; set; } = 1; // retained for UI, unused (fixed 500ms)
        public TrailingActivationType ElasticProfitUnits { get; set; } = TrailingActivationType.Dollars;
        public double ElasticIncrementValue { get; set; } = 10.0;

        // Trailing activation/stop/increments settings
        public TrailingActivationType TrailingActivationMode { get; set; } = TrailingActivationType.Ticks;
        public double TrailingActivationValue { get; set; } = 25.0;
        public TrailingActivationType TrailingTriggerType { get; set; } = TrailingActivationType.Dollars;
        public double TrailingTriggerValue { get; set; } = 100.0;
        public TrailingActivationType TrailingStopType { get; set; } = TrailingActivationType.Dollars;
        public double TrailingStopValue { get; set; } = 50.0;
        public TrailingActivationType TrailingIncrementsType { get; set; } = TrailingActivationType.Dollars;
        public double TrailingIncrementsValue { get; set; } = 10.0;
        private bool _useAlternativeTrailing = true;
        public bool UseAlternativeTrailing
        {
            get => _useAlternativeTrailing;
            set { if (_useAlternativeTrailing != value) { _useAlternativeTrailing = value; OnPropertyChanged(nameof(UseAlternativeTrailing)); } }
        }

        // Legacy/indicator settings retained for compatibility
        public bool EnableTrailingStop { get; set; } = false;
        public int ActivateTrailAfterPipsProfit { get; set; } = 20;
        public InitialStopPlacementType InitialStopPlacement { get; set; } = InitialStopPlacementType.StartTrailingImmediately;
        public ContinuousTrailingType TrailingType { get; set; } = ContinuousTrailingType.DollarAmountTrail;
        public double DollarTrailDistance { get; set; } = 100.00;
        public int PipTrailDistance { get; set; } = 15;
        public int TickTrailDistance { get; set; } = 30;
        public string StepTrailLevelsCsv { get; set; } = "20:10,40:20,60:30";
        public int AtrPeriod { get; set; } = 14;
        public double AtrMultiplier { get; set; } = 2.5;
        public bool UseATRTrailing { get; set; } = false;
        public int DEMA_ATR_Period { get; set; } = 14;
        public double DEMA_ATR_Multiplier { get; set; } = 1.5;

        // Public accessors for UI
        public Dictionary<string, ElasticPositionTracker> ElasticPositions => new Dictionary<string, ElasticPositionTracker>(elasticPositions);
        public Dictionary<string, TraditionalTrailingStop> TraditionalTrailingStops => new Dictionary<string, TraditionalTrailingStop>(traditionalTrailingStops);

        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        #endregion

        #region Logging Helper
        private void LogAndPrint(string message)
        {
            try { parentManager?.LogInfo("TRAILING", message); }
            catch { NinjaTrader.Code.Output.Process(message, PrintTo.OutputTab1); }
        }
        #endregion

        #region Public Methods
        public void InitializeElasticMonitoring(Account monitoredAccount)
        {
            if (elasticMonitorTimer != null) { LogAndPrint("ELASTIC_DEBUG: Elastic monitoring already initialized, skipping"); return; }
            LogAndPrint("ELASTIC_DEBUG: Initializing elastic hedging monitoring");
            LogAndPrint($"ELASTIC_DEBUG: EnableElasticHedging = {EnableElasticHedging}");
            LogAndPrint($"ELASTIC_DEBUG: ProfitUpdateThreshold = {ProfitUpdateThreshold}");
            LogAndPrint($"ELASTIC_DEBUG: ElasticProfitUnits = {ElasticProfitUnits}, ElasticIncrementValue = {ElasticIncrementValue}");
            LogAndPrint($"ELASTIC_DEBUG: MonitoredAccount = {(monitoredAccount?.Name ?? "NULL")}");

            elasticMonitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            elasticMonitorTimer.Tick += (sender, e) => MonitorPositionsForElasticHedging(monitoredAccount);
            elasticMonitorTimer.Start();
            LogAndPrint($"ELASTIC_DEBUG: Elastic monitoring timer started with {ElasticUpdateIntervalSeconds}s interval for account {monitoredAccount?.Name}");
        }

        public void StopElasticMonitoring()
        {
            if (elasticMonitorTimer != null)
            {
                elasticMonitorTimer.Stop();
                elasticMonitorTimer = null;
                LogAndPrint("Elastic monitoring stopped");
            }
        }

        public void RegisterTrade(TradeSyncService.TradeRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return;

            if (!EnableElasticHedging)
            {
                LogAndPrint($"ELASTIC_ADD_DEBUG: Elastic hedging disabled, skipping tracking for {record.TradeId}");
                return;
            }

            var tracker = new ElasticPositionTracker
            {
                BaseId = record.TradeId,
                InstrumentFullName = record.Instrument ?? string.Empty,
                InstrumentName = record.Instrument ?? string.Empty,
                MarketPosition = record.Side,
                EntryPrice = record.EntryPrice,
                CurrentTrailedStopPrice = 0,
                HighWaterMarkPrice = record.EntryPrice,
                LowWaterMarkPrice = record.EntryPrice,
                ProfitUpdatesSent = 0,
                LastReportedProfitLevel = 0.0,
                LastReportedIncrement = 0,
                LastUpdateTime = DateTime.Now,
                PendingBarStart = DateTime.UtcNow,
                PendingBarOpen = record.EntryPrice,
                PendingBarHigh = record.EntryPrice,
                PendingBarLow = record.EntryPrice,
                PendingBarClose = record.EntryPrice,
                IsSLTPLogicCompleteForEntry = false,
                Triggered = false,
                IncrementUnitsAtTrigger = 0,
                OriginalQuantity = record.NtQuantity,
                RemainingQuantity = record.RemainingQuantity
            };

            elasticPositions[tracker.BaseId] = tracker;
            LogAndPrint($"ELASTIC_ADD_DEBUG: Registered trade {tracker.BaseId} for elastic tracking (qty={tracker.OriginalQuantity})");
        }

        public void UpdateRemainingQuantity(string baseId, int remainingQuantity)
        {
            if (string.IsNullOrWhiteSpace(baseId))
                return;

            if (!elasticPositions.TryGetValue(baseId, out var tracker))
                return;

            tracker.RemainingQuantity = remainingQuantity;
            tracker.LastUpdateTime = DateTime.Now;

            if (remainingQuantity <= 0)
            {
                tracker.Triggered = false;
                tracker.ProfitUpdatesSent = 0;
                tracker.LastReportedIncrement = 0;
            }
        }

        public void CompleteTrade(string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId))
                return;

            if (elasticPositions.Remove(baseId))
            {
                LogAndPrint($"ELASTIC_DEBUG: Removed elastic tracking for {baseId}");
            }

            if (traditionalTrailingStops.Remove(baseId))
            {
                LogAndPrint($"TRADITIONAL_TRAILING: Removed trailing tracking for {baseId}");
            }
        }

        public void ApplyManualOverride(string baseId, bool? stopLocked, bool? targetLocked)
        {
            if (string.IsNullOrWhiteSpace(baseId))
                return;

            if (!elasticPositions.TryGetValue(baseId, out var tracker))
            {
                LogAndPrint($"TRAILING_MANUAL_OVERRIDE: Received override for {baseId} but no tracker exists.");
                return;
            }

            if (stopLocked.HasValue && tracker.ManualStopOverride != stopLocked.Value)
            {
                tracker.ManualStopOverride = stopLocked.Value;
                LogAndPrint($"TRAILING_MANUAL_OVERRIDE: {baseId} stopLocked => {stopLocked.Value}");
            }

            if (targetLocked.HasValue && tracker.ManualTargetOverride != targetLocked.Value)
            {
                tracker.ManualTargetOverride = targetLocked.Value;
                LogAndPrint($"TRAILING_MANUAL_OVERRIDE: {baseId} targetLocked => {targetLocked.Value}");
            }
        }

        public void AddElasticPositionTracking(string baseId, Position position, double entryPrice)
        {
            LogAndPrint($"ELASTIC_ADD_DEBUG: AddElasticPositionTracking called - BaseId: {baseId}, EnableElasticHedging: {EnableElasticHedging}");
            if (!EnableElasticHedging) { LogAndPrint($"ELASTIC_ADD_DEBUG: Elastic hedging disabled, skipping tracking for {baseId}"); return; }
            var tracker = new ElasticPositionTracker
            {
                BaseId = baseId,
                EntryPrice = entryPrice,
                LastReportedProfitLevel = 0.0,
                ProfitUpdatesSent = 0,
                LastUpdateTime = DateTime.Now,
                LastReportedIncrement = 0,
                InstrumentFullName = position.Instrument.FullName,
                InstrumentName = position.Instrument.FullName,
                MarketPosition = position.MarketPosition,
                IsTrailingActive = false,
                CurrentTrailedStopPrice = 0,
                HighWaterMarkPrice = entryPrice,
                LowWaterMarkPrice = entryPrice,
                PendingBarStart = DateTime.UtcNow,
                PendingBarOpen = entryPrice,
                PendingBarHigh = entryPrice,
                PendingBarLow = entryPrice,
                PendingBarClose = entryPrice,
                IsSLTPLogicCompleteForEntry = false,
                OriginalQuantity = position.Quantity,
                RemainingQuantity = position.Quantity
            };
            elasticPositions[baseId] = tracker;
            double addCurrentPrice = GetCurrentPrice(position.Instrument);
            LogAndPrint($"ELASTIC_ADD_DEBUG: ✅ Successfully added elastic tracking for {baseId} at {entryPrice:F2}. Total tracked: {elasticPositions.Count}. PnL=${position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, addCurrentPrice):F2}");
        }

        public void RemovePositionTracking(string baseId)
        {
            bool removedElastic = elasticPositions.Remove(baseId);
            if (removedElastic) LogAndPrint($"Removed position tracking for {baseId} - Elastic: {removedElastic}");
        }

        public void InitializeBarsRequests()
        {
            try
            {
                LogAndPrint("Initializing BarsRequest subscriptions for trailing stops");
                if (parentManager.MonitoredAccount != null && parentManager.MonitoredAccount.Positions != null)
                {
                    foreach (var position in parentManager.MonitoredAccount.Positions)
                    {
                        if (position.Quantity != 0) SubscribeToInstrumentBars(position.Instrument);
                    }
                }
                LogAndPrint($"BarsRequest initialization complete. Active subscriptions: {barsRequests.Count}");
            }
            catch (Exception ex) { LogAndPrint($"Error initializing BarsRequest subscriptions: {ex.Message}"); }
        }

        public void CleanupBarsRequests()
        {
            try
            {
                LogAndPrint($"Cleaning up {barsRequests.Count} BarsRequest subscriptions");
                foreach (var kvp in barsRequests)
                {
                    try { if (kvp.Value != null) { kvp.Value.Update -= OnBarsUpdate; kvp.Value.Dispose(); } }
                    catch (Exception ex) { LogAndPrint($"Error disposing BarsRequest for {kvp.Key}: {ex.Message}"); }
                }
                barsRequests.Clear();
                LogAndPrint("BarsRequest cleanup complete");
            }
            catch (Exception ex) { LogAndPrint($"Error in CleanupBarsRequests: {ex.Message}"); }
        }

        // Placeholder subscription method to satisfy builds in environments where the BarsRequest API
        // isn’t available or when bar streaming isn’t required. If you want indicator-driven trailing,
        // wire up an actual BarsRequest here and hook OnBarsUpdate.
        private void SubscribeToInstrumentBars(Instrument instrument)
        {
            try
            {
                if (instrument == null) return;
                string key = instrument.FullName;
                if (barsRequests.ContainsKey(key)) return; // Already tracked

                // In some NinjaTrader environments, constructing BarsRequest requires specific params
                // and session templates. To keep this build-safe, we log and no-op by default.
                LogAndPrint($"BarsRequest subscription not configured in this build for {key}. Skipping.");
            }
            catch (Exception ex)
            {
                LogAndPrint($"Error in SubscribeToInstrumentBars: {ex.Message}");
            }
        }
        #endregion

        #region Private Methods - Elastic Hedging
        private void UpdateIndicatorSeries(ElasticPositionTracker tracker, double currentPrice)
        {
            try
            {
                if (tracker == null || currentPrice <= 0)
                    return;

                DateTime now = DateTime.UtcNow;

                if (tracker.PendingBarStart == DateTime.MinValue)
                {
                    tracker.PendingBarStart = now;
                    tracker.PendingBarOpen = currentPrice;
                    tracker.PendingBarHigh = currentPrice;
                    tracker.PendingBarLow = currentPrice;
                    tracker.PendingBarClose = currentPrice;
                    return;
                }

                tracker.PendingBarHigh = Math.Max(tracker.PendingBarHigh, currentPrice);
                tracker.PendingBarLow = Math.Min(tracker.PendingBarLow, currentPrice);
                tracker.PendingBarClose = currentPrice;

                if (now - tracker.PendingBarStart < IndicatorAggregationPeriod)
                    return;

                tracker.QuoteBuffer.Add(new Quote
                {
                    Date = now,
                    Open = tracker.PendingBarOpen,
                    High = tracker.PendingBarHigh,
                    Low = tracker.PendingBarLow,
                    Close = tracker.PendingBarClose,
                    Volume = 0
                });

                if (tracker.QuoteBuffer.Count > MaxIndicatorQuotes)
                    tracker.QuoteBuffer.RemoveRange(0, tracker.QuoteBuffer.Count - MaxIndicatorQuotes);

                tracker.PendingBarStart = now;
                tracker.PendingBarOpen = currentPrice;
                tracker.PendingBarHigh = currentPrice;
                tracker.PendingBarLow = currentPrice;
                tracker.PendingBarClose = currentPrice;
                tracker.LastBarTimeProcessed = now;

                CalculateIndicators(tracker);
            }
            catch (Exception ex)
            {
                LogAndPrint($"Error updating indicator series: {ex.Message}");
            }
        }

        private void MonitorPositionsForElasticHedging(Account monitoredAccount)
        {
            if (monitoredAccount == null || !EnableElasticHedging) return;
            try
            {
                ScanForMissingPositionTrackers(monitoredAccount);
                foreach (var tracker in elasticPositions.Values.ToList())
                {
                    if (tracker.RemainingQuantity <= 0)
                        continue;

                    var position = monitoredAccount.Positions.FirstOrDefault(p => p.Instrument.FullName == tracker.InstrumentFullName && p.MarketPosition == tracker.MarketPosition);
                    if (position != null)
                    {
                        double currentPrice = GetCurrentPrice(position.Instrument);
                        UpdateIndicatorSeries(tracker, currentPrice);
                        double currentProfitDollars = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);

                        // Compute trigger progress in trigger units
                        double triggerUnits = ElasticTriggerType switch
                        {
                            TrailingActivationType.Dollars => currentProfitDollars,
                            TrailingActivationType.Pips => Math.Abs(currentPrice - tracker.EntryPrice) / GetPipValueForInstrument(position.Instrument),
                            TrailingActivationType.Ticks => Math.Abs(currentPrice - tracker.EntryPrice) / position.Instrument.MasterInstrument.TickSize,
                            TrailingActivationType.Percent => Math.Abs((currentPrice - tracker.EntryPrice) / tracker.EntryPrice) * 100.0,
                            _ => currentProfitDollars
                        };

                        double trigger = ProfitUpdateThreshold;
                        double inc = Math.Max(ElasticIncrementValue, 1e-7);

                        // Trigger detection
                        if (!tracker.Triggered && triggerUnits >= trigger)
                        {
                            tracker.Triggered = true;
                            tracker.IncrementUnitsAtTrigger = ElasticProfitUnits switch
                            {
                                TrailingActivationType.Dollars => currentProfitDollars,
                                TrailingActivationType.Pips => Math.Abs(currentPrice - tracker.EntryPrice) / GetPipValueForInstrument(position.Instrument),
                                TrailingActivationType.Ticks => Math.Abs(currentPrice - tracker.EntryPrice) / position.Instrument.MasterInstrument.TickSize,
                                TrailingActivationType.Percent => Math.Abs((currentPrice - tracker.EntryPrice) / tracker.EntryPrice) * 100.0,
                                _ => currentProfitDollars
                            };
                            int earnedAtTrigger = 1;
                            Task.Run(() => SendElasticHedgeUpdate(tracker.BaseId, currentProfitDollars, earnedAtTrigger));
                            tracker.LastReportedIncrement = earnedAtTrigger;
                            tracker.ProfitUpdatesSent++;
                            tracker.LastUpdateTime = DateTime.Now;
                            LogAndPrint($"Elastic update sent (trigger) for {tracker.BaseId}: Profit=${currentProfitDollars:F2}, Level={earnedAtTrigger}");
                            continue;
                        }

                        // If triggered, compute increments
                        int earnedIncrements = tracker.LastReportedIncrement;
                        double currentIncUnits = 0.0;
                        if (tracker.Triggered)
                        {
                            currentIncUnits = ElasticProfitUnits switch
                            {
                                TrailingActivationType.Dollars => currentProfitDollars,
                                TrailingActivationType.Pips => Math.Abs(currentPrice - tracker.EntryPrice) / GetPipValueForInstrument(position.Instrument),
                                TrailingActivationType.Ticks => Math.Abs(currentPrice - tracker.EntryPrice) / position.Instrument.MasterInstrument.TickSize,
                                TrailingActivationType.Percent => Math.Abs((currentPrice - tracker.EntryPrice) / tracker.EntryPrice) * 100.0,
                                _ => currentProfitDollars
                            };
                            double deltaUnits = Math.Max(0.0, currentIncUnits - tracker.IncrementUnitsAtTrigger);
                            int additional = (int)Math.Floor(deltaUnits / inc);
                            earnedIncrements = Math.Max(earnedIncrements, 1 + additional);
                        }

                        bool shouldSend = earnedIncrements > tracker.LastReportedIncrement;
                        LogAndPrint($"ELASTIC_DEBUG: {tracker.BaseId} TriggerUnits={triggerUnits:F2} ({ElasticTriggerType}), Trigger={trigger}, IncUnits={currentIncUnits:F2} ({ElasticProfitUnits}), Inc={inc}, Earned={earnedIncrements}, LastSent={tracker.LastReportedIncrement}, ShouldSend={shouldSend}");

                        if (shouldSend && earnedIncrements > 0)
                        {
                            int levelToSend = earnedIncrements;
                            Task.Run(() => SendElasticHedgeUpdate(tracker.BaseId, currentProfitDollars, levelToSend));
                            tracker.LastReportedIncrement = earnedIncrements;
                            tracker.ProfitUpdatesSent++;
                            tracker.LastUpdateTime = DateTime.Now;
                            LogAndPrint($"Elastic update sent for {tracker.BaseId}: Profit=${currentProfitDollars:F2}, Level={levelToSend}");
                        }

                        // Trailing stop updates (alternative or traditional)
                        if (EnableTrailing && ShouldUpdateTrailingStop(tracker, position))
                        {
                            UpdateTrailingStop(tracker, position);
                            tracker.LastUpdateTime = DateTime.Now;
                        }
                    }
                    else
                    {
                        // Confirm the NT position is truly flat (not just MT5 hedge reduced) before removing tracking
                        try
                        {
                            var acct = parentManager?.MonitoredAccount;
                            var stillOpen = acct?.Positions?.Any(p => p.Instrument.FullName == tracker.InstrumentFullName && p.MarketPosition == tracker.MarketPosition && p.Quantity != 0) == true;
                            if (stillOpen)
                            {
                                LogAndPrint($"ELASTIC_DEBUG: Position missing in local lookup but account still shows open qty for {tracker.BaseId}; retaining elastic tracking");
                            }
                            else
                            {
                                elasticPositions.Remove(tracker.BaseId);
                                LogAndPrint($"Position {tracker.BaseId} closed, removing from elastic tracking");
                            }
                        }
                        catch
                        {
                            // Fallback to prior behavior if account check fails
                            elasticPositions.Remove(tracker.BaseId);
                            LogAndPrint($"Position {tracker.BaseId} closed, removing from elastic tracking");
                        }
                    }
                }
            }
            catch (Exception ex) { LogAndPrint($"ERROR in elastic monitoring: {ex.Message}"); }
        }

        private void ScanForMissingPositionTrackers(Account monitoredAccount)
        {
            if (monitoredAccount == null) return;
            try
            {
                var positions = monitoredAccount.Positions.Where(p => p.MarketPosition != MarketPosition.Flat).ToList();
                if (positions.Count > 0 || elasticPositions.Count > 0)
                    LogAndPrint($"POSITION_SCAN: Scanning account {monitoredAccount.Name} - {positions.Count} positions, {elasticPositions.Count} trackers");

                foreach (var position in positions)
                {
                    bool alreadyTracked = elasticPositions.Values.Any(tr => tr.InstrumentFullName == position.Instrument.FullName && tr.MarketPosition == position.MarketPosition);
                    if (!alreadyTracked)
                    {
                        LogAndPrint($"POSITION_SCAN: Position {position.Instrument.FullName} ({position.MarketPosition}) not tracked yet - awaiting strategy-issued trade_id, skipping synthetic tracker");
                    }
                }
            }
            catch (Exception ex) { LogAndPrint($"ERROR in ScanForMissingPositionTrackers: {ex.Message}"); }
        }

    private async Task SendElasticHedgeUpdate(string baseId, double currentProfit, int profitLevel)
        {
            var updateData = new Dictionary<string, object>
            {
        // Keep lightweight action for diagnostics; server will convert to EVENT internally
        { "action", "ELASTIC_PING" },
        { "event_type", "elastic_ping" },
        { "base_id", baseId },
        // Use schema-aligned keys so the gRPC client fills proto fields
        { "current_profit", currentProfit },
        { "profit_level", profitLevel },
        { "id", Guid.NewGuid().ToString() },
        { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
            };
            LogAndPrint($"Sending elastic ping for {baseId}: Profit=${currentProfit:F2}, Level={profitLevel}");
            try
            {
                var json = SimpleJson.SerializeObject(updateData);
                bool success = TradingGrpcClient.SubmitElasticUpdate(json);
                if (success) LogAndPrint($"Elastic ping sent via gRPC for {baseId}");
                else LogAndPrint($"Failed to send elastic ping via gRPC: {TradingGrpcClient.LastError}");
            }
            catch (Exception grpcEx) { LogAndPrint($"ERROR sending elastic ping via gRPC: {grpcEx.Message}"); }
        }
        #endregion

        #region Private Methods - Alternative/Traditional Trailing
    private void UpdateTrailingStop(ElasticPositionTracker tracker, Position position)
        {
            try
            {
                double unrealizedPips = CalculateUnrealizedPips(position);
                double trailCurrentPrice = GetCurrentPrice(position.Instrument);
                double currentPnL = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, trailCurrentPrice);
                LogAndPrint($"TRAILING_DEBUG: Checking trailing for {tracker.BaseId} - EnableTrailing: {EnableTrailing}, UnrealizedPips: {unrealizedPips:F2}, PnL: ${currentPnL:F2}");

                if (tracker.ManualStopOverride)
                {
                    LogAndPrint($"TRAILING_MANUAL_OVERRIDE: Stop locked for {tracker.BaseId}; skipping trailing update.");
                    return;
                }

                // For Alternative trailing, activate based on the Elastic trigger settings so UI semantics match (e.g., 50 ticks)
                TrailingActivationType activeMode = UseAlternativeTrailing ? ElasticTriggerType : TrailingActivationMode;
                double activeThreshold = UseAlternativeTrailing ? ProfitUpdateThreshold : TrailingActivationValue;

                bool shouldActivate = false; double currentValue = 0;
                switch (activeMode)
                {
                    case TrailingActivationType.Ticks:
                        currentValue = unrealizedPips / GetPipValueForInstrument(position.Instrument) * position.Instrument.MasterInstrument.TickSize;
                        shouldActivate = currentValue >= activeThreshold; break;
                    case TrailingActivationType.Pips:
                        currentValue = unrealizedPips; shouldActivate = currentValue >= activeThreshold; break;
                    case TrailingActivationType.Dollars:
                        currentValue = currentPnL; shouldActivate = currentValue >= activeThreshold; break;
                    case TrailingActivationType.Percent:
                        currentValue = Math.Abs((trailCurrentPrice - tracker.EntryPrice) / tracker.EntryPrice) * 100; shouldActivate = currentValue >= activeThreshold; break;
                }

                if (!tracker.IsTrailingActive && shouldActivate)
                {
                    tracker.IsTrailingActive = true;
                    tracker.HighWaterMarkPrice = trailCurrentPrice;
                    tracker.LowWaterMarkPrice = trailCurrentPrice;
                    LogAndPrint($"TRAILING_ACTIVATED: Trailing activated for {tracker.BaseId} at {activeMode}={currentValue:F2} (threshold: {activeThreshold})");
                }

                if (tracker.IsTrailingActive)
                {
                    if (position.MarketPosition == MarketPosition.Long && trailCurrentPrice > tracker.HighWaterMarkPrice)
                        tracker.HighWaterMarkPrice = trailCurrentPrice;
                    else if (position.MarketPosition == MarketPosition.Short && (tracker.LowWaterMarkPrice == 0 || trailCurrentPrice < tracker.LowWaterMarkPrice))
                        tracker.LowWaterMarkPrice = trailCurrentPrice;

                    int earnedLevel = 0;
                    double newStopPrice = UseAlternativeTrailing ?
                        CalculateAlternativeTrailingStopPrice(tracker, position, out earnedLevel) :
                        CalculateTrailingStopPrice(tracker, position);

                    if (!UseAlternativeTrailing && TrailingType == ContinuousTrailingType.DEMAAtrTrail &&
                        (tracker.CurrentAtrValue <= 0 || tracker.CurrentDemaValue <= 0))
                    {
                        if (tracker.CurrentTrailedStopPrice <= 0)
                            LogAndPrint($"DEMA_TRAIL_WAIT: {tracker.BaseId} waiting for indicator warmup (quotes={tracker.QuoteBuffer.Count}).");
                        return;
                    }

                    bool shouldUpdate = ShouldUpdateTrailingStop(tracker, position);
                    bool significantChange = Math.Abs(newStopPrice - tracker.CurrentTrailedStopPrice) >= position.Instrument.MasterInstrument.TickSize;
                    // Always allow initial placement
                    bool initialPlacement = tracker.ManagedStopOrder == null || tracker.CurrentTrailedStopPrice <= 0;
                    if (initialPlacement) { shouldUpdate = true; significantChange = true; }
                    LogAndPrint($"TRAILING_DEBUG: Stop calculation - Current: {tracker.CurrentTrailedStopPrice:F2}, New: {newStopPrice:F2}, ShouldUpdate: {shouldUpdate}, SignificantChange: {significantChange}");

                    if (shouldUpdate && significantChange)
                    {
                        if (!UseAlternativeTrailing)
                        {
                            if (tracker.ManagedStopOrder == null) SubmitTrailingStopOrder(tracker, position, newStopPrice);
                            else UpdateTrailingStopOrder(tracker, newStopPrice);
                            tracker.CurrentTrailedStopPrice = newStopPrice;
                            // Reset PnL baseline for dollar-based increment gating
                            try { tracker.LastReportedProfitLevel = currentPnL; } catch { }
                        }
                        else
                        {
                            // Alternative trailing now also submits/updates a local NT stop so you can see it on the chart
                            if (tracker.ManagedStopOrder == null) SubmitTrailingStopOrder(tracker, position, newStopPrice);
                            else UpdateTrailingStopOrder(tracker, newStopPrice);
                            Task.Run(() => SendTrailingStopUpdate(tracker.BaseId, newStopPrice, trailCurrentPrice));
                            tracker.CurrentTrailedStopPrice = newStopPrice;
                            LogAndPrint($"Trailing stop updated for {tracker.BaseId}: {newStopPrice:F2}");
                            // On each trailing increment hit and applied, also send a profit update
                            try
                            {
                                double currentProfitDollars = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, trailCurrentPrice);
                                int levelToSend = Math.Max(earnedLevel, 1);
                                Task.Run(() => SendElasticHedgeUpdate(tracker.BaseId, currentProfitDollars, levelToSend));
                                // Keep monitor and trailing paths in sync to avoid out-of-order/lower-level resends
                                if (tracker.LastReportedIncrement < levelToSend)
                                    tracker.LastReportedIncrement = levelToSend;
                                LogAndPrint($"Elastic profit update sent due to trailing increment for {tracker.BaseId}: Profit=${currentProfitDollars:F2}, Level={levelToSend}");
                            }
                            catch (Exception sendEx)
                            {
                                LogAndPrint($"ERROR sending profit update on trailing increment: {sendEx.Message}");
                            }
                            // Reset PnL baseline for dollar-based increment gating
                            try { tracker.LastReportedProfitLevel = currentPnL; } catch { }
                        }
                    }
                    else
                    {
                        LogAndPrint($"TRAILING_UPDATE_SKIP: Not updating stop for {tracker.BaseId} - ShouldUpdate: {shouldUpdate}, SignificantChange: {significantChange}");
                    }
                }
            }
            catch (Exception ex) { LogAndPrint($"ERROR updating trailing stop for {tracker.BaseId}: {ex.Message}"); }
        }

        private double CalculateAlternativeTrailingStopPrice(ElasticPositionTracker tracker, Position position, out int earnedLevel)
        {
            earnedLevel = 0;
            try
            {
                double currentPrice = GetCurrentPrice(position.Instrument);
                if (currentPrice == 0) return tracker.CurrentTrailedStopPrice;
                double entryPrice = position.AveragePrice;
                double currentPnL = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
                double pointValuePerContract = position.Instrument.MasterInstrument.PointValue;
                double tickSizeGlobal = position.Instrument.MasterInstrument.TickSize;

                double stopPrice = tracker.CurrentTrailedStopPrice;
                // Use Elastic trigger settings to align with UI
                var uiTriggerType = ElasticTriggerType;
                double uiTriggerValue = ProfitUpdateThreshold;
                switch (TrailingStopType)
                {
                    case TrailingActivationType.Dollars:
                        {
                            // Treat dollars as per-contract amounts (e.g., $5 = 1 tick for many US futures)
                            double initialLockedProfit = TrailingStopValue; // dollars per contract from entry
                            double trailingIncrement = Math.Max(TrailingIncrementsValue, 1e-9); // dollars per contract
                            double triggerLevel = uiTriggerType == TrailingActivationType.Dollars
                                ? uiTriggerValue
                                : uiTriggerType == TrailingActivationType.Ticks
                                    ? (uiTriggerValue * position.Instrument.MasterInstrument.TickSize) * (pointValuePerContract * position.Quantity)
                                    : uiTriggerType == TrailingActivationType.Pips
                                        ? (uiTriggerValue * GetPipValueForInstrument(position.Instrument)) * (pointValuePerContract * position.Quantity)
                                        : // Percent
                                          ((uiTriggerValue / 100.0) * entryPrice) * (pointValuePerContract * position.Quantity);
                            // Conversion: one per-contract dollar step to price units and ticks
                            double priceIncrementFromDollars = (trailingIncrement / pointValuePerContract);
                            double ticksPerIncrementDollars = priceIncrementFromDollars / tickSizeGlobal;
                            LogAndPrint($"TRAILING_CONVERSION_DEBUG: Dollars mode — tickSize={tickSizeGlobal}, incDollars={trailingIncrement}, priceIncrement={priceIncrementFromDollars:F4} (~{ticksPerIncrementDollars:F2} ticks)");
                            if (currentPnL < triggerLevel) return tracker.CurrentTrailedStopPrice;
                            double extraProfit = Math.Max(0, currentPnL - triggerLevel);
                            int additional = (int)Math.Floor(extraProfit / (trailingIncrement * position.Quantity));
                            earnedLevel = 1 + additional;
                            // Compute candidate stop from cumulative levels
                            double totalLockedPerContract = initialLockedProfit + (additional * trailingIncrement);
                            double lockedProfitInPoints = totalLockedPerContract / pointValuePerContract;
                            double candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + lockedProfitInPoints : entryPrice - lockedProfitInPoints;
                            // Initial placement: strictly initial distance only
                            if (tracker.CurrentTrailedStopPrice <= 0)
                            {
                                double initPoints = initialLockedProfit / pointValuePerContract;
                                candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + initPoints : entryPrice - initPoints;
                            }
                            // Cap to one increment per update
                            double incPrice = priceIncrementFromDollars;
                            if (tracker.CurrentTrailedStopPrice > 0)
                            {
                                if (position.MarketPosition == MarketPosition.Long)
                                    candidate = Math.Min(candidate, tracker.CurrentTrailedStopPrice + incPrice);
                                else
                                    candidate = Math.Max(candidate, tracker.CurrentTrailedStopPrice - incPrice);
                                LogAndPrint($"TRAILING_STEP_CAP_DEBUG: Dollars — prev={tracker.CurrentTrailedStopPrice:F4}, incPrice={incPrice:F4}, cappedCandidate={candidate:F4}");
                            }
                            stopPrice = candidate;
                        }
                        break;
                    case TrailingActivationType.Pips:
                        {
                            double pipValue = GetPipValueForInstrument(position.Instrument);
                            double initialPips = TrailingStopValue; // pips from entry
                            double incPips = Math.Max(TrailingIncrementsValue, 1e-9); // pips
                            // Conversion debug: show price units and ticks per increment
                            double priceIncrementFromPips = incPips * pipValue;
                            double ticksPerIncrementPips = priceIncrementFromPips / tickSizeGlobal;
                            LogAndPrint($"TRAILING_CONVERSION_DEBUG: Pips mode — tickSize={tickSizeGlobal}, pipValue={pipValue}, incPips={incPips}, priceIncrement={priceIncrementFromPips:F4} (~{ticksPerIncrementPips:F2} ticks)");
                            double triggerPips = uiTriggerType switch
                            {
                                TrailingActivationType.Pips => uiTriggerValue,
                                TrailingActivationType.Ticks => (uiTriggerValue * position.Instrument.MasterInstrument.TickSize) / pipValue,
                                TrailingActivationType.Dollars => (uiTriggerValue / (pointValuePerContract * position.Quantity)) / pipValue,
                                TrailingActivationType.Percent => ((uiTriggerValue / 100.0) * entryPrice) / pipValue,
                                _ => uiTriggerValue
                            };
                            double currentPips = Math.Abs(currentPrice - entryPrice) / pipValue;
                            if (currentPips < triggerPips) return tracker.CurrentTrailedStopPrice;
                            double extraPips = Math.Max(0, currentPips - triggerPips);
                            int additional = (int)Math.Floor(extraPips / incPips);
                            earnedLevel = 1 + additional;
                            double totalPipsFromEntry = initialPips + (additional * incPips);
                            double candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + (totalPipsFromEntry * pipValue) : entryPrice - (totalPipsFromEntry * pipValue);
                            if (tracker.CurrentTrailedStopPrice <= 0)
                            {
                                double initPrice = initialPips * pipValue;
                                candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + initPrice : entryPrice - initPrice;
                            }
                            // Cap to one increment
                            double incPrice = priceIncrementFromPips;
                            if (tracker.CurrentTrailedStopPrice > 0)
                            {
                                if (position.MarketPosition == MarketPosition.Long)
                                    candidate = Math.Min(candidate, tracker.CurrentTrailedStopPrice + incPrice);
                                else
                                    candidate = Math.Max(candidate, tracker.CurrentTrailedStopPrice - incPrice);
                                LogAndPrint($"TRAILING_STEP_CAP_DEBUG: Pips — prev={tracker.CurrentTrailedStopPrice:F4}, incPrice={incPrice:F4}, cappedCandidate={candidate:F4}");
                            }
                            stopPrice = candidate;
                        }
                        break;
                    case TrailingActivationType.Ticks:
                        {
                            double tickSize = position.Instrument.MasterInstrument.TickSize;
                            double initialTicks = TrailingStopValue; // ticks from entry
                            double incTicks = Math.Max(TrailingIncrementsValue, 1e-9); // ticks
                            // Extra diagnostics to ensure UI -> Elastic -> Trailing sync
                            LogAndPrint($"TRAILING_SYNC_DEBUG: Units Sync — ElasticProfitUnits={ElasticProfitUnits}, ElasticIncrementValue={ElasticIncrementValue}, TrailingIncrementsType={TrailingIncrementsType}, TrailingIncrementsValue={TrailingIncrementsValue}");
                            // Conversion debug: show price increment in this mode
                            double priceIncrementFromTicks = incTicks * tickSize;
                            LogAndPrint($"TRAILING_CONVERSION_DEBUG: Ticks mode — tickSize={tickSize}, incTicks={incTicks}, priceIncrement={priceIncrementFromTicks:F4} (exact {incTicks:F2} ticks)");
                            double triggerTicks = uiTriggerType switch
                            {
                                TrailingActivationType.Ticks => uiTriggerValue,
                                TrailingActivationType.Pips => (uiTriggerValue * GetPipValueForInstrument(position.Instrument)) / tickSize,
                                TrailingActivationType.Dollars => (uiTriggerValue / (pointValuePerContract * position.Quantity)) / tickSize,
                                TrailingActivationType.Percent => ((uiTriggerValue / 100.0) * entryPrice) / tickSize,
                                _ => uiTriggerValue
                            };
                            double currentTicks = Math.Abs(currentPrice - entryPrice) / tickSize;
                            if (currentTicks < triggerTicks) return tracker.CurrentTrailedStopPrice;
                            double extraTicks = Math.Max(0, currentTicks - triggerTicks);
                            int additional = (int)Math.Floor(extraTicks / incTicks);
                            earnedLevel = 1 + additional;
                            double totalTicksFromEntry = initialTicks + (additional * incTicks);
                            double candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + (totalTicksFromEntry * tickSize) : entryPrice - (totalTicksFromEntry * tickSize);
                            if (tracker.CurrentTrailedStopPrice <= 0)
                            {
                                double initPrice = initialTicks * tickSize;
                                candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + initPrice : entryPrice - initPrice;
                            }
                            // Cap to one increment
                            double incPrice = priceIncrementFromTicks;
                            if (tracker.CurrentTrailedStopPrice > 0)
                            {
                                if (position.MarketPosition == MarketPosition.Long)
                                    candidate = Math.Min(candidate, tracker.CurrentTrailedStopPrice + incPrice);
                                else
                                    candidate = Math.Max(candidate, tracker.CurrentTrailedStopPrice - incPrice);
                                LogAndPrint($"TRAILING_STEP_CAP_DEBUG: Ticks — prev={tracker.CurrentTrailedStopPrice:F4}, incPrice={incPrice:F4}, cappedCandidate={candidate:F4}");
                            }
                            stopPrice = candidate;
                        }
                        break;
                    case TrailingActivationType.Percent:
                        {
                            double initialPercent = TrailingStopValue; // percent from entry
                            double incPercent = Math.Max(TrailingIncrementsValue, 1e-9); // percent
                            // Conversion debug: show price units and ticks per increment
                            double priceIncrementFromPercent = (incPercent / 100.0) * entryPrice;
                            double ticksPerIncrementPercent = priceIncrementFromPercent / tickSizeGlobal;
                            LogAndPrint($"TRAILING_CONVERSION_DEBUG: Percent mode — tickSize={tickSizeGlobal}, incPercent={incPercent}, priceIncrement={priceIncrementFromPercent:F4} (~{ticksPerIncrementPercent:F2} ticks)");
                            double triggerPercent = uiTriggerType switch
                            {
                                TrailingActivationType.Percent => uiTriggerValue,
                                TrailingActivationType.Ticks => ((uiTriggerValue * position.Instrument.MasterInstrument.TickSize) / entryPrice) * 100.0,
                                TrailingActivationType.Pips => ((uiTriggerValue * GetPipValueForInstrument(position.Instrument)) / entryPrice) * 100.0,
                                TrailingActivationType.Dollars => ((uiTriggerValue / (pointValuePerContract * position.Quantity)) / entryPrice) * 100.0,
                                _ => uiTriggerValue
                            };
                            double currentPercent = Math.Abs((currentPrice - entryPrice) / entryPrice) * 100.0;
                            if (currentPercent < triggerPercent) return tracker.CurrentTrailedStopPrice;
                            double extraPercent = Math.Max(0, currentPercent - triggerPercent);
                            int additional = (int)Math.Floor(extraPercent / incPercent);
                            earnedLevel = 1 + additional;
                            double totalPercentFromEntry = initialPercent + (additional * incPercent);
                            double percentDistance = (totalPercentFromEntry / 100.0) * entryPrice;
                            double candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + percentDistance : entryPrice - percentDistance;
                            if (tracker.CurrentTrailedStopPrice <= 0)
                            {
                                double initPrice = (initialPercent / 100.0) * entryPrice;
                                candidate = position.MarketPosition == MarketPosition.Long ? entryPrice + initPrice : entryPrice - initPrice;
                            }
                            // Cap to one increment
                            double incPrice = priceIncrementFromPercent;
                            if (tracker.CurrentTrailedStopPrice > 0)
                            {
                                if (position.MarketPosition == MarketPosition.Long)
                                    candidate = Math.Min(candidate, tracker.CurrentTrailedStopPrice + incPrice);
                                else
                                    candidate = Math.Max(candidate, tracker.CurrentTrailedStopPrice - incPrice);
                                LogAndPrint($"TRAILING_STEP_CAP_DEBUG: Percent — prev={tracker.CurrentTrailedStopPrice:F4}, incPrice={incPrice:F4}, cappedCandidate={candidate:F4}");
                            }
                            stopPrice = candidate;
                        }
                        break;
                }
                double finalTickSize = position.Instrument.MasterInstrument.TickSize;
                stopPrice = Math.Round(stopPrice / finalTickSize) * finalTickSize;
                // Never retract: clamp against current trailed stop
                if (tracker.CurrentTrailedStopPrice > 0)
                {
                    if (position.MarketPosition == MarketPosition.Long)
                        stopPrice = Math.Max(stopPrice, tracker.CurrentTrailedStopPrice);
                    else
                        stopPrice = Math.Min(stopPrice, tracker.CurrentTrailedStopPrice);
                }
                return stopPrice;
            }
            catch (Exception ex) { LogAndPrint($"ERROR calculating alternative trailing stop price: {ex.Message}"); return tracker.CurrentTrailedStopPrice; }
        }

        private double CalculateTrailingStopPrice(ElasticPositionTracker tracker, Position position)
        {
            double stopPrice = tracker.CurrentTrailedStopPrice;
            double pointValue = position.Instrument.MasterInstrument.PointValue;
            double tickSize = position.Instrument.MasterInstrument.TickSize;
            bool isLong = position.MarketPosition == MarketPosition.Long;
            try
            {
                switch (TrailingType)
                {
                    case ContinuousTrailingType.DollarAmountTrail:
                        double dollarOffset = DollarTrailDistance / pointValue;
                        stopPrice = isLong ? tracker.HighWaterMarkPrice - dollarOffset : tracker.LowWaterMarkPrice + dollarOffset; break;
                    case ContinuousTrailingType.PipTrail:
                        double pipValue = GetPipValueForInstrument(position.Instrument);
                        double pipOffset = PipTrailDistance * pipValue;
                        stopPrice = isLong ? tracker.HighWaterMarkPrice - pipOffset : tracker.LowWaterMarkPrice + pipOffset; break;
                    case ContinuousTrailingType.TickTrail:
                        double tickOffset = TickTrailDistance * tickSize;
                        stopPrice = isLong ? tracker.HighWaterMarkPrice - tickOffset : tracker.LowWaterMarkPrice + tickOffset; break;
                    case ContinuousTrailingType.DEMAAtrTrail:
                        double referencePrice = tracker.PendingBarClose > 0 ? tracker.PendingBarClose : GetCurrentPrice(position.Instrument);
                        var sharedStop = SharedDemaAtrTrailing.CalculateTrailingStop(tracker.QuoteBuffer, DEMA_ATR_Period, DEMA_ATR_Multiplier, isLong, referencePrice);
                        if (sharedStop.HasValue)
                            stopPrice = sharedStop.Value;
                        break;
                    case ContinuousTrailingType.StepTrail:
                        stopPrice = CalculateStepTrailPrice(tracker, position); break;
                }
                if (isLong && stopPrice < tracker.CurrentTrailedStopPrice) stopPrice = tracker.CurrentTrailedStopPrice;
                else if (!isLong && stopPrice > tracker.CurrentTrailedStopPrice) stopPrice = tracker.CurrentTrailedStopPrice;
                return Math.Round(stopPrice / tickSize) * tickSize;
            }
            catch (Exception ex) { LogAndPrint($"Error calculating trailing stop price: {ex.Message}"); return tracker.CurrentTrailedStopPrice; }
        }

        private bool ShouldUpdateTrailingStop(ElasticPositionTracker tracker, Position position)
        {
            try
            {
                TrailingActivationType frequencyType = TrailingIncrementsType;
                double frequencyValue = TrailingIncrementsValue;
                DateTime now = DateTime.Now;
                TimeSpan timeSinceLastUpdate = now - tracker.LastUpdateTime;
                double requiredInterval = 0;

                switch (frequencyType)
                {
                    case TrailingActivationType.Dollars:
                        double freqCurrentPrice = GetCurrentPrice(position.Instrument);
                        double currentPnL = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, freqCurrentPrice);
                        double pnlChange = Math.Abs(currentPnL - tracker.LastReportedProfitLevel);
                        double perPositionThreshold = frequencyValue * Math.Max(1, position.Quantity);
                        bool shouldUpdateDollar = pnlChange >= perPositionThreshold;
                        LogAndPrint($"TRAILING_INCREMENTS_DEBUG: Dollar-based increments - PnL change: ${pnlChange:F2}, Required (pos): ${perPositionThreshold}, Should update: {shouldUpdateDollar}");
                        return shouldUpdateDollar;
                    case TrailingActivationType.Ticks:
                        double currentPrice = GetCurrentPrice(position.Instrument);
                        double priceChange = Math.Abs(currentPrice - tracker.EntryPrice) / position.Instrument.MasterInstrument.TickSize;
                        bool shouldUpdateTicks = priceChange >= frequencyValue;
                        LogAndPrint($"TRAILING_INCREMENTS_DEBUG: Tick-based increments - Tick change: {priceChange:F1}, Required: {frequencyValue}, Should update: {shouldUpdateTicks}");
                        return shouldUpdateTicks;
                    case TrailingActivationType.Pips:
                        currentPrice = GetCurrentPrice(position.Instrument);
                        double pipValue = GetPipValueForInstrument(position.Instrument);
                        double pipChange = Math.Abs(currentPrice - tracker.EntryPrice) / pipValue;
                        bool shouldUpdatePips = pipChange >= frequencyValue;
                        LogAndPrint($"TRAILING_INCREMENTS_DEBUG: Pip-based increments - Pip change: {pipChange:F2}, Required: {frequencyValue}, Should update: {shouldUpdatePips}");
                        return shouldUpdatePips;
                    case TrailingActivationType.Percent:
                        requiredInterval = frequencyValue; break; // treat as seconds
                }
                bool timeElapsed = timeSinceLastUpdate.TotalSeconds >= requiredInterval;
                LogAndPrint($"TRAILING_INCREMENTS_DEBUG: Time-based increments - Elapsed: {timeSinceLastUpdate.TotalSeconds:F1}s, Required: {requiredInterval}s, Should update: {timeElapsed}");
                return timeElapsed;
            }
            catch (Exception ex) { LogAndPrint($"ERROR checking trailing frequency: {ex.Message}"); return true; }
        }

        private double CalculateStepTrailPrice(ElasticPositionTracker tracker, Position position)
        {
            try
            {
                var stepLevels = ParseStepTrailLevels(StepTrailLevelsCsv);
                if (stepLevels.Count == 0) return tracker.CurrentTrailedStopPrice;
                double currentProfit = CalculateUnrealizedPips(position);
                double stopDistance = 0;
                foreach (var level in stepLevels.OrderByDescending(x => x.Key))
                {
                    if (currentProfit >= level.Key) { stopDistance = level.Value; break; }
                }
                if (stopDistance == 0) return tracker.CurrentTrailedStopPrice;
                double pipValue = GetPipValueForInstrument(position.Instrument);
                double stopOffset = stopDistance * pipValue;
                bool isLong = position.MarketPosition == MarketPosition.Long;
                return isLong ? tracker.HighWaterMarkPrice - stopOffset : tracker.LowWaterMarkPrice + stopOffset;
            }
            catch (Exception ex) { LogAndPrint($"Error calculating step trail price: {ex.Message}"); return tracker.CurrentTrailedStopPrice; }
        }

        private Dictionary<double, double> ParseStepTrailLevels(string csvLevels)
        {
            var levels = new Dictionary<double, double>();
            try
            {
                if (string.IsNullOrEmpty(csvLevels)) return levels;
                var pairs = csvLevels.Split(',');
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(':');
                    if (parts.Length == 2 && double.TryParse(parts[0].Trim(), out double profit) && double.TryParse(parts[1].Trim(), out double stopDistance))
                    { levels[profit] = stopDistance; }
                }
            }
            catch (Exception ex) { LogAndPrint($"Error parsing step trail levels: {ex.Message}"); }
            return levels;
        }

        private double GetPipValueForInstrument(Instrument instrument)
        {
            try
            {
                if (instrument.MasterInstrument.InstrumentType == InstrumentType.Forex)
                {
                    if (instrument.FullName.Contains("JPY")) return 0.01; else return 0.0001;
                }
                return instrument.MasterInstrument.TickSize;
            }
            catch (Exception ex) { LogAndPrint($"Error getting pip value for {instrument.FullName}: {ex.Message}"); return instrument.MasterInstrument.TickSize; }
        }

        private double CalculateUnrealizedPips(Position position)
        {
            try
            {
                if (position.Quantity == 0) return 0;
                double currentPrice = GetCurrentMarketPrice(position.Instrument);
                double entryPrice = position.AveragePrice;
                double pipValue = GetPipValueForInstrument(position.Instrument);
                bool isLong = position.MarketPosition == MarketPosition.Long;
                double priceDiff = isLong ? currentPrice - entryPrice : entryPrice - currentPrice;
                return priceDiff / pipValue;
            }
            catch (Exception ex) { LogAndPrint($"Error calculating unrealized pips: {ex.Message}"); return 0; }
        }

        private double GetCurrentMarketPrice(Instrument instrument)
        {
            try { return instrument.MarketData.Last?.Price ?? instrument.MarketData.Ask?.Price ?? 0; }
            catch (Exception ex) { LogAndPrint($"Error getting market price for {instrument.FullName}: {ex.Message}"); return 0; }
        }
        private double GetCurrentPrice(Instrument instrument) => GetCurrentMarketPrice(instrument);

        private void SubmitTrailingStopOrder(ElasticPositionTracker tracker, Position position, double stopPrice)
        {
            try
            {
                var account = parentManager?.MonitoredAccount;
                if (account == null)
                { LogAndPrint($"TRAILING_ORDER_ERROR: No monitored account to submit stop for {tracker.BaseId}"); return; }
                double tickSize = position.Instrument.MasterInstrument.TickSize;
                stopPrice = Math.Round(stopPrice / tickSize) * tickSize;
                OrderAction action = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                string orderName = $"MSM_ALT_TRAIL_STOP_{tracker.BaseId}";
                var order = account.CreateOrder(
                    position.Instrument,
                    action,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    position.Quantity,
                    0,
                    stopPrice,
                    string.Empty,
                    orderName,
                    default(DateTime),
                    null);
                account.Submit(new[] { order });
                tracker.ManagedStopOrder = order;
                LogAndPrint($"TRAILING_ORDER: Submitted trailing stop for {tracker.BaseId} at {stopPrice:F2}");
            }
            catch (Exception ex)
            {
                LogAndPrint($"ERROR submitting trailing stop for {tracker.BaseId}: {ex.Message}");
            }
        }

        private void UpdateTrailingStopOrder(ElasticPositionTracker tracker, double newStopPrice)
        {
            try
            {
                var account = parentManager?.MonitoredAccount;
                if (account == null || tracker.ManagedStopOrder == null) return;
                double tickSize = tracker.ManagedStopOrder.Instrument.MasterInstrument.TickSize;
                newStopPrice = Math.Round(newStopPrice / tickSize) * tickSize;
                // Cancel and replace pattern (consistent with Traditional trailing implementation)
                account.Cancel(new[] { tracker.ManagedStopOrder });
                System.Threading.Thread.Sleep(50);
                OrderAction action = tracker.ManagedStopOrder.OrderAction; // same side as initial
                string orderName = tracker.ManagedStopOrder.Name;
                var newOrder = account.CreateOrder(
                    tracker.ManagedStopOrder.Instrument,
                    action,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    tracker.ManagedStopOrder.Quantity,
                    0,
                    newStopPrice,
                    string.Empty,
                    orderName,
                    default(DateTime),
                    null);
                account.Submit(new[] { newOrder });
                tracker.ManagedStopOrder = newOrder;
                LogAndPrint($"TRAILING_ORDER: Modified trailing stop for {tracker.BaseId} to {newStopPrice:F2}");
            }
            catch (Exception ex)
            {
                LogAndPrint($"ERROR modifying trailing stop for {tracker.BaseId}: {ex.Message}");
            }
        }

        private void OnBarsUpdate(object sender, BarsUpdateEventArgs e)
        {
            try
            {
                BarsRequest barsRequest = sender as BarsRequest; if (barsRequest == null) return;
                string instrumentKey = barsRequest.Bars.Instrument.FullName;
                var positionTracker = elasticPositions.Values.FirstOrDefault(p => p.InstrumentName == instrumentKey);
                if (positionTracker == null) return;
                UpdateQuoteBuffer(barsRequest, positionTracker);
                CalculateIndicators(positionTracker);
                positionTracker.LastBarTimeProcessed = DateTime.Now;
            }
            catch (Exception ex) { LogAndPrint($"Error in OnBarsUpdate: {ex.Message}"); }
        }

        private void UpdateQuoteBuffer(BarsRequest barsRequest, ElasticPositionTracker tracker)
        {
            try
            {
                var bars = barsRequest.Bars; int count = bars.Count; tracker.QuoteBuffer.Clear();
                int startIndex = Math.Max(0, count - (AtrPeriod + DEMA_ATR_Period + 20));
                for (int i = startIndex; i < count; i++)
                {
                    tracker.QuoteBuffer.Add(new Quote
                    {
                        Date = bars.GetTime(i),
                        Open = bars.GetOpen(i),
                        High = bars.GetHigh(i),
                        Low = bars.GetLow(i),
                        Close = bars.GetClose(i),
                        Volume = bars.GetVolume(i)
                    });
                }
            }
            catch (Exception ex) { LogAndPrint($"Error updating quote buffer: {ex.Message}"); }
        }

        private void CalculateIndicators(ElasticPositionTracker tracker)
        {
            try
            {
                int minBarsForAtr = Math.Max(AtrPeriod / 2 + 3, Math.Min(AtrPeriod + 1, 10));
                int minBarsForDema = Math.Max(DEMA_ATR_Period / 2 + 3, Math.Min(DEMA_ATR_Period, 10));
                int minQuotesRequired = Math.Max(minBarsForAtr, minBarsForDema);
                if (!IndicatorCalculator.ValidateQuoteData(tracker.QuoteBuffer, minQuotesRequired)) return;

                double? atrValue = IndicatorCalculator.CalculateAtr(tracker.QuoteBuffer, AtrPeriod);
                if (atrValue.HasValue) tracker.CurrentAtrValue = atrValue.Value;
                double? demaValue = IndicatorCalculator.CalculateDema(tracker.QuoteBuffer, DEMA_ATR_Period);
                if (demaValue.HasValue) tracker.CurrentDemaValue = demaValue.Value;
                LogAndPrint($"Indicators updated for {tracker.InstrumentName}: ATR={tracker.CurrentAtrValue:F4}, DEMA={tracker.CurrentDemaValue:F4}");
            }
            catch (Exception ex) { LogAndPrint($"Error calculating indicators: {ex.Message}"); }
        }

        public void HandleTrailingStopOrderUpdate(Order order)
        {
            try
            {
                var tracker = elasticPositions.Values.FirstOrDefault(t => t.ManagedStopOrder != null && t.ManagedStopOrder.OrderId == order.OrderId);
                if (tracker != null) OnTrailingStopOrderUpdate(order, tracker);
            }
            catch (Exception ex) { LogAndPrint($"Error handling trailing stop order update: {ex.Message}"); }
        }

        private void OnTrailingStopOrderUpdate(Order order, ElasticPositionTracker tracker)
        {
            try
            {
                if (order == null || tracker == null) return;
                LogAndPrint($"Trailing stop order update for {tracker.BaseId}: {order.OrderState}");
                switch (order.OrderState)
                {
                    case OrderState.Filled:
                        LogAndPrint($"Trailing stop order filled for {tracker.BaseId} at {order.AverageFillPrice:F2}");
                        tracker.IsTrailingActive = false; tracker.ManagedStopOrder = null; break;
                    case OrderState.Cancelled:
                    case OrderState.Rejected:
                        LogAndPrint($"Trailing stop order {order.OrderState} for {tracker.BaseId}"); tracker.ManagedStopOrder = null; break;
                    case OrderState.CancelPending:
                    case OrderState.ChangePending: break;
                    case OrderState.Working:
                    case OrderState.Accepted:
                        LogAndPrint($"Trailing stop order active for {tracker.BaseId} at {order.StopPrice:F2}"); break;
                }
            }
            catch (Exception ex) { LogAndPrint($"Error handling trailing stop order update: {ex.Message}"); }
        }

        public void AddElasticPositionTrackingFromExecution(string baseId, Execution execution)
        {
            LogAndPrint($"ELASTIC_ADD_DEBUG: AddElasticPositionTrackingFromExecution called - BaseId: {baseId}, EnableElasticHedging: {EnableElasticHedging}");
            if (!EnableElasticHedging) { LogAndPrint($"ELASTIC_ADD_DEBUG: Elastic hedging disabled, skipping tracking for {baseId}"); return; }
            int estimatedQuantity = execution != null && execution.Order != null ? (int)Math.Abs(execution.Order.Quantity) : Math.Max(1, Math.Abs((int)execution.Quantity));
            var tracker = new ElasticPositionTracker
            {
                BaseId = baseId,
                EntryPrice = execution.Price,
                LastReportedProfitLevel = 0.0,
                ProfitUpdatesSent = 0,
                LastUpdateTime = DateTime.Now,
                LastReportedIncrement = 0,
                InstrumentFullName = execution.Instrument.FullName,
                InstrumentName = execution.Instrument.FullName,
                MarketPosition = execution.MarketPosition,
                IsTrailingActive = false,
                CurrentTrailedStopPrice = 0,
                HighWaterMarkPrice = execution.Price,
                LowWaterMarkPrice = execution.Price,
                PendingBarStart = DateTime.UtcNow,
                PendingBarOpen = execution.Price,
                PendingBarHigh = execution.Price,
                PendingBarLow = execution.Price,
                PendingBarClose = execution.Price,
                IsSLTPLogicCompleteForEntry = false,
                OriginalQuantity = estimatedQuantity,
                RemainingQuantity = estimatedQuantity
            };
            elasticPositions[baseId] = tracker;
            LogAndPrint($"ELASTIC_ADD_DEBUG: ✅ Added elastic tracking for {baseId} using execution data. Total tracked: {elasticPositions.Count}");
        }

        private void MonitorTraditionalTrailingStops(Account monitoredAccount)
        {
            try
            {
                if (monitoredAccount == null) return;
                LogAndPrint($"TRADITIONAL_TRAILING_DEBUG: Monitoring {traditionalTrailingStops.Count} traditional stops");
                foreach (var position in monitoredAccount.Positions.Where(p => p.MarketPosition != MarketPosition.Flat))
                {
                    string baseId = GetBaseIdFromPosition(position);
                    if (string.IsNullOrEmpty(baseId)) { LogAndPrint($"TRADITIONAL_TRAILING_DEBUG: No baseId found for position {position.Instrument.FullName}"); continue; }
                    if (!traditionalTrailingStops.ContainsKey(baseId)) CheckAndInitializeTraditionalTrailing(baseId, position, monitoredAccount);
                }
                var stopsToUpdate = traditionalTrailingStops.Values.ToList();
                foreach (var stop in stopsToUpdate) UpdateTraditionalTrailingStop(stop, monitoredAccount);
                CleanupClosedPositionStops(monitoredAccount);
            }
            catch (Exception ex) { LogAndPrint($"ERROR in MonitorTraditionalTrailingStops: {ex.Message}"); }
        }

        private void CheckAndInitializeTraditionalTrailing(string baseId, Position position, Account account)
        {
            try
            {
                double currentPrice = GetCurrentPrice(position.Instrument); if (currentPrice == 0) return;
                double unrealizedPnL = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
                double pointsFromEntry = Math.Abs(currentPrice - position.AveragePrice);
                double percentFromEntry = (pointsFromEntry / position.AveragePrice) * 100;

                bool shouldActivate = false;
                switch (TrailingTriggerType)
                {
                    case TrailingActivationType.Dollars: shouldActivate = unrealizedPnL >= TrailingTriggerValue; break;
                    case TrailingActivationType.Percent: shouldActivate = percentFromEntry >= TrailingTriggerValue; break;
                    case TrailingActivationType.Ticks: double ticks = pointsFromEntry / position.Instrument.MasterInstrument.TickSize; shouldActivate = ticks >= TrailingTriggerValue; break;
                    case TrailingActivationType.Pips: double pips = pointsFromEntry / GetPipSize(position.Instrument); shouldActivate = pips >= TrailingTriggerValue; break;
                }
                if (shouldActivate)
                {
                    LogAndPrint($"TRADITIONAL_TRAILING: Activating for {baseId} at ${unrealizedPnL:F2} profit");
                    SubmitInitialTraditionalStop(baseId, position, account, currentPrice);
                }
            }
            catch (Exception ex) { LogAndPrint($"ERROR in CheckAndInitializeTraditionalTrailing: {ex.Message}"); }
        }

        private void SubmitInitialTraditionalStop(string baseId, Position position, Account account, double currentPrice)
        {
            try
            {
                double stopPrice = CalculateTraditionalStopPrice(position, currentPrice, true);
                if (stopPrice <= 0) { LogAndPrint($"TRADITIONAL_TRAILING: Invalid stop price calculated for {baseId}"); return; }
                OrderAction orderAction = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                string orderName = $"MSM_TRAIL_STOP_{baseId}";
                LogAndPrint($"TRADITIONAL_TRAILING: Submitting initial stop for {baseId} at {stopPrice:F2}");
                Order stopOrder = account.CreateOrder(
                    position.Instrument,
                    orderAction,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    position.Quantity,
                    0,
                    stopPrice,
                    string.Empty,
                    orderName,
                    default(DateTime),
                    null);
                account.Submit(new[] { stopOrder });
                var tracker = new TraditionalTrailingStop
                {
                    BaseId = baseId,
                    CurrentStopOrder = stopOrder,
                    LastStopPrice = stopPrice,
                    LastModificationTime = DateTime.Now,
                    ModificationCount = 0,
                    TrackedPosition = position,
                    EntryPrice = position.AveragePrice,
                    IsActive = true,
                    ActivationTime = DateTime.Now,
                    ActivationPrice = currentPrice,
                    MaxProfit = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice),
                    InitialStopDistance = Math.Abs(stopPrice - position.AveragePrice),
                    LastKnownOrderState = OrderState.Submitted,
                    LastOrderStateUpdate = DateTime.Now,
                    IsPendingModification = false,
                    FailedModificationAttempts = 0
                };
                traditionalTrailingStops[baseId] = tracker;
                LogAndPrint($"TRADITIONAL_TRAILING: Initial stop submitted for {baseId}");
            }
            catch (Exception ex) { LogAndPrint($"ERROR in SubmitInitialTraditionalStop: {ex.Message}"); }
        }

        private void UpdateTraditionalTrailingStop(TraditionalTrailingStop stop, Account account)
        {
            try
            {
                if (!stop.IsActive || stop.IsPendingModification) return;
                var position = account.Positions.FirstOrDefault(p => p.Instrument.FullName == stop.TrackedPosition.Instrument.FullName && p.MarketPosition != MarketPosition.Flat);
                if (position == null)
                { LogAndPrint($"TRADITIONAL_TRAILING: Position closed for {stop.BaseId}"); traditionalTrailingStops.Remove(stop.BaseId); return; }

                if (stop.CurrentStopOrder != null)
                {
                    if (stop.CurrentStopOrder.OrderState == OrderState.Filled)
                    { LogAndPrint($"TRADITIONAL_TRAILING: Stop filled for {stop.BaseId}"); traditionalTrailingStops.Remove(stop.BaseId); return; }
                    if (stop.CurrentStopOrder.OrderState == OrderState.Cancelled || stop.CurrentStopOrder.OrderState == OrderState.Rejected)
                    {
                        LogAndPrint($"TRADITIONAL_TRAILING: Stop order {stop.CurrentStopOrder.OrderState} for {stop.BaseId}");
                        stop.CurrentStopOrder = null; stop.FailedModificationAttempts++;
                        if (stop.FailedModificationAttempts < 3)
                        { double retryCurrentPrice = GetCurrentPrice(position.Instrument); SubmitInitialTraditionalStop(stop.BaseId, position, account, retryCurrentPrice); }
                        else { LogAndPrint($"TRADITIONAL_TRAILING: Max retries reached for {stop.BaseId}, removing"); traditionalTrailingStops.Remove(stop.BaseId); }
                        return;
                    }
                }

                double currentPrice = GetCurrentPrice(position.Instrument); if (currentPrice == 0) return;
                double newStopPrice = CalculateTraditionalStopPrice(position, currentPrice, false);
                double currentProfit = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
                if (currentProfit > stop.MaxProfit) stop.MaxProfit = currentProfit;

                bool shouldUpdate = false;
                if (position.MarketPosition == MarketPosition.Long)
                    shouldUpdate = newStopPrice > stop.LastStopPrice && Math.Abs(newStopPrice - stop.LastStopPrice) >= position.Instrument.MasterInstrument.TickSize;
                else if (position.MarketPosition == MarketPosition.Short)
                    shouldUpdate = newStopPrice < stop.LastStopPrice && Math.Abs(newStopPrice - stop.LastStopPrice) >= position.Instrument.MasterInstrument.TickSize;

                TimeSpan timeSinceLastUpdate = DateTime.Now - stop.LastModificationTime;
                if (shouldUpdate && timeSinceLastUpdate.TotalSeconds >= 1)
                { LogAndPrint($"TRADITIONAL_TRAILING: Updating stop for {stop.BaseId} from {stop.LastStopPrice:F2} to {newStopPrice:F2}"); ModifyTraditionalStop(stop, newStopPrice, account); }
            }
            catch (Exception ex) { LogAndPrint($"ERROR in UpdateTraditionalTrailingStop: {ex.Message}"); }
        }

        private void ModifyTraditionalStop(TraditionalTrailingStop stop, double newStopPrice, Account account)
        {
            try
            {
                if (stop.CurrentStopOrder == null) return;
                stop.IsPendingModification = true;
                account.Cancel(new[] { stop.CurrentStopOrder });
                System.Threading.Thread.Sleep(100);
                OrderAction orderAction = stop.TrackedPosition.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                string orderName = $"MSM_TRAIL_STOP_{stop.BaseId}";
                Order newStopOrder = account.CreateOrder(
                    stop.TrackedPosition.Instrument,
                    orderAction,
                    OrderType.StopMarket,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    stop.TrackedPosition.Quantity,
                    0,
                    newStopPrice,
                    string.Empty,
                    orderName,
                    default(DateTime),
                    null);
                account.Submit(new[] { newStopOrder });
                stop.CurrentStopOrder = newStopOrder;
                stop.LastStopPrice = newStopPrice;
                stop.LastModificationTime = DateTime.Now;
                stop.ModificationCount++;
                stop.IsPendingModification = false;
                stop.LastKnownOrderState = OrderState.Submitted;
                LogAndPrint($"TRADITIONAL_TRAILING: Stop modified for {stop.BaseId} - Count: {stop.ModificationCount}");
                Task.Run(() => SendTrailingStopUpdate(stop.BaseId, newStopPrice, GetCurrentPrice(stop.TrackedPosition.Instrument)));
            }
            catch (Exception ex) { LogAndPrint($"ERROR in ModifyTraditionalStop: {ex.Message}"); stop.IsPendingModification = false; stop.FailedModificationAttempts++; }
        }

        private double CalculateTraditionalStopPrice(Position position, double currentPrice, bool isInitial)
        {
            try
            {
                double unrealizedPnL = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, currentPrice);
                double entryPrice = position.AveragePrice;
                double pointValue = position.Instrument.MasterInstrument.PointValue * position.Quantity;
                double lockedProfit = 0;
                switch (TrailingStopType)
                {
                    case TrailingActivationType.Dollars:
                        double initialLockedProfit = TrailingStopValue;
                        double trailingIncrement = TrailingIncrementsValue;
                        double triggerLevel = TrailingTriggerValue;
                        double extraProfit = Math.Max(0, unrealizedPnL - triggerLevel);
                        double incrementsEarned = Math.Floor(extraProfit / trailingIncrement);
                        lockedProfit = initialLockedProfit + (incrementsEarned * trailingIncrement); break;
                    case TrailingActivationType.Percent:
                        double percentProfit = (unrealizedPnL / pointValue) * 100;
                        double percentTrigger = TrailingTriggerValue;
                        double percentInitial = TrailingStopValue;
                        double percentIncrement = TrailingIncrementsValue;
                        double percentExtra = Math.Max(0, percentProfit - percentTrigger);
                        double percentIncrements = Math.Floor(percentExtra / percentIncrement);
                        double totalPercent = percentInitial + (percentIncrements * percentIncrement);
                        lockedProfit = (totalPercent / 100) * pointValue; break;
                }
                double lockedProfitInPoints = lockedProfit / (position.Instrument.MasterInstrument.PointValue * position.Quantity);
                double stopPrice = position.MarketPosition == MarketPosition.Long ? entryPrice + lockedProfitInPoints : entryPrice - lockedProfitInPoints;
                if (isInitial)
                {
                    if (position.MarketPosition == MarketPosition.Long) stopPrice = Math.Max(stopPrice, entryPrice);
                    else stopPrice = Math.Min(stopPrice, entryPrice);
                }
                return stopPrice;
            }
            catch (Exception ex) { LogAndPrint($"ERROR in CalculateTraditionalStopPrice: {ex.Message}"); return 0; }
        }

        private void CleanupClosedPositionStops(Account account)
        {
            try
            {
                var keysToRemove = new List<string>();
                foreach (var kvp in traditionalTrailingStops)
                {
                    var position = account.Positions.FirstOrDefault(p => p.Instrument.FullName == kvp.Value.TrackedPosition.Instrument.FullName && p.MarketPosition != MarketPosition.Flat);
                    if (position == null)
                    {
                        if (kvp.Value.CurrentStopOrder != null && (kvp.Value.CurrentStopOrder.OrderState == OrderState.Working || kvp.Value.CurrentStopOrder.OrderState == OrderState.Accepted))
                        {
                            try { account.Cancel(new[] { kvp.Value.CurrentStopOrder }); LogAndPrint($"TRADITIONAL_TRAILING: Cancelled orphaned stop for {kvp.Key}"); }
                            catch (Exception ex) { LogAndPrint($"ERROR cancelling orphaned stop: {ex.Message}"); }
                        }
                        keysToRemove.Add(kvp.Key);
                    }
                }
                foreach (var key in keysToRemove) { traditionalTrailingStops.Remove(key); LogAndPrint($"TRADITIONAL_TRAILING: Cleaned up tracking for {key}"); }
            }
            catch (Exception ex) { LogAndPrint($"ERROR in CleanupClosedPositionStops: {ex.Message}"); }
        }

        private string GetBaseIdFromPosition(Position position)
        {
            var elasticPosition = elasticPositions.Values.FirstOrDefault(ep => ep.InstrumentFullName == position.Instrument.FullName && ep.MarketPosition == position.MarketPosition);
            if (elasticPosition != null) return elasticPosition.BaseId;
            return null;
        }

        private double GetPipSize(Instrument instrument)
        {
            try
            {
                string instrumentName = instrument.FullName.ToUpper();
                if (instrumentName.Contains("JPY")) return 0.01; return 0.0001;
            }
            catch { return 0.0001; }
        }

    private async Task SendTrailingStopUpdate(string baseId, double newStopPrice, double currentPrice)
        {
            var updateData = new Dictionary<string, object>
            {
        { "event_type", "TRAILING_STOP_UPDATE" },
        { "base_id", baseId },
        { "new_stop_price", newStopPrice },
        { "trailing_type", UseAlternativeTrailing ? "alternative" : "traditional" },
        { "current_price", currentPrice },
        { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") }
            };
            LogAndPrint($"Sending trailing stop update for {baseId}: Stop=${newStopPrice:F2}, Current=${currentPrice:F2}");
            try
            {
                var json = SimpleJson.SerializeObject(updateData);
                bool success = TradingGrpcClient.SubmitTrailingUpdate(json);
                if (success) LogAndPrint($"Trailing stop update sent via gRPC for {baseId}");
                else LogAndPrint($"Failed to send trailing stop update via gRPC: {TradingGrpcClient.LastError}");
            }
            catch (Exception grpcEx) { LogAndPrint($"ERROR sending trailing stop update via gRPC: {grpcEx.Message}"); }
        }
        #endregion
    }
}
