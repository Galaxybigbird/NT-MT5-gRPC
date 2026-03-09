
//+------------------------------------------------------------------+
//|                                            PineScriptalgo_EA.mq5 |
//|      Strict-parity conversion of PineScriptalgo.txt core block   |
//+------------------------------------------------------------------+
#property strict
#property version   "2.00"
#property description "Strict-parity EA conversion of the first active Pine strategy block."

#include <Trade/Trade.mqh>
#include <CustomProfitBasedCriterion.mqh>

enum TPS_TYPE
{
   TPS_ATR = 0,
   TPS_TRAILING = 1,
   TPS_OPTIONS = 2
};

enum SETUP_TYPE
{
   SETUP_OPEN_CLOSE = 0,
   SETUP_RENKO = 1
};

enum SIDEWAYS_FILTER_TYPE
{
   FILTER_ATR = 0,
   FILTER_RSI = 1,
   FILTER_ATR_OR_RSI = 2,
   FILTER_ATR_AND_RSI = 3,
   FILTER_NO_FILTER = 4,
   FILTER_SIDEWAYS_ATR_OR_RSI = 5,
   FILTER_SIDEWAYS_ATR_AND_RSI = 6
};

enum TRAILING_MODE
{
   TRAIL_MODE_ATR = 0,
   TRAIL_MODE_POINTS = 1,
   TRAIL_MODE_DOLLARS = 2
};

enum ATR_TRAIL_BEHAVIOR
{
   ATR_BEHAVIOR_INTRABAR = 0,
   ATR_BEHAVIOR_BAR_CLOSE = 1
};

enum ATR_TRAIL_SOURCE
{
   ATR_SOURCE_TRADITIONAL = 0,
   ATR_SOURCE_DEMA = 1
};

enum ATR_EXTERNAL_ACTIVATION_TYPE
{
   ATR_EXTERNAL_POINTS = 0,
   ATR_EXTERNAL_DOLLARS = 1
};

enum ENTRY_STOPLOSS_TYPE
{
   ENTRY_STOPLOSS_ATR = 0,
   ENTRY_STOPLOSS_DEMA_ATR = 1,
   ENTRY_STOPLOSS_MARKET_STRUCTURE = 2
};

enum STRUCTURE_STOP_MODEL
{
   STRUCTURE_MODEL_CHART_SWING_PIVOT = 0,
   STRUCTURE_MODEL_BOS_CHOCH = 1,
   STRUCTURE_MODEL_SIGNAL_TIMEFRAME_SWING = 2
};

enum BOS_CHOCH_ENGINE
{
   BOS_CHOCH_ENGINE_SIMPLIFIED_MQL = 0,
   BOS_CHOCH_ENGINE_CLOSE_PINE_PARITY = 1
};

enum STRUCTURE_BUFFER_TYPE
{
   STRUCTURE_BUFFER_POINTS = 0,
   STRUCTURE_BUFFER_ATR = 1
};
input group "=== Strategy Core (Pine Parity) ==="
input long                 InpMagicNumber = 790179;
input TPS_TYPE             InpTPSType = TPS_TRAILING;
input SETUP_TYPE           InpSetupType = SETUP_OPEN_CLOSE;
input int                  InpTimeframeMultiplier = 18;
input bool                 InpUseLookaheadApprox = true; // Approximate Pine lookahead_on behavior.
input int                  InpSlippagePoints = 20;
input bool                 InpVerboseLogs = true;
input int                  InpBarSummaryEveryNBars = 12; // 0 disables periodic bar summary logs.

input group "=== Position Sizing (Pine default_qty_type parity) ==="
input bool                 InpUsePercentOfEquity = true;
input double               InpPercentOfEquity = 50.0; // Pine default_qty_value = 50
input double               InpFixedLots = 0.10;

input group "=== Date Filter ==="
input bool                 InpEnableDateFilter = true;
input datetime             InpFromDate = D'2023.01.01 00:00';
input datetime             InpToDate = D'2099.12.31 23:59';

input group "=== Sideways Filtering ==="
input SIDEWAYS_FILTER_TYPE InpFilterType = FILTER_NO_FILTER;
input int                  InpRSIPeriod = 7;
input int                  InpTopLimitRSI = 45;
input int                  InpBotLimitRSI = 10;
input int                  InpAtrFilterLen = 5;
input int                  InpAtrMaLen = 5;
input bool                 InpReplicateAtrMaTypo = true; // Pine compares atrMaType == 'EM' while value is 'EMA'.
input bool                 InpAtrMaUseEMAIfNoTypo = false;

input group "=== Renko Settings ==="
input bool                 InpRenkoUseATR = true;
input int                  InpRenkoAtrLen = 3;
input int                  InpRenkoTraditionalPoints = 1000;
input int                  InpRenkoFastEMA = 2;
input int                  InpRenkoSlowEMA = 10;
input int                  InpRenkoSourceBars = 500;

input group "=== ATR Risk Management Mode ==="
input int                  InpAtrLength = 20;
input double               InpProfitFactor = 2.5;
input double               InpQtyTP1 = 50.0;
input double               InpQtyTP2 = 30.0;
input double               InpQtyTP3 = 20.0;

input group "=== Trailing Engine ==="
input bool                 InpEnableTrailingEngine = true;
input TRAILING_MODE        InpTrailingMode = TRAIL_MODE_POINTS;
input bool                 InpTrailingVerboseLogs = false;

input group "=== ATR Trailing ==="
input ATR_TRAIL_BEHAVIOR   InpAtrTrailBehavior = ATR_BEHAVIOR_INTRABAR;
input ATR_TRAIL_SOURCE     InpAtrTrailSource = ATR_SOURCE_TRADITIONAL;
input int                  InpTrailingAtrPeriod = 14;
input int                  InpTrailingDemaLength = 14;
input bool                 InpAtrUseExternalActivationThreshold = false;
input ATR_EXTERNAL_ACTIVATION_TYPE InpAtrExternalActivationType = ATR_EXTERNAL_POINTS;
input double               InpAtrTrailActivation = 0.0;
input double               InpAtrTrailStep = 0.0;
input double               InpAtrTrailStop = 1.0;

input group "=== Points Trailing ==="
input double               InpPointsTrailActivation = 0.0;
input double               InpPointsTrailStep = 0.0;
input double               InpPointsTrailStop = 0.0;

input group "=== Dollars Trailing ==="
input double               InpDollarsTrailActivation = 0.0;
input double               InpDollarsTrailStep = 0.0;
input double               InpDollarsTrailStop = 0.0;

input group "=== Entry Stop Loss ==="
input bool                 InpEnableEntryStopLoss = false;
input ENTRY_STOPLOSS_TYPE  InpEntryStopLossType = ENTRY_STOPLOSS_ATR;
input double               InpStopFactor = 1.0; // ATR/DEMA entry stop multiple.
input int                  InpEntryStopAtrPeriod = 14;
input int                  InpEntryStopDemaLength = 14;

input group "=== Market Structure Stop Loss ==="
input STRUCTURE_STOP_MODEL InpStructureStopModel = STRUCTURE_MODEL_CHART_SWING_PIVOT;
input BOS_CHOCH_ENGINE     InpBosChochEngine = BOS_CHOCH_ENGINE_SIMPLIFIED_MQL;
input int                  InpStructurePivotStrength = 3;
input STRUCTURE_BUFFER_TYPE InpStructureBufferType = STRUCTURE_BUFFER_POINTS;
input double               InpStructurePointsBuffer = 0.0;
input double               InpStructureAtrBufferMultiple = 0.0;

input group "=== Visuals ==="
input bool                 InpShowRibbon = true;
input bool                 InpShowRiskLines = true;
input bool                 InpShowEventLabels = true;
input bool                 InpShowDebugComment = true;
input int                  InpRiskLineRightBars = 10;
input bool                 InpCleanupObjectsOnDeinit = true;

input group "=== Optimization Criterion ==="
input bool                 Crit_Use_Custom_Criterion   = true;   // Use Custom max with profit-based criterion
input bool                 Crit_Verbose_Log            = false;  // Print per-run breakdown

input bool                 Crit_Use_ROI                = true;   // Profit normalization (true: Profit/Deposit)
input int                  Crit_Tmin                   = 80;     // Min trades for full credit
input int                  Crit_Tcap                   = 1000;   // Trade count saturation cap
input bool                 Crit_Enforce_Hard_Min_Trades= true;   // Reject runs below hard minimum trade count
input int                  Crit_Hard_Min_Trades        = 95;     // Hard minimum required trade count
input double               Crit_WR_Floor               = 0.30;   // Win rate soft floor
input double               Crit_WR_Target              = 0.70;   // Win rate saturation
input double               Crit_DD_Sweet               = 30.0;   // Drawdown half-credit point (%)
input double               Crit_DD_Exp                 = 2.0;    // Drawdown penalty curvature
input double               Crit_PF_Cap                 = 5.0;    // Profit Factor cap
input double               Crit_Sharpe_Cap             = 3.0;    // Sharpe cap
input double               Crit_Sortino_Cap            = 4.0;    // Sortino cap
input double               Crit_Recov_Cap              = 5.0;    // Recovery cap
input double               Crit_W_Trades               = 0.35;   // Weight: trades
input double               Crit_W_PF                   = 0.18;   // Weight: PF
input double               Crit_W_WR                   = 0.22;   // Weight: win rate
input double               Crit_W_Sharpe               = 0.10;   // Weight: Sharpe
input double               Crit_W_Sortino              = 0.10;   // Weight: Sortino
input double               Crit_W_Recovery             = 0.05;   // Weight: Recovery
input double               Crit_WR_PriorA              = 3.0;    // Bayesian smoothing alpha
input double               Crit_WR_PriorB              = 3.0;    // Bayesian smoothing beta
input bool                 Crit_Mild_Margin_Nudge      = true;   // Tiny penalty for low min margin level.
struct EVAL_STATE
{
   bool valid;
   datetime barTime;
   double barOpen;
   double barHigh;
   double barLow;
   double barClose;

   bool tradeDateAllowed;

   bool buyEntry;
   bool sellEntry;
   bool leTrigger;
   bool seTrigger;
   bool buyColor;

   bool ribbonValid;
   double ribbonTop;
   double ribbonBottom;

   double conditionPrev;
   double conditionNow;
   double entryLine;
   double slLine;
   double tp1Line;
   double tp2Line;
   double tp3Line;

   bool longE;
   bool shortE;
   bool longX;
   bool shortX;
   bool longSL;
   bool shortSL;
   bool longTP1;
   bool shortTP1;
   bool longTP2;
   bool shortTP2;
   bool longTP3;
   bool shortTP3;
};

CTrade g_trade;

int g_rsiHandle = INVALID_HANDLE;
int g_atrFilterHandle = INVALID_HANDLE;
int g_atrTpHandle = INVALID_HANDLE;
int g_haHandle = INVALID_HANDLE;
int g_trailingAtrHandle = INVALID_HANDLE;
int g_trailingAtrPeriodCached = 0;
ENUM_TIMEFRAMES g_haTf = PERIOD_CURRENT;
datetime g_lastBarTime = 0;
datetime g_lastForeignWarn = 0;
int g_barCounter = 0;

double g_condition = 0.0;
double g_entryLine = 0.0;
double g_slLine = 0.0;
double g_tp1Line = 0.0;
double g_tp2Line = 0.0;
double g_tp3Line = 0.0;
datetime g_entryStartTime = 0;

double g_prevRibbonTop = 0.0;
double g_prevRibbonBottom = 0.0;
bool g_prevRibbonValid = false;

double g_atrEntryVolume = 0.0;
int g_atrEntryDirection = 0;

const string OBJ_PREFIX = "PSA79_";

void Log(const string msg)
{
   if(InpVerboseLogs)
      Print("[PineScriptalgo_EA] ", msg);
}

