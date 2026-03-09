//+------------------------------------------------------------------+
//|                                  Adaptive_VWAP_Institutional.mq5 |
//|                         Institutional-Grade VWAP Implementation  |
//|                                    Optimized for Trading Systems |
//+------------------------------------------------------------------+
#property strict
#property copyright   "Awran5"
#property link        "https://github.com/awran5/mql-trading-tools"
#property version     "1.00"
#property description "Adaptive VWAP Institutional with Auto-Detection"
#property description "Universal: Forex, Gold, Crypto, Indices, Stocks"

//--- Version constant for centralized management
#define VERSION "1.0.0"

#property indicator_chart_window
#property indicator_buffers 8
#property indicator_plots   5

//--- VWAP Main Line
#property indicator_label1  "VWAP"
#property indicator_type1   DRAW_LINE
#property indicator_color1  0x0FB9FF      // Golden amber (BGR format)
#property indicator_style1  STYLE_SOLID
#property indicator_width1  2

//--- Upper Deviation Bands
#property indicator_label2  "VWAP +1σ"
#property indicator_type2   DRAW_LINE
#property indicator_color2  0xE6AA78      // Soft cyan (1σ bands) - BGR
#property indicator_style2  STYLE_DOT
#property indicator_width2  1

#property indicator_label3  "VWAP +2σ"
#property indicator_type3   DRAW_LINE
#property indicator_color3  0xCC6633      // Deep ocean blue (2σ bands) - BGR
#property indicator_style3  STYLE_DOT
#property indicator_width3  1

//--- Lower Deviation Bands
#property indicator_label4  "VWAP -1σ"
#property indicator_type4   DRAW_LINE
#property indicator_color4  0xE6AA78      // Soft cyan (1σ bands) - BGR
#property indicator_style4  STYLE_DOT
#property indicator_width4  1

#property indicator_label5  "VWAP -2σ"
#property indicator_type5   DRAW_LINE
#property indicator_color5  0xCC6633      // Deep ocean blue (2σ bands) - BGR
#property indicator_style5  STYLE_DOT
#property indicator_width5  1

//+------------------------------------------------------------------+
//| Enumeration: Detected Asset Class                                 |
//+------------------------------------------------------------------+
enum ENUM_ASSET_CLASS
{
   ASSET_FOREX,         // Forex Pairs
   ASSET_METAL,         // Precious Metals (XAU, XAG)
   ASSET_CRYPTO,        // Cryptocurrencies
   ASSET_INDEX,         // Stock Indices
   ASSET_STOCK,         // Individual Stocks
   ASSET_ENERGY,        // Oil, Gas
   ASSET_UNKNOWN        // Unknown/Other
};

//+------------------------------------------------------------------+
//| Enumeration: Session Reset Period                                 |
//+------------------------------------------------------------------+
enum ENUM_VWAP_RESET
{
   RESET_AUTO,       // Auto (Based on Asset)
   RESET_DAILY,      // Daily (Midnight)
   RESET_FOREX_5PM,  // Forex Daily (5pm NY)
   RESET_WEEKLY,     // Weekly (Monday)
   RESET_MONTHLY,    // Monthly
   RESET_NONE        // No Reset (Continuous)
};

//+------------------------------------------------------------------+
//| Enumeration: Timezone Selection                                   |
//+------------------------------------------------------------------+
enum ENUM_VWAP_TIMEZONE
{
   TZ_AUTO,          // Auto (Based on Asset)
   TZ_SERVER,        // Server Time
   TZ_UTC,           // UTC (GMT+0)
   TZ_NEW_YORK,      // New York (DST-aware)
   TZ_LONDON,        // London (DST-aware)
   TZ_TOKYO,         // Tokyo (GMT+9, no DST)
   TZ_SYDNEY,        // Sydney (DST-aware)
   TZ_CUSTOM         // Custom Offset
};

//+------------------------------------------------------------------+
//| Input Parameters                                                  |
//+------------------------------------------------------------------+
input group           "═══════════ VWAP Settings ═══════════"
input ENUM_VWAP_RESET InpResetPeriod     = RESET_AUTO;     // Session Reset Period
input bool            InpShowBands       = false;          // Show Deviation Bands
input double          InpBandMult1       = 1.0;            // Band 1 Multiplier (σ)
input double          InpBandMult2       = 2.0;            // Band 2 Multiplier (σ)

input group           "═══════════ Timezone Settings ═══════════"
input ENUM_VWAP_TIMEZONE InpTimezone     = TZ_AUTO;        // Session Reset Timezone
input int             InpServerUTCOffset = 99;             // Server UTC Offset (99=Auto, or manual hours like +2)
input int             InpCustomOffsetHrs = 0;              // Custom Target UTC Offset (hours)

input group           "═══════════ Visual Settings ═══════════"
input color           InpVwapColor       = 0x0FB9FF;       // VWAP Line Color (Golden amber)
input int             InpVwapWidth       = 2;              // VWAP Line Width
input color           InpBand1Color      = 0xE6AA78;       // ±1σ Band Color (Soft cyan)
input color           InpBand2Color      = 0xCC6633;       // ±2σ Band Color (Deep ocean)
input bool            InpShowDiagnostics = true;           // Show On-Chart Diagnostics

input group           "═══════════ Data Quality ═══════════"
input bool            InpFilterSpikes    = true;           // Filter Volume Spikes
input double          InpSpikeThreshold  = 10.0;           // Spike Threshold (x median)

input group           "═══════════ Performance ═══════════"
input int             InpMaxRecalcBars   = 1000;           // Max Bars to Recalc on Update

input group           "═══════════ Caching ═══════════"
input bool            InpEnableCache        = true;        // Enable Disk Cache
input bool            InpClearCacheOnRemove = false;       // Clear Cache on Indicator Removal

//+------------------------------------------------------------------+
//| Indicator Buffers                                                 |
//+------------------------------------------------------------------+
double g_vwapBuffer[];          // Plot 0: Main VWAP line
double g_upperBand1[];          // Plot 1: +1 Standard Deviation
double g_upperBand2[];          // Plot 2: +2 Standard Deviation
double g_lowerBand1[];          // Plot 3: -1 Standard Deviation
double g_lowerBand2[];          // Plot 4: -2 Standard Deviation

//--- Calculation buffers (managed by MT5)
double g_cumPV[];               // Buffer 5: Cumulative (Price * Volume)
double g_cumVol[];              // Buffer 6: Cumulative Volume
double g_cumPV2[];              // Buffer 7: Cumulative (Price² * Volume)

