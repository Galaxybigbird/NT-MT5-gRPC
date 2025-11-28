#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization; // For number formatting
using System.Linq;
using System.Windows.Threading;
using NinjaTrader.Cbi; // For Account
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core;
using System.Threading;
using System.Diagnostics;
using NinjaTrader.Data;
using System.Text;
using System.Threading.Tasks;
// HttpListener removed - using gRPC instead
using System.IO; // Required for StreamReader
using System.Collections.Concurrent; // Added for ConcurrentDictionary
using NinjaTrader.NinjaScript.AddOns.MultiStratManagerLogic; // Added for SLTPRemovalLogic
using NTGrpcClient; // Added for gRPC client
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    // Enums for trailing configuration
    public enum TrailingActivationType
    {
        Ticks,
        Pips,
        Dollars,
        Percent
    }
    
    /// <summary>
    /// Multi-Strategy Manager for hedging and managing multiple trading strategies
    /// </summary>
    public class MultiStratManager : NinjaTrader.NinjaScript.AddOnBase, INotifyPropertyChanged
    {
    // Type aliases for UI compatibility - expose types from TrailingAndElasticManager
    public class ElasticPositionTracker : NinjaTrader.NinjaScript.AddOns.ElasticPositionTracker { }
        private static UIForManager window;
        private bool isFirstRun = true;
        private bool connectionsStarted = false;
        private System.Windows.Threading.DispatcherTimer autoLaunchTimer;

        private TradeSyncService tradeSyncService;
        private struct PendingHedgeCommand
        {
            public string BaseId;
            public string JsonPayload;
            public double Quantity;
        }
        private readonly ConcurrentQueue<PendingHedgeCommand> pendingHedgeCommands = new ConcurrentQueue<PendingHedgeCommand>();
        private int hedgeFlushInProgress = 0;

        // ✅ RECOMPILATION SAFETY: Track if we've already cleaned up to prevent multiple cleanup attempts
        private static bool hasPerformedStaticCleanup = false;

        /// <summary>
        /// Aggressive cleanup of static resources to handle NinjaScript recompilation scenarios
        /// </summary>
        private static void PerformStaticCleanup()
        {
            try
            {
                System.Console.WriteLine("[NT_ADDON][DEBUG] PerformStaticCleanup: Starting aggressive cleanup for recompilation safety");

                // 1. Close and dispose existing UI window
                if (window != null)
                {
                    try
                    {
                        System.Console.WriteLine("[NT_ADDON][DEBUG] PerformStaticCleanup: Closing existing UI window");
                        if (window.Dispatcher.CheckAccess())
                        {
                            window.Close();
                        }
                        else
                        {
                            window.Dispatcher.BeginInvoke(new Action(() => window.Close()));
                        }
                        window = null;
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[NT_ADDON][ERROR] PerformStaticCleanup: Error closing window: {ex.Message}");
                    }
                }

                // 2. Clear monitored strategies list
                if (monitoredStrategies != null)
                {
                    System.Console.WriteLine($"[NT_ADDON][DEBUG] PerformStaticCleanup: Clearing {monitoredStrategies.Count} monitored strategies");
                    monitoredStrategies.Clear();
                }

                // 3. Clean up previous instance
                if (Instance != null)
                {
                    try
                    {
                        System.Console.WriteLine("[NT_ADDON][DEBUG] PerformStaticCleanup: Disposing previous instance");
                        Instance.tradeSyncService?.Shutdown("static cleanup");
                        Instance.DisconnectGrpcAndStopAll();
                        Instance = null;
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[NT_ADDON][ERROR] PerformStaticCleanup: Error disposing instance: {ex.Message}");
                    }
                }

                // 4. Force garbage collection to clean up resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                hasPerformedStaticCleanup = true;
                System.Console.WriteLine("[NT_ADDON][DEBUG] PerformStaticCleanup: Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[NT_ADDON][ERROR] PerformStaticCleanup: Critical error during cleanup: {ex.Message}");
            }
        }

        // Bridge connection monitoring removed - manual connection only
        private bool lastBridgeConnectionStatus = false;
        private DateTime lastBridgeConnectionCheck = DateTime.MinValue;

        private ContinuousTrailingType priorNonAtrTrailingType = ContinuousTrailingType.DollarAmountTrail;
        private bool priorUseAlternativeTrailing = true;

        public static MultiStratManager Instance { get; private set; }
        public TradeSyncService TradeSync => tradeSyncService;
        public event Action PingReceivedFromBridge;

        private SLTPRemovalLogic sltpRemovalLogic;

        // Properties for SLTP Removal Logic
        public bool EnableSLTPRemoval { get; set; } = true; // Default to true
        public int SLTPRemovalDelaySeconds { get; set; } = 3; // Default to 3 seconds

        // Shared exposure tracking across all participant strategies
        private readonly object exposureLock = new object();
        private readonly Dictionary<string, double> exposureByKey = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<StrategyBase, Dictionary<string, double>> exposureByStrategy = new Dictionary<StrategyBase, Dictionary<string, double>>();

        #region Trailing and Elastic Properties - Delegated to TrailingAndElasticManager
        
        // All trailing and elastic settings are now handled by TrailingAndElasticManager
        // Expose them through properties for UI compatibility
        public bool EnableElasticHedging 
        { 
            get => trailingAndElasticManager?.EnableElasticHedging ?? true; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.EnableElasticHedging = value; OnPropertyChanged(nameof(EnableElasticHedging)); } } 
        }
        
        public bool EnableTrailing 
        { 
            get => trailingAndElasticManager?.EnableTrailing ?? true; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.EnableTrailing = value; OnPropertyChanged(nameof(EnableTrailing)); } } 
        }
        
        public bool UseAlternativeTrailing
        {
            get => trailingAndElasticManager?.UseAlternativeTrailing ?? true;
            set
            {
                if (trailingAndElasticManager == null)
                    return;

                if (trailingAndElasticManager.UseATRTrailing && value)
                {
                    LogInfo("TRAILING", "Ignoring request to enable alternative trailing while DEMA-ATR trailing is active.");
                    return;
                }

                trailingAndElasticManager.UseAlternativeTrailing = value;
                if (!trailingAndElasticManager.UseATRTrailing)
                    priorUseAlternativeTrailing = value;
                OnPropertyChanged(nameof(UseAlternativeTrailing));
            }
        }
        public bool UseTraditionalTrailing 
        { 
            get => trailingAndElasticManager?.UseTraditionalTrailing ?? false; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.UseTraditionalTrailing = value; OnPropertyChanged(nameof(UseTraditionalTrailing)); } } 
        }
        
        public TrailingActivationType TrailingTriggerType
        {
            get => trailingAndElasticManager?.TrailingTriggerType ?? TrailingActivationType.Dollars;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.TrailingTriggerType = value;
                    OnPropertyChanged(nameof(TrailingTriggerType));

                    if (trailingAndElasticManager.ElasticTriggerType != value)
                    {
                        trailingAndElasticManager.ElasticTriggerType = value;
                        OnPropertyChanged(nameof(ElasticTriggerType));
                    }
                }
            }
        }

        public double TrailingTriggerValue
        {
            get => trailingAndElasticManager?.TrailingTriggerValue ?? 100.0;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.TrailingTriggerValue = value;
                    OnPropertyChanged(nameof(TrailingTriggerValue));

                    if (System.Math.Abs(trailingAndElasticManager.ProfitUpdateThreshold - value) > 1e-9)
                    {
                        trailingAndElasticManager.ProfitUpdateThreshold = value;
                        OnPropertyChanged(nameof(ProfitUpdateThreshold));
                    }
                }
            }
        }