string DirToText(const int dir)
{
   if(dir > 0)
      return "LONG";
   if(dir < 0)
      return "SHORT";
   return "FLAT";
}

bool HasSignalEvent(const EVAL_STATE &st)
{
   return (st.buyEntry || st.sellEntry || st.longE || st.shortE || st.longX || st.shortX ||
           st.longSL || st.shortSL || st.longTP1 || st.shortTP1 ||
           st.longTP2 || st.shortTP2 || st.longTP3 || st.shortTP3);
}

string SignalFlagsSummary(const EVAL_STATE &st)
{
   string s = "";
   if(st.buyEntry) s += " buyEntry";
   if(st.sellEntry) s += " sellEntry";
   if(st.longE) s += " longE";
   if(st.shortE) s += " shortE";
   if(st.longX) s += " longX";
   if(st.shortX) s += " shortX";
   if(st.longTP1) s += " longTP1";
   if(st.shortTP1) s += " shortTP1";
   if(st.longTP2) s += " longTP2";
   if(st.shortTP2) s += " shortTP2";
   if(st.longTP3) s += " longTP3";
   if(st.shortTP3) s += " shortTP3";
   if(st.longSL) s += " longSL";
   if(st.shortSL) s += " shortSL";
   if(StringLen(s) == 0)
      s = " none";
   return s;
}

bool EqCond(const double a, const double b)
{
   return (MathAbs(a - b) < 1e-8);
}

double Truncate2(const double x)
{
   double factor = 100.0;
   return (double)((int)(x * factor)) / factor;
}

int FrameMinutes(const ENUM_TIMEFRAMES tf)
{
   int sec = PeriodSeconds(tf);
   if(sec <= 0)
      sec = 60;
   int minutes = sec / 60;
   if(minutes <= 0)
      minutes = 1;
   return minutes;
}

ENUM_TIMEFRAMES ResolveSignalTimeframe()
{
   int baseMinutes = FrameMinutes(_Period);
   int target = MathMax(1, baseMinutes * InpTimeframeMultiplier);

   ENUM_TIMEFRAMES tfs[] =
   {
      PERIOD_M1, PERIOD_M2, PERIOD_M3, PERIOD_M4, PERIOD_M5, PERIOD_M6,
      PERIOD_M10, PERIOD_M12, PERIOD_M15, PERIOD_M20, PERIOD_M30,
      PERIOD_H1, PERIOD_H2, PERIOD_H3, PERIOD_H4, PERIOD_H6, PERIOD_H8, PERIOD_H12,
      PERIOD_D1, PERIOD_W1, PERIOD_MN1
   };

   int tfMinutes[] =
   {
      1, 2, 3, 4, 5, 6,
      10, 12, 15, 20, 30,
      60, 120, 180, 240, 360, 480, 720,
      1440, 10080, 43200
   };

   int total = ArraySize(tfMinutes);
   for(int i = 0; i < total; ++i)
   {
      if(target <= tfMinutes[i])
         return tfs[i];
   }

   return PERIOD_MN1;
}

void ReleaseHandles()
{
   if(g_rsiHandle != INVALID_HANDLE)
      IndicatorRelease(g_rsiHandle);
   g_rsiHandle = INVALID_HANDLE;

   if(g_atrFilterHandle != INVALID_HANDLE)
      IndicatorRelease(g_atrFilterHandle);
   g_atrFilterHandle = INVALID_HANDLE;

   if(g_atrTpHandle != INVALID_HANDLE)
      IndicatorRelease(g_atrTpHandle);
   g_atrTpHandle = INVALID_HANDLE;

   if(g_haHandle != INVALID_HANDLE)
      IndicatorRelease(g_haHandle);
   g_haHandle = INVALID_HANDLE;

   if(g_trailingAtrHandle != INVALID_HANDLE)
      IndicatorRelease(g_trailingAtrHandle);
   g_trailingAtrHandle = INVALID_HANDLE;
   g_trailingAtrPeriodCached = 0;
}
bool EnsureHandles()
{
   if(g_rsiHandle == INVALID_HANDLE)
      g_rsiHandle = iRSI(_Symbol, _Period, InpRSIPeriod, PRICE_CLOSE);
   if(g_rsiHandle == INVALID_HANDLE)
      return false;

   if(g_atrFilterHandle == INVALID_HANDLE)
      g_atrFilterHandle = iATR(_Symbol, _Period, InpAtrFilterLen);
   if(g_atrFilterHandle == INVALID_HANDLE)
      return false;

   if(g_atrTpHandle == INVALID_HANDLE)
      g_atrTpHandle = iATR(_Symbol, _Period, InpAtrLength);
   if(g_atrTpHandle == INVALID_HANDLE)
      return false;

   ENUM_TIMEFRAMES neededTf = ResolveSignalTimeframe();
   if(g_haHandle == INVALID_HANDLE || g_haTf != neededTf)
   {
      if(g_haHandle != INVALID_HANDLE)
         IndicatorRelease(g_haHandle);

      g_haHandle = iCustom(_Symbol, neededTf, "Examples\\Heiken_Ashi");
      if(g_haHandle == INVALID_HANDLE)
         g_haHandle = iCustom(_Symbol, neededTf, "Heiken_Ashi");
      if(g_haHandle == INVALID_HANDLE)
         g_haHandle = iCustom(_Symbol, neededTf, "Heiken Ashi");

      if(g_haHandle == INVALID_HANDLE)
         return false;

      g_haTf = neededTf;
   }

   return true;
}

bool ReadBufferValue(const int handle, const int buffer, const int shift, double &value)
{
   if(handle == INVALID_HANDLE)
      return false;

   double tmp[];
   ArraySetAsSeries(tmp, true);
   int copied = CopyBuffer(handle, buffer, shift, 1, tmp);
   if(copied != 1)
      return false;

   value = tmp[0];
   return true;
}

bool IsNewBar()
{
   datetime t = iTime(_Symbol, _Period, 0);
   if(t <= 0)
      return false;

   if(g_lastBarTime == 0)
   {
      g_lastBarTime = t;
      return false;
   }

   if(t != g_lastBarTime)
   {
      g_lastBarTime = t;
      return true;
   }

   return false;
}
double SMAArray(const double &arr[])
{
   int n = ArraySize(arr);
   if(n <= 0)
      return 0.0;

   double s = 0.0;
   for(int i = 0; i < n; ++i)
      s += arr[i];
   return (s / (double)n);
}

double EMAArraySeries(const double &arr[], const int len)
{
   int n = ArraySize(arr);
   if(n <= 0)
      return 0.0;
   if(len <= 1)
      return arr[0];

   double alpha = 2.0 / (len + 1.0);
   double ema = arr[n - 1];
   for(int i = n - 2; i >= 0; --i)
      ema = alpha * arr[i] + (1.0 - alpha) * ema;

   return ema;
}

void TrailingLog(const string msg)
{
   if(InpTrailingVerboseLogs)
      Print("[PineScriptalgo_EA][Trailing] ", msg);
}

double GetSymbolPointValue()
{
   double point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
   if(point <= 0.0)
      point = _Point;
   return point;
}

double GetSymbolTickSizeValue()
{
   double tick = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tick <= 0.0)
      tick = GetSymbolPointValue();
   return tick;
}

int GetSymbolDigitsValue()
{
   int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
   if(digits < 0)
      digits = _Digits;
   return digits;
}

double NormalizePriceToTick(const double price)
{
   double tick = GetSymbolTickSizeValue();
   int digits = GetSymbolDigitsValue();
   if(tick <= 0.0)
      return NormalizeDouble(price, digits);

   double ticks = MathRound(price / tick);
   return NormalizeDouble(ticks * tick, digits);
}

bool EnsureTrailingAtrHandle()
{
   int period = MathMax(1, InpTrailingAtrPeriod);
   if(g_trailingAtrHandle != INVALID_HANDLE && g_trailingAtrPeriodCached != period)
   {
      IndicatorRelease(g_trailingAtrHandle);
      g_trailingAtrHandle = INVALID_HANDLE;
      g_trailingAtrPeriodCached = 0;
   }

   if(g_trailingAtrHandle == INVALID_HANDLE)
   {
      g_trailingAtrHandle = iATR(_Symbol, _Period, period);
      if(g_trailingAtrHandle == INVALID_HANDLE)
      {
         TrailingLog("Failed to create ATR handle. period=" + (string)period);
         return false;
      }
      g_trailingAtrPeriodCached = period;
   }

   return true;
}

void BuildEMAArraySeries(const double &src[], const int len, double &dst[])
{
   int n = ArraySize(src);
   ArrayResize(dst, n);
   ArraySetAsSeries(dst, true);
   if(n <= 0)
      return;

   if(len <= 1)
   {
      for(int i = 0; i < n; ++i)
         dst[i] = src[i];
      return;
   }

   double alpha = 2.0 / (len + 1.0);
   dst[n - 1] = src[n - 1];
   for(int i = n - 2; i >= 0; --i)
      dst[i] = alpha * src[i] + (1.0 - alpha) * dst[i + 1];
}

bool ReadTrailingAtrValue(const int shift, double &atrValue)
{
   atrValue = 0.0;
   if(!EnsureTrailingAtrHandle())
      return false;

   if(InpAtrTrailSource == ATR_SOURCE_TRADITIONAL)
      return ReadBufferValue(g_trailingAtrHandle, 0, shift, atrValue) && atrValue > 0.0;

   int demaLen = MathMax(1, InpTrailingDemaLength);
   int count = MathMax(shift + 1, MathMax(64, demaLen * 8));

   double atrSeries[];
   ArraySetAsSeries(atrSeries, true);
   int copied = CopyBuffer(g_trailingAtrHandle, 0, 0, count, atrSeries);
   if(copied <= shift)
      return false;

   ArrayResize(atrSeries, copied);
   double ema1[];
   double ema2[];
   BuildEMAArraySeries(atrSeries, demaLen, ema1);
   BuildEMAArraySeries(ema1, demaLen, ema2);

   atrValue = (2.0 * ema1[shift]) - ema2[shift];
   return (atrValue > 0.0);
}

bool ReadAtrValueForTimeframe(const ENUM_TIMEFRAMES tf,
                              const int period,
                              const int shift,
                              double &atrValue)
{
   atrValue = 0.0;
   int safePeriod = (int)MathMax(1, period);
   int handle = iATR(_Symbol, tf, safePeriod);
   if(handle == INVALID_HANDLE)
      return false;

   bool ok = ReadBufferValue(handle, 0, shift, atrValue) && atrValue > 0.0;
   IndicatorRelease(handle);
   return ok;
}

bool ReadDemaAtrValueForTimeframe(const ENUM_TIMEFRAMES tf,
                                  const int period,
                                  const int demaLen,
                                  const int shift,
                                  double &atrValue)
{
   atrValue = 0.0;
   int safePeriod = (int)MathMax(1, period);
   int safeDemaLen = (int)MathMax(1, demaLen);
   int handle = iATR(_Symbol, tf, safePeriod);
   if(handle == INVALID_HANDLE)
      return false;

   int count = (int)MathMax(shift + 1, MathMax(64, safeDemaLen * 8));
   double atrSeries[];
   ArraySetAsSeries(atrSeries, true);
   int copied = CopyBuffer(handle, 0, 0, count, atrSeries);
   IndicatorRelease(handle);
   if(copied <= shift)
      return false;

   ArrayResize(atrSeries, copied);
   double ema1[];
   double ema2[];
   BuildEMAArraySeries(atrSeries, safeDemaLen, ema1);
   BuildEMAArraySeries(ema1, safeDemaLen, ema2);

   atrValue = (2.0 * ema1[shift]) - ema2[shift];
   return (atrValue > 0.0);
}
bool IsManagedPositionByTicket(const ulong ticket)
{
   if(ticket == 0 || !PositionSelectByTicket(ticket))
      return false;
   if(PositionGetString(POSITION_SYMBOL) != _Symbol)
      return false;
   if((long)PositionGetInteger(POSITION_MAGIC) != InpMagicNumber)
      return false;
   return true;
}