//+------------------------------------------------------------------+
//| Global State Variables                                            |
//+------------------------------------------------------------------+
ENUM_ASSET_CLASS g_assetClass       = ASSET_UNKNOWN;
ENUM_VWAP_RESET  g_effectiveReset   = RESET_DAILY;
ENUM_VWAP_TIMEZONE g_effectiveTZ    = TZ_SERVER;
string           g_assetClassName   = "Unknown";
int              g_sessionBarCount  = 0;
double           g_sessionVolume    = 0.0;
double           g_currentVwap      = 0.0;
double           g_medianVolume     = 0.0;
datetime         g_sessionStartTime = 0;
datetime         g_lastBarTime      = 0;        // For cache validation
bool             g_cacheLoaded      = false;    // Track if cache was loaded

//--- DST Cache for performance optimization
datetime         g_lastDSTCheckTime = 0;        // Last time DST was checked
int              g_cachedTZOffset   = 0;         // Cached timezone offset in seconds

//+------------------------------------------------------------------+
//| Cache Constants                                                   |
//+------------------------------------------------------------------+
#define CACHE_MAGIC         0x56574150  // "VWAP" in hex
#define CACHE_VERSION       2           // Incremented for new cacheTime field
#define CACHE_DIR           "VWAP_Cache"
#define CACHE_EXPIRY_HOURS  24          // Cache expires after 24 hours
#define MEDIAN_SAMPLE_SIZE  100         // Sample size for median volume calculation
#define CACHE_SEARCH_LIMIT  100         // Max bars to search for cached bar
#define DST_CACHE_INTERVAL  3600        // DST check interval in seconds (1 hour)

//+------------------------------------------------------------------+
//| Cache Data Structure                                              |
//+------------------------------------------------------------------+
struct VWAPCacheData
{
   ulong    magic;              // Validation: CACHE_MAGIC
   int      cacheVersion;       // Cache format version
   int      period;             // Must match current timeframe
   datetime cacheTime;          // When cache was saved (for expiry)
   datetime sessionStart;       // Session start timestamp
   datetime lastBarTime;        // Last processed bar time
   double   cumPV;              // Cumulative (Price * Volume)
   double   cumVol;             // Cumulative Volume  
   double   cumPV2;             // Cumulative (Price² * Volume)
   double   currentVwap;        // Last calculated VWAP
   int      sessionBarCount;    // Bars in current session
   double   sessionVolume;      // Total session volume
   double   medianVolume;       // Cached median volume
};

//+------------------------------------------------------------------+
//| Get cache file path for current symbol/timeframe                  |
//+------------------------------------------------------------------+
string GetCacheFilePath()
{
   return CACHE_DIR + "\\" + _Symbol + "_" + EnumToString(_Period) + ".bin";
}

