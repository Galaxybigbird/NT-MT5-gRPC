#property link      ""
#property version   "1.24"
#property strict
#property description "gRPC Hedge Receiver with MT5 hedge management"

//+------------------------------------------------------------------+
//| gRPC Connection Settings                                         |
//+------------------------------------------------------------------+
input group "===== gRPC Connection Settings =====";
input string BridgeServerAddress = "127.0.0.1";  // gRPC Server Address
input int    BridgeServerPort = 50051;            // gRPC Server Port

//+------------------------------------------------------------------+
//| Trading Settings                                                |
//+------------------------------------------------------------------+
input group "===== Trading Settings =====";
enum LOT_MODE { Fixed_Lot_Size = 0, LOTS_INVERSE_PNL = 2 };
input LOT_MODE LotSizingMode = LOTS_INVERSE_PNL;    // Lot sizing method

// Status overlay expects SELF_ELASTIC_MODE; self-elastic mode is removed in V2.
#define SELF_ELASTIC_MODE (-1)

input bool   EnableHedging = true;   // Enable hedging? (false = copy direction)
input double DefaultLot = 1.0;       // Default lot size if not specified
input int    Slippage = 200;         // Slippage
input int    MagicNumber = 12345;    // MagicNumber for trades

enum MARTINGALE_MODE { Martingale_Multiplier = 0, Martingale_Addition = 1 };
input group "===== Martingale Settings =====";
input MARTINGALE_MODE MartingaleMode = Martingale_Multiplier; // Fixed_Lot_Size only: use multiplier or addition for next lot
input double MartingaleValue = 1.5;  // Multiplier value or lot addition value for martingale mode

input double SimpleStopLoss_Points            = 4000;       // Static SL distance (points), 0 = off
input bool   AllowManualStopAdjustments       = true;     // Allow manual SL edits without snapback (simple SL mode)

input group "===== Inverse PnL Settings =====";
input double Tier1_Limit           = -400.0;  // Tier1_Limit (Safe Zone threshold)
input double Tier1_Lots            = 0.03;    // Tier1_Lots (Safe Zone lot size)
input double Tier2_Limit           = -800.0;  // Tier2_Limit (Loading Zone threshold)
input double Tier2_Lots            = 0.10;    // Tier2_Lots (Loading Zone lot size)
input double Tier3_MaxLots         = 0.20;    // Tier3_MaxLots (Kill Zone hard cap)
input double Safety_MaxMarginPct   = 90.0;    // Safety_MaxMarginPct (max % Free Margin in Tier 3)
input double Tier2_InitialSL_Points = 2500.0; // Tier 2 initial SL distance (points) for LOTS_INVERSE_PNL
input double Tier3_InitialSL_Points = 1100.0; // Tier 3 initial SL distance (points) for LOTS_INVERSE_PNL
input double Tier2_RunUp_PointDist = 250.0;   // Run-up distance override for Tier 2 (points)
input double Tier2_RunUp_PointStep = 25.0;    // Run-up step override for Tier 2 (points)
input double Tier3_RunUp_PointDist = 100.0;   // Run-up distance override for Tier 3 (points)
input double Tier3_RunUp_PointStep = 10.0;    // Run-up step override for Tier 3 (points)

input group "===== Tier 1 Fixed Trailing (Dollars) =====";
input bool   Tier1_DollarTrail_Enabled        = false; // Enable tier 1 trailing in account currency
input double Tier1_DollarTrail_ActivationUSD  = 0.0;   // Activation profit ($)
input double Tier1_DollarTrail_StepUSD        = 0.0;   // Trailing step ($)
input double Tier1_DollarTrail_ModificationUSD = 0.0;  // Profit clamp ($)

input group "===== Tier 2/3 Fixed Trailing (Points) =====";
input double Tier2_FixedTrail_ActivationPts    = 0.0; // Tier 2 trailing activation trigger (points in profit, 0 = off)
input double Tier2_FixedTrail_StepPts          = 0.0; // Tier 2 trailing step (points, 0 = off)
input double Tier2_FixedTrail_ModificationPts  = 0.0; // Tier 2 trailing stop modification (points, 0 = off)
input double Tier3_FixedTrail_ActivationPts    = 0.0; // Tier 3 trailing activation trigger (points in profit, 0 = off)
input double Tier3_FixedTrail_StepPts          = 0.0; // Tier 3 trailing step (points, 0 = off)
input double Tier3_FixedTrail_ModificationPts  = 0.0; // Tier 3 trailing stop modification (points, 0 = off)

//+------------------------------------------------------------------+
//| MT5 Hedge Trade Run-Up Inputs                                    |
//+------------------------------------------------------------------+
enum RUNUP_INCREMENT_MODE { RunUpIncrement_Points = 0, RunUpIncrement_DEMA_ATR = 1 };
input group "===== MT5 Hedge Trade Run-Up =====";
input bool   HedgeRunUp_Enabled             = true;   // Enable MT5 hedge trade run-up (keeps hedge open on NT loss)
input double HedgeRunUp_InitialDistancePts  = 450;    // Initial run-up stop distance (points)
input RUNUP_INCREMENT_MODE HedgeRunUp_IncrementMode = RunUpIncrement_Points; // Run-up increment type (Points or DEMA-ATR)
input double HedgeRunUp_IncrementPoints     = 100;    // Run-up increment step in points (when Points mode)
input int    HedgeRunUp_DemaPeriod          = 7;      // Run-up DEMA-ATR period (run-up only)
input double HedgeRunUp_DemaMultiplier      = 1.2;    // Run-up DEMA-ATR multiplier (run-up only)
input bool   HedgeRunUp_UseReactiveATR      = true;   // Use built-in reactive ATR values for DEMA-ATR run-up step

enum COUNTER_HEDGE_TRIGGER_MODE { CounterTrigger_Dollars = 0, CounterTrigger_Points = 1 };
input group "===== Counter-Hedge Protection =====";
input bool   CounterHedge_Enabled      = false; // Enable Counter-Hedge protection
input COUNTER_HEDGE_TRIGGER_MODE CounterHedge_InitialMode = CounterTrigger_Dollars; // First trigger measurement
input double CounterHedge_InitialValue = 20.0;  // First trigger drawdown threshold
input double CounterHedge_LotSize      = 0.01;  // Lot size for each Counter-Hedge trade
input COUNTER_HEDGE_TRIGGER_MODE CounterHedge_RepeatMode = CounterTrigger_Dollars; // Repeat trigger measurement
input double CounterHedge_RepeatStep   = 20.0;  // Additional drawdown per next Counter-Hedge

input group "--- General Settings ---";

input group "=====On-Chart Element Positions=====";
input int StatusLabelXPos_EA    = 200; // X distance for status label position
input int StatusLabelYPos_EA    = 50;  // Y distance for status label position

#include <gRPC/StatusIndicator_gRPC.mqh>
#include <gRPC/StatusOverlay_gRPC.mqh>
#include <gRPC/UnifiedLogging.mqh>

#include <Trade/Trade.mqh>
#include <Generic/HashMap.mqh>
#include <Strings/String.mqh>
#include <Trade/DealInfo.mqh>
#include <Trade/PositionInfo.mqh>

// Note: Do not redefine Print with macros; MQL5 preprocessor doesn't support variadic macros.
// Use ULogInfoPrint/ULogWarnPrint/ULogErrorPrint with StringConcatenate where needed.

CTrade trade;

const string CandleCountdownObjName = "ACHM_CandleCountdown";
const string MartingaleButtonObjName = "ACHM_MartingaleToggle";
datetime g_last_candle_countdown_update = 0;

struct HedgeRunUpState
{
    ulong   ticket;
    string  baseId;
    double  anchorPrice;
    double  initialDistancePts;
    double  incrementPoints;
    bool    useDemaAtr;
    bool    useReactiveAtr;
    double  lastStopPrice;
    datetime lastUpdate;
};

HedgeRunUpState g_runUpStates[];

struct TierFixedTrailState
{
    ulong   ticket;
    int     tier;
    double  anchorPrice;
    double  activationTriggerPts;
    double  stepPts;
    double  modificationPts;
    double  lastStopPrice;
    datetime lastUpdate;
};

TierFixedTrailState g_tierFixedTrailStates[];

struct Tier1DollarTrailState
{
    ulong   ticket;
    double  anchorPrice;
    double  activationUsd;
    double  stepUsd;
    double  modificationUsd;
    double  commissionUsd;
    double  lastStopPrice;
    datetime lastUpdate;
};

Tier1DollarTrailState g_tier1DollarTrailStates[];

// Error code constant for hedging-related errors
#define ERR_TRADE_NOT_ALLOWED           4756  // Trading is prohibited

// Function declarations for functions not in include files
bool IsTradingPermitted(string &reason); // Forward declaration for trading permission preflight
int  FindRunUpStateIndex(ulong ticket);
bool IsRunUpActiveForTicket(ulong ticket);
void RemoveRunUpState(ulong ticket);
void GetRunUpParameters(double &outDistancePts, double &outStepPts);
bool StartHedgeRunUpForBaseId(const string &baseId, const string &closureReason, ulong explicitTicket = 0);
bool UpdateRunUpTrailingForTicket(ulong ticket, ENUM_POSITION_TYPE posType, double currentPrice);
double ComputeRunUpIncrementPoints(bool useDemaAtr, bool useReactiveAtr, double pointIncrementPts);
double ComputeRunUpDemaAtrPrice(int period, double &outDemaAtr);
int  FindTierFixedTrailStateIndex(ulong ticket);
bool IsTierFixedTrailingActive(ulong ticket);
void RemoveTierFixedTrailState(ulong ticket);
void CleanupTierFixedTrailStates();
int  ResolveInverseTierForTicket(ulong ticket);
bool GetTierFixedTrailingSettings(int tier, double &activationPts, double &stepPts, double &modificationPts);
bool HandleTierFixedTrailingForPosition(ulong ticket, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice);
int  FindTier1DollarTrailStateIndex(ulong ticket);
void RemoveTier1DollarTrailState(ulong ticket);
void CleanupTier1DollarTrailStates();
bool GetTier1DollarTrailingSettings(double &activationUsd, double &stepUsd, double &modificationUsd);
double DollarsToPriceDistance(double dollars, double volume);
double PriceDistanceToDollars(double priceDistance, double volume);
double GetPositionCommissionTotal(ulong ticket);
bool HandleTier1DollarTrailingForPosition(ulong ticket, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double volume);
void UpdateCandleCountdown();
string GetMartingaleStateGlobalKey();
void LoadMartingaleToggleState();
void SaveMartingaleToggleState();
void EnsureMartingaleToggleButton();
void UpdateMartingaleToggleButton();
bool GetLastOpenedEaPositionLot(double &outLot);
double NormalizeLotDownToStep(double lot, double step);
double CalculateFixedLotSize();

// Map trade mode integer to readable string (MQL5 requires top-level, cannot nest functions)
string TradeModeName(const long mode)
{
    if(mode == SYMBOL_TRADE_MODE_DISABLED)   return "DISABLED";
    if(mode == SYMBOL_TRADE_MODE_CLOSEONLY)  return "CLOSEONLY";
    if(mode == SYMBOL_TRADE_MODE_FULL)       return "FULL";
    if(mode == SYMBOL_TRADE_MODE_LONGONLY)   return "LONGONLY";
    if(mode == SYMBOL_TRADE_MODE_SHORTONLY)  return "SHORTONLY";
    return StringFormat("UNKNOWN(%d)", (int)mode);
}
double AdjustLotForMargin(double desiredLot, ENUM_ORDER_TYPE orderType); // Downscale lot to fit free margin
int    DetermineInversePnlTier();               // Determine current inverse PnL tier
double CalculateInversePnLLot(ENUM_ORDER_TYPE orderType); // Inverse PnL tiered lot sizing
double CalculateLotSize(double ntQuantity, const string& baseId, const string& trade_json, ENUM_ORDER_TYPE orderType);
bool BaseIdMatchesTarget(const string &candidateBaseId, const string &targetBaseId);

// Forward declarations for JSON helpers used before their definitions
double GetJSONDouble(string json, string key);
double GetJSONDoubleValue(string json, string key, double defaultValue);
int    GetJSONIntValue(string json, string key, int defaultValue);
string GetJSONStringValue(string json, string key_with_quotes);

// Forward declarations for presence-aware NT performance updates
bool ParseNTPerformanceData(string json_str, double &nt_balance, double &nt_daily_pnl,
                           string &nt_trade_result, int &nt_session_trades,
                           bool &has_balance, bool &has_daily_pnl, bool &has_trade_result, bool &has_session_trades);
void UpdateNTPerformanceTrackingPartial(double nt_balance, double nt_daily_pnl,
                                string nt_trade_result, int nt_session_trades,
                                bool has_balance, bool has_daily_pnl,
                                bool has_trade_result, bool has_session_trades);

const string    CommentPrefix = "NT_Hedge_";  // Prefix for hedge order comments
const string    EA_COMMENT_PREFIX_BUY = CommentPrefix + "BUY_"; // Specific prefix for EA BUY hedges
const string    EA_COMMENT_PREFIX_SELL = CommentPrefix + "SELL_"; // Specific prefix for EA SELL hedges

const string    CounterCommentPrefix = "NT_CH_";
const string    COUNTER_COMMENT_PREFIX_BUY = CounterCommentPrefix + "BUY_";
const string    COUNTER_COMMENT_PREFIX_SELL = CounterCommentPrefix + "SELL_";

enum MANAGED_TRADE_KIND
{
    ManagedTrade_None = 0,
    ManagedTrade_PrimaryHedge = 1,
    ManagedTrade_CounterHedge = 2
};

bool IsPrimaryHedgeComment(const string &comment);
bool IsCounterHedgeComment(const string &comment);
MANAGED_TRADE_KIND GetManagedTradeKindFromComment(const string &comment);
bool TryExtractManagedBaseIdFromComment(const string &comment, string &outBaseId);
int CollectManagedTicketsForBaseId(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[]);
int CollectPrimaryHedgeTicketsForBaseId(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[]);
int FindCounterHedgeIndexByTicket(ulong counterTicket);
bool TryResolveCounterParentTicket(ulong counterTicket, ulong &outParentTicket);
bool TryResolveCounterBaseId(ulong counterTicket, string &outBaseId);
int CountLinkedCounterHedges(ulong parentTicket);
void RemoveCounterHedgeTracking(ulong counterTicket);
void CleanupCounterHedgeTracking();
bool CloseLinkedCounterHedges(ulong parentTicket, const string &baseId, const string &reason);
double GetCounterHedgeDrawdownValue(COUNTER_HEDGE_TRIGGER_MODE mode, double adversePriceDistance, double floatingProfit, double volume);
double GetCounterHedgeRepeatBaselineValue(double volume);
int GetCounterHedgeTargetCount(ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double floatingProfit, double volume);
bool RegisterCounterHedgePosition(ulong counterTicket, ulong parentTicket, const string &baseId, const string &action);
bool OpenCounterHedgeTrade(ulong parentTicket, const string &baseId, ENUM_POSITION_TYPE parentType);
bool EnsureCounterHedgeCoverage(ulong parentTicket, const string &baseId, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double floatingProfit, double volume);

//+------------------------------------------------------------------+
//| Pure C++ gRPC Client DLL Import                                 |
//+------------------------------------------------------------------+
// Unified logging GrpcLog import comes from Include\gRPC\UnifiedLogging.mqh

// Pure C++ client for initialization, health, streaming, and RPCs (renamed DLL to avoid collision with managed)
#import "MT5GrpcClientNative.dll"
    int TestFunction();
    // Core connection
    int GrpcInitialize(string server_address, int port);
    int GrpcShutdown();
    int GrpcIsConnected();
    int GrpcReconnect();

    int GrpcStartTradeStream();
    int GrpcStopTradeStream();
    int GrpcGetNextTrade(string &trade_json, int buffer_size);
    int GrpcGetTradeQueueSize();

    int GrpcSubmitTradeResult(string result_json);
    // Health check via native client (wide-char safe)
    int GrpcHealthCheck(string request_json, string &response_json, int buffer_size);
    int GrpcNotifyHedgeClose(string notification_json);
    int GrpcSubmitElasticUpdate(string update_json);
    int GrpcSubmitTrailingUpdate(string update_json);

    int GrpcGetConnectionStatus(string &status_json, int buffer_size);
    int GrpcGetStreamingStats(string &stats_json, int buffer_size);
    int GrpcGetLastError(string &error_message, int buffer_size);
#import

//+------------------------------------------------------------------+
//| Risk Management - Asymmetrical Compounding                       |
//+------------------------------------------------------------------+
// Global variable to track the aggregated net futures position from NT trades.
double globalFutures = 0.0;
string lastTradeTime = "";  // Track the last processed trade time
string lastTradeId = "";  // Track the last processed trade ID

// Track recently seen trade keys to avoid duplicate processing while allowing
// multiple hedges for the same base entry when contract_num differs.
// Key format: id[#contract_num] (e.g., "abc123#2" or just "abc123" if absent)
string   g_seen_trade_keys[];
datetime g_seen_trade_times[];

// Per-base occurrence tracking to disambiguate identical per-contract messages
// when upstream doesn't increment contract_num.
string   g_occ_base_ids[];
int      g_occ_counts[];         // how many hedges already processed for this base_id
datetime g_occ_updated[];        // last time this base_id was touched (for cleanup)

// Add a seen key with simple LRU trimming
void AddSeenTradeKey(const string &key)
{
    int n = ArraySize(g_seen_trade_keys);
    ArrayResize(g_seen_trade_keys, n + 1);
    ArrayResize(g_seen_trade_times, n + 1);
    g_seen_trade_keys[n] = key;
    g_seen_trade_times[n] = TimeCurrent();

    // Simple cap to keep memory in check
    const int MAX_KEYS = 200;
    const int TRIM_TO  = 140;
    if(n + 1 > MAX_KEYS)
    {
        // Shift last TRIM_TO items to front
        int keep = MathMin(TRIM_TO, ArraySize(g_seen_trade_keys));
        int start = ArraySize(g_seen_trade_keys) - keep;

        // Create temp copies of last segment
        string   tmpKeys[];
        datetime tmpTimes[];
        ArrayResize(tmpKeys, keep);
        ArrayResize(tmpTimes, keep);
        for(int i = 0; i < keep; i++)
        {
            tmpKeys[i]  = g_seen_trade_keys[start + i];
            tmpTimes[i] = g_seen_trade_times[start + i];
        }
        // Replace arrays with trimmed content
        ArrayResize(g_seen_trade_keys, keep);
        ArrayResize(g_seen_trade_times, keep);
        for(int i = 0; i < keep; i++)
        {
            g_seen_trade_keys[i]  = tmpKeys[i];
            g_seen_trade_times[i] = tmpTimes[i];
        }
    }
}

bool HasSeenTradeKey(const string &key)
{
    int n = ArraySize(g_seen_trade_keys);
    for(int i = n - 1; i >= 0; i--)
    {
        if(g_seen_trade_keys[i] == key)
            return true;
    }
    return false;
}

// Lookup index of base_id in occurrence arrays; returns -1 if not found
int FindBaseIdOccIndex(const string &baseId)
{
    for(int i = ArraySize(g_occ_base_ids) - 1; i >= 0; i--)
    {
        if(g_occ_base_ids[i] == baseId)
            return i;
    }
    return -1;
}

// Return next occurrence index we would assign for this baseId (without incrementing)
int PeekNextOccurrenceIndex(const string &baseId)
{
    int idx = FindBaseIdOccIndex(baseId);
    if(idx < 0) return 1; // first occurrence
    return g_occ_counts[idx] + 1;
}

// Increment occurrence counter for baseId (create if new) and return new value
int IncrementOccurrence(const string &baseId)
{
    int idx = FindBaseIdOccIndex(baseId);
    if(idx < 0)
    {
        int n = ArraySize(g_occ_base_ids);
        ArrayResize(g_occ_base_ids, n + 1);
        ArrayResize(g_occ_counts,   n + 1);
        ArrayResize(g_occ_updated,  n + 1);
        g_occ_base_ids[n] = baseId;
        g_occ_counts[n]   = 1;
        g_occ_updated[n]  = TimeCurrent();
        return 1;
    }
    g_occ_counts[idx] += 1;
    g_occ_updated[idx] = TimeCurrent();
    return g_occ_counts[idx];
}

// Periodically trim stale base_id occurrence entries to bound memory
void CleanupOldOccurrences(int maxAgeSec = 900)
{
    datetime now = TimeCurrent();
    for(int i = ArraySize(g_occ_base_ids) - 1; i >= 0; i--)
    {
        if(now - g_occ_updated[i] > maxAgeSec)
        {
            // compact arrays by shifting tail over i
            for(int j = i; j < ArraySize(g_occ_base_ids) - 1; j++)
            {
                g_occ_base_ids[j] = g_occ_base_ids[j+1];
                g_occ_counts[j]   = g_occ_counts[j+1];
                g_occ_updated[j]  = g_occ_updated[j+1];
            }
            ArrayResize(g_occ_base_ids, ArraySize(g_occ_base_ids) - 1);
            ArrayResize(g_occ_counts,   ArraySize(g_occ_counts) - 1);
            ArrayResize(g_occ_updated,  ArraySize(g_occ_updated) - 1);
        }
    }
}

// Add new struct for TP/SL measurements
struct TPSLMeasurement {
    string baseTradeId;
    string orderType;  // "TP" or "SL"
    int pips;
    double rawMeasurement;
};

// Add global variables for measurements
TPSLMeasurement lastTPSL;

// Dynamic high-water hedge state
double g_highWaterEOD = 0.0;  // highest *settled* balance
const  double CUSHION_BAND = 90.0;    // Trailing drawdown cushion (30% of $300 account)
double g_lastOHF      = 0.05; // last over-high-water hedge factor
double g_lastCushion  = 0.0;  // last calculated cushion for debugging

// Progressive hedging state for combine scenarios
double g_ntCumulativeLoss = 0.0;  // Track cumulative NT losses for progressive scaling
int g_ntLossStreak = 0;           // Count consecutive losing days
double g_lastNTBalance = 0.0;     // Track NT balance changes
double g_ntDailyPnL = 0.0;        // Current day's NT P&L
double g_NT_Daily_PnL = 0.0;      // Parsed nt_daily_pnl value for inverse sizing
string g_lastNTTradeResult = "";  // Last trade result: "win" or "loss"
int g_ntSessionTrades = 0;        // Number of trades in current session
datetime g_lastNTUpdateTime = 0;  // Last time NT data was updated
bool g_ntDataAvailable = false;   // Flag to indicate if NT data is available
bool g_hasNtDailyPnl = false;     // Flag to indicate if last message included nt_daily_pnl
int g_inversePnlTier = 1;         // Active tier for inverse PnL sizing
double g_inversePnlNextLot = 0.0; // Last computed lot for inverse PnL mode

// WHACK-A-MOLE FIX: State change tracking for overlay calculations
static datetime g_lastNTDataUpdate = 0;
static double g_lastNTBalanceForCalc = 0.0;
static double g_lastNTDailyPnLForCalc = 0.0;
static string g_lastNTResultForCalc = "";
static int g_lastNTSessionTradesForCalc = 0;

// Broker specification cache
struct BrokerSpecs {
    double tickSize;        // Minimum price change
    double tickValue;       // Dollar value per tick
    double pointValue;      // Dollar value per point
    double contractSize;    // Contract size
    double minLot;          // Minimum lot size
    double maxLot;          // Maximum lot size
    double lotStep;         // Lot step increment
    double marginRequired;  // Margin per lot
    bool   isValid;         // Whether specs have been loaded
} g_brokerSpecs;
    // Optional runtime hint from NT: NT price points corresponding to a $1,000 NT loss
    double g_ntPointsPer1kLoss = 0.0; // 0 means unset; if provided in trade JSON, we use it

// Race condition fix: Flag to indicate if broker specs are loaded and valid.
bool g_broker_specs_ready = false;


// gRPC connection state
bool grpc_connected = false;
bool grpc_streaming = false;
datetime grpc_last_connection_attempt = 0;
int grpc_connection_retry_interval = 5; // seconds
int grpc_max_retries = 3;
// Track parameter-change restarts to avoid unnecessary re-initialization
bool g_param_change_restart = false;
bool g_martingale_enabled = false;

// Instead of struct array, use separate arrays for each field
string g_baseIds[];           // Array of base trade IDs
int g_totalQuantities[];      // Array of total quantities
int g_processedQuantities[];  // Array of processed quantities
string g_actions[];           // Array of trade actions
bool g_isComplete[];          // Array of completion flags
string g_ntInstrumentSymbols[]; // Array of NT instrument symbols
string g_ntAccountNames[];    // Array of NT account names
int g_mt5HedgesOpenedCount[]; // Count of MT5 hedges opened for this group
int g_mt5HedgesClosedCount[]; // Count of MT5 hedges closed for this group
bool g_isMT5Opened[];         // Flag if MT5 hedge has been opened for this group
bool g_isMT5Closed[];         // Flag if all MT5 hedges for this group are closed

CHashMap<long, string> *g_map_position_id_to_base_id = NULL; // Map PositionID (long) to original base_id as plain string
CHashMap<long, int>    *g_simple_sl_tickets = NULL;         // Tickets where a simple SL (non-planner) was applied
CHashMap<long, double> *g_inverse_sl_locks = NULL;          // Locked SL price per ticket for inverse PnL hedges (prevents tier drift)
CHashMap<long, int>    *g_inverse_tier_locks = NULL;        // Locked inverse PnL tier at entry per ticket

// New parallel arrays for MT5 position details
long g_open_mt5_pos_ids[];       // Stores MT5 Position IDs
string g_open_mt5_base_ids[];    // Stores corresponding NT Base IDs
string g_open_mt5_nt_symbols[];  // Stores corresponding NT Instrument Symbols
string g_open_mt5_nt_accounts[]; // Stores corresponding NT Account Names
string g_open_mt5_actions[];     // Stores the MT5 position type ("buy" or "sell") for open positions
string g_open_mt5_original_nt_actions[];    // Stores original NT action for rehydrated open MT5 positions
double g_open_mt5_original_nt_quantities[]; // Stores original NT quantity for rehydrated open MT5 positions

long g_counter_hedge_pos_ids[];        // Open Counter-Hedge position tickets
long g_counter_hedge_parent_pos_ids[]; // Parent hedge ticket for each Counter-Hedge
string g_counter_hedge_base_ids[];     // Base ID linked to each Counter-Hedge
string g_counter_hedge_actions[];      // Counter-Hedge action (BUY/SELL)

// DUPLICATE NOTIFICATION PREVENTION: Track positions closed by NT to prevent duplicate notifications
long g_nt_closed_position_ids[];  // Stores position IDs that were closed by NT (to prevent duplicate notifications)
datetime g_nt_closed_timestamps[]; // Stores timestamps when positions were closed by NT (for cleanup)

// TRAILING STOP IGNORE: Track base IDs that have been closed to ignore subsequent trailing stop updates
string g_closed_base_ids[];       // Stores base IDs that have been closed (to ignore trailing stop updates)
datetime g_closed_base_timestamps[]; // Stores timestamps when base IDs were closed (for cleanup)

// Stop-loss helpers
double GetStopLossDistance();
double ExtractStopPriceFromDealComment(const string &dealComment);
double GetBrokerMinimumStopPoints();

// COMPREHENSIVE DUPLICATE PREVENTION: Track all notifications sent per base_id to prevent multiple notifications
string g_notified_base_ids[];     // Stores base_ids that have already been notified
datetime g_notified_timestamps[]; // Stores timestamps when notifications were sent (for cleanup)

// Mutex-like mechanism to prevent concurrent array modifications
bool g_array_modification_in_progress = false;
datetime g_last_array_modification_time = 0;
const int ARRAY_MODIFICATION_TIMEOUT_SECONDS = 30; // Maximum time to wait for array modification to complete

// Query and cache broker specifications for current symbol
bool QueryBrokerSpecs()
{
    g_brokerSpecs.tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
    g_brokerSpecs.tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
    g_brokerSpecs.contractSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_CONTRACT_SIZE);
    g_brokerSpecs.minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    g_brokerSpecs.maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    g_brokerSpecs.lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    g_brokerSpecs.marginRequired = SymbolInfoDouble(_Symbol, SYMBOL_MARGIN_INITIAL);

    // Calculate point value (dollar value per point movement)
    double point = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
    if(point > 0 && g_brokerSpecs.tickSize > 0)
    {
        // Point value = (tick value / tick size) * point size
        g_brokerSpecs.pointValue = (g_brokerSpecs.tickValue / g_brokerSpecs.tickSize) * point;
    }
    else
    {
        g_brokerSpecs.pointValue = 0.0;
    }

    // CRITICAL FIX: Handle zero margin requirement with realistic fallback
    if(g_brokerSpecs.marginRequired <= 0)
    {
        // Calculate realistic margin based on current price and leverage
        double currentPrice = SymbolInfoDouble(_Symbol, SYMBOL_BID);
        long leverageLong = AccountInfoInteger(ACCOUNT_LEVERAGE);
        double leverage = (double)leverageLong; // Explicit cast to avoid warning

        if(currentPrice > 0 && leverage > 0)
        {
            // For NAS100: Contract size * Current price / Leverage
                        g_brokerSpecs.marginRequired = (g_brokerSpecs.contractSize * currentPrice) / leverage;
                        string __log = StringFormat(
                            "BROKER_SPECS_FIX: Calculated margin requirement: $%.2f per lot (Price: %.5f, Leverage: 1:%d)",
                            (double)g_brokerSpecs.marginRequired,
                            (double)currentPrice,
                            (int)leverageLong
                        );
                        Print(__log); ULogInfoPrint(__log);
        }
        else
        {
            // Ultimate fallback for $300 account safety
                        g_brokerSpecs.marginRequired = 50.0; // Conservative $50 per lot
                        string __log2 = StringFormat(
                            "BROKER_SPECS_FALLBACK: Using conservative margin requirement: $%.2f per lot",
                            (double)g_brokerSpecs.marginRequired
                        );
                        Print(__log2); ULogInfoPrint(__log2);
        }
    }

    // Validate that we got reasonable values
    g_brokerSpecs.isValid = (g_brokerSpecs.tickSize > 0 &&
                            g_brokerSpecs.tickValue > 0 &&
                            g_brokerSpecs.contractSize > 0 &&
                            g_brokerSpecs.minLot > 0 &&
                            g_brokerSpecs.maxLot > 0 &&
                            g_brokerSpecs.lotStep > 0 &&
                            g_brokerSpecs.marginRequired > 0); // Added margin validation

    if(g_brokerSpecs.isValid)
    {
        g_broker_specs_ready = true; // Specs are valid
        ULogInfoPrint("BROKER_SPECS: Successfully queried specifications for " + _Symbol);
        ULogInfoPrint("  Tick Size: " + DoubleToString(g_brokerSpecs.tickSize, 10));
        ULogInfoPrint("  Tick Value: $" + DoubleToString(g_brokerSpecs.tickValue, 2));
        ULogInfoPrint("  Point Value: $" + DoubleToString(g_brokerSpecs.pointValue, 6) + " per point per lot");
        ULogInfoPrint("  Contract Size: " + DoubleToString(g_brokerSpecs.contractSize, 2));
        ULogInfoPrint("  Min Lot: " + DoubleToString(g_brokerSpecs.minLot, 2));
        ULogInfoPrint("  Max Lot: " + DoubleToString(g_brokerSpecs.maxLot, 2));
        ULogInfoPrint("  Lot Step: " + DoubleToString(g_brokerSpecs.lotStep, 2));
        ULogInfoPrint("  Margin Required: $" + DoubleToString(g_brokerSpecs.marginRequired, 2) + " per lot");

        // Additional safety check for $300 account
        double accountBalance = AccountInfoDouble(ACCOUNT_BALANCE);
        double maxSafeLots = (accountBalance * 0.50) / g_brokerSpecs.marginRequired; // 50% max usage
    ULogInfoPrint("  SAFETY: For $" + DoubleToString(accountBalance, 2) + " account, max safe lots: " + DoubleToString(maxSafeLots, 2) + " (50% margin usage)");
    }
    else
    {
        g_broker_specs_ready = false; // Specs are invalid
    ULogWarnPrint("BROKER_SPECS_ERROR: Failed to query valid specifications for " + _Symbol);
    ULogWarnPrint("  Tick Size: " + DoubleToString(g_brokerSpecs.tickSize, 10));
    ULogWarnPrint("  Tick Value: " + DoubleToString(g_brokerSpecs.tickValue, 6));
    ULogWarnPrint("  Contract Size: " + DoubleToString(g_brokerSpecs.contractSize, 2));
    ULogWarnPrint("  Min/Max/Step Lot: " + DoubleToString(g_brokerSpecs.minLot, 2) + "/" + DoubleToString(g_brokerSpecs.maxLot, 2) + "/" + DoubleToString(g_brokerSpecs.lotStep, 2));
    ULogWarnPrint("  Margin Required: " + DoubleToString(g_brokerSpecs.marginRequired, 2));
    }

    return g_brokerSpecs.isValid;
}

// Helper functions for parsing NT performance data from JSON
bool ParseJSONDouble(string json_str, string key, double &value)
{
    // GetJSONDouble() returns 0.0 when the key is missing, so use key presence as the indicator.
    // This avoids false "missing" when the real value is 0 (e.g., sim account reset -> nt_daily_pnl=0).
    if(StringFind(json_str, "\"" + key + "\"") < 0)
    {
        value = 0.0;
        return false;
    }

    value = GetJSONDouble(json_str, key);
    return true;
}

bool ParseJSONString(string json_str, string key, string &value)
{
    value = GetJSONStringValue(json_str, "\"" + key + "\"");
    return (value != "");
}

bool ParseJSONInt(string json_str, string key, int &value)
{
    value = GetJSONIntValue(json_str, key, -999999); // Use unlikely default
    return (value != -999999);
}

// Parse NT performance data from enhanced JSON messages
// Parse NT performance data from enhanced JSON messages.
// Returns booleans indicating which fields were present so callers can avoid
// overwriting cached state with default zeros when keys are omitted.
bool ParseNTPerformanceData(string json_str, double &nt_balance, double &nt_daily_pnl,
                           string &nt_trade_result, int &nt_session_trades,
                           bool &has_balance, bool &has_daily_pnl, bool &has_trade_result, bool &has_session_trades)
{
    has_balance = ParseJSONDouble(json_str, "nt_balance", nt_balance);
    has_daily_pnl = ParseJSONDouble(json_str, "nt_daily_pnl", nt_daily_pnl);
    has_trade_result = ParseJSONString(json_str, "nt_trade_result", nt_trade_result);
    has_session_trades = ParseJSONInt(json_str, "nt_session_trades", nt_session_trades);

    if(!has_balance) { ULogWarnPrint("NT_PARSE_WARNING: nt_balance not found in JSON (no change)"); }
    if(!has_daily_pnl) { ULogWarnPrint("NT_PARSE_WARNING: nt_daily_pnl not found in JSON (no change)"); }
    if(!has_trade_result) { ULogWarnPrint("NT_PARSE_WARNING: nt_trade_result not found in JSON (no change)"); }
    if(!has_session_trades) { ULogWarnPrint("NT_PARSE_WARNING: nt_session_trades not found in JSON (no change)"); }

    return true;
}