double FavorableMovePrice(const ENUM_POSITION_TYPE posType,
                          const double openPrice,
                          const double bid,
                          const double ask)
{
   if(openPrice <= 0.0)
      return 0.0;

   double currentPrice = (posType == POSITION_TYPE_BUY) ? bid : ask;
   if(currentPrice <= 0.0)
      return 0.0;

   return (posType == POSITION_TYPE_BUY) ? (currentPrice - openPrice) : (openPrice - currentPrice);
}

double ActivationMetricPoints(const ENUM_POSITION_TYPE posType,
                              const double openPrice,
                              const double bid,
                              const double ask)
{
   double point = GetSymbolPointValue();
   if(point <= 0.0)
      return 0.0;

   return FavorableMovePrice(posType, openPrice, bid, ask) / point;
}

double ActivationMetricDollars()
{
   return PositionGetDouble(POSITION_PROFIT);
}

double ActivationMetricAtr(const ENUM_POSITION_TYPE posType,
                           const double openPrice,
                           const double bid,
                           const double ask,
                           const double atrValue)
{
   if(atrValue <= 0.0)
      return 0.0;

   return FavorableMovePrice(posType, openPrice, bid, ask) / atrValue;
}

double PointsUnitsToPriceDistance(const double units)
{
   return units * GetSymbolPointValue();
}

double DollarsUnitsToPriceDistance(const double units, const double volume)
{
   if(units == 0.0)
      return 0.0;

   double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
   if(tickSize <= 0.0)
      tickSize = GetSymbolPointValue();

   double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
   if(tickValue <= 0.0)
      tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE_PROFIT);
   if(tickValue <= 0.0)
      tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE_LOSS);

   if(tickSize <= 0.0 || tickValue <= 0.0 || volume <= 0.0)
      return 0.0;

   return (units / (tickValue * volume)) * tickSize;
}

double AtrUnitsToPriceDistance(const double units, const double atrValue)
{
   if(units == 0.0 || atrValue <= 0.0)
      return 0.0;

   return units * atrValue;
}

bool CanModifyTrailingStop(const ENUM_POSITION_TYPE posType, const double sl, string &reasonOut)
{
   reasonOut = "";
   MqlTick tick;
   if(!SymbolInfoTick(_Symbol, tick))
   {
      reasonOut = "tick_unavailable";
      return false;
   }

   double point = GetSymbolPointValue();
   if(point <= 0.0)
   {
      reasonOut = "invalid_point";
      return false;
   }

   int stopsLevel = (int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
   int freezeLevel = (int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_FREEZE_LEVEL);
   double stopsDist = MathMax(0, stopsLevel) * point;
   double freezeDist = MathMax(0, freezeLevel) * point;

   if(posType == POSITION_TYPE_BUY)
   {
      if(sl >= tick.bid)
      {
         reasonOut = "buy_sl_not_below_bid";
         return false;
      }

      double dist = tick.bid - sl;
      if(stopsLevel > 0 && dist <= stopsDist)
      {
         reasonOut = "buy_sl_stops_level";
         return false;
      }
      if(freezeLevel > 0 && dist <= freezeDist)
      {
         reasonOut = "buy_sl_freeze_level";
         return false;
      }
   }
   else if(posType == POSITION_TYPE_SELL)
   {
      if(sl <= tick.ask)
      {
         reasonOut = "sell_sl_not_above_ask";
         return false;
      }

      double dist = sl - tick.ask;
      if(stopsLevel > 0 && dist <= stopsDist)
      {
         reasonOut = "sell_sl_stops_level";
         return false;
      }
      if(freezeLevel > 0 && dist <= freezeDist)
      {
         reasonOut = "sell_sl_freeze_level";
         return false;
      }
   }
   else
   {
      reasonOut = "unsupported_position_type";
      return false;
   }

   return true;
}

bool ModifyPositionSLWithTrailing(const ulong ticket, const double proposedSL, const string context)
{
   if(!IsManagedPositionByTicket(ticket))
      return false;

   ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
   double currentSL = PositionGetDouble(POSITION_SL);
   double currentTP = PositionGetDouble(POSITION_TP);
   double newSL = NormalizePriceToTick(proposedSL);
   if(newSL <= 0.0)
      return false;

   double eps = GetSymbolTickSizeValue() * 0.5;
   bool improve = false;
   if(currentSL <= 0.0)
      improve = true;
   else if(posType == POSITION_TYPE_BUY && newSL > (currentSL + eps))
      improve = true;
   else if(posType == POSITION_TYPE_SELL && newSL < (currentSL - eps))
      improve = true;

   if(!improve)
      return false;

   string reason = "";
   if(!CanModifyTrailingStop(posType, newSL, reason))
   {
      TrailingLog("Modify skipped ticket=" + (string)ticket + " context=" + context + " reason=" + reason);
      return false;
   }

   int digits = GetSymbolDigitsValue();
   double normSL = NormalizeDouble(newSL, digits);
   double normTP = (currentTP > 0.0) ? NormalizeDouble(currentTP, digits) : 0.0;
   if(!g_trade.PositionModify(ticket, normSL, normTP))
   {
      TrailingLog("PositionModify failed ticket=" + (string)ticket +
                  " context=" + context +
                  " retcode=" + (string)g_trade.ResultRetcode() +
                  " retmsg=" + g_trade.ResultRetcodeDescription());
      return false;
   }

   TrailingLog("PositionModify ok ticket=" + (string)ticket +
               " context=" + context +
               " sl=" + DoubleToString(normSL, digits));
   return true;
}

bool ShouldRunTrailingIntrabar()
{
   if(!InpEnableTrailingEngine)
      return false;

   if(InpTrailingMode == TRAIL_MODE_ATR)
      return (InpAtrTrailBehavior == ATR_BEHAVIOR_INTRABAR);

   return true;
}

bool ShouldRunTrailingBarClose()
{
   return (InpEnableTrailingEngine &&
           InpTrailingMode == TRAIL_MODE_ATR &&
           InpAtrTrailBehavior == ATR_BEHAVIOR_BAR_CLOSE);
}

void RunTrailingEngine(const bool intrabarPass)
{
   if(intrabarPass)
   {
      if(!ShouldRunTrailingIntrabar())
         return;
   }
   else if(!ShouldRunTrailingBarClose())
   {
      return;
   }

   int total = PositionsTotal();
   if(total <= 0)
      return;

   double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
   if(bid <= 0.0 || ask <= 0.0)
      return;

   double atrValue = 0.0;
   int atrShift = intrabarPass ? 0 : 1;
   if(InpTrailingMode == TRAIL_MODE_ATR)
   {
      if(!ReadTrailingAtrValue(atrShift, atrValue))
      {
         TrailingLog("ATR trailing skipped: ATR value unavailable.");
         return;
      }
   }

   for(int i = total - 1; i >= 0; --i)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !IsManagedPositionByTicket(ticket))
         continue;

      ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      if(posType != POSITION_TYPE_BUY && posType != POSITION_TYPE_SELL)
         continue;

      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double volume = PositionGetDouble(POSITION_VOLUME);
      if(openPrice <= 0.0 || volume <= 0.0)
         continue;

      double activationMetric = 0.0;
      double activationThreshold = 0.0;
      double stepUnits = 0.0;
      double stopUnits = 0.0;
      double dist = 0.0;
      string context = "trailing_engine";
      double atrProgress = 0.0;

      switch(InpTrailingMode)
      {
         case TRAIL_MODE_POINTS:
         {
            activationMetric = ActivationMetricPoints(posType, openPrice, bid, ask);
            activationThreshold = InpPointsTrailActivation;
            stepUnits = InpPointsTrailStep;
            stopUnits = InpPointsTrailStop;
            context = "trailing_points";
            break;
         }
         case TRAIL_MODE_DOLLARS:
         {
            activationMetric = ActivationMetricDollars();
            activationThreshold = InpDollarsTrailActivation;
            stepUnits = InpDollarsTrailStep;
            stopUnits = InpDollarsTrailStop;
            context = "trailing_dollars";
            break;
         }
         case TRAIL_MODE_ATR:
         {
            atrProgress = ActivationMetricAtr(posType, openPrice, bid, ask, atrValue);
            if(InpAtrUseExternalActivationThreshold)
            {
               if(InpAtrExternalActivationType == ATR_EXTERNAL_DOLLARS)
               {
                  activationMetric = ActivationMetricDollars();
                  activationThreshold = InpDollarsTrailActivation;
                  context = "trailing_atr_gate_dollars";
               }
               else
               {
                  activationMetric = ActivationMetricPoints(posType, openPrice, bid, ask);
                  activationThreshold = InpPointsTrailActivation;
                  context = "trailing_atr_gate_points";
               }
            }
            else
            {
               activationMetric = atrProgress;
               activationThreshold = InpAtrTrailActivation;
               context = "trailing_atr";
            }

            stepUnits = InpAtrTrailStep;
            stopUnits = InpAtrTrailStop;
            break;
         }
         default:
            break;
      }

      if(activationMetric < activationThreshold)
         continue;

      double steps = 0.0;
      if(InpTrailingMode == TRAIL_MODE_ATR)
      {
         double atrStepAnchor = InpAtrUseExternalActivationThreshold ? 0.0 : InpAtrTrailActivation;
         if(stepUnits > 0.0 && atrProgress > atrStepAnchor)
            steps = MathFloor((atrProgress - atrStepAnchor) / stepUnits + 1e-9);
         dist = AtrUnitsToPriceDistance(stopUnits + (steps * stepUnits), atrValue);
      }
      else
      {
         if(stepUnits > 0.0)
            steps = MathFloor((activationMetric - activationThreshold) / stepUnits + 1e-9);

         double targetUnits = stopUnits + (steps * stepUnits);
         if(InpTrailingMode == TRAIL_MODE_POINTS)
            dist = PointsUnitsToPriceDistance(targetUnits);
         else if(InpTrailingMode == TRAIL_MODE_DOLLARS)
            dist = DollarsUnitsToPriceDistance(targetUnits, volume);
      }

      if(dist <= 0.0)
         continue;

      double proposedSL = (posType == POSITION_TYPE_BUY) ? (openPrice + dist) : (openPrice - dist);
      if(proposedSL <= 0.0)
         continue;

      ModifyPositionSLWithTrailing(ticket, proposedSL, context);
   }
}
bool ComputeTrendType(bool &trendType)
{
   trendType = true;

   if(!EnsureHandles())
   {
      Log("ComputeTrendType: EnsureHandles failed.");
      return false;
   }

   double rsi = 0.0;
   if(!ReadBufferValue(g_rsiHandle, 0, 1, rsi))
   {
      Log("ComputeTrendType: RSI buffer read failed.");
      return false;
   }
   rsi = Truncate2(rsi);

   int need = MathMax(1, InpAtrMaLen);
   double atrVals[];
   ArraySetAsSeries(atrVals, true);
   int copied = CopyBuffer(g_atrFilterHandle, 0, 1, need, atrVals);
   if(copied != need)
   {
      Log("ComputeTrendType: ATR filter buffer read failed. copied=" + (string)copied + " need=" + (string)need);
      return false;
   }

   double atrNow = atrVals[0];
   double atrMa = 0.0;

   if(InpReplicateAtrMaTypo)
   {
      // Pine code: atrMaType = 'EMA' but condition checks 'EM', so SMA branch always runs.
      atrMa = SMAArray(atrVals);
   }
   else
   {
      atrMa = InpAtrMaUseEMAIfNoTypo ? EMAArraySeries(atrVals, InpAtrMaLen) : SMAArray(atrVals);
   }

   bool cndSidwayss1 = (atrNow >= atrMa);
   bool cndSidwayss2 = (rsi > InpTopLimitRSI || rsi < InpBotLimitRSI);
   bool cndSidways = (cndSidwayss1 || cndSidwayss2);
   bool cndSidways1 = (cndSidwayss1 && cndSidwayss2);

   bool sidewayss1 = (atrNow <= atrMa);
   bool sidewayss2 = (rsi < InpTopLimitRSI && rsi > InpBotLimitRSI);
   bool sideways = (sidewayss1 || sidewayss2);
   bool sideways1 = (sidewayss1 && sidewayss2);

   switch(InpFilterType)
   {
      case FILTER_ATR:
         trendType = cndSidwayss1;
         break;
      case FILTER_RSI:
         trendType = cndSidwayss2;
         break;
      case FILTER_ATR_OR_RSI:
         trendType = cndSidways;
         break;
      case FILTER_ATR_AND_RSI:
         trendType = cndSidways1;
         break;
      case FILTER_NO_FILTER:
         trendType = (rsi > 0.0);
         break;
      case FILTER_SIDEWAYS_ATR_OR_RSI:
         trendType = sideways;
         break;
      case FILTER_SIDEWAYS_ATR_AND_RSI:
         trendType = sideways1;
         break;
      default:
         trendType = true;
         break;
   }

   return true;
}

