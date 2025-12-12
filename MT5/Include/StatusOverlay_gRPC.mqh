//+------------------------------------------------------------------+
//| StatusOverlay.mqh                                                |
//| Elastic Hedging Telemetry Overlay                               |
//+------------------------------------------------------------------+

// Global variables for overlay
string g_overlayPrefix = "ElasticOverlay_";
int g_overlayX = 10;
int g_overlayY = 50;
color g_overlayTextColor = clrWhite;
color g_overlayBackColor = clrDarkBlue;
int g_overlayFontSize = 9;
string g_overlayFont = "Consolas";
const int OVERLAY_MAX_PLANNER_LINES = 10;
string g_overlayPlannerLines[];
datetime g_overlayPlannerLastUpdate = 0;
bool g_overlayPlannerHasPlan = false;
bool g_overlayPlannerTargetAchieved = false;
double g_overlayPlannerProjectedLoss = 0.0;

#ifndef SELF_ELASTIC_MODE
    #ifdef Self_Elastic_Closures
        #define SELF_ELASTIC_MODE Self_Elastic_Closures
    #else
        #define SELF_ELASTIC_MODE Elastic_Hedging
    #endif
#endif

// Cushion band colors (adjusted for $300 account)
color GetCushionColor(double cushion)
{
    if(cushion >= 73) return clrLimeGreen;      // SAFE
    if(cushion >= 55) return clrYellow;         // LOW RISK
    if(cushion >= 37) return clrOrange;         // MEDIUM RISK
    if(cushion >= 19) return clrRed;            // HIGH RISK
    return clrDarkRed;                          // DANGER
}

// Initialize the status overlay
void InitStatusOverlay()
{
    // Create background rectangle
    string bgName = g_overlayPrefix + "Background";
    ObjectCreate(0, bgName, OBJ_RECTANGLE_LABEL, 0, 0, 0);
    ObjectSetInteger(0, bgName, OBJPROP_XDISTANCE, g_overlayX);
    ObjectSetInteger(0, bgName, OBJPROP_YDISTANCE, g_overlayY);
    ObjectSetInteger(0, bgName, OBJPROP_XSIZE, 300);
    ObjectSetInteger(0, bgName, OBJPROP_YSIZE, 260);
    ObjectSetInteger(0, bgName, OBJPROP_BGCOLOR, g_overlayBackColor);
    ObjectSetInteger(0, bgName, OBJPROP_BORDER_TYPE, BORDER_FLAT);
    ObjectSetInteger(0, bgName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, bgName, OBJPROP_STYLE, STYLE_SOLID);
    ObjectSetInteger(0, bgName, OBJPROP_WIDTH, 1);
    ObjectSetInteger(0, bgName, OBJPROP_BACK, false);
    ObjectSetInteger(0, bgName, OBJPROP_SELECTABLE, false);
    ObjectSetInteger(0, bgName, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, bgName, OBJPROP_HIDDEN, true);
    
    Print("StatusOverlay: Initialized background");
}