void ApplySimpleStopLossIfNeeded(ulong positionTicket,
                                 ENUM_POSITION_TYPE posType,
                                 double entryPrice,
                                 bool forceAlignToEntry = false)
{
    if(SimpleStopLoss_Points <= 0.0)
    {
        { string __log="SIMPLE_SL_SKIP: SimpleStopLoss_Points <= 0; simple SL not applied."; Print(__log); ULogInfoPrint(__log); }
        return;
    }
    if(positionTicket == 0 || entryPrice <= 0.0)
    {
        { string __log=""; StringConcatenate(__log, "SIMPLE_SL_SKIP: Invalid ticket/entry (ticket=", (long)positionTicket, " entry=", DoubleToString(entryPrice, _Digits), ")"); Print(__log); ULogWarnPrint(__log); }
        return;
    }

    bool inverseMode = (LotSizingMode == LOTS_INVERSE_PNL);

    // If run-up is active, do not interfere with its trailing.
    if(inverseMode && IsRunUpActiveForTicket(positionTicket))
        return;
    if(inverseMode && IsTierFixedTrailingActive(positionTicket))
        return;
    if(inverseMode && FindTier1DollarTrailStateIndex(positionTicket) >= 0)
        return;

    double brokerMinPts = GetBrokerMinimumStopPoints();
    double distPts = 0.0;
    double slPrice = 0.0;

    if(inverseMode && g_inverse_sl_locks != NULL)
    {
        // Use locked SL if already computed for this ticket to prevent tier-based drift.
        if(g_inverse_sl_locks.TryGetValue((long)positionTicket, slPrice))
        {
            distPts = MathMax(MathAbs(entryPrice - slPrice) / _Point, brokerMinPts);
        }
    }

    if(distPts <= 0.0 || slPrice <= 0.0)
    {
        // First-time calculation: lock the SL at current tier distance, then never change it unless run-up moves it.
        double dist = GetStopLossDistance(); // tier-aware at entry time
        if(dist <= 0.0)
        {
            { string __log="SIMPLE_SL_SKIP: Stop distance unavailable; not applying SL."; Print(__log); ULogWarnPrint(__log); }
            return;
        }
        distPts = dist / _Point;
        if(distPts < brokerMinPts)
            distPts = brokerMinPts;
        dist = distPts * _Point;
        slPrice = (posType == POSITION_TYPE_BUY)
            ? entryPrice - dist
            : entryPrice + dist;

        if(g_inverse_sl_locks != NULL)
            g_inverse_sl_locks.Add((long)positionTicket, slPrice);
    }

    if(!PositionSelectByTicket(positionTicket))
    {
        { string __log=""; StringConcatenate(__log, "SIMPLE_SL_SKIP: PositionSelectByTicket failed for ", (long)positionTicket); Print(__log); ULogWarnPrint(__log); }
        return;
    }

    double existingSL = PositionGetDouble(POSITION_SL);

    // In inverse-PnL mode, keep the SL locked at the entry-time tier distance unless run-up or fixed trailing is active.
    if(inverseMode && existingSL > 0.0)
    {
        if(AllowManualStopAdjustments && slPrice > 0.0 && MathAbs(existingSL - slPrice) > _Point * 5.0)
        {
            if(g_inverse_sl_locks != NULL)
            {
                g_inverse_sl_locks.Remove((long)positionTicket);
                g_inverse_sl_locks.Add((long)positionTicket, existingSL);
            }
            if(g_simple_sl_tickets != NULL)
            {
                g_simple_sl_tickets.Remove((long)positionTicket);
                g_simple_sl_tickets.Add((long)positionTicket, 1);
            }
            { string __log=""; StringConcatenate(__log,
                "SIMPLE_SL_MANUAL: honoring manual SL for ticket ", (long)positionTicket,
                " locked=", DoubleToString(existingSL, _Digits)); Print(__log); ULogInfoPrint(__log); }
            return;
        }

        if(g_inverse_sl_locks != NULL)
        {
            double _tmp = 0.0;
            if(!g_inverse_sl_locks.TryGetValue((long)positionTicket, _tmp))
                g_inverse_sl_locks.Add((long)positionTicket, existingSL);
        }

        // If another subsystem tightened/moved the stop, restore it back to the locked simple SL.
        // Use a small tolerance to avoid thrashing due to broker rounding/normalization.
        if(slPrice > 0.0 && MathAbs(existingSL - slPrice) > _Point * 5.0)
        {
            trade.SetExpertMagicNumber(MagicNumber);
            trade.SetDeviationInPoints(Slippage);
            bool modified = trade.PositionModify(positionTicket, slPrice, 0.0);
            { string __log=""; StringConcatenate(__log,
                "SIMPLE_SL_RESTORE: ticket=", (long)positionTicket,
                " from=", DoubleToString(existingSL, _Digits),
                " to=", DoubleToString(slPrice, _Digits),
                " ok=", (int)modified,
                " retcode=", trade.ResultRetcode(),
                " comment=", trade.ResultComment());
              Print(__log); ULogInfoPrint(__log); }

            if(modified && g_simple_sl_tickets != NULL)
            {
                g_simple_sl_tickets.Remove((long)positionTicket);
                g_simple_sl_tickets.Add((long)positionTicket, 1);
            }
        }
        return;
    }

    if(!inverseMode && !forceAlignToEntry && AllowManualStopAdjustments && existingSL > 0.0 && slPrice > 0.0 &&
       MathAbs(existingSL - slPrice) > _Point * 5.0)
    {
        return;
    }

    // If already set and roughly same, skip
    if(existingSL > 0.0 && MathAbs(existingSL - slPrice) <= _Point * 0.5)
    {
        if(g_simple_sl_tickets != NULL)
        {
            g_simple_sl_tickets.Remove((long)positionTicket);
            g_simple_sl_tickets.Add((long)positionTicket, 1);
        }
        return;
    }

    if(forceAlignToEntry && existingSL > 0.0 && slPrice > 0.0 &&
       MathAbs(existingSL - slPrice) > _Point * 5.0)
    {
        { string __log=""; StringConcatenate(__log,
            "SIMPLE_SL_REALIGN: ticket=", (long)positionTicket,
            " existing=", DoubleToString(existingSL, _Digits),
            " desired=", DoubleToString(slPrice, _Digits),
            " entry=", DoubleToString(entryPrice, _Digits));
          Print(__log); ULogInfoPrint(__log); }
    }

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    bool modified = trade.PositionModify(positionTicket, slPrice, 0.0);
    { string __log=""; StringConcatenate(__log,
        "SIMPLE_SL_SET: ticket=", (long)positionTicket,
        " sl=", DoubleToString(slPrice, _Digits),
        " distPts=", DoubleToString(distPts, 2),
        " brokerMinPts=", DoubleToString(brokerMinPts, 2),
        " ok=", (int)modified,
        " retcode=", trade.ResultRetcode(),
        " comment=", trade.ResultComment());
      Print(__log); ULogInfoPrint(__log); }
    if(modified && g_simple_sl_tickets != NULL)
    {
        g_simple_sl_tickets.Remove((long)positionTicket);
        g_simple_sl_tickets.Add((long)positionTicket, 1);
    }
}

// Update NT performance tracking variables
// Partial update variant: only fields marked present are applied to state.
void UpdateNTPerformanceTrackingPartial(double nt_balance, double nt_daily_pnl,
                                string nt_trade_result, int nt_session_trades,
                                bool has_balance, bool has_daily_pnl,
                                bool has_trade_result, bool has_session_trades)
{
    // WHACK-A-MOLE FIX: Check if NT data has actually changed
    bool nt_data_changed = false;

    // Use current state as baseline
    double new_balance = g_lastNTBalance;
    double new_pnl = g_ntDailyPnL;
    string new_result = g_lastNTTradeResult;
    int new_trades = g_ntSessionTrades;

    if(has_balance) new_balance = nt_balance;
    if(has_daily_pnl) new_pnl = nt_daily_pnl;
    if(has_trade_result) new_result = nt_trade_result;
    if(has_session_trades) new_trades = nt_session_trades;

    if((has_balance && MathAbs(new_balance - g_lastNTBalanceForCalc) > 0.01) ||
       (has_daily_pnl && MathAbs(new_pnl - g_lastNTDailyPnLForCalc) > 0.01) ||
       (has_trade_result && new_result != g_lastNTResultForCalc) ||
       (has_session_trades && new_trades != g_lastNTSessionTradesForCalc) ||
       !g_ntDataAvailable) // First time data becomes available
    {
        nt_data_changed = true;
        if(has_balance) g_lastNTBalanceForCalc = new_balance;
        if(has_daily_pnl) g_lastNTDailyPnLForCalc = new_pnl;
        if(has_trade_result) g_lastNTResultForCalc = new_result;
        if(has_session_trades) g_lastNTSessionTradesForCalc = new_trades;
        g_lastNTDataUpdate = TimeCurrent();
    }

    // Update global tracking variables
    double previous_balance = g_lastNTBalance;
    double previous_pnl = g_ntDailyPnL;
    if(has_balance) g_lastNTBalance = new_balance;
    if(has_daily_pnl) g_ntDailyPnL = new_pnl;
    if(has_trade_result) g_lastNTTradeResult = new_result;
    if(has_session_trades) g_ntSessionTrades = new_trades;
    if(has_daily_pnl) {
        g_NT_Daily_PnL = new_pnl;
        g_hasNtDailyPnl = true;
        g_inversePnlTier = DetermineInversePnlTierFromValue(g_NT_Daily_PnL); // keep tier in sync with latest PnL
    } else if(g_ntDataAvailable && g_NT_Daily_PnL != 0.0) {
        // Preserve previously parsed PnL so inverse-tier logic doesn't fall back to Tier 1
        g_hasNtDailyPnl = true;
    }
    g_lastNTUpdateTime = TimeCurrent();
    g_ntDataAvailable = g_ntDataAvailable || has_balance || has_daily_pnl || has_trade_result || has_session_trades;

    // Update loss streak tracking
    if(has_trade_result && nt_trade_result == "loss") {
        g_ntLossStreak++;
        if(has_daily_pnl && nt_daily_pnl < 0) {
            g_ntCumulativeLoss += MathAbs(nt_daily_pnl);
        }
    } else if(has_trade_result && nt_trade_result == "win") {
        g_ntLossStreak = 0; // Reset loss streak on win
    }

    // Only print and force recalculation if data actually changed
    if(nt_data_changed) {
        { string __log=""; StringConcatenate(__log,
              "NT_PERFORMANCE_UPDATE: Balance: $", nt_balance,
              ", Daily P&L: $", nt_daily_pnl,
              ", Trade Result: ", nt_trade_result,
              ", Session Trades: ", nt_session_trades,
              ", Loss Streak: ", g_ntLossStreak,
              ", Cumulative Loss: $", g_ntCumulativeLoss);
          Print(__log); ULogInfoPrint(__log); }

        // WHACK-A-MOLE FIX: Update overlay directly when NT data actually changes
        UpdateStatusOverlay();
    }
}

// Calculate progressive hedging target based on NT performance scenarios
double CalculateProgressiveHedgingTarget()
{
    // Default conservative target if no NT data available
    if(!g_ntDataAvailable) {
        { string __log="PROGRESSIVE_HEDGING: No NT data available, using default $60 target"; Print(__log); ULogInfoPrint(__log); }
        return 60.0;
    }

    double targetProfit = 60.0;  // Base target for first loss

    // Progressive hedging logic based on NT performance:
    if(g_ntLossStreak == 0) {
        // No current loss streak - use minimal hedging
    targetProfit = 30.0;
    { string __log=""; StringConcatenate(__log, "PROGRESSIVE_HEDGING: No loss streak - Minimal hedging target: $", targetProfit); Print(__log); ULogInfoPrint(__log); }
    }
    else if(g_ntLossStreak == 1) {
        // First loss - Day 1 scenario: Target $50-70 to break even
    targetProfit = 60.0;
    { string __log=""; StringConcatenate(__log, "PROGRESSIVE_HEDGING: First loss (Day 1) - Standard target: $", targetProfit); Print(__log); ULogInfoPrint(__log); }
    }
    else if(g_ntLossStreak >= 2) {
        // Multiple losses - Day 2+ scenario: Scale up to cover multiple combines
        if(g_lastNTTradeResult == "loss") {
            // Day 2+ Loss: Target $200+ to cover both combines
            targetProfit = 200.0 + (g_ntLossStreak - 2) * 50.0; // Scale up for additional losses
            { string __log=""; StringConcatenate(__log, "PROGRESSIVE_HEDGING: Multiple losses (Day ", g_ntLossStreak, ") - Scaled target: $", targetProfit); Print(__log); ULogInfoPrint(__log); }
        } else {
            // Day 2+ Win after losses: Reduce target to minimize MT5 loss
            targetProfit = 80.0; // Reduced target when NT wins after losses
            { string __log=""; StringConcatenate(__log, "PROGRESSIVE_HEDGING: Win after losses - Reduced target: $", targetProfit); Print(__log); ULogInfoPrint(__log); }
        }
    }

    // Additional scaling based on cumulative losses
    if(g_ntCumulativeLoss > 500.0) {
    targetProfit *= 1.5; // Increase target by 50% for significant cumulative losses
    { string __log=""; StringConcatenate(__log, "PROGRESSIVE_HEDGING: High cumulative loss ($", g_ntCumulativeLoss, ") - Adjusted target: $", targetProfit); Print(__log); ULogInfoPrint(__log); }
    }

    { string __log=""; StringConcatenate(__log,
          "PROGRESSIVE_HEDGING: Final target: $", targetProfit,
          " (Loss Streak: ", g_ntLossStreak,
          ", Last Result: ", g_lastNTTradeResult,
          ", Daily P&L: $", g_ntDailyPnL, ")");
      Print(__log); ULogInfoPrint(__log); }

    return targetProfit;
}

// Calculate lot size needed to achieve target profit in USD
double CalculateLotForTargetProfit(double targetProfitUSD, double expectedPointMove)
{
    if(!g_brokerSpecs.isValid)
    {
    { string __log="ELASTIC_ERROR: Broker specs not loaded. Cannot calculate lot for target profit."; Print(__log); ULogErrorPrint(__log); }
        return g_brokerSpecs.minLot;
    }

    if(g_brokerSpecs.pointValue <= 0 || expectedPointMove <= 0)
    {
        { string __log=""; StringConcatenate(__log,
              "ELASTIC_ERROR: Invalid point value ($", g_brokerSpecs.pointValue,
              ") or expected move (", expectedPointMove, " points)");
          Print(__log); ULogErrorPrint(__log); }
        return g_brokerSpecs.minLot;
    }

    // Required lot = Target Profit / (Point Value * Expected Point Move)
    double requiredLot = targetProfitUSD / (g_brokerSpecs.pointValue * expectedPointMove);

    // Apply broker constraints
    requiredLot = MathMax(requiredLot, g_brokerSpecs.minLot);
    requiredLot = MathMin(requiredLot, g_brokerSpecs.maxLot);
    requiredLot = MathFloor(requiredLot / g_brokerSpecs.lotStep) * g_brokerSpecs.lotStep;

    { string __log=""; StringConcatenate(__log,
          "ELASTIC_CALC: Target profit $", targetProfitUSD,
          ", Expected move ", expectedPointMove, " points",
          ", Point value $", g_brokerSpecs.pointValue, "/point/lot",
          " -> Required lot: ", requiredLot);
      Print(__log); ULogInfoPrint(__log); }

    return requiredLot;
}

// Function to find or create trade group
int FindOrCreateTradeGroup(string baseId, int totalQty, string action)
{
    // First try to find an existing group with this base ID
    // Handle both full match (legacy) and partial match (new format due to MT5 comment length limit)
    int arraySize = ArraySize(g_baseIds);
    for(int i = 0; i < arraySize; i++)
    {
        bool isMatch = false;
        if(g_baseIds[i] == baseId && !g_isComplete[i]) {
            // Full match (legacy format)
            isMatch = true;
        } else if(StringLen(g_baseIds[i]) >= 16 && StringLen(baseId) >= 16 && !g_isComplete[i]) {
            // Partial match - compare first 16 characters (new format)
            string shortStoredBaseId = StringSubstr(g_baseIds[i], 0, 16);
            string shortBaseId = StringSubstr(baseId, 0, 16);
            if(shortStoredBaseId == shortBaseId) {
                isMatch = true;
                Print("DEBUG: FindOrCreateTradeGroup - Matched using partial base_id. Stored: '", shortStoredBaseId, "' (from full: '", g_baseIds[i], "'), Input: '", shortBaseId, "' (from full: '", baseId, "')");
            }
        }

        if(isMatch) {
            // Found existing group - don't update global futures position again
            Print("DEBUG: Found existing trade group at index ", i, " for base ID: ", baseId);
            return i;
        }
    }

    // Create new group if not found
    int newIndex = arraySize;
    ArrayResize(g_baseIds, newIndex + 1);
    ArrayResize(g_totalQuantities, newIndex + 1);
    ArrayResize(g_processedQuantities, newIndex + 1);
    ArrayResize(g_actions, newIndex + 1);
    ArrayResize(g_isComplete, newIndex + 1);

    g_baseIds[newIndex] = baseId;
    g_totalQuantities[newIndex] = totalQty;  // Use the total quantity from the message
    g_processedQuantities[newIndex] = 0;
    g_actions[newIndex] = action;
    g_isComplete[newIndex] = false;

    // Update global futures position based on total quantity
    if(action == "Buy" || action == "BuyToCover")
        globalFutures += 1;  // Add one contract at a time
    else if(action == "Sell" || action == "SellShort")
        globalFutures -= 1;  // Subtract one contract at a time

    Print("DEBUG: New trade group created. Base ID: ", baseId,
          ", Total Qty: ", totalQty,
          ", Action: ", action,
          ", Updated Global Futures: ", globalFutures);

    return newIndex;
}

//+------------------------------------------------------------------+
//| gRPC Connection Management                                       |
//+------------------------------------------------------------------+
bool InitializeGrpcConnection()
{
    ULogInfoPrint(StringFormat("Initializing gRPC connection to %s:%d", BridgeServerAddress, BridgeServerPort));
    // Test if DLL exports are working at all
    int testResult = TestFunction();

    if(testResult != 42) {
        ULogErrorPrint("ERROR: DLL exports not working correctly!");
        return false;
    }

    ULogInfoPrint("INFO: DLL connection verified");

    // If transport already reports connected, reuse existing connection (common during parameter changes)
    int already = GrpcIsConnected();
    if(already == 1)
    {
        ULogInfoPrint("InitializeGrpcConnection: Transport already connected; reusing without re-init");
        grpc_last_connection_attempt = TimeCurrent();
        return true;
    }

    ULogInfoPrint("INFO: Initializing gRPC connection...");

    // Initialize the gRPC client with timeout protection
    int result = GrpcInitialize(BridgeServerAddress, BridgeServerPort);

    if(result != 0) {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        ULogWarnPrint(StringFormat("gRPC initialization failed. Error: %d - %s", result, error_msg));
        ULogInfoPrint("NOTE: This is normal if bridge server is not running yet");
        return false;
    }

    // Verify connection with health check (with timeout protection)
    string health_request = "{\"source\":\"MT5_EA\",\"open_positions\":0}";
    string health_response;
    StringReserve(health_response, 2048); // Pre-allocate buffer for C++ DLL

    // Health check via native client (wide-char safe)
    result = GrpcHealthCheck(health_request, health_response, 2048);

    if(result != 0) {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        ULogWarnPrint(StringFormat("gRPC health check failed. Error: %d - %s", result, error_msg));
        ULogInfoPrint("NOTE: Bridge server may not be ready yet, will retry later");
        return false;
    }

    ULogInfoPrint("gRPC health check successful. Response: " + health_response);
    grpc_last_connection_attempt = TimeCurrent();

    return true;
}

bool StartGrpcTradeStreaming()
{
    ULogInfoPrint("Starting gRPC trade streaming with timeout protection...");
    // If a previous stream is still flagged as running, defensively stop it first
    if(grpc_streaming)
    {
        ULogWarnPrint("StartGrpcTradeStreaming: Previous stream flag was true; attempting to stop before restart");
        int stop_rc = GrpcStopTradeStream();
        if(stop_rc != 0)
        {
            string stop_err; StringReserve(stop_err, 1024); GrpcGetLastError(stop_err, 1024);
            ULogWarnPrint(StringFormat("StartGrpcTradeStreaming: GrpcStopTradeStream returned %d - %s (continuing to start)", stop_rc, stop_err));
        }
        grpc_streaming = false;
    }

    // Check if we're still connected before attempting to start streaming
    if(!grpc_connected) {
        ULogWarnPrint("Cannot start streaming: gRPC not connected");
        return false;
    }

    int result = GrpcStartTradeStream();

    if(result != 0) {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        ULogWarnPrint(StringFormat("Failed to start gRPC trade streaming. Error: %d - %s", result, error_msg));
        ULogInfoPrint("Streaming will be retried automatically");
        return false;
    }

    grpc_streaming = true;
    ULogInfoPrint("gRPC trade streaming started successfully");

    // Optional: kick a health check immediately to refresh Bridge status and leave a clear audit trail
    string _hc_req = "{\"source\":\"hedgebot\",\"open_positions\":" + IntegerToString(PositionsTotal()) + "}";
    string _hc_resp; StringReserve(_hc_resp, 2048);
    int _hc_rc = GrpcHealthCheck(_hc_req, _hc_resp, 2048);
    if(_hc_rc == 0)
    {
        ULogInfoPrint("Post-stream-start health check OK: " + _hc_resp);
    }
    else
    {
        string _hc_err; StringReserve(_hc_err, 1024); GrpcGetLastError(_hc_err, 1024);
        ULogWarnPrint(StringFormat("Post-stream-start health check failed rc=%d: %s", _hc_rc, _hc_err));
    }

    return true;
}

bool ReconnectGrpc()
{
    ULogWarnPrint("Attempting gRPC reconnection...");

    // Stop current streaming
    if(grpc_streaming) {
        GrpcStopTradeStream();
        grpc_streaming = false;
    }

    // Attempt reconnection
    int result = GrpcReconnect();

    if(result != 0) {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        ULogWarnPrint(StringFormat("gRPC reconnection failed. Error: %d - %s", result, error_msg));
        grpc_connected = false;
        UpdateStatusIndicator("gRPC Disconnected", clrRed);
        return false;
    }

    // Restart streaming
    if(StartGrpcTradeStreaming()) {
        grpc_connected = true;
        UpdateStatusIndicator("gRPC Connected", clrGreen);
        ULogInfoPrint("gRPC reconnection successful");
        return true;
    } else {
        grpc_connected = false;
        UpdateStatusIndicator("gRPC Streaming Failed", clrOrange);
        return false;
    }
}

void CheckGrpcConnection()
{
    if(!grpc_connected) {
        // Attempt reconnection if enough time has passed
        if(TimeCurrent() - grpc_last_connection_attempt >= grpc_connection_retry_interval) {
            grpc_last_connection_attempt = TimeCurrent();
            ReconnectGrpc();
        }
        return;
    }

    // Check if connection is still active
    int connected = GrpcIsConnected();
    // Treat health check as source of truth to avoid false negatives from GrpcIsConnected
    string health_request = "{\"source\":\"hedgebot\",\"open_positions\":" + IntegerToString(PositionsTotal()) + "}";
    string health_response;
    StringReserve(health_response, 2048);
    // Health check via native client (wide-char safe)
    int hc_result = GrpcHealthCheck(health_request, health_response, 2048);

    if(hc_result == 0) {
        // Health endpoint responded OK; consider bridge connected
        if(!grpc_connected) {
            ULogInfoPrint("gRPC health check succeeded; marking connected");
        }
        grpc_connected = true;
        UpdateStatusIndicator("gRPC Connected", clrGreen);
        // Ensure streaming is running; throttle start attempts
        static datetime _last_stream_attempt = 0;
        if(!grpc_streaming && (TimeCurrent() - _last_stream_attempt >= 3)) {
            _last_stream_attempt = TimeCurrent();
            if(StartGrpcTradeStreaming()) {
                grpc_streaming = true;
            }
        }
        return;
    }

    // Health check failed; capture DLL error detail
    string _hc_err; StringReserve(_hc_err, 1024); GrpcGetLastError(_hc_err, 1024);
    { string __log=""; StringConcatenate(__log, "gRPC health check failed (rc=", hc_result, "): ", _hc_err); Print(__log); ULogWarnPrint(__log); }
    // Only then trust GrpcIsConnected to decide disconnect handling
    if(connected == 0) {
        ULogWarnPrint("gRPC connection lost (health + isConnected failed). Will attempt reconnection.");
        grpc_connected = false;
        grpc_streaming = false;
        UpdateStatusIndicator("gRPC Disconnected", clrRed);
    } else {
        // isConnected true but health failed; degrade gracefully and retry later
        ULogWarnPrint("gRPC health check failed but transport reports connected. Will retry.");
        UpdateStatusIndicator("gRPC Health Failed", clrOrange);
    }
}

void ProcessGrpcTrades()
{
    if(!grpc_connected || !grpc_streaming) {
        static int debug_counter = 0;
        debug_counter++;
        if(debug_counter >= 1000) { // Print every 1000 skips
            debug_counter = 0;
            { string __log=""; StringConcatenate(__log, "DEBUG: Skipping trade processing - grpc_connected: ", grpc_connected, ", grpc_streaming: ", grpc_streaming); Print(__log); ULogInfoPrint(__log); }
        }
        return;
    }

    // Check how many trades are queued
    int queue_size = GrpcGetTradeQueueSize();
    if(queue_size <= 0) {
        return; // No trades to process
    }

    // Process a capped number of trades per timer cycle; scale with backlog to reduce lag.
    int processed = 0;
    int max_per_cycle = queue_size;
    if(max_per_cycle < 10)
        max_per_cycle = 10;
    if(max_per_cycle > 100)
        max_per_cycle = 100;

    while(processed < max_per_cycle && processed < queue_size) {
        string trade_json;
        StringReserve(trade_json, 8192); // Pre-allocate buffer for C++ DLL
        int result = GrpcGetNextTrade(trade_json, 8192);

        if(result != 0) {
            string error_msg;
            GrpcGetLastError(error_msg, 1024);
            { string __log=""; StringConcatenate(__log, "Error getting next trade: ", result, " - ", error_msg); Print(__log); ULogErrorPrint(__log); }
            break;
        }

        if(trade_json == "") {
            break; // No more trades
        }

        // Process the trade
        ProcessTradeFromJson(trade_json);
        processed++;
    }

    if(processed > 0) {
        { string __log=""; StringConcatenate(__log, "Processed ", processed, " trades from gRPC stream (", (queue_size - processed), " remaining)"); Print(__log); ULogInfoPrint(__log); }
    }
}

//+------------------------------------------------------------------+
//| Process gRPC trades without verbose logging (for timer events)  |
//+------------------------------------------------------------------+
void ProcessGrpcTradesQuiet()
{
    if(!grpc_connected || !grpc_streaming) {
        return; // Silent return - no logging
    }

    // Check how many trades are queued
    int queue_size = GrpcGetTradeQueueSize();
    if(queue_size <= 0) {
        return; // No trades to process - silent return
    }

    // Process a capped number of trades per timer cycle; scale with backlog to reduce lag.
    int processed = 0;
    int max_per_cycle = queue_size;
    if(max_per_cycle < 10)
        max_per_cycle = 10;
    if(max_per_cycle > 100)
        max_per_cycle = 100;

    while(processed < max_per_cycle && processed < queue_size) {
        string trade_json;
        StringReserve(trade_json, 8192); // Pre-allocate buffer for C++ DLL
        int result = GrpcGetNextTrade(trade_json, 8192);

        if(result != 0) {
            // Only log errors - these are important
            string error_msg;
            GrpcGetLastError(error_msg, 1024);
            { string __log=""; StringConcatenate(__log, "Error getting next trade: ", result, " - ", error_msg); Print(__log); ULogErrorPrint(__log); }
            break;
        }

        if(trade_json == "") {
            break; // No more trades
        }

        // Process the trade - this will log important events like trade execution
        ProcessTradeFromJson(trade_json);
        processed++;
    }

    // Only log if we actually processed trades (event happened)
    if(processed > 0) {
        { string __log=""; StringConcatenate(__log, "Processed ", processed, " trades from gRPC stream (", (queue_size - processed), " remaining)"); Print(__log); ULogInfoPrint(__log); }
    }
}

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
    Print("===== ACHedgeMaster gRPC v3.08 Initializing =====");
    // Initialize unified logging and emit startup log
    ULogInit();
    ULOG_CURRENT_BASE_ID = "";
    ULOG_INFO("EA OnInit started");
    // Mirror version/banner and terminal/account to unified log
    ULogInfoPrint(StringFormat("EA: %s", MQLInfoString(MQL_PROGRAM_NAME)));
    ULogInfoPrint(StringFormat("Terminal: %s build %d", TerminalInfoString(TERMINAL_NAME), (int)TerminalInfoInteger(TERMINAL_BUILD)));
    ULogInfoPrint(StringFormat("Account: %I64d / %s", (long)AccountInfoInteger(ACCOUNT_LOGIN), AccountInfoString(ACCOUNT_NAME)));

    // Initialize CTrade object
    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    trade.SetTypeFilling(ORDER_FILLING_IOC);

    // Reset trade groups on startup
    ResetTradeGroups();
    LoadMartingaleToggleState();

    // Initialize position tracking map
    if(g_map_position_id_to_base_id == NULL) {
        g_map_position_id_to_base_id = new CHashMap<long, string>();
        if(CheckPointer(g_map_position_id_to_base_id) == POINTER_INVALID) {
            { string __log="FATAL ERROR: Failed to initialize position tracking map!"; Print(__log); ULogErrorPrint(__log); }
            return(INIT_FAILED);
        }
    { string __log="Position tracking map initialized"; Print(__log); ULogInfoPrint(__log); }
    }
    if(g_simple_sl_tickets == NULL) {
        g_simple_sl_tickets = new CHashMap<long, int>();
        if(CheckPointer(g_simple_sl_tickets) == POINTER_INVALID) {
            { string __log="FATAL ERROR: Failed to initialize simple SL ticket map!"; Print(__log); ULogErrorPrint(__log); }
            return(INIT_FAILED);
        }
        { string __log="Simple SL ticket map initialized"; Print(__log); ULogInfoPrint(__log); }
    }
    if(g_inverse_sl_locks == NULL) {
        g_inverse_sl_locks = new CHashMap<long, double>();
        if(CheckPointer(g_inverse_sl_locks) == POINTER_INVALID) {
            { string __log="FATAL ERROR: Failed to initialize inverse SL lock map!"; Print(__log); ULogErrorPrint(__log); }
            return(INIT_FAILED);
        }
        { string __log="Inverse SL lock map initialized"; Print(__log); ULogInfoPrint(__log); }
    }
    if(g_inverse_tier_locks == NULL) {
        g_inverse_tier_locks = new CHashMap<long, int>();
        if(CheckPointer(g_inverse_tier_locks) == POINTER_INVALID) {
            { string __log="FATAL ERROR: Failed to initialize inverse tier lock map!"; Print(__log); ULogErrorPrint(__log); }
            return(INIT_FAILED);
        }
        { string __log="Inverse tier lock map initialized"; Print(__log); ULogInfoPrint(__log); }
    }

    // Initialize arrays
    ArrayResize(g_ntInstrumentSymbols, 0);
    ArrayResize(g_ntAccountNames, 0);
    ArrayResize(g_open_mt5_original_nt_actions, 0);
    ArrayResize(g_open_mt5_original_nt_quantities, 0);

    // Verify automated trading is enabled
    if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED)) {
        MessageBox("Please enable automated trading in MT5 settings!", "Error", MB_OK|MB_ICONERROR);
        return INIT_FAILED;
    }

    // Check account type
    ENUM_ACCOUNT_MARGIN_MODE margin_mode = (ENUM_ACCOUNT_MARGIN_MODE)AccountInfoInteger(ACCOUNT_MARGIN_MODE);
    if(margin_mode != ACCOUNT_MARGIN_MODE_RETAIL_HEDGING) {
    { string __log="Warning: Account does not support hedging. Operating in netting mode."; Print(__log); ULogWarnPrint(__log); }
    { string __log=""; StringConcatenate(__log, "Current margin mode: ", margin_mode); Print(__log); ULogWarnPrint(__log); }
    }

    // Initialize broker specs
    QueryBrokerSpecs();

    // State recovery for existing positions
    PerformStateRecovery();

    // Initialize UI elements (before gRPC to ensure they work regardless)
    InitStatusIndicator();
    InitStatusOverlay();
    UpdateMartingaleToggleButton();

    // Initialize or reuse gRPC connection (NON-BLOCKING)
    { string __log="Attempting gRPC connection (EA will work without bridge)..."; Print(__log); ULogInfoPrint(__log); }
    bool init_ok = false;
    if(g_param_change_restart)
    {
        ULogInfoPrint("PARAM_CHANGE: OnInit detected parameter-change restart; preferring existing connection if present");
        if(GrpcIsConnected() == 1)
        {
            init_ok = true;
            ULogInfoPrint("PARAM_CHANGE: Reusing existing gRPC transport (skip GrpcInitialize)");
        }
        else
        {
            ULogInfoPrint("PARAM_CHANGE: Transport not connected; performing normal initialization");
            init_ok = InitializeGrpcConnection();
        }
    }
    else
    {
        init_ok = InitializeGrpcConnection();
    }

    if(!init_ok) {
        { string __log="INFO: gRPC connection not available. EA running in offline mode."; Print(__log); ULogInfoPrint(__log); }
        { string __log="Bridge server connection will be retried automatically."; Print(__log); ULogInfoPrint(__log); }
        grpc_connected = false;
        UpdateStatusIndicator("Bridge Offline", clrOrange);
    } else {
        { string __log="gRPC connection established or reused successfully"; Print(__log); ULogInfoPrint(__log); }
        grpc_connected = true;
        UpdateStatusIndicator("Bridge Connected", clrGreen);

        // Start trade streaming (non-critical)
        if(!StartGrpcTradeStreaming()) {
            { string __log="INFO: Trade streaming not started. Will retry automatically."; Print(__log); ULogInfoPrint(__log); }
        }
    }

    // Clear the param-change hint once handled
    g_param_change_restart = false;

    Print("=================================");
    Print("AC HedgeMaster gRPC initialization complete");
    Print("Server: ", BridgeServerAddress, ":", BridgeServerPort);
    Print("EA Status: Ready (works with or without bridge)");
    Print("=================================");
    ULogInfoPrint("ACHedgeMaster gRPC initialization complete");
    ULogInfoPrint(StringFormat("Server: %s:%d", BridgeServerAddress, BridgeServerPort));
    ULogInfoPrint("EA Status: Ready (works with or without bridge)");
    // Direct logging path test: send one small event and print rc for diagnostics
    string _ulog_direct = "{\"timestamp_ns\":0,\"source\":\"mt5\",\"level\":\"INFO\",\"component\":\"EA\",\"message\":\"ulog direct smoke test\",\"schema_version\":\"mt5-1\"}";
    int _ulog_direct_rc = GrpcLog(_ulog_direct);
    { string __log=""; StringConcatenate(__log, "GrpcLog direct test rc=", _ulog_direct_rc); Print(__log); ULogInfoPrint(__log); }
    // Flush any startup logs to bridge
    int _ulog_flushed = ULogFlush();
    { string __log=""; StringConcatenate(__log, "Unified logs flushed at init: ", _ulog_flushed); Print(__log); ULogInfoPrint(__log); }

    // Set up millisecond timer for fast trade processing (100ms intervals)
    EventSetMillisecondTimer(100);
    { string __log="Fast trade processing timer initialized (100ms intervals)"; Print(__log); ULogInfoPrint(__log); }

    // Perform an immediate connection check to align UI state promptly
    CheckGrpcConnection();

    return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Timer event handler - Fast trade processing (100ms intervals)   |