bool ResolveSignalShifts(const ENUM_TIMEFRAMES tf, const datetime signalBarTime, int &curShift, int &prevShift)
{
   int baseShift = iBarShift(_Symbol, tf, signalBarTime, false);
   if(baseShift < 0)
   {
      Log("ResolveSignalShifts failed. tf=" + EnumToString(tf) +
          " barTime=" + TimeToString(signalBarTime, TIME_DATE | TIME_MINUTES));
      return false;
   }

   curShift = baseShift + (InpUseLookaheadApprox ? 0 : 1);
   prevShift = curShift + 1;
   return true;
}

bool ComputeOpenCloseSignal(const datetime signalBarTime, bool &buyOC, bool &sellOC, bool &buyColor, double &lineTop, double &lineBottom)
{
   buyOC = false;
   sellOC = false;
   buyColor = false;
   lineTop = 0.0;
   lineBottom = 0.0;

   if(!EnsureHandles())
   {
      Log("ComputeOpenCloseSignal: EnsureHandles failed.");
      return false;
   }

   int curShift = 0;
   int prevShift = 0;
   if(!ResolveSignalShifts(g_haTf, signalBarTime, curShift, prevShift))
      return false;

   double openCur = 0.0, openPrev = 0.0, closeCur = 0.0, closePrev = 0.0;

   // Standard Heiken Ashi buffers in MT5: 0=open, 1=high, 2=low, 3=close
   if(!ReadBufferValue(g_haHandle, 0, curShift, openCur))
   {
      Log("ComputeOpenCloseSignal: HA open current read failed. shift=" + (string)curShift);
      return false;
   }
   if(!ReadBufferValue(g_haHandle, 0, prevShift, openPrev))
   {
      Log("ComputeOpenCloseSignal: HA open previous read failed. shift=" + (string)prevShift);
      return false;
   }
   if(!ReadBufferValue(g_haHandle, 3, curShift, closeCur))
   {
      Log("ComputeOpenCloseSignal: HA close current read failed. shift=" + (string)curShift);
      return false;
   }
   if(!ReadBufferValue(g_haHandle, 3, prevShift, closePrev))
   {
      Log("ComputeOpenCloseSignal: HA close previous read failed. shift=" + (string)prevShift);
      return false;
   }

   buyOC = (closeCur > openCur && closePrev <= openPrev);
   sellOC = (closeCur < openCur && closePrev >= openPrev);
   buyColor = (closeCur > openCur);

   lineTop = closeCur;
   lineBottom = openCur;

   return true;
}

bool ComputeRenkoSignal(const datetime signalBarTime, bool &buyR, bool &sellR, bool &buyColor, double &lineTop, double &lineBottom)
{
   buyR = false;
   sellR = false;
   buyColor = false;
   lineTop = 0.0;
   lineBottom = 0.0;

   ENUM_TIMEFRAMES tf = ResolveSignalTimeframe();
   int curShift = 0;
   int prevShift = 0;
   if(!ResolveSignalShifts(tf, signalBarTime, curShift, prevShift))
      return false;

   int startShift = curShift;
   int barsNeed = MathMax(120, InpRenkoSourceBars);

   MqlRates rates[];
   ArraySetAsSeries(rates, true);
   int copied = CopyRates(_Symbol, tf, startShift, barsNeed, rates);
   if(copied < 30)
   {
      Log("ComputeRenkoSignal: CopyRates failed. tf=" + EnumToString(tf) +
          " startShift=" + (string)startShift + " copied=" + (string)copied);
      return false;
   }

   double atrVals[];
   if(InpRenkoUseATR)
   {
      int atrHandle = iATR(_Symbol, tf, InpRenkoAtrLen);
      if(atrHandle == INVALID_HANDLE)
      {
         Log("ComputeRenkoSignal: ATR handle create failed for Renko.");
         return false;
      }

      ArraySetAsSeries(atrVals, true);
      int atrCopied = CopyBuffer(atrHandle, 0, startShift, copied, atrVals);
      IndicatorRelease(atrHandle);
      if(atrCopied != copied)
      {
         Log("ComputeRenkoSignal: ATR buffer read failed. copied=" + (string)atrCopied + " need=" + (string)copied);
         return false;
      }
   }

   double renkoOpen[];
   double renkoClose[];
   ArrayResize(renkoOpen, copied);
   ArrayResize(renkoClose, copied);

   double rc = rates[copied - 1].close;
   double ro = rc;

   for(int i = copied - 1; i >= 0; --i)
   {
      double brick = InpRenkoUseATR ? atrVals[i] : (InpRenkoTraditionalPoints * _Point);
      if(brick <= _Point)
         brick = _Point;

      double price = rates[i].close;
      int guard = 0;

      while(price >= rc + brick && guard < 200)
      {
         ro = rc;
         rc += brick;
         guard++;
      }

      while(price <= rc - brick && guard < 400)
      {
         ro = rc;
         rc -= brick;
         guard++;
      }

      renkoOpen[i] = ro;
      renkoClose[i] = rc;
   }

   double emaFast[];
   double emaSlow[];
   ArrayResize(emaFast, copied);
   ArrayResize(emaSlow, copied);

   double kf = 2.0 / (InpRenkoFastEMA + 1.0);
   double ks = 2.0 / (InpRenkoSlowEMA + 1.0);

   for(int i = copied - 1; i >= 0; --i)
   {
      if(i == copied - 1)
      {
         emaFast[i] = renkoClose[i];
         emaSlow[i] = renkoClose[i];
      }
      else
      {
         emaFast[i] = kf * renkoClose[i] + (1.0 - kf) * emaFast[i + 1];
         emaSlow[i] = ks * renkoClose[i] + (1.0 - ks) * emaSlow[i + 1];
      }
   }

   const int cur = 0;
   const int prev = 1;
   if(prev >= copied)
      return false;

   buyR = (emaFast[cur] > emaSlow[cur] && emaFast[prev] <= emaSlow[prev]);
   sellR = (emaFast[cur] < emaSlow[cur] && emaFast[prev] >= emaSlow[prev]);
   buyColor = (renkoClose[cur] > renkoOpen[cur]);

   lineTop = renkoClose[cur];
   lineBottom = renkoOpen[cur];

   return true;
}

bool CrossSeries(const double srcCur, const double lvlCur, const double srcPrev, const double lvlPrev, const bool over)
{
   if(over)
      return (srcCur > lvlCur && srcPrev < lvlPrev);
   return (srcCur < lvlCur && srcPrev > lvlPrev);
}
int PositionDirectionByMagic()
{
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         continue;

      long type = PositionGetInteger(POSITION_TYPE);
      return (type == POSITION_TYPE_BUY) ? 1 : -1;
   }
   return 0;
}

double PositionVolumeByMagic()
{
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         continue;

      return PositionGetDouble(POSITION_VOLUME);
   }
   return 0.0;
}

bool ShouldApplyEntryStopLoss()
{
   return (InpEnableEntryStopLoss &&
           (InpTPSType == TPS_TRAILING || InpTPSType == TPS_OPTIONS));
}

bool ShouldUseMarketStructureStopUpdater()
{
   return (ShouldApplyEntryStopLoss() &&
           InpEntryStopLossType == ENTRY_STOPLOSS_MARKET_STRUCTURE);
}

double ResolveEntryReferencePrice(const int direction, const double preferredPrice = 0.0)
{
   if(preferredPrice > 0.0)
      return preferredPrice;

   MqlTick tick;
   if(!SymbolInfoTick(_Symbol, tick))
      return 0.0;

   return (direction > 0) ? tick.ask : tick.bid;
}

ulong FindManagedPositionTicketByDirection(const int direction)
{
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         continue;

      long type = PositionGetInteger(POSITION_TYPE);
      int posDirection = (type == POSITION_TYPE_BUY) ? 1 : -1;
      if(direction != 0 && posDirection != direction)
         continue;

      return (ulong)PositionGetInteger(POSITION_TICKET);
   }

   return 0;
}

bool CopyStructureRates(const ENUM_TIMEFRAMES tf, MqlRates &rates[])
{
   int strength = (int)MathMax(1, InpStructurePivotStrength);
   int count = (int)MathMax(200, strength * 50);
   ArraySetAsSeries(rates, true);
   int copied = CopyRates(_Symbol, tf, 0, count, rates);
   return (copied > (strength * 2 + 10));
}

bool IsPivotHighAt(const MqlRates &rates[], const int idx, const int strength)
{
   int total = ArraySize(rates);
   if(idx < strength + 1 || idx + strength >= total)
      return false;

   double candidate = rates[idx].high;
   for(int k = 1; k <= strength; ++k)
   {
      if(candidate < rates[idx - k].high)
         return false;
      if(candidate <= rates[idx + k].high)
         return false;
   }

   return true;
}

bool IsPivotLowAt(const MqlRates &rates[], const int idx, const int strength)
{
   int total = ArraySize(rates);
   if(idx < strength + 1 || idx + strength >= total)
      return false;

   double candidate = rates[idx].low;
   for(int k = 1; k <= strength; ++k)
   {
      if(candidate > rates[idx - k].low)
         return false;
      if(candidate >= rates[idx + k].low)
         return false;
   }

   return true;
}

bool FindLatestConfirmedPivot(const MqlRates &rates[],
                              const int strength,
                              const bool wantHigh,
                              int &pivotShift,
                              double &pivotPrice)
{
   pivotShift = -1;
   pivotPrice = 0.0;
   int total = ArraySize(rates);
   int maxShift = total - strength - 1;
   for(int i = strength + 1; i <= maxShift; ++i)
   {
      bool isPivot = wantHigh ? IsPivotHighAt(rates, i, strength) : IsPivotLowAt(rates, i, strength);
      if(!isPivot)
         continue;

      pivotShift = i;
      pivotPrice = wantHigh ? rates[i].high : rates[i].low;
      return true;
   }

   return false;
}

bool FindConfirmedPivotInRange(const MqlRates &rates[],
                               const int strength,
                               const bool wantHigh,
                               const int minShift,
                               const int maxShift,
                               int &pivotShift,
                               double &pivotPrice)
{
   pivotShift = -1;
   pivotPrice = 0.0;

   int total = ArraySize(rates);
   int start = (int)MathMax(strength + 1, minShift);
   int finish = (int)MathMin(maxShift, total - strength - 1);
   if(start > finish)
      return false;

   for(int i = start; i <= finish; ++i)
   {
      bool isPivot = wantHigh ? IsPivotHighAt(rates, i, strength) : IsPivotLowAt(rates, i, strength);
      if(!isPivot)
         continue;

      pivotShift = i;
      pivotPrice = wantHigh ? rates[i].high : rates[i].low;
      return true;
   }

   return false;
}