// Create or update a text label
void CreateOrUpdateLabel(string name, string text, int yOffset, color textColor = clrWhite)
{
    string fullName = g_overlayPrefix + name;
    
    if(ObjectFind(0, fullName) < 0)
    {
        ObjectCreate(0, fullName, OBJ_LABEL, 0, 0, 0);
        ObjectSetInteger(0, fullName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
        ObjectSetInteger(0, fullName, OBJPROP_ANCHOR, ANCHOR_LEFT_UPPER);
        ObjectSetInteger(0, fullName, OBJPROP_BACK, false);
        ObjectSetInteger(0, fullName, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(0, fullName, OBJPROP_SELECTED, false);
        ObjectSetInteger(0, fullName, OBJPROP_HIDDEN, true);
        ObjectSetString(0, fullName, OBJPROP_FONT, g_overlayFont);
        ObjectSetInteger(0, fullName, OBJPROP_FONTSIZE, g_overlayFontSize);
    }
    
    ObjectSetInteger(0, fullName, OBJPROP_XDISTANCE, g_overlayX + 5);
    ObjectSetInteger(0, fullName, OBJPROP_YDISTANCE, g_overlayY + yOffset);
    ObjectSetInteger(0, fullName, OBJPROP_COLOR, textColor);
    ObjectSetString(0, fullName, OBJPROP_TEXT, text);
}

void DeleteOverlayLabel(string name)
{
    string fullName = g_overlayPrefix + name;
    if(ObjectFind(0, fullName) >= 0)
        ObjectDelete(0, fullName);
}

void OverlayClearPlannerStatistics()
{
    g_overlayPlannerHasPlan = false;
    g_overlayPlannerTargetAchieved = false;
    g_overlayPlannerProjectedLoss = 0.0;
    g_overlayPlannerLastUpdate = 0;
    ArrayResize(g_overlayPlannerLines, 0);
}

void OverlaySetPlannerStatistics(const string summary, double projectedLoss, bool targetAchieved)
{
    g_overlayPlannerProjectedLoss = projectedLoss;
    g_overlayPlannerTargetAchieved = targetAchieved;
    g_overlayPlannerLastUpdate = TimeCurrent();
    ArrayResize(g_overlayPlannerLines, 0);

    if(summary == "")
    {
        g_overlayPlannerHasPlan = false;
        return;
    }

    string parts[];
    const ushort newline = 10; // avoid implicit string->number conversion warning
    int count = StringSplit(summary, newline, parts);

    if(count > 0)
    {
        g_overlayPlannerHasPlan = true;
        ArrayResize(g_overlayPlannerLines, count);
        for(int i = 0; i < count; ++i)
            g_overlayPlannerLines[i] = parts[i];
    }
    else
    {
        g_overlayPlannerHasPlan = false;
    }
}

// Global variables for caching overlay calculations
static double g_cached_next_lot_est = 0.0;
static datetime g_last_calculation_time = 0;
static double g_last_balance_for_calc = 0.0;
static double g_last_global_futures_for_calc = 0.0;
static bool g_force_recalculation = false;

// WHACK-A-MOLE FIX: Enhanced state tracking for better change detection
static double g_last_cushion_for_calc = 0.0;
static double g_last_ohf_for_calc = 0.0;
static double g_last_nt_balance_for_overlay = 0.0;
static double g_last_nt_daily_pnl_for_overlay = 0.0;
static string g_last_nt_result_for_overlay = "";
static int g_last_nt_session_trades_for_overlay = 0;
static datetime g_last_forced_recalc_time = 0;

// WHACK-A-MOLE DEBUG: Flag to enable/disable debug logging for overlay calculations
static bool g_overlay_debug_enabled = false; // Disable after fixing whack-a-mole issue

// Minimum time between forced recalculations (prevent spam)
const int MIN_FORCED_RECALC_INTERVAL = 30; // 30 seconds

// Force recalculation on next overlay update (call this when state changes)
void ForceOverlayRecalculation()
{
    // WHACK-A-MOLE FIX: Throttle forced recalculations to prevent spam
    datetime current_time = TimeCurrent();
    if(current_time - g_last_forced_recalc_time < MIN_FORCED_RECALC_INTERVAL)
    {
        if(g_overlay_debug_enabled) {
            Print("OVERLAY_THROTTLE: Ignoring forced recalculation request (too soon). Last: ",
                  TimeToString(g_last_forced_recalc_time), ", Current: ", TimeToString(current_time));
        }
        return;
    }

    g_force_recalculation = true;
    g_last_forced_recalc_time = current_time;

    if(g_overlay_debug_enabled) {
        Print("OVERLAY_FORCE: Forced recalculation scheduled at ", TimeToString(current_time));
    }
}

// Update the status overlay with current data
void UpdateStatusOverlay()
{
    bool isElastic = (LotSizingMode == SELF_ELASTIC_MODE);
    bool isInverse = (LotSizingMode == LOTS_INVERSE_PNL);

    // Only show overlay when in supported modes
    if(!isElastic && !isInverse)
    {
        RemoveStatusOverlay();
        return;
    }

    // Inverse PnL overlay path
    if(isInverse)
    {
        double balance = AccountInfoDouble(ACCOUNT_BALANCE);
        double nextLotEst = (g_inversePnlNextLot > 0.0) ? g_inversePnlNextLot : Tier1_Lots;
        int openHedgeCount = CountAllHedgePositions();
        string tierText = IntegerToString(g_inversePnlTier);

        CreateOrUpdateLabel("Title", "=== INVERSE PNL TELEMETRY ===", 5, clrCyan);
        CreateOrUpdateLabel("Balance", StringFormat("Balance:        $%.2f", balance), 25);
        CreateOrUpdateLabel("Mode", "Mode:           INVERSE PNL", 40);
        CreateOrUpdateLabel("NTDailyPnL", StringFormat("NT Daily PnL:   $%.2f", g_NT_Daily_PnL), 55);
        CreateOrUpdateLabel("InverseTier", StringFormat("Current Tier:   %s", tierText), 70);
        CreateOrUpdateLabel("NextLot", StringFormat("Next Lot (est): %.2f", nextLotEst), 85);
        CreateOrUpdateLabel("OpenHedges", StringFormat("Open Hedges:    %d", openHedgeCount), 100);

        // Clean planner-specific labels when not in elastic mode
        DeleteOverlayLabel("PlannerHeader");
        DeleteOverlayLabel("PlannerStatus");
        DeleteOverlayLabel("PlannerUpdated");
        for(int i = 0; i < OVERLAY_MAX_PLANNER_LINES; ++i)
        {
            string labelName = StringFormat("PlannerLine%d", i + 1);
            DeleteOverlayLabel(labelName);
        }
        return;
    }

    double balance = AccountInfoDouble(ACCOUNT_BALANCE);
    double cushion = g_lastCushion;
    double ohf = g_lastOHF;
    string mode = EnumToString(LotSizingMode);

    // WHACK-A-MOLE FIX: Enhanced change detection with more state variables
    bool balance_changed = (MathAbs(balance - g_last_balance_for_calc) > 0.01);
    bool futures_changed = false;
    bool cushion_changed = (MathAbs(g_lastCushion - g_last_cushion_for_calc) > 0.01);
    bool ohf_changed = (MathAbs(g_lastOHF - g_last_ohf_for_calc) > 0.01);
    bool nt_data_changed = (MathAbs(g_lastNTBalance - g_last_nt_balance_for_overlay) > 0.01) ||
                          (MathAbs(g_ntDailyPnL - g_last_nt_daily_pnl_for_overlay) > 0.01) ||
                          (g_lastNTTradeResult != g_last_nt_result_for_overlay) ||
                          (g_ntSessionTrades != g_last_nt_session_trades_for_overlay);
    bool time_expired = (TimeCurrent() - g_last_calculation_time > 300); // 5 minutes max

    bool need_recalculation = g_force_recalculation || balance_changed || futures_changed ||
                             cushion_changed || ohf_changed || nt_data_changed || time_expired;

    double nextLotEst;
    if(need_recalculation)
    {
        if(g_overlay_debug_enabled) {
            Print("OVERLAY_CALC: Recalculation triggered - Force:", g_force_recalculation,
                  " Balance:", balance_changed, " Futures:", futures_changed,
                  " Cushion:", cushion_changed, " OHF:", ohf_changed,
                  " NT:", nt_data_changed, " Time:", time_expired);
        }

        // Calculate next lot estimate based on lot sizing mode
        if (LotSizingMode == SELF_ELASTIC_MODE) {
            // Use tier-based calculation for elastic hedging
        double targetProfit;
        bool isHighRiskTier = (g_ntDailyPnL <= -1000.0); // Tier 2 threshold
            
            if (isHighRiskTier) {
                targetProfit = 200.0; // Tier 2 target
            } else {
                targetProfit = 70.0;  // Tier 1 target
            }
            
            double pointsMove = 50.0 * 100.0; // 50 NT points * conversion = 5000 MT5 points
            double tickValue = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE);
            double tickSize = SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE);
            double pointSize = SymbolInfoDouble(_Symbol, SYMBOL_POINT);
            double pointValue = (tickValue / tickSize) * pointSize;
            
            if (pointValue > 0) {
                nextLotEst = targetProfit / (pointsMove * pointValue);
            } else {
                nextLotEst = 1.0; // Default fallback
            }
        } else {
            nextLotEst = DefaultLot; // Use default for other modes
        }

        // Update all cached values
        g_cached_next_lot_est = nextLotEst;
        g_last_calculation_time = TimeCurrent();
        g_last_balance_for_calc = balance;
        g_last_global_futures_for_calc = balance;
        g_last_cushion_for_calc = cushion;
        g_last_ohf_for_calc = ohf;
        g_last_nt_balance_for_overlay = g_lastNTBalance;
        g_last_nt_daily_pnl_for_overlay = g_ntDailyPnL;
        g_last_nt_result_for_overlay = g_lastNTTradeResult;
        g_last_nt_session_trades_for_overlay = g_ntSessionTrades;
        g_force_recalculation = false;

        if(g_overlay_debug_enabled) {
            Print("OVERLAY_CALC: Recalculated lot estimate: ", nextLotEst,
                  " (Balance: $", balance, ", Futures: ", 0.0,
                  ", Cushion: $", cushion, ", OHF: ", ohf, ")");
        }
    }
    else
    {
        // Use cached value to avoid triggering whack-a-mole calculations
        nextLotEst = g_cached_next_lot_est;

        if(g_overlay_debug_enabled && (TimeCurrent() % 60 == 0)) { // Log once per minute when using cache
            Print("OVERLAY_CACHE: Using cached lot estimate: ", nextLotEst, " (no state changes detected)");
        }
    }

    // Count hedge positions
    int openHedgeCount = CountAllHedgePositions();
    
    // Create title
    CreateOrUpdateLabel("Title", "=== ELASTIC HEDGING TELEMETRY ===", 5, clrCyan);
    
    // Balance
    CreateOrUpdateLabel("Balance", StringFormat("Balance:        $%.2f", balance), 25);
    
    // Mode
    CreateOrUpdateLabel("Mode", StringFormat("Mode:           %s", mode), 40);
    
    // Next lot estimate
    CreateOrUpdateLabel("NextLot", StringFormat("Next Lot (est): %.2f", nextLotEst), 55);
    
    // Open hedge count
    CreateOrUpdateLabel("OpenHedges", StringFormat("Open Hedges:    %d", openHedgeCount), 70);

    // Remove deprecated labels if they still exist
    DeleteOverlayLabel("EODHigh");
    DeleteOverlayLabel("Cushion");
    DeleteOverlayLabel("OHF");
    DeleteOverlayLabel("GlobalFutures");
    DeleteOverlayLabel("DesiredHedges");
    DeleteOverlayLabel("BandDesc");
    DeleteOverlayLabel("LastUpdate");

    int yOffset = 90;
    CreateOrUpdateLabel("PlannerHeader", "--- Planner Statistics ---", yOffset, clrYellow);
    yOffset += 15;

    if(g_overlayPlannerHasPlan && ArraySize(g_overlayPlannerLines) > 0)
    {
        int linesToShow = MathMin(ArraySize(g_overlayPlannerLines), OVERLAY_MAX_PLANNER_LINES);
        for(int i = 0; i < linesToShow; ++i)
        {
            string labelName = StringFormat("PlannerLine%d", i + 1);
            CreateOrUpdateLabel(labelName, g_overlayPlannerLines[i], yOffset, clrWhite);
            yOffset += 15;
        }
        for(int i = linesToShow; i < OVERLAY_MAX_PLANNER_LINES; ++i)
        {
            string labelName = StringFormat("PlannerLine%d", i + 1);
            DeleteOverlayLabel(labelName);
        }
        string statusLabel = g_overlayPlannerTargetAchieved ? "Plan Status:    Target Achieved" : "Plan Status:    Needs Compression";
        CreateOrUpdateLabel("PlannerStatus", statusLabel, yOffset, g_overlayPlannerTargetAchieved ? clrLime : clrOrange);
        yOffset += 15;
        string updatedText = StringFormat("Plan Updated:   %s",
                                          g_overlayPlannerLastUpdate > 0
                                          ? TimeToString(g_overlayPlannerLastUpdate, TIME_SECONDS)
                                          : "n/a");
        CreateOrUpdateLabel("PlannerUpdated", updatedText, yOffset, clrSilver);
    }
    else
    {
        CreateOrUpdateLabel("PlannerLine1", "Planner stats pending…", yOffset, clrSilver);
        yOffset += 15;
        for(int i = 1; i < OVERLAY_MAX_PLANNER_LINES; ++i)
        {
            string labelName = StringFormat("PlannerLine%d", i + 1);
            DeleteOverlayLabel(labelName);
        }
        DeleteOverlayLabel("PlannerStatus");
        DeleteOverlayLabel("PlannerUpdated");
    }
}