//+------------------------------------------------------------------+
void OnTimer()
{
    // Maintain connection status and streaming
    CheckGrpcConnection();
    // Process gRPC trades without verbose logging
    ProcessGrpcTradesQuiet();
    // Periodic unified logging auto-flush (throttled in helper)
    ULogAutoFlush();
    UpdateCandleCountdown();
    UpdateMartingaleToggleButton();
}

// Periodic maintenance checks handled in OnTick

//+------------------------------------------------------------------+
//| Trade Processing Functions                                       |
//+------------------------------------------------------------------+
void ProcessTradeFromJson(const string& trade_json)
{
    // Debug logging for all responses (including CLOSE_HEDGE detection)
    if(StringFind(trade_json, "CLOSE_HEDGE") >= 0) {
        { string __log="INFO: Processing CLOSE_HEDGE request from gRPC response"; Print(__log); ULogInfoPrint(__log); }
    }

    // Extract minimal fields early for correct dedup behavior
    // 1) trade id
    string tradeId = "";
    int idPos = StringFind(trade_json, "\"id\":\"");
    if(idPos >= 0) {
        idPos += 6;  // Length of "\"id\":\""
        int idEndPos = StringFind(trade_json, "\"", idPos);
        if(idEndPos > idPos) {
            tradeId = StringSubstr(trade_json, idPos, idEndPos - idPos);
        }
    }
    // 2) base_id
    string baseIdForKey = GetJSONStringValue(trade_json, "\"base_id\"");
    if(baseIdForKey == "") {
        int tempBaseIdPos = StringFind(trade_json, "\"base_id\":\"");
        if(tempBaseIdPos >= 0) {
            tempBaseIdPos += 11;
            int tempBaseIdEndPos = StringFind(trade_json, "\"", tempBaseIdPos);
            if(tempBaseIdEndPos > tempBaseIdPos) {
                baseIdForKey = StringSubstr(trade_json, tempBaseIdPos, tempBaseIdEndPos - tempBaseIdPos);
            }
        }
    }
    // 3) quick action/orderType to allow non-open messages to bypass dedup
    string quickAction = GetJSONStringValue(trade_json, "\"action\"");
    string quickOrderType = GetJSONStringValue(trade_json, "\"order_type\"");
    bool quickIsAggregateEntry = (quickOrderType == "ENTRY_AGG");

    // Ignore init_stream messages
    if(tradeId == "init_stream") {
        { string __log="ACHM_LOG: [ProcessTradeFromJson] Ignoring init_stream message"; Print(__log); ULogInfoPrint(__log); }
        return;
    }

    // Build dedup key with contract_num or fallback to per-base occurrence index.
    // IMPORTANT: Skip dedup entirely for CLOSE_HEDGE / TP / SL so they are never dropped.
    bool isCloseOrTPSL = (quickAction == "CLOSE_HEDGE" || quickOrderType == "TP" || quickOrderType == "SL");
    int contractNumForKey = GetJSONIntValue(trade_json, "contract_num", -1);
    int totalQtyForKey    = GetJSONIntValue(trade_json, "total_quantity", -1);
    string dedupKey = "";
    if(!isCloseOrTPSL)
    {
        bool hasBase = (StringLen(baseIdForKey) > 0);
        bool multiFillIntent = (totalQtyForKey > 1 && !quickIsAggregateEntry);
        bool cnProvided = (contractNumForKey >= 0);

        if(hasBase)
        {
            // Detect repeated contract_num for same base_id (e.g., cn1 used for every fill)
            bool cnIsDuplicateForBase = false;
            if(cnProvided)
            {
                string cnKeyProbe = baseIdForKey + "#cn" + IntegerToString(contractNumForKey);
                cnIsDuplicateForBase = HasSeenTradeKey(cnKeyProbe);
            }

            if(cnProvided)
            {
                // Always include id with contract_num so distinct executions aren't dropped,
                // even if upstream reuses the same contract_num.
                if(StringLen(tradeId) > 0) {
                    dedupKey = baseIdForKey + "#cn" + IntegerToString(contractNumForKey) + "#id" + tradeId;
                } else {
                    // No id? fall back to occurrence to avoid merging multiple executions
                    int occIdx = PeekNextOccurrenceIndex(baseIdForKey);
                    dedupKey = baseIdForKey + "#cn" + IntegerToString(contractNumForKey) + "#occ" + IntegerToString(occIdx);
                    { string __log=""; StringConcatenate(__log, "ACHM_LOG: [ProcessTradeFromJson] Missing id; forcing occurrence with cn for base_id=", baseIdForKey, ", cn=", contractNumForKey, ", occ=", occIdx); Print(__log); ULogWarnPrint(__log); }
                }
            }
            else if(multiFillIntent)
            {
                // No contract_num provided but multi-fill intent signaled: use occurrence + optional id
                int occIdx = PeekNextOccurrenceIndex(baseIdForKey); // do not increment yet
                dedupKey = baseIdForKey + "#occ" + IntegerToString(occIdx);
                if(StringLen(tradeId) > 0)
                    dedupKey = dedupKey + "#id" + tradeId;
                { string __log=""; StringConcatenate(__log, "ACHM_LOG: [ProcessTradeFromJson] Using occurrence for multi-fill without contract_num for base_id=", baseIdForKey, ", occ=", occIdx, "/", totalQtyForKey); Print(__log); ULogInfoPrint(__log); }
            }
            else
            {
                // No multi-fill intent signaled: dedup by trade id if available
                if(StringLen(tradeId) > 0)
                    dedupKey = tradeId;
            }
        }
        else if(StringLen(tradeId) > 0)
        {
            // No base_id: fall back to id + optional cn; id already unique per execution
            if(cnProvided)
                dedupKey = tradeId + "#cn" + IntegerToString(contractNumForKey);
            else
                dedupKey = tradeId; // last resort
        }

        if(StringLen(dedupKey) > 0 && HasSeenTradeKey(dedupKey)) {
            { string __log=""; StringConcatenate(__log, "ACHM_LOG: [ProcessTradeFromJson] Ignoring duplicate message with key: ", dedupKey); Print(__log); ULogInfoPrint(__log); }
            return;
        }
        if(StringLen(dedupKey) > 0) {
            AddSeenTradeKey(dedupKey);
            // If we used the occurrence fallback, advance the counter now
            if(StringFind(dedupKey, "#occ") >= 0 && StringLen(baseIdForKey) > 0)
                IncrementOccurrence(baseIdForKey);
        }
    }
    lastTradeId = tradeId; // keep legacy tracking for diagnostics

    // Parse trade information from JSON response
    string incomingNtAction = "";
    double incomingNtQuantity = 0.0;
    double price = 0.0;
    string baseIdFromJson = "";
    bool isExit = false;
    int measurementPips = 0;
    string orderType = "";
    bool isLossClose = false;
    string closureReasonHint = "";

    // Parse NT performance data from enhanced JSON message
    double nt_balance = 0.0;
    double nt_daily_pnl = 0.0;
    string nt_trade_result = "";
    int nt_session_trades = 0;

    // Parse enhanced NT performance data if available (handled below with presence flags)

    // Optional: capture NT-provided points-per-$1k loss for sizing (if present in JSON)
    double json_nt_points_per_1k = GetJSONDoubleValue(trade_json, "nt_points_per_1k_loss", -1.0);
    if(json_nt_points_per_1k > 0) {
        g_ntPointsPer1kLoss = json_nt_points_per_1k;
        { string __log=""; StringConcatenate(__log, "ELASTIC_HINT: nt_points_per_1k_loss set from JSON: ", g_ntPointsPer1kLoss); Print(__log); ULogInfoPrint(__log); }
    } else {
        // Diagnostics: confirm whether the JSON actually contains the key and what value was parsed
        int __keyPos = StringFind(trade_json, "\"nt_points_per_1k_loss\"");
        string __snippet = StringSubstr(trade_json, (__keyPos > 20 ? __keyPos - 20 : 0), 80);
        { string __log=""; StringConcatenate(__log, "ELASTIC_DEBUG: nt_points_per_1k_loss missing or <=0 (parsed=", DoubleToString(json_nt_points_per_1k, 4), ") keyPos=", (string)IntegerToString(__keyPos), ", snippet=", __snippet); Print(__log); ULogInfoPrint(__log); }
    }
    // Parse enhanced NT performance data if available; only update fields that are present
    bool __hasBal=false, __hasPnL=false, __hasRes=false, __hasTrades=false;
    // Peek at action to decide if zero PnL in EVENT should be ignored (proto defaults)
    string __incomingAction = GetJSONStringValue(trade_json, "\"action\"");
    string __incomingEventType = GetJSONStringValue(trade_json, "\"event_type\"");
    ParseNTPerformanceData(trade_json, nt_balance, nt_daily_pnl, nt_trade_result, nt_session_trades, __hasBal, __hasPnL, __hasRes, __hasTrades);
    // Heuristic: For any non-entry action (not Buy/Sell), treat nt_daily_pnl=0.0 as "not present"
    // to avoid resetting tier due to proto-default zeros emitted via proto -> C++ JSON bridge.
    string __actLower = __incomingAction; StringToLower(__actLower);
    string __evtLower = __incomingEventType; StringToLower(__evtLower);
    if(__actLower != "buy" && __actLower != "sell" && nt_daily_pnl == 0.0) {
        // Allow explicit PnL update events to reset to 0 (e.g., after NT sim account reset).
        if(__evtLower != "nt_pnl_update") {
            __hasPnL = false;
            { string __log="NT_PARSE_GUARD: Ignoring zero nt_daily_pnl on non-entry action to preserve tier state"; Print(__log); ULogInfoPrint(__log); }
        }
    }
    // Additional guard: entry messages sometimes send nt_daily_pnl=0.0 even when session is in drawdown.
    // If we already have a non-zero cached PnL, keep it instead of overwriting with zero.
    if(__hasPnL && nt_daily_pnl == 0.0 && g_ntDataAvailable && MathAbs(g_NT_Daily_PnL) > 0.01) {
        __hasPnL = false;
        { string __log="NT_PARSE_GUARD: Suppressing zero nt_daily_pnl on entry to keep cached drawdown for inverse tiering"; Print(__log); ULogInfoPrint(__log); }
    }
    if(__hasBal || __hasPnL || __hasRes || __hasTrades) {
        UpdateNTPerformanceTrackingPartial(nt_balance, nt_daily_pnl, nt_trade_result, nt_session_trades, __hasBal, __hasPnL, __hasRes, __hasTrades);
    }

    // Parse basic trade data
    incomingNtAction = GetJSONStringValue(trade_json, "\"action\"");
    incomingNtQuantity = GetJSONDouble(trade_json, "quantity");
    price = GetJSONDouble(trade_json, "price");

    // Parse base_id
    baseIdFromJson = baseIdForKey;

    // Parse order type and measurement
    orderType = GetJSONStringValue(trade_json, "\"order_type\"");
    measurementPips = GetJSONIntValue(trade_json, "measurement_pips", 0);
    closureReasonHint = GetJSONStringValue(trade_json, "\"closure_reason\"");
    if(closureReasonHint == "")
        closureReasonHint = GetJSONStringValue(trade_json, "\"event_type\"");

    { string __log=""; StringConcatenate(__log, "ACHM_LOG: [ProcessTradeFromJson] Parsed NT base_id: '", baseIdFromJson, "', Action: '", incomingNtAction, "', Qty: ", incomingNtQuantity); Print(__log); ULogInfoPrint(__log); }

    // Special-case: handle trailing events delivered over the trade stream (Option B)
    // Expecting JSON fields:
    //  - trailing_stop_update: base_id, new_stop_price[, current_price]
    string evtType = GetJSONStringValue(trade_json, "\"event_type\"");
    // Log event_type presence and a short JSON snippet for diagnostics
    {
        string __snippet = StringSubstr(trade_json, 0, 200);
        string __log=""; StringConcatenate(__log, "EVENT_DEBUG: evtType='", evtType, "' base_id='", baseIdFromJson, "' json=", __snippet);
        Print(__log); ULogInfoPrint(__log);
    }
    if (evtType == "elastic_ping")
    {
        { string __log=""; StringConcatenate(__log, "ELASTIC_PING: Ignoring elastic ping event (self-elastic closures removed). base_id=", baseIdFromJson); Print(__log); ULogInfoPrint(__log); }
        return;
    }
    else if (evtType == "trailing_stop_update")
    {
        string evtBaseId2 = GetJSONStringValue(trade_json, "\"base_id\"");
        if(StringLen(evtBaseId2) == 0) evtBaseId2 = baseIdFromJson;
        // Parse stop and optional current price
        double newSL = GetJSONDoubleValue(trade_json, "new_stop_price", 0.0);
        double curPx = GetJSONDoubleValue(trade_json, "current_price", 0.0);
        if(curPx <= 0.0) {
            // Fallback to symbol side
            double bid = SymbolInfoDouble(_Symbol, SYMBOL_BID);
            double ask = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
            curPx = (bid > 0 && ask > 0) ? ((bid + ask) / 2.0) : (bid > 0 ? bid : ask);
        }
        { string __log=""; StringConcatenate(__log, "TRAIL_STOP: Received trailing update for BaseID: ", evtBaseId2, ", new SL: ", DoubleToString(newSL, _Digits), ", curPx: ", DoubleToString(curPx, _Digits)); Print(__log); ULogInfoPrint(__log); }
        if(newSL > 0.0)
            ProcessTrailingStopUpdate(evtBaseId2, newSL, curPx);
        // Do not process further as a regular trade
        return;
    }
    else if (evtType == "nt_pnl_update")
    {
        // Refresh inverse PnL overlay/estimates without opening trades
        if(LotSizingMode == LOTS_INVERSE_PNL)
        {
            CalculateInversePnLLot(ORDER_TYPE_BUY);
            ForceOverlayRecalculation();
            UpdateStatusOverlay();
        }
        return;
    }
    else if (evtType == "hedge_close_notification")
    {
        // Bridge-originated notification; informational only for EA. Avoid acting as a trade.
        { string __log="ACHM_LOG: [ProcessTradeFromJson] Ignoring incoming hedge_close_notification event"; Print(__log); ULogInfoPrint(__log); }
        return;
    }
    else
    {
        // If this arrived as a generic EVENT without a recognized event_type, ignore it to prevent accidental opens
        string __actionLower = incomingNtAction; StringToLower(__actionLower);
        if(__actionLower == "event") {
            { string __log="ACHM_LOG: [ProcessTradeFromJson] Ignoring generic EVENT without recognized event_type"; Print(__log); ULogInfoPrint(__log); }
            return;
        }
    }

    // Validate parsed data - prevent processing empty trades
    if(StringLen(incomingNtAction) == 0 && incomingNtQuantity == 0.0 && StringLen(baseIdFromJson) == 0) {
    { string __log="ACHM_LOG: [ProcessTradeFromJson] Ignoring empty trade data"; Print(__log); ULogWarnPrint(__log); }
        return;
    }

    // Filter out HedgeClose orders
    string orderName = GetJSONStringValue(trade_json, "\"order_name\"");
    if (orderName == "") {
        orderName = GetJSONStringValue(trade_json, "\"name\"");
    }

    if (StringFind(orderName, "HedgeClose") >= 0) {
    { string __log=""; StringConcatenate(__log, "ACHM_LOG: [ProcessTradeFromJson] Ignoring HedgeClose order: ", orderName); Print(__log); ULogInfoPrint(__log); }
        return;
    }

    // Process the trade based on action type
    if(incomingNtAction == "CLOSE_HEDGE") {
        // Determine if this close should trigger MT5 hedge run-up: ONLY when NT stop-loss closed the trade.
        if(!isLossClose)
        {
            string reasonLower = closureReasonHint;
            StringToLower(reasonLower);
            if(reasonLower == "nt_stop_loss" || reasonLower == "nt_stoploss" || reasonLower == "nt_chop_limit_fill")
                isLossClose = true;
        }
        if(isLossClose)
            ULogInfoPrint("RUNUP_FLAG: Marking NT close as loss; MT5 hedge run-up will be used instead of in-sync closure");

        // Extract MT5 ticket from JSON if available (support both snake_case and camelCase)
        ulong mt5Ticket = 0;
        { string __log=""; StringConcatenate(__log, "ACHM_CLOSURE_DEBUG: [ProcessTradeFromJson] Examining JSON for mt5_ticket/mt5Ticket: ", StringSubstr(trade_json, 0, 200)); Print(__log); ULogInfoPrint(__log); }

        int ticketPos = StringFind(trade_json, "\"mt5_ticket\":");
        int keyLen = 13; // default length of "mt5_ticket":
        if(ticketPos < 0) {
            ticketPos = StringFind(trade_json, "\"mt5Ticket\":");
            keyLen = 12; // length of "mt5Ticket":
        }
        if(ticketPos >= 0) {
            ticketPos += keyLen;
            string ticketStr = StringSubstr(trade_json, ticketPos, 32);
            // Trim potential whitespace and quotes
            int start = 0;
            while(start < StringLen(ticketStr) && (StringGetCharacter(ticketStr, start) == ' ' || StringGetCharacter(ticketStr, start) == '"')) start++;
            ticketStr = StringSubstr(ticketStr, start);

            int commaPos = StringFind(ticketStr, ",");
            int bracePos = StringFind(ticketStr, "}");
            int endQuote = StringFind(ticketStr, "\"");
            int endPos = -1;
            // Prefer comma/brace termination; if value was a quoted string, stop at quote
            if(endQuote >= 0 && (commaPos < 0 || endQuote < commaPos) && (bracePos < 0 || endQuote < bracePos)) endPos = endQuote;
            else if(commaPos > 0 && (bracePos < 0 || commaPos < bracePos)) endPos = commaPos;
            else if(bracePos > 0) endPos = bracePos;

            if(endPos > 0) {
                ticketStr = StringSubstr(ticketStr, 0, endPos);
                // Remove any remaining quotes/spaces
                while(StringLen(ticketStr) > 0 && (StringGetCharacter(ticketStr, 0) == ' ' || StringGetCharacter(ticketStr, 0) == '"'))
                    ticketStr = StringSubstr(ticketStr, 1);
                while(StringLen(ticketStr) > 0) {
                    int last = StringLen(ticketStr) - 1;
                    // StringGetCharacter returns ushort; use compatible type to avoid narrowing
                    ushort ch = (ushort)StringGetCharacter(ticketStr, last);
                    if(ch == ' ' || ch == '"') ticketStr = StringSubstr(ticketStr, 0, last);
                    else break;
                }
                mt5Ticket = (ulong)StringToInteger(ticketStr);
                { string __log=""; StringConcatenate(__log, "ACHM_CLOSURE_DEBUG: [ProcessTradeFromJson] Extracted MT5 ticket: ", mt5Ticket); Print(__log); ULogInfoPrint(__log); }
            }
        }

        ProcessCloseHedgeAction(baseIdFromJson, trade_json, mt5Ticket, isLossClose);
    } else if(orderType == "TP" || orderType == "SL") {
        ProcessTPSLOrder(baseIdFromJson, orderType, measurementPips, trade_json);
    } else {
        ProcessRegularTrade(incomingNtAction, incomingNtQuantity, price, baseIdFromJson, trade_json);
    }
    // Opportunistic cleanup of stale per-base occurrence entries
    CleanupOldOccurrences(900);
}

//+------------------------------------------------------------------+
//| Deterministic hedge-closing helpers                              |
//+------------------------------------------------------------------+
bool BaseIdMatchesTarget(const string &candidateBaseId, const string &targetBaseId)
{
    if(candidateBaseId == "" || targetBaseId == "")
        return false;
    if(candidateBaseId == targetBaseId)
        return true;
    if(StringLen(candidateBaseId) >= 16 && StringLen(targetBaseId) >= 16)
        return (StringSubstr(candidateBaseId, 0, 16) == StringSubstr(targetBaseId, 0, 16));
    return false;
}

bool IsPrimaryHedgeComment(const string &comment)
{
    if(comment == NULL || comment == "")
        return false;
    if(StringFind(comment, EA_COMMENT_PREFIX_BUY) == 0 || StringFind(comment, EA_COMMENT_PREFIX_SELL) == 0)
        return true;
    if(StringFind(comment, CommentPrefix) == 0)
        return true;
    if(StringFind(comment, "AC_HEDGE") == 0)
        return true;
    return false;
}

bool IsCounterHedgeComment(const string &comment)
{
    if(comment == NULL || comment == "")
        return false;
    return (StringFind(comment, COUNTER_COMMENT_PREFIX_BUY) == 0 || StringFind(comment, COUNTER_COMMENT_PREFIX_SELL) == 0);
}

MANAGED_TRADE_KIND GetManagedTradeKindFromComment(const string &comment)
{
    if(IsCounterHedgeComment(comment))
        return ManagedTrade_CounterHedge;
    if(IsPrimaryHedgeComment(comment))
        return ManagedTrade_PrimaryHedge;
    return ManagedTrade_None;
}

bool TryExtractManagedBaseIdFromComment(const string &comment, string &outBaseId)
{
    outBaseId = "";
    if(comment == NULL || comment == "")
        return false;

    if(StringFind(comment, EA_COMMENT_PREFIX_BUY) == 0)
        outBaseId = StringSubstr(comment, StringLen(EA_COMMENT_PREFIX_BUY));
    else if(StringFind(comment, EA_COMMENT_PREFIX_SELL) == 0)
        outBaseId = StringSubstr(comment, StringLen(EA_COMMENT_PREFIX_SELL));
    else if(StringFind(comment, COUNTER_COMMENT_PREFIX_BUY) == 0)
        outBaseId = StringSubstr(comment, StringLen(COUNTER_COMMENT_PREFIX_BUY));
    else if(StringFind(comment, COUNTER_COMMENT_PREFIX_SELL) == 0)
        outBaseId = StringSubstr(comment, StringLen(COUNTER_COMMENT_PREFIX_SELL));
    else if(StringFind(comment, CommentPrefix) == 0)
        outBaseId = StringSubstr(comment, StringLen(CommentPrefix));
    else
    {
        string bid_marker = "BID:";
        int start_pos = StringFind(comment, bid_marker, 0);
        if(start_pos != -1)
        {
            int value_start_pos = start_pos + StringLen(bid_marker);
            if(value_start_pos < StringLen(comment))
            {
                int end_pos = StringFind(comment, ";", value_start_pos);
                if(end_pos != -1)
                    outBaseId = StringSubstr(comment, value_start_pos, end_pos - value_start_pos);
                else
                    outBaseId = StringSubstr(comment, value_start_pos);
            }
        }
    }

    return (outBaseId != "");
}

int FindCounterHedgeIndexByTicket(ulong counterTicket)
{
    int total = ArraySize(g_counter_hedge_pos_ids);
    for(int i = 0; i < total; i++)
    {
        if((ulong)g_counter_hedge_pos_ids[i] == counterTicket)
            return i;
    }
    return -1;
}

bool TryResolveCounterParentTicket(ulong counterTicket, ulong &outParentTicket)
{
    outParentTicket = 0;
    int idx = FindCounterHedgeIndexByTicket(counterTicket);
    if(idx < 0)
        return false;
    outParentTicket = (ulong)g_counter_hedge_parent_pos_ids[idx];
    return (outParentTicket > 0);
}

bool TryResolveCounterBaseId(ulong counterTicket, string &outBaseId)
{
    outBaseId = "";
    int idx = FindCounterHedgeIndexByTicket(counterTicket);
    if(idx < 0)
        return false;
    outBaseId = g_counter_hedge_base_ids[idx];
    return (outBaseId != "");
}

int CountLinkedCounterHedges(ulong parentTicket)
{
    int count = 0;
    for(int i = ArraySize(g_counter_hedge_pos_ids) - 1; i >= 0; i--)
    {
        if((ulong)g_counter_hedge_parent_pos_ids[i] != parentTicket)
            continue;
        ulong counterTicket = (ulong)g_counter_hedge_pos_ids[i];
        if(counterTicket == 0)
            continue;
        if(!PositionSelectByTicket(counterTicket))
            continue;
        count++;
    }
    return count;
}

void RemoveCounterHedgeTracking(ulong counterTicket)
{
    int idx = FindCounterHedgeIndexByTicket(counterTicket);
    if(idx < 0)
        return;

    int last = ArraySize(g_counter_hedge_pos_ids) - 1;
    if(idx != last)
    {
        g_counter_hedge_pos_ids[idx] = g_counter_hedge_pos_ids[last];
        g_counter_hedge_parent_pos_ids[idx] = g_counter_hedge_parent_pos_ids[last];
        g_counter_hedge_base_ids[idx] = g_counter_hedge_base_ids[last];
        g_counter_hedge_actions[idx] = g_counter_hedge_actions[last];
    }

    ArrayResize(g_counter_hedge_pos_ids, last);
    ArrayResize(g_counter_hedge_parent_pos_ids, last);
    ArrayResize(g_counter_hedge_base_ids, last);
    ArrayResize(g_counter_hedge_actions, last);

    if(g_map_position_id_to_base_id != NULL)
        g_map_position_id_to_base_id.Remove((long)counterTicket);
}

void CleanupCounterHedgeTracking()
{
    for(int i = ArraySize(g_counter_hedge_pos_ids) - 1; i >= 0; i--)
    {
        ulong ticket = (ulong)g_counter_hedge_pos_ids[i];
        if(ticket == 0 || !PositionSelectByTicket(ticket))
            RemoveCounterHedgeTracking(ticket);
    }
}

bool TryResolveBaseIdForTicket(ulong ticket, string &outBaseId)
{
    outBaseId = "";
    if(g_map_position_id_to_base_id != NULL)
    {
        string mapped = "";
        if(g_map_position_id_to_base_id.TryGetValue((long)ticket, mapped) && mapped != "")
        {
            outBaseId = mapped;
            return true;
        }
    }

    for(int i = 0; i < ArraySize(g_open_mt5_pos_ids); i++)
    {
        if((ulong)g_open_mt5_pos_ids[i] == ticket)
        {
            outBaseId = g_open_mt5_base_ids[i];
            return (outBaseId != "");
        }
    }

    if(TryResolveCounterBaseId(ticket, outBaseId))
        return true;

    if(PositionSelectByTicket(ticket))
    {
        string comment = PositionGetString(POSITION_COMMENT);
        if(TryExtractManagedBaseIdFromComment(comment, outBaseId))
            return true;
    }
    return false;
}

void RemoveOpenPositionTracking(ulong ticket)
{
    int size = ArraySize(g_open_mt5_pos_ids);
    if(size <= 0)
        return;

    for(int i = 0; i < size; i++)
    {
        if((ulong)g_open_mt5_pos_ids[i] == ticket)
        {
            int last = size - 1;
            if(i != last)
            {
                g_open_mt5_pos_ids[i] = g_open_mt5_pos_ids[last];
                g_open_mt5_base_ids[i] = g_open_mt5_base_ids[last];
                g_open_mt5_nt_symbols[i] = g_open_mt5_nt_symbols[last];
                g_open_mt5_nt_accounts[i] = g_open_mt5_nt_accounts[last];
                g_open_mt5_actions[i] = g_open_mt5_actions[last];
                g_open_mt5_original_nt_actions[i] = g_open_mt5_original_nt_actions[last];
                g_open_mt5_original_nt_quantities[i] = g_open_mt5_original_nt_quantities[last];
            }
            ArrayResize(g_open_mt5_pos_ids, last);
            ArrayResize(g_open_mt5_base_ids, last);
            ArrayResize(g_open_mt5_nt_symbols, last);
            ArrayResize(g_open_mt5_nt_accounts, last);
            ArrayResize(g_open_mt5_actions, last);
            ArrayResize(g_open_mt5_original_nt_actions, last);
            ArrayResize(g_open_mt5_original_nt_quantities, last);
            RemoveRunUpState(ticket);
            break;
        }
    }
}

void AppendTicketEntry(ulong &tickets[], datetime &openTimes[], double &volumes[], ulong ticket, datetime openTime, double volume)
{
    int newSize = ArraySize(tickets) + 1;
    ArrayResize(tickets, newSize);
    tickets[newSize - 1] = ticket;
    ArrayResize(openTimes, newSize);
    openTimes[newSize - 1] = openTime;
    ArrayResize(volumes, newSize);
    volumes[newSize - 1] = volume;
}

int CollectTicketsForBaseIdInternal(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[], bool includeCounterHedges)
{
    ArrayResize(tickets, 0);
    ArrayResize(openTimes, 0);
    ArrayResize(volumes, 0);

    if(baseId == "")
        return 0;

    int totalPositions = PositionsTotal();
    for(int i = 0; i < totalPositions; i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0)
            continue;
        if(!PositionSelectByTicket(ticket))
            continue;
        if(PositionGetString(POSITION_SYMBOL) != _Symbol)
            continue;
        if(PositionGetInteger(POSITION_MAGIC) != MagicNumber)
            continue;

        string comment = PositionGetString(POSITION_COMMENT);
        MANAGED_TRADE_KIND tradeKind = GetManagedTradeKindFromComment(comment);
        if(tradeKind == ManagedTrade_None)
            continue;
        if(!includeCounterHedges && tradeKind != ManagedTrade_PrimaryHedge)
            continue;

        string mappedBaseId = "";
        if(!TryResolveBaseIdForTicket(ticket, mappedBaseId))
            continue;
        if(!BaseIdMatchesTarget(mappedBaseId, baseId))
            continue;

        datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
        double volume = PositionGetDouble(POSITION_VOLUME);
        AppendTicketEntry(tickets, openTimes, volumes, ticket, openTime, volume);
    }
    return ArraySize(tickets);
}

int CollectPrimaryHedgeTicketsForBaseId(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[])
{
    return CollectTicketsForBaseIdInternal(baseId, tickets, openTimes, volumes, false);
}

int CollectManagedTicketsForBaseId(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[])
{
    return CollectTicketsForBaseIdInternal(baseId, tickets, openTimes, volumes, true);
}

int CollectTicketsForBaseId(const string &baseId, ulong &tickets[], datetime &openTimes[], double &volumes[])
{
    return CollectPrimaryHedgeTicketsForBaseId(baseId, tickets, openTimes, volumes);
}

void SortTicketsByOpenTime(datetime &openTimes[], ulong &tickets[], double &volumes[])
{
    int count = ArraySize(openTimes);
    for(int i = 0; i < count - 1; i++)
    {
        int minIdx = i;
        for(int j = i + 1; j < count; j++)
        {
            if(openTimes[j] < openTimes[minIdx])
                minIdx = j;
        }
        if(minIdx != i)
        {
            datetime tmpTime = openTimes[i];
            openTimes[i] = openTimes[minIdx];
            openTimes[minIdx] = tmpTime;

            ulong tmpTicket = tickets[i];
            tickets[i] = tickets[minIdx];
            tickets[minIdx] = tmpTicket;

            double tmpVol = volumes[i];
            volumes[i] = volumes[minIdx];
            volumes[minIdx] = tmpVol;
        }
    }
}

double GetCounterHedgeDrawdownValue(COUNTER_HEDGE_TRIGGER_MODE mode, double adversePriceDistance, double floatingProfit, double volume)
{
    if(mode == CounterTrigger_Points)
    {
        if(adversePriceDistance <= 0.0)
            return 0.0;
        return adversePriceDistance / _Point;
    }

    double floatingLoss = -floatingProfit;
    if(floatingLoss < 0.0)
        floatingLoss = 0.0;
    return floatingLoss;
}

double GetCounterHedgeRepeatBaselineValue(double volume)
{
    if(CounterHedge_InitialMode == CounterHedge_RepeatMode)
        return CounterHedge_InitialValue;

    double initialPriceDistance = 0.0;
    if(CounterHedge_InitialMode == CounterTrigger_Points)
        initialPriceDistance = CounterHedge_InitialValue * _Point;
    else
        initialPriceDistance = DollarsToPriceDistance(CounterHedge_InitialValue, volume);

    if(initialPriceDistance <= 0.0)
        return 0.0;

    if(CounterHedge_RepeatMode == CounterTrigger_Points)
        return initialPriceDistance / _Point;
    return PriceDistanceToDollars(initialPriceDistance, volume);
}

int GetCounterHedgeTargetCount(ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double floatingProfit, double volume)
{
    if(!CounterHedge_Enabled || volume <= 0.0 || entryPrice <= 0.0 || currentPrice <= 0.0)
        return 0;

    double adversePriceDistance = (posType == POSITION_TYPE_BUY)
        ? (entryPrice - currentPrice)
        : (currentPrice - entryPrice);
    if(adversePriceDistance < 0.0)
        adversePriceDistance = 0.0;

    double initialDrawdown = GetCounterHedgeDrawdownValue(CounterHedge_InitialMode, adversePriceDistance, floatingProfit, volume);
    if(initialDrawdown + 1e-8 < CounterHedge_InitialValue)
        return 0;

    int targetCount = 1;
    if(CounterHedge_RepeatStep > 0.0)
    {
        double repeatDrawdown = GetCounterHedgeDrawdownValue(CounterHedge_RepeatMode, adversePriceDistance, floatingProfit, volume);
        double repeatBaseline = GetCounterHedgeRepeatBaselineValue(volume);
        double extraDrawdown = repeatDrawdown - repeatBaseline;
        if(extraDrawdown > 0.0)
            targetCount += (int)MathFloor((extraDrawdown + 1e-8) / CounterHedge_RepeatStep);
    }

    return targetCount;
}