bool FindExtremePriceInRange(const MqlRates &rates[],
                             const int fromShift,
                             const int toShift,
                             const bool wantHigh,
                             double &extremePrice)
{
   extremePrice = 0.0;
   int total = ArraySize(rates);
   int start = (int)MathMax(1, MathMin(fromShift, toShift));
   int finish = (int)MathMin(total - 1, MathMax(fromShift, toShift));
   if(start > finish)
      return false;

   extremePrice = wantHigh ? rates[start].high : rates[start].low;
   for(int i = start + 1; i <= finish; ++i)
   {
      double candidate = wantHigh ? rates[i].high : rates[i].low;
      if(wantHigh)
         extremePrice = MathMax(extremePrice, candidate);
      else
         extremePrice = MathMin(extremePrice, candidate);
   }

   return true;
}

int FindCloseBreakAbove(const MqlRates &rates[], const int pivotShift, const double level)
{
   for(int i = pivotShift - 1; i >= 1; --i)
   {
      if(rates[i].close > level)
         return i;
   }
   return -1;
}

int FindCloseBreakBelow(const MqlRates &rates[], const int pivotShift, const double level)
{
   for(int i = pivotShift - 1; i >= 1; --i)
   {
      if(rates[i].close < level)
         return i;
   }
   return -1;
}

bool FindBullishBosChochAnchor(const MqlRates &rates[],
                               const int strength,
                               const bool parityMode,
                               double &anchorPrice)
{
   anchorPrice = 0.0;
   int total = ArraySize(rates);
   int maxShift = total - strength - 1;
   for(int pivotShift = strength + 1; pivotShift <= maxShift; ++pivotShift)
   {
      if(!IsPivotHighAt(rates, pivotShift, strength))
         continue;

      double pivotPrice = rates[pivotShift].high;
      int breakShift = FindCloseBreakAbove(rates, pivotShift, pivotPrice);
      if(breakShift < 1)
         continue;

      if(parityMode)
      {
         if(FindExtremePriceInRange(rates, breakShift, pivotShift, false, anchorPrice))
            return true;
      }
      else
      {
         int anchorShift = -1;
         if(FindConfirmedPivotInRange(rates, strength, false, breakShift + 1, pivotShift - 1, anchorShift, anchorPrice))
            return true;
         if(FindExtremePriceInRange(rates, breakShift, pivotShift, false, anchorPrice))
            return true;
      }
   }

   return false;
}

bool FindBearishBosChochAnchor(const MqlRates &rates[],
                               const int strength,
                               const bool parityMode,
                               double &anchorPrice)
{
   anchorPrice = 0.0;
   int total = ArraySize(rates);
   int maxShift = total - strength - 1;
   for(int pivotShift = strength + 1; pivotShift <= maxShift; ++pivotShift)
   {
      if(!IsPivotLowAt(rates, pivotShift, strength))
         continue;

      double pivotPrice = rates[pivotShift].low;
      int breakShift = FindCloseBreakBelow(rates, pivotShift, pivotPrice);
      if(breakShift < 1)
         continue;

      if(parityMode)
      {
         if(FindExtremePriceInRange(rates, breakShift, pivotShift, true, anchorPrice))
            return true;
      }
      else
      {
         int anchorShift = -1;
         if(FindConfirmedPivotInRange(rates, strength, true, breakShift + 1, pivotShift - 1, anchorShift, anchorPrice))
            return true;
         if(FindExtremePriceInRange(rates, breakShift, pivotShift, true, anchorPrice))
            return true;
      }
   }

   return false;
}

bool ResolveStructureBufferDistance(const ENUM_TIMEFRAMES tf, double &bufferDistance)
{
   bufferDistance = 0.0;
   if(InpStructureBufferType == STRUCTURE_BUFFER_POINTS)
   {
      bufferDistance = PointsUnitsToPriceDistance(MathMax(0.0, InpStructurePointsBuffer));
      return true;
   }

   double atrValue = 0.0;
   if(!ReadAtrValueForTimeframe(tf, InpEntryStopAtrPeriod, 1, atrValue))
      return false;

   bufferDistance = AtrUnitsToPriceDistance(MathMax(0.0, InpStructureAtrBufferMultiple), atrValue);
   return true;
}

bool ComputeStructureStopAnchor(const int direction,
                                double &anchorPrice,
                                ENUM_TIMEFRAMES &tfUsed,
                                string &context)
{
   anchorPrice = 0.0;
   tfUsed = (ENUM_TIMEFRAMES)_Period;
   context = "entry_stop_structure";

   int strength = (int)MathMax(1, InpStructurePivotStrength);
   bool wantHigh = (direction < 0);
   MqlRates rates[];

   switch(InpStructureStopModel)
   {
      case STRUCTURE_MODEL_SIGNAL_TIMEFRAME_SWING:
      {
         tfUsed = ResolveSignalTimeframe();
         context = "entry_stop_structure_signal_pivot";
         if(!CopyStructureRates(tfUsed, rates))
            return false;

         int pivotShift = -1;
         return FindLatestConfirmedPivot(rates, strength, wantHigh, pivotShift, anchorPrice);
      }
      case STRUCTURE_MODEL_BOS_CHOCH:
      {
         tfUsed = (ENUM_TIMEFRAMES)_Period;
         context = (InpBosChochEngine == BOS_CHOCH_ENGINE_CLOSE_PINE_PARITY)
                   ? "entry_stop_structure_bos_choch_pine"
                   : "entry_stop_structure_bos_choch_mql";
         if(!CopyStructureRates(tfUsed, rates))
            return false;

         bool ok = (direction > 0)
                   ? FindBullishBosChochAnchor(rates,
                                               strength,
                                               InpBosChochEngine == BOS_CHOCH_ENGINE_CLOSE_PINE_PARITY,
                                               anchorPrice)
                   : FindBearishBosChochAnchor(rates,
                                               strength,
                                               InpBosChochEngine == BOS_CHOCH_ENGINE_CLOSE_PINE_PARITY,
                                               anchorPrice);
         if(ok)
            return true;

         int pivotShift = -1;
         return FindLatestConfirmedPivot(rates, strength, wantHigh, pivotShift, anchorPrice);
      }
      case STRUCTURE_MODEL_CHART_SWING_PIVOT:
      default:
      {
         tfUsed = (ENUM_TIMEFRAMES)_Period;
         context = "entry_stop_structure_chart_pivot";
         if(!CopyStructureRates(tfUsed, rates))
            return false;

         int pivotShift = -1;
         return FindLatestConfirmedPivot(rates, strength, wantHigh, pivotShift, anchorPrice);
      }
   }
}

bool ComputeEntryStopLossPrice(const int direction,
                               const double referenceOpenPrice,
                               double &stopLoss,
                               string &context)
{
   stopLoss = 0.0;
   context = "entry_stop";
   if(!ShouldApplyEntryStopLoss())
      return false;

   double refPrice = ResolveEntryReferencePrice(direction, referenceOpenPrice);
   switch(InpEntryStopLossType)
   {
      case ENTRY_STOPLOSS_ATR:
      {
         double atrValue = 0.0;
         if(refPrice <= 0.0 || !ReadAtrValueForTimeframe((ENUM_TIMEFRAMES)_Period, InpEntryStopAtrPeriod, 1, atrValue))
            return false;

         double dist = AtrUnitsToPriceDistance(MathMax(0.0, InpStopFactor), atrValue);
         if(dist <= 0.0)
            return false;

         context = "entry_stop_atr";
         stopLoss = (direction > 0) ? (refPrice - dist) : (refPrice + dist);
         break;
      }
      case ENTRY_STOPLOSS_DEMA_ATR:
      {
         double atrValue = 0.0;
         if(refPrice <= 0.0 || !ReadDemaAtrValueForTimeframe((ENUM_TIMEFRAMES)_Period,
                                                             InpEntryStopAtrPeriod,
                                                             InpEntryStopDemaLength,
                                                             1,
                                                             atrValue))
            return false;

         double dist = AtrUnitsToPriceDistance(MathMax(0.0, InpStopFactor), atrValue);
         if(dist <= 0.0)
            return false;

         context = "entry_stop_dema_atr";
         stopLoss = (direction > 0) ? (refPrice - dist) : (refPrice + dist);
         break;
      }
      case ENTRY_STOPLOSS_MARKET_STRUCTURE:
      {
         double anchorPrice = 0.0;
         double bufferDistance = 0.0;
         ENUM_TIMEFRAMES tfUsed = (ENUM_TIMEFRAMES)_Period;
         if(!ComputeStructureStopAnchor(direction, anchorPrice, tfUsed, context))
            return false;
         if(anchorPrice <= 0.0 || !ResolveStructureBufferDistance(tfUsed, bufferDistance))
            return false;

         stopLoss = (direction > 0) ? (anchorPrice - bufferDistance) : (anchorPrice + bufferDistance);
         break;
      }
      default:
         return false;
   }

   stopLoss = NormalizePriceToTick(stopLoss);
   return (stopLoss > 0.0);
}

bool AttachEntryStopLossToPosition(const ulong ticket, const int direction, const string reason)
{
   if(ticket == 0 || !IsManagedPositionByTicket(ticket))
      return false;

   double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
   double stopLoss = 0.0;
   string context = reason;
   if(!ComputeEntryStopLossPrice(direction, openPrice, stopLoss, context))
   {
      Log("Entry stop retry skipped ticket=" + (string)ticket + " reason=" + context + " compute_failed");
      return false;
   }

   if(ModifyPositionSLWithTrailing(ticket, stopLoss, context + "_retry"))
      return true;

   Log("Entry stop retry failed ticket=" + (string)ticket + " context=" + context + " stop=" + DoubleToString(stopLoss, _Digits));
   return false;
}

void UpdateMarketStructureStops()
{
   if(!ShouldUseMarketStructureStopUpdater())
      return;

   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0 || !IsManagedPositionByTicket(ticket))
         continue;

      ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
      int direction = (posType == POSITION_TYPE_BUY) ? 1 : -1;
      double stopLoss = 0.0;
      string context = "entry_stop_structure_update";
      if(!ComputeEntryStopLossPrice(direction, PositionGetDouble(POSITION_PRICE_OPEN), stopLoss, context))
         continue;

      ModifyPositionSLWithTrailing(ticket, stopLoss, context + "_ratchet");
   }
}
bool HasForeignPositionOnSymbol()
{
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         return true;
   }
   return false;
}

string FirstForeignPositionSummary()
{
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic == InpMagicNumber)
         continue;

      long type = PositionGetInteger(POSITION_TYPE);
      int dir = (type == POSITION_TYPE_BUY) ? 1 : -1;
      ulong ticket = (ulong)PositionGetInteger(POSITION_TICKET);
      double vol = PositionGetDouble(POSITION_VOLUME);

      return "ticket=" + (string)ticket +
             " magic=" + (string)magic +
             " dir=" + DirToText(dir) +
             " vol=" + DoubleToString(vol, 2);
   }

   return "none";
}

double NormalizeVolume(const double volume)
{
   double minVol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   double maxVol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
   double step = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

   if(step <= 0.0)
      step = minVol;
   if(step <= 0.0)
      step = 0.01;

   double v = MathMax(minVol, MathMin(maxVol, volume));
   double steps = MathFloor((v - minVol) / step + 0.5);
   double norm = minVol + steps * step;
   if(norm < minVol)
      norm = minVol;
   return norm;
}

double ComputeOrderVolume(const int direction)
{
   if(!InpUsePercentOfEquity)
      return NormalizeVolume(InpFixedLots);

   double equity = AccountInfoDouble(ACCOUNT_EQUITY);
   double price = (direction > 0) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK) : SymbolInfoDouble(_Symbol, SYMBOL_BID);
   double contractSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_CONTRACT_SIZE);

   if(equity <= 0.0 || price <= 0.0 || contractSize <= 0.0)
      return NormalizeVolume(InpFixedLots);

   double notional = equity * (InpPercentOfEquity / 100.0);
   double rawLots = notional / (price * contractSize);

   return NormalizeVolume(rawLots);
}