//+------------------------------------------------------------------+
//| Save session state to disk                                        |
//+------------------------------------------------------------------+
bool SaveSessionState()
{
   if(!InpEnableCache)
      return false;
   
   //--- Create directory if needed (FILE_COMMON for consistency with FileOpen)
   if(!FolderCreate(CACHE_DIR, FILE_COMMON))
   {
      int err = GetLastError();
      if(err != 5004)  // 5004 = folder already exists
      {
         PrintFormat("[VWAP] WARN: FolderCreate failed, error: %d", err);
         return false;
      }
   }
   
   string filePath = GetCacheFilePath();
   int handle = FileOpen(filePath, FILE_WRITE | FILE_BIN | FILE_COMMON | FILE_SHARE_READ | FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
   {
      PrintFormat("[VWAP] WARN: FileOpen for write failed, error: %d", GetLastError());
      return false;
   }
   
   //--- Prepare cache data
   VWAPCacheData data;
   data.magic           = CACHE_MAGIC;
   data.cacheVersion    = CACHE_VERSION;
   data.period          = (int)_Period;
   data.cacheTime       = TimeCurrent();  // Record save time for expiry check
   data.sessionStart    = g_sessionStartTime;
   data.lastBarTime     = g_lastBarTime;
   data.cumPV           = (ArraySize(g_cumPV) > 0) ? g_cumPV[0] : 0.0;
   data.cumVol          = (ArraySize(g_cumVol) > 0) ? g_cumVol[0] : 0.0;
   data.cumPV2          = (ArraySize(g_cumPV2) > 0) ? g_cumPV2[0] : 0.0;
   data.currentVwap     = g_currentVwap;
   data.sessionBarCount = g_sessionBarCount;
   data.sessionVolume   = g_sessionVolume;
   data.medianVolume    = g_medianVolume;
   
   //--- Write to file
   uint bytesWritten = FileWriteStruct(handle, data);
   FileClose(handle);
   
   if(bytesWritten != sizeof(VWAPCacheData))
   {
      PrintFormat("[VWAP] WARN: Cache write incomplete, wrote %d of %d bytes", bytesWritten, sizeof(VWAPCacheData));
      return false;
   }
   
   PrintFormat("[VWAP] Cache saved: %s (VWAP=%.5f, Bars=%d)", filePath, g_currentVwap, g_sessionBarCount);
   return true;
}

//+------------------------------------------------------------------+
//| Load session state from disk                                      |
//+------------------------------------------------------------------+
bool LoadSessionState()
{
   if(!InpEnableCache)
      return false;
   
   string filePath = GetCacheFilePath();
   
   if(!FileIsExist(filePath, FILE_COMMON))
      return false;
   
   int handle = FileOpen(filePath, FILE_READ | FILE_BIN | FILE_COMMON | FILE_SHARE_READ | FILE_SHARE_WRITE);
   if(handle == INVALID_HANDLE)
   {
      PrintFormat("[VWAP] WARN: FileOpen for read failed, error: %d", GetLastError());
      return false;
   }
   
   VWAPCacheData data;
   uint bytesRead = FileReadStruct(handle, data);
   FileClose(handle);
   
   if(bytesRead != sizeof(VWAPCacheData))
   {
      PrintFormat("[VWAP] WARN: Cache read incomplete, read %d of %d bytes", bytesRead, sizeof(VWAPCacheData));
      return false;
   }
   
   //--- Validate cache data
   if(data.magic != CACHE_MAGIC)
   {
      PrintFormat("[VWAP] WARN: Invalid cache magic number");
      return false;
   }
   
   if(data.cacheVersion != CACHE_VERSION)
   {
      PrintFormat("[VWAP] INFO: Cache version mismatch (file: %d, expected: %d), will recalculate", data.cacheVersion, CACHE_VERSION);
      return false;
   }
   
   if(data.period != (int)_Period)
   {
      PrintFormat("[VWAP] INFO: Cache timeframe mismatch, will recalculate");
      return false;
   }
   
   //--- Check cache expiry (24 hours)
   datetime cacheAge = TimeCurrent() - data.cacheTime;
   if(cacheAge > CACHE_EXPIRY_HOURS * 3600)
   {
      PrintFormat("[VWAP] INFO: Cache expired (%.1f hours old), will recalculate", 
                  (double)cacheAge / 3600.0);
      return false;
   }
   
   //--- Check if cache is from a different session (cross-day validation)
   //--- Compare session start day with current day
   MqlDateTime cachedDt, currentDt;
   TimeToStruct(data.sessionStart, cachedDt);
   TimeToStruct(TimeCurrent(), currentDt);
   
   if(cachedDt.day != currentDt.day || cachedDt.mon != currentDt.mon || cachedDt.year != currentDt.year)
   {
      PrintFormat("[VWAP] INFO: Cache from previous session (%s), will recalculate",
                  TimeToString(data.sessionStart, TIME_DATE));
      return false;
   }
   
   //--- Additional check for FOREX_5PM: validate 5pm NY boundary wasn't crossed
   if(g_effectiveReset == RESET_FOREX_5PM)
   {
      //--- Get hour in NY timezone for both cached time and current time
      datetime cachedNY = data.cacheTime - (InpServerUTCOffset * 3600) + (-5 * 3600);  // Assume EST
      datetime currentNY = TimeCurrent() - (InpServerUTCOffset * 3600) + (-5 * 3600);
      
      MqlDateTime cachedNYDt, currentNYDt;
      TimeToStruct(cachedNY, cachedNYDt);
      TimeToStruct(currentNY, currentNYDt);
      
      //--- Check if 5pm boundary was crossed (different trading day)
      //--- Trading day changes at 17:00, so compare "trading days"
      int cachedTradingDay = (cachedNYDt.hour >= 17) ? cachedNYDt.day_of_year + 1 : cachedNYDt.day_of_year;
      int currentTradingDay = (currentNYDt.hour >= 17) ? currentNYDt.day_of_year + 1 : currentNYDt.day_of_year;
      
      if(cachedTradingDay != currentTradingDay || cachedNYDt.year != currentNYDt.year)
      {
         PrintFormat("[VWAP] INFO: Cache from different forex trading day (5pm NY crossed), will recalculate");
         return false;
      }
   }
   
   //--- Restore state
   g_sessionStartTime = data.sessionStart;
   g_lastBarTime      = data.lastBarTime;
   g_currentVwap      = data.currentVwap;
   g_sessionBarCount  = data.sessionBarCount;
   g_sessionVolume    = data.sessionVolume;
   g_medianVolume     = data.medianVolume;
   g_cacheLoaded      = true;
   
   PrintFormat("[VWAP] Cache restored: %s (VWAP=%.5f, Bars=%d, Age=%.1fh)", 
               filePath, g_currentVwap, g_sessionBarCount, 
               (double)cacheAge / 3600.0);
   return true;
}

//+------------------------------------------------------------------+
//| Delete cache file from disk                                       |
//+------------------------------------------------------------------+
void DeleteCacheFile()
{
   string filePath = GetCacheFilePath();
   
   if(FileIsExist(filePath, FILE_COMMON))
   {
      if(FileDelete(filePath, FILE_COMMON))
         PrintFormat("[VWAP] Cache deleted: %s", filePath);
      else
         PrintFormat("[VWAP] WARN: Failed to delete cache, error: %d", GetLastError());
   }
}

//+------------------------------------------------------------------+
//| Detect asset class from symbol properties                         |
//+------------------------------------------------------------------+
ENUM_ASSET_CLASS DetectAssetClass(const string symbol)
{
   string sym = symbol;
   StringToUpper(sym);
   
   //--- Method 1: Check symbol path/category (most reliable in MQL5)
   string path;
   if(!SymbolInfoString(symbol, SYMBOL_PATH, path))
   {
      PrintFormat("[VWAP] WARN: SymbolInfoString(SYMBOL_PATH) failed, error: %d", GetLastError());
      path = "";
   }
   StringToUpper(path);
   
   if(StringFind(path, "CRYPTO") >= 0 || StringFind(path, "COIN") >= 0)
      return ASSET_CRYPTO;
   if(StringFind(path, "FOREX") >= 0 || StringFind(path, "FX") >= 0)
      return ASSET_FOREX;
   if(StringFind(path, "METAL") >= 0 || StringFind(path, "GOLD") >= 0)
      return ASSET_METAL;
   if(StringFind(path, "INDEX") >= 0 || StringFind(path, "INDIC") >= 0)
      return ASSET_INDEX;
   if(StringFind(path, "STOCK") >= 0 || StringFind(path, "EQUIT") >= 0)
      return ASSET_STOCK;
   if(StringFind(path, "ENERG") >= 0 || StringFind(path, "OIL") >= 0)
      return ASSET_ENERGY;
   
   //--- Method 2: Pattern matching on symbol name
   //--- Crypto patterns
   if(StringFind(sym, "BTC") >= 0 || StringFind(sym, "ETH") >= 0 ||
      StringFind(sym, "XRP") >= 0 || StringFind(sym, "LTC") >= 0 ||
      StringFind(sym, "ADA") >= 0 || StringFind(sym, "SOL") >= 0 ||
      StringFind(sym, "DOGE") >= 0 || StringFind(sym, "BNB") >= 0 ||
      StringFind(sym, "DOT") >= 0 || StringFind(sym, "AVAX") >= 0 ||
      StringFind(sym, "MATIC") >= 0 || StringFind(sym, "LINK") >= 0 ||
      StringFind(sym, "USDT") >= 0 || StringFind(sym, "USDC") >= 0)
      return ASSET_CRYPTO;
   
   //--- Precious metals
   if(StringFind(sym, "XAU") >= 0 || StringFind(sym, "GOLD") >= 0 ||
      StringFind(sym, "XAG") >= 0 || StringFind(sym, "SILVER") >= 0 ||
      StringFind(sym, "XPT") >= 0 || StringFind(sym, "XPD") >= 0)
      return ASSET_METAL;
   
   //--- Energy
   if(StringFind(sym, "WTI") >= 0 || StringFind(sym, "BRENT") >= 0 ||
      StringFind(sym, "CRUDE") >= 0 || StringFind(sym, "USOIL") >= 0 ||
      StringFind(sym, "UKOIL") >= 0 || StringFind(sym, "NGAS") >= 0 ||
      StringFind(sym, "XBRUSD") >= 0 || StringFind(sym, "XTIUSD") >= 0)
      return ASSET_ENERGY;
   
   //--- Indices
   if(StringFind(sym, "US30") >= 0 || StringFind(sym, "US500") >= 0 ||
      StringFind(sym, "US100") >= 0 || StringFind(sym, "NAS100") >= 0 ||
      StringFind(sym, "SPX") >= 0 || StringFind(sym, "NDX") >= 0 ||
      StringFind(sym, "DJI") >= 0 || StringFind(sym, "DAX") >= 0 ||
      StringFind(sym, "FTSE") >= 0 || StringFind(sym, "UK100") >= 0 ||
      StringFind(sym, "JP225") >= 0 || StringFind(sym, "NKY") >= 0 ||
      StringFind(sym, "DE30") >= 0 || StringFind(sym, "DE40") >= 0 ||
      StringFind(sym, "AUS200") >= 0 || StringFind(sym, "HK50") >= 0 ||
      StringFind(sym, "STOXX") >= 0 || StringFind(sym, "VIX") >= 0)
      return ASSET_INDEX;
   
   //--- Method 3: Check calculation mode for Forex
   long calcModeVal = 0;
   if(!SymbolInfoInteger(symbol, SYMBOL_TRADE_CALC_MODE, calcModeVal))
   {
      PrintFormat("[VWAP] WARN: SymbolInfoInteger(SYMBOL_TRADE_CALC_MODE) failed, error: %d", GetLastError());
   }
   ENUM_SYMBOL_CALC_MODE calcMode = (ENUM_SYMBOL_CALC_MODE)calcModeVal;
   
   if(calcMode == SYMBOL_CALC_MODE_FOREX || 
      calcMode == SYMBOL_CALC_MODE_FOREX_NO_LEVERAGE)
      return ASSET_FOREX;
   
   //--- Method 4: Standard forex pair length check (6 chars like EURUSD)
   if(StringLen(symbol) == 6 || StringLen(symbol) == 7)  // EURUSD or EURUSD.
   {
      string base = StringSubstr(sym, 0, 3);
      string quote = StringSubstr(sym, 3, 3);
      
      //--- Common currency codes (static to avoid reallocation)
      static const string currencies[] = {"EUR","USD","GBP","JPY","CHF","AUD","NZD","CAD",
                                           "SEK","NOK","DKK","SGD","HKD","MXN","ZAR","TRY",
                                           "PLN","CZK","HUF","CNH","CNY","INR","THB","KRW"};
      
      bool baseIsCurrency = false;
      bool quoteIsCurrency = false;
      
      for(int i = 0; i < ArraySize(currencies); i++)
      {
         if(base == currencies[i]) baseIsCurrency = true;
         if(quote == currencies[i]) quoteIsCurrency = true;
      }
      
      if(baseIsCurrency && quoteIsCurrency)
         return ASSET_FOREX;
   }
   
   //--- Method 5: Check for stock characteristics
   if(calcMode == SYMBOL_CALC_MODE_EXCH_STOCKS ||
      calcMode == SYMBOL_CALC_MODE_EXCH_STOCKS_MOEX)
      return ASSET_STOCK;
   
   return ASSET_UNKNOWN;
}

//+------------------------------------------------------------------+
//| Get asset class name for display                                  |
//+------------------------------------------------------------------+
string GetAssetClassName(const ENUM_ASSET_CLASS assetClass)
{
   switch(assetClass)
   {
      case ASSET_FOREX:   return "Forex";
      case ASSET_METAL:   return "Metal";
      case ASSET_CRYPTO:  return "Crypto";
      case ASSET_INDEX:   return "Index";
      case ASSET_STOCK:   return "Stock";
      case ASSET_ENERGY:  return "Energy";
      default:            return "Unknown";
   }
}

//+------------------------------------------------------------------+
//| Apply optimal settings based on asset class                       |
//+------------------------------------------------------------------+
void ApplyAssetSettings(const ENUM_ASSET_CLASS assetClass)
{
   //--- Determine effective reset period
   if(InpResetPeriod == RESET_AUTO)
   {
      switch(assetClass)
      {
         case ASSET_CRYPTO:
            g_effectiveReset = RESET_DAILY;  // UTC midnight for 24/7 markets
            break;
         case ASSET_FOREX:
         case ASSET_METAL:
         case ASSET_ENERGY:
            g_effectiveReset = RESET_FOREX_5PM;  // 5pm NY rollover (industry standard)
            break;
         case ASSET_INDEX:
         case ASSET_STOCK:
            g_effectiveReset = RESET_DAILY;  // Exchange open
            break;
         default:
            g_effectiveReset = RESET_DAILY;
      }
   }
   else
   {
      g_effectiveReset = (ENUM_VWAP_RESET)InpResetPeriod;
   }
   
   //--- Determine effective timezone
   if(InpTimezone == TZ_AUTO)
   {
      switch(assetClass)
      {
         case ASSET_CRYPTO:
            g_effectiveTZ = TZ_UTC;           // Standardized 24/7 cycle
            break;
         case ASSET_FOREX:
            g_effectiveTZ = TZ_NEW_YORK;      // 5pm NY rollover standard
            break;
         case ASSET_METAL:
            g_effectiveTZ = TZ_NEW_YORK;      // COMEX alignment
            break;
         case ASSET_ENERGY:
            g_effectiveTZ = TZ_NEW_YORK;      // NYMEX alignment
            break;
         case ASSET_INDEX:
            g_effectiveTZ = TZ_NEW_YORK;      // US indices dominant
            break;
         case ASSET_STOCK:
            g_effectiveTZ = TZ_NEW_YORK;      // NYSE/NASDAQ default
            break;
         default:
            g_effectiveTZ = TZ_SERVER;
      }
   }
   else
   {
      g_effectiveTZ = (ENUM_VWAP_TIMEZONE)InpTimezone;
   }
}

//+------------------------------------------------------------------+
//| Get day-of-week for any date (Zeller's congruence variant)        |
//| Returns: 0=Sunday, 1=Monday, ..., 6=Saturday                      |
//+------------------------------------------------------------------+
int GetDayOfWeek(int year, int month, int day)
{
   //--- Adjust for Jan/Feb (treat as months 13/14 of previous year)
   if(month < 3)
   {
      month += 12;
      year--;
   }
   
   int q = day;
   int m = month;
   int k = year % 100;
   int j = year / 100;
   
   //--- Zeller's formula for Gregorian calendar
   int h = (q + (13 * (m + 1)) / 5 + k + k / 4 + j / 4 - 2 * j) % 7;
   
   //--- Convert from Zeller (0=Sat) to standard (0=Sun)
   int dow = ((h + 6) % 7);
   return dow;
}

//+------------------------------------------------------------------+
//| Get the Nth occurrence of a weekday in a given month              |
//| n=1 for first, n=2 for second, n=-1 for last                      |
//+------------------------------------------------------------------+
int GetNthWeekdayOfMonth(int year, int month, int targetDow, int n)
{
   if(n > 0)
   {
      //--- Find first occurrence
      int firstDow = GetDayOfWeek(year, month, 1);
      int firstOccurrence = 1 + ((targetDow - firstDow + 7) % 7);
      
      //--- Add weeks to get nth occurrence
      return firstOccurrence + (n - 1) * 7;
   }
   else if(n == -1)
   {
      //--- Find last occurrence: start from last day of month (static to avoid reallocation)
      static const int daysInMonth[] = {0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31};
      int lastDay = daysInMonth[month];
      
      //--- Leap year check for February
      if(month == 2 && ((year % 4 == 0 && year % 100 != 0) || year % 400 == 0))
         lastDay = 29;
      
      int lastDow = GetDayOfWeek(year, month, lastDay);
      int daysBack = (lastDow - targetDow + 7) % 7;
      return lastDay - daysBack;
   }
   
   return 1;  // Fallback
}

//+------------------------------------------------------------------+
//| Check if DST is active for a given timezone                       |
//+------------------------------------------------------------------+
bool IsDSTActive(const datetime checkTime, const ENUM_VWAP_TIMEZONE tz)
{
   MqlDateTime dt;
   TimeToStruct(checkTime, dt);
   
   int year  = dt.year;
   int month = dt.mon;
   int day   = dt.day;
   int hour  = dt.hour;
   
   switch(tz)
   {
      case TZ_NEW_YORK:
      {
         //--- US DST: Second Sunday March 2:00 AM to First Sunday November 2:00 AM
         if(month < 3 || month > 11) return false;
         if(month > 3 && month < 11) return true;
         
         if(month == 3)
         {
            int secondSunday = GetNthWeekdayOfMonth(year, 3, 0, 2);  // 0=Sunday, 2nd occurrence
            if(day > secondSunday) return true;
            if(day == secondSunday && hour >= 2) return true;
            return false;
         }
         if(month == 11)
         {
            int firstSunday = GetNthWeekdayOfMonth(year, 11, 0, 1);  // 0=Sunday, 1st occurrence
            if(day < firstSunday) return true;
            if(day == firstSunday && hour < 2) return true;
            return false;
         }
         break;
      }
      
      case TZ_LONDON:
      {
         //--- UK DST: Last Sunday March 1:00 AM to Last Sunday October 2:00 AM
         if(month < 3 || month > 10) return false;
         if(month > 3 && month < 10) return true;
         
         if(month == 3)
         {
            int lastSunday = GetNthWeekdayOfMonth(year, 3, 0, -1);  // Last Sunday
            if(day > lastSunday) return true;
            if(day == lastSunday && hour >= 1) return true;
            return false;
         }
         if(month == 10)
         {
            int lastSunday = GetNthWeekdayOfMonth(year, 10, 0, -1);  // Last Sunday
            if(day < lastSunday) return true;
            if(day == lastSunday && hour < 2) return true;
            return false;
         }
         break;
      }
      
      case TZ_SYDNEY:
      {
         //--- Australia DST: First Sunday October 2:00 AM to First Sunday April 3:00 AM
         //--- (Southern hemisphere: summer = Oct-Apr)
         if(month >= 4 && month <= 9) return false;  // Apr-Sep: no DST
         
         //--- October: Check if past first Sunday
         if(month == 10)
         {
            int firstSunday = GetNthWeekdayOfMonth(year, 10, 0, 1);
            if(day > firstSunday) return true;
            if(day == firstSunday && hour >= 2) return true;
            return false;
         }
         
         //--- April: Check if before first Sunday
         if(month == 4)
         {
            int firstSunday = GetNthWeekdayOfMonth(year, 4, 0, 1);
            if(day < firstSunday) return true;
            if(day == firstSunday && hour < 3) return true;
            return false;
         }
         
         //--- Nov, Dec, Jan, Feb, Mar: DST is active
         return true;
      }
      
      case TZ_TOKYO:
         return false;  // Japan doesn't observe DST
         
      default:
         return false;
   }
   
   return false;
}

//+------------------------------------------------------------------+
//| Get timezone offset in seconds (DST-aware with caching)          |
//+------------------------------------------------------------------+
int GetTimezoneOffsetSeconds(const datetime checkTime)
{
   //--- Use cached offset if within cache interval (avoid repeated DST calculations)
   if(checkTime - g_lastDSTCheckTime < DST_CACHE_INTERVAL && g_lastDSTCheckTime > 0)
      return g_cachedTZOffset;
   
   //--- Update cache timestamp
   g_lastDSTCheckTime = checkTime;
   
   bool dst = IsDSTActive(checkTime, g_effectiveTZ);
   
   switch(g_effectiveTZ)
   {
      case TZ_UTC:      
         g_cachedTZOffset = 0;
         break;
      case TZ_NEW_YORK: 
         g_cachedTZOffset = dst ? -4 * 3600 : -5 * 3600;
         break;
      case TZ_LONDON:   
         g_cachedTZOffset = dst ? 1 * 3600 : 0;
         break;
      case TZ_TOKYO:    
         g_cachedTZOffset = 9 * 3600;
         break;
      case TZ_SYDNEY:   
         g_cachedTZOffset = dst ? 11 * 3600 : 10 * 3600;
         break;
      case TZ_CUSTOM:   
         g_cachedTZOffset = InpCustomOffsetHrs * 3600;
         break;
      case TZ_SERVER:
      default:          
         g_cachedTZOffset = 0;
         break;
   }
   
   return g_cachedTZOffset;
}

//+------------------------------------------------------------------+
//| Get effective server UTC offset (auto or manual)                  |
//+------------------------------------------------------------------+
int GetServerUTCOffset()
{
   //--- 99 means auto-detect
   if(InpServerUTCOffset == 99)
   {
      //--- Auto-detect: difference between server time and GMT
      return (int)(TimeCurrent() - TimeGMT());
   }
   //--- Manual override
   return InpServerUTCOffset * 3600;
}

//+------------------------------------------------------------------+
//| Convert server time to adjusted timezone                          |
//| Fixed: Now properly accounts for server UTC offset                 |
//+------------------------------------------------------------------+
datetime AdjustTimezone(const datetime serverTime)
{
   if(g_effectiveTZ == TZ_SERVER)
      return serverTime;
   
   //--- Get target timezone offset (where we want to measure time)
   int targetOffset = GetTimezoneOffsetSeconds(serverTime);
   
   //--- Get server offset (auto or manual)
   int serverOffset = GetServerUTCOffset();
   
   //--- Convert: serverTime is in server's local time
   //--- First convert to UTC: serverTime - serverOffset
   //--- Then convert to target: + targetOffset
   return serverTime - serverOffset + targetOffset;
}

//+------------------------------------------------------------------+
//| Detect if new session has started                                 |
//+------------------------------------------------------------------+
bool IsNewSession(const datetime &time[], const int idx, const int total)
{
   if(idx >= total - 1)
      return true;
   
   datetime adjCurrent = AdjustTimezone(time[idx]);
   datetime adjPrev    = AdjustTimezone(time[idx + 1]);
   
   MqlDateTime dtCurrent, dtPrev;
   TimeToStruct(adjCurrent, dtCurrent);
   TimeToStruct(adjPrev, dtPrev);
   
   switch(g_effectiveReset)
   {
      case RESET_DAILY:
         return (dtCurrent.day != dtPrev.day || 
                 dtCurrent.mon != dtPrev.mon ||
                 dtCurrent.year != dtPrev.year);
      
      case RESET_FOREX_5PM:
         //--- Forex standard: New session at 17:00 New York time
         //--- Use -17h shift so "day" boundary = 5pm instead of midnight
         {
            //--- Shift times by -17 hours to make 17:00 the "midnight"
            datetime shiftedCurrent = adjCurrent - 17 * 3600;
            datetime shiftedPrev = adjPrev - 17 * 3600;
            
            MqlDateTime scDt, spDt;
            TimeToStruct(shiftedCurrent, scDt);
            TimeToStruct(shiftedPrev, spDt);
            
            //--- Now compare days: this will trigger ONLY at 5pm, not at midnight
            return (scDt.day != spDt.day || scDt.mon != spDt.mon || scDt.year != spDt.year);
         }
                 
      case RESET_WEEKLY:
         //--- Fixed: Only trigger on Monday (1) from Sunday (0) or week boundary crossing
         return (dtCurrent.day_of_week == 1 && dtPrev.day_of_week == 0) ||
                (dtCurrent.day_of_week == 1 && dtPrev.day_of_week > 1);
         
      case RESET_MONTHLY:
         return (dtCurrent.mon != dtPrev.mon || dtCurrent.year != dtPrev.year);
         
      case RESET_NONE:
      default:
         return (idx == total - 1);
   }
}

//+------------------------------------------------------------------+
//| Find session start bar index                                      |
//+------------------------------------------------------------------+
int FindSessionStart(const datetime &time[], const int fromIdx, const int total)
{
   for(int i = fromIdx; i < total; i++)
   {
      if(IsNewSession(time, i, total))
         return i;
   }
   return total - 1;
}

//+------------------------------------------------------------------+
//| Calculate median volume for spike detection                       |
//+------------------------------------------------------------------+
double CalculateMedianVolume(const long &tick_volume[], const long &volume[],
                              const int start, const int count)
{
   if(count <= 0)
      return 1.0;
   
   double values[];
   ArrayResize(values, count);
   
   int validCount = 0;
   for(int i = start; i < start + count && i < ArraySize(tick_volume); i++)
   {
      double vol = (double)((volume[i] > 0) ? volume[i] : tick_volume[i]);
      if(vol > 0)
      {
         values[validCount++] = vol;
      }
   }
   
   if(validCount == 0)
      return 1.0;
   
   ArrayResize(values, validCount);
   ArraySort(values);
   
   return values[validCount / 2];
}

//+------------------------------------------------------------------+
//| Check if volume is a spike (bad tick)                             |
//+------------------------------------------------------------------+
bool IsVolumeSpike(const double vol, const double median)
{
   if(!InpFilterSpikes || median <= 0.0)
      return false;
   
   return (vol > median * InpSpikeThreshold);
}

//+------------------------------------------------------------------+
//| Calculate Standard Deviation                                      |
//+------------------------------------------------------------------+
double CalculateStdDev(const double cumPV, const double cumPV2, 
                       const double cumVol, const double vwap)
{
   if(cumVol <= 0.0)
      return 0.0;
   
   double variance = (cumPV2 / cumVol) - (vwap * vwap);
   return (variance > 0.0) ? MathSqrt(variance) : 0.0;
}

//+------------------------------------------------------------------+
//| Calculate VWAP value from cumulative stats                        |
//+------------------------------------------------------------------+
double CalculateVWAPValue(const double cumPV, const double cumVol, const double fallback)
{
   return (cumVol > 0.0) ? (cumPV / cumVol) : fallback;
}

//+------------------------------------------------------------------+
//| Update session statistics for bar 0                               |
//+------------------------------------------------------------------+
void UpdateSessionStatsOnBar0(const int i, const int sessionStartIdx, const double vwap, 
                              const datetime &time[], const int rates_total)
{
   if(i != 0) return;
   
   g_currentVwap = vwap;
   g_lastBarTime = time[0];
   
   //--- Calculate session bars count
   int startIdx = (sessionStartIdx >= 0) ? sessionStartIdx : FindSessionStart(time, 0, rates_total);
   g_sessionBarCount = startIdx + 1;
   g_sessionVolume = g_cumVol[0];
}

//+------------------------------------------------------------------+
//| Calculate and store deviation bands                               |
//+------------------------------------------------------------------+
void CalculateBandsValues(const int i, const double vwap, const double stdDev)
{
   if(!InpShowBands) return;
   
   g_upperBand1[i] = NormalizeDouble(vwap + (stdDev * InpBandMult1), _Digits);
   g_upperBand2[i] = NormalizeDouble(vwap + (stdDev * InpBandMult2), _Digits);
   g_lowerBand1[i] = NormalizeDouble(vwap - (stdDev * InpBandMult1), _Digits);
   g_lowerBand2[i] = NormalizeDouble(vwap - (stdDev * InpBandMult2), _Digits);
}

//+------------------------------------------------------------------+
//| Get adjusted volume with spike filtering                          |
//+------------------------------------------------------------------+
double GetAdjustedVolume(const int i, const long &tick_volume[], const long &volume[])
{
   double vol = (double)((volume[i] > 0) ? volume[i] : tick_volume[i]);
   if(vol <= 0.0) vol = 1.0;
   
   return IsVolumeSpike(vol, g_medianVolume) ? g_medianVolume : vol;
}

//+------------------------------------------------------------------+
//| Update cumulative pricing and volume statistics                   |
//+------------------------------------------------------------------+
void UpdateCumulativeStats(const int i, const bool isNewSession, const double typicalPrice, 
                           const double vol, const datetime barTime, const int rates_total, 
                           int &sessionStartIdx)
{
   if(isNewSession)
   {
      g_cumPV[i]  = typicalPrice * vol;
      g_cumVol[i] = vol;
      g_cumPV2[i] = typicalPrice * typicalPrice * vol;
      g_sessionStartTime = barTime;
      sessionStartIdx = i;
   }
   else
   {
      int prev = i + 1;
      if(prev < rates_total)
      {
         g_cumPV[i]  = g_cumPV[prev]  + (typicalPrice * vol);
         g_cumVol[i] = g_cumVol[prev] + vol;
         g_cumPV2[i] = g_cumPV2[prev] + (typicalPrice * typicalPrice * vol);
      }
      else
      {
         g_cumPV[i]  = typicalPrice * vol;
         g_cumVol[i] = vol;
         g_cumPV2[i] = typicalPrice * typicalPrice * vol;
      }
   }
}

//+------------------------------------------------------------------+
//| Update on-chart diagnostics panel                                 |
//+------------------------------------------------------------------+
void UpdateDiagnostics(const double currentPrice)
{
   if(!InpShowDiagnostics)
   {
      Comment("");
      return;
   }
   
   double distance = (g_currentVwap > 0) ? 
      ((currentPrice - g_currentVwap) / g_currentVwap) * 100.0 : 0.0;
   
   string distStr = (distance >= 0) ? 
      StringFormat("+%.2f%%", distance) : StringFormat("%.2f%%", distance);
   
   string tzName = EnumToString(g_effectiveTZ);
   StringReplace(tzName, "TZ_", "");
   
   string resetName = EnumToString(g_effectiveReset);
   StringReplace(resetName, "RESET_", "");
   
   string comment = StringFormat(
      "══════════ Adaptive VWAP v" + VERSION + "%s ══════════\n"
      "Asset: %s (%s)\n"
      "Session: %s | TZ: %s\n"
      "────────────────────────────────\n"
      "VWAP: %s\n"
      "Distance: %s\n"
      "Session Bars: %d\n"
      "Session Vol: %.0f\n"
      "Started: %s",
      g_cacheLoaded ? " (Cached)" : "",  // Show cached indicator
      _Symbol,
      g_assetClassName,
      resetName,
      tzName,
      DoubleToString(g_currentVwap, _Digits),
      distStr,
      g_sessionBarCount,
      g_sessionVolume,
      TimeToString(g_sessionStartTime, TIME_DATE | TIME_MINUTES)
   );
   
   Comment(comment);
}

//+------------------------------------------------------------------+
//| Custom indicator initialization                                   |
//+------------------------------------------------------------------+
int OnInit()
{
   //--- 1. Input Validation & Clamping (Safety First)
   if(InpBandMult1 <= 0.0 || InpBandMult2 <= 0.0)
   {
      PrintFormat("[VWAP] FATAL: Band multipliers must be positive. Current: %.2f, %.2f", InpBandMult1, InpBandMult2);
      return(INIT_PARAMETERS_INCORRECT);
   }
   
   //--- Clamp visually sensitive parameters to safe institutional ranges
   int safeWidth = (int)MathMax(1, MathMin(5, InpVwapWidth));
   int safeDepth = (int)MathMax(100, InpMaxRecalcBars);
   double safeThreshold = MathMax(1.1, InpSpikeThreshold);
   
   if(safeWidth != InpVwapWidth)
      PrintFormat("[VWAP] INFO: Width clamped from %d to %d for visual stability", InpVwapWidth, safeWidth);
   if(safeDepth != InpMaxRecalcBars)
      PrintFormat("[VWAP] INFO: MaxRecalcBars set to minimum safe value of 100");
   
   //--- 2. Detect & Apply Asset Strategy
   g_assetClass = DetectAssetClass(_Symbol);
   g_assetClassName = GetAssetClassName(g_assetClass);
   ApplyAssetSettings(g_assetClass);
   
   //--- 3. Buffer Registration
   SetIndexBuffer(0, g_vwapBuffer,  INDICATOR_DATA);
   
   ENUM_INDEXBUFFER_TYPE bandType = InpShowBands ? INDICATOR_DATA : INDICATOR_CALCULATIONS;
   SetIndexBuffer(1, g_upperBand1, bandType);
   SetIndexBuffer(2, g_upperBand2, bandType);
   SetIndexBuffer(3, g_lowerBand1, bandType);
   SetIndexBuffer(4, g_lowerBand2, bandType);
   
   SetIndexBuffer(5, g_cumPV,  INDICATOR_CALCULATIONS);
   SetIndexBuffer(6, g_cumVol, INDICATOR_CALCULATIONS);
   SetIndexBuffer(7, g_cumPV2, INDICATOR_CALCULATIONS);
   
   //--- Configure Buffers
   ArraySetAsSeries(g_vwapBuffer, true);
   ArraySetAsSeries(g_upperBand1, true);
   ArraySetAsSeries(g_upperBand2, true);
   ArraySetAsSeries(g_lowerBand1, true);
   ArraySetAsSeries(g_lowerBand2, true);
   ArraySetAsSeries(g_cumPV,  true);
   ArraySetAsSeries(g_cumVol, true);
   ArraySetAsSeries(g_cumPV2, true);
   
   //--- 4. Visual Configuration
   PlotIndexSetInteger(0, PLOT_LINE_COLOR, InpVwapColor);
   PlotIndexSetInteger(0, PLOT_LINE_WIDTH, safeWidth);
   PlotIndexSetDouble(0, PLOT_EMPTY_VALUE, EMPTY_VALUE);
   
   if(InpShowBands)
   {
      PlotIndexSetInteger(1, PLOT_DRAW_TYPE, DRAW_LINE);
      PlotIndexSetInteger(1, PLOT_LINE_COLOR, InpBand1Color);
      PlotIndexSetInteger(3, PLOT_DRAW_TYPE, DRAW_LINE);
      PlotIndexSetInteger(3, PLOT_LINE_COLOR, InpBand1Color);
      
      PlotIndexSetInteger(2, PLOT_DRAW_TYPE, DRAW_LINE);
      PlotIndexSetInteger(2, PLOT_LINE_COLOR, InpBand2Color);
      PlotIndexSetInteger(4, PLOT_DRAW_TYPE, DRAW_LINE);
      PlotIndexSetInteger(4, PLOT_LINE_COLOR, InpBand2Color);
   }
   else
   {
      for(int i = 1; i <= 4; i++)
         PlotIndexSetInteger(i, PLOT_DRAW_TYPE, DRAW_NONE);
   }
   
   for(int i = 1; i <= 4; i++)
      PlotIndexSetDouble(i, PLOT_EMPTY_VALUE, EMPTY_VALUE);
   
   //--- 5. Branding & Naming
   string tzName = EnumToString(g_effectiveTZ);
   StringReplace(tzName, "TZ_", "");
   
   IndicatorSetString(INDICATOR_SHORTNAME, 
      StringFormat("AV Institutional VWAP v" + VERSION + " (%s|%s)", g_assetClassName, tzName));
   IndicatorSetInteger(INDICATOR_DIGITS, _Digits);
   
   //--- 6. Load State
   if(InpEnableCache && LoadSessionState())
      PrintFormat("[VWAP] State resumed from disk");
   
   PrintFormat("[VWAP] Initialized | Asset: %s | Reset: %s | TZ: %s",
                g_assetClassName, EnumToString(g_effectiveReset), EnumToString(g_effectiveTZ));
   
   return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Custom indicator deinitialization                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   Comment("");
   
   //--- Handle cache on removal
   if(InpClearCacheOnRemove)
   {
      DeleteCacheFile();
   }
   else if(InpEnableCache)
   {
      SaveSessionState();
   }
}

//+------------------------------------------------------------------+
//| Main calculation function                                         |
//+------------------------------------------------------------------+
int OnCalculate(const int rates_total,
                const int prev_calculated,
                const datetime &time[],
                const double &open[],
                const double &high[],
                const double &low[],
                const double &close[],
                const long &tick_volume[],
                const long &volume[],
                const int &spread[])
{
   if(rates_total < 2)
      return 0;
   
   //--- Set arrays as series
   ArraySetAsSeries(time, true);
   ArraySetAsSeries(high, true);
   ArraySetAsSeries(low, true);
   ArraySetAsSeries(close, true);
   ArraySetAsSeries(tick_volume, true);
   ArraySetAsSeries(volume, true);
   
   //--- Calculate median volume for spike detection (sample last MEDIAN_SAMPLE_SIZE bars)
   if(prev_calculated <= 0 || g_medianVolume <= 0.0)
   {
      int sampleSize = MathMin(MEDIAN_SAMPLE_SIZE, rates_total);
      g_medianVolume = CalculateMedianVolume(tick_volume, volume, 0, sampleSize);
   }
   
   //--- Determine calculation range
   int limit;
   if(prev_calculated <= 0)
   {
      //--- Full recalculation: Fill the whole chart
      limit = rates_total - 1;
      
      //--- Apply historical depth limit if configured
      if(InpMaxRecalcBars > 0 && limit > InpMaxRecalcBars)
         limit = InpMaxRecalcBars;
         
      //--- Initialize all buffers to prevent visual artifacts
      ArrayInitialize(g_vwapBuffer, EMPTY_VALUE);
      ArrayInitialize(g_cumPV,  0.0);
      ArrayInitialize(g_cumVol, 0.0);
      ArrayInitialize(g_cumPV2, 0.0);
      
      if(InpShowBands)
      {
         ArrayInitialize(g_upperBand1, EMPTY_VALUE);
         ArrayInitialize(g_upperBand2, EMPTY_VALUE);
         ArrayInitialize(g_lowerBand1, EMPTY_VALUE);
         ArrayInitialize(g_lowerBand2, EMPTY_VALUE);
      }
      
      PrintFormat("[VWAP] Initializing: calculating %d bars", limit);
   }
   else
   {
      //--- Incremental update: usually just the new tick(s)
      limit = rates_total - prev_calculated;
   }
   
   //--- Track session start for correct statistics
   int currentSessionStartIdx = -1;
   
   //--- Main calculation loop
   for(int i = limit; i >= 0; i--)
   {
      double typicalPrice = (high[i] + low[i] + close[i]) / 3.0;
      double vol = GetAdjustedVolume(i, tick_volume, volume);
      
      bool isNewSession = IsNewSession(time, i, rates_total);
      
      //--- Update cumulative totals and session boundaries
      UpdateCumulativeStats(i, isNewSession, typicalPrice, vol, time[i], rates_total, currentSessionStartIdx);
      
      //--- Calculate VWAP
      double vwap = CalculateVWAPValue(g_cumPV[i], g_cumVol[i], typicalPrice);
      g_vwapBuffer[i] = NormalizeDouble(vwap, _Digits);
      
      //--- Update session metrics on bar 0
      UpdateSessionStatsOnBar0(i, currentSessionStartIdx, vwap, time, rates_total);
      
      //--- Deviation bands calculation
      if(InpShowBands)
      {
         double stdDev = CalculateStdDev(g_cumPV[i], g_cumPV2[i], g_cumVol[i], vwap);
         CalculateBandsValues(i, vwap, stdDev);
      }
   }
   
   //--- Update diagnostics panel
   UpdateDiagnostics(close[0]);
   
   return rates_total;
}
//+------------------------------------------------------------------+