bool RegisterCounterHedgePosition(ulong counterTicket, ulong parentTicket, const string &baseId, const string &action)
{
    if(counterTicket == 0 || parentTicket == 0 || baseId == "")
        return false;
    if(FindCounterHedgeIndexByTicket(counterTicket) >= 0)
        return true;

    int size = ArraySize(g_counter_hedge_pos_ids);
    ArrayResize(g_counter_hedge_pos_ids, size + 1);
    ArrayResize(g_counter_hedge_parent_pos_ids, size + 1);
    ArrayResize(g_counter_hedge_base_ids, size + 1);
    ArrayResize(g_counter_hedge_actions, size + 1);

    g_counter_hedge_pos_ids[size] = (long)counterTicket;
    g_counter_hedge_parent_pos_ids[size] = (long)parentTicket;
    g_counter_hedge_base_ids[size] = baseId;
    g_counter_hedge_actions[size] = action;

    if(g_map_position_id_to_base_id != NULL)
    {
        g_map_position_id_to_base_id.Remove((long)counterTicket);
        g_map_position_id_to_base_id.Add((long)counterTicket, baseId);
    }

    return true;
}

bool OpenCounterHedgeTrade(ulong parentTicket, const string &baseId, ENUM_POSITION_TYPE parentType)
{
    if(!CounterHedge_Enabled || CounterHedge_LotSize <= 0.0 || parentTicket == 0 || baseId == "")
        return false;

    string tradeBlockReason = "";
    if(!IsTradingPermitted(tradeBlockReason))
    {
        string __log = StringFormat("COUNTER_HEDGE_SKIP: Trading not permitted for base_id=%s parent=%I64u reason=%s", baseId, (long)parentTicket, tradeBlockReason);
        Print(__log); ULogWarnPrint(__log);
        return false;
    }

    double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(minLot <= 0.0)
        minLot = 0.01;
    if(maxLot < minLot)
        maxLot = minLot;
    if(lotStep <= 0.0)
        lotStep = minLot;

    double finalVol = CounterHedge_LotSize;
    if(finalVol < minLot)
        finalVol = minLot;
    if(finalVol > maxLot)
        finalVol = maxLot;
    double stepUnits = finalVol / lotStep;
    double roundedUnits = MathRound(stepUnits);
    finalVol = NormalizeDouble(roundedUnits * lotStep, 8);
    if(finalVol < minLot)
        finalVol = minLot;
    if(finalVol > maxLot)
        finalVol = maxLot;

    ENUM_ORDER_TYPE counterOrderType = (parentType == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
    string counterAction = (counterOrderType == ORDER_TYPE_BUY) ? "BUY" : "SELL";
    string commentPrefix = (counterOrderType == ORDER_TYPE_BUY) ? COUNTER_COMMENT_PREFIX_BUY : COUNTER_COMMENT_PREFIX_SELL;
    string comment = commentPrefix + StringSubstr(baseId, 0, 16);
    double price = SymbolInfoDouble(_Symbol, (counterOrderType == ORDER_TYPE_BUY) ? SYMBOL_ASK : SYMBOL_BID);

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);

    bool sent = (counterOrderType == ORDER_TYPE_BUY)
        ? trade.Buy(finalVol, _Symbol, price, 0.0, 0.0, comment)
        : trade.Sell(finalVol, _Symbol, price, 0.0, 0.0, comment);

    if(!sent)
    {
        string __log = StringFormat("COUNTER_HEDGE_FAIL: base_id=%s parent=%I64u action=%s vol=%.4f rc=%d comment=%s",
            baseId, (long)parentTicket, counterAction, finalVol, (int)trade.ResultRetcode(), trade.ResultComment());
        Print(__log); ULogWarnPrint(__log);
        return false;
    }

    ulong counterTicket = 0;
    ulong dealTicket = trade.ResultDeal();
    if(dealTicket > 0 && HistoryDealSelect(dealTicket))
        counterTicket = (ulong)HistoryDealGetInteger(dealTicket, DEAL_POSITION_ID);

    if(counterTicket == 0)
    {
        datetime newestTime = 0;
        int total = PositionsTotal();
        for(int i = 0; i < total; i++)
        {
            ulong scanTicket = PositionGetTicket(i);
            if(scanTicket == 0)
                continue;
            if(!PositionSelectByTicket(scanTicket))
                continue;
            if(PositionGetString(POSITION_SYMBOL) != _Symbol)
                continue;
            if(PositionGetInteger(POSITION_MAGIC) != MagicNumber)
                continue;
            string scanComment = PositionGetString(POSITION_COMMENT);
            if(scanComment != comment || !IsCounterHedgeComment(scanComment))
                continue;
            if(FindCounterHedgeIndexByTicket(scanTicket) >= 0)
                continue;
            datetime posTime = (datetime)PositionGetInteger(POSITION_TIME);
            if(posTime >= newestTime)
            {
                newestTime = posTime;
                counterTicket = scanTicket;
            }
        }
    }

    if(counterTicket == 0)
    {
        string __log = StringFormat("COUNTER_HEDGE_WARN: Opened trade for base_id=%s but failed to resolve ticket", baseId);
        Print(__log); ULogWarnPrint(__log);
        return false;
    }

    RegisterCounterHedgePosition(counterTicket, parentTicket, baseId, counterAction);
    string __log = StringFormat("COUNTER_HEDGE_OPEN: base_id=%s parent=%I64u ticket=%I64u action=%s vol=%.4f",
        baseId, (long)parentTicket, (long)counterTicket, counterAction, finalVol);
    Print(__log); ULogInfoPrint(__log);
    return true;
}

bool EnsureCounterHedgeCoverage(ulong parentTicket, const string &baseId, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double floatingProfit, double volume)
{
    if(!CounterHedge_Enabled || CounterHedge_LotSize <= 0.0 || parentTicket == 0 || baseId == "")
        return false;

    int targetCount = GetCounterHedgeTargetCount(posType, entryPrice, currentPrice, floatingProfit, volume);
    if(targetCount <= 0)
        return false;

    int currentCount = CountLinkedCounterHedges(parentTicket);
    if(currentCount >= targetCount)
        return false;

    int missingCount = targetCount - currentCount;
    bool openedAny = false;
    for(int i = 0; i < missingCount; i++)
    {
        if(OpenCounterHedgeTrade(parentTicket, baseId, posType))
        {
            openedAny = true;
            currentCount++;
        }
        else
        {
            break;
        }
    }
    return openedAny;
}

bool CloseLinkedCounterHedges(ulong parentTicket, const string &baseId, const string &reason)
{
    ulong linkedTickets[];
    ArrayResize(linkedTickets, 0);

    for(int i = 0; i < ArraySize(g_counter_hedge_pos_ids); i++)
    {
        if((ulong)g_counter_hedge_parent_pos_ids[i] != parentTicket)
            continue;
        int next = ArraySize(linkedTickets);
        ArrayResize(linkedTickets, next + 1);
        linkedTickets[next] = (ulong)g_counter_hedge_pos_ids[i];
    }

    if(ArraySize(linkedTickets) == 0)
        return false;

    bool closedAny = false;
    for(int i = 0; i < ArraySize(linkedTickets); i++)
    {
        ulong counterTicket = linkedTickets[i];
        if(counterTicket == 0)
            continue;
        if(!PositionSelectByTicket(counterTicket))
        {
            RemoveCounterHedgeTracking(counterTicket);
            continue;
        }

        trade.SetExpertMagicNumber(MagicNumber);
        trade.SetDeviationInPoints(Slippage);
        bool closed = trade.PositionClose(counterTicket, Slippage);
        if(closed)
        {
            string __log = StringFormat("COUNTER_HEDGE_CLOSE: base_id=%s parent=%I64u ticket=%I64u reason=%s",
                baseId, (long)parentTicket, (long)counterTicket, reason);
            Print(__log); ULogInfoPrint(__log);
            RemoveCounterHedgeTracking(counterTicket);
            closedAny = true;
        }
        else
        {
            string __log = StringFormat("COUNTER_HEDGE_CLOSE_WARN: base_id=%s parent=%I64u ticket=%I64u rc=%d comment=%s",
                baseId, (long)parentTicket, (long)counterTicket, (int)trade.ResultRetcode(), trade.ResultComment());
            Print(__log); ULogWarnPrint(__log);
        }
    }

    return closedAny;
}
bool CloseHedgeTicket(const string &baseId, ulong ticket, double &closedVolume, const string &reason)
{
    closedVolume = 0.0;
    if(ticket == 0)
        return false;

    if(!PositionSelectByTicket(ticket))
    {
        { string __log=""; StringConcatenate(__log, "ACHM_CLOSURE_WARN: Ticket ", ticket, " could not be selected for base_id ", baseId); Print(__log); ULogWarnPrint(__log); }
        return false;
    }

    closedVolume = PositionGetDouble(POSITION_VOLUME);

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);

    bool success = trade.PositionClose(ticket);
    if(success)
    {
        SubmitTradeResult("success", ticket, closedVolume, true, baseId);
        CloseLinkedCounterHedges(ticket, baseId, reason);
        NotifyMT5PositionClosure(baseId, ticket, closedVolume, reason);
        return true;
    }

    int closeError = GetLastError();
    { string __log=""; StringConcatenate(__log, "ACHM_CLOSURE_ERROR: Failed to close ticket ", ticket, " for base_id ", baseId, " (error ", closeError, ")"); Print(__log); ULogErrorPrint(__log); }
    SubmitTradeResult("failed", ticket, closedVolume, true, baseId);
    return false;
}

int ComputeRequestedClosures(const string &trade_json)
{
    double requested = GetJSONDoubleValue(trade_json, "closed_hedge_quantity", -1.0);
    if(requested < 0.0)
        requested = GetJSONDoubleValue(trade_json, "quantity", -1.0);
    if(requested <= 0.0)
        return 1;
    int contracts = (int)MathRound(requested);
    if(contracts <= 0)
        contracts = 1;
    return contracts;
}

void ProcessCloseHedgeAction(const string& baseId, const string& trade_json, ulong mt5Ticket = 0, bool isLossClose = false)
{
    string canonicalBaseId = baseId;
    if(canonicalBaseId == "" && mt5Ticket > 0)
    {
        string resolved = "";
        if(TryResolveBaseIdForTicket(mt5Ticket, resolved))
            canonicalBaseId = resolved;
    }

    if(canonicalBaseId == "")
    {
        { string __log="ACHM_CLOSURE_WARN: CLOSE_HEDGE missing base_id and unable to resolve"; Print(__log); ULogWarnPrint(__log); }
        return;
    }

    string closureReason = GetJSONStringValue(trade_json, "\"closure_reason\"");
    if(closureReason == "")
        closureReason = "NT_close_request";

    // If this NT close is a loss and run-up is enabled, keep the hedge open and trail instead.
    if(isLossClose && HedgeRunUp_Enabled)
    {
        bool started = StartHedgeRunUpForBaseId(canonicalBaseId, closureReason, mt5Ticket);
        if(started)
            return;
        { string __log=""; StringConcatenate(__log, "RUNUP_FALLBACK: Unable to start run-up for base_id ", canonicalBaseId, " (will proceed with CLOSE_HEDGE)"); Print(__log); ULogWarnPrint(__log); }
    }

    int requestedClosures = ComputeRequestedClosures(trade_json);
    if(requestedClosures <= 0)
        requestedClosures = 1;

    if(mt5Ticket > 0)
    {
        double closedVol = 0.0;
        if(CloseHedgeTicket(canonicalBaseId, mt5Ticket, closedVol, closureReason))
        {
            { string __log=StringFormat("ACHM_CLOSURE: Closed hedge via ticket %I64u for base_id %s (vol=%.2f)", (long)mt5Ticket, canonicalBaseId, closedVol); Print(__log); ULogInfoPrint(__log); }
        }
        else
        {
            { string __log=StringFormat("ACHM_CLOSURE_WARN: Ticket %I64u could not be closed for base_id %s", (long)mt5Ticket, canonicalBaseId); Print(__log); ULogWarnPrint(__log); }
        }

        ulong remainingTickets[]; datetime remainingTimes[]; double remainingVolumes[];
        return;
    }

    ulong tickets[]; datetime openTimes[]; double volumes[];
    int available = CollectPrimaryHedgeTicketsForBaseId(canonicalBaseId, tickets, openTimes, volumes);
    if(available == 0)
    {
        { string __log=StringFormat("ACHM_CLOSURE_WARN: No open hedges found for base_id %s to satisfy CLOSE_HEDGE", canonicalBaseId); Print(__log); ULogWarnPrint(__log); }
        SubmitTradeResult("not_found", 0, 0.0, true, canonicalBaseId);
        return;
    }

    SortTicketsByOpenTime(openTimes, tickets, volumes);

    int toClose = requestedClosures;
    if(toClose > available)
        toClose = available;

    int closedCount = 0;
    for(int i = 0; i < toClose; i++)
    {
        double closedVol = 0.0;
        if(CloseHedgeTicket(canonicalBaseId, tickets[i], closedVol, closureReason))
            closedCount++;
    }

    if(closedCount < toClose)
    {
        { string __log=StringFormat("ACHM_CLOSURE_WARN: Requested closures %d but only closed %d for base_id %s", toClose, closedCount, canonicalBaseId); Print(__log); ULogWarnPrint(__log); }
    }
    else
    {
        { string __log=StringFormat("ACHM_CLOSURE_INFO: Closed %d hedge position(s) for base_id %s", closedCount, canonicalBaseId); Print(__log); ULogInfoPrint(__log); }
    }

    ulong remainingTickets[]; datetime remainingTimes[]; double remainingVolumes[];
}

void ProcessTPSLOrder(const string& baseId, const string& orderType, int measurementPips, const string& trade_json)
{
    { string __log = StringFormat("ACHM_LOG: [ProcessTPSLOrder] Processing %s order for base_id: %s, pips: %d", (string)orderType, (string)baseId, (int)measurementPips); Print(__log); ULogInfoPrint(__log); }

    // Store TP/SL measurement
    lastTPSL.baseTradeId = baseId;
    lastTPSL.orderType = orderType;
    lastTPSL.pips = measurementPips;

    // Get raw measurement from JSON
    double rawMeasurement = 0.0;
    int rawPos = StringFind(trade_json, "\"raw_measurement\":");
    if(rawPos >= 0) {
        rawPos += 18; // Length of "\"raw_measurement\":"
        string rawStr = StringSubstr(trade_json, rawPos, 20);
        int commaPos = StringFind(rawStr, ",");
        if(commaPos > 0) {
            rawStr = StringSubstr(rawStr, 0, commaPos);
        }
        rawMeasurement = StringToDouble(rawStr);
    }
    lastTPSL.rawMeasurement = rawMeasurement;

    { string __log = StringFormat("ACHM_LOG: [ProcessTPSLOrder] Stored TP/SL measurement: %s = %d pips (%.8f)", (string)orderType, (int)measurementPips, (double)rawMeasurement); Print(__log); ULogInfoPrint(__log); }
}

void ProcessRegularTrade(const string& action, double quantity, double price, const string& baseId, const string& trade_json)
{
    { string __log = StringFormat("ACHM_LOG: [ProcessRegularTrade] Processing regular trade - Action: %s, Qty: %.8f, Price: %.8f, BaseId: %s", (string)action, (double)quantity, (double)price, (string)baseId); Print(__log); ULogInfoPrint(__log); }

    // Determine trade direction for hedging
    ENUM_ORDER_TYPE orderType;
    string commentPrefix = ""; // local comment prefix for order comments

        // Guard: only process NT actions that are explicit buy/sell. Ignore anything else (e.g., EVENT) to avoid false opens
        string __actLower = action; StringToLower(__actLower);
        bool __isBuy  = (__actLower == "buy");
        bool __isSell = (__actLower == "sell");
        if(!__isBuy && !__isSell)
        {
            { string __ilog = StringFormat("ACHM_LOG: [ProcessRegularTrade] Ignoring non-trade action '%s' for base_id: %s", (string)action, (string)baseId); Print(__ilog); ULogInfoPrint(__ilog); }
            SubmitTradeResult("ignored", 0, 0.0, false, baseId);
            return;
        }
        // Preflight: verify trading is permitted to avoid 4756 (Trading is prohibited)
        string tradeBlockReason = "";
        if(!IsTradingPermitted(tradeBlockReason))
        {
            { string __elog = StringFormat("ACHM_ERROR: Trading not permitted for symbol %s: %s. Skipping hedge for base_id: %s", (string)_Symbol, (string)tradeBlockReason, (string)baseId); Print(__elog); ULogErrorPrint(__elog); }
            SubmitTradeResult("failed", 0, 0.0, false, baseId);
            return;
        }

        // Calculate lot size based on mode

    { string __log = StringFormat("ACHM_HEDGE_DEBUG: [ProcessRegularTrade] NT Action: '%s', EnableHedging: %d", (string)action, (int)EnableHedging); Print(__log); ULogInfoPrint(__log); }

    if(EnableHedging) {
        // Hedge opposite direction
        if(__isBuy) {
            orderType = ORDER_TYPE_SELL;
            commentPrefix = EA_COMMENT_PREFIX_SELL;
            { string __log="ACHM_HEDGE_DEBUG: [ProcessRegularTrade] HEDGING: NT BUY -> MT5 SELL"; Print(__log); ULogInfoPrint(__log); }
        } else {
            orderType = ORDER_TYPE_BUY;
            commentPrefix = EA_COMMENT_PREFIX_BUY;
            { string __log="ACHM_HEDGE_DEBUG: [ProcessRegularTrade] HEDGING: NT SELL -> MT5 BUY"; Print(__log); ULogInfoPrint(__log); }
        }
    } else {
        // Copy same direction
        if(__isBuy) {
            orderType = ORDER_TYPE_BUY;
            commentPrefix = EA_COMMENT_PREFIX_BUY;
            { string __log="ACHM_HEDGE_DEBUG: [ProcessRegularTrade] COPYING: NT BUY -> MT5 BUY"; Print(__log); ULogInfoPrint(__log); }
        } else {
            orderType = ORDER_TYPE_SELL;
            commentPrefix = EA_COMMENT_PREFIX_SELL;
            { string __log="ACHM_HEDGE_DEBUG: [ProcessRegularTrade] COPYING: NT SELL -> MT5 SELL"; Print(__log); ULogInfoPrint(__log); }
        }
    }

    { string __log = StringFormat("ACHM_HEDGE_DEBUG: [ProcessRegularTrade] Final orderType: %s", EnumToString(orderType)); Print(__log); ULogInfoPrint(__log); }

    // Calculate lot size based on mode
    double lotSize = CalculateLotSize(quantity, baseId, trade_json, orderType);
    { string __log = StringFormat("ACHM_HEDGE_DEBUG: [ProcessRegularTrade] Inverse tier=%d lotSize(before margin)=%.4f nt_daily_pnl=%.2f hasPnl=%d", (int)g_inversePnlTier, (double)lotSize, (double)g_NT_Daily_PnL, (int)g_hasNtDailyPnl); Print(__log); ULogInfoPrint(__log); }

    // Validate lot size
    double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double maxLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

    if(lotSize < minLot) {
    { string __log = StringFormat("ACHM_LOG: Calculated lot size %.8f is below minimum %.8f. Using minimum.", (double)lotSize, (double)minLot); Print(__log); ULogWarnPrint(__log); }
        lotSize = minLot;
    }
    if(lotSize > maxLot) {
    { string __log = StringFormat("ACHM_LOG: Calculated lot size %.8f exceeds maximum %.8f. Using maximum.", (double)lotSize, (double)maxLot); Print(__log); ULogWarnPrint(__log); }
        lotSize = maxLot;
    }

    // Round to lot step
    lotSize = NormalizeDouble(lotSize / lotStep, 0) * lotStep;

    // Margin-aware downscaling to avoid retcode 10019 (No money)
    double adjLot = AdjustLotForMargin(lotSize, orderType);
    if(adjLot < lotSize) {
        { string __log = StringFormat("ACHM_MARGIN: Downscaling lot due to free margin. Requested %.8f, adjusted %.8f", (double)lotSize, (double)adjLot); Print(__log); ULogWarnPrint(__log); }
    }
    if(adjLot <= 0) {
        { string __elog = StringFormat("ACHM_ERROR: Insufficient free margin to open even min lot for %s. Skipping hedge for base_id: %s", (string)_Symbol, (string)baseId); Print(__elog); ULogErrorPrint(__elog); }
        SubmitTradeResult("failed", 0, 0.0, false, baseId);
        return;
    }
    lotSize = adjLot;
    if(LotSizingMode == LOTS_INVERSE_PNL)
        g_inversePnlNextLot = lotSize;

    // Execute the trades - loop for multiple contracts
    string comment = commentPrefix + baseId;
    int contractNumMsg = GetJSONIntValue(trade_json, "contract_num", -1);
    int totalQuantityMsg = GetJSONIntValue(trade_json, "total_quantity", -1);
    string orderTypeMsg = GetJSONStringValue(trade_json, "\"order_type\"");
    bool isAggregateEntry = (orderTypeMsg == "ENTRY_AGG");
    // MULTI_HEDGE_FIX_V2: Per-contract messages create 1 hedge; ENTRY_AGG forces a single hedge.
    int totalContracts = (contractNumMsg >= 0 || isAggregateEntry ? 1 : (int)MathRound(quantity));
    int successfulTrades = 0;

    if(contractNumMsg >= 0)
    {
        string totalStr = (totalQuantityMsg > 0 ? (string)IntegerToString(totalQuantityMsg) : "unknown");
        { string __log = StringFormat("ACHM_LOG: Per-contract message: contract #%d of %s, base_id: %s", (int)(contractNumMsg + 1), (string)totalStr, (string)baseId); Print(__log); ULogInfoPrint(__log); }
    }
    else
    {
        string __log = StringFormat("ACHM_LOG: Need to open %d hedge trades for NT quantity %.8f", (int)totalContracts, (double)quantity);
        Print(__log); ULogInfoPrint(__log);
    }

    for(int i = 0; i < totalContracts; i++) {
        bool success = false;
        ulong orderTicket = 0;
        ulong dealId = 0;
        ulong positionTicket = 0;

        // Conservative retry: if broker returns NO_MONEY, step down lot and retry a few times
        double minLot = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
        double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
        if(lotStep <= 0) lotStep = 0.01;
        double sendLot = lotSize;
        int maxAttempts = 8;
        uint lastRetcode = 0;
        int lastError = 0;
        string lastRcDesc = "";
        string lastComment = "";

        for(int attempt = 0; attempt < maxAttempts && sendLot >= minLot; attempt++)
        {
            double slPrice = 0.0;
            double tpPrice = 0.0;
            if(orderType == ORDER_TYPE_BUY) {
                success = trade.Buy(sendLot, _Symbol, 0, slPrice, tpPrice, comment);
            } else {
                success = trade.Sell(sendLot, _Symbol, 0, slPrice, tpPrice, comment);
            }

            if(success) break;

            lastError = GetLastError();
            lastRetcode = trade.ResultRetcode();
            lastRcDesc = trade.ResultRetcodeDescription();
            lastComment = trade.ResultComment();

            // 10019 = TRADE_RETCODE_NO_MONEY
            // Avoid any implicit conversions by using explicit lowercase temp strings
            string lcDesc = lastRcDesc;
            StringToLower(lcDesc);
            string lcComment = lastComment;
            StringToLower(lcComment);
            int idxDesc = StringFind(lcDesc, "money");
            int idxComment = StringFind(lcComment, "money");
            bool noMoney = (lastRetcode == 10019) || (idxDesc >= 0) || (idxComment >= 0);
            if(noMoney)
            {
                string __log = StringFormat(
                    "ACHM_MARGIN: NO_MONEY retcode on send (retcode=%d, desc=%s) for lot=%.8f. Stepping down by lotStep=%.8f and retrying...",
                    (int)lastRetcode,
                    lastRcDesc,
                    (double)sendLot,
                    (double)lotStep
                );
                Print(__log); ULogWarnPrint(__log);
                // Step down to next lower step
                double next = MathFloor((sendLot - lotStep) / lotStep) * lotStep;
                if(next < minLot) { sendLot = 0.0; break; }
                sendLot = NormalizeDouble(next, 8);
                // Small backoff
                Sleep(25);
                continue;
            }

            // For other errors, don't loop excessively
            break;
        }

        if(success) {
            // Capture identifiers. Prefer position ticket for downstream mapping.
            orderTicket = trade.ResultOrder();
            dealId = trade.ResultDeal();
            if(dealId > 0 && HistoryDealSelect(dealId)) {
                positionTicket = (ulong)HistoryDealGetInteger(dealId, DEAL_POSITION_ID);
            }
            // Fallback: scan open positions for matching comment not already mapped
            if(positionTicket == 0) {
                int total_positions = PositionsTotal();
                for(int pi = total_positions - 1; pi >= 0; pi--) {
                    ulong pt = PositionGetTicket(pi);
                    if(pt == 0) continue;
                    if(!PositionSelectByTicket(pt)) continue;
                    string pc = PositionGetString(POSITION_COMMENT);
                    if(pc == comment) {
                        bool exists = false;
                        if(g_map_position_id_to_base_id != NULL) {
                            string tmp = "";
                            exists = g_map_position_id_to_base_id.TryGetValue((long)pt, tmp);
                        }
                        if(!exists) { positionTicket = pt; break; }
                    }
                }
            }
            if(positionTicket == 0) positionTicket = orderTicket; // last resort
            if(contractNumMsg >= 0 && totalQuantityMsg > 0)
            {
                string __log = StringFormat(
                    "ACHM_LOG: Successfully executed %s order #%I64u (pos %I64u) for %.2f lots (contract %d of %d), base_id: %s",
                    EnumToString(orderType),
                    (ulong)orderTicket,
                    (ulong)positionTicket,
                    (double)lotSize,
                    (int)(contractNumMsg + 1),
                    (int)totalQuantityMsg,
                    baseId
                );
                Print(__log); ULogInfoPrint(__log);
            }
            else
            {
                string __log = StringFormat(
                    "ACHM_LOG: Successfully executed %s order #%I64u (pos %I64u) for %.2f lots (trade %d of %d), base_id: %s",
                    EnumToString(orderType),
                    (ulong)orderTicket,
                    (ulong)positionTicket,
                    (double)lotSize,
                    (int)(i+1),
                    (int)totalContracts,
                    baseId
                );
                Print(__log); ULogInfoPrint(__log);
            }

            // Add to position tracking
            if(g_map_position_id_to_base_id != NULL && positionTicket > 0) {
                g_map_position_id_to_base_id.Add((long)positionTicket, baseId);
            }
            if(LotSizingMode == LOTS_INVERSE_PNL && g_inverse_tier_locks != NULL && positionTicket > 0) {
                int tier = g_inversePnlTier;
                if(tier <= 0)
                    tier = ResolveInversePnlTier();
                int existing = 0;
                if(g_inverse_tier_locks.TryGetValue((long)positionTicket, existing))
                    g_inverse_tier_locks.Remove((long)positionTicket);
                g_inverse_tier_locks.Add((long)positionTicket, tier);
            }

            // Submit success result for each trade
            SubmitTradeResult("success", positionTicket, lotSize, false, baseId);
            successfulTrades++;

        } else {
            int error = (lastError != 0 ? lastError : GetLastError());
            uint retcode = (lastRetcode != 0 ? lastRetcode : trade.ResultRetcode());
            string rcdesc = (lastRcDesc != "" ? lastRcDesc : trade.ResultRetcodeDescription());
            string rccmt = (lastComment != "" ? lastComment : trade.ResultComment());
            double triedLot = (sendLot > 0 ? sendLot : lotSize);
            string __elog = StringFormat(
                "ACHM_LOG: Failed to execute %s order %d of %d for base_id: %s. Lots: %.8f, Error: %d, Retcode: %u (%s), Comment: %s",
                EnumToString(orderType),
                (int)(i+1),
                (int)totalContracts,
                baseId,
                (double)triedLot,
                (int)error,
                (uint)retcode,
                rcdesc,
                rccmt
            );
            Print(__elog); ULogErrorPrint(__elog);
            // Provide a specific hint for common prohibition code 4756
            if(error == ERR_TRADE_NOT_ALLOWED)
            {
                string __hint = StringFormat(
                    "ACHM_HINT: Trading is prohibited (4756). Ensure global AutoTrading is ON, EA 'Allow algo trading' is enabled, and symbol %s is tradable and not Close-Only.",
                    _Symbol
                );
                Print(__hint); ULogWarnPrint(__hint);
            }
            SubmitTradeResult("failed", 0, triedLot, false, baseId);
        }

        // Small delay between trades to avoid overwhelming the broker
        if(i < totalContracts - 1) {
            Sleep(50);  // 50ms delay
        }
    }

    // Update global futures tracking based on successful trades
    if(action == "buy" || action == "BUY") {
        globalFutures += successfulTrades;
    } else {
        globalFutures -= successfulTrades;
    }

    // Force overlay recalculation
    ForceOverlayRecalculation();

    {
        string __log = StringFormat(
            "ACHM_LOG: Opened %d of %d requested hedge trades for base_id: %s",
            (int)successfulTrades,
            (int)totalContracts,
            baseId
        );
        Print(__log); ULogInfoPrint(__log);
    }
}

//+------------------------------------------------------------------+
//| Verify trading is permitted and return reason if not             |
//+------------------------------------------------------------------+
bool IsTradingPermitted(string &reason)
{
    // (Helper moved to top-level: TradeModeName)

    // Global terminal AutoTrading toggle
    if(!TerminalInfoInteger(TERMINAL_TRADE_ALLOWED))
    {
        reason = "Global AutoTrading is disabled (TERMINAL_TRADE_ALLOWED=false).";
        return false;
    }

    // EA-level permissions
    if(!MQLInfoInteger(MQL_TRADE_ALLOWED))
    {
        reason = "EA is not permitted to trade (MQL_TRADE_ALLOWED=false). Enable 'Allow algo trading' in EA properties.";
        return false;
    }

    // Some brokers expose this flag; if unavailable it will just be 0/ignored by compiler constants
    #ifdef ACCOUNT_TRADE_EXPERT
    if(AccountInfoInteger(ACCOUNT_TRADE_EXPERT) == 0)
    {
        reason = "Broker/account policy prohibits expert trading (ACCOUNT_TRADE_EXPERT=0).";
        return false;
    }
    #endif

    // Symbol trade mode checks
    long tradeMode = (long)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_MODE);
    if(tradeMode == SYMBOL_TRADE_MODE_DISABLED || tradeMode == SYMBOL_TRADE_MODE_CLOSEONLY)
    {
        string initialState = TradeModeName(tradeMode);
        // Attempt recovery: ensure symbol is selected (sometimes new accounts hide symbols)
        bool wasSelected = SymbolSelect(_Symbol, true);
        long refreshedMode = (long)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_MODE);
        if(refreshedMode != tradeMode)
        {
            string __rlog = StringFormat("ACHM_RECOVERY: Symbol %s trade mode changed %s -> %s after SymbolSelect(%d)", (string)_Symbol, (string)initialState, (string)TradeModeName(refreshedMode), (int)wasSelected); Print(__rlog); ULogWarnPrint(__rlog);
            // Re-evaluate if now tradable
            if(refreshedMode == SYMBOL_TRADE_MODE_FULL || refreshedMode == SYMBOL_TRADE_MODE_LONGONLY || refreshedMode == SYMBOL_TRADE_MODE_SHORTONLY)
            {
                return true; // recovered
            }
        }

        if(tradeMode == SYMBOL_TRADE_MODE_DISABLED)
        {
            reason = StringFormat("Symbol trade mode is DISABLED (mode=%s). Verify instrument permissions on this account or choose a different symbol.", (string)initialState);
        }
        else
        {
            reason = StringFormat("Symbol trade mode is CLOSE-ONLY (mode=%s, new positions not allowed).", (string)initialState);
        }
        // Emit detailed hint once per failure path
        string __hint = StringFormat("ACHM_HINT: %s | Check: Market Watch > Symbols > %s > Specifications. Ensure trading sessions are open, account type allows this symbol, and AutoTrading + EA algo trading are enabled.", (string)reason, (string)_Symbol); Print(__hint); ULogWarnPrint(__hint);
        return false;
    }

    // LONGONLY / SHORTONLY and FULL are allowed; direction constraints will be handled when sending order
    return true;
}

int DetermineInversePnlTier()
{
    double ntPnl = g_NT_Daily_PnL;
    bool havePnl = g_hasNtDailyPnl || (g_ntDataAvailable && ntPnl != 0.0);
    if(havePnl)
    {
        if(ntPnl <= Tier2_Limit)
            return 3;
        if(ntPnl <= Tier1_Limit)
            return 2;
    }

    // Missing PnL data or Safe Zone
    return 1;
}

// Helper: resolve tier from a specific PnL value without relying on global flags
int DetermineInversePnlTierFromValue(double ntPnl)
{
    if(ntPnl <= Tier2_Limit)
        return 3;
    if(ntPnl <= Tier1_Limit)
        return 2;
    return 1;
}

// Helper: prefer cached tier when PnL is missing, otherwise compute from latest PnL.
int ResolveInversePnlTier()
{
    double ntPnl = g_NT_Daily_PnL;
    bool havePnl = g_hasNtDailyPnl || (g_ntDataAvailable && MathAbs(ntPnl) > 0.01);
    if(havePnl)
        return DetermineInversePnlTierFromValue(ntPnl);

    if(g_inversePnlTier > 0)
        return g_inversePnlTier;

    return DetermineInversePnlTier();
}