bool ClosePositionsByMagic(const int directionFilter = 0)
{
   bool ok = true;
   for(int i = PositionsTotal() - 1; i >= 0; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         continue;

      long type = PositionGetInteger(POSITION_TYPE);
      int direction = (type == POSITION_TYPE_BUY) ? 1 : -1;
      if(directionFilter != 0 && direction != directionFilter)
         continue;

      ulong ticket = (ulong)PositionGetInteger(POSITION_TICKET);
      Log("Closing " + DirToText(direction) + " ticket=" + (string)ticket + " (reason: signal switch)");
      if(!g_trade.PositionClose(ticket, InpSlippagePoints))
      {
         ok = false;
         Log("PositionClose failed ticket=" + (string)ticket +
             " err=" + (string)GetLastError() +
             " retcode=" + (string)g_trade.ResultRetcode() +
             " retmsg=" + g_trade.ResultRetcodeDescription());
      }
   }
   return ok;
}

bool ClosePartialByMagic(const int directionFilter, const double volumeToClose)
{
   double remaining = NormalizeVolume(volumeToClose);
   double minVol = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
   if(remaining < minVol)
      return true;

   bool ok = true;
   for(int i = PositionsTotal() - 1; i >= 0 && remaining >= minVol; --i)
   {
      string sym = PositionGetSymbol(i);
      if(sym != _Symbol)
         continue;

      long magic = PositionGetInteger(POSITION_MAGIC);
      if(magic != InpMagicNumber)
         continue;

      long type = PositionGetInteger(POSITION_TYPE);
      int direction = (type == POSITION_TYPE_BUY) ? 1 : -1;
      if(directionFilter != 0 && direction != directionFilter)
         continue;

      ulong ticket = (ulong)PositionGetInteger(POSITION_TICKET);
      double posVol = PositionGetDouble(POSITION_VOLUME);
      double closeVol = NormalizeVolume(MathMin(posVol, remaining));
      if(closeVol < minVol)
         continue;

      if(!g_trade.PositionClosePartial(ticket, closeVol, InpSlippagePoints))
      {
         // Fallback to full close if target ~= full position.
         if(closeVol >= posVol - (minVol * 0.5))
         {
            if(!g_trade.PositionClose(ticket, InpSlippagePoints))
            {
               ok = false;
               Log("Partial/fallback close failed ticket=" + (string)ticket + " err=" + (string)GetLastError());
               break;
            }
            remaining -= posVol;
         }
         else
         {
            ok = false;
            Log("PositionClosePartial failed ticket=" + (string)ticket + " err=" + (string)GetLastError());
            break;
         }
      }
      else
      {
         remaining -= closeVol;
      }
   }

   return ok;
}

bool OpenDirection(const int direction, const string comment)
{
   double vol = ComputeOrderVolume(direction);
   bool wantsEntryStop = ShouldApplyEntryStopLoss();
   bool ok = false;
   double initialStopLoss = 0.0;
   string stopContext = "entry_stop";
   Log("Open attempt dir=" + DirToText(direction) + " vol=" + DoubleToString(vol, 2) + " tag=" + comment);

   if(wantsEntryStop && ComputeEntryStopLossPrice(direction, 0.0, initialStopLoss, stopContext))
   {
      int digits = GetSymbolDigitsValue();
      double normSL = NormalizeDouble(initialStopLoss, digits);
      if(direction > 0)
         ok = g_trade.Buy(vol, _Symbol, 0.0, normSL, 0.0, comment);
      else
         ok = g_trade.Sell(vol, _Symbol, 0.0, normSL, 0.0, comment);

      if(ok)
      {
         Log("OpenDirection ok dir=" + DirToText(direction) +
             " order=" + (string)g_trade.ResultOrder() +
             " deal=" + (string)g_trade.ResultDeal() +
             " price=" + DoubleToString(g_trade.ResultPrice(), _Digits) +
             " sl=" + DoubleToString(normSL, digits) +
             " context=" + stopContext);
         return true;
      }

      Log("Protected open failed dir=" + DirToText(direction) +
          " err=" + (string)GetLastError() +
          " retcode=" + (string)g_trade.ResultRetcode() +
          " retmsg=" + g_trade.ResultRetcodeDescription() +
          " retry=naked");
   }
   else if(wantsEntryStop)
   {
      Log("Entry stop pre-compute failed dir=" + DirToText(direction) + " retry=naked");
   }

   if(direction > 0)
      ok = g_trade.Buy(vol, _Symbol, 0.0, 0.0, 0.0, comment);
   else
      ok = g_trade.Sell(vol, _Symbol, 0.0, 0.0, 0.0, comment);

   if(!ok)
   {
      Log("OpenDirection failed dir=" + DirToText(direction) +
          " err=" + (string)GetLastError() +
          " retcode=" + (string)g_trade.ResultRetcode() +
          " retmsg=" + g_trade.ResultRetcodeDescription());
      return false;
   }

   Log("OpenDirection ok dir=" + DirToText(direction) +
       " order=" + (string)g_trade.ResultOrder() +
       " deal=" + (string)g_trade.ResultDeal() +
       " price=" + DoubleToString(g_trade.ResultPrice(), _Digits));

   if(wantsEntryStop)
   {
      ulong ticket = FindManagedPositionTicketByDirection(direction);
      if(ticket == 0)
      {
         Log("Entry stop retry skipped dir=" + DirToText(direction) + " managed ticket not found after open.");
      }
      else if(!AttachEntryStopLossToPosition(ticket, direction, stopContext))
      {
         Log("Entry stop retry left position open ticket=" + (string)ticket + " dir=" + DirToText(direction));
      }
   }

   return true;
}
void CloseAtrByPercent(const int direction, const double pct)
{
   double baseVol = g_atrEntryVolume;
   if(baseVol <= 0.0)
      baseVol = PositionVolumeByMagic();
   if(baseVol <= 0.0)
      return;

   double reqVol = NormalizeVolume(baseVol * (pct / 100.0));
   double curVol = PositionVolumeByMagic();
   if(curVol <= 0.0)
      return;

   if(reqVol > curVol)
      reqVol = curVol;

   ClosePartialByMagic(direction, reqVol);
}
bool EvaluateBar(EVAL_STATE &st)
{
   ZeroMemory(st);
   st.valid = false;

   if(!EnsureHandles())
   {
      Log("EvaluateBar failed: EnsureHandles.");
      return false;
   }

   MqlRates bars[];
   ArraySetAsSeries(bars, true);
   int copied = CopyRates(_Symbol, _Period, 1, 3, bars);
   if(copied < 3)
   {
      Log("EvaluateBar failed: CopyRates returned " + (string)copied + " bars.");
      return false;
   }

   st.barTime = bars[0].time;
   st.barOpen = bars[0].open;
   st.barHigh = bars[0].high;
   st.barLow = bars[0].low;
   st.barClose = bars[0].close;

   st.tradeDateAllowed = (!InpEnableDateFilter || (st.barTime >= InpFromDate && st.barTime <= InpToDate));

   bool trendType = true;
   if(!ComputeTrendType(trendType))
   {
      Log("EvaluateBar failed: ComputeTrendType.");
      return false;
   }

   bool buySig = false;
   bool sellSig = false;
   bool buyColor = false;
   double ribTop = 0.0;
   double ribBottom = 0.0;

   if(InpSetupType == SETUP_OPEN_CLOSE)
   {
      if(!ComputeOpenCloseSignal(st.barTime, buySig, sellSig, buyColor, ribTop, ribBottom))
      {
         Log("EvaluateBar failed: ComputeOpenCloseSignal.");
         return false;
      }
   }
   else
   {
      if(!ComputeRenkoSignal(st.barTime, buySig, sellSig, buyColor, ribTop, ribBottom))
      {
         Log("EvaluateBar failed: ComputeRenkoSignal.");
         return false;
      }
   }

   st.buyEntry = (buySig && trendType);
   st.sellEntry = (sellSig && trendType);
   st.leTrigger = st.buyEntry;
   st.seTrigger = st.sellEntry;
   st.buyColor = buyColor;
   st.ribbonValid = true;
   st.ribbonTop = ribTop;
   st.ribbonBottom = ribBottom;

   double atrTP = 0.0;
   if(!ReadBufferValue(g_atrTpHandle, 0, 1, atrTP))
   {
      Log("EvaluateBar failed: ATR TP buffer read.");
      return false;
   }

   double takeProfit1Buy = 1.0 * InpProfitFactor * atrTP;
   double takeProfit2Buy = 2.0 * InpProfitFactor * atrTP;
   double takeProfit3Buy = 3.0 * InpProfitFactor * atrTP;

   double takeProfit1Sell = 1.0 * InpProfitFactor * atrTP;
   double takeProfit2Sell = 2.0 * InpProfitFactor * atrTP;
   double takeProfit3Sell = 3.0 * InpProfitFactor * atrTP;

   double i_lxLvlTP1 = st.leTrigger ? takeProfit1Buy : (st.seTrigger ? takeProfit1Sell : EMPTY_VALUE);
   double i_lxLvlTP2 = st.leTrigger ? takeProfit2Buy : (st.seTrigger ? takeProfit2Sell : EMPTY_VALUE);
   double i_lxLvlTP3 = st.leTrigger ? takeProfit3Buy : (st.seTrigger ? takeProfit3Sell : EMPTY_VALUE);
   double i_lxLvlSL = st.leTrigger ? takeProfit1Buy : (st.seTrigger ? takeProfit1Sell : EMPTY_VALUE);

   double prevCondition = g_condition;
   double prevEntryLine = g_entryLine;
   double prevSlLine = g_slLine;
   double prevTp1Line = g_tp1Line;
   double prevTp2Line = g_tp2Line;
   double prevTp3Line = g_tp3Line;

   st.conditionPrev = prevCondition;

   st.entryLine = (st.leTrigger && prevCondition <= 0.0) ? st.barClose :
                  (st.seTrigger && prevCondition >= 0.0) ? st.barClose : prevEntryLine;

   double slTopLvl = st.barClose + i_lxLvlSL;
   double slBotLvl = st.barClose - i_lxLvlSL;

   st.slLine = (prevCondition <= 0.0 && st.leTrigger) ? slBotLvl :
               (prevCondition >= 0.0 && st.seTrigger) ? slTopLvl : prevSlLine;

   st.tp1Line = prevTp1Line;
   if(!EqCond(prevCondition, 1.0) && st.leTrigger)
      st.tp1Line = st.barClose + i_lxLvlTP1;
   else if(!EqCond(prevCondition, -1.0) && st.seTrigger)
      st.tp1Line = st.barClose - i_lxLvlTP1;

   st.tp2Line = prevTp2Line;
   if(!EqCond(prevCondition, 1.1) && st.leTrigger)
      st.tp2Line = st.barClose + i_lxLvlTP2;
   else if(!EqCond(prevCondition, -1.1) && st.seTrigger)
      st.tp2Line = st.barClose - i_lxLvlTP2;

   st.tp3Line = prevTp3Line;
   if(!EqCond(prevCondition, 1.2) && st.leTrigger)
      st.tp3Line = st.barClose + i_lxLvlTP3;
   else if(!EqCond(prevCondition, -1.2) && st.seTrigger)
      st.tp3Line = st.barClose - i_lxLvlTP3;

   double highCur = bars[0].high;
   double lowCur = bars[0].low;
   double highPrev = bars[1].high;
   double lowPrev = bars[1].low;

   bool slLong = CrossSeries(lowCur, st.slLine, lowPrev, prevSlLine, false);
   bool slShort = CrossSeries(highCur, st.slLine, highPrev, prevSlLine, true);

   bool tp1Long = CrossSeries(highCur, st.tp1Line, highPrev, prevTp1Line, true);
   bool tp1Short = CrossSeries(lowCur, st.tp1Line, lowPrev, prevTp1Line, false);
   bool tp2Long = CrossSeries(highCur, st.tp2Line, highPrev, prevTp2Line, true);
   bool tp2Short = CrossSeries(lowCur, st.tp2Line, lowPrev, prevTp2Line, false);
   bool tp3Long = CrossSeries(highCur, st.tp3Line, highPrev, prevTp3Line, true);
   bool tp3Short = CrossSeries(lowCur, st.tp3Line, lowPrev, prevTp3Line, false);

   double condition = prevCondition;

   // Pine switch order parity.
   if(st.leTrigger && prevCondition <= 0.0)
      condition = 1.0;
   else if(st.seTrigger && prevCondition >= 0.0)
      condition = -1.0;
   else if(tp3Long && EqCond(prevCondition, 1.2))
      condition = 1.3;
   else if(tp3Short && EqCond(prevCondition, -1.2))
      condition = -1.3;
   else if(tp2Long && EqCond(prevCondition, 1.1))
      condition = 1.2;
   else if(tp2Short && EqCond(prevCondition, -1.1))
      condition = -1.2;
   else if(tp1Long && EqCond(prevCondition, 1.0))
      condition = 1.1;
   else if(tp1Short && EqCond(prevCondition, -1.0))
      condition = -1.1;
   else if(slLong && prevCondition >= 1.0)
      condition = 0.0;
   else if(slShort && prevCondition <= -1.0)
      condition = 0.0;

   st.conditionNow = condition;

   st.longE = (st.leTrigger && prevCondition <= 0.0 && EqCond(condition, 1.0));
   st.shortE = (st.seTrigger && prevCondition >= 0.0 && EqCond(condition, -1.0));

   st.longX = false;
   st.shortX = false;

   st.longSL = (slLong && prevCondition >= 1.0 && EqCond(condition, 0.0));
   st.shortSL = (slShort && prevCondition <= -1.0 && EqCond(condition, 0.0));

   st.longTP1 = (tp1Long && EqCond(prevCondition, 1.0) && EqCond(condition, 1.1));
   st.shortTP1 = (tp1Short && EqCond(prevCondition, -1.0) && EqCond(condition, -1.1));
   st.longTP2 = (tp2Long && EqCond(prevCondition, 1.1) && EqCond(condition, 1.2));
   st.shortTP2 = (tp2Short && EqCond(prevCondition, -1.1) && EqCond(condition, -1.2));
   st.longTP3 = (tp3Long && EqCond(prevCondition, 1.2) && EqCond(condition, 1.3));
   st.shortTP3 = (tp3Short && EqCond(prevCondition, -1.2) && EqCond(condition, -1.3));

   st.valid = true;
   return true;
}