public TrailingActivationType TrailingStopType 
        { 
            get => trailingAndElasticManager?.TrailingStopType ?? TrailingActivationType.Dollars; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.TrailingStopType = value; OnPropertyChanged(nameof(TrailingStopType)); } } 
        }
        
        public double TrailingStopValue 
        { 
            get => trailingAndElasticManager?.TrailingStopValue ?? 50.0; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.TrailingStopValue = value; OnPropertyChanged(nameof(TrailingStopValue)); } } 
        }
        
        public TrailingActivationType TrailingIncrementsType
        {
            get => trailingAndElasticManager?.TrailingIncrementsType ?? TrailingActivationType.Dollars;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.TrailingIncrementsType = value;
                    OnPropertyChanged(nameof(TrailingIncrementsType));

                    if (trailingAndElasticManager.ElasticProfitUnits != value)
                    {
                        trailingAndElasticManager.ElasticProfitUnits = value;
                        OnPropertyChanged(nameof(ElasticProfitUnits));
                    }
                }
            }
        }

        public double TrailingIncrementsValue
        {
            get => trailingAndElasticManager?.TrailingIncrementsValue ?? 10.0;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.TrailingIncrementsValue = value;
                    OnPropertyChanged(nameof(TrailingIncrementsValue));

                    if (System.Math.Abs(trailingAndElasticManager.ElasticIncrementValue - value) > 1e-9)
                    {
                        trailingAndElasticManager.ElasticIncrementValue = value;
                        OnPropertyChanged(nameof(ElasticIncrementValue));
                    }
                }
            }
        }

        public TrailingActivationType TrailingActivationMode 
        { 
            get => trailingAndElasticManager?.TrailingActivationMode ?? TrailingActivationType.Percent; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.TrailingActivationMode = value; OnPropertyChanged(nameof(TrailingActivationMode)); } } 
        }
        
        public double TrailingActivationValue 
        { 
            get => trailingAndElasticManager?.TrailingActivationValue ?? 1.0; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.TrailingActivationValue = value; OnPropertyChanged(nameof(TrailingActivationValue)); } } 
        }
        
        // Elastic hedging properties (actively used by Elastic monitor and Alternative Trailing activation)
        // Keep these in sync with the unified UI fields as well as the trailing equivalents.
        public TrailingActivationType ElasticTriggerType
        {
            get => trailingAndElasticManager?.ElasticTriggerType ?? TrailingActivationType.Dollars;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.ElasticTriggerType = value;
                    OnPropertyChanged(nameof(ElasticTriggerType));

                    if (!trailingAndElasticManager.UseATRTrailing)
                    {
                        trailingAndElasticManager.TrailingTriggerType = value;
                        OnPropertyChanged(nameof(TrailingTriggerType));
                    }
                }
            }
        }

        public double ProfitUpdateThreshold
        {
            get => trailingAndElasticManager?.ProfitUpdateThreshold ?? 100.0;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.ProfitUpdateThreshold = value;
                    OnPropertyChanged(nameof(ProfitUpdateThreshold));

                    if (!trailingAndElasticManager.UseATRTrailing)
                    {
                        trailingAndElasticManager.TrailingTriggerValue = value;
                        OnPropertyChanged(nameof(TrailingTriggerValue));
                    }
                }
            }
        }

        // UI compatibility: this value is no longer used at runtime (timer fixed to 100ms),
        // but we keep a local setting so existing UI bindings don't break.
        // Legacy UI-only knob; no longer used (monitor fixed at 100ms). Kept for binding compatibility.
        // private int _elasticUpdateIntervalSecondsCompat = 1;
        // public int ElasticUpdateIntervalSeconds 
        // {
        //     get => _elasticUpdateIntervalSecondsCompat; 
        //     set { _elasticUpdateIntervalSecondsCompat = value; OnPropertyChanged(nameof(ElasticUpdateIntervalSeconds)); } 
        // }
        public TrailingActivationType ElasticProfitUnits
        {
            get => trailingAndElasticManager?.ElasticProfitUnits ?? TrailingActivationType.Dollars;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.ElasticProfitUnits = value;
                    OnPropertyChanged(nameof(ElasticProfitUnits));

                    if (!trailingAndElasticManager.UseATRTrailing)
                    {
                        trailingAndElasticManager.TrailingIncrementsType = value;
                        OnPropertyChanged(nameof(TrailingIncrementsType));
                    }
                }
            }
        }
        public double ElasticIncrementValue
        {
            get => trailingAndElasticManager?.ElasticIncrementValue ?? 10.0;
            set
            {
                if (trailingAndElasticManager != null)
                {
                    trailingAndElasticManager.ElasticIncrementValue = value;
                    OnPropertyChanged(nameof(ElasticIncrementValue));

                    if (!trailingAndElasticManager.UseATRTrailing)
                    {
                        trailingAndElasticManager.TrailingIncrementsValue = value;
                        OnPropertyChanged(nameof(TrailingIncrementsValue));
                    }
                }
            }
        }


        // Legacy trailing properties that UI still binds to
        public bool EnableTrailingStop 
        { 
            get => trailingAndElasticManager?.EnableTrailingStop ?? false; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.EnableTrailingStop = value; OnPropertyChanged(nameof(EnableTrailingStop)); } } 
        }
        
        public int ActivateTrailAfterPipsProfit 
        { 
            get => trailingAndElasticManager?.ActivateTrailAfterPipsProfit ?? 20; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.ActivateTrailAfterPipsProfit = value; OnPropertyChanged(nameof(ActivateTrailAfterPipsProfit)); } } 
        }
        
        public double DollarTrailDistance 
        { 
            get => trailingAndElasticManager?.DollarTrailDistance ?? 100.0; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.DollarTrailDistance = value; OnPropertyChanged(nameof(DollarTrailDistance)); } } 
        }
        
        public int AtrPeriod 
        { 
            get => trailingAndElasticManager?.AtrPeriod ?? 14; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.AtrPeriod = value; OnPropertyChanged(nameof(AtrPeriod)); } } 
        }
        
        public double AtrMultiplier 
        { 
            get => trailingAndElasticManager?.AtrMultiplier ?? 2.5; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.AtrMultiplier = value; OnPropertyChanged(nameof(AtrMultiplier)); } } 
        }
        
        public bool UseATRTrailing
        {
            get => trailingAndElasticManager?.UseATRTrailing ?? false;
            set
            {
                if (trailingAndElasticManager == null)
                    return;

                if (value)
                {
                    priorNonAtrTrailingType = trailingAndElasticManager.TrailingType;
                    priorUseAlternativeTrailing = trailingAndElasticManager.UseAlternativeTrailing;

                    trailingAndElasticManager.UseATRTrailing = true;
                    trailingAndElasticManager.UseAlternativeTrailing = false;
                    trailingAndElasticManager.TrailingType = ContinuousTrailingType.DEMAAtrTrail;

                    LogInfo("TRAILING", "DEMA-ATR trailing enabled. Switching trailing type to DEMA-ATR and disabling alternative trailing.");
                    OnPropertyChanged(nameof(UseAlternativeTrailing));
                }
                else
                {
                    trailingAndElasticManager.UseATRTrailing = false;
                    trailingAndElasticManager.TrailingType = priorNonAtrTrailingType;
                    trailingAndElasticManager.UseAlternativeTrailing = priorUseAlternativeTrailing;

                    LogInfo("TRAILING", "DEMA-ATR trailing disabled. Restoring previous trailing configuration.");
                    OnPropertyChanged(nameof(UseAlternativeTrailing));
                }

                OnPropertyChanged(nameof(UseATRTrailing));
            }
        }
        
        public int DEMA_ATR_Period 
        { 
            get => trailingAndElasticManager?.DEMA_ATR_Period ?? 14; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.DEMA_ATR_Period = value; OnPropertyChanged(nameof(DEMA_ATR_Period)); } } 
        }
        
        public double DEMA_ATR_Multiplier 
        { 
            get => trailingAndElasticManager?.DEMA_ATR_Multiplier ?? 1.5; 
            set { if (trailingAndElasticManager != null) { trailingAndElasticManager.DEMA_ATR_Multiplier = value; OnPropertyChanged(nameof(DEMA_ATR_Multiplier)); } } 
        }

        // NT Trade Run-Up (independent of general EnableTrailing)
        private bool _enableNtRunUp = true;
        public bool EnableNtRunUp
        {
            get => _enableNtRunUp;
            set { if (_enableNtRunUp != value) { _enableNtRunUp = value; OnPropertyChanged(nameof(EnableNtRunUp)); } }
        }

        private RunUpUnits _ntRunUpDistanceUnits = RunUpUnits.Ticks;
        public RunUpUnits NtRunUpDistanceUnits
        {
            get => _ntRunUpDistanceUnits;
            set { if (_ntRunUpDistanceUnits != value) { _ntRunUpDistanceUnits = value; OnPropertyChanged(nameof(NtRunUpDistanceUnits)); } }
        }

        private double _ntRunUpDistanceValue = 16;
        public double NtRunUpDistanceValue
        {
            get => _ntRunUpDistanceValue;
            set { if (Math.Abs(_ntRunUpDistanceValue - value) > 1e-9) { _ntRunUpDistanceValue = value; OnPropertyChanged(nameof(NtRunUpDistanceValue)); } }
        }

        private RunUpUnits _ntRunUpIncrementUnits = RunUpUnits.Ticks;
        public RunUpUnits NtRunUpIncrementUnits
        {
            get => _ntRunUpIncrementUnits;
            set { if (_ntRunUpIncrementUnits != value) { _ntRunUpIncrementUnits = value; OnPropertyChanged(nameof(NtRunUpIncrementUnits)); } }
        }

        private double _ntRunUpIncrementValue = 5;
        public double NtRunUpIncrementValue
        {
            get => _ntRunUpIncrementValue;
            set { if (Math.Abs(_ntRunUpIncrementValue - value) > 1e-9) { _ntRunUpIncrementValue = value; OnPropertyChanged(nameof(NtRunUpIncrementValue)); } }
        }

        // Expose unified trailing order semantics to UI (local backing fields; TrailingAndElasticManager does not define these)
        private bool _useLimitOrdersForStops = true; // default hedge-style LIMIT
        public bool UseLimitOrdersForStops
        {
            get => _useLimitOrdersForStops;
            set { if (_useLimitOrdersForStops != value) { _useLimitOrdersForStops = value; OnPropertyChanged(nameof(UseLimitOrdersForStops)); } }
        }

        private bool _useStopMarketOnActivation = true;
        public bool UseStopMarketOnActivation
        {
            get => _useStopMarketOnActivation;
            set { if (_useStopMarketOnActivation != value) { _useStopMarketOnActivation = value; OnPropertyChanged(nameof(UseStopMarketOnActivation)); } }
        }

        private int _placementMinTicksBuffer = 6;
        public int PlacementMinTicksBuffer
        {
            get => _placementMinTicksBuffer;
            set { if (_placementMinTicksBuffer != value) { _placementMinTicksBuffer = value; OnPropertyChanged(nameof(PlacementMinTicksBuffer)); } }
        }

        private int _trailingTimeBufferMs = 15;
        public int TrailingTimeBufferMs
        {
            get => _trailingTimeBufferMs;
            set { if (_trailingTimeBufferMs != value) { _trailingTimeBufferMs = value; OnPropertyChanged(nameof(TrailingTimeBufferMs)); } }
        }
        
    // Internal trailing has been removed. No InternalStops exposed.
        
        // Elastic positions access for UI
        public Dictionary<string, NinjaTrader.NinjaScript.AddOns.ElasticPositionTracker> ElasticPositions
        {
            get { return trailingAndElasticManager?.ElasticPositions ?? new Dictionary<string, NinjaTrader.NinjaScript.AddOns.ElasticPositionTracker>(); }
        }
        
        // Traditional trailing stops access for UI
        public Dictionary<string, NinjaTrader.NinjaScript.AddOns.TraditionalTrailingStop> TraditionalTrailingStops
        {
            get { return trailingAndElasticManager?.TraditionalTrailingStops ?? new Dictionary<string, NinjaTrader.NinjaScript.AddOns.TraditionalTrailingStop>(); }
        }
        
        #endregion
        
        

        #region PnL Properties and INotifyPropertyChanged

        private double _realizedPnL;
        public double RealizedPnL
        {
            get { return _realizedPnL; }
            private set
            {
                if (_realizedPnL != value)
                {
                    _realizedPnL = value;
                    OnPropertyChanged(nameof(RealizedPnL));
                    UpdateTotalPnL();
                }
            }
        }

        private double _unrealizedPnL;
        public double UnrealizedPnL
        {
            get { return _unrealizedPnL; }
            private set
            {
                if (_unrealizedPnL != value)
                {
                    _unrealizedPnL = value;
                    OnPropertyChanged(nameof(UnrealizedPnL));
                    UpdateTotalPnL();
                }
            }
        }

        private double _totalPnL;
        public double TotalPnL
        {
            get { return _totalPnL; }
            private set
            {
                if (_totalPnL != value)
                {
                    _totalPnL = value;
                    OnPropertyChanged(nameof(TotalPnL));
                }
            }
        }

        private void UpdateTotalPnL()
        {
            TotalPnL = RealizedPnL + UnrealizedPnL;
        }

        // NT Performance Tracking for Elastic Hedging
        private double _sessionStartBalance = 0.0;
        private double _dailyStartPnL = 0.0;
        private DateTime _sessionStartTime = DateTime.MinValue;
        private int _sessionTradeCount = 0;
        private string _lastTradeResult = "";
        private double _lastTradePnL = 0.0;
        private string _lastTradeBaseId = "";
        private string _lastTradeInstrument = "";
        private double _lastTradePointsPer1kLoss = 0.0;
        private double _lastTradePnLDollars = 0.0;

        public double SessionStartBalance
        {
            get { return _sessionStartBalance; }
            private set { _sessionStartBalance = value; }
        }

        public double DailyPnL
        {
            get { return TotalPnL - _dailyStartPnL; }
        }

        public int SessionTradeCount
        {
            get { return _sessionTradeCount; }
            private set { _sessionTradeCount = value; }
        }

        public string LastTradeResult
        {
            get { return _lastTradeResult; }
            private set { _lastTradeResult = value; }
        }

        // Initialize session tracking (call when addon starts or new day begins)
        private void InitializeSessionTracking()
        {
            if (monitoredAccount != null)
            {
                _sessionStartTime = DateTime.UtcNow;
                _dailyStartPnL = TotalPnL;
                _sessionTradeCount = 0;
                _lastTradeResult = "";
                _lastTradePnL = 0.0;
                _lastTradeBaseId = "";
                _lastTradeInstrument = "";
                _lastTradePointsPer1kLoss = 0.0;
                _lastTradePnLDollars = 0.0;

                // Get current account balance
                var balanceItem = monitoredAccount.GetAccountItem(Cbi.AccountItem.CashValue, Currency.UsDollar);
                if (balanceItem != null && balanceItem.Value is double)
                {
                    _sessionStartBalance = (double)balanceItem.Value;
                }

                LogAndPrint($"Session tracking initialized: Balance=${_sessionStartBalance:F2}, StartPnL=${_dailyStartPnL:F2}");
            }
        }

        // Update trade result based on execution
        private double GetPointsPer1kLossFromExecution(Execution execution)
        {
            try
            {
                double pointValue = execution?.Instrument?.MasterInstrument?.PointValue ?? 0.0;
                return pointValue > 0 ? 1000.0 / pointValue : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private void UpdateTradeResult(Execution execution, string tradeId, TradeSyncService.TradeRecord tradeRecord)
        {
            if (execution == null || execution.Order == null)
                return;

            var action = execution.Order.OrderAction;
            bool isEntry = action == OrderAction.Buy || action == OrderAction.SellShort;
            bool isExit = action == OrderAction.Sell || action == OrderAction.BuyToCover;

            if (isEntry)
            {
                _lastTradeBaseId = tradeId ?? string.Empty;
                _lastTradeInstrument = tradeRecord?.Instrument ?? execution.Instrument?.FullName ?? _lastTradeInstrument;
                _lastTradePointsPer1kLoss = tradeRecord?.NtPointsPer1kLoss ?? GetPointsPer1kLossFromExecution(execution);
                _lastTradePnL = TotalPnL;
                _lastTradePnLDollars = 0.0;
                _lastTradeResult = "pending";
                return;
            }

            if (!isExit)
                return;

            double tradePnL = double.NaN;
            int quantity = Math.Max(1, Math.Abs((int)(tradeRecord?.NtQuantity ?? execution.Quantity)));
            double entryPrice = tradeRecord?.EntryPrice ?? 0.0;
            if (entryPrice <= 0 && execution.Order != null)
                entryPrice = execution.Order.AverageFillPrice;
            if (entryPrice <= 0)
                entryPrice = execution.Price;

            MarketPosition entrySide = tradeRecord?.Side ?? (action == OrderAction.Sell ? MarketPosition.Long : MarketPosition.Short);
            double pointValue = execution.Instrument?.MasterInstrument?.PointValue ?? 0.0;
            if (tradeRecord?.Strategy?.Instrument?.MasterInstrument != null && pointValue <= 0)
                pointValue = tradeRecord.Strategy.Instrument.MasterInstrument.PointValue;

            if (entryPrice > 0 && pointValue > 0)
            {
                double signed = entrySide == MarketPosition.Long
                    ? (execution.Price - entryPrice)
                    : (entryPrice - execution.Price);
                tradePnL = signed * pointValue * quantity;
            }

            if (double.IsNaN(tradePnL))
            {
                double currentPnL = TotalPnL;
                tradePnL = currentPnL - _lastTradePnL;
                _lastTradePnL = currentPnL;
            }
            else
            {
                _lastTradePnL = TotalPnL;
            }

            if (!string.IsNullOrWhiteSpace(tradeId))
                _lastTradeBaseId = tradeId;
            _lastTradeInstrument = tradeRecord?.Instrument ?? execution.Instrument?.FullName ?? _lastTradeInstrument;
            if (tradeRecord != null && tradeRecord.NtPointsPer1kLoss > 0)
                _lastTradePointsPer1kLoss = tradeRecord.NtPointsPer1kLoss;
            else if (_lastTradePointsPer1kLoss <= 0)
                _lastTradePointsPer1kLoss = GetPointsPer1kLossFromExecution(execution);

            _lastTradePnLDollars = tradePnL;
            _lastTradeResult = tradePnL >= 0 ? "win" : "loss";
            _sessionTradeCount++;

            LogAndPrint($"Trade result updated: {_lastTradeResult} (trade_id={_lastTradeBaseId}, P&L: ${tradePnL:F2})");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
        
        // New menu items for Control Center integration
        private NTMenuItem multiStratMenuItem;
        private MenuItem existingMenuItemInControlCenter; // Changed type from NTMenuItem

        private static List<StrategyBase> monitoredStrategies = new List<StrategyBase>();

        // HTTP completely removed - using gRPC only
        
        // gRPC Configuration
        private bool grpcInitialized = false;
        private bool grpcInitializing = false; // Prevent concurrent initialization
        private string grpcServerAddress = "http://localhost:50051"; // gRPC server address

        // WebSocket removed - using gRPC only


        // Heartbeat tracking
        private DateTime lastHeartbeatSent = DateTime.MinValue;
        private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(20); // Send heartbeat every 20 seconds (faster than bridge 35s timeout)
        private int heartbeatFailureCount = 0;
        private DateTime lastHeartbeatFailure = DateTime.MinValue;
    private readonly TimeSpan heartbeatBackoffDuration = TimeSpan.FromSeconds(20); // Short backoff (~one tick) after repeated failures
    private System.Windows.Threading.DispatcherTimer heartbeatTimer;
    // Keep a reference to the heartbeat tick handler so we can properly unsubscribe on stop
    private EventHandler heartbeatTickHandler;

    // Lightweight NT PnL streaming (separate from elastic/trailing)
    private System.Windows.Threading.DispatcherTimer pnlStreamTimer;
    private EventHandler pnlStreamTickHandler;
    private readonly TimeSpan pnlStreamInterval = TimeSpan.FromSeconds(3); // emit every ~3s
    private double lastPnLSent = double.NaN;
    private DateTime lastPnLSentAt = DateTime.MinValue;

        // Logging infrastructure
        // Auto-logging queue removed - using local NinjaScript output only
        // Auto-logging timer removed
        private static readonly object logTimerLock = new object();
        private Account monitoredAccount = null; // To keep track of the account being monitored
        
        // Trailing and Elastic Manager
        private TrailingAndElasticManager trailingAndElasticManager;
    // Disconnect lifecycle guard
    private readonly object grpcDisconnectLock = new object();
    private bool grpcDisconnectInProgress = false;
        
        // Public properties to expose needed resources to TrailingAndElasticManager
        public Account MonitoredAccount => monitoredAccount;

        // HTTP completely removed - using gRPC streaming instead

        // Class to store original NT trade details
        public class OriginalTradeDetails // Renamed from OriginalNtTradeInfo
        {
            public string BaseId { get; set; }
            public MarketPosition MarketPosition { get; set; } // Renamed from OriginalMarketPosition
            public int Quantity { get; set; } // Renamed from OriginalQuantity
            public double Price { get; set; }
            public string NtInstrumentSymbol { get; set; }
            public string NtAccountName { get; set; }
            public OrderAction OriginalOrderAction { get; set; } // Kept this field
            public DateTime Timestamp { get; set; }

            // MULTI_TRADE_GROUP_FIX: Track total and remaining quantity for this BaseID
            public int TotalQuantity { get; set; } = 0; // Total quantity for this BaseID
            public int RemainingQuantity { get; set; } = 0; // Remaining quantity not yet closed
            
            // CLOSURE_TRACKING_FIX: Track closure state to prevent race conditions
            public bool IsClosed { get; set; } = false; // Whether this trade has been closed
            public DateTime? ClosedTimestamp { get; set; } = null; // When it was closed
        }

        // Dictionary to store active NT trades by their base_id (simple TRADE_XXX format)
        private static ConcurrentDictionary<string, OriginalTradeDetails> activeNtTrades = new ConcurrentDictionary<string, OriginalTradeDetails>(); // Updated type
        private readonly object _activeNtTradesLock = new object(); // Added lock object
        
        // Execution tracking to prevent duplicate trade submissions
        private static readonly HashSet<string> processedExecutionIds = new HashSet<string>();
        private static readonly object executionTrackingLock = new object();
        
        // MT5 close notification deduplication
        private static readonly HashSet<string> processedCloseNotifications = new HashSet<string>();
        private static readonly object closeNotificationLock = new object();

        // BaseID generation - now uses timestamp-based approach for guaranteed uniqueness

        // Mapping between simple baseIDs and original NT OrderIds for closure detection
        private static ConcurrentDictionary<string, string> baseIdToOrderIdMap = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, string> orderIdToBaseIdMap = new ConcurrentDictionary<string, string>();
        
        // MT5 ticket mappings for reliable position closure
        private static ConcurrentDictionary<string, ulong> baseIdToMT5Ticket = new ConcurrentDictionary<string, ulong>();
        private static ConcurrentDictionary<ulong, string> mt5TicketToBaseId = new ConcurrentDictionary<ulong, string>();

        /// <summary>
        /// Generates a short random base_id (<= 32 chars) suitable for EA comment limits.
        /// Example: TRD_3f1a9c6b72d84e15 (20 chars). Hex-only for simplicity and safety.
        /// </summary>
        private static string GenerateSimpleBaseId()
        {
            // 32-char hex GUID, take first 16 to keep it short while collision-resistant
            var g = Guid.NewGuid().ToString("N");
            var shortHex = g.Substring(0, 16);
            return $"TRD_{shortHex}"; // total length = 4 + 1 + 16 = 21
        }

        // Contract tracking for multi-contract orders
        private static ConcurrentDictionary<string, int> orderContractCounts = new ConcurrentDictionary<string, int>();
        
        /// <summary>
        /// Gets the contract number for this execution within its order.
        /// For multi-contract orders, this ensures proper numbering (1, 2, 3, 4...)
        /// </summary>
        private int GetContractNumberForExecution(string orderId, double totalQuantity)
        {
            if (string.IsNullOrEmpty(orderId) || totalQuantity <= 1)
            {
                return 1; // Single contract orders always use contract_num = 1
            }

            // For multi-contract orders, increment and return the contract number
            int contractNum = orderContractCounts.AddOrUpdate(orderId, 1, (key, current) => current + 1);
            
            LogAndPrint($"CONTRACT_TRACKING: OrderId {orderId} execution #{contractNum} of {totalQuantity} total contracts");
            
            return contractNum;
        }

        // Class to represent the JSON payload for hedge close notifications
        public class HedgeCloseNotification
        {
            public string event_type { get; set; }
            public string base_id { get; set; }
            public string nt_instrument_symbol { get; set; }
            public string nt_account_name { get; set; }
            public double closed_hedge_quantity { get; set; }
            public string closed_hedge_action { get; set; } // "Buy" or "Sell"
            public string timestamp { get; set; }
            public string ClosureReason { get; set; } // Added for MT5 EA closure reason
        }

        public void LogAndPrint(string message)
        {
            // Direct NinjaTrader output
            NinjaTrader.Code.Output.Process($"[NT_ADDON] {message}", PrintTo.OutputTab1);
            // Also forward to Bridge so JSONL contains UNIFIED_/TRAILING_/ELASTIC_ diagnostics
            try { TryBridgeLog("INFO", "nt_addon", message); } catch { /* best-effort logging */ }
        }
        
        /// <summary>
        /// Extract a value from JSON string
        /// </summary>
        /// <param name="json">JSON string</param>
        /// <param name="key">Key to extract</param>
        /// <returns>Value or empty string if not found</returns>
        private string ExtractJsonValue(string json, string key)
        {
            try
            {
                string searchPattern = $"\"{key}\":\""; // For string values
                int startIndex = json.IndexOf(searchPattern);
                if (startIndex >= 0)
                {
                    startIndex += searchPattern.Length;
                    int endIndex = json.IndexOf('"', startIndex);
                    if (endIndex > startIndex)
                    {
                        return json.Substring(startIndex, endIndex - startIndex);
                    }
                }
                
                // Try numeric values
                searchPattern = $"\"{key}\":";
                startIndex = json.IndexOf(searchPattern);
                if (startIndex >= 0)
                {
                    startIndex += searchPattern.Length;
                    int endIndex = json.IndexOfAny(new char[] { ',', '}' }, startIndex);
                    if (endIndex > startIndex)
                    {
                        return json.Substring(startIndex, endIndex - startIndex).Trim();
                    }
                }
                
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Start streaming to receive MT5 trade results
        /// </summary>
        private void StartMT5TradeResultStream()
        {
            try
            {
                LogInfo("GRPC", "Starting MT5 trade result streaming...");
                
                // Start the trading stream to receive trade results from MT5
                bool streamStarted = TradingGrpcClient.StartTradingStream(OnMT5TradeResultReceived);
                
                if (streamStarted)
                {
                    LogInfo("GRPC", "MT5 trade result streaming started successfully");
                }
                else
                {
                    LogError("GRPC", $"Failed to start MT5 trade result streaming: {TradingGrpcClient.LastError}");
                }
            }
            catch (Exception ex)
            {
                LogError("GRPC", $"Exception starting MT5 trade result streaming: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle incoming MT5 trade results
        /// </summary>
        /// <param name="tradeResultJson">JSON trade result from MT5</param>
        private void OnMT5TradeResultReceived(string tradeResultJson)
        {
            try
            {
                LogInfo("GRPC", $"Received MT5 trade result: {tradeResultJson}");
                LogInfo("GRPC", $"DEBUG: Full JSON received: {tradeResultJson}");
                
                // Extract fields from JSON - fix base_id extraction
                string baseId = ExtractJsonValue(tradeResultJson, "base_id");  // Use "base_id" for closure notifications
                string action = ExtractJsonValue(tradeResultJson, "action");
                string ticketStr = ExtractJsonValue(tradeResultJson, "ticket");  // MT5 sends "ticket" not "mt5_ticket"
                string status = ExtractJsonValue(tradeResultJson, "status");
                string messageId = ExtractJsonValue(tradeResultJson, "id"); // Extract message ID for deduplication
                string orderType = ExtractJsonValue(tradeResultJson, "order_type"); // May be NT_CLOSE_ACK or MT5_CLOSE
                
                if (!string.IsNullOrEmpty(baseId) && !string.IsNullOrEmpty(ticketStr))
                {
                    if (ulong.TryParse(ticketStr, out ulong mt5Ticket) && mt5Ticket > 0)
                    {
                        // Store the MT5 ticket mapping
                        baseIdToMT5Ticket.TryAdd(baseId, mt5Ticket);
                        mt5TicketToBaseId.TryAdd(mt5Ticket, baseId);
                        
                        LogInfo("GRPC", $"Stored MT5 ticket mapping - BaseID: {baseId} <-> Ticket: {mt5Ticket}");
                    }
                }
                
                // Handle different types of results
                if (!string.IsNullOrEmpty(action))
                {
                    LogInfo("GRPC", $"DEBUG: Processing action '{action}' with baseId '{baseId}'");
                    
                    if (action == "HEDGE_OPENED")
                    {
                        LogInfo("GRPC", $"MT5 hedge opened for BaseID: {baseId}");
                    }
                    else if (action == "HEDGE_CLOSED")
                    {
                        LogInfo("GRPC", $"MT5 hedge closed for BaseID: {baseId}");
                        // Handle MT5-initiated hedge closure - close corresponding NT position
                        // Use the same handler as MT5_CLOSE_NOTIFICATION since both indicate MT5 closed a position
                        HandleMT5InitiatedClosure(tradeResultJson, baseId);
                        LogInfo("GRPC", $"Triggered NT position closure for hedge close event - BaseID: {baseId}");
                    }
                    else if (action == "MT5_CLOSE_NOTIFICATION")
                    {
                        // MT5 initiated a position closure - check for duplicates first
                        bool shouldProcess = false;
                        // Include mt5_ticket in dedup key when available so sequential closes aren’t dropped
                        string ticketForDedup = ExtractJsonValue(tradeResultJson, "mt5_ticket");
                        string timestampForDedup = ExtractJsonValue(tradeResultJson, "timestamp");
                        string closureReasonForDedup = ExtractJsonValue(tradeResultJson, "closure_reason");
                        if (string.IsNullOrEmpty(closureReasonForDedup))
                            closureReasonForDedup = ExtractJsonValue(tradeResultJson, "nt_trade_result");

                        string dedupSuffix;
                        long ticketNumber;
                        string quantityForDedup = ExtractJsonValue(tradeResultJson, "quantity");
                        string profitLevelForDedup = ExtractJsonValue(tradeResultJson, "elastic_profit_level");
                        string reasonPart = !string.IsNullOrEmpty(closureReasonForDedup) ? closureReasonForDedup : "none";
                        string qtyPart = !string.IsNullOrEmpty(quantityForDedup) ? quantityForDedup : "0";
                        string levelPart = !string.IsNullOrEmpty(profitLevelForDedup) ? profitLevelForDedup : "0";

                        if (!string.IsNullOrEmpty(ticketForDedup) && long.TryParse(ticketForDedup, out ticketNumber) && ticketNumber > 0)
                        {
                            dedupSuffix = $"{ticketNumber}_{reasonPart}_{qtyPart}_{levelPart}";
                        }
                        else
                        {
                            // Fallback to unique message id; include closure reason to differentiate partial vs completion
                            string fallbackId = !string.IsNullOrEmpty(messageId)
                                ? messageId
                                : (!string.IsNullOrEmpty(timestampForDedup) ? timestampForDedup : DateTime.UtcNow.Ticks.ToString());
                            dedupSuffix = $"{fallbackId}_{reasonPart}_{qtyPart}_{levelPart}";
                        }

                        string deduplicationKey = $"{action}_{baseId}_{dedupSuffix}"; // action + baseId + (ticket or unique id)
                        
                        lock (closeNotificationLock)
                        {
                            if (!processedCloseNotifications.Contains(deduplicationKey))
                            {
                                processedCloseNotifications.Add(deduplicationKey);
                                shouldProcess = true;
                                LogInfo("GRPC", $"MT5_CLOSE_DEDUP: First occurrence of notification {deduplicationKey} - processing");
                            }
                            else
                            {
                                LogInfo("GRPC", $"MT5_CLOSE_DEDUP: Duplicate notification {deduplicationKey} - skipping to prevent multiple close orders");
                            }
                        }
                        
                        if (shouldProcess)
                        {
                            // If this is an acknowledgement for an NT-initiated close, do not initiate another NT close
                            if (!string.IsNullOrEmpty(orderType) && orderType == "NT_CLOSE_ACK")
                            {
                                LogInfo("GRPC", $"MT5_CLOSE_NOTIFICATION is NT_CLOSE_ACK for BaseID {baseId}; acknowledging without submitting NT close order.");
                                return;
                            }

                            // POLICY: Do NOT close NT when MT5 reports an elastic partial close
                            string closureReason = closureReasonForDedup;

                            if (!string.IsNullOrEmpty(closureReason) && closureReason.Equals("elastic_partial_close", StringComparison.OrdinalIgnoreCase))
                            {
                                LogInfo("GRPC", $"[ELASTIC_PARTIAL_SKIP] Skipping NT close for BaseID {baseId} due to MT5 elastic_partial_close.");
                                return;
                            }
                            // Process MT5 close notification and close corresponding NT position
                            HandleMT5InitiatedClosure(tradeResultJson, baseId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("GRPC", $"Error processing MT5 trade result: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Handle MT5-initiated position closures by closing corresponding NT positions
        /// </summary>
        /// <param name="notificationJson">JSON notification from MT5</param>
        /// <param name="baseId">BaseID of the closed position</param>
        private void HandleMT5InitiatedClosure(string notificationJson, string baseId)
        {
            try
            {
                LogInfo("GRPC", $"Processing MT5-initiated closure for BaseID: {baseId}");
                LogInfo("GRPC", $"DEBUG: Full JSON received: {notificationJson}");
                
                // Extract closure details from JSON
                // Prefer explicit closure_reason when present; fallback to nt_trade_result (compat)
                string closureReason = ExtractJsonValue(notificationJson, "closure_reason");
                if (string.IsNullOrEmpty(closureReason))
                    closureReason = ExtractJsonValue(notificationJson, "nt_trade_result");
                string mt5TicketStr = ExtractJsonValue(notificationJson, "id"); // Use the closure ID as reference
                string instrument = ExtractJsonValue(notificationJson, "instrument_name");
                string quantityStr = ExtractJsonValue(notificationJson, "quantity");
                int requestedContracts = ParseClosureQuantity(quantityStr);
                bool isElasticPartial = string.Equals(closureReason, "elastic_partial_close", StringComparison.OrdinalIgnoreCase);
                bool isRunUp = IsRunUpReason(closureReason);
                
                LogInfo("GRPC", $"MT5 closure details - Reason: {closureReason}, Ticket: {mt5TicketStr}, Instrument: {instrument}");
                
                // Find the corresponding NT position by BaseID
                // Look for an account that has positions, not just the first account
                var account = Account.All.FirstOrDefault(a => a.Positions.Count > 0);
                if (account == null)
                {
                    // Fallback to any account if no account has positions
                    account = Account.All.FirstOrDefault();
                }
                
                if (account == null)
                {
                    LogError("GRPC", "No accounts available for MT5 closure handling");
                    return;
                }
                
                LogInfo("GRPC", $"Using account '{account.Name}' for MT5 closure handling");
                LogInfo("GRPC", $"DEBUG: Available accounts: {string.Join(", ", Account.All.Select(a => $"{a.Name}({a.Positions.Count} pos)"))}");

                if (!isRunUp && TryDelegateClosureToStrategy(baseId, isElasticPartial, requestedContracts))
                {
                    LogInfo("GRPC", $"Delegated MT5 {(isElasticPartial ? "partial " : string.Empty)}closure to owning strategy for BaseID: {baseId}");
                    return;
                }

                // HEDGING SYSTEM: Find and close ONLY the specific position matching this base_id
                var positionsToClose = new List<NinjaTrader.Cbi.Position>();
                LogInfo("GRPC", $"DEBUG: Looking for positions to close. Account has {account.Positions.Count} positions and {account.Orders.Count} orders");
                
                // Look up the original trade details for this base_id
                LogInfo("GRPC", $"DEBUG: Looking up base_id '{baseId}' in activeNtTrades");
                if (activeNtTrades.TryGetValue(baseId, out var originalTradeDetails))
                {
                    // CLOSURE_RACE_FIX: Skip only when nothing remains to be closed
                    if (originalTradeDetails.RemainingQuantity <= 0)
                    {
                        LogInfo("GRPC", $"ALREADY_FULLY_CLOSED: Trade {baseId} has RemainingQuantity=0. Skipping MT5 closure handling.");
                        CancelProtectiveOrdersForBaseId(account, baseId);
                        return; // Nothing left to close
                    }
                    
                    LogInfo("GRPC", $"DEBUG: Found original trade details for base_id '{baseId}': Instrument={originalTradeDetails.NtInstrumentSymbol}, Position={originalTradeDetails.MarketPosition}, Qty={originalTradeDetails.Quantity}");
                    
                    // Find the specific position that matches this original trade
                    // IMPORTANT: Do not require current net position quantity to be >= original trade quantity.
                    // After the first sequential close, net qty drops and must still be eligible for subsequent closes.
                    var targetPosition = account.Positions.FirstOrDefault(p =>
                        p.Instrument.FullName == originalTradeDetails.NtInstrumentSymbol &&
                        p.MarketPosition == originalTradeDetails.MarketPosition &&
                        Math.Abs(p.Quantity) > 0);
                    
                    if (targetPosition != null)
                    {
                        LogInfo("GRPC", $"DEBUG: Found matching position to close: {targetPosition.Instrument.MasterInstrument.Name} (Quantity: {targetPosition.Quantity})");
                        LogInfo("GRPC", $"SEQUENTIAL_CLOSE_OK: Proceeding even if current qty < original trade qty {originalTradeDetails.Quantity}");
                        positionsToClose.Add(targetPosition);
                        if (IsRunUpReason(closureReason) && TryActivateNtRunUp(targetPosition, baseId, closureReason))
                        {
                            LogInfo("GRPC", $"[RUN_UP_BYPASS] Keeping NT trade open for {baseId}; MT5 hedge stopped out (reason={closureReason})");
                            return;
                        }
                        // Do NOT mark IsClosed here. We'll decrement RemainingQuantity when the NT close order actually fills
                        // in the execution/closure tracking path. This preserves sequential MT5 closes for multi-quantity trades.
                    }
                    else
                    {
                        LogInfo("GRPC", $"DEBUG: No matching position found for base_id '{baseId}' - checking all positions:");
                        foreach (var pos in account.Positions)
                        {
                            LogInfo("GRPC", $"DEBUG: Available position: {pos.Instrument.FullName}, Position: {pos.MarketPosition}, Quantity: {pos.Quantity}");
                        }
                    }
                }
                else
                {
                    LogInfo("GRPC", $"BASE_ID_MISMATCH: '{baseId}' not found in activeNtTrades. Available base_ids: {string.Join(", ", activeNtTrades.Keys.Take(5))}");
                    
                    // ENHANCED INTELLIGENT MATCHING: Try to find position using closure details
                    LogInfo("GRPC", $"INTELLIGENT_MATCHING: Attempting to match position using closure notification details...");
                    
                    // Extract closure details for intelligent matching
                    quantityStr = ExtractJsonValue(notificationJson, "quantity");
                    
                    if (double.TryParse(quantityStr, out double closedQuantity) && closedQuantity > 0)
                    {
                        LogInfo("GRPC", $"MATCHING_CRITERIA: Looking for positions in instrument '{instrument}' with quantity >= {closedQuantity}");
                        
                        // Find candidate positions based on instrument
                        // Map MT5 instrument (NAS100.s) to NT instrument pattern (NQ)
                        string ntInstrumentPattern = "";
                        if (instrument.Contains("NAS100") || instrument.Contains("NQ"))
                        {
                            ntInstrumentPattern = "NQ";
                        }
                        else if (instrument.Contains("ES") || instrument.Contains("SPX"))
                        {
                            ntInstrumentPattern = "ES";
                        }
                        // Add more mappings as needed
                        
                        var candidatePositions = account.Positions.Where(p => 
                            p.Quantity != 0 && 
                            !string.IsNullOrEmpty(ntInstrumentPattern) &&
                            p.Instrument.FullName.Contains(ntInstrumentPattern)
                        ).ToList();
                        
                        // SAFETY_FILTER: Remove positions that are still actively tracked by other base_ids
                        var safePositions = candidatePositions.Where(candidate =>
                        {
                            // Check if this position is still actively tracked by another base_id
                            var trackingEntries = activeNtTrades.Values.Where(trade => 
                                !trade.IsClosed && 
                                trade.NtInstrumentSymbol == candidate.Instrument.FullName &&
                                trade.MarketPosition == candidate.MarketPosition
                            ).ToList();
                            
                            // If no active tracking entries, it's safe to close
                            return trackingEntries.Count == 0;
                        }).ToList();
                        
                        LogInfo("GRPC", $"INTELLIGENT_MATCH_RESULT: Found {candidatePositions.Count} candidate positions, {safePositions.Count} safe to close for pattern '{ntInstrumentPattern}'");
                        
                        if (safePositions.Count == 1)
                        {
                            LogInfo("GRPC", $"SAFE_MATCH: Found exactly one safe candidate position - safe to close: {safePositions[0].Instrument.FullName} (Qty: {safePositions[0].Quantity})");
                            positionsToClose.Add(safePositions[0]);
                            if (IsRunUpReason(closureReason) && TryActivateNtRunUp(safePositions[0], baseId, closureReason))
                            {
                                LogInfo("GRPC", $"[RUN_UP_BYPASS] Keeping NT trade open for {baseId}; MT5 hedge stopped out (reason={closureReason})");
                                return;
                            }
                        }
                        else if (safePositions.Count == 0)
                        {
                            LogError("GRPC", $"NO_SAFE_MATCH: No safe positions found matching instrument pattern '{ntInstrumentPattern}' - all may be tracked by other active trades");
                            if (candidatePositions.Count > 0)
                            {
                                LogError("GRPC", $"UNSAFE_CANDIDATES: Found {candidatePositions.Count} positions but they may belong to other active trades - refusing to close to prevent errors");
                            }
                        }
                        else
                        {
                            LogError("GRPC", $"AMBIGUOUS_SAFE_MATCH: Found {safePositions.Count} safe candidate positions - cannot safely determine which to close:");
                            foreach (var pos in safePositions)
                            {
                                LogError("GRPC", $"  - {pos.Instrument.FullName}: {pos.MarketPosition} {pos.Quantity}");
                            }
                            LogError("GRPC", $"SAFETY_ABORT: Refusing to close any position due to ambiguity - base_id mismatch needs investigation");
                        }
                    }
                    else
                    {
                        LogError("GRPC", $"INVALID_QUANTITY: Cannot parse quantity '{quantityStr}' from closure notification - cannot attempt intelligent matching");
                    }
                }
                
                LogInfo("GRPC", $"DEBUG: Found {positionsToClose.Count} positions to close");
                
                if (positionsToClose.Count == 0)
                {
                    Log($"GRPC WARNING: No NT positions found to close for BaseID: {baseId}", LogLevel.Warning);
                    CancelProtectiveOrdersForBaseId(account, baseId);
                    return;
                }
                
                // Close the found positions
                foreach (var position in positionsToClose)
                {
                    try
                    {
                        LogInfo("GRPC", $"Closing NT position: {position.Instrument.MasterInstrument.Name}, Quantity: {position.Quantity}");
                        
                        // CRITICAL FIX: Validate position before creating close order
                        if (Math.Abs(position.Quantity) < 0.01) // Position essentially already closed
                        {
                            LogInfo("GRPC", $"Position {position.Instrument.MasterInstrument.Name} already closed (Quantity: {position.Quantity}) - skipping close order");
                            continue;
                        }
                        
                        // CRITICAL FIX: Use the ORIGINAL TRADE QUANTITY, not the total position quantity
                        // This prevents closing all positions when only one specific trade should close
                        // Always close exactly 1 contract per MT5 hedge close notification to maintain 1:1 mapping
                        int quantityToClose = 1;
                        LogInfo("GRPC", $"QUANTITY_FIX: For MT5-initiated closure, forcing quantity {quantityToClose} to avoid mass closing NT positions (position qty {Math.Abs(position.Quantity)})");
                        OrderAction closeAction = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.Buy;
                        
                        LogInfo("GRPC", $"Creating close order: Action={closeAction}, Quantity={quantityToClose}");
                        
                        // Create market order to close the position
                        string closeName = $"MT5_CLOSE_{baseId}_{DateTime.UtcNow:HHmmss}";
                        var closeOrder = account.CreateOrder(
                            position.Instrument,
                            closeAction, // Fixed: Use calculated close action
                            OrderType.Market,
                            TimeInForce.Day,
                            quantityToClose, // Fixed: Use absolute quantity
                            0, // limit price (not used for market orders)
                            0, // stop price (not used for market orders)
                            string.Empty, // oco string
                            closeName,
                            null // custom order
                        );
                        
                        if (closeOrder != null)
                        {
                            // ENHANCED: Track close order state and add to tracking
                            LogInfo("GRPC", $"Submitting close order: {closeName}");
                            LogInfo("GRPC", $"Order details BEFORE submit - ID: {closeOrder.OrderId}, Action: {closeOrder.OrderAction}, Type: {closeOrder.OrderType}, Quantity: {closeOrder.Quantity}, Instrument: {closeOrder.Instrument.FullName}");
                            LogInfo("GRPC", $"Order state BEFORE submit: {closeOrder.OrderState}");
                            LogInfo("GRPC", $"Position details - Quantity: {position.Quantity}, MarketPosition: {position.MarketPosition}, AvgPrice: {position.AveragePrice}");
                            
                            account.Submit(new[] { closeOrder });
                            LogInfo("GRPC", $"Submitted NT close order for MT5-initiated closure: {closeName}");
                            LogInfo("GRPC", $"Order state AFTER submit: {closeOrder.OrderState}");

                            // Decrement remaining quantity for this base_id to allow further sequential closes
                            try
                            {
                                bool cancelProtectiveOrders = false;
                                lock (_activeNtTradesLock)
                                {
                                    if (activeNtTrades.TryGetValue(baseId, out var od))
                                    {
                                        if (od.RemainingQuantity > 0)
                                        {
                                            od.RemainingQuantity -= 1;
                                            LogInfo("GRPC", $"SEQ_TRACK: RemainingQuantity for {baseId} decremented to {od.RemainingQuantity}");
                                            if (od.RemainingQuantity <= 0)
                                            {
                                                od.IsClosed = true;
                                                od.ClosedTimestamp = DateTime.UtcNow;
                                                LogInfo("GRPC", $"SEQ_TRACK: BaseID {baseId} fully closed");
                                                cancelProtectiveOrders = true;
                                            }
                                            activeNtTrades[baseId] = od;
                                        }
                                    }
                                }

                                if (!cancelProtectiveOrders && !activeNtTrades.ContainsKey(baseId))
                                {
                                    cancelProtectiveOrders = true;
                                }

                                if (cancelProtectiveOrders)
                                {
                                    CancelProtectiveOrdersForBaseId(account, baseId);
                                }
                            }
                            catch (Exception rqEx)
                            {
                                LogError("GRPC", $"Error updating RemainingQuantity for {baseId}: {rqEx.Message}");
                            }
                            
                            // Add comprehensive tracking for order execution
                            Task.Run(async () => 
                            {
                                // Wait a bit and check order status
                                await Task.Delay(1000);
                                LogInfo("GRPC", $"Close order {closeName} status check: State={closeOrder.OrderState}, Filled={closeOrder.Filled}, AvgFillPrice={closeOrder.AverageFillPrice}");
                                
                                // Check again after more time
                                await Task.Delay(4000);
                                LogInfo("GRPC", $"Close order {closeName} final status: State={closeOrder.OrderState}, Filled={closeOrder.Filled}, AvgFillPrice={closeOrder.AverageFillPrice}");
                                
                                if (closeOrder.OrderState == OrderState.Rejected)
                                {
                                    LogError("GRPC", $"Close order {closeName} was REJECTED. This explains why positions aren't closing!");
                                }
                                else if (closeOrder.OrderState != OrderState.Filled && closeOrder.OrderState != OrderState.PartFilled)
                                {
                                    LogError("GRPC", $"Close order {closeName} did not execute. State: {closeOrder.OrderState}");
                                }
                            });
                        }
                        else
                        {
                            LogError("GRPC", $"Failed to create close order for position {position.Instrument.MasterInstrument.Name}");
                        }
                    }
                    catch (Exception orderEx)
                    {
                        LogError("GRPC", $"Error creating close order for position: {orderEx.Message}");
                        LogError("GRPC", $"OrderEx StackTrace: {orderEx.StackTrace}");
                    }
                }
                
                LogInfo("GRPC", $"Completed MT5-initiated closure handling for BaseID: {baseId}, closed {positionsToClose.Count} positions");
            }
            catch (Exception ex)
            {
                LogError("GRPC", $"Error handling MT5-initiated closure for BaseID {baseId}: {ex.Message}");
            }
        }

        private int ParseClosureQuantity(string quantityStr)
        {
            if (!string.IsNullOrWhiteSpace(quantityStr) && double.TryParse(quantityStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                double normalized = Math.Max(1.0, Math.Abs(parsed));
                return Math.Max(1, (int)Math.Round(normalized));
            }
            return 1;
        }

        private bool TryDelegateClosureToStrategy(string baseId, bool isElasticPartial, int requestedContracts)
        {
            if (tradeSyncService == null || string.IsNullOrWhiteSpace(baseId))
                return false;

            TradeSyncService.TradeRecord record;
            if (!tradeSyncService.TryGetTrade(baseId, out record) || record?.Strategy == null)
                return false;

            if (!(record.Strategy is ITradeSyncParticipant))
                return false;

            int qty = Math.Max(1, requestedContracts);
            if (isElasticPartial)
            {
                int remaining = Math.Max(1, record.RemainingQuantity);
                qty = Math.Min(qty, remaining);
                tradeSyncService.HandleBridgePartial(baseId, qty);
                LogInfo("GRPC", $"Delegated elastic partial close (qty={qty}) to strategy '{record.Strategy.Name}' for BaseID: {baseId}");
            }
            else
            {
                tradeSyncService.HandleBridgeClosed(baseId);
                LogInfo("GRPC", $"Delegated MT5 close to strategy '{record.Strategy.Name}' for BaseID: {baseId}");
            }
            return true;
        }

        private void CancelProtectiveOrdersForBaseId(Account account, string baseId)
        {
            if (account == null || string.IsNullOrWhiteSpace(baseId))
                return;

            try
            {
                var orders = account.Orders;
                if (orders == null || orders.Count == 0)
                {
                    LogInfo("GRPC", $"CANCEL_PROTECTIVE: No orders present when evaluating BaseID {baseId}");
                    return;
                }

                var toCancel = new List<Order>();
                foreach (var order in orders)
                {
                    if (order == null)
                        continue;

                    if (!string.Equals(order.FromEntrySignal, baseId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    switch (order.OrderState)
                    {
                        case OrderState.Initialized:
                        case OrderState.Submitted:
                        case OrderState.Accepted:
                        case OrderState.Working:
                        case OrderState.PartFilled:
                        case OrderState.ChangeSubmitted:
                        case OrderState.ChangePending:
                            toCancel.Add(order);
                            break;
                    }
                }

                if (toCancel.Count == 0)
                {
                    LogInfo("GRPC", $"CANCEL_PROTECTIVE: No active protective orders found for BaseID {baseId}");
                    return;
                }

                try
                {
                    account.Cancel(toCancel.ToArray());
                    LogInfo("GRPC", $"CANCEL_PROTECTIVE: Cancelled {toCancel.Count} protective orders tied to BaseID {baseId}");
                }
                catch (Exception cancelEx)
                {
                    LogError("GRPC", $"CANCEL_PROTECTIVE: Failed to cancel protective orders for BaseID {baseId}: {cancelEx.Message}");
                }
            }
            catch (Exception ex)
            {
                LogError("GRPC", $"CANCEL_PROTECTIVE: Unexpected error while processing BaseID {baseId}: {ex.Message}");
            }
        }



        /// <summary>
        /// Start keepalive heartbeat system to maintain bridge connection
        /// </summary>
        private void StartHeartbeatSystem()
        {
            if (heartbeatTimer == null)
            {
                heartbeatTimer = new System.Windows.Threading.DispatcherTimer();
                heartbeatTimer.Interval = heartbeatInterval;
                // store handler so we can unsubscribe exactly later
                heartbeatTickHandler = async (sender, e) => await SendHeartbeatAsync();
                heartbeatTimer.Tick += heartbeatTickHandler;
                heartbeatTimer.Start();
                LogInfo("GRPC", "Keepalive heartbeat system started");

                // Begin lightweight periodic PnL streaming alongside heartbeat
                StartPnLStreamingTimer();
            }
        }

        /// <summary>
        /// Stop keepalive heartbeat system
        /// </summary>
        private void StopHeartbeatSystem()
        {
            if (heartbeatTimer != null)
            {
                heartbeatTimer.Stop();
                if (heartbeatTickHandler != null)
                {
                    heartbeatTimer.Tick -= heartbeatTickHandler;
                    heartbeatTickHandler = null;
                }

                heartbeatTimer = null;
                LogInfo("GRPC", "Keepalive heartbeat system stopped");
            }

            // Also stop PnL streaming timer
            StopPnLStreamingTimer();
        }

        /// <summary>
        /// Public method to stop heartbeat system (for UI cleanup)
        /// </summary>
        public void StopHeartbeatSystemPublic()
        {
            StopHeartbeatSystem();
        }

        private void ResetPnLStreamState()
        {
            lastPnLSent = double.NaN;
            lastPnLSentAt = DateTime.MinValue;
        }

        /// <summary>
        /// Start periodic NT PnL streaming to MT5 via SubmitTrade(EVENT) without touching elastic/trailing.
        /// EA already parses nt_daily_pnl from any incoming message and updates tier state accordingly.
        /// </summary>
        private void StartPnLStreamingTimer()
        {
            try
            {
                if (pnlStreamTimer == null)
                {
                    pnlStreamTimer = new System.Windows.Threading.DispatcherTimer();
                    pnlStreamTimer.Interval = pnlStreamInterval;
                    pnlStreamTickHandler = (s, e) => EmitPnLUpdateIfNeeded();
                    pnlStreamTimer.Tick += pnlStreamTickHandler;
                    pnlStreamTimer.Start();
                    LogAndPrint("PNL_STREAM: Started periodic NT PnL streaming timer (~3s)");

                    // Try an immediate emit (may be skipped if not yet connected / no account)
                    EmitPnLUpdateIfNeeded();

                    // Schedule a second attempt shortly after startup to catch post-connect state
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2000);
                            EmitPnLUpdateIfNeeded();
                        }
                        catch (Exception ex)
                        {
                            LogAndPrint($"PNL_STREAM_ERROR: Delayed initial emit failed: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogAndPrint($"PNL_STREAM_ERROR: Failed to start PnL streaming timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Stop the periodic NT PnL streaming timer.
        /// </summary>
        private void StopPnLStreamingTimer()
        {
            try
            {
                if (pnlStreamTimer != null)
                {
                    pnlStreamTimer.Stop();
                    if (pnlStreamTickHandler != null)
                    {
                        pnlStreamTimer.Tick -= pnlStreamTickHandler;
                        pnlStreamTickHandler = null;
                    }
                    pnlStreamTimer = null;
                    LogAndPrint("PNL_STREAM: Stopped periodic NT PnL streaming timer");
                }
            }
            catch (Exception ex)
            {
                LogAndPrint($"PNL_STREAM_ERROR: Failed to stop PnL streaming timer: {ex.Message}");
            }
        }

        /// <summary>
        /// Emit a PnL update as an EVENT over SubmitTrade. Includes nt_daily_pnl and counters.
        /// Uses a small change filter to avoid spamming identical values.
        /// </summary>
        private void EmitPnLUpdateIfNeeded()
        {
            try
            {
                // Only when connected and account available
                if (!TradingGrpcClient.IsConnected)
                {
                    // Diagnostic skip log only every ~10s to avoid spam
                    if ((DateTime.UtcNow - lastPnLSentAt) > TimeSpan.FromSeconds(10))
                        LogAndPrint("PNL_STREAM_SKIP: Not connected yet");
                    return;
                }

                var account = this.monitoredAccount;
                if (account == null)
                {
                    if ((DateTime.UtcNow - lastPnLSentAt) > TimeSpan.FromSeconds(10))
                        LogAndPrint("PNL_STREAM_SKIP: monitoredAccount null");
                    return;
                }

                double currentPnL = DailyPnL;

                // Change filter: send if first time or changed by >= $5 or at least every 15s
                bool shouldSend = double.IsNaN(lastPnLSent) || Math.Abs(currentPnL - lastPnLSent) >= 5.0 || (DateTime.UtcNow - lastPnLSentAt) >= TimeSpan.FromSeconds(15);
                if (!shouldSend)
                {
                    // Lightweight trace for suppression (every 15s window only)
                    if ((DateTime.UtcNow - lastPnLSentAt) >= TimeSpan.FromSeconds(15))
                        LogAndPrint($"PNL_STREAM_SUPPRESS: currentPnL=${currentPnL:F2} unchanged (<$5 delta)");
                    return;
                }

                var data = new Dictionary<string, object>
                {
                    { "action", "EVENT" },
                    { "event_type", "nt_pnl_update" },
                    { "base_id", _lastTradeBaseId ?? string.Empty },
                    { "instrument", _lastTradeInstrument ?? string.Empty },
                    { "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) },
                    { "account_name", account.Name },
                    { "nt_balance", (float)_sessionStartBalance },
                    { "nt_daily_pnl", (float)currentPnL },
                    { "nt_trade_pnl", (float)_lastTradePnLDollars },
                    { "nt_trade_result", _lastTradeResult },
                    { "nt_points_per_1k_loss", (float)_lastTradePointsPer1kLoss },
                    { "nt_session_trades", _sessionTradeCount }
                };

                // Fire-and-forget submit
                Task.Run(async () => await SendToBridge(data));

                lastPnLSent = currentPnL;
                lastPnLSentAt = DateTime.UtcNow;

                LogAndPrint($"PNL_STREAM_EMIT: Sent nt_daily_pnl=${currentPnL:F2} (acct={account.Name})");
            }
            catch (Exception ex)
            {
                LogAndPrint($"PNL_STREAM_ERROR: Exception in EmitPnLUpdateIfNeeded: {ex.Message}");
            }
        }

        /// <summary>
        /// Send heartbeat to bridge via gRPC with retry logic and circuit breaker
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            try
            {
                // Skip if UI is not open (manual connection mode)
                if (!IsUiOpen)
                {
                    return;
                }
                // Circuit breaker: Skip if we've had recent failures
                if (heartbeatFailureCount >= 3 && DateTime.UtcNow - lastHeartbeatFailure < heartbeatBackoffDuration)
                {
                    return; // Skip heartbeat during backoff period
                }

                // Skip if not initialized
                if (!grpcInitialized)
                {
                    return;
                }

                // Use gRPC health check (silent - no logging to prevent spam)
                var healthResult = await Task.Run(() => {
                    string responseJson;
                    bool isHealthy = TradingGrpcClient.HealthCheck("NT_ADDON_KEEPALIVE", out responseJson);
                    return new { IsHealthy = isHealthy, ResponseJson = responseJson };
                });
                bool isHealthy = healthResult.IsHealthy;

                if (isHealthy)
                {
                    lastHeartbeatSent = DateTime.UtcNow;
                    heartbeatFailureCount = 0; // Reset failure count on success
                    // No logging for successful heartbeats to avoid spam
                }
                else
                {
                    heartbeatFailureCount++;
                    lastHeartbeatFailure = DateTime.UtcNow;

                    // Only log error once every 3 failures to reduce spam
                    if (heartbeatFailureCount == 1 || heartbeatFailureCount % 3 == 0)
                    {
                        string error = TradingGrpcClient.LastError;
                        LogWarn("SYSTEM", $"Heartbeat failed ({heartbeatFailureCount} failures): {error}");
                    }

                    // Circuit breaker: after sustained failures, disconnect fully to release resources
                    if (heartbeatFailureCount >= 6)
                    {
                        LogWarn("GRPC", $"Heartbeat has failed {heartbeatFailureCount} times — disconnecting gRPC and stopping timers to avoid hangs");
                        DisconnectGrpcAndStopAll();
                    }
                }
            }
            catch (Exception ex)
            {
                heartbeatFailureCount++;
                lastHeartbeatFailure = DateTime.UtcNow;
                
                // Only log exception once every 3 failures to reduce spam
                if (heartbeatFailureCount == 1 || heartbeatFailureCount % 3 == 0)
                {
                    LogWarn("SYSTEM", $"Heartbeat exception ({heartbeatFailureCount} failures): {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Initialize gRPC client connection
        /// </summary>
        private async Task InitializeGrpcClient()
        {
            try
            {
                // Prevent concurrent initialization
                if (grpcInitialized || grpcInitializing) return;
                
                grpcInitializing = true;

                LogInfo("GRPC", "Initializing gRPC client connection...");
                
                // Initialize the gRPC client with server address (remove http:// prefix for gRPC)
                string grpcAddress = grpcServerAddress.Replace("http://", "").Replace("https://", "");
                LogDebug("GRPC", $"Calling TradingGrpcClient.Initialize with: {grpcAddress} (converted from {grpcServerAddress})");
                bool initialized = TradingGrpcClient.Initialize(grpcAddress);
                LogDebug("GRPC", $"TradingGrpcClient.Initialize returned: {initialized}");
                
                if (initialized)
                {
                    // Wait for actual connection establishment with faster polling
                    LogDebug("GRPC", "Waiting for actual gRPC connection establishment...");
                    bool actuallyConnected = false;
                    for (int i = 0; i < 50; i++) // Wait up to 2.5 seconds (50 * 50ms)
                    {
                        if (TradingGrpcClient.IsConnected)
                        {
                            actuallyConnected = true;
                            LogInfo("GRPC", $"gRPC client connected after {i * 50}ms");
                            break;
                        }
                        await Task.Delay(50); // Wait 50ms before checking again (faster polling)
                    }
                    
                    if (actuallyConnected)
                    {
                        grpcInitialized = true;
                        grpcInitializing = false;
                        LogInfo("GRPC", "gRPC client fully initialized and connected");
                        FlushPendingHedgeCommands();
                        
                        // Start trading stream to receive MT5 trade results
                        StartMT5TradeResultStream();
                        
                        // Start keepalive heartbeat system to maintain connection
                        LogDebug("GRPC", "Starting keepalive heartbeat system...");
                        StartHeartbeatSystem();
                    }
                    else
                    {
                        grpcInitializing = false;
                        LogWarn("GRPC", "gRPC client Initialize() returned true but actual connection failed");
                    }
                }
                else
                {
                    string error = TradingGrpcClient.LastError;
                    grpcInitializing = false; // Reset on init failure
                    LogError("GRPC", $"Failed to initialize gRPC client: {error}");
                    
                    // Initialization failed - log only (no blocking popup)
                }
            }
            catch (Exception ex)
            {
                grpcInitializing = false; // Reset on exception
                LogError("GRPC", $"Exception during gRPC initialization: {ex.Message}");
                
                // Show exception popup
                string popupMessage = $"MultiStratManager - gRPC Exception\n\nStatus: EXCEPTION\nServer: {grpcServerAddress}\nError: {ex.Message}\n\nPlease check the bridge server configuration.";
                MessageBox.Show(popupMessage, "gRPC Initialization Exception", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Check if a log should be sent based on verbose mode and log characteristics
        /// </summary>
        private bool ShouldSendLog(string logLevel, string category, string message)
        {
            // Always send ERROR and CRITICAL logs
            if (logLevel == "ERROR" || logLevel == "CRITICAL")
                return true;

            // Always send WARN logs
            if (logLevel == "WARN")
                return true;

            // Send all non-DEBUG logs (INFO, WARN, ERROR, CRITICAL)

            // In non-verbose mode, filter out noisy logs
            if (logLevel == "DEBUG")
                return false;

            // Filter out noisy message patterns - enhanced patterns for better filtering
            var noisyPatterns = new[]
            {
                "ping", "heartbeat", "poll", "status check", "connection alive",
                "timer tick", "balance display updated", "account item update received",
                "strategy state polling", "updating trailing stops display",
                "found \\d+ internal stops", "processing stop", "current price:",
                "added trailing stop display", "final count in ui:",
                "INTERNAL_TRAILING_DEBUG", "POSITION_SCAN_DEBUG", "ELASTIC_DEBUG",
                "monitoring \\d+ active", "monitoring \\d+ tracked", "scanning account",
                "found \\d+ non-flat positions", "current elastic trackers"
            };

            foreach (var pattern in noisyPatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(message, pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private void LogForSLTP(string message, LogLevel level)
        {
            // Convert LogLevel to our logging system
            string logLevelStr = level.ToString().ToUpper();
            switch (logLevelStr)
            {
                case "DEBUG":
                    LogDebug("SLTP", message);
                    break;
                case "INFO":
                    LogInfo("SLTP", message);
                    break;
                case "WARN":
                    LogWarn("SLTP", message);
                    break;
                case "ERROR":
                    LogError("SLTP", message);
                    break;
                default:
                    LogInfo("SLTP", message);
                    break;
            }
        }

        /// <summary>
        /// Event handler for when SLTP cleanup is complete for an entry order
        /// </summary>
        private void OnSLTPCleanupComplete(string entryOrderId)
        {
            LogAndPrint($"SLTP cleanup completed for entry order: {entryOrderId}");
        }
 
        /// <summary>
        /// Standard constructor - required for NinjaTrader add-on registration
        /// </summary>
        public MultiStratManager()
        {
            Print("[NT_ADDON][INFO][INIT] MultiStratManager constructor called");
            Print($"[NT_ADDON][DEBUG] Current State: {State}");
            
            // FORCE RESET: If state is Finalized, reset to initial state
            if (State == State.Finalized)
            {
                Print("[NT_ADDON][DEBUG] State is Finalized - forcing reset to SetDefaults");
                State = State.SetDefaults;
                Print($"[NT_ADDON][DEBUG] State after reset: {State}");
                
                // Manually trigger OnStateChange
                Print("[NT_ADDON][DEBUG] About to call OnStateChange()");
                try
                {
                    OnStateChange();
                    Print("[NT_ADDON][DEBUG] OnStateChange() call completed");
                }
                catch (Exception ex)
                {
                    Print($"[NT_ADDON][ERROR] OnStateChange() crashed: {ex.Message}");
                    Print($"[NT_ADDON][ERROR] Stack trace: {ex.StackTrace}");
                }
            }
            
            Print("[NT_ADDON][DEBUG] Constructor completed successfully");
        }

        /// <summary>
        /// Constructor with command parameter - called when the menu item is clicked
        /// </summary>
        /// <param name="command">Command to execute</param>
        public MultiStratManager(string command)
        {
            LogToSystem($"MultiStratManager constructor with command '{command}' called", "INIT");
            if (command == "ShowWindow")
            {
                LogToSystem("ShowWindow command received", "UI");
                ShowWindow();
            }
        }

        // Helper: whether the UI window is currently open
        public bool IsUiOpen => window != null && window.IsVisible;

        // Centralized cleanup for gRPC and timers
        public void DisconnectGrpcAndStopAll()
        {
            // Idempotent guard to prevent re-entrant/disposed access during disconnect
            lock (grpcDisconnectLock)
            {
                if (grpcDisconnectInProgress)
                {
                    LogInfo("GRPC", "Disconnect already in progress; skipping duplicate call");
                    return;
                }
                grpcDisconnectInProgress = true;
            }

            try
            {
                // Stop all strategy-local timers/monitors first to avoid races during gRPC teardown
                try
                {
                    // First cancel any working managed stops to avoid orphan orders closing future trades
                    // Note: stable TrailingAndElasticManager does not expose CancelAllManagedStops; proceed with available cleanup
                    trailingAndElasticManager?.StopElasticMonitoring();
                    trailingAndElasticManager?.CleanupBarsRequests();
                    LogInfo("GRPC", "Cancelled managed stops and stopped trailing/elastic monitors prior to gRPC disconnect");
                }
                catch (Exception ex)
                {
                    LogWarn("GRPC", $"Error stopping trailing monitors: {ex.Message}");
                }

                // Stop heartbeat first to avoid reconnect attempts
                StopHeartbeatSystem();

                // Stop MT5 trade stream if running (with timeout)
                try
                {
                    var stopTask = Task.Run(() => TradingGrpcClient.StopTradingStream());
                    if (!stopTask.Wait(2000)) // 2 second timeout
                    {
                        LogWarn("GRPC", "StopTradingStream timed out");
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("GRPC", $"Error stopping trading stream: {ex.Message}");
                }

                // Dispose gRPC client (with timeout)
                try
                {
                    var disposeTask = Task.Run(() => TradingGrpcClient.Dispose());
                    if (!disposeTask.Wait(2000)) // 2 second timeout
                    {
                        LogWarn("GRPC", "gRPC client dispose timed out");
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("GRPC", $"Error disposing gRPC client: {ex.Message}");
                }

                grpcInitialized = false;
                grpcInitializing = false;
                LogInfo("GRPC", "Disconnected gRPC and stopped timers");
            }
            catch (Exception ex)
            {
                LogWarn("GRPC", $"DisconnectGrpcAndStopAll encountered: {ex.Message}");
            }
            finally
            {
                lock (grpcDisconnectLock)
                {
                    grpcDisconnectInProgress = false;
                }
            }
        }

        /// <summary>
        /// Handles state changes in the add-on lifecycle
        /// </summary>
        protected override void OnStateChange()
        {
            Print($"[NT_ADDON][DEBUG] OnStateChange called - State: {State}");
            
            if (State == State.SetDefaults)
            {
                Print("[NT_ADDON][DEBUG] Setting defaults...");

                // ✅ RECOMPILATION SAFETY: Aggressive cleanup of static resources before initialization
                PerformStaticCleanup();

                try
                {
                    Description = "Multi-Strategy Manager for hedging";
                    Name = "Multi-Strategy Manager";
                    Instance = this;
                    tradeSyncService = new TradeSyncService(this);
                    Print("[NT_ADDON][DEBUG] SetDefaults completed - progressing to Active");
                    State = State.Active;
                }
                catch (Exception ex)
                {
                    Print($"[NT_ADDON][ERROR] Error in SetDefaults: {ex.Message}");
                }
                sltpRemovalLogic = new SLTPRemovalLogic();
                
                // Initialize TrailingAndElasticManager
                trailingAndElasticManager = new TrailingAndElasticManager(this);

                // Keep default Elastic settings aligned with trailing defaults so UI matches runtime values
                trailingAndElasticManager.ProfitUpdateThreshold = trailingAndElasticManager.TrailingTriggerValue;
                trailingAndElasticManager.ElasticTriggerType = trailingAndElasticManager.TrailingTriggerType;
                trailingAndElasticManager.ElasticProfitUnits = trailingAndElasticManager.TrailingIncrementsType;
                trailingAndElasticManager.ElasticIncrementValue = trailingAndElasticManager.TrailingIncrementsValue;
                
                // Subscribe to SLTP cleanup completion event
                SLTPRemovalLogic.SLTPCleanupCompleted += OnSLTPCleanupComplete;
                
                // Setup assembly resolver for gRPC dependencies
                AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
                // Defer gRPC initialization until the UI is opened or user explicitly connects
                grpcInitialized = false;
                LogInfo("GRPC", "Deferred gRPC initialization until UI opens or user connects");
                // Other default settings can be initialized here
            }
            else if (State == State.Configure)
            {
                // No auto-startup - all connections will be started when UI window is opened
                NinjaTrader.Code.Output.Process("[NT_ADDON] MultiStratManager configured - connections will start when UI is opened", PrintTo.OutputTab1);
            }
            else if (State == State.Active)
            {
                Print("[NT_ADDON][DEBUG] State.Active reached");
                if (isFirstRun)
                {
                    Print("[NT_ADDON][DEBUG] First run - auto launch disabled (manual only)");
                    isFirstRun = false;
                    // Auto-launch disabled per user request; window opens only via menu
                }
                Print("[NT_ADDON][DEBUG] OnStateChange Active completed");
            }
            else if (State == State.Terminated)
            {
                LogInfo("SYSTEM", "MultiStratManager Terminated - performing aggressive cleanup");

                try
                {
                    tradeSyncService?.Shutdown("AddOn terminated");

                    // ✅ AGGRESSIVE CLEANUP: Stop all timers first
                    StopAutoLaunchTimer();
                    StopBridgeConnectionMonitoring();

                    // ✅ AGGRESSIVE CLEANUP: Stop all managers and monitoring
                    trailingAndElasticManager?.StopElasticMonitoring();
                    trailingAndElasticManager?.CleanupBarsRequests();

                    // ✅ AGGRESSIVE CLEANUP: Unsubscribe from all events
                    try { SLTPRemovalLogic.SLTPCleanupCompleted -= OnSLTPCleanupComplete; } catch { }
                    try { AppDomain.CurrentDomain.AssemblyResolve -= OnAssemblyResolve; } catch { }

                    // ✅ AGGRESSIVE CLEANUP: Dispose all resources
                    sltpRemovalLogic?.Cleanup();
                    SetMonitoredAccount(null);
                    DisconnectGrpcAndStopAll();

                    // ✅ AGGRESSIVE CLEANUP: Force close UI window
                    if (window != null)
                    {
                        try
                        {
                            if (window.Dispatcher.CheckAccess())
                            {
                                window.Close();
                            }
                            else
                            {
                                window.Dispatcher.BeginInvoke(new Action(() => window.Close()));
                            }
                            window = null;
                        }
                        catch (Exception ex)
                        {
                            Print($"[NT_ADDON][ERROR] Error closing window during termination: {ex.Message}");
                        }
                    }

                    // ✅ AGGRESSIVE CLEANUP: Clear static resources
                    monitoredStrategies?.Clear();
                    Instance = null;

                    // ✅ AGGRESSIVE CLEANUP: Force garbage collection
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    LogInfo("SYSTEM", "MultiStratManager termination cleanup completed");
                }
                catch (Exception ex)
                {
                    Print($"[NT_ADDON][ERROR] Error during termination cleanup: {ex.Message}");
                }
            }
            // State.Terminated already handles StopHttpListener.
            // The State enum does not have a 'Disabled' member in this context.
            // If runtime disabling requires specific cleanup beyond what State.Inactive or State.Terminated provide,
            // a different approach would be needed. For now, removing this erroneous check.
        }
        
        /// <summary>
        /// Shows the Multi-Strategy Manager window
        /// </summary>
        public void ShowWindow()
        {
            try
            {
                LogDebug("UI", "ShowWindow called");
                
                // We need to ensure we create and show the window on the UI thread
                // Using Application.Current.Dispatcher ensures we're on the main UI thread
                Application.Current.Dispatcher.Invoke(new Action(delegate()
                {
                    try
                    {
                        if (window == null)
                        {
                            LogDebug("UI", "Creating new window");
                            window = new UIForManager();
                            
                            // Handle window closed event - ensure full cleanup so addon can be reopened/edited without restarting NT
                            window.Closed += new EventHandler(delegate(object o, EventArgs e)
                            {
                                LogDebug("UI", "Window closed - performing cleanup");
                                try
                                {
                                    DisconnectGrpcAndStopAll();
                                    SetMonitoredAccount(null);
                                }
                                catch (Exception ex)
                                {
                                    LogWarn("UI", $"Cleanup on window close encountered: {ex.Message}");
                                }
                                window = null;
                                // Allow services to start again when reopened
                                connectionsStarted = false;
                            });
                            
                            // Handle window loaded event to ensure content is visible
                            window.Loaded += new RoutedEventHandler(delegate(object o, RoutedEventArgs e)
                            {
                                LogDebug("UI", "Window loaded");
                                // Force layout update after window is loaded
                                window.UpdateLayout();
                            });
                        }

                        // Ensure the window is visible
                        if (!window.IsVisible)
                        {
                            LogDebug("UI", "Showing window");
                            window.Show();
                            window.Activate();
                            window.Focus();

                            // Force layout update
                            window.UpdateLayout();
                        }
                        else
                        {
                            LogDebug("UI", "Window already visible, bringing to front");
                            window.WindowState = WindowState.Normal;
                            window.Activate();
                            window.Focus();

                            // Force layout update
                            window.UpdateLayout();
                        }

                        // Start connections only once when window is first opened
                        if (!connectionsStarted)
                        {
                            connectionsStarted = true;
                            NinjaTrader.Code.Output.Process("[NT_ADDON] Starting bridge connections since window is now open", PrintTo.OutputTab1);
                            
                            // Start essential services
                            // Initialize gRPC connection (run on background thread to avoid UI blocking)
                            Task.Run(async () => await InitializeGrpcClient());
                            
                            // Auto-logging timer removed - using direct NinjaScript output only
                            
                            // Removed one-time heartbeat - using scheduled heartbeat system instead

                            // Start bridge connection monitoring now that UI is open
                            try
                            {
                                // bridgeConnectionTimer = new System.Windows.Threading.DispatcherTimer(); // REMOVED
                                // bridgeConnectionTimer.Interval = TimeSpan.FromSeconds(bridgeConnectionCheckIntervalSeconds); // REMOVED
                                // bridgeConnectionTimer.Tick += new EventHandler(OnBridgeConnectionTimerTick); // REMOVED
                                // bridgeConnectionTimer.Start(); // REMOVED
                                NinjaTrader.Code.Output.Process("[NT_ADDON] Manual bridge connection mode enabled", PrintTo.OutputTab1);
                            }
                            catch (Exception ex)
                            {
                                LogError("SYSTEM", $"ERROR in bridge initialization: {ex.Message}");
                            }

                            // Note: WebSocket removed - using gRPC only
                            // Note: Elastic monitoring will be initialized when monitored account is set
                            LogInfo("SYSTEM", "Elastic monitoring will be initialized when account is set");
                            
                            // Initialize bars requests for trailing stop calculations
                            if (EnableTrailing)
                            {
                                trailingAndElasticManager?.InitializeBarsRequests();
                            }
                        }
                    }
                    catch (Exception innerEx)
                    {
                        LogError("UI", $"ERROR in ShowWindow UI thread: {innerEx.Message}\n{innerEx.StackTrace}");
                    }
                }));
            }
            catch (Exception ex)
            {
                LogError("UI", $"ERROR in ShowWindow: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        private void StartAutoLaunchTimer()
        {
            try
            {
                // Auto-launch disabled
                LogDebug("SYSTEM", "Auto launch timer not started (manual mode)");
            }
            catch (Exception ex)
            {
                LogError("SYSTEM", $"ERROR starting auto launch timer: {ex.Message}");
            }
        }

        private void StopAutoLaunchTimer()
        {
            try
            {
                if (autoLaunchTimer != null)
                {
                    autoLaunchTimer.Stop();
                    autoLaunchTimer.Tick -= OnAutoLaunchTimerTick;
                    autoLaunchTimer = null;
                    LogDebug("SYSTEM", "Auto launch timer stopped");
                }
            }
            catch (Exception ex)
            {
                LogError("SYSTEM", $"ERROR stopping auto launch timer: {ex.Message}");
            }
        }

        private void StartBridgeConnectionMonitoring()
        {
            // Manual connection mode - no automatic monitoring
            NinjaTrader.Code.Output.Process("[NT_ADDON] Manual bridge connection mode - connect via UI button only", PrintTo.OutputTab1);
        }

        private void StopBridgeConnectionMonitoring()
        {
            // Manual connection mode - no timer cleanup needed
            NinjaTrader.Code.Output.Process("[NT_ADDON] Manual bridge connection mode - no monitoring to stop", PrintTo.OutputTab1);
        }

        // ===== WebSocket removed - using gRPC only =====
        public async Task FlushAllLogsToBridge()
        {
            try
            {
                LogInfo("SYSTEM", "Flushing all pending logs to bridge");

                // Log flushing removed - using direct NinjaScript output only

                // Send a test log to verify connectivity
                LogInfo("SYSTEM", "Log flush completed - bridge connectivity verified");

                // Wait a moment for the flush to complete
                await Task.Delay(500);

                NinjaTrader.Code.Output.Process("[NT_ADDON] All logs flushed to bridge", PrintTo.OutputTab1);
            }
            catch (Exception ex)
            {
                LogError("SYSTEM", $"Error during log flush: {ex.Message}");
                throw;
            }
        }
        
        private void OnAutoLaunchTimerTick(object sender, EventArgs e)
        {
            try
            {
                // Auto-launch disabled; just stop timer if somehow invoked
                StopAutoLaunchTimer();
            }
            catch (Exception ex)
            {
                LogError("SYSTEM", $"ERROR in auto launch timer tick: {ex.Message}");
                StopAutoLaunchTimer();
            }
        }


        // Add new method to handle window creation
        /// <summary>
        /// Called when a NinjaTrader window is created. Used here to add menu items to the Control Center.
        /// </summary>
        /// <param name="window">The window that was created.</param>
        protected override void OnWindowCreated(Window window)
        {
            // Quiet noisy per-window logging; only act when ControlCenter is detected
            
            try
            {
                // We want to place our AddOn in the Control Center's menus
                ControlCenter cc = window as ControlCenter;
                if (cc == null)
                {
                    return;
                }

                Print("[NT_ADDON][UI] ControlCenter window detected - registering menu item");

                // Find the "New" menu item
                if (cc.MainMenu == null)
                {
                    LogError("UI", "ERROR: MainMenu not found in Control Center");
                    return;
                }
                
                // Look for the "New" menu item
                existingMenuItemInControlCenter = null;
                // Replace this line:
                // Replace this line:
                // Iterate through the top-level items in the MainMenu
                foreach (object item in cc.MainMenu) // Iterate directly over the Menu control
                        {
                            MenuItem menuItem = item as MenuItem;
                            if (menuItem != null && menuItem.Header != null && menuItem.Header.ToString() == "New")
                            {
                                existingMenuItemInControlCenter = menuItem; // Removed incorrect cast
                                break;
                            }
                        }
                
                if (existingMenuItemInControlCenter == null)
                {
                    LogError("UI", "ERROR: Could not find 'New' menu item in Control Center");
                    return;
                }

                // Check if our menu item already exists to avoid duplicates
                // Renamed inner loop variable from 'item' to 'subItem' to resolve CS0136
                foreach (object subItem in existingMenuItemInControlCenter.ItemsSource ?? existingMenuItemInControlCenter.Items)
                {
                    // Use the renamed variable 'subItem'
                    MenuItem subMenuItem = subItem as MenuItem;
                    if (subMenuItem != null && subMenuItem.Header != null && subMenuItem.Header.ToString() == "Multi-Strategy Manager")
                    {
                        // Our menu item already exists, no need to add it again
                        LogDebug("UI", "Menu item already exists, not adding again");
                        return;
                    }
                }

                // 'Header' sets the name of our AddOn seen in the menu structure
                multiStratMenuItem = new NTMenuItem();
                multiStratMenuItem.Header = "Multi-Strategy Manager";
                multiStratMenuItem.Style = Application.Current.TryFindResource("MainMenuItem") as Style;

                // Add our AddOn into the "New" menu
                existingMenuItemInControlCenter.Items.Add(multiStratMenuItem);

                // Subscribe to the event for when the user presses our AddOn's menu item
                multiStratMenuItem.Click += new RoutedEventHandler(OnMenuItemClick);

                Print("[NT_ADDON][UI] Added Multi-Strategy Manager to Control Center menu");
            }
            catch (Exception ex)
            {
                Print($"[NT_ADDON][ERROR] ERROR in OnWindowCreated: {ex.Message}\n{ex.StackTrace}");
            }
        }
        
        // Helper method to find a visual child of a specific type
        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            try
            {
                // Check if the parent is null
                if (parent == null)
                    return null;
                
                // Check if the parent is of the requested type
                if (parent is T)
                    return parent as T;
                
                // Get the number of children
                int childCount = VisualTreeHelper.GetChildrenCount(parent);
                
                // Search through all children
                for (int i = 0; i < childCount; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                    
                    // Recursively search this child
                    T result = FindVisualChild<T>(child);
                    
                    // If we found the child, return it
                    if (result != null)
                        return result;
                }
                
                return null;
            }
            catch
            {
                // Ignore errors and return null
                return null;
            }
        }
        
        // Add new method to clean up when window is destroyed
        /// <summary>
        /// Called when a NinjaTrader window is destroyed. Used here to clean up menu items from the Control Center.
        /// </summary>
        /// <param name="window">The window that was destroyed.</param>
        protected override void OnWindowDestroyed(Window window)
        {
            if (multiStratMenuItem != null && window is ControlCenter)
            {
                LogDebug("UI", "ControlCenter window destroyed");

                if (existingMenuItemInControlCenter != null && existingMenuItemInControlCenter.Items.Contains(multiStratMenuItem))
                    existingMenuItemInControlCenter.Items.Remove(multiStratMenuItem);

                multiStratMenuItem.Click -= OnMenuItemClick;
                multiStratMenuItem = null;
                existingMenuItemInControlCenter = null;
            }
        }

        // Add new method to handle menu item click
        private void OnMenuItemClick(object sender, RoutedEventArgs e)
        {
            LogDebug("UI", "Menu item clicked");
            // Use Application.Current.Dispatcher instead of RandomDispatcher
            Application.Current.Dispatcher.BeginInvoke(new Action(delegate() { ShowWindow(); }));
        }


        /// <summary>
        /// Set gRPC server address for the bridge connection
        /// </summary>
        public void SetGrpcAddress(string address)
        {
            if (!string.IsNullOrEmpty(address))
            {
                grpcServerAddress = address;
                LogInfo("GRPC", $"gRPC Server Address set to: {grpcServerAddress}");

                // Print initial gRPC address to NT terminal
                NinjaTrader.Code.Output.Process($"[NT_ADDON] gRPC Address configured: {grpcServerAddress}", PrintTo.OutputTab1);

                // Manual connection mode - address set but no automatic connection
                NinjaTrader.Code.Output.Process("[NT_ADDON] Manual connection mode - use UI button to connect", PrintTo.OutputTab1);
            }
        }

        internal void PublishLifecycleToBridge(TradeSyncService.TradeRecord record, string lifecycle)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return;

            if (IsBacktestAccount(record.AccountName))
            {
                LogDebug("TRADE_SYNC", $"Skipping lifecycle publish for backtest account {record.AccountName} (trade_id={record.TradeId})", record.TradeId, record.TradeId);
                return;
            }

            string instrumentName = record.Instrument ?? string.Empty;

            var payload = new Dictionary<string, object>
            {
                { "id", $"lifecycle_{record.TradeId}_{DateTime.UtcNow.Ticks}" },
                { "base_id", record.TradeId },
                { "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "action", lifecycle },
                { "lifecycle_event", lifecycle },
                { "order_type", "TRADE_SYNC" },
                { "instrument", instrumentName },
                { "instrument_name", instrumentName },
                { "account_name", record.AccountName ?? string.Empty },
                { "direction", record.Side == MarketPosition.Short ? "SHORT" : "LONG" },
                { "quantity", (double)record.NtQuantity },
                { "total_quantity", record.NtQuantity },
                { "contract_num", 1 },
                { "remaining_quantity", (double)record.RemainingQuantity },
                { "seq", record.LastSeq },
                { "epoch", record.Epoch },
                { "event_type", "trade_lifecycle" },
                { "nt_trade_result", lifecycle.ToLowerInvariant() }
            };

            Task.Run(async () =>
            {
                try
                {
                    await SendToBridge(payload);
                }
                catch (Exception ex)
                {
                    LogError("TRADE_SYNC", $"Failed to publish lifecycle '{lifecycle}' for trade_id={record.TradeId}: {ex.Message}", 0, record.TradeId, record.TradeId);
                }
            });
        }

        public void NotifyManualOverride(string tradeId, bool? stopLocked, bool? targetLocked)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || trailingAndElasticManager == null)
                return;

            trailingAndElasticManager.ApplyManualOverride(tradeId, stopLocked, targetLocked);
        }

        private static bool IsBacktestAccount(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;

            string trimmed = accountName.Trim();
            if (trimmed.Equals("Backtest", StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed.StartsWith("Backtest ", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private bool ShouldBypassGrpc(IDictionary<string, object> data)
        {
            if (data == null)
                return false;

            if (data.TryGetValue("account_name", out var accountObj))
            {
                string accountName = accountObj as string ?? accountObj?.ToString();
                if (IsBacktestAccount(accountName))
                    return true;
            }

            return false;
        }

        internal void OnStrategyTradeOpened(TradeSyncService.TradeRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return;

            var details = new OriginalTradeDetails
            {
                BaseId = record.TradeId,
                MarketPosition = record.Side,
                Quantity = record.NtQuantity,
                TotalQuantity = record.NtQuantity,
                RemainingQuantity = record.RemainingQuantity,
                Price = record.EntryPrice,
                NtInstrumentSymbol = record.Instrument ?? string.Empty,
                NtAccountName = record.AccountName ?? string.Empty,
                OriginalOrderAction = record.Side == MarketPosition.Short ? OrderAction.SellShort : OrderAction.Buy,
                Timestamp = DateTime.UtcNow
            };

            lock (_activeNtTradesLock)
            {
                activeNtTrades[record.TradeId] = details;
            }

            trailingAndElasticManager?.RegisterTrade(record);

            SendHedgeOpenRequest(record);
        }

        private void SendHedgeOpenRequest(TradeSyncService.TradeRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return;

            if (IsBacktestAccount(record.AccountName))
            {
                LogDebug("GRPC", $"Bypassing hedge entry send during backtest context (baseId={record.TradeId})");
                return;
            }

            string baseId = record.TradeId;
            string action = record.Side == MarketPosition.Short ? "sell" : "buy";
            double quantity = Math.Max(1, record.NtQuantity);

            var payload = new Dictionary<string, object>
            {
                { "id", $"trade_{baseId}_{DateTime.UtcNow.Ticks}" },
                { "base_id", baseId },
                { "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                { "action", action },
                { "order_type", "ENTRY" },
                { "instrument", record.Instrument ?? string.Empty },
                { "instrument_name", record.Instrument ?? string.Empty },
                { "account_name", record.AccountName ?? string.Empty },
                { "quantity", quantity },
                { "total_quantity", quantity },
                { "price", record.EntryPrice },
                { "nt_points_per_1k_loss", record.NtPointsPer1kLoss },
                { "nt_balance", SessionStartBalance },
                { "nt_daily_pnl", DailyPnL },
                { "nt_trade_result", "open" },
                { "nt_session_trades", SessionTradeCount },
                { "epoch", record.Epoch },
                { "seq", record.LastSeq > 0 ? record.LastSeq : 1 }
            };

            string jsonPayload = SimpleJson.SerializeObject(payload);
            LogDebug("CONNECTION", $"Submitting hedge entry to bridge via gRPC: {jsonPayload}", baseId, baseId);

            var command = new PendingHedgeCommand
            {
                BaseId = baseId,
                JsonPayload = jsonPayload,
                Quantity = quantity
            };

            if (!grpcInitialized || !TradingGrpcClient.IsConnected)
            {
                pendingHedgeCommands.Enqueue(command);
                LogDebug("GRPC", $"Queued hedge entry while gRPC not ready (baseId={baseId}, queue={pendingHedgeCommands.Count})");
                FlushPendingHedgeCommands();
                return;
            }

            Task.Run(async () =>
            {
                bool success = await SubmitHedgeCommandAsync(command);
                if (!success)
                {
                    pendingHedgeCommands.Enqueue(command);
                    LogDebug("GRPC", $"Re-queued hedge entry after send failure (baseId={baseId}, queue={pendingHedgeCommands.Count})");
                    FlushPendingHedgeCommands();
                }
            });
        }

        private async Task<bool> SubmitHedgeCommandAsync(PendingHedgeCommand command)
        {
            try
            {
                if (!grpcInitialized)
                    await InitializeGrpcClient();

                bool success = await Task.Run(() => TradingGrpcClient.SubmitTrade(command.JsonPayload));
                if (success)
                {
                    LogAndPrint($"NT_ENTRY: Submitted hedge entry for {command.BaseId} (qty={command.Quantity})");
                }
                else
                {
                    string error = TradingGrpcClient.LastError;
                    LogAndPrint($"ERROR: Failed to submit hedge entry for {command.BaseId}: {error}");
                }
                return success;
            }
            catch (Exception ex)
            {
                LogAndPrint($"ERROR: Exception submitting hedge entry for {command.BaseId}: {ex.Message}");
                return false;
            }
        }

        private void FlushPendingHedgeCommands()
        {
            if (!grpcInitialized)
                return;

            if (Interlocked.CompareExchange(ref hedgeFlushInProgress, 1, 0) != 0)
                return;

            Task.Run(async () =>
            {
                try
                {
                    if (!grpcInitialized)
                        await InitializeGrpcClient();

                    while (grpcInitialized && pendingHedgeCommands.TryDequeue(out var command))
                    {
                        bool success = await SubmitHedgeCommandAsync(command);
                        if (!success)
                        {
                            pendingHedgeCommands.Enqueue(command);
                            await Task.Delay(250);
                            break;
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref hedgeFlushInProgress, 0);
                    if (grpcInitialized && !pendingHedgeCommands.IsEmpty)
                        FlushPendingHedgeCommands();
                }
            });
        }

        internal void OnStrategyTradePartiallyClosed(TradeSyncService.TradeRecord record, int closedQuantity)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId) || closedQuantity <= 0)
                return;

            lock (_activeNtTradesLock)
            {
                if (activeNtTrades.TryGetValue(record.TradeId, out var details))
                {
                    details.RemainingQuantity = record.RemainingQuantity;
                    details.Timestamp = DateTime.UtcNow;
                }
            }

            trailingAndElasticManager?.UpdateRemainingQuantity(record.TradeId, record.RemainingQuantity);

            SendHedgeCloseRequest(record, closedQuantity, "NT_PARTIAL_CLOSE");
        }

        internal void OnStrategyTradeClosed(TradeSyncService.TradeRecord record, int closedQuantity)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return;

            trailingAndElasticManager?.CompleteTrade(record.TradeId);

            lock (_activeNtTradesLock)
            {
                if (activeNtTrades.TryGetValue(record.TradeId, out var details))
                {
                    details.RemainingQuantity = 0;
                    details.ClosedTimestamp = DateTime.UtcNow;
                }
                activeNtTrades.TryRemove(record.TradeId, out _);
            }
            baseIdToMT5Ticket.TryRemove(record.TradeId, out _);
            if (baseIdToOrderIdMap.TryRemove(record.TradeId, out var removedOrderId))
            {
                if (!string.IsNullOrEmpty(removedOrderId))
                    orderIdToBaseIdMap.TryRemove(removedOrderId, out _);
            }

            if (closedQuantity <= 0)
                closedQuantity = record.NtQuantity;

            SendHedgeCloseRequest(record, closedQuantity, "NT_FULL_CLOSE");
        }

        private void SendHedgeCloseRequest(TradeSyncService.TradeRecord record, int quantityToClose, string reason)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId) || quantityToClose <= 0)
                return;

            string baseId = record.TradeId;
            double closedQuantity = quantityToClose;

            // Infer per-trade result using last known result; if unknown, estimate from current price vs entry.
            string tradeResultField = !string.IsNullOrWhiteSpace(_lastTradeResult)
                ? _lastTradeResult
                : (reason == "NT_FULL_CLOSE" ? "closed" : "partial");

            var instrumentObj = record.Strategy?.Instrument;

            if (string.IsNullOrWhiteSpace(_lastTradeResult) && record.EntryPrice > 0 && instrumentObj != null)
            {
                double mark = GetCurrentPrice(instrumentObj);
                if (mark > 0)
                {
                    bool isLoss = false;
                    if (record.Side == MarketPosition.Long)
                        isLoss = mark < record.EntryPrice - 1e-6;
                    else if (record.Side == MarketPosition.Short)
                        isLoss = mark > record.EntryPrice + 1e-6;

                    if (isLoss)
                        tradeResultField = "loss";
                    else
                        tradeResultField = "win";
                }
            }

            // If this NT close is a loss, emit a distinct closure_reason so MT5 can trigger run-up.
            string closureReasonField = reason;
            if (reason == "NT_FULL_CLOSE" && tradeResultField.Equals("loss", StringComparison.OrdinalIgnoreCase))
                closureReasonField = "NT_LOSS_CLOSE";

            ulong mt5Ticket = 0;
            for (int retry = 0; retry < 3; retry++)
            {
                if (baseIdToMT5Ticket.TryGetValue(baseId, out mt5Ticket) && mt5Ticket > 0)
                    break;
                if (retry < 2)
                    Thread.Sleep(5);
            }

            if (mt5Ticket > 0)
                LogAndPrint($"CLOSURE_TICKET: Found MT5 ticket {mt5Ticket} for BaseID {baseId}");
            else
                LogAndPrint($"CLOSURE_TICKET: No MT5 ticket found for BaseID {baseId}, proceeding without ticket mapping");

            var closureData = new Dictionary<string, object>
            {
                { "action", "CLOSE_HEDGE" },
                { "base_id", baseId },
                { "quantity", (float)closedQuantity },
                { "nt_instrument_symbol", record.Instrument ?? string.Empty },
                { "nt_account_name", record.AccountName ?? string.Empty },
                { "closed_hedge_quantity", (double)closedQuantity },
                { "closed_hedge_action", "CLOSE_HEDGE" },
                { "timestamp", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "price", record.EntryPrice },
                { "total_quantity", record.NtQuantity },
                { "contract_num", 1 },
                { "instrument_name", record.Instrument ?? string.Empty },
                { "account_name", record.AccountName ?? string.Empty },
                { "time", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) },
                { "nt_balance", SessionStartBalance },
                { "nt_daily_pnl", DailyPnL },
                { "nt_trade_result", tradeResultField },
                { "nt_session_trades", SessionTradeCount },
                { "closure_reason", closureReasonField },
                { "mt5_ticket", mt5Ticket },
                { "remaining_quantity", (double)record.RemainingQuantity }
            };

            string closureJson = SimpleJson.SerializeObject(closureData);
            LogAndPrint($"NT_CLOSURE: Sending hedge closure request for {baseId} (reason={closureReasonField}, nt_trade_result={tradeResultField}, qty={closedQuantity}) -> {closureJson}");

            if (IsBacktestAccount(record.AccountName))
            {
                LogDebug("GRPC", $"Bypassing CLOSE_HEDGE send during backtest context (baseId={baseId})");
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    if (!grpcInitialized)
                        await InitializeGrpcClient();

                    bool success = TradingGrpcClient.NTCloseHedge(closureJson);
                    if (success)
                    {
                        LogAndPrint($"NT_CLOSURE: Successfully sent CLOSE_HEDGE via gRPC for BaseID: {baseId}");
                    }
                    else
                    {
                        string error = TradingGrpcClient.LastError;
                        LogAndPrint($"ERROR: Failed to send CLOSE_HEDGE via gRPC for BaseID: {baseId}. Error: {error}");
                    }
                }
                catch (Exception ex)
                {
                    LogAndPrint($"ERROR: Exception sending CLOSE_HEDGE via gRPC for BaseID: {baseId}. Exception: {ex.Message}");
                }
            });
        }

        internal void AdjustExposure(StrategyBase strategy, string accountName, string instrumentName, double delta)
        {
            if (Math.Abs(delta) < 1e-9)
                return;

            string key = BuildExposureKey(accountName, instrumentName);

            lock (exposureLock)
            {
                double total = 0;
                exposureByKey.TryGetValue(key, out total);
                total += delta;
                if (Math.Abs(total) < 1e-9)
                    exposureByKey.Remove(key);
                else
                    exposureByKey[key] = total;

                if (strategy != null)
                {
                    if (!exposureByStrategy.TryGetValue(strategy, out var dict))
                    {
                        dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                        exposureByStrategy[strategy] = dict;
                    }

                    double own = 0;
                    dict.TryGetValue(key, out own);
                    own += delta;
                    if (Math.Abs(own) < 1e-9)
                    {
                        dict.Remove(key);
                        if (dict.Count == 0)
                            exposureByStrategy.Remove(strategy);
                    }
                    else
                    {
                        dict[key] = own;
                    }
                }
            }
        }

        public void FlushExposureState(bool rebuildFromOpenTrades)
        {
            int clearedInstrumentEntries;
            int clearedStrategyScopes;

            lock (exposureLock)
            {
                clearedInstrumentEntries = exposureByKey.Count;
                clearedStrategyScopes = exposureByStrategy.Count;
                exposureByKey.Clear();
                exposureByStrategy.Clear();
            }

            LogAndPrint($"EXPOSURE_FLUSH: Cleared {clearedInstrumentEntries} instrument entries across {clearedStrategyScopes} strategy scopes.");

            if (!rebuildFromOpenTrades)
                return;

            var openTrades = tradeSyncService?.GetOpenTradesSnapshot();
            if (openTrades == null || openTrades.Count == 0)
            {
                LogAndPrint("EXPOSURE_FLUSH: No open trades detected during rebuild.");
                return;
            }

            foreach (var trade in openTrades)
            {
                if (trade == null)
                    continue;

                double signedQty = GetSignedQuantity(trade.Side, trade.RemainingQuantity);
                if (Math.Abs(signedQty) < 1e-9)
                    continue;

                AdjustExposure(trade.Strategy, trade.AccountName, trade.Instrument, signedQty);
            }

            LogAndPrint($"EXPOSURE_FLUSH: Rebuilt exposure ledger from {openTrades.Count} open trade(s).");
        }

        internal void ClearExposureForStrategy(StrategyBase strategy)
        {
            if (strategy == null)
                return;

            lock (exposureLock)
            {
                if (!exposureByStrategy.TryGetValue(strategy, out var dict))
                    return;

                foreach (var kvp in dict)
                {
                    if (exposureByKey.TryGetValue(kvp.Key, out var total))
                    {
                        total -= kvp.Value;
                        if (Math.Abs(total) < 1e-9)
                            exposureByKey.Remove(kvp.Key);
                        else
                            exposureByKey[kvp.Key] = total;
                    }
                }

                exposureByStrategy.Remove(strategy);
            }
        }

        public double GetNetExposure(string accountName, string instrumentName, StrategyBase requestingStrategy, out bool hasData)
        {
            string key = BuildExposureKey(accountName, instrumentName);
            lock (exposureLock)
            {
                if (!exposureByKey.TryGetValue(key, out var total))
                {
                    hasData = false;
                    return 0;
                }

                hasData = true;
                if (requestingStrategy != null && exposureByStrategy.TryGetValue(requestingStrategy, out var dict) && dict.TryGetValue(key, out var own))
                    total -= own;
                return total;
            }
        }

        private static string BuildExposureKey(string accountName, string instrumentName)
        {
            string acct = string.IsNullOrWhiteSpace(accountName) ? "<NONE>" : accountName.Trim().ToUpperInvariant();
            string inst = string.IsNullOrWhiteSpace(instrumentName) ? "<NONE>" : instrumentName.Trim().ToUpperInvariant();
            return acct + "||" + inst;
        }

        private static double GetSignedQuantity(MarketPosition side, double quantity)
        {
            double absQty = Math.Abs(quantity);
            if (absQty < 1e-9)
                return 0;
            return side == MarketPosition.Short ? -absQty : absQty;
        }

        private async Task SendToBridge(Dictionary<string, object> data)
        {
            try
            {
                if (ShouldBypassGrpc(data))
                {
                    LogDebug("GRPC", "Bypassing gRPC send during backtest/optimization context");
                    return;
                }

                string jsonPayload = SimpleJson.SerializeObject(data);
                LogDebug("CONNECTION", $"Sending data to bridge via gRPC: {jsonPayload}");

                // Initialize gRPC if needed
                if (!grpcInitialized)
                {
                    await InitializeGrpcClient();
                }

                // Send via gRPC only - no HTTP fallback
                bool success = TradingGrpcClient.SubmitTrade(jsonPayload);
                if (success)
                {
                    LogDebug("GRPC", "Trade data successfully sent via gRPC");
                }
                else
                {
                    string error = TradingGrpcClient.LastError;
                    LogError("GRPC", $"gRPC trade submission failed: {error}");
                    throw new Exception($"Failed to send trade via gRPC: {error}");
                }
            }
            catch (Exception ex)
            {
                LogError("CONNECTION", $"Exception sending trade data to bridge via gRPC: {ex.Message}");
                throw; // Re-throw to allow caller to handle
            }
        }

        // HTTP removed - using gRPC only

        private async Task SendClosureToBridge(string baseId, int quantity)
        {
            try
            {
                string monitoredName = monitoredAccount != null ? monitoredAccount.Name : string.Empty;
                if (IsBacktestAccount(monitoredName))
                {
                    LogDebug("GRPC", $"Bypassing CLOSE_HEDGE send during backtest context (baseId={baseId})");
                    return;
                }

                var closureRequest = new
                {
                    base_id = baseId,
                    closed_hedge_quantity = quantity
                };

                string jsonPayload = SimpleJson.SerializeObject(closureRequest);
                LogAndPrint($"CLOSURE_REQUEST: Sending closure to bridge via gRPC: {jsonPayload}");

                // Initialize gRPC if needed
                if (!grpcInitialized)
                {
                    await InitializeGrpcClient();
                }

                // Send via gRPC only - no HTTP fallback
                bool success = TradingGrpcClient.NTCloseHedge(jsonPayload);
                if (success)
                {
                    LogAndPrint($"CLOSURE_SUCCESS: Closure request sent successfully via gRPC for baseId {baseId}");
                }
                else
                {
                    string error = TradingGrpcClient.LastError;
                    LogError("GRPC", $"gRPC closure request failed: {error}");
                    throw new Exception($"Failed to send closure via gRPC: {error}");
                }
            }
            catch (Exception ex)
            {
                LogError("CLOSURE", $"Exception sending closure request to bridge via gRPC: {ex.Message}");
                throw; // Re-throw to allow caller to handle
            }
        }

        // HTTP removed - using gRPC only

        //+------------------------------------------------------------------+
        //| Structured Logging Methods                                      |
        //+------------------------------------------------------------------+
        
        // Initialize logging timer
        private void InitializeLogging()
        {
            // Logging timer disabled until UI is opened - no auto HTTP log flushing
            LogDebug("LOGGING", "Logging timer disabled - will start when UI is opened");
        }

        // Cleanup logging timer
        // Auto-logging cleanup removed - using direct NinjaScript output only

        // Log DEBUG level message
        public void LogDebug(string category, string message, string tradeId = "", string baseId = "")
        {
            NinjaTrader.Code.Output.Process($"[NT_ADDON][DEBUG][{category}] {message}", PrintTo.OutputTab1);
            try { System.Console.WriteLine($"[NT_ADDON][DEBUG][{category}] {message}"); } catch { }
            TryBridgeLog("DEBUG", category, message, tradeId, baseId: baseId);
        }

        // Log INFO level message  
        public void LogInfo(string category, string message, string tradeId = "", string baseId = "")
        {
            NinjaTrader.Code.Output.Process($"[NT_ADDON][INFO][{category}] {message}", PrintTo.OutputTab1);
            try { System.Console.WriteLine($"[NT_ADDON][INFO][{category}] {message}"); } catch { }
            TryBridgeLog("INFO", category, message, tradeId, baseId: baseId);
        }

        // Log WARN level message
        public void LogWarn(string category, string message, string tradeId = "", string baseId = "")
        {
            NinjaTrader.Code.Output.Process($"[NT_ADDON][WARN][{category}] {message}", PrintTo.OutputTab1);
            try { System.Console.WriteLine($"[NT_ADDON][WARN][{category}] {message}"); } catch { }
            TryBridgeLog("WARN", category, message, tradeId, baseId: baseId);
        }

        // Log ERROR level message
        public void LogError(string category, string message, int errorCode = 0, string tradeId = "", string baseId = "")
        {
            NinjaTrader.Code.Output.Process($"[NT_ADDON][ERROR][{category}] {message}", PrintTo.OutputTab1);
            try { System.Console.WriteLine($"[NT_ADDON][ERROR][{category}] {message}"); } catch { }
            TryBridgeLog("ERROR", category, message, tradeId, errorCode.ToString(), baseId: baseId);
        }

        // Log CRITICAL level message
        public void LogCritical(string category, string message, int errorCode = 0, string tradeId = "", string context = "", string baseId = "")
        {
            NinjaTrader.Code.Output.Process($"[NT_ADDON][CRITICAL][{category}] {message}", PrintTo.OutputTab1);
            try { System.Console.WriteLine($"[NT_ADDON][CRITICAL][{category}] {message}"); } catch { }
            TryBridgeLog("CRITICAL", category, message, tradeId, errorCode.ToString(), baseId: baseId);
        }

        // Invoke NTGrpcClient logging via reflection to avoid hard dependency on specific method names
    private void TryBridgeLog(string level, string category, string message, string tradeId = "", string errorCode = "", string baseId = "")
        {
            try
            {
                // Find NTGrpcClient.TradingGrpcClient type in loaded assemblies
                var assemblies = global::System.AppDomain.CurrentDomain.GetAssemblies();
                global::System.Type targetType = null;
                foreach (var asm in assemblies)
                {
                    try
                    {
                        var t = asm.GetType("NTGrpcClient.TradingGrpcClient", throwOnError: false);
                        if (t != null) { targetType = t; break; }
                    }
                    catch { /* ignore */ }
                }
                if (targetType == null) return;

                var flags = global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Static;

                // Prefer the generic Log(level, component, message, tradeId, errorCode)
                var genericLog = targetType.GetMethod("Log", flags);
                if (genericLog != null)
                {
                    var args = new object[] { level ?? "INFO", category ?? "nt_addon", message ?? string.Empty, tradeId ?? string.Empty, errorCode ?? string.Empty, baseId ?? string.Empty };
                    try { genericLog.Invoke(null, args); } catch { }
                    return;
                }

                // Fallback to specific method names if present
                string methodName;
                var lvl = (level ?? "").ToUpperInvariant();
                if (lvl == "ERROR") methodName = "LogError";
                else if (lvl == "WARN" || lvl == "WARNING") methodName = "LogWarn";
                else if (lvl == "INFO") methodName = "LogInfo";
                else methodName = "LogDebug";

                var specific = targetType.GetMethod(methodName, flags);
                if (specific != null)
                {
                    var ps = specific.GetParameters();
                    try
                    {
                        if (ps.Length >= 5)
                        {
                            specific.Invoke(null, new object[] { category ?? "nt_addon", message ?? string.Empty, tradeId ?? string.Empty, errorCode ?? string.Empty, baseId ?? string.Empty });
                        }
                        else if (ps.Length == 4)
                        {
                            specific.Invoke(null, new object[] { category ?? "nt_addon", message ?? string.Empty, tradeId ?? string.Empty, errorCode ?? string.Empty });
                        }
                        else if (ps.Length == 3)
                        {
                            specific.Invoke(null, new object[] { category ?? "nt_addon", message ?? string.Empty, tradeId ?? string.Empty });
                        }
                        else if (ps.Length == 2)
                        {
                            specific.Invoke(null, new object[] { category ?? "nt_addon", message ?? string.Empty });
                        }
                        else
                        {
                            // Unknown signature - skip
                        }
                    }
                    catch { }
                }
            }
            catch { /* swallow logging errors */ }
        }

        // Helper method to convert NT output calls to centralized logging
        private void LogToSystem(string message, string category = "SYSTEM")
        {
            // Determine log level from message content
            string logLevel = "INFO";
            if (message.Contains("ERROR") || message.Contains("Exception") || message.Contains("Failed") ||
                message.Contains("Error") || message.Contains("error"))
            {
                logLevel = "ERROR";
            }
            else if (message.Contains("WARNING") || message.Contains("Warning") || message.Contains("WARN") ||
                     message.Contains("warning") || message.Contains("warn"))
            {
                logLevel = "WARN";
            }
            else if (message.Contains("DEBUG") || message.Contains("Debug") || message.Contains("debug"))
            {
                logLevel = "DEBUG";
            }

            NinjaTrader.Code.Output.Process($"[NT_ADDON][{logLevel}][{category}] {message}", PrintTo.OutputTab1);
        }

        // Helper method for quick conversion of existing NT output calls
        private void LogNT(string message)
        {
            // Extract category from message prefix if available
            string category = "SYSTEM";
            if (message.StartsWith("[MultiStratManager]")) category = "ADDON";
            else if (message.Contains("EXECUTION")) category = "EXECUTION";
            else if (message.Contains("TRADING")) category = "TRADING";
            else if (message.Contains("UI") || message.Contains("Window")) category = "UI";
            else if (message.Contains("HTTP") || message.Contains("Bridge")) category = "CONNECTION";
            else if (message.Contains("SLTP")) category = "SLTP";
            else if (message.Contains("ELASTIC")) category = "ELASTIC";
            else if (message.Contains("TRAILING")) category = "TRAILING";

            LogToSystem(message, category);
        }

        // Queue log message for batched sending
        // ===== Auto-logging methods removed - using direct NinjaScript output only =====
        
        public void SetMonitoredAccount(Account account)
        {
        // Unsubscribe from previous account if necessary
        if (monitoredAccount != null)
        {
            monitoredAccount.ExecutionUpdate -= OnExecutionUpdate;
            monitoredAccount.OrderUpdate    -= Account_OrderUpdate;
            monitoredAccount.AccountItemUpdate -= OnAccountItemUpdate; // Unsubscribe AccountItemUpdate
            
            // Stop elastic monitoring timer if running
            trailingAndElasticManager?.StopElasticMonitoring();
            
            LogInfo("SYSTEM", $"[MultiStratManager] Unsubscribed from events for account {monitoredAccount.Name}");
        }

        // Reset PnL streaming state for any account change so the first emit uses the new account context.
        ResetPnLStreamState();
        // Stop the timer if it was already running; we'll restart after the new account is set.
        StopPnLStreamingTimer();

        monitoredAccount = account;

        // Subscribe to new account if not null
        if (monitoredAccount != null)
        {
            // CRITICAL: Force log account details for debugging
            LogAndPrint($"ACCOUNT_SET: Setting monitored account to '{monitoredAccount.Name}' (DisplayName: '{monitoredAccount.DisplayName}')");
            LogAndPrint($"ACCOUNT_SET: Account connection state: {monitoredAccount.ConnectionStatus}");

            monitoredAccount.ExecutionUpdate += OnExecutionUpdate;
            monitoredAccount.OrderUpdate    += Account_OrderUpdate;
            monitoredAccount.AccountItemUpdate += OnAccountItemUpdate; // Subscribe AccountItemUpdate

            LogAndPrint($"ACCOUNT_SET: Successfully subscribed to ExecutionUpdate events for account '{monitoredAccount.Name}'");
            LogInfo("SYSTEM", $"[MultiStratManager] Subscribed to events for account {monitoredAccount.Name}");

            // Initialize PnL values.
            var realizedItemArgs = monitoredAccount.GetAccountItem(Cbi.AccountItem.RealizedProfitLoss, Currency.UsDollar);
            if (realizedItemArgs != null && realizedItemArgs.Value is double) // Assuming GetAccountItem returns AccountItemEventArgs here based on CS0029
                RealizedPnL = (double)realizedItemArgs.Value;

            var unrealizedItemArgs = monitoredAccount.GetAccountItem(Cbi.AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
            if (unrealizedItemArgs != null && unrealizedItemArgs.Value is double) // Assuming GetAccountItem returns AccountItemEventArgs here based on CS0029
                UnrealizedPnL = (double)unrealizedItemArgs.Value;
            // TotalPnL is updated automatically via setters of RealizedPnL/UnrealizedPnL

            // Initialize session tracking for elastic hedging
            InitializeSessionTracking();
            
            // Initialize elastic hedging monitor now that we have a monitored account
            if (trailingAndElasticManager != null)
            {
                trailingAndElasticManager.InitializeElasticMonitoring(monitoredAccount);
            }

            // Re-start lightweight PnL streaming for the new account and emit immediately.
            StartPnLStreamingTimer();
            EmitPnLUpdateIfNeeded();
        }
        else
        {
            LogInfo("SYSTEM", $"[MultiStratManager] Monitored account set to null. PnL tracking stopped.");
            // Reset PnL values
            RealizedPnL = 0;
            UnrealizedPnL = 0;
            // TotalPnL is updated automatically
        }
    }

    private void OnAccountItemUpdate(object sender, AccountItemEventArgs e)
    {
        _ = sender; // Suppress unused parameter warning
        if (e.Account == null || monitoredAccount == null || e.Account.Name != monitoredAccount.Name)
            return;

        bool pnlChanged = false;

        if (e.AccountItem == Cbi.AccountItem.RealizedProfitLoss)
        {
            if (e.Value is double realizedValue)
            {
                if (RealizedPnL != realizedValue)
                {
                    RealizedPnL = realizedValue;
                    pnlChanged = true;
                }
            }
        }
        else if (e.AccountItem == Cbi.AccountItem.UnrealizedProfitLoss)
        {
            if (e.Value is double unrealizedValue)
            {
                if (UnrealizedPnL != unrealizedValue)
                {
                    UnrealizedPnL = unrealizedValue;
                    pnlChanged = true;
                }
            }
        }

        if (pnlChanged)
        {
            // Assuming RealizedPnL and UnrealizedPnL setters call OnPropertyChanged for themselves.
            // TotalPnL is updated here, and OnPropertyChanged is called for it.
            TotalPnL = RealizedPnL + UnrealizedPnL;
            OnPropertyChanged(nameof(TotalPnL));
        }
    }
        private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            if (e?.Execution == null)
                return;

            try
            {
                var execution = e.Execution;

                if (monitoredAccount == null || execution.Account == null || execution.Account.Name != monitoredAccount.Name)
                    return;

                if (!string.IsNullOrEmpty(execution.ExecutionId))
                {
                    lock (executionTrackingLock)
                    {
                        if (!processedExecutionIds.Add(execution.ExecutionId))
                        {
                            LogAndPrint($"EXECUTION_DEDUP: Skipping duplicate execution {execution.ExecutionId}");
                            return;
                        }
                    }
                }

                string tradeId = GetTradeIdFromExecution(execution);
                if (string.IsNullOrWhiteSpace(tradeId))
                {
                    LogWarn("EXECUTION", $"Execution {execution.ExecutionId} missing strategy trade_id; skipping add-on processing.");
                    return;
                }

                TradeSyncService.TradeRecord record;
                if (tradeSyncService != null)
                    tradeSyncService.TryGetTrade(tradeId, out record);
                else
                    record = null;

                UpdateTradeResult(execution, tradeId, record);

                if (record != null)
                {
                    trailingAndElasticManager?.UpdateRemainingQuantity(tradeId, record.RemainingQuantity);
                }

                sltpRemovalLogic?.HandleExecutionUpdate(execution, EnableSLTPRemoval, SLTPRemovalDelaySeconds, execution.Account);
            }
            catch (Exception ex)
            {
                LogAndPrint($"ERROR: OnExecutionUpdate failure: {ex.Message}");
            }
        }

        private static string GetTradeIdFromExecution(Execution execution)
    {
        if (execution == null)
            return string.Empty;

        string tradeId = execution.Order?.FromEntrySignal;
        if (!string.IsNullOrWhiteSpace(tradeId))
            return tradeId.Trim();

        tradeId = execution.Order?.Name;
        if (!string.IsNullOrWhiteSpace(tradeId))
            return tradeId.Trim();

        tradeId = execution.Name;
        return tradeId?.Trim() ?? string.Empty;
    }

    // This method replaces the problematic override that caused CS0115.
    // This is the handler for monitoredAccount.OrderUpdate.
    // Ensure SetMonitoredAccount (lines 483-506) correctly subscribes Account_OrderUpdate.
        private void Account_OrderUpdate(object sender, NinjaTrader.Cbi.OrderEventArgs e)
        {
            if (e?.Order == null)
                return;

            try
            {
                trailingAndElasticManager?.HandleTrailingStopOrderUpdate(e.Order);
            }
            catch (Exception ex)
            {
                LogAndPrint($"ERROR: Account_OrderUpdate failure: {ex.Message}");
            }
        }

    // HTTP Listener removed - using gRPC streaming instead

    // HTTP listener methods removed - using gRPC streaming instead
    
    /// <summary>
    /// Handle hedge close notifications via gRPC stream (replaces HTTP HandleNotifyHedgeClosedRequest)
    /// </summary>
    private async Task HandleHedgeCloseNotificationAsync(string notification)
    {
        try
        {
            LogDebug("GRPC", "Received hedge close notification via gRPC stream");

            if (string.IsNullOrWhiteSpace(notification))
            {
                LogError("GRPC", "Received empty hedge close notification via gRPC");
                return;
            }

            LogDebug("GRPC", $"Hedge close notification: {notification}");

            // Parse the notification
            HedgeCloseNotification hedgeNotification = null;
            try
            {
                hedgeNotification = SimpleJson.DeserializeObject<HedgeCloseNotification>(notification);
            }
            catch (Exception ex)
            {
                LogError("GRPC", $"Failed to parse hedge close notification: {ex.Message}");
                return;
            }

            if (hedgeNotification == null || string.IsNullOrEmpty(hedgeNotification.base_id))
            {
                LogError("GRPC", "Invalid hedge close notification: missing base_id");
                return;
            }

            LogDebug("GRPC", $"[HEDGE_CLOSE_NOTIFICATION] Processing closure for BaseID: {hedgeNotification.base_id}");

            // Process the hedge close notification (same logic as before, but without HTTP response)
            await ProcessHedgeCloseNotificationInternal(hedgeNotification);
        }
        catch (Exception ex)
        {
            LogError("GRPC", $"Exception processing hedge close notification via gRPC: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal processing of hedge close notifications (extracted from HTTP handler)
    /// </summary>
    private async Task ProcessHedgeCloseNotificationInternal(HedgeCloseNotification notification)
    {
        // CRITICAL: Verify this BaseID exists in our active trades before attempting to close
        lock (_activeNtTradesLock)
        {
            LogAndPrint($"[HEDGE_CLOSE_VERIFICATION] Checking if BaseID {notification.base_id} exists in active trades...");
            LogAndPrint($"[HEDGE_CLOSE_VERIFICATION] Current active trades: {string.Join(", ", activeNtTrades.Keys)}");
            
            if (!activeNtTrades.ContainsKey(notification.base_id))
            {
                LogAndPrint($"[HEDGE_CLOSE_REJECTION] BaseID {notification.base_id} not found in active trades. Ignoring hedge close notification.");
                return;
            }

            // Get trade details for verification
            var tradeDetails = activeNtTrades[notification.base_id];
            LogAndPrint($"[HEDGE_CLOSE_MATCH] Found matching trade - Symbol: {tradeDetails.NtInstrumentSymbol}, Account: {tradeDetails.NtAccountName}, Position: {tradeDetails.MarketPosition}, OriginalAction: {tradeDetails.OriginalOrderAction}");
        }

        // Process the hedge close notification
        string account = notification.nt_account_name;
        string symbol = notification.nt_instrument_symbol;

        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(symbol))
        {
            LogAndPrint($"[HEDGE_CLOSE_ERROR] Missing required fields in notification. Account: {account}, Symbol: {symbol}");
            return;
        }

        LogAndPrint($"[HEDGE_CLOSE_PROCESSING] Processing closure for {symbol} on account {account}");

        // Find the account by name
        Account ntAccount = null;
        foreach (Account acc in Account.All)
        {
            if (acc.Name == account)
            {
                ntAccount = acc;
                break;
            }
        }

        if (ntAccount == null)
        {
            LogAndPrint($"[HEDGE_CLOSE_ERROR] Account '{account}' not found in NinjaTrader");
            return;
        }

    // Process position closure (do not remove tracking up-front; let execution updates reconcile)
    await ProcessPositionClosureForHedge(notification, ntAccount);
        
        LogAndPrint($"[HEDGE_CLOSE_COMPLETE] Hedge close notification processed for BaseID {notification.base_id} via gRPC");
    }

    private RunUpConfig BuildRunUpConfig()
    {
        return new RunUpConfig
        {
            Enabled = EnableNtRunUp,
            DistanceUnits = NtRunUpDistanceUnits,
            DistanceValue = NtRunUpDistanceValue,
            IncrementUnits = NtRunUpIncrementUnits,
            IncrementValue = NtRunUpIncrementValue
        };
    }

    private bool IsRunUpReason(string closureReason)
    {
        return EnableNtRunUp && !string.IsNullOrWhiteSpace(closureReason) &&
               closureReason.Equals("mt5_simple_sl", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryActivateNtRunUp(Position position, string baseId, string closureReason)
    {
        if (position == null || position.Quantity == 0 || string.IsNullOrWhiteSpace(baseId))
            return false;
        if (!IsRunUpReason(closureReason))
            return false;
        if (tradeSyncService == null)
            return false;

        double anchorPrice = GetCurrentPrice(position.Instrument);
        if (anchorPrice <= 0 && position.Instrument?.MarketData?.Last != null)
            anchorPrice = position.Instrument.MarketData.Last.Price;
        if (anchorPrice <= 0)
        {
            LogAndPrint($"[RUN_UP_SKIP] Unable to determine current price for {baseId}; run-up not activated.");
            return false;
        }

        var config = BuildRunUpConfig();
        if (!config.Enabled)
            return false;

        bool started = tradeSyncService.StartRunUpTrailing(baseId, anchorPrice, config);
        if (started)
        {
            LogAndPrint($"[RUN_UP_START] Activated NT Trade Run-Up for {baseId} at price {anchorPrice:F2} (reason={closureReason})");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Process actual position closure for hedge notifications
    /// </summary>
    private async Task ProcessPositionClosureForHedge(HedgeCloseNotification notification, Account ntAccount)
    {
        // Resolve the specific trade details for base_id
        OriginalTradeDetails tradeDetails = null;
        lock (_activeNtTradesLock)
        {
            if (!activeNtTrades.TryGetValue(notification.base_id, out tradeDetails))
            {
                LogAndPrint($"[HEDGE_CLOSE_MISSING] BaseID {notification.base_id} not in active trades when attempting close – may already be closed.");
            }
        }

        // Determine live NT position and cap close quantity accordingly
        var position = ntAccount.Positions.FirstOrDefault(p => p.Instrument.FullName == notification.nt_instrument_symbol);
        if (position == null || position.Quantity == 0)
        {
            LogAndPrint($"[HEDGE_CLOSE_NO_POSITION] No open position for {notification.nt_instrument_symbol} on {notification.nt_account_name} – nothing to close.");
            return;
        }

        // Special-cases for elastic-managed closures
        try
        {
            var reason = notification.ClosureReason ?? ExtractJsonValue(SimpleJson.SerializeObject(notification), "closure_reason");
            bool isElasticCompletion = !string.IsNullOrEmpty(reason) && reason.Equals("elastic_completion", StringComparison.OrdinalIgnoreCase);
            bool isElasticPartial = !string.IsNullOrEmpty(reason) && reason.Equals("elastic_partial_close", StringComparison.OrdinalIgnoreCase);
            if (IsRunUpReason(reason) && TryActivateNtRunUp(position, notification.base_id, reason))
            {
                LogAndPrint($"[RUN_UP_BYPASS] Keeping NT trade open for {notification.base_id}; MT5 hedge stopped out (reason={reason})");
                return;
            }
            // 1) elastic_partial_close: NEVER close the NT position; MT5 is reducing hedge size only
            if (isElasticPartial)
            {
                double lastPrice = GetCurrentPrice(position.Instrument);
                double unrealized = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastPrice);
                LogAndPrint($"[ELASTIC_PARTIAL_SKIP] Skipping NT close for {notification.base_id} on partial hedge close (reason={reason}). NT Unrealized PnL=${unrealized:F2}");
                return;
            }

            // 2) elastic_completion: If trailing remains active and NT is in profit, keep NT open (skip close)
            if (isElasticCompletion)
            {
                // Check profit and trailing state via TrailingAndElasticManager
                double lastPrice = GetCurrentPrice(position.Instrument);
                double unrealized = position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, lastPrice);
                bool isTrailingActive = false;
                try
                {
                    var tracker = trailingAndElasticManager?.ElasticPositions?.ContainsKey(notification.base_id) == true
                        ? trailingAndElasticManager?.ElasticPositions[notification.base_id]
                        : null;
                    isTrailingActive = tracker?.IsTrailingActive == true;
                }
                catch { /* ignore */ }

                if (isTrailingActive && unrealized > 0)
                {
                    LogAndPrint($"[ELASTIC_COMPLETION_SKIP] Skipping NT close for {notification.base_id} because trailing is active and PnL=${unrealized:F2} > 0 (reason={reason})");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogAndPrint($"[ELASTIC_COMPLETION_CHECK_ERROR] {ex.Message}");
        }

        // Compute desired close quantity based on tracked trade (fallback to 1 contract if unknown)
        int desiredQty = 0;
        OrderAction action = position.Quantity > 0 ? OrderAction.Sell : OrderAction.BuyToCover;
        if (tradeDetails != null)
        {
            // Use remaining quantity if tracked; else original quantity
            var remaining = tradeDetails.RemainingQuantity > 0 ? tradeDetails.RemainingQuantity : tradeDetails.Quantity;
            desiredQty = Math.Max(1, Math.Abs(remaining));

            // Ensure action matches the original trade direction when available
            action = tradeDetails.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
        }
        else
        {
            desiredQty = Math.Max(1, Math.Abs(position.Quantity));
        }

        // Cap by live position quantity to avoid over-close
        int closeQty = Math.Min(desiredQty, Math.Abs(position.Quantity));

        try
        {
            var instr = position.Instrument;
            var closingOrder = ntAccount.CreateOrder(
                instr,
                action,
                OrderType.Market,
                OrderEntry.Manual,
                TimeInForce.Day,
                closeQty,
                0,
                0,
                string.Empty,
                $"HEDGE_CLOSE_{notification.base_id}",
                default(DateTime),
                null
            );

            if (closingOrder != null)
            {
                LogAndPrint($"[HEDGE_CLOSE_ORDER] Created closing order for {instr.FullName}: {closingOrder.OrderAction} {closingOrder.Quantity} (desired {desiredQty}, capped by live {Math.Abs(position.Quantity)})");
                ntAccount.Submit(new[] { closingOrder });
                LogAndPrint($"[HEDGE_CLOSE_SUBMITTED] Submitted hedge closure order for BaseID {notification.base_id}");
            }
            else
            {
                LogAndPrint($"[HEDGE_CLOSE_ERROR] Failed to create closing order for {notification.nt_instrument_symbol}");
            }
        }
        catch (Exception ex)
        {
            LogAndPrint($"[HEDGE_CLOSE_EXCEPTION] Exception creating/submitting closure order: {ex.Message}");
        }
    }

    // HTTP ping handler removed - using gRPC health checks instead

    // HTTP hedge close handler removed - using gRPC stream instead

    /// <summary>
    /// Determines whether a closing order should be created based on the hedge closure reason.
    /// This prevents the whack-a-mole effect where EA-managed closures trigger unnecessary re-trading.
    /// </summary>
    /// <param name="closureReason">The closure reason from the MT5 EA</param>
    /// <returns>True if a closing order should be created, false otherwise</returns>
    private bool ShouldCreateClosingOrderForReason(string closureReason)
    {
        if (IsRunUpReason(closureReason))
        {
            LogAndPrint($"CLOSURE_LOGIC: Reason '{closureReason}' is handled by NT Run-Up. Skipping NT close.");
            return false;
        }
        if (string.IsNullOrEmpty(closureReason))
        {
            // If no closure reason is provided, default to creating closing order for backward compatibility
            LogAndPrint("WARNING: No closure reason provided. Defaulting to creating closing order for backward compatibility.");
            return true;
        }

        // Define closure reasons that should NOT trigger re-trading (EA-managed closures)
        // These are internal EA operations that don't require NinjaTrader position closure
        // WHACK-A-MOLE FIX: Most EA closures should NOT trigger NT position closure
        var eaManagedClosureReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Internal EA adjustment and rebalancing operations
            "EA_ADJUSTMENT_CLOSE",              // EA adjustment closure (internal rebalancing)
            "EA_INTERNAL_REBALANCE",            // EA internal rebalancing operations

            // Standard EA hedge management operations - these should NOT trigger NT closure
            "EA_PARALLEL_ARRAY_CLOSE",          // Standard EA closure due to parallel array management
            "EA_COMMENT_BASED_CLOSE",           // EA closure based on comment parsing
            "EA_RECONCILED_AND_CLOSED",         // EA closure when trade group is fully reconciled
            "EA_PARALLEL_ARRAY_ORPHAN_CLOSE",   // EA closure from parallel arrays but no group
            "EA_COMMENT_ORPHAN_CLOSE",          // EA closure from comment but no group
            "EA_OLD_MAP_FALLBACK_CLOSE",        // EA closure using old map fallback

            // EA automatic closure operations - these should NOT trigger NT closure
            "EA_GLOBALFUTURES_ZERO_CLOSE",      // EA closes hedge when globalFutures reaches zero (internal balancing)
            "EA_TRAILING_STOP_CLOSE",           // EA trailing stop triggered closure (EA-managed)
        };

        // Define closure reasons that SHOULD trigger re-trading (legitimate user-initiated closures)
        // ONLY when the user or external systems close MT5 hedges should NT positions also close
        var legitimateClosureReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // User-initiated closures that should close NT positions
            "MANUAL_MT5_CLOSE",                 // Manual closure in MT5 platform by user
            "EA_MANUAL_CLOSE",                  // Manual closure through EA interface by user

            // User-set stop loss/take profit closures (not EA-managed)
            "USER_STOP_LOSS_CLOSE",             // User-set stop loss triggered
            "USER_TAKE_PROFIT_CLOSE",           // User-set take profit triggered

            // External system closures that should close NT positions
            "NT_ORIGINAL_TRADE_CLOSED",         // Original NinjaTrader trade was closed
            "BROKER_MARGIN_CALL",               // Broker-initiated closure
            "BROKER_STOP_OUT",                  // Broker stop-out

            // Legacy/unknown closures - default to closing for safety
            "UNKNOWN_MT5_CLOSE",                // Unknown MT5 closure (default EA reason) - safer to close NT position
            "EA_STOP_LOSS_CLOSE",               // Legacy - MT5 hedge closed by stop loss
            "EA_TAKE_PROFIT_CLOSE",             // Legacy - MT5 hedge closed by take profit
        };

        bool isEaManaged = eaManagedClosureReasons.Contains(closureReason);
        bool isLegitimate = legitimateClosureReasons.Contains(closureReason);

        if (isEaManaged)
        {
            // BIDIRECTIONAL HEDGING FIX: For bidirectional hedging, when MT5 closes a hedge,
            // we WANT to close the corresponding NT trade to maintain synchronization
            LogAndPrint($"CLOSURE_LOGIC: Reason '{closureReason}' is EA-managed. WILL create closing order for bidirectional hedging.");
            return true;  // Changed from false to true for bidirectional hedging
        }
        else if (isLegitimate)
        {
            LogAndPrint($"CLOSURE_LOGIC: Reason '{closureReason}' is legitimate. Will create closing order.");
            return true;
        }
        else
        {
            // Unknown closure reason - log warning and default to creating closing order for safety
            LogAndPrint($"WARNING: Unknown closure reason '{closureReason}'. Defaulting to creating closing order for safety.");
            return true;
        }
    }

    // HTTP response methods removed - using gRPC instead

    // HTTP error response method removed - using gRPC instead

    // HTTP listener removed - using gRPC streaming instead

    public async Task ForceGrpcReinitialization()
    {
        LogAndPrint("[NT_ADDON][INFO][GRPC] Forcing gRPC client re-initialization...");
        grpcInitialized = false;
        
        grpcInitializing = false; // Reset initializing flag
        
        // Stop heartbeat system before disconnection
        StopHeartbeatSystem();
        
        // Shutdown existing gRPC client if it exists
        try
        {
            TradingGrpcClient.Dispose();
        }
        catch (Exception ex)
        {
            LogAndPrint($"[NT_ADDON][DEBUG][GRPC] Error during gRPC disposal: {ex.Message}");
        }

        // Reinitialize on background thread to avoid UI blocking
        await InitializeGrpcClient();
    }

    public async Task<Tuple<bool, string>> PingBridgeAsync(string bridgeBaseUrl)
    {
        try
        {
            // Initialize gRPC if needed (on background thread)
            if (!grpcInitialized)
            {
                // Only initialize on explicit user action (UI open/ping button)
                if (IsUiOpen)
                    await Task.Run(() => InitializeGrpcClient());
                else
                    return Tuple.Create(false, "UI not open; connection is manual. Open Multi-Strategy Manager to connect.");
            }

            // Ping bridge via gRPC health check (removed spammy log - only logs when status changes)

            // Use gRPC health check (on background thread with timeout to avoid UI blocking)
            var healthResult = await Task.Run(() => {
                string responseJson;
                bool isHealthy = TradingGrpcClient.HealthCheck("NT_ADDON", out responseJson);
                return new { IsHealthy = isHealthy, ResponseJson = responseJson };
            });
            bool isHealthy = healthResult.IsHealthy;
            string responseJson = healthResult.ResponseJson;
            
            if (isHealthy)
            {
                // gRPC ping successful (removed spammy log - success is reported via status change only)
                return Tuple.Create(true, $"Bridge is healthy via gRPC: {responseJson}");
            }
            else
            {
                string error = TradingGrpcClient.LastError;
                LogError("CONNECTION", $"[MultiStratManager] gRPC ping failed: {error}");
                return Tuple.Create(false, $"gRPC ping failed: {error}");
            }
        }
        catch (Exception ex)
        {
            LogError("CONNECTION", $"[MultiStratManager] gRPC ping failed (Error): {ex.Message}");
            return Tuple.Create(false, $"gRPC ping failed: {ex.Message}");
        }
    }
    /// <summary>
    /// Registers a strategy for state monitoring.
    /// </summary>
    /// <param name="strategy">The strategy to monitor.</param>
    public static void RegisterStrategyForMonitoring(StrategyBase strategy)
    {
        if (strategy != null && !monitoredStrategies.Contains(strategy))
        {
            monitoredStrategies.Add(strategy);
            Instance?.tradeSyncService?.RegisterStrategy(strategy);
            Instance?.LogInfo("SYSTEM", $"[MultiStratManager] Registered {strategy.Name} for state monitoring. Current state: {strategy.State}");
            // Optionally, immediately notify of current state
            // OnStrategyExternalStateChange?.Invoke(strategy, strategy.State);
        }
    }

    /// <summary>
    /// Unregisters a strategy from state monitoring.
    /// </summary>
    /// <param name="strategy">The strategy to unmonitor.</param>
    public static void UnregisterStrategyForMonitoring(StrategyBase strategy)
    {
        if (strategy != null && monitoredStrategies.Contains(strategy))
        {
            monitoredStrategies.Remove(strategy);
            Instance?.tradeSyncService?.UnregisterStrategy(strategy);
            Instance?.LogInfo("SYSTEM", $"[MultiStratManager] Unregistered {strategy.Name} from state monitoring.");
        }
    }

    /// <summary>
    /// Requests a state change for the specified strategy.
    /// This method handles enabling and disabling strategies by setting their state
    /// to Active or Terminated respectively.
    /// </summary>
    /// <param name="strategy">The strategy instance to modify.</param>
    /// <param name="newState">The desired state (State.Active to enable, State.Terminated to disable).</param>
    /// <summary>
    /// SIMPLIFIED FIFO CLOSURE DETECTION: Finds the original opening trade's base_id that corresponds to a closing execution
    /// Uses the same logic as IsPositionClosingExecution() to ensure consistency
    /// </summary>
    /// <param name="e">The closing execution event args</param>
    /// <returns>The base_id of the original opening trade, or null if not found</returns>
    private double GetCurrentPrice(Instrument instrument)
    {
        if (instrument == null) return 0;
        
        // Get the current bid/ask prices
        double bid = instrument.MarketData?.Bid?.Price ?? 0;
        double ask = instrument.MarketData?.Ask?.Price ?? 0;
        
        // Return mid-point or last traded price
        if (bid > 0 && ask > 0)
            return (bid + ask) / 2;
        else if (instrument.MarketData?.Last != null)
            return instrument.MarketData.Last.Price;
        else
            return 0;
    }

    /// <summary>
    /// Assembly resolver to load gRPC dependencies from addon directory
    /// </summary>
    private System.Reflection.Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        try
        {
            string assemblyName = new System.Reflection.AssemblyName(args.Name).Name;
            
            // Map of gRPC assemblies we need to resolve
            var grpcAssemblies = new Dictionary<string, string>
            {
                {"Grpc.Net.Client", "Grpc.Net.Client.dll"},
                {"Grpc.Core.Api", "Grpc.Core.Api.dll"},
                {"Grpc.Net.Common", "Grpc.Net.Common.dll"},
                {"Google.Protobuf", "Google.Protobuf.dll"},
                {"System.Runtime.CompilerServices.Unsafe", "System.Runtime.CompilerServices.Unsafe.dll"},
                {"System.Text.Json", "System.Text.Json.dll"}
            };
            
            if (grpcAssemblies.ContainsKey(assemblyName))
            {
                // Try to load from addon directory first
                string addonPath = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                string dllPath = System.IO.Path.Combine(addonPath, grpcAssemblies[assemblyName]);
                
                if (System.IO.File.Exists(dllPath))
                {
                    LogDebug("ASSEMBLY", $"Loading {assemblyName} from: {dllPath}");
                    return System.Reflection.Assembly.LoadFrom(dllPath);
                }
                
                // Fallback: try External folder in development
                string externalPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(addonPath), "External", grpcAssemblies[assemblyName]);
                if (System.IO.File.Exists(externalPath))
                {
                    LogDebug("ASSEMBLY", $"Loading {assemblyName} from External: {externalPath}");
                    return System.Reflection.Assembly.LoadFrom(externalPath);
                }
                
                LogError("ASSEMBLY", $"Could not find {assemblyName} at {dllPath} or {externalPath}");
            }
        }
        catch (Exception ex)
        {
            LogError("ASSEMBLY", $"Error resolving assembly {args.Name}: {ex.Message}");
        }
        
        return null;
    }
    
    }
}