double CalculateInversePnLLot(ENUM_ORDER_TYPE orderType)
{
    double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(minLot <= 0)  minLot = 0.01;
    if(maxLot <= 0)  maxLot = 1.0;
    if(lotStep <= 0) lotStep = 0.01;

    // Use resolved tier (cached when PnL present, otherwise fallback)
    int tier = ResolveInversePnlTier();
    double lotChoice = Tier1_Lots;

    if(tier == 2)
    {
        lotChoice = Tier2_Lots;
    }

    if(tier == 3)
    {
        double price = (orderType == ORDER_TYPE_BUY) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                                                     : SymbolInfoDouble(_Symbol, SYMBOL_BID);
        if(price <= 0) price = SymbolInfoDouble(_Symbol, SYMBOL_LAST);

        double marginPerLot = 0.0;
        double calcMargin = 0.0;
        if(OrderCalcMargin(orderType, _Symbol, 1.0, price, calcMargin))
            marginPerLot = calcMargin;
        if(marginPerLot <= 0.0 && g_brokerSpecs.marginRequired > 0.0)
            marginPerLot = g_brokerSpecs.marginRequired;

        double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
        double rawMaxLots = 0.0;
        if(marginPerLot > 0.0 && freeMargin > 0.0)
            rawMaxLots = freeMargin / marginPerLot;

        double safetyFactor = Safety_MaxMarginPct / 100.0;
        if(safetyFactor <= 0.0 || safetyFactor > 1.0)
            safetyFactor = 0.9;

        double safeMaxLots = rawMaxLots * safetyFactor;
        double cappedLots = (safeMaxLots > 0.0) ? MathMin(Tier3_MaxLots, safeMaxLots)
                                                : MathMin(Tier3_MaxLots, Tier2_Lots);

        // Do not shrink below Tier 2 unless margin forces it
        if(cappedLots < Tier2_Lots && safeMaxLots >= Tier2_Lots)
            cappedLots = Tier2_Lots;

        lotChoice = cappedLots;
    }

    lotChoice = MathMin(MathMax(lotChoice, minLot), maxLot);
    lotChoice = MathFloor(lotChoice / lotStep + 0.5) * lotStep;
    if(lotChoice < minLot) lotChoice = minLot;

    g_inversePnlTier = tier;
    g_inversePnlNextLot = lotChoice;
    { string __log = StringFormat("ACHM_TIER_DEBUG: tier=%d nt_daily_pnl=%.2f hasPnl=%d lot(before margin)=%.4f", (int)tier, (double)g_NT_Daily_PnL, (int)g_hasNtDailyPnl, (double)lotChoice); Print(__log); ULogInfoPrint(__log); }

    return lotChoice;
}

string GetMartingaleStateGlobalKey()
{
    return StringFormat("ACHM_MG_%I64d", ChartID());
}

void LoadMartingaleToggleState()
{
    string key = GetMartingaleStateGlobalKey();
    if(GlobalVariableCheck(key))
        g_martingale_enabled = (GlobalVariableGet(key) > 0.5);
    else
        g_martingale_enabled = false;
}

void SaveMartingaleToggleState()
{
    string key = GetMartingaleStateGlobalKey();
    GlobalVariableSet(key, g_martingale_enabled ? 1.0 : 0.0);
}

void EnsureMartingaleToggleButton()
{
    string name = MartingaleButtonObjName;
    if(ObjectFind(0, name) < 0)
    {
        if(!ObjectCreate(0, name, OBJ_BUTTON, 0, 0, 0))
            return;

        ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_LOWER);
        ObjectSetInteger(0, name, OBJPROP_XSIZE, 160);
        ObjectSetInteger(0, name, OBJPROP_YSIZE, 22);
        ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
        ObjectSetInteger(0, name, OBJPROP_BACK, false);
        ObjectSetInteger(0, name, OBJPROP_ZORDER, 100);
        ObjectSetString(0, name, OBJPROP_FONT, "Arial");
        ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 9);
    }

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_LOWER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, 10);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, 28);
}

void UpdateMartingaleToggleButton()
{
    EnsureMartingaleToggleButton();
    if(ObjectFind(0, MartingaleButtonObjName) < 0)
        return;

    bool fixedMode = (LotSizingMode == Fixed_Lot_Size);
    string text = g_martingale_enabled ? "Martingale: ON" : "Martingale: OFF";
    color bgColor = clrRed;
    color textColor = clrWhite;
    color borderColor = clrBlack;

    if(g_martingale_enabled && fixedMode)
    {
        bgColor = clrLime;
        textColor = clrBlack;
    }
    else if(g_martingale_enabled && !fixedMode)
    {
        text = "Martingale: ON (Fixed only)";
        bgColor = clrDarkOrange;
    }
    else if(!fixedMode)
    {
        bgColor = clrDimGray;
    }

    ObjectSetInteger(0, MartingaleButtonObjName, OBJPROP_STATE, g_martingale_enabled);
    ObjectSetInteger(0, MartingaleButtonObjName, OBJPROP_BGCOLOR, bgColor);
    ObjectSetInteger(0, MartingaleButtonObjName, OBJPROP_COLOR, textColor);
    ObjectSetInteger(0, MartingaleButtonObjName, OBJPROP_BORDER_COLOR, borderColor);
    ObjectSetString(0, MartingaleButtonObjName, OBJPROP_TEXT, text);
}

double NormalizeLotDownToStep(double lot, double step)
{
    if(lot <= 0.0)
        return 0.0;
    if(step <= 0.0)
        return NormalizeDouble(lot, 8);

    double units = MathFloor((lot / step) + 1e-8);
    if(units < 0.0)
        units = 0.0;
    return NormalizeDouble(units * step, 8);
}

bool GetLastOpenedEaPositionLot(double &outLot)
{
    outLot = 0.0;
    long latestTimeMsc = -1;
    long latestTicket = -1;

    int total = PositionsTotal();
    for(int i = 0; i < total; i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0 || !PositionSelectByTicket(ticket))
            continue;
        if(PositionGetString(POSITION_SYMBOL) != _Symbol)
            continue;
        if(PositionGetInteger(POSITION_MAGIC) != MagicNumber)
            continue;

        double volume = PositionGetDouble(POSITION_VOLUME);
        if(volume <= 0.0)
            continue;

        long openTimeMsc = PositionGetInteger(POSITION_TIME_MSC);
        if(openTimeMsc > latestTimeMsc || (openTimeMsc == latestTimeMsc && (long)ticket > latestTicket))
        {
            latestTimeMsc = openTimeMsc;
            latestTicket = (long)ticket;
            outLot = volume;
        }
    }

    return (outLot > 0.0);
}

double CalculateFixedLotSize()
{
    double lotSize = DefaultLot;
    if(LotSizingMode != Fixed_Lot_Size || !g_martingale_enabled)
        return lotSize;

    double previousLot = 0.0;
    if(!GetLastOpenedEaPositionLot(previousLot) || previousLot <= 0.0)
        return lotSize;

    if(MartingaleMode == Martingale_Multiplier)
        lotSize = previousLot * MartingaleValue;
    else
        lotSize = previousLot + MartingaleValue;

    if(lotSize < 0.0)
        lotSize = 0.0;

    double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(lotStep <= 0.0)
        lotStep = 0.01;
    lotSize = NormalizeLotDownToStep(lotSize, lotStep);

    { string __log=""; StringConcatenate(__log,
        "MARTINGALE_LOT: prev=", DoubleToString(previousLot, 4),
        " mode=", (MartingaleMode == Martingale_Multiplier ? "Multiplier" : "Addition"),
        " value=", DoubleToString(MartingaleValue, 4),
        " next=", DoubleToString(lotSize, 4));
      Print(__log); ULogInfoPrint(__log); }

    return lotSize;
}

double CalculateLotSize(double ntQuantity, const string& baseId, const string& trade_json, ENUM_ORDER_TYPE orderType)
{
    double lotSize = DefaultLot;

    switch(LotSizingMode) {
        case Fixed_Lot_Size:
            lotSize = CalculateFixedLotSize();
            break;

        case LOTS_INVERSE_PNL:
            lotSize = CalculateInversePnLLot(orderType);
            break;
    }

    return lotSize;
}

//+------------------------------------------------------------------+
//| Adjust lot size to fit available free margin                     |
//+------------------------------------------------------------------+
double AdjustLotForMargin(double desiredLot, ENUM_ORDER_TYPE orderType)
{
    // Broker constraints
    double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);
    if(lotStep <= 0) lotStep = 0.01; // fallback safety

    // Current price for margin calc
    double price = (orderType == ORDER_TYPE_BUY) ? SymbolInfoDouble(_Symbol, SYMBOL_ASK)
                                                : SymbolInfoDouble(_Symbol, SYMBOL_BID);
    if(price <= 0) price = SymbolInfoDouble(_Symbol, SYMBOL_LAST);

    double freeMargin = AccountInfoDouble(ACCOUNT_MARGIN_FREE);
    double safety = 0.85; // slightly more conservative headroom

    // Calculate margin for desired lot
    double margin = 0.0;
    bool ok = OrderCalcMargin(orderType, _Symbol, desiredLot, price, margin);
    if(!ok) {
        // Approximate required margin if OrderCalcMargin unavailable
        if(g_brokerSpecs.marginRequired > 0)
            margin = g_brokerSpecs.marginRequired * desiredLot;
    }

    if(margin > 0 && freeMargin >= margin * safety) {
        return MathMin(MathMax(desiredLot, minLot), maxLot);
    }

    // Compute scaled lot proportionally
    double scaled = desiredLot;
    if(margin > 0) {
        double ratio = (freeMargin * safety) / margin;
        scaled = desiredLot * MathMax(0.0, ratio);
    } else if(g_brokerSpecs.marginRequired > 0) {
        double perLot = g_brokerSpecs.marginRequired;
        scaled = (freeMargin * safety) / perLot;
    } else {
        // No way to estimate margin, give up
        return 0.0;
    }

    // Apply constraints and step rounding
    scaled = MathMin(MathMax(scaled, minLot), maxLot);
    if(scaled <= 0) return 0.0;
    scaled = MathFloor(scaled / lotStep) * lotStep;
    if(scaled < minLot) return 0.0;

    // Refine: ensure scaled lot actually fits margin (few iterations)
    for(int i=0; i<5; i++) {
        double m2 = 0.0;
        if(!OrderCalcMargin(orderType, _Symbol, scaled, price, m2)) {
            if(g_brokerSpecs.marginRequired > 0)
                m2 = g_brokerSpecs.marginRequired * scaled;
        }
        if(m2 > 0 && m2 > freeMargin * safety) {
            double next = MathMax(minLot, scaled - lotStep);
            next = MathFloor(next / lotStep) * lotStep;
            if(next >= scaled || next < minLot) { scaled = 0.0; break; }
            scaled = next;
        } else {
            break;
        }
    }

    return scaled;
}

//+------------------------------------------------------------------+
//| Trade Result Submission                                         |
//+------------------------------------------------------------------+
void SubmitTradeResult(const string& status, ulong ticket, double volume, bool isClose, const string& id)
{
    string result_json = "{";
    result_json += "\"status\":\"" + status + "\",";
    result_json += "\"ticket\":" + IntegerToString(ticket) + ",";
    result_json += "\"volume\":" + DoubleToString(volume, 2) + ",";
    result_json += "\"is_close\":" + (isClose ? "true" : "false") + ",";
    result_json += "\"id\":\"" + id + "\"";
    result_json += "}";

    int result = GrpcSubmitTradeResult(result_json);

    if(result != 0) {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        { string __log=""; StringConcatenate(__log, "Failed to submit trade result via gRPC. Error: ", result, " - ", error_msg); Print(__log); ULogErrorPrint(__log); }
    }
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
    Print("OnDeinit: Starting graceful cleanup... Reason: ", reason);
    Print("OnDeinit: Deinit reason codes: 0=Program, 1=Remove, 2=Recompile, 3=ChartClose, 4=Parameters, 5=Account, 6=Template, 7=Initfailed, 8=Close");
    SaveMartingaleToggleState();

    // CRITICAL FIX: Handle parameter changes without full shutdown
    if(reason == 4) { // REASON_PARAMETERS
    Print("OnDeinit: Parameter/settings change detected - performing minimal cleanup to preserve connection");
    Print("OnDeinit: EA will automatically restart with new parameters WHILE maintaining gRPC stability");
    ULogInfoPrint("PARAM_CHANGE_START: minimal deinit begin");

        // Only stop timer to prevent processing during restart
        EventKillTimer();
        Print("OnDeinit: Timer stopped (parameter change mode)");

    // IMPORTANT: Do NOT flip grpc_connected/grpc_streaming flags here to avoid transient "offline" state in logs/UI.
    // We also avoid any artificial sleeps; MT5 will immediately call OnInit with new params.
    // Mark that a param-change restart is pending so OnInit can prefer connection reuse
    g_param_change_restart = true;

    Print("OnDeinit: Minimal cleanup complete - gRPC connection preserved for quick restart");
    ULogInfoPrint("PARAM_CHANGE_END: minimal deinit complete");
        return; // Skip full shutdown - EA will restart with OnInit
    }

    // Full shutdown for other reasons (chart close, EA removal, etc.)
    Print("OnDeinit: Performing full cleanup...");
    // Step 1: Stop timer immediately to prevent new processing
    EventKillTimer();
    Print("OnDeinit: Timer stopped");

    // Step 2: Set global flag to stop all processing
    grpc_connected = false;
    grpc_streaming = false;

    // Step 3: Allow brief time for current operations to complete
    Sleep(50);

    // Step 4: Attempt graceful gRPC shutdown with timeout protection
    Print("OnDeinit: Attempting graceful gRPC shutdown...");

    // Try to stop streaming first (safer)
    int stream_stop_result = GrpcStopTradeStream();
    if(stream_stop_result != 0) {
        Print("OnDeinit: Trade stream stop returned: ", stream_stop_result, " (non-critical)");
    }

    // Brief pause before full shutdown
    Sleep(25);

    // Attempt graceful shutdown with error handling
    int shutdown_result = GrpcShutdown();
    if(shutdown_result != 0) {
        string error_msg = "Unknown error";
        GrpcGetLastError(error_msg, 1024);
        Print("OnDeinit: gRPC shutdown returned: ", shutdown_result, " - ", error_msg);
        Print("OnDeinit: This is normal if bridge was not connected");
    } else {
        Print("OnDeinit: gRPC connection shut down successfully");
    }

    // Step 5: Clean up UI elements (always safe)
    RemoveStatusIndicator();
    RemoveStatusOverlay();
    ObjectDelete(0, CandleCountdownObjName);
    ObjectDelete(0, MartingaleButtonObjName);
    Comment("");
    Print("OnDeinit: UI elements cleaned up");

    // Step 6: Clean up memory structures with enhanced safety
    if(CheckPointer(g_map_position_id_to_base_id) == POINTER_DYNAMIC) {
        Print("OnDeinit: Cleaning up position tracking map...");

        // Enhanced safety checks
        int mapCount = g_map_position_id_to_base_id.Count();
        if(mapCount >= 0 && mapCount < 10000) { // Sanity check
            long keys[];
            string values[];
            if(g_map_position_id_to_base_id.CopyTo(keys, values)) {
                Print("OnDeinit: Prepared to clean ", ArraySize(values), " map entries");
            }
            g_map_position_id_to_base_id.Clear();
        }
        delete g_map_position_id_to_base_id;
        g_map_position_id_to_base_id = NULL;
        Print("OnDeinit: Position tracking map cleaned up");
    }

    // Step 7: Final brief pause to ensure cleanup completion
    Sleep(25);  // Reduced from 200ms for faster shutdown

    Print("OnDeinit: EA shutdown complete - all resources cleaned up");
    Print("OnDeinit: EA can be safely removed or reloaded");
}

//+------------------------------------------------------------------+
//| Expert tick function                                             |
//+------------------------------------------------------------------+
void OnTick()
{
    // CRITICAL: Process gRPC trade queue
    ProcessGrpcTrades();

    // Add periodic connection checks
    static int health_check_counter = 0;
    health_check_counter++;
    if(health_check_counter >= 100) {
        health_check_counter = 0;
        CheckGrpcConnection();
    }

    // Throttle UI updates to reduce CPU usage - update every 10 ticks
    static int tick_counter = 0;
    static bool last_connection_status = false;
    tick_counter++;

    bool current_connection_status = grpc_connected;

    if(tick_counter >= 10 || current_connection_status != last_connection_status) {
        tick_counter = 0;
        last_connection_status = current_connection_status;

        string ea_name = MQLInfoString(MQL_PROGRAM_NAME);
        string ea_version = "3.00";
        string connection_status = current_connection_status ? "Connected" : "Disconnected";
        string martingale_status = g_martingale_enabled
            ? (LotSizingMode == Fixed_Lot_Size ? "ON" : "ON*")
            : "OFF";

        string stats_comment = StringFormat("%s v%s | %s | Balance: %.2f | Positions: %d | gRPC: %s | MG: %s",
                                            ea_name,
                                            ea_version,
                                            _Symbol,
                                            AccountInfoDouble(ACCOUNT_BALANCE),
                                            PositionsTotal(),
                                            connection_status,
                                            martingale_status);
        Comment(stats_comment);
        UpdateMartingaleToggleButton();

        if(current_connection_status)
            UpdateStatusIndicator("HedgeBot: gRPC Connected & Ready", clrLime);
        else
            UpdateStatusIndicator("HedgeBot: gRPC Disconnected", clrRed);
    }

    int totalPositionsSnapshot = PositionsTotal();
    if(totalPositionsSnapshot > 0) {
        for(int i = 0; i < totalPositionsSnapshot; i++)
        {
            ulong ticket = PositionGetTicket(i);
            if(ticket == 0)
                continue;
            if(!PositionSelectByTicket(ticket))
                continue;
            if(PositionGetInteger(POSITION_MAGIC) != MagicNumber || PositionGetString(POSITION_SYMBOL) != _Symbol)
                continue;

            string posComment = PositionGetString(POSITION_COMMENT);
            MANAGED_TRADE_KIND tradeKind = GetManagedTradeKindFromComment(posComment);
            if(tradeKind != ManagedTrade_PrimaryHedge)
                continue;

            ENUM_POSITION_TYPE positionType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
            if(positionType != POSITION_TYPE_BUY && positionType != POSITION_TYPE_SELL)
                continue;

            double entryPrice = PositionGetDouble(POSITION_PRICE_OPEN);
            double bidPrice = SymbolInfoDouble(_Symbol, SYMBOL_BID);
            double askPrice = SymbolInfoDouble(_Symbol, SYMBOL_ASK);
            double volume = PositionGetDouble(POSITION_VOLUME);
            double floatingProfit = PositionGetDouble(POSITION_PROFIT);
            double currentPrice = (positionType == POSITION_TYPE_BUY) ? bidPrice : askPrice;

            if(IsRunUpActiveForTicket(ticket))
            {
                UpdateRunUpTrailingForTicket(ticket, positionType, currentPrice);
            }
            else
            {
                HandleTier1DollarTrailingForPosition(ticket, positionType, entryPrice, currentPrice, volume);
                HandleTierFixedTrailingForPosition(ticket, positionType, entryPrice, currentPrice);
                if(SimpleStopLoss_Points > 0.0)
                    ApplySimpleStopLossIfNeeded(ticket, positionType, entryPrice);
            }

            string baseId = "";
            if(TryResolveBaseIdForTicket(ticket, baseId))
                EnsureCounterHedgeCoverage(ticket, baseId, positionType, entryPrice, currentPrice, floatingProfit, volume);
        }
    }

    CleanupRunUpStates();
    CleanupTierFixedTrailStates();
    CleanupTier1DollarTrailStates();
    CleanupCounterHedgeTracking();

    if(tick_counter == 0)
        UpdateStatusOverlay();

    static datetime g_last_maintenance = 0;
    static datetime g_last_integrity_check = 0;
    const int MAINTENANCE_INTERVAL = 60;
    const int INTEGRITY_CHECK_INTERVAL = 300;

    datetime current_time = TimeCurrent();

    if(current_time - g_last_maintenance >= MAINTENANCE_INTERVAL) {
        g_last_maintenance = current_time;

        if(GrpcIsConnected() != 1) {
            { string __log="gRPC connection lost, attempting reconnection..."; Print(__log); ULogWarnPrint(__log); }
            ReconnectGrpc();
        }

        if(!g_broker_specs_ready)
            UpdateStatusIndicator("Specs...", clrOrange);
    }

    if(current_time - g_last_integrity_check >= INTEGRITY_CHECK_INTERVAL) {
        g_last_integrity_check = current_time;

        if(!ValidateArrayIntegrity(false)) {
            { string __log=""; StringConcatenate(__log, "CRITICAL_ARRAY_CORRUPTION: Array integrity check failed at ", TimeToString(current_time)); Print(__log); ULogErrorPrint(__log); }
            ValidateArrayIntegrity(true);
        }

        CleanupNotificationTracking();
        CleanupClosedBaseIdTracking();
    }
}
void OnChartEvent(const int id,
                  const long &lparam,
                  const double &dparam,
                  const string &sparam)
{
    if(id != CHARTEVENT_OBJECT_CLICK || sparam != MartingaleButtonObjName)
        return;

    g_martingale_enabled = !g_martingale_enabled;
    SaveMartingaleToggleState();
    UpdateMartingaleToggleButton();
    ChartRedraw(0);

    { string __log=""; StringConcatenate(__log,
        "MARTINGALE_TOGGLE: ",
        (g_martingale_enabled ? "ON" : "OFF"),
        " lotMode=",
        (LotSizingMode == Fixed_Lot_Size ? "Fixed_Lot_Size" : "LOTS_INVERSE_PNL"));
      Print(__log); ULogInfoPrint(__log); }
}

//+------------------------------------------------------------------+
//| OnTradeTransaction - Handle trade transactions for closure detection |
//+------------------------------------------------------------------+
void OnTradeTransaction(const MqlTradeTransaction& trans,
                       const MqlTradeRequest& request,
                       const MqlTradeResult& result)
{
    { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Transaction detected - Type: ", (int)trans.type,
          ", Deal: ", trans.deal, ", Order: ", trans.order, ", Position: ", trans.position); Print(__log); ULogInfoPrint(__log); }

    if(trans.type != TRADE_TRANSACTION_DEAL_ADD)
        return;
    if(trans.deal == 0) {
        { string __log="CLOSURE_DEBUG: Skipping - Deal ID is 0"; Print(__log); ULogInfoPrint(__log); }
        return;
    }
    if(!HistoryDealSelect(trans.deal)) {
        { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Failed to select deal: ", trans.deal); Print(__log); ULogWarnPrint(__log); }
        return;
    }

    long deal_magic = HistoryDealGetInteger(trans.deal, DEAL_MAGIC);
    { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Deal magic: ", deal_magic, ", EA magic: ", MagicNumber); Print(__log); ULogInfoPrint(__log); }

    ENUM_DEAL_TYPE deal_type = (ENUM_DEAL_TYPE)HistoryDealGetInteger(trans.deal, DEAL_TYPE);
    ENUM_DEAL_ENTRY deal_entry = (ENUM_DEAL_ENTRY)HistoryDealGetInteger(trans.deal, DEAL_ENTRY);
    string deal_comment = HistoryDealGetString(trans.deal, DEAL_COMMENT);
    ulong position_ticket = HistoryDealGetInteger(trans.deal, DEAL_POSITION_ID);
    double deal_volume = HistoryDealGetDouble(trans.deal, DEAL_VOLUME);

    { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Deal details - Type: ", (int)deal_type,
          ", Entry: ", (int)deal_entry, ", Magic: ", deal_magic, ", Comment: ", deal_comment); Print(__log); ULogInfoPrint(__log); }

    if(deal_magic != MagicNumber) {
        { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Magic mismatch - Deal: ", deal_magic,
              ", EA: ", MagicNumber, " - Continuing anyway"); Print(__log); ULogWarnPrint(__log); }
    }

    if(deal_entry != DEAL_ENTRY_OUT) {
        { string __log=""; StringConcatenate(__log, "CLOSURE_DEBUG: Skipping - Not an exit deal. Entry type: ", (int)deal_entry); Print(__log); ULogInfoPrint(__log); }
        return;
    }

    { string __log=""; StringConcatenate(__log, "CLOSURE_DETECTION: Position closed - Ticket: ", position_ticket,
          ", Volume: ", deal_volume, ", Comment: ", deal_comment); Print(__log); ULogInfoPrint(__log); }

    string managedComment = deal_comment;
    if(managedComment == "" && PositionSelectByTicket(position_ticket))
        managedComment = PositionGetString(POSITION_COMMENT);

    MANAGED_TRADE_KIND tradeKind = GetManagedTradeKindFromComment(managedComment);
    if(tradeKind == ManagedTrade_None)
    {
        ulong parentTicket = 0;
        if(TryResolveCounterParentTicket(position_ticket, parentTicket))
            tradeKind = ManagedTrade_CounterHedge;
    }

    string baseId = "";
    TryExtractManagedBaseIdFromComment(managedComment, baseId);
    if(baseId == "")
        TryResolveBaseIdForTicket(position_ticket, baseId);

    if(tradeKind == ManagedTrade_CounterHedge)
    {
        RemoveCounterHedgeTracking(position_ticket);
        { string __log=""; StringConcatenate(__log, "COUNTER_HEDGE_DETECTION: Closed Counter-Hedge ticket ", (long)position_ticket,
              " base_id=", baseId, ". Skipping bridge notification."); Print(__log); ULogInfoPrint(__log); }
        return;
    }

    if(baseId == "") {
        { string __log; StringConcatenate(__log, "CLOSURE_DETECTION: Could not determine canonical BaseID for ticket ", position_ticket,
              ". Skipping MT5->Bridge closure notification to avoid mismatched base_id."); Print(__log); ULogErrorPrint(__log); }
        return;
    }

    { string __log; StringConcatenate(__log, "CLOSURE_DETECTION: Extracted BaseID: ", baseId, " from closed position"); Print(__log); ULogInfoPrint(__log); }

    bool hadSimpleSLFlag = false;
    if(g_simple_sl_tickets != NULL)
    {
        int _dummy = 0;
        hadSimpleSLFlag = g_simple_sl_tickets.TryGetValue((long)position_ticket, _dummy);
    }

    double lockedSimpleSL = 0.0;
    bool hasLockedSimpleSL = false;
    if(g_inverse_sl_locks != NULL)
        hasLockedSimpleSL = g_inverse_sl_locks.TryGetValue((long)position_ticket, lockedSimpleSL);

    string closure_reason = "MT5_position_closed";
    string commentLower = managedComment;
    StringToLower(commentLower);

    bool isStopLoss = (StringFind(commentLower, "[sl") >= 0 || StringFind(commentLower, "stop loss") >= 0);
    bool isTakeProfit = (StringFind(commentLower, "[tp") >= 0 || StringFind(commentLower, "take profit") >= 0);

    if(isStopLoss)
    {
        double stopPrice = ExtractStopPriceFromDealComment(managedComment);
        bool isSimpleStop = false;
        if(hadSimpleSLFlag && hasLockedSimpleSL && stopPrice > 0.0 && lockedSimpleSL > 0.0)
        {
            double tol = _Point * 5.0;
            if(MathAbs(stopPrice - lockedSimpleSL) <= tol)
                isSimpleStop = true;
        }

        closure_reason = isSimpleStop ? "mt5_simple_sl" : "MT5_stop_loss";

        { string __log=""; StringConcatenate(__log,
              "CLOSURE_REASON_DEBUG: ticket=", (long)position_ticket,
              " stopPx=", DoubleToString(stopPrice, _Digits),
              " lockedSimpleSL=", DoubleToString(lockedSimpleSL, _Digits),
              " hadSimpleFlag=", (int)hadSimpleSLFlag,
              " -> reason=", closure_reason);
          Print(__log); ULogInfoPrint(__log); }
    }
    else if(isTakeProfit)
    {
        closure_reason = "MT5_take_profit";
    }

    bool runUpActive = IsRunUpActiveForTicket(position_ticket);
    if(runUpActive)
        closure_reason = "mt5_runup_close";

    RemoveRunUpState((ulong)position_ticket);
    RemoveTierFixedTrailState((ulong)position_ticket);
    if(g_inverse_sl_locks != NULL)
        g_inverse_sl_locks.Remove((long)position_ticket);
    if(g_inverse_tier_locks != NULL)
        g_inverse_tier_locks.Remove((long)position_ticket);
    if(g_simple_sl_tickets != NULL)
        g_simple_sl_tickets.Remove((long)position_ticket);

    CloseLinkedCounterHedges(position_ticket, baseId, closure_reason);

    string dedupKey = baseId + ":" + StringFormat("%I64u", position_ticket);
    if(HasNotificationBeenSent(dedupKey, "hedge_close") ||
       HasNotificationBeenSent(dedupKey, "hedge_close_pending"))
    {
        { string __log; StringConcatenate(__log, "CLOSURE_DETECTION: Skipping generic MT5_position_closed for ", dedupKey,
              " because a specific hedge_close was already sent."); Print(__log); ULogInfoPrint(__log); }
    }
    else
    {
        NotifyMT5PositionClosure(baseId, position_ticket, deal_volume, closure_reason);
    }
}
//+------------------------------------------------------------------+
//| Notify Bridge Server of MT5 position closure                   |
//+------------------------------------------------------------------+
void NotifyMT5PositionClosure(string baseId, ulong mt5Ticket, double volume, string closureReason)
{
    { string __log=""; StringConcatenate(__log, "CLOSURE_NOTIFICATION: Notifying bridge of MT5 closure - BaseID: ", baseId,
          ", Ticket: ", mt5Ticket, ", Reason: ", closureReason); Print(__log); ULogInfoPrint(__log); }

    // Create hedge close notification JSON
    string notification_json = StringFormat(
        "{"
        "\"event_type\":\"HEDGE_CLOSE\","
        "\"base_id\":\"%s\","
        "\"nt_instrument_symbol\":\"%s\","
        "\"nt_account_name\":\"MT5_Account\","
        "\"closed_hedge_quantity\":%.2f,"
        "\"closed_hedge_action\":\"%s\","
        "\"timestamp\":\"%s\","
        "\"closure_reason\":\"%s\","
        "\"mt5_ticket\":%d"
        "}",
        baseId,
        _Symbol,
        volume,
        volume > 0 ? "SELL" : "BUY",  // Opposite of original direction
        TimeToString(TimeCurrent(), TIME_DATE|TIME_MINUTES|TIME_SECONDS),
        closureReason,
        mt5Ticket
    );

    { string __log=""; StringConcatenate(__log, "CLOSURE_NOTIFICATION: Sending notification JSON: ", notification_json); Print(__log); ULogInfoPrint(__log); }

    // Send via gRPC
    int result = GrpcNotifyHedgeClose(notification_json);
    if(result == 0) {
        { string __log=""; StringConcatenate(__log, "CLOSURE_NOTIFICATION: Successfully sent MT5 closure notification for BaseID: ", baseId); Print(__log); ULogInfoPrint(__log); }
        string dedupKey = baseId + ":" + StringFormat("%I64u", mt5Ticket);
        MarkNotificationSent(dedupKey, "hedge_close");
    } else {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        { string __log=""; StringConcatenate(__log, "CLOSURE_NOTIFICATION: Failed to send closure notification. Error: ", result, " - ", error_msg); Print(__log); ULogErrorPrint(__log); }
    }

    if(g_map_position_id_to_base_id != NULL)
        g_map_position_id_to_base_id.Remove((long)mt5Ticket);
    RemoveOpenPositionTracking(mt5Ticket);
}

double ExtractStopPriceFromDealComment(const string &dealComment)
{
    if(dealComment == "")
        return 0.0;

    string lower = dealComment;
    StringToLower(lower);

    int slPos = StringFind(lower, "[sl");
    if(slPos < 0)
        return 0.0;

    int endPos = StringFind(lower, "]", slPos);
    if(endPos < 0)
        endPos = StringLen(lower);

    int i = slPos + 3; // after "[sl"
    while(i < endPos)
    {
        ushort c = (ushort)StringGetCharacter(lower, i);
        if(c == ' ' || c == '\t' || c == ':')
        {
            i++;
            continue;
        }
        break;
    }

    if(i >= endPos)
        return 0.0;

    string num = "";
    for(int j = i; j < endPos; j++)
    {
        ushort c = (ushort)StringGetCharacter(lower, j);
        if((c >= '0' && c <= '9') || c == '.' || c == '-')
        {
            num += StringSubstr(lower, j, 1);
            continue;
        }
        // Stop at first non-numeric after we have started capturing
        if(num != "")
            break;
    }

    if(num == "")
        return 0.0;

    return StringToDouble(num);
}

//+------------------------------------------------------------------+
//| Array Integrity Validation Functions                            |
//+------------------------------------------------------------------+
bool ValidateArrayIntegrity(bool log_details = false)
{
    int pos_ids_size = ArraySize(g_open_mt5_pos_ids);
    int actions_size = ArraySize(g_open_mt5_actions);
    int base_ids_size = ArraySize(g_open_mt5_base_ids);
    int nt_symbols_size = ArraySize(g_open_mt5_nt_symbols);
    int nt_accounts_size = ArraySize(g_open_mt5_nt_accounts);
    int orig_nt_actions_size = ArraySize(g_open_mt5_original_nt_actions);
    int orig_nt_qty_size = ArraySize(g_open_mt5_original_nt_quantities);

    bool integrity_ok = true;

    if(log_details) {
        Print("ARRAY_INTEGRITY_CHECK: Array sizes - pos_ids=", pos_ids_size,
              ", actions=", actions_size, ", base_ids=", base_ids_size,
              ", nt_symbols=", nt_symbols_size, ", nt_accounts=", nt_accounts_size,
              ", orig_actions=", orig_nt_actions_size, ", orig_qty=", orig_nt_qty_size);
    }

    // Check if all arrays have the same size
    if(actions_size != pos_ids_size || base_ids_size != pos_ids_size ||
       nt_symbols_size != pos_ids_size || nt_accounts_size != pos_ids_size ||
       orig_nt_actions_size != pos_ids_size || orig_nt_qty_size != pos_ids_size) {

        integrity_ok = false;
        { string __log=""; StringConcatenate(__log, "ARRAY_INTEGRITY_ERROR: Size mismatch detected! Expected all arrays to have size ", pos_ids_size); Print(__log); ULogErrorPrint(__log); }
        Print("ARRAY_INTEGRITY_ERROR: Actual sizes - actions=", actions_size,
              ", base_ids=", base_ids_size, ", nt_symbols=", nt_symbols_size,
              ", nt_accounts=", nt_accounts_size, ", orig_actions=", orig_nt_actions_size,
              ", orig_qty=", orig_nt_qty_size);
    }

    // Enhanced content validation: Check for invalid data in all parallel arrays
    for(int i = 0; i < MathMin(pos_ids_size, actions_size); i++) {
        if(g_open_mt5_actions[i] == "") {
            integrity_ok = false;
            Print("ARRAY_INTEGRITY_ERROR: Empty action at index ", i, " (PosID: ", g_open_mt5_pos_ids[i], ")");
        }
        if(g_open_mt5_base_ids[i] == "") {
            integrity_ok = false;
            Print("ARRAY_INTEGRITY_ERROR: Empty base_id at index ", i, " (PosID: ", g_open_mt5_pos_ids[i], ")");
        }
        if(g_open_mt5_pos_ids[i] <= 0) {
            integrity_ok = false;
            Print("ARRAY_INTEGRITY_ERROR: Invalid position ID at index ", i, " (PosID: ", g_open_mt5_pos_ids[i], ")");
        }
    }

    return integrity_ok;
}