void ExecuteTrailingMode(const EVAL_STATE &st)
{
   if(!st.tradeDateAllowed)
   {
      if(st.buyEntry || st.sellEntry)
         Log("Entry blocked by date filter. barTime=" + TimeToString(st.barTime, TIME_DATE | TIME_MINUTES) +
             " flags:" + SignalFlagsSummary(st));
      return;
   }

   if(st.buyEntry)
   {
      int dir = PositionDirectionByMagic();
      if(dir > 0)
      {
         Log("buyEntry ignored: already LONG.");
      }
      else
      {
         if(dir < 0)
         {
            if(!ClosePositionsByMagic(-1))
               Log("buyEntry: close SHORT failed before opening LONG.");
         }

         if(PositionDirectionByMagic() <= 0)
            OpenDirection(1, "LE");
         else
            Log("buyEntry blocked: SHORT position remained after close attempt.");
      }
   }

   if(st.sellEntry)
   {
      int dir = PositionDirectionByMagic();
      if(dir < 0)
      {
         Log("sellEntry ignored: already SHORT.");
      }
      else
      {
         if(dir > 0)
         {
            if(!ClosePositionsByMagic(1))
               Log("sellEntry: close LONG failed before opening SHORT.");
         }

         if(PositionDirectionByMagic() >= 0)
            OpenDirection(-1, "SE");
         else
            Log("sellEntry blocked: LONG position remained after close attempt.");
      }
   }
}

void ExecuteOptionsMode(const EVAL_STATE &st)
{
   if(!st.tradeDateAllowed)
   {
      if(st.buyEntry || st.sellEntry)
         Log("Options signal blocked by date filter. barTime=" + TimeToString(st.barTime, TIME_DATE | TIME_MINUTES) +
             " flags:" + SignalFlagsSummary(st));
      return;
   }

   if(st.buyEntry)
   {
      int dir = PositionDirectionByMagic();
      if(dir > 0)
      {
         Log("Options buyEntry ignored: already LONG.");
      }
      else
      {
         if(dir < 0)
         {
            if(!ClosePositionsByMagic(-1))
               Log("Options buyEntry: close SHORT failed before opening LONG.");
         }

         if(PositionDirectionByMagic() <= 0)
            OpenDirection(1, "LE");
         else
            Log("Options buyEntry blocked: SHORT remained after close attempt.");
      }
   }

   if(st.sellEntry)
   {
      int dir = PositionDirectionByMagic();
      if(dir > 0)
      {
         Log("Options sellEntry: closing LONG.");
         ClosePositionsByMagic(1);
      }
      else
      {
         Log("Options sellEntry ignored: no LONG position to close.");
      }
   }
}

void ExecuteAtrMode(const EVAL_STATE &st)
{
   if(!st.tradeDateAllowed)
   {
      if(st.longE || st.shortE || st.longSL || st.shortSL ||
         st.longTP1 || st.longTP2 || st.longTP3 || st.shortTP1 || st.shortTP2 || st.shortTP3)
      {
         Log("ATR signal blocked by date filter. barTime=" + TimeToString(st.barTime, TIME_DATE | TIME_MINUTES) +
             " flags:" + SignalFlagsSummary(st));
      }
      return;
   }

   int posDir = PositionDirectionByMagic();

   if(st.longE)
   {
      if(posDir > 0)
      {
         Log("longE ignored: already LONG.");
      }
      else
      {
         if(posDir < 0)
         {
            if(!ClosePositionsByMagic(-1))
               Log("longE: close SHORT failed before LONG entry.");
         }

         if(PositionDirectionByMagic() <= 0 && OpenDirection(1, "LE"))
         {
            g_atrEntryDirection = 1;
            g_atrEntryVolume = PositionVolumeByMagic();
         }
      }
   }

   posDir = PositionDirectionByMagic();
   if(st.shortE)
   {
      if(posDir < 0)
      {
         Log("shortE ignored: already SHORT.");
      }
      else
      {
         if(posDir > 0)
         {
            if(!ClosePositionsByMagic(1))
               Log("shortE: close LONG failed before SHORT entry.");
         }

         if(PositionDirectionByMagic() >= 0 && OpenDirection(-1, "SE"))
         {
            g_atrEntryDirection = -1;
            g_atrEntryVolume = PositionVolumeByMagic();
         }
      }
   }

   posDir = PositionDirectionByMagic();

   if(posDir > 0)
   {
      if(st.longSL)
      {
         Log("ATR longSL hit: closing LONG.");
         ClosePositionsByMagic(1);
      }
      else if(st.longTP1)
      {
         Log("ATR longTP1 hit: partial close.");
         CloseAtrByPercent(1, InpQtyTP1);
      }
      else if(st.longTP2)
      {
         Log("ATR longTP2 hit: partial close.");
         CloseAtrByPercent(1, InpQtyTP2);
      }
      else if(st.longTP3)
      {
         Log("ATR longTP3 hit: partial close.");
         CloseAtrByPercent(1, InpQtyTP3);
      }
   }
   else if(posDir < 0)
   {
      if(st.shortSL)
      {
         Log("ATR shortSL hit: closing SHORT.");
         ClosePositionsByMagic(-1);
      }
      else if(st.shortTP1)
      {
         Log("ATR shortTP1 hit: partial close.");
         CloseAtrByPercent(-1, InpQtyTP1);
      }
      else if(st.shortTP2)
      {
         Log("ATR shortTP2 hit: partial close.");
         CloseAtrByPercent(-1, InpQtyTP2);
      }
      else if(st.shortTP3)
      {
         Log("ATR shortTP3 hit: partial close.");
         CloseAtrByPercent(-1, InpQtyTP3);
      }
   }
   else if(st.longSL || st.shortSL || st.longTP1 || st.shortTP1 || st.longTP2 || st.shortTP2 || st.longTP3 || st.shortTP3)
   {
      Log("ATR exit signal ignored: no open EA position.");
   }

   if(PositionDirectionByMagic() == 0)
   {
      g_atrEntryDirection = 0;
      g_atrEntryVolume = 0.0;
   }
}
void DeleteObjectIfExists(const string name)
{
   if(ObjectFind(0, name) >= 0)
      ObjectDelete(0, name);
}

void UpsertTrendLine(const string name, const datetime t1, const double p1, const datetime t2, const double p2, const color clr, const ENUM_LINE_STYLE style, const int width)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_TREND, 0, t1, p1, t2, p2);
   else
   {
      ObjectMove(0, name, 0, t1, p1);
      ObjectMove(0, name, 1, t2, p2);
   }

   ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT, false);
   ObjectSetInteger(0, name, OBJPROP_STYLE, style);
   ObjectSetInteger(0, name, OBJPROP_WIDTH, width);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
}

void UpsertTextLabel(const string name, const datetime t, const double p, const string text, const color clr)
{
   if(ObjectFind(0, name) < 0)
      ObjectCreate(0, name, OBJ_TEXT, 0, t, p);
   else
      ObjectMove(0, name, 0, t, p);

   ObjectSetString(0, name, OBJPROP_TEXT, text);
   ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 8);
}

void DrawEvent(const string tag, const datetime t, const double p, const color clr, const bool up)
{
   string key = OBJ_PREFIX + tag + "_" + (string)t;
   string arrName = key + "_AR";
   string txtName = key + "_TX";

   if(ObjectFind(0, arrName) < 0)
   {
      ObjectCreate(0, arrName, OBJ_ARROW, 0, t, p);
      ObjectSetInteger(0, arrName, OBJPROP_ARROWCODE, up ? 241 : 242);
      ObjectSetInteger(0, arrName, OBJPROP_COLOR, clr);
      ObjectSetInteger(0, arrName, OBJPROP_WIDTH, 1);
   }

   double offset = SymbolInfoDouble(_Symbol, SYMBOL_POINT) * (up ? -40.0 : 40.0);
   if(ObjectFind(0, txtName) < 0)
      ObjectCreate(0, txtName, OBJ_TEXT, 0, t, p + offset);
   else
      ObjectMove(0, txtName, 0, t, p + offset);

   ObjectSetString(0, txtName, OBJPROP_TEXT, tag);
   ObjectSetInteger(0, txtName, OBJPROP_COLOR, clr);
   ObjectSetInteger(0, txtName, OBJPROP_FONTSIZE, 8);
}

void ClearRiskVisuals()
{
   DeleteObjectIfExists(OBJ_PREFIX + "SL_LINE");
   DeleteObjectIfExists(OBJ_PREFIX + "ENTRY_LINE");
   DeleteObjectIfExists(OBJ_PREFIX + "TP1_LINE");
   DeleteObjectIfExists(OBJ_PREFIX + "TP2_LINE");
   DeleteObjectIfExists(OBJ_PREFIX + "TP3_LINE");

   DeleteObjectIfExists(OBJ_PREFIX + "SL_TXT");
   DeleteObjectIfExists(OBJ_PREFIX + "ENTRY_TXT");
   DeleteObjectIfExists(OBJ_PREFIX + "TP1_TXT");
   DeleteObjectIfExists(OBJ_PREFIX + "TP2_TXT");
   DeleteObjectIfExists(OBJ_PREFIX + "TP3_TXT");
}

