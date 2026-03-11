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
using System.Windows.Input;
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
        // --- indicator refs
        private SMA sma;
        private EMA emaFast, emaSlow;
        private RSI rsi;
        private MACD macd;
        private Series<double> vwapSeries;
        private Series<double> vwapUpperBand1;
        private Series<double> vwapUpperBand2;
        private Series<double> vwapLowerBand1;
        private Series<double> vwapLowerBand2;
        private int vwapMrBarsIndex = -1;
        private int vwapMrLastProcessedBar = -1;
        private VwapMrValues lastVwapMrValues;
        private bool hasVwapMrValues;
        private double vwapMedianVolume;
        private const int VwapMedianSampleSize = 100;
        private readonly HashSet<Indicator> indicatorAttached = new HashSet<Indicator>();
        private readonly Dictionary<Indicator, Brush[]> indicatorPlotBrushes = new Dictionary<Indicator, Brush[]>();
        private readonly Dictionary<Indicator, float[]> indicatorPlotWidths = new Dictionary<Indicator, float[]>();
        private ATR atr;
        private ATR vwapAtr;
        private SMA atrVolBaseline;
        private SMA rvolBaseline;
        private ADX adxChop;
        private Bollinger bbChop;
        private ATR htfAtrPrimary;
        private ATR htfAtrSecondary;

        // --- internal
        private bool stopSet, targetSet;
        private bool demaTrailingActive;
        private double demaHighWater;
        private double demaLowWater;
        private int maxSignalSlots; // number of enabled indicator families (SMA/EMA/RSI/MACD)
        private double vwapCumulativePriceVolume;
        private double vwapCumulativeVolume;
        private double vwapCumulativePriceVolume2;
        private int vwapSessionBars;
        private double vwapSessionVolume;
        private DateTime vwapSessionStartTime = DateTime.MinValue;
        private VwapAssetClass vwapAssetClass = VwapAssetClass.Unknown;
        private VwapResetPeriodOption vwapEffectiveReset = VwapResetPeriodOption.Daily;
        private VwapTimezoneOption vwapEffectiveTimezone = VwapTimezoneOption.Exchange;
        private TimeZoneInfo vwapSourceTimeZone;
        private TimeZoneInfo vwapTargetTimeZone;
        private VwapResetPeriodOption vwapResetSetting = VwapResetPeriodOption.Auto;
        private VwapTimezoneOption vwapTimezoneSetting = VwapTimezoneOption.Auto;
        private int vwapCustomUtcOffsetHours = 0;
        private double lastVwapValue;
        private double lastVwapUpperBand1;
        private double lastVwapUpperBand2;
        private double lastVwapUpperBand3;
        private double lastVwapLowerBand1;
        private double lastVwapLowerBand2;
        private double lastVwapLowerBand3;
        private double orbHigh = double.MinValue;
        private double orbLow = double.MaxValue;
        private DateTime orbSessionStart = DateTime.MinValue;
        private DateTime orbSessionEnd = DateTime.MinValue;
        private DateTime orbEndTime = DateTime.MinValue;
        private bool orbRangeReady;
        private bool orbBreakoutSatisfied;
        private bool orbUsingFallback;
        private SessionIterator orbSessionIterator;
        private SessionIterator straddleSessionIterator;
        private DateTime straddleSessionStart = DateTime.MinValue;
        private DateTime straddleSessionEnd = DateTime.MinValue;
        private DateTime straddleEventTime = DateTime.MinValue;
        private DateTime straddleRangeStart = DateTime.MinValue;
        private DateTime straddleRangeEnd = DateTime.MinValue;
        private DateTime straddleWindowEnd = DateTime.MinValue;
        private bool straddleRangeReady;
        private bool straddleArmed;
        private bool straddleLongTriggered;
        private bool straddleShortTriggered;
        private double straddleRangeHigh = double.MinValue;
        private double straddleRangeLow = double.MaxValue;
        private double straddleLongZoneUpper;
        private double straddleLongZoneLower;
        private double straddleShortZoneUpper;
        private double straddleShortZoneLower;
        private double? straddleLongManualOffsetTicks;
        private double? straddleShortManualOffsetTicks;
        private double? straddleLongAutoCenter;
        private double? straddleShortAutoCenter;
        private double? straddleHardStopLongManualOffsetTicks;
        private double? straddleHardStopShortManualOffsetTicks;
        private double? straddleHardStopLongAutoPrice;
        private double? straddleHardStopShortAutoPrice;
        private Rectangle straddleLongZoneRect;
        private Rectangle straddleShortZoneRect;
        private HorizontalLine straddleHardStopLongLine;
        private HorizontalLine straddleHardStopShortLine;
        private readonly List<string> straddlePendingLongTradeIds = new List<string>();
        private readonly List<string> straddlePendingShortTradeIds = new List<string>();
        private double lastBid = double.NaN;
        private double lastAsk = double.NaN;
        private double lastLast = double.NaN;
        private DateTime lastMarketDataTime = DateTime.MinValue;
        private bool straddleZonesFrozen;
        private double straddleRangeShift;

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
        private readonly List<PnlLabelInfo> pnlLabelInfos = new List<PnlLabelInfo>();
        private readonly object pnlLabelLock = new object();
        private bool lastShowTradePnlTags;
        private bool lastPnlTagToggleState;
        private bool lastSmaVisualToggleState;
        private bool lastEmaVisualToggleState;
        private bool lastRsiVisualToggleState;
        private bool lastMacdVisualToggleState;
        private bool lastAtrVisualToggleState;
        private bool lastBbVisualToggleState;
        private bool lastVwapVisualToggleState;
        private bool lastVwapGateToggleState;
        private bool lastReverseSignalToggleState;
        private bool lastSmaVisualActive;
        private bool lastEmaVisualActive;
        private bool lastRsiVisualActive;
        private bool lastMacdVisualActive;
        private bool lastAtrVisualActive;
        private bool lastBbVisualActive;
        private bool lastVwapVisualActive;
        private TradeBias lastBiasToggleValue = TradeBias.Both;
        private bool vwapFlipPending;
        private MarketPosition vwapFlipSide = MarketPosition.Flat;
        private double vwapFlipStopPrice;
        private double vwapFlipTargetPrice;
        private int vwapFlipQuantity;
        private double vwapFlipBandMultiplier;
        private string vwapFlipReason;
        private DateTime lastOnBarUpdateExceptionLog = DateTime.MinValue;
        private EntrySignalSnapshot lastEntrySnapshot;
        private string lastChecklistText;
        private bool lastChecklistHealthy;
        private bool lastChopActive;
        private bool lastBreakoutSignalActive;
        private double lastChopRangeHigh;
        private double lastChopRangeLow;
        private double lastChopRangeMid;
        private bool lastChopRangeReady;
        private double? chopLongManualOffsetTicks;
        private double? chopShortManualOffsetTicks;
        private int lastChopAddOnBar = -1;
        private bool scaleInActive;
        private bool scaleInTriggered;
        private int scaleInTradesExecuted;
        private int scaleInTradesPending;
        private double scaleInInitialEntryPrice;
        private double scaleInLockPrice;
        private double scaleInHighWater;
        private double scaleInLowWater;
        private double scaleInLastStopPrice;
        private double scaleInActivationPrice;
        private bool scaleInTrailActivated;
        private MarketPosition scaleInSide = MarketPosition.Flat;
        private const int ScaleInHoldSeconds = 10;
        private DateTime scaleInHoldUntil = DateTime.MinValue;
        private bool globalTrailActivated;
        private double globalTrailActivationPrice;
        private double globalTrailLockPrice;
        private double globalTrailLastStopPrice;
        private MarketPosition globalTrailSide = MarketPosition.Flat;

        private const string OrbHighTag = "orb_high";
        private const string OrbLowTag = "orb_low";
        private const string OrbBoxTag = "orb_box";
        private const string OrbStatusTag = "orb_status";
        private const string OrbStartTag = "orb_start";
        private const string OrbEndTag = "orb_end";
        private const string OrbHighLabelTag = "orb_high_label";
        private const string OrbLowLabelTag = "orb_low_label";
        private const string ChopHighTag = "chop_break_high";
        private const string ChopLowTag = "chop_break_low";
        private const string ChopMidTag = "chop_break_mid";
        private const string ChopStatusTag = "chop_status";
        private const string ChopRangeTag = "chop_range";
        private const string ChopHighLabelTag = "chop_high_label";
        private const string ChopLowLabelTag = "chop_low_label";
        private const string ChopMidLabelTag = "chop_mid_label";
        private const string FilterStatusTag = "filter_status";
        private const string VwapLineTagPrefix = "vwap_line_";
        private const string VwapBand1UpperTagPrefix = "vwap_band1_up_";
        private const string VwapBand2UpperTagPrefix = "vwap_band2_up_";
        private const string VwapBand1LowerTagPrefix = "vwap_band1_dn_";
        private const string VwapBand2LowerTagPrefix = "vwap_band2_dn_";
        private const string VwapBand3UpperTagPrefix = "vwap_band3_up_";
        private const string VwapBand3LowerTagPrefix = "vwap_band3_dn_";
        private const string StraddleRangeTag = "straddle_range";
        private const string StraddleLongZoneTag = "straddle_long_zone";
        private const string StraddleShortZoneTag = "straddle_short_zone";
        private const string StraddleStatusTag = "straddle_status";
        private const string StraddleCountdownTag = "straddle_countdown";
        private const string StraddleHardStopLongTag = "straddle_hard_stop_long";
        private const string StraddleHardStopShortTag = "straddle_hard_stop_short";
        private const string StraddleHardStopLongLabelTag = "straddle_hard_stop_long_label";
        private const string StraddleHardStopShortLabelTag = "straddle_hard_stop_short_label";
        private const string ScaleInDrawdownTagPrefix = "scale_in_dd_";
        private const int MaxScaleInDrawdownLines = 50;
        private static readonly TimeSpan StraddlePreArmOffset = TimeSpan.FromMilliseconds(250);

 

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
        private const int MaxTradesPerEntry = 10;
        private const int ManualOrderSeriesIndex = 1;
        private const string ManualCloseReason = "NT_MANUAL_BUTTON";
        private const string StopLossCloseReason = "NT_STOP_CLOSE";
        private const double ManualProtectionHoldSeconds = 2.0;
        private const double TightDemaAtrPeriodScale = 0.6;
        private const double TightDemaAtrMultiplierScale = 0.7;
        private const int TightDemaAtrMinPeriod = 3;
        private const int TightDemaAtrLookbackFloor = 30;
        private const int VwapSwingStopLookback = 5;
        private const int VwapTrailInitialTicks = 5;
        private const int VwapTrailIncrementTicks = 1;
        private const int VwapTweezerToleranceTicks = 2;
        private const int VwapFlipStopBufferTicks = 1;
        private const double VwapRailroadBodyDiffPct = 0.10;
        private const double VwapRailroadAtrMultiplier = 1.5;
        private const double VwapPinBarWickToBody = 2.0;
        private const double VwapDojiBodyPct = 0.15;
        private const double VwapFlipBandMultiplier = 4.0;
        private int htfPrimaryIndex = -1;
        private int htfSecondaryIndex = -1;

        private int entryCooldownStartBar = -1;
        private int entryCooldownEndBar = -1;
        private bool entryCooldownPending;

        private bool manualHaltActive;
        private string manualHaltStatusText;
        private DateTime manualHaltActivatedAt = DateTime.MinValue;
        private DateTime manualHaltLastEnforceAttemptAt = DateTime.MinValue;
        private DateTime manualHaltLastEnforceLogAt = DateTime.MinValue;
        private bool shutdownInProgress;

        private Chart chartWindow;
        private ChartTrader chartTrader;
        private Grid chartTraderGrid;
        private RowDefinition chartTraderButtonsRow;
        private StackPanel chartTraderButtonPanel;
        private Button manualBuyButton;
        private Button manualSellButton;
        private Button manualLimitButton;
        private Button manualStopButton;
        private Button manualFlattenButton;
        private Button manualResumeButton;
        private Button biasBothToggleButton;
        private Button biasLongToggleButton;
        private Button biasShortToggleButton;
        private Button vwapGateToggleButton;
        private Button addOnTradeButton;
        private Button pnlTagsToggleButton;
        private Button reverseSignalToggleButton;
        private Button visualsToggleButton;
        private Button smaVisualToggleButton;
        private Button emaVisualToggleButton;
        private Button rsiVisualToggleButton;
        private Button macdVisualToggleButton;
        private Button atrVisualToggleButton;
        private Button bbVisualToggleButton;
        private Button vwapVisualToggleButton;
        private WrapPanel visualButtonsPanel;
        private TextBlock tradesPerEntryLabel;
        private TextBox tradesPerEntryTextBox;
        private TextBlock chopTradesPerEntryLabel;
        private TextBox chopTradesPerEntryTextBox;
        private bool chartTraderButtonsAdded;
        private bool visualsPanelExpanded = true;
        private bool indicatorVisualsPrimed = false;
        private int tradesPerEntryOverride;
        private int lastTradesPerEntryDisplay = -1;
        private int chopTradesPerEntryOverride;
        private int lastChopTradesPerEntryDisplay = -1;
        private bool lastManualButtonsEnabled;
        private bool lastAddOnButtonEnabled;

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
            public bool IsChopEntry;
            public bool IsScaleInEntry;
            public bool ChopTrailActive;
            public bool ChopTrailForced;
            public double ChopTrailHighWater;
            public double ChopTrailLowWater;
            public bool PendingEntryPriceUpdate;
            public double PendingEntryLimitPrice;
            public double LastAutoEntryLimitPrice;
            public bool EntryOrderPending;
            public bool EntryCancelRequested;
            public bool ManualStopOverride;
            public bool ManualTargetOverride;
            public bool ManualStopPending;
            public bool ManualTargetPending;
            public DateTime ManualStopPendingUntil;
            public DateTime ManualTargetPendingUntil;
            public bool PendingAutoStopUpdate;
            public bool PendingAutoTargetUpdate;
            public double PendingAutoStopPrice;
            public double PendingAutoTargetPrice;
            public bool ForcedDemaTrailLogged;
            public double LastStopPrice;
            public double LastTargetPrice;
            public bool RunUpActive;
            public double RunUpAnchorPrice;
            public double RunUpInitialDistance;
            public double RunUpIncrement;
            public double? RunUpLastStopPrice;
            public double RunUpHighWater;
            public double RunUpLowWater;
            public bool IsVwapEntry;
            public bool VwapIsFlip;
            public double VwapBandMultiplier;
            public double VwapTargetPrice;
            public double VwapNextBandPrice;
            public bool VwapTrailOnVwapTouch;
            public bool VwapTrailActive;
            public double VwapTrailAnchorPrice;
            public double VwapTrailDistance;
            public double VwapTrailIncrement;
            public double? VwapTrailLastStopPrice;
            public double VwapTrailHighWater;
            public double VwapTrailLowWater;
            public double VwapFailureHigh;
            public double VwapFailureLow;
            public int VwapFailureCheckBar;
            public int EntryBarIndex;
            public bool BreakEvenActivated;
            public bool SyntheticLogEmitted;
            public bool Bootstrapped;
            public bool IsManualEntry;
            public bool AllowOpenPublish;
            public bool PendingClosePublish;
            public bool ClosePublished;
            public bool ExitAllTriggered;
            public Order EntryOrder;
            public Order StopOrder;
            public Order TargetOrder;
            public int ProtectionRetryCount;
            public DateTime LastProtectionRetry;
            public int ProtectionRearmCount;
            public DateTime LastProtectionRearm;
            public int EntryVotes;
            public int EntryMinVotes;
            public bool EntryOrbAllowed;
            public bool EntryChopAllowed;
            public double EntryChopAdx;
            public double EntryChopBbWidthPct;
            public bool EntryChopDecayActive;
            public double EntryChopDecayAdxDelta;
            public double EntryChopDecayBbDeltaPct;
            public bool EntryHtfEnabled;
            public bool EntryHtfNear;
            public bool EntryHtfBlocked;
            public bool EntryHtfHeldBeyond;
            public double EntryHtfDistanceAtr;
            public string EntryHtfSource;
            public string EntryHtfTimeframe;
            public bool EntryVolExpEnabled;
            public bool EntryVolExpOk;
            public double EntryVolExpBbWidthPct;
            public double EntryVolExpBbDeltaPct;
            public double EntryVolExpAtr;
            public double EntryVolExpAtrBaseline;
            public double EntryVolExpAtrRatio;
            public bool EntryRvolEnabled;
            public bool EntryRvolReady;
            public bool EntryRvolOk;
            public double EntryRvolValue;
            public double EntryRvolAvg;
            public bool EntryVrocReady;
            public bool EntryVrocOk;
            public double EntryVrocPct;
            public bool EntryRegimeSwitchingEnabled;
            public bool EntryRegimeIsChop;
            public bool EntryReverseSignalTrading;
            public DateTime EntrySignalTime;
            public string EntryContext;
            public double MaxFavorablePrice;
            public double MaxAdversePrice;
            public bool IsStraddleEntry;
            public DateTime StraddleEntryTime;
            public DateTime StraddleProfitStart;
            public bool StraddleProfitGatePassed;
            public bool StraddleTrailingActive;
            public double StraddleTrailHighWater;
            public double StraddleTrailLowWater;
        }

        private class PnlLabelInfo
        {
            public string Tag;
            public DateTime Time;
            public double Price;
            public string Label;
            public bool IsProfit;
        }

        private class MultiEntrySyncGroup
        {
            public string TradeId;
            public MarketPosition Side;
            public int TotalQuantity;
            public int LastPublishedRemaining;
            public bool OpenPublished;
            public bool ClosedPublished;
        }

        private struct HtfSwingGateResult
        {
            public bool Enabled;
            public bool HasData;
            public bool Near;
            public bool Blocked;
            public bool HeldBeyond;
            public int ExtraVotes;
            public double DistanceAtr;
            public double DistancePoints;
            public double SwingPrice;
            public string Source;
            public string TimeframeLabel;
        }

        private struct EntrySignalSnapshot
        {
            public int LongVotes;
            public int ShortVotes;
            public int MinLong;
            public int MinShort;
            public bool RegimeSwitchingEnabled;
            public bool RegimeIsChop;
            public bool ReverseSignalTrading;
            public bool OrbLong;
            public bool OrbShort;
            public bool ChopLong;
            public bool ChopShort;
            public double ChopAdx;
            public double ChopBbWidthPct;
            public bool ChopDecayActive;
            public double ChopDecayAdxDelta;
            public double ChopDecayBbDeltaPct;
            public bool HtfEnabled;
            public HtfSwingGateResult HtfLong;
            public HtfSwingGateResult HtfShort;
            public bool VolExpEnabled;
            public bool VolExpOk;
            public double VolExpBbWidthPct;
            public double VolExpBbDeltaPct;
            public double VolExpAtr;
            public double VolExpAtrBaseline;
            public double VolExpAtrRatio;
            public bool RvolEnabled;
            public bool RvolReady;
            public bool RvolOk;
            public double RvolValue;
            public double RvolAvg;
            public bool VrocReady;
            public bool VrocOk;
            public double VrocPct;
            public DateTime Time;
        }

        private struct VwapMrValues
        {
            public double Vwap;
            public double StdDev;
            public double UpperBand1;
            public double UpperBand2;
            public double UpperBand3;
            public double LowerBand1;
            public double LowerBand2;
            public double LowerBand3;
            public bool IsNewSession;
        }

        private struct VwapMrSignal
        {
            public bool Ready;
            public bool BandTouched;
            public double BandPrice;
            public double BandMultiplier;
            public double NextBandPrice;
            public double DistancePct;
            public bool DistanceOk;
            public bool CloseInside;
            public bool PinBar;
            public bool Doji;
            public bool Engulfing;
            public bool Tweezer;
            public bool Railroad;
            public bool DojiStar;
            public bool ThreeInside;
            public bool PatternOk;
        }

        private enum IndicatorVisualType
        {
            Sma,
            Ema,
            Rsi,
            Macd,
            Atr,
            Bollinger,
            Vwap
        }

        public enum VwapMrTimeframeOption
        {
            M5 = 5,
            M15 = 15,
            H1 = 60
        }

        public enum VwapResetPeriodOption
        {
            Auto,
            Daily,
            Forex5Pm,
            Weekly,
            Monthly,
            None
        }

        public enum VwapTimezoneOption
        {
            Auto,
            Exchange,
            UTC,
            NewYork,
            London,
            Tokyo,
            Sydney,
            Custom
        }

        private enum VwapAssetClass
        {
            Forex,
            Metal,
            Crypto,
            Index,
            Stock,
            Energy,
            Unknown
        }

        public override string DisplayName
        {
            get { return Name; }
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
                    IsOverlay = true;
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

                // VWAP Indicator + Direction Gate
                UseVwapDirectionGate = true;
                VwapMrTimeframe = VwapMrTimeframeOption.M5;
                VwapBand1Multiplier = 2.0;
                VwapBand2Multiplier = 3.0;
                VwapFilterSpikes = true;
                VwapSpikeThreshold = 10.0;

                // Legacy VWAP MR (disabled)
                EnableVwapMrStrategy = false;
                MinDistFromVWAP_Percent = 0.0005;
                VwapExitMode = VwapExitModeOption.TargetVwap;
                EnableVwapFailureFlip = false;
                EnableVwapPinBar = false;
                EnableVwapDoji = false;
                EnableVwapEngulfing = false;
                EnableVwapTweezer = false;
                EnableVwapRailroad = false;
                EnableVwapDojiStar = false;
                EnableVwapThreeInside = false;

                // Signal control
                Bias = TradeBias.Both;
                MinSignalsToEnterLong = 2;
                MinSignalsToEnterShort = 2;
                TradesPerEntry = 1;
                TreatMultiEntryAsSingleTrade = false;
                EntryCooldownBars = 0;
                ReverseSignalTrading = false;
                EnableVoteEntrySignals = true;
                EnableRegimeSwitching = true;
                EnableCandleConviction = true;
                RsiChopLongThreshold = 30;
                RsiChopShortThreshold = 70;
                EnableOrbFilter = true;
                OrbMinutes = 15;
                OrbUseFixedStartTime = true;
                OrbStartHour = 6;
                OrbStartMinute = 30;
                OrbPreStartBlockMinutes = 30;
                EnableChopFilter = true;
                ChopLookbackBars = 20;
                ChopAdxPeriod = 14;
                ChopAdxThreshold = 25;
                ChopBollingerPeriod = 20;
                ChopBollingerStdDev = 2.0;
                ChopBBWidthPct = 0.8;
                ChopBreakoutBufferTicks = 2;
                EnableChopDecayGate = true;
                ChopDecayBars = 3;
                ChopDecayAdxDelta = 1.0;
                ChopDecayBbWidthDeltaPct = 0.01;
                EnableChopRangeTrades = false;
                ChopRangeMode = ChopRangeModeOption.HighLow;
                ChopRangeLookbackBars = 20;
                ChopTradesPerEntry = 1;
                ChopStopType = StopKind.Ticks;
                ChopStopTicks = 12;
                ChopStopAtrMult = 1.0;
                ChopTrailTicks = 6;
                ChopTrailPlusTicks = 2;
                ChopAddOnProfitMode = ChopAddOnProfitModeOption.Ticks;
                ChopAddOnProfitTicks = 8;
                ChopAddOnProfitDollars = 50;
                EnableHtfSwingGate = false;
                HtfSwingMode = HtfSwingModeOption.Pivot;
                HtfSwingAction = HtfSwingActionOption.AddVote;
                HtfSwingLookbackBars = 20;
                HtfSwingPivotStrength = 3;
                HtfSwingDistanceAtr = 0.5;
                HtfSwingAtrPeriod = 14;
                HtfSwingHoldBars = 1;
                HtfSwingPrimaryMinutes = 5;
                HtfSwingSecondaryMinutes = 15;
                ShowFilterVisuals = true;
                ShowSmaVisuals = true;
                ShowEmaVisuals = true;
                ShowRsiVisuals = true;
                ShowMacdVisuals = true;
                ShowAtrVisuals = true;
                ShowChopBbVisuals = true;
                ShowVwapMrVisuals = true;
                ShowTradePnlTags = true;
                EnableStraddleTrades = false;
                StraddleStartHour = OrbStartHour;
                StraddleStartMinute = OrbStartMinute;
                StraddleRangeMinutes = 20;
                StraddleZoneTicks = 5;
                StraddleZoneOffsetTicks = 0;
                TradesPerStraddleEntry = 1;
                StraddleAtrStopMult = 1.0;
                StraddleAtrTrailMult = 1.0;
                StraddleTrailActivationDollars = 500;
                StraddleMinProfitHoldSeconds = 10;
                EnableScaleInTrades = true;
                PublishScaleInTradesToBridge = false;
                EnableScaleInTrailing = true;
                ScaleInDrawdownTicks = 30;
                ScaleInTradesToAdd = 1;
                ScaleInMaxTrades = 5;
                ScaleInTrailActivationMode = BreakEvenTriggerModeOption.Dollars;
                ScaleInTrailActivationValue = 500;
                ScaleInProfitLockMode = BreakEvenTriggerModeOption.Dollars;
                ScaleInProfitLockValue = 300;
                ScaleInTrailIncrementMode = BreakEvenTriggerModeOption.Ticks;
                ScaleInTrailIncrementValue = 1;

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
                ManualEntryOffsetTicks = 100;
                EnableGlobalTrailing = false;
                GlobalTrailActivationMode = BreakEvenTriggerModeOption.Dollars;
                GlobalTrailActivationValue = 150;
                GlobalProfitLockMode = BreakEvenTriggerModeOption.Dollars;
                GlobalProfitLockValue = 35;
                GlobalTrailIncrementMode = BreakEvenTriggerModeOption.Dollars;
                GlobalTrailIncrementValue = 5;

                // Legacy ATR trailing inputs retained for reference only; strategy now always
                // uses shared DEMA-ATR trailing logic that mirrors the AddOn configuration.
                // TrailType = TrailKind.None;
                // AtrTrailMult = 1.5;
                // TrailTicks = 20;

                Debug = false;
                EnableSignalDiagnostics = false;
                EnableTradeStoryLogging = false;
                StartHaltedOnEnable = false;
                DemaAtrPeriod = 14;
                DemaAtrMultiplier = 1.5;
                UseDemaAtrTrailing = true;
                UseTightDemaAtrTrailing = false;
                DemaAtrActivationMode = TrailingActivationType.Percent;
                DemaAtrActivationValue = 1.0;
                UseBreakEvenClamp = true;
                BreakEvenTriggerMode = BreakEvenTriggerModeOption.Ticks;
                BreakEvenTriggerTicks = 30;
                BreakEvenTriggerDollars = 0;
                BreakEvenPlusTicks = 2;
                EnableDemaAtrOnBreakEvenClamp = false;
                EnableVolatilityExpansionVote = false;
                VolExpBbWidthDeltaPct = 0.05;
                VolExpAtrBaselinePeriod = 50;
                VolExpAtrMultiplier = 1.05;
                EnableRvolGate = true;
                RvolLookbackBars = 30;
                RvolMin = 1.15;
                VrocLookbackBars = 6;
                VrocMinPct = 12.0;
                EnableCompressionGuard = true;
                CompressionGuardBbWidthPct = ChopBBWidthPct;
                CompressionGuardRequireBoth = false;
                ChopBreakoutHoldBars = 2;

                EnableDailyPnLLimits = false;
                DailyLossLimit = -2000;
                DailyProfitLimit = 840;

                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
                activeTradeId = null;
            }
            else if (State == State.Configure)
            {
                if (BarsArray.Length == 1)
                {
                    AddDataSeries(BarsPeriodType.Tick, 1);
                }

                int nextIndex = BarsArray.Length;
                if (BarsArray.Length == 1)
                    nextIndex++;

                vwapMrBarsIndex = nextIndex;
                AddDataSeries(BarsPeriodType.Minute, GetVwapMrTimeframeMinutes());
                nextIndex++;

                htfPrimaryIndex = nextIndex;
                AddDataSeries(BarsPeriodType.Minute, HtfSwingPrimaryMinutes);
                nextIndex++;

                htfSecondaryIndex = nextIndex;
                AddDataSeries(BarsPeriodType.Minute, HtfSwingSecondaryMinutes);

                EntriesPerDirection = MaxTradesPerEntry;

                    // optional: log parameters once per iteration for diagnostics
                if (Debug)
                {
                    StrategyLogDebug($"PARAMS: Bias={Bias}, ReverseSignalTrading={ReverseSignalTrading}, EnableVoteEntrySignals={EnableVoteEntrySignals}, EnableRegimeSwitching={EnableRegimeSwitching}, EnableCandleConviction={EnableCandleConviction}, RsiChopLong={RsiChopLongThreshold}, RsiChopShort={RsiChopShortThreshold}, MinLong={MinSignalsToEnterLong}, MinShort={MinSignalsToEnterShort}, TradesPerEntry={TradesPerEntry}, TreatMultiEntryAsSingleTrade={TreatMultiEntryAsSingleTrade}, EntryCooldownBars={EntryCooldownBars}, EnableOrbFilter={EnableOrbFilter}, OrbMinutes={OrbMinutes}, OrbUseFixedStartTime={OrbUseFixedStartTime}, OrbStartHour={OrbStartHour}, OrbStartMinute={OrbStartMinute}, OrbPreStartBlockMinutes={OrbPreStartBlockMinutes}, EnableChopFilter={EnableChopFilter}, ChopLookbackBars={ChopLookbackBars}, ChopAdxPeriod={ChopAdxPeriod}, ChopAdxThreshold={ChopAdxThreshold}, ChopBollingerPeriod={ChopBollingerPeriod}, ChopBollingerStdDev={ChopBollingerStdDev}, ChopBBWidthPct={ChopBBWidthPct}, ChopBreakoutBufferTicks={ChopBreakoutBufferTicks}, ChopBreakoutHoldBars={ChopBreakoutHoldBars}, EnableCompressionGuard={EnableCompressionGuard}, CompressionGuardBbWidthPct={CompressionGuardBbWidthPct}, CompressionGuardRequireBoth={CompressionGuardRequireBoth}, EnableChopDecayGate={EnableChopDecayGate}, ChopDecayBars={ChopDecayBars}, ChopDecayAdxDelta={ChopDecayAdxDelta}, ChopDecayBbWidthDeltaPct={ChopDecayBbWidthDeltaPct}, EnableChopRangeTrades={EnableChopRangeTrades}, ChopRangeMode={ChopRangeMode}, ChopRangeLookbackBars={ChopRangeLookbackBars}, ChopTradesPerEntry={ChopTradesPerEntry}, ChopStopType={ChopStopType}, ChopStopTicks={ChopStopTicks}, ChopStopAtrMult={ChopStopAtrMult}, ChopTrailTicks={ChopTrailTicks}, ChopTrailPlusTicks={ChopTrailPlusTicks}, ChopAddOnProfitMode={ChopAddOnProfitMode}, ChopAddOnProfitTicks={ChopAddOnProfitTicks}, ChopAddOnProfitDollars={ChopAddOnProfitDollars}, EnableHtfSwingGate={EnableHtfSwingGate}, HtfSwingMode={HtfSwingMode}, HtfSwingAction={HtfSwingAction}, HtfSwingLookbackBars={HtfSwingLookbackBars}, HtfSwingPivotStrength={HtfSwingPivotStrength}, HtfSwingDistanceAtr={HtfSwingDistanceAtr}, HtfSwingAtrPeriod={HtfSwingAtrPeriod}, HtfSwingHoldBars={HtfSwingHoldBars}, HtfSwingPrimaryMinutes={HtfSwingPrimaryMinutes}, HtfSwingSecondaryMinutes={HtfSwingSecondaryMinutes}, EnableVolatilityExpansionVote={EnableVolatilityExpansionVote}, VolExpBbWidthDeltaPct={VolExpBbWidthDeltaPct}, VolExpAtrBaselinePeriod={VolExpAtrBaselinePeriod}, VolExpAtrMultiplier={VolExpAtrMultiplier}, EnableRvolGate={EnableRvolGate}, RvolLookbackBars={RvolLookbackBars}, RvolMin={RvolMin}, VrocLookbackBars={VrocLookbackBars}, VrocMinPct={VrocMinPct}, ShowFilterVisuals={ShowFilterVisuals}, ShowTradePnlTags={ShowTradePnlTags}, Visuals[SMA={ShowSmaVisuals},EMA={ShowEmaVisuals},RSI={ShowRsiVisuals},MACD={ShowMacdVisuals},ATR={ShowAtrVisuals},BB={ShowChopBbVisuals},VWAP={ShowVwapMrVisuals}], EnableStraddleTrades={EnableStraddleTrades}, StraddleStartHour={StraddleStartHour}, StraddleStartMinute={StraddleStartMinute}, StraddleRangeMinutes={StraddleRangeMinutes}, StraddleZoneTicks={StraddleZoneTicks}, StraddleZoneOffsetTicks={StraddleZoneOffsetTicks}, TradesPerStraddleEntry={TradesPerStraddleEntry}, StraddleAtrStopMult={StraddleAtrStopMult}, StraddleAtrTrailMult={StraddleAtrTrailMult}, StraddleTrailActivationDollars={StraddleTrailActivationDollars}, StraddleMinProfitHoldSeconds={StraddleMinProfitHoldSeconds}, EnableScaleInTrades={EnableScaleInTrades}, PublishScaleInTradesToBridge={PublishScaleInTradesToBridge}, EnableScaleInTrailing={EnableScaleInTrailing}, ScaleInDrawdownTicks={ScaleInDrawdownTicks}, ScaleInTradesToAdd={ScaleInTradesToAdd}, ScaleInMaxTrades={ScaleInMaxTrades}, ScaleInTrailActivationMode={ScaleInTrailActivationMode}, ScaleInTrailActivationValue={ScaleInTrailActivationValue}, ScaleInProfitLockMode={ScaleInProfitLockMode}, ScaleInProfitLockValue={ScaleInProfitLockValue}, ScaleInTrailIncrementMode={ScaleInTrailIncrementMode}, ScaleInTrailIncrementValue={ScaleInTrailIncrementValue}, UseSMA={UseSMA}, SmaPeriod={SmaPeriod}, UseEMA={UseEMA}, EmaFast={EmaFast}, EmaSlow={EmaSlow}, UseRSI={UseRSI}, RsiPeriod={RsiPeriod}, RsiSmooth={RsiSmooth}, RsiLong={RsiLongThreshold}, RsiShort={RsiShortThreshold}, UseMACD={UseMACD}, VwapGate={UseVwapDirectionGate}, VwapTF={VwapMrTimeframe}, VwapBands={VwapBand1Multiplier}/{VwapBand2Multiplier}, VwapSpikeFilter={VwapFilterSpikes}, VwapSpikeThreshold={VwapSpikeThreshold}, MacdFast={MacdFast}, MacdSlow={MacdSlow}, MacdSmooth={MacdSmooth}, AtrPeriod={AtrPeriod}, StopType={StopType}, StopTicks={StopTicks}, AtrStopMult={AtrStopMult}, TargetType={TargetType}, TargetTicks={TargetTicks}, AtrTargetMult={AtrTargetMult}, ManualEntryOffsetTicks={ManualEntryOffsetTicks}, EnableGlobalTrailing={EnableGlobalTrailing}, GlobalTrailActivationMode={GlobalTrailActivationMode}, GlobalTrailActivationValue={GlobalTrailActivationValue}, GlobalProfitLockMode={GlobalProfitLockMode}, GlobalProfitLockValue={GlobalProfitLockValue}, GlobalTrailIncrementMode={GlobalTrailIncrementMode}, GlobalTrailIncrementValue={GlobalTrailIncrementValue}, UseDemaAtrTrailing={UseDemaAtrTrailing}, UseTightDemaAtrTrailing={UseTightDemaAtrTrailing}, DemaAtrPeriod={DemaAtrPeriod}, DemaAtrMultiplier={DemaAtrMultiplier}, DemaAtrActivationMode={DemaAtrActivationMode}, DemaAtrActivationValue={DemaAtrActivationValue}, UseBreakEvenClamp={UseBreakEvenClamp}, BreakEvenTriggerMode={BreakEvenTriggerMode}, BreakEvenTriggerTicks={BreakEvenTriggerTicks}, BreakEvenTriggerDollars={BreakEvenTriggerDollars}, BreakEvenPlusTicks={BreakEvenPlusTicks}, EnableDemaAtrOnBreakEvenClamp={EnableDemaAtrOnBreakEvenClamp}, EnableSignalDiagnostics={EnableSignalDiagnostics}, EnableTradeStoryLogging={EnableTradeStoryLogging}, StartHaltedOnEnable={StartHaltedOnEnable}");
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
                EnsureSmaIndicatorInstance();
                EnsureEmaIndicatorInstances();
                EnsureRsiIndicatorInstance();
                EnsureMacdIndicatorInstance();
                EnsureVwapMrSeries();
                EnsureAtrIndicatorInstance();
                if (VolExpAtrBaselinePeriod > 1)
                    atrVolBaseline = SMA(atr, VolExpAtrBaselinePeriod);
                if (RvolLookbackBars > 1)
                    rvolBaseline = SMA(Volume, RvolLookbackBars);
                adxChop = ADX(ChopAdxPeriod);
                EnsureChopBbIndicatorInstance();
                AttachIndicatorForVisuals(sma, ShowSmaVisuals);
                AttachIndicatorForVisuals(emaFast, ShowEmaVisuals);
                AttachIndicatorForVisuals(emaSlow, ShowEmaVisuals);
                AttachIndicatorForVisuals(rsi, ShowRsiVisuals);
                AttachIndicatorForVisuals(macd, ShowMacdVisuals);
                AttachIndicatorForVisuals(atr, ShowAtrVisuals);
                AttachIndicatorForVisuals(bbChop, ShowChopBbVisuals);
                if (htfPrimaryIndex >= 0 && htfPrimaryIndex < BarsArray.Length)
                    htfAtrPrimary = ATR(BarsArray[htfPrimaryIndex], HtfSwingAtrPeriod);
                if (htfSecondaryIndex >= 0 && htfSecondaryIndex < BarsArray.Length)
                    htfAtrSecondary = ATR(BarsArray[htfSecondaryIndex], HtfSwingAtrPeriod);
                orbSessionIterator = new SessionIterator(Bars);
                straddleSessionIterator = new SessionIterator(Bars);

                UpdateIndicatorVisuals(true);

				// Compute how many indicator families are enabled so we can cap required votes safely
				maxSignalSlots = (UseSMA ? 1 : 0) + (UseEMA ? 1 : 0) + (UseRSI ? 1 : 0) + (UseMACD ? 1 : 0) + (EnableVolatilityExpansionVote ? 1 : 0);
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
                // Daily PnL limits are "today-only" per Accounts tab; reset local latch on startup.
                ResetDailyPnLLimitState("realtime_start");
                RefreshDailyPnLLimitOnEnable();
                manualHaltActive = false;
                manualHaltStatusText = null;
                manualHaltActivatedAt = DateTime.MinValue;
                shutdownInProgress = false;
                entryCooldownPending = false;
                entryCooldownStartBar = -1;
                entryCooldownEndBar = -1;

                // Flush any historical bookkeeping so live executions start from a clean slate.
                ResetTradeState();
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
                    if (Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0)
                        UpdateStatusLabel(string.Format("Managing {0} {1} ({2})", Position.MarketPosition, Position.Quantity, activeTradeId ?? "<pending>"), true);
                    else
                        UpdateStatusLabel("Active: syncing live state", true);
                }
            }
            else if (State == State.Terminated)
            {
                shutdownInProgress = true;
                if (MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null)
                    MultiStratManager.Instance.TradeSync.UnregisterStrategy(this);

                try
                {
                    MultiStratManager.Instance?.ClearManualHaltOverride(Account != null ? Account.Name : string.Empty, "strategy_terminated");
                }
                catch { }

                bool preserveProtection = ShouldPreserveProtectionOnTerminate();
                if (preserveProtection)
                    StrategyLogInfo("[SAFETY] Strategy terminating while account disconnected; preserving protective orders.");

                // Cancel any working entry orders so they cannot fill after disable.
                CancelWorkingEntryOrders("strategy_terminated");
                if (!preserveProtection)
                {
                    CancelTrackedOrdersOnShutdown("strategy_terminated");

                    // Safety: flatten any open position when the strategy terminates to avoid naked risk.
                    TryFlattenActivePosition("strategy_terminated");
                }

                ResetTradeState(preserveProtection);
                RemoveChartTraderButtons();
                RemoveDrawObject("BaseOptAutoChecklist");
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

        private bool AllowHistoricalTrading
        {
            get { return State == State.Historical; }
        }

        protected override void OnMarketData(MarketDataEventArgs e)
        {
            base.OnMarketData(e);

            if (e == null)
                return;
            if (State != State.Realtime)
                return;

            bool isPrimarySeries = BarsInProgress == 0;
            bool isTickSeries = BarsInProgress == 1;
            if (!isPrimarySeries && !isTickSeries)
                return;
            DateTime eventTime = e.Time != DateTime.MinValue
                ? e.Time
                : (lastMarketDataTime != DateTime.MinValue
                    ? lastMarketDataTime
                    : (Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow));
            if (e.MarketDataType == MarketDataType.Bid)
                lastBid = e.Price;
            else if (e.MarketDataType == MarketDataType.Ask)
                lastAsk = e.Price;
            else if (e.MarketDataType == MarketDataType.Last)
                lastLast = e.Price;
            else
                return;
            if (e.Time != DateTime.MinValue)
                lastMarketDataTime = e.Time;

            double price = lastLast > 0 ? lastLast : (e.Price > 0 ? e.Price : GetRealtimePrice());
            if (price <= 0)
                return;

            if (EnableStraddleTrades)
            {
                if (e.MarketDataType == MarketDataType.Last)
                    UpdateStraddleRangeIntrabar(price, eventTime);
                UpdateStraddleZones();
                if (TryHandleStraddleEntry(price, eventTime, lastBid, lastAsk))
                    return;

                if (isPrimarySeries)
                {
                    UpdateStraddleVisualsIntrabar(eventTime);
                    UpdateStraddleCountdownVisual(eventTime, price);
                }
            }

            if (!isPrimarySeries)
                return;

            if (manualHaltActive || dailyPnLLimitHalted || desyncHoldActive)
                return;

            bool straddleWindowActive = IsStraddleWindowActive(eventTime);
            if (CurrentBar < BarsRequiredToTrade && !straddleWindowActive)
                return;

            if (straddleWindowActive || IsStraddleTradeOpen())
                return;

            if (!IsChopRangeTradingEnabled())
                return;

            HandleChopRangeIntrabar(price);
        }

        protected override void OnBarUpdate()
        {
            try
            {
                OnBarUpdateCore();
            }
            catch (Exception ex)
            {
                if ((DateTime.UtcNow - lastOnBarUpdateExceptionLog).TotalSeconds >= 5)
                {
                    lastOnBarUpdateExceptionLog = DateTime.UtcNow;
                    string barsInfo = string.Empty;
                    try
                    {
                        if (CurrentBars != null)
                        {
                            for (int i = 0; i < CurrentBars.Length; i++)
                            {
                                if (i > 0)
                                    barsInfo += ",";
                                barsInfo += string.Format("B{0}={1}", i, CurrentBars[i]);
                            }
                        }
                    }
                    catch { }

                    StrategyLogError(string.Format("[OnBarUpdate] exception bip={0} curBar={1} primaryBar={2} bars={3} state={4} :: {5}",
                        BarsInProgress,
                        CurrentBar,
                        GetPrimaryCurrentBar(),
                        barsInfo,
                        State,
                        ex));
                }
            }
        }

        private void OnBarUpdateCore()
        {
            int primaryBar = GetPrimaryCurrentBar();
            int minPrimaryBars = BarsRequiredToTrade;
            if (primaryBar < minPrimaryBars)
            {
                if (BarsInProgress == 0)
                    UpdateStatusLabel($"Warming up... {Math.Max(0, minPrimaryBars - primaryBar)} bars to go", false);
                else if (BarsInProgress == 1)
                    UpdateAddOnTradeButton();
                return;
            }
            if (BarsInProgress != 0)
            {
                int bipBars = (CurrentBars != null && BarsInProgress < CurrentBars.Length)
                    ? CurrentBars[BarsInProgress]
                    : CurrentBar;
                if (bipBars < 1)
                {
                    if (BarsInProgress == 1)
                        UpdateAddOnTradeButton();
                    return;
                }
            }

            if (BarsInProgress == 0 || BarsInProgress == 1)
            {
                if (State == State.Realtime)
                    PublishPendingOpens();

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

                    string statusText;
                    if (TryCheckDailyPnLLimit(out statusText))
                    {
                        dailyPnLLimitStatusText = statusText;
                        UpdateStatusLabel(dailyPnLLimitStatusText, false);
                        return;
                    }
                }
            }

            if (BarsInProgress == 1)
            {
                if (State == State.Realtime && EnableStraddleTrades)
                {
                    double tickPrice = Close[0];
                    DateTime tickTime = Time[0];
                    if (tickPrice > 0)
                    {
                        lastLast = tickPrice;
                        if (tickTime != DateTime.MinValue)
                            lastMarketDataTime = tickTime;
                        UpdateStraddleRangeIntrabar(tickPrice, tickTime);
                        UpdateStraddleZones();
                        TryHandleStraddleEntry(tickPrice, tickTime);
                    }
                }

                if (Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0)
                    UpdateStopsTargets(GetRealtimePrice());
                UpdateAddOnTradeButton();
                return;
            }

            if (vwapMrBarsIndex >= 0 && BarsInProgress == vwapMrBarsIndex)
            {
                UpdateVwapMrSeries();
                return;
            }

            if (BarsInProgress != 0)
                return;

            // Allow Strategy Analyzer/backtest runs in the main strategy.
            if (State != State.Realtime && !AllowHistoricalTrading)
                return;

            int requiredBars = BarsRequiredToTrade;
            if (CurrentBar < requiredBars)
            {
                bool liveManaging = Position != null && Position.MarketPosition != MarketPosition.Flat && tradeStates != null && tradeStates.Count > 0;
                if (liveManaging)
                {
                    UpdateStatusLabel(string.Format("Managing {0} {1} ({2})", Position.MarketPosition, Position.Quantity, activeTradeId ?? "<pending>"), true);
                    UpdateStopsTargets(GetRealtimePrice());
                }
                else
                {
                    int remaining = Math.Max(0, requiredBars - CurrentBar);
                    UpdateStatusLabel($"Warming up... {remaining} bars to go", false);
                }
                return;
            }

            UpdateOpeningRange();
            UpdateStraddleState(Time[0]);
            UpdateFilterVisuals();
            UpdateStraddleCountdownVisual(Time[0], GetRealtimePrice());
            UpdateTradePnlLabelVisibility();
            UpdateIndicatorVisuals();
            UpdateIndicatorVisualButtons();
            UpdatePnlTagToggleButton();
            UpdateReverseSignalToggleButton();
            UpdateBiasToggleButtons();
            UpdateVwapGateToggleButton();
            UpdateTradesPerEntryInput();
            UpdateChopTradesPerEntryInput();
            UpdateAddOnTradeButton();
            UpdateEntryCooldownState();

            VwapMrValues vwapValues = new VwapMrValues();
            bool vwapValuesReady = TryUpdateVwapMrValues(out vwapValues);
            double vwapValue = vwapValuesReady ? vwapValues.Vwap : 0.0;

            // If flat with pending close publishes, retry but keep scanning for new signals.
            bool isFlat = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
            if (!isFlat)
                UpdateTradeExcursions();
            else
                ResetScaleInState();
            if (isFlat && tradeStates != null && tradeStates.Count > 0)
            {
                bool hasWorkingEntry = HasWorkingEntryOrders();
                bool hasWorkingNonChopEntry = HasWorkingNonChopEntryOrders();
                bool pending = PublishPendingCloses();
                if (!pending && !hasWorkingEntry)
                    ResetTradeState();
                if (hasWorkingEntry)
                {
                    UpdateStatusLabel("Active: pending entry (waiting for fill/cancel)", true);
                    if (hasWorkingNonChopEntry)
                        return;
                }
                UpdateStatusLabel("Active: scanning (position flat)", true);
                // Do not return; allow new entries even if pending closes remain.
            }

            if (EnableStraddleTrades)
            {
                DateTime now = Time[0];
                if (IsStraddleWindowActive(now) || IsStraddleTradeOpen())
                    return;
            }

            // Build signals
            bool smaLongCond = false;
            bool smaShortCond = false;
            bool emaLongCond = false;
            bool emaShortCond = false;
            bool rsiLongCond = false;
            bool rsiShortCond = false;
            bool macdLongCond = false;
            bool macdShortCond = false;
            bool rsiBbLong = false;
            bool rsiBbShort = false;
            int trendVotesLong = 0, trendVotesShort = 0;
            int momVotesLong = 0, momVotesShort = 0;

            if (UseSMA)
            {
                smaLongCond = Close[0] > sma[0] && sma[0] > sma[1];
                smaShortCond = Close[0] < sma[0] && sma[0] < sma[1];
                if (smaLongCond) trendVotesLong++;
                if (smaShortCond) trendVotesShort++;
            }

            if (UseEMA)
            {
                emaLongCond = emaFast[0] > emaSlow[0];
                emaShortCond = emaFast[0] < emaSlow[0];
                if (emaLongCond) trendVotesLong++;
                if (emaShortCond) trendVotesShort++;
            }

            double chopAdx = adxChop != null ? adxChop[0] : 0.0;
            double chopBbWidth = GetChopBbWidthPct();
            bool chopActive = false;
            bool chopDecayActive = false;
            double chopDecayAdxDelta = 0.0;
            double chopDecayBbDelta = 0.0;
            double rangeHigh;
            double rangeLow;
            double buffer;
            double adxValue;
            double bbWidthPct;
            int chopLookback = Math.Max(2, ChopLookbackBars);
            bool chopReady = TryGetChopState(chopLookback, out chopActive, out chopDecayActive, out chopDecayAdxDelta, out chopDecayBbDelta, out rangeHigh, out rangeLow, out buffer, out adxValue, out bbWidthPct);
            if (chopReady)
            {
                chopAdx = adxValue;
                chopBbWidth = bbWidthPct;
                if (rangeHigh > 0 && rangeLow > 0)
                {
                    lastChopRangeHigh = rangeHigh;
                    lastChopRangeLow = rangeLow;
                    lastChopRangeMid = (rangeHigh + rangeLow) * 0.5;
                    lastChopRangeReady = true;
                }
                else
                {
                    lastChopRangeReady = false;
                }
            }
            else
            {
                lastChopRangeReady = false;
            }

            bool useMeanReversion = EnableRegimeSwitching && chopActive;

            if (UseRSI)
            {
                if (useMeanReversion)
                {
                    bool bbTouchLong = false;
                    bool bbTouchShort = false;
                    if (bbChop != null)
                    {
                        double lower = bbChop.Lower[0];
                        double upper = bbChop.Upper[0];
                        if (!double.IsNaN(lower) && !double.IsNaN(upper))
                        {
                            bbTouchLong = Low[0] <= lower || Close[0] <= lower;
                            bbTouchShort = High[0] >= upper || Close[0] >= upper;
                        }
                    }

                    rsiBbLong = bbTouchLong;
                    rsiBbShort = bbTouchShort;
                    rsiLongCond = rsi.Avg[0] < RsiChopLongThreshold && bbTouchLong;
                    rsiShortCond = rsi.Avg[0] > RsiChopShortThreshold && bbTouchShort;
                }
                else
                {
                    rsiLongCond = CrossAbove(rsi.Avg, RsiLongThreshold, 1);
                    rsiShortCond = CrossBelow(rsi.Avg, RsiShortThreshold, 1);
                }
                if (rsiLongCond) momVotesLong++;
                if (rsiShortCond) momVotesShort++;
            }

            if (UseMACD)
            {
                if (!useMeanReversion)
                {
                    double hist = macd.Default[0] - macd.Avg[0];
                    macdLongCond = hist > 0;
                    macdShortCond = hist < 0;
                    if (macdLongCond) momVotesLong++;
                    if (macdShortCond) momVotesShort++;
                }
            }

            if (!useMeanReversion)
            {
                bool isInsideBar = High[0] < High[1] && Low[0] > Low[1];
                if (isInsideBar)
                {
                    if (Close[0] > Open[0]) momVotesLong++;
                    if (Close[0] < Open[0]) momVotesShort++;
                }
            }

            int effMinLong = Math.Max(1, Math.Min(MinSignalsToEnterLong, maxSignalSlots));
            int effMinShort = Math.Max(1, Math.Min(MinSignalsToEnterShort, maxSignalSlots));
            bool orbAllowsLong = IsOrbEntryAllowed(MarketPosition.Long);
            bool orbAllowsShort = IsOrbEntryAllowed(MarketPosition.Short);

            bool rvolEnabled = EnableRvolGate;
            bool rvolReady;
            bool vrocReady;
            bool rvolOk;
            bool vrocOk;
            double rvolValue;
            double rvolAvg;
            double vrocPct;
            GetRvolGateState(out rvolReady, out vrocReady, out rvolOk, out vrocOk, out rvolValue, out rvolAvg, out vrocPct);
            bool rvolGateReady = rvolReady && vrocReady;
            bool rvolGateOk = !rvolEnabled || !rvolGateReady || (rvolOk && vrocOk);

            bool volExpEnabled = EnableVolatilityExpansionVote;
            double volExpBbPrev = GetChopBbWidthPct(1);
            double volExpBbDelta = chopBbWidth - volExpBbPrev;
            bool volExpBbOk = false;
            if (volExpEnabled && VolExpBbWidthDeltaPct > 0 && volExpBbPrev > 0)
                volExpBbOk = volExpBbDelta >= VolExpBbWidthDeltaPct;

            double volExpAtr = atr != null ? atr[0] : 0.0;
            double volExpAtrBaseline = atrVolBaseline != null ? atrVolBaseline[0] : 0.0;
            double volExpAtrRatio = volExpAtrBaseline > 0 ? volExpAtr / volExpAtrBaseline : 0.0;
            bool volExpAtrOk = false;
            if (volExpEnabled && VolExpAtrBaselinePeriod > 1 && volExpAtrBaseline > 0)
            {
                double multiplier = VolExpAtrMultiplier > 0 ? VolExpAtrMultiplier : 1.0;
                volExpAtrOk = volExpAtr >= volExpAtrBaseline * multiplier;
            }

            bool volExpOk = volExpEnabled && (volExpBbOk || volExpAtrOk);
            if (!useMeanReversion && volExpOk)
            {
                momVotesLong++;
                momVotesShort++;
            }

            int longVotes = useMeanReversion ? momVotesLong : (trendVotesLong + momVotesLong);
            int shortVotes = useMeanReversion ? momVotesShort : (trendVotesShort + momVotesShort);

            bool chopAllowsLong = IsChopEntryAllowed(MarketPosition.Long, rvolGateReady, rvolOk, vrocOk, volExpOk);
            bool chopAllowsShort = IsChopEntryAllowed(MarketPosition.Short, rvolGateReady, rvolOk, vrocOk, volExpOk);

            bool isFlatPosition = Position == null
                || Position.MarketPosition == MarketPosition.Flat
                || Position.Quantity == 0;

            double currentPrice = GetRealtimePrice();
            HtfSwingGateResult htfLongGate = EvaluateHtfSwingGate(MarketPosition.Long, currentPrice);
            HtfSwingGateResult htfShortGate = EvaluateHtfSwingGate(MarketPosition.Short, currentPrice);
            if (EnableHtfSwingGate)
            {
                effMinLong += htfLongGate.ExtraVotes;
                effMinShort += htfShortGate.ExtraVotes;
            }

            bool vwapGateEnabled = UseVwapDirectionGate && vwapValuesReady && vwapValue > 0.0;
            bool biasAllowsLong = (Bias == TradeBias.Both || Bias == TradeBias.LongOnly);
            bool biasAllowsShort = (Bias == TradeBias.Both || Bias == TradeBias.ShortOnly);
            // When VWAP gate is enabled, let VWAP decide direction and ignore manual bias.
            bool effectiveBiasAllowsLong = vwapGateEnabled ? true : biasAllowsLong;
            bool effectiveBiasAllowsShort = vwapGateEnabled ? true : biasAllowsShort;

            bool qualityLong = false;
            bool qualityShort = false;
            if (useMeanReversion)
            {
                if (momVotesLong > 0)
                {
                    qualityLong = true;
                    chopAllowsLong = true;
                }
                if (momVotesShort > 0)
                {
                    qualityShort = true;
                    chopAllowsShort = true;
                }
            }
            else
            {
                bool splitLong = (trendVotesLong > 0 && momVotesLong > 0) || effMinLong == 1;
                bool splitShort = (trendVotesShort > 0 && momVotesShort > 0) || effMinShort == 1;
                bool convictionLong = true;
                bool convictionShort = true;
                if (EnableCandleConviction && (High[0] - Low[0] > TickSize * 4))
                {
                    double range = High[0] - Low[0];
                    if (Close[0] < Low[0] + (range * 0.5)) convictionLong = false;
                    if (Close[0] > High[0] - (range * 0.5)) convictionShort = false;
                }

                qualityLong = splitLong && convictionLong && (longVotes >= effMinLong);
                qualityShort = splitShort && convictionShort && (shortVotes >= effMinShort);
            }

            bool canLong = effectiveBiasAllowsLong && qualityLong;
            bool canShort = effectiveBiasAllowsShort && qualityShort;

            bool longGated = (!orbAllowsLong || !chopAllowsLong) || (EnableHtfSwingGate && htfLongGate.Blocked);
            bool shortGated = (!orbAllowsShort || !chopAllowsShort) || (EnableHtfSwingGate && htfShortGate.Blocked);

            if (longGated)
                canLong = false;
            if (shortGated)
                canShort = false;

            if (!EnableVoteEntrySignals)
            {
                canLong = false;
                canShort = false;
            }
            else if (ReverseSignalTrading)
            {
                bool reverseActive = longVotes > 0 || shortVotes > 0;
                bool targetLong = false;
                bool targetShort = false;

                if (reverseActive)
                {
                    if (longVotes == shortVotes)
                    {
                        if (effectiveBiasAllowsLong ^ effectiveBiasAllowsShort)
                        {
                            targetLong = effectiveBiasAllowsLong;
                            targetShort = effectiveBiasAllowsShort;
                        }
                    }
                    else if (longVotes < shortVotes)
                    {
                        targetLong = true;
                    }
                    else
                    {
                        targetShort = true;
                    }
                }

                canLong = targetLong && effectiveBiasAllowsLong && !longGated;
                canShort = targetShort && effectiveBiasAllowsShort && !shortGated;
            }

            bool vwapGateAllowsLong = true;
            bool vwapGateAllowsShort = true;
            if (vwapGateEnabled)
            {
                if (Close[0] > vwapValue)
                    vwapGateAllowsShort = false;
                else if (Close[0] < vwapValue)
                    vwapGateAllowsLong = false;
            }

            if (vwapGateEnabled)
            {
                if (!vwapGateAllowsLong)
                    canLong = false;
                if (!vwapGateAllowsShort)
                    canShort = false;
            }

            double vwapDistancePct = 0.0;
            if (vwapValuesReady && vwapValue > 0.0 && currentPrice > 0.0)
                vwapDistancePct = ((currentPrice - vwapValue) / vwapValue) * 100.0;

            lastBreakoutSignalActive = canLong || canShort;

            lastEntrySnapshot = new EntrySignalSnapshot
            {
                LongVotes = longVotes,
                ShortVotes = shortVotes,
                MinLong = effMinLong,
                MinShort = effMinShort,
                RegimeSwitchingEnabled = EnableRegimeSwitching,
                RegimeIsChop = useMeanReversion,
                ReverseSignalTrading = ReverseSignalTrading,
                OrbLong = orbAllowsLong,
                OrbShort = orbAllowsShort,
                ChopLong = chopAllowsLong,
                ChopShort = chopAllowsShort,
                    ChopAdx = chopAdx,
                    ChopBbWidthPct = chopBbWidth,
                    ChopDecayActive = chopDecayActive,
                    ChopDecayAdxDelta = chopDecayAdxDelta,
                    ChopDecayBbDeltaPct = chopDecayBbDelta,
                    HtfEnabled = EnableHtfSwingGate,
                    HtfLong = htfLongGate,
                    HtfShort = htfShortGate,
                VolExpEnabled = volExpEnabled,
                VolExpOk = volExpOk,
                VolExpBbWidthPct = chopBbWidth,
                VolExpBbDeltaPct = volExpBbDelta,
                VolExpAtr = volExpAtr,
                VolExpAtrBaseline = volExpAtrBaseline,
                VolExpAtrRatio = volExpAtrRatio,
                RvolEnabled = rvolEnabled,
                RvolReady = rvolReady,
                RvolOk = rvolOk,
                RvolValue = rvolValue,
                RvolAvg = rvolAvg,
                VrocReady = vrocReady,
                VrocOk = vrocOk,
                VrocPct = vrocPct,
                Time = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow
            };

            UpdateEntryChecklist(longVotes, shortVotes, effMinLong, effMinShort, orbAllowsLong, orbAllowsShort,
                chopAllowsLong, chopAllowsShort, chopActive, chopDecayActive, chopDecayAdxDelta, chopDecayBbDelta, chopAdx, chopBbWidth, htfLongGate, htfShortGate,
                volExpEnabled, volExpOk, volExpBbDelta, volExpAtrRatio,
                rvolEnabled, rvolGateReady, rvolOk, vrocOk, rvolValue, vrocPct,
                canLong, canShort,
                vwapGateEnabled, vwapValuesReady, vwapValue, vwapDistancePct, vwapSessionBars, vwapSessionVolume, vwapGateAllowsLong, vwapGateAllowsShort,
                smaLongCond, smaShortCond, emaLongCond, emaShortCond, rsiLongCond, rsiShortCond, macdLongCond, macdShortCond);

            bool chopRangeReady = chopReady && rangeHigh > 0 && rangeLow > 0;
            bool chopFilterActive = EnableChopFilter && chopActive;
            bool chopRangeActive = chopFilterActive && chopRangeReady;
            bool chopJustActivated = chopFilterActive && !lastChopActive;
            lastChopActive = chopFilterActive;

            if (State == State.Realtime && EnableSignalDiagnostics && isFlatPosition)
            {
                string htfLongSummary = FormatHtfGateSummary(htfLongGate, true);
                string htfShortSummary = FormatHtfGateSummary(htfShortGate, true);
                string volExpStatus = volExpEnabled ? (volExpOk ? "OK" : "NO") : "OFF";
                string rvolStatus = rvolEnabled
                    ? (rvolGateReady ? ((rvolOk && vrocOk) ? "OK" : "NO") : "n/a")
                    : "OFF";
                string chopDecayText = EnableChopDecayGate
                    ? string.Format(" DECAY {0} dADX {1:F2} dBB {2:F2}%", chopDecayActive ? "ON" : "OFF", chopDecayAdxDelta, chopDecayBbDelta)
                    : string.Empty;
                string regimeText = EnableRegimeSwitching ? (useMeanReversion ? "CHOP" : "TREND") : "TREND(off)";
                string reverseText = ReverseSignalTrading ? "ON" : "OFF";
                string rsiBbText = useMeanReversion
                    ? string.Format("RSI_BB L {0} S {1}", rsiBbLong, rsiBbShort)
                    : "RSI_BB L n/a S n/a";
                StrategyLogInfo(string.Format("[SIGNAL] votes L {0}/{1} S {2}/{3} ORB L {4} S {5} CHOP L {6} S {7} REGIME {8} REV {9} {10} (ADX {11:F1} BB {12:F2}%{13}) HTF L {14} S {15} VOL {16} (bbDelta {17:F2}% atrx {18:F2}) RVOL {19} (rvol {20:F2} vroc {21:F1}%)",
                    longVotes, effMinLong, shortVotes, effMinShort,
                    orbAllowsLong, orbAllowsShort,
                    chopAllowsLong, chopAllowsShort,
                    regimeText, reverseText, rsiBbText,
                    chopAdx, chopBbWidth,
                    chopDecayText,
                    htfLongSummary, htfShortSummary,
                    volExpStatus,
                    volExpBbDelta,
                    volExpAtrRatio,
                    rvolStatus,
                    rvolValue,
                    vrocPct));
            }
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

            if (StartHaltedOnEnable && State != State.Realtime)
            {
                UpdateStatusLabel("HALTED: manual (awaiting resume)", false);
                return;
            }

            if (manualHaltActive)
            {
                TryEnforceManualHaltFlat();
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
                ResetGlobalTrailingState();
                bool cooldownActive = IsEntryCooldownActive();
                bool cooldownBypassForChop = cooldownActive && IsChopRangeTradingEnabled() && chopRangeActive;
                if (cooldownActive && !cooldownBypassForChop)
                {
                    if (IsChopRangeTradingEnabled() && HasWorkingChopEntryOrders())
                        CancelWorkingChopEntryOrders("cooldown");
                    int remainingBars = GetEntryCooldownRemainingBars();
                    UpdateStatusLabel($"Cooldown: waiting {remainingBars} bar(s) before next entry", false);
                    return;
                }

                if (IsChopRangeTradingEnabled())
                {
                    if (chopRangeActive)
                    {
                        if (!cooldownActive && (canLong || canShort))
                        {
                            if (HasWorkingChopEntryOrders())
                                CancelWorkingChopEntryOrders("breakout_signal");
                        }
                        else
                        {
                            double rangeMid = (rangeHigh + rangeLow) * 0.5;
                            double priceForSide = currentPrice > 0 ? currentPrice : Close[0];
                            bool inLowerHalf = priceForSide <= rangeMid;
                            bool inUpperHalf = priceForSide > rangeMid;
                            bool chopRangeAllowLong = (Bias == TradeBias.Both || Bias == TradeBias.LongOnly) && inLowerHalf;
                            bool chopRangeAllowShort = (Bias == TradeBias.Both || Bias == TradeBias.ShortOnly) && inUpperHalf;
                            UpdateChopEntryOrders(rangeHigh, rangeLow, chopRangeAllowLong, chopRangeAllowShort, false);
                        }
                    }
                    else if (HasWorkingChopEntryOrders())
                    {
                        CancelWorkingChopEntryOrders("chop_inactive");
                    }
                }
                else if (HasWorkingChopEntryOrders())
                {
                    CancelWorkingChopEntryOrders("chop_disabled");
                }

                bool tradeSyncOk = MultiStratManager.Instance != null && MultiStratManager.Instance.TradeSync != null;
                if (cooldownActive)
                {
                    int remainingBars = GetEntryCooldownRemainingBars();
                    UpdateStatusLabel($"Cooldown: waiting {remainingBars} bar(s) before next entry (chop allowed)", false);
                }
                else
                {
                    UpdateStatusLabel($"Active: scanning L/S votes {longVotes}/{shortVotes} (bias {Bias}, min {effMinLong}/{effMinShort})", tradeSyncOk);
                }

                if (!cooldownActive && canLong)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Long))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterLong because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                        UpdateStatusLabel("Blocked: opposing exposure prevents new LONG", false);
                    }
                    else
                    {
                        int entriesToSubmit = GetEffectiveTradesPerEntry();
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
                else if (!cooldownActive && canShort)
                {
                    if (IsAccountOpposedPosition(MarketPosition.Short))
                    {
                        if (Debug)
                            StrategyLogDebug($"[AUTO][GUARD] Skipping EnterShort because other strategies are net {GetOtherStrategyExposure()} on this instrument.");
                        UpdateStatusLabel("Blocked: opposing exposure prevents new SHORT", false);
                    }
                    else
                    {
                        int entriesToSubmit = GetEffectiveTradesPerEntry();
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
                if (IsChopRangeTradingEnabled() && HasWorkingChopEntryOrders())
                    CancelWorkingChopEntryOrders("position_open");

                if (IsChopRangeTradingEnabled() && chopJustActivated && lastChopAddOnBar != CurrentBar && IsChopAddOnProfitSatisfied(currentPrice))
                {
                    int entriesToSubmit = GetEffectiveChopTradesPerEntry();
                    int quantityPerEntry = Math.Max(1, DefaultQuantity);
                    SubmitChopAddOnEntries(Position.MarketPosition, entriesToSubmit, quantityPerEntry);
                    ForceChopTrailingForOpenTrades(currentPrice);
                    lastChopAddOnBar = CurrentBar;
                }

                bool hasChopManagedTrades = GetChopManagedStates().Count > 0;
                if (hasChopManagedTrades && (canLong || canShort))
                {
                    StrategyLogInfo("[CHOP] Breakout signal detected; closing chop-managed trades.");
                    ExitChopRangeTrades("CHOP_BRK");
                    return;
                }

                string statusTradeId = !string.IsNullOrEmpty(activeTradeId) ? activeTradeId : "<pending>";
                UpdateStatusLabel($"Managing {Position.MarketPosition} {Position.Quantity} ({statusTradeId})", true);
                UpdateStopsTargets(GetRealtimePrice());
            }

            if (Debug)
                StrategyLogDebug($"{Time[0]} votes L/S: {longVotes}/{shortVotes} canL={canLong} canS={canShort} bias={Bias} minL={MinSignalsToEnterLong}->{effMinLong} minS={MinSignalsToEnterShort}->{effMinShort} Pos:{Position.MarketPosition}");
        }

        private void HandleChopRangeIntrabar(double currentPrice)
        {
            if (!IsChopRangeTradingEnabled())
                return;

            bool chopActive = lastChopActive;
            if (!chopActive)
            {
                if (HasWorkingChopEntryOrders())
                    CancelWorkingChopEntryOrders("chop_inactive");
                return;
            }

            double rangeHigh = lastChopRangeHigh;
            double rangeLow = lastChopRangeLow;
            double rangeMid = lastChopRangeMid;

            if (!lastChopRangeReady || rangeHigh <= 0 || rangeLow <= 0)
            {
                if (!TryGetChopRange(out rangeHigh, out rangeLow, out rangeMid, out _))
                    return;

                lastChopRangeHigh = rangeHigh;
                lastChopRangeLow = rangeLow;
                lastChopRangeMid = rangeMid;
                lastChopRangeReady = true;
            }

            bool isFlat = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
            if (!isFlat)
            {
                if (HasWorkingChopEntryOrders())
                    CancelWorkingChopEntryOrders("position_open");

                TryApplyChopTradeProtection(currentPrice, rangeHigh, rangeLow, rangeMid, chopActive);
                return;
            }

            if (lastBreakoutSignalActive)
            {
                if (HasWorkingChopEntryOrders())
                    CancelWorkingChopEntryOrders("breakout_signal");
                return;
            }

            bool inLowerHalf = currentPrice <= rangeMid;
            bool inUpperHalf = currentPrice > rangeMid;
            bool allowLong = (Bias == TradeBias.Both || Bias == TradeBias.LongOnly) && inLowerHalf;
            bool allowShort = (Bias == TradeBias.Both || Bias == TradeBias.ShortOnly) && inUpperHalf;

            MarketPosition desiredSide = allowLong ? MarketPosition.Long : (allowShort ? MarketPosition.Short : MarketPosition.Flat);
            if (desiredSide == MarketPosition.Flat)
            {
                if (HasWorkingChopEntryOrders())
                    CancelWorkingChopEntryOrders("chop_side_none");
                return;
            }

            MarketPosition oppositeSide = desiredSide == MarketPosition.Long ? MarketPosition.Short : MarketPosition.Long;
            bool hasDesired = HasWorkingChopEntryOrders(desiredSide);
            bool hasOpposite = HasWorkingChopEntryOrders(oppositeSide);

            if (!hasDesired && hasOpposite)
                CancelWorkingChopEntryOrders(oppositeSide, "chop_mid_flip");

            UpdateChopEntryOrders(rangeHigh, rangeLow, desiredSide == MarketPosition.Long, desiredSide == MarketPosition.Short, !hasDesired && hasOpposite);
        }

        private void UpdateTradeExcursions()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            double barHigh = High[0];
            double barLow = Low[0];

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;

                double entry = state.EntryPrice;
                if (entry <= 0)
                    continue;

                if (state.EntrySide == MarketPosition.Long)
                {
                    if (state.MaxFavorablePrice <= 0)
                        state.MaxFavorablePrice = entry;
                    if (state.MaxAdversePrice <= 0)
                        state.MaxAdversePrice = entry;

                    if (barHigh > state.MaxFavorablePrice)
                        state.MaxFavorablePrice = barHigh;
                    if (barLow < state.MaxAdversePrice)
                        state.MaxAdversePrice = barLow;
                }
                else if (state.EntrySide == MarketPosition.Short)
                {
                    if (state.MaxFavorablePrice <= 0)
                        state.MaxFavorablePrice = entry;
                    if (state.MaxAdversePrice <= 0)
                        state.MaxAdversePrice = entry;

                    // For shorts, favorable is lower, adverse is higher.
                    if (barLow < state.MaxFavorablePrice)
                        state.MaxFavorablePrice = barLow;
                    if (barHigh > state.MaxAdversePrice)
                        state.MaxAdversePrice = barHigh;
                }
            }
        }

        private void UpdateStopsTargets(double? priceOverride = null, bool forceDemaTrailing = false, string demaContext = null)
        {
            if (string.IsNullOrEmpty(activeTradeId))
                return;
            if (shutdownInProgress)
                return;

            if (State == State.Realtime && dailyPnLLimitHalted)
                return;

            TradeRuntimeState activeState;
            if (!TryGetTradeState(activeTradeId, out activeState))
                return;

            if (ShouldPublishTradeLifecycle(activeState) && !activeState.OpenPublished && !activeState.IsSynthetic)
            {
                if (PublishOpenEvent(activeState))
                    activeState.OpenPublished = true;
            }

            int primaryBar = GetPrimaryCurrentBar();
            if (primaryBar < BarsRequiredToTrade)
                return;

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
            bool stopHold = IsManualProtectionHoldActive(activeState, true);
            bool targetHold = IsManualProtectionHoldActive(activeState, false);
            bool manualStopLocked = stopHold || activeState.ManualStopOverride;
            bool manualTargetLocked = targetHold || activeState.ManualTargetOverride;

            UpdateScaleInDrawdownVisuals(currentPrice);
            if (!EnableGlobalTrailing)
                ResetGlobalTrailingState();

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

            if (IsMultiEntrySyncEnabled)
            {
                MultiEntrySyncGroup manualGroup;
                if (TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out manualGroup))
                {
                    List<TradeRuntimeState> states = GetMultiEntrySyncStates(manualGroup.TradeId);
                    foreach (TradeRuntimeState state in states)
                    {
                        if (state == null || state.RemainingQuantity <= 0)
                            continue;
                        manualStopLocked = manualStopLocked || state.ManualStopOverride;
                        manualTargetLocked = manualTargetLocked || state.ManualTargetOverride;
                        EnforceManualProtectionForState(state);
                    }
                }
                else
                {
                    if (openTradeOrder != null && openTradeOrder.Count > 0)
                    {
                        foreach (var tradeId in openTradeOrder)
                        {
                            TradeRuntimeState state;
                            if (!TryGetTradeState(tradeId, out state))
                                continue;
                            if (state == null || state.RemainingQuantity <= 0)
                                continue;
                            manualStopLocked = manualStopLocked || state.ManualStopOverride;
                            manualTargetLocked = manualTargetLocked || state.ManualTargetOverride;
                            EnforceManualProtectionForState(state);
                        }
                    }
                    else
                    {
                        EnforceManualProtectionForState(activeState);
                    }
                }
            }
            else
            {
                EnforceManualProtectionForState(activeState);
            }

            DateTime now = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
            if (activeState.IsStraddleEntry)
            {
                TryApplyStraddleProtection(activeState, currentPrice, now);
                TryTriggerScaleIn(currentPrice);
                ApplyScaleInTrailing(currentPrice, manualStopLocked, manualTargetLocked);
                return;
            }

            if (activeState.RunUpActive)
            {
                if (manualStopLocked)
                    return;

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

            bool useGlobalTrailing = EnableGlobalTrailing && !activeState.IsChopEntry;

            if (activeState.IsVwapEntry)
            {
                bool vwapHandled = ApplyVwapProtection(activeState, currentPrice, manualStopLocked, manualTargetLocked);
                TryTriggerScaleIn(currentPrice);
                if (!useGlobalTrailing)
                    ApplyScaleInTrailing(currentPrice, manualStopLocked, manualTargetLocked);
                if (vwapHandled && !useGlobalTrailing)
                    return;
            }

            if (IsChopRangeTradingEnabled())
            {
                bool chopActiveNow = IsChopActiveNow();
                double rangeHigh = lastChopRangeHigh;
                double rangeLow = lastChopRangeLow;
                double rangeMid = lastChopRangeMid;

                if (chopActiveNow && (!lastChopRangeReady || rangeHigh <= 0 || rangeLow <= 0))
                {
                    if (TryGetChopRange(out rangeHigh, out rangeLow, out rangeMid, out _))
                    {
                        lastChopRangeHigh = rangeHigh;
                        lastChopRangeLow = rangeLow;
                        lastChopRangeMid = rangeMid;
                        lastChopRangeReady = true;
                    }
                }

                if (TryApplyChopTradeProtection(currentPrice, rangeHigh, rangeLow, rangeMid, chopActiveNow))
                    return;
            }

            TryTriggerScaleIn(currentPrice);

            bool scaleInHoldActive = false;
            if (scaleInHoldUntil != DateTime.MinValue)
            {
                DateTime holdNow = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                scaleInHoldActive = holdNow < scaleInHoldUntil;
            }

            if (scaleInHoldUntil != DateTime.MinValue || scaleInTrailActivated)
            {
                if (!useGlobalTrailing)
                {
                    ApplyScaleInTrailing(currentPrice, manualStopLocked, manualTargetLocked);
                    if (scaleInHoldActive || scaleInTrailActivated)
                        return;
                }
            }

            bool demaApplied = false;
            bool globalTrailingApplied = false;
            if (useGlobalTrailing)
                globalTrailingApplied = ApplyGlobalTrailing(activeState, currentPrice, manualStopLocked);

            if (!useGlobalTrailing)
            {
                bool breakEvenForcesDema = ShouldForceDemaFromBreakEven(currentPrice);
                string effectiveDemaContext = demaContext;
                if (breakEvenForcesDema)
                    effectiveDemaContext = string.IsNullOrWhiteSpace(effectiveDemaContext) ? "BREAKEVEN_CLAMP" : effectiveDemaContext + "|BREAKEVEN_CLAMP";

                bool forceDema = forceDemaTrailing || breakEvenForcesDema;
                if (UseDemaAtrTrailing || forceDema)
                    demaApplied = TryApplyDemaAtrTrailingStop(currentPrice, forceDema, effectiveDemaContext);

                bool forceBreakEvenClamp = HasContext(effectiveDemaContext, "CHOP_PROFIT");
                if (!demaApplied && UseBreakEvenClamp)
                    TryApplyBreakEvenStop(activeTradeId, currentPrice, forceBreakEvenClamp);
            }

            if (!demaApplied && !globalTrailingApplied && !stopSet && !stopHold)
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

            if (!targetSet && !targetHold)
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

            if (!useGlobalTrailing)
                ApplyScaleInTrailing(currentPrice, manualStopLocked, manualTargetLocked);
        }

        private bool TryApplyStraddleProtection(TradeRuntimeState activeState, double currentPrice, DateTime now)
        {
            if (activeState == null || !activeState.IsStraddleEntry)
                return false;

            List<TradeRuntimeState> states = null;
            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group) && group != null)
            {
                states = GetMultiEntrySyncStates(group.TradeId);
            }
            else if (openTradeOrder != null && openTradeOrder.Count > 0)
            {
                states = new List<TradeRuntimeState>();
                foreach (string tradeId in openTradeOrder)
                {
                    TradeRuntimeState state;
                    if (TryGetTradeState(tradeId, out state) && state != null && state.IsStraddleEntry && state.RemainingQuantity > 0)
                        states.Add(state);
                }
            }
            else
            {
                states = new List<TradeRuntimeState> { activeState };
            }

            return ApplyStraddleProtection(states, currentPrice, now);
        }

        private bool ApplyStraddleProtection(List<TradeRuntimeState> states, double currentPrice, DateTime now)
        {
            if (states == null || states.Count == 0)
                return true;

            states = states.Where(s => s != null && s.RemainingQuantity > 0).ToList();
            if (states.Count == 0)
                return true;

            bool isLong = states[0].EntrySide == MarketPosition.Long;
            double entryPrice = Position != null && Position.Quantity > 0 ? Position.AveragePrice : states[0].EntryPrice;
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                return true;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double? hardStop = GetStraddleHardStopPrice(isLong ? MarketPosition.Long : MarketPosition.Short);
            if (!hardStop.HasValue || hardStop.Value <= 0)
                return true;

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

            foreach (var state in states)
            {
                if (state == null)
                    continue;
                double desiredStop = hardStop.Value;
                double? lastAccepted = state.LastStopPrice > 0 ? (double?)state.LastStopPrice : null;
                if (state.StraddleTrailingActive && lastAccepted.HasValue)
                    desiredStop = isLong ? Math.Max(lastAccepted.Value, desiredStop) : Math.Min(lastAccepted.Value, desiredStop);
                double? safeStop = ClampStopPrice(desiredStop, clampRef, isLong, lastAccepted);
                if (!safeStop.HasValue)
                    continue;
                if (lastAccepted.HasValue && PricesClose(lastAccepted.Value, safeStop.Value))
                    continue;

                IssueStopLoss(state.TradeId, CalculationMode.Price, safeStop.Value, false);
            }

            if (!EnableScaleInTrades || !scaleInActive)
            {
                foreach (var state in states)
                {
                    if (state == null)
                        continue;
                    if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState))
                        TryCancelOrder(state.TradeId, state.TargetOrder, null, "target");
                    state.TargetOrder = null;
                    state.LastTargetPrice = 0;
                }
            }

            double pnl = CalculateSignedUnrealizedPnlAtPrice(currentPrice);
            bool gatePassed = states.Any(s => s.StraddleProfitGatePassed);
            if (!gatePassed)
            {
                if (pnl > 0)
                {
                    DateTime start = states[0].StraddleProfitStart;
                    if (start == DateTime.MinValue)
                    {
                        foreach (var state in states)
                            state.StraddleProfitStart = now;
                    }
                    else if ((now - start).TotalSeconds >= Math.Max(0, StraddleMinProfitHoldSeconds))
                    {
                        foreach (var state in states)
                            state.StraddleProfitGatePassed = true;
                        gatePassed = true;
                    }
                }
                else
                {
                    foreach (var state in states)
                        state.StraddleProfitStart = DateTime.MinValue;
                }
            }

            if (gatePassed)
            {
                bool trailingActive = states.Any(s => s.StraddleTrailingActive);
                bool trailAllowed = trailingActive || StraddleTrailActivationDollars <= 0 || pnl >= StraddleTrailActivationDollars;
                if (trailAllowed && atr != null && atr[0] > 0)
                {
                    double atrTrail = atr[0] * StraddleAtrTrailMult;
                    if (atrTrail <= 0)
                        atrTrail = tickSize;

                    double highWater = states.Max(s => s.StraddleTrailHighWater > 0 ? s.StraddleTrailHighWater : entryPrice);
                    double lowWater = states.Min(s => s.StraddleTrailLowWater > 0 ? s.StraddleTrailLowWater : entryPrice);
                    if (isLong)
                        highWater = Math.Max(highWater, currentPrice);
                    else
                        lowWater = Math.Min(lowWater, currentPrice);

                    double trailStop = isLong ? highWater - atrTrail : lowWater + atrTrail;
                    trailStop = Instrument?.MasterInstrument?.RoundToTickSize(trailStop) ?? Math.Round(trailStop / tickSize) * tickSize;

                    foreach (var state in states)
                    {
                        if (state == null)
                            continue;

                        double desiredStop = trailStop;
                        desiredStop = isLong ? Math.Max(desiredStop, hardStop.Value) : Math.Min(desiredStop, hardStop.Value);
                        if (isLong && state.LastStopPrice > 0)
                            desiredStop = Math.Max(state.LastStopPrice, desiredStop);
                        if (!isLong && state.LastStopPrice > 0)
                            desiredStop = Math.Min(state.LastStopPrice, desiredStop);

                        double? lastAccepted = state.LastStopPrice > 0 ? (double?)state.LastStopPrice : null;
                        double? safeStop = ClampStopPrice(desiredStop, clampRef, isLong, lastAccepted);
                        if (safeStop.HasValue && (!lastAccepted.HasValue || !PricesClose(lastAccepted.Value, safeStop.Value)))
                            IssueStopLoss(state.TradeId, CalculationMode.Price, safeStop.Value, false);

                        state.StraddleTrailingActive = true;
                        state.StraddleTrailHighWater = highWater;
                        state.StraddleTrailLowWater = lowWater;
                    }
                }
            }

            return true;
        }

        private bool ShouldForceDemaFromBreakEven(double currentPrice)
        {
            if (!EnableDemaAtrOnBreakEvenClamp || !UseBreakEvenClamp)
                return false;

            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;

                double entryPrice = state.EntryPrice > 0 && !double.IsNaN(state.EntryPrice)
                    ? state.EntryPrice
                    : Position.AveragePrice;
                if (entryPrice <= 0 || double.IsNaN(entryPrice))
                    continue;

                double? clamp = TryGetBreakEvenClampPrice(state, entryPrice, currentPrice, isLong, false);
                if (clamp.HasValue)
                    return true;
            }

            return false;
        }

        private static bool HasContext(string context, string token)
        {
            if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(token))
                return false;

            return context.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void StartEntryCooldown()
        {
            if (EntryCooldownBars <= 0)
                return;

            entryCooldownStartBar = CurrentBar + 1;
            entryCooldownEndBar = entryCooldownStartBar + EntryCooldownBars - 1;
            entryCooldownPending = true;
            StrategyLogInfo($"[COOLDOWN] Entry cooldown started for {EntryCooldownBars} bars (start={entryCooldownStartBar}, end={entryCooldownEndBar}).");
        }

        private void UpdateEntryCooldownState()
        {
            if (!entryCooldownPending)
                return;

            if (CurrentBar >= entryCooldownStartBar)
                entryCooldownPending = false;
        }

        private bool IsEntryCooldownActive()
        {
            if (EntryCooldownBars <= 0)
                return false;

            if (entryCooldownPending)
                return true;

            if (entryCooldownStartBar < 0 || entryCooldownEndBar < entryCooldownStartBar)
                return false;

            return CurrentBar >= entryCooldownStartBar && CurrentBar <= entryCooldownEndBar;
        }

        private int GetEntryCooldownRemainingBars()
        {
            if (EntryCooldownBars <= 0)
                return 0;

            if (entryCooldownPending && CurrentBar < entryCooldownStartBar)
                return EntryCooldownBars;

            if (entryCooldownStartBar < 0 || entryCooldownEndBar < entryCooldownStartBar)
                return 0;

            if (CurrentBar > entryCooldownEndBar)
                return 0;

            return entryCooldownEndBar - CurrentBar + 1;
        }

        private bool IssueStopLoss(string tradeId, CalculationMode mode, double value, bool simulated = false)
        {
            if (string.IsNullOrEmpty(tradeId))
                return false;
            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return false;
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double desired = value;
            double desiredPrice = 0;
            if (mode == CalculationMode.Price)
            {
                desired = Instrument?.MasterInstrument?.RoundToTickSize(value) ?? Math.Round(value / tickSize) * tickSize;
                desiredPrice = desired;
            }
            else if (mode == CalculationMode.Ticks)
            {
                double entry = state.EntryPrice;
                if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                    entry = Position.AveragePrice;
                if (entry > 0 && !double.IsNaN(entry))
                    desiredPrice = state.EntrySide == MarketPosition.Long
                        ? entry - value * tickSize
                        : entry + value * tickSize;
                if (desiredPrice > 0)
                    desiredPrice = Instrument?.MasterInstrument?.RoundToTickSize(desiredPrice) ?? Math.Round(desiredPrice / tickSize) * tickSize;
            }

            if (state.PendingAutoStopUpdate && state.PendingAutoStopPrice > 0 && desiredPrice > 0 && PricesClose(state.PendingAutoStopPrice, desiredPrice))
            {
                stopSet = true;
                return true;
            }

            if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState))
            {
                double workingPrice = state.StopOrder.StopPrice > 0 ? state.StopOrder.StopPrice : state.LastStopPrice;
                if (workingPrice > 0 && desiredPrice > 0 && PricesClose(workingPrice, desiredPrice))
                {
                    stopSet = true;
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    return true;
                }

                if ((state.StopOrder.OrderState == OrderState.ChangePending || state.StopOrder.OrderState == OrderState.ChangeSubmitted) &&
                    state.PendingAutoStopPrice > 0 &&
                    desiredPrice > 0 &&
                    PricesClose(state.PendingAutoStopPrice, desiredPrice))
                {
                    stopSet = true;
                    return true;
                }

                if (state.StopOrder.OrderState == OrderState.ChangePending || state.StopOrder.OrderState == OrderState.ChangeSubmitted)
                {
                    if (Debug)
                        StrategyLogDebug($"[AUTO][STOP] Skip stop update for {tradeId} (change pending).");
                    return false;
                }
            }

            if (IsManualProtectionHoldActive(state, true))
            {
                if (Debug)
                    StrategyLogDebug($"[AUTO][STOP] Skipping auto stop update for {tradeId} due to manual adjustment.");
                return false;
            }

            double targetValue = desired;

            state.PendingAutoStopUpdate = true;
            double pendingStopPrice = 0;
            if (mode == CalculationMode.Price)
            {
                pendingStopPrice = targetValue;
            }
            else if (mode == CalculationMode.Ticks)
            {
                if (tickSize > 0)
                {
                    double entry = state.EntryPrice;
                    if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                        entry = Position.AveragePrice;
                    if (entry > 0 && !double.IsNaN(entry))
                        pendingStopPrice = state.EntrySide == MarketPosition.Long
                            ? entry - value * tickSize
                            : entry + value * tickSize;
                }
            }
            if (pendingStopPrice > 0)
                pendingStopPrice = Instrument?.MasterInstrument?.RoundToTickSize(pendingStopPrice) ?? Math.Round(pendingStopPrice / tickSize) * tickSize;
            state.PendingAutoStopPrice = pendingStopPrice;
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
                        double calcTickSize = tickSize <= 0 ? 1.0 : tickSize;
                        if (entry > 0 && calcTickSize > 0)
                        {
                            stopPrice = state.EntrySide == MarketPosition.Long
                                ? entry - targetValue * calcTickSize
                                : entry + targetValue * calcTickSize;
                        }
                    }

                    if (stopPrice <= 0)
                    {
                        state.PendingAutoStopUpdate = false;
                        StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Unable to compute stop price for {0} (entry={1:F2}, target={2})", tradeId, entry, targetValue));
                        return false;
                    }
                    state.PendingAutoStopPrice = stopPrice;

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
                    stopSet = true;
                    StrategyLogInfo(string.Format("[AUTO][BOOTSTRAP] Submitted explicit stop at {0:F2} for {1}", stopPrice, tradeId));
                }
                else
                {
                    // For managed stops, validate the derived price to avoid Ninja zero-price errors.
                    double derived = 0;
                    if (mode == CalculationMode.Ticks && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    {
                        double calcTickSize = tickSize <= 0 ? 1.0 : tickSize;
                        double entry = Position.AveragePrice;
                        if (entry > 0 && calcTickSize > 0)
                        {
                            derived = state.EntrySide == MarketPosition.Long
                                ? entry - value * calcTickSize
                                : entry + value * calcTickSize;
                        }
                        if (derived <= 0)
                        {
                            state.PendingAutoStopUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][STOP] Skip SetStopLoss for {0}: derived price <= 0 (entry={1:F2} ticks={2} tickSize={3})", tradeId, entry, value, calcTickSize));
                            return false;
                        }
                        if (derived > 0)
                            state.PendingAutoStopPrice = derived;
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
                    if (mode == CalculationMode.Price)
                        state.PendingAutoStopPrice = targetValue;
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
            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return false;
            if (IsManualProtectionHoldActive(state, false))
            {
                if (Debug)
                    StrategyLogDebug($"[AUTO][TARGET] Skipping auto target update for {tradeId} due to manual adjustment.");
                return false;
            }

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double desiredValue = value;
            double desiredPrice = 0;
            if (mode == CalculationMode.Price)
            {
                desiredValue = Instrument?.MasterInstrument?.RoundToTickSize(value) ?? Math.Round(value / tickSize) * tickSize;
                desiredPrice = desiredValue;
            }
            else if (mode == CalculationMode.Ticks)
            {
                double entry = state.EntryPrice;
                if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                    entry = Position.AveragePrice;
                if (entry > 0 && !double.IsNaN(entry))
                    desiredPrice = state.EntrySide == MarketPosition.Long
                        ? entry + value * tickSize
                        : entry - value * tickSize;
                if (desiredPrice > 0)
                    desiredPrice = Instrument?.MasterInstrument?.RoundToTickSize(desiredPrice) ?? Math.Round(desiredPrice / tickSize) * tickSize;
            }

            if (state.PendingAutoTargetUpdate && state.PendingAutoTargetPrice > 0 && desiredPrice > 0 && PricesClose(state.PendingAutoTargetPrice, desiredPrice))
            {
                targetSet = true;
                return true;
            }

            if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState))
            {
                double workingPrice = state.TargetOrder.LimitPrice > 0 ? state.TargetOrder.LimitPrice : state.LastTargetPrice;
                if (workingPrice > 0 && desiredPrice > 0 && PricesClose(workingPrice, desiredPrice))
                {
                    targetSet = true;
                    state.PendingAutoTargetUpdate = false;
                    state.PendingAutoTargetPrice = 0;
                    return true;
                }

                if ((state.TargetOrder.OrderState == OrderState.ChangePending || state.TargetOrder.OrderState == OrderState.ChangeSubmitted) &&
                    state.PendingAutoTargetPrice > 0 &&
                    desiredPrice > 0 &&
                    PricesClose(state.PendingAutoTargetPrice, desiredPrice))
                {
                    targetSet = true;
                    return true;
                }
            }

            state.PendingAutoTargetUpdate = true;
            double pendingTargetPrice = 0;
            if (mode == CalculationMode.Price)
            {
                pendingTargetPrice = desiredValue;
            }
            else if (mode == CalculationMode.Ticks)
            {
                if (tickSize > 0)
                {
                    double entry = state.EntryPrice;
                    if ((entry <= 0 || double.IsNaN(entry)) && Position != null)
                        entry = Position.AveragePrice;
                    if (entry > 0 && !double.IsNaN(entry))
                        pendingTargetPrice = state.EntrySide == MarketPosition.Long
                            ? entry + value * tickSize
                            : entry - value * tickSize;
                }
            }
            if (pendingTargetPrice > 0)
                pendingTargetPrice = Instrument?.MasterInstrument?.RoundToTickSize(pendingTargetPrice) ?? Math.Round(pendingTargetPrice / tickSize) * tickSize;
            state.PendingAutoTargetPrice = pendingTargetPrice;
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
                        double calcTickSize = tickSize <= 0 ? 1.0 : tickSize;
                        if (entry > 0 && calcTickSize > 0)
                        {
                            targetPrice = state.EntrySide == MarketPosition.Long
                                ? entry + value * calcTickSize
                                : entry - value * calcTickSize;
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
                        double calcTickSize = tickSize <= 0 ? 1.0 : tickSize;
                        double entry = Position.AveragePrice;
                        double derived = 0;
                        if (entry > 0 && calcTickSize > 0)
                        {
                            derived = state.EntrySide == MarketPosition.Long
                                ? entry + value * calcTickSize
                                : entry - value * calcTickSize;
                        }
                        if (derived <= 0)
                        {
                            state.PendingAutoTargetUpdate = false;
                            StrategyLogInfo(string.Format("[AUTO][TARGET] Skip SetProfitTarget for {0}: derived price <= 0 (entry={1:F2} ticks={2} tickSize={3})", tradeId, entry, value, calcTickSize));
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

        private bool TryApplyDemaAtrTrailingStop(double currentPrice, bool force = false, string context = null)
        {
            if (string.IsNullOrEmpty(activeTradeId))
                return false;

            if (!IsMultiEntrySyncEnabled)
                return TryApplyDemaAtrTrailingStopForState(activeTradeId, currentPrice, force, context);

            MultiEntrySyncGroup group;
            if (!TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group))
                return TryApplyDemaAtrTrailingStopForState(activeTradeId, currentPrice, force, context);

            string logContext = string.IsNullOrWhiteSpace(context) ? string.Empty : $"[{context}] ";
            var states = GetMultiEntrySyncStates(group.TradeId);
            int attempted = 0;
            int updated = 0;
            bool appliedAny = false;
            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;

                attempted++;
                if (TryApplyDemaAtrTrailingStopForState(state.TradeId, currentPrice, force, context))
                {
                    appliedAny = true;
                    updated++;
                }
            }

            if (Debug)
                StrategyLogDebug($"[DEMA-ATR]{logContext}Group {group.TradeId} stop updates applied for {updated}/{attempted} trade(s).");

            return appliedAny;
        }

        private bool TryApplyDemaAtrTrailingStopForState(string tradeId, double currentPrice, bool force = false, string context = null)
        {
            if ((!UseDemaAtrTrailing && !force) || Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (string.IsNullOrEmpty(tradeId))
                return false;

            string logContext = string.IsNullOrWhiteSpace(context) ? string.Empty : $"[{context}] ";
            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Trade state missing for {tradeId}; skipping trail update.");
                return false;
            }
            if (state.IsSynthetic)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Skipping synthetic trade {tradeId} until live order executes.");
                return false;
            }
            if (IsManualProtectionHoldActive(state, true))
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Manual stop override/hold active for {tradeId}; skipping trail update.");
                return false;
            }
            if (force && !state.ForcedDemaTrailLogged)
            {
                StrategyLogInfo($"[DEMA-ATR] Forcing trailing{(string.IsNullOrWhiteSpace(context) ? string.Empty : " " + context)} for {tradeId} (UseDemaAtrTrailing={UseDemaAtrTrailing}).");
                state.ForcedDemaTrailLogged = true;
            }

            double entryPrice = Position.AveragePrice;
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Invalid entry price ({entryPrice}); skipping trailing.");
                return false;
            }

            bool isLong = state.EntrySide == MarketPosition.Long;
            bool forceBreakEvenClamp = HasContext(context, "CHOP_PROFIT");
            double? breakEvenClamp = TryGetBreakEvenClampPrice(state, entryPrice, currentPrice, isLong, forceBreakEvenClamp);

            double activationPrice = GetActivationProbePrice(currentPrice, demaTrailingActive);
            if (!demaTrailingActive)
            {
                if (EnableDemaAtrOnBreakEvenClamp && breakEvenClamp.HasValue)
                {
                    ActivateDemaAtr(activationPrice);
                    if (Debug)
                        StrategyLogDebug($"[DEMA-ATR]{logContext} Activation forced by break-even clamp at {activationPrice:F2}.");
                }
                else if (!EnsureDemaAtrActivation(entryPrice, activationPrice))
                {
                    if (Debug)
                        StrategyLogDebug($"[DEMA-ATR]{logContext} Activation pending; entry={entryPrice:F2} probe={activationPrice:F2}.");
                    return false;
                }
            }

            UpdateDemaAtrWatermarks(currentPrice);

            int primaryBar = GetPrimaryCurrentBar();
            int availableBars = primaryBar + 1;
            int effectivePeriod = UseTightDemaAtrTrailing
                ? Math.Max(TightDemaAtrMinPeriod, (int)Math.Round(DemaAtrPeriod * TightDemaAtrPeriodScale))
                : DemaAtrPeriod;
            double effectiveMultiplier = UseTightDemaAtrTrailing
                ? Math.Max(0.1, DemaAtrMultiplier * TightDemaAtrMultiplierScale)
                : DemaAtrMultiplier;
            int lookbackFloor = UseTightDemaAtrTrailing ? TightDemaAtrLookbackFloor : 50;
            int lookback = Math.Max(effectivePeriod * 2 + 10, lookbackFloor);
            int barsNeeded = Math.Max(effectivePeriod, lookback);
            if (availableBars < barsNeeded)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Waiting for {barsNeeded} bars (have {availableBars}) before trailing.");
                return false;
            }

            var quotes = BuildQuoteHistory(Math.Min(lookback, availableBars), currentPrice);
            if (quotes.Count < effectivePeriod)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Need {effectivePeriod} quotes but only have {quotes.Count}.");
                return false;
            }

            double? stopPrice = SharedDemaAtrTrailing.CalculateTrailingStop(quotes, effectivePeriod, effectiveMultiplier, isLong, currentPrice);
            if (!stopPrice.HasValue)
            {
                if (Debug)
                    StrategyLogDebug($"[DEMA-ATR]{logContext} Stop calculation returned null; skipping update.");
                return false;
            }

            double rounded = Instrument != null
                ? Instrument.MasterInstrument.RoundToTickSize(stopPrice.Value)
                : stopPrice.Value;

            if (breakEvenClamp.HasValue)
            {
                double prior = rounded;
                rounded = isLong ? Math.Max(rounded, breakEvenClamp.Value) : Math.Min(rounded, breakEvenClamp.Value);
                if (Debug && Math.Abs(rounded - prior) > 1e-9)
                    StrategyLogDebug(string.Format("[BE]{0} Clamped trailing stop from {1:F2} to {2:F2} (entry={3:F2}).", logContext, prior, rounded, entryPrice));
            }

            double? lastAccepted = state != null ? (state.RunUpLastStopPrice ?? state.LastStopPrice) : (double?)null;
            double? safePrice = ClampStopPrice(rounded, currentPrice, isLong, lastAccepted);
            if (!safePrice.HasValue)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[DEMA-ATR]{0}Skipped stop update because desired price {1:F2} violates market constraints (current={2:F2}).", logContext, rounded, currentPrice));
                return false;
            }

            if (!IssueStopLoss(tradeId, CalculationMode.Price, safePrice.Value, false))
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[DEMA-ATR]{0}Stop update rejected for {1} at {2:F2}.", logContext, tradeId ?? "<unknown>", safePrice.Value));
                return false;
            }

            stopSet = true;
            state.LastStopPrice = safePrice.Value;
            if (Debug)
                StrategyLogDebug(string.Format("[DEMA-ATR]{0}Applied trailing stop @ {1:F2} (isLong={2})", logContext, rounded, isLong));
            return true;
        }

        private double? TryGetBreakEvenClampPrice(TradeRuntimeState state, double entryPrice, double currentPrice, bool isLong, bool forceClamp = false)
        {
            if (!UseBreakEvenClamp || state == null)
                return null;

            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = Math.Max(Math.Abs(entryPrice) * 1e-6, 1e-6);

            bool triggered = state.BreakEvenActivated;
            if (forceClamp && !triggered)
            {
                double pnl = CalculateSignedUnrealizedPnlAtPrice(currentPrice);
                if (pnl > 0)
                {
                    triggered = true;
                    state.BreakEvenActivated = true;
                }
                else
                {
                    return null;
                }
            }
            if (!triggered)
            {
                if (BreakEvenTriggerMode == BreakEvenTriggerModeOption.Ticks)
                {
                    int triggerTicks = Math.Max(0, BreakEvenTriggerTicks);
                    if (triggerTicks <= 0)
                        return null;
                    double triggerPrice = isLong
                        ? entryPrice + triggerTicks * tickSize
                        : entryPrice - triggerTicks * tickSize;
                    if ((isLong && currentPrice >= triggerPrice) || (!isLong && currentPrice <= triggerPrice))
                        triggered = true;
                }
                else if (BreakEvenTriggerMode == BreakEvenTriggerModeOption.Dollars)
                {
                    double triggerDollars = Math.Max(0.0, BreakEvenTriggerDollars);
                    if (triggerDollars <= 0)
                        return null;
                    double pnl = CalculateSignedUnrealizedPnlAtPrice(currentPrice);
                    if (pnl >= triggerDollars)
                        triggered = true;
                }

                if (triggered)
                {
                    state.BreakEvenActivated = true;
                    if (Debug)
                        StrategyLogDebug(string.Format("[BE] Triggered for {0} at price {1:F2} entry={2:F2} mode={3}.",
                            state.TradeId ?? "<unknown>", currentPrice, entryPrice, BreakEvenTriggerMode));
                }
            }

            if (!state.BreakEvenActivated)
                return null;

            int plusTicks = Math.Max(0, BreakEvenPlusTicks);
            double breakEvenPrice = entryPrice + (isLong ? plusTicks : -plusTicks) * tickSize;
            if (breakEvenPrice <= 0 || double.IsNaN(breakEvenPrice))
                return null;

            return Instrument != null ? Instrument.MasterInstrument.RoundToTickSize(breakEvenPrice) : breakEvenPrice;
        }

        private bool TryApplyBreakEvenStop(string tradeId, double currentPrice, bool forceBreakEvenClamp)
        {
            if (!UseBreakEvenClamp || string.IsNullOrEmpty(tradeId))
                return false;

            if (!IsMultiEntrySyncEnabled)
                return TryApplyBreakEvenStopForState(tradeId, currentPrice, forceBreakEvenClamp);

            MultiEntrySyncGroup group;
            if (!TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
                return TryApplyBreakEvenStopForState(tradeId, currentPrice, forceBreakEvenClamp);

            var states = GetMultiEntrySyncStates(group.TradeId);
            bool appliedAny = false;
            int updated = 0;
            int attempted = 0;
            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;

                attempted++;
                if (TryApplyBreakEvenStopForState(state.TradeId, currentPrice, forceBreakEvenClamp))
                {
                    appliedAny = true;
                    updated++;
                }
            }

            if (Debug)
                StrategyLogDebug($"[BE] Group {group.TradeId} stop updates applied for {updated}/{attempted} trade(s).");

            return appliedAny;
        }

        private bool TryApplyBreakEvenStopForState(string tradeId, double currentPrice, bool forceBreakEvenClamp)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return false;
            if (state.IsSynthetic)
                return false;
            if (IsManualProtectionHoldActive(state, true))
                return false;

            double entryPrice = Position.AveragePrice;
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                return false;

            bool isLong = state.EntrySide == MarketPosition.Long;
            double? breakEvenPrice = TryGetBreakEvenClampPrice(state, entryPrice, currentPrice, isLong, forceBreakEvenClamp);
            if (!breakEvenPrice.HasValue)
                return false;

            double? lastAccepted = state.RunUpLastStopPrice ?? state.LastStopPrice;
            double? safePrice = ClampStopPrice(breakEvenPrice.Value, currentPrice, isLong, lastAccepted);
            if (!safePrice.HasValue)
                return false;

            if (lastAccepted.HasValue && PricesClose(lastAccepted.Value, safePrice.Value))
                return false;

            if (!IssueStopLoss(tradeId, CalculationMode.Price, safePrice.Value, false))
                return false;

            stopSet = true;
            state.LastStopPrice = safePrice.Value;
            if (Debug)
                StrategyLogDebug(string.Format("[BE] Applied break-even stop @ {0:F2} for {1}.", safePrice.Value, tradeId ?? "<unknown>"));
            return true;
        }


        private List<Quote> BuildQuoteHistory(int maxBars, double currentPrice)
        {
            var quotes = new List<Quote>();
            if (maxBars <= 0 || Instrument == null)
                return quotes;

            int primaryCount = BarsArray.Length > 0 ? BarsArray[0].Count : 0;
            int count = Math.Min(maxBars, primaryCount);
            for (int barsAgo = count - 1; barsAgo >= 0; barsAgo--)
            {
                double open = Opens[0][barsAgo];
                double high = Highs[0][barsAgo];
                double low = Lows[0][barsAgo];
                double close = Closes[0][barsAgo];
                if (barsAgo == 0 && currentPrice > 0 && !double.IsNaN(currentPrice))
                {
                    high = Math.Max(high, currentPrice);
                    low = Math.Min(low, currentPrice);
                    close = currentPrice;
                }
                quotes.Add(new Quote
                {
                    Date = Times[0][barsAgo],
                    Open = open,
                    High = high,
                    Low = low,
                    Close = close,
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
                if (currentPrice > barHigh)
                    barHigh = currentPrice;
                return Math.Max(currentPrice, barHigh);
            }

            if (Position.MarketPosition == MarketPosition.Short)
            {
                double barLow = Lows[0].Count > 0 ? Lows[0][0] : currentPrice;
                if (currentPrice < barLow)
                    barLow = currentPrice;
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

        private double CalculateSignedUnrealizedPnlAtPrice(double price)
        {
            if (Position == null || Position.Quantity == 0)
                return 0;

            try
            {
                return Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, price);
            }
            catch
            {
                double entry = Position.AveragePrice;
                if (entry <= 0 || double.IsNaN(entry))
                    return 0;
                double direction = Position.MarketPosition == MarketPosition.Long ? 1.0 : -1.0;
                double pointValue = Instrument?.MasterInstrument?.PointValue ?? 1.0;
                double diff = (price - entry) * direction;
                return diff * pointValue;
            }
        }

        private double GetAccountCashValue()
        {
            try
            {
                var resolved = ResolveCanonicalAccount(Account);
                var account = resolved ?? Account;
                if (account == null)
                    return 0.0;
                return account.GetAccountItem(AccountItem.CashValue, Currency.UsDollar)?.Value ?? 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private bool IsChopAddOnProfitSatisfied(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            if (ChopAddOnProfitMode == ChopAddOnProfitModeOption.Dollars)
            {
                double target = Math.Max(0, ChopAddOnProfitDollars);
                if (target <= 0)
                    return true;
                double pnl = CalculateSignedUnrealizedPnlAtPrice(currentPrice);
                return pnl >= target;
            }

            double ticksTarget = Math.Max(0, ChopAddOnProfitTicks);
            if (ticksTarget <= 0)
                return true;

            double entry = Position.AveragePrice;
            if (entry <= 0 || double.IsNaN(entry))
                return false;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return false;

            double direction = Position.MarketPosition == MarketPosition.Long ? 1.0 : -1.0;
            double ticks = ((currentPrice - entry) / tickSize) * direction;
            return ticks >= ticksTarget;
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

        private double GetStopClampReference(bool isLong, double fallbackPrice)
        {
            double refPrice = isLong ? GetCurrentBid() : GetCurrentAsk();
            if (refPrice <= 0 || double.IsNaN(refPrice))
                refPrice = fallbackPrice;
            return refPrice;
        }

        private bool IsScaleInConfigured()
        {
            return EnableScaleInTrades && ScaleInTradesToAdd > 0 && ScaleInDrawdownTicks > 0;
        }

        private void TryTriggerScaleIn(double currentPrice)
        {
            if (!IsScaleInConfigured())
            {
                if (scaleInActive || scaleInTriggered)
                    ResetScaleInState();
                return;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
            {
                ResetScaleInState();
                return;
            }

            TradeRuntimeState activeState;
            if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out activeState) && activeState != null)
            {
                if (activeState.IsChopEntry)
                    return;
            }

            if (scaleInSide != MarketPosition.Flat && scaleInSide != Position.MarketPosition)
                ResetScaleInState();

            TradeRuntimeState referenceState = GetScaleInReferenceState();
            double entryPrice = ResolveScaleInInitialEntryPrice(currentPrice, referenceState);
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
                return;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double drawdownTicks = 0;
            if (Position.MarketPosition == MarketPosition.Long)
                drawdownTicks = (entryPrice - currentPrice) / tickSize;
            else if (Position.MarketPosition == MarketPosition.Short)
                drawdownTicks = (currentPrice - entryPrice) / tickSize;

            double? stopGuard = ResolveScaleInStopPrice(referenceState, currentPrice);
            if (stopGuard.HasValue)
            {
                bool isLong = Position.MarketPosition == MarketPosition.Long;
                double refPrice = GetStopClampReference(isLong, currentPrice);
                double buffer = tickSize;
                if (isLong && refPrice <= stopGuard.Value + buffer)
                    return;
                if (!isLong && refPrice >= stopGuard.Value - buffer)
                    return;
            }

            if (drawdownTicks < ScaleInDrawdownTicks)
                return;

            int stepTicks = Math.Max(1, ScaleInDrawdownTicks);
            int steps = (int)Math.Floor(drawdownTicks / stepTicks);
            if (steps <= 0)
                return;

            int targetAdds = steps * ScaleInTradesToAdd;
            int currentAdds = scaleInTradesExecuted + scaleInTradesPending;
            int addsNeeded = targetAdds - currentAdds;
            if (ScaleInMaxTrades > 0)
            {
                int remainingAllowed = ScaleInMaxTrades - currentAdds;
                if (remainingAllowed <= 0)
                    return;
                addsNeeded = Math.Min(addsNeeded, remainingAllowed);
            }
            if (addsNeeded <= 0)
                return;

            if (!scaleInActive)
                ActivateScaleIn(currentPrice);

            StartScaleInHold(currentPrice);

            int qty = Math.Max(1, DefaultQuantity);
            SubmitScaleInEntries(Position.MarketPosition, addsNeeded, qty, false);

            scaleInTriggered = true;
            StrategyLogInfo(string.Format("[SCALE_IN] DCA add {0} at drawdown {1:F1} ticks (step {2}, entry={3:F2})", addsNeeded, drawdownTicks, steps, entryPrice));
        }

        private TradeRuntimeState GetScaleInReferenceState()
        {
            TradeRuntimeState referenceState = null;
            if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out referenceState) && referenceState != null && !referenceState.IsScaleInEntry)
                return referenceState;
            if (tradeStates != null)
                referenceState = tradeStates.Values.FirstOrDefault(s => s != null && !s.IsScaleInEntry && s.RemainingQuantity > 0);
            return referenceState;
        }

        private double ResolveScaleInInitialEntryPrice(double currentPrice)
        {
            return ResolveScaleInInitialEntryPrice(currentPrice, GetScaleInReferenceState());
        }

        private double ResolveScaleInInitialEntryPrice(double currentPrice, TradeRuntimeState referenceState)
        {
            if (scaleInInitialEntryPrice > 0 && !double.IsNaN(scaleInInitialEntryPrice))
                return scaleInInitialEntryPrice;

            double entry = 0;
            if (referenceState != null && referenceState.EntryPrice > 0 && !double.IsNaN(referenceState.EntryPrice))
                entry = referenceState.EntryPrice;

            if ((entry <= 0 || double.IsNaN(entry)) && Position != null && Position.AveragePrice > 0 && !double.IsNaN(Position.AveragePrice))
                entry = Position.AveragePrice;

            if ((entry <= 0 || double.IsNaN(entry)) && currentPrice > 0 && !double.IsNaN(currentPrice))
                entry = currentPrice;

            if (entry > 0 && !double.IsNaN(entry))
                scaleInInitialEntryPrice = entry;

            return entry;
        }

        private void UpdateScaleInDrawdownVisuals(double currentPrice)
        {
            if (!EnableScaleInTrades || ScaleInDrawdownTicks <= 0)
            {
                ClearScaleInDrawdownLines();
                return;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
            {
                ClearScaleInDrawdownLines();
                return;
            }

            double entryPrice = ResolveScaleInInitialEntryPrice(currentPrice);
            if (entryPrice <= 0 || double.IsNaN(entryPrice))
            {
                ClearScaleInDrawdownLines();
                return;
            }

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            int stepTicks = Math.Max(1, ScaleInDrawdownTicks);
            int stopTicks = 0;
            if (StopType == StopKind.ATR && atr != null && atr[0] > 0 && tickSize > 0)
                stopTicks = (int)Math.Max(1, Math.Round((atr[0] * AtrStopMult) / tickSize));
            else
                stopTicks = Math.Max(1, StopTicks);

            int maxSteps = Math.Max(1, stopTicks / stepTicks);
            maxSteps = Math.Min(maxSteps, MaxScaleInDrawdownLines);
            int maxStepsByTrades = 0;
            if (ScaleInMaxTrades > 0)
            {
                maxStepsByTrades = (int)Math.Ceiling(ScaleInMaxTrades / (double)Math.Max(1, ScaleInTradesToAdd));
                maxSteps = Math.Min(maxSteps, Math.Max(1, maxStepsByTrades));
            }

            int placedSteps = 0;
            int currentAdds = scaleInTradesExecuted + scaleInTradesPending;
            if (ScaleInTradesToAdd > 0)
                placedSteps = currentAdds / ScaleInTradesToAdd;
            if (ScaleInMaxTrades > 0 && currentAdds >= ScaleInMaxTrades && maxStepsByTrades > 0)
                placedSteps = Math.Max(placedSteps, maxStepsByTrades);

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            Brush lineBrush = Brushes.Aqua;

            for (int i = 1; i <= MaxScaleInDrawdownLines; i++)
            {
                string tag = ScaleInDrawdownTagPrefix + i;
                if (i <= placedSteps || i > maxSteps)
                {
                    RemoveDrawObject(tag);
                    continue;
                }

                double price = isLong
                    ? entryPrice - (i * stepTicks * tickSize)
                    : entryPrice + (i * stepTicks * tickSize);
                if (price <= 0 || double.IsNaN(price))
                {
                    RemoveDrawObject(tag);
                    continue;
                }

                var line = Draw.HorizontalLine(this, tag, price, lineBrush);
                ApplyScaleInDrawdownLineStyle(line, lineBrush);
            }
        }

        private void ApplyScaleInDrawdownLineStyle(NinjaTrader.NinjaScript.DrawingTools.HorizontalLine line, Brush brush)
        {
            if (line?.Stroke == null)
                return;

            line.Stroke.Brush = brush;
            line.Stroke.Width = 3;

            var stroke = line.Stroke;
            var helperProp = stroke.GetType().GetProperty("DashStyleHelper");
            if (helperProp != null)
            {
                object dashValue = Enum.Parse(helperProp.PropertyType, "Dash");
                helperProp.SetValue(stroke, dashValue, null);
                return;
            }

            var dashProp = stroke.GetType().GetProperty("DashStyle");
            if (dashProp != null && dashProp.PropertyType == typeof(DashStyle))
            {
                var custom = new DashStyle(new double[] { 6, 3 }, 0);
                dashProp.SetValue(stroke, custom, null);
            }
        }

        private void ClearScaleInDrawdownLines()
        {
            for (int i = 1; i <= MaxScaleInDrawdownLines; i++)
                RemoveDrawObject(ScaleInDrawdownTagPrefix + i);
        }

        private void ActivateScaleIn(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return;

            scaleInActive = true;
            scaleInSide = Position.MarketPosition;
            scaleInHighWater = currentPrice;
            scaleInLowWater = currentPrice;
            scaleInLockPrice = 0;
            scaleInLastStopPrice = 0;
            scaleInActivationPrice = 0;
            scaleInTrailActivated = false;
            if (scaleInInitialEntryPrice <= 0 || double.IsNaN(scaleInInitialEntryPrice))
                scaleInInitialEntryPrice = ResolveScaleInInitialEntryPrice(currentPrice);
        }

        private double ComputeGlobalTrailLockPrice(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return 0;

            double entry = Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return 0;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double offset = 0;
            if (GlobalProfitLockMode == BreakEvenTriggerModeOption.Ticks)
            {
                int ticks = (int)Math.Max(0, Math.Round(GlobalProfitLockValue));
                offset = ticks * tickSize;
            }
            else
            {
                offset = ConvertDollarsToPrice(Math.Max(0.0, GlobalProfitLockValue));
            }

            if (offset <= 0)
                return 0;

            double desired = Position.MarketPosition == MarketPosition.Short
                ? entry - offset
                : entry + offset;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private double ComputeGlobalTrailActivationPrice(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return 0;

            double entry = Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return 0;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double offset = 0;
            if (GlobalTrailActivationMode == BreakEvenTriggerModeOption.Ticks)
            {
                int ticks = (int)Math.Max(0, Math.Round(GlobalTrailActivationValue));
                if (ticks <= 0)
                    return entry;
                offset = ticks * tickSize;
            }
            else
            {
                double dollars = Math.Max(0.0, GlobalTrailActivationValue);
                if (dollars <= 0)
                    return entry;
                offset = ConvertDollarsToPrice(dollars);
                if (offset <= 0)
                    return 0;
            }

            if (offset <= 0)
                return entry;

            double desired = Position.MarketPosition == MarketPosition.Short
                ? entry - offset
                : entry + offset;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private bool ApplyGlobalTrailing(TradeRuntimeState activeState, double currentPrice, bool manualStopLocked)
        {
            if (!EnableGlobalTrailing)
            {
                ResetGlobalTrailingState();
                return false;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
            {
                ResetGlobalTrailingState();
                return false;
            }

            if (globalTrailSide != Position.MarketPosition)
            {
                ResetGlobalTrailingState();
                globalTrailSide = Position.MarketPosition;
            }
            else if (globalTrailSide == MarketPosition.Flat)
            {
                globalTrailSide = Position.MarketPosition;
            }

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            List<TradeRuntimeState> states = null;
            MultiEntrySyncGroup group;
            if (IsMultiEntrySyncEnabled && TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group) && group != null)
            {
                states = GetMultiEntrySyncStates(group.TradeId);
            }
            else if (openTradeOrder != null && openTradeOrder.Count > 0)
            {
                states = new List<TradeRuntimeState>();
                foreach (string tradeId in openTradeOrder)
                {
                    TradeRuntimeState state;
                    if (TryGetTradeState(tradeId, out state) && state != null && state.RemainingQuantity > 0)
                        states.Add(state);
                }
            }
            else if (activeState != null)
            {
                states = new List<TradeRuntimeState> { activeState };
            }

            if (states == null || states.Count == 0)
                return false;

            states = states.Where(s => s != null && s.RemainingQuantity > 0 && !s.IsSynthetic).ToList();
            if (states.Count == 0)
                return false;

            bool hasManualOverride = states.Any(s => s.ManualStopOverride);
            bool activationReached = false;
            if (manualStopLocked && !globalTrailActivated)
            {
                if (!hasManualOverride)
                    return false;

                double activationPrice = ComputeGlobalTrailActivationPrice(currentPrice);
                if (activationPrice > 0)
                    activationReached = isLong ? currentPrice >= activationPrice : currentPrice <= activationPrice;
                if (!activationReached)
                    return false;
            }

            if (manualStopLocked && (globalTrailActivated || activationReached))
            {
                foreach (var state in states)
                {
                    if (state == null)
                        continue;
                    state.ManualStopOverride = false;
                    ClearManualProtectionPending(state, true);
                }
                manualStopLocked = false;
            }

            double? lastAccepted = globalTrailLastStopPrice > 0 ? (double?)globalTrailLastStopPrice : null;
            foreach (var state in states)
            {
                double? stopPrice = null;
                if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState) && state.StopOrder.StopPrice > 0)
                    stopPrice = state.StopOrder.StopPrice;
                else if (state.LastStopPrice > 0)
                    stopPrice = state.LastStopPrice;

                if (stopPrice.HasValue && stopPrice.Value > 0)
                {
                    if (!lastAccepted.HasValue)
                        lastAccepted = stopPrice.Value;
                    else
                        lastAccepted = isLong ? Math.Max(lastAccepted.Value, stopPrice.Value) : Math.Min(lastAccepted.Value, stopPrice.Value);
                }
            }

            if (!globalTrailActivated)
            {
                double activationPrice = ComputeGlobalTrailActivationPrice(currentPrice);
                if (activationPrice > 0)
                {
                    bool activationReachedNow = isLong ? currentPrice >= activationPrice : currentPrice <= activationPrice;
                    if (activationReachedNow)
                    {
                        double lockPrice = ComputeGlobalTrailLockPrice(currentPrice);
                        if (lockPrice > 0)
                        {
                            globalTrailActivated = true;
                            globalTrailActivationPrice = activationPrice;
                            globalTrailLockPrice = lockPrice;
                        }
                    }
                }
            }

            if (!globalTrailActivated)
                return false;

            if (globalTrailActivationPrice <= 0)
                globalTrailActivationPrice = ComputeGlobalTrailActivationPrice(currentPrice);
            if (globalTrailLockPrice <= 0)
                globalTrailLockPrice = ComputeGlobalTrailLockPrice(currentPrice);

            if (globalTrailLockPrice <= 0)
                return false;

            double desiredStop = globalTrailLockPrice;

            if (GlobalTrailIncrementMode == BreakEvenTriggerModeOption.Ticks)
            {
                int trailTicks = (int)Math.Max(0, Math.Round(GlobalTrailIncrementValue));
                if (trailTicks > 0 && globalTrailActivationPrice > 0)
                {
                    double favorableTicks = isLong
                        ? (currentPrice - globalTrailActivationPrice) / tickSize
                        : (globalTrailActivationPrice - currentPrice) / tickSize;
                    if (favorableTicks > 0)
                    {
                        int steps = (int)Math.Floor(favorableTicks / trailTicks);
                        if (steps > 0)
                        {
                            double offset = steps * trailTicks * tickSize;
                            desiredStop = isLong ? desiredStop + offset : desiredStop - offset;
                        }
                    }
                }
            }
            else
            {
                double trailDollars = Math.Max(0.0, GlobalTrailIncrementValue);
                if (trailDollars > 0 && globalTrailActivationPrice > 0)
                {
                    double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0.0;
                    int qty = Position != null ? Math.Max(1, Math.Abs(Position.Quantity)) : 1;
                    if (pointValue > 0 && qty > 0)
                    {
                        double favorableMove = isLong
                            ? currentPrice - globalTrailActivationPrice
                            : globalTrailActivationPrice - currentPrice;
                        if (favorableMove > 0)
                        {
                            double favorableDollars = favorableMove * pointValue * qty;
                            int steps = (int)Math.Floor(favorableDollars / trailDollars);
                            if (steps > 0)
                            {
                                double offset = steps * trailDollars / (pointValue * qty);
                                desiredStop = isLong ? desiredStop + offset : desiredStop - offset;
                            }
                        }
                    }
                }
            }

            if (globalTrailLastStopPrice > 0)
                desiredStop = isLong ? Math.Max(desiredStop, globalTrailLastStopPrice) : Math.Min(desiredStop, globalTrailLastStopPrice);
            if (globalTrailLockPrice > 0)
                desiredStop = isLong ? Math.Max(desiredStop, globalTrailLockPrice) : Math.Min(desiredStop, globalTrailLockPrice);

            double rounded = Instrument?.MasterInstrument?.RoundToTickSize(desiredStop) ?? Math.Round(desiredStop / tickSize) * tickSize;
            double clampRef = GetStopClampReference(isLong, currentPrice);
            double? clamped = ClampStopPrice(rounded, clampRef, isLong, lastAccepted);
            if (!clamped.HasValue)
                return false;

            bool updated = false;
            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;
                if (IsManualProtectionHoldActive(state, true))
                    continue;

                if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                {
                    state.LastStopPrice = clamped.Value;
                    updated = true;
                }
            }

            if (updated)
            {
                globalTrailLastStopPrice = clamped.Value;
                stopSet = true;
            }

            return updated;
        }

        private double ComputeScaleInLockPrice(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return 0;

            double entry = Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return 0;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double offset = 0;
            if (ScaleInProfitLockMode == BreakEvenTriggerModeOption.Ticks)
            {
                int ticks = (int)Math.Max(0, Math.Round(ScaleInProfitLockValue));
                offset = ticks * tickSize;
            }
            else
            {
                offset = ConvertDollarsToPrice(Math.Max(0.0, ScaleInProfitLockValue));
            }

            if (offset <= 0)
                return 0;

            double desired = Position.MarketPosition == MarketPosition.Short
                ? entry - offset
                : entry + offset;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private double ComputeScaleInActivationPrice(double currentPrice)
        {
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return 0;

            double entry = Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return 0;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double offset = 0;
            if (ScaleInTrailActivationMode == BreakEvenTriggerModeOption.Ticks)
            {
                int ticks = (int)Math.Max(0, Math.Round(ScaleInTrailActivationValue));
                if (ticks <= 0)
                    return entry;
                offset = ticks * tickSize;
            }
            else
            {
                double dollars = Math.Max(0.0, ScaleInTrailActivationValue);
                if (dollars <= 0)
                    return entry;
                offset = ConvertDollarsToPrice(dollars);
                if (offset <= 0)
                    return 0;
            }

            if (offset <= 0)
                return entry;

            double desired = Position.MarketPosition == MarketPosition.Short
                ? entry - offset
                : entry + offset;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private double ConvertDollarsToPrice(double dollars)
        {
            if (dollars <= 0)
                return 0;

            double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0.0;
            if (pointValue <= 0)
                return 0;

            int qty = Position != null ? Math.Max(1, Math.Abs(Position.Quantity)) : 1;
            if (qty <= 0)
                return 0;

            return dollars / (pointValue * qty);
        }

        private void StartScaleInHold(double currentPrice)
        {
            DateTime now = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
            if (scaleInHoldUntil != DateTime.MinValue && now < scaleInHoldUntil)
                return;

            scaleInHoldUntil = now.AddSeconds(ScaleInHoldSeconds);
            scaleInTrailActivated = false;
            scaleInActivationPrice = 0;
            scaleInLockPrice = 0;

            if (!scaleInActive)
                ActivateScaleIn(currentPrice);

            ResetScaleInManualProtection();
        }

        private void ResetScaleInManualProtection()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && state.EntrySide != Position.MarketPosition)
                    continue;
                if (state.IsChopEntry)
                    continue;

                if (state.ManualStopOverride || state.ManualStopPending || state.ManualTargetOverride || state.ManualTargetPending)
                {
                    state.ManualStopOverride = false;
                    state.ManualStopPending = false;
                    state.ManualStopPendingUntil = DateTime.MinValue;
                    state.ManualTargetOverride = false;
                    state.ManualTargetPending = false;
                    state.ManualTargetPendingUntil = DateTime.MinValue;
                }
            }
        }

        private void ApplyScaleInTrailing(double currentPrice, bool manualStopLocked, bool manualTargetLocked)
        {
            if (!EnableScaleInTrades)
                return;

            if (!EnableScaleInTrailing)
            {
                scaleInTrailActivated = false;
                scaleInActivationPrice = 0;
                scaleInLockPrice = 0;
                scaleInLastStopPrice = 0;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
            {
                ResetScaleInState();
                return;
            }

            if (scaleInSide != MarketPosition.Flat && scaleInSide != Position.MarketPosition)
            {
                ResetScaleInState();
                return;
            }

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            if (!scaleInActive && (scaleInTradesExecuted + scaleInTradesPending) > 0)
                scaleInActive = true;

            List<TradeRuntimeState> states = GetScaleInManagedStates();
            if (states.Count == 0)
                return;

            if (scaleInHoldUntil != DateTime.MinValue)
            {
                manualStopLocked = false;
                manualTargetLocked = false;
            }

            double? lastAccepted = scaleInLastStopPrice > 0 ? (double?)scaleInLastStopPrice : null;
            foreach (var state in states)
            {
                if (state == null)
                    continue;
                double? stopPrice = null;
                if (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState) && state.StopOrder.StopPrice > 0)
                    stopPrice = state.StopOrder.StopPrice;
                else if (state.LastStopPrice > 0)
                    stopPrice = state.LastStopPrice;

                if (stopPrice.HasValue && stopPrice.Value > 0)
                {
                    if (!lastAccepted.HasValue)
                        lastAccepted = stopPrice.Value;
                    else
                        lastAccepted = isLong ? Math.Max(lastAccepted.Value, stopPrice.Value) : Math.Min(lastAccepted.Value, stopPrice.Value);
                }
            }

            if (EnableScaleInTrailing && !scaleInTrailActivated)
            {
                double activationPrice = ComputeScaleInActivationPrice(currentPrice);
                if (activationPrice > 0)
                {
                    bool activationReached = isLong ? currentPrice >= activationPrice : currentPrice <= activationPrice;
                    if (activationReached)
                    {
                        double lockPrice = ComputeScaleInLockPrice(currentPrice);
                        if (lockPrice > 0)
                        {
                            scaleInTrailActivated = true;
                            scaleInActivationPrice = activationPrice;
                            scaleInLockPrice = lockPrice;
                            scaleInActive = true;
                        }
                    }
                }
            }

            double desiredStop = 0;
            bool shouldUpdateStop = false;
            bool trailingActive = EnableScaleInTrailing && scaleInTrailActivated;

            if (trailingActive)
            {
                if (scaleInActivationPrice <= 0)
                    scaleInActivationPrice = ComputeScaleInActivationPrice(currentPrice);
                if (scaleInLockPrice <= 0)
                    scaleInLockPrice = ComputeScaleInLockPrice(currentPrice);

                if (scaleInLockPrice > 0)
                {
                    desiredStop = scaleInLockPrice;

                    if (ScaleInTrailIncrementMode == BreakEvenTriggerModeOption.Ticks)
                    {
                        int trailTicks = (int)Math.Max(0, Math.Round(ScaleInTrailIncrementValue));
                        if (trailTicks > 0 && scaleInActivationPrice > 0)
                        {
                            double favorableTicks = isLong
                                ? (currentPrice - scaleInActivationPrice) / tickSize
                                : (scaleInActivationPrice - currentPrice) / tickSize;
                            if (favorableTicks > 0)
                            {
                                int steps = (int)Math.Floor(favorableTicks / trailTicks);
                                if (steps > 0)
                                {
                                    double offset = steps * trailTicks * tickSize;
                                    desiredStop = isLong ? desiredStop + offset : desiredStop - offset;
                                }
                            }
                        }
                    }
                    else
                    {
                        double trailDollars = Math.Max(0.0, ScaleInTrailIncrementValue);
                        if (trailDollars > 0 && scaleInActivationPrice > 0)
                        {
                            double pointValue = Instrument?.MasterInstrument?.PointValue ?? 0.0;
                            int qty = Position != null ? Math.Max(1, Math.Abs(Position.Quantity)) : 1;
                            if (pointValue > 0 && qty > 0)
                            {
                                double favorableMove = isLong
                                    ? currentPrice - scaleInActivationPrice
                                    : scaleInActivationPrice - currentPrice;
                                if (favorableMove > 0)
                                {
                                    double favorableDollars = favorableMove * pointValue * qty;
                                    int steps = (int)Math.Floor(favorableDollars / trailDollars);
                                    if (steps > 0)
                                    {
                                        double offset = steps * trailDollars / (pointValue * qty);
                                        desiredStop = isLong ? desiredStop + offset : desiredStop - offset;
                                    }
                                }
                            }
                        }
                    }

                    if (scaleInLastStopPrice > 0)
                        desiredStop = isLong ? Math.Max(desiredStop, scaleInLastStopPrice) : Math.Min(desiredStop, scaleInLastStopPrice);
                    if (scaleInLockPrice > 0)
                        desiredStop = isLong ? Math.Max(desiredStop, scaleInLockPrice) : Math.Min(desiredStop, scaleInLockPrice);

                    shouldUpdateStop = desiredStop > 0;
                }
            }
            else if (scaleInActive)
            {
                TradeRuntimeState referenceState = null;
                if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out referenceState) && referenceState != null && !referenceState.IsScaleInEntry)
                {
                    // use active state
                }
                else if (tradeStates != null)
                {
                    referenceState = tradeStates.Values.FirstOrDefault(s => s != null && !s.IsScaleInEntry && s.RemainingQuantity > 0);
                }

                if (referenceState == null)
                    referenceState = states[0];

                double? referenceStop = ResolveScaleInStopPrice(referenceState, currentPrice);
                if (referenceStop.HasValue && referenceStop.Value > 0)
                {
                    desiredStop = referenceStop.Value;
                    shouldUpdateStop = true;
                }
            }

            if (shouldUpdateStop && desiredStop > 0)
            {
                double rounded = Instrument?.MasterInstrument?.RoundToTickSize(desiredStop) ?? Math.Round(desiredStop / tickSize) * tickSize;
                double clampRef = GetStopClampReference(isLong, currentPrice);
                double? clamped = ClampStopPrice(rounded, clampRef, isLong, lastAccepted);
                if (clamped.HasValue && !manualStopLocked)
                {
                    foreach (var state in states)
                    {
                        if (IsManualProtectionHoldActive(state, true))
                            continue;
                        if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                        {
                            state.LastStopPrice = clamped.Value;
                            stopSet = true;
                        }
                    }
                    if (trailingActive)
                        scaleInLastStopPrice = clamped.Value;
                }
            }

            if (!manualTargetLocked)
            {
                double? targetPrice = ResolveScaleInTargetPrice(states, currentPrice);
                if (targetPrice.HasValue)
                {
                    foreach (var state in states)
                    {
                        if (IsManualProtectionHoldActive(state, false))
                            continue;
                        if (IssueProfitTarget(state.TradeId, CalculationMode.Price, targetPrice.Value))
                        {
                            state.LastTargetPrice = targetPrice.Value;
                            targetSet = true;
                        }
                    }
                }
            }
        }

        private List<TradeRuntimeState> GetScaleInManagedStates()
        {
            var states = new List<TradeRuntimeState>();
            if (tradeStates == null || tradeStates.Count == 0)
                return states;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;
                if (Position != null && Position.MarketPosition != MarketPosition.Flat && state.EntrySide != Position.MarketPosition)
                    continue;
                if (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))
                    continue;
                if (state.IsChopEntry)
                    continue;
                states.Add(state);
            }

            return states;
        }

        private double? ResolveScaleInTargetPrice(List<TradeRuntimeState> states, double currentPrice)
        {
            TradeRuntimeState activeState;
            if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out activeState) && activeState != null)
            {
                double activeTarget = activeState.LastTargetPrice;
                if (activeTarget <= 0 && activeState.TargetOrder != null && activeState.TargetOrder.LimitPrice > 0)
                    activeTarget = activeState.TargetOrder.LimitPrice;
                if (activeTarget > 0)
                    return activeTarget;
            }

            if (states != null)
            {
                foreach (var state in states)
                {
                    if (state.LastTargetPrice > 0)
                        return state.LastTargetPrice;
                    if (state.TargetOrder != null && state.TargetOrder.LimitPrice > 0)
                        return state.TargetOrder.LimitPrice;
                }
            }

            double entry = Position != null && Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return null;

            int targetTicks = TargetType == TargetKind.ATR && atr != null && atr[0] > 0 && tickSize > 0
                ? (int)Math.Max(1, Math.Round((atr[0] * AtrTargetMult) / tickSize))
                : Math.Max(1, TargetTicks);

            double desired = Position.MarketPosition == MarketPosition.Long
                ? entry + targetTicks * tickSize
                : entry - targetTicks * tickSize;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private bool IsScaleInExpectedStop(double price)
        {
            if (!scaleInActive || price <= 0)
                return false;
            if (!EnableScaleInTrailing)
                return false;

            if (scaleInLastStopPrice > 0 && PricesClose(scaleInLastStopPrice, price))
                return true;
            if (scaleInLockPrice > 0 && PricesClose(scaleInLockPrice, price))
                return true;
            return false;
        }

        private bool IsRunUpExpectedStop(TradeRuntimeState state, double price)
        {
            if (state == null || !state.RunUpActive || price <= 0)
                return false;
            if (state.RunUpLastStopPrice.HasValue && PricesClose(state.RunUpLastStopPrice.Value, price))
                return true;
            if (state.PendingAutoStopPrice > 0 && PricesClose(state.PendingAutoStopPrice, price))
                return true;
            return false;
        }

        private double? ResolveScaleInStopPrice(TradeRuntimeState referenceState, double currentPrice)
        {
            if (EnableScaleInTrailing && scaleInActive && scaleInLastStopPrice > 0)
                return scaleInLastStopPrice;

            if (referenceState != null)
            {
                if (referenceState.LastStopPrice > 0)
                    return referenceState.LastStopPrice;
                if (referenceState.StopOrder != null && referenceState.StopOrder.StopPrice > 0)
                    return referenceState.StopOrder.StopPrice;
            }

            double entry = 0;
            if (referenceState != null && referenceState.EntryPrice > 0 && !double.IsNaN(referenceState.EntryPrice))
                entry = referenceState.EntryPrice;
            if ((entry <= 0 || double.IsNaN(entry)) && scaleInInitialEntryPrice > 0 && !double.IsNaN(scaleInInitialEntryPrice))
                entry = scaleInInitialEntryPrice;
            if ((entry <= 0 || double.IsNaN(entry)) && Position != null && Position.AveragePrice > 0 && !double.IsNaN(Position.AveragePrice))
                entry = Position.AveragePrice;
            if ((entry <= 0 || double.IsNaN(entry)) && currentPrice > 0 && !double.IsNaN(currentPrice))
                entry = currentPrice;
            if (entry <= 0 || double.IsNaN(entry))
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return null;

            int stopTicks = StopType == StopKind.ATR && atr != null && atr[0] > 0 && tickSize > 0
                ? (int)Math.Max(1, Math.Round((atr[0] * AtrStopMult) / tickSize))
                : Math.Max(1, StopTicks);

            double desired = Position.MarketPosition == MarketPosition.Long
                ? entry - stopTicks * tickSize
                : entry + stopTicks * tickSize;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? Math.Round(desired / tickSize) * tickSize;
        }

        private void SyncScaleInProtectionForEntry(TradeRuntimeState state, double currentPrice)
        {
            if (state == null || state.RemainingQuantity <= 0)
                return;

            TradeRuntimeState referenceState = null;
            if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out referenceState) && referenceState != null && !referenceState.IsScaleInEntry)
            {
                // use active state
            }
            else if (tradeStates != null)
            {
                referenceState = tradeStates.Values.FirstOrDefault(s => s != null && !s.IsScaleInEntry && s.RemainingQuantity > 0);
            }

            if (referenceState == null)
                referenceState = state;

            EnsureProtectionForActiveTrade(referenceState, currentPrice);

            double? stopPrice = ResolveScaleInStopPrice(referenceState, currentPrice);
            if (stopPrice.HasValue && !IsManualProtectionHoldActive(state, true))
            {
                if (IssueStopLoss(state.TradeId, CalculationMode.Price, stopPrice.Value, false))
                    state.LastStopPrice = stopPrice.Value;
            }

            double? targetPrice = ResolveScaleInTargetPrice(GetScaleInManagedStates(), currentPrice);
            if (targetPrice.HasValue && !IsManualProtectionHoldActive(state, false))
            {
                if (IssueProfitTarget(state.TradeId, CalculationMode.Price, targetPrice.Value))
                    state.LastTargetPrice = targetPrice.Value;
            }
        }

        private void SubmitScaleInEntries(MarketPosition side, int entriesToSubmit, int quantityPerEntry, bool isManual)
        {
            if (entriesToSubmit <= 0 || quantityPerEntry <= 0)
                return;

            for (int i = 0; i < entriesToSubmit; i++)
            {
                string tradeId = CreateTradeId(side);
                var state = PrepareTradeState(tradeId, side, quantityPerEntry, preserveProtectionOrders: true, setActiveTrade: false, isScaleIn: true);
                state.IsScaleInEntry = true;
                state.EntryContext = isManual ? "SCALEIN_MANUAL" : "SCALEIN_AUTO";
                state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                state.EntryOrderPending = true;

                scaleInTradesPending++;
                string context = isManual ? "manual" : "auto";
                StrategyLogInfo(string.Format("[SCALE_IN] Submit {0} {1} {2} qty={3} ({4}/{5})", context, side, tradeId, quantityPerEntry, i + 1, entriesToSubmit));
                if (side == MarketPosition.Long)
                    EnterLong(quantityPerEntry, tradeId);
                else if (side == MarketPosition.Short)
                    EnterShort(quantityPerEntry, tradeId);
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
            if (IsManualProtectionHoldActive(state, true))
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

        private bool TryHandleVwapFailureFlip(TradeRuntimeState state, double currentPrice)
        {
            if (state == null || !state.IsVwapEntry || state.VwapIsFlip || !EnableVwapFailureFlip)
                return false;
            int vwapBarIndex = ResolveVwapBarsIndex();
            int currentVwapBar = (CurrentBars != null && vwapBarIndex < CurrentBars.Length) ? CurrentBars[vwapBarIndex] : CurrentBar;
            if (state.VwapFailureCheckBar < 0 || currentVwapBar < state.VwapFailureCheckBar)
                return false;
            if (state.VwapFailureHigh <= 0 || state.VwapFailureLow <= 0)
            {
                state.VwapFailureCheckBar = -1;
                return false;
            }
            if (!VwapBarsReady(0))
                return false;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double vwapClose = GetVwapClose(0);
            if (double.IsNaN(vwapClose))
                return false;

            bool isShort = state.EntrySide == MarketPosition.Short;
            double tolerance = tickSize * 0.5;
            bool failed = isShort
                ? vwapClose > state.VwapFailureHigh + tolerance
                : vwapClose < state.VwapFailureLow - tolerance;

            state.VwapFailureCheckBar = -1;

            if (!failed)
                return false;
            if (vwapFlipPending)
                return true;

            MarketPosition flipSide = isShort ? MarketPosition.Long : MarketPosition.Short;
            double stopPrice = isShort
                ? GetVwapLow(0) - VwapFlipStopBufferTicks * tickSize
                : GetVwapHigh(0) + VwapFlipStopBufferTicks * tickSize;
            if (double.IsNaN(stopPrice) || stopPrice <= 0)
            {
                stopPrice = isShort
                    ? Low[0] - VwapFlipStopBufferTicks * tickSize
                    : High[0] + VwapFlipStopBufferTicks * tickSize;
            }

            double targetPrice = ResolveVwapFlipTargetPrice(state, flipSide);
            double flipBand = ResolveVwapFlipBandMultiplier(state);

            vwapFlipPending = true;
            vwapFlipSide = flipSide;
            vwapFlipStopPrice = stopPrice;
            vwapFlipTargetPrice = targetPrice;
            vwapFlipQuantity = Math.Max(1, DefaultQuantity);
            vwapFlipBandMultiplier = flipBand;
            vwapFlipReason = "VWAP_FAIL";

            StrategyLogInfo(string.Format("[VWAP_FLIP] Failure detected at {0:F2}; flipping {1} target={2:F2} stop={3:F2} band={4:F1}",
                currentPrice, flipSide, targetPrice, stopPrice, flipBand));
            UpdateStatusLabel($"VWAP flip pending: {flipSide}", false);

            ExitTradesForVwap("VWAP_FAIL");
            return true;
        }

        private double ResolveVwapFlipTargetPrice(TradeRuntimeState state, MarketPosition flipSide)
        {
            if (state != null && state.VwapNextBandPrice > 0)
                return state.VwapNextBandPrice;

            bool isLong = flipSide == MarketPosition.Long;
            double bandMult = state != null ? state.VwapBandMultiplier : 0;
            if (Math.Abs(bandMult - VwapBand2Multiplier) < 1e-6)
                return isLong ? lastVwapUpperBand3 : lastVwapLowerBand3;
            if (Math.Abs(bandMult - VwapBand1Multiplier) < 1e-6)
                return isLong ? lastVwapUpperBand2 : lastVwapLowerBand2;

            return isLong ? lastVwapUpperBand3 : lastVwapLowerBand3;
        }

        private double ResolveVwapFlipBandMultiplier(TradeRuntimeState state)
        {
            if (state != null && Math.Abs(state.VwapBandMultiplier - VwapBand2Multiplier) < 1e-6)
                return VwapFlipBandMultiplier;
            return VwapBand2Multiplier;
        }

        private bool TrySubmitVwapFlipEntries()
        {
            if (!vwapFlipPending)
                return false;
            if (!EnableVwapMrStrategy || !EnableVwapFailureFlip)
            {
                vwapFlipPending = false;
                vwapFlipSide = MarketPosition.Flat;
                return false;
            }
            if (vwapFlipSide == MarketPosition.Flat)
            {
                vwapFlipPending = false;
                return false;
            }

            if (IsAccountOpposedPosition(vwapFlipSide))
            {
                if (Debug)
                    StrategyLogDebug($"[VWAP_FLIP] Skipping {vwapFlipSide} flip due to opposing exposure.");
                UpdateStatusLabel("VWAP flip blocked: opposing exposure", false);
                return true;
            }

            int entriesToSubmit = GetVwapEntriesToSubmit();
            int quantityPerEntry = GetVwapQuantityPerEntry(entriesToSubmit);
            if (entriesToSubmit <= 0 || quantityPerEntry <= 0)
                return false;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(vwapFlipSide, entriesToSubmit, quantityPerEntry);
            for (int i = 0; i < entriesToSubmit; i++)
            {
                string tradeId = CreateTradeId(vwapFlipSide);
                var state = PrepareTradeState(tradeId, vwapFlipSide, quantityPerEntry);
                state.IsVwapEntry = true;
                state.VwapIsFlip = true;
                state.VwapBandMultiplier = vwapFlipBandMultiplier;
                state.VwapTargetPrice = vwapFlipTargetPrice;
                state.VwapNextBandPrice = vwapFlipTargetPrice;
                state.VwapTrailOnVwapTouch = false;
                state.VwapTrailActive = false;
                state.VwapTrailAnchorPrice = 0;
                state.VwapTrailDistance = VwapTrailInitialTicks * tickSize;
                state.VwapTrailIncrement = VwapTrailIncrementTicks * tickSize;
                state.VwapTrailLastStopPrice = null;
                state.VwapTrailHighWater = 0;
                state.VwapTrailLowWater = 0;
                if (vwapFlipSide == MarketPosition.Long)
                    state.VwapFailureLow = vwapFlipStopPrice;
                else
                    state.VwapFailureHigh = vwapFlipStopPrice;
                state.VwapFailureCheckBar = -1;
                state.EntryContext = "VWAP_FLIP";
                state.EntrySignalTime = GetVwapSignalTime();
                state.EntryBarIndex = CurrentBar;

                AttachTradeStateToSyncGroup(state, syncGroup);

                if (Debug)
                    StrategyLogDebug($"{Time[0]} VWAP FLIP {vwapFlipSide} ({tradeId}) qty={quantityPerEntry} entry={i + 1}/{entriesToSubmit}");

                if (vwapFlipSide == MarketPosition.Long)
                    EnterLong(quantityPerEntry, tradeId);
                else if (vwapFlipSide == MarketPosition.Short)
                    EnterShort(quantityPerEntry, tradeId);
            }

            vwapFlipPending = false;
            vwapFlipSide = MarketPosition.Flat;
            vwapFlipStopPrice = 0;
            vwapFlipTargetPrice = 0;
            vwapFlipQuantity = 0;
            vwapFlipBandMultiplier = 0;
            vwapFlipReason = null;

            return true;
        }

        private bool TrySubmitVwapEntries(bool vwapCanLong, bool vwapCanShort, VwapMrSignal longSignal, VwapMrSignal shortSignal, VwapMrValues values)
        {
            if (!vwapCanLong && !vwapCanShort)
                return false;

            MarketPosition side = MarketPosition.Flat;
            VwapMrSignal signal = new VwapMrSignal();

            if (vwapCanLong && vwapCanShort)
            {
                if (longSignal.DistancePct >= shortSignal.DistancePct)
                {
                    side = MarketPosition.Long;
                    signal = longSignal;
                }
                else
                {
                    side = MarketPosition.Short;
                    signal = shortSignal;
                }
            }
            else if (vwapCanLong)
            {
                side = MarketPosition.Long;
                signal = longSignal;
            }
            else
            {
                side = MarketPosition.Short;
                signal = shortSignal;
            }

            if (side == MarketPosition.Flat)
                return false;

            if (IsAccountOpposedPosition(side))
            {
                if (Debug)
                    StrategyLogDebug($"[VWAP_MR] Skipping {side} due to opposing exposure.");
                UpdateStatusLabel("VWAP MR blocked: opposing exposure", false);
                return true;
            }

            int entriesToSubmit = GetVwapEntriesToSubmit();
            int quantityPerEntry = GetVwapQuantityPerEntry(entriesToSubmit);
            if (entriesToSubmit <= 0 || quantityPerEntry <= 0)
                return false;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(side, entriesToSubmit, quantityPerEntry);
            for (int i = 0; i < entriesToSubmit; i++)
            {
                string tradeId = CreateTradeId(side);
                var state = PrepareTradeState(tradeId, side, quantityPerEntry);
                state.IsVwapEntry = true;
                state.VwapIsFlip = false;
                state.VwapBandMultiplier = signal.BandMultiplier;
                state.VwapTargetPrice = values.Vwap;
                state.VwapNextBandPrice = signal.NextBandPrice;
                state.VwapTrailOnVwapTouch = VwapExitMode == VwapExitModeOption.TrailOnVwapTouch;
                state.VwapTrailActive = false;
                state.VwapTrailAnchorPrice = 0;
                state.VwapTrailDistance = VwapTrailInitialTicks * tickSize;
                state.VwapTrailIncrement = VwapTrailIncrementTicks * tickSize;
                state.VwapTrailLastStopPrice = null;
                state.VwapTrailHighWater = 0;
                state.VwapTrailLowWater = 0;
                double entryHigh = GetVwapHigh(0);
                double entryLow = GetVwapLow(0);
                if (double.IsNaN(entryHigh) || double.IsNaN(entryLow))
                {
                    entryHigh = High[0];
                    entryLow = Low[0];
                }
                state.VwapFailureHigh = entryHigh;
                state.VwapFailureLow = entryLow;
                int vwapBarIndex = ResolveVwapBarsIndex();
                int vwapBar = (CurrentBars != null && vwapBarIndex < CurrentBars.Length) ? CurrentBars[vwapBarIndex] : CurrentBar;
                state.VwapFailureCheckBar = EnableVwapFailureFlip ? vwapBar + 1 : -1;
                state.EntryContext = "VWAP_MR";
                state.EntrySignalTime = GetVwapSignalTime();
                state.EntryBarIndex = CurrentBar;

                AttachTradeStateToSyncGroup(state, syncGroup);

                if (Debug)
                    StrategyLogDebug($"{Time[0]} VWAP MR {side} ({tradeId}) band={signal.BandMultiplier:F1} qty={quantityPerEntry} entry={i + 1}/{entriesToSubmit}");

                if (side == MarketPosition.Long)
                    EnterLong(quantityPerEntry, tradeId);
                else if (side == MarketPosition.Short)
                    EnterShort(quantityPerEntry, tradeId);
            }

            return true;
        }

        private double ResolveVwapNextBandPrice(TradeRuntimeState state, bool isLong)
        {
            if (state != null && state.VwapNextBandPrice > 0)
                return state.VwapNextBandPrice;

            double bandMult = state != null ? state.VwapBandMultiplier : 0;
            if (Math.Abs(bandMult - VwapBand2Multiplier) < 1e-6)
                return isLong ? lastVwapLowerBand3 : lastVwapUpperBand3;
            if (Math.Abs(bandMult - VwapBand1Multiplier) < 1e-6)
                return isLong ? lastVwapLowerBand2 : lastVwapUpperBand2;

            return isLong ? lastVwapLowerBand3 : lastVwapUpperBand3;
        }

        private bool ApplyVwapProtection(TradeRuntimeState activeState, double currentPrice, bool manualStopLocked, bool manualTargetLocked)
        {
            if (activeState == null || !activeState.IsVwapEntry)
                return false;
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            List<TradeRuntimeState> states = null;
            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group) && group != null)
            {
                states = GetMultiEntrySyncStates(group.TradeId);
            }
            else if (openTradeOrder != null && openTradeOrder.Count > 0)
            {
                states = new List<TradeRuntimeState>();
                foreach (string tradeId in openTradeOrder)
                {
                    TradeRuntimeState state;
                    if (TryGetTradeState(tradeId, out state) && state != null)
                        states.Add(state);
                }
            }
            else
            {
                states = new List<TradeRuntimeState> { activeState };
            }

            states = states.Where(s => s != null && s.RemainingQuantity > 0 && s.IsVwapEntry && !s.IsScaleInEntry).ToList();
            if (states.Count == 0)
                return false;

            bool isLong = Position.MarketPosition == MarketPosition.Long;
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

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

            double baseStop = 0.0;
            if (activeState.VwapIsFlip)
            {
                baseStop = isLong ? activeState.VwapFailureLow : activeState.VwapFailureHigh;
                if (baseStop <= 0)
                {
                    int primaryBar = GetPrimaryCurrentBar();
                    int lookback = Math.Min(VwapSwingStopLookback, primaryBar + 1);
                    double swingHigh = MAX(Highs[0], lookback)[0];
                    double swingLow = MIN(Lows[0], lookback)[0];
                    double swingStop = isLong ? swingLow - tickSize : swingHigh + tickSize;
                    double hardStop = ResolveVwapNextBandPrice(activeState, isLong);
                    baseStop = swingStop;
                    if (hardStop > 0)
                        baseStop = isLong ? Math.Min(baseStop, hardStop) : Math.Max(baseStop, hardStop);
                }
            }
            else
            {
                int primaryBar = GetPrimaryCurrentBar();
                int lookback = Math.Min(VwapSwingStopLookback, primaryBar + 1);
                double swingHigh = MAX(Highs[0], lookback)[0];
                double swingLow = MIN(Lows[0], lookback)[0];
                double swingStop = isLong ? swingLow - tickSize : swingHigh + tickSize;
                double hardStop = ResolveVwapNextBandPrice(activeState, isLong);
                baseStop = swingStop;
                if (hardStop > 0)
                    baseStop = isLong ? Math.Min(baseStop, hardStop) : Math.Max(baseStop, hardStop);
            }

            if (baseStop > 0)
                baseStop = Instrument?.MasterInstrument?.RoundToTickSize(baseStop) ?? Math.Round(baseStop / tickSize) * tickSize;

            bool trailMode = activeState.VwapTrailOnVwapTouch && !activeState.VwapIsFlip;
            bool trailActive = activeState.VwapTrailActive;

            if (trailMode)
            {
                double vwapValue = lastVwapValue;
                bool touched = vwapValue > 0 && (isLong ? Highs[0][0] >= vwapValue : Lows[0][0] <= vwapValue);
                if (touched && !trailActive)
                {
                    foreach (var state in states)
                    {
                        state.VwapTrailActive = true;
                        state.VwapTrailAnchorPrice = vwapValue;
                        state.VwapTrailDistance = VwapTrailInitialTicks * tickSize;
                        state.VwapTrailIncrement = VwapTrailIncrementTicks * tickSize;
                        state.VwapTrailHighWater = currentPrice;
                        state.VwapTrailLowWater = currentPrice;
                        state.VwapTrailLastStopPrice = null;
                    }
                    trailActive = true;
                }

                if (trailActive)
                {
                    foreach (var state in states)
                    {
                        if (isLong)
                        {
                            if (currentPrice > state.VwapTrailHighWater || state.VwapTrailHighWater <= 0)
                                state.VwapTrailHighWater = currentPrice;
                            if (state.VwapTrailLowWater <= 0)
                                state.VwapTrailLowWater = currentPrice;
                        }
                        else
                        {
                            if (currentPrice < state.VwapTrailLowWater || state.VwapTrailLowWater <= 0)
                                state.VwapTrailLowWater = currentPrice;
                            if (state.VwapTrailHighWater <= 0)
                                state.VwapTrailHighWater = currentPrice;
                        }
                    }
                }
            }

            double desiredStop = baseStop;

            if (trailMode && trailActive)
            {
                double anchor = activeState.VwapTrailAnchorPrice > 0 ? activeState.VwapTrailAnchorPrice : lastVwapValue;
                double distance = activeState.VwapTrailDistance > 0 ? activeState.VwapTrailDistance : VwapTrailInitialTicks * tickSize;
                double increment = activeState.VwapTrailIncrement > 0 ? activeState.VwapTrailIncrement : VwapTrailIncrementTicks * tickSize;
                double highWater = activeState.VwapTrailHighWater > 0 ? activeState.VwapTrailHighWater : currentPrice;
                double lowWater = activeState.VwapTrailLowWater > 0 ? activeState.VwapTrailLowWater : currentPrice;

                double progress = isLong ? (highWater - anchor) : (anchor - lowWater);
                double steps = increment > 0 ? Math.Floor(Math.Max(0.0, progress) / increment) : 0;
                double trailStop = isLong
                    ? anchor - distance + steps * increment
                    : anchor + distance - steps * increment;

                if (baseStop > 0)
                    trailStop = isLong ? Math.Max(trailStop, baseStop) : Math.Min(trailStop, baseStop);

                double? lastAccepted = activeState.VwapTrailLastStopPrice ?? (activeState.LastStopPrice > 0 ? (double?)activeState.LastStopPrice : null);
                if (lastAccepted.HasValue)
                {
                    if (isLong && trailStop <= lastAccepted.Value)
                        trailStop = lastAccepted.Value;
                    else if (!isLong && trailStop >= lastAccepted.Value)
                        trailStop = lastAccepted.Value;
                }

                desiredStop = trailStop;
            }

            if (!manualStopLocked && desiredStop > 0)
            {
                foreach (var state in states)
                {
                    if (state == null)
                        continue;
                    if (IsManualProtectionHoldActive(state, true))
                        continue;

                    double? lastAccepted = state.VwapTrailLastStopPrice ?? (state.LastStopPrice > 0 ? (double?)state.LastStopPrice : null);
                    double? clamped = ClampStopPrice(desiredStop, clampRef, isLong, lastAccepted);
                    if (!clamped.HasValue)
                        continue;
                    if (lastAccepted.HasValue && PricesClose(lastAccepted.Value, clamped.Value))
                        continue;

                    if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                    {
                        state.LastStopPrice = clamped.Value;
                        if (trailMode && trailActive)
                            state.VwapTrailLastStopPrice = clamped.Value;
                        stopSet = true;
                    }
                }
            }

            if (trailMode && !manualTargetLocked)
            {
                foreach (var state in states)
                {
                    if (state == null)
                        continue;
                    if (IsManualProtectionHoldActive(state, false))
                        continue;
                    if (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState))
                        TryCancelOrder(state.TradeId, state.TargetOrder, null, "target");
                    state.TargetOrder = null;
                    state.LastTargetPrice = 0;
                }
            }
            else if (!manualTargetLocked)
            {
                double targetPrice = activeState.VwapIsFlip ? activeState.VwapTargetPrice : lastVwapValue;
                if (targetPrice > 0)
                {
                    targetPrice = Instrument?.MasterInstrument?.RoundToTickSize(targetPrice) ?? Math.Round(targetPrice / tickSize) * tickSize;
                    foreach (var state in states)
                    {
                        if (state == null)
                            continue;
                        if (IsManualProtectionHoldActive(state, false))
                            continue;

                        double lastTarget = state.LastTargetPrice > 0 ? state.LastTargetPrice : 0;
                        if (lastTarget > 0 && PricesClose(lastTarget, targetPrice))
                            continue;

                        if (IssueProfitTarget(state.TradeId, CalculationMode.Price, targetPrice))
                        {
                            state.LastTargetPrice = targetPrice;
                            targetSet = true;
                        }
                    }
                }
            }

            return true;
        }

        private double GetRealtimePrice()
        {
            if (BarsArray.Length > 1 && Closes[1].Count > 0)
                return Closes[1][0];
            return Closes[0].Count > 0 ? Closes[0][0] : Close[0];
        }

        private bool TryGetStraddleTrackingPrice(out double price)
        {
            price = double.NaN;
            double bid = !double.IsNaN(lastBid) ? lastBid : 0;
            double ask = !double.IsNaN(lastAsk) ? lastAsk : 0;

            if (lastLast > 0)
                price = lastLast;
            else if (bid > 0 && ask > 0)
                price = (bid + ask) * 0.5;
            else if (bid > 0)
                price = bid;
            else if (ask > 0)
                price = ask;

            if (price <= 0 || double.IsNaN(price))
                price = GetRealtimePrice();

            return price > 0 && !double.IsNaN(price);
        }

        private int GetVwapMrTimeframeMinutes()
        {
            switch (VwapMrTimeframe)
            {
                case VwapMrTimeframeOption.M15:
                    return 15;
                case VwapMrTimeframeOption.H1:
                    return 60;
                default:
                    return 5;
            }
        }

        private int ResolveVwapBarsIndex()
        {
            if (vwapMrBarsIndex >= 0 && vwapMrBarsIndex < BarsArray.Length)
                return vwapMrBarsIndex;
            return 0;
        }

        private bool VwapBarsReady(int barsAgo)
        {
            int idx = ResolveVwapBarsIndex();
            return CurrentBars != null && idx < CurrentBars.Length && CurrentBars[idx] >= barsAgo;
        }

        private double GetVwapOpen(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return double.NaN;
            int idx = ResolveVwapBarsIndex();
            return Opens[idx][barsAgo];
        }

        private double GetVwapHigh(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return double.NaN;
            int idx = ResolveVwapBarsIndex();
            return Highs[idx][barsAgo];
        }

        private double GetVwapLow(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return double.NaN;
            int idx = ResolveVwapBarsIndex();
            return Lows[idx][barsAgo];
        }

        private double GetVwapClose(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return double.NaN;
            int idx = ResolveVwapBarsIndex();
            return Closes[idx][barsAgo];
        }

        private double GetVwapVolume(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return 0.0;
            int idx = ResolveVwapBarsIndex();
            double vol = Volumes[idx][barsAgo];
            return vol > 0 ? vol : 1.0;
        }

        private DateTime GetVwapSignalTime()
        {
            int idx = ResolveVwapBarsIndex();
            if (Times != null && idx < Times.Length && VwapBarsReady(0))
                return Times[idx][0];
            if (Time != null && Time.Count > 0)
                return Time[0];
            return DateTime.UtcNow;
        }

        private double CalculateVwapMedianVolume()
        {
            int idx = ResolveVwapBarsIndex();
            if (CurrentBars == null || idx >= CurrentBars.Length)
                return 1.0;

            int available = CurrentBars[idx] + 1;
            int sampleSize = Math.Min(VwapMedianSampleSize, available);
            if (sampleSize <= 0)
                return 1.0;

            List<double> values = new List<double>(sampleSize);
            for (int i = 0; i < sampleSize; i++)
            {
                double vol = Volumes[idx][i];
                if (vol <= 0)
                    continue;
                values.Add(vol);
            }

            if (values.Count == 0)
                return 1.0;

            values.Sort();
            return values[values.Count / 2];
        }

        private double GetAdjustedVwapVolume(int barsAgo)
        {
            double vol = GetVwapVolume(barsAgo);
            if (!VwapFilterSpikes)
                return vol;

            if (vwapMedianVolume <= 0.0)
                vwapMedianVolume = CalculateVwapMedianVolume();

            if (vwapMedianVolume > 0.0 && vol > vwapMedianVolume * VwapSpikeThreshold)
                return vwapMedianVolume;

            return vol;
        }

        private bool IsVwapMrSeriesEnabled()
        {
            return ShowVwapMrVisuals || UseVwapDirectionGate;
        }

        private void EnsureVwapMrSeries()
        {
            if (!IsVwapMrSeriesEnabled())
                return;

            if (vwapSeries != null)
                return;

            if (vwapMrBarsIndex < 0)
                return;

            vwapSeries = new Series<double>(this);
            vwapUpperBand1 = new Series<double>(this);
            vwapUpperBand2 = new Series<double>(this);
            vwapLowerBand1 = new Series<double>(this);
            vwapLowerBand2 = new Series<double>(this);
            if (vwapMrBarsIndex >= 0 && vwapMrBarsIndex < BarsArray.Length)
                vwapAtr = ATR(BarsArray[vwapMrBarsIndex], 14);
            else
                vwapAtr = ATR(14);
            vwapMedianVolume = 0.0;
            hasVwapMrValues = false;
            vwapMrLastProcessedBar = -1;
            vwapSessionStartTime = DateTime.MinValue;
            UpdateVwapSessionSettings();
        }

        private void UpdateVwapMrSeries()
        {
            EnsureVwapMrSeries();
            if (!IsVwapMrSeriesEnabled() || vwapMrBarsIndex < 0)
                return;

            if (!VwapBarsReady(0))
                return;

            int idx = ResolveVwapBarsIndex();
            int currentVwapBar = (CurrentBars != null && idx < CurrentBars.Length) ? CurrentBars[idx] : 0;
            if (currentVwapBar == vwapMrLastProcessedBar)
                return;

            if (vwapTargetTimeZone == null || vwapSourceTimeZone == null)
                UpdateVwapSessionSettings();

            DateTime currentTime = GetVwapTime(0);
            DateTime prevTime = VwapBarsReady(1) ? GetVwapTime(1) : currentTime;
            bool isNewSession = IsVwapNewSession(currentTime, prevTime, currentVwapBar == 0);
            if (isNewSession)
            {
                vwapCumulativePriceVolume = 0.0;
                vwapCumulativeVolume = 0.0;
                vwapCumulativePriceVolume2 = 0.0;
                vwapSessionBars = 0;
                vwapSessionVolume = 0.0;
                vwapSessionStartTime = currentTime;
            }
            else if (vwapSessionStartTime == DateTime.MinValue)
            {
                vwapSessionStartTime = currentTime;
            }

            double high = GetVwapHigh(0);
            double low = GetVwapLow(0);
            double close = GetVwapClose(0);
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close))
                return;

            double typicalPrice = (high + low + close) / 3.0;
            double volume = GetAdjustedVwapVolume(0);
            if (volume <= 0)
                volume = 1.0;

            vwapCumulativePriceVolume += typicalPrice * volume;
            vwapCumulativeVolume += volume;
            vwapCumulativePriceVolume2 += typicalPrice * typicalPrice * volume;
            vwapSessionBars += 1;
            vwapSessionVolume += volume;

            double vwap = vwapCumulativeVolume > 0.0
                ? vwapCumulativePriceVolume / vwapCumulativeVolume
                : typicalPrice;
            double variance = vwapCumulativeVolume > 0.0
                ? (vwapCumulativePriceVolume2 / vwapCumulativeVolume) - (vwap * vwap)
                : 0.0;
            double stdDev = variance > 0.0 ? Math.Sqrt(variance) : 0.0;

            lastVwapMrValues = new VwapMrValues
            {
                Vwap = vwap,
                StdDev = stdDev,
                UpperBand1 = vwap + stdDev * VwapBand1Multiplier,
                UpperBand2 = vwap + stdDev * VwapBand2Multiplier,
                UpperBand3 = vwap + stdDev * VwapFlipBandMultiplier,
                LowerBand1 = vwap - stdDev * VwapBand1Multiplier,
                LowerBand2 = vwap - stdDev * VwapBand2Multiplier,
                LowerBand3 = vwap - stdDev * VwapFlipBandMultiplier,
                IsNewSession = isNewSession
            };

            lastVwapValue = lastVwapMrValues.Vwap;
            lastVwapUpperBand1 = lastVwapMrValues.UpperBand1;
            lastVwapUpperBand2 = lastVwapMrValues.UpperBand2;
            lastVwapUpperBand3 = lastVwapMrValues.UpperBand3;
            lastVwapLowerBand1 = lastVwapMrValues.LowerBand1;
            lastVwapLowerBand2 = lastVwapMrValues.LowerBand2;
            lastVwapLowerBand3 = lastVwapMrValues.LowerBand3;

            hasVwapMrValues = true;
            vwapMrLastProcessedBar = currentVwapBar;
        }

        private bool TryUpdateVwapMrValues(out VwapMrValues values)
        {
            values = new VwapMrValues();
            EnsureVwapMrSeries();
            if (!IsVwapMrSeriesEnabled() || vwapSeries == null || !hasVwapMrValues)
                return false;

            values = lastVwapMrValues;

            vwapSeries[0] = values.Vwap;
            vwapUpperBand1[0] = values.UpperBand1;
            vwapUpperBand2[0] = values.UpperBand2;
            vwapLowerBand1[0] = values.LowerBand1;
            vwapLowerBand2[0] = values.LowerBand2;

            if ((ShowVwapMrVisuals || UseVwapDirectionGate) && CurrentBar > 0)
                DrawVwapMrLines(values);

            return true;
        }

        private void DrawVwapMrLines(VwapMrValues values)
        {
            if (CurrentBar <= 0)
                return;

            Brush vwapBrush = Brushes.Gold;
            Brush band1Brush = Brushes.DeepSkyBlue;
            Brush band2Brush = Brushes.RoyalBlue;
            int vwapWidth = 3;
            int band1Width = 1;
            int band2Width = 2;

            var vwapLine = Draw.Line(this, VwapLineTagPrefix + CurrentBar, false, Time[1], vwapSeries[1], Time[0], values.Vwap, string.Empty);
            ApplyVwapLineStyle(vwapLine, vwapBrush, vwapWidth);
            var band1Up = Draw.Line(this, VwapBand1UpperTagPrefix + CurrentBar, false, Time[1], vwapUpperBand1[1], Time[0], values.UpperBand1, string.Empty);
            ApplyVwapLineStyle(band1Up, band1Brush, band1Width);
            var band2Up = Draw.Line(this, VwapBand2UpperTagPrefix + CurrentBar, false, Time[1], vwapUpperBand2[1], Time[0], values.UpperBand2, string.Empty);
            ApplyVwapLineStyle(band2Up, band2Brush, band2Width);
            var band1Dn = Draw.Line(this, VwapBand1LowerTagPrefix + CurrentBar, false, Time[1], vwapLowerBand1[1], Time[0], values.LowerBand1, string.Empty);
            ApplyVwapLineStyle(band1Dn, band1Brush, band1Width);
            var band2Dn = Draw.Line(this, VwapBand2LowerTagPrefix + CurrentBar, false, Time[1], vwapLowerBand2[1], Time[0], values.LowerBand2, string.Empty);
            ApplyVwapLineStyle(band2Dn, band2Brush, band2Width);
        }

        private void ApplyVwapLineStyle(NinjaTrader.NinjaScript.DrawingTools.Line line, Brush brush, int width)
        {
            if (line == null || line.Stroke == null)
                return;

            line.Stroke.Brush = brush;
            line.Stroke.Width = Math.Max(1, width);
        }

        private VwapMrSignal EvaluateVwapMrSignal(MarketPosition side, VwapMrValues values)
        {
            var signal = new VwapMrSignal();
            if (values.Vwap <= 0)
                return signal;
            if (!VwapBarsReady(0))
                return signal;

            bool isShort = side == MarketPosition.Short;
            double band1 = isShort ? values.UpperBand1 : values.LowerBand1;
            double band2 = isShort ? values.UpperBand2 : values.LowerBand2;
            double band3 = isShort ? values.UpperBand3 : values.LowerBand3;

            double barHigh = GetVwapHigh(0);
            double barLow = GetVwapLow(0);
            double barClose = GetVwapClose(0);
            if (double.IsNaN(barHigh) || double.IsNaN(barLow) || double.IsNaN(barClose))
                return signal;

            bool touchesBand2 = isShort ? barHigh >= band2 : barLow <= band2;
            bool touchesBand1 = isShort ? barHigh >= band1 : barLow <= band1;
            if (touchesBand2)
            {
                signal.BandTouched = true;
                signal.BandPrice = band2;
                signal.BandMultiplier = VwapBand2Multiplier;
                signal.NextBandPrice = band3;
            }
            else if (touchesBand1)
            {
                signal.BandTouched = true;
                signal.BandPrice = band1;
                signal.BandMultiplier = VwapBand1Multiplier;
                signal.NextBandPrice = band2;
            }
            else
            {
                return signal;
            }

            double distance = isShort
                ? (barHigh - values.Vwap) / values.Vwap
                : (values.Vwap - barLow) / values.Vwap;
            signal.DistancePct = distance;
            signal.DistanceOk = distance >= Math.Max(0.0, MinDistFromVWAP_Percent);
            signal.CloseInside = isShort ? barClose < signal.BandPrice : barClose > signal.BandPrice;

            signal.PinBar = EnableVwapPinBar && IsPinBar(isShort);
            signal.Doji = EnableVwapDoji && IsDoji(0);
            signal.Engulfing = EnableVwapEngulfing && IsEngulfing(isShort);
            signal.Tweezer = EnableVwapTweezer && IsTweezer(isShort);
            signal.Railroad = EnableVwapRailroad && IsRailroadTracks(isShort);
            signal.DojiStar = EnableVwapDojiStar && IsDojiStar(isShort, signal.BandPrice);
            signal.ThreeInside = EnableVwapThreeInside && IsThreeInside(isShort, signal.BandPrice);
            signal.PatternOk = signal.PinBar || signal.Doji || signal.Engulfing || signal.Tweezer || signal.Railroad || signal.DojiStar || signal.ThreeInside;

            signal.Ready = signal.BandTouched && signal.DistanceOk && signal.CloseInside && signal.PatternOk;
            return signal;
        }

        private void UpdateVwapSessionSettings()
        {
            vwapAssetClass = DetectVwapAssetClass();
            vwapEffectiveReset = ResolveVwapResetPeriod(vwapAssetClass);
            vwapEffectiveTimezone = ResolveVwapTimezoneOption(vwapAssetClass);

            int idx = ResolveVwapBarsIndex();
            vwapSourceTimeZone = ResolveVwapSeriesTimeZone(idx);
            vwapTargetTimeZone = ResolveVwapTimeZoneInfo(vwapEffectiveTimezone, vwapSourceTimeZone);
        }

        private VwapAssetClass DetectVwapAssetClass()
        {
            try
            {
                if (Instrument?.MasterInstrument != null)
                {
                    var instType = Instrument.MasterInstrument.InstrumentType;
                    switch (instType)
                    {
                        case InstrumentType.Forex:
                            return VwapAssetClass.Forex;
                        case InstrumentType.Index:
                            return VwapAssetClass.Index;
                        case InstrumentType.Stock:
                            return VwapAssetClass.Stock;
                        case InstrumentType.Future:
                            break;
                    }

                    if (string.Equals(instType.ToString(), "CryptoCurrency", StringComparison.OrdinalIgnoreCase))
                        return VwapAssetClass.Crypto;
                }
            }
            catch { }

            string symbol = Instrument?.MasterInstrument?.Name ?? Instrument?.FullName ?? string.Empty;
            string upper = symbol.ToUpperInvariant();

            if (LooksLikeForexPair(upper))
                return VwapAssetClass.Forex;
            if (ContainsAny(upper, new[] { "BTC", "ETH", "XRP", "LTC", "ADA", "SOL", "DOGE", "BNB", "DOT", "AVAX", "MATIC", "LINK", "USDT", "USDC" }))
                return VwapAssetClass.Crypto;
            if (ContainsAny(upper, new[] { "XAU", "GOLD", "GC", "XAG", "SILVER", "SI", "XPT", "XPD", "PLAT" }))
                return VwapAssetClass.Metal;
            if (ContainsAny(upper, new[] { "WTI", "BRENT", "CRUDE", "USOIL", "UKOIL", "NGAS", "CL", "MCL", "NG", "XBR", "XTI" }))
                return VwapAssetClass.Energy;
            if (ContainsAny(upper, new[] { "US30", "US500", "US100", "NAS100", "SPX", "NDX", "DJI", "DOW", "NQ", "MNQ", "ES", "MES", "YM", "MYM", "RTY", "M2K" }))
                return VwapAssetClass.Index;

            return VwapAssetClass.Unknown;
        }

        private bool LooksLikeForexPair(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol) || symbol.Length < 6)
                return false;

            string pair = symbol.Trim();
            if (pair.Length < 6)
                return false;

            string baseCcy = pair.Substring(0, 3);
            string quoteCcy = pair.Substring(3, 3);
            return IsForexCode(baseCcy) && IsForexCode(quoteCcy);
        }

        private bool IsForexCode(string code)
        {
            switch (code)
            {
                case "USD":
                case "EUR":
                case "GBP":
                case "JPY":
                case "AUD":
                case "NZD":
                case "CAD":
                case "CHF":
                case "SEK":
                case "NOK":
                case "DKK":
                case "SGD":
                case "HKD":
                    return true;
                default:
                    return false;
            }
        }

        private bool ContainsAny(string text, string[] tokens)
        {
            if (string.IsNullOrEmpty(text) || tokens == null)
                return false;
            foreach (string token in tokens)
            {
                if (!string.IsNullOrEmpty(token) && text.Contains(token))
                    return true;
            }
            return false;
        }

        private VwapResetPeriodOption ResolveVwapResetPeriod(VwapAssetClass assetClass)
        {
            if (vwapResetSetting != VwapResetPeriodOption.Auto)
                return vwapResetSetting;

            switch (assetClass)
            {
                case VwapAssetClass.Crypto:
                    return VwapResetPeriodOption.Daily;
                case VwapAssetClass.Forex:
                case VwapAssetClass.Metal:
                case VwapAssetClass.Energy:
                    return VwapResetPeriodOption.Forex5Pm;
                case VwapAssetClass.Index:
                case VwapAssetClass.Stock:
                    return VwapResetPeriodOption.Daily;
                default:
                    return VwapResetPeriodOption.Daily;
            }
        }

        private VwapTimezoneOption ResolveVwapTimezoneOption(VwapAssetClass assetClass)
        {
            if (vwapTimezoneSetting != VwapTimezoneOption.Auto)
                return vwapTimezoneSetting;

            switch (assetClass)
            {
                case VwapAssetClass.Crypto:
                    return VwapTimezoneOption.UTC;
                case VwapAssetClass.Forex:
                case VwapAssetClass.Metal:
                case VwapAssetClass.Energy:
                case VwapAssetClass.Index:
                case VwapAssetClass.Stock:
                    return VwapTimezoneOption.NewYork;
                default:
                    return VwapTimezoneOption.Exchange;
            }
        }

        private TimeZoneInfo ResolveVwapSeriesTimeZone(int idx)
        {
            try
            {
                if (BarsArray != null && idx >= 0 && idx < BarsArray.Length)
                {
                    var tz = BarsArray[idx]?.TradingHours?.TimeZoneInfo;
                    if (tz != null)
                        return tz;
                }
            }
            catch { }
            return TimeZoneInfo.Local;
        }

        private TimeZoneInfo ResolveVwapTimeZoneInfo(VwapTimezoneOption option, TimeZoneInfo fallback)
        {
            switch (option)
            {
                case VwapTimezoneOption.UTC:
                    return TimeZoneInfo.Utc;
                case VwapTimezoneOption.NewYork:
                    return TryGetTimeZone("Eastern Standard Time", "America/New_York");
                case VwapTimezoneOption.London:
                    return TryGetTimeZone("GMT Standard Time", "Europe/London");
                case VwapTimezoneOption.Tokyo:
                    return TryGetTimeZone("Tokyo Standard Time", "Asia/Tokyo");
                case VwapTimezoneOption.Sydney:
                    return TryGetTimeZone("AUS Eastern Standard Time", "Australia/Sydney");
                case VwapTimezoneOption.Custom:
                    try
                    {
                        return TimeZoneInfo.CreateCustomTimeZone("VWAP_CUSTOM", TimeSpan.FromHours(vwapCustomUtcOffsetHours), "VWAP_CUSTOM", "VWAP_CUSTOM");
                    }
                    catch
                    {
                        return TimeZoneInfo.Local;
                    }
                case VwapTimezoneOption.Exchange:
                default:
                    return fallback ?? TimeZoneInfo.Local;
            }
        }

        private TimeZoneInfo TryGetTimeZone(string windowsId, string ianaId)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }
            catch
            {
                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
                }
                catch
                {
                    return TimeZoneInfo.Local;
                }
            }
        }

        private DateTime GetVwapTime(int barsAgo)
        {
            int idx = ResolveVwapBarsIndex();
            if (Times != null && idx < Times.Length && VwapBarsReady(barsAgo))
                return Times[idx][barsAgo];
            if (Time != null && Time.Count > barsAgo)
                return Time[barsAgo];
            return DateTime.UtcNow;
        }

        private DateTime AdjustVwapTime(DateTime time)
        {
            if (vwapSourceTimeZone == null || vwapTargetTimeZone == null || vwapSourceTimeZone == vwapTargetTimeZone)
                return time;

            try
            {
                return TimeZoneInfo.ConvertTime(time, vwapSourceTimeZone, vwapTargetTimeZone);
            }
            catch
            {
                return time;
            }
        }

        private bool IsVwapNewSession(DateTime currentTime, DateTime previousTime, bool isFirstBar)
        {
            if (isFirstBar)
                return true;

            DateTime adjCurrent = AdjustVwapTime(currentTime);
            DateTime adjPrev = AdjustVwapTime(previousTime);

            switch (vwapEffectiveReset)
            {
                case VwapResetPeriodOption.Daily:
                    return adjCurrent.Date != adjPrev.Date;
                case VwapResetPeriodOption.Forex5Pm:
                    DateTime shiftedCurrent = adjCurrent.AddHours(-17);
                    DateTime shiftedPrev = adjPrev.AddHours(-17);
                    return shiftedCurrent.Date != shiftedPrev.Date;
                case VwapResetPeriodOption.Weekly:
                    return adjCurrent.DayOfWeek == DayOfWeek.Monday && adjPrev.DayOfWeek != DayOfWeek.Monday;
                case VwapResetPeriodOption.Monthly:
                    return adjCurrent.Month != adjPrev.Month || adjCurrent.Year != adjPrev.Year;
                case VwapResetPeriodOption.None:
                    return false;
                default:
                    return adjCurrent.Date != adjPrev.Date;
            }
        }

        private double GetVwapAtrValue(int barsAgo)
        {
            if (barsAgo < 0)
                return 0.0;

            int idx = ResolveVwapBarsIndex();
            if (vwapAtr != null && CurrentBars != null && idx < CurrentBars.Length && CurrentBars[idx] >= barsAgo)
            {
                double value = vwapAtr[barsAgo];
                if (value > 0)
                    return value;
            }

            if (atr != null && CurrentBar >= barsAgo)
                return atr[barsAgo];

            return 0.0;
        }

        private bool IsPinBar(bool isShort)
        {
            if (!VwapBarsReady(0))
                return false;
            double high = GetVwapHigh(0);
            double low = GetVwapLow(0);
            double close = GetVwapClose(0);
            double open = GetVwapOpen(0);
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close) || double.IsNaN(open))
                return false;
            double range = high - low;
            if (range <= 0)
                return false;
            double body = Math.Abs(close - open);
            double upperWick = high - Math.Max(open, close);
            double lowerWick = Math.Min(open, close) - low;
            if (body <= 0)
                body = TickSize;
            if (isShort)
                return upperWick >= VwapPinBarWickToBody * body && close < open;
            return lowerWick >= VwapPinBarWickToBody * body && close > open;
        }

        private bool IsDoji(int barsAgo)
        {
            if (!VwapBarsReady(barsAgo))
                return false;
            double high = GetVwapHigh(barsAgo);
            double low = GetVwapLow(barsAgo);
            double close = GetVwapClose(barsAgo);
            double open = GetVwapOpen(barsAgo);
            if (double.IsNaN(high) || double.IsNaN(low) || double.IsNaN(close) || double.IsNaN(open))
                return false;
            double range = high - low;
            if (range <= 0)
                return false;
            double body = Math.Abs(close - open);
            return body <= range * VwapDojiBodyPct;
        }

        private bool IsEngulfing(bool isShort)
        {
            if (!VwapBarsReady(1))
                return false;
            double close0 = GetVwapClose(0);
            double open0 = GetVwapOpen(0);
            double close1 = GetVwapClose(1);
            double open1 = GetVwapOpen(1);
            if (double.IsNaN(close0) || double.IsNaN(open0) || double.IsNaN(close1) || double.IsNaN(open1))
                return false;
            bool currBull = close0 > open0;
            bool prevBull = close1 > open1;
            bool currBear = close0 < open0;
            bool prevBear = close1 < open1;

            if (isShort)
            {
                if (!currBear || !prevBull)
                    return false;
                return open0 >= close1 && close0 <= open1;
            }

            if (!currBull || !prevBear)
                return false;
            return open0 <= close1 && close0 >= open1;
        }

        private bool IsTweezer(bool isShort)
        {
            if (!VwapBarsReady(1))
                return false;
            double tolerance = VwapTweezerToleranceTicks * TickSize;
            if (isShort)
                return Math.Abs(GetVwapHigh(0) - GetVwapHigh(1)) <= tolerance && GetVwapClose(0) < GetVwapClose(1);
            return Math.Abs(GetVwapLow(0) - GetVwapLow(1)) <= tolerance && GetVwapClose(0) > GetVwapClose(1);
        }

        private bool IsRailroadTracks(bool isShort)
        {
            if (!VwapBarsReady(1))
                return false;
            double close1 = GetVwapClose(1);
            double open1 = GetVwapOpen(1);
            double close0 = GetVwapClose(0);
            double open0 = GetVwapOpen(0);
            if (double.IsNaN(close1) || double.IsNaN(open1) || double.IsNaN(close0) || double.IsNaN(open0))
                return false;
            double body1 = Math.Abs(close1 - open1);
            double body2 = Math.Abs(close0 - open0);
            if (body1 <= 0 || body2 <= 0)
                return false;
            bool opposite = (close1 > open1) != (close0 > open0);
            if (!opposite)
                return false;
            double diffPct = Math.Abs(body1 - body2) / Math.Max(body1, body2);
            if (diffPct > VwapRailroadBodyDiffPct)
                return false;
            double atrValue = GetVwapAtrValue(0);
            if (atrValue <= 0)
                return false;
            double minBody = atrValue * VwapRailroadAtrMultiplier;
            if (body1 < minBody || body2 < minBody)
                return false;

            if (isShort)
                return close1 > open1 && close0 < open0;
            return close1 < open1 && close0 > open0;
        }

        private bool IsDojiStar(bool isShort, double bandPrice)
        {
            if (!VwapBarsReady(2) || bandPrice <= 0)
                return false;

            bool doji = IsDoji(1);
            if (!doji)
                return false;

            double close2 = GetVwapClose(2);
            double open2 = GetVwapOpen(2);
            double close0 = GetVwapClose(0);
            double open0 = GetVwapOpen(0);
            if (double.IsNaN(close2) || double.IsNaN(open2) || double.IsNaN(close0) || double.IsNaN(open0))
                return false;
            bool candle1Bull = close2 > open2;
            bool candle1Bear = close2 < open2;
            bool candle3Bull = close0 > open0;
            bool candle3Bear = close0 < open0;

            double atrValue = GetVwapAtrValue(2);
            double body1 = Math.Abs(close2 - open2);
            if (atrValue > 0 && body1 < atrValue)
                return false;

            double midpoint = (open2 + close2) * 0.5;
            if (isShort)
            {
                bool touchesBand = GetVwapHigh(2) >= bandPrice;
                return candle1Bull && candle3Bear && touchesBand && close0 <= midpoint;
            }

            bool touchesLower = GetVwapLow(2) <= bandPrice;
            return candle1Bear && candle3Bull && touchesLower && close0 >= midpoint;
        }

        private bool IsThreeInside(bool isShort, double bandPrice)
        {
            if (!VwapBarsReady(2) || bandPrice <= 0)
                return false;

            double close2 = GetVwapClose(2);
            double open2 = GetVwapOpen(2);
            if (double.IsNaN(close2) || double.IsNaN(open2))
                return false;
            bool motherBull = close2 > open2;
            bool motherBear = close2 < open2;
            bool inside = GetVwapHigh(1) <= GetVwapHigh(2) && GetVwapLow(1) >= GetVwapLow(2);
            if (!inside)
                return false;

            if (isShort)
            {
                if (!motherBull || GetVwapHigh(2) < bandPrice)
                    return false;
                return GetVwapClose(0) < GetVwapOpen(0) && GetVwapClose(0) < GetVwapLow(1);
            }

            if (!motherBear || GetVwapLow(2) > bandPrice)
                return false;
            return GetVwapClose(0) > GetVwapOpen(0) && GetVwapClose(0) > GetVwapHigh(1);
        }

        private void UpdateFilterVisuals()
        {
            if (!ShowFilterVisuals || BarsInProgress != 0)
            {
                RemoveDrawObject(OrbHighTag);
                RemoveDrawObject(OrbLowTag);
                RemoveDrawObject(OrbBoxTag);
                RemoveDrawObject(OrbStatusTag);
                RemoveDrawObject(OrbStartTag);
                RemoveDrawObject(OrbEndTag);
                RemoveDrawObject(OrbHighLabelTag);
                RemoveDrawObject(OrbLowLabelTag);
                RemoveDrawObject(ChopHighTag);
                RemoveDrawObject(ChopLowTag);
                RemoveDrawObject(ChopStatusTag);
                RemoveDrawObject(ChopRangeTag);
                RemoveDrawObject(ChopHighLabelTag);
                RemoveDrawObject(ChopLowLabelTag);
                RemoveDrawObject(StraddleRangeTag);
                RemoveDrawObject(StraddleLongZoneTag);
                RemoveDrawObject(StraddleShortZoneTag);
                RemoveDrawObject(StraddleStatusTag);
                RemoveDrawObject(StraddleCountdownTag);
                RemoveDrawObject(StraddleHardStopLongTag);
                RemoveDrawObject(StraddleHardStopShortTag);
                RemoveDrawObject(StraddleHardStopLongLabelTag);
                RemoveDrawObject(StraddleHardStopShortLabelTag);
                RemoveDrawObject(FilterStatusTag);
                straddleLongZoneRect = null;
                straddleShortZoneRect = null;
                straddleHardStopLongLine = null;
                straddleHardStopShortLine = null;
                return;
            }

            RemoveDrawObject(OrbStatusTag);
            RemoveDrawObject(ChopStatusTag);

            string orbSummary = string.Empty;
            string chopSummary = string.Empty;
            string straddleSummary = string.Empty;

            if (EnableOrbFilter)
            {
                bool hasStart = orbSessionStart != DateTime.MinValue;
                bool hasEnd = orbEndTime != DateTime.MinValue;
                DateTime blockStart = GetOrbBlockStartTime();
                bool hasBlockStart = blockStart != DateTime.MinValue;
                bool blockStarted = hasBlockStart && Time[0] >= blockStart;
                bool started = hasStart && Time[0] >= orbSessionStart;
                bool ended = hasEnd && Time[0] >= orbEndTime;
                bool hasRange = orbHigh > double.MinValue && orbLow < double.MaxValue;

                if (hasRange)
                {
                    Draw.HorizontalLine(this, OrbHighTag, orbHigh, Brushes.LimeGreen);
                    Draw.HorizontalLine(this, OrbLowTag, orbLow, Brushes.Red);
                    Draw.Text(this, OrbHighLabelTag, false, $"ORB H {orbHigh:F2}", 0, orbHigh, 0, Brushes.LimeGreen, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                    Draw.Text(this, OrbLowLabelTag, false, $"ORB L {orbLow:F2}", 0, orbLow, 0, Brushes.Red, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                }
                else
                {
                    RemoveDrawObject(OrbHighTag);
                    RemoveDrawObject(OrbLowTag);
                    RemoveDrawObject(OrbHighLabelTag);
                    RemoveDrawObject(OrbLowLabelTag);
                }

                if (hasStart && started)
                    Draw.VerticalLine(this, OrbStartTag, orbSessionStart, Brushes.DodgerBlue);
                else
                    RemoveDrawObject(OrbStartTag);

                if (hasEnd && ended)
                    Draw.VerticalLine(this, OrbEndTag, orbEndTime, Brushes.DodgerBlue);
                else
                    RemoveDrawObject(OrbEndTag);

                if (hasStart && hasEnd && started && hasRange)
                {
                    int startBarsAgo = FindBarsAgoForTime(orbSessionStart);
                    int endBarsAgo = Time[0] < orbEndTime ? 0 : FindBarsAgoForTime(orbEndTime);
                    if (startBarsAgo >= 0 && endBarsAgo >= 0 && orbHigh > double.MinValue && orbLow < double.MaxValue)
                    {
                        if (startBarsAgo < endBarsAgo)
                        {
                            int tmp = startBarsAgo;
                            startBarsAgo = endBarsAgo;
                            endBarsAgo = tmp;
                        }
                        Draw.Rectangle(this, OrbBoxTag, false, startBarsAgo, orbHigh, endBarsAgo, orbLow, Brushes.LightGray, Brushes.LightGray, 15);
                    }
                }
                else
                {
                    RemoveDrawObject(OrbBoxTag);
                }

                string orbState;
                if (!hasStart)
                    orbState = "ORB: waiting session";
                else if (hasBlockStart && !blockStarted)
                    orbState = $"ORB: idle until {blockStart:HH:mm}";
                else if (!started)
                    orbState = $"ORB: waiting {orbSessionStart:HH:mm}";
                else if (!orbRangeReady)
                    orbState = "ORB: building";
                else if (!orbBreakoutSatisfied)
                    orbState = "ORB: waiting breakout";
                else
                    orbState = "ORB: breakout";

                if (orbUsingFallback)
                    orbState += " (fallback)";

                string orbWindow = (hasStart && hasEnd && orbEndTime > orbSessionStart)
                    ? $" {orbSessionStart:HH:mm}-{orbEndTime:HH:mm}"
                    : string.Empty;
                string orbRangeText = hasRange ? $" H {orbHigh:F2} L {orbLow:F2}" : string.Empty;
                orbSummary = orbState + orbWindow + orbRangeText;
            }
            else
            {
                RemoveDrawObject(OrbHighTag);
                RemoveDrawObject(OrbLowTag);
                RemoveDrawObject(OrbBoxTag);
                RemoveDrawObject(OrbStatusTag);
                RemoveDrawObject(OrbStartTag);
                RemoveDrawObject(OrbEndTag);
                RemoveDrawObject(OrbHighLabelTag);
                RemoveDrawObject(OrbLowLabelTag);
            }

            if (EnableChopFilter)
            {
                bool chopActive = false;
                bool chopDecayActive = false;
                double chopDecayAdxDelta = 0.0;
                double chopDecayBbDelta = 0.0;
                double rangeHigh = 0.0;
                double rangeLow = 0.0;
                double buffer = 0.0;
                double adxValue = 0.0;
                double bbWidthPct = 0.0;
                int lookback = Math.Max(2, ChopLookbackBars);
                bool chopReady = TryGetChopState(lookback, out chopActive, out chopDecayActive, out chopDecayAdxDelta, out chopDecayBbDelta, out rangeHigh, out rangeLow, out buffer, out adxValue, out bbWidthPct);

                if (chopActive)
                {
                    double lineHigh = rangeHigh + buffer;
                    double lineLow = rangeLow - buffer;
                    double lineMid = (rangeHigh + rangeLow) * 0.5;
                    Draw.HorizontalLine(this, ChopHighTag, lineHigh, Brushes.OrangeRed);
                    Draw.HorizontalLine(this, ChopLowTag, lineLow, Brushes.OrangeRed);
                    Draw.HorizontalLine(this, ChopMidTag, lineMid, Brushes.Goldenrod);
                    Draw.Text(this, ChopHighLabelTag, false, $"CHOP H {lineHigh:F2}", 0, lineHigh, 0, Brushes.OrangeRed, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                    Draw.Text(this, ChopLowLabelTag, false, $"CHOP L {lineLow:F2}", 0, lineLow, 0, Brushes.OrangeRed, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                    Draw.Text(this, ChopMidLabelTag, false, $"CHOP MID {lineMid:F2}", 0, lineMid, 0, Brushes.Goldenrod, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);

                    int startBarsAgo = lookback;
                    int endBarsAgo = 1;
                    if (startBarsAgo < endBarsAgo)
                    {
                        int tmp = startBarsAgo;
                        startBarsAgo = endBarsAgo;
                        endBarsAgo = tmp;
                    }
                    Draw.Rectangle(this, ChopRangeTag, false, startBarsAgo, rangeHigh, endBarsAgo, rangeLow, Brushes.OrangeRed, Brushes.OrangeRed, 8);
                }
                else
                {
                    RemoveDrawObject(ChopHighTag);
                    RemoveDrawObject(ChopLowTag);
                    RemoveDrawObject(ChopMidTag);
                    RemoveDrawObject(ChopRangeTag);
                    RemoveDrawObject(ChopHighLabelTag);
                    RemoveDrawObject(ChopLowLabelTag);
                    RemoveDrawObject(ChopMidLabelTag);
                }

                string chopState = !chopReady
                    ? "CHOP: warming"
                    : (chopActive ? (chopDecayActive ? "CHOP: decay" : "CHOP: active") : "CHOP: clear");
                string chopMetrics = chopReady ? $" ADX {adxValue:F1} BB {bbWidthPct:F2}%" : string.Empty;
                if (chopReady && EnableChopDecayGate)
                    chopMetrics += $" dADX {chopDecayAdxDelta:F2} dBB {chopDecayBbDelta:F2}%";
                string chopRangeText = chopActive ? $" H {rangeHigh + buffer:F2} L {rangeLow - buffer:F2}" : string.Empty;
                chopSummary = chopState + chopMetrics + chopRangeText;
            }
            else
            {
                RemoveDrawObject(ChopHighTag);
                RemoveDrawObject(ChopLowTag);
                RemoveDrawObject(ChopMidTag);
                RemoveDrawObject(ChopStatusTag);
                RemoveDrawObject(ChopRangeTag);
                RemoveDrawObject(ChopHighLabelTag);
                RemoveDrawObject(ChopLowLabelTag);
                RemoveDrawObject(ChopMidLabelTag);
            }

            if (EnableStraddleTrades)
            {
            bool hasRange = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
            if (!hasRange)
                return;
                RemoveDrawObject(StraddleRangeTag);

                bool inRangeWindow = straddleRangeStart != DateTime.MinValue && Time[0] >= straddleRangeStart && Time[0] <= straddleRangeEnd;
                bool canDrawZones = hasRange && (straddleRangeReady || inRangeWindow);
                if (canDrawZones)
                {
                    UpdateStraddleZones();
                    int startBarsAgo = FindBarsAgoForTime(straddleRangeStart);
                    int endBarsAgo = 0;
                    if (startBarsAgo >= 0 && endBarsAgo >= 0)
                    {
                        if (startBarsAgo < endBarsAgo)
                        {
                            int tmp = startBarsAgo;
                            startBarsAgo = endBarsAgo;
                            endBarsAgo = tmp;
                        }
                        var longRect = Draw.Rectangle(this, StraddleLongZoneTag, false, startBarsAgo, straddleLongZoneUpper, endBarsAgo, straddleLongZoneLower, Brushes.DarkOrange, Brushes.Transparent, 8);
                        if (longRect != null)
                        {
                            longRect.IsLocked = true;
                            straddleLongZoneRect = longRect;
                        }

                        var shortRect = Draw.Rectangle(this, StraddleShortZoneTag, false, startBarsAgo, straddleShortZoneUpper, endBarsAgo, straddleShortZoneLower, Brushes.DeepSkyBlue, Brushes.Transparent, 8);
                        if (shortRect != null)
                        {
                            shortRect.IsLocked = true;
                            straddleShortZoneRect = shortRect;
                        }
                    }

                    double? longHard = GetStraddleHardStopPrice(MarketPosition.Long);
                    if (longHard.HasValue && longHard.Value > 0)
                    {
                        var line = Draw.HorizontalLine(this, StraddleHardStopLongTag, longHard.Value, Brushes.LimeGreen);
                        if (line != null)
                        {
                            line.IsLocked = true;
                            line.Stroke.Width = 2;
                            straddleHardStopLongLine = line;
                        }
                        Draw.Text(this, StraddleHardStopLongLabelTag, false, $"STRADDLE HARD STOP L {longHard.Value:F2}", 0, longHard.Value, 0, Brushes.LimeGreen, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                    }

                    double? shortHard = GetStraddleHardStopPrice(MarketPosition.Short);
                    if (shortHard.HasValue && shortHard.Value > 0)
                    {
                        var line = Draw.HorizontalLine(this, StraddleHardStopShortTag, shortHard.Value, Brushes.OrangeRed);
                        if (line != null)
                        {
                            line.IsLocked = true;
                            line.Stroke.Width = 2;
                            straddleHardStopShortLine = line;
                        }
                        Draw.Text(this, StraddleHardStopShortLabelTag, false, $"STRADDLE HARD STOP S {shortHard.Value:F2}", 0, shortHard.Value, 0, Brushes.OrangeRed, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
                    }
                }
                else
                {
                    RemoveDrawObject(StraddleLongZoneTag);
                    RemoveDrawObject(StraddleShortZoneTag);
                    RemoveDrawObject(StraddleHardStopLongTag);
                    RemoveDrawObject(StraddleHardStopShortTag);
                    RemoveDrawObject(StraddleHardStopLongLabelTag);
                    RemoveDrawObject(StraddleHardStopShortLabelTag);
                    straddleLongZoneRect = null;
                    straddleShortZoneRect = null;
                    straddleHardStopLongLine = null;
                    straddleHardStopShortLine = null;
                }

                string state = !straddleRangeReady ? "STRADDLE: building"
                    : (straddleArmed ? "STRADDLE: armed" : "STRADDLE: waiting");
                string window = straddleEventTime != DateTime.MinValue
                    ? $" {straddleRangeStart:HH:mm}-{straddleRangeEnd:HH:mm}"
                    : string.Empty;
                string rangeText = hasRange ? $" H {straddleRangeHigh:F2} L {straddleRangeLow:F2}" : string.Empty;
                straddleSummary = state + window + rangeText;
            }
            else
            {
                RemoveDrawObject(StraddleRangeTag);
                RemoveDrawObject(StraddleLongZoneTag);
                RemoveDrawObject(StraddleShortZoneTag);
                RemoveDrawObject(StraddleStatusTag);
                RemoveDrawObject(StraddleCountdownTag);
                RemoveDrawObject(StraddleHardStopLongTag);
                RemoveDrawObject(StraddleHardStopShortTag);
                RemoveDrawObject(StraddleHardStopLongLabelTag);
                RemoveDrawObject(StraddleHardStopShortLabelTag);
                straddleLongZoneRect = null;
                straddleShortZoneRect = null;
                straddleHardStopLongLine = null;
                straddleHardStopShortLine = null;
            }

            if (!string.IsNullOrEmpty(orbSummary) || !string.IsNullOrEmpty(chopSummary) || !string.IsNullOrEmpty(straddleSummary))
            {
                string filterText = orbSummary;
                if (!string.IsNullOrEmpty(chopSummary))
                    filterText = string.IsNullOrEmpty(filterText) ? chopSummary : filterText + "\n" + chopSummary;
                if (!string.IsNullOrEmpty(straddleSummary))
                    filterText = string.IsNullOrEmpty(filterText) ? straddleSummary : filterText + "\n" + straddleSummary;

                var font = new SimpleFont("Arial", 12) { Bold = true };
                Draw.TextFixed(this, FilterStatusTag, filterText, TextPosition.BottomRight, Brushes.White, font, Brushes.Transparent, Brushes.Black, 50);
            }
            else
            {
                RemoveDrawObject(FilterStatusTag);
            }
        }

        private void UpdateTradePnlLabelVisibility(bool force = false)
        {
            if (!force && BarsInProgress != 0)
                return;

            if (lastShowTradePnlTags == ShowTradePnlTags)
                return;

            if (ShowTradePnlTags)
                RedrawTradePnlLabels();
            else
                ClearTradePnlLabels();

            lastShowTradePnlTags = ShowTradePnlTags;
        }

        private void UpdateIndicatorVisuals(bool force = false)
        {
            if (!indicatorVisualsPrimed && ChartControl != null)
                PrimeIndicatorVisuals();

            bool desiredSma = ShowSmaVisuals;
            if (desiredSma && sma == null)
                EnsureSmaIndicatorInstance();
            SyncIndicatorVisual(sma, desiredSma, ref lastSmaVisualActive, force);

            bool desiredEma = ShowEmaVisuals;
            if (desiredEma && (emaFast == null || emaSlow == null))
                EnsureEmaIndicatorInstances();
            SyncEmaVisuals(desiredEma, force);

            bool desiredRsi = ShowRsiVisuals;
            if (desiredRsi && rsi == null)
                EnsureRsiIndicatorInstance();
            SyncIndicatorVisual(rsi, desiredRsi, ref lastRsiVisualActive, force);

            bool desiredMacd = ShowMacdVisuals;
            if (desiredMacd && macd == null)
                EnsureMacdIndicatorInstance();
            SyncIndicatorVisual(macd, desiredMacd, ref lastMacdVisualActive, force);

            bool desiredAtr = ShowAtrVisuals;
            if (desiredAtr && atr == null)
                EnsureAtrIndicatorInstance();
            SyncIndicatorVisual(atr, desiredAtr, ref lastAtrVisualActive, force);

            bool desiredBb = ShowChopBbVisuals;
            if (desiredBb && bbChop == null)
                EnsureChopBbIndicatorInstance();
            SyncIndicatorVisual(bbChop, desiredBb, ref lastBbVisualActive, force);

            bool desiredVwap = ShowVwapMrVisuals || UseVwapDirectionGate;
            if (desiredVwap && vwapSeries == null)
                EnsureVwapMrSeries();
            if (force || lastVwapVisualActive != desiredVwap)
            {
                if (!desiredVwap)
                    ClearVwapMrLines();
                lastVwapVisualActive = desiredVwap;
            }
        }

        private void PrimeIndicatorVisuals()
        {
            if (indicatorVisualsPrimed || ChartControl == null)
                return;

            EnsureSmaIndicatorInstance();
            EnsureEmaIndicatorInstances();
            EnsureRsiIndicatorInstance();
            EnsureMacdIndicatorInstance();
            EnsureAtrIndicatorInstance();
            EnsureChopBbIndicatorInstance();

            EnsureIndicatorAttached(sma, ShowSmaVisuals);
            EnsureIndicatorAttached(emaFast, ShowEmaVisuals);
            EnsureIndicatorAttached(emaSlow, ShowEmaVisuals);
            EnsureIndicatorAttached(rsi, ShowRsiVisuals);
            EnsureIndicatorAttached(macd, ShowMacdVisuals);
            EnsureIndicatorAttached(atr, ShowAtrVisuals);
            EnsureIndicatorAttached(bbChop, ShowChopBbVisuals);

            indicatorVisualsPrimed = true;
        }

        private void EnsureSmaIndicatorInstance()
        {
            if (sma == null)
                sma = SMA(Closes[0], SmaPeriod);

            if (sma != null && sma.Plots != null && sma.Plots.Count() > 0)
            {
                sma.Plots[0].Brush = Brushes.Silver;
                sma.Plots[0].Width = 2;
            }
            CacheIndicatorPlotStyles(sma);
        }

        private void EnsureEmaIndicatorInstances()
        {
            if (emaFast == null)
                emaFast = EMA(Closes[0], EmaFast);
            if (emaFast != null && emaFast.Plots != null && emaFast.Plots.Count() > 0)
            {
                emaFast.Plots[0].Brush = Brushes.DodgerBlue;
                emaFast.Plots[0].Width = 2;
            }
            CacheIndicatorPlotStyles(emaFast);

            if (emaSlow == null)
                emaSlow = EMA(Closes[0], EmaSlow);
            if (emaSlow != null && emaSlow.Plots != null && emaSlow.Plots.Count() > 0)
            {
                emaSlow.Plots[0].Brush = Brushes.MediumVioletRed;
                emaSlow.Plots[0].Width = 2;
            }
            CacheIndicatorPlotStyles(emaSlow);
        }

        private void EnsureRsiIndicatorInstance()
        {
            if (rsi == null)
                rsi = RSI(Closes[0], RsiPeriod, RsiSmooth);

            if (rsi != null && rsi.Plots != null && rsi.Plots.Count() > 0)
            {
                rsi.IsOverlay = false;
                rsi.DrawOnPricePanel = false;
                rsi.PaintPriceMarkers = false;
                rsi.Plots[0].Brush = Brushes.Gold;
                rsi.Plots[0].Width = 2;
                if (rsi.Plots.Count() > 1)
                {
                    rsi.Plots[1].Brush = Brushes.DimGray;
                    rsi.Plots[1].Width = 1;
                }
            }
            CacheIndicatorPlotStyles(rsi);
        }

        private void EnsureMacdIndicatorInstance()
        {
            if (macd == null)
                macd = MACD(Closes[0], MacdFast, MacdSlow, MacdSmooth);

            if (macd != null && macd.Plots != null && macd.Plots.Count() > 0)
            {
                macd.IsOverlay = false;
                macd.DrawOnPricePanel = false;
                macd.PaintPriceMarkers = false;
                macd.Plots[0].Brush = Brushes.DodgerBlue;
                macd.Plots[0].Width = 2;
                if (macd.Plots.Count() > 1)
                {
                    macd.Plots[1].Brush = Brushes.OrangeRed;
                    macd.Plots[1].Width = 1;
                }
                if (macd.Plots.Count() > 2)
                {
                    macd.Plots[2].Brush = Brushes.Gray;
                    macd.Plots[2].Width = 1;
                }
            }
            CacheIndicatorPlotStyles(macd);
        }

        private void EnsureAtrIndicatorInstance()
        {
            if (atr == null)
                atr = ATR(Closes[0], AtrPeriod);

            if (atr != null && atr.Plots != null && atr.Plots.Count() > 0)
            {
                atr.IsOverlay = false;
                atr.DrawOnPricePanel = false;
                atr.PaintPriceMarkers = false;
                atr.Plots[0].Brush = Brushes.DeepSkyBlue;
                atr.Plots[0].Width = 2;
            }
            CacheIndicatorPlotStyles(atr);
        }

        private void EnsureChopBbIndicatorInstance()
        {
            if (bbChop == null)
                bbChop = Bollinger(Closes[0], ChopBollingerStdDev, ChopBollingerPeriod);
            if (bbChop != null && bbChop.Plots != null && bbChop.Plots.Count() >= 3)
            {
                bbChop.IsOverlay = true;
                bbChop.DrawOnPricePanel = true;
                bbChop.PaintPriceMarkers = false;
                bbChop.Plots[0].Brush = Brushes.DimGray;
                bbChop.Plots[1].Brush = Brushes.Gray;
                bbChop.Plots[2].Brush = Brushes.DimGray;
                bbChop.Plots[0].Width = 1;
                bbChop.Plots[1].Width = 1;
                bbChop.Plots[2].Width = 1;
            }
            CacheIndicatorPlotStyles(bbChop);
        }

        private void SyncEmaVisuals(bool desired, bool force)
        {
            bool needsAttach = desired && ((emaFast != null && !indicatorAttached.Contains(emaFast)) || (emaSlow != null && !indicatorAttached.Contains(emaSlow)));
            if (!force && lastEmaVisualActive == desired && !needsAttach)
                return;

            if (emaFast == null || emaSlow == null)
            {
                lastEmaVisualActive = false;
                return;
            }

            if (desired)
            {
                EnsureIndicatorAttached(emaFast, desired);
                EnsureIndicatorAttached(emaSlow, desired);
            }

            if (indicatorAttached.Contains(emaFast))
                SetIndicatorPlotVisibility(emaFast, desired);
            if (indicatorAttached.Contains(emaSlow))
                SetIndicatorPlotVisibility(emaSlow, desired);

            bool attached = !desired || (indicatorAttached.Contains(emaFast) && indicatorAttached.Contains(emaSlow));
            lastEmaVisualActive = attached ? desired : false;
        }

        private void SyncIndicatorVisual(Indicator indicator, bool desired, ref bool lastState, bool force)
        {
            bool needsAttach = desired && indicator != null && !indicatorAttached.Contains(indicator);
            if (!force && lastState == desired && !needsAttach)
                return;

            if (indicator == null)
            {
                lastState = false;
                return;
            }

            if (desired)
                EnsureIndicatorAttached(indicator, desired);
            if (indicatorAttached.Contains(indicator))
                SetIndicatorPlotVisibility(indicator, desired);

            lastState = desired && indicatorAttached.Contains(indicator);
        }

        private void EnsureIndicatorAttached(Indicator indicator, bool? desiredVisibility = null)
        {
            if (indicator == null)
                return;

            if (desiredVisibility.HasValue && !desiredVisibility.Value)
            {
                if (indicatorAttached.Contains(indicator))
                    SetIndicatorPlotVisibility(indicator, false);
                return;
            }

            try
            {
                if (ChartControl == null)
                    return;

                Action attach = () =>
                {
                    try
                    {
                        if (!indicatorAttached.Contains(indicator))
                        {
                            AddChartIndicator(indicator);
                            indicatorAttached.Add(indicator);
                        }
                        if (desiredVisibility.HasValue)
                            SetIndicatorPlotVisibilityCore(indicator, desiredVisibility.Value);
                    }
                    catch (Exception ex)
                    {
                        if (Debug)
                            StrategyLogDebug($"[VISUALS] AddChartIndicator failed: {ex.Message}");
                    }
                };

                if (!ChartControl.Dispatcher.CheckAccess())
                    ChartControl.Dispatcher.InvokeAsync(attach);
                else
                    attach();
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[VISUALS] AddChartIndicator failed: {ex.Message}");
            }
        }

        private void CacheIndicatorPlotStyles(Indicator indicator)
        {
            if (indicator == null || indicator.Plots == null || indicator.Plots.Count() == 0)
                return;

            if (indicatorPlotBrushes.ContainsKey(indicator))
                return;

            int count = indicator.Plots.Count();
            var brushes = new Brush[count];
            var widths = new float[count];
            for (int i = 0; i < count; i++)
            {
                brushes[i] = indicator.Plots[i].Brush;
                widths[i] = indicator.Plots[i].Width;
            }

            indicatorPlotBrushes[indicator] = brushes;
            indicatorPlotWidths[indicator] = widths;
        }

        private void AttachIndicatorForVisuals(Indicator indicator, bool visible)
        {
            if (indicator == null)
                return;

            if (indicatorAttached.Contains(indicator))
            {
                SetIndicatorPlotVisibilityCore(indicator, visible);
                return;
            }

            try
            {
                AddChartIndicator(indicator);
                indicatorAttached.Add(indicator);
                SetIndicatorPlotVisibilityCore(indicator, visible);
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug($"[VISUALS] AddChartIndicator failed (init): {ex.Message}");
            }
        }

        private void SetIndicatorPlotVisibility(Indicator indicator, bool visible)
        {
            if (indicator == null)
                return;

            if (ChartControl == null)
            {
                SetIndicatorPlotVisibilityCore(indicator, visible);
                return;
            }

            if (!ChartControl.Dispatcher.CheckAccess())
            {
                ChartControl.Dispatcher.InvokeAsync(() => SetIndicatorPlotVisibilityCore(indicator, visible));
                return;
            }

            SetIndicatorPlotVisibilityCore(indicator, visible);
        }

        private void SetIndicatorPlotVisibilityCore(Indicator indicator, bool visible)
        {
            if (indicator == null || indicator.Plots == null || indicator.Plots.Count() == 0)
                return;

            CacheIndicatorPlotStyles(indicator);

            Brush[] brushes;
            float[] widths;
            indicatorPlotBrushes.TryGetValue(indicator, out brushes);
            indicatorPlotWidths.TryGetValue(indicator, out widths);

            for (int i = 0; i < indicator.Plots.Count(); i++)
            {
                if (visible)
                {
                    if (brushes != null && i < brushes.Length)
                        indicator.Plots[i].Brush = brushes[i];
                    if (widths != null && i < widths.Length)
                        indicator.Plots[i].Width = widths[i];
                }
                else
                {
                    indicator.Plots[i].Brush = Brushes.Transparent;
                    indicator.Plots[i].Width = 0;
                }
            }

            if (ChartControl != null)
                ChartControl.InvalidateVisual();
        }

        private bool IsPanelIndicator(Indicator indicator)
        {
            if (indicator == null)
                return false;

            return !indicator.IsOverlay && !indicator.DrawOnPricePanel;
        }

        private void RemoveIndicatorFromChart(Indicator indicator)
        {
            if (indicator == null || ChartControl == null)
                return;

            Action remove = () =>
            {
                try
                {
                    // Use reflection to access indicator collections (API differs across NT builds).
                    bool removed = false;
                    var ctrlType = ChartControl.GetType();
                    var indicatorsProp = ctrlType.GetProperty("Indicators");
                    if (indicatorsProp != null)
                    {
                        object indicatorsObj = indicatorsProp.GetValue(ChartControl, null);
                        if (indicatorsObj != null)
                        {
                            var removeMethod = indicatorsObj.GetType().GetMethod("Remove");
                            if (removeMethod != null)
                            {
                                removeMethod.Invoke(indicatorsObj, new object[] { indicator });
                                removed = true;
                            }
                        }
                    }

                    if (!removed && ChartControl.ChartPanels != null)
                    {
                        foreach (var panel in ChartControl.ChartPanels)
                        {
                            if (panel == null)
                                continue;

                            var panelIndicatorsProp = panel.GetType().GetProperty("Indicators");
                            if (panelIndicatorsProp == null)
                                continue;

                            object panelIndicatorsObj = panelIndicatorsProp.GetValue(panel, null);
                            if (panelIndicatorsObj == null)
                                continue;

                            var removeMethod = panelIndicatorsObj.GetType().GetMethod("Remove");
                            if (removeMethod == null)
                                continue;

                            removeMethod.Invoke(panelIndicatorsObj, new object[] { indicator });
                            removed = true;
                            break;
                        }
                    }

                    indicatorAttached.Remove(indicator);
                }
                catch (Exception ex)
                {
                    if (Debug)
                        StrategyLogDebug($"[VISUALS] Remove indicator failed: {ex.Message}");
                }
            };

            if (!ChartControl.Dispatcher.CheckAccess())
                ChartControl.Dispatcher.InvokeAsync(remove);
            else
                remove();
        }

        private void ClearVwapMrLines()
        {
            if (DrawObjects == null)
                return;

            var tags = new List<string>();
            foreach (var obj in DrawObjects)
            {
                if (obj == null || string.IsNullOrEmpty(obj.Tag))
                    continue;

                string tag = obj.Tag;
                if (tag.StartsWith(VwapLineTagPrefix) ||
                    tag.StartsWith(VwapBand1UpperTagPrefix) ||
                    tag.StartsWith(VwapBand2UpperTagPrefix) ||
                    tag.StartsWith(VwapBand1LowerTagPrefix) ||
                    tag.StartsWith(VwapBand2LowerTagPrefix) ||
                    tag.StartsWith(VwapBand3UpperTagPrefix) ||
                    tag.StartsWith(VwapBand3LowerTagPrefix))
                {
                    tags.Add(tag);
                }
            }

            foreach (var tag in tags)
                RemoveDrawObject(tag);
        }

        private void UpdateIndicatorVisualButtons(bool force = false)
        {
            if (smaVisualToggleButton == null || ChartControl == null)
                return;

            bool smaState = ShowSmaVisuals;
            bool emaState = ShowEmaVisuals;
            bool rsiState = ShowRsiVisuals;
            bool macdState = ShowMacdVisuals;
            bool atrState = ShowAtrVisuals;
            bool bbState = ShowChopBbVisuals;
            bool vwapState = ShowVwapMrVisuals || UseVwapDirectionGate;

            if (!force &&
                lastSmaVisualToggleState == smaState &&
                lastEmaVisualToggleState == emaState &&
                lastRsiVisualToggleState == rsiState &&
                lastMacdVisualToggleState == macdState &&
                lastAtrVisualToggleState == atrState &&
                lastBbVisualToggleState == bbState &&
                lastVwapVisualToggleState == vwapState)
                return;

            lastSmaVisualToggleState = smaState;
            lastEmaVisualToggleState = emaState;
            lastRsiVisualToggleState = rsiState;
            lastMacdVisualToggleState = macdState;
            lastAtrVisualToggleState = atrState;
            lastBbVisualToggleState = bbState;
            lastVwapVisualToggleState = vwapState;

            Action apply = () =>
            {
                ApplyIndicatorVisualButtonStyle(smaVisualToggleButton, "SMA", smaState);
                ApplyIndicatorVisualButtonStyle(emaVisualToggleButton, "EMA", emaState);
                ApplyIndicatorVisualButtonStyle(rsiVisualToggleButton, "RSI", rsiState);
                ApplyIndicatorVisualButtonStyle(macdVisualToggleButton, "MACD", macdState);
                ApplyIndicatorVisualButtonStyle(atrVisualToggleButton, "ATR", atrState);
                ApplyIndicatorVisualButtonStyle(bbVisualToggleButton, "Chop BB", bbState);
                ApplyIndicatorVisualButtonStyle(vwapVisualToggleButton, "VWAP", vwapState);
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateVisualsPanelVisibility(bool force = false)
        {
            if (visualButtonsPanel == null || visualsToggleButton == null || ChartControl == null)
                return;

            Action apply = () =>
            {
                visualButtonsPanel.Visibility = visualsPanelExpanded ? Visibility.Visible : Visibility.Collapsed;
                visualsToggleButton.Content = visualsPanelExpanded ? "Visuals ▾" : "Visuals ▸";
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void ApplyIndicatorVisualButtonStyle(Button button, string label, bool enabled)
        {
            if (button == null)
                return;

            button.Content = string.Format("{0}: {1}", label, enabled ? "ON" : "OFF");
            button.Background = enabled ? Brushes.SeaGreen : Brushes.DimGray;
            button.Foreground = enabled ? Brushes.White : Brushes.LightGray;
            button.Opacity = enabled ? 1.0 : 0.7;
        }

        private void UpdatePnlTagToggleButton(bool force = false)
        {
            if (pnlTagsToggleButton == null || ChartControl == null)
                return;

            bool desiredState = ShowTradePnlTags;
            if (!force && lastPnlTagToggleState == desiredState)
                return;

            lastPnlTagToggleState = desiredState;

            Action apply = () =>
            {
                if (pnlTagsToggleButton == null)
                    return;

                pnlTagsToggleButton.Content = desiredState ? "PnL Tags: ON" : "PnL Tags: OFF";
                pnlTagsToggleButton.Background = desiredState ? Brushes.SeaGreen : Brushes.LightCoral;
                pnlTagsToggleButton.Foreground = desiredState ? Brushes.White : Brushes.Black;
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateReverseSignalToggleButton(bool force = false)
        {
            if (reverseSignalToggleButton == null || ChartControl == null)
                return;

            bool desiredState = ReverseSignalTrading;
            if (!force && lastReverseSignalToggleState == desiredState)
                return;

            lastReverseSignalToggleState = desiredState;

            Action apply = () =>
            {
                if (reverseSignalToggleButton == null)
                    return;

                ApplyIndicatorVisualButtonStyle(reverseSignalToggleButton, "Reverse", desiredState);
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateBiasToggleButtons(bool force = false)
        {
            if (biasBothToggleButton == null || ChartControl == null)
                return;

            TradeBias desired = Bias;
            if (!force && lastBiasToggleValue == desired)
                return;

            lastBiasToggleValue = desired;

            Action apply = () =>
            {
                if (biasBothToggleButton == null || biasLongToggleButton == null || biasShortToggleButton == null)
                    return;

                bool bothOn = desired == TradeBias.Both;
                bool longOn = desired == TradeBias.LongOnly;
                bool shortOn = desired == TradeBias.ShortOnly;

                ApplyBiasButtonStyle(biasBothToggleButton, bothOn);
                ApplyBiasButtonStyle(biasLongToggleButton, longOn);
                ApplyBiasButtonStyle(biasShortToggleButton, shortOn);
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void ApplyBiasButtonStyle(Button button, bool active)
        {
            if (button == null)
                return;

            button.Background = active ? Brushes.SteelBlue : Brushes.DimGray;
            button.Foreground = active ? Brushes.White : Brushes.LightGray;
            button.Opacity = active ? 1.0 : 0.7;
        }

        private void UpdateVwapGateToggleButton(bool force = false)
        {
            if (vwapGateToggleButton == null || ChartControl == null)
                return;

            bool desiredState = UseVwapDirectionGate;
            if (!force && lastVwapGateToggleState == desiredState)
                return;

            lastVwapGateToggleState = desiredState;

            Action apply = () =>
            {
                ApplyIndicatorVisualButtonStyle(vwapGateToggleButton, "VWAP Gate", desiredState);
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateManualTradeButtons(bool force = false)
        {
            if ((manualBuyButton == null && manualSellButton == null && manualLimitButton == null && manualStopButton == null) || ChartControl == null)
                return;

            bool enabled = manualHaltActive && State == State.Realtime;
            if (!force && lastManualButtonsEnabled == enabled)
                return;

            lastManualButtonsEnabled = enabled;

            Action apply = () =>
            {
                if (manualBuyButton != null)
                {
                    manualBuyButton.IsEnabled = enabled;
                    manualBuyButton.Background = enabled ? Brushes.DarkGreen : Brushes.DimGray;
                    manualBuyButton.Foreground = enabled ? Brushes.White : Brushes.LightGray;
                    manualBuyButton.Opacity = enabled ? 1.0 : 0.6;
                    manualBuyButton.ToolTip = enabled
                        ? "Manual trades enabled while strategy is halted."
                        : "Enable by clicking Flatten + Halt.";
                }

                if (manualSellButton != null)
                {
                    manualSellButton.IsEnabled = enabled;
                    manualSellButton.Background = enabled ? Brushes.DarkRed : Brushes.DimGray;
                    manualSellButton.Foreground = enabled ? Brushes.White : Brushes.LightGray;
                    manualSellButton.Opacity = enabled ? 1.0 : 0.6;
                    manualSellButton.ToolTip = enabled
                        ? "Manual trades enabled while strategy is halted."
                        : "Enable by clicking Flatten + Halt.";
                }

                if (manualLimitButton != null)
                {
                    manualLimitButton.IsEnabled = enabled;
                    manualLimitButton.Background = enabled ? Brushes.DarkGreen : Brushes.DimGray;
                    manualLimitButton.Foreground = enabled ? Brushes.White : Brushes.LightGray;
                    manualLimitButton.Opacity = enabled ? 1.0 : 0.6;
                    manualLimitButton.ToolTip = enabled
                        ? $"Place buy limit {ManualEntryOffsetTicks} ticks above current price."
                        : "Enable by clicking Flatten + Halt.";
                }

                if (manualStopButton != null)
                {
                    manualStopButton.IsEnabled = enabled;
                    manualStopButton.Background = enabled ? Brushes.DarkRed : Brushes.DimGray;
                    manualStopButton.Foreground = enabled ? Brushes.White : Brushes.LightGray;
                    manualStopButton.Opacity = enabled ? 1.0 : 0.6;
                    manualStopButton.ToolTip = enabled
                        ? $"Place sell stop {ManualEntryOffsetTicks} ticks below current price."
                        : "Enable by clicking Flatten + Halt.";
                }
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateAddOnTradeButton(bool force = false)
        {
            if (addOnTradeButton == null || ChartControl == null)
                return;

            bool enabled = EnableScaleInTrades &&
                           State == State.Realtime &&
                           !shutdownInProgress &&
                           !dailyPnLLimitHalted &&
                           !desyncHoldActive &&
                           Position != null &&
                           Position.MarketPosition != MarketPosition.Flat &&
                           Position.Quantity != 0;
            if (!force && lastAddOnButtonEnabled == enabled)
                return;

            lastAddOnButtonEnabled = enabled;

            Action apply = () =>
            {
                if (addOnTradeButton == null)
                    return;

                addOnTradeButton.IsEnabled = enabled;
                addOnTradeButton.Background = enabled ? Brushes.SteelBlue : Brushes.DimGray;
                addOnTradeButton.Foreground = enabled ? Brushes.White : Brushes.LightGray;
                addOnTradeButton.Opacity = enabled ? 1.0 : 0.6;
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateTradesPerEntryInput(bool force = false)
        {
            if (tradesPerEntryTextBox == null || ChartControl == null)
                return;

            if (!force && tradesPerEntryTextBox.IsKeyboardFocusWithin)
                return;

            int desiredValue = GetEffectiveTradesPerEntry();
            if (!force && lastTradesPerEntryDisplay == desiredValue)
                return;

            lastTradesPerEntryDisplay = desiredValue;

            Action apply = () =>
            {
                if (tradesPerEntryTextBox == null)
                    return;

                tradesPerEntryTextBox.Text = desiredValue.ToString();
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void UpdateChopTradesPerEntryInput(bool force = false)
        {
            if (chopTradesPerEntryTextBox == null || ChartControl == null)
                return;

            if (!force && chopTradesPerEntryTextBox.IsKeyboardFocusWithin)
                return;

            int desiredValue = GetEffectiveChopTradesPerEntry();
            if (!force && lastChopTradesPerEntryDisplay == desiredValue)
                return;

            lastChopTradesPerEntryDisplay = desiredValue;

            Action apply = () =>
            {
                if (chopTradesPerEntryTextBox == null)
                    return;

                chopTradesPerEntryTextBox.Text = desiredValue.ToString();
            };

            if (ChartControl.Dispatcher.CheckAccess())
                apply();
            else
                ChartControl.Dispatcher.InvokeAsync(apply);
        }

        private void ClearTradePnlLabels()
        {
            List<PnlLabelInfo> snapshot;
            lock (pnlLabelLock)
            {
                if (pnlLabelInfos == null || pnlLabelInfos.Count == 0)
                    return;
                snapshot = pnlLabelInfos.ToList();
            }

            foreach (var info in snapshot)
            {
                if (!string.IsNullOrEmpty(info.Tag))
                    RemoveDrawObject(info.Tag);
            }
        }

        private void RedrawTradePnlLabels()
        {
            List<PnlLabelInfo> snapshot;
            lock (pnlLabelLock)
            {
                if (pnlLabelInfos == null || pnlLabelInfos.Count == 0)
                    return;
                snapshot = pnlLabelInfos.ToList();
            }

            foreach (var info in snapshot)
            {
                int barIndex = Bars.GetBar(info.Time);
                if (barIndex < 0)
                    continue;
                int barsAgo = Math.Max(0, CurrentBar - barIndex);
                Brush brush = info.IsProfit ? Brushes.LimeGreen : Brushes.Red;
                Draw.Text(this, info.Tag, false, info.Label, barsAgo, info.Price, 0, brush, new SimpleFont("Arial", 12), TextAlignment.Center, null, null, 0);
            }
        }

        private void UpdateOpeningRange()
        {
            if (BarsInProgress != 0)
                return;

            if (orbSessionIterator == null)
                orbSessionIterator = new SessionIterator(Bars);

            DateTime barTime = Time[0];
            bool newSession = Bars.IsFirstBarOfSession || orbSessionStart == DateTime.MinValue || (orbSessionEnd != DateTime.MinValue && barTime >= orbSessionEnd);
            if (newSession)
                InitializeOrbSession(barTime);

            if (orbSessionStart == DateTime.MinValue)
                return;

            if (!orbRangeReady)
            {
                if (barTime >= orbSessionStart && barTime < orbEndTime)
                {
                    if (orbHigh == double.MinValue && orbLow == double.MaxValue)
                        BackfillOrbRange(barTime);

                    orbHigh = Math.Max(orbHigh, High[0]);
                    orbLow = Math.Min(orbLow, Low[0]);
                }

                if (barTime >= orbEndTime)
                {
                    if (orbHigh == double.MinValue || orbLow == double.MaxValue)
                        BackfillOrbRange();

                    orbRangeReady = orbHigh > double.MinValue && orbLow < double.MaxValue;
                    if (!orbRangeReady)
                        orbBreakoutSatisfied = true;
                }
            }
        }

        private void UpdateStraddleState(DateTime barTime)
        {
            if (!EnableStraddleTrades)
            {
                straddleArmed = false;
                return;
            }

            if (straddleSessionIterator == null)
                straddleSessionIterator = new SessionIterator(Bars);

            bool newSession = Bars.IsFirstBarOfSession || straddleSessionStart == DateTime.MinValue ||
                (straddleSessionEnd != DateTime.MinValue && barTime >= straddleSessionEnd);
            if (newSession)
                InitializeStraddleSession(barTime);

            if (straddleEventTime == DateTime.MinValue)
                return;
            DateTime armTime = GetStraddleArmTime();

            if (!straddleRangeReady)
            {
                if (barTime >= straddleRangeStart && barTime < straddleRangeEnd)
                {
                    if (straddleRangeHigh == double.MinValue && straddleRangeLow == double.MaxValue)
                        BackfillStraddleRange(barTime);

                    straddleRangeHigh = Math.Max(straddleRangeHigh, High[0]);
                    straddleRangeLow = Math.Min(straddleRangeLow, Low[0]);
                }

                if (barTime >= straddleRangeEnd)
                {
                    if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                        BackfillStraddleRange(straddleRangeEnd);

                    straddleRangeReady = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
                }
                else if (armTime != DateTime.MinValue && barTime >= armTime &&
                    straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue)
                {
                    straddleRangeReady = true;
                }
            }

            if (straddleRangeReady)
                UpdateStraddleZones();

            straddleArmed = straddleRangeReady && armTime != DateTime.MinValue &&
                barTime >= armTime && barTime <= straddleWindowEnd;
            if (barTime > straddleWindowEnd && !IsStraddleTradeOpen())
                straddleArmed = false;
            if (straddleLongTriggered && straddleShortTriggered && !IsStraddleTradeOpen())
                straddleArmed = false;
        }

        private void InitializeStraddleSession(DateTime barTime)
        {
            if (straddleSessionIterator == null)
                return;

            bool hasSession = straddleSessionIterator.GetNextSession(barTime, true);
            if (!hasSession)
                return;

            straddleSessionStart = straddleSessionIterator.ActualSessionBegin;
            straddleSessionEnd = straddleSessionIterator.ActualSessionEnd;
            straddleEventTime = GetFixedStraddleStart(straddleSessionEnd);

            int rangeMinutes = Math.Max(1, StraddleRangeMinutes);
            straddleRangeStart = straddleEventTime.AddMinutes(-rangeMinutes);
            straddleRangeEnd = straddleEventTime;
            straddleWindowEnd = straddleEventTime.AddMinutes(rangeMinutes);

            straddleRangeHigh = double.MinValue;
            straddleRangeLow = double.MaxValue;
            straddleRangeReady = false;
            straddleArmed = false;
            straddleLongTriggered = false;
            straddleShortTriggered = false;
            straddleLongManualOffsetTicks = null;
            straddleShortManualOffsetTicks = null;
            straddleLongAutoCenter = null;
            straddleShortAutoCenter = null;
            straddleHardStopLongManualOffsetTicks = null;
            straddleHardStopShortManualOffsetTicks = null;
            straddleHardStopLongAutoPrice = null;
            straddleHardStopShortAutoPrice = null;
            straddleLongZoneRect = null;
            straddleShortZoneRect = null;
            straddleHardStopLongLine = null;
            straddleHardStopShortLine = null;
            straddlePendingLongTradeIds.Clear();
            straddlePendingShortTradeIds.Clear();
            straddleZonesFrozen = false;
            straddleRangeShift = 0;
            UpdateStraddleZones();
        }

        private DateTime GetFixedStraddleStart(DateTime sessionEnd)
        {
            DateTime baseDate = sessionEnd != DateTime.MinValue
                ? sessionEnd.Date
                : (Time != null && Time.Count > 0 ? Time[0].Date : DateTime.Today);
            int hour = Math.Max(0, Math.Min(23, StraddleStartHour));
            int minute = Math.Max(0, Math.Min(59, StraddleStartMinute));
            return baseDate.AddHours(hour).AddMinutes(minute);
        }

        private DateTime GetStraddleArmTime()
        {
            if (straddleRangeEnd == DateTime.MinValue)
                return DateTime.MinValue;
            DateTime armTime = straddleRangeEnd - StraddlePreArmOffset;
            if (straddleRangeStart != DateTime.MinValue && armTime < straddleRangeStart)
                armTime = straddleRangeStart;
            return armTime;
        }

        private void BackfillStraddleRange(DateTime endTime)
        {
            int startBarsAgo = FindBarsAgoForTime(straddleRangeStart);
            int endBarsAgo = FindBarsAgoForTime(endTime);
            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            if (startBarsAgo < endBarsAgo)
            {
                int tmp = startBarsAgo;
                startBarsAgo = endBarsAgo;
                endBarsAgo = tmp;
            }

            for (int i = startBarsAgo; i >= endBarsAgo; i--)
            {
                straddleRangeHigh = Math.Max(straddleRangeHigh, Highs[0][i]);
                straddleRangeLow = Math.Min(straddleRangeLow, Lows[0][i]);
            }
        }

        private void UpdateStraddleZones()
        {
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = TickSize;

            int zoneTicks = Math.Max(1, StraddleZoneTicks);
            double zoneHeight = zoneTicks * tickSize;
            double halfZone = zoneHeight * 0.5;

            DateTime now = lastMarketDataTime != DateTime.MinValue
                ? lastMarketDataTime
                : (Time != null && Time.Count > 0 ? Time[0] : DateTime.MinValue);
            bool inWindow = now != DateTime.MinValue &&
                straddleRangeStart != DateTime.MinValue &&
                straddleWindowEnd != DateTime.MinValue &&
                now >= straddleRangeStart && now <= straddleWindowEnd;

            DateTime armTime = GetStraddleArmTime();
            if (!straddleZonesFrozen && armTime != DateTime.MinValue && now >= armTime)
                straddleZonesFrozen = true;

            double referencePrice;
            if (!straddleZonesFrozen && inWindow && armTime != DateTime.MinValue && now < armTime &&
                straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue &&
                TryGetStraddleTrackingPrice(out referencePrice))
            {
                double longOffsetTicks = GetEffectiveStraddleZoneOffsetTicks(MarketPosition.Long);
                double shortOffsetTicks = GetEffectiveStraddleZoneOffsetTicks(MarketPosition.Short);
                double baseLongCenter = straddleRangeHigh + longOffsetTicks * tickSize;
                double baseShortCenter = straddleRangeLow - shortOffsetTicks * tickSize;
                double baseLongLower = baseLongCenter - halfZone;
                double baseLongUpper = baseLongCenter + halfZone;
                double baseShortUpper = baseShortCenter + halfZone;
                double baseShortLower = baseShortCenter - halfZone;

                double shift = straddleRangeShift;

                double currentLongLower = baseLongLower + shift;
                double currentShortUpper = baseShortUpper + shift;
                double buffer = 5 * tickSize;
                double shiftUp = 0;
                double shiftDown = 0;
                if (referencePrice >= currentLongLower - buffer)
                    shiftUp = (referencePrice + buffer) - currentLongLower;
                if (referencePrice <= currentShortUpper + buffer)
                    shiftDown = (referencePrice - buffer) - currentShortUpper;

                if (shiftUp != 0 && shiftDown != 0)
                    shift += Math.Abs(shiftUp) >= Math.Abs(shiftDown) ? shiftUp : shiftDown;
                else if (shiftUp != 0)
                    shift += shiftUp;
                else if (shiftDown != 0)
                    shift += shiftDown;

                straddleLongZoneUpper = baseLongUpper + shift;
                straddleLongZoneLower = baseLongLower + shift;
                straddleShortZoneUpper = baseShortUpper + shift;
                straddleShortZoneLower = baseShortLower + shift;
                straddleLongAutoCenter = (straddleLongZoneUpper + straddleLongZoneLower) * 0.5;
                straddleShortAutoCenter = (straddleShortZoneUpper + straddleShortZoneLower) * 0.5;
                straddleHardStopLongAutoPrice = straddleShortZoneUpper + tickSize;
                straddleHardStopShortAutoPrice = straddleLongZoneLower - tickSize;
                straddleRangeShift = shift;
                straddleLongManualOffsetTicks = null;
                straddleShortManualOffsetTicks = null;
                straddleHardStopLongManualOffsetTicks = null;
                straddleHardStopShortManualOffsetTicks = null;
                return;
            }

            if (straddleZonesFrozen && straddleLongAutoCenter.HasValue && straddleShortAutoCenter.HasValue)
            {
                double longFrozenCenter = straddleLongAutoCenter.Value;
                double shortFrozenCenter = straddleShortAutoCenter.Value;

                straddleLongZoneUpper = longFrozenCenter + halfZone;
                straddleLongZoneLower = longFrozenCenter - halfZone;
                straddleShortZoneUpper = shortFrozenCenter + halfZone;
                straddleShortZoneLower = shortFrozenCenter - halfZone;
                straddleHardStopLongAutoPrice = straddleShortZoneUpper + tickSize;
                straddleHardStopShortAutoPrice = straddleLongZoneLower - tickSize;
                return;
            }

            if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                return;

            double fallbackLongOffsetTicks = GetEffectiveStraddleZoneOffsetTicks(MarketPosition.Long);
            double fallbackShortOffsetTicks = GetEffectiveStraddleZoneOffsetTicks(MarketPosition.Short);
            double fallbackShift = straddleRangeShift;
            double longCenter = straddleRangeHigh + fallbackLongOffsetTicks * tickSize + fallbackShift;
            double shortCenter = straddleRangeLow - fallbackShortOffsetTicks * tickSize + fallbackShift;

            straddleLongZoneUpper = longCenter + halfZone;
            straddleLongZoneLower = longCenter - halfZone;
            straddleShortZoneUpper = shortCenter + halfZone;
            straddleShortZoneLower = shortCenter - halfZone;

            straddleLongAutoCenter = (straddleLongZoneUpper + straddleLongZoneLower) * 0.5;
            straddleShortAutoCenter = (straddleShortZoneUpper + straddleShortZoneLower) * 0.5;

            straddleHardStopLongAutoPrice = straddleShortZoneUpper + tickSize;
            straddleHardStopShortAutoPrice = straddleLongZoneLower - tickSize;
        }

        private double GetEffectiveStraddleZoneOffsetTicks(MarketPosition side)
        {
            if (side == MarketPosition.Long)
                return straddleLongManualOffsetTicks ?? StraddleZoneOffsetTicks;
            if (side == MarketPosition.Short)
                return straddleShortManualOffsetTicks ?? StraddleZoneOffsetTicks;
            return StraddleZoneOffsetTicks;
        }

        private double? GetStraddleHardStopPrice(MarketPosition side)
        {
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return null;

            if (side == MarketPosition.Long)
            {
                if (!straddleHardStopLongAutoPrice.HasValue)
                    return null;
                double price = straddleHardStopLongAutoPrice.Value;
                if (straddleHardStopLongManualOffsetTicks.HasValue)
                    price += straddleHardStopLongManualOffsetTicks.Value * tickSize;
                return price;
            }

            if (side == MarketPosition.Short)
            {
                if (!straddleHardStopShortAutoPrice.HasValue)
                    return null;
                double price = straddleHardStopShortAutoPrice.Value;
                if (straddleHardStopShortManualOffsetTicks.HasValue)
                    price += straddleHardStopShortManualOffsetTicks.Value * tickSize;
                return price;
            }

            return null;
        }

        private void CancelStraddlePendingEntries(List<string> tradeIds, string reason)
        {
            if (tradeIds == null || tradeIds.Count == 0)
                return;

            foreach (string tradeId in tradeIds)
            {
                TradeRuntimeState state;
                if (TryGetTradeState(tradeId, out state) && state != null)
                {
                    state.EntryCancelRequested = true;
                    TryCancelOrder(tradeId, state.EntryOrder, null, reason);
                    state.EntryOrderPending = false;
                }
            }

            tradeIds.Clear();
        }

        private void SubmitStraddleStopEntries(int entriesToSubmit, int quantityPerEntry, DateTime now, bool allowMarketFallback)
        {
            if (entriesToSubmit <= 0 || quantityPerEntry <= 0)
                return;

            double longLower = straddleLongZoneLower;
            double longUpper = straddleLongZoneUpper;
            double shortLower = straddleShortZoneLower;
            double shortUpper = straddleShortZoneUpper;
            double longStop = longLower;
            double shortStop = shortUpper;
            if (longStop <= 0 || shortStop <= 0)
                return;

            double currentAsk = lastAsk > 0 ? lastAsk : GetCurrentAsk();
            double currentBid = lastBid > 0 ? lastBid : GetCurrentBid();
            double referencePrice = lastLast > 0
                ? lastLast
                : (currentAsk > 0 && currentBid > 0 ? (currentAsk + currentBid) * 0.5 : GetRealtimePrice());
            if (currentAsk <= 0)
                currentAsk = referencePrice;
            if (currentBid <= 0)
                currentBid = referencePrice;

            if (!allowMarketFallback)
                return;

            bool longTouched = currentAsk > 0 && currentAsk >= longLower && currentAsk <= longUpper;
            bool shortTouched = currentBid > 0 && currentBid <= shortUpper && currentBid >= shortLower;
            if (longTouched && shortTouched)
            {
                if (Math.Abs(referencePrice - longStop) <= Math.Abs(referencePrice - shortStop))
                    shortTouched = false;
                else
                    longTouched = false;
            }

            MultiEntrySyncGroup longGroup = null;
            MultiEntrySyncGroup shortGroup = null;
            if (TreatMultiEntryAsSingleTrade)
            {
                if (!straddleLongTriggered && straddlePendingLongTradeIds.Count == 0 && longTouched)
                    longGroup = StartMultiEntrySyncGroup(MarketPosition.Long, entriesToSubmit, quantityPerEntry, true);
                if (!straddleShortTriggered && straddlePendingShortTradeIds.Count == 0 && shortTouched)
                    shortGroup = StartMultiEntrySyncGroup(MarketPosition.Short, entriesToSubmit, quantityPerEntry, true);
            }

            if (!straddleLongTriggered && straddlePendingLongTradeIds.Count == 0 && longTouched)
            {
                for (int i = 0; i < entriesToSubmit; i++)
                {
                    string tradeId = CreateTradeId(MarketPosition.Long);
                    var state = PrepareTradeState(tradeId, MarketPosition.Long, quantityPerEntry);
                    state.IsStraddleEntry = true;
                    state.EntryContext = "STRADDLE";
                    state.EntrySignalTime = now;
                    state.EntryPrice = longStop;
                    state.StraddleEntryTime = DateTime.MinValue;
                    state.StraddleProfitStart = DateTime.MinValue;
                    state.StraddleProfitGatePassed = false;
                    state.StraddleTrailingActive = false;
                    state.StraddleTrailHighWater = 0;
                    state.StraddleTrailLowWater = 0;
                    state.EntryOrderPending = true;
                    state.EntryCancelRequested = false;
                    AttachTradeStateToSyncGroup(state, longGroup);

                    StrategyLogInfo($"[STRADDLE] Trigger Long market {tradeId} @ {referencePrice:F2} (stop {longStop:F2}) ({i + 1}/{entriesToSubmit})");
                    EnterLong(quantityPerEntry, tradeId);
                    straddlePendingLongTradeIds.Add(tradeId);
                }
            }

            if (!straddleShortTriggered && straddlePendingShortTradeIds.Count == 0 && shortTouched)
            {
                for (int i = 0; i < entriesToSubmit; i++)
                {
                    string tradeId = CreateTradeId(MarketPosition.Short);
                    var state = PrepareTradeState(tradeId, MarketPosition.Short, quantityPerEntry);
                    state.IsStraddleEntry = true;
                    state.EntryContext = "STRADDLE";
                    state.EntrySignalTime = now;
                    state.EntryPrice = shortStop;
                    state.StraddleEntryTime = DateTime.MinValue;
                    state.StraddleProfitStart = DateTime.MinValue;
                    state.StraddleProfitGatePassed = false;
                    state.StraddleTrailingActive = false;
                    state.StraddleTrailHighWater = 0;
                    state.StraddleTrailLowWater = 0;
                    state.EntryOrderPending = true;
                    state.EntryCancelRequested = false;
                    AttachTradeStateToSyncGroup(state, shortGroup);

                    StrategyLogInfo($"[STRADDLE] Trigger Short market {tradeId} @ {referencePrice:F2} (stop {shortStop:F2}) ({i + 1}/{entriesToSubmit})");
                    EnterShort(quantityPerEntry, tradeId);
                    straddlePendingShortTradeIds.Add(tradeId);
                }
            }
        }

        private void UpdateStraddlePendingEntryStops(double longStop, double shortStop)
        {
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return;

            foreach (string tradeId in straddlePendingLongTradeIds.ToList())
            {
                TradeRuntimeState state;
                if (!TryGetTradeState(tradeId, out state) || state == null || state.EntryOrder == null)
                    continue;

                if (IsTerminalState(state.EntryOrder.OrderState))
                    continue;
                if (state.EntryOrder.OrderType != OrderType.StopMarket)
                    continue;

                if (!PricesClose(state.EntryOrder.StopPrice, longStop))
                {
                    try
                    {
                        ChangeOrder(state.EntryOrder, state.EntryOrder.Quantity, state.EntryOrder.LimitPrice, longStop);
                        state.EntryPrice = longStop;
                        if (Debug)
                            StrategyLogDebug($"[STRADDLE] Adjust Long stop {tradeId} -> {longStop:F2}");
                    }
                    catch (Exception ex)
                    {
                        if (Debug)
                            StrategyLogDebug($"[STRADDLE] Failed adjust Long stop {tradeId}: {ex.Message}");
                    }
                }
            }

            foreach (string tradeId in straddlePendingShortTradeIds.ToList())
            {
                TradeRuntimeState state;
                if (!TryGetTradeState(tradeId, out state) || state == null || state.EntryOrder == null)
                    continue;

                if (IsTerminalState(state.EntryOrder.OrderState))
                    continue;
                if (state.EntryOrder.OrderType != OrderType.StopMarket)
                    continue;

                if (!PricesClose(state.EntryOrder.StopPrice, shortStop))
                {
                    try
                    {
                        ChangeOrder(state.EntryOrder, state.EntryOrder.Quantity, state.EntryOrder.LimitPrice, shortStop);
                        state.EntryPrice = shortStop;
                        if (Debug)
                            StrategyLogDebug($"[STRADDLE] Adjust Short stop {tradeId} -> {shortStop:F2}");
                    }
                    catch (Exception ex)
                    {
                        if (Debug)
                            StrategyLogDebug($"[STRADDLE] Failed adjust Short stop {tradeId}: {ex.Message}");
                    }
                }
            }
        }

        private void SyncStraddleManualOffsetsFromChart()
        {
            if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                return;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return;

            if (straddleLongZoneRect != null)
            {
                double longTop = Math.Max(straddleLongZoneRect.StartAnchor.Price, straddleLongZoneRect.EndAnchor.Price);
                double longBottom = Math.Min(straddleLongZoneRect.StartAnchor.Price, straddleLongZoneRect.EndAnchor.Price);
                double longCenter = (longTop + longBottom) * 0.5;
                if (longCenter > 0 && !double.IsNaN(longCenter))
                {
                    double? autoCenter = straddleLongAutoCenter;
                    if (autoCenter.HasValue && Math.Abs(longCenter - autoCenter.Value) > tickSize * 0.25)
                        straddleLongManualOffsetTicks = (longCenter - straddleRangeHigh) / tickSize;
                }
            }

            if (straddleShortZoneRect != null)
            {
                double shortTop = Math.Max(straddleShortZoneRect.StartAnchor.Price, straddleShortZoneRect.EndAnchor.Price);
                double shortBottom = Math.Min(straddleShortZoneRect.StartAnchor.Price, straddleShortZoneRect.EndAnchor.Price);
                double shortCenter = (shortTop + shortBottom) * 0.5;
                if (shortCenter > 0 && !double.IsNaN(shortCenter))
                {
                    double? autoCenter = straddleShortAutoCenter;
                    if (autoCenter.HasValue && Math.Abs(shortCenter - autoCenter.Value) > tickSize * 0.25)
                        straddleShortManualOffsetTicks = (straddleRangeLow - shortCenter) / tickSize;
                }
            }
        }

        private void SyncStraddleHardStopManualOffsetsFromChart()
        {
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return;

            if (straddleHardStopLongLine != null && straddleHardStopLongAutoPrice.HasValue)
            {
                double linePrice = straddleHardStopLongLine.StartAnchor.Price;
                if (linePrice > 0 && !double.IsNaN(linePrice))
                {
                    if (Math.Abs(linePrice - straddleHardStopLongAutoPrice.Value) > tickSize * 0.25)
                        straddleHardStopLongManualOffsetTicks = (linePrice - straddleHardStopLongAutoPrice.Value) / tickSize;
                }
            }

            if (straddleHardStopShortLine != null && straddleHardStopShortAutoPrice.HasValue)
            {
                double linePrice = straddleHardStopShortLine.StartAnchor.Price;
                if (linePrice > 0 && !double.IsNaN(linePrice))
                {
                    if (Math.Abs(linePrice - straddleHardStopShortAutoPrice.Value) > tickSize * 0.25)
                        straddleHardStopShortManualOffsetTicks = (linePrice - straddleHardStopShortAutoPrice.Value) / tickSize;
                }
            }
        }

        private void UpdateStraddleRangeIntrabar(double price, DateTime now)
        {
            if (!EnableStraddleTrades || price <= 0)
                return;

            if (straddleSessionIterator == null)
                straddleSessionIterator = new SessionIterator(Bars);

            bool newSession = straddleSessionStart == DateTime.MinValue ||
                (straddleSessionEnd != DateTime.MinValue && now >= straddleSessionEnd);
            if (newSession)
                InitializeStraddleSession(now);

            if (straddleEventTime == DateTime.MinValue)
                return;

            if (now < straddleRangeStart || now > straddleWindowEnd)
                return;

            if (now >= straddleRangeStart && now <= straddleRangeEnd)
            {
                if (straddleRangeHigh == double.MinValue || price > straddleRangeHigh)
                    straddleRangeHigh = price;
                if (straddleRangeLow == double.MaxValue || price < straddleRangeLow)
                    straddleRangeLow = price;
            }

            DateTime armTime = GetStraddleArmTime();
            if (!straddleRangeReady && armTime != DateTime.MinValue && now >= armTime)
            {
                if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                    BackfillStraddleRange(straddleRangeEnd != DateTime.MinValue ? straddleRangeEnd : now);
                straddleRangeReady = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
                if (straddleRangeReady)
                    UpdateStraddleZones();
            }

            if (straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue)
                UpdateStraddleZones();
        }

        private void UpdateStraddleVisualsIntrabar(DateTime now)
        {
            if (!ShowFilterVisuals || BarsInProgress != 0 || !EnableStraddleTrades)
                return;
            if (straddleRangeStart == DateTime.MinValue)
                return;

            if (now < straddleRangeStart || now > straddleWindowEnd)
                return;

            bool inRangeWindow = now >= straddleRangeStart && now <= straddleRangeEnd;
            if (!straddleRangeReady && !inRangeWindow)
                return;

            bool hasRange = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
            if (!hasRange)
                return;

            UpdateStraddleZones();

            int startBarsAgo = FindBarsAgoForTime(straddleRangeStart);
            int endBarsAgo = 0;
            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;
            if (startBarsAgo < endBarsAgo)
            {
                int tmp = startBarsAgo;
                startBarsAgo = endBarsAgo;
                endBarsAgo = tmp;
            }

            var longRect = Draw.Rectangle(this, StraddleLongZoneTag, false, startBarsAgo, straddleLongZoneUpper, endBarsAgo, straddleLongZoneLower, Brushes.DarkOrange, Brushes.Transparent, 8);
            if (longRect != null)
            {
                longRect.IsLocked = true;
                straddleLongZoneRect = longRect;
            }

            var shortRect = Draw.Rectangle(this, StraddleShortZoneTag, false, startBarsAgo, straddleShortZoneUpper, endBarsAgo, straddleShortZoneLower, Brushes.DeepSkyBlue, Brushes.Transparent, 8);
            if (shortRect != null)
            {
                shortRect.IsLocked = true;
                straddleShortZoneRect = shortRect;
            }

            double? longHard = GetStraddleHardStopPrice(MarketPosition.Long);
            if (longHard.HasValue && longHard.Value > 0)
            {
                var line = Draw.HorizontalLine(this, StraddleHardStopLongTag, longHard.Value, Brushes.LimeGreen);
                if (line != null)
                {
                    line.IsLocked = true;
                    line.Stroke.Width = 2;
                    straddleHardStopLongLine = line;
                }
                Draw.Text(this, StraddleHardStopLongLabelTag, false, $"STRADDLE HARD STOP L {longHard.Value:F2}", 0, longHard.Value, 0, Brushes.LimeGreen, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
            }

            double? shortHard = GetStraddleHardStopPrice(MarketPosition.Short);
            if (shortHard.HasValue && shortHard.Value > 0)
            {
                var line = Draw.HorizontalLine(this, StraddleHardStopShortTag, shortHard.Value, Brushes.OrangeRed);
                if (line != null)
                {
                    line.IsLocked = true;
                    line.Stroke.Width = 2;
                    straddleHardStopShortLine = line;
                }
                Draw.Text(this, StraddleHardStopShortLabelTag, false, $"STRADDLE HARD STOP S {shortHard.Value:F2}", 0, shortHard.Value, 0, Brushes.OrangeRed, new SimpleFont("Arial", 11), TextAlignment.Right, null, null, 0);
            }
        }

        private void UpdateStraddleCountdownVisual(DateTime now, double currentPrice)
        {
            if (!ShowFilterVisuals || !EnableStraddleTrades || BarsInProgress != 0)
            {
                RemoveDrawObject(StraddleCountdownTag);
                return;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || tradeStates == null || tradeStates.Count == 0)
            {
                RemoveDrawObject(StraddleCountdownTag);
                return;
            }

            var states = tradeStates.Values.Where(s => s != null && s.IsStraddleEntry && s.RemainingQuantity > 0).ToList();
            if (states.Count == 0)
            {
                RemoveDrawObject(StraddleCountdownTag);
                return;
            }

            if (states.Any(s => s.StraddleProfitGatePassed))
            {
                RemoveDrawObject(StraddleCountdownTag);
                return;
            }

            double pnl = CalculateSignedUnrealizedPnlAtPrice(currentPrice);
            if (pnl <= 0 || StraddleMinProfitHoldSeconds <= 0)
            {
                foreach (var state in states)
                    state.StraddleProfitStart = DateTime.MinValue;
                RemoveDrawObject(StraddleCountdownTag);
                return;
            }

            DateTime start = states.Select(s => s.StraddleProfitStart).FirstOrDefault(t => t != DateTime.MinValue);
            if (start == DateTime.MinValue)
            {
                foreach (var state in states)
                    state.StraddleProfitStart = now;
                start = now;
            }

            double remaining = Math.Max(0.0, StraddleMinProfitHoldSeconds - (now - start).TotalSeconds);
            string text = string.Format("STRADDLE HOLD: {0:0.0}s", remaining);
            var font = new SimpleFont("Arial", 12) { Bold = true };
            Draw.TextFixed(this, StraddleCountdownTag, text, TextPosition.TopLeft, Brushes.Gold, font, Brushes.Transparent, Brushes.Black, 0);
        }

        private bool IsStraddleWindowActive(DateTime now)
        {
            if (!EnableStraddleTrades || straddleEventTime == DateTime.MinValue)
                return false;
            if (IsStraddleTradeOpen())
                return true;
            if (straddleLongTriggered && straddleShortTriggered)
                return false;
            return now >= straddleRangeStart && now <= straddleWindowEnd;
        }

        private bool IsStraddleTradeOpen()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;
            return tradeStates.Values.Any(s => s != null && s.IsStraddleEntry && s.RemainingQuantity > 0);
        }

        private bool TryHandleStraddleEntry(double price, DateTime now, double? bidOverride = null, double? askOverride = null)
        {
            if (!EnableStraddleTrades || price <= 0 || shutdownInProgress)
                return false;
            if (manualHaltActive || dailyPnLLimitHalted || desyncHoldActive)
            {
                CancelStraddlePendingEntries(straddlePendingLongTradeIds, "straddle_halted");
                CancelStraddlePendingEntries(straddlePendingShortTradeIds, "straddle_halted");
                return false;
            }
            if (straddleEventTime == DateTime.MinValue)
            {
                if (straddleSessionIterator == null)
                    straddleSessionIterator = new SessionIterator(Bars);
                InitializeStraddleSession(now);
            }

            DateTime armTime = GetStraddleArmTime();
            if (!straddleRangeReady && armTime != DateTime.MinValue && now >= armTime)
            {
                if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                    BackfillStraddleRange(straddleRangeEnd != DateTime.MinValue ? straddleRangeEnd : now);
                straddleRangeReady = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
                if (straddleRangeReady)
                    UpdateStraddleZones();
            }

            if (!straddleRangeReady && straddleRangeEnd != DateTime.MinValue && now >= straddleRangeEnd)
            {
                if (straddleRangeHigh == double.MinValue || straddleRangeLow == double.MaxValue)
                    BackfillStraddleRange(straddleRangeEnd);
                straddleRangeReady = straddleRangeHigh > double.MinValue && straddleRangeLow < double.MaxValue;
                if (straddleRangeReady)
                    UpdateStraddleZones();
            }

            if (straddleRangeReady)
                UpdateStraddleZones();

            straddleArmed = straddleRangeReady && armTime != DateTime.MinValue &&
                now >= armTime && now <= straddleWindowEnd;

            bool allowStaging = straddleArmed;
            if (!allowStaging || !IsStraddleWindowActive(now))
            {
                CancelStraddlePendingEntries(straddlePendingLongTradeIds, "straddle_window_closed");
                CancelStraddlePendingEntries(straddlePendingShortTradeIds, "straddle_window_closed");
                return false;
            }

            bool isFlat = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
            if (!isFlat)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    CancelStraddlePendingEntries(straddlePendingShortTradeIds, "straddle_long_open");
                else if (Position.MarketPosition == MarketPosition.Short)
                    CancelStraddlePendingEntries(straddlePendingLongTradeIds, "straddle_short_open");
                else
                {
                    CancelStraddlePendingEntries(straddlePendingLongTradeIds, "straddle_not_flat");
                    CancelStraddlePendingEntries(straddlePendingShortTradeIds, "straddle_not_flat");
                }
                return false;
            }

            int entriesToSubmit = GetEffectiveStraddleTradesPerEntry();
            int quantityPerEntry = Math.Max(1, DefaultQuantity);
            UpdateStraddleZones();
            SubmitStraddleStopEntries(entriesToSubmit, quantityPerEntry, now, straddleArmed);
            return false;
        }

        private void SubmitStraddleEntries(MarketPosition side, int entriesToSubmit, int quantityPerEntry, DateTime now)
        {
            // Legacy entry path - keep for compatibility but route to stop-market logic.
            SubmitStraddleStopEntries(entriesToSubmit, quantityPerEntry, now, straddleArmed);
        }

        private void InitializeOrbSession(DateTime barTime)
        {
            if (orbSessionIterator == null)
                return;

            bool hasSession = orbSessionIterator.GetNextSession(barTime, true);
            if (!hasSession)
                return;

            orbSessionStart = orbSessionIterator.ActualSessionBegin;
            orbSessionEnd = orbSessionIterator.ActualSessionEnd;
            if (OrbUseFixedStartTime)
                orbSessionStart = GetFixedOrbStart(orbSessionEnd);
            orbEndTime = orbSessionStart.AddMinutes(Math.Max(1, OrbMinutes));
            if (orbSessionEnd != DateTime.MinValue && orbSessionEnd > orbSessionStart && orbEndTime > orbSessionEnd)
                orbEndTime = orbSessionEnd;
            orbHigh = double.MinValue;
            orbLow = double.MaxValue;
            orbRangeReady = false;
            orbBreakoutSatisfied = false;
            orbUsingFallback = false;

            EnsureOrbStartInData();

            if (barTime >= orbEndTime)
            {
                BackfillOrbRange();
                orbRangeReady = orbHigh > double.MinValue && orbLow < double.MaxValue;
                if (!orbRangeReady)
                    orbBreakoutSatisfied = true;
            }
        }

        private void EnsureOrbStartInData()
        {
            if (orbSessionStart == DateTime.MinValue || orbUsingFallback)
                return;

            if (Time == null || Time.Count == 0 || Time[0] < orbSessionStart)
                return;

            int startBarsAgo = FindBarsAgoForTime(orbSessionStart);
            if (startBarsAgo >= 0)
                return;

            DateTime fallbackStart;
            if (!TryGetFallbackOrbStart(orbSessionStart, orbSessionEnd, out fallbackStart))
                return;

            orbUsingFallback = true;
            orbSessionStart = fallbackStart;
            orbEndTime = orbSessionStart.AddMinutes(Math.Max(1, OrbMinutes));
            orbHigh = double.MinValue;
            orbLow = double.MaxValue;
            orbRangeReady = false;
            orbBreakoutSatisfied = false;

            if (Debug)
                StrategyLogInfo(string.Format("[AUTO][ORB] Fallback start {0:yyyy-MM-dd HH:mm:ss} (session start not loaded)", orbSessionStart));
        }

        private bool TryGetFallbackOrbStart(DateTime sessionBegin, DateTime sessionEnd, out DateTime fallbackStart)
        {
            fallbackStart = DateTime.MinValue;
            int last = GetPrimaryCurrentBar();
            if (last < 0)
                return false;

            if (Times != null && Times.Length > 0 && Times[0] != null)
            {
                for (int i = last; i >= 0; i--)
                {
                    DateTime t = Times[0][i];
                    if (t < sessionBegin)
                        break;
                    if (t <= sessionEnd)
                        fallbackStart = t;
                }
            }
            else
            {
                for (int i = last; i >= 0; i--)
                {
                    DateTime t = Time[i];
                    if (t < sessionBegin)
                        break;
                    if (t <= sessionEnd)
                        fallbackStart = t;
                }
            }

            return fallbackStart != DateTime.MinValue;
        }

        private DateTime GetFixedOrbStart(DateTime sessionEnd)
        {
            DateTime baseDate = sessionEnd != DateTime.MinValue
                ? sessionEnd.Date
                : (Time != null && Time.Count > 0 ? Time[0].Date : DateTime.Today);
            int hour = Math.Max(0, Math.Min(23, OrbStartHour));
            int minute = Math.Max(0, Math.Min(59, OrbStartMinute));
            return baseDate.AddHours(hour).AddMinutes(minute);
        }

        private DateTime GetOrbBlockStartTime()
        {
            if (orbSessionStart == DateTime.MinValue)
                return DateTime.MinValue;

            int preBlock = Math.Max(0, OrbPreStartBlockMinutes);
            if (preBlock <= 0)
                return orbSessionStart;

            return orbSessionStart.AddMinutes(-preBlock);
        }

        private void BackfillOrbRange()
        {
            BackfillOrbRange(orbEndTime);
        }

        private void BackfillOrbRange(DateTime endTime)
        {
            int startBarsAgo = FindBarsAgoForTime(orbSessionStart);
            int endBarsAgo = FindBarsAgoForTime(endTime);
            if (startBarsAgo < 0 || endBarsAgo < 0)
                return;

            if (startBarsAgo < endBarsAgo)
            {
                int tmp = startBarsAgo;
                startBarsAgo = endBarsAgo;
                endBarsAgo = tmp;
            }

            for (int i = startBarsAgo; i >= endBarsAgo; i--)
            {
                orbHigh = Math.Max(orbHigh, Highs[0][i]);
                orbLow = Math.Min(orbLow, Lows[0][i]);
            }
        }

        private int FindBarsAgoForTime(DateTime target)
        {
            int last = (CurrentBars != null && CurrentBars.Length > 0) ? CurrentBars[0] : CurrentBar;
            if (Times != null && Times.Length > 0 && Times[0] != null)
            {
                for (int i = 0; i <= last; i++)
                {
                    if (Times[0][i] <= target)
                        return i;
                }
                return -1;
            }

            for (int i = 0; i <= last; i++)
            {
                if (Time[i] <= target)
                    return i;
            }

            return -1;
        }

        private bool IsOrbEntryAllowed(MarketPosition side)
        {
            if (!EnableOrbFilter)
                return true;

            DateTime blockStart = GetOrbBlockStartTime();
            if (blockStart != DateTime.MinValue && Time[0] < blockStart)
                return true;

            if (orbBreakoutSatisfied)
                return true;

            if (!orbRangeReady || orbHigh == double.MinValue || orbLow == double.MaxValue)
                return false;

            double close = Close[0];
            if (side == MarketPosition.Long && close > orbHigh)
            {
                orbBreakoutSatisfied = true;
                return true;
            }
            if (side == MarketPosition.Short && close < orbLow)
            {
                orbBreakoutSatisfied = true;
                return true;
            }

            return false;
        }

        private bool IsChopEntryAllowed(MarketPosition side, bool rvolGateReady, bool rvolOk, bool vrocOk, bool volExpOk)
        {
            if (!EnableChopFilter && !EnableCompressionGuard)
                return true;

            int primaryBar = GetPrimaryCurrentBar();
            int lookback = Math.Max(2, ChopLookbackBars);
            bool isChop;
            bool chopDecayActive;
            double chopDecayAdxDelta;
            double chopDecayBbDelta;
            double rangeHigh;
            double rangeLow;
            double buffer;
            double adxValue;
            double bbWidthPct;
            bool chopReady = TryGetChopState(lookback, out isChop, out chopDecayActive, out chopDecayAdxDelta, out chopDecayBbDelta, out rangeHigh, out rangeLow, out buffer, out adxValue, out bbWidthPct);
            if (!chopReady)
                return true;

            bool compressionActive = EnableCompressionGuard && bbWidthPct <= CompressionGuardBbWidthPct;
            if (!isChop && !compressionActive)
                return true;

            if (!isChop)
            {
                if (primaryBar < lookback + 1)
                    return false;
                rangeHigh = MAX(Highs[0], lookback)[1];
                rangeLow = MIN(Lows[0], lookback)[1];
                buffer = 0.0;
                if (ChopBreakoutBufferTicks > 0 && TickSize > 0)
                    buffer = ChopBreakoutBufferTicks * TickSize;
            }

            if (primaryBar < 0)
                return false;
            double close = Closes[0][0];

            double longThreshold = rangeHigh + buffer;
            double shortThreshold = rangeLow - buffer;
            bool breakout = false;
            if (side == MarketPosition.Long)
                breakout = close > longThreshold;
            else if (side == MarketPosition.Short)
                breakout = close < shortThreshold;

            if (!breakout)
                return false;

            if (ChopBreakoutHoldBars > 1)
            {
                if (!HasBreakoutHold(side, longThreshold, shortThreshold, ChopBreakoutHoldBars))
                    return false;
            }

            if (compressionActive)
            {
                bool rvolPass = !EnableRvolGate || (rvolGateReady && rvolOk && vrocOk);
                bool volExpPass = !EnableVolatilityExpansionVote || volExpOk;
                bool anyGateEnabled = EnableRvolGate || EnableVolatilityExpansionVote;
                bool guardOk = !anyGateEnabled
                    || (CompressionGuardRequireBoth ? (rvolPass && volExpPass) : (rvolPass || volExpPass));
                if (!guardOk)
                    return false;
            }

            return true;
        }

        private bool HasBreakoutHold(MarketPosition side, double longThreshold, double shortThreshold, int holdBars)
        {
            if (holdBars <= 1)
                return true;
            int primaryBar = GetPrimaryCurrentBar();
            if (primaryBar < holdBars)
                return false;

            for (int i = 0; i < holdBars; i++)
            {
                double close = Closes[0][i];
                if (side == MarketPosition.Long)
                {
                    if (close <= longThreshold)
                        return false;
                }
                else if (side == MarketPosition.Short)
                {
                    if (close >= shortThreshold)
                        return false;
                }
            }

            return true;
        }

        private void GetRvolGateState(out bool rvolReady, out bool vrocReady, out bool rvolOk, out bool vrocOk, out double rvolValue, out double rvolAvg, out double vrocPct)
        {
            rvolReady = false;
            vrocReady = false;
            rvolOk = false;
            vrocOk = false;
            rvolValue = 0.0;
            rvolAvg = 0.0;
            vrocPct = 0.0;

            if (!EnableRvolGate)
                return;

            int primaryBar = GetPrimaryCurrentBar();
            double currentVol = Volumes[0].Count > 0 ? Volumes[0][0] : 0.0;

            if (RvolLookbackBars > 1 && rvolBaseline != null && primaryBar >= RvolLookbackBars)
            {
                rvolAvg = rvolBaseline[0];
                if (rvolAvg > 0)
                {
                    rvolValue = currentVol / rvolAvg;
                    rvolOk = RvolMin <= 0 ? true : rvolValue >= RvolMin;
                    rvolReady = true;
                }
            }

            if (VrocLookbackBars > 0 && primaryBar >= VrocLookbackBars)
            {
                double pastVol = Volumes[0][VrocLookbackBars];
                if (pastVol > 0)
                {
                    vrocPct = ((currentVol - pastVol) / pastVol) * 100.0;
                    vrocOk = VrocMinPct <= 0 ? true : vrocPct >= VrocMinPct;
                    vrocReady = true;
                }
            }
        }

        private bool TryGetChopState(int lookback, out bool chopActive, out bool chopDecayActive, out double chopDecayAdxDelta, out double chopDecayBbDelta, out double rangeHigh, out double rangeLow, out double buffer, out double adxValue, out double bbWidthPct)
        {
            chopActive = false;
            chopDecayActive = false;
            chopDecayAdxDelta = 0.0;
            chopDecayBbDelta = 0.0;
            rangeHigh = 0.0;
            rangeLow = 0.0;
            buffer = 0.0;
            adxValue = 0.0;
            bbWidthPct = 0.0;

            int decayBars = EnableChopDecayGate ? Math.Max(1, ChopDecayBars) : 0;
            int primaryBar = GetPrimaryCurrentBar();
            if (primaryBar < lookback + 1)
                return false;

            adxValue = adxChop != null ? adxChop[0] : 0.0;
            bbWidthPct = GetChopBbWidthPct();
            bool baseChopActive = adxValue <= ChopAdxThreshold && bbWidthPct <= ChopBBWidthPct;

            if (EnableChopDecayGate && decayBars > 0 && primaryBar >= decayBars)
            {
                int barsAgo = Math.Min(decayBars, primaryBar);
                double adxPast = adxChop != null ? adxChop[barsAgo] : 0.0;
                double bbPast = GetChopBbWidthPct(barsAgo);
                chopDecayAdxDelta = adxValue - adxPast;
                chopDecayBbDelta = bbWidthPct - bbPast;

                double adxThreshold = Math.Max(0.0, ChopDecayAdxDelta);
                double bbThreshold = Math.Max(0.0, ChopDecayBbWidthDeltaPct);
                bool adxDecaying = chopDecayAdxDelta <= -adxThreshold;
                bool bbContracting = chopDecayBbDelta <= -bbThreshold;
                chopDecayActive = adxDecaying && bbContracting;
            }

            chopActive = baseChopActive || chopDecayActive;
            if (chopActive)
            {
                double rangeMid;
                string source;
                if (!TryGetChopRange(out rangeHigh, out rangeLow, out rangeMid, out source))
                    return false;
                if (ChopBreakoutBufferTicks > 0 && TickSize > 0)
                    buffer = ChopBreakoutBufferTicks * TickSize;
            }

            return true;
        }

        private bool TryGetChopRange(out double rangeHigh, out double rangeLow, out double rangeMid, out string source)
        {
            rangeHigh = 0.0;
            rangeLow = 0.0;
            rangeMid = 0.0;
            source = string.Empty;

            if (ChopRangeMode == ChopRangeModeOption.Bollinger)
            {
                int primaryBar = GetPrimaryCurrentBar();
                if (bbChop == null || primaryBar < Math.Max(2, ChopBollingerPeriod))
                    return false;

                rangeHigh = bbChop.Upper[0];
                rangeLow = bbChop.Lower[0];
                source = "BB";
            }
            else
            {
                int rangeLookback = Math.Max(2, ChopRangeLookbackBars > 0 ? ChopRangeLookbackBars : ChopLookbackBars);
                int primaryBar = GetPrimaryCurrentBar();
                if (primaryBar < rangeLookback + 1)
                    return false;

                rangeHigh = MAX(Highs[0], rangeLookback)[1];
                rangeLow = MIN(Lows[0], rangeLookback)[1];
                source = "HL";
            }

            if (rangeHigh <= 0 || rangeLow <= 0)
                return false;

            rangeMid = (rangeHigh + rangeLow) * 0.5;
            return true;
        }

        private bool IsChopActiveNow()
        {
            int lookback = Math.Max(2, ChopLookbackBars);
            bool chopActive;
            bool chopDecayActive;
            double chopDecayAdxDelta;
            double chopDecayBbDelta;
            double rangeHigh;
            double rangeLow;
            double buffer;
            double adxValue;
            double bbWidthPct;
            bool chopReady = TryGetChopState(lookback, out chopActive, out chopDecayActive, out chopDecayAdxDelta, out chopDecayBbDelta, out rangeHigh, out rangeLow, out buffer, out adxValue, out bbWidthPct);
            return chopReady && chopActive;
        }

        private int GetPrimaryCurrentBar()
        {
            if (CurrentBars != null && CurrentBars.Length > 0)
                return CurrentBars[0];
            return CurrentBar;
        }

        private HtfSwingGateResult EvaluateHtfSwingGate(MarketPosition side, double currentPrice)
        {
            var result = new HtfSwingGateResult
            {
                Enabled = EnableHtfSwingGate,
                HasData = false,
                Near = false,
                Blocked = false,
                HeldBeyond = false,
                ExtraVotes = 0,
                DistanceAtr = double.NaN,
                DistancePoints = double.NaN,
                SwingPrice = 0.0,
                Source = string.Empty,
                TimeframeLabel = string.Empty
            };

            var best = new HtfSwingGateResult { DistanceAtr = double.PositiveInfinity };
            bool found = false;

            if (TryBuildHtfSwingResult(htfPrimaryIndex, HtfSwingPrimaryMinutes, side, currentPrice, out HtfSwingGateResult primary))
            {
                best = primary;
                found = true;
            }

            if (TryBuildHtfSwingResult(htfSecondaryIndex, HtfSwingSecondaryMinutes, side, currentPrice, out HtfSwingGateResult secondary))
            {
                if (!found || secondary.DistanceAtr < best.DistanceAtr)
                {
                    best = secondary;
                    found = true;
                }
            }

            if (!found)
                return result;

            best.Enabled = EnableHtfSwingGate;
            result = best;

            if (EnableHtfSwingGate && result.Near)
            {
                if (HtfSwingAction == HtfSwingActionOption.AddVote || HtfSwingAction == HtfSwingActionOption.Both)
                    result.ExtraVotes = 1;
                if ((HtfSwingAction == HtfSwingActionOption.Block || HtfSwingAction == HtfSwingActionOption.Both) && !result.HeldBeyond)
                    result.Blocked = true;
            }

            return result;
        }

        private bool TryBuildHtfSwingResult(int htfIndex, int minutes, MarketPosition side, double currentPrice, out HtfSwingGateResult result)
        {
            result = new HtfSwingGateResult
            {
                Enabled = EnableHtfSwingGate,
                HasData = false,
                Near = false,
                Blocked = false,
                HeldBeyond = false,
                ExtraVotes = 0,
                DistanceAtr = double.PositiveInfinity,
                DistancePoints = double.PositiveInfinity,
                SwingPrice = 0.0,
                Source = string.Empty,
                TimeframeLabel = minutes > 0 ? $"{minutes}m" : string.Empty
            };

            if (htfIndex < 0 || htfIndex >= BarsArray.Length)
                return false;
            if (CurrentBars == null || htfIndex >= CurrentBars.Length)
                return false;

            int availableBars = CurrentBars[htfIndex];
            int minBars = Math.Max(HtfSwingLookbackBars, HtfSwingPivotStrength * 2 + 1);
            if (availableBars < minBars)
                return false;

            double swingHigh;
            double swingLow;
            string sourceHigh;
            string sourceLow;
            if (!TryGetHtfSwingLevels(htfIndex, currentPrice, out swingHigh, out swingLow, out sourceHigh, out sourceLow))
                return false;

            double level = side == MarketPosition.Long ? swingHigh : swingLow;
            string source = side == MarketPosition.Long ? sourceHigh : sourceLow;
            if (level <= 0 || double.IsNaN(level))
                return false;

            double atr = GetHtfAtrValue(htfIndex);
            if (atr <= 0)
                return false;

            bool beyond = side == MarketPosition.Long ? currentPrice >= level : currentPrice <= level;
            double distancePoints = beyond ? 0 : Math.Abs(level - currentPrice);
            double distanceAtr = distancePoints / atr;
            bool near = !beyond && distanceAtr <= HtfSwingDistanceAtr;
            bool heldBeyond = beyond && HasHeldBeyondSwing(level, side);

            result.HasData = true;
            result.Near = near;
            result.HeldBeyond = heldBeyond;
            result.DistanceAtr = distanceAtr;
            result.DistancePoints = distancePoints;
            result.SwingPrice = level;
            result.Source = source;
            result.TimeframeLabel = minutes > 0 ? $"{minutes}m" : string.Empty;
            return true;
        }

        private bool TryGetHtfSwingLevels(int htfIndex, double currentPrice, out double swingHigh, out double swingLow, out string sourceHigh, out string sourceLow)
        {
            swingHigh = 0.0;
            swingLow = 0.0;
            sourceHigh = string.Empty;
            sourceLow = string.Empty;

            bool hasPivot = false;
            double pivotHigh = 0.0;
            double pivotLow = 0.0;
            if (HtfSwingMode == HtfSwingModeOption.Pivot || HtfSwingMode == HtfSwingModeOption.Both)
                hasPivot = TryGetHtfPivotLevels(htfIndex, HtfSwingPivotStrength, HtfSwingLookbackBars, out pivotHigh, out pivotLow);

            bool hasRange = false;
            double rangeHigh = 0.0;
            double rangeLow = 0.0;
            if (HtfSwingMode == HtfSwingModeOption.Range || HtfSwingMode == HtfSwingModeOption.Both)
                hasRange = TryGetHtfRangeLevels(htfIndex, HtfSwingLookbackBars, out rangeHigh, out rangeLow);

            if (HtfSwingMode == HtfSwingModeOption.Pivot)
            {
                if (!hasPivot)
                    return false;
                swingHigh = pivotHigh;
                swingLow = pivotLow;
                sourceHigh = "Pivot";
                sourceLow = "Pivot";
                return true;
            }

            if (HtfSwingMode == HtfSwingModeOption.Range)
            {
                if (!hasRange)
                    return false;
                swingHigh = rangeHigh;
                swingLow = rangeLow;
                sourceHigh = "Range";
                sourceLow = "Range";
                return true;
            }

            if (!hasPivot && !hasRange)
                return false;

            if (hasPivot && !hasRange)
            {
                swingHigh = pivotHigh;
                swingLow = pivotLow;
                sourceHigh = "Pivot";
                sourceLow = "Pivot";
                return true;
            }

            if (!hasPivot && hasRange)
            {
                swingHigh = rangeHigh;
                swingLow = rangeLow;
                sourceHigh = "Range";
                sourceLow = "Range";
                return true;
            }

            double pivotHighDist = pivotHigh >= currentPrice ? pivotHigh - currentPrice : double.PositiveInfinity;
            double rangeHighDist = rangeHigh >= currentPrice ? rangeHigh - currentPrice : double.PositiveInfinity;
            if (pivotHighDist <= rangeHighDist)
            {
                swingHigh = pivotHigh;
                sourceHigh = "Pivot";
            }
            else
            {
                swingHigh = rangeHigh;
                sourceHigh = "Range";
            }

            double pivotLowDist = pivotLow <= currentPrice ? currentPrice - pivotLow : double.PositiveInfinity;
            double rangeLowDist = rangeLow <= currentPrice ? currentPrice - rangeLow : double.PositiveInfinity;
            if (pivotLowDist <= rangeLowDist)
            {
                swingLow = pivotLow;
                sourceLow = "Pivot";
            }
            else
            {
                swingLow = rangeLow;
                sourceLow = "Range";
            }

            return true;
        }

        private bool TryGetHtfRangeLevels(int htfIndex, int lookback, out double high, out double low)
        {
            high = 0.0;
            low = 0.0;
            if (lookback <= 0)
                return false;
            if (htfIndex < 0 || htfIndex >= BarsArray.Length)
                return false;
            if (CurrentBars == null || htfIndex >= CurrentBars.Length)
                return false;
            if (CurrentBars[htfIndex] < lookback)
                return false;

            double maxHigh = double.MinValue;
            double minLow = double.MaxValue;
            for (int barsAgo = 0; barsAgo < lookback; barsAgo++)
            {
                maxHigh = Math.Max(maxHigh, Highs[htfIndex][barsAgo]);
                minLow = Math.Min(minLow, Lows[htfIndex][barsAgo]);
            }

            if (maxHigh == double.MinValue || minLow == double.MaxValue)
                return false;

            high = maxHigh;
            low = minLow;
            return true;
        }

        private bool TryGetHtfPivotLevels(int htfIndex, int strength, int lookback, out double pivotHigh, out double pivotLow)
        {
            pivotHigh = 0.0;
            pivotLow = 0.0;
            if (strength <= 0 || lookback <= 0)
                return false;
            if (htfIndex < 0 || htfIndex >= BarsArray.Length)
                return false;
            if (CurrentBars == null || htfIndex >= CurrentBars.Length)
                return false;

            int available = CurrentBars[htfIndex];
            int minBars = strength * 2 + 1;
            if (available < minBars)
                return false;

            int maxBarsAgo = Math.Min(available - strength, lookback);
            bool foundHigh = false;
            bool foundLow = false;

            for (int barsAgo = strength; barsAgo <= maxBarsAgo; barsAgo++)
            {
                double candidate = Highs[htfIndex][barsAgo];
                bool isPivot = true;
                for (int i = 1; i <= strength; i++)
                {
                    if (Highs[htfIndex][barsAgo - i] >= candidate || Highs[htfIndex][barsAgo + i] >= candidate)
                    {
                        isPivot = false;
                        break;
                    }
                }
                if (isPivot)
                {
                    pivotHigh = candidate;
                    foundHigh = true;
                    break;
                }
            }

            for (int barsAgo = strength; barsAgo <= maxBarsAgo; barsAgo++)
            {
                double candidate = Lows[htfIndex][barsAgo];
                bool isPivot = true;
                for (int i = 1; i <= strength; i++)
                {
                    if (Lows[htfIndex][barsAgo - i] <= candidate || Lows[htfIndex][barsAgo + i] <= candidate)
                    {
                        isPivot = false;
                        break;
                    }
                }
                if (isPivot)
                {
                    pivotLow = candidate;
                    foundLow = true;
                    break;
                }
            }

            return foundHigh || foundLow;
        }

        private double GetHtfAtrValue(int htfIndex)
        {
            if (htfIndex == htfPrimaryIndex && htfAtrPrimary != null)
                return htfAtrPrimary[0];
            if (htfIndex == htfSecondaryIndex && htfAtrSecondary != null)
                return htfAtrSecondary[0];
            return 0.0;
        }

        private bool HasHeldBeyondSwing(double swingPrice, MarketPosition side)
        {
            if (swingPrice <= 0 || HtfSwingHoldBars <= 0)
                return false;

            int bars = Math.Min(HtfSwingHoldBars, CurrentBar + 1);
            for (int i = 0; i < bars; i++)
            {
                double close = Close[i];
                if (side == MarketPosition.Long)
                {
                    if (close <= swingPrice)
                        return false;
                }
                else
                {
                    if (close >= swingPrice)
                        return false;
                }
            }

            return true;
        }

        private void ExitTradesForVwap(string exitSuffix)
        {
            if (string.IsNullOrEmpty(exitSuffix))
                exitSuffix = "VWAP";

            if (tradeStates == null || tradeStates.Count == 0)
            {
                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong();
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort();
                }
                return;
            }

            bool usedSyncGroup = false;
            if (!string.IsNullOrEmpty(activeTradeId))
            {
                MultiEntrySyncGroup group;
                if (TryGetMultiEntrySyncGroupByTradeId(activeTradeId, out group))
                {
                    int totalRemaining = GetMultiEntrySyncRemainingQuantity(group.TradeId);
                    if (totalRemaining > 0)
                    {
                        ExitMultiEntrySyncTrades(group.TradeId, totalRemaining, exitSuffix);
                        usedSyncGroup = true;
                    }
                }
            }

            if (usedSyncGroup)
                return;

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;

                int qty = Math.Max(1, state.RemainingQuantity);
                if (state.Bootstrapped && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    qty = Math.Min(qty, Math.Abs(Position.Quantity));

                string exitSignal = BuildExitSignalName(state.TradeId, exitSuffix);
                string fromEntry = state.Bootstrapped ? null : state.TradeId;
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, fromEntry);
                else if (state.EntrySide == MarketPosition.Short)
                    ExitShort(qty, exitSignal, fromEntry);
            }
        }

        private void ExitChopRangeTrades(string exitSuffix)
        {
            if (string.IsNullOrEmpty(exitSuffix))
                exitSuffix = "CHOPR";

            CancelWorkingChopEntryOrders("chop_exit");

            if (tradeStates == null || tradeStates.Count == 0)
            {
                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong();
                    else if (Position.MarketPosition == MarketPosition.Short)
                        ExitShort();
                }
                return;
            }

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;
                if (state.IsManualEntry)
                    continue;
                if (!state.IsChopEntry && !state.ChopTrailActive && !state.ChopTrailForced)
                    continue;

                int qty = Math.Max(1, state.RemainingQuantity);
                if (state.Bootstrapped && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    qty = Math.Min(qty, Math.Abs(Position.Quantity));

                string exitSignal = BuildExitSignalName(state.TradeId, exitSuffix);
                string fromEntry = state.Bootstrapped ? null : state.TradeId;
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, fromEntry);
                else if (state.EntrySide == MarketPosition.Short)
                    ExitShort(qty, exitSignal, fromEntry);
            }
        }

        private double GetChopBbWidthPct()
        {
            if (bbChop == null)
                return 0.0;

            double mid = bbChop.Middle[0];
            if (Math.Abs(mid) < 1e-9)
                return 0.0;

            double width = bbChop.Upper[0] - bbChop.Lower[0];
            return (width / mid) * 100.0;
        }

        private double GetChopBbWidthPct(int barsAgo)
        {
            int primaryBar = GetPrimaryCurrentBar();
            if (bbChop == null || barsAgo < 0 || primaryBar < barsAgo)
                return 0.0;

            double mid = bbChop.Middle[barsAgo];
            if (Math.Abs(mid) < 1e-9)
                return 0.0;

            double width = bbChop.Upper[barsAgo] - bbChop.Lower[barsAgo];
            return (width / mid) * 100.0;
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

            if (state.IsStraddleEntry)
                return;

            bool stopHold = IsManualProtectionHoldActive(state, true);
            bool targetHold = IsManualProtectionHoldActive(state, false);
            bool stopArmed = stopSet || (state.StopOrder != null && !IsTerminalState(state.StopOrder.OrderState)) || stopHold;
            bool targetArmed = targetSet || (state.TargetOrder != null && !IsTerminalState(state.TargetOrder.OrderState)) || targetHold;

            // If stops/targets already armed, nothing to do.
            if (stopArmed && targetArmed)
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
            if (!stopSet && !stopHold && state.LastStopPrice > 0 && (state.StopOrder == null || IsTerminalState(state.StopOrder.OrderState)))
            {
                if (IssueStopLoss(activeTradeId, CalculationMode.Price, state.LastStopPrice, false))
                    stopSet = true;
            }

            // Try managed stops/targets first. Do not re-arm if we already have a recorded stop price.
            if (!stopSet && !stopHold && state.LastStopPrice <= 0)
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
                    bool isLong = Position.MarketPosition == MarketPosition.Long;
                    double price = isLong
                        ? entryPrice - stopTicks * TickSize
                        : entryPrice + stopTicks * TickSize;
                    double refPrice = currentPrice > 0 ? currentPrice : GetRealtimePrice();
                    try
                    {
                        double bid = GetCurrentBid();
                        double ask = GetCurrentAsk();
                        if (isLong && bid > 0)
                            refPrice = bid;
                        else if (!isLong && ask > 0)
                            refPrice = ask;
                    }
                    catch { }
                    double? safeStop = ClampStopPrice(price, refPrice, isLong, null);
                    if (!safeStop.HasValue)
                        return;
                    if (isLong)
                        ExitLongStopMarket(safeStop.Value, stopSignal, activeTradeId);
                    else
                        ExitShortStopMarket(safeStop.Value, stopSignal, activeTradeId);
                    stopSet = true;
                }
            }

            if (!targetSet && !targetHold && state.LastTargetPrice > 0 && (state.TargetOrder == null || IsTerminalState(state.TargetOrder.OrderState)))
            {
                if (IssueProfitTarget(activeTradeId, CalculationMode.Price, state.LastTargetPrice))
                    targetSet = true;
            }

            if (!targetSet && !targetHold && state.LastTargetPrice <= 0)
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

        private void ResetGlobalTrailingState()
        {
            globalTrailActivated = false;
            globalTrailActivationPrice = 0;
            globalTrailLockPrice = 0;
            globalTrailLastStopPrice = 0;
            globalTrailSide = MarketPosition.Flat;
        }

        private void ResetScaleInState()
        {
            scaleInActive = false;
            scaleInTriggered = false;
            scaleInTradesExecuted = 0;
            scaleInTradesPending = 0;
            scaleInInitialEntryPrice = 0;
            scaleInLockPrice = 0;
            scaleInHighWater = 0;
            scaleInLowWater = 0;
            scaleInLastStopPrice = 0;
            scaleInActivationPrice = 0;
            scaleInTrailActivated = false;
            scaleInSide = MarketPosition.Flat;
            scaleInHoldUntil = DateTime.MinValue;
            ClearScaleInDrawdownLines();
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

        private void BootstrapExistingPositionState(bool allowWhileHalted = false)
        {
            if (manualHaltActive && !allowWhileHalted)
                return;
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
                ManualStopPending = false,
                ManualTargetPending = false,
                ManualStopPendingUntil = DateTime.MinValue,
                ManualTargetPendingUntil = DateTime.MinValue,
                PendingAutoStopUpdate = false,
                PendingAutoTargetUpdate = false,
                PendingAutoStopPrice = 0,
                PendingAutoTargetPrice = 0,
                LastStopPrice = 0,
                LastTargetPrice = 0,
                RunUpActive = false,
                RunUpAnchorPrice = 0,
                RunUpInitialDistance = 0,
                RunUpIncrement = 0,
                RunUpLastStopPrice = null,
                RunUpHighWater = 0,
                RunUpLowWater = 0,
                IsVwapEntry = false,
                VwapIsFlip = false,
                VwapBandMultiplier = 0,
                VwapTargetPrice = 0,
                VwapNextBandPrice = 0,
                VwapTrailOnVwapTouch = false,
                VwapTrailActive = false,
                VwapTrailAnchorPrice = 0,
                VwapTrailDistance = 0,
                VwapTrailIncrement = 0,
                VwapTrailLastStopPrice = null,
                VwapTrailHighWater = 0,
                VwapTrailLowWater = 0,
                VwapFailureHigh = 0,
                VwapFailureLow = 0,
                VwapFailureCheckBar = -1,
                EntryBarIndex = -1,
                BreakEvenActivated = false,
                SyntheticLogEmitted = false,
                Bootstrapped = true,
                IsManualEntry = false,
                ExitAllTriggered = false,
                AllowOpenPublish = State == State.Realtime,
                EntryVolExpEnabled = false,
                EntryVolExpOk = false,
                EntryVolExpBbWidthPct = 0,
                EntryVolExpBbDeltaPct = 0,
                EntryVolExpAtr = 0,
                EntryVolExpAtrBaseline = 0,
                EntryVolExpAtrRatio = 0,
                EntryRvolEnabled = false,
                EntryRvolReady = false,
                EntryRvolOk = false,
                EntryRvolValue = 0,
                EntryRvolAvg = 0,
                EntryVrocReady = false,
                EntryVrocOk = false,
                EntryVrocPct = 0,
                EntryChopDecayActive = false,
                EntryChopDecayAdxDelta = 0,
                EntryChopDecayBbDeltaPct = 0,
                IsStraddleEntry = false,
                StraddleEntryTime = DateTime.MinValue,
                StraddleProfitStart = DateTime.MinValue,
                StraddleProfitGatePassed = false,
                StraddleTrailingActive = false,
                StraddleTrailHighWater = 0,
                StraddleTrailLowWater = 0
            };

            if (scaleInInitialEntryPrice <= 0 || double.IsNaN(scaleInInitialEntryPrice))
                scaleInInitialEntryPrice = state.EntryPrice;

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

        private void ResetTradeState(bool preserveProtectionOrders = false)
        {
            if (tradeStates != null && tradeStates.Count > 0)
            {
                foreach (var state in tradeStates.Values.ToList())
                {
                    if (state.ManualStopOverride || state.ManualTargetOverride)
                        NotifyAddonManualOverride(state.TradeId,
                            state.ManualStopOverride ? false : (bool?)null,
                            state.ManualTargetOverride ? false : (bool?)null);
                    if (!preserveProtectionOrders)
                        CancelProtectiveOrders(state);
                }
            }

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            else
                tradeStates.Clear();

            if (!preserveProtectionOrders)
                ClearGlobalStopsTargets();
            openTradeOrder.Clear();
            activeTradeId = null;
            multiEntrySyncGroups.Clear();
            stopSet = false;
            targetSet = false;
            ResetDemaTrailingState();
            ResetGlobalTrailingState();
            ResetScaleInState();
            lastStatusText = null;
            lastStatusHealthy = false;
            lastStatusHasPnLLines = false;
            lastStatusPnlNegative = false;
        }

        private bool ShouldPreserveProtectionOnTerminate()
        {
            try
            {
                if (Account == null)
                    return false;
                return Account.ConnectionStatus != ConnectionStatus.Connected;
            }
            catch
            {
                return false;
            }
        }

        private int GetEffectiveTradesPerEntry()
        {
            int effective = tradesPerEntryOverride > 0 ? tradesPerEntryOverride : TradesPerEntry;
            if (effective < 1)
                effective = 1;
            if (effective > MaxTradesPerEntry)
                effective = MaxTradesPerEntry;

            if (EntriesPerDirection < effective)
            {
                try
                {
                    EntriesPerDirection = effective;
                }
                catch (Exception ex)
                {
                    if (Debug)
                        StrategyLogDebug($"[UI] Failed to update EntriesPerDirection to {effective}: {ex.Message}");
                }
            }

            return effective;
        }

        private int GetVwapEntriesToSubmit()
        {
            int entries = GetEffectiveTradesPerEntry();
            int maxEntries = Math.Max(1, DefaultQuantity);
            if (entries > maxEntries)
                entries = maxEntries;
            if (entries < 1)
                entries = 1;
            return entries;
        }

        private int GetVwapQuantityPerEntry(int entriesToSubmit)
        {
            int total = Math.Max(1, DefaultQuantity);
            int entries = Math.Max(1, entriesToSubmit);
            int perEntry = total / entries;
            if (perEntry < 1)
                perEntry = 1;
            return perEntry;
        }

        private int GetEffectiveChopTradesPerEntry()
        {
            int effective = chopTradesPerEntryOverride > 0 ? chopTradesPerEntryOverride : ChopTradesPerEntry;
            if (effective < 1)
                effective = 1;
            if (effective > MaxTradesPerEntry)
                effective = MaxTradesPerEntry;

            if (EntriesPerDirection < effective)
            {
                try
                {
                    EntriesPerDirection = effective;
                }
                catch (Exception ex)
                {
                    if (Debug)
                        StrategyLogDebug($"[UI] Failed to update EntriesPerDirection to {effective}: {ex.Message}");
                }
            }

            return effective;
        }

        private int GetEffectiveStraddleTradesPerEntry()
        {
            int effective = TradesPerStraddleEntry;
            if (effective < 1)
                effective = 1;
            if (effective > MaxTradesPerEntry)
                effective = MaxTradesPerEntry;

            if (EntriesPerDirection < effective)
            {
                try
                {
                    EntriesPerDirection = effective;
                }
                catch (Exception ex)
                {
                    if (Debug)
                        StrategyLogDebug($"[STRADDLE] Failed to update EntriesPerDirection to {effective}: {ex.Message}");
                }
            }

            return effective;
        }

        private bool IsMultiEntrySyncEnabled
        {
            get { return TreatMultiEntryAsSingleTrade && GetEffectiveTradesPerEntry() > 1; }
        }

        private bool ShouldPublishTradeLifecycle(TradeRuntimeState state)
        {
            if (state == null)
                return false;

            if (!state.IsScaleInEntry)
                return true;

            return state.AllowOpenPublish || state.OpenPublished;
        }

        private bool HasActiveStatesForSyncGroup(string syncTradeId)
        {
            if (string.IsNullOrEmpty(syncTradeId) || tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state == null)
                    continue;
                if (!string.Equals(state.SyncTradeId, syncTradeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (state.IsScaleInEntry)
                    continue;
                if (state.RemainingQuantity > 0 || state.EntryOrderPending)
                    return true;
                if (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))
                    return true;
            }

            return false;
        }

        private MultiEntrySyncGroup StartMultiEntrySyncGroup(MarketPosition side, int entriesToSubmit, int quantityPerEntry)
        {
            return StartMultiEntrySyncGroup(side, entriesToSubmit, quantityPerEntry, false);
        }

        private MultiEntrySyncGroup StartMultiEntrySyncGroup(MarketPosition side, int entriesToSubmit, int quantityPerEntry, bool force)
        {
            if ((!force && !IsMultiEntrySyncEnabled) || entriesToSubmit <= 1)
                return null;

            var group = new MultiEntrySyncGroup
            {
                TradeId = CreateTradeId(side),
                Side = side,
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

            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
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
                    TradeRuntimeState state;
                    if (tradeStates.TryGetValue(tradeId, out state) &&
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

            TradeRuntimeState active;
            if (!string.IsNullOrEmpty(activeTradeId) &&
                tradeStates.TryGetValue(activeTradeId, out active) &&
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

        private bool PublishPendingOpens()
        {
            if (State != State.Realtime)
                return false;

            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            bool isFlat = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
            bool hasPending = false;

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null)
                    continue;
                if (state.IsSynthetic)
                    continue;
                if (!ShouldPublishTradeLifecycle(state))
                    continue;
                if (state.IsChopEntry && (state.EntryOrderPending || (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))))
                    continue;
                if (!state.OpenPublished && isFlat && state.RemainingQuantity <= 0)
                {
                    StrategyLogInfo($"[AUTO][SYNC] Dropping unsynced closed trade {state.TradeId} (addon offline during entry).");
                    tradeStates.Remove(state.TradeId);
                    CleanupMultiEntrySyncGroup(state.SyncTradeId);
                    continue;
                }
                if (state.OpenPublished)
                    continue;

                try
                {
                    if (PublishOpenEvent(state))
                        state.OpenPublished = true;
                    else
                        hasPending = true;
                }
                catch
                {
                    hasPending = true;
                }
            }

            return hasPending;
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
                if (state.IsManualEntry)
                    continue;
                if (!ShouldPublishTradeLifecycle(state))
                    continue;
                if (!state.OpenPublished)
                    continue;
                if (state.ClosePublished)
                {
                    if (state.IsChopEntry && (state.EntryOrderPending || (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))))
                        continue;
                    tradeStates.Remove(state.TradeId);
                    CleanupMultiEntrySyncGroup(state.SyncTradeId);
                    continue;
                }
                if (state.IsChopEntry && (state.EntryOrderPending || (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))))
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

        private TradeRuntimeState PrepareTradeState(string tradeId, MarketPosition side, int quantityHint, bool preserveProtectionOrders = false, bool setActiveTrade = true, bool isScaleIn = false)
        {
            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);

            // Ensure no prior global stop/target bleeds into a fresh trade
            if (!preserveProtectionOrders)
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
                IsChopEntry = false,
                IsScaleInEntry = isScaleIn,
                ChopTrailActive = false,
                ChopTrailForced = false,
                ChopTrailHighWater = 0,
                ChopTrailLowWater = 0,
                PendingEntryPriceUpdate = false,
                PendingEntryLimitPrice = 0,
                LastAutoEntryLimitPrice = 0,
                EntryOrderPending = false,
                EntryCancelRequested = false,
                ManualStopOverride = false,
                ManualTargetOverride = false,
                ManualStopPending = false,
                ManualTargetPending = false,
                ManualStopPendingUntil = DateTime.MinValue,
                ManualTargetPendingUntil = DateTime.MinValue,
                PendingAutoStopUpdate = false,
                PendingAutoTargetUpdate = false,
                PendingAutoStopPrice = 0,
                PendingAutoTargetPrice = 0,
                LastStopPrice = 0,
                LastTargetPrice = 0,
                IsSynthetic = State != State.Realtime && !AllowHistoricalTrading,
                RunUpActive = false,
                RunUpAnchorPrice = 0,
                RunUpInitialDistance = 0,
                RunUpIncrement = 0,
                RunUpLastStopPrice = null,
                RunUpHighWater = 0,
                RunUpLowWater = 0,
                IsVwapEntry = false,
                VwapIsFlip = false,
                VwapBandMultiplier = 0,
                VwapTargetPrice = 0,
                VwapNextBandPrice = 0,
                VwapTrailOnVwapTouch = false,
                VwapTrailActive = false,
                VwapTrailAnchorPrice = 0,
                VwapTrailDistance = 0,
                VwapTrailIncrement = 0,
                VwapTrailLastStopPrice = null,
                VwapTrailHighWater = 0,
                VwapTrailLowWater = 0,
                VwapFailureHigh = 0,
                VwapFailureLow = 0,
                VwapFailureCheckBar = -1,
                EntryBarIndex = -1,
                BreakEvenActivated = false,
                SyntheticLogEmitted = false,
                Bootstrapped = false,
                IsManualEntry = false,
                ExitAllTriggered = false,
                AllowOpenPublish = false,
                ClosePublished = false,
                EntryOrder = null,
                StopOrder = null,
                TargetOrder = null,
                EntryVolExpEnabled = false,
                EntryVolExpOk = false,
                EntryVolExpBbWidthPct = 0,
                EntryVolExpBbDeltaPct = 0,
                EntryVolExpAtr = 0,
                EntryVolExpAtrBaseline = 0,
                EntryVolExpAtrRatio = 0,
                EntryRvolEnabled = false,
                EntryRvolReady = false,
                EntryRvolOk = false,
                EntryRvolValue = 0,
                EntryRvolAvg = 0,
                EntryVrocReady = false,
                EntryVrocOk = false,
                EntryVrocPct = 0,
                EntryChopDecayActive = false,
                EntryChopDecayAdxDelta = 0,
                EntryChopDecayBbDeltaPct = 0
            };

            ApplyEntrySnapshot(state, side);
            if (string.IsNullOrEmpty(state.EntryContext))
                state.EntryContext = "AUTO";

            tradeStates[tradeId] = state;
            if (setActiveTrade)
                activeTradeId = tradeId;
            if (!openTradeOrder.Contains(tradeId))
                openTradeOrder.Add(tradeId);
            return state;
        }

        private void ApplyEntrySnapshot(TradeRuntimeState state, MarketPosition side)
        {
            if (state == null)
                return;

            EntrySignalSnapshot snap = lastEntrySnapshot;
            if (snap.Time == DateTime.MinValue)
                return;

            state.EntryVotes = side == MarketPosition.Long ? snap.LongVotes : snap.ShortVotes;
            state.EntryMinVotes = side == MarketPosition.Long ? snap.MinLong : snap.MinShort;
            state.EntryRegimeSwitchingEnabled = snap.RegimeSwitchingEnabled;
            state.EntryRegimeIsChop = snap.RegimeIsChop;
            state.EntryReverseSignalTrading = snap.ReverseSignalTrading;
            state.EntryOrbAllowed = side == MarketPosition.Long ? snap.OrbLong : snap.OrbShort;
            state.EntryChopAllowed = side == MarketPosition.Long ? snap.ChopLong : snap.ChopShort;
            state.EntryChopAdx = snap.ChopAdx;
            state.EntryChopBbWidthPct = snap.ChopBbWidthPct;
            state.EntryChopDecayActive = snap.ChopDecayActive;
            state.EntryChopDecayAdxDelta = snap.ChopDecayAdxDelta;
            state.EntryChopDecayBbDeltaPct = snap.ChopDecayBbDeltaPct;
            state.EntryHtfEnabled = snap.HtfEnabled;

            HtfSwingGateResult gate = side == MarketPosition.Long ? snap.HtfLong : snap.HtfShort;
            state.EntryHtfNear = gate.Near;
            state.EntryHtfBlocked = gate.Blocked;
            state.EntryHtfHeldBeyond = gate.HeldBeyond;
            state.EntryHtfDistanceAtr = gate.DistanceAtr;
            state.EntryHtfSource = gate.Source;
            state.EntryHtfTimeframe = gate.TimeframeLabel;
            state.EntryVolExpEnabled = snap.VolExpEnabled;
            state.EntryVolExpOk = snap.VolExpOk;
            state.EntryVolExpBbWidthPct = snap.VolExpBbWidthPct;
            state.EntryVolExpBbDeltaPct = snap.VolExpBbDeltaPct;
            state.EntryVolExpAtr = snap.VolExpAtr;
            state.EntryVolExpAtrBaseline = snap.VolExpAtrBaseline;
            state.EntryVolExpAtrRatio = snap.VolExpAtrRatio;
            state.EntryRvolEnabled = snap.RvolEnabled;
            state.EntryRvolReady = snap.RvolReady;
            state.EntryRvolOk = snap.RvolOk;
            state.EntryRvolValue = snap.RvolValue;
            state.EntryRvolAvg = snap.RvolAvg;
            state.EntryVrocReady = snap.VrocReady;
            state.EntryVrocOk = snap.VrocOk;
            state.EntryVrocPct = snap.VrocPct;
            state.EntrySignalTime = snap.Time;
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
            bool syncBehaviorFill = isEntryAction && missingEntrySignal && syncBehavior;

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
                // Continue so backtests still use the same state/stop logic (TradeSync stays suppressed).
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
                TradeRuntimeState prepared;
                if (!string.IsNullOrEmpty(orderSignal) &&
                    tradeStates.TryGetValue(orderSignal, out prepared) &&
                    prepared != null &&
                    !prepared.Bootstrapped)
                {
                    hasPreparedStateForEntry = true;
                }
            }

            bool isSyncEntryFill = syncBehaviorFill && !hasPreparedStateForEntry;

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

            if (missingEntrySignal && isExitAction && tradeStates != null && !tradeStates.ContainsKey(tradeId))
            {
                string resolvedExitTradeId = ResolveTradeIdFromOrder(execution.Order);
                if (!string.IsNullOrEmpty(resolvedExitTradeId))
                    tradeId = resolvedExitTradeId;
            }

            bool isLiveExecution = IsLiveExecutionContext(execution);
            bool allowHistoricalManagement = AllowHistoricalTrading;
            if (!isLiveExecution && State != State.Realtime && !isSyncEntryFill && !allowHistoricalManagement)
                return;
            // Allow historical/execution replay to flow through so chart markers and status stay in sync.
            // We still suppress TradeSync publishes for synthetic/historical trades below.
            if (!isLiveExecution && Debug)
                StrategyLogDebug(string.Format("{0:yyyy-MM-dd HH:mm:ss}: Processing non-realtime execution for trade {1}", time, tradeId ?? "<unknown>"));

            if (exitOnClose && tradeStates != null && tradeStates.Count > 0)
            {
                TradeRuntimeState resolvedState = null;

                TradeRuntimeState activeState;
                if (!string.IsNullOrEmpty(activeTradeId) && tradeStates.TryGetValue(activeTradeId, out activeState))
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
                HandleEntryExecution(execution, state, isSyncEntryFill, allowHistoricalManagement);
                if (state.IsStraddleEntry)
                {
                    if (state.StraddleEntryTime == DateTime.MinValue)
                        state.StraddleEntryTime = time;
                    if (state.EntrySide == MarketPosition.Long)
                    {
                        straddleLongTriggered = true;
                        straddlePendingLongTradeIds.Remove(state.TradeId);
                        CancelStraddlePendingEntries(straddlePendingShortTradeIds, "straddle_opposite_filled");
                    }
                    else if (state.EntrySide == MarketPosition.Short)
                    {
                        straddleShortTriggered = true;
                        straddlePendingShortTradeIds.Remove(state.TradeId);
                        CancelStraddlePendingEntries(straddlePendingLongTradeIds, "straddle_opposite_filled");
                    }
                }
                if (!isLive && !allowHistoricalManagement && !isSyncEntryFill)
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
                    {
                        if (flag)
                            return true;
                        if (State == State.Realtime || State == State.Transition)
                            return true;
                        return false;
                    }
                }
            }
            catch
            {
                // Swallow - we'll fall back to state-based heuristics below.
            }

            if (State == State.Realtime || State == State.Transition)
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

            bool isFlatPosition = marketPosition == MarketPosition.Flat || quantity == 0;

            if (State == State.Realtime && manualHaltActive)
            {
                TryEnforceManualHaltFlat();
                if (isFlatPosition)
                    CleanupFlatPositionState(false);
                return;
            }

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
                    TradeRuntimeState seededState;
                    if (!string.IsNullOrEmpty(activeTradeId) && TryGetTradeState(activeTradeId, out seededState))
                        EnsureProtectionForActiveTrade(seededState, GetRealtimePrice());
                    UpdateStatusLabel($"Managing {marketPosition} {quantity} ({activeTradeId ?? "<pending>"})", true);
                }
            }

            // If the platform reports flat but we still have runtime state, clear it to avoid ghost trades.
            if (isFlatPosition)
            {
                bool pending = CleanupFlatPositionState(true);

                // Even if pending closes remain, do not block signal processing.
                if (!pending)
                    return;
            }
        }

        private bool CleanupFlatPositionState(bool updateScanningStatus)
        {
            bool pending = false;

            if (tradeStates != null && tradeStates.Count > 0)
            {
                bool hasWorkingEntry = HasWorkingEntryOrders();
                if (!hasWorkingEntry)
                {
                    CancelWorkingEntryOrders("position_flat");
                    foreach (var st in tradeStates.Values.ToList())
                        CancelProtectiveOrders(st);
                    pending = PublishPendingCloses();
                    if (!pending)
                    {
                        ResetTradeState();
                        if (updateScanningStatus)
                            UpdateStatusLabel("Active: scanning (position flat)", true);
                    }
                }
                else
                {
                    // Preserve runtime state for pending entries (manual or auto).
                    pending = PublishPendingCloses();
                }
            }

            ResetScaleInState();
            return pending;
        }

        private void HandleEntryExecution(Execution execution, TradeRuntimeState state, bool isSyncEntryFill = false, bool allowHistorical = false)
        {
            if (state != null)
            {
                state.IsSynthetic = false;
                state.SyntheticLogEmitted = false;
                // Only arm publish on real-time sync fills or executions flagged live.
                if (isSyncEntryFill || IsLiveExecutionContext(execution))
                {
                    if (!state.IsScaleInEntry || PublishScaleInTradesToBridge)
                        state.AllowOpenPublish = true;
                }
                state.EntryOrder = execution != null ? execution.Order : state.EntryOrder;
                state.EntryOrderPending = false;
                state.EntryCancelRequested = false;
            }

            int orderQty = Math.Max(1, Math.Abs((int)execution.Order.Quantity));
            int filledQty = Math.Max(1, Math.Abs((int)execution.Order.Filled));

            state.OriginalQuantity = orderQty;
            state.RemainingQuantity = Math.Max(orderQty, filledQty);
            state.EntrySide = (execution.Order.OrderAction == OrderAction.SellShort || execution.Order.OrderAction == OrderAction.Sell)
                ? MarketPosition.Short
                : MarketPosition.Long;
            state.EntryPrice = execution.Price;
            if (!state.IsScaleInEntry && state.EntryPrice > 0 && (scaleInInitialEntryPrice <= 0 || double.IsNaN(scaleInInitialEntryPrice)))
                scaleInInitialEntryPrice = state.EntryPrice;
            if (state.EntryPrice > 0)
            {
                state.MaxFavorablePrice = state.EntryPrice;
                state.MaxAdversePrice = state.EntryPrice;
            }
            if (string.IsNullOrEmpty(state.EntryContext))
                state.EntryContext = state.IsManualEntry ? "MANUAL" : (isSyncEntryFill ? "BOOTSTRAP" : "AUTO");
            if (state.EntrySignalTime == DateTime.MinValue)
                state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;

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
            else if (!state.IsScaleInEntry)
            {
                stopSet = false;
                targetSet = false;
            }

            if (!state.IsScaleInEntry)
            {
                ResetDemaTrailingState();
                ResetGlobalTrailingState();
                activeTradeId = state.TradeId;
            }
            else if (string.IsNullOrEmpty(activeTradeId))
            {
                activeTradeId = state.TradeId;
            }

            if (!state.IsScaleInEntry && state.IsStraddleEntry)
            {
                DateTime now = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                double entryRef = execution != null && execution.Price > 0 ? execution.Price : GetRealtimePrice();
                TryApplyStraddleProtection(state, entryRef, now);
            }

            // Publish open for all live entries (including sync-behavior fills).
            if (ShouldPublishTradeLifecycle(state) && !state.OpenPublished && !state.IsSynthetic && state.AllowOpenPublish)
            {
                if (PublishOpenEvent(state))
                    state.OpenPublished = true;
            }

            // For start-behavior sync fills, arm protection immediately instead of waiting for the next bar.
            bool shouldArmProtection = isSyncEntryFill || IsLiveExecutionContext(execution) || allowHistorical;
            if (shouldArmProtection)
            {
                double protectionPrice = execution != null ? execution.Price : GetRealtimePrice();
                if (state.IsScaleInEntry)
                {
                    SyncScaleInProtectionForEntry(state, protectionPrice);
                }
                else
                {
                    EnsureProtectionForActiveTrade(state, protectionPrice);
                    UpdateStatusLabel($"Managing {state.EntrySide} {state.RemainingQuantity} ({state.TradeId})", true);
                }
            }

            if (state.IsScaleInEntry)
            {
                if (state.EntryOrderPending)
                {
                    scaleInTradesPending = Math.Max(0, scaleInTradesPending - 1);
                    state.EntryOrderPending = false;
                    scaleInTradesExecuted++;
                }
                return;
            }

            if (state.IsChopEntry)
            {
                MarketPosition opposite = state.EntrySide == MarketPosition.Long ? MarketPosition.Short : MarketPosition.Long;
                CancelWorkingChopEntryOrders(opposite, "chop_entry_filled");
            }
        }

        private string GetExitSignalSuffixForExecution(Execution execution, TradeRuntimeState state)
        {
            if (execution != null && execution.Order != null)
            {
                string suffix = TryGetExitSignalSuffix(execution.Order.Name);
                if (string.IsNullOrEmpty(suffix))
                    suffix = TryGetExitSignalSuffix(execution.Order.FromEntrySignal);
                if (!string.IsNullOrWhiteSpace(suffix))
                    return suffix.Trim().ToUpperInvariant();
            }

            if (execution != null)
            {
                if (IsStopLossExecution(execution))
                    return "BS";
                if (execution.Order != null && LooksLikeTargetOrder(execution.Order))
                    return "BT";
                if (execution.Order != null && execution.Order.OrderEntry == OrderEntry.Manual && state != null)
                    return state.EntrySide == MarketPosition.Long ? "MANSELL" : "MANBUY";
            }

            return "CLS";
        }

        private void TriggerExitAllAfterPartial(Execution execution, TradeRuntimeState triggerState)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            string suffix = GetExitSignalSuffixForExecution(execution, triggerState);
            if (string.IsNullOrWhiteSpace(suffix))
                suffix = "CLS";
            bool stopExitAll = string.Equals(suffix, "BS", StringComparison.OrdinalIgnoreCase);

            CancelWorkingEntryOrders("partial_exit_all");

            foreach (var state in tradeStates.Values.ToList())
            {
                if (state == null)
                    continue;
                if (state.RemainingQuantity <= 0)
                    continue;
                if (state.EntryOrderPending)
                    continue;

                state.ExitAllTriggered = true;
                if (stopExitAll)
                    RegisterStopLossCloseOverride(state.TradeId);
                CancelProtectiveOrders(state);

                int qty = Math.Max(1, state.RemainingQuantity);
                if (state.Bootstrapped && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    qty = Math.Min(qty, Math.Abs(Position.Quantity));
                if (qty <= 0)
                    continue;

                string exitSignal = BuildExitSignalName(state.TradeId, suffix);
                string fromEntry = state.Bootstrapped ? null : state.TradeId;
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, fromEntry);
                else if (state.EntrySide == MarketPosition.Short)
                    ExitShort(qty, exitSignal, fromEntry);
            }

            string tradeRef = triggerState != null ? triggerState.TradeId : string.Empty;
            if (!string.IsNullOrEmpty(tradeRef))
                StrategyLogInfo($"[EXIT_ALL] Partial fill detected for {tradeRef}; exiting remaining positions (suffix={suffix}).");
            else
                StrategyLogInfo($"[EXIT_ALL] Partial fill detected; exiting remaining positions (suffix={suffix}).");
        }

        private void HandleExitExecution(Execution execution, TradeRuntimeState state)
        {
            int execQty = Math.Max(1, Math.Abs((int)execution.Quantity));
            if (execQty > state.RemainingQuantity)
                execQty = state.RemainingQuantity;

            state.RemainingQuantity = Math.Max(0, state.RemainingQuantity - execQty);

            if (state.RemainingQuantity > 0)
            {
                if (!state.ExitAllTriggered)
                {
                    if (State == State.Realtime && !state.IsSynthetic && ShouldPublishTradeLifecycle(state) && IsStopLossExecution(execution))
                        RegisterStopLossCloseOverride(state.TradeId);

                    state.ExitAllTriggered = true;
                    TriggerExitAllAfterPartial(execution, state);
                }
                return;
            }
            else
            {
                bool stopLossExecution = State == State.Realtime && !state.IsSynthetic && ShouldPublishTradeLifecycle(state) && IsStopLossExecution(execution);
                if (stopLossExecution)
                    RegisterStopLossCloseOverride(state.TradeId);

                if (stopLossExecution && !state.IsScaleInEntry && tradeStates != null)
                {
                    foreach (var candidate in tradeStates.Values.ToList())
                    {
                        if (candidate == null || !candidate.IsScaleInEntry || candidate.RemainingQuantity <= 0)
                            continue;
                        if (string.Equals(candidate.TradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                            continue;

                        RegisterStopLossCloseOverride(candidate.TradeId);
                    }
                }

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
                    LogTradeOutcomeStory(execution, state, entryPrice, qty, execPnl);
                }

                if (!state.IsSynthetic && ShouldPublishTradeLifecycle(state))
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
                if (ShouldPublishTradeLifecycle(state) && (state.ManualStopOverride || state.ManualTargetOverride))
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
                {
                    string fallback = openTradeOrder[openTradeOrder.Count - 1];
                    for (int i = openTradeOrder.Count - 1; i >= 0; i--)
                    {
                        TradeRuntimeState candidate;
                        if (TryGetTradeState(openTradeOrder[i], out candidate) && candidate != null && !candidate.IsScaleInEntry)
                        {
                            fallback = openTradeOrder[i];
                            break;
                        }
                    }
                    activeTradeId = fallback;
                }

                if (state.Bootstrapped)
                    ClearGlobalStopsTargets();

                stopSet = false;
                targetSet = false;
                ResetDemaTrailingState();
                ResetGlobalTrailingState();

                if (State == State.Realtime && !state.IsSynthetic)
                {
                    bool isFlatNow = Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0;
                    bool hasRemainingTrades = tradeStates != null && tradeStates.Count > 0;
                    if (isFlatNow && !hasRemainingTrades)
                    {
                        bool isChopExit = state.IsChopEntry || (state.EntryContext != null && state.EntryContext.StartsWith("CHOP", StringComparison.OrdinalIgnoreCase));
                        if (!isChopExit)
                            StartEntryCooldown();
                    }
                }
            }
        }

        private bool IsStopLossExecution(Execution execution)
        {
            if (execution == null || execution.Order == null)
                return false;

            Order order = execution.Order;
            string name = order.Name ?? string.Empty;
            bool nameStop = name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;
            bool nameTarget = name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;
            bool stopType = order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit;
            if (nameTarget)
                return false;

            return nameStop || stopType;
        }

        private void RegisterStopLossCloseOverride(string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
                state != null &&
                !ShouldPublishTradeLifecycle(state))
            {
                return;
            }

            string syncTradeId = ResolveSyncTradeId(tradeId);
            MultiStratManager.Instance?.RegisterManualCloseOverride(syncTradeId, StopLossCloseReason, TimeSpan.FromSeconds(20));
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
            bool isProfit = rounded >= 0;

            var info = new PnlLabelInfo
            {
                Tag = tag,
                Time = execution.Time,
                Price = execution.Price,
                Label = label,
                IsProfit = isProfit
            };

            lock (pnlLabelLock)
            {
                if (pnlLabelInfos != null)
                {
                    for (int i = pnlLabelInfos.Count - 1; i >= 0; i--)
                    {
                        if (string.Equals(pnlLabelInfos[i].Tag, tag, StringComparison.OrdinalIgnoreCase))
                            pnlLabelInfos.RemoveAt(i);
                    }
                    pnlLabelInfos.Add(info);
                }
            }

            if (!ShowTradePnlTags)
                return;

            int barIndex = Bars.GetBar(execution.Time);
            if (barIndex < 0)
                barIndex = CurrentBar;
            int barsAgo = Math.Max(0, CurrentBar - barIndex);
            Draw.Text(this, tag, false, label, barsAgo, execution.Price, 0, brush, new SimpleFont("Arial", 12), TextAlignment.Center, null, null, 0);
        }

        private void LogTradeOutcomeStory(Execution execution, TradeRuntimeState state, double entryPrice, int qty, double execPnl)
        {
            if (!EnableTradeStoryLogging || execution == null || state == null)
                return;

            string tradeId = !string.IsNullOrEmpty(state.TradeId)
                ? state.TradeId
                : (execution.Order != null ? execution.Order.Name : "<unknown>");

            double exitPrice = execution.Price > 0 ? execution.Price : (execution.Order != null ? execution.Order.AverageFillPrice : 0.0);
            string outcome = execPnl >= 0 ? "PROFIT" : "LOSS";
            string exitReason = ResolveExitReason(execution);

            string votesText = state.EntryMinVotes > 0
                ? $"{state.EntryVotes}/{state.EntryMinVotes}"
                : "n/a";
            string orbText = state.EntryOrbAllowed ? "OK" : "NO";
            string chopText = state.EntryChopAllowed ? "OK" : "NO";
            string chopDecayText = state.EntryChopDecayActive ? "DECAY" : "NONE";
            string htfText = FormatHtfEntrySummary(state);
            string volExpText = FormatVolExpEntrySummary(state);
            string rvolText = FormatRvolEntrySummary(state);
            string entryContext = string.IsNullOrEmpty(state.EntryContext) ? "AUTO" : state.EntryContext;
            string regimeText = state.EntryRegimeSwitchingEnabled
                ? (state.EntryRegimeIsChop ? "CHOP" : "TREND")
                : "TREND(off)";
            string reverseText = state.EntryReverseSignalTrading ? "ON" : "OFF";

            DateTime entryTime = state.EntrySignalTime != DateTime.MinValue ? state.EntrySignalTime : (Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow);
            DateTime exitTime = execution.Time != DateTime.MinValue ? execution.Time : (Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow);

            double pointValue = Instrument?.MasterInstrument?.PointValue ?? (execution.Instrument?.MasterInstrument?.PointValue ?? 0.0);
            if (pointValue <= 0 && state.NtPointsPer1kLoss > 0)
                pointValue = 1000.0 / state.NtPointsPer1kLoss;

            double favorable = state.MaxFavorablePrice > 0 ? state.MaxFavorablePrice : entryPrice;
            double adverse = state.MaxAdversePrice > 0 ? state.MaxAdversePrice : entryPrice;

            double mfePoints = 0.0;
            double maePoints = 0.0;
            if (entryPrice > 0)
            {
                if (state.EntrySide == MarketPosition.Long)
                {
                    mfePoints = Math.Max(0.0, favorable - entryPrice);
                    maePoints = Math.Max(0.0, entryPrice - adverse);
                }
                else if (state.EntrySide == MarketPosition.Short)
                {
                    mfePoints = Math.Max(0.0, entryPrice - favorable);
                    maePoints = Math.Max(0.0, adverse - entryPrice);
                }
            }

            string mfeText = "n/a";
            string maeText = "n/a";
            if (pointValue > 0)
            {
                double mfeCash = mfePoints * pointValue * Math.Max(1, qty);
                double maeCash = maePoints * pointValue * Math.Max(1, qty);
                mfeText = $"{mfeCash:C0} ({mfePoints:F2}pt)";
                maeText = $"{maeCash:C0} ({maePoints:F2}pt)";
            }

            StrategyLogInfo(string.Format("[TRADE_STORY] {0} {1} qty={2} entry={3:F2} exit={4:F2} pnl={5:C0} {6} reason={7} ctx={8} votes={9} ORB={10} CHOP={11} REGIME={12} REV={13} ADX={14:F1} BB={15:F2}% {16} dADX={17:F2} dBB={18:F2}% HTF={19} VOL={20} RVOL={21} MFE={22} MAE={23} entryTime={24:yyyy-MM-dd HH:mm:ss} exitTime={25:yyyy-MM-dd HH:mm:ss}",
                tradeId,
                state.EntrySide,
                Math.Max(1, qty),
                entryPrice,
                exitPrice,
                execPnl,
                outcome,
                exitReason,
                entryContext,
                votesText,
                orbText,
                chopText,
                regimeText,
                reverseText,
                state.EntryChopAdx,
                state.EntryChopBbWidthPct,
                chopDecayText,
                state.EntryChopDecayAdxDelta,
                state.EntryChopDecayBbDeltaPct,
                htfText,
                volExpText,
                rvolText,
                mfeText,
                maeText,
                entryTime,
                exitTime));
        }

        private string ResolveExitReason(Execution execution)
        {
            if (execution == null || execution.Order == null)
                return "UNKNOWN";

            string suffix = TryGetExitSignalSuffix(execution.Order.Name);
            if (string.IsNullOrEmpty(suffix))
                suffix = TryGetExitSignalSuffix(execution.Order.FromEntrySignal);

            string normalized = string.IsNullOrEmpty(suffix) ? string.Empty : suffix.Trim().ToUpperInvariant();
            switch (normalized)
            {
                case "BS":
                    return "STOP";
                case "BT":
                    return "TARGET";
                case "CHOP":
                    return "CHOP_EXIT";
                case "MHLT":
                    return "MANUAL_HALT";
                case "MANBUY":
                case "MANSELL":
                    return "MANUAL_BUTTON";
                case "EXT":
                    return "EXTERNAL_PARTIAL";
                case "CLS":
                    return "EXTERNAL_CLOSE";
            }

            if (IsStopLossExecution(execution))
                return "STOP";
            if (LooksLikeTargetOrder(execution.Order))
                return "TARGET";
            if (execution.Order.OrderEntry == OrderEntry.Manual)
                return "MANUAL";

            return string.IsNullOrEmpty(normalized) ? "EXIT" : normalized;
        }

        private static string TryGetExitSignalSuffix(string signal)
        {
            if (string.IsNullOrWhiteSpace(signal))
                return null;

            int idx = signal.LastIndexOf('_');
            if (idx < 0 || idx >= signal.Length - 1)
                return null;

            return signal.Substring(idx + 1);
        }

        private static bool LooksLikeTargetOrder(Order order)
        {
            if (order == null)
                return false;

            string name = order.Name ?? string.Empty;
            if (name.IndexOf("_BT", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (order.OrderType == OrderType.Limit &&
                name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            return false;
        }

        private string FormatHtfEntrySummary(TradeRuntimeState state)
        {
            if (state == null || !state.EntryHtfEnabled)
                return "OFF";

            bool hasDistance = !double.IsNaN(state.EntryHtfDistanceAtr) && state.EntryHtfDistanceAtr != double.PositiveInfinity;
            string stateText = state.EntryHtfNear ? (state.EntryHtfBlocked ? "BLOCK" : "NEAR") : "CLEAR";
            string dist = hasDistance ? state.EntryHtfDistanceAtr.ToString("0.00") + "ATR" : "n/a";
            string held = state.EntryHtfHeldBeyond ? "HOLD" : string.Empty;

            string source = string.Empty;
            if (!string.IsNullOrEmpty(state.EntryHtfTimeframe))
                source = state.EntryHtfTimeframe;
            if (!string.IsNullOrEmpty(state.EntryHtfSource))
                source = string.IsNullOrEmpty(source) ? state.EntryHtfSource : $"{source}:{state.EntryHtfSource}";

            var sb = new StringBuilder();
            sb.Append(stateText).Append(' ').Append(dist);
            if (!string.IsNullOrWhiteSpace(held))
                sb.Append(' ').Append(held);
            if (!string.IsNullOrWhiteSpace(source))
                sb.Append(' ').Append(source);
            return sb.ToString();
        }

        private string FormatVolExpEntrySummary(TradeRuntimeState state)
        {
            if (state == null || !state.EntryVolExpEnabled)
                return "OFF";

            string status = state.EntryVolExpOk ? "OK" : "NO";
            string bbDelta = state.EntryVolExpBbDeltaPct != 0 ? state.EntryVolExpBbDeltaPct.ToString("0.00") + "%" : "n/a";
            string atrRatio = state.EntryVolExpAtrRatio > 0 ? state.EntryVolExpAtrRatio.ToString("0.00") + "x" : "n/a";
            return $"{status} bbDelta {bbDelta} atrx {atrRatio}";
        }

        private string FormatRvolEntrySummary(TradeRuntimeState state)
        {
            if (state == null || !state.EntryRvolEnabled)
                return "OFF";

            if (!state.EntryRvolReady || !state.EntryVrocReady)
                return "n/a";

            string status = (state.EntryRvolOk && state.EntryVrocOk) ? "OK" : "NO";
            string rvolText = state.EntryRvolValue > 0 ? state.EntryRvolValue.ToString("0.00") : "n/a";
            string vrocText = state.EntryVrocPct.ToString("0.0") + "%";
            return $"{status} rvol {rvolText} vroc {vrocText}";
        }

        private bool PublishOpenEvent(TradeRuntimeState state)
        {
            if (state == null)
                return false;
            if (!ShouldPublishTradeLifecycle(state))
                return false;

            if (!state.IsScaleInEntry)
                EnsureMultiEntrySyncAssignment(state);

            if (state.EntryOrderPending)
            {
                var entryOrder = state.EntryOrder;
                if (entryOrder == null || entryOrder.Filled <= 0)
                    return false;
            }

            if (!state.AllowOpenPublish)
            {
                if (State == State.Realtime && !state.IsSynthetic)
                    state.AllowOpenPublish = true;
                else
                    return false;
            }

            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][SYNC] PublishOpen skipped (manager or TradeSync null) for {0}", state?.TradeId ?? "<null>"));
                return false;
            }
            if (!IsTradeSyncReady(manager))
            {
                WarnTradeSyncOffline();
                return false;
            }
            // Allow sync-start fills to publish before State.Realtime when AllowOpenPublish is armed.

            MultiEntrySyncGroup group;
            if (!string.IsNullOrEmpty(state.SyncTradeId) && TryGetMultiEntrySyncGroupByTradeId(state.TradeId, out group))
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
                    manager.TradeSync.PublishOpen(this, group.TradeId, state.InstrumentName, state.EntrySide, totalQty, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice, true, false);
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
                bool aggregateEntry = !state.IsScaleInEntry && TreatMultiEntryAsSingleTrade && state.OriginalQuantity > 1;
                manager.TradeSync.PublishOpen(this, state.TradeId, state.InstrumentName, state.EntrySide, state.OriginalQuantity, state.AccountName, state.NtPointsPer1kLoss, state.EntryPrice, aggregateEntry, state.IsScaleInEntry);
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][SYNC] Published OPEN for {0} qty={1} side={2} price={3:F2}{4}", state.TradeId, state.OriginalQuantity, state.EntrySide, state.EntryPrice, aggregateEntry ? " (aggregated)" : string.Empty));
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

        private void EnsureMultiEntrySyncAssignment(TradeRuntimeState state)
        {
            if (state == null || !TreatMultiEntryAsSingleTrade || !string.IsNullOrEmpty(state.SyncTradeId))
                return;

            MultiEntrySyncGroup group = FindActiveMultiEntrySyncGroup(state.EntrySide);
            if (group == null)
                return;

            state.SyncTradeId = group.TradeId;
            group.TotalQuantity = GetMultiEntrySyncTotalQuantity(group.TradeId, group.TotalQuantity);
        }

        private MultiEntrySyncGroup FindActiveMultiEntrySyncGroup(MarketPosition side)
        {
            if (multiEntrySyncGroups == null || multiEntrySyncGroups.Count == 0)
                return null;

            MultiEntrySyncGroup fallback = null;
            foreach (var group in multiEntrySyncGroups.Values)
            {
                if (group == null || string.IsNullOrEmpty(group.TradeId))
                    continue;
                if (group.Side != side)
                    continue;
                if (!HasActiveStatesForSyncGroup(group.TradeId))
                    continue;
                if (group.OpenPublished)
                    return group;
                if (fallback == null)
                    fallback = group;
            }

            return fallback;
        }

        private void PublishPartialEvent(string tradeId, int remainingQuantity)
        {
            if (State != State.Realtime)
                return;
            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
                state != null &&
                !ShouldPublishTradeLifecycle(state))
            {
                return;
            }

            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return;
            if (!IsTradeSyncReady(manager))
            {
                WarnTradeSyncOffline();
                return;
            }

            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
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
            if (State != State.Realtime)
                return true;
            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
                state != null &&
                !ShouldPublishTradeLifecycle(state))
            {
                return true;
            }

            MultiStratManager manager = MultiStratManager.Instance;
            if (manager == null || manager.TradeSync == null)
                return false;
            if (!IsTradeSyncReady(manager))
            {
                WarnTradeSyncOffline();
                TradeRuntimeState st;
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out st))
                    st.PendingClosePublish = true;
                return false;
            }

            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
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
                    foreach (var st in GetMultiEntrySyncStates(group.TradeId))
                    {
                        if (st != null)
                            st.ClosePublished = true;
                    }
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
                TradeRuntimeState st;
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out st))
                {
                    st.PendingClosePublish = false;
                    st.ClosePublished = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                if (!tradeSyncWarned)
                    StrategyLogError(string.Format("[AUTO][SYNC] Failed to publish closed for {0}: {1}", tradeId ?? "<unknown>", ex.Message));
                WarnTradeSyncOffline();
                TradeRuntimeState st;
                if (tradeStates != null && tradeStates.TryGetValue(tradeId, out st))
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

        private bool ShouldSuppressProtectionRearm()
        {
            if (shutdownInProgress)
                return true;
            if (manualHaltActive)
                return true;
            if (dailyPnLLimitHalted)
                return true;
            return false;
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

            if (!string.IsNullOrEmpty(name) &&
                (orderState == OrderState.Cancelled || orderState == OrderState.Rejected || orderState == OrderState.Unknown))
            {
                if (straddlePendingLongTradeIds.Contains(name))
                    straddlePendingLongTradeIds.Remove(name);
                if (straddlePendingShortTradeIds.Contains(name))
                    straddlePendingShortTradeIds.Remove(name);
            }

            string resolvedTradeId = ResolveTradeIdFromOrder(order);
            if (string.IsNullOrEmpty(order.FromEntrySignal))
            {
                string entryKey = order.Name;
                TradeRuntimeState entryState;
                if (!string.IsNullOrEmpty(entryKey) &&
                    tradeStates != null &&
                    tradeStates.TryGetValue(entryKey, out entryState))
                {
                    bool isEntryAction = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
                    if (isEntryAction)
                    {
                        entryState.EntryOrder = order;
                        if (IsTerminalState(orderState))
                            entryState.EntryOrder = null;
                        if (IsTerminalState(orderState))
                        {
                            entryState.EntryOrderPending = false;
                            entryState.EntryCancelRequested = false;
                        }
                    }
                }
            }
            TradeRuntimeState state;
            if (!string.IsNullOrEmpty(resolvedTradeId) && TryGetTradeState(resolvedTradeId, out state))
            {
                bool isEntryAction = order.OrderAction == OrderAction.Buy || order.OrderAction == OrderAction.SellShort;
                if (state.IsChopEntry && isEntryAction && order.OrderType == OrderType.Limit)
                {
                    state.EntryOrder = order;
                    if (IsTerminalState(orderState))
                    {
                        bool cancelled = orderState == OrderState.Cancelled || orderState == OrderState.Rejected;
                        state.EntryOrder = null;
                        state.EntryOrderPending = false;
                        state.EntryCancelRequested = false;
                        if (cancelled && order.Filled == 0)
                        {
                            tradeStates.Remove(state.TradeId);
                            openTradeOrder.Remove(state.TradeId);
                            CleanupMultiEntrySyncGroup(state.SyncTradeId);

                            if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                            {
                                activeTradeId = null;
                                if (openTradeOrder.Count > 0)
                                {
                                    string fallback = openTradeOrder[openTradeOrder.Count - 1];
                                    for (int i = openTradeOrder.Count - 1; i >= 0; i--)
                                    {
                                        TradeRuntimeState candidate;
                                        if (TryGetTradeState(openTradeOrder[i], out candidate) && candidate != null && !candidate.IsScaleInEntry)
                                        {
                                            fallback = openTradeOrder[i];
                                            break;
                                        }
                                    }
                                    activeTradeId = fallback;
                                }
                            }
                        }
                    }
                    else
                    {
                        state.EntryOrderPending = false;

                        if (state.EntryCancelRequested)
                        {
                            TryCancelOrder(state.TradeId, order, null, "chop_entry_cancel_pending");
                            return;
                        }
                    }
                }

                if (state.IsScaleInEntry && isEntryAction && IsTerminalState(orderState) && order.Filled == 0)
                {
                    scaleInTradesPending = Math.Max(0, scaleInTradesPending - 1);
                    tradeStates.Remove(state.TradeId);
                    openTradeOrder.Remove(state.TradeId);
                    if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                    {
                        activeTradeId = null;
                        if (openTradeOrder.Count > 0)
                        {
                            string fallback = openTradeOrder[openTradeOrder.Count - 1];
                            for (int i = openTradeOrder.Count - 1; i >= 0; i--)
                            {
                                TradeRuntimeState candidate;
                                if (TryGetTradeState(openTradeOrder[i], out candidate) && candidate != null && !candidate.IsScaleInEntry)
                                {
                                    fallback = openTradeOrder[i];
                                    break;
                                }
                            }
                            activeTradeId = fallback;
                        }
                    }
                }

                if (!state.IsChopEntry && !state.IsScaleInEntry && isEntryAction && IsTerminalState(orderState) && order.Filled == 0)
                {
                    state.EntryOrder = null;
                    state.EntryOrderPending = false;
                    state.EntryCancelRequested = false;
                    tradeStates.Remove(state.TradeId);
                    openTradeOrder.Remove(state.TradeId);
                    CleanupMultiEntrySyncGroup(state.SyncTradeId);

                    if (!string.IsNullOrEmpty(activeTradeId) && string.Equals(activeTradeId, state.TradeId, StringComparison.OrdinalIgnoreCase))
                    {
                        activeTradeId = null;
                        if (openTradeOrder.Count > 0)
                        {
                            string fallback = openTradeOrder[openTradeOrder.Count - 1];
                            for (int i = openTradeOrder.Count - 1; i >= 0; i--)
                            {
                                TradeRuntimeState candidate;
                                if (TryGetTradeState(openTradeOrder[i], out candidate) && candidate != null && !candidate.IsScaleInEntry)
                                {
                                    fallback = openTradeOrder[i];
                                    break;
                                }
                            }
                            activeTradeId = fallback;
                        }
                    }

                    StrategyLogInfo($"[AUTO][ORDERUPD] Entry {state.TradeId} cancelled/rejected with no fill; dropping pending trade state.");
                    return;
                }

                bool suppressProtectionRearm = ShouldSuppressProtectionRearm();
                // Only treat clearly-tagged protective orders as stops/targets.
                bool isStopOrder = name.IndexOf("_BS", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isStopOrder && (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit))
                    isStopOrder = name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;

                bool isTargetOrder = name.IndexOf("_BT", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isTargetOrder && order.OrderType == OrderType.Limit)
                    isTargetOrder = name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;

                DateTime now = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                if (orderState == OrderState.Cancelled && error == ErrorCode.NoError && Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    if (isStopOrder && !state.PendingAutoStopUpdate)
                        MarkManualProtectionPending(state, true);
                    if (isTargetOrder && !state.PendingAutoTargetUpdate)
                        MarkManualProtectionPending(state, false);
                }

                // Re-arm protection if a bootstrapped stop/target gets cancelled/rejected (common when qty=0 or signal missing).
                if ((orderState == OrderState.Cancelled || orderState == OrderState.Rejected) && state.Bootstrapped && !suppressProtectionRearm)
                {
                    if (State != State.Realtime || Position == null || Position.MarketPosition == MarketPosition.Flat || state.RemainingQuantity <= 0)
                        return;

                        // Track re-arms; keep protection persistent without hard throttling.
                        if ((now - state.LastProtectionRetry).TotalMinutes > 5)
                            state.ProtectionRetryCount = 0;
                        state.ProtectionRetryCount++;
                        state.LastProtectionRetry = now;

                    if (isStopOrder)
                    {
                        if (IsManualProtectionHoldActive(state, true))
                        {
                            if (Debug)
                                StrategyLogDebug($"[AUTO][STOP] Manual adjustment hold active; skipping rearm for {resolvedTradeId ?? "<unknown>"}.");
                        }
                        else
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
                    }
                    else if (isTargetOrder)
                    {
                        if (IsManualProtectionHoldActive(state, false))
                        {
                            if (Debug)
                                StrategyLogDebug($"[AUTO][TARGET] Manual adjustment hold active; skipping rearm for {resolvedTradeId ?? "<unknown>"}.");
                        }
                        else
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
            }

            DetectManualStopTargetAdjustments(order, limitPrice, stopPrice, orderState);
            DetectManualChopEntryAdjustments(order, limitPrice, orderState);
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

            if (tradeStates != null && tradeStates.ContainsKey(name))
                return name;

            // As a last resort, use activeTradeId if side matches the order action
            if (!string.IsNullOrEmpty(activeTradeId))
                return activeTradeId;
            return null;
        }

        private void MarkManualProtectionPending(TradeRuntimeState state, bool isStop)
        {
            if (state == null)
                return;

            DateTime holdUntil = DateTime.UtcNow.AddSeconds(ManualProtectionHoldSeconds);
            if (isStop)
            {
                state.ManualStopPending = true;
                state.ManualStopPendingUntil = holdUntil;
            }
            else
            {
                state.ManualTargetPending = true;
                state.ManualTargetPendingUntil = holdUntil;
            }

            if (Debug)
                StrategyLogDebug(string.Format("[AUTO][{0}] Manual adjustment pending; suppressing rearm until {1:HH:mm:ss.fff} for {2}.",
                    isStop ? "STOP" : "TARGET",
                    holdUntil,
                    state.TradeId ?? "<unknown>"));
        }

        private void ClearManualProtectionPending(TradeRuntimeState state, bool isStop)
        {
            if (state == null)
                return;

            if (isStop)
            {
                state.ManualStopPending = false;
                state.ManualStopPendingUntil = DateTime.MinValue;
                return;
            }

            state.ManualTargetPending = false;
            state.ManualTargetPendingUntil = DateTime.MinValue;
        }

        private bool IsManualProtectionHoldActive(TradeRuntimeState state, bool isStop)
        {
            if (state == null)
                return false;

            DateTime now = DateTime.UtcNow;
            if (isStop)
            {
                if (state.ManualStopOverride)
                    return true;
                if (!state.ManualStopPending)
                    return false;
                if (state.ManualStopPendingUntil == DateTime.MinValue || now <= state.ManualStopPendingUntil)
                    return true;
                state.ManualStopPending = false;
                return false;
            }

            if (state.ManualTargetOverride)
                return true;
            if (!state.ManualTargetPending)
                return false;
            if (state.ManualTargetPendingUntil == DateTime.MinValue || now <= state.ManualTargetPendingUntil)
                return true;
            state.ManualTargetPending = false;
            return false;
        }

        private bool TryGetManualSyncGroupStates(string tradeId, out List<TradeRuntimeState> states)
        {
            states = null;
            if (!IsMultiEntrySyncEnabled)
                return false;

            MultiEntrySyncGroup group;
            if (!TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
                return false;

            states = GetMultiEntrySyncStates(group.TradeId);
            return states != null && states.Count > 0;
        }

        private bool ApplyManualStopToGroup(string tradeId, double price, bool wasLocked)
        {
            List<TradeRuntimeState> states;
            if (!TryGetManualSyncGroupStates(tradeId, out states))
                return false;

            bool groupWasLocked = wasLocked || states.Any(s => s != null && s.ManualStopOverride);
            foreach (var st in states)
            {
                if (st == null || st.RemainingQuantity <= 0)
                    continue;

                st.LastStopPrice = price;
                if (st.RunUpActive)
                    st.RunUpLastStopPrice = price;
                st.ManualStopOverride = true;
                ClearManualProtectionPending(st, true);
                AlignManagedStopWithManual(st.TradeId, price);
            }

            if (!groupWasLocked)
                NotifyAddonManualOverride(tradeId, true, null);

            return true;
        }

        private bool ApplyManualTargetToGroup(string tradeId, double price, bool wasLocked)
        {
            List<TradeRuntimeState> states;
            if (!TryGetManualSyncGroupStates(tradeId, out states))
                return false;

            bool groupWasLocked = wasLocked || states.Any(s => s != null && s.ManualTargetOverride);
            foreach (var st in states)
            {
                if (st == null || st.RemainingQuantity <= 0)
                    continue;

                st.LastTargetPrice = price;
                st.ManualTargetOverride = true;
                ClearManualProtectionPending(st, false);
                AlignManagedTargetWithManual(st.TradeId, price);
            }

            if (!groupWasLocked)
                NotifyAddonManualOverride(tradeId, null, true);

            return true;
        }

        private void EnforceManualProtectionForState(TradeRuntimeState state)
        {
            if (state == null || state.IsSynthetic)
                return;

            if (state.ManualStopOverride && state.LastStopPrice > 0 && !state.PendingAutoStopUpdate)
            {
                double currentStop = state.StopOrder != null ? state.StopOrder.StopPrice : 0;
                if (currentStop <= 0 || !PricesClose(currentStop, state.LastStopPrice))
                {
                    AlignManagedStopWithManual(state.TradeId, state.LastStopPrice);
                    stopSet = true;
                    if (Debug)
                        StrategyLogDebug(string.Format("[AUTO][STOP] Enforced manual stop {0:F2} for {1}.", state.LastStopPrice, state.TradeId ?? "<unknown>"));
                }
            }

            if (state.ManualTargetOverride && state.LastTargetPrice > 0 && !state.PendingAutoTargetUpdate)
            {
                double currentTarget = state.TargetOrder != null ? state.TargetOrder.LimitPrice : 0;
                if (currentTarget <= 0 || !PricesClose(currentTarget, state.LastTargetPrice))
                {
                    AlignManagedTargetWithManual(state.TradeId, state.LastTargetPrice);
                    targetSet = true;
                    if (Debug)
                        StrategyLogDebug(string.Format("[AUTO][TARGET] Enforced manual target {0:F2} for {1}.", state.LastTargetPrice, state.TradeId ?? "<unknown>"));
                }
            }
        }

        private void DetectManualStopTargetAdjustments(Order order, double limitPrice, double stopPrice, OrderState orderState)
        {
            if (order == null)
                return;
            string tradeId = ResolveTradeIdFromOrder(order);
            if (string.IsNullOrEmpty(tradeId))
                return;
            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return;

            string orderName = order.Name ?? string.Empty;
            bool isStopOrder = orderName.IndexOf("_BS", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isStopOrder && (order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit))
                isStopOrder = orderName.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isTargetOrder = orderName.IndexOf("_BT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isTargetOrder && order.OrderType == OrderType.Limit)
                isTargetOrder = orderName.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                orderName.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;
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
                if (!IsTerminalState(orderState))
                    ClearManualProtectionPending(state, true);
                double effectivePrice = stopPrice;
                if (effectivePrice <= 0 && order.StopPrice > 0)
                    effectivePrice = order.StopPrice;
                if (effectivePrice <= 0)
                    return;

                bool pendingMismatch = false;
                if (state.PendingAutoStopUpdate)
                {
                    if (!IsStopUpdateFinal(orderState))
                        return;

                    bool matchesPending = false;
                    if (state.PendingAutoStopPrice > 0)
                        matchesPending = PricesClose(state.PendingAutoStopPrice, effectivePrice);
                    else if (state.LastStopPrice > 0)
                        matchesPending = PricesClose(state.LastStopPrice, effectivePrice);

                    if (matchesPending)
                    {
                        state.PendingAutoStopUpdate = false;
                        state.PendingAutoStopPrice = 0;
                        state.LastStopPrice = effectivePrice;
                        return;
                    }
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    pendingMismatch = true;
                }

                if (state.LastStopPrice <= 0)
                {
                    state.LastStopPrice = effectivePrice;
                    if (!pendingMismatch)
                        return;
                }

                if (IsScaleInExpectedStop(effectivePrice))
                {
                    state.LastStopPrice = effectivePrice;
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    state.ManualStopOverride = false;
                    ClearManualProtectionPending(state, true);
                    scaleInLastStopPrice = effectivePrice;
                    stopSet = true;
                    return;
                }

                if (IsRunUpExpectedStop(state, effectivePrice))
                {
                    state.LastStopPrice = effectivePrice;
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    state.ManualStopOverride = false;
                    ClearManualProtectionPending(state, true);
                    state.RunUpLastStopPrice = effectivePrice;
                    stopSet = true;
                    return;
                }

                bool allowGlobalTrailing = EnableGlobalTrailing && !state.IsChopEntry && !state.RunUpActive && !state.IsStraddleEntry;
                if (allowGlobalTrailing && !PricesClose(state.LastStopPrice, effectivePrice))
                {
                    state.LastStopPrice = effectivePrice;
                    state.PendingAutoStopUpdate = false;
                    state.PendingAutoStopPrice = 0;
                    ClearManualProtectionPending(state, true);
                    stopSet = true;

                    if (globalTrailSide == MarketPosition.Flat)
                        globalTrailSide = state.EntrySide;
                    if (globalTrailLastStopPrice <= 0)
                        globalTrailLastStopPrice = effectivePrice;
                    else
                        globalTrailLastStopPrice = state.EntrySide == MarketPosition.Long
                            ? Math.Max(globalTrailLastStopPrice, effectivePrice)
                            : Math.Min(globalTrailLastStopPrice, effectivePrice);

                    if (globalTrailActivated)
                    {
                        state.ManualStopOverride = false;
                        StrategyLogInfo(string.Format("[AUTO][STOP] Manual stop move for {0} -> {1:F2}; global trailing remains active.", tradeId, effectivePrice));
                        return;
                    }

                    bool wasLocked = state.ManualStopOverride;
                    state.ManualStopOverride = true;
                    StrategyLogInfo(string.Format("[AUTO][STOP] Manual stop move for {0} -> {1:F2}; holding manual until global trail activates.", tradeId, effectivePrice));
                    if (!ApplyManualStopToGroup(tradeId, effectivePrice, wasLocked))
                    {
                        if (!wasLocked)
                            NotifyAddonManualOverride(tradeId, true, null);
                        AlignManagedStopWithManual(tradeId, effectivePrice);
                    }
                    return;
                }

                if (!PricesClose(state.LastStopPrice, effectivePrice))
                {
                    state.LastStopPrice = effectivePrice;
                    if (state.RunUpActive)
                        state.RunUpLastStopPrice = effectivePrice;
                    bool wasLocked = state.ManualStopOverride;
                    state.ManualStopOverride = true;
                    ClearManualProtectionPending(state, true);
                    StrategyLogInfo(string.Format("[AUTO][STOP] Detected manual stop move for {0} -> {1:F2}; auto trailing disabled for this trade.", tradeId, effectivePrice));
                    if (!ApplyManualStopToGroup(tradeId, effectivePrice, wasLocked))
                    {
                        if (!wasLocked)
                            NotifyAddonManualOverride(tradeId, true, null);
                        AlignManagedStopWithManual(tradeId, effectivePrice);
                    }
                }
            }
            else if (isTargetOrder)
            {
                if (!IsTerminalState(orderState))
                    ClearManualProtectionPending(state, false);
                double effectivePrice = limitPrice;
                if (effectivePrice <= 0 && order.LimitPrice > 0)
                    effectivePrice = order.LimitPrice;
                if (effectivePrice <= 0)
                    return;

                bool pendingMismatch = false;
                if (state.PendingAutoTargetUpdate)
                {
                    if (!IsStopUpdateFinal(orderState))
                        return;

                    bool matchesPending = false;
                    if (state.PendingAutoTargetPrice > 0)
                        matchesPending = PricesClose(state.PendingAutoTargetPrice, effectivePrice);
                    else if (state.LastTargetPrice > 0)
                        matchesPending = PricesClose(state.LastTargetPrice, effectivePrice);

                    if (matchesPending)
                    {
                        state.PendingAutoTargetUpdate = false;
                        state.PendingAutoTargetPrice = 0;
                        state.LastTargetPrice = effectivePrice;
                        return;
                    }

                    state.PendingAutoTargetUpdate = false;
                    state.PendingAutoTargetPrice = 0;
                    pendingMismatch = true;
                }

                if (state.LastTargetPrice <= 0)
                {
                    state.LastTargetPrice = effectivePrice;
                    if (!pendingMismatch)
                        return;
                }

                if (!PricesClose(state.LastTargetPrice, effectivePrice))
                {
                    state.LastTargetPrice = effectivePrice;
                    bool wasLocked = state.ManualTargetOverride;
                    state.ManualTargetOverride = true;
                    ClearManualProtectionPending(state, false);
                    StrategyLogInfo(string.Format("[AUTO][TARGET] Detected manual target move for {0} -> {1:F2}; auto target locked for this trade.", tradeId, effectivePrice));
                    if (!ApplyManualTargetToGroup(tradeId, effectivePrice, wasLocked))
                    {
                        if (!wasLocked)
                            NotifyAddonManualOverride(tradeId, null, true);
                        AlignManagedTargetWithManual(tradeId, effectivePrice);
                    }
                }
            }
        }

        private void DetectManualChopEntryAdjustments(Order order, double limitPrice, OrderState orderState)
        {
            if (order == null)
                return;

            if (order.OrderAction != OrderAction.Buy && order.OrderAction != OrderAction.SellShort)
                return;

            if (order.OrderType != OrderType.Limit)
                return;

            if (orderState == OrderState.Filled || orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                return;

            string tradeId = order.FromEntrySignal;
            if (string.IsNullOrEmpty(tradeId))
                tradeId = order.Name;
            if (string.IsNullOrEmpty(tradeId))
                return;

            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return;

            if (state == null || !state.IsChopEntry)
                return;

            bool isLongOrder = order.OrderAction == OrderAction.Buy;
            bool isShortOrder = order.OrderAction == OrderAction.SellShort;
            if (state.EntrySide == MarketPosition.Long && !isLongOrder)
                return;
            if (state.EntrySide == MarketPosition.Short && !isShortOrder)
                return;

            double effectivePrice = limitPrice;
            if (effectivePrice <= 0 && order.LimitPrice > 0)
                effectivePrice = order.LimitPrice;
            if (effectivePrice <= 0)
                return;

            if (state.PendingEntryPriceUpdate)
            {
                if (PricesClose(state.PendingEntryLimitPrice, effectivePrice))
                {
                    state.PendingEntryPriceUpdate = false;
                    state.PendingEntryLimitPrice = 0;
                    return;
                }

                // Ignore stale updates while an auto-adjust is in flight.
                state.PendingEntryPriceUpdate = false;
                state.PendingEntryLimitPrice = 0;
                return;
            }

            if (state.LastAutoEntryLimitPrice <= 0)
            {
                state.LastAutoEntryLimitPrice = effectivePrice;
                return;
            }

            if (!lastChopRangeReady || lastChopRangeHigh <= 0 || lastChopRangeLow <= 0)
                return;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return;

            double baseEdge = state.EntrySide == MarketPosition.Long ? lastChopRangeLow : lastChopRangeHigh;
            if (baseEdge <= 0)
                return;

            double offsetTicks = (effectivePrice - baseEdge) / tickSize;
            double currentOffset = GetChopManualOffsetTicks(state.EntrySide);

            if (PricesClose(state.LastAutoEntryLimitPrice, effectivePrice))
                return;

            if (Math.Abs(offsetTicks - currentOffset) > 0.01)
            {
                SetChopManualOffsetTicks(state.EntrySide, offsetTicks);
                state.LastAutoEntryLimitPrice = effectivePrice;
                StrategyLogInfo(string.Format("[CHOP][ORDER] Manual {0} entry offset set to {1:F2} ticks (price={2:F2}).",
                    state.EntrySide,
                    offsetTicks,
                    effectivePrice));
            }
        }

        // Flatten any open position when the strategy terminates or encounters fatal errors.
        private void TryFlattenActivePosition(string reason)
        {
            try
            {
                if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity <= 0)
                    return;

                double strategySigned = GetSignedStrategyPosition();
                int strategyQty = (int)Math.Abs(strategySigned);
                int accountQty = GetAccountInstrumentSignedQuantity();
                if (accountQty == 0)
                {
                    StrategyLogInfo($"[SAFETY] Skip termination flatten; account flat (strategy {Position.MarketPosition} {Position.Quantity}).");
                    return;
                }
                if (strategyQty <= 0)
                    return;
                if (Math.Sign(accountQty) != Math.Sign(strategySigned))
                {
                    StrategyLogInfo($"[SAFETY] Skip termination flatten; account side mismatch (strategy {Position.MarketPosition} {Position.Quantity}, account {accountQty}).");
                    return;
                }

                int qty = Math.Min(strategyQty, Math.Abs(accountQty));
                if (qty <= 0)
                    return;
                var activeStates = new List<TradeRuntimeState>();
                if (openTradeOrder != null && openTradeOrder.Count > 0 && tradeStates != null)
                {
                    foreach (var tradeId in openTradeOrder)
                    {
                        if (string.IsNullOrEmpty(tradeId))
                            continue;
                        TradeRuntimeState st;
                        if (tradeStates.TryGetValue(tradeId, out st) && st != null && st.RemainingQuantity > 0 && !st.IsSynthetic)
                            activeStates.Add(st);
                    }
                }
                if (activeStates.Count == 0 && tradeStates != null)
                {
                    foreach (var st in tradeStates.Values)
                    {
                        if (st != null && st.RemainingQuantity > 0 && !st.IsSynthetic)
                            activeStates.Add(st);
                    }
                }

                if (IsMultiEntrySyncEnabled)
                {
                    MultiEntrySyncGroup group;
                    string seedId = !string.IsNullOrEmpty(activeTradeId)
                        ? activeTradeId
                        : (activeStates.Count > 0 ? activeStates[0].TradeId : null);
                    if (!string.IsNullOrEmpty(seedId) && TryGetMultiEntrySyncGroupByTradeId(seedId, out group))
                    {
                        int remaining = Math.Min(qty, GetMultiEntrySyncRemainingQuantity(group.TradeId));
                        if (remaining > 0)
                        {
                            StrategyLogInfo($"[SAFETY] Flattening {Position.MarketPosition.ToString().ToLowerInvariant()} {remaining} across sync group due to {reason}");
                            ExitMultiEntrySyncTrades(group.TradeId, remaining, "TERM");
                            return;
                        }
                    }
                }

                if (activeStates.Count > 1)
                {
                    int remaining = qty;
                    foreach (var st in activeStates)
                    {
                        if (st == null || st.RemainingQuantity <= 0)
                            continue;
                        int stateQty = Math.Max(1, st.RemainingQuantity);
                        int exitQty = Math.Min(remaining, stateQty);
                        if (exitQty <= 0)
                            continue;

                        string exitSignal = BuildExitSignalName(st.TradeId, "TERM");
                        string fromEntry = st.Bootstrapped ? null : st.TradeId;
                        if (Position.MarketPosition == MarketPosition.Long)
                            ExitLong(exitQty, exitSignal, fromEntry);
                        else if (Position.MarketPosition == MarketPosition.Short)
                            ExitShort(exitQty, exitSignal, fromEntry);

                        remaining -= exitQty;
                        if (remaining <= 0)
                            break;
                    }
                    if (remaining <= 0)
                        return;
                }

                string flattenTradeId = activeTradeId;
                if (string.IsNullOrEmpty(flattenTradeId) && openTradeOrder != null && openTradeOrder.Count > 0)
                    flattenTradeId = openTradeOrder[openTradeOrder.Count - 1];

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    StrategyLogInfo($"[SAFETY] Flattening long {qty} due to {reason}");
                    if (!string.IsNullOrEmpty(flattenTradeId))
                        ExitLong(qty, "STRAT_TERM_FLAT_L", flattenTradeId);
                    else
                        SubmitAccountFlatten(OrderAction.Sell, qty, reason);
                }
                else if (Position.MarketPosition == MarketPosition.Short)
                {
                    StrategyLogInfo($"[SAFETY] Flattening short {qty} due to {reason}");
                    if (!string.IsNullOrEmpty(flattenTradeId))
                        ExitShort(qty, "STRAT_TERM_FLAT_S", flattenTradeId);
                    else
                        SubmitAccountFlatten(OrderAction.BuyToCover, qty, reason);
                }
            }
            catch (Exception ex)
            {
                StrategyLogError($"[SAFETY] Failed to flatten on termination: {ex.Message}");
            }
        }

        private void SubmitAccountFlatten(OrderAction action, int quantity, string reason)
        {
            if (Account == null || Instrument == null || quantity <= 0)
                return;

            try
            {
                var order = Account.CreateOrder(
                    Instrument,
                    action,
                    OrderType.Market,
                    OrderEntry.Manual,
                    TimeInForce.Day,
                    Math.Abs(quantity),
                    0,
                    0,
                    string.Empty,
                    "STRAT_TERM_FLAT",
                    default(DateTime),
                    null);

                Account.Submit(new[] { order });
                StrategyLogInfo($"[SAFETY] Submitted account-level flatten due to {reason} (action={action}, qty={quantity})");
            }
            catch (Exception ex)
            {
                StrategyLogError($"[SAFETY] Account-level flatten failed due to {reason}: {ex.Message}");
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

                bool skipManual = false;
                if (!string.IsNullOrEmpty(reason))
                {
                    if (reason.IndexOf("manual_halt_enforce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        reason.IndexOf("position_flat", StringComparison.OrdinalIgnoreCase) >= 0)
                        skipManual = true;
                }
                if (skipManual)
                {
                    bool isManual = state.IsManualEntry ||
                                    (!string.IsNullOrEmpty(state.EntryContext) &&
                                     state.EntryContext.StartsWith("MANUAL", StringComparison.OrdinalIgnoreCase));
                    if (isManual)
                        continue;
                }

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

        private void CancelTrackedOrdersOnShutdown(string reason)
        {
            if (Account == null)
                return;

            var knownTradeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (tradeStates != null)
            {
                foreach (var state in tradeStates.Values)
                {
                    if (!string.IsNullOrEmpty(state?.TradeId))
                        knownTradeIds.Add(state.TradeId);
                }
            }
            if (!string.IsNullOrEmpty(activeTradeId))
                knownTradeIds.Add(activeTradeId);
            if (openTradeOrder != null && openTradeOrder.Count > 0)
            {
                foreach (var tradeId in openTradeOrder)
                {
                    if (!string.IsNullOrEmpty(tradeId))
                        knownTradeIds.Add(tradeId);
                }
            }

            if (knownTradeIds.Count == 0)
                return;

            int cancelled = 0;
            try
            {
                var orders = Account.Orders != null ? new List<Order>(Account.Orders) : new List<Order>();
                bool skipProtective = Position != null && Position.MarketPosition != MarketPosition.Flat;
                foreach (var order in orders)
                {
                    if (order == null || !IsOrderWorking(order))
                        continue;

                    string name = order.Name ?? string.Empty;
                    string fromEntry = order.FromEntrySignal ?? string.Empty;
                    bool match = false;

                    foreach (var tradeId in knownTradeIds)
                    {
                        if (!string.IsNullOrEmpty(fromEntry) && string.Equals(fromEntry, tradeId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                        if (!string.IsNullOrEmpty(name) && name.StartsWith(tradeId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }

                    if (!match)
                        continue;

                    if (skipProtective)
                    {
                        bool isProtective = order.OrderType == OrderType.StopMarket || order.OrderType == OrderType.StopLimit;
                        if (!isProtective && order.OrderType == OrderType.Limit)
                        {
                            isProtective = name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                           name.IndexOf("profit", StringComparison.OrdinalIgnoreCase) >= 0;
                        }
                        if (!isProtective && name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) >= 0)
                            isProtective = true;

                        if (isProtective)
                            continue;
                    }

                    try
                    {
                        Account.Cancel(new[] { order });
                        cancelled++;
                    }
                    catch { }
                }
            }
            catch { }

            if (cancelled > 0)
                StrategyLogInfo($"[SAFETY] Cancelled {cancelled} tracked order(s) due to {reason}");
        }

        private bool HasWorkingEntryOrders()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state == null)
                    continue;
                if (state.EntryOrderPending)
                    return true;
                var order = state.EntryOrder;
                if (order == null)
                    continue;
                if (!IsTerminalState(order.OrderState))
                    return true;
            }

            return false;
        }

        private bool HasWorkingNonChopEntryOrders()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.IsChopEntry)
                    continue;
                if (state.EntryOrderPending)
                    return true;
                var order = state.EntryOrder;
                if (order == null)
                    continue;
                if (!IsTerminalState(order.OrderState))
                    return true;
            }

            return false;
        }

        private bool IsChopRangeTradingEnabled()
        {
            return EnableChopRangeTrades && EnableChopFilter;
        }

        private bool HasOpenChopTrades()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state != null && state.IsChopEntry && state.RemainingQuantity > 0)
                {
                    if (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))
                        continue;
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<TradeRuntimeState> EnumerateWorkingChopEntryStates()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                yield break;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || !state.IsChopEntry)
                    continue;

                if (state.EntryOrderPending)
                {
                    yield return state;
                    continue;
                }

                var order = state.EntryOrder;
                if (order == null || IsTerminalState(order.OrderState))
                    continue;

                yield return state;
            }
        }

        private bool HasWorkingChopEntryOrders()
        {
            return EnumerateWorkingChopEntryStates().Any();
        }

        private bool HasWorkingChopEntryOrders(MarketPosition side)
        {
            if (side != MarketPosition.Long && side != MarketPosition.Short)
                return false;

            foreach (var state in EnumerateWorkingChopEntryStates())
            {
                if (state != null && state.EntrySide == side)
                    return true;
            }

            return false;
        }

        private void CancelWorkingChopEntryOrders(string reason)
        {
            foreach (var state in EnumerateWorkingChopEntryStates().ToList())
            {
                var order = state.EntryOrder;
                if (order == null)
                {
                    if (state.EntryOrderPending)
                    {
                        state.EntryCancelRequested = true;
                        if (Debug)
                            StrategyLogDebug($"[CHOP][ORDER] Flagged pending entry {state.TradeId ?? "<entry>"} for cancel due to {reason}.");
                    }
                    continue;
                }

                try
                {
                    state.EntryCancelRequested = true;
                    CancelOrder(order);
                    StrategyLogInfo($"[CHOP][ORDER] Cancelled chop entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}");
                }
                catch (Exception ex)
                {
                    StrategyLogError($"[CHOP][ORDER] Failed to cancel chop entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}: {ex.Message}");
                }
            }
        }

        private void CancelWorkingChopEntryOrders(MarketPosition side, string reason)
        {
            if (side != MarketPosition.Long && side != MarketPosition.Short)
                return;

            foreach (var state in EnumerateWorkingChopEntryStates().ToList())
            {
                if (state.EntrySide != side)
                    continue;

                var order = state.EntryOrder;
                if (order == null)
                {
                    if (state.EntryOrderPending)
                    {
                        state.EntryCancelRequested = true;
                        if (Debug)
                            StrategyLogDebug($"[CHOP][ORDER] Flagged pending {side} entry {state.TradeId ?? "<entry>"} for cancel due to {reason}.");
                    }
                    continue;
                }

                try
                {
                    state.EntryCancelRequested = true;
                    CancelOrder(order);
                    StrategyLogInfo($"[CHOP][ORDER] Cancelled chop entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}");
                }
                catch (Exception ex)
                {
                    StrategyLogError($"[CHOP][ORDER] Failed to cancel chop entry {order.Name ?? state.TradeId ?? "<entry>"} due to {reason}: {ex.Message}");
                }
            }
        }

        private double GetChopManualOffsetTicks(MarketPosition side)
        {
            if (side == MarketPosition.Long)
                return chopLongManualOffsetTicks ?? 0;
            if (side == MarketPosition.Short)
                return chopShortManualOffsetTicks ?? 0;
            return 0;
        }

        private void SetChopManualOffsetTicks(MarketPosition side, double offsetTicks)
        {
            if (side == MarketPosition.Long)
                chopLongManualOffsetTicks = offsetTicks;
            else if (side == MarketPosition.Short)
                chopShortManualOffsetTicks = offsetTicks;
        }

        private double? GetChopEntryLimitPrice(MarketPosition side, double rangeHigh, double rangeLow)
        {
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double offsetTicks = GetChopManualOffsetTicks(side);
            double desired = side == MarketPosition.Long
                ? rangeLow + offsetTicks * tickSize
                : rangeHigh + offsetTicks * tickSize;

            if (desired <= 0 || double.IsNaN(desired))
                return null;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? desired;
        }

        private MultiEntrySyncGroup ResolveChopEntrySyncGroup(MarketPosition side)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return null;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || !state.IsChopEntry || state.EntrySide != side)
                    continue;
                if (string.IsNullOrEmpty(state.SyncTradeId))
                    continue;
                MultiEntrySyncGroup group;
                if (multiEntrySyncGroups.TryGetValue(state.SyncTradeId, out group))
                    return group;
            }

            return null;
        }

        private void UpdateChopEntryOrders(double rangeHigh, double rangeLow, bool allowLong, bool allowShort, bool forceFlip = false)
        {
            if (!IsChopRangeTradingEnabled())
                return;

            int entriesToSubmit = GetEffectiveChopTradesPerEntry();
            int quantityPerEntry = Math.Max(1, DefaultQuantity);

            double? longLimit = allowLong ? GetChopEntryLimitPrice(MarketPosition.Long, rangeHigh, rangeLow) : null;
            double? shortLimit = allowShort ? GetChopEntryLimitPrice(MarketPosition.Short, rangeHigh, rangeLow) : null;

            int workingLong = 0;
            int workingShort = 0;
            bool hasWorkingLong = false;
            bool hasWorkingShort = false;
            bool cancelledLong = false;
            bool cancelledShort = false;

            foreach (var state in EnumerateWorkingChopEntryStates().ToList())
            {
                if (state == null)
                    continue;

                var order = state.EntryOrder;
                if (state.EntryOrderPending && order == null)
                {
                    if (state.EntrySide == MarketPosition.Long)
                    {
                        if (state.EntryCancelRequested)
                        {
                            cancelledLong = true;
                        }
                        else
                        {
                            hasWorkingLong = true;
                            workingLong++;
                        }
                        if (!allowLong || !longLimit.HasValue)
                            state.EntryCancelRequested = true;
                    }
                    else if (state.EntrySide == MarketPosition.Short)
                    {
                        if (state.EntryCancelRequested)
                        {
                            cancelledShort = true;
                        }
                        else
                        {
                            hasWorkingShort = true;
                            workingShort++;
                        }
                        if (!allowShort || !shortLimit.HasValue)
                            state.EntryCancelRequested = true;
                    }
                    continue;
                }
                if (order == null || IsTerminalState(order.OrderState))
                    continue;

                if (state.EntryCancelRequested &&
                    (order.OrderState == OrderState.CancelPending || order.OrderState == OrderState.CancelSubmitted))
                {
                    if (state.EntrySide == MarketPosition.Long)
                        cancelledLong = true;
                    else if (state.EntrySide == MarketPosition.Short)
                        cancelledShort = true;
                    continue;
                }

                if (state.EntrySide == MarketPosition.Long)
                    hasWorkingLong = true;
                else if (state.EntrySide == MarketPosition.Short)
                    hasWorkingShort = true;

                if (state.EntrySide == MarketPosition.Long)
                {
                    if (!allowLong || !longLimit.HasValue)
                    {
                        state.EntryCancelRequested = true;
                        TryCancelOrder(state.TradeId, order, null, "chop_entry_long_blocked");
                        cancelledLong = true;
                        continue;
                    }

                    workingLong++;
                    double desired = longLimit.Value;
                    double current = order.LimitPrice > 0 ? order.LimitPrice : desired;
                    if (!PricesClose(current, desired))
                    {
                        state.PendingEntryPriceUpdate = true;
                        state.PendingEntryLimitPrice = desired;
                        state.LastAutoEntryLimitPrice = desired;
                        try
                        {
                            ChangeOrder(order, order.Quantity, desired, order.StopPrice);
                            StrategyLogDebug($"[CHOP][ORDER] Adjusted long entry {state.TradeId} -> {desired:F2}");
                        }
                        catch (Exception ex)
                        {
                            state.PendingEntryPriceUpdate = false;
                            StrategyLogError($"[CHOP][ORDER] Failed to adjust long entry {state.TradeId}: {ex.Message}");
                        }
                    }
                }
                else if (state.EntrySide == MarketPosition.Short)
                {
                    if (!allowShort || !shortLimit.HasValue)
                    {
                        state.EntryCancelRequested = true;
                        TryCancelOrder(state.TradeId, order, null, "chop_entry_short_blocked");
                        cancelledShort = true;
                        continue;
                    }

                    workingShort++;
                    double desired = shortLimit.Value;
                    double current = order.LimitPrice > 0 ? order.LimitPrice : desired;
                    if (!PricesClose(current, desired))
                    {
                        state.PendingEntryPriceUpdate = true;
                        state.PendingEntryLimitPrice = desired;
                        state.LastAutoEntryLimitPrice = desired;
                        try
                        {
                            ChangeOrder(order, order.Quantity, desired, order.StopPrice);
                            StrategyLogDebug($"[CHOP][ORDER] Adjusted short entry {state.TradeId} -> {desired:F2}");
                        }
                        catch (Exception ex)
                        {
                            state.PendingEntryPriceUpdate = false;
                            StrategyLogError($"[CHOP][ORDER] Failed to adjust short entry {state.TradeId}: {ex.Message}");
                        }
                    }
                }
            }

            if (allowLong && longLimit.HasValue)
            {
                int toSubmit = Math.Max(0, entriesToSubmit - workingLong);
                if (toSubmit > 0)
                {
                    bool blockLong = hasWorkingShort && !cancelledShort && !forceFlip;
                    if (blockLong)
                    {
                        if (Debug)
                            StrategyLogDebug("[CHOP][ORDER] Holding long entry until short entry cancels.");
                    }
                    else
                    {
                    MultiEntrySyncGroup group = ResolveChopEntrySyncGroup(MarketPosition.Long);
                    if (group == null)
                        group = StartMultiEntrySyncGroup(MarketPosition.Long, entriesToSubmit, quantityPerEntry, true);

                    for (int i = 0; i < toSubmit; i++)
                    {
                        string tradeId = CreateTradeId(MarketPosition.Long);
                        var state = PrepareTradeState(tradeId, MarketPosition.Long, quantityPerEntry);
                        state.IsChopEntry = true;
                        state.EntryContext = "CHOP";
                        state.LastAutoEntryLimitPrice = longLimit.Value;
                        state.EntryPrice = longLimit.Value;
                        state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                        state.EntryOrderPending = true;
                        state.EntryCancelRequested = false;
                        AttachTradeStateToSyncGroup(state, group);
                        StrategyLogInfo($"[CHOP][ORDER] Submit long limit {tradeId} @ {longLimit.Value:F2} ({i + 1}/{entriesToSubmit})");
                        EnterLongLimit(quantityPerEntry, longLimit.Value, tradeId);
                    }
                    }
                }
            }

            if (allowShort && shortLimit.HasValue)
            {
                int toSubmit = Math.Max(0, entriesToSubmit - workingShort);
                if (toSubmit > 0)
                {
                    bool blockShort = hasWorkingLong && !cancelledLong && !forceFlip;
                    if (blockShort)
                    {
                        if (Debug)
                            StrategyLogDebug("[CHOP][ORDER] Holding short entry until long entry cancels.");
                    }
                    else
                    {
                    MultiEntrySyncGroup group = ResolveChopEntrySyncGroup(MarketPosition.Short);
                    if (group == null)
                        group = StartMultiEntrySyncGroup(MarketPosition.Short, entriesToSubmit, quantityPerEntry, true);

                    for (int i = 0; i < toSubmit; i++)
                    {
                        string tradeId = CreateTradeId(MarketPosition.Short);
                        var state = PrepareTradeState(tradeId, MarketPosition.Short, quantityPerEntry);
                        state.IsChopEntry = true;
                        state.EntryContext = "CHOP";
                        state.LastAutoEntryLimitPrice = shortLimit.Value;
                        state.EntryPrice = shortLimit.Value;
                        state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                        state.EntryOrderPending = true;
                        state.EntryCancelRequested = false;
                        AttachTradeStateToSyncGroup(state, group);
                        StrategyLogInfo($"[CHOP][ORDER] Submit short limit {tradeId} @ {shortLimit.Value:F2} ({i + 1}/{entriesToSubmit})");
                        EnterShortLimit(quantityPerEntry, shortLimit.Value, tradeId);
                    }
                    }
                }
            }
        }

        private void SubmitChopAddOnEntries(MarketPosition side, int entriesToSubmit, int quantityPerEntry)
        {
            if (entriesToSubmit <= 0 || quantityPerEntry <= 0)
                return;

            MultiEntrySyncGroup group = StartMultiEntrySyncGroup(side, entriesToSubmit, quantityPerEntry, true);
            for (int i = 0; i < entriesToSubmit; i++)
            {
                string tradeId = CreateTradeId(side);
                var state = PrepareTradeState(tradeId, side, quantityPerEntry);
                state.IsChopEntry = true;
                state.ChopTrailForced = true;
                state.EntryContext = "CHOP_ADD";
                AttachTradeStateToSyncGroup(state, group);

                StrategyLogInfo($"[CHOP][ADD] Submit add-on {side} {tradeId} qty={quantityPerEntry} ({i + 1}/{entriesToSubmit})");
                if (side == MarketPosition.Long)
                    EnterLong(quantityPerEntry, tradeId);
                else if (side == MarketPosition.Short)
                    EnterShort(quantityPerEntry, tradeId);
            }
        }

        private List<TradeRuntimeState> GetChopManagedStates()
        {
            var states = new List<TradeRuntimeState>();
            if (tradeStates == null || tradeStates.Count == 0)
                return states;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;
                if (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))
                    continue;
                if (state.IsChopEntry || state.ChopTrailActive || state.ChopTrailForced)
                    states.Add(state);
            }

            return states;
        }

        private double? GetChopStopPrice(TradeRuntimeState state, double currentPrice)
        {
            if (state == null)
                return null;

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                tickSize = 1e-6;

            double stopDistance = 0.0;
            if (ChopStopType == StopKind.ATR)
            {
                double atrValue = atr != null ? atr[0] : 0.0;
                if (atrValue > 0 && ChopStopAtrMult > 0)
                    stopDistance = atrValue * ChopStopAtrMult;
            }

            if (stopDistance <= 0.0)
            {
                int ticks = Math.Max(1, ChopStopTicks);
                stopDistance = ticks * tickSize;
            }

            if (stopDistance <= 0.0)
                return null;

            double entryRef = state.EntryPrice > 0
                ? state.EntryPrice
                : (Position != null && Position.AveragePrice > 0 ? Position.AveragePrice : currentPrice);
            if (entryRef <= 0)
                return null;

            double desired = state.EntrySide == MarketPosition.Long
                ? entryRef - stopDistance
                : entryRef + stopDistance;

            if (desired <= 0 || double.IsNaN(desired))
                return null;

            return Instrument?.MasterInstrument?.RoundToTickSize(desired) ?? desired;
        }

        private bool ApplyChopTrailingStops(List<TradeRuntimeState> states, double currentPrice)
        {
            if (states == null || states.Count == 0)
                return false;

            int trailTicks = Math.Max(1, ChopTrailTicks);
            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
                return false;

            bool isLong = states[0].EntrySide == MarketPosition.Long;
            double barHigh = High[0];
            double barLow = Low[0];

            double highWater = states.Max(s => s.ChopTrailHighWater > 0 ? s.ChopTrailHighWater : s.EntryPrice);
            double lowWater = states.Min(s => s.ChopTrailLowWater > 0 ? s.ChopTrailLowWater : s.EntryPrice);
            if (isLong)
                highWater = Math.Max(highWater, barHigh);
            else
                lowWater = Math.Min(lowWater, barLow);

            foreach (var state in states)
            {
                state.ChopTrailActive = true;
                state.ChopTrailForced = false;
                state.ChopTrailHighWater = highWater;
                state.ChopTrailLowWater = lowWater;
            }

            double entryRef = Position != null && Position.AveragePrice > 0 ? Position.AveragePrice : states[0].EntryPrice;
            double plusTicks = Math.Max(0, ChopTrailPlusTicks) * tickSize;
            double breakevenStop = isLong ? entryRef + plusTicks : entryRef - plusTicks;
            double trailDistance = trailTicks * tickSize;
            double desired = isLong ? highWater - trailDistance : lowWater + trailDistance;
            if (isLong)
                desired = Math.Max(desired, breakevenStop);
            else
                desired = Math.Min(desired, breakevenStop);

            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;
                if (IsManualProtectionHoldActive(state, true))
                    continue;

                double? clamped = ClampStopPrice(desired, currentPrice, isLong, state.LastStopPrice);
                if (!clamped.HasValue)
                    continue;

                if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                {
                    if (Debug)
                        StrategyLogDebug(string.Format("[CHOP][TRAIL] Updated {0} stop {1:F2} (trailTicks={2})", state.TradeId, clamped.Value, trailTicks));
                }
            }

            return true;
        }

        private bool ApplyChopStops(List<TradeRuntimeState> states, double currentPrice)
        {
            if (states == null || states.Count == 0)
                return false;

            foreach (var state in states)
            {
                if (state == null || state.RemainingQuantity <= 0)
                    continue;
                if (IsManualProtectionHoldActive(state, true))
                    continue;

                double? stopPrice = GetChopStopPrice(state, currentPrice);
                if (!stopPrice.HasValue)
                    continue;

                bool isLong = state.EntrySide == MarketPosition.Long;
                double? clamped = ClampStopPrice(stopPrice.Value, currentPrice, isLong, state.LastStopPrice);
                if (!clamped.HasValue)
                    continue;

                if (IssueStopLoss(state.TradeId, CalculationMode.Price, clamped.Value, false))
                {
                    if (Debug)
                        StrategyLogDebug(string.Format("[CHOP][STOP] Updated {0} hard stop {1:F2}", state.TradeId, clamped.Value));
                }
            }

            return true;
        }

        private bool TryApplyChopTradeProtection(double currentPrice, double rangeHigh, double rangeLow, double rangeMid, bool chopActive)
        {
            if (!IsChopRangeTradingEnabled())
                return false;
            if (Position == null || Position.MarketPosition == MarketPosition.Flat)
                return false;

            var states = GetChopManagedStates();
            if (states.Count == 0)
                return false;

            if (!chopActive)
            {
                StrategyLogInfo("[CHOP] Chop inactive; exiting chop-managed trades.");
                ExitChopRangeTrades("CHOP_OFF");
                return true;
            }

            bool forceActivate = states.Any(s => s != null && s.ChopTrailForced);
            bool trailAlreadyActive = states.Any(s => s != null && s.ChopTrailActive);
            double barHigh = High[0];
            double barLow = Low[0];
            bool midlineHit = Position.MarketPosition == MarketPosition.Long
                ? (currentPrice >= rangeMid || barHigh >= rangeMid)
                : (currentPrice <= rangeMid || barLow <= rangeMid);

            if (forceActivate || midlineHit || trailAlreadyActive)
            {
                if (ApplyChopTrailingStops(states, currentPrice))
                    return true;
            }

            return ApplyChopStops(states, currentPrice);
        }

        private void ForceChopTrailingForOpenTrades(double currentPrice)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return;

            foreach (var state in tradeStates.Values)
            {
                if (state == null || state.RemainingQuantity <= 0 || state.IsSynthetic)
                    continue;
                state.ChopTrailForced = true;
                state.ChopTrailActive = true;
                if (state.ChopTrailHighWater <= 0)
                    state.ChopTrailHighWater = currentPrice;
                if (state.ChopTrailLowWater <= 0)
                    state.ChopTrailLowWater = currentPrice;
            }
        }

        private void HandleStopUpdateErrors(Order order, double stopPrice, OrderState orderState, ErrorCode error, string nativeError)
        {
            if (order == null)
                return;

            string tradeId = order.FromEntrySignal;
            if (string.IsNullOrEmpty(tradeId))
                return;
            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return;
            if (IsManualProtectionHoldActive(state, true))
                return;
            if (ShouldSuppressProtectionRearm())
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
            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
                state != null &&
                !ShouldPublishTradeLifecycle(state))
            {
                return;
            }

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

            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
                return group.TradeId;

            return tradeId;
        }

        private void AlignManagedStopWithManual(string tradeId, double price)
        {
            if (string.IsNullOrEmpty(tradeId) || price <= 0)
                return;

            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return;

            if (state.IsSynthetic)
                return;

            state.PendingAutoStopUpdate = true;
            state.PendingAutoStopPrice = price;
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

            TradeRuntimeState state;
            if (!TryGetTradeState(tradeId, out state))
                return;

            if (state.IsSynthetic)
                return;

            state.PendingAutoTargetUpdate = true;
            state.PendingAutoTargetPrice = price;
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

        private void AppendChecklistLine(List<string> lines, List<bool?> states, string label, bool? passed)
        {
            if (string.IsNullOrWhiteSpace(label))
                return;

            string text = label;
            if (passed.HasValue)
                text = string.Format("[{0}] {1}", passed.Value ? "x" : " ", label);

            lines.Add(text);
            states.Add(passed);
        }

        private void UpdateEntryChecklist(int longVotes, int shortVotes, int effMinLong, int effMinShort,
            bool orbAllowsLong, bool orbAllowsShort, bool chopAllowsLong, bool chopAllowsShort,
            bool chopActive, bool chopDecayActive, double chopDecayAdxDelta, double chopDecayBbDelta, double chopAdx, double chopBbWidth, HtfSwingGateResult htfLongGate, HtfSwingGateResult htfShortGate,
            bool volExpEnabled, bool volExpOk, double volExpBbDelta, double volExpAtrRatio,
            bool rvolEnabled, bool rvolGateReady, bool rvolOk, bool vrocOk, double rvolValue, double vrocPct,
            bool canLong, bool canShort,
            bool vwapGateEnabled, bool vwapValuesReady, double vwapValue, double vwapDistancePct, int vwapSessionBars, double vwapSessionVolume, bool vwapGateAllowsLong, bool vwapGateAllowsShort,
            bool smaLongCond, bool smaShortCond, bool emaLongCond, bool emaShortCond, bool rsiLongCond, bool rsiShortCond, bool macdLongCond, bool macdShortCond)
        {
            if (ChartControl == null)
                return;

            bool readyLong = canLong;
            bool readyShort = canShort;

            string readiness;
            if (readyLong && readyShort)
                readiness = "READY LONG/SHORT";
            else if (readyLong)
                readiness = "READY LONG";
            else if (readyShort)
                readiness = "READY SHORT";
            else
                readiness = "READY NO";

            var lines = new List<string>();
            var states = new List<bool?>();

            AppendChecklistLine(lines, states, $"CHECK: {readiness}", readyLong || readyShort);

            bool useMeanReversion = EnableRegimeSwitching && chopActive;
            string regimeLabel = EnableRegimeSwitching
                ? (useMeanReversion ? "CHOP (MR)" : "TREND")
                : "TREND (switch off)";
            AppendChecklistLine(lines, states, $"Regime {regimeLabel}", null);
            AppendChecklistLine(lines, states, $"Entry Mode {(useMeanReversion ? "CHOP-RSI+BB" : "TREND-SPLIT")}", null);
            AppendChecklistLine(lines, states, $"Reverse Trading {(ReverseSignalTrading ? "ON" : "OFF")}", null);

            if (EnableVoteEntrySignals)
            {
                AppendChecklistLine(lines, states, $"Votes L {longVotes}/{effMinLong}", longVotes >= effMinLong);
                AppendChecklistLine(lines, states, $"Votes S {shortVotes}/{effMinShort}", shortVotes >= effMinShort);
            }

            if (EnableVoteEntrySignals && UseSMA)
            {
                string smaDir = smaLongCond ? "LONG" : smaShortCond ? "SHORT" : "NO";
                AppendChecklistLine(lines, states, $"SMA {smaDir}", smaLongCond || smaShortCond);
            }

            if (EnableVoteEntrySignals && UseEMA)
            {
                string emaDir = emaLongCond ? "LONG" : emaShortCond ? "SHORT" : "NO";
                AppendChecklistLine(lines, states, $"EMA {emaDir}", emaLongCond || emaShortCond);
            }

            if (EnableVoteEntrySignals && UseRSI)
            {
                string rsiDir = rsiLongCond ? "LONG" : rsiShortCond ? "SHORT" : "NO";
                AppendChecklistLine(lines, states, $"RSI {rsiDir}", rsiLongCond || rsiShortCond);
            }

            if (EnableVoteEntrySignals && UseMACD)
            {
                string macdDir = macdLongCond ? "LONG" : macdShortCond ? "SHORT" : "NO";
                AppendChecklistLine(lines, states, $"MACD {macdDir}", macdLongCond || macdShortCond);
            }

            if (EnableOrbFilter)
            {
                AppendChecklistLine(lines, states, $"ORB L {(orbAllowsLong ? "OK" : "NO")}", orbAllowsLong);
                AppendChecklistLine(lines, states, $"ORB S {(orbAllowsShort ? "OK" : "NO")}", orbAllowsShort);
            }

            if (EnableChopFilter)
            {
                AppendChecklistLine(lines, states, $"Chop L {(chopAllowsLong ? "OK" : "NO")}", chopAllowsLong);
                AppendChecklistLine(lines, states, $"Chop S {(chopAllowsShort ? "OK" : "NO")}", chopAllowsShort);
            }

            if (EnableHtfSwingGate)
            {
                bool htfLongOk = htfLongGate.HasData && !htfLongGate.Blocked;
                bool htfShortOk = htfShortGate.HasData && !htfShortGate.Blocked;
                AppendChecklistLine(lines, states, $"HTF L {FormatHtfGateSummary(htfLongGate, true)}", htfLongOk);
                AppendChecklistLine(lines, states, $"HTF S {FormatHtfGateSummary(htfShortGate, true)}", htfShortOk);
            }

            if (volExpEnabled)
            {
                string volExpAtrRatioText = volExpAtrRatio > 0 ? volExpAtrRatio.ToString("0.00") : "n/a";
                AppendChecklistLine(lines, states, $"VolExp {(volExpOk ? "OK" : "NO")} bbΔ {volExpBbDelta:F2}% ATRx {volExpAtrRatioText}", volExpOk);
            }

            if (rvolEnabled)
            {
                bool rvolPass = rvolGateReady && rvolOk && vrocOk;
                string rvolText = rvolGateReady ? (rvolPass ? "OK" : "NO") : "WARM";
                AppendChecklistLine(lines, states, $"RVOL {rvolText} rvol {rvolValue:0.00} vroc {vrocPct:0.0}%", rvolPass);
            }

            if (vwapGateEnabled)
            {
                AppendChecklistLine(lines, states, $"VWAP Gate L {(vwapGateAllowsLong ? "OK" : "NO")}", vwapGateAllowsLong);
                AppendChecklistLine(lines, states, $"VWAP Gate S {(vwapGateAllowsShort ? "OK" : "NO")}", vwapGateAllowsShort);
            }

            bool vwapPanelEnabled = ShowVwapMrVisuals || UseVwapDirectionGate;
            if (vwapPanelEnabled)
            {
                if (vwapValuesReady && vwapValue > 0.0)
                {
                    string distText = vwapDistancePct >= 0 ? $"+{vwapDistancePct:0.00}%" : $"{vwapDistancePct:0.00}%";
                    AppendChecklistLine(lines, states, $"VWAP Value {vwapValue:F2}", null);
                    AppendChecklistLine(lines, states, $"Distance {distText}", null);
                    AppendChecklistLine(lines, states, $"Session Bars {vwapSessionBars}", null);
                    AppendChecklistLine(lines, states, $"Session Vol {vwapSessionVolume:0}", null);
                }
                else
                {
                    AppendChecklistLine(lines, states, "VWAP Warming Up", null);
                }
            }

            if (lines.Count == 0)
            {
                RemoveDrawObject("BaseOptAutoChecklist");
                RemoveDrawObject("BaseOptAutoChecklistGreen");
                RemoveDrawObject("BaseOptAutoChecklistRed");
                RemoveDrawObject("BaseOptAutoChecklistNeutral");
                return;
            }

            string compositeKey = string.Join("\n", lines);
            bool highlight = readyLong || readyShort;
            if (string.Equals(compositeKey, lastChecklistText, StringComparison.Ordinal) &&
                highlight == lastChecklistHealthy)
            {
                return;
            }

            lastChecklistText = compositeKey;
            lastChecklistHealthy = highlight;

            bool pnlNegative;
            string pnlLine;
            string limitsLine;
            bool hasPnLLines = TryBuildDailyPnLLimitLines(out pnlLine, out pnlNegative, out limitsLine);
            int statusLines = 1 + (hasPnLLines ? 2 : 0);
            string padding = new string('\n', statusLines + 1);

            var green = new StringBuilder();
            var red = new StringBuilder();
            var neutral = new StringBuilder();
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
                RemoveDrawObject("BaseOptAutoChecklist");
                Draw.TextFixed(this, "BaseOptAutoChecklistGreen", green.ToString(), TextPosition.BottomLeft, Brushes.LimeGreen, font, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.TextFixed(this, "BaseOptAutoChecklistRed", red.ToString(), TextPosition.BottomLeft, Brushes.OrangeRed, font, Brushes.Transparent, Brushes.Transparent, 0);
                Draw.TextFixed(this, "BaseOptAutoChecklistNeutral", neutral.ToString(), TextPosition.BottomLeft, Brushes.LightGray, font, Brushes.Transparent, Brushes.Transparent, 0);
            }
            catch (Exception ex)
            {
                if (Debug)
                    StrategyLogDebug(string.Format("[AUTO][CHECKLIST] Failed to draw checklist: {0}", ex.Message));
            }
        }

        private string FormatHtfGateSummary(HtfSwingGateResult gate, bool includeSource)
        {
            if (!gate.HasData)
                return "n/a";

            string state = gate.Near ? (gate.Blocked ? "BLOCK" : "NEAR") : "CLEAR";
            string dist = double.IsNaN(gate.DistanceAtr) ? "n/a" : gate.DistanceAtr.ToString("0.00") + "ATR";
            if (!includeSource)
                return $"{state} {dist}";

            string source = string.Empty;
            if (!string.IsNullOrEmpty(gate.TimeframeLabel))
                source = gate.TimeframeLabel;
            if (!string.IsNullOrEmpty(gate.Source))
                source = string.IsNullOrEmpty(source) ? gate.Source : $"{source}:{gate.Source}";

            return string.IsNullOrEmpty(source) ? $"{state} {dist}" : $"{state} {dist} {source}";
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

                    var manualButtonsPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 2)
                    };

                    var manualHaltPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 2)
                    };

                    var manualTradePanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var biasTogglePanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 0, 0, 2)
                    };

                    var biasButtonsPanel = new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var toggleButtonsPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var manualOffsetPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 2, 0, 2)
                    };

                    var visualTogglePanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    visualButtonsPanel = new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };

                    var tradesPerEntryPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    var chopTradesPerEntryPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    var addOnTradePanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Margin = new Thickness(0, 2, 0, 0)
                    };

                    manualFlattenButton = new Button
                    {
                        Content = "Flatten + Halt",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    manualFlattenButton.Click += ManualFlattenButton_Click;

                    manualBuyButton = new Button
                    {
                        Content = "Manual Buy",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = Brushes.DarkGreen,
                        Foreground = Brushes.White,
                        ToolTip = "Enabled only while manual halt is active."
                    };
                    manualBuyButton.Click += ManualBuyButton_Click;

                    manualSellButton = new Button
                    {
                        Content = "Manual Sell",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = Brushes.DarkRed,
                        Foreground = Brushes.White,
                        ToolTip = "Enabled only while manual halt is active."
                    };
                    manualSellButton.Click += ManualSellButton_Click;

                    manualResumeButton = new Button
                    {
                        Content = "Resume",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    manualResumeButton.Click += ManualResumeButton_Click;

                    biasBothToggleButton = new Button
                    {
                        Content = "Both",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        ToolTip = "Allow both long and short entries."
                    };
                    biasBothToggleButton.Click += BiasToggleButton_Click;

                    biasLongToggleButton = new Button
                    {
                        Content = "LongOnly",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        ToolTip = "Only allow long entries."
                    };
                    biasLongToggleButton.Click += BiasToggleButton_Click;

                    biasShortToggleButton = new Button
                    {
                        Content = "ShortOnly",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        ToolTip = "Only allow short entries."
                    };
                    biasShortToggleButton.Click += BiasToggleButton_Click;

                    vwapGateToggleButton = new Button
                    {
                        Content = "VWAP Gate: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        ToolTip = "Gate entries using VWAP direction (above=long, below=short)."
                    };
                    vwapGateToggleButton.Click += VwapGateToggleButton_Click;

                    addOnTradeButton = new Button
                    {
                        Content = "Add-On Trade",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = Brushes.SteelBlue,
                        Foreground = Brushes.White,
                        ToolTip = "Adds an entry in the current position direction."
                    };
                    addOnTradeButton.Click += AddOnTradeButton_Click;

                    pnlTagsToggleButton = new Button
                    {
                        Content = "PnL Tags: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    pnlTagsToggleButton.Click += PnlTagsToggleButton_Click;

                    reverseSignalToggleButton = new Button
                    {
                        Content = "Reverse: OFF",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        ToolTip = "Toggle reverse-signal trading (take the opposite side)."
                    };
                    reverseSignalToggleButton.Click += ReverseSignalToggleButton_Click;

                    manualLimitButton = new Button
                    {
                        Content = "LMT",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = Brushes.DarkGreen,
                        Foreground = Brushes.White,
                        ToolTip = "Place a buy limit offset below current price."
                    };
                    manualLimitButton.Click += ManualLimitButton_Click;

                    manualStopButton = new Button
                    {
                        Content = "STP",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2),
                        Background = Brushes.DarkRed,
                        Foreground = Brushes.White,
                        ToolTip = "Place a sell stop offset below current price."
                    };
                    manualStopButton.Click += ManualStopButton_Click;

                    smaVisualToggleButton = new Button
                    {
                        Content = "SMA: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    smaVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    emaVisualToggleButton = new Button
                    {
                        Content = "EMA: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    emaVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    rsiVisualToggleButton = new Button
                    {
                        Content = "RSI: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    rsiVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    macdVisualToggleButton = new Button
                    {
                        Content = "MACD: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    macdVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    atrVisualToggleButton = new Button
                    {
                        Content = "ATR: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    atrVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    bbVisualToggleButton = new Button
                    {
                        Content = "Chop BB: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    bbVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    vwapVisualToggleButton = new Button
                    {
                        Content = "VWAP: ON",
                        Margin = new Thickness(2),
                        Padding = new Thickness(6, 2, 6, 2)
                    };
                    vwapVisualToggleButton.Click += IndicatorVisualToggleButton_Click;

                    tradesPerEntryLabel = new TextBlock
                    {
                        Text = "Trades/Entry",
                        Margin = new Thickness(2, 4, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    };

                    tradesPerEntryTextBox = new TextBox
                    {
                        Width = 50,
                        Margin = new Thickness(2),
                        Padding = new Thickness(4, 1, 4, 1),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        ToolTip = "Overrides TradesPerEntry while strategy is running."
                    };
                    tradesPerEntryTextBox.PreviewMouseDown += TradesPerEntryTextBox_PreviewMouseDown;
                    tradesPerEntryTextBox.PreviewKeyDown += TradesPerEntryTextBox_PreviewKeyDown;
                    tradesPerEntryTextBox.LostFocus += TradesPerEntryTextBox_LostFocus;

                    chopTradesPerEntryLabel = new TextBlock
                    {
                        Text = "Chop Trades/Entry",
                        Margin = new Thickness(2, 4, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    };

                    chopTradesPerEntryTextBox = new TextBox
                    {
                        Width = 50,
                        Margin = new Thickness(2),
                        Padding = new Thickness(4, 1, 4, 1),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        ToolTip = "Overrides ChopTradesPerEntry while strategy is running."
                    };
                    chopTradesPerEntryTextBox.PreviewMouseDown += ChopTradesPerEntryTextBox_PreviewMouseDown;
                    chopTradesPerEntryTextBox.PreviewKeyDown += ChopTradesPerEntryTextBox_PreviewKeyDown;
                    chopTradesPerEntryTextBox.LostFocus += ChopTradesPerEntryTextBox_LostFocus;

                    manualHaltPanel.Children.Add(manualFlattenButton);
                    manualHaltPanel.Children.Add(manualResumeButton);
                    manualTradePanel.Children.Add(manualBuyButton);
                    manualTradePanel.Children.Add(manualSellButton);
                    manualButtonsPanel.Children.Add(manualHaltPanel);
                    manualButtonsPanel.Children.Add(manualTradePanel);
                    biasTogglePanel.Children.Add(new TextBlock
                    {
                        Text = "Bias",
                        Margin = new Thickness(2, 4, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.White
                    });
                    biasButtonsPanel.Children.Add(biasBothToggleButton);
                    biasButtonsPanel.Children.Add(biasLongToggleButton);
                    biasButtonsPanel.Children.Add(biasShortToggleButton);
                    biasButtonsPanel.Children.Add(vwapGateToggleButton);
                    biasTogglePanel.Children.Add(biasButtonsPanel);
                    toggleButtonsPanel.Children.Add(pnlTagsToggleButton);
                    toggleButtonsPanel.Children.Add(reverseSignalToggleButton);
                    manualOffsetPanel.Children.Add(manualLimitButton);
                    manualOffsetPanel.Children.Add(manualStopButton);
                    visualsToggleButton = new Button
                    {
                        Content = "Visuals ▾",
                        Margin = new Thickness(2, 4, 6, 2),
                        Padding = new Thickness(6, 2, 6, 2),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Background = Brushes.DimGray,
                        Foreground = Brushes.White
                    };
                    visualsToggleButton.Click += VisualsToggleButton_Click;
                    visualTogglePanel.Children.Add(visualsToggleButton);
                    visualButtonsPanel.Children.Add(smaVisualToggleButton);
                    visualButtonsPanel.Children.Add(emaVisualToggleButton);
                    visualButtonsPanel.Children.Add(rsiVisualToggleButton);
                    visualButtonsPanel.Children.Add(macdVisualToggleButton);
                    visualButtonsPanel.Children.Add(atrVisualToggleButton);
                    visualButtonsPanel.Children.Add(bbVisualToggleButton);
                    visualButtonsPanel.Children.Add(vwapVisualToggleButton);
                    visualTogglePanel.Children.Add(visualButtonsPanel);
                    tradesPerEntryPanel.Children.Add(tradesPerEntryLabel);
                    tradesPerEntryPanel.Children.Add(tradesPerEntryTextBox);
                    chopTradesPerEntryPanel.Children.Add(chopTradesPerEntryLabel);
                    chopTradesPerEntryPanel.Children.Add(chopTradesPerEntryTextBox);
                    addOnTradePanel.Children.Add(addOnTradeButton);

                    chartTraderButtonPanel.Children.Add(manualButtonsPanel);
                    chartTraderButtonPanel.Children.Add(biasTogglePanel);
                    chartTraderButtonPanel.Children.Add(toggleButtonsPanel);
                    chartTraderButtonPanel.Children.Add(manualOffsetPanel);
                    chartTraderButtonPanel.Children.Add(visualTogglePanel);
                    chartTraderButtonPanel.Children.Add(tradesPerEntryPanel);
                    chartTraderButtonPanel.Children.Add(chopTradesPerEntryPanel);
                    chartTraderButtonPanel.Children.Add(addOnTradePanel);

                    Grid.SetRow(chartTraderButtonPanel, chartTraderGrid.RowDefinitions.Count - 1);
                    Grid.SetColumnSpan(chartTraderButtonPanel, Math.Max(1, chartTraderGrid.ColumnDefinitions.Count));
                    chartTraderGrid.Children.Add(chartTraderButtonPanel);

                    chartTraderButtonsAdded = true;
                    UpdateManualTradeButtons(true);
                    UpdatePnlTagToggleButton(true);
                    UpdateReverseSignalToggleButton(true);
                    UpdateIndicatorVisualButtons(true);
                    UpdateVisualsPanelVisibility(true);
                    PrimeIndicatorVisuals();
                    UpdateBiasToggleButtons(true);
                    UpdateVwapGateToggleButton(true);
                    UpdateTradesPerEntryInput(true);
                    UpdateChopTradesPerEntryInput(true);
                    UpdateAddOnTradeButton(true);
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

                    if (manualBuyButton != null)
                        manualBuyButton.Click -= ManualBuyButton_Click;
                    if (manualSellButton != null)
                        manualSellButton.Click -= ManualSellButton_Click;
                    if (manualLimitButton != null)
                        manualLimitButton.Click -= ManualLimitButton_Click;
                    if (manualStopButton != null)
                        manualStopButton.Click -= ManualStopButton_Click;
                    if (manualFlattenButton != null)
                        manualFlattenButton.Click -= ManualFlattenButton_Click;
                    if (manualResumeButton != null)
                        manualResumeButton.Click -= ManualResumeButton_Click;
                    if (biasBothToggleButton != null)
                        biasBothToggleButton.Click -= BiasToggleButton_Click;
                    if (biasLongToggleButton != null)
                        biasLongToggleButton.Click -= BiasToggleButton_Click;
                    if (biasShortToggleButton != null)
                        biasShortToggleButton.Click -= BiasToggleButton_Click;
                    if (vwapGateToggleButton != null)
                        vwapGateToggleButton.Click -= VwapGateToggleButton_Click;
                    if (addOnTradeButton != null)
                        addOnTradeButton.Click -= AddOnTradeButton_Click;
                    if (pnlTagsToggleButton != null)
                        pnlTagsToggleButton.Click -= PnlTagsToggleButton_Click;
                    if (reverseSignalToggleButton != null)
                        reverseSignalToggleButton.Click -= ReverseSignalToggleButton_Click;
                    if (visualsToggleButton != null)
                        visualsToggleButton.Click -= VisualsToggleButton_Click;
                    if (smaVisualToggleButton != null)
                        smaVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (emaVisualToggleButton != null)
                        emaVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (rsiVisualToggleButton != null)
                        rsiVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (macdVisualToggleButton != null)
                        macdVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (atrVisualToggleButton != null)
                        atrVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (bbVisualToggleButton != null)
                        bbVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (vwapVisualToggleButton != null)
                        vwapVisualToggleButton.Click -= IndicatorVisualToggleButton_Click;
                    if (tradesPerEntryTextBox != null)
                    {
                        tradesPerEntryTextBox.PreviewMouseDown -= TradesPerEntryTextBox_PreviewMouseDown;
                        tradesPerEntryTextBox.PreviewKeyDown -= TradesPerEntryTextBox_PreviewKeyDown;
                        tradesPerEntryTextBox.LostFocus -= TradesPerEntryTextBox_LostFocus;
                    }
                    if (chopTradesPerEntryTextBox != null)
                    {
                        chopTradesPerEntryTextBox.PreviewMouseDown -= ChopTradesPerEntryTextBox_PreviewMouseDown;
                        chopTradesPerEntryTextBox.PreviewKeyDown -= ChopTradesPerEntryTextBox_PreviewKeyDown;
                        chopTradesPerEntryTextBox.LostFocus -= ChopTradesPerEntryTextBox_LostFocus;
                    }

                    chartTraderButtonPanel = null;
                    manualBuyButton = null;
                    manualSellButton = null;
                    manualLimitButton = null;
                    manualStopButton = null;
                    manualFlattenButton = null;
                    manualResumeButton = null;
                    biasBothToggleButton = null;
                    biasLongToggleButton = null;
                    biasShortToggleButton = null;
                    vwapGateToggleButton = null;
                    addOnTradeButton = null;
                    pnlTagsToggleButton = null;
                    reverseSignalToggleButton = null;
                    visualsToggleButton = null;
                    smaVisualToggleButton = null;
                    emaVisualToggleButton = null;
                    rsiVisualToggleButton = null;
                    macdVisualToggleButton = null;
                    atrVisualToggleButton = null;
                    bbVisualToggleButton = null;
                    vwapVisualToggleButton = null;
                    visualButtonsPanel = null;
                    tradesPerEntryLabel = null;
                    tradesPerEntryTextBox = null;
                    chopTradesPerEntryLabel = null;
                    chopTradesPerEntryTextBox = null;
                    lastTradesPerEntryDisplay = -1;
                    lastChopTradesPerEntryDisplay = -1;
                    lastManualButtonsEnabled = false;
                    lastAddOnButtonEnabled = false;
                    lastBiasToggleValue = TradeBias.Both;
                    lastVwapGateToggleState = false;
                    lastReverseSignalToggleState = false;
                    lastSmaVisualToggleState = false;
                    lastEmaVisualToggleState = false;
                    lastRsiVisualToggleState = false;
                    lastMacdVisualToggleState = false;
                    lastAtrVisualToggleState = false;
                    lastBbVisualToggleState = false;
                    lastVwapVisualToggleState = false;
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

        private void ManualBuyButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOrderRequest(MarketPosition.Long), null);
        }

        private void ManualSellButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOrderRequest(MarketPosition.Short), null);
        }

        private void ManualLimitButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOffsetEntryRequest(MarketPosition.Long, OrderType.Limit), null);
        }

        private void ManualStopButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualOffsetEntryRequest(MarketPosition.Short, OrderType.StopMarket), null);
        }

        private void ManualResumeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleManualResumeRequest(), null);
        }

        private void AddOnTradeButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleAddOnTradeRequest(), null);
        }

        private void PnlTagsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandlePnlTagsToggleRequest(), null);
        }

        private void ReverseSignalToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleReverseSignalToggleRequest(), null);
        }

        private void VisualsToggleButton_Click(object sender, RoutedEventArgs e)
        {
            visualsPanelExpanded = !visualsPanelExpanded;
            UpdateVisualsPanelVisibility(true);
        }

        private void BiasToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == biasBothToggleButton)
                TriggerCustomEvent(o => HandleBiasToggleRequest(TradeBias.Both), null);
            else if (sender == biasLongToggleButton)
                TriggerCustomEvent(o => HandleBiasToggleRequest(TradeBias.LongOnly), null);
            else if (sender == biasShortToggleButton)
                TriggerCustomEvent(o => HandleBiasToggleRequest(TradeBias.ShortOnly), null);
        }

        private void VwapGateToggleButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerCustomEvent(o => HandleVwapGateToggleRequest(), null);
        }

        private void IndicatorVisualToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == smaVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Sma);
            else if (sender == emaVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Ema);
            else if (sender == rsiVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Rsi);
            else if (sender == macdVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Macd);
            else if (sender == atrVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Atr);
            else if (sender == bbVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Bollinger);
            else if (sender == vwapVisualToggleButton)
                HandleIndicatorVisualToggleRequest(IndicatorVisualType.Vwap);
        }

        private void TradesPerEntryTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (tradesPerEntryTextBox == null)
                return;

            tradesPerEntryTextBox.Focus();
            Keyboard.Focus(tradesPerEntryTextBox);
            tradesPerEntryTextBox.SelectAll();
            e.Handled = true;
        }

        private void TradesPerEntryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (tradesPerEntryTextBox == null)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                SubmitTradesPerEntryInput();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
                return;

            if (e.Key == Key.Back)
            {
                string current = tradesPerEntryTextBox.Text ?? string.Empty;
                int start = tradesPerEntryTextBox.SelectionStart;
                int length = tradesPerEntryTextBox.SelectionLength;
                if (length > 0)
                {
                    current = current.Remove(start, length);
                }
                else if (start > 0 && current.Length > 0)
                {
                    current = current.Remove(start - 1, 1);
                    start--;
                }
                tradesPerEntryTextBox.Text = current;
                tradesPerEntryTextBox.SelectionStart = Math.Max(0, start);
                tradesPerEntryTextBox.SelectionLength = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                string current = tradesPerEntryTextBox.Text ?? string.Empty;
                int start = tradesPerEntryTextBox.SelectionStart;
                int length = tradesPerEntryTextBox.SelectionLength;
                if (length > 0)
                {
                    current = current.Remove(start, length);
                }
                else if (start < current.Length)
                {
                    current = current.Remove(start, 1);
                }
                tradesPerEntryTextBox.Text = current;
                tradesPerEntryTextBox.SelectionStart = Math.Min(start, current.Length);
                tradesPerEntryTextBox.SelectionLength = 0;
                e.Handled = true;
            }
            else
            {
                char digit;
                if (TryGetDigitFromKey(e.Key, out digit))
                {
                    ApplyTradesPerEntryTextEdit(digit.ToString());
                    e.Handled = true;
                }
            }
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

        private void ApplyTradesPerEntryTextEdit(string insertText)
        {
            if (tradesPerEntryTextBox == null || string.IsNullOrEmpty(insertText))
                return;

            string current = tradesPerEntryTextBox.Text ?? string.Empty;
            int start = tradesPerEntryTextBox.SelectionStart;
            int length = tradesPerEntryTextBox.SelectionLength;
            if (start < 0)
                start = 0;
            if (start > current.Length)
                start = current.Length;
            if (length < 0)
                length = 0;
            if (start + length > current.Length)
                length = current.Length - start;

            string updated = current.Remove(start, length).Insert(start, insertText);
            tradesPerEntryTextBox.Text = updated;
            tradesPerEntryTextBox.SelectionStart = Math.Min(updated.Length, start + insertText.Length);
            tradesPerEntryTextBox.SelectionLength = 0;
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

        private void ChopTradesPerEntryTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (chopTradesPerEntryTextBox == null)
                return;

            chopTradesPerEntryTextBox.Focus();
            Keyboard.Focus(chopTradesPerEntryTextBox);
            chopTradesPerEntryTextBox.SelectAll();
            e.Handled = true;
        }

        private void ChopTradesPerEntryTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (chopTradesPerEntryTextBox == null)
                return;

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                SubmitChopTradesPerEntryInput();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
                return;

            if (e.Key == Key.Back)
            {
                string current = chopTradesPerEntryTextBox.Text ?? string.Empty;
                int start = chopTradesPerEntryTextBox.SelectionStart;
                int length = chopTradesPerEntryTextBox.SelectionLength;
                if (length > 0)
                {
                    current = current.Remove(start, length);
                }
                else if (start > 0 && current.Length > 0)
                {
                    current = current.Remove(start - 1, 1);
                    start--;
                }
                chopTradesPerEntryTextBox.Text = current;
                chopTradesPerEntryTextBox.SelectionStart = Math.Max(0, start);
                chopTradesPerEntryTextBox.SelectionLength = 0;
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                string current = chopTradesPerEntryTextBox.Text ?? string.Empty;
                int start = chopTradesPerEntryTextBox.SelectionStart;
                int length = chopTradesPerEntryTextBox.SelectionLength;
                if (length > 0)
                {
                    current = current.Remove(start, length);
                }
                else if (start < current.Length)
                {
                    current = current.Remove(start, 1);
                }
                chopTradesPerEntryTextBox.Text = current;
                chopTradesPerEntryTextBox.SelectionStart = Math.Min(start, current.Length);
                chopTradesPerEntryTextBox.SelectionLength = 0;
                e.Handled = true;
            }
            else
            {
                char digit;
                if (TryGetDigitFromKey(e.Key, out digit))
                {
                    ApplyChopTradesPerEntryTextEdit(digit.ToString());
                    e.Handled = true;
                }
            }
        }

        private void ApplyChopTradesPerEntryTextEdit(string insertText)
        {
            if (chopTradesPerEntryTextBox == null || string.IsNullOrEmpty(insertText))
                return;

            string current = chopTradesPerEntryTextBox.Text ?? string.Empty;
            int start = chopTradesPerEntryTextBox.SelectionStart;
            int length = chopTradesPerEntryTextBox.SelectionLength;
            if (start < 0)
                start = 0;
            if (start > current.Length)
                start = current.Length;
            if (length < 0)
                length = 0;
            if (start + length > current.Length)
                length = current.Length - start;

            string updated = current.Remove(start, length).Insert(start, insertText);
            chopTradesPerEntryTextBox.Text = updated;
            chopTradesPerEntryTextBox.SelectionStart = Math.Min(updated.Length, start + insertText.Length);
            chopTradesPerEntryTextBox.SelectionLength = 0;
        }

        private void ChopTradesPerEntryTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            SubmitChopTradesPerEntryInput();
        }

        private void SubmitChopTradesPerEntryInput()
        {
            if (chopTradesPerEntryTextBox == null)
                return;

            string text = chopTradesPerEntryTextBox.Text;
            TriggerCustomEvent(o => HandleChopTradesPerEntryOverrideRequest(o as string), text);
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
            manualHaltStatusText = "HALTED: manual flatten (awaiting resume)";
            manualHaltLastEnforceAttemptAt = DateTime.MinValue;
            manualHaltLastEnforceLogAt = DateTime.MinValue;

            try
            {
                MultiStratManager.Instance?.ActivateManualHaltOverride(Account != null ? Account.Name : string.Empty, Name);
            }
            catch { }

            CancelWorkingEntryOrders("manual_halt");
            int submitted = SubmitManualHaltExits("MHLT");
            if (submitted == 0)
                TryFlattenAccountEverything("manual_halt", activeTradeId ?? string.Empty, "MANUAL_HALT");
            StrategyLogInfo($"[MANUAL_HALT] Flatten requested (submittedExits={submitted}).");
            UpdateStatusLabel(manualHaltStatusText, false);
            UpdateManualTradeButtons(true);
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
            UpdateManualTradeButtons(true);
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
            SubmitManualOrder(direction);
        }

        private void HandleManualOffsetEntryRequest(MarketPosition direction, OrderType orderType)
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (strategy not realtime).");
                return;
            }

            if (!manualHaltActive)
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (manual halt not active).");
                return;
            }

            if (ManualEntryOffsetTicks <= 0)
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (offset ticks <= 0).");
                return;
            }

            double tickSize = Instrument?.MasterInstrument?.TickSize ?? TickSize;
            if (tickSize <= 0)
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (invalid tick size).");
                return;
            }

            double referencePrice;
            if (!TryGetStraddleTrackingPrice(out referencePrice))
                referencePrice = GetRealtimePrice();
            if (referencePrice <= 0 || double.IsNaN(referencePrice))
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (invalid price).");
                return;
            }

            double offset = ManualEntryOffsetTicks * tickSize;
            bool isLimitOrder = orderType == OrderType.Limit;
            bool isLong = direction == MarketPosition.Long;
            double desired = referencePrice + ((isLong == isLimitOrder) ? -offset : offset);

            double? bid = (!double.IsNaN(lastBid) && lastBid > 0) ? (double?)lastBid : null;
            double? ask = (!double.IsNaN(lastAsk) && lastAsk > 0) ? (double?)lastAsk : null;
            if (bid.HasValue && ask.HasValue)
            {
                if (isLimitOrder)
                {
                    if (isLong && desired >= ask.Value)
                        desired = ask.Value - tickSize;
                    else if (!isLong && desired <= bid.Value)
                        desired = bid.Value + tickSize;
                }
                else if (orderType == OrderType.StopMarket)
                {
                    if (isLong && desired <= ask.Value)
                        desired = ask.Value + tickSize;
                    else if (!isLong && desired >= bid.Value)
                        desired = bid.Value - tickSize;
                }
            }

            if (desired <= 0 || double.IsNaN(desired))
            {
                StrategyLogInfo("[MANUAL] Offset entry ignored (derived price invalid).");
                return;
            }

            double rounded = Instrument?.MasterInstrument?.RoundToTickSize(desired)
                ?? Math.Round(desired / tickSize) * tickSize;

            EnsureManualTradeStateForPosition();
            SubmitManualPriceOrder(direction, orderType, rounded);
        }

        private void HandleAddOnTradeRequest()
        {
            if (State != State.Realtime)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (strategy not realtime).");
                return;
            }

            if (!EnableScaleInTrades)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (scale-in disabled).");
                return;
            }

            if (shutdownInProgress)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (shutdown in progress).");
                return;
            }

            if (dailyPnLLimitHalted)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (daily limit halted).");
                return;
            }
            if (desyncHoldActive)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (desync hold active).");
                return;
            }

            if (Position == null || Position.MarketPosition == MarketPosition.Flat || Position.Quantity == 0)
            {
                StrategyLogInfo("[SCALE_IN] Add-on ignored (position flat).");
                return;
            }

            if (tradeStates == null || tradeStates.Count == 0)
                BootstrapExistingPositionState(true);

            StartScaleInHold(GetRealtimePrice());

            MarketPosition side = Position.MarketPosition;
            int qty = Math.Max(1, DefaultQuantity);
            SubmitScaleInEntries(side, 1, qty, true);
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

        private TradeRuntimeState CreateRestoredTradeStateFromTradeSyncRecord(TradeSyncService.TradeRecord record, string accountName, string instrumentName, int fallbackQuantity, double fallbackEntryPrice)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.TradeId))
                return null;

            int qty = record.RemainingQuantity > 0
                ? record.RemainingQuantity
                : Math.Max(1, record.NtQuantity > 0 ? record.NtQuantity : fallbackQuantity);
            int originalQty = record.NtQuantity > 0 ? record.NtQuantity : qty;
            double entryPrice = record.EntryPrice > 0 ? record.EntryPrice : fallbackEntryPrice;

            var state = new TradeRuntimeState
            {
                TradeId = record.TradeId,
                SyncTradeId = record.AggregateEntry && !record.IsScaleInTrade ? record.TradeId : null,
                EntrySide = record.Side,
                OriginalQuantity = originalQty,
                RemainingQuantity = qty,
                InstrumentName = string.IsNullOrWhiteSpace(record.Instrument) ? instrumentName : record.Instrument,
                AccountName = string.IsNullOrWhiteSpace(record.AccountName) ? accountName : record.AccountName,
                EntryPrice = entryPrice,
                OpenPublished = true,
                IsSynthetic = false,
                IsScaleInEntry = record.IsScaleInTrade,
                ManualStopOverride = record.ManualStopOverride,
                ManualTargetOverride = record.ManualTargetOverride,
                ManualStopPending = false,
                ManualTargetPending = false,
                ManualStopPendingUntil = DateTime.MinValue,
                ManualTargetPendingUntil = DateTime.MinValue,
                PendingAutoStopUpdate = false,
                PendingAutoTargetUpdate = false,
                PendingAutoStopPrice = 0,
                PendingAutoTargetPrice = 0,
                LastStopPrice = 0,
                LastTargetPrice = 0,
                RunUpActive = false,
                RunUpAnchorPrice = 0,
                RunUpInitialDistance = 0,
                RunUpIncrement = 0,
                RunUpLastStopPrice = null,
                RunUpHighWater = 0,
                RunUpLowWater = 0,
                IsVwapEntry = false,
                VwapIsFlip = false,
                VwapBandMultiplier = 0,
                VwapTargetPrice = 0,
                VwapNextBandPrice = 0,
                VwapTrailOnVwapTouch = false,
                VwapTrailActive = false,
                VwapTrailAnchorPrice = 0,
                VwapTrailDistance = 0,
                VwapTrailIncrement = 0,
                VwapTrailLastStopPrice = null,
                VwapTrailHighWater = 0,
                VwapTrailLowWater = 0,
                VwapFailureHigh = 0,
                VwapFailureLow = 0,
                VwapFailureCheckBar = -1,
                EntryBarIndex = -1,
                BreakEvenActivated = false,
                SyntheticLogEmitted = false,
                Bootstrapped = true,
                IsManualEntry = false,
                ExitAllTriggered = false,
                AllowOpenPublish = true,
                PendingClosePublish = false,
                ClosePublished = false,
                EntryOrder = null,
                StopOrder = null,
                TargetOrder = null,
                ProtectionRetryCount = 0,
                LastProtectionRetry = DateTime.MinValue,
                ProtectionRearmCount = 0,
                LastProtectionRearm = DateTime.MinValue,
                EntryVolExpEnabled = false,
                EntryVolExpOk = false,
                EntryVolExpBbWidthPct = 0,
                EntryVolExpBbDeltaPct = 0,
                EntryVolExpAtr = 0,
                EntryVolExpAtrBaseline = 0,
                EntryVolExpAtrRatio = 0,
                EntryRvolEnabled = false,
                EntryRvolReady = false,
                EntryRvolOk = false,
                EntryRvolValue = 0,
                EntryRvolAvg = 0,
                EntryVrocReady = false,
                EntryVrocOk = false,
                EntryVrocPct = 0
            };

            state.NtPointsPer1kLoss = record.NtPointsPer1kLoss;
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

            return state;
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

            List<TradeSyncService.TradeRecord> openTrades = tradeSync.GetOpenTradesSnapshot();
            if (openTrades == null || openTrades.Count == 0)
                return false;

            var matchingRecords = openTrades
                .Where(r =>
                    r != null &&
                    !string.IsNullOrWhiteSpace(r.TradeId) &&
                    r.Side == side &&
                    string.Equals((r.AccountName ?? string.Empty).Trim(), acct.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((r.Instrument ?? string.Empty).Trim(), inst.Trim(), StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.OpenedAtUtc)
                .ThenBy(r => r.TradeId)
                .ToList();

            if (matchingRecords.Count == 0)
                return false;

            if (tradeStates == null)
                tradeStates = new Dictionary<string, TradeRuntimeState>(StringComparer.OrdinalIgnoreCase);
            if (tradeStates.Count > 0 || openTradeOrder.Count > 0)
                return false;

            int posQty = Math.Max(1, Math.Abs(Position.Quantity));
            string restoredPrimaryTradeId = null;
            int restoredScaleInCount = 0;

            foreach (var record in matchingRecords)
            {
                var state = CreateRestoredTradeStateFromTradeSyncRecord(record, acct, inst, posQty, Position.AveragePrice);
                if (state == null)
                    continue;

                tradeStates[state.TradeId] = state;
                openTradeOrder.Add(state.TradeId);
                if (!state.IsScaleInEntry)
                    restoredPrimaryTradeId = state.TradeId;
                else if (state.RemainingQuantity > 0)
                    restoredScaleInCount++;
            }

            if (tradeStates.Count == 0 || openTradeOrder.Count == 0)
                return false;

            activeTradeId = !string.IsNullOrEmpty(restoredPrimaryTradeId)
                ? restoredPrimaryTradeId
                : openTradeOrder[openTradeOrder.Count - 1];
            stopSet = false;
            targetSet = false;

            ResetScaleInState();
            if (restoredScaleInCount > 0)
            {
                scaleInTradesExecuted = restoredScaleInCount;
                scaleInActive = true;
                scaleInSide = side;

                TradeRuntimeState referenceState = tradeStates.Values.FirstOrDefault(s => s != null && !s.IsScaleInEntry && s.EntryPrice > 0);
                if (referenceState == null)
                    referenceState = tradeStates.Values.FirstOrDefault(s => s != null && s.IsScaleInEntry && s.EntryPrice > 0);
                if (referenceState != null && referenceState.EntryPrice > 0)
                    scaleInInitialEntryPrice = referenceState.EntryPrice;
            }

            if (Debug)
                StrategyLogDebug($"[MANUAL][SYNC] Rehydrated {tradeStates.Count} trade state(s) from TradeSync.");

            return true;
        }

        private void SubmitManualOrder(MarketPosition direction)
        {
            int entriesToSubmit = GetEffectiveTradesPerEntry();
            int quantityPerEntry = Math.Max(1, DefaultQuantity);
            int totalQuantity = Math.Max(1, entriesToSubmit) * quantityPerEntry;
            int remainingToOpen = totalQuantity;

            StrategyLogInfo(string.Format("[MANUAL][ORDER] dir={0} entries={1} qtyPerEntry={2} totalQty={3} pos={4} posQty={5}",
                direction, entriesToSubmit, quantityPerEntry, totalQuantity, Position.MarketPosition, Position.Quantity));

            if (direction == MarketPosition.Long)
            {
                if (Position.MarketPosition != MarketPosition.Short && HasOpposingExternalExposure(MarketPosition.Long, out double exposure))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Long ignored; other strategies net short ({0}).", exposure));
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

                if (remainingToOpen > 0)
                {
                    if (HasOpposingExternalExposure(MarketPosition.Long, out double exposureAfter))
                    {
                        StrategyLogInfo(string.Format("[MANUAL][GUARD] Long ignored; other strategies remain net short ({0}).", exposureAfter));
                        return;
                    }
                    OpenManualEntry(MarketPosition.Long, remainingToOpen, quantityPerEntry);
                }
            }
            else if (direction == MarketPosition.Short)
            {
                if (Position.MarketPosition != MarketPosition.Long && HasOpposingExternalExposure(MarketPosition.Short, out double exposure))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Short ignored; other strategies net long ({0}).", exposure));
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

                if (remainingToOpen > 0)
                {
                    if (HasOpposingExternalExposure(MarketPosition.Short, out double exposureAfter))
                    {
                        StrategyLogInfo(string.Format("[MANUAL][GUARD] Short ignored; other strategies remain net long ({0}).", exposureAfter));
                        return;
                    }
                    OpenManualEntry(MarketPosition.Short, remainingToOpen, quantityPerEntry);
                }
            }
        }

        private void SubmitManualPriceOrder(MarketPosition direction, OrderType orderType, double price)
        {
            int entriesToSubmit = GetEffectiveTradesPerEntry();
            int quantityPerEntry = Math.Max(1, DefaultQuantity);
            int totalQuantity = Math.Max(1, entriesToSubmit) * quantityPerEntry;
            int remainingToOpen = totalQuantity;

            string orderLabel = orderType == OrderType.Limit ? "LIMIT" : "STOP";
            StrategyLogInfo(string.Format("[MANUAL][ORDER] type={0} dir={1} price={2:F2} entries={3} qtyPerEntry={4} totalQty={5} pos={6} posQty={7}",
                orderLabel, direction, price, entriesToSubmit, quantityPerEntry, totalQuantity, Position.MarketPosition, Position.Quantity));

            if (direction == MarketPosition.Long)
            {
                if (Position.MarketPosition != MarketPosition.Short && HasOpposingExternalExposure(MarketPosition.Long, out double exposure))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Long ignored; other strategies net short ({0}).", exposure));
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

                if (remainingToOpen > 0)
                {
                    if (HasOpposingExternalExposure(MarketPosition.Long, out double exposureAfter))
                    {
                        StrategyLogInfo(string.Format("[MANUAL][GUARD] Long ignored; other strategies remain net short ({0}).", exposureAfter));
                        return;
                    }
                    OpenManualEntryAtPrice(MarketPosition.Long, remainingToOpen, quantityPerEntry, orderType, price);
                }
            }
            else if (direction == MarketPosition.Short)
            {
                if (Position.MarketPosition != MarketPosition.Long && HasOpposingExternalExposure(MarketPosition.Short, out double exposure))
                {
                    StrategyLogInfo(string.Format("[MANUAL][GUARD] Short ignored; other strategies net long ({0}).", exposure));
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

                if (remainingToOpen > 0)
                {
                    if (HasOpposingExternalExposure(MarketPosition.Short, out double exposureAfter))
                    {
                        StrategyLogInfo(string.Format("[MANUAL][GUARD] Short ignored; other strategies remain net long ({0}).", exposureAfter));
                        return;
                    }
                    OpenManualEntryAtPrice(MarketPosition.Short, remainingToOpen, quantityPerEntry, orderType, price);
                }
            }
        }

        private void SubmitManualExit(MarketPosition side, int quantity)
        {
            if (quantity <= 0)
                return;

            int remaining = quantity;
            int exitSeriesIndex = BarsArray.Length > ManualOrderSeriesIndex ? ManualOrderSeriesIndex : 0;

            foreach (var state in EnumerateOpenTrades(side))
            {
                int available = Math.Max(0, state.RemainingQuantity);
                if (available <= 0)
                    continue;

                int qtyToExit = Math.Min(available, remaining);
                if (qtyToExit <= 0)
                    continue;

                RegisterManualCloseOverride(state.TradeId);
                string exitSignal = BuildExitSignalName(state.TradeId, side == MarketPosition.Long ? "MANSELL" : "MANBUY");

                if (side == MarketPosition.Long)
                {
                    if (exitSeriesIndex > 0)
                        ExitLong(exitSeriesIndex, qtyToExit, exitSignal, state.Bootstrapped ? null : state.TradeId);
                    else
                        ExitLong(qtyToExit, exitSignal, state.Bootstrapped ? null : state.TradeId);
                }
                else
                {
                    if (exitSeriesIndex > 0)
                        ExitShort(exitSeriesIndex, qtyToExit, exitSignal, state.Bootstrapped ? null : state.TradeId);
                    else
                        ExitShort(qtyToExit, exitSignal, state.Bootstrapped ? null : state.TradeId);
                }

                remaining -= qtyToExit;
                if (remaining <= 0)
                    break;
            }

            if (remaining > 0)
            {
                StrategyLogInfo(string.Format("[MANUAL][WARN] Manual exit requested {0} but only {1} available.", quantity, quantity - remaining));
                string exitSignal = BuildExitSignalName("FALLBACK", side == MarketPosition.Long ? "MANSELL" : "MANBUY");
                if (!string.IsNullOrEmpty(activeTradeId))
                    RegisterManualCloseOverride(activeTradeId);
                if (side == MarketPosition.Long)
                    ExitLong(exitSignal);
                else
                    ExitShort(exitSignal);
            }
        }

        private IEnumerable<TradeRuntimeState> EnumerateOpenTrades(MarketPosition side)
        {
            if (tradeStates == null || tradeStates.Count == 0)
                yield break;

            for (int i = openTradeOrder.Count - 1; i >= 0; i--)
            {
                string tradeId = openTradeOrder[i];
                TradeRuntimeState state;
                if (tradeStates.TryGetValue(tradeId, out state))
                {
                    if (state != null && state.EntrySide == side && state.RemainingQuantity > 0)
                        yield return state;
                }
            }
        }

        private void OpenManualEntry(MarketPosition direction, int totalQuantity, int quantityPerEntry)
        {
            if (totalQuantity <= 0)
                return;

            int entriesToSubmit = Math.Max(1, (int)Math.Ceiling((double)totalQuantity / Math.Max(1, quantityPerEntry)));
            MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(direction, entriesToSubmit, quantityPerEntry);
            int remaining = totalQuantity;
            int actualTotal = 0;

            for (int i = 0; i < entriesToSubmit; i++)
            {
                int entryQty = Math.Min(quantityPerEntry, remaining);
                if (entryQty <= 0)
                    break;

                string tradeId = CreateTradeId(direction);
                var state = PrepareTradeState(tradeId, direction, entryQty);
                state.IsManualEntry = true;
                state.EntryContext = "MANUAL";
                state.EntryHtfEnabled = false;
                state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                state.EntryOrderPending = true;
                AttachTradeStateToSyncGroup(state, syncGroup);

                int entrySeriesIndex = BarsArray.Length > ManualOrderSeriesIndex ? ManualOrderSeriesIndex : 0;
                if (direction == MarketPosition.Long)
                {
                    if (entrySeriesIndex > 0)
                        EnterLong(entrySeriesIndex, entryQty, tradeId);
                    else
                        EnterLong(entryQty, tradeId);
                }
                else
                {
                    if (entrySeriesIndex > 0)
                        EnterShort(entrySeriesIndex, entryQty, tradeId);
                    else
                        EnterShort(entryQty, tradeId);
                }

                actualTotal += entryQty;
                remaining -= entryQty;
            }

            if (syncGroup != null)
                syncGroup.TotalQuantity = Math.Max(1, actualTotal);
        }

        private void OpenManualEntryAtPrice(MarketPosition direction, int totalQuantity, int quantityPerEntry, OrderType orderType, double price)
        {
            if (totalQuantity <= 0)
                return;

            int entriesToSubmit = Math.Max(1, (int)Math.Ceiling((double)totalQuantity / Math.Max(1, quantityPerEntry)));
            MultiEntrySyncGroup syncGroup = StartMultiEntrySyncGroup(direction, entriesToSubmit, quantityPerEntry);
            int remaining = totalQuantity;
            int actualTotal = 0;

            for (int i = 0; i < entriesToSubmit; i++)
            {
                int entryQty = Math.Min(quantityPerEntry, remaining);
                if (entryQty <= 0)
                    break;

                string tradeId = CreateTradeId(direction);
                var state = PrepareTradeState(tradeId, direction, entryQty);
                state.IsManualEntry = true;
                state.EntryContext = orderType == OrderType.Limit ? "MANUAL_LMT" : "MANUAL_STP";
                state.EntryHtfEnabled = false;
                state.EntrySignalTime = Time != null && Time.Count > 0 ? Time[0] : DateTime.UtcNow;
                state.EntryOrderPending = true;
                state.EntryCancelRequested = false;
                state.EntryPrice = price;
                AttachTradeStateToSyncGroup(state, syncGroup);

                int entrySeriesIndex = BarsArray.Length > ManualOrderSeriesIndex ? ManualOrderSeriesIndex : 0;
                if (direction == MarketPosition.Long)
                {
                    if (orderType == OrderType.Limit)
                    {
                        if (entrySeriesIndex > 0)
                            EnterLongLimit(entrySeriesIndex, true, entryQty, price, tradeId);
                        else
                            EnterLongLimit(entryQty, price, tradeId);
                    }
                    else if (orderType == OrderType.StopMarket)
                    {
                        if (entrySeriesIndex > 0)
                            EnterLongStopMarket(entrySeriesIndex, true, entryQty, price, tradeId);
                        else
                            EnterLongStopMarket(entryQty, price, tradeId);
                    }
                    else
                    {
                        if (entrySeriesIndex > 0)
                            EnterLong(entrySeriesIndex, entryQty, tradeId);
                        else
                            EnterLong(entryQty, tradeId);
                    }
                }
                else
                {
                    if (orderType == OrderType.Limit)
                    {
                        if (entrySeriesIndex > 0)
                            EnterShortLimit(entrySeriesIndex, true, entryQty, price, tradeId);
                        else
                            EnterShortLimit(entryQty, price, tradeId);
                    }
                    else if (orderType == OrderType.StopMarket)
                    {
                        if (entrySeriesIndex > 0)
                            EnterShortStopMarket(entrySeriesIndex, true, entryQty, price, tradeId);
                        else
                            EnterShortStopMarket(entryQty, price, tradeId);
                    }
                    else
                    {
                        if (entrySeriesIndex > 0)
                            EnterShort(entrySeriesIndex, entryQty, tradeId);
                        else
                            EnterShort(entryQty, tradeId);
                    }
                }

                actualTotal += entryQty;
                remaining -= entryQty;
            }

            if (syncGroup != null)
                syncGroup.TotalQuantity = Math.Max(1, actualTotal);
        }

        private void RegisterManualCloseOverride(string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;
            TradeRuntimeState state;
            if (tradeStates != null &&
                tradeStates.TryGetValue(tradeId, out state) &&
                state != null &&
                !ShouldPublishTradeLifecycle(state))
            {
                return;
            }

            string syncTradeId = ResolveSyncTradeId(tradeId);
            MultiStratManager.Instance?.RegisterManualCloseOverride(syncTradeId, ManualCloseReason);
        }

        private void HandleBiasToggleRequest(TradeBias desired)
        {
            if (Bias == desired)
            {
                UpdateBiasToggleButtons(true);
                return;
            }

            Bias = desired;
            UpdateBiasToggleButtons(true);
            StrategyLogInfo($"[UI] Bias set to {Bias}.");
        }

        private void HandleVwapGateToggleRequest()
        {
            UseVwapDirectionGate = !UseVwapDirectionGate;
            UpdateVwapGateToggleButton(true);
            UpdateIndicatorVisualButtons(true);
            UpdateIndicatorVisuals(true);
            StrategyLogInfo($"[UI] VWAP Gate toggled {(UseVwapDirectionGate ? "ON" : "OFF")}");
        }

        private void HandlePnlTagsToggleRequest()
        {
            ShowTradePnlTags = !ShowTradePnlTags;
            try
            {
                UpdateTradePnlLabelVisibility(true);
            }
            catch (Exception ex)
            {
                StrategyLogError($"[UI] PnL tag toggle failed: {ex.Message}");
            }
            UpdatePnlTagToggleButton(true);
            StrategyLogInfo($"[UI] Trade PnL tags toggled {(ShowTradePnlTags ? "ON" : "OFF")}");
        }

        private void HandleReverseSignalToggleRequest()
        {
            ReverseSignalTrading = !ReverseSignalTrading;
            UpdateReverseSignalToggleButton(true);
            StrategyLogInfo($"[UI] Reverse-signal trading toggled {(ReverseSignalTrading ? "ON" : "OFF")}");
        }

        private void HandleIndicatorVisualToggleRequest(IndicatorVisualType target)
        {
            TriggerCustomEvent(state =>
            {
                if (state == null)
                    return;
                HandleIndicatorVisualToggleRequestCore((IndicatorVisualType)state);
            }, target);
        }

        private void HandleIndicatorVisualToggleRequestCore(IndicatorVisualType target)
        {
            string label = target.ToString().ToUpperInvariant();
            bool newState = false;

            switch (target)
            {
                case IndicatorVisualType.Sma:
                    ShowSmaVisuals = !ShowSmaVisuals;
                    newState = ShowSmaVisuals;
                    label = "SMA";
                    break;
                case IndicatorVisualType.Ema:
                    ShowEmaVisuals = !ShowEmaVisuals;
                    newState = ShowEmaVisuals;
                    label = "EMA";
                    break;
                case IndicatorVisualType.Rsi:
                    ShowRsiVisuals = !ShowRsiVisuals;
                    newState = ShowRsiVisuals;
                    label = "RSI";
                    break;
                case IndicatorVisualType.Macd:
                    ShowMacdVisuals = !ShowMacdVisuals;
                    newState = ShowMacdVisuals;
                    label = "MACD";
                    break;
                case IndicatorVisualType.Atr:
                    ShowAtrVisuals = !ShowAtrVisuals;
                    newState = ShowAtrVisuals;
                    label = "ATR";
                    break;
                case IndicatorVisualType.Bollinger:
                    ShowChopBbVisuals = !ShowChopBbVisuals;
                    newState = ShowChopBbVisuals;
                    label = "Chop BB";
                    break;
                case IndicatorVisualType.Vwap:
                    ShowVwapMrVisuals = !ShowVwapMrVisuals;
                    newState = ShowVwapMrVisuals || UseVwapDirectionGate;
                    label = "VWAP";
                    break;
            }

            UpdateIndicatorVisualButtons(true);
            UpdateIndicatorVisuals(true);
            StrategyLogInfo($"[UI] {label} visuals toggled {(newState ? "ON" : "OFF")}");
        }

        private void HandleTradesPerEntryOverrideRequest(string text)
        {
            string trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                tradesPerEntryOverride = 0;
                StrategyLogInfo("[UI] TradesPerEntry override cleared; using strategy setting.");
                UpdateTradesPerEntryInput(true);
                return;
            }

            int parsed;
            if (!int.TryParse(trimmed, out parsed))
            {
                StrategyLogInfo($"[UI] TradesPerEntry override invalid ('{trimmed}'); keeping {GetEffectiveTradesPerEntry()}.");
                UpdateTradesPerEntryInput(true);
                return;
            }

            if (parsed <= 0)
            {
                tradesPerEntryOverride = 0;
                StrategyLogInfo("[UI] TradesPerEntry override cleared; using strategy setting.");
                UpdateTradesPerEntryInput(true);
                return;
            }

            int clamped = Math.Max(1, Math.Min(MaxTradesPerEntry, parsed));
            tradesPerEntryOverride = clamped;
            StrategyLogInfo($"[UI] TradesPerEntry override set to {clamped}.");
            UpdateTradesPerEntryInput(true);
        }

        private void HandleChopTradesPerEntryOverrideRequest(string text)
        {
            string trimmed = string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                chopTradesPerEntryOverride = 0;
                StrategyLogInfo("[UI] ChopTradesPerEntry override cleared; using strategy setting.");
                UpdateChopTradesPerEntryInput(true);
                return;
            }

            int parsed;
            if (!int.TryParse(trimmed, out parsed))
            {
                StrategyLogInfo($"[UI] ChopTradesPerEntry override invalid ('{trimmed}'); keeping {GetEffectiveChopTradesPerEntry()}.");
                UpdateChopTradesPerEntryInput(true);
                return;
            }

            if (parsed <= 0)
            {
                chopTradesPerEntryOverride = 0;
                StrategyLogInfo("[UI] ChopTradesPerEntry override cleared; using strategy setting.");
                UpdateChopTradesPerEntryInput(true);
                return;
            }

            int clamped = Math.Max(1, Math.Min(MaxTradesPerEntry, parsed));
            chopTradesPerEntryOverride = clamped;
            StrategyLogInfo($"[UI] ChopTradesPerEntry override set to {clamped}.");
            UpdateChopTradesPerEntryInput(true);
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
                if (state.Bootstrapped && Position != null && Position.MarketPosition != MarketPosition.Flat)
                    qty = Math.Min(qty, Math.Abs(Position.Quantity));
                if (qty <= 0)
                    continue;

                string exitSignal = BuildExitSignalName(state.TradeId, reasonSuffix);
                string fromEntry = state.Bootstrapped ? null : state.TradeId;
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, fromEntry);
                else
                    ExitShort(qty, exitSignal, fromEntry);
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

        private bool IsDailyLimitBreached(double totalPnL, out string limitType)
        {
            limitType = string.Empty;

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
                limitType = "DLL";
                return true;
            }

            if (hasProfit && totalPnL >= profitLimit - 1e-9)
            {
                limitType = "DPL";
                return true;
            }

            return false;
        }

        private void RefreshDailyPnLLimitOnEnable()
        {
            if (!EnableDailyPnLLimits || Account == null)
                return;

            double totalPnL;
            if (!TryGetAccountTotalPnL(out totalPnL))
                return;

            string limitType;
            if (!IsDailyLimitBreached(totalPnL, out limitType))
            {
                try
                {
                    MultiStratManager.Instance?.ClearDailyLimitOverrideForAccount(Account.Name, "limits_refresh");
                }
                catch { }

                if (dailyPnLLimitHalted)
                    ResetDailyPnLLimitState("limits_refresh");
                return;
            }

            dailyPnLLimitHalted = true;
            dailyPnLLimitType = limitType ?? string.Empty;
            dailyPnLLimitTriggeredPnL = totalPnL;
            dailyPnLLimitTriggeredAt = DateTime.UtcNow;
            dailyPnLLimitStatusText = BuildDailyPnLLimitStatusText(totalPnL);
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

                DateTime resetAtUtc;
                if (!manager.TryGetManualDailyLimitReset(Account.Name, out resetAtUtc))
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

                double currentPnL;
                if (!TryGetAccountTotalPnL(out currentPnL))
                    currentPnL = overridePnL;

                string limitType;
                if (!IsDailyLimitBreached(currentPnL, out limitType))
                {
                    manager.ClearDailyLimitOverrideForAccount(Account.Name, "limits_refresh");
                    if (dailyPnLLimitHalted)
                        ResetDailyPnLLimitState("limits_refresh");
                    return;
                }

                dailyPnLLimitHalted = true;
                dailyPnLLimitType = string.IsNullOrWhiteSpace(overrideType) ? limitType : overrideType;
                dailyPnLLimitTriggeredPnL = overridePnL;
                dailyPnLLimitTriggeredAt = overrideActivatedAt != DateTime.MinValue ? overrideActivatedAt : DateTime.UtcNow;

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

            double currentPnL;
            if (TryGetAccountTotalPnL(out currentPnL))
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

        private bool HasManualTradesOpen()
        {
            if (tradeStates == null || tradeStates.Count == 0)
                return false;

            foreach (var state in tradeStates.Values)
            {
                if (state == null)
                    continue;

                bool isManual = state.IsManualEntry ||
                                (!string.IsNullOrEmpty(state.EntryContext) &&
                                 state.EntryContext.StartsWith("MANUAL", StringComparison.OrdinalIgnoreCase));
                if (!isManual)
                    continue;

                if (state.RemainingQuantity > 0)
                    return true;
                if (state.EntryOrderPending)
                    return true;
                if (state.EntryOrder != null && !IsTerminalState(state.EntryOrder.OrderState))
                    return true;
            }

            return false;
        }

        private void TryEnforceManualHaltFlat()
        {
            if (!manualHaltActive || Account == null)
                return;

            if (HasManualTradesOpen())
                return;

            var now = DateTime.UtcNow;
            if (manualHaltLastEnforceAttemptAt != DateTime.MinValue &&
                (now - manualHaltLastEnforceAttemptAt) < TimeSpan.FromSeconds(2))
            {
                return;
            }
            manualHaltLastEnforceAttemptAt = now;

            int openPositions;
            int workingOrders;
            if (!TryGetAccountRiskCounts(out openPositions, out workingOrders))
                return;

            if (openPositions == 0 && workingOrders == 0)
                return;

            if (manualHaltLastEnforceLogAt == DateTime.MinValue || (now - manualHaltLastEnforceLogAt) > TimeSpan.FromSeconds(20))
            {
                manualHaltLastEnforceLogAt = now;
                StrategyLogInfo($"[MANUAL_HALT] Enforcing flat (positions={openPositions}, workingOrders={workingOrders})");
            }

            CancelWorkingEntryOrders("manual_halt_enforce");
            int resubmitted = SubmitManualHaltExits("MHLT");

            bool forceFlatten = openPositions > 0 && resubmitted == 0;
            if (!forceFlatten && openPositions > 0 && manualHaltActivatedAt != DateTime.MinValue)
            {
                if ((now - manualHaltActivatedAt) >= TimeSpan.FromSeconds(3))
                    forceFlatten = true;
            }

            if (forceFlatten)
                TryFlattenAccountEverything("manual_halt_enforce", activeTradeId ?? string.Empty, "MANUAL_HALT");
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

        private void TryFlattenAccountEverything(string reason, string tradeRef = "", string logContext = "DAILY_LIMIT")
        {
            if (Account == null)
                return;

            try
            {
                string context = string.IsNullOrWhiteSpace(logContext) ? "FLATTEN" : logContext.Trim();
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

                StrategyLogInfo($"[{context}] Flatten requested due to {reason} (cancelledOrders={cancelled}, submittedFlattens={submitted})");
            }
            catch (Exception ex)
            {
                string context = string.IsNullOrWhiteSpace(logContext) ? "FLATTEN" : logContext.Trim();
                StrategyLogError($"[{context}] Failed to flatten account due to {reason}: {ex.Message}");
                var manager = MultiStratManager.Instance;
                if (manager != null)
                    manager.LogError(context, $"Flatten failed: {ex.Message}", 0, tradeRef, tradeRef);
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
                string fromEntry = state.Bootstrapped ? null : state.TradeId;
                if (state.EntrySide == MarketPosition.Long)
                    ExitLong(qty, exitSignal, fromEntry);
                else
                    ExitShort(qty, exitSignal, fromEntry);

                remaining -= qty;
                if (remaining <= 0)
                    break;
            }
        }

        void ITradeSyncParticipant.HandleTradeSyncPartial(string tradeId, int quantityToExit)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || quantityToExit <= 0)
                return;

            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
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
                ExitLong(qty, exitSignal, state.Bootstrapped ? null : tradeId);
            else
                ExitShort(qty, exitSignal, state.Bootstrapped ? null : tradeId);
        }

        void ITradeSyncParticipant.HandleTradeSyncClose(string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
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
                ExitLong(qty, exitSignal, state.Bootstrapped ? null : tradeId);
            else
                ExitShort(qty, exitSignal, state.Bootstrapped ? null : tradeId);
        }

        void IRunUpParticipant.HandleRunUpStart(string tradeId, double anchorPrice, RunUpConfig config)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || config == null || !config.Enabled)
                return;

            List<TradeRuntimeState> targetStates = null;
            MultiEntrySyncGroup group;
            if (TryGetMultiEntrySyncGroupByTradeId(tradeId, out group))
            {
                targetStates = GetMultiEntrySyncStates(group.TradeId);
            }
            else
            {
                TradeRuntimeState singleState;
                if (TryGetTradeState(tradeId, out singleState))
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
                    state.LastStopPrice = clamped.Value;
                    stopSet = true;
                    StrategyLogInfo(string.Format("[RUN_UP] Activated for {0}: anchor={1:F2}, stop={2:F2}, dist={3:F4}, inc={4:F4}", state.TradeId, anchorPrice, clamped.Value, distance, increment));
                }
            }
        }

        #region Params

        public enum TradeBias { Both, LongOnly, ShortOnly }
        public enum StopKind { Ticks, ATR }
        public enum TargetKind { Ticks, ATR }
        public enum HtfSwingModeOption { Pivot, Range, Both }
        public enum HtfSwingActionOption { AddVote, Block, Both }
        public enum BreakEvenTriggerModeOption { Ticks, Dollars }
        public enum ChopRangeModeOption { Bollinger, HighLow }
        public enum ChopAddOnProfitModeOption { Ticks, Dollars }
        public enum VwapExitModeOption { TargetVwap, TrailOnVwapTouch }
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

        [NinjaScriptProperty, Range(0, 1000), Display(Name = "Entry Cooldown (bars)", GroupName = "01 - Bias & Voting", Order = 5)]
        public int EntryCooldownBars { get; set; }

        [NinjaScriptProperty, Display(Name = "Reverse-Signal Trading", GroupName = "01 - Bias & Voting", Order = 6)]
        public bool ReverseSignalTrading { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Vote Entry Signals", GroupName = "01 - Bias & Voting", Order = 7)]
        public bool EnableVoteEntrySignals { get; set; }

        [NinjaScriptProperty, Display(Name = "Use VWAP Direction Gate", GroupName = "01 - Bias & Voting", Order = 8)]
        public bool UseVwapDirectionGate { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Regime Switching", GroupName = "01 - Bias & Voting", Order = 9)]
        public bool EnableRegimeSwitching { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Candle Conviction", GroupName = "01 - Bias & Voting", Order = 10)]
        public bool EnableCandleConviction { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "RSI Chop Long Threshold", GroupName = "01 - Bias & Voting", Order = 11)]
        public int RsiChopLongThreshold { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "RSI Chop Short Threshold", GroupName = "01 - Bias & Voting", Order = 12)]
        public int RsiChopShortThreshold { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable ORB Filter", GroupName = "02 - Filters", Order = 0)]
        public bool EnableOrbFilter { get; set; }

        [NinjaScriptProperty, Range(1, 120), Display(Name = "ORB Minutes", GroupName = "02 - Filters", Order = 1)]
        public int OrbMinutes { get; set; }

        [NinjaScriptProperty, Display(Name = "Use Fixed ORB Start Time", GroupName = "02 - Filters", Order = 2)]
        public bool OrbUseFixedStartTime { get; set; }

        [NinjaScriptProperty, Range(0, 23), Display(Name = "ORB Start Hour (chart time)", GroupName = "02 - Filters", Order = 3)]
        public int OrbStartHour { get; set; }

        [NinjaScriptProperty, Range(0, 59), Display(Name = "ORB Start Minute (chart time)", GroupName = "02 - Filters", Order = 4)]
        public int OrbStartMinute { get; set; }

        [NinjaScriptProperty, Range(0, 240), Display(Name = "ORB Pre-Start Block (min)", GroupName = "02 - Filters", Order = 5)]
        public int OrbPreStartBlockMinutes { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Chop Filter", GroupName = "02 - Filters", Order = 6)]
        public bool EnableChopFilter { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "Chop Lookback Bars", GroupName = "02 - Filters", Order = 7)]
        public int ChopLookbackBars { get; set; }

        [NinjaScriptProperty, Range(2, 100), Display(Name = "Chop ADX Period", GroupName = "02 - Filters", Order = 8)]
        public int ChopAdxPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "Chop ADX Threshold", GroupName = "02 - Filters", Order = 9)]
        public int ChopAdxThreshold { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "Chop Bollinger Period", GroupName = "02 - Filters", Order = 10)]
        public int ChopBollingerPeriod { get; set; }

        [NinjaScriptProperty, Range(0.5, 5.0), Display(Name = "Chop Bollinger StdDev", GroupName = "02 - Filters", Order = 11)]
        public double ChopBollingerStdDev { get; set; }

        [NinjaScriptProperty, Range(0.1, 5.0), Display(Name = "Chop BB Width %", GroupName = "02 - Filters", Order = 12)]
        public double ChopBBWidthPct { get; set; }

        [NinjaScriptProperty, Range(0, 20), Display(Name = "Chop Breakout Buffer (ticks)", GroupName = "02 - Filters", Order = 13)]
        public int ChopBreakoutBufferTicks { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable HTF Swing Gate", GroupName = "02 - Filters", Order = 14)]
        public bool EnableHtfSwingGate { get; set; }

        [NinjaScriptProperty, Display(Name = "HTF Swing Mode", GroupName = "02 - Filters", Order = 15)]
        public HtfSwingModeOption HtfSwingMode { get; set; }

        [NinjaScriptProperty, Display(Name = "HTF Swing Action", GroupName = "02 - Filters", Order = 16)]
        public HtfSwingActionOption HtfSwingAction { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "HTF Swing Lookback Bars", GroupName = "02 - Filters", Order = 17)]
        public int HtfSwingLookbackBars { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "HTF Swing Pivot Strength", GroupName = "02 - Filters", Order = 18)]
        public int HtfSwingPivotStrength { get; set; }

        [NinjaScriptProperty, Range(0.1, 5.0), Display(Name = "HTF Swing Distance (ATR)", GroupName = "02 - Filters", Order = 19)]
        public double HtfSwingDistanceAtr { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "HTF Swing ATR Period", GroupName = "02 - Filters", Order = 20)]
        public int HtfSwingAtrPeriod { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "HTF Swing Hold Bars", GroupName = "02 - Filters", Order = 21)]
        public int HtfSwingHoldBars { get; set; }

        [NinjaScriptProperty, Range(1, 240), Display(Name = "HTF Swing Primary Minutes", GroupName = "02 - Filters", Order = 22)]
        public int HtfSwingPrimaryMinutes { get; set; }

        [NinjaScriptProperty, Range(1, 240), Display(Name = "HTF Swing Secondary Minutes", GroupName = "02 - Filters", Order = 23)]
        public int HtfSwingSecondaryMinutes { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Filter Visuals", GroupName = "02 - Filters", Order = 24)]
        public bool ShowFilterVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show SMA Visuals", GroupName = "02 - Indicator Visuals", Order = 0)]
        public bool ShowSmaVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show EMA Visuals", GroupName = "02 - Indicator Visuals", Order = 1)]
        public bool ShowEmaVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show RSI Visuals", GroupName = "02 - Indicator Visuals", Order = 2)]
        public bool ShowRsiVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show MACD Visuals", GroupName = "02 - Indicator Visuals", Order = 3)]
        public bool ShowMacdVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show ATR Visuals", GroupName = "02 - Indicator Visuals", Order = 4)]
        public bool ShowAtrVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Chop BB Visuals", GroupName = "02 - Indicator Visuals", Order = 5)]
        public bool ShowChopBbVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show VWAP MR Visuals", GroupName = "02 - Indicator Visuals", Order = 6)]
        public bool ShowVwapMrVisuals { get; set; }

        [NinjaScriptProperty, Display(Name = "Show Trade PnL Tags", GroupName = "02 - Filters", Order = 26)]
        public bool ShowTradePnlTags { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Volatility Expansion Vote", GroupName = "02 - Filters", Order = 28)]
        public bool EnableVolatilityExpansionVote { get; set; }

        [NinjaScriptProperty, Range(0.0, 5.0), Display(Name = "VolExp BB Width Delta %", GroupName = "02 - Filters", Order = 29)]
        public double VolExpBbWidthDeltaPct { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "VolExp ATR Baseline Period", GroupName = "02 - Filters", Order = 30)]
        public int VolExpAtrBaselinePeriod { get; set; }

        [NinjaScriptProperty, Range(0.5, 5.0), Display(Name = "VolExp ATR Multiplier", GroupName = "02 - Filters", Order = 31)]
        public double VolExpAtrMultiplier { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable RVOL/VROC Gate", GroupName = "02 - Filters", Order = 32)]
        public bool EnableRvolGate { get; set; }

        [NinjaScriptProperty, Range(5, 200), Display(Name = "RVOL Lookback Bars", GroupName = "02 - Filters", Order = 33)]
        public int RvolLookbackBars { get; set; }

        [NinjaScriptProperty, Range(0.1, 5.0), Display(Name = "RVOL Min", GroupName = "02 - Filters", Order = 34)]
        public double RvolMin { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "VROC Lookback Bars", GroupName = "02 - Filters", Order = 35)]
        public int VrocLookbackBars { get; set; }

        [NinjaScriptProperty, Range(0.0, 500.0), Display(Name = "VROC Min %", GroupName = "02 - Filters", Order = 36)]
        public double VrocMinPct { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Compression Guard", GroupName = "02 - Filters", Order = 37)]
        public bool EnableCompressionGuard { get; set; }

        [NinjaScriptProperty, Range(0.0, 5.0), Display(Name = "Compression BB Width %", GroupName = "02 - Filters", Order = 38)]
        public double CompressionGuardBbWidthPct { get; set; }

        [NinjaScriptProperty, Display(Name = "Compression Require Both Gates", GroupName = "02 - Filters", Order = 39)]
        public bool CompressionGuardRequireBoth { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "Chop Breakout Hold Bars", GroupName = "02 - Filters", Order = 40)]
        public int ChopBreakoutHoldBars { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Chop Decay Gate", GroupName = "02 - Filters", Order = 41)]
        public bool EnableChopDecayGate { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "Chop Decay Bars", GroupName = "02 - Filters", Order = 42)]
        public int ChopDecayBars { get; set; }

        [NinjaScriptProperty, Range(0.0, 50.0), Display(Name = "Chop Decay ADX Delta", GroupName = "02 - Filters", Order = 43)]
        public double ChopDecayAdxDelta { get; set; }

        [NinjaScriptProperty, Range(0.0, 5.0), Display(Name = "Chop Decay BB Width Delta %", GroupName = "02 - Filters", Order = 44)]
        public double ChopDecayBbWidthDeltaPct { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Chop Range Trades", GroupName = "02 - Chop Trading", Order = 0)]
        public bool EnableChopRangeTrades { get; set; }

        [NinjaScriptProperty, Display(Name = "Chop Range Mode", GroupName = "02 - Chop Trading", Order = 1)]
        public ChopRangeModeOption ChopRangeMode { get; set; }

        [NinjaScriptProperty, Range(2, 200), Display(Name = "Chop Range Lookback Bars", GroupName = "02 - Chop Trading", Order = 2)]
        public int ChopRangeLookbackBars { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "Chop Trades Per Entry", GroupName = "02 - Chop Trading", Order = 3)]
        public int ChopTradesPerEntry { get; set; }

        [NinjaScriptProperty, Display(Name = "Chop Stop Type", GroupName = "02 - Chop Trading", Order = 4)]
        public StopKind ChopStopType { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "Chop Stop Ticks", GroupName = "02 - Chop Trading", Order = 5)]
        public int ChopStopTicks { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name = "Chop Stop ATR Mult", GroupName = "02 - Chop Trading", Order = 6)]
        public double ChopStopAtrMult { get; set; }

        [NinjaScriptProperty, Range(1, 100), Display(Name = "Chop Trail Ticks", GroupName = "02 - Chop Trading", Order = 7)]
        public int ChopTrailTicks { get; set; }

        [NinjaScriptProperty, Range(0, 50), Display(Name = "Chop Trail Plus Ticks", GroupName = "02 - Chop Trading", Order = 8)]
        public int ChopTrailPlusTicks { get; set; }

        [NinjaScriptProperty, Display(Name = "Chop Add-on Profit Mode", GroupName = "02 - Chop Trading", Order = 9)]
        public ChopAddOnProfitModeOption ChopAddOnProfitMode { get; set; }

        [NinjaScriptProperty, Range(0, 1000), Display(Name = "Chop Add-on Profit Ticks", GroupName = "02 - Chop Trading", Order = 10)]
        public int ChopAddOnProfitTicks { get; set; }

        [NinjaScriptProperty, Range(0, 10000), Display(Name = "Chop Add-on Profit Dollars", GroupName = "02 - Chop Trading", Order = 11)]
        public double ChopAddOnProfitDollars { get; set; }

        [NinjaScriptProperty, Display(Name = "UseSMA", GroupName = "02 - Indicator Toggles", Order = 0)]
        public bool UseSMA { get; set; }

        [NinjaScriptProperty, Display(Name = "UseEMA", GroupName = "02 - Indicator Toggles", Order = 1)]
        public bool UseEMA { get; set; }

        [NinjaScriptProperty, Display(Name = "UseRSI", GroupName = "02 - Indicator Toggles", Order = 2)]
        public bool UseRSI { get; set; }

        [NinjaScriptProperty, Display(Name = "UseMACD", GroupName = "02 - Indicator Toggles", Order = 3)]
        public bool UseMACD { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapMrStrategy { get; set; }

        [NinjaScriptProperty, Display(Name = "VWAP Timeframe", GroupName = "03 - Indicator Periods", Order = 10)]
        public VwapMrTimeframeOption VwapMrTimeframe { get; set; }

        [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "VWAP Band 1 Mult", GroupName = "03 - Indicator Periods", Order = 11)]
        public double VwapBand1Multiplier { get; set; }

        [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "VWAP Band 2 Mult", GroupName = "03 - Indicator Periods", Order = 12)]
        public double VwapBand2Multiplier { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public double MinDistFromVWAP_Percent { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public VwapExitModeOption VwapExitMode { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapFailureFlip { get; set; }

        [NinjaScriptProperty, Display(Name = "VWAP Filter Spikes", GroupName = "03 - Indicator Periods", Order = 13)]
        public bool VwapFilterSpikes { get; set; }

        [NinjaScriptProperty, Range(1.1, 50.0), Display(Name = "VWAP Spike Threshold (x median)", GroupName = "03 - Indicator Periods", Order = 14)]
        public double VwapSpikeThreshold { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapPinBar { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapDoji { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapEngulfing { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapTweezer { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapRailroad { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapDojiStar { get; set; }

        [NinjaScriptProperty, Browsable(false)]
        public bool EnableVwapThreeInside { get; set; }

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

        [NinjaScriptProperty, Range(2, 100), Display(Name = "Base Atr Period", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 0)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty, Display(Name = "StopType", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 1)]
        public StopKind StopType { get; set; }

        [NinjaScriptProperty, Range(1, 200), Display(Name = "StopTicks", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 2)]
        public int StopTicks { get; set; }

        [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "AtrStopMult", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 3)]
        public double AtrStopMult { get; set; }

        [NinjaScriptProperty, Display(Name = "TargetType", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 4)]
        public TargetKind TargetType { get; set; }

        [NinjaScriptProperty, Range(1, 400), Display(Name = "TargetTicks", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 5)]
        public int TargetTicks { get; set; }

        [NinjaScriptProperty, Range(0.5, 20.0), Display(Name = "AtrTargetMult", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 6)]
        public double AtrTargetMult { get; set; }

        [NinjaScriptProperty, Range(1, 10000), Display(Name = "Manual Entry Offset (ticks)", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 7)]
        public int ManualEntryOffsetTicks { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Global Trailing", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 8)]
        public bool EnableGlobalTrailing { get; set; }

        [NinjaScriptProperty, Display(Name = "Global Trail Activation Mode", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 9)]
        public BreakEvenTriggerModeOption GlobalTrailActivationMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Global Trail Activation Value", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 10)]
        public double GlobalTrailActivationValue { get; set; }

        [NinjaScriptProperty, Display(Name = "Global Profit Lock Mode", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 11)]
        public BreakEvenTriggerModeOption GlobalProfitLockMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Global Profit Lock Value", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 12)]
        public double GlobalProfitLockValue { get; set; }

        [NinjaScriptProperty, Display(Name = "Global Trail Increment Mode", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 13)]
        public BreakEvenTriggerModeOption GlobalTrailIncrementMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Global Trail Increment Value", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 14)]
        public double GlobalTrailIncrementValue { get; set; }

        // [NinjaScriptProperty, Display(Name = "TrailType", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 6)]
        // public TrailKind TrailType { get; set; }

        // [NinjaScriptProperty, Range(1, 200), Display(Name = "TrailTicks", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 7)]
        // public int TrailTicks { get; set; }

        // [NinjaScriptProperty, Range(0.5, 10.0), Display(Name = "AtrTrailMult", GroupName = "04 - Stops, Targets, & Global Trailing", Order = 8)]
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

        [NinjaScriptProperty, Display(Name = "Use Tight DEMA ATR Trailing", GroupName = "05 - DEMA ATR Trailing", Order = 5)]
        public bool UseTightDemaAtrTrailing { get; set; }

        [NinjaScriptProperty, Display(Name = "Use BreakEven Clamp", GroupName = "06 - BreakEven", Order = 0)]
        public bool UseBreakEvenClamp { get; set; }

        [NinjaScriptProperty, Display(Name = "BreakEven Trigger Mode", GroupName = "06 - BreakEven", Order = 1)]
        public BreakEvenTriggerModeOption BreakEvenTriggerMode { get; set; }

        [NinjaScriptProperty, Range(0, 400), Display(Name = "BreakEven Trigger Ticks", GroupName = "06 - BreakEven", Order = 2)]
        public int BreakEvenTriggerTicks { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "BreakEven Trigger Dollars", GroupName = "06 - BreakEven", Order = 3)]
        public double BreakEvenTriggerDollars { get; set; }

        [NinjaScriptProperty, Range(0, 100), Display(Name = "BreakEven Plus Ticks", GroupName = "06 - BreakEven", Order = 4)]
        public int BreakEvenPlusTicks { get; set; }

        [NinjaScriptProperty, Display(Name = "Force DEMA on BreakEven Clamp", GroupName = "06 - BreakEven", Order = 5)]
        public bool EnableDemaAtrOnBreakEvenClamp { get; set; }

        [NinjaScriptProperty, Display(Name = "Debug", GroupName = "07 - Misc", Order = 0)]
        public bool Debug { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Signal Diagnostics", GroupName = "07 - Misc", Order = 1)]
        public bool EnableSignalDiagnostics { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Trade Story Logging", GroupName = "07 - Misc", Order = 2)]
        public bool EnableTradeStoryLogging { get; set; }

        [NinjaScriptProperty, Display(Name = "Start Halted On Enable", GroupName = "07 - Misc", Order = 3)]
        public bool StartHaltedOnEnable { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Daily PnL Limits (DLL/DPL)", GroupName = "08 - Daily Limits", Order = 0)]
        public bool EnableDailyPnLLimits { get; set; }

        [NinjaScriptProperty, Range(-1000000.0, 0.0), Display(Name = "Daily Loss Limit (DLL)", GroupName = "08 - Daily Limits", Order = 1)]
        public double DailyLossLimit { get; set; }

        [NinjaScriptProperty, Range(0.0, 1000000.0), Display(Name = "Daily Profit Limit (DPL)", GroupName = "08 - Daily Limits", Order = 2)]
        public double DailyProfitLimit { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Straddle Trades", GroupName = "09 - Straddle", Order = 0)]
        public bool EnableStraddleTrades { get; set; }

        [NinjaScriptProperty, Range(0, 23), Display(Name = "Straddle Start Hour (chart time)", GroupName = "09 - Straddle", Order = 1)]
        public int StraddleStartHour { get; set; }

        [NinjaScriptProperty, Range(0, 59), Display(Name = "Straddle Start Minute (chart time)", GroupName = "09 - Straddle", Order = 2)]
        public int StraddleStartMinute { get; set; }

        [NinjaScriptProperty, Range(1, 120), Display(Name = "Straddle Range Minutes", GroupName = "09 - Straddle", Order = 3)]
        public int StraddleRangeMinutes { get; set; }

        [NinjaScriptProperty, Range(1, 50), Display(Name = "Straddle Zone Size (ticks)", GroupName = "09 - Straddle", Order = 4)]
        public int StraddleZoneTicks { get; set; }

        [NinjaScriptProperty, Range(-50, 50), Display(Name = "Straddle Zone Offset (ticks)", GroupName = "09 - Straddle", Order = 5)]
        public int StraddleZoneOffsetTicks { get; set; }

        [NinjaScriptProperty, Range(1, 10), Display(Name = "Trades Per Straddle Entry", GroupName = "09 - Straddle", Order = 6)]
        public int TradesPerStraddleEntry { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name = "Straddle ATR Stop Mult", GroupName = "09 - Straddle", Order = 7)]
        public double StraddleAtrStopMult { get; set; }

        [NinjaScriptProperty, Range(0.1, 10.0), Display(Name = "Straddle ATR Trail Mult", GroupName = "09 - Straddle", Order = 8)]
        public double StraddleAtrTrailMult { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Straddle Trail Activation ($)", GroupName = "09 - Straddle", Order = 9)]
        public double StraddleTrailActivationDollars { get; set; }

        [NinjaScriptProperty, Range(0, 120), Display(Name = "Straddle Min Profit Hold (sec)", GroupName = "09 - Straddle", Order = 10)]
        public int StraddleMinProfitHoldSeconds { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Scale-In Trades", GroupName = "10 - Scale-In", Order = 0)]
        public bool EnableScaleInTrades { get; set; }

        [NinjaScriptProperty, Display(Name = "Publish Scale-In Trades To Bridge", GroupName = "10 - Scale-In", Order = 1)]
        public bool PublishScaleInTradesToBridge { get; set; }

        [NinjaScriptProperty, Display(Name = "Enable Scale-In Trailing", GroupName = "10 - Scale-In", Order = 2)]
        public bool EnableScaleInTrailing { get; set; }

        [NinjaScriptProperty, Range(0, 400), Display(Name = "Scale-In Drawdown Step (ticks)", GroupName = "10 - Scale-In", Order = 3)]
        public int ScaleInDrawdownTicks { get; set; }

        [NinjaScriptProperty, Range(0, 10), Display(Name = "Scale-In Trades to Add", GroupName = "10 - Scale-In", Order = 4)]
        public int ScaleInTradesToAdd { get; set; }

        [NinjaScriptProperty, Range(0, 50), Display(Name = "Scale-In Max Trades", GroupName = "10 - Scale-In", Order = 5)]
        public int ScaleInMaxTrades { get; set; }

        [NinjaScriptProperty, Display(Name = "Scale-In Trail Activation Mode", GroupName = "10 - Scale-In", Order = 6)]
        public BreakEvenTriggerModeOption ScaleInTrailActivationMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Scale-In Trail Activation Value", GroupName = "10 - Scale-In", Order = 7)]
        public double ScaleInTrailActivationValue { get; set; }

        [NinjaScriptProperty, Display(Name = "Scale-In Profit Lock Mode", GroupName = "10 - Scale-In", Order = 8)]
        public BreakEvenTriggerModeOption ScaleInProfitLockMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Scale-In Profit Lock Value", GroupName = "10 - Scale-In", Order = 9)]
        public double ScaleInProfitLockValue { get; set; }

        [NinjaScriptProperty, Display(Name = "Scale-In Trail Increment Mode", GroupName = "10 - Scale-In", Order = 10)]
        public BreakEvenTriggerModeOption ScaleInTrailIncrementMode { get; set; }

        [NinjaScriptProperty, Range(0.0, 100000.0), Display(Name = "Scale-In Trail Increment Value", GroupName = "10 - Scale-In", Order = 11)]
        public double ScaleInTrailIncrementValue { get; set; }

        #endregion
    }
}