//+------------------------------------------------------------------+
//| Clean up old closed base_id tracking entries                    |
//+------------------------------------------------------------------+
void CleanupClosedBaseIdTracking()
{
    datetime current_time = TimeCurrent();
    int cleanup_threshold = 300; // 5 minutes

    for(int i = ArraySize(g_closed_base_ids) - 1; i >= 0; i--)
    {
        if(current_time - g_closed_base_timestamps[i] > cleanup_threshold)
        {
            // Remove old entry
            for(int j = i; j < ArraySize(g_closed_base_ids) - 1; j++)
            {
                g_closed_base_ids[j] = g_closed_base_ids[j + 1];
                g_closed_base_timestamps[j] = g_closed_base_timestamps[j + 1];
            }
            ArrayResize(g_closed_base_ids, ArraySize(g_closed_base_ids) - 1);
            ArrayResize(g_closed_base_timestamps, ArraySize(g_closed_base_timestamps) - 1);

            Print("TRAILING_STOP_IGNORE: Cleaned up old closed base_id tracking entry. Remaining: ", ArraySize(g_closed_base_ids));
        }
    }
}

//+------------------------------------------------------------------+
//| Clean up old notification tracking entries                      |
//+------------------------------------------------------------------+
void CleanupNotificationTracking()
{
    datetime current_time = TimeCurrent();
    int cleanup_threshold = 300; // 5 minutes

    for(int i = ArraySize(g_notified_base_ids) - 1; i >= 0; i--)
    {
        if(current_time - g_notified_timestamps[i] > cleanup_threshold)
        {
            // Remove old entry
            for(int j = i; j < ArraySize(g_notified_base_ids) - 1; j++)
            {
                g_notified_base_ids[j] = g_notified_base_ids[j + 1];
                g_notified_timestamps[j] = g_notified_timestamps[j + 1];
            }
            ArrayResize(g_notified_base_ids, ArraySize(g_notified_base_ids) - 1);
            ArrayResize(g_notified_timestamps, ArraySize(g_notified_timestamps) - 1);

            Print("COMPREHENSIVE_DUPLICATE_PREVENTION: Cleaned up old notification tracking entry. Remaining: ", ArraySize(g_notified_base_ids));
        }
    }
}

//+------------------------------------------------------------------+
//| Clean up completed trade groups                                 |
//+------------------------------------------------------------------+
void CleanupTradeGroups()
{
    Print("ACHM_DIAG: [CleanupTradeGroups] Starting cleanup. Current g_baseIds size: ", ArraySize(g_baseIds));
    int arraySize = ArraySize(g_baseIds);
    if(arraySize == 0) return;  // Nothing to clean up

    int keepCount = 0;
    bool groupsToKeep[]; // Temp array to mark groups to keep
    if(arraySize > 0) ArrayResize(groupsToKeep, arraySize);

    for(int i = 0; i < arraySize; i++)
    {
        bool nt_fills_complete = g_isComplete[i];
        // Ensure index is valid for new arrays before accessing
        bool mt5_hedges_opened_exist = (i < ArraySize(g_mt5HedgesOpenedCount) && g_mt5HedgesOpenedCount[i] > 0);
        bool all_mt5_hedges_closed = (i < ArraySize(g_mt5HedgesClosedCount) && i < ArraySize(g_mt5HedgesOpenedCount) &&
                                      g_mt5HedgesClosedCount[i] >= g_mt5HedgesOpenedCount[i]);

        // Keep if NT not complete, OR if NT is complete but MT5 side is not fully resolved
        if (!nt_fills_complete || (nt_fills_complete && mt5_hedges_opened_exist && !all_mt5_hedges_closed) ) {
            groupsToKeep[i] = true;
            keepCount++;
            Print("ACHM_DIAG: [CleanupTradeGroups] KEEPING group with base_id: '", g_baseIds[i], "' at index ", i,
                  ". NT_Complete: ", nt_fills_complete,
                  ", MT5_Opened_Exist: ", mt5_hedges_opened_exist,
                  ", MT5_All_Closed: ", all_mt5_hedges_closed,
                  ", Opened: ", (i < ArraySize(g_mt5HedgesOpenedCount) ? (string)g_mt5HedgesOpenedCount[i] : "N/A"),
                  ", Closed: ", (i < ArraySize(g_mt5HedgesClosedCount) ? (string)g_mt5HedgesClosedCount[i] : "N/A"));
        } else {
            groupsToKeep[i] = false; // Mark for removal
            Print("ACHM_DIAG: [CleanupTradeGroups] Eligible for REMOVAL group with base_id: '", g_baseIds[i], "' at index ", i,
                  ". NT_Complete: ", nt_fills_complete,
                  ", MT5_Opened_Exist: ", mt5_hedges_opened_exist,
                  ", MT5_All_Closed: ", all_mt5_hedges_closed,
                  ", Opened: ", (i < ArraySize(g_mt5HedgesOpenedCount) ? (string)g_mt5HedgesOpenedCount[i] : "N/A"),
                  ", Closed: ", (i < ArraySize(g_mt5HedgesClosedCount) ? (string)g_mt5HedgesClosedCount[i] : "N/A"));
        }
    }

    if(keepCount < arraySize) // If there are groups to remove
    {
        string tempBaseIds[];
        int tempTotalQty[];
        int tempProcessedQty[];
        string tempActions[];
        bool tempComplete[];
        string tempNtSymbols[];
        string tempNtAccounts[];
        int tempMt5Opened[];
        int tempMt5Closed[];
        bool tempIsMT5Opened[];
        bool tempIsMT5Closed[];

        if(keepCount > 0)
        {
            ArrayResize(tempBaseIds, keepCount);
            ArrayResize(tempTotalQty, keepCount);
            ArrayResize(tempProcessedQty, keepCount);
            ArrayResize(tempActions, keepCount);
            ArrayResize(tempComplete, keepCount);
            ArrayResize(tempNtSymbols, keepCount);
            ArrayResize(tempNtAccounts, keepCount);
            ArrayResize(tempMt5Opened, keepCount);
            ArrayResize(tempMt5Closed, keepCount);
            ArrayResize(tempIsMT5Opened, keepCount);
            ArrayResize(tempIsMT5Closed, keepCount);

            int newIndex = 0;
            for(int i = 0; i < arraySize; i++)
            {
                if(groupsToKeep[i]) // If marked to keep
                {
                    tempBaseIds[newIndex] = g_baseIds[i];
                    tempTotalQty[newIndex] = g_totalQuantities[i];
                    tempProcessedQty[newIndex] = g_processedQuantities[i];
                    tempActions[newIndex] = g_actions[i];
                    tempComplete[newIndex] = g_isComplete[i];
                    if (i < ArraySize(g_ntInstrumentSymbols)) tempNtSymbols[newIndex] = g_ntInstrumentSymbols[i]; else tempNtSymbols[newIndex] = "";
                    if (i < ArraySize(g_ntAccountNames)) tempNtAccounts[newIndex] = g_ntAccountNames[i]; else tempNtAccounts[newIndex] = "";
                    if (i < ArraySize(g_mt5HedgesOpenedCount)) tempMt5Opened[newIndex] = g_mt5HedgesOpenedCount[i]; else tempMt5Opened[newIndex] = 0;
                    if (i < ArraySize(g_mt5HedgesClosedCount)) tempMt5Closed[newIndex] = g_mt5HedgesClosedCount[i]; else tempMt5Closed[newIndex] = 0;
                    if (i < ArraySize(g_isMT5Opened)) tempIsMT5Opened[newIndex] = g_isMT5Opened[i]; else tempIsMT5Opened[newIndex] = false;
                    if (i < ArraySize(g_isMT5Closed)) tempIsMT5Closed[newIndex] = g_isMT5Closed[i]; else tempIsMT5Closed[newIndex] = false;
                    newIndex++;
                }
            }
        }

        ArrayFree(g_baseIds);
        ArrayFree(g_totalQuantities);
        ArrayFree(g_processedQuantities);
        ArrayFree(g_actions);
        ArrayFree(g_isComplete);
        ArrayFree(g_ntInstrumentSymbols);
        ArrayFree(g_ntAccountNames);
        ArrayFree(g_mt5HedgesOpenedCount);
        ArrayFree(g_mt5HedgesClosedCount);
        ArrayFree(g_isMT5Opened);
        ArrayFree(g_isMT5Closed);

        if(keepCount > 0)
        {
            ArrayCopy(g_baseIds, tempBaseIds);
            ArrayCopy(g_totalQuantities, tempTotalQty);
            ArrayCopy(g_processedQuantities, tempProcessedQty);
            ArrayCopy(g_actions, tempActions);
            ArrayCopy(g_isComplete, tempComplete);
            ArrayCopy(g_ntInstrumentSymbols, tempNtSymbols);
            ArrayCopy(g_ntAccountNames, tempNtAccounts);
            ArrayCopy(g_mt5HedgesOpenedCount, tempMt5Opened);
            ArrayCopy(g_mt5HedgesClosedCount, tempMt5Closed);
            ArrayCopy(g_isMT5Opened, tempIsMT5Opened);
            ArrayCopy(g_isMT5Closed, tempIsMT5Closed);
        }
        else // No groups to keep, so resize all to 0
        {
            ArrayResize(g_baseIds, 0);
            ArrayResize(g_totalQuantities, 0);
            ArrayResize(g_processedQuantities, 0);
            ArrayResize(g_actions, 0);
            ArrayResize(g_isComplete, 0);
            ArrayResize(g_ntInstrumentSymbols, 0);
            ArrayResize(g_ntAccountNames, 0);
            ArrayResize(g_mt5HedgesOpenedCount, 0);
            ArrayResize(g_mt5HedgesClosedCount, 0);
            ArrayResize(g_isMT5Opened, 0);
            ArrayResize(g_isMT5Closed, 0);
        }
    } else {
         Print("ACHM_DIAG: [CleanupTradeGroups] No groups eligible for removal based on new criteria. Current count: ", arraySize);
    }
    if(arraySize > 0) ArrayFree(groupsToKeep); // Free the temporary boolean array
}

//+------------------------------------------------------------------+
//| Reset all trade group arrays                                    |
//+------------------------------------------------------------------+
void ResetTradeGroups()
{
    Print("DEBUG: Resetting all trade group arrays");
    ArrayResize(g_baseIds, 0);
    ArrayResize(g_totalQuantities, 0);
    ArrayResize(g_processedQuantities, 0);
    ArrayResize(g_actions, 0);
    ArrayResize(g_isComplete, 0);
    ArrayResize(g_ntInstrumentSymbols, 0);
    ArrayResize(g_ntAccountNames, 0);
    ArrayResize(g_mt5HedgesOpenedCount, 0);
    ArrayResize(g_mt5HedgesClosedCount, 0);
    ArrayResize(g_isMT5Opened, 0);
    ArrayResize(g_isMT5Closed, 0);

    Print("DEBUG: All trade group arrays reset to size 0");
}

//+------------------------------------------------------------------+
//| Stop-loss helpers                                               |
//+------------------------------------------------------------------+
double GetStopLossDistance()
{
    double brokerMinPts = GetBrokerMinimumStopPoints();
    // LOTS_INVERSE_PNL: tier-specific stop based on simple SL for tier 1, overrides for tiers 2/3.
    if(LotSizingMode == LOTS_INVERSE_PNL)
    {
        double basePts = (SimpleStopLoss_Points > 0.0) ? SimpleStopLoss_Points : 0.0;
        int tier = ResolveInversePnlTier(); // always resolve from latest NT data instead of stale cache
        g_inversePnlTier = tier;            // keep cached tier aligned for downstream logging
        if(tier == 2 && Tier2_InitialSL_Points > 0.0)
            basePts = Tier2_InitialSL_Points;
        else if(tier == 3 && Tier3_InitialSL_Points > 0.0)
            basePts = Tier3_InitialSL_Points;

        double effectivePts = MathMax(basePts, brokerMinPts);
        if(effectivePts <= 0.0)
            effectivePts = brokerMinPts;
        return effectivePts * _Point;
    }

    if(SimpleStopLoss_Points > 0.0)
    {
        double pts = MathMax(SimpleStopLoss_Points, brokerMinPts);
        return pts * _Point;
    }

    double effectiveMin = brokerMinPts > 0 ? brokerMinPts * _Point : 100 * _Point;
    return effectiveMin;
}

double GetBrokerMinimumStopPoints()
{
    int stopLevel = (int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_STOPS_LEVEL);
    int freezeLevel = (int)SymbolInfoInteger(_Symbol, SYMBOL_TRADE_FREEZE_LEVEL);
    int minLevel = MathMax(stopLevel, freezeLevel);
    if(minLevel <= 0)
        minLevel = 1;
    return (double)minLevel;
}

//+------------------------------------------------------------------+
//| JSON Parsing Helper Functions                                   |
//| Note: Still needed for MQL5 C# DLL interface                    |
//+------------------------------------------------------------------+
double GetJSONDouble(string json, string key)
{
    string searchKey = "\"" + key + "\"";
    int keyPos = StringFind(json, searchKey);
    if(keyPos == -1)
        return 0.0;

    int colonPos = StringFind(json, ":", keyPos);
    if(colonPos == -1)
        return 0.0;

    int start = colonPos + 1;
    // Skip whitespace characters
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if(ch != ' ' && ch != '\t' && ch != '\n' && ch != '\r')
            break;
        start++;
    }

    // Build the numeric string
    string numStr = "";
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if((ch >= '0' && ch <= '9') || ch == '.' || ch == '-')
        {
            numStr += CharToString((uchar)ch);
            start++;
        }
        else
            break;
    }

    return StringToDouble(numStr);
}

//+------------------------------------------------------------------+
//| Extract double value from JSON string with default               |
//+------------------------------------------------------------------+
double GetJSONDoubleValue(string json, string key, double defaultValue)
{
    string searchKey = "\"" + key + "\"";
    int keyPos = StringFind(json, searchKey);
    if(keyPos == -1) {
        return defaultValue;
    }

    // Search for colon after the key to avoid preceding matches
    int colonPos = StringFind(json, ":", keyPos + StringLen(searchKey));
    if(colonPos == -1) {
        return defaultValue;
    }

    int start = colonPos + 1;
    // Skip whitespace characters
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if(ch != ' ' && ch != '\t' && ch != '\n' && ch != '\r')
            break;
        start++;
    }

    if(start >= StringLen(json)) {
        return defaultValue;
    }

    // Build the numeric string (supports sign and decimal)
    string numStr = "";
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if((ch >= '0' && ch <= '9') || ch == '.' || ch == '-' || ch == '+')
        {
            numStr += CharToString((uchar)ch);
            start++;
        }
        else
            break;
    }

    if(numStr == "") {
        return defaultValue;
    }

    return StringToDouble(numStr);
}

//+------------------------------------------------------------------+
//| Extract integer value from JSON string                          |
//+------------------------------------------------------------------+
int GetJSONIntValue(string json, string key, int defaultValue)
{
    string searchKey = "\"" + key + "\"";
    int keyPos = StringFind(json, searchKey);
    if(keyPos == -1) {
        return defaultValue;
    }

    // Search for colon *after* the key itself to avoid matching colons in preceding values
    int colonPos = StringFind(json, ":", keyPos + StringLen(searchKey));
    if(colonPos == -1) {
        return defaultValue;
    }

    int start = colonPos + 1;
    // Skip whitespace characters
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if(ch != ' ' && ch != '\t' && ch != '\n' && ch != '\r')
            break;
        start++;
    }

    if(start >= StringLen(json)) { // Reached end of string while skipping whitespace
        return defaultValue;
    }

    // Build the numeric string
    string numStr = "";
    while(start < StringLen(json))
    {
        ushort ch = StringGetCharacter(json, start);
        if(ch >= '0' && ch <= '9') // Only digits for an integer
        {
            numStr += CharToString((uchar)ch);
            start++;
        }
        else
            break;
    }

    if(numStr == "") {
        return defaultValue; // No digits found after key and colon
    }

    int result = (int)StringToInteger(numStr);
    return result;
}

//+------------------------------------------------------------------+
//| Extract string value from JSON                                  |
//+------------------------------------------------------------------+
string GetJSONStringValue(string json_string, string key_with_quotes)
{
    // The key_with_quotes parameter is expected to be like "\"nt_instrument_symbol\""
    // So, we search for key_with_quotes + ":" + "\""
    // e.g., "\"nt_instrument_symbol\":\""
    string search_pattern = StringSubstr(key_with_quotes, 1, StringLen(key_with_quotes) - 2); // Remove outer quotes from key_with_quotes
    search_pattern = "\"" + search_pattern + "\":\"";

    int key_pos = StringFind(json_string, search_pattern, 0);
    if(key_pos == -1)
    {
        // Fallback: Try key without quotes around it in the JSON
        string plain_key = StringSubstr(key_with_quotes, 1, StringLen(key_with_quotes) - 2);
        search_pattern = plain_key + ":\"";
        key_pos = StringFind(json_string, search_pattern, 0);
        if(key_pos == -1) return ""; // Key not found
    }

    int value_start_pos = key_pos + StringLen(search_pattern);
    int value_end_pos = StringFind(json_string, "\"", value_start_pos);

    if(value_end_pos == -1) return ""; // Closing quote not found for the value

    return StringSubstr(json_string, value_start_pos, value_end_pos - value_start_pos);
}

// Duplicate JSON parsing functions removed - using originals at lines 315-331

//+------------------------------------------------------------------+
//| Process trailing stop update from NT                            |
//+------------------------------------------------------------------+
void ProcessTrailingStopUpdate(string baseId, double newStopPrice, double currentPrice)
{
    // Find corresponding MT5 position
    int posIndex = FindPositionByBaseId(baseId);
    if (posIndex < 0) {
        { string __log=""; StringConcatenate(__log, "TRAIL_STOP: No position found for BaseID: ", baseId); Print(__log); ULogWarnPrint(__log); }
        return;
    }

    ulong ticket = g_open_mt5_pos_ids[posIndex];
    if (!PositionSelectByTicket(ticket)) {
        { string __log=""; StringConcatenate(__log, "TRAIL_STOP: Failed to select position ticket: ", ticket); Print(__log); ULogErrorPrint(__log); }
        return;
    }

    // Update stop loss to match NT trailing stop
    double currentSL = PositionGetDouble(POSITION_SL);
    ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
    bool isLong = (posType == POSITION_TYPE_BUY);

    // Only update if new stop is better
    bool shouldUpdate = false;
    if (isLong && (currentSL == 0 || newStopPrice > currentSL)) {
        shouldUpdate = true;
    } else if (!isLong && (currentSL == 0 || newStopPrice < currentSL)) {
        shouldUpdate = true;
    }

    if (shouldUpdate) {
        MqlTradeRequest request = {};
        MqlTradeResult result = {};

        request.action = TRADE_ACTION_SLTP;
        request.position = ticket;
        request.symbol = _Symbol;
        request.sl = NormalizeDouble(newStopPrice, _Digits);
        request.tp = PositionGetDouble(POSITION_TP); // Keep existing TP
        request.magic = MagicNumber;

        if (OrderSend(request, result)) {
            if (result.retcode == TRADE_RETCODE_DONE) {
                { string __log=""; StringConcatenate(__log, "TRAIL_STOP: Updated stop for ", baseId, " from ", currentSL, " to ", newStopPrice); Print(__log); ULogInfoPrint(__log); }

                // Send notification to Bridge via gRPC
                string update_json = "{";
                update_json += "\"base_id\":\"" + baseId + "\",";
                update_json += "\"new_stop_price\":" + DoubleToString(newStopPrice, _Digits) + ",";
                update_json += "\"reason\":\"TrailingStop\"";
                update_json += "}";

                GrpcSubmitTrailingUpdate(update_json);
            } else {
                Print("TRAIL_STOP: Failed to update stop. Error: ", result.retcode);
            }
        }
    } else {
        Print("TRAIL_STOP: Stop not updated. Current: ", currentSL, ", New: ", newStopPrice);
    }
}

//+------------------------------------------------------------------+
//| Find position index by base ID                                  |
//+------------------------------------------------------------------+
int FindPositionByBaseId(string baseId)
{
    // Search in the open positions arrays
    for (int i = 0; i < ArraySize(g_open_mt5_base_ids); i++) {
        if (g_open_mt5_base_ids[i] == baseId) {
            return i;
        }
    }
    return -1;
}

//+------------------------------------------------------------------+
//| Notification System for Bridge Communication via gRPC          |
//+------------------------------------------------------------------+

// COMPREHENSIVE DUPLICATE PREVENTION: Track all notifications sent per base_id to prevent duplicates
// Note: g_notified_base_ids and g_notified_timestamps arrays already declared above

//+------------------------------------------------------------------+
//| Add a base_id to the notification tracking list                 |
//+------------------------------------------------------------------+
void AddNotifiedBaseId(string base_id)
{
    int current_size = ArraySize(g_notified_base_ids);
    ArrayResize(g_notified_base_ids, current_size + 1);
    ArrayResize(g_notified_timestamps, current_size + 1);

    g_notified_base_ids[current_size] = base_id;
    g_notified_timestamps[current_size] = TimeCurrent();

    Print("COMPREHENSIVE_DUPLICATE_PREVENTION: Added base_id '", base_id, "' to notification tracking list. Total tracked: ", current_size + 1);
}

//+------------------------------------------------------------------+
//| Check if a base_id has already been notified                   |
//+------------------------------------------------------------------+
bool IsBaseIdAlreadyNotified(string base_id)
{
    for(int i = 0; i < ArraySize(g_notified_base_ids); i++)
    {
        if(g_notified_base_ids[i] == base_id)
        {
            Print("COMPREHENSIVE_DUPLICATE_PREVENTION: Base_id '", base_id, "' found in notification tracking list. Skipping duplicate notification.");
            return true;
        }
    }
    return false;
}

//+------------------------------------------------------------------+
//| Check if notification has been sent for specific event type     |
//+------------------------------------------------------------------+
bool HasNotificationBeenSent(string baseId, string eventType)
{
    // For now, we use the base_id tracking as a general mechanism
    // Can be extended to track specific event types if needed
    return IsBaseIdAlreadyNotified(baseId + "_" + eventType);
}

//+------------------------------------------------------------------+
//| Mark notification as sent for specific event type               |
//+------------------------------------------------------------------+
void MarkNotificationSent(string baseId, string eventType)
{
    AddNotifiedBaseId(baseId + "_" + eventType);
}

//+------------------------------------------------------------------+
//| Remove a notification mark for specific event type               |
//+------------------------------------------------------------------+
void RemoveNotificationMark(string baseId, string eventType)
{
    string key = baseId + "_" + eventType;
    int count = ArraySize(g_notified_base_ids);
    for (int i = 0; i < count; i++)
    {
        if (g_notified_base_ids[i] == key)
        {
            // Remove by swapping with last and resizing arrays to keep them in sync
            int last = count - 1;
            if (i != last)
            {
                g_notified_base_ids[i] = g_notified_base_ids[last];
                g_notified_timestamps[i] = g_notified_timestamps[last];
            }
            if (last >= 0)
            {
                ArrayResize(g_notified_base_ids, last);
                ArrayResize(g_notified_timestamps, last);
            }
            Print("COMPREHENSIVE_DUPLICATE_PREVENTION: Removed notification mark '", key, "'. New total: ", last);
            return;
        }
    }
}

//+------------------------------------------------------------------+
//| Send hedge close notification to Bridge via gRPC               |
//+------------------------------------------------------------------+
// Add optional profit_level to deduplicate per level when callers provide it
void SendHedgeCloseNotification(string base_id,
                                string nt_instrument_symbol,
                                string nt_account_name,
                                double closed_hedge_quantity,
                                string closed_hedge_action,
                                datetime timestamp_dt,
                                string closure_reason,
                                int profit_level = -1,
                                ulong mt5_ticket_hint = 0)
{
    string eventType = "HEDGE_CLOSE";
    string dedupEvent = "hedge_close";

    // Try to resolve the associated MT5 ticket (per-ticket fidelity)
    ulong mt5Ticket = mt5_ticket_hint;
    if(mt5Ticket == 0)
    {
        int posIndex = FindPositionByBaseId(base_id);
        if (posIndex >= 0) {
            mt5Ticket = g_open_mt5_pos_ids[posIndex];
        }
    }

    // Compose a more granular dedup key: base_id + ticket + event
    string dedupEntity = base_id;
    if (mt5Ticket > 0) {
        string tkStr = StringFormat("%I64u", mt5Ticket);
        dedupEntity = base_id + ":" + tkStr;
    }
    // If profit_level is provided, include it to allow one notification per level
    if (profit_level >= 0)
    {
        dedupEntity = dedupEntity + ":lvl" + IntegerToString(profit_level);
    }

    // Check for duplicate notification for this specific (base_id,ticket,event)
    if(HasNotificationBeenSent(dedupEntity, dedupEvent)) {
        Print("SendHedgeCloseNotification: Skipping duplicate notification for base_id/ticket: ", dedupEntity, " event=", dedupEvent);
        return;
    }

    // Format timestamp
    string timestamp_str = TimeToString(timestamp_dt, TIME_DATE|TIME_SECONDS) + " GMT";

    // Build JSON payload for gRPC notification
    string payload = "{";
    payload += "\"event_type\":\"" + eventType + "\",";
    payload += "\"base_id\":\"" + base_id + "\",";
    payload += "\"nt_instrument_symbol\":\"" + nt_instrument_symbol + "\",";
    payload += "\"nt_account_name\":\"" + nt_account_name + "\",";
    payload += "\"closed_hedge_quantity\":" + DoubleToString(closed_hedge_quantity, (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS)) + ",";
    payload += "\"closed_hedge_action\":\"" + closed_hedge_action + "\",";
    payload += "\"timestamp\":\"" + timestamp_str + "\",";
    payload += "\"closure_reason\":\"" + closure_reason + "\"";
    if (mt5Ticket > 0) {
        string tkStr2 = StringFormat("%I64u", mt5Ticket);
        payload += ",\"mt5_ticket\":" + tkStr2;
    }
    payload += "}";

    // Send notification via gRPC
    int result = GrpcNotifyHedgeClose(payload);

    if(result == 0) {
        Print("SendHedgeCloseNotification: Successfully sent notification for base_id/ticket: ", dedupEntity);
        // Mark this specific (base_id,ticket,event) as sent
        MarkNotificationSent(dedupEntity, dedupEvent);
    } else {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        Print("SendHedgeCloseNotification: Failed to send notification for base_id: ", base_id, ". Error: ", result, " - ", error_msg);
    }
}

//+------------------------------------------------------------------+
//| Send trailing stop update notification to Bridge via gRPC      |
//+------------------------------------------------------------------+
void SendTrailingUpdateNotification(string baseId, double newStopPrice, string reason)
{
    if(HasNotificationBeenSent(baseId, "trailing_update")) {
        Print("SendTrailingUpdateNotification: Skipping duplicate notification for base_id: ", baseId);
        return;
    }

    // Find the MT5 position ticket for this BaseID
    ulong mt5Ticket = 0;
    int posIndex = FindPositionByBaseId(baseId);
    if (posIndex >= 0) {
        mt5Ticket = g_open_mt5_pos_ids[posIndex];
    }

    string update_json = "{";
    update_json += "\"event_type\":\"trailing_stop_update\",";
    update_json += "\"base_id\":\"" + baseId + "\",";
    update_json += "\"new_stop_price\":" + DoubleToString(newStopPrice, _Digits) + ",";
    update_json += "\"reason\":\"" + reason + "\",";
    update_json += "\"timestamp\":\"" + TimeToString(TimeCurrent(), TIME_DATE|TIME_SECONDS) + " GMT\",";
    update_json += "\"mt5_ticket\":" + IntegerToString(mt5Ticket);
    update_json += "}";

    Print("TRAILING_UPDATE: Sending notification with MT5 ticket: ", mt5Ticket, " for BaseID: ", baseId);

    int result = GrpcSubmitTrailingUpdate(update_json);

    if(result == 0) {
        Print("SendTrailingUpdateNotification: Successfully sent trailing update for base_id: ", baseId);
        MarkNotificationSent(baseId, "trailing_update");
    } else {
        string error_msg;
        GrpcGetLastError(error_msg, 1024);
        Print("SendTrailingUpdateNotification: Failed to send trailing update for base_id: ", baseId, ". Error: ", result, " - ", error_msg);
    }
}