void UpdateVisuals(const EVAL_STATE &st)
{
   color green = clrLimeGreen;
   color red = clrTomato;
   color blue = clrDodgerBlue;
   color tpPink = (color)0x00A740EC;

   if(InpShowRibbon && st.ribbonValid)
   {
      datetime tPrev = iTime(_Symbol, _Period, 2);
      if(tPrev <= 0)
         tPrev = st.barTime - (datetime)PeriodSeconds(_Period);

      color trendClr = st.buyColor ? green : red;
      double topPrev = g_prevRibbonValid ? g_prevRibbonTop : st.ribbonTop;
      double botPrev = g_prevRibbonValid ? g_prevRibbonBottom : st.ribbonBottom;

      UpsertTrendLine(OBJ_PREFIX + "RIBBON_TOP", tPrev, topPrev, st.barTime, st.ribbonTop, trendClr, STYLE_DOT, 1);
      UpsertTrendLine(OBJ_PREFIX + "RIBBON_BOT", tPrev, botPrev, st.barTime, st.ribbonBottom, trendClr, STYLE_DOT, 1);

      g_prevRibbonTop = st.ribbonTop;
      g_prevRibbonBottom = st.ribbonBottom;
      g_prevRibbonValid = true;
   }
   else
   {
      DeleteObjectIfExists(OBJ_PREFIX + "RIBBON_TOP");
      DeleteObjectIfExists(OBJ_PREFIX + "RIBBON_BOT");
      g_prevRibbonValid = false;
   }

   bool riskActive = (InpTPSType == TPS_ATR && InpShowRiskLines && MathAbs(st.conditionNow) >= 1.0 && !st.leTrigger && !st.seTrigger);

   if(riskActive)
   {
      datetime t1 = (g_entryStartTime > 0) ? g_entryStartTime : st.barTime;
      datetime t2 = st.barTime + (datetime)(PeriodSeconds(_Period) * InpRiskLineRightBars);

      UpsertTrendLine(OBJ_PREFIX + "SL_LINE", t1, st.slLine, t2, st.slLine, red, STYLE_SOLID, 1);
      UpsertTrendLine(OBJ_PREFIX + "ENTRY_LINE", t1, st.entryLine, t2, st.entryLine, blue, STYLE_SOLID, 1);
      UpsertTrendLine(OBJ_PREFIX + "TP1_LINE", t1, st.tp1Line, t2, st.tp1Line, green, STYLE_SOLID, 1);
      UpsertTrendLine(OBJ_PREFIX + "TP2_LINE", t1, st.tp2Line, t2, st.tp2Line, green, STYLE_SOLID, 1);
      UpsertTrendLine(OBJ_PREFIX + "TP3_LINE", t1, st.tp3Line, t2, st.tp3Line, green, STYLE_SOLID, 1);

      UpsertTextLabel(OBJ_PREFIX + "SL_TXT", t2, st.slLine, "SL: " + DoubleToString(st.slLine, _Digits), red);
      UpsertTextLabel(OBJ_PREFIX + "ENTRY_TXT", t2, st.entryLine, "Entry: " + DoubleToString(st.entryLine, _Digits), blue);
      UpsertTextLabel(OBJ_PREFIX + "TP1_TXT", t2, st.tp1Line, "TP1: " + DoubleToString(st.tp1Line, _Digits), green);
      UpsertTextLabel(OBJ_PREFIX + "TP2_TXT", t2, st.tp2Line, "TP2: " + DoubleToString(st.tp2Line, _Digits), green);
      UpsertTextLabel(OBJ_PREFIX + "TP3_TXT", t2, st.tp3Line, "TP3: " + DoubleToString(st.tp3Line, _Digits), green);
   }
   else
   {
      ClearRiskVisuals();
   }

   if(InpShowEventLabels)
   {
      double pad = SymbolInfoDouble(_Symbol, SYMBOL_POINT) * 10.0;

      if(st.longE)
         DrawEvent("Long", st.barTime, st.barLow - pad, green, true);
      if(st.shortE)
         DrawEvent("Short", st.barTime, st.barHigh + pad, red, false);
      if(st.longX || st.shortX)
         DrawEvent("Close", st.barTime, st.barClose, clrGray, true);

      if(st.longTP1 || st.shortTP1)
         DrawEvent("TP1", st.barTime, st.barClose, tpPink, true);
      if(st.longTP2 || st.shortTP2)
         DrawEvent("TP2", st.barTime, st.barClose, tpPink, true);
      if(st.longTP3 || st.shortTP3)
         DrawEvent("TP3", st.barTime, st.barClose, tpPink, true);
      if(st.longSL || st.shortSL)
         DrawEvent("SL", st.barTime, st.barClose, clrMaroon, false);
   }
}

void EmitAlerts(const EVAL_STATE &st)
{
   if(st.longE || st.shortE || st.longX || st.shortX)
      Log("Any Alert");
   if(st.longE)
      Log("Long Entry");
   if(st.shortE)
      Log("Short Entry");
   if(st.longX)
      Log("Long Exit");
   if(st.shortX)
      Log("Short Exit");
}

void DeleteObjectsByPrefix(const string prefix)
{
   int total = ObjectsTotal(0, -1, -1);
   for(int i = total - 1; i >= 0; --i)
   {
      string name = ObjectName(0, i, -1, -1);
      if(StringFind(name, prefix) == 0)
         ObjectDelete(0, name);
   }
}
int OnInit()
{
   g_trade.SetExpertMagicNumber(InpMagicNumber);
   g_trade.SetDeviationInPoints(InpSlippagePoints);

   if(!EnsureHandles())
   {
      Log("Failed to initialize indicator handles.");
      return INIT_FAILED;
   }

   ENUM_TIMEFRAMES sigTf = ResolveSignalTimeframe();
   Log("Initialized. setupType=" + (string)InpSetupType +
       " tpsType=" + (string)InpTPSType +
       " chartTf=" + EnumToString((ENUM_TIMEFRAMES)_Period) +
       " signalTf=" + EnumToString(sigTf) +
       " lookaheadApprox=" + (InpUseLookaheadApprox ? "true" : "false"));
   return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
   if(InpCleanupObjectsOnDeinit)
      DeleteObjectsByPrefix(OBJ_PREFIX);

   if(InpShowDebugComment)
      Comment("");

   ReleaseHandles();
}

void OnTick()
{
   if(ShouldRunTrailingIntrabar())
      RunTrailingEngine(true);

   if(!IsNewBar())
      return;

   g_barCounter++;

   EVAL_STATE st;
   if(!EvaluateBar(st) || !st.valid)
   {
      Log("Bar evaluation failed.");
      return;
   }

   // Persist state each bar (Pine series parity).
   g_condition = st.conditionNow;
   g_entryLine = st.entryLine;
   g_slLine = st.slLine;
   g_tp1Line = st.tp1Line;
   g_tp2Line = st.tp2Line;
   g_tp3Line = st.tp3Line;

   if(st.longE || st.shortE)
      g_entryStartTime = st.barTime;

   bool hasSignals = HasSignalEvent(st);
   if(hasSignals)
   {
      Log("Signal bar " + TimeToString(st.barTime, TIME_DATE | TIME_MINUTES) +
          " cond=" + DoubleToString(st.conditionNow, 1) +
          " prev=" + DoubleToString(st.conditionPrev, 1) +
          " flags:" + SignalFlagsSummary(st) +
          " pos=" + DirToText(PositionDirectionByMagic()) +
          " dateOK=" + (st.tradeDateAllowed ? "1" : "0"));
   }
   else if(InpBarSummaryEveryNBars > 0 && (g_barCounter % InpBarSummaryEveryNBars) == 0)
   {
      Log("Bar summary " + TimeToString(st.barTime, TIME_DATE | TIME_MINUTES) +
          " cond=" + DoubleToString(st.conditionNow, 1) +
          " buy=" + (st.buyEntry ? "1" : "0") +
          " sell=" + (st.sellEntry ? "1" : "0") +
          " pos=" + DirToText(PositionDirectionByMagic()));
   }

   bool foreignPos = HasForeignPositionOnSymbol();
   if(foreignPos)
   {
      if(hasSignals || (TimeCurrent() - g_lastForeignWarn) > 60)
      {
         Log("Foreign position exists; execution continues (hardcoded). " + FirstForeignPositionSummary() +
             " flags:" + SignalFlagsSummary(st));
         g_lastForeignWarn = TimeCurrent();
      }
   }

   switch(InpTPSType)
   {
      case TPS_TRAILING:
         ExecuteTrailingMode(st);
         break;
      case TPS_OPTIONS:
         ExecuteOptionsMode(st);
         break;
      case TPS_ATR:
         ExecuteAtrMode(st);
         break;
      default:
         break;
   }

   if(ShouldUseMarketStructureStopUpdater())
      UpdateMarketStructureStops();

   if(ShouldRunTrailingIntrabar())
      RunTrailingEngine(true);
   else if(ShouldRunTrailingBarClose())
      RunTrailingEngine(false);

   UpdateVisuals(st);
   EmitAlerts(st);

   if(InpShowDebugComment)
   {
      string txt = "Pine79 Parity EA\n";
      txt += "cond=" + DoubleToString(st.conditionNow, 1) + "  prev=" + DoubleToString(st.conditionPrev, 1) + "\n";
      txt += "buyEntry=" + (st.buyEntry ? "1" : "0") + " sellEntry=" + (st.sellEntry ? "1" : "0") + "\n";
      txt += "LongE=" + (st.longE ? "1" : "0") + " ShortE=" + (st.shortE ? "1" : "0") + "\n";
      txt += "TP1=" + (st.longTP1 || st.shortTP1 ? "1" : "0") + " TP2=" + (st.longTP2 || st.shortTP2 ? "1" : "0") + " TP3=" + (st.longTP3 || st.shortTP3 ? "1" : "0") + " SL=" + (st.longSL || st.shortSL ? "1" : "0") + "\n";
      txt += "entry=" + DoubleToString(st.entryLine, _Digits) + " sl=" + DoubleToString(st.slLine, _Digits);
      Comment(txt);
   }
}

double OnTester()
{
   double trades = TesterStatistics(STAT_TRADES);
   if(Crit_Enforce_Hard_Min_Trades)
   {
      if(!MathIsValidNumber(trades) || trades < (double)Crit_Hard_Min_Trades)
         return -1.0;
   }

   if(!Crit_Use_Custom_Criterion)
      return TesterStatistics(STAT_PROFIT);

   CritConfig cfg = BuildDefaultConfig();
   cfg.use_roi = Crit_Use_ROI;
   cfg.Tmin = Crit_Tmin;
   cfg.Tcap = Crit_Tcap;
   cfg.wr_floor = Crit_WR_Floor;
   cfg.wr_target = Crit_WR_Target;
   cfg.DD_sweet = Crit_DD_Sweet;
   cfg.dd_exp = Crit_DD_Exp;
   cfg.PF_cap = Crit_PF_Cap;
   cfg.Sharpe_cap = Crit_Sharpe_Cap;
   cfg.Sortino_cap = Crit_Sortino_Cap;
   cfg.Recov_cap = Crit_Recov_Cap;
   cfg.w_trades = Crit_W_Trades;
   cfg.w_pf = Crit_W_PF;
   cfg.w_wr = Crit_W_WR;
   cfg.w_sharpe = Crit_W_Sharpe;
   cfg.w_sortino = Crit_W_Sortino;
   cfg.w_recovery = Crit_W_Recovery;
   cfg.wr_prior_a = Crit_WR_PriorA;
   cfg.wr_prior_b = Crit_WR_PriorB;
   cfg.mild_margin_nudge = Crit_Mild_Margin_Nudge;

   return ComputeCustomCriterion(cfg, Crit_Verbose_Log);
}