// Count current hedge positions (all types)
int CountAllHedgePositions()
{
    int count = 0;
    for(int i = 0; i < PositionsTotal(); i++)
    {
        if(PositionGetTicket(i) > 0)
        {
            if(PositionGetInteger(POSITION_MAGIC) == MagicNumber &&
               PositionGetString(POSITION_SYMBOL) == _Symbol)
            {
                string comment = PositionGetString(POSITION_COMMENT);
                if(StringFind(comment, CommentPrefix) >= 0)
                {
                    count++;
                }
            }
        }
    }
    return count;
}

// Remove the status overlay
void RemoveStatusOverlay()
{
    string objects[] = {
        "Background", "Title", "Balance", "Mode", "NTDailyPnL", "InverseTier", "NextLot", "OpenHedges",
        "PlannerHeader", "PlannerStatus", "PlannerUpdated",
        "EODHigh", "Cushion", "OHF", "GlobalFutures", "DesiredHedges", "BandDesc", "LastUpdate"
    };
    
    for(int i = 0; i < ArraySize(objects); i++)
    {
        DeleteOverlayLabel(objects[i]);
    }

    for(int i = 0; i < OVERLAY_MAX_PLANNER_LINES; ++i)
    {
        string labelName = StringFormat("PlannerLine%d", i + 1);
        DeleteOverlayLabel(labelName);
    }

    OverlayClearPlannerStatistics();
}