//+------------------------------------------------------------------+
//| Helper to extract Base ID from MT5 Position Comment              |
//| Comment format: "AC_HEDGE;BID:{base_id};NTA:..."               |
//+------------------------------------------------------------------+
string ExtractBaseIdFromComment(string comment_str)
{
    string base_id = "";
    if(!TryExtractManagedBaseIdFromComment(comment_str, base_id))
        return "";

    int id_len = StringLen(base_id);
    if(StringFind(comment_str, "AC_HEDGE", 0) != -1)
    {
        if(id_len > 0 && (id_len < 16 || id_len > 36) && base_id != "TEST_BASE_ID_RECOVERY")
             Print("ACHM_PARSE_INFO: ExtractBaseIdFromComment - Extracted base_id '", base_id, "' from '", comment_str, "' has length: ", id_len, " (expected 16 for new format, 32 for legacy)");
        else if(id_len == 0 && StringFind(comment_str, "BID:", 0) != -1)
            Print("ACHM_PARSE_FAIL: ExtractBaseIdFromComment - Failed to extract base_id from AC_HEDGE comment containing BID: '", comment_str, "'");
    }

    return base_id;
}
//+------------------------------------------------------------------+
//| Perform state recovery for existing positions                   |
//+------------------------------------------------------------------+
void PerformStateRecovery()
{
    Print("ACHM_RECOVERY: Starting state recovery for existing MT5 positions...");
    int total_positions = PositionsTotal();
    int rehydrated_count = 0;
    double recovered_global_futures_adjustment = 0.0;

    for(int i = 0; i < total_positions; i++) {
        ulong mt5_ticket = PositionGetTicket(i);
        if(mt5_ticket == 0) continue;
        if(!PositionSelectByTicket(mt5_ticket)) continue;

        if(PositionGetInteger(POSITION_MAGIC) == MagicNumber && PositionGetString(POSITION_SYMBOL) == _Symbol) {
            string comment = PositionGetString(POSITION_COMMENT);
            MANAGED_TRADE_KIND managedKind = GetManagedTradeKindFromComment(comment);
            if(managedKind == ManagedTrade_CounterHedge) {
                Print("ACHM_RECOVERY: Skipping Counter-Hedge position during recovery (runtime-only scope). Ticket: ", mt5_ticket, ", Comment: '", comment, "'");
                continue;
            }
            long mt5_pos_id = (long)PositionGetInteger(POSITION_IDENTIFIER); // Same as mt5_ticket for MT5 positions
            ENUM_POSITION_TYPE mt5_pos_type = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
            double mt5_pos_volume = PositionGetDouble(POSITION_VOLUME);

            Print("ACHM_RECOVERY: Checking EA position. Ticket: ", mt5_ticket, ", Comment: '", comment, "', Type: ", EnumToString(mt5_pos_type), ", Vol: ", mt5_pos_volume);

            // Parse comment: "AC_HEDGE;BID:{base_id};NTA:{NT_ACTION};NTQ:{NT_QTY};MTA:{MT5_ACTION}"
            string base_id_str = ExtractBaseIdFromComment(comment);

            if (base_id_str != "") {
                Print("ACHM_RECOVERY: Extracted BaseID='", base_id_str, "' from comment '", comment, "' for PosID ", mt5_pos_id);

                // 1. Re-populate g_map_position_id_to_base_id (Primary Goal)
                if(CheckPointer(g_map_position_id_to_base_id) == POINTER_DYNAMIC) {
                    if(!g_map_position_id_to_base_id.Add(mt5_pos_id, base_id_str)) {
                        Print("ACHM_RECOVERY_ERROR: Failed to add to g_map_position_id_to_base_id for PosID ", mt5_pos_id, " with base_id '", base_id_str, "'");
                    } else {
                         Print("ACHM_RECOVERY: Re-mapped g_map_position_id_to_base_id: MT5 PosID ", mt5_pos_id, " -> base_id '", base_id_str, "'");
                    }
                }

                // Attempt to parse other parts for more complete rehydration
                string nt_action_str = "";
                int nt_qty_val = 0;
                string mt5_action_str = ""; // This is the MT5 hedge's action (Buy/Sell)

                string parts[];
                int num_parts = StringSplit(comment, ';', parts);

                if (num_parts > 0 && parts[0] == "AC_HEDGE") {
                    // NTA:{NT_ACTION} - Original NT Action (parts[2])
                    if (num_parts > 2 && StringFind(parts[2], "NTA:", 0) == 0) {
                        string nta_part[]; StringSplit(parts[2], ':', nta_part);
                        if(ArraySize(nta_part) == 2) nt_action_str = nta_part[1];
                    }
                    // NTQ:{NT_QTY} - Original NT Quantity (parts[3])
                    if (num_parts > 3 && StringFind(parts[3], "NTQ:", 0) == 0) {
                        string ntq_part[]; StringSplit(parts[3], ':', ntq_part);
                        if(ArraySize(ntq_part) == 2) nt_qty_val = (int)StringToInteger(ntq_part[1]);
                    }
                    // MTA:{MT5_ACTION} - MT5 Action (parts[4])
                    if (num_parts > 4 && StringFind(parts[4], "MTA:", 0) == 0) {
                        string mta_part[]; StringSplit(parts[4], ':', mta_part);
                        if(ArraySize(mta_part) == 2) mt5_action_str = mta_part[1];
                    }
                    Print("ACHM_RECOVERY: Attempted parsing NTA/NTQ/MTA from '", comment, "': NT_Action='", nt_action_str, "', NT_Qty=", nt_qty_val, ", MT5_Action='", mt5_action_str, "'");
                } else {
                    Print("ACHM_RECOVERY_INFO: Comment '", comment, "' for PosID ", mt5_pos_id, " did not start with AC_HEDGE or was too short for full NTA/NTQ/MTA parsing after splitting by ';'. BaseID '", base_id_str, "' was still extracted.");
                }

                // MODIFIED: Proceed with full rehydration if all essential parts were parsed
                if(nt_action_str != "" && nt_qty_val > 0 && mt5_action_str != "") {
                    Print("ACHM_RECOVERY: All parts parsed for full rehydration. Ticket ", mt5_ticket, ": BaseID='", base_id_str, "', NT_Action='", nt_action_str, "', NT_Qty=", nt_qty_val, ", MT5_Action='", mt5_action_str, "'");

                    // 2. Re-create Trade Group Entry
                    int group_idx = -1;
                    // Handle both full match (legacy) and partial match (new format due to MT5 comment length limit)
                    for(int k=0; k < ArraySize(g_baseIds); k++) {
                        bool isMatch = false;
                        if(g_baseIds[k] == base_id_str) {
                            // Full match (legacy format)
                            isMatch = true;
                        } else if(StringLen(g_baseIds[k]) >= 16 && StringLen(base_id_str) >= 16) {
                            // Partial match - compare first 16 characters (new format)
                            string shortStoredBaseId = StringSubstr(g_baseIds[k], 0, 16);
                            string shortBaseId = StringSubstr(base_id_str, 0, 16);
                            if(shortStoredBaseId == shortBaseId) {
                                isMatch = true;
                                Print("ACHM_RECOVERY: Matched using partial base_id. Stored: '", shortStoredBaseId, "' (from full: '", g_baseIds[k], "'), Input: '", shortBaseId, "' (from full: '", base_id_str, "')");
                            }
                        }

                        if(isMatch) {
                            group_idx = k;
                            Print("ACHM_RECOVERY: Found existing (potentially incomplete) trade group for base_id '", base_id_str, "' at index ", group_idx);
                            break;
                        }
                    }
                    if(group_idx == -1) { // Create new if not found
                        group_idx = ArraySize(g_baseIds);
                        ArrayResize(g_baseIds, group_idx + 1);
                        ArrayResize(g_totalQuantities, group_idx + 1);
                        ArrayResize(g_processedQuantities, group_idx + 1);
                        ArrayResize(g_actions, group_idx + 1);
                        ArrayResize(g_isComplete, group_idx + 1);
                        ArrayResize(g_mt5HedgesOpenedCount, group_idx + 1);
                        ArrayResize(g_mt5HedgesClosedCount, group_idx + 1);
                        ArrayResize(g_isMT5Opened, group_idx + 1);
                        ArrayResize(g_isMT5Closed, group_idx + 1);
                        ArrayResize(g_ntInstrumentSymbols, group_idx + 1);
                        ArrayResize(g_ntAccountNames, group_idx + 1);
                        Print("ACHM_RECOVERY: Creating new trade group for rehydrated base_id '", base_id_str, "' at index ", group_idx);
                    }

                    g_baseIds[group_idx] = base_id_str;
                    g_actions[group_idx] = nt_action_str;
                    g_totalQuantities[group_idx] = nt_qty_val;
                    g_processedQuantities[group_idx] = nt_qty_val;
                    g_isComplete[group_idx] = true;

                    g_mt5HedgesOpenedCount[group_idx] = 1;
                    g_mt5HedgesClosedCount[group_idx] = 0;
                    g_isMT5Opened[group_idx] = true;
                    g_isMT5Closed[group_idx] = false;

                    // Set placeholder NT details (will be updated with real data when available)
                    g_ntInstrumentSymbols[group_idx] = "RECOVERED_SYMBOL";
                    g_ntAccountNames[group_idx] = "RECOVERED_ACCOUNT";

                    // 3. Add to parallel tracking arrays
                    int open_mt5_idx = ArraySize(g_open_mt5_pos_ids);
                    ArrayResize(g_open_mt5_pos_ids, open_mt5_idx + 1);
                    ArrayResize(g_open_mt5_base_ids, open_mt5_idx + 1);
                    ArrayResize(g_open_mt5_original_nt_actions, open_mt5_idx + 1);
                    ArrayResize(g_open_mt5_original_nt_quantities, open_mt5_idx + 1);
                    ArrayResize(g_open_mt5_actions, open_mt5_idx + 1);

                    g_open_mt5_pos_ids[open_mt5_idx] = mt5_pos_id;
                    g_open_mt5_base_ids[open_mt5_idx] = base_id_str;
                    g_open_mt5_original_nt_actions[open_mt5_idx] = nt_action_str;
                    g_open_mt5_original_nt_quantities[open_mt5_idx] = nt_qty_val;
                    g_open_mt5_actions[open_mt5_idx] = mt5_action_str; // <<< ADDED FOR MT5 ACTION RECOVERY
                    Print("ACHM_RECOVERY: Added to g_open_mt5_ arrays. PosID:", mt5_pos_id, " BaseID:", base_id_str, " NT_Action:'", nt_action_str, "', NT_Qty:", nt_qty_val, ", MT5_Action:'", mt5_action_str, "'"); // <<< UPDATED LOG

                    // 4. Adjust globalFutures
                    if (mt5_pos_type == POSITION_TYPE_BUY) {
                        recovered_global_futures_adjustment -= nt_qty_val;
                        Print("ACHM_RECOVERY: MT5 BUY hedge (for NT SELL) rehydrated. Adjusting globalFutures by -", nt_qty_val);
                    } else if (mt5_pos_type == POSITION_TYPE_SELL) {
                        recovered_global_futures_adjustment += nt_qty_val;
                        Print("ACHM_RECOVERY: MT5 SELL hedge (for NT BUY) rehydrated. Adjusting globalFutures by +", nt_qty_val);
                    }
                    rehydrated_count++;
                    Print("ACHM_RECOVERY: Successfully rehydrated state for MT5 PositionID ", mt5_pos_id, " (Ticket: ", mt5_ticket, ")");

                    // Planner/trailing removed: no per-position elastic tracking required.
                } else {
                    // CORRUPTION FIX: Even if full parsing failed, ensure parallel arrays are populated with placeholders
                     Print("ACHM_RECOVERY_WARN: Base_id '", base_id_str, "' extracted, but other parts (NTA/NTQ/MTA) for full rehydration are missing/invalid from comment '", comment, "'. Adding to arrays with placeholder values.");

                     // Use placeholder values for missing data
                     string placeholder_nt_action = (nt_action_str != "") ? nt_action_str : "UNKNOWN_ACTION";
                     int placeholder_nt_qty = (nt_qty_val > 0) ? nt_qty_val : 1;
                     string placeholder_mt5_action = (mt5_action_str != "") ? mt5_action_str : ((mt5_pos_type == POSITION_TYPE_BUY) ? "BUY" : "SELL");

                     // Add to parallel arrays to prevent corruption
                     int open_mt5_idx = ArraySize(g_open_mt5_pos_ids);
                     ArrayResize(g_open_mt5_pos_ids, open_mt5_idx + 1);
                     ArrayResize(g_open_mt5_base_ids, open_mt5_idx + 1);
                     ArrayResize(g_open_mt5_original_nt_actions, open_mt5_idx + 1);
                     ArrayResize(g_open_mt5_original_nt_quantities, open_mt5_idx + 1);
                     ArrayResize(g_open_mt5_actions, open_mt5_idx + 1);

                     g_open_mt5_pos_ids[open_mt5_idx] = mt5_pos_id;
                     g_open_mt5_base_ids[open_mt5_idx] = base_id_str;
                     g_open_mt5_original_nt_actions[open_mt5_idx] = placeholder_nt_action;
                     g_open_mt5_original_nt_quantities[open_mt5_idx] = placeholder_nt_qty;
                     g_open_mt5_actions[open_mt5_idx] = placeholder_mt5_action;

                     Print("ACHM_RECOVERY_PLACEHOLDER: Added position ", mt5_pos_id, " to arrays with placeholders - NT_Action:'", placeholder_nt_action, "', NT_Qty:", placeholder_nt_qty, ", MT5_Action:'", placeholder_mt5_action, "'");

                     // Planner/trailing removed: no per-position elastic tracking required.
                }
            } else { // base_id_str is empty
                Print("ACHM_RECOVERY_FAIL: Failed to extract a valid base_id from comment '", comment, "' for position ticket ", mt5_ticket, ". Cannot rehydrate this position's state.");
            }
        }
    }
    globalFutures += recovered_global_futures_adjustment; // Apply the total adjustment
    Print("ACHM_RECOVERY: State recovery complete. Rehydrated ", rehydrated_count, " positions. Total adjustment to globalFutures: ", recovered_global_futures_adjustment, ". New globalFutures: ", globalFutures);
}

// Note: Duplicate OnInit function removed - using the one at line 746

//+------------------------------------------------------------------+
//| Open a new hedge order - AC-aware + dynamic hedging             |
//+------------------------------------------------------------------+
bool OpenNewHedgeOrder(string hedgeOrigin, string tradeId, string nt_instrument_symbol, string nt_account_name)
{
    /*----------------------------------------------------------------
     0.  Generic request skeleton
    ----------------------------------------------------------------*/
    MqlTradeRequest request = {};
    MqlTradeResult  result  = {};
    request.action    = TRADE_ACTION_DEAL;
    request.symbol    = _Symbol;
    request.magic     = MagicNumber;
    request.deviation = Slippage;

    /*----------------------------------------------------------------
     1.  Symbol limits
    ----------------------------------------------------------------*/
    const double minLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN);
    const double maxLot  = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX);
    const double lotStep = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP);

    /*----------------------------------------------------------------
     2.  Stop-loss distance (ATR-based)
    ----------------------------------------------------------------*/
    double slDist = GetStopLossDistance();
    double brokerMinPts = GetBrokerMinimumStopPoints();
    if(slDist <= 0)
    {
        Print("ERROR: SL distance not available, aborting order.");
        return false;
    }
        double slPoints = slDist / SymbolInfoDouble(_Symbol, SYMBOL_POINT);
        { string __log=""; StringConcatenate(__log,
            "HEDGE_ORDER_SL: base_id=", tradeId,
            " simpleSLpts=", DoubleToString(SimpleStopLoss_Points, 2),
            " slDist=", DoubleToString(slDist, 5),
            " slPts=", DoubleToString(slPoints, 2),
            " brokerMinPts=", DoubleToString(brokerMinPts, 2));
          Print(__log); ULogInfoPrint(__log); }

    /*----------------------------------------------------------------
     3.  Determine NT quantity (from group) and MT5 order side
    ----------------------------------------------------------------*/
    // Try to locate the NT quantity for this base_id (tradeId)
    int ntQty = 0;
    for(int k=0; k < ArraySize(g_baseIds); k++) {
        if(g_baseIds[k] == tradeId) {
            if(k < ArraySize(g_totalQuantities)) ntQty = g_totalQuantities[k];
            break;
        }
    }

    // If EnableHedging is true, OnTimer sets hedgeOrigin to the OPPOSITE of the NT action.
    // If EnableHedging is false (copying), OnTimer sets hedgeOrigin to the SAME as the NT action.
    if (hedgeOrigin == "Buy") {
        request.type = ORDER_TYPE_BUY;
    } else if (hedgeOrigin == "Sell") {
        request.type = ORDER_TYPE_SELL;
    } else {
        Print("ERROR: OpenNewHedgeOrder - Invalid hedgeOrigin '", hedgeOrigin, "'. Cannot determine order type.");
        return false;
    }

    /*----------------------------------------------------------------
     4.  Calculate volume based on lot mode
    ----------------------------------------------------------------*/
    double volume = 0.0;
    if(LotSizingMode == LOTS_INVERSE_PNL)
    {
        volume = CalculateInversePnLLot(request.type);
    }
    else
    {
        volume = CalculateFixedLotSize();
    }

    if(volume <= 0.0)
    {
        double symMin = SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN); if(symMin <= 0) symMin = 0.01;
        volume = symMin;
    }

    double finalVol = volume;  // Volume already calculated based on selected mode

    // Clamp to limits
    if(finalVol < minLot)  finalVol = minLot;
    if(finalVol > maxLot)  finalVol = maxLot;

    // Round to nearest step: compute units, round to integer, then scale
    double stepUnits = finalVol / lotStep;
    double roundedUnits = MathRound(stepUnits);
    finalVol = NormalizeDouble(roundedUnits * lotStep, 8);
    if(finalVol < minLot) finalVol = minLot; // ensure never below exchange minimum after rounding

    /*----------------------------------------------------------------
     5.  Margin-aware adjustment and comment
    ----------------------------------------------------------------*/
    // Adjust for free margin if needed
    double adjVol = AdjustLotForMargin(finalVol, request.type);
    if(adjVol < finalVol - 1e-8) {
        ULogWarnPrint(StringFormat("ACHM_MARGIN: Downscaling lot due to free margin. Requested %.4f -> adjusted %.4f", finalVol, adjVol));
        finalVol = adjVol;
    }
    if(LotSizingMode == LOTS_INVERSE_PNL)
        g_inversePnlNextLot = finalVol;
    if(finalVol < minLot - 1e-8) {
        ULogErrorPrint(StringFormat("ACHM_MARGIN: Insufficient free margin even for min lot %.4f. Aborting order for base_id=%s", minLot, tradeId));
        return false;
    }

    // Determine original NT action and quantity for the comment
    string original_nt_action_for_comment = "N/A";
    int original_nt_qty_for_comment = 0;
    int group_idx_for_comment = -1;
    for(int k=0; k < ArraySize(g_baseIds); k++) {
        if(g_baseIds[k] == tradeId) { // tradeId is base_id
            group_idx_for_comment = k;
            break;
        }
    }
    if(group_idx_for_comment != -1) {
        if(group_idx_for_comment < ArraySize(g_actions)) original_nt_action_for_comment = g_actions[group_idx_for_comment];
        if(group_idx_for_comment < ArraySize(g_totalQuantities)) original_nt_qty_for_comment = g_totalQuantities[group_idx_for_comment];
    } else {
        Print("WARN: OpenNewHedgeOrder - Could not find trade group for base_id '", tradeId, "' to create detailed comment. Using N/A.");
    }

    // Format comment to match existing CLOSE_HEDGE matching logic:
    // "NT_Hedge_{BUY|SELL}_{short_base_id}"
    string short_base_id = StringSubstr(tradeId, 0, 16); // first 16 chars used elsewhere for matching
    request.comment = StringFormat("%s%s_%s", CommentPrefix, hedgeOrigin, short_base_id);

    request.price   = SymbolInfoDouble(_Symbol,
                   (request.type == ORDER_TYPE_BUY) ? SYMBOL_ASK
                                                     : SYMBOL_BID);

    /*----------------------------------------------------------------
     6.  SL / TP
    ----------------------------------------------------------------*/
    double slPrice = (request.type == ORDER_TYPE_BUY)
                     ? request.price - slDist
                     : request.price + slDist;

    double tpPrice = 0.0;

    /*----------------------------------------------------------------
     6.  Send via CTrade
    ----------------------------------------------------------------*/
    // Unified logging context and preflight details
    ULogSetInstrument(_Symbol);
    ULogInfoPrint(StringFormat(
        "HEDGE_ORDER_ATTEMPT: base_id=%s action=%s type=%s vol=%.4f minLot=%.4f maxLot=%.4f step=%.4f price=%.5f sl=%.5f tp=%.5f comment='%s'",
        tradeId, hedgeOrigin, EnumToString(request.type), finalVol, minLot, maxLot, lotStep, request.price,
        (request.type == ORDER_TYPE_BUY) ? request.price - slDist : request.price + slDist,
        tpPrice,
        request.comment
    ));

    Print("INFO: OpenNewHedgeOrder: Placing MT5 Order. Determined MT5 Action (from hedgeOrigin param): '", hedgeOrigin, "', Actual MqlTradeRequest.type: ", EnumToString(request.type), ", Comment: '", request.comment, "', Volume: ", finalVol, " for base_id: '", tradeId, "'");
    bool sent = (request.type == ORDER_TYPE_BUY)
                ? trade.Buy (finalVol, _Symbol, request.price,
                             slPrice, tpPrice, request.comment)
                : trade.Sell(finalVol, _Symbol, request.price,
                             slPrice, tpPrice, request.comment);

    if(!sent)
    {
        int lastErr = GetLastError();
        int retcode = (int)trade.ResultRetcode();
        string retmsg = trade.ResultComment();
        // Unified logging on failure with error context
        ULogSetErrorCode(IntegerToString(retcode) + "|" + IntegerToString(lastErr));
        ULogSetMt5Ticket(0);
        ULogErrorPrint(StringFormat(
            "HEDGE_ORDER_FAILED: base_id=%s action=%s vol=%.4f type=%s retcode=%d lastErr=%d comment='%s'",
            tradeId, hedgeOrigin, finalVol, EnumToString(request.type), retcode, lastErr, retmsg
        ));

        PrintFormat("ERROR: CTrade %s failed (%d / %s)",
                    (request.type == ORDER_TYPE_BUY ? "Buy" : "Sell"),
                    retcode, retmsg);
        // Submit failure so bridge can correlate
        SubmitTradeResult("failed", 0, finalVol, false, tradeId);
        return false;
    }

    ulong order_ticket_for_map = trade.ResultOrder();
    ulong deal_ticket_for_map = trade.ResultDeal();
    if(sent && deal_ticket_for_map > 0)
    {
        // Increment MT5 hedges opened count for this base_id's group
        int groupIdxOpen = -1;
        for(int i = 0; i < ArraySize(g_baseIds); i++) {
            if(g_baseIds[i] == tradeId) {
                groupIdxOpen = i;
                break;
            }
        }
        if(groupIdxOpen != -1 && groupIdxOpen < ArraySize(g_mt5HedgesOpenedCount)) {
            g_mt5HedgesOpenedCount[groupIdxOpen]++;
            Print("ACHM_DIAG: [OpenNewHedgeOrder] Incremented g_mt5HedgesOpenedCount for base_id '", tradeId, "' (index ", groupIdxOpen, ") to ", g_mt5HedgesOpenedCount[groupIdxOpen]);
        }
    }

    PrintFormat("INFO: Placed hedge %s %.2f lots SL %.1f TP %.1f deal %I64u",
                (request.type == ORDER_TYPE_BUY ? "BUY" : "SELL"),
                finalVol, slPrice, tpPrice, deal_ticket_for_map);

    if(deal_ticket_for_map > 0)
    {
        if(HistoryDealSelect(deal_ticket_for_map))
        {
            ulong new_mt5_position_id = HistoryDealGetInteger(deal_ticket_for_map, DEAL_POSITION_ID);
            if(new_mt5_position_id > 0)
            {
                // Store details in parallel arrays
                if(!ValidateArrayIntegrity()) {
                    PrintFormat("CRITICAL_ARRAY_ERROR: Array integrity check failed BEFORE adding new position. Aborting position addition.");
                    return false;
                }

                int current_array_size = ArraySize(g_open_mt5_pos_ids);
                PrintFormat("ARRAY_ADD: Adding new position at index %d. Current array size: %d", current_array_size, current_array_size);

                // Perform atomic array resizing
                ArrayResize(g_open_mt5_pos_ids, current_array_size + 1);
                ArrayResize(g_open_mt5_base_ids, current_array_size + 1);
                ArrayResize(g_open_mt5_nt_symbols, current_array_size + 1);
                ArrayResize(g_open_mt5_nt_accounts, current_array_size + 1);
                ArrayResize(g_open_mt5_actions, current_array_size + 1);
                ArrayResize(g_open_mt5_original_nt_actions, current_array_size + 1);
                ArrayResize(g_open_mt5_original_nt_quantities, current_array_size + 1);

                // Add position data
                g_open_mt5_pos_ids[current_array_size] = (long)new_mt5_position_id;
                g_open_mt5_base_ids[current_array_size] = tradeId;
                g_open_mt5_nt_symbols[current_array_size] = nt_instrument_symbol;
                g_open_mt5_nt_accounts[current_array_size] = nt_account_name;
                g_open_mt5_actions[current_array_size] = hedgeOrigin;

                // Get original NT details for new arrays
                string original_nt_action_for_open_mt5 = "";
                int original_nt_qty_for_open_mt5 = 0;
                if(group_idx_for_comment != -1) {
                    if(group_idx_for_comment < ArraySize(g_actions)) original_nt_action_for_open_mt5 = g_actions[group_idx_for_comment];
                    if(group_idx_for_comment < ArraySize(g_totalQuantities)) original_nt_qty_for_open_mt5 = g_totalQuantities[group_idx_for_comment];
                }

                // Validate data and use placeholders if invalid
                if(original_nt_action_for_open_mt5 == "") {
                    Print("CRITICAL: OpenNewHedgeOrder - Trade group found but NT action is empty for base_id '", tradeId, "'. Using placeholder.");
                    original_nt_action_for_open_mt5 = "EMPTY_GROUP_ACTION";
                }
                if(original_nt_qty_for_open_mt5 <= 0) {
                    Print("CRITICAL: OpenNewHedgeOrder - Trade group found but NT quantity is invalid for base_id '", tradeId, "'. Using placeholder.");
                    original_nt_qty_for_open_mt5 = 1;
                }

                g_open_mt5_original_nt_actions[current_array_size] = original_nt_action_for_open_mt5;
                g_open_mt5_original_nt_quantities[current_array_size] = original_nt_qty_for_open_mt5;

                Print("DEBUG: Stored details in parallel arrays for PosID ", (long)new_mt5_position_id, " at index ", current_array_size,
                      ". BaseID: ", tradeId, ", NT Symbol: ", nt_instrument_symbol, ", NT Account: ", nt_account_name,
                      ", MT5 Action: ", hedgeOrigin, ", Orig NT Action: ", original_nt_action_for_open_mt5, ", Orig NT Qty: ", original_nt_qty_for_open_mt5);

                // Store in hashmap
                if(CheckPointer(g_map_position_id_to_base_id) == POINTER_DYNAMIC) {
                    if(!g_map_position_id_to_base_id.Add((long)new_mt5_position_id, tradeId)) {
                        Print("ERROR: OpenNewHedgeOrder - Failed to Add base_id '", tradeId, "' to g_map_position_id_to_base_id for PositionID ", new_mt5_position_id, ".");
                    } else {
                        Print("DEBUG_HEDGE_CLOSURE: Stored mapping for MT5 PosID ", (long)new_mt5_position_id, " to base_id '", tradeId, "' in g_map_position_id_to_base_id.");
                    }
                }

                // Apply simple stop-loss for new positions when enabled
                if(SimpleStopLoss_Points > 0.0 && PositionSelectByTicket(new_mt5_position_id))
                {
                    ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
                    double posEntry = PositionGetDouble(POSITION_PRICE_OPEN);
                    ApplySimpleStopLossIfNeeded(new_mt5_position_id, posType, posEntry, true);
                }

                // Final validation after position addition
                if(!ValidateArrayIntegrity()) {
                    PrintFormat("CRITICAL_ARRAY_ERROR: Array integrity check failed AFTER adding new position at index %d", current_array_size);
                    return false;
                } else {
                    PrintFormat("ARRAY_ADD_SUCCESS: Position added successfully at index %d. All arrays remain synchronized.", current_array_size);
                }
            }
        }
    }

    // Unified logging on success and report identifiers
    ULogSetMt5Ticket((long)order_ticket_for_map);
    ULogInfoPrint(StringFormat(
        "HEDGE_ORDER_SUCCESS: base_id=%s action=%s vol=%.4f order=%I64u deal=%I64u",
        tradeId, hedgeOrigin, finalVol, order_ticket_for_map, deal_ticket_for_map
    ));

    SubmitTradeResult("success", deal_ticket_for_map, finalVol, false, tradeId);
    return true;
}

//+------------------------------------------------------------------+
//| Close one hedge position                                         |
//+------------------------------------------------------------------+
bool CloseOneHedgePosition(string hedgeOrigin, string specificTradeId = "")
{
    long ticket_to_close_long = 0;

    if (specificTradeId != "") {
        // If specificTradeId is provided, prioritize finding by it + origin
        int total = PositionsTotal();
        for(int i = 0; i < total; i++) {
            ulong current_ticket_ulong = PositionGetTicket(i);
            if(current_ticket_ulong == 0) continue;
            if(!PositionSelectByTicket(current_ticket_ulong)) continue;

            if(PositionGetString(POSITION_SYMBOL)   != _Symbol)     continue;
            if(PositionGetInteger(POSITION_MAGIC)   != MagicNumber) continue;

            string comment = PositionGetString(POSITION_COMMENT);
            string originSearchStr = CommentPrefix + hedgeOrigin;

            if (StringFind(comment, originSearchStr) != -1 && StringFind(comment, specificTradeId) != -1) {
                ticket_to_close_long = (long)current_ticket_ulong;
                Print("DEBUG: CloseOneHedgePosition - Found specific ticket ", ticket_to_close_long, " matching ID ", specificTradeId, " and origin ", hedgeOrigin);
                break;
            }
        }
        if (ticket_to_close_long == 0) {
            Print("DEBUG: CloseOneHedgePosition - No ticket found matching specificTradeId '", specificTradeId, "' and origin '", hedgeOrigin, "'");
            return false;
        }
    } else {
        // If no specificTradeId, find by origin only
        ticket_to_close_long = FindOldestHedgeToCloseTicket(hedgeOrigin);
        if (ticket_to_close_long == 0) {
            Print("DEBUG: CloseOneHedgePosition - No ticket found by origin '", hedgeOrigin, "' (no specificTradeId provided).");
            return false;
        }
    }

    ulong ticket_to_close = (ulong)ticket_to_close_long;

    // Select again to be sure
    if(!PositionSelectByTicket(ticket_to_close)) {
        Print("ERROR: CloseOneHedgePosition - Failed to select ticket ", (long)ticket_to_close, " before closing.");
        return false;
    }

    double volumeToClose = PositionGetDouble(POSITION_VOLUME);
    string originalComment = PositionGetString(POSITION_COMMENT);

    Print(StringFormat(
          "DEBUG: Closing hedge position via CTrade (CloseOneHedgePosition) - Ticket:%I64u  Vol:%.2f  Comment:%s",
          ticket_to_close, volumeToClose, originalComment));

    bool closed = trade.PositionClose(ticket_to_close, Slippage);

    if(closed)
    {
        Print("DEBUG: PositionClose succeeded (via CloseOneHedgePosition). Order:", trade.ResultOrder(),
              "  Deal:", trade.ResultDeal());

        string closedTradeId = "";
        // Extract trade-id from comment
        int originMarkerEnd = StringFind(originalComment, hedgeOrigin);
        if(originMarkerEnd != -1) originMarkerEnd += StringLen(hedgeOrigin);

        int idStart = -1;
        if(originMarkerEnd != -1 && originMarkerEnd < StringLen(originalComment)) {
            idStart = StringFind(originalComment, "_", originMarkerEnd) + 1;
        }

        if(idStart > 0 && idStart < StringLen(originalComment)) {
            closedTradeId = StringSubstr(originalComment, idStart);
        }

        double closeProfit = 0;
        if(trade.ResultDeal() > 0) closeProfit = HistoryDealGetDouble(trade.ResultDeal(), DEAL_PROFIT);
        ProcessTradeResult(closeProfit > 0, closedTradeId, closeProfit);

        SubmitTradeResult("success", trade.ResultOrder(), volumeToClose, true, closedTradeId);
        return true;
    }
    else
    {
        Print(StringFormat("ERROR: PositionClose failed (via CloseOneHedgePosition) for ticket %I64u [%d/%s]", ticket_to_close, trade.ResultRetcode(), trade.ResultRetcodeDescription()));
        return false;
    }
}

//+------------------------------------------------------------------+
//| Count hedge positions for a specific base_id and MT5 action     |
//+------------------------------------------------------------------+
int CountHedgePositionsForBaseId(string baseIdToCount, string mt5HedgeAction)
{
    int count = 0;
    string specificCommentSearch = StringFormat("%s%s_%s", CommentPrefix, mt5HedgeAction, baseIdToCount);

    int total = PositionsTotal();
    for(int i = 0; i < total; i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(!PositionSelectByTicket(ticket)) continue;

        if(PositionGetString(POSITION_SYMBOL)   != _Symbol)     continue;
        if(PositionGetInteger(POSITION_MAGIC)   != MagicNumber) continue;

        string comment = PositionGetString(POSITION_COMMENT);

        // Check if comment contains our search pattern
        if(StringFind(comment, specificCommentSearch) >= 0) {
            count++;
            Print("DEBUG: CountHedgePositionsForBaseId - Found matching position: Ticket=", ticket, ", Comment='", comment, "'");
        }
    }

    Print("DEBUG: CountHedgePositionsForBaseId - Found ", count, " positions for baseId='", baseIdToCount, "', action='", mt5HedgeAction, "'");
    return count;
}

//+------------------------------------------------------------------+
//| Close hedge positions for a specific base_id                    |
//+------------------------------------------------------------------+
bool CloseHedgePositionsForBaseId(string baseId, string reason = "NT_CLOSE_REQUEST")
{
    int closedCount = 0;
    ulong tickets[]; datetime openTimes[]; double volumes[];
    int available = CollectManagedTicketsForBaseId(baseId, tickets, openTimes, volumes);

    Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Starting closure for base_id: '", baseId, "', reason: '", reason, "'. Matched positions: ", available);

    if(available <= 0)
    {
        Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] No managed positions found for base_id: '", baseId, "'");
        return false;
    }

    SortTicketsByOpenTime(openTimes, tickets, volumes);

    for(int pass = 0; pass < 2; pass++)
    {
        MANAGED_TRADE_KIND desiredKind = (pass == 0) ? ManagedTrade_CounterHedge : ManagedTrade_PrimaryHedge;
        for(int i = 0; i < ArraySize(tickets); i++)
        {
            ulong ticket = tickets[i];
            if(ticket == 0)
                continue;
            if(!PositionSelectByTicket(ticket))
                continue;
            if(PositionGetString(POSITION_SYMBOL) != _Symbol)
                continue;
            if(PositionGetInteger(POSITION_MAGIC) != MagicNumber)
                continue;

            string posComment = PositionGetString(POSITION_COMMENT);
            MANAGED_TRADE_KIND tradeKind = GetManagedTradeKindFromComment(posComment);
            if(tradeKind != desiredKind)
                continue;

            double posVolume = PositionGetDouble(POSITION_VOLUME);
            ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
            Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Closing position: Ticket=", ticket, ", Volume=", posVolume, ", Type=", EnumToString(posType), ", Comment='", posComment, "'");

            if(tradeKind == ManagedTrade_CounterHedge)
            {
                trade.SetExpertMagicNumber(MagicNumber);
                trade.SetDeviationInPoints(Slippage);
                bool closed = trade.PositionClose(ticket, Slippage);
                if(closed)
                {
                    closedCount++;
                    RemoveCounterHedgeTracking(ticket);
                    Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Successfully closed Counter-Hedge position: ", ticket);
                }
                else
                {
                    Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Failed to close Counter-Hedge position: ", ticket, ". Error: ", trade.ResultRetcode(), " - ", trade.ResultComment());
                }
                continue;
            }

            double closedVol = 0.0;
            if(CloseHedgeTicket(baseId, ticket, closedVol, reason))
            {
                closedCount++;
                Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Successfully closed hedge position: ", ticket);
            }
            else
            {
                Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Failed to close hedge position: ", ticket);
            }
        }
    }

    Print("ACHM_NT_CLOSURE: [CloseHedgePositionsForBaseId] Completed closure for base_id: '", baseId, "'. Closed ", closedCount, " positions.");
    return (closedCount > 0);
}
//+------------------------------------------------------------------+
//| Find oldest hedge position ticket to close                      |
//+------------------------------------------------------------------+
long FindOldestHedgeToCloseTicket(string hedgeOrigin)
{
    ulong oldestTicket = 0;
    datetime oldestTime = LONG_MAX;

    int total = PositionsTotal();
    for(int i = 0; i < total; i++)
    {
        ulong ticket = PositionGetTicket(i);
        if(ticket == 0) continue;
        if(!PositionSelectByTicket(ticket)) continue;

        if(PositionGetString(POSITION_SYMBOL) != _Symbol) continue;
        if(PositionGetInteger(POSITION_MAGIC) != MagicNumber) continue;

        string comment = PositionGetString(POSITION_COMMENT);
        string searchStr = CommentPrefix + hedgeOrigin;

        if(StringFind(comment, searchStr) >= 0) {
            datetime posTime = (datetime)PositionGetInteger(POSITION_TIME);
            if(posTime < oldestTime) {
                oldestTime = posTime;
                oldestTicket = ticket;
            }
        }
    }

    return (long)oldestTicket;
}

//+------------------------------------------------------------------+
//| Process trade result for AC risk management                     |
//+------------------------------------------------------------------+
void ProcessTradeResult(bool isWin, string tradeId, double profit = 0.0)
{
    // Placeholder for future risk tracking.
}

//+------------------------------------------------------------------+
//| Hedge Run-Up helpers                                             |
//+------------------------------------------------------------------+
int FindRunUpStateIndex(ulong ticket)
{
    int total = ArraySize(g_runUpStates);
    for(int i = 0; i < total; i++)
    {
        if(g_runUpStates[i].ticket == ticket)
            return i;
    }
    return -1;
}

bool IsRunUpActiveForTicket(ulong ticket)
{
    return FindRunUpStateIndex(ticket) >= 0;
}

void CleanupRunUpStates()
{
    for(int i = ArraySize(g_runUpStates) - 1; i >= 0; i--)
    {
        ulong ticket = g_runUpStates[i].ticket;
        if(ticket == 0 || !PositionSelectByTicket(ticket))
            RemoveRunUpState(ticket);
    }
}

void RemoveRunUpState(ulong ticket)
{
    int idx = FindRunUpStateIndex(ticket);
    if(idx < 0)
        return;
    int last = ArraySize(g_runUpStates) - 1;
    if(idx != last)
        g_runUpStates[idx] = g_runUpStates[last];
    ArrayResize(g_runUpStates, last);
}

void GetRunUpParameters(double &outDistancePts, double &outStepPts)
{
    outDistancePts = HedgeRunUp_InitialDistancePts;
    outStepPts = HedgeRunUp_IncrementPoints;

    if(LotSizingMode == LOTS_INVERSE_PNL)
    {
        int tier = DetermineInversePnlTier();
        if(tier == 2)
        {
            outDistancePts = Tier2_RunUp_PointDist;
            outStepPts = Tier2_RunUp_PointStep;
        }
        else if(tier == 3)
        {
            outDistancePts = Tier3_RunUp_PointDist;
            outStepPts = Tier3_RunUp_PointStep;
        }
    }

    if(outDistancePts <= 0.0)
        outDistancePts = HedgeRunUp_InitialDistancePts;
    if(outStepPts <= 0.0)
        outStepPts = HedgeRunUp_IncrementPoints;
}

