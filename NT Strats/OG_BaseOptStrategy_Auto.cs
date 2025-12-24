#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
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
    public class BaseOptStrategyAuto : Strategy, ITradeSyncParticipant, IRunUpParticipant
    {
        public class ConnectedAccountNameConverter : StringConverter
        {
            public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
            public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => false;

            public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
            {
                var ordered = GetAvailableAccountNames();
                return new StandardValuesCollection(ordered);
            }
        }

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
        private string lastStatusText;
        private bool lastStatusHealthy;
        private bool lastStatusHasPnLLines;
        private bool lastStatusPnlNegative;
        private bool tradeSyncWarned;
        private readonly Dictionary<string, MultiEntrySyncGroup> multiEntrySyncGroups = new Dictionary<string, MultiEntrySyncGroup>(StringComparer.OrdinalIgnoreCase);

        private bool desyncHoldActive;
        private DateTime desyncHoldActivatedAt = DateTime.MinValue;

        private bool accountOverrideApplied;
        private bool accountOverrideLoggedUnavailable;
        private DateTime lastAccountDiagnosticsAt = DateTime.MinValue;

        private bool dailyPnLLimitHalted;
        private string dailyPnLLimitStatusText;
        private string dailyPnLLimitType;
        private double dailyPnLLimitTriggeredPnL;
        private DateTime dailyPnLLimitTriggeredAt = DateTime.MinValue;
        private DateTime dailyPnLLimitLastEnforceAttemptAt = DateTime.MinValue;
        private DateTime dailyPnLLimitLastEnforceLogAt = DateTime.MinValue;
        private DateTime dailyPnLLimitLastManualResetAt = DateTime.MinValue;
        private DateTime dailyPnLLimitProfitCandidateAt = DateTime.MinValue;
        private double dailyPnLLimitProfitCandidatePnL = 0.0;
        private const double DailyPnLLimitProfitConfirmSeconds = 0.75;

        private bool manualHaltActive;
        private string manualHaltStatusText;
        private DateTime manualHaltActivatedAt = DateTime.MinValue;

        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private RowDefinition chartTraderButtonsRow;
        private StackPanel chartTraderButtonPanel;
        private Button manualFlattenButton;
        private Button manualResumeButton;
        private bool chartTraderButtonsAdded;

        private class TradeRuntimeState
        {
            public string TradeId;
            public string SyncTradeId;
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
            public bool SyntheticLogEmitted;
            public bool Bootstrapped;
            public bool AllowOpenPublish;
            public bool PendingClosePublish;
            public Order EntryOrder;
            public Order StopOrder;
            public Order TargetOrder;
            public int ProtectionRetryCount;
            public DateTime LastProtectionRetry;
            public int ProtectionRearmCount;
            public DateTime LastProtectionRearm;
        }

        private class MultiEntrySyncGroup
        {
            public string TradeId;
            public int TotalQuantity;
            public int LastPublishedRemaining;
            public bool OpenPublished;
            public bool ClosedPublished;
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
                TradesPerEntry = 1;
                TreatMultiEntryAsSingleTrade = false;

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

                EnableDailyPnLLimits = false;
                DailyLossLimit = -2000;
                DailyProfitLimit = 840;

                ExecutionAccountOverride = string.Empty;
                LogAccountDiagnostics = false;
                accountOverrideApplied = false;
                accountOverrideLoggedUnavailable = false;

                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                activeTradeId = null;
            }
            else if (State == State.Configure)
            {
                if (BarsArray.Length == 1)
                {
                    AddDataSeries(BarsPeriodType.Tick, 1);
                }

                EntriesPerDirection = Math.Max(1, TradesPerEntry);

                    // optional: log parameters once per iteration for diagnostics
                if (Debug)
                {
                    StrategyLogDebug($"PARAMS: Bias={Bias}, MinLong={MinSignalsToEnterLong}, MinShort={MinSignalsToEnterShort}, TradesPerEntry={TradesPerEntry}, TreatMultiEntryAsSingleTrade={TreatMultiEntryAsSingleTrade}, UseSMA={UseSMA}, SmaPeriod={SmaPeriod}, UseEMA={UseEMA}, EmaFast={EmaFast}, EmaSlow={EmaSlow}, UseRSI={UseRSI}, RsiPeriod={RsiPeriod}, RsiSmooth={RsiSmooth}, RsiLong={RsiLongThreshold}, RsiShort={RsiShortThreshold}, UseMACD={UseMACD}, MacdFast={MacdFast}, MacdSlow={MacdSlow}, MacdSmooth={MacdSmooth}, AtrPeriod={AtrPeriod}, StopType={StopType}, StopTicks={StopTicks}, AtrStopMult={AtrStopMult}, TargetType={TargetType}, TargetTicks={TargetTicks}, AtrTargetMult={AtrTargetMult}, UseDemaAtrTrailing={UseDemaAtrTrailing}, DemaAtrPeriod={DemaAtrPeriod}, DemaAtrMultiplier={DemaAtrMultiplier}, DemaAtrActivationMode={DemaAtrActivationMode}, DemaAtrActivationValue={DemaAtrActivationValue}, UseBreakEven={UseBreakEven}, BETriggerTicks={BreakEvenTriggerTicks}, BEPlusTicks={BreakEvenPlusTicks}");
                }

                if (tradeStates == null)
                    tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                else
                    tradeStates.Clear();

                activeTradeId = null;
                openTradeOrder.Clear();
                multiEntrySyncGroups.Clear();

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
                UpdateStatusLabel("Loading data... waiting for realtime", false);
            }
            else if (State == State.Historical)
            {
                TryInitializeChartTraderButtons();
            }
            else if (State == State.Realtime)
            {
                ApplyExecutionAccountOverrideIfNeeded();
                if (LogAccountDiagnostics)
                    DumpAccounts("realtime_start");

                // Daily PnL limits are "today-only" per Accounts tab; reset local latch on startup.
                ResetDailyPnLLimitState("realtime_start");
                manualHaltActive = false;
                manualHaltStatusText = null;
                manualHaltActivatedAt = DateTime.MinValue;

                // Flush any historical bookkeeping so live executions start from a clean slate.
                ResetTradeState();
                StrategyLogInfo("[AUTO] Strategy entered realtime; automation enabled");

                BootstrapExistingPositionState();
                TryInitializeChartTraderButtons();
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0)
                    UpdateStatusLabel(string.Format("Managing {0} {1} ({2})", Position.MarketPosition, Position.Quantity, activeTradeId ?? "<pending>"), true);
                else
                    UpdateStatusLabel("Active: syncing live state", true);
            }
            else if (State == State.Terminated)
            {
                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.UnregisterStrategy(this);

                try
                {
                    MultiStratManager.Instance?.ClearManualHaltOverride(Account != null ? Account.Name : string.Empty, "strategy_terminated");
                }
                catch { }

                // Cancel any working entry orders so they cannot fill after disable.
                CancelWorkingEntryOrders("strategy_terminated");

                // Safety: flatten any open position when the strategy terminates to avoid naked risk.
                TryFlattenActivePosition("strategy_terminated");

                ResetTradeState();
                RemoveChartTraderButtons();
                UpdateStatusLabel("Stopped", false);
            }
        }
            catch (Exception ex)
            {
                StrategyLogError(string.Format("[STATE] OnStateChange({0}) exception: {1}", State, ex));
                throw;
            }
        }
        #endregion

        private void ApplyExecutionAccountOverrideIfNeeded()
        {
            if (accountOverrideApplied)
                return;

            string desired = (ExecutionAccountOverride ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(desired))
            {
                accountOverrideApplied = true;
                return;
            }

            NinjaTrader.Cbi.Account resolved = null;
            try
            {
                string desiredNorm = NormalizeAccountKey(desired);
                lock (NinjaTrader.Cbi.Account.All)
                {
                    foreach (var account in NinjaTrader.Cbi.Account.All)
                    {
                        if (account == null)
                            continue;

                        if (string.Equals(account.Name, desired, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(account.DisplayName, desired, StringComparison.OrdinalIgnoreCase))
                        {
                            resolved = account;
                            break;
                        }

                        string nameNorm = NormalizeAccountKey(account.Name);
                        string displayNorm = NormalizeAccountKey(account.DisplayName);
                        if (!string.IsNullOrEmpty(desiredNorm) &&
                            (!string.IsNullOrEmpty(nameNorm) || !string.IsNullOrEmpty(displayNorm)) &&
                            (string.Equals(nameNorm, desiredNorm, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(displayNorm, desiredNorm, StringComparison.OrdinalIgnoreCase)))
                        {
                            resolved = account;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (resolved == null)
            {
                if (!accountOverrideLoggedUnavailable)
                {
                    accountOverrideLoggedUnavailable = true;
                    var available = GetAvailableAccountNames();
                    var availableText = available.Count > 0 ? string.Join(", ", available) : "<none>";
                    StrategyLogInfo($"[AUTO][ACCOUNT] ExecutionAccountOverride '{desired}' not found; available={availableText}; staying on {(Account != null ? Account.Name : "<null>")}");
                    if (LogAccountDiagnostics)
                        DumpAccounts("override_not_found");
                }
                return;
            }

            if (Account != null && string.Equals(Account.Name, resolved.Name, StringComparison.OrdinalIgnoreCase))
            {
                accountOverrideApplied = true;
                StrategyLogInfo($"[AUTO][ACCOUNT] ExecutionAccountOverride resolved to current account '{resolved.Name}'");
                return;
            }

            string error;
            if (!TrySetStrategyAccount(resolved, out error))
            {
                accountOverrideApplied = true;
                StrategyLogInfo($"[AUTO][ACCOUNT] Failed to apply ExecutionAccountOverride '{resolved.Name}': {error}");
                return;
            }

            accountOverrideApplied = true;
            StrategyLogInfo($"[AUTO][ACCOUNT] Strategy account overridden to '{resolved.Name}' (from '{desired}')");
            if (LogAccountDiagnostics)
                DumpAccounts("override_applied");
        }

        private static string NormalizeAccountKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            var builder = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToUpperInvariant(c));
            }
            return builder.ToString();
        }

        private static List<string> GetAvailableAccountNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (NinjaTrader.Cbi.Account.All)
                {
                    foreach (var account in NinjaTrader.Cbi.Account.All)
                    {
                        if (account == null)
                            continue;

                        if (!string.IsNullOrWhiteSpace(account.Name))
                            names.Add(account.Name.Trim());
                        if (!string.IsNullOrWhiteSpace(account.DisplayName))
                            names.Add(account.DisplayName.Trim());
                    }
                }
            }
            catch { }

            return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void DumpAccounts(string reason)
        {
            // throttle to avoid spamming if property grid calls converters repeatedly
            if (lastAccountDiagnosticsAt != DateTime.MinValue && (DateTime.UtcNow - lastAccountDiagnosticsAt).TotalSeconds < 10)
                return;
            lastAccountDiagnosticsAt = DateTime.UtcNow;

            try
            {
                List<NinjaTrader.Cbi.Account> accounts;
                lock (NinjaTrader.Cbi.Account.All)
                    accounts = NinjaTrader.Cbi.Account.All.Where(a => a != null).ToList();

                StrategyLogInfo($"[AUTO][ACCOUNT_DUMP] reason={reason} current={(Account != null ? Account.Name : "<null>")} count={accounts.Count}");
                foreach (var account in accounts)
                {
                    string connName = string.Empty;
                    string connStatus = string.Empty;
                    try
                    {
                        if (account.Connection != null)
                        {
                            var connection = account.Connection;
                            connName = connection.Options != null ? connection.Options.Name : null;

                            if (string.IsNullOrEmpty(connName))
                            {
                                var nameProp = connection.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (nameProp != null)
                                    connName = Convert.ToString(nameProp.GetValue(connection, null));
                            }

                            connStatus = connection.Status.ToString();
                        }
                    }
                    catch { }

                    StrategyLogInfo($"[AUTO][ACCOUNT_DUMP] name='{account.Name}' display='{account.DisplayName}' conn='{connName}' status='{connStatus}'");

                    // Probe a few common flags via reflection (varies across NT versions/providers)
                    try
                    {
                        var flags = new[] { "IsSimulation", "IsSimulated", "IsPlayback", "IsEnabled", "CanSubmitOrders", "CanSubmit", "IsTradable" };
                        var parts = new List<string>();
                        foreach (var flag in flags)
                        {
                            var p = account.GetType().GetProperty(flag, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (p != null && p.PropertyType == typeof(bool))
                            {
                                bool v = (bool)p.GetValue(account, null);
                                parts.Add($"{flag}={v}");
                            }
                        }
                        if (parts.Count > 0)
                            StrategyLogInfo($"[AUTO][ACCOUNT_DUMP] flags name='{account.Name}' {string.Join(" ", parts)}");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                StrategyLogInfo($"[AUTO][ACCOUNT_DUMP] failed reason={reason} err={ex.GetType().Name}: {ex.Message}");
            }
        }

        private bool TrySetStrategyAccount(NinjaTrader.Cbi.Account targetAccount, out string error)
        {
            error = null;
            if (targetAccount == null)
            {
                error = "targetAccount null";
                return false;
            }

            try
            {
                PropertyInfo prop = null;
                for (Type t = GetType(); t != null && prop == null; t = t.BaseType)
                {
                    prop = t.GetProperty("Account", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (prop == null)
                {
                    error = "Account property not found via reflection";
                    return false;
                }

                var setter = prop.GetSetMethod(true);
                if (setter == null)
                {
                    error = "Account has no setter";
                    return false;
                }

                setter.Invoke(this, new object[] { targetAccount });
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        protected override void OnBarUpdate()
        {
            if (State == State.Realtime)
                ApplyExecutionAccountOverrideIfNeeded();

            if (BarsInProgress == 0 || BarsInProgress == 1)
            {
                if (State == State.Realtime && EnableDailyPnLLimits)
                {
                    MaybeResetDailyPnLLimitForNewDay();

                    if (!dailyPnLLimitHalted)
                        MaybeHydrateDailyPnLLimitFromAddonOverride();

                    MaybeClearDailyPnLLimitForSimReset();
                    MaybeClearDailyPnLLimitFromManualReset();

                    if (dailyPnLLimitHalted)
                    {
                        MaybeClearDailyPnLLimitIfRecovered();
                        if (!dailyPnLLimitHalted)
                            return;

                        RefreshDailyPnLLimitStatusText();
                        TryEnforceDailyPnLLimitFlat();
                        if (!string.IsNullOrWhiteSpace(dailyPnLLimitStatusText))
                            UpdateStatusLabel(dailyPnLLimitStatusText, false);
                        return;
                    }

                    if (TryCheckDailyPnLLimit(out var statusText))
                    {
                        dailyPnLLimitStatusText = statusText;
                        UpdateStatusLabel(dailyPnLLimitStatusText, false);
                        return;
                    }
                }
            }

            if (BarsInProgress == 1)
            {
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0)
                    UpdateStopsTargets(GetRealtimePrice());
                return;
            }

            if (BarsInProgress != 0)
                return;

            // Block signal processing while still in historical/calculating state to avoid premature entries/hedges.
            if (State != State.Realtime)
                return;

            // If flat with pending close publishes, retry but keep scanning for new signals.
            bool isFlat = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
            if (isFlat && tradeStates != null && tradeStates.Count > 0)
            {
                bool hasWorkingEntry = HasWorkingEntryOrders();
                bool pending = PublishPendingCloses();
                if (!pending && !hasWorkingEntry)
                    ResetTradeState();
                if (hasWorkingEntry)
                {
                    UpdateStatusLabel("Active: pending entry (waiting for fill/cancel)", true);
                    return;
                }
                UpdateStatusLabel("Active: scanning (position flat)", true);
                // Do not return; allow new entries even if pending closes remain.
            }

            if (CurrentBar < BarsRequiredToTrade)
            {
                bool liveManaging = Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0;
                if (liveManaging)
                {
                    UpdateStatusLabel(string.Format("Managing {0} {1} ({2})", Position.MarketPosition, Position.Quantity, activeTradeId ?? "<pending>"), true);
                    UpdateStopsTargets(GetRealtimePrice());
                }
                else
                {
                    int remaining = Math.Max(0, BarsRequiredToTrade - CurrentBar);
                    UpdateStatusLabel($"Warming up... {remaining} bars to go", false);
                }
                return;
            }

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
                // Always attempt to rebuild runtime state from the platform position first.
                BootstrapExistingPositionState();
                hasTrackedTrades = tradeStates != null && tradeStates.Count > 0;

                // Only pause if we still have no runtime state after bootstrapping and account also shows flat.
                if (!hasTrackedTrades && !accountHasExposure)
                {
                    if (!desyncHoldActive)
                    {
                        desyncHoldActive = true;
                        desyncHoldActivatedAt = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                        StrategyLogInfo(string.Format("[AUTO][DESYNC] Holding automation because NT reports {0} qty={1} while account exposure and trade state are empty.",
                            Position != null ? Position.MarketPosition.ToString() : "Flat",
                            Position != null ? Position.Quantity : 0));
                        UpdateStatusLabel("Paused: waiting for platform/account flatten", false);
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
                    UpdateStatusLabel("Paused: waiting for platform/account flatten", false);
                    return;
                }
            }

            if (manualHaltActive)
            {
                string haltText = string.IsNullOrWhiteSpace(manualHaltStatusText) ? "HALTED: manual (awaiting resume)" : manualHaltStatusText;
                if (!isFlatPosition && hasTrackedTrades)
                {
                    UpdateStatusLabel(haltText, false);
                    UpdateStopsTargets(GetRealtimePrice());
                }
                else
                {
                    UpdateStatusLabel(haltText, false);
                }
                return;
            }

            // Manage orders
            if (isFlatPosition)
            {
                stopSet = targetSet = false;
                activeTradeId = null;
                ResetDemaTrailingState();
                bool tradeSyncOk = MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null;
                UpdateStatusLabel($"Active: scanning L/S votes {longVotes}/{shortVotes} (bias {Bias}, min {effMinLong}/{effMinShort})", tradeSyncOk);

                if (canLong)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Long))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterLong because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                        UpdateStatusLabel("Blocked: opposing exposure prevents new LONG", false);
                    }
                    else
                    {
                        int entriesToSubmit = Math.Max(1, TradesPerEntry);
                        MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(MarketPosition.Long, entriesToSubmit, Math.Max(1, DefaultQuantity));
                        for (int i = 0; i < entriesToSubmit; i++)
                        {
                            string tradeId = CreateTradeId(MarketPosition.Long);
                            var state = PrepareTradeState(tradeId, MarketPosition.Long, Math.Max(1, DefaultQuantity));
                            AttachTradeStateToSyncGroup(state, syncGroup);
                            if (Debug) StrategyLogDebug($"{Time[0]} EnterLong({tradeId}) votes={longVotes} effMin={effMinLong} entry={i + 1}/{entriesToSubmit}");
                            EnterLong(tradeId);
                        }
                    }
                }
                else if (canShort)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Short))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterShort because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                        UpdateStatusLabel("Blocked: opposing exposure prevents new SHORT", false);
                    }
                    else
                    {
                        int entriesToSubmit = Math.Max(1, TradesPerEntry);
                        MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(MarketPosition.Short, entriesToSubmit, Math.Max(1, DefaultQuantity));
                        for (int i = 0; i < entriesToSubmit; i++)
                        {
                            string tradeId = CreateTradeId(MarketPosition.Short);
                            var state = PrepareTradeState(tradeId, MarketPosition.Short, Math.Max(1, DefaultQuantity));
                            AttachTradeStateToSyncGroup(state, syncGroup);
                            if (Debug) StrategyLogDebug($"{Time[0]} EnterShort({tradeId}) votes={shortVotes} effMin={effMinShort} entry={i + 1}/{entriesToSubmit}");
                            EnterShort(tradeId);
                        }
                    }
                }
            }
            else if (hasTrackedTrades)
            {
                string statusTradeId = !string.IsNullOrEmpty(activeTradeId) ? activeTradeId : "<pending>";
                UpdateStatusLabel($"Managing {Position.MarketPosition} {Position.Quantity} ({statusTradeId})", true);
                UpdateStopsTargets(GetRealtimePrice());
            }

            if (Debug)
                StrategyLogDebug($"{Time[0]} votes L/S: {longVotes}/{shortVotes} canL={canLong} canS={canShort} bias={Bias} minL={MinSignalsToEnterLong}->{effMinLong} minS={MinSignalsToEnterShort}->{effMinShort} Pos:{Position.MarketPosition}");
        }

        private void UpdateStopsTargets(double? priceOverride = null)
        {
            if (string.IsNullOrEmpty(activeTradeId))
                return;

            if (State == State.Realtime && dailyPnLLimitHalted)
                return;

            if (!TryGetTradeState(activeTradeId, out var activeState))
                return;

            if (!activeState.OpenPublished && !activeState.IsSynthetic)
            {
                if (PublishOpenEvent(activeState))
                    activeState.OpenPublished = true;
            }

            if (activeState.IsSynthetic)
            {
                if (Debug && !activeState.SyntheticLogEmitted)
                {
                    StrategyLogDebug($"[STOPS] Skipping stop/target setup for synthetic trade {activeTradeId} while waiting for live fill.");
                    activeState.SyntheticLogEmitted = true;
                }
                return;
            }

            double currentPrice = priceOverride ?? GetRealtimePrice();

            // If this is a bootstrapped position and we still have no stops/targets, force protection first.
            if (activeState.Bootstrapped && (!stopSet || !targetSet) && Position != null && Position.MarketPosition != MarketPosition.Flat)
            {
                if (Debug)
                {
                    StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] Ensuring protection for {0} stopSet={1} targetSet={2} pos={3}@{4:F2}",
                        activeTradeId ?? "<unknown>",
                        stopSet,
                        targetSet,
                        Position.MarketPosition,
                        Position.AveragePrice));
                }

                if (Debug)
                {
                    StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] State snapshot: entry={0:F4} lastStop={1:F4} lastTarget={2:F4} ticks stop/target={3}/{4}",
                        activeState.EntryPrice,
                        activeState.LastStopPrice,
                        activeState.LastTargetPrice,
                        StopTicks,
                        TargetTicks));
                }

                EnsureProtectionForActiveTrade(activeState, currentPrice);
                if (!stopSet || !targetSet)
                {
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Protection still missing after EnsureProtection (stopSet={0} targetSet={1}) for {2}",
                        stopSet,
                        targetSet,
                        activeTradeId ?? "<unknown>"));
                }
            }

            if (activeState.RunUpActive)
            {
                MultiEntrySyncGroup group;
                if (TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group))
                {
                    List<TradeRuntimeState> states = GetMultiEntrySyncStates(group.TradeId);
                    foreach (TradeRuntimeState state in states)
                        ApplyRunUpTrailing(state, currentPrice);
                }
                else
                {
                    ApplyRunUpTrailing(activeState, currentPrice);
                }
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
            // If we already have a working stop at (or effectively equal to) the desired price, do nothing.
            double desired = value;
            if (mode == CalculationMode.Price)
            {
                double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                if (tickSize > 0)
                    desired = Instrument?.MasterInstrument?.RoundToTickSize(value) ?? Math.Round(value / tickSize) * tickSize;
            }
            if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState) && state.LastStopPrice > 0 && PricesClose(state.LastStopPrice, desired))
            {
                stopSet = true;
                return true;
            }
            if (state.ManualStopOverride)
            {
                if (Debug)
                    StrategyLogDebug($"[AUTO][STOP] Skipping auto stop update for {tradeId} due to manual adjustment.");
                return false;
            }

            double targetValue = desired;

            // If we already have a working stop at (or effectively equal to) the desired price, do nothing.
            if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState) && state.LastStopPrice > 0 && PricesClose(state.LastStopPrice, targetValue))
            {
                stopSet = true;
                state.PendingAutoStopUpdate = false;
                return true;
            }

            state.PendingAutoStopUpdate = true;
            state.PendingAutoStopPrice = targetValue;
            bool useGlobalSignal = state.Bootstrapped;
            try
            {
                if (useGlobalSignal)
                {
                    double entry = state.EntryPrice;
                    if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                        entry = Position.AveragePrice;
                    if (entry <= 0 || double.IsNaN(entry))
                        entry = GetRealtimePrice();

                    double stopPrice = 0;
                    if (mode == CalculationMode.Price)
                    {
                        stopPrice = targetValue;
                    }
                    else
                    {
                        double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                        if (tickSize <= 0)
                            tickSize = 1.0;
                        if (entry > 0 && tickSize > 0)
                        {
                            stopPrice = state.EntrySide == MarketPosition.Long
                                ? entry - targetValue * tickSize
                                : entry + targetValue * tickSize;
                        }
                    }

                    if (stopPrice <= 0)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Unable to compute stop price for {0} (entry={1:F2}, target={2})", tradeId, entry, targetValue));
                        return false;
                    }

                    // Sanity: stop must be on the correct side of market to avoid rejection/termination.
                    double bid = GetCurrentBid();
                    double ask = GetCurrentAsk();
                    double refPrice = Position.MarketPosition == MarketPosition.Long ? bid : ask;
                    double tick = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                    if (tick <= 0) tick = 1e-6;
                    if (refPrice <= 0) refPrice = GetRealtimePrice();
                    if (Position.MarketPosition == MarketPosition.Long && stopPrice >= refPrice - tick)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Skip stop for {0}: computed {1:F2} not below market {2:F2}", tradeId, stopPrice, refPrice));
                        return false;
                    }
                    if (Position.MarketPosition == MarketPosition.Short && stopPrice <= refPrice + tick)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Skip stop for {0}: computed {1:F2} not above market {2:F2}", tradeId, stopPrice, refPrice));
                        return false;
                    }

                    if (Debug)
                        StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] Placing global stop {0:F2} for {1} entry={2:F2} ticks={3}", stopPrice, tradeId, entry, targetValue));
                    string stopDetail = mode == CalculationMode.Ticks ? $"{targetValue}" : "price";
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Submit stop {0:F2} for {1} (entry={2:F2}, detail={3})", stopPrice, tradeId, entry, stopDetail));

                    string stopSignal = BuildExitSignalName(tradeId, "BS");
                    int qty = GetActiveQuantity(state);
                    if (qty <= 0)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Abort stop for {0}: resolved quantity <= 0", tradeId));
                        return false;
                    }

                    // If a working stop already exists, try a price change; if that fails, fall back to cancel/resubmit so run-up/trailing can proceed.
                    if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState))
                    {
                        if (PricesClose(state.LastStopPrice, stopPrice))
                        {
                            state.PendingAutoStopUpdate = false;
                            stopSet = true;
                            return true;
                        }

                        bool changed = false;
                        try
                        {
                            if (state.StopOrder.OrderState == OrderState.Submitted
                                || state.StopOrder.OrderState == OrderState.Working
                                || state.StopOrder.OrderState == OrderState.Accepted)
                            {
                                ChangeOrder(state.StopOrder, state.StopOrder.Quantity, stopPrice, stopPrice);
                                state.LastStopPrice = stopPrice;
                                state.PendingAutoStopUpdate = false;
                                stopSet = true;
                                StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Changed existing stop to {0:F2} for {1}", stopPrice, tradeId));
                                return true;
                            }
                        }
                        catch { changed = false; }

                        // If we cannot change (cancelling or non-working), cancel and proceed to submit a fresh stop.
                        try { CancelOrder(state.StopOrder); } catch { /* ignore */ }
                        state.StopOrder = null;
                        state.LastStopPrice = 0;
                        state.PendingAutoStopUpdate = true;
                    }

                    // Start-behavior sync fills have no entry signal; use global exit orders (null fromEntrySignal)
                    // so Ninja attaches to the account position instead of expecting a matching signal name.
                    string fromEntry = state.Bootstrapped ? null : tradeId;
                    if (state.EntrySide == MarketPosition.Long)
                        state.StopOrder = ExitLongStopMarket(qty, stopPrice, stopSignal, fromEntry);
                    else
                        state.StopOrder = ExitShortStopMarket(qty, stopPrice, stopSignal, fromEntry);

                    state.LastStopPrice = stopPrice;
                    state.PendingAutoStopUpdate = false;
                    stopSet = true;
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Submitted explicit stop at {0:F2} for {1}", stopPrice, tradeId));
                }
                else
                {
                    // For managed stops, validate the derived price to avoid Ninja zero-price errors.
                    if (mode == CalculationMode.Ticks && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    {
                        double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                        if (tickSize <= 0)
                            tickSize = 1.0;
                        double entry = Position.AveragePrice;
                        double derived = 0;
                        if (entry > 0 && tickSize > 0)
                        {
                            derived = state.EntrySide == MarketPosition.Long
                                ? entry - value * tickSize
                                : entry + value * tickSize;
                        }
                        if (derived <= 0)
                        {
                            state.PendingAutoStopUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][STOP] Skip SetStopLoss for {0}: derived price <= 0 (entry={1:F2} ticks={2} tickSize={3})", tradeId, entry, value, tickSize));
                            return false;
                        }
                    }
                    else if (mode == CalculationMode.Price && value <= 0)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][STOP] Skip SetStopLoss for {0}: price <= 0 ({1})", tradeId, value));
                        return false;
                    }
                    else if (mode == CalculationMode.Price && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    {
                        double bid = GetCurrentBid();
                        double ask = GetCurrentAsk();
                        double refPrice = Position.MarketPosition == MarketPosition.Long ? bid : ask;
                        double tick = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                        if (tick <= 0) tick = 1e-6;
                        if (refPrice <= 0) refPrice = GetRealtimePrice();

                        if (Position.MarketPosition == MarketPosition.Long && value > refPrice - tick)
                        {
                            state.PendingAutoStopUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][STOP] Skip SetStopLoss for {0}: price {1:F2} not at least 1 tick below market {2:F2}", tradeId, value, refPrice));
                            return false;
                        }
                        if (Position.MarketPosition == MarketPosition.Short && value < refPrice + tick)
                        {
                            state.PendingAutoStopUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][STOP] Skip SetStopLoss for {0}: price {1:F2} not at least 1 tick above market {2:F2}", tradeId, value, refPrice));
                            return false;
                        }
                    }
                    // If an existing stop is in a non-working state, drop it so we can submit a clean one.
                    if (state.StopOrder != null && (state.StopOrder.OrderState == OrderState.CancelPending || state.StopOrder.OrderState == OrderState.CancelSubmitted))
                    {
                        try { CancelOrder(state.StopOrder); } catch { }
                        state.StopOrder = null;
                        state.LastStopPrice = 0;
                    }

                    SetStopLoss(tradeId, mode, targetValue, simulated);
                }
                return true;
            }
            catch (Exception ex)
            {
                state.PendingAutoStopUpdate = false;
                StrategyLogError($"[ERROR] SetStopLoss failed for {tradeId}: {ex.Message}");
                // Fallback for bootstrapped positions that lack entry signals: try global stop.
                if (state.Bootstrapped)
                {
                    try
                    {
                        SetStopLoss(mode, targetValue);
                        StrategyLogInfo(string.Format("[AUTO][STOP] Applied fallback stop for {0} using global signal (bootstrapped)", tradeId));
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        StrategyLogError($"[ERROR] Fallback SetStopLoss failed for {tradeId}: {ex2.Message}");
                    }
                }
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
                    StrategyLogDebug($"[AUTO][TARGET] Skipping auto target update for {tradeId} due to manual adjustment.");
                return false;
            }

            // If we already have a working target at (or effectively equal to) the desired price, do nothing.
            double desiredValue = value;
            if (mode == CalculationMode.Price)
            {
                double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                if (tickSize > 0)
                    desiredValue = Instrument?.MasterInstrument?.RoundToTickSize(value) ?? Math.Round(value / tickSize) * tickSize;
            }
            if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState) && state.LastTargetPrice > 0 && PricesClose(state.LastTargetPrice, desiredValue))
            {
                targetSet = true;
                state.PendingAutoTargetUpdate = false;
                return true;
            }

            state.PendingAutoTargetUpdate = true;
            state.PendingAutoStopPrice = 0;
            bool useGlobalSignal = state.Bootstrapped;
            try
            {
                if (useGlobalSignal)
                {
                    double entry = state.EntryPrice;
                    if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                        entry = Position.AveragePrice;
                    if (entry <= 0 || double.IsNaN(entry))
                        entry = GetRealtimePrice();

                    double targetPrice = 0;
                    if (mode == CalculationMode.Price)
                    {
                        targetPrice = value;
                    }
                    else
                    {
                        double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                        if (tickSize <= 0)
                            tickSize = 1.0;
                        if (entry > 0 && tickSize > 0)
                        {
                            targetPrice = state.EntrySide == MarketPosition.Long
                                ? entry + value * tickSize
                                : entry - value * tickSize;
                        }
                    }

                    if (targetPrice <= 0)
                    {
                        state.PendingAutoTargetUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Unable to compute target price for {0} (entry={1:F2}, target={2})", tradeId, entry, value));
                        return false;
                    }

                    if (Debug)
                        StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] Placing global target {0:F2} for {1} entry={2:F2} ticks={3}", targetPrice, tradeId, entry, value));
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Submit target {0:F2} for {1} (entry={2:F2}, ticks={3})", targetPrice, tradeId, entry, value));

                    string targetSignal = BuildExitSignalName(tradeId, "BT");
                    int qty = GetActiveQuantity(state);
                    if (qty <= 0)
                    {
                        state.PendingAutoTargetUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Abort target for {0}: resolved quantity <= 0", tradeId));
                        return false;
                    }

                    if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState))
                    {
                        if (PricesClose(state.LastTargetPrice, targetPrice))
                        {
                            state.PendingAutoTargetUpdate = false;
                            targetSet = true;
                            return true;
                        }

                        try
                        {
                            if (state.TargetOrder.OrderState == OrderState.Submitted
                                || state.TargetOrder.OrderState == OrderState.Working
                                || state.TargetOrder.OrderState == OrderState.Accepted)
                            {
                                ChangeOrder(state.TargetOrder, state.TargetOrder.Quantity, targetPrice, targetPrice);
                                state.LastTargetPrice = targetPrice;
                                state.PendingAutoTargetUpdate = false;
                                targetSet = true;
                                StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Changed existing target to {0:F2} for {1}", targetPrice, tradeId));
                                return true;
                            }
                        }
                        catch { /* ignore change failure; do not cancel/resubmit */ }

                        state.PendingAutoTargetUpdate = false;
                        return false;
                    }

                    // Use global exit orders for sync-bootstrapped fills (no entry signal)
                    string fromEntry = state.Bootstrapped ? null : tradeId;
                    if (state.EntrySide == MarketPosition.Long)
                        state.TargetOrder = ExitLongLimit(qty, targetPrice, targetSignal, fromEntry);
                    else
                        state.TargetOrder = ExitShortLimit(qty, targetPrice, targetSignal, fromEntry);

                    state.LastTargetPrice = targetPrice;
                    state.PendingAutoTargetUpdate = false;
                    targetSet = true;
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Submitted explicit target at {0:F2} for {1}", targetPrice, tradeId));
                }
                else
                {
                    if (mode == CalculationMode.Ticks && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    {
                        double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
                        if (tickSize <= 0)
                            tickSize = 1.0;
                        double entry = Position.AveragePrice;
                        double derived = 0;
                        if (entry > 0 && tickSize > 0)
                        {
                            derived = state.EntrySide == MarketPosition.Long
                                ? entry + value * tickSize
                                : entry - value * tickSize;
                        }
                        if (derived <= 0)
                        {
                            state.PendingAutoTargetUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][TARGET] Skip SetProfitTarget for {0}: derived price <= 0 (entry={1:F2} ticks={2} tickSize={3})", tradeId, entry, value, tickSize));
                            return false;
                        }
                    }
                    else if (mode == CalculationMode.Price && value <= 0)
                    {
                        state.PendingAutoTargetUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][TARGET] Skip SetProfitTarget for {0}: price <= 0 ({1})", tradeId, value));
                        return false;
                    }
                    SetProfitTarget(tradeId, mode, desiredValue);
                }
                return true;
            }
            catch (Exception ex)
            {
                state.PendingAutoTargetUpdate = false;
                StrategyLogError($"[ERROR] SetProfitTarget failed for {tradeId}: {ex.Message}");
                // Fallback for bootstrapped positions that lack entry signals: try global target.
                if (state.Bootstrapped)
                {
                    try
                    {
                        SetProfitTarget(mode, value);
                        StrategyLogInfo(string.Format("[AUTO][TARGET] Applied fallback target for {0} using global signal (bootstrapped)", tradeId));
                        return true;
                    }
                    catch (Exception ex2)
                    {
                        StrategyLogError($"[ERROR] Fallback SetProfitTarget failed for {tradeId}: {ex2.Message}");
                    }
                }
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

            bool isLong = state.EntrySide == MarketPosition.Long;
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
            state.LastStopPrice = safePrice.Value;
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

            double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
            // Prevent loosening: pin desired to last accepted stop if the math would move it backward.
            if (lastAccepted.HasValue)
            {
                if (isLong && desiredStop <= lastAccepted.Value)
                    desiredStop = lastAccepted.Value;
                else if (!isLong && desiredStop >= lastAccepted.Value)
                    desiredStop = lastAccepted.Value;
            }
            // Allow run-up stops to advance beyond entry as long as they remain on the correct side of current price (ClampStopPrice enforces).
            double entryPrice = Position.AveragePrice;
            // If we are already at the pinned stop, skip re-issuing.
            if (lastAccepted.HasValue && PricesClose(desiredStop, lastAccepted.Value))
                return;

            // Clamp against live bid/ask so we don't compute a stop that's too close to the market due to last-price lag.
            double clampRef = currentPrice;
            try
            {
                double bid = GetCurrentBid();
                double ask = GetCurrentAsk();
                if (isLong && bid > 0)
                    clampRef = bid;
                else if (!isLong && ask > 0)
                    clampRef = ask;
            }
            catch { }

            var clamped = ClampStopPrice(desiredStop, clampRef, isLong, lastAccepted);
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

            if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
            {
                state.RunUpLastStopPrice = clamped.Value;
                state.LastStopPrice = clamped.Value;
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

        private void ClearGlobalStopsTargets()
        {
            try
            {
                SetStopLoss(CalculationMode.Price, double.MaxValue);
            }
            catch { }

            try
            {
                SetProfitTarget(CalculationMode.Price, double.MaxValue);
            }
            catch { }
        }

        private void EnsureProtectionForActiveTrade(TradeRuntimeState state, double currentPrice)
        {
            if (State == State.Realtime && dailyPnLLimitHalted)
                return;

            if (state == null || string.IsNullOrEmpty(activeTradeId) || Position == null || Position.MarketPosition == MarketPosition.Flat)
                return;

            // If stops/targets already armed, nothing to do.
            if ((stopSet || (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState))) &&
                (targetSet || (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState))))
                return;

            // Reuse ATR/ticks logic to compute baseline protections.
            double entryPrice = state.EntryPrice;
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                entryPrice = Position.AveragePrice;
            double atrValue = 0;
            try
            {
                if (atr != null)
                    atrValue = atr[0];
            }
            catch { }

            int stopTicks = StopType == StopKind.ATR && atrValue > 0 && TickSize > 0
                ? (int)Math.Max(1, Math.Round((atrValue * AtrStopMult) / TickSize))
                : Math.Max(1, StopTicks);

            int targetTicks = TargetType == TargetKind.ATR && atrValue > 0 && TickSize > 0
                ? (int)Math.Max(1, Math.Round((atrValue * AtrTargetMult) / TickSize))
                : Math.Max(1, TargetTicks);

            // If we have a recorded stop price but no working order (e.g., platform cancelled), re-arm once.
            if (!stopSet && state.LastStopPrice > 0 && (state.StopOrder == null || IsTerminalState(state.StopOrder.OrderState)))
            {
                if (IssueStopLoss(activeTradeId, CalculationMode.Price, state.LastStopPrice, false))
                    stopSet = true;
            }

            // Try managed stops/targets first. Do not re-arm if we already have a recorded stop price.
            if (!stopSet && state.LastStopPrice <= 0)
            {
                if (IssueStopLoss(activeTradeId, CalculationMode.Ticks, stopTicks, false))
                {
                    stopSet = true;
                    state.LastStopPrice = Position.MarketPosition == MarketPosition.Long
                        ? entryPrice - stopTicks * TickSize
                        : entryPrice + stopTicks * TickSize;
                }
                else
                {
                    string stopSignal = BuildExitSignalName(activeTradeId, "BS");
                    double price = Position.MarketPosition == MarketPosition.Long
                        ? entryPrice - stopTicks * TickSize
                        : entryPrice + stopTicks * TickSize;
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLongStopMarket(price, stopSignal, activeTradeId);
                    else
                        ExitShortStopMarket(price, stopSignal, activeTradeId);
                    stopSet = true;
                }
            }

            if (!targetSet && state.LastTargetPrice > 0 && (state.TargetOrder == null || IsTerminalState(state.TargetOrder.OrderState)))
            {
                if (IssueProfitTarget(activeTradeId, CalculationMode.Price, state.LastTargetPrice))
                    targetSet = true;
            }

            if (!targetSet && state.LastTargetPrice <= 0)
            {
                if (IssueProfitTarget(activeTradeId, CalculationMode.Ticks, targetTicks))
                {
                    targetSet = true;
                    state.LastTargetPrice = Position.MarketPosition == MarketPosition.Long
                        ? entryPrice + targetTicks * TickSize
                        : entryPrice - targetTicks * TickSize;
                }
                else
                {
                    string targetSignal = BuildExitSignalName(activeTradeId, "BT");
                    double price = Position.MarketPosition == MarketPosition.Long
                        ? entryPrice + targetTicks * TickSize
                        : entryPrice - targetTicks * TickSize;
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLongLimit(price, targetSignal, activeTradeId);
                    else
                        ExitShortLimit(price, targetSignal, activeTradeId);
                    targetSet = true;
                }
            }
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
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][BOOTSTRAP] Unable to read account quantity: {0}", ex.Message));
            }

            return 0;
        }

        private bool AccountHasInstrumentExposure()
        {
            // Treat the strategy Position as authoritative when non-flat to avoid false pauses
            if (Position != null && Position.MarketPosition != MarketPosition.Flat && Position.Quantity != 0)
                return true;

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

        private int GetActiveQuantity(TradeRuntimeState state)
        {
            int posQty = Position != null ? Math.Abs(Position.Quantity) : 0;
            int stateQty = state != null ? Math.Max(0, state.RemainingQuantity) : 0;
            int hintQty = state != null ? Math.Max(0, state.OriginalQuantity) : 0;
            return Math.Max(1, Math.Max(posQty, Math.Max(stateQty, hintQty)));
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

            int accountQty = GetAccountInstrumentSignedQuantity();
            if (StartBehavior == StartBehavior.ImmediatelySubmitSynchronizeAccount && accountQty == 0)
            {
                if (Debug)
                    StrategyLogDebug("[AUTO][BOOTSTRAP] Deferring bootstrap while waiting for start-behavior sync entry (account flat).");
                return;
            }

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            if (tradeStates.Count > 0 || openTradeOrder.Count > 0)
                return;

            MarketPosition side = Position.MarketPosition;
            int qty = Math.Abs(Position.Quantity);
            string tradeId = CreateTradeId(side);

            var state = new TradeRuntimeState
            {
                TradeId = tradeId,
                SyncTradeId = null,
                EntrySide = side,
                OriginalQuantity = qty,
                RemainingQuantity = qty,
                InstrumentName = Instrument != null ? Instrument.FullName : string.Empty,
                AccountName = Account != null ? Account.Name : string.Empty,
                EntryPrice = Position.AveragePrice,
                OpenPublished = false,
                IsSynthetic = false, // synced position should be managed immediately
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
                RunUpLowWater = 0,
                SyntheticLogEmitted = false,
                Bootstrapped = true,
                AllowOpenPublish = State == State.Realtime
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
            stopSet = false;
            targetSet = false;
            state.SyntheticLogEmitted = false;

            StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Seeded trade {0} for existing position {1} qty={2}", tradeId, side, qty));

            if (!state.OpenPublished && state.AllowOpenPublish)
            {
                if (PublishOpenEvent(state))
                    state.OpenPublished = true;
            }

            // Immediately manage stops/targets when in realtime so bootstrapped trades are protected.
            if (State == State.Realtime)
            {
                UpdateStopsTargets(GetRealtimePrice());
                EnsureProtectionForActiveTrade(state, GetRealtimePrice());
                UpdateStatusLabel($"Managing {side} {qty} ({tradeId})", true);
            }

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
                    CancelProtectiveOrders(state);
                }
            }

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            else
                tradeStates.Clear();

            ClearGlobalStopsTargets();
            openTradeOrder.Clear();
            activeTradeId = null;
            multiEntrySyncGroups.Clear();
            stopSet = false;
            targetSet = false;
            ResetDemaTrailingState();
            lastStatusText = null;
            lastStatusHealthy = false;
            lastStatusHasPnLLines = false;
            lastStatusPnlNegative = false;
        }

        private bool IsMultiEntrySyncEnabled
        {
            get { return TreatMultiEntryAsSingleTrade && TradesPerEntry > 1; }
        }

        private MultiEntrySyncGroup StartMultiEntrySyncGroup(MarketPosition side, int entriesToSubmit, int quantityPerEntry)
        {
            if (!IsMultiEntrySyncEnabled || entriesToSubmit <= 1)
                return null;

            var group = new MultiEntrySyncGroup
            {
                TradeId = CreateTradeId(side),
                TotalQuantity = Math.Max(1, entriesToSubmit) * Math.Max(1, quantityPerEntry),
                LastPublishedRemaining = 0,
                OpenPublished = false,
                ClosedPublished = false
            };

            multiEntrySyncGroups[group.TradeId] = group;
            return group;
        }

        private void AttachTradeStateToSyncGroup(TradeRuntimeState state, MultiEntrySyncGroup group)
        {
            if (state == null || group == null)
                return;

            state.SyncTradeId = group.TradeId;
        }

        private bool TryGetMultiEntrySyncGroupByTradeId(string tradeId, out MultiEntrySyncGroup group)
        {
            group = null;
            if (string.IsNullOrEmpty(tradeId))
                return false;

            if (multiEntrySyncGroups.TryGetValue(tradeId, out group))
                return true;

            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out var state) &&
                state != null &&
                !string.IsNullOrEmpty(state.SyncTradeId))
            {
                return multiEntrySyncGroups.TryGetValue(state.SyncTradeId, out group);
            }

            return false;
        }

        private int GetMultiEntrySyncTotalQuantity(string syncTradeId, int fallbackQty)
        {
            if (string.IsNullOrEmpty(syncTradeId))
                return Math.Max(1, fallbackQty);

            int total = 0;
            if (tradeStates != null)
            {
                foreach (var state in tradeStates.Values)
                {
                    if (state == null || string.IsNullOrEmpty(state.SyncTradeId))
                        continue;
                    if (!string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    total += Math.Max(1, state.OriginalQuantity);
                }
            }

            return total > 0 ? total : Math.Max(1, fallbackQty);
        }

        private int GetMultiEntrySyncRemainingQuantity(string syncTradeId)
        {
            if (string.IsNullOrEmpty(syncTradeId))
                return 0;

            int total = 0;
            if (tradeStates != null)
            {
                foreach (var state in tradeStates.Values)
                {
                    if (state == null || string.IsNullOrEmpty(state.SyncTradeId))
                        continue;
                    if (!string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    total += Math.Max(0, state.RemainingQuantity);
                }
            }

            return Math.Max(0, total);
        }

        private List<TradeRuntimeState> GetMultiEntrySyncStates(string syncTradeId)
        {
            var states = new List<TradeRuntimeState>();
            if (string.IsNullOrEmpty(syncTradeId) || tradeStates == null || tradeStates.Count == 0)
                return states;

            if (openTradeOrder != null && openTradeOrder.Count > 0)
            {
                foreach (var tradeId in openTradeOrder)
                {
                    if (tradeStates.TryGetValue(tradeId, out var state) &&
                        state != null &&
                        string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                    {
                        states.Add(state);
                    }
                }
            }
            else
            {
                foreach (var state in tradeStates.Values)
                {
                    if (state != null && string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                        states.Add(state);
                }
            }

            return states;
        }

        private TradeRuntimeState ResolvePrimarySyncState(string syncTradeId)
        {
            if (string.IsNullOrEmpty(syncTradeId) || tradeStates == null || tradeStates.Count == 0)
                return null;

            if (!string.IsNullOrEmpty(activeTradeId) &&
                tradeStates.TryGetValue(activeTradeId, out var active) &&
                active != null &&
                string.Equals(active.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
            {
                return active;
            }

            var states = GetMultiEntrySyncStates(syncTradeId);
            return states.Count > 0 ? states[states.Count - 1] : null;
        }

        private void CleanupMultiEntrySyncGroup(string syncTradeId)
        {
            if (string.IsNullOrEmpty(syncTradeId))
                return;

            if (tradeStates != null)
            {
                foreach (var state in tradeStates.Values)
                {
                    if (state != null &&
                        string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            multiEntrySyncGroups.Remove(syncTradeId);
        }

        private bool PublishPendingCloses()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            bool hasPending = false;

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null)
                    continue;
                if (!state.OpenPublished)
                    continue;

                try
                {
                    if (PublishClosedEvent(state.TradeId))
                    {
                        tradeStates.Remove(state.TradeId);
                        CleanupMultiEntrySyncGroup(state.SyncTradeId);
                    }
                    else
                    {
                        state.PendingClosePublish = true;
                        hasPending = true;
                    }
                }
                catch { }
            }

            return hasPending;
        }

        private TradeRuntimeState PrepareTradeState(string tradeId, MarketPosition side, int quantityHint)
        {
            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            // Ensure no prior global stop/target bleeds into a fresh trade
            ClearGlobalStopsTargets();

            var state = new TradeRuntimeState
            {
                TradeId = tradeId,
                SyncTradeId = null,
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
                RunUpLowWater = 0,
                SyntheticLogEmitted = false,
                Bootstrapped = false,
                AllowOpenPublish = false,
                EntryOrder = null,
                StopOrder = null,
                TargetOrder = null
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
            bool syncBehavior = StartBehavior == StartBehavior.ImmediatelySubmitSynchronizeAccount;
            bool missingEntrySignal = string.IsNullOrEmpty(execution.Order.FromEntrySignal);

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

             MarketPosition inferredSide = (action == OrderAction.SellShort || action == OrderAction.Sell)
                 ? MarketPosition.Short
                 : MarketPosition.Long;

            // When StartBehavior=SynchronizeAccount, entry executions also have an empty FromEntrySignal.
            // Only treat the fill as a sync-start "bootstrapped" entry if we do NOT already have a
            // pre-created runtime state for the order name (normal strategy entries call PrepareTradeState first).
            bool hasPreparedStateForEntry = false;
            if (isEntryAction && missingEntrySignal && syncBehavior && tradeStates != null)
            {
                string orderSignal = execution.Order?.Name;
                if (!string.IsNullOrEmpty(orderSignal) &&
                    tradeStates.TryGetValue(orderSignal, out var prepared) &&
                    prepared != null &&
                    !prepared.Bootstrapped)
                {
                    hasPreparedStateForEntry = true;
                }
            }

            bool isSyncEntryFill = isEntryAction && missingEntrySignal && syncBehavior && !hasPreparedStateForEntry;

            string tradeId = !string.IsNullOrEmpty(execution.Order.FromEntrySignal)
                ? execution.Order.FromEntrySignal
                : execution.Order.Name;

            // For sync-behavior bootstrap fills Ninja produces an empty FromEntrySignal; assign our own
            // trade id so downstream stop/target mapping and AddOn events work like normal entries.
            if (isSyncEntryFill)
            {
                // If we already bootstrapped a trade state from PositionUpdate, reuse it instead of creating
                // a new tradeId (prevents duplicate protective orders on sync entries).
                if (tradeStates != null && tradeStates.Count > 0)
                {
                    if (!string.IsNullOrEmpty(activeTradeId) && tradeStates.ContainsKey(activeTradeId))
                        tradeId = activeTradeId;
                    else
                    {
                        var boot = tradeStates.Values.FirstOrDefault(st => st != null && st.Bootstrapped && st.EntrySide == inferredSide);
                        if (boot != null && !string.IsNullOrEmpty(boot.TradeId))
                            tradeId = boot.TradeId;
                        else
                            tradeId = CreateTradeId(inferredSide);
                    }
                }
                else
                {
                    tradeId = CreateTradeId(inferredSide);
                }
            }

            bool isLiveExecution = IsLiveExecutionContext(execution);
            if (!isLiveExecution && State != State.Realtime && !isSyncEntryFill)
                return;
            // Allow historical/execution replay to flow through so chart markers and status stay in sync.
            // We still suppress TradeSync publishes for synthetic/historical trades below.
            if (!isLiveExecution && Debug)
                StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Processing non-realtime execution for trade {1}", time, tradeId ?? "<unknown>"));

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
                // Sync-entry fills should map to any existing bootstrapped state instead of spawning a new one.
                if (isSyncEntryFill && tradeStates.Count > 0)
                {
                    var boot = tradeStates.Values.FirstOrDefault(st => st != null && st.Bootstrapped && st.EntrySide == inferredSide);
                    if (boot != null)
                    {
                        state = boot;
                        tradeId = boot.TradeId;
                    }
                }

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
                    state = PrepareTradeState(tradeId, inferredSide, Math.Max(1, Math.Abs((int)execution.Order.Quantity)));
                }
                else if (isExitAction)
                {
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

            // Mark sync-start fills as bootstrapped so we use explicit stop/target orders (global signal)
            // instead of SetStopLoss/SetProfitTarget that require a matching entry signal.
            if (isSyncEntryFill)
            {
                state.Bootstrapped = true;
                state.IsSynthetic = false;
                state.SyntheticLogEmitted = false;
            }

            // Treat Buy/SellShort as entries; BuyToCover/Sell as exits. Ninja leaves FromEntrySignal empty for many manual actions,
            // so rely on OrderAction instead of FromEntrySignal alone to avoid misclassifying exits as new entries.
            bool isEntry = isEntryAction && !exitOnClose;
            bool isLive = IsLiveExecutionContext(execution);

            if (isEntry)
            {
                HandleEntryExecution(execution, state, isSyncEntryFill);
                if (!isLive)
                    state.IsSynthetic = true; // keep publishes suppressed for historical markers

                if (!state.IsSynthetic && !state.OpenPublished && state.AllowOpenPublish)
                {
                    if (PublishOpenEvent(state))
                        state.OpenPublished = true;
                }
            }
            else
            {
                HandleExitExecution(execution, state);
            }

            // If we were synthetic but now have a live execution, ensure management is active.
            if (isLive && state.IsSynthetic)
            {
                state.IsSynthetic = false;
                stopSet = false;
                targetSet = false;
                UpdateStopsTargets(GetRealtimePrice());
                EnsureProtectionForActiveTrade(state, GetRealtimePrice());
            }
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

            // For start-behavior sync entries the account can move from flat to live between bar closes.
            // Bootstrap immediately here so we generate a tradeId, publish to the AddOn, and arm SL/TP
            // without waiting for the next primary-bar OnBarUpdate.
            if (State == State.Realtime &&
                marketPosition != MarketPosition.Flat &&
                quantity != 0 &&
                (tradeStates == null || tradeStates.Count == 0))
            {
                BootstrapExistingPositionState();

                if (tradeStates != null && tradeStates.Count > 0)
                {
                    UpdateStopsTargets(GetRealtimePrice());
                    if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out var seededState))
                        EnsureProtectionForActiveTrade(seededState, GetRealtimePrice());
                    UpdateStatusLabel($"Managing {marketPosition} {quantity} ({activeTradeId ?? "<pending>"})", true);
                }
            }

            // If the platform reports flat but we still have runtime state, clear it to avoid ghost trades.
            if (marketPosition == MarketPosition.Flat || quantity == 0)
            {
                bool pending = false;
                if (tradeStates != null && tradeStates.Count > 0)
                {
                    foreach (var st in tradeStates.Values.ToList())
                        CancelProtectiveOrders(st);
                    pending = PublishPendingCloses();
                    if (!pending)
                    {
                        ResetTradeState();
                        UpdateStatusLabel("Active: scanning (position flat)", true);
                    }
                }
                // Even if pending closes remain, do not block signal processing.
                if (!pending)
                    return;
            }
        }

        private void HandleEntryExecution(Execution execution, TradeRuntimeState state, bool isSyncEntryFill = false)
        {
            if (state != null)
            {
                state.IsSynthetic = false;
                state.SyntheticLogEmitted = false;
                // Only arm publish on real-time sync fills or executions flagged live.
                if (isSyncEntryFill || IsLiveExecutionContext(execution))
                    state.AllowOpenPublish = true;
            }

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

            bool hasWorkingStop = (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState)) || state.LastStopPrice > 0;
            bool hasWorkingTarget = (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState)) || state.LastTargetPrice > 0;

            if (state.Bootstrapped && (hasWorkingStop || hasWorkingTarget))
            {
                // Preserve existing protection placed during PositionUpdate bootstrap to avoid duplicates.
                stopSet = hasWorkingStop;
                targetSet = hasWorkingTarget;
            }
            else
            {
                stopSet = false;
                targetSet = false;
            }

            ResetDemaTrailingState();
            activeTradeId = state.TradeId;

            // Publish open for all live entries (including sync-behavior fills).
            if (!state.OpenPublished && !state.IsSynthetic && state.AllowOpenPublish)
            {
                if (PublishOpenEvent(state))
                    state.OpenPublished = true;
            }

            // For start-behavior sync fills, arm protection immediately instead of waiting for the next bar.
            if (isSyncEntryFill)
            {
                EnsureProtectionForActiveTrade(state, execution != null ? execution.Price : GetRealtimePrice());
                UpdateStatusLabel($"Managing {state.EntrySide} {state.RemainingQuantity} ({state.TradeId})", true);
            }
            else if (IsLiveExecutionContext(execution))
            {
                EnsureProtectionForActiveTrade(state, execution != null ? execution.Price : GetRealtimePrice());
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
                CancelProtectiveOrders(state, execution != null ? execution.Order : null);
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
                {
                    if (!PublishClosedEvent(state.TradeId))
                    {
                        state.PendingClosePublish = true;
                        state.RemainingQuantity = 0;
                        state.OpenPublished = true;
                        if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                            activeTradeId = null;
                        return; // keep state so we can retry close publish later
                    }
                }
                if (state.ManualStopOverride || state.ManualTargetOverride)
                    NotifyAddonManualOverride(state.TradeId,
                        state.ManualStopOverride ? false : (bool?)null,
                        state.ManualTargetOverride ? false : (bool?)null);
                state.RunUpActive = false;
                state.RunUpLastStopPrice = null;
                string syncTradeId = state.SyncTradeId;
                tradeStates.Remove(state.TradeId);
                openTradeOrder.Remove(state.TradeId);
                CleanupMultiEntrySyncGroup(syncTradeId);

                if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                    activeTradeId = null;

                if (string.IsNullOrEmpty(activeTradeId) && openTradeOrder.Count > 0)
                    activeTradeId = openTradeOrder[openTradeOrder.Count - 1];

                if (state.Bootstrapped)
                    ClearGlobalStopsTargets();

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

            int barIndex = Bars.GetBar(execution.Time);
            if (barIndex < 0)
                barIndex = CurrentBar;
            int barsAgo = Math.Max(0, CurrentBar - barIndex);
            Draw.Text(this, tag, false, label, barsAgo, execution.Price, 0, brush, new SimpleFont("Arial", 12), TextAlignment.Center, null, null, 0);
        }

        private bool PublishOpenEvent(TradeRuntimeState state)
        {
            if (state == null || !state.AllowOpenPublish)
                return false;

            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][SYNC] PublishOpen skipped (manager or TradeSync null) for {0}", state?.TradeId ?? "<null>"));
                return false;
            }
            if (State != State.Realtime)
                return false;

            if (!string.IsNullOrEmpty(state.SyncTradeId) && TryGetMultiEntrySyncGroupByTradeId(state.TradeId, out var group))
            {
                if (group.OpenPublished)
                    return true;

                int totalQty = group.TotalQuantity > 0
                    ? group.TotalQuantity
                    : GetMultiEntrySyncTotalQuantity(group.TradeId, state.OriginalQuantity);
                group.TotalQuantity = totalQty;
                group.LastPublishedRemaining = totalQty;

                try
                {
                    manager.TradeSync.PublishOpen(this, group.TradeId, state.InstrumentName, state.EntrySide, totalQty, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice, true);
                    if (Debug)
                        StrategyLogDebug(string.Format("[AUTO][SYNC] Published OPEN for {0} qty={1} side={2} price={3:F2} (grouped)", group.TradeId, totalQty, state.EntrySide, state.EntryPrice));
                    tradeSyncWarned = false;
                    group.OpenPublished = true;
                    return true;
                }
                catch (Exception ex)
                {
                    if (!tradeSyncWarned)
                        StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish open for {0}: {1}", group.TradeId ?? "<unknown>", ex.Message));
                    WarnTradeSyncOffline();
                    return false;
                }
            }

            try
            {
                manager.TradeSync.PublishOpen(this, state.TradeId, state.InstrumentName, state.EntrySide, state.OriginalQuantity, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice);
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][SYNC] Published OPEN for {0} qty={1} side={2} price={3:F2}", state.TradeId, state.OriginalQuantity, state.EntrySide, state.EntryPrice));
                tradeSyncWarned = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!tradeSyncWarned)
                    StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish open for {0}: {1}", state.TradeId ?? "<unknown>", ex.Message));
                WarnTradeSyncOffline();
                return false;
            }
        }

        private void PublishPartialEvent(string tradeId, int remainingQuantity)
        {
            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return;
            if (!IsTradeSyncReady(manager))
            {
                WarnTradeSyncOffline();
                return;
            }

            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
            {
                if (group.ClosedPublished)
                    return;

                int remaining = GetMultiEntrySyncRemainingQuantity(group.TradeId);
                if (remaining <= 0 || remaining == group.LastPublishedRemaining)
                    return;

                try
                {
                    manager.TradeSync.PublishPartial(this, group.TradeId, remaining);
                    group.LastPublishedRemaining = remaining;
                    tradeSyncWarned = false;
                }
                catch (Exception ex)
                {
                    if (!tradeSyncWarned)
                        StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish partial for {0}: {1}", group.TradeId ?? "<unknown>", ex.Message));
                    WarnTradeSyncOffline();
                }
                return;
            }

            try
            {
                manager.TradeSync.PublishPartial(this, tradeId, remainingQuantity);
                tradeSyncWarned = false;
            }
            catch (Exception ex)
            {
                if (!tradeSyncWarned)
                    StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish partial for {0}: {1}", tradeId ?? "<unknown>", ex.Message));
                WarnTradeSyncOffline();
            }
        }

        private bool PublishClosedEvent(string tradeId)
        {
            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return false;

            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
            {
                int remaining = GetMultiEntrySyncRemainingQuantity(group.TradeId);
                if (remaining > 0)
                {
                    PublishPartialEvent(tradeId, remaining);
                    return true;
                }

                if (group.ClosedPublished)
                    return true;

                try
                {
                    manager.TradeSync.PublishClosed(this, group.TradeId);
                    group.ClosedPublished = true;
                    group.LastPublishedRemaining = 0;
                    tradeSyncWarned = false;
                    return true;
                }
                catch (Exception ex)
                {
                    if (!tradeSyncWarned)
                        StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish closed for {0}: {1}", group.TradeId ?? "<unknown>", ex.Message));
                    WarnTradeSyncOffline();
                    return false;
                }
            }

            try
            {
                manager.TradeSync.PublishClosed(this, tradeId);
                tradeSyncWarned = false;
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out var st))
                    st.PendingClosePublish = false;
                return true;
            }
            catch (Exception ex)
            {
                if (!tradeSyncWarned)
                    StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish closed for {0}: {1}", tradeId ?? "<unknown>", ex.Message));
                WarnTradeSyncOffline();
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out var st))
                    st.PendingClosePublish = true;
                return false;
            }
        }

        private static bool IsTerminalState(OrderState state)
        {
            return state == OrderState.Cancelled
                || state == OrderState.Filled
                || state == OrderState.Rejected
                || state == OrderState.Unknown;
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

            if (looksManual || Debug)
            {
                StrategyLogInfo(string.Format("[AUTO][ORDERUPD] name={0} fromEntry={1} action={2} state={3} qty={4} filled={5} oco={6} stop={7} limit={8} error={9} native='{10}'",
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
            }

            string resolvedTradeId = ResolveTradeIdFromOrder(order);
            if (string.IsNullOrEmpty(order.FromEntrySignal))
            {
                string entryKey = order.Name;
                if (!string.IsNullOrEmpty(entryKey) &&
                    tradeStates != null &&
                    tradeStates.TryGetValue(entryKey, out var entryState))
                {
                    bool isEntryAction = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
                    if (isEntryAction)
                    {
                        entryState.EntryOrder = order;
                        if (IsTerminalState(orderState))
                            entryState.EntryOrder = null;
                    }
                }
            }
            if (!string.IsNullOrEmpty(resolvedTradeId) && TryGetTradeState(resolvedTradeId, out var state))
            {
                // Only treat clearly-tagged protective orders as stops/targets.
                bool isStopOrder = name.IndexOf("_BS", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isStopOrder && (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit))
                    isStopOrder = name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isTargetOrder = name.IndexOf("_BT", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isTargetOrder && order.OrderType == OrderType.Limit)
                    isTargetOrder = name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;

                // Re-arm protection if a bootstrapped stop/target gets cancelled/rejected (common when qty=0 or signal missing).
                if ((orderState == OrderState.Cancelled || orderState == OrderState.Rejected) && state.Bootstrapped)
                {
                    if (State != State.Realtime || Position == null || Position.MarketPosition == MarketPosition.Flat || state.RemainingQuantity <= 0)
                        return;

                        // Track re-arms; keep protection persistent without hard throttling.
                        var now = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                        if ((now - state.LastProtectionRetry).TotalMinutes > 5)
                            state.ProtectionRetryCount = 0;
                        state.ProtectionRetryCount++;
                        state.LastProtectionRetry = now;

                    if (isStopOrder)
                    {
                        // Ignore cancels for legacy/old stop orders if we already have a different tracked stop working.
                        if (state.StopOrder != null && !ReferenceEquals(order, state.StopOrder) && !IsTerminalState(state.StopOrder.OrderState))
                            return;
                        // If we already have a working stop at the same price, do not re-arm.
                        double effectivePrice = stopPrice > 0 ? stopPrice : (order.StopPrice > 0 ? order.StopPrice : 0);
                        if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState) && state.LastStopPrice > 0 && PricesClose(state.LastStopPrice, effectivePrice))
                            return;

                        // If run-up is active, reapply the run-up stop instead of resetting to baseline.
                        if (state.RunUpActive)
                        {
                            double desired = state.RunUpLastStopPrice ?? state.LastStopPrice;
                            if (desired > 0)
                            {
                                if (IssueStopLoss(resolvedTradeId, CalculationMode.Price, desired, false))
                                {
                                    state.LastStopPrice = desired;
                                    state.PendingAutoStopUpdate = false;
                                    state.PendingAutoStopPrice = 0;
                                    stopSet = true;
                                }
                                return;
                            }
                        }

                        state.PendingAutoStopUpdate = false;
                        state.PendingAutoStopPrice = 0;
                        state.LastStopPrice = 0;
                        state.StopOrder = null;

                        // Recompute and re-arm immediately to keep protection active.
                        if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                        {
                            stopSet = false;
                            targetSet = targetSet && state.LastTargetPrice > 0;
                            state.ProtectionRearmCount++;
                            state.LastProtectionRearm = now;
                            EnsureProtectionForActiveTrade(state, GetRealtimePrice());
                        }
                    }
                    else if (isTargetOrder)
                    {
                        // Ignore cancels for legacy/old target orders if we already have a different tracked target working.
                        if (state.TargetOrder != null && !ReferenceEquals(order, state.TargetOrder) && !IsTerminalState(state.TargetOrder.OrderState))
                            return;
                        double effectivePrice = limitPrice > 0 ? limitPrice : (order.LimitPrice > 0 ? order.LimitPrice : 0);
                        if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState) && state.LastTargetPrice > 0 && PricesClose(state.LastTargetPrice, effectivePrice))
                            return;

                        state.PendingAutoTargetUpdate = false;
                        state.LastTargetPrice = 0;
                        state.TargetOrder = null;

                        if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                        {
                            targetSet = false;
                            stopSet = stopSet && state.LastStopPrice > 0;
                            state.ProtectionRearmCount++;
                            state.LastProtectionRearm = now;
                            EnsureProtectionForActiveTrade(state, GetRealtimePrice());
                        }
                    }
                }
            }

            DetectManualStopTargetAdjustments(order, limitPrice, stopPrice, orderState);
            HandleStopUpdateErrors(order, stopPrice, orderState, error, nativeError);
        }

        private string ResolveTradeIdFromOrder(Order order)
        {
            if (order == null)
                return null;
            if (!string.IsNullOrEmpty(order.FromEntrySignal))
                return order.FromEntrySignal;

            string name = order.Name;
            if (string.IsNullOrEmpty(name))
                return null;

            int underscore = name.IndexOf('_');
            if (underscore > 0)
                return name.Substring(0, underscore);

            // As a last resort, use activeTradeId if side matches the order action
            if (!string.IsNullOrEmpty(activeTradeId))
                return activeTradeId;
            return null;
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
                state.StopOrder = order;
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
                    stopSet = IsTerminalState(orderState) ? false : stopSet;
            }
            if (isTargetOrder)
            {
                state.TargetOrder = order;
                if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Filled)
                    targetSet = IsTerminalState(orderState) ? false : targetSet;
            }

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
                    StrategyLogInfo(string.Format("[AUTO][STOP] Detected manual stop move for {0} -> {1:F2}; auto trailing disabled for this trade.", tradeId, effectivePrice));
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
                    StrategyLogInfo(string.Format("[AUTO][TARGET] Detected manual target move for {0} -> {1:F2}; auto target locked for this trade.", tradeId, effectivePrice));
                    if (!wasLocked)
                        NotifyAddonManualOverride(tradeId, null, true);
                    AlignManagedTargetWithManual(tradeId, effectivePrice);
                }
            }
        }

        // Flatten any open position when the strategy terminates or encounters fatal errors.
        private void TryFlattenActivePosition(string reason)
        {
            try
            {
                if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity <= 0)
                    return;

                int qty = Position.Quantity;
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    StrategyLogInfo($"[SAFETY] Flattening long {qty} due to {reason}");
                    ExitLong(qty, "STRAT_TERM_FLAT_L", activeTradeId);
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    StrategyLogInfo($"[SAFETY] Flattening short {qty} due to {reason}");
                    ExitShort(qty, "STRAT_TERM_FLAT_S", activeTradeId);
                }
            }
            catch (Exception ex)
            {
                StrategyLogError($"[SAFETY] Failed to flatten on termination: {ex.Message}");
            }
        }

        private void CancelWorkingEntryOrders(string reason)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null)
                    continue;

                var order = state.EntryOrder;
                if (order == null || IsTerminalState(order.OrderState))
                    continue;

                try
                {
                    CancelOrder(order);
                    StrategyLogInfo($"[SAFETY] Cancelled entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}");
                }
                catch (Exception ex)
                {
                    StrategyLogError($"[SAFETY] Failed to cancel entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}: {ex.Message}");
                }
            }
        }

        private bool HasWorkingEntryOrders()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state == null)
                    continue;
                var order = state.EntryOrder;
                if (order == null)
                    continue;
                if (!IsTerminalState(order.OrderState))
                    return true;
            }

            return false;
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
            if (!IsTradeSyncReady(manager))
            {
                WarnTradeSyncOffline();
                return;
            }

            string syncTradeId = ResolveSyncTradeId(tradeId);
            manager.TradeSync.PublishManualOverride(this, syncTradeId, stopLocked, targetLocked);
        }

        private string ResolveSyncTradeId(string tradeId)
        {
            if (string.IsNullOrEmpty(tradeId))
                return tradeId;

            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
                return group.TradeId;

            return tradeId;
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
                StrategyLogError(string.Format("[AUTO][STOP] Failed to align managed stop for {0}: {1}", tradeId, ex.Message));
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
                StrategyLogError(string.Format("[AUTO][TARGET] Failed to align managed target for {0}: {1}", tradeId, ex.Message));
            }
            finally
            {
                state.PendingAutoTargetUpdate = false;
            }
        }

        private void CancelProtectiveOrders(TradeRuntimeState state, Order filledOrder = null)
        {
            if (state == null)
                return;

            TryCancelOrder(state.TradeId, state.StopOrder, filledOrder, "stop");
            TryCancelOrder(state.TradeId, state.TargetOrder, filledOrder, "target");
            state.StopOrder = null;
            state.TargetOrder = null;
        }

        private void TryCancelOrder(string tradeId, Order order, Order filledOrder, string label)
        {
            if (order == null || ReferenceEquals(order, filledOrder) || IsTerminalState(order.OrderState))
                return;

            try
            {
                CancelOrder(order);
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][CANCEL] Cancelled {0} for {1} (state={2})", label, tradeId ?? "<unknown>", order.OrderState));
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][CANCEL] Failed to cancel {0} for {1}: {2}", label, tradeId ?? "<unknown>", ex.Message));
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

        private bool IsTradeSyncReady(MultiStratManager manager)
        {
            if (manager == null || manager.TradeSync == null)
                return false;

            try
            {
                var ts = manager.TradeSync;
                var prop = ts.GetType().GetProperty("IsReady") ?? ts.GetType().GetProperty("IsConnected") ?? ts.GetType().GetProperty("IsInitialized");
                if (prop != null && prop.PropertyType == typeof(bool))
                {
                    bool ready = (bool)prop.GetValue(ts, null);
                    if (ready)
                        tradeSyncWarned = false;
                    return ready;
                }
            }
            catch (Exception ex)
            {
                if (Debug && !tradeSyncWarned)
                    StrategyLogDebug(string.Format("[AUTO][SYNC] TradeSync readiness check failed: {0}", ex.Message));
            }

            return false;
        }

        private void UpdateStatusLabel(string message, bool healthy)
        {
            if (State == State.Realtime && dailyPnLLimitHalted && !string.IsNullOrWhiteSpace(dailyPnLLimitStatusText))
            {
                message = dailyPnLLimitStatusText;
                healthy = false;
            }
            else if (State == State.Realtime && manualHaltActive)
            {
                message = string.IsNullOrWhiteSpace(manualHaltStatusText) ? "HALTED: manual (awaiting resume)" : manualHaltStatusText;
                healthy = false;
            }

            string line1 = $"AUTO: {message}";
            string pnlLine;
            string limitsLine;
            bool pnlNegative;
            bool hasPnLLines = TryBuildDailyPnLLimitLines(out pnlLine, out pnlNegative, out limitsLine);
            string composite = hasPnLLines ? line1 + "\n" + pnlLine + "\n" + limitsLine : line1;

            if (string.Equals(composite, lastStatusText, StringComparison.Ordinal) &&
                healthy == lastStatusHealthy &&
                hasPnLLines == lastStatusHasPnLLines &&
                pnlNegative == lastStatusPnlNegative)
            {
                return;
            }

            lastStatusText = composite;
            lastStatusHealthy = healthy;
            lastStatusHasPnLLines = hasPnLLines;
            lastStatusPnlNegative = pnlNegative;

            var font = new SimpleFont("Arial", 13) { Bold = true };
            var color = healthy ? Brushes.LimeGreen : Brushes.OrangeRed;
            try
            {
                string line1Text = hasPnLLines ? line1 + "\n\n" : line1;
                Draw.TextFixed(this, "BaseOptAutoStatus", line1Text, TextPosition.BottomLeft, color, font, Brushes.Black, Brushes.Transparent, 0);

                if (hasPnLLines)
                {
                    var pnlColor = pnlNegative ? Brushes.Red : Brushes.LimeGreen;
                    var limitsColor = Brushes.LimeGreen;
                    var transparent = Brushes.Transparent;
                    Draw.TextFixed(this, "BaseOptAutoPnl", pnlLine + "\n", TextPosition.BottomLeft, pnlColor, font, transparent, transparent, 0);
                    Draw.TextFixed(this, "BaseOptAutoLimits", limitsLine, TextPosition.BottomLeft, limitsColor, font, transparent, transparent, 0);
                }
                else
                {
                    RemoveDrawObject("BaseOptAutoPnl");
                    RemoveDrawObject("BaseOptAutoLimits");
                }
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][STATUS] Failed to draw status label: {0}", ex.Message));
            }
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
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(2, 4, 2, 2)
                    };

                    manualFlattenButton = new Button
                    {
                        Content = "Flatten + Halt",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    manualFlattenButton.Click += ManualFlattenButton_Click;

                    manualResumeButton = new Button
                    {
                        Content = "Resume",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    manualResumeButton.Click += ManualResumeButton_Click;

                    chartTraderButtonPanel.Children.Add(manualFlattenButton);
                    chartTraderButtonPanel.Children.Add(manualResumeButton);

                    Grid.SetRow(chartTraderButtonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                    Grid.SetColumnSpan(chartTraderButtonPanel, Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                    chartTraderGrid.Children.Add(chartTraderButtonPanel);

                    chartTraderButtonsAdded = true;
                });
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[UI] Failed to init ChartTrader buttons: {ex.Message}");
            }
        }

        private void RemoveChartTraderButtons()
        {
            if (!chartTraderButtonsAdded || ChartControl == null)
                return;

            try
            {
                ChartControl.Dispatcher.InvokeAsync(() =>
                {
                    if (chartTraderButtonPanel != null && chartTraderGrid != null)
                        chartTraderGrid.Children.Remove(chartTraderButtonPanel);

                    if (chartTraderButtonsRow != null && chartTraderGrid != null)
                        chartTraderGrid.RowDefinitions.Remove(chartTraderButtonsRow);

                    if (manualFlattenButton != null)
                        manualFlattenButton.Click -= ManualFlattenButton_Click;
                    if (manualResumeButton != null)
                        manualResumeButton.Click -= ManualResumeButton_Click;

                    chartTraderButtonPanel = null;
                    manualFlattenButton = null;
                    manualResumeButton = null;
                    chartTraderButtonsRow = null;
                    chartTraderGrid = null;
                    chartTrader = null;
                    chartWindow = null;
                    chartTraderButtonsAdded = false;
                });
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[UI] Failed to remove ChartTrader buttons: {ex.Message}");
            }
        }

        private void ManualFlattenButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualHaltRequest(), null);
        }

        private void ManualResumeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualResumeRequest(), null);
        }

        private void HandleManualHaltRequest()
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[MANUAL_HALT] Flatten ignored (strategy not realtime).");
                return;
            }

            manualHaltActive = true;
            manualHaltActivatedAt = DateTime.UtcNow;
            manualHaltStatusText = "HALTED: manual flatten (awaiting resume)";

            try
            {
                MultiStratManager.Instance?.ActivateManualHaltOverride(Account != null ? Account.Name : string.Empty, Name);
            }
            catch { }

            CancelWorkingEntryOrders("manual_halt");
            int submitted = SubmitManualHaltExits("MHLT");
            if (submitted == 0)
                TryFlattenAccountEverything("manual_halt", activeTradeId ?? string.Empty);
            StrategyLogInfo($"[MANUAL_HALT] Flatten requested (submittedExits={submitted}).");
            UpdateStatusLabel(manualHaltStatusText, false);
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
                UpdateStatusLabel("Active: already running", true);
                return;
            }

            manualHaltActive = false;
            manualHaltStatusText = null;
            manualHaltActivatedAt = DateTime.MinValue;
            try
            {
                MultiStratManager.Instance?.ClearManualHaltOverride(Account != null ? Account.Name : string.Empty, "manual_resume");
            }
            catch { }
            StrategyLogInfo("[MANUAL_HALT] Strategy resumed by user.");
            UpdateStatusLabel("Active: resumed (manual)", true);
        }

        private int SubmitManualHaltExits(string reasonSuffix)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return 0;

            int submitted = 0;
            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null)
                    continue;

                int qty = Math.Max(0, state.RemainingQuantity);
                if (qty <= 0)
                    continue;

                string exitSignal = BuildExitSignalName(state.TradeId, reasonSuffix);
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, state.TradeId);
                else
                    ExitShort(qty, exitSignal, state.TradeId);
                submitted++;
            }

            return submitted;
        }

        private static T FindFirstChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                T descendant = FindFirstChild<T>(child);
                if (descendant != null)
                    return descendant;
            }

            return null;
        }

        private bool TryBuildDailyPnLLimitLines(out string pnlLine, out bool pnlNegative, out string limitsLine)
        {
            pnlLine = null;
            limitsLine = null;
            pnlNegative = false;

            if (Account == null)
                return false;

            double totalPnL;
            if (!TryGetAccountTotalPnL(out totalPnL))
                return false;

            double absPnL = Math.Abs(totalPnL);
            pnlNegative = totalPnL < -0.005;
            string pnlValue = pnlNegative ? "-" + absPnL.ToString("C2") : absPnL.ToString("C2");
            pnlLine = $"TotalPnL: {pnlValue}";

            double lossLimit = DailyLossLimit;
            if (lossLimit > 0)
                lossLimit = -Math.Abs(lossLimit);

            double profitLimit = DailyProfitLimit;
            if (profitLimit < 0)
                profitLimit = Math.Abs(profitLimit);

            string dllText = Math.Abs(lossLimit) > 1e-9 ? lossLimit.ToString("C2") : "off";
            string dplText = Math.Abs(profitLimit) > 1e-9 ? profitLimit.ToString("C2") : "off";
            limitsLine = $"DLL: {dllText} | DPL: {dplText}";
            return true;
        }

        private bool TryCheckDailyPnLLimit(out string statusText)
        {
            statusText = null;

            if (Account == null)
                return false;

            double totalPnL;
            if (!TryGetAccountTotalPnL(out totalPnL))
                return false;

            double lossLimit = DailyLossLimit;
            if (lossLimit > 0)
                lossLimit = -Math.Abs(lossLimit);

            double profitLimit = DailyProfitLimit;
            if (profitLimit < 0)
                profitLimit = Math.Abs(profitLimit);

            bool hasLoss = Math.Abs(lossLimit) > 1e-9;
            bool hasProfit = Math.Abs(profitLimit) > 1e-9;
            if (!hasLoss && !hasProfit)
                return false;

            if (hasLoss && totalPnL <= lossLimit + 1e-9)
            {
                dailyPnLLimitProfitCandidateAt = DateTime.MinValue;
                dailyPnLLimitProfitCandidatePnL = 0.0;
                TriggerDailyPnLLimit("DLL", totalPnL);
                statusText = BuildDailyPnLLimitStatusText(totalPnL);
                return true;
            }

            if (hasProfit && totalPnL >= profitLimit - 1e-9)
            {
                if (dailyPnLLimitProfitCandidateAt == DateTime.MinValue)
                {
                    dailyPnLLimitProfitCandidateAt = DateTime.UtcNow;
                    dailyPnLLimitProfitCandidatePnL = totalPnL;
                    return false;
                }

                if (totalPnL > dailyPnLLimitProfitCandidatePnL)
                    dailyPnLLimitProfitCandidatePnL = totalPnL;

                var elapsed = DateTime.UtcNow - dailyPnLLimitProfitCandidateAt;
                if (elapsed.TotalSeconds < DailyPnLLimitProfitConfirmSeconds)
                    return false;

                TriggerDailyPnLLimit("DPL", totalPnL);
                statusText = BuildDailyPnLLimitStatusText(totalPnL);
                return true;
            }

            dailyPnLLimitProfitCandidateAt = DateTime.MinValue;
            dailyPnLLimitProfitCandidatePnL = 0.0;
            return false;
        }

        private static bool IsSimulatedAccountName(string accountName)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return false;

            string trimmed = accountName.Trim();
            if (trimmed.StartsWith("Sim", StringComparison.OrdinalIgnoreCase))
                return true;
            if (trimmed.StartsWith("Playback", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private void MaybeClearDailyPnLLimitForSimReset()
        {
            try
            {
                if (!dailyPnLLimitHalted || Account == null)
                    return;

                if (!IsSimulatedAccountName(Account.Name))
                    return;

                // If the AddOn override is still active, do not clear local halt yet.
                try
                {
                    var manager = MultiStratManager.Instance;
                    if (manager != null && manager.TryGetDailyLimitOverride(Account.Name, out _, out _, out _))
                        return;
                }
                catch { }

                double totalPnL;
                if (!TryGetAccountTotalPnL(out totalPnL))
                    return;

                // Sim reset snaps TotalPnL back to ~0.
                if (Math.Abs(totalPnL) > 0.01)
                    return;

                int openPositions;
                int workingOrders;
                if (!TryGetAccountRiskCounts(out openPositions, out workingOrders))
                    return;

                if (openPositions != 0 || workingOrders != 0)
                    return;

                ResetDailyPnLLimitState("sim_account_reset");
            }
            catch { }
        }

        private void MaybeClearDailyPnLLimitFromManualReset()
        {
            try
            {
                if (!dailyPnLLimitHalted || Account == null)
                    return;

                var manager = MultiStratManager.Instance;
                if (manager == null)
                    return;

                if (!manager.TryGetManualDailyLimitReset(Account.Name, out var resetAtUtc))
                    return;

                if (resetAtUtc == DateTime.MinValue)
                    return;

                if (resetAtUtc <= dailyPnLLimitTriggeredAt || resetAtUtc <= dailyPnLLimitLastManualResetAt)
                    return;

                dailyPnLLimitLastManualResetAt = resetAtUtc;
                ResetDailyPnLLimitState("manual_reset");
            }
            catch { }
        }

        private void MaybeClearDailyPnLLimitIfRecovered()
        {
            try
            {
                if (!dailyPnLLimitHalted || Account == null)
                    return;

                // For live accounts we keep the latch semantics; sim accounts can auto-clear for continuous testing.
                if (!IsSimulatedAccountName(Account.Name))
                    return;

                // Avoid immediately clearing right after a trigger; give enforcement time to run.
                if (dailyPnLLimitTriggeredAt != DateTime.MinValue &&
                    (DateTime.UtcNow - dailyPnLLimitTriggeredAt) < TimeSpan.FromSeconds(10))
                {
                    return;
                }

                double totalPnL;
                if (!TryGetAccountTotalPnL(out totalPnL))
                    return;

                double lossLimit = DailyLossLimit;
                if (lossLimit > 0)
                    lossLimit = -Math.Abs(lossLimit);

                double profitLimit = DailyProfitLimit;
                if (profitLimit < 0)
                    profitLimit = Math.Abs(profitLimit);

                bool recovered;
                string type = (dailyPnLLimitType ?? string.Empty).Trim().ToUpperInvariant();
                if (type == "DLL")
                    recovered = totalPnL > lossLimit + 1e-9;
                else if (type == "DPL")
                    recovered = totalPnL < profitLimit - 1e-9;
                else
                    recovered = totalPnL > lossLimit + 1e-9 && totalPnL < profitLimit - 1e-9;

                if (!recovered)
                    return;

                int openPositions;
                int workingOrders;
                if (!TryGetAccountRiskCounts(out openPositions, out workingOrders))
                    return;

                // Only clear once the account is truly clean to avoid re-entry during an in-progress flatten.
                if (openPositions != 0 || workingOrders != 0)
                    return;

                ResetDailyPnLLimitState("pnl_recovered");
                try
                {
                    MultiStratManager.Instance?.ClearDailyLimitOverrideForAccount(Account.Name, "pnl_recovered");
                }
                catch { }
            }
            catch { }
        }

        private void MaybeHydrateDailyPnLLimitFromAddonOverride()
        {
            try
            {
                if (Account == null)
                    return;

                var manager = MultiStratManager.Instance;
                if (manager == null)
                    return;

                string overrideType;
                double overridePnL;
                DateTime overrideActivatedAt;
                if (!manager.TryGetDailyLimitOverride(Account.Name, out overrideType, out overridePnL, out overrideActivatedAt))
                    return;

                dailyPnLLimitHalted = true;
                dailyPnLLimitType = overrideType ?? string.Empty;
                dailyPnLLimitTriggeredPnL = overridePnL;
                dailyPnLLimitTriggeredAt = overrideActivatedAt != DateTime.MinValue ? overrideActivatedAt : DateTime.UtcNow;

                double currentPnL;
                if (!TryGetAccountTotalPnL(out currentPnL))
                    currentPnL = overridePnL;

                dailyPnLLimitStatusText = BuildDailyPnLLimitStatusText(currentPnL);
                StrategyLogInfo($"[DAILY_LIMIT] Hydrated halt state from AddOn override (type={dailyPnLLimitType}, triggeredPnL={overridePnL:F2}).");
            }
            catch { }
        }

        private bool TryGetAccountTotalPnL(out double totalPnL)
        {
            totalPnL = 0;

            try
            {
                var resolved = ResolveCanonicalAccount(Account);
                if (resolved == null)
                    return false;

                // Prefer the AddOn-monitored TotalPnL for this account (matches Accounts tab / gRPC stream).
                try
                {
                    var manager = MultiStratManager.Instance;
                    var monitored = TryGetMonitoredAccount(manager);
                    if (manager != null && monitored != null &&
                        string.Equals(monitored.Name, resolved.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        double addonDaily = manager.DailyPnL;
                        if (!double.IsNaN(addonDaily) && !double.IsInfinity(addonDaily))
                        {
                            totalPnL = addonDaily;
                            return true;
                        }

                        double addonTotal = manager.TotalPnL;
                        if (!double.IsNaN(addonTotal) && !double.IsInfinity(addonTotal))
                        {
                            totalPnL = addonTotal;
                            return true;
                        }
                    }
                }
                catch { }

                double realized = resolved.GetAccountItem(AccountItem.RealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0;
                double unrealized = resolved.GetAccountItem(AccountItem.UnrealizedProfitLoss, Currency.UsDollar)?.Value ?? 0.0;
                totalPnL = realized + unrealized;
                return true;
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[DAILY_LIMIT] Failed to read Account TotalPnL: {ex.Message}");
                return false;
            }
        }

        private static NinjaTrader.Cbi.Account ResolveCanonicalAccount(NinjaTrader.Cbi.Account account)
        {
            if (account == null)
                return null;

            try
            {
                string name = account.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    lock (NinjaTrader.Cbi.Account.All)
                    {
                        foreach (var a in NinjaTrader.Cbi.Account.All)
                        {
                            if (a == null)
                                continue;
                            if (string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                                return a;
                        }
                    }
                }
            }
            catch { }

            return account;
        }

        private static NinjaTrader.Cbi.Account TryGetMonitoredAccount(MultiStratManager manager)
        {
            if (manager == null)
                return null;

            try
            {
                var field = manager.GetType().GetField("monitoredAccount", BindingFlags.Instance | BindingFlags.NonPublic);
                return field?.GetValue(manager) as NinjaTrader.Cbi.Account;
            }
            catch
            {
                return null;
            }
        }

        private void ResetDailyPnLLimitState(string reason)
        {
            bool wasHalted = dailyPnLLimitHalted;
            dailyPnLLimitHalted = false;
            dailyPnLLimitStatusText = null;
            dailyPnLLimitType = string.Empty;
            dailyPnLLimitTriggeredPnL = 0.0;
            dailyPnLLimitTriggeredAt = DateTime.MinValue;
            dailyPnLLimitLastEnforceAttemptAt = DateTime.MinValue;
            dailyPnLLimitLastEnforceLogAt = DateTime.MinValue;
            dailyPnLLimitProfitCandidateAt = DateTime.MinValue;
            dailyPnLLimitProfitCandidatePnL = 0.0;

            if (wasHalted)
                StrategyLogInfo($"[DAILY_LIMIT] Cleared halt state ({reason}).");
        }

        private void MaybeResetDailyPnLLimitForNewDay()
        {
            if (!dailyPnLLimitHalted)
                return;

            // Daily PnL in Accounts tab is "today-only"; clear the latch when the UTC day rolls.
            if (dailyPnLLimitTriggeredAt != DateTime.MinValue &&
                DateTime.UtcNow.Date != dailyPnLLimitTriggeredAt.Date)
            {
                ResetDailyPnLLimitState("new_utc_day");
            }
        }

        private string BuildDailyPnLLimitStatusText(double currentPnL)
        {
            string type = (dailyPnLLimitType ?? string.Empty).Trim().ToUpperInvariant();

            double lossLimit = DailyLossLimit;
            if (lossLimit > 0)
                lossLimit = -Math.Abs(lossLimit);

            double profitLimit = DailyProfitLimit;
            if (profitLimit < 0)
                profitLimit = Math.Abs(profitLimit);

            if (type == "DLL")
                return $"HALTED: DLL reached (triggered {dailyPnLLimitTriggeredPnL:C2} <= {lossLimit:C2}; current {currentPnL:C2})";
            if (type == "DPL")
                return $"HALTED: DPL reached (triggered {dailyPnLLimitTriggeredPnL:C2} >= {profitLimit:C2}; current {currentPnL:C2})";

            return $"HALTED: Daily limit reached (triggered {dailyPnLLimitTriggeredPnL:C2}; current {currentPnL:C2})";
        }

        private void RefreshDailyPnLLimitStatusText()
        {
            if (!dailyPnLLimitHalted)
                return;

            if (TryGetAccountTotalPnL(out var currentPnL))
                dailyPnLLimitStatusText = BuildDailyPnLLimitStatusText(currentPnL);
        }

        private void TryEnforceDailyPnLLimitFlat()
        {
            if (!dailyPnLLimitHalted || Account == null)
                return;

            var now = DateTime.UtcNow;
            if (dailyPnLLimitLastEnforceAttemptAt != DateTime.MinValue &&
                (now - dailyPnLLimitLastEnforceAttemptAt) < TimeSpan.FromSeconds(2))
            {
                return;
            }
            dailyPnLLimitLastEnforceAttemptAt = now;

            int openPositions;
            int workingOrders;
            if (!TryGetAccountRiskCounts(out openPositions, out workingOrders))
                return;

            if (openPositions == 0 && workingOrders == 0)
                return;

            if (dailyPnLLimitLastEnforceLogAt == DateTime.MinValue || (now - dailyPnLLimitLastEnforceLogAt) > TimeSpan.FromSeconds(20))
            {
                dailyPnLLimitLastEnforceLogAt = now;
                StrategyLogInfo($"[DAILY_LIMIT] Enforcing flat (positions={openPositions}, workingOrders={workingOrders})");
            }

            TryFlattenAccountEverything($"daily_{dailyPnLLimitType}_enforce", activeTradeId ?? string.Empty);
        }

        private bool TryGetAccountRiskCounts(out int openPositions, out int workingOrders)
        {
            openPositions = 0;
            workingOrders = 0;

            try
            {
                if (Account == null)
                    return false;

                if (Account.Positions != null)
                {
                    foreach (var p in Account.Positions)
                    {
                        if (p == null || p.Quantity == 0 || p.MarketPosition == MarketPosition.Flat)
                            continue;
                        openPositions++;
                    }
                }

                if (Account.Orders != null)
                {
                    foreach (var o in Account.Orders)
                    {
                        if (o == null)
                            continue;
                        if (IsOrderWorking(o))
                            workingOrders++;
                    }
                }
            }
            catch
            {
                return false;
            }

            return true;
        }

        private static bool IsOrderWorking(Order order)
        {
            if (order == null)
                return false;

            switch (order.OrderState)
            {
                case OrderState.Accepted:
                case OrderState.Submitted:
                case OrderState.Working:
                case OrderState.PartFilled:
                case OrderState.ChangePending:
                case OrderState.ChangeSubmitted:
                    return true;
                default:
                    return false;
            }
        }

        private void TriggerDailyPnLLimit(string limitType, double totalPnL)
        {
            if (dailyPnLLimitHalted)
                return;

            dailyPnLLimitHalted = true;
            dailyPnLLimitType = limitType ?? string.Empty;
            dailyPnLLimitTriggeredPnL = totalPnL;
            dailyPnLLimitTriggeredAt = DateTime.UtcNow;

            string tradeRef = activeTradeId ?? string.Empty;
            StrategyLogInfo($"[DAILY_LIMIT] {dailyPnLLimitType} triggered at {totalPnL:F2}; flattening account and halting entries.");

            try
            {
                MultiStratManager.Instance?.ActivateDailyLimitOverride(
                    Account != null ? Account.Name : string.Empty,
                    dailyPnLLimitType,
                    totalPnL,
                    Name);
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[DAILY_LIMIT] Failed to activate daily limit override in AddOn: {ex.Message}");
            }

            TryFlattenAccountEverything($"daily_{dailyPnLLimitType}", tradeRef);
        }

        private void TryFlattenAccountEverything(string reason, string tradeRef = "")
        {
            if (Account == null)
                return;

            try
            {
                int cancelled = 0;
                try
                {
                    // Cancel any working orders so stops/targets can't re-open a position while halted.
                    var orders = Account.Orders != null ? new List<Order>(Account.Orders) : new List<Order>();
                    foreach (var o in orders)
                    {
                        if (o == null)
                            continue;
                        if (!IsOrderWorking(o))
                            continue;

                        try
                        {
                            Account.Cancel(new[] { o });
                            cancelled++;
                        }
                        catch { }
                    }
                }
                catch { }

                // Fallback: submit market orders per open position.
                var positions = Account.Positions != null
                    ? new List<NinjaTrader.Cbi.Position>(Account.Positions)
                    : new List<NinjaTrader.Cbi.Position>();

                int submitted = 0;
                foreach (var position in positions)
                {
                    if (position == null || position.Quantity == 0 || position.MarketPosition == MarketPosition.Flat)
                        continue;

                    int qty = Math.Abs(position.Quantity);
                    if (qty <= 0)
                        continue;

                    OrderAction actionToFlatten = position.MarketPosition == MarketPosition.Long ? OrderAction.Sell : OrderAction.BuyToCover;
                    string orderName = "PnLLimitFlatten";
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(tradeRef) &&
                            Instrument != null &&
                            position.Instrument != null &&
                            string.Equals(position.Instrument.FullName, Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                        {
                            orderName = tradeRef.Trim();
                        }
                    }
                    catch { }

                    var order = Account.CreateOrder(
                        position.Instrument,
                        actionToFlatten,
                        OrderType.Market,
                        OrderEntry.Manual,
                        TimeInForce.Day,
                        qty,
                        0,
                        0,
                        string.Empty,
                        orderName,
                        default(DateTime),
                        null);

                    Account.Submit(new[] { order });
                    submitted++;
                }

                StrategyLogInfo($"[DAILY_LIMIT] Flatten requested due to {reason} (cancelledOrders={cancelled}, submittedFlattens={submitted})");
            }
            catch (Exception ex)
            {
                StrategyLogError($"[DAILY_LIMIT] Failed to flatten account due to {reason}: {ex.Message}");
                var manager = MultiStratManager.Instance;
                if (manager != null)
                    manager.LogError("DAILY_LIMIT", $"Flatten failed: {ex.Message}", 0, tradeRef, tradeRef);
            }
        }

        private void WarnTradeSyncOffline()
        {
            if (tradeSyncWarned)
                return;
            tradeSyncWarned = true;
            StrategyLogInfo("[AUTO][SYNC] TradeSync/addon offline; suppressing publish to avoid gRPC errors.");
            UpdateStatusLabel("Active: addon offline (no sync)", true);
        }

        private void ExitMultiEntrySyncTrades(string syncTradeId, int quantityToExit, string exitSuffix)
        {
            if (string.IsNullOrEmpty(syncTradeId) || quantityToExit <= 0)
                return;

            var states = GetMultiEntrySyncStates(syncTradeId);
            int remaining = quantityToExit;

            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;

                int qty = Math.Min(remaining, Math.Max(0, state.RemainingQuantity));
                if (qty <= 0)
                    continue;

                string exitSignal = BuildExitSignalName(state.TradeId, exitSuffix);
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, state.TradeId);
                else
                    ExitShort(qty, exitSignal, state.TradeId);

                remaining -= qty;
                if (remaining <= 0)
                    break;
            }
        }

        void ITradeSyncParticipant.HandleTradeSyncPartial(string tradeId, int quantityToExit)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || quantityToExit <= 0)
                return;

            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
            {
                ExitMultiEntrySyncTrades(group.TradeId, quantityToExit, "EXT");
                return;
            }

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

            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
            {
                int totalRemaining = GetMultiEntrySyncRemainingQuantity(group.TradeId);
                if (totalRemaining <= 0)
                    return;
                ExitMultiEntrySyncTrades(group.TradeId, totalRemaining, "CLS");
                return;
            }

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

            List<TradeRuntimeState> targetStates = null;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out var group))
            {
                targetStates = GetMultiEntrySyncStates(group.TradeId);
            }
            else if (TryGetTradeState(tradeId, out var singleState))
            {
                targetStates = new List<TradeRuntimeState> { singleState };
            }

            if (targetStates == null || targetStates.Count == 0)
                return;

            double distance = ConvertRunUpValueToPrice(config.DistanceUnits, config.DistanceValue);
            double increment = ConvertRunUpValueToPrice(config.IncrementUnits, config.IncrementValue);
            if (distance <= 0)
            {
                StrategyLogInfo(string.Format("[RUN_UP] Skip activation for {0}: distance must be > 0 (got {1:F4})", tradeId, distance));
                return;
            }

            foreach (var state in targetStates)
            {
                if (state == null)
                    continue;

                state.RunUpActive = true;
                state.RunUpAnchorPrice = anchorPrice;
                state.RunUpInitialDistance = distance;
                state.RunUpIncrement = increment;
                state.RunUpLastStopPrice = null;
                state.RunUpHighWater = anchorPrice;
                state.RunUpLowWater = anchorPrice;
                state.AllowOpenPublish = state.AllowOpenPublish || State == State.Realtime;
                state.PendingClosePublish = false;
                state.StopOrder = null;
                state.TargetOrder = null;

                if (string.IsNullOrEmpty(activeTradeId))
                    activeTradeId = state.TradeId;

                bool isLong = state.EntrySide == MarketPosition.Long;
                double desiredStop = isLong ? anchorPrice - distance : anchorPrice + distance;
                double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
                var clamped = ClampStopPrice(desiredStop, anchorPrice, isLong, lastAccepted);
                if (!clamped.HasValue)
                    continue;

                if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                {
                    state.RunUpLastStopPrice = clamped.Value;
                    stopSet = true;
                    StrategyLogInfo(string.Format("[RUN_UP] Activated for {0}: anchor={1:F2}, stop={2:F2}, dist={3:F4}, inc={4:F4}", state.TradeId, anchorPrice, clamped.Value, distance, increment));
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

        [NinjaScriptProperty, Range(1, 10), Display(Name = "TradesPerEntry", GroupName = "01 - Bias & Voting", Order = 3)]
        public int TradesPerEntry { get; set; }

        [NinjaScriptProperty, Display(Name = "Treat Multi-Entry as 1 Trade?", GroupName = "01 - Bias & Voting", Order = 4)]
        public bool TreatMultiEntryAsSingleTrade { get; set; }

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

        [NinjaScriptProperty, TypeConverter(typeof(ConnectedAccountNameConverter)), Display(Name = "Execution Account Override (optional)", GroupName = "07 - Misc", Order = 1)]
        public string ExecutionAccountOverride { get; set; }

        [NinjaScriptProperty, Display(Name = "Log Account Diagnostics", GroupName = "07 - Misc", Order = 2)]
        public bool LogAccountDiagnostics { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Daily PnL Limits (DLL/DPL)", GroupName = "08 - Daily Limits", Order = 0)]
        public bool EnableDailyPnLLimits { get; set; }

        [NinjaScriptProperty, Range(-1000000.0, 0.0), Display(Name = "Daily Loss Limit (DLL)", GroupName = "08 - Daily Limits", Order = 1)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty, Range(0.0, 1000000.0), Display(Name = "Daily Profit Limit (DPL)", GroupName = "08 - Daily Limits", Order = 2)]
        public double DailyProfitLimit { get; set; }

        #endregion
    }
}