double ComputeRunUpDemaAtrPrice(int period, double &outDemaAtr)
{
    outDemaAtr = 0.0;
    int lookback = MathMax(2, period + 2);
    MqlRates rates[];
    int copied = CopyRates(_Symbol, PERIOD_CURRENT, 0, lookback, rates);
    if(copied < period + 1)
        return 0.0;

    double alpha = 2.0 / (period + 1);
    double ema1 = 0.0, ema2 = 0.0;
    bool init = false;

    // Iterate from oldest to newest so smoothing behaves consistently
    for(int i = copied - 2; i >= 0 && i >= copied - 1 - period; i--)
    {
        int prev = i + 1;
        double prevClose = rates[prev].close;
        double tr1 = rates[i].high - rates[i].low;
        double tr2 = MathAbs(rates[i].high - prevClose);
        double tr3 = MathAbs(rates[i].low - prevClose);
        double tr = MathMax(tr1, MathMax(tr2, tr3));

        if(!init)
        {
            ema1 = tr;
            ema2 = tr;
            init = true;
        }
        else
        {
            ema1 = ema1 + alpha * (tr - ema1);
            ema2 = ema2 + alpha * (ema1 - ema2);
        }
    }

    outDemaAtr = 2.0 * ema1 - ema2;
    return outDemaAtr;
}

double ComputeRunUpIncrementPoints(bool useDemaAtr, bool useReactiveAtr, double pointIncrementPts)
{
    double baseStep = pointIncrementPts;
    if(baseStep <= 0.0)
        baseStep = HedgeRunUp_IncrementPoints;

    if(!useDemaAtr)
        return baseStep;

    const int runupReactivePeriod = 8;
    const double runupReactiveMultiplier = 0.85;
    int period = useReactiveAtr ? MathMax(2, runupReactivePeriod) : MathMax(2, HedgeRunUp_DemaPeriod);
    double multiplier = useReactiveAtr ? MathMax(0.1, runupReactiveMultiplier) : MathMax(0.1, HedgeRunUp_DemaMultiplier);

    double demaAtrPrice = 0.0;
    ComputeRunUpDemaAtrPrice(period, demaAtrPrice);
    if(demaAtrPrice <= 0.0)
        return baseStep;

    double stepPoints = (demaAtrPrice * multiplier) / _Point;
    if(stepPoints <= 0.0)
        stepPoints = baseStep;
    return stepPoints;
}

bool StartHedgeRunUpForBaseId(const string &baseId, const string &closureReason, ulong explicitTicket = 0)
{
    if(!HedgeRunUp_Enabled)
        return false;

    ulong tickets[]; datetime openTimes[]; double volumes[];
    int available = 0;
    if(explicitTicket > 0)
    {
        if(PositionSelectByTicket(explicitTicket))
        {
            ArrayResize(tickets, 1); tickets[0] = explicitTicket;
            ArrayResize(openTimes, 1); openTimes[0] = (datetime)PositionGetInteger(POSITION_TIME);
            ArrayResize(volumes, 1); volumes[0] = PositionGetDouble(POSITION_VOLUME);
            available = 1;
        }
    }
    else
    {
        available = CollectPrimaryHedgeTicketsForBaseId(baseId, tickets, openTimes, volumes);
    }

    if(available <= 0)
    {
        { string __log=""; StringConcatenate(__log, "RUNUP_SKIP: No MT5 hedge tickets found for base_id ", baseId, " (closure_reason=", closureReason, ")"); Print(__log); ULogWarnPrint(__log); }
        return false;
    }

    SortTicketsByOpenTime(openTimes, tickets, volumes);
    double brokerMinPts = GetBrokerMinimumStopPoints();
    double runUpDistancePts = HedgeRunUp_InitialDistancePts;
    double runUpStepPts = HedgeRunUp_IncrementPoints;
    GetRunUpParameters(runUpDistancePts, runUpStepPts);
    double distancePts = MathMax(runUpDistancePts, brokerMinPts);
    bool anyActivated = false;

    for(int i = 0; i < ArraySize(tickets); i++)
    {
        ulong ticket = tickets[i];
        if(ticket == 0)
            continue;
        if(!PositionSelectByTicket(ticket))
            continue;

        if(IsRunUpActiveForTicket(ticket))
            continue;

        ENUM_POSITION_TYPE posType = (ENUM_POSITION_TYPE)PositionGetInteger(POSITION_TYPE);
        bool isLong = (posType == POSITION_TYPE_BUY);
        double anchorPrice = isLong ? SymbolInfoDouble(_Symbol, SYMBOL_BID) : SymbolInfoDouble(_Symbol, SYMBOL_ASK);
        if(anchorPrice <= 0.0)
            anchorPrice = PositionGetDouble(POSITION_PRICE_OPEN);
        if(anchorPrice <= 0.0)
            continue;

        double initialStop = isLong
            ? anchorPrice - distancePts * _Point
            : anchorPrice + distancePts * _Point;

        double currentSL = PositionGetDouble(POSITION_SL);
        bool shouldModify = (currentSL <= 0.0) ||
                            (isLong && initialStop > currentSL + (_Point * 0.1)) ||
                            (!isLong && initialStop < currentSL - (_Point * 0.1));

        if(shouldModify)
        {
            trade.SetExpertMagicNumber(MagicNumber);
            trade.SetDeviationInPoints(Slippage);
            bool modified = trade.PositionModify(ticket, initialStop, PositionGetDouble(POSITION_TP));
            if(!modified)
            {
                { string __log=""; StringConcatenate(__log, "RUNUP_WARN: Failed to place initial run-up stop for ticket ", (long)ticket, " rc=", trade.ResultRetcode(), " comment=", trade.ResultComment()); Print(__log); ULogWarnPrint(__log); }
                // Even if modify failed, continue to track so we can try to trail later
            }
            else
            {
                { string __log=""; StringConcatenate(__log, "RUNUP_START: ticket=", (long)ticket, " base_id=", baseId, " anchor=", DoubleToString(anchorPrice, _Digits), " stop=", DoubleToString(initialStop, _Digits), " distPts=", DoubleToString(distancePts, 1)); Print(__log); ULogInfoPrint(__log); }
            }
        }

        int idx = ArraySize(g_runUpStates);
        ArrayResize(g_runUpStates, idx + 1);
        g_runUpStates[idx].ticket = ticket;
        g_runUpStates[idx].baseId = baseId;
        g_runUpStates[idx].anchorPrice = anchorPrice;
        g_runUpStates[idx].initialDistancePts = distancePts;
        g_runUpStates[idx].incrementPoints = runUpStepPts;
        g_runUpStates[idx].useDemaAtr = (HedgeRunUp_IncrementMode == RunUpIncrement_DEMA_ATR);
        g_runUpStates[idx].useReactiveAtr = HedgeRunUp_UseReactiveATR;
        g_runUpStates[idx].lastStopPrice = shouldModify ? initialStop : currentSL;
        g_runUpStates[idx].lastUpdate = TimeCurrent();
        anyActivated = true;
    }

    if(anyActivated)
    {
        { string __log=""; StringConcatenate(__log, "RUNUP_ACTIVATED: Started MT5 hedge run-up for base_id ", baseId, " (reason=", closureReason, ")"); Print(__log); ULogInfoPrint(__log); }
    }

    return anyActivated;
}

bool UpdateRunUpTrailingForTicket(ulong ticket, ENUM_POSITION_TYPE posType, double currentPrice)
{
    int idx = FindRunUpStateIndex(ticket);
    if(idx < 0)
        return false;

    bool isLong = (posType == POSITION_TYPE_BUY);
    double anchor = g_runUpStates[idx].anchorPrice;
    if(anchor <= 0.0)
        anchor = currentPrice;

    double runUpDistancePts = HedgeRunUp_InitialDistancePts;
    double runUpStepPts = HedgeRunUp_IncrementPoints;
    GetRunUpParameters(runUpDistancePts, runUpStepPts);

    double distancePts = MathMax(runUpDistancePts, GetBrokerMinimumStopPoints());
    double incrementPts = ComputeRunUpIncrementPoints(g_runUpStates[idx].useDemaAtr, g_runUpStates[idx].useReactiveAtr, runUpStepPts);
    if(incrementPts <= 0.0)
        incrementPts = MathMax(runUpStepPts, HedgeRunUp_IncrementPoints);

    g_runUpStates[idx].initialDistancePts = runUpDistancePts;
    g_runUpStates[idx].incrementPoints = runUpStepPts;

    double progress = isLong ? (currentPrice - anchor) : (anchor - currentPrice);
    if(progress < 0.0)
        progress = 0.0;

    // Convert increment points to price units so step detection respects user inputs
    double stepSizePrice = incrementPts * _Point;
    double steps = (stepSizePrice > 0.0) ? MathFloor(progress / stepSizePrice) : 0.0;
    double desiredStop = isLong
        ? anchor - distancePts * _Point + steps * incrementPts * _Point
        : anchor + distancePts * _Point - steps * incrementPts * _Point;

    double minStopDist = MathMax(GetBrokerMinimumStopPoints(), 1.0) * _Point;
    if(isLong)
        desiredStop = MathMin(desiredStop, currentPrice - minStopDist);
    else
        desiredStop = MathMax(desiredStop, currentPrice + minStopDist);

    double lastStop = g_runUpStates[idx].lastStopPrice;
    if(desiredStop <= 0.0)
        return false;

    // Never loosen the stop
    if(lastStop > 0.0)
    {
        if(isLong && desiredStop <= lastStop + (_Point * 0.1))
        {
            if(steps >= 1.0) // price advanced but stop not better
            {
                { string __log=""; StringConcatenate(__log, "RUNUP_TRACE: No improvement (long) ticket=", (long)ticket,
                      " progressPts=", DoubleToString(progress / _Point, 2),
                      " steps=", DoubleToString(steps, 1),
                      " desiredStop=", DoubleToString(desiredStop, _Digits),
                      " lastStop=", DoubleToString(lastStop, _Digits)); Print(__log); ULogInfoPrint(__log); }
            }
            return false;
        }
        if(!isLong && desiredStop >= lastStop - (_Point * 0.1))
        {
            if(steps >= 1.0)
            {
                { string __log=""; StringConcatenate(__log, "RUNUP_TRACE: No improvement (short) ticket=", (long)ticket,
                      " progressPts=", DoubleToString(progress / _Point, 2),
                      " steps=", DoubleToString(steps, 1),
                      " desiredStop=", DoubleToString(desiredStop, _Digits),
                      " lastStop=", DoubleToString(lastStop, _Digits)); Print(__log); ULogInfoPrint(__log); }
            }
            return false;
        }
    }

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    bool modified = trade.PositionModify(ticket, desiredStop, PositionGetDouble(POSITION_TP));
    if(modified)
    {
        { string __log=""; StringConcatenate(__log, "RUNUP_TRAIL: ticket=", (long)ticket,
              " anchor=", DoubleToString(anchor, _Digits),
              " progressPts=", DoubleToString(progress / _Point, 2),
              " steps=", DoubleToString(steps, 1),
              " desiredStop=", DoubleToString(desiredStop, _Digits),
              " lastStop=", DoubleToString(lastStop, _Digits)); Print(__log); ULogInfoPrint(__log); }
        g_runUpStates[idx].lastStopPrice = desiredStop;
        g_runUpStates[idx].lastUpdate = TimeCurrent();
        return true;
    }
    else
    {
        { string __log=""; StringConcatenate(__log, "RUNUP_WARN: PositionModify failed for ticket ", (long)ticket,
              " desiredStop=", DoubleToString(desiredStop, _Digits),
              " retcode=", trade.ResultRetcode(),
              " comment=", trade.ResultComment()); Print(__log); ULogWarnPrint(__log); }
    }

    return false;
}

//+------------------------------------------------------------------+
//| Tier 1 Fixed Trailing (Dollars) helpers                          |
//+------------------------------------------------------------------+
int FindTier1DollarTrailStateIndex(ulong ticket)
{
    int total = ArraySize(g_tier1DollarTrailStates);
    for(int i = 0; i < total; i++)
    {
        if(g_tier1DollarTrailStates[i].ticket == ticket)
            return i;
    }
    return -1;
}

void RemoveTier1DollarTrailState(ulong ticket)
{
    int idx = FindTier1DollarTrailStateIndex(ticket);
    if(idx < 0)
        return;

    int last = ArraySize(g_tier1DollarTrailStates) - 1;
    if(idx != last)
        g_tier1DollarTrailStates[idx] = g_tier1DollarTrailStates[last];
    ArrayResize(g_tier1DollarTrailStates, last);
}

void CleanupTier1DollarTrailStates()
{
    for(int i = ArraySize(g_tier1DollarTrailStates) - 1; i >= 0; i--)
    {
        ulong ticket = g_tier1DollarTrailStates[i].ticket;
        if(ticket == 0 || !PositionSelectByTicket(ticket))
            RemoveTier1DollarTrailState(ticket);
    }
}

bool GetTier1DollarTrailingSettings(double &activationUsd, double &stepUsd, double &modificationUsd)
{
    activationUsd = 0.0;
    stepUsd = 0.0;
    modificationUsd = 0.0;

    if(!Tier1_DollarTrail_Enabled)
        return false;

    activationUsd = Tier1_DollarTrail_ActivationUSD;
    stepUsd = Tier1_DollarTrail_StepUSD;
    modificationUsd = Tier1_DollarTrail_ModificationUSD;

    if(activationUsd <= 0.0 || stepUsd <= 0.0 || modificationUsd <= 0.0)
        return false;

    return true;
}

double DollarsToPriceDistance(double dollars, double volume)
{
    if(dollars <= 0.0 || volume <= 0.0)
        return 0.0;

    double tickValue = 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE_PROFIT, tickValue) || tickValue <= 0.0)
        SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE, tickValue);

    double tickSize = 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE, tickSize) || tickSize <= 0.0)
        tickSize = _Point;

    if(tickValue <= 0.0 || tickSize <= 0.0)
        return 0.0;

    return (dollars / (tickValue * volume)) * tickSize;
}

double PriceDistanceToDollars(double priceDistance, double volume)
{
    if(priceDistance <= 0.0 || volume <= 0.0)
        return 0.0;

    double tickValue = 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE_PROFIT, tickValue) || tickValue <= 0.0)
        SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE, tickValue);

    double tickSize = 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE, tickSize) || tickSize <= 0.0)
        tickSize = _Point;

    if(tickValue <= 0.0 || tickSize <= 0.0)
        return 0.0;

    return (priceDistance / tickSize) * (tickValue * volume);
}

double GetPositionCommissionTotal(ulong ticket)
{
    if(ticket == 0)
        return 0.0;

    datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);
    if(openTime <= 0)
        openTime = TimeCurrent() - 3600;

    if(!HistorySelect(openTime - 60, TimeCurrent()))
        return 0.0;

    double commission = 0.0;
    int total = HistoryDealsTotal();
    for(int i = total - 1; i >= 0; i--)
    {
        ulong dealTicket = HistoryDealGetTicket(i);
        if(dealTicket == 0)
            continue;
        if((ulong)HistoryDealGetInteger(dealTicket, DEAL_POSITION_ID) != ticket)
            continue;
        double dealCommission = HistoryDealGetDouble(dealTicket, DEAL_COMMISSION);
        if(dealCommission < 0.0)
            commission += -dealCommission;
    }

    return commission;
}

bool HandleTier1DollarTrailingForPosition(ulong ticket, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice, double volume)
{
    if(LotSizingMode != LOTS_INVERSE_PNL)
        return false;
    if(ticket == 0 || entryPrice <= 0.0 || currentPrice <= 0.0 || volume <= 0.0)
        return false;

    double activationUsd = 0.0;
    double stepUsd = 0.0;
    double modificationUsd = 0.0;
    if(!GetTier1DollarTrailingSettings(activationUsd, stepUsd, modificationUsd))
    {
        RemoveTier1DollarTrailState(ticket);
        return false;
    }

    int tier = ResolveInverseTierForTicket(ticket);
    if(tier != 1)
    {
        RemoveTier1DollarTrailState(ticket);
        return false;
    }

    double profitUsd = PositionGetDouble(POSITION_PROFIT);
    if(profitUsd < activationUsd)
        return false;

    int idx = FindTier1DollarTrailStateIndex(ticket);
    double commissionUsd = 0.0;
    if(idx >= 0)
    {
        commissionUsd = g_tier1DollarTrailStates[idx].commissionUsd;
        if(commissionUsd <= 0.0)
        {
            double latestCommission = GetPositionCommissionTotal(ticket);
            if(latestCommission > 0.0)
            {
                commissionUsd = latestCommission;
                g_tier1DollarTrailStates[idx].commissionUsd = latestCommission;
            }
        }
    }
    else
    {
        commissionUsd = GetPositionCommissionTotal(ticket);
    }

    double extraCostUsd = commissionUsd;
    double swap = PositionGetDouble(POSITION_SWAP);
    if(swap < 0.0)
        extraCostUsd += -swap;

    double stepPrice = DollarsToPriceDistance(stepUsd, volume);
    double clampPrice = DollarsToPriceDistance(modificationUsd + extraCostUsd, volume);
    if(stepPrice <= 0.0 || clampPrice <= 0.0)
        return false;

    bool isLong = (posType == POSITION_TYPE_BUY);
    double minStopDist = MathMax(GetBrokerMinimumStopPoints(), 1.0) * _Point;
    double clampStop = isLong ? entryPrice + clampPrice : entryPrice - clampPrice;
    double minStopLimit = isLong ? currentPrice - minStopDist : currentPrice + minStopDist;

    if(idx < 0)
    {
        Tier1DollarTrailState state;
        state.ticket = ticket;
        state.anchorPrice = currentPrice;
        state.activationUsd = activationUsd;
        state.stepUsd = stepUsd;
        state.modificationUsd = modificationUsd;
        state.commissionUsd = commissionUsd;
        double currentSL = PositionGetDouble(POSITION_SL);
        state.lastStopPrice = currentSL;
        state.lastUpdate = TimeCurrent();

        if((isLong && clampStop > minStopLimit) || (!isLong && clampStop < minStopLimit))
            return false;

        double initialStop = clampStop;

        bool shouldModify = (currentSL <= 0.0) ||
                            (isLong && initialStop > currentSL + (_Point * 0.1)) ||
                            (!isLong && initialStop < currentSL - (_Point * 0.1));

        bool modified = false;
        if(shouldModify)
        {
            trade.SetExpertMagicNumber(MagicNumber);
            trade.SetDeviationInPoints(Slippage);
            modified = trade.PositionModify(ticket, initialStop, PositionGetDouble(POSITION_TP));
            if(modified)
            {
                state.lastStopPrice = initialStop;
                state.lastUpdate = TimeCurrent();
                { string __log=""; StringConcatenate(__log,
                    "T1DTRAIL_START: ticket=", (long)ticket,
                    " anchor=", DoubleToString(state.anchorPrice, _Digits),
                    " stop=", DoubleToString(initialStop, _Digits),
                    " actUsd=", DoubleToString(activationUsd, 2),
                    " stepUsd=", DoubleToString(stepUsd, 2),
                    " modUsd=", DoubleToString(modificationUsd, 2));
                  Print(__log); ULogInfoPrint(__log); }
            }
            else
            {
                { string __log=""; StringConcatenate(__log,
                    "T1DTRAIL_WARN: Failed to set initial stop for ticket ", (long)ticket,
                    " desiredStop=", DoubleToString(initialStop, _Digits),
                    " retcode=", trade.ResultRetcode(),
                    " comment=", trade.ResultComment());
                  Print(__log); ULogWarnPrint(__log); }
            }
        }

        if(currentSL > 0.0 || modified)
        {
            int newSize = ArraySize(g_tier1DollarTrailStates) + 1;
            ArrayResize(g_tier1DollarTrailStates, newSize);
            g_tier1DollarTrailStates[newSize - 1] = state;
        }
        return modified;
    }

    g_tier1DollarTrailStates[idx].activationUsd = activationUsd;
    g_tier1DollarTrailStates[idx].stepUsd = stepUsd;
    g_tier1DollarTrailStates[idx].modificationUsd = modificationUsd;

    double anchor = g_tier1DollarTrailStates[idx].anchorPrice;
    if(anchor <= 0.0)
    {
        anchor = currentPrice;
        g_tier1DollarTrailStates[idx].anchorPrice = anchor;
    }

    double progress = isLong ? (currentPrice - anchor) : (anchor - currentPrice);
    if(progress < 0.0)
        progress = 0.0;

    double steps = (stepPrice > 0.0) ? MathFloor(progress / stepPrice) : 0.0;
    double desiredStop = isLong
        ? clampStop + steps * stepPrice
        : clampStop - steps * stepPrice;

    if(isLong && desiredStop > minStopLimit)
    {
        if(minStopLimit < clampStop - (_Point * 0.1))
            return false;
        desiredStop = minStopLimit;
    }
    else if(!isLong && desiredStop < minStopLimit)
    {
        if(minStopLimit > clampStop + (_Point * 0.1))
            return false;
        desiredStop = minStopLimit;
    }

    double currentSL = PositionGetDouble(POSITION_SL);
    double lastStop = g_tier1DollarTrailStates[idx].lastStopPrice;
    if(currentSL > 0.0)
    {
        if(isLong)
        {
            if(lastStop <= 0.0 || currentSL > lastStop)
                lastStop = currentSL;
        }
        else
        {
            if(lastStop <= 0.0 || currentSL < lastStop)
                lastStop = currentSL;
        }
    }
    if(lastStop > 0.0)
        g_tier1DollarTrailStates[idx].lastStopPrice = lastStop;

    if(desiredStop <= 0.0)
        return false;

    if(lastStop > 0.0)
    {
        if(isLong && desiredStop <= lastStop + (_Point * 0.1))
            return false;
        if(!isLong && desiredStop >= lastStop - (_Point * 0.1))
            return false;
    }

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    bool modified = trade.PositionModify(ticket, desiredStop, PositionGetDouble(POSITION_TP));
    if(modified)
    {
        g_tier1DollarTrailStates[idx].lastStopPrice = desiredStop;
        g_tier1DollarTrailStates[idx].lastUpdate = TimeCurrent();
        { string __log=""; StringConcatenate(__log,
            "T1DTRAIL_UPDATE: ticket=", (long)ticket,
            " anchor=", DoubleToString(anchor, _Digits),
            " steps=", DoubleToString(steps, 1),
            " desiredStop=", DoubleToString(desiredStop, _Digits));
          Print(__log); ULogInfoPrint(__log); }
        return true;
    }
    else
    {
        { string __log=""; StringConcatenate(__log,
            "T1DTRAIL_WARN: PositionModify failed for ticket ", (long)ticket,
            " desiredStop=", DoubleToString(desiredStop, _Digits),
            " retcode=", trade.ResultRetcode(),
            " comment=", trade.ResultComment());
          Print(__log); ULogWarnPrint(__log); }
    }

    return false;
}

//+------------------------------------------------------------------+
//| Tier 2/3 Fixed Trailing helpers                                  |
//+------------------------------------------------------------------+
int FindTierFixedTrailStateIndex(ulong ticket)
{
    int total = ArraySize(g_tierFixedTrailStates);
    for(int i = 0; i < total; i++)
    {
        if(g_tierFixedTrailStates[i].ticket == ticket)
            return i;
    }
    return -1;
}

bool IsTierFixedTrailingActive(ulong ticket)
{
    return FindTierFixedTrailStateIndex(ticket) >= 0;
}

void RemoveTierFixedTrailState(ulong ticket)
{
    int idx = FindTierFixedTrailStateIndex(ticket);
    if(idx < 0)
        return;

    int last = ArraySize(g_tierFixedTrailStates) - 1;
    if(idx != last)
        g_tierFixedTrailStates[idx] = g_tierFixedTrailStates[last];
    ArrayResize(g_tierFixedTrailStates, last);
}

void CleanupTierFixedTrailStates()
{
    for(int i = ArraySize(g_tierFixedTrailStates) - 1; i >= 0; i--)
    {
        ulong ticket = g_tierFixedTrailStates[i].ticket;
        if(ticket == 0 || !PositionSelectByTicket(ticket))
        {
            if(g_inverse_tier_locks != NULL)
                g_inverse_tier_locks.Remove((long)ticket);
            RemoveTierFixedTrailState(ticket);
        }
    }
}

int ResolveInverseTierForTicket(ulong ticket)
{
    if(LotSizingMode != LOTS_INVERSE_PNL)
        return 1;

    if(g_inverse_tier_locks != NULL)
    {
        int storedTier = 0;
        if(g_inverse_tier_locks.TryGetValue((long)ticket, storedTier) && storedTier > 0)
            return storedTier;
    }

    int tier = ResolveInversePnlTier();
    if(g_inverse_tier_locks != NULL && tier > 0)
        g_inverse_tier_locks.Add((long)ticket, tier);
    return tier;
}

bool GetTierFixedTrailingSettings(int tier, double &activationPts, double &stepPts, double &modificationPts)
{
    activationPts = 0.0;
    stepPts = 0.0;
    modificationPts = 0.0;

    if(tier == 2)
    {
        activationPts = Tier2_FixedTrail_ActivationPts;
        stepPts = Tier2_FixedTrail_StepPts;
        modificationPts = Tier2_FixedTrail_ModificationPts;
    }
    else if(tier == 3)
    {
        activationPts = Tier3_FixedTrail_ActivationPts;
        stepPts = Tier3_FixedTrail_StepPts;
        modificationPts = Tier3_FixedTrail_ModificationPts;
    }
    else
    {
        return false;
    }

    if(activationPts <= 0.0 || stepPts <= 0.0 || modificationPts <= 0.0)
        return false;

    return true;
}

bool HandleTierFixedTrailingForPosition(ulong ticket, ENUM_POSITION_TYPE posType, double entryPrice, double currentPrice)
{
    if(LotSizingMode != LOTS_INVERSE_PNL)
        return false;
    if(ticket == 0 || entryPrice <= 0.0 || currentPrice <= 0.0)
        return false;

    int tier = ResolveInverseTierForTicket(ticket);
    double activationPts = 0.0;
    double stepPts = 0.0;
    double modificationPts = 0.0;
    if(!GetTierFixedTrailingSettings(tier, activationPts, stepPts, modificationPts))
        return false;

    bool isLong = (posType == POSITION_TYPE_BUY);
    double profitPts = isLong ? (currentPrice - entryPrice) / _Point
                              : (entryPrice - currentPrice) / _Point;
    if(profitPts < activationPts)
        return false;

    double minStopDist = MathMax(GetBrokerMinimumStopPoints(), 1.0) * _Point;
    int idx = FindTierFixedTrailStateIndex(ticket);
    if(idx < 0)
    {
        TierFixedTrailState state;
        state.ticket = ticket;
        state.tier = tier;
        state.anchorPrice = currentPrice;
        state.activationTriggerPts = activationPts;
        state.stepPts = stepPts;
        state.modificationPts = modificationPts;
        double currentSL = PositionGetDouble(POSITION_SL);
        state.lastStopPrice = currentSL;
        state.lastUpdate = TimeCurrent();

        double initialStop = isLong
            ? currentPrice - modificationPts * _Point
            : currentPrice + modificationPts * _Point;

        if(isLong)
            initialStop = MathMin(initialStop, currentPrice - minStopDist);
        else
            initialStop = MathMax(initialStop, currentPrice + minStopDist);

        bool shouldModify = (currentSL <= 0.0) ||
                            (isLong && initialStop > currentSL + (_Point * 0.1)) ||
                            (!isLong && initialStop < currentSL - (_Point * 0.1));

        bool modified = false;
        if(shouldModify)
        {
            trade.SetExpertMagicNumber(MagicNumber);
            trade.SetDeviationInPoints(Slippage);
            modified = trade.PositionModify(ticket, initialStop, PositionGetDouble(POSITION_TP));
            if(modified)
            {
                state.lastStopPrice = initialStop;
                state.lastUpdate = TimeCurrent();
                { string __log=""; StringConcatenate(__log,
                    "FIXTRAIL_START: ticket=", (long)ticket,
                    " tier=", tier,
                    " anchor=", DoubleToString(state.anchorPrice, _Digits),
                    " stop=", DoubleToString(initialStop, _Digits),
                    " actPts=", DoubleToString(activationPts, 1),
                    " stepPts=", DoubleToString(stepPts, 1),
                    " modPts=", DoubleToString(modificationPts, 1));
                  Print(__log); ULogInfoPrint(__log); }
            }
            else
            {
                { string __log=""; StringConcatenate(__log,
                    "FIXTRAIL_WARN: Failed to set initial stop for ticket ", (long)ticket,
                    " desiredStop=", DoubleToString(initialStop, _Digits),
                    " retcode=", trade.ResultRetcode(),
                    " comment=", trade.ResultComment());
                  Print(__log); ULogWarnPrint(__log); }
            }
        }

        if(currentSL > 0.0 || modified)
        {
            int newSize = ArraySize(g_tierFixedTrailStates) + 1;
            ArrayResize(g_tierFixedTrailStates, newSize);
            g_tierFixedTrailStates[newSize - 1] = state;
        }
        return modified;
    }

    g_tierFixedTrailStates[idx].activationTriggerPts = activationPts;
    g_tierFixedTrailStates[idx].stepPts = stepPts;
    g_tierFixedTrailStates[idx].modificationPts = modificationPts;

    double anchor = g_tierFixedTrailStates[idx].anchorPrice;
    if(anchor <= 0.0)
    {
        anchor = currentPrice;
        g_tierFixedTrailStates[idx].anchorPrice = anchor;
    }

    double progress = isLong ? (currentPrice - anchor) : (anchor - currentPrice);
    if(progress < 0.0)
        progress = 0.0;

    double stepSizePrice = stepPts * _Point;
    double steps = (stepSizePrice > 0.0) ? MathFloor(progress / stepSizePrice) : 0.0;
    double desiredStop = isLong
        ? anchor - modificationPts * _Point + steps * stepPts * _Point
        : anchor + modificationPts * _Point - steps * stepPts * _Point;

    if(isLong)
        desiredStop = MathMin(desiredStop, currentPrice - minStopDist);
    else
        desiredStop = MathMax(desiredStop, currentPrice + minStopDist);

    double currentSL = PositionGetDouble(POSITION_SL);
    double lastStop = g_tierFixedTrailStates[idx].lastStopPrice;
    if(currentSL > 0.0)
    {
        if(isLong)
        {
            if(lastStop <= 0.0 || currentSL > lastStop)
                lastStop = currentSL;
        }
        else
        {
            if(lastStop <= 0.0 || currentSL < lastStop)
                lastStop = currentSL;
        }
    }
    if(lastStop > 0.0)
        g_tierFixedTrailStates[idx].lastStopPrice = lastStop;

    if(desiredStop <= 0.0)
        return false;

    if(lastStop > 0.0)
    {
        if(isLong && desiredStop <= lastStop + (_Point * 0.1))
            return false;
        if(!isLong && desiredStop >= lastStop - (_Point * 0.1))
            return false;
    }

    trade.SetExpertMagicNumber(MagicNumber);
    trade.SetDeviationInPoints(Slippage);
    bool modified = trade.PositionModify(ticket, desiredStop, PositionGetDouble(POSITION_TP));
    if(modified)
    {
        g_tierFixedTrailStates[idx].lastStopPrice = desiredStop;
        g_tierFixedTrailStates[idx].lastUpdate = TimeCurrent();
        { string __log=""; StringConcatenate(__log,
            "FIXTRAIL_UPDATE: ticket=", (long)ticket,
            " tier=", g_tierFixedTrailStates[idx].tier,
            " anchor=", DoubleToString(anchor, _Digits),
            " steps=", DoubleToString(steps, 1),
            " desiredStop=", DoubleToString(desiredStop, _Digits));
          Print(__log); ULogInfoPrint(__log); }
        return true;
    }
    else
    {
        { string __log=""; StringConcatenate(__log,
            "FIXTRAIL_WARN: PositionModify failed for ticket ", (long)ticket,
            " desiredStop=", DoubleToString(desiredStop, _Digits),
            " retcode=", trade.ResultRetcode(),
            " comment=", trade.ResultComment());
          Print(__log); ULogWarnPrint(__log); }
    }

    return false;
}

// Helper: basic detector for index CFD symbols (reduces oversizing risk)
bool __IsIndexCFD(string sym)
{
    string up = sym;
    StringToUpper(up);
    if(StringFind(up, "NAS")   >= 0 || StringFind(up, "US100") >= 0 || StringFind(up, "NAS100") >= 0) return true;
    if(StringFind(up, "US30")  >= 0 || StringFind(up, "DJ30")  >= 0 || StringFind(up, "DOW")    >= 0) return true;
    if(StringFind(up, "SPX")   >= 0 || StringFind(up, "US500") >= 0 || StringFind(up, "SP500")  >= 0) return true;
    if(StringFind(up, "GER40") >= 0 || StringFind(up, "DE40")  >= 0) return true;
    if(StringFind(up, "UK100") >= 0 || StringFind(up, "FTSE")  >= 0) return true;
    return false;
}

void UpdateCandleCountdown()
{
    datetime now = TimeCurrent();
    if(now <= 0)
        return;
    if(now == g_last_candle_countdown_update)
        return;
    g_last_candle_countdown_update = now;

    int periodSec = PeriodSeconds(_Period);
    if(periodSec <= 0)
        return;

    datetime barOpen = iTime(_Symbol, _Period, 0);
    if(barOpen <= 0)
        return;

    int remaining = (int)(barOpen + periodSec - now);
    if(remaining < 0)
        remaining = 0;

    int hours = remaining / 3600;
    int minutes = (remaining % 3600) / 60;
    int seconds = remaining % 60;
    string text = (hours > 0)
        ? StringFormat("%02d:%02d:%02d", hours, minutes, seconds)
        : StringFormat("%02d:%02d", minutes, seconds);

    double price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    if(price <= 0.0)
        price = SymbolInfoDouble(_Symbol, SYMBOL_LAST);
    if(price <= 0.0)
        price = iClose(_Symbol, _Period, 0);
    if(price <= 0.0)
        return;

    int x = 0;
    int y = 0;
    datetime barRight = barOpen + periodSec;
    if(!ChartTimePriceToXY(0, 0, barRight, price, x, y))
    {
        if(!ChartTimePriceToXY(0, 0, barOpen, price, x, y))
            return;
    }

    string name = CandleCountdownObjName;
    if(ObjectFind(0, name) < 0)
    {
        ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);
        ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
        ObjectSetInteger(0, name, OBJPROP_ANCHOR, ANCHOR_LEFT);
        ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 12);
        ObjectSetString(0, name, OBJPROP_FONT, "Arial");
        ObjectSetInteger(0, name, OBJPROP_COLOR, clrWhite);
        ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
        ObjectSetInteger(0, name, OBJPROP_BACK, false);
    }

    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x + 12);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
}
