#property copyright ""
#property link      ""
#property version   "1.00"
#property strict
#property description "Standalone martingale / DCA planner with draggable chart ladder and basket TP/SL math"

input group "===== Planner Inputs =====";
enum PLANNER_DIRECTION
{
    PlannerDirection_Long = 0,
    PlannerDirection_Short = 1
};
enum PLANNER_LEVEL_COUNT_MODE
{
    PlannerLevelCount_AutoFillToFinalSL = 0,
    PlannerLevelCount_UserMaxLevels = 1
};
enum PLANNER_LOT_MODE
{
    PlannerLotMode_AutoMaintainMinTp = 0,
    PlannerLotMode_FixedScaleInLots = 1
};
enum PLANNER_MAX_LOSS_MODE
{
    PlannerMaxLossMode_OverrideMinFinalTp = 0,
    PlannerMaxLossMode_ShowWarning = 1
};
input PLANNER_DIRECTION         InpDirection                  = PlannerDirection_Short;
input PLANNER_LEVEL_COUNT_MODE  InpLevelCountMode             = PlannerLevelCount_AutoFillToFinalSL;
input PLANNER_LOT_MODE          InpLotMode                    = PlannerLotMode_AutoMaintainMinTp;
input PLANNER_MAX_LOSS_MODE     InpMaxLossMode                = PlannerMaxLossMode_OverrideMinFinalTp;
input double                    InpFinalTakeProfitPoints      = 10000.0;
input double                    InpFinalStopLossPoints        = 25000.0;
input double                    InpMinimumFinalTakeProfitUsd  = 25.0;
input double                    InpInitialHedgeLot            = 0.10;
input double                    InpLevelSpacingPoints         = 1000.0;
input double                    InpSpacingMultiplier          = 1.00;
input int                       InpMaxLevels                  = 6;
input double                    InpFixedScaleInLot            = 0.10;
input double                    InpMaxLossUsd                 = 0.0;
input double                    InpMaxTpOverridePercent       = 70.0;

struct PlannerLevel
{
    double distance_points;
    double price;
    double lot;
    bool   manual_price;
    bool   manual_lot;
};

const int    PANEL_X = 10;
const int    PANEL_Y = 40;
const int    PANEL_W = 760;
const int    PANEL_H = 680;
const int    LEVEL_ROWS_PER_PAGE = 8;
const int    MAX_LEVELS_LIMIT = 60;
const double MAX_LOSS_LEVEL_ZONE_FRACTION = 0.70;
const int    CALLOUT_BASE_X_GAP = 28;
const int    CALLOUT_LANE_STEP = 188;
const int    CALLOUT_MAIN_GAP_Y = 18;
const int    CALLOUT_LEVEL_GAP_Y = 14;
const int    CALLOUT_MAIN_W = 252;
const int    CALLOUT_MAIN_H = 24;
const int    CALLOUT_LEVEL_W = 176;
const int    CALLOUT_LEVEL_ACTIVE_W = 304;
const int    CALLOUT_LEVEL_H = 20;
const int    CALLOUT_TEXT_PAD_X = 8;
const int    CALLOUT_TEXT_PAD_Y = 3;
const int    CALLOUT_DRAWDOWN_X = 122;
const int    PANEL_HEADER_H = 42;
const int    PANEL_CONTENT_MARGIN = 14;
const int    PANEL_CONTENT_TOP = 56;
const int    PANEL_MIN_W = 420;
const int    PANEL_MIN_H = 300;
const int    PANEL_RESIZE_HANDLE = 18;
const int    PANEL_SCROLL_BUTTON = 18;
const int    PANEL_SCROLL_STEP = 48;
const int    PANEL_RESIZE_STEP_W = 80;
const int    PANEL_RESIZE_STEP_H = 60;
const int    PANEL_MINIMIZED_W = 360;
const int    PANEL_MINIMIZED_H = PANEL_HEADER_H + 2;
const string PANEL_FONT = "Segoe UI";
const string PANEL_FONT_BOLD = "Segoe UI Semibold";
const string PLANNER_OBJECT_PREFIX_ROOT = "EHMP_";

string g_prefix = "";

PLANNER_DIRECTION         g_direction;
PLANNER_LEVEL_COUNT_MODE  g_level_count_mode;
PLANNER_LOT_MODE          g_lot_mode;
PLANNER_MAX_LOSS_MODE     g_max_loss_mode;
double                    g_final_tp_points = 0.0;
double                    g_final_sl_points = 0.0;
double                    g_min_final_tp_usd = 0.0;
double                    g_initial_lot = 0.0;
double                    g_spacing_points = 0.0;
double                    g_spacing_multiplier = 1.0;
int                       g_max_levels = 0;
double                    g_fixed_scale_in_lot = 0.0;
double                    g_max_loss_usd = 0.0;
double                    g_max_tp_override_percent = 70.0;

PlannerLevel g_levels[];

double g_entry_price = 0.0;
int    g_consumed_levels = 0;
bool   g_custom_mode = false;
bool   g_symbol_specs_ok = false;
bool   g_plan_valid = false;
bool   g_generated_feasible = true;
int    g_level_page = 0;

double g_point_value_per_lot = 0.0;
double g_min_lot = 0.0;
double g_max_lot = 0.0;
double g_lot_step = 0.0;
int    g_digits = 5;

double g_current_tp_price = 0.0;
double g_current_sl_price = 0.0;
double g_last_tp_usd = 0.0;
double g_last_sl_usd = 0.0;
double g_last_tp_points_from_entry = 0.0;
double g_last_sl_points_from_entry = 0.0;
string g_plan_warning = "";
string g_validation_warning = "";
string g_solver_status = "";
int    g_panel_x = PANEL_X;
int    g_panel_y = PANEL_Y;
int    g_panel_w = PANEL_W;
int    g_panel_h = PANEL_H;
int    g_panel_scroll_x = 0;
int    g_panel_scroll_y = 0;
int    g_panel_content_w = PANEL_W - (PANEL_CONTENT_MARGIN * 2);
int    g_panel_content_h = PANEL_H - PANEL_CONTENT_TOP - PANEL_CONTENT_MARGIN;
bool   g_panel_minimized = false;
int    g_panel_restore_w = PANEL_W;
int    g_panel_restore_h = PANEL_H;

string Obj(const string id)
{
    return g_prefix + id;
}

bool StartsWith(const string value, const string prefix)
{
    return (StringLen(value) >= StringLen(prefix) && StringSubstr(value, 0, StringLen(prefix)) == prefix);
}

void AppendLine(string &dest, const string line)
{
    if(dest == "")
        dest = line;
    else
        dest += "\n" + line;
}

string DirectionName()
{
    return (g_direction == PlannerDirection_Long ? "Long" : "Short");
}

string LevelCountModeName()
{
    return (g_level_count_mode == PlannerLevelCount_AutoFillToFinalSL ? "AutoFillToFinalSL" : "UserMaxLevels");
}

string LotModeName()
{
    return (g_lot_mode == PlannerLotMode_AutoMaintainMinTp ? "AutoMaintainMinTp" : "FixedScaleInLots");
}

string LevelCountModeButtonText()
{
    return (g_level_count_mode == PlannerLevelCount_AutoFillToFinalSL ? "Auto Fill" : "User Max");
}

string LotModeButtonText()
{
    return (g_lot_mode == PlannerLotMode_AutoMaintainMinTp ? "Auto Min TP" : "Fixed Lots");
}

string MaxLossModeName()
{
    return (g_max_loss_mode == PlannerMaxLossMode_OverrideMinFinalTp ? "OverrideMinFinalTP" : "ShowWarning");
}

string MaxLossModeButtonText()
{
    return (g_max_loss_mode == PlannerMaxLossMode_OverrideMinFinalTp ? "Override TP" : "Warn Only");
}

int DirectionSign()
{
    return (g_direction == PlannerDirection_Long ? 1 : -1);
}

double AnchorPrice()
{
    double price = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    if(price <= 0.0)
        price = SymbolInfoDouble(_Symbol, SYMBOL_LAST);
    if(price <= 0.0)
        price = iClose(_Symbol, PERIOD_CURRENT, 0);
    return price;
}

bool RefreshSymbolSpecs()
{
    g_symbol_specs_ok = false;
    g_point_value_per_lot = 0.0;
    g_min_lot = 0.0;
    g_max_lot = 0.0;
    g_lot_step = 0.0;
    g_digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);

    double tick_value = 0.0;
    double tick_size = 0.0;
    double point_size = 0.0;
    SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE, tick_value);
    SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE, tick_size);
    SymbolInfoDouble(_Symbol, SYMBOL_POINT, point_size);
    SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, g_min_lot);
    SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MAX, g_max_lot);
    SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP, g_lot_step);

    if(g_lot_step <= 0.0)
        g_lot_step = 0.01;
    if(g_min_lot < 0.0)
        g_min_lot = 0.0;
    if(g_max_lot <= 0.0)
        g_max_lot = 1000.0;

    if(tick_value <= 0.0 || tick_size <= 0.0 || point_size <= 0.0)
        return false;

    g_point_value_per_lot = tick_value * (point_size / tick_size);
    if(g_point_value_per_lot <= 0.0)
        return false;

    g_symbol_specs_ok = true;
    return true;
}

double NormalizeLotNearest(double volume)
{
    if(volume <= 0.0)
        return 0.0;

    double steps = MathRound(volume / g_lot_step);
    double normalized = NormalizeDouble(steps * g_lot_step, 8);
    if(normalized < g_min_lot)
        normalized = g_min_lot;
    if(normalized > g_max_lot)
        normalized = g_max_lot;
    return NormalizeDouble(normalized, 8);
}

double NormalizeLotUp(double volume)
{
    if(volume <= 0.0)
        return 0.0;

    double steps = MathCeil((volume / g_lot_step) - 1e-10);
    double normalized = NormalizeDouble(steps * g_lot_step, 8);
    if(normalized < g_min_lot)
        normalized = g_min_lot;
    if(normalized > g_max_lot)
        normalized = g_max_lot;
    return NormalizeDouble(normalized, 8);
}

double NormalizeLotDown(double volume)
{
    if(volume <= 0.0)
        return 0.0;

    double steps = MathFloor((volume / g_lot_step) + 1e-10);
    double normalized = NormalizeDouble(steps * g_lot_step, 8);
    if(normalized < g_min_lot)
        return 0.0;
    if(normalized > g_max_lot)
        normalized = g_max_lot;
    return NormalizeDouble(normalized, 8);
}

double NormalizeManualLevelLot(double volume)
{
    if(volume <= 0.0)
        return 0.0;

    double steps = MathRound(volume / g_lot_step);
    double normalized = NormalizeDouble(steps * g_lot_step, 8);
    if(normalized < g_min_lot)
        normalized = g_min_lot;
    if(normalized > g_max_lot)
        normalized = g_max_lot;
    return NormalizeDouble(normalized, 8);
}

string FormatPrice(const double price)
{
    return DoubleToString(price, g_digits);
}

string FormatUsd(const double value)
{
    if(value >= 0.0)
        return StringFormat("+$%.2f", value);
    return StringFormat("-$%.2f", MathAbs(value));
}

string FormatDrawdownUsd(const double value)
{
    if(value <= 0.0)
        return StringFormat("DD -$%.2f", MathAbs(value));
    return StringFormat("DD +$%.2f", value);
}

string FormatPoints(const double value)
{
    return DoubleToString(value, 1) + " pts";
}

bool MaxLossEnabled()
{
    return (g_max_loss_usd > 0.0);
}

double MaxLossCapUsd()
{
    return MathAbs(g_max_loss_usd);
}

double NormalizedTpOverridePercent()
{
    if(g_max_tp_override_percent < 0.0)
        return 0.0;
    if(g_max_tp_override_percent > 100.0)
        return 100.0;
    return g_max_tp_override_percent;
}

double BaseTradeTpUsd()
{
    double base_tp_price = g_entry_price + (DirectionSign() * g_final_tp_points * _Point);
    return BasketLegUsd(g_entry_price, g_initial_lot, base_tp_price);
}

double OverrideTpFloorUsd()
{
    double base_tp_usd = BaseTradeTpUsd();
    if(base_tp_usd <= 0.0)
        return 0.0;

    double remaining_fraction = 1.0 - (NormalizedTpOverridePercent() / 100.0);
    if(remaining_fraction < 0.0)
        remaining_fraction = 0.0;
    if(remaining_fraction > 1.0)
        remaining_fraction = 1.0;

    return base_tp_usd * remaining_fraction;
}

bool MaxLossOverridesMinTp()
{
    return (g_max_loss_mode == PlannerMaxLossMode_OverrideMinFinalTp);
}

double EffectiveSpacingMultiplier()
{
    if(g_spacing_multiplier < 1.0)
        return 1.0;
    return g_spacing_multiplier;
}

void CopyDoubleArray(const double &src[], double &dest[])
{
    int count = ArraySize(src);
    ArrayResize(dest, count);
    for(int i = 0; i < count; i++)
        dest[i] = src[i];
}

string TrimSpaces(const string value)
{
    int start = 0;
    int end = StringLen(value) - 1;
    while(start <= end)
    {
        ushort ch = (ushort)StringGetCharacter(value, start);
        if(ch != ' ' && ch != '\t')
            break;
        start++;
    }
    while(end >= start)
    {
        ushort ch = (ushort)StringGetCharacter(value, end);
        if(ch != ' ' && ch != '\t')
            break;
        end--;
    }
    if(end < start)
        return "";
    return StringSubstr(value, start, end - start + 1);
}

string WrapSingleLine(const string line, const int max_chars)
{
    string remaining = TrimSpaces(line);
    string wrapped = "";
    if(max_chars <= 0)
        return remaining;

    while(StringLen(remaining) > max_chars)
    {
        int split = max_chars;
        while(split > 0 && StringGetCharacter(remaining, split - 1) != ' ')
            split--;
        if(split <= 0)
            split = max_chars;

        string chunk = TrimSpaces(StringSubstr(remaining, 0, split));
        if(chunk != "")
            AppendLine(wrapped, chunk);
        remaining = TrimSpaces(StringSubstr(remaining, split));
    }

    if(remaining != "")
        AppendLine(wrapped, remaining);

    return wrapped;
}

string WrapText(const string text, const int max_chars)
{
    string lines[];
    string wrapped = "";
    const ushort newline = 10;
    int count = StringSplit(text, newline, lines);
    if(count <= 0)
        return WrapSingleLine(text, max_chars);

    for(int i = 0; i < count; i++)
    {
        string chunk = WrapSingleLine(lines[i], max_chars);
        if(chunk != "")
        {
            if(wrapped != "")
                wrapped += "\n";
            wrapped += chunk;
        }
    }
    return wrapped;
}

double PriceFromAdverseDistance(const double adverse_points)
{
    return g_entry_price - (DirectionSign() * adverse_points * _Point);
}

double FavorablePointsBetween(const double entry_price, const double exit_price)
{
    return (DirectionSign() * (exit_price - entry_price) / _Point);
}

double RawAdverseDistancePoints(const double price)
{
    return -FavorablePointsBetween(g_entry_price, price);
}

double CurrentDeepestConsumedDistance()
{
    if(g_consumed_levels <= 0 || g_consumed_levels > ArraySize(g_levels))
        return 0.0;
    return MathMax(0.0, g_levels[g_consumed_levels - 1].distance_points);
}

int CurrentMartingaleLevelIndex()
{
    if(g_consumed_levels <= 0 || g_consumed_levels > ArraySize(g_levels))
        return -1;
    return g_consumed_levels - 1;
}

double ComputeCurrentTpPrice()
{
    double deepest = CurrentDeepestConsumedDistance();
    return g_entry_price + (DirectionSign() * (g_final_tp_points - deepest) * _Point);
}

double ComputeCurrentSlPrice()
{
    return g_entry_price - (DirectionSign() * g_final_sl_points * _Point);
}

double BasketLegUsd(const double leg_entry_price, const double lot, const double exit_price)
{
    if(lot <= 0.0 || g_point_value_per_lot <= 0.0)
        return 0.0;

    double favorable_points = FavorablePointsBetween(leg_entry_price, exit_price);
    return favorable_points * g_point_value_per_lot * lot;
}

double BasketUsdAtPrice(const double exit_price, const int active_levels)
{
    double total = BasketLegUsd(g_entry_price, g_initial_lot, exit_price);
    int count = MathMin(active_levels, ArraySize(g_levels));
    for(int i = 0; i < count; i++)
        total += BasketLegUsd(g_levels[i].price, g_levels[i].lot, exit_price);
    return total;
}

double CurrentMartingaleDrawdownUsd()
{
    int idx = CurrentMartingaleLevelIndex();
    if(idx < 0)
        return 0.0;
    return BasketUsdAtPrice(g_levels[idx].price, g_consumed_levels);
}

string CurrentMartingaleDrawdownText()
{
    int idx = CurrentMartingaleLevelIndex();
    if(idx < 0)
        return "";
    return FormatDrawdownUsd(CurrentMartingaleDrawdownUsd());
}

double CurrentOpenLots()
{
    double total = g_initial_lot;
    int count = MathMin(g_consumed_levels, ArraySize(g_levels));
    for(int i = 0; i < count; i++)
        total += g_levels[i].lot;
    return NormalizeDouble(total, 8);
}

string CurrentOpenLotsText()
{
    return StringFormat("Open Lots: %.4f", CurrentOpenLots());
}

double GeneratedBasketUsdAtPrice(const double &distances[], const double &lots[], const int active_levels, const double exit_price)
{
    double total = BasketLegUsd(g_entry_price, g_initial_lot, exit_price);
    int count = MathMin(active_levels, MathMin(ArraySize(distances), ArraySize(lots)));
    for(int i = 0; i < count; i++)
        total += BasketLegUsd(PriceFromAdverseDistance(distances[i]), lots[i], exit_price);
    return total;
}

int MaxLevelsBeforeSl()
{
    if(g_spacing_points <= 0.0 || g_final_sl_points <= 0.0)
        return 0;

    int count = 0;
    double distance = 0.0;
    double gap = g_spacing_points;
    double multiplier = EffectiveSpacingMultiplier();
    double max_distance = g_final_sl_points - 1e-6;

    while(count < MAX_LEVELS_LIMIT)
    {
        distance += gap;
        if(distance >= max_distance)
            break;

        count++;
        gap *= multiplier;
        if(gap <= 0.0 || gap > g_final_sl_points * 1000.0)
            break;
    }

    return count;
}

double DistanceFactorSum(const int level_count)
{
    if(level_count <= 0)
        return 0.0;

    double sum = 0.0;
    double factor = 1.0;
    double multiplier = EffectiveSpacingMultiplier();
    for(int i = 0; i < level_count; i++)
    {
        sum += factor;
        if(i < level_count - 1)
            factor *= multiplier;
        if(factor > 1e100)
            return 1e100;
    }
    return sum;
}

double MaxBaseSpacingInsideMaxLossZone(const int level_count)
{
    if(level_count <= 0 || g_final_sl_points <= 0.0)
        return 0.0;

    double factor_sum = DistanceFactorSum(level_count);
    if(factor_sum <= 0.0)
        return 0.0;

    double available_points = g_final_sl_points * MAX_LOSS_LEVEL_ZONE_FRACTION;
    if(available_points <= 0.0)
        available_points = g_final_sl_points - 1.0;
    if(available_points <= 0.0)
        return 0.0;

    return available_points / factor_sum;
}

void BuildDistancesForBaseSpacing(const int level_count, const double base_spacing_points, double &distances[])
{
    ArrayResize(distances, level_count);
    double distance = 0.0;
    double gap = base_spacing_points;
    double multiplier = EffectiveSpacingMultiplier();
    for(int i = 0; i < level_count; i++)
    {
        distance += gap;
        distances[i] = distance;
        gap *= multiplier;
    }
}

void BuildDistances(const int level_count, double &distances[])
{
    BuildDistancesForBaseSpacing(level_count, g_spacing_points, distances);
}

bool BuildMaxLossCandidateDistances(const int level_count, double &distances[], double &out_base_spacing)
{
    out_base_spacing = g_spacing_points;
    if(level_count <= 0)
    {
        ArrayResize(distances, 0);
        return true;
    }

    double max_base_spacing = MaxBaseSpacingInsideMaxLossZone(level_count);
    if(max_base_spacing <= 0.0)
        return false;

    out_base_spacing = max_base_spacing;
    BuildDistancesForBaseSpacing(level_count, out_base_spacing, distances);
    return true;
}

bool AttemptSolveAutoLots(const int level_count,
                          const double &distances[],
                          double &out_lots[],
                          double &out_risk_abs,
                          double &out_last_stage_tp_usd,
                          string &out_note)
{
    ArrayResize(out_lots, level_count);
    for(int reset = 0; reset < level_count; reset++)
        out_lots[reset] = 0.0;

    out_risk_abs = 0.0;
    out_last_stage_tp_usd = BasketLegUsd(g_entry_price, g_initial_lot, ComputeCurrentTpPrice());
    out_note = "";

    if(g_point_value_per_lot <= 0.0 || g_final_tp_points <= 0.0)
    {
        out_note = "Invalid symbol point value or final TP distance.";
        return false;
    }

    bool feasible = true;
    double sl_price = ComputeCurrentSlPrice();
    bool max_loss_enabled = MaxLossEnabled();
    bool override_min_tp = (max_loss_enabled && MaxLossOverridesMinTp());
    double max_loss_cap = MaxLossCapUsd();
    double override_tp_floor_usd = (override_min_tp ? OverrideTpFloorUsd() : g_min_final_tp_usd);
    double base_risk_abs = MathAbs(GeneratedBasketUsdAtPrice(distances, out_lots, 0, sl_price));
    if(max_loss_enabled && base_risk_abs > max_loss_cap + 1e-6)
    {
        out_risk_abs = base_risk_abs;
        if(override_min_tp)
        {
            AppendLine(out_note, StringFormat("Initial hedge alone projects %s at final SL, above max loss -$%.2f.",
                                              FormatUsd(-base_risk_abs),
                                              max_loss_cap));
            return false;
        }
    }

    if(level_count <= 0)
    {
        double base_tp = GeneratedBasketUsdAtPrice(distances, out_lots, 0, g_entry_price + (DirectionSign() * g_final_tp_points * _Point));
        out_last_stage_tp_usd = base_tp;
        out_risk_abs = base_risk_abs;
        if(base_tp + 1e-6 < g_min_final_tp_usd)
        {
            AppendLine(out_note, StringFormat("Base trade only reaches %s at TP, below target %s.",
                                              FormatUsd(base_tp),
                                              FormatUsd(g_min_final_tp_usd)));
            if(!override_min_tp)
                feasible = false;
        }
        if(override_min_tp && base_tp + 1e-6 < override_tp_floor_usd)
        {
            AppendLine(out_note, StringFormat("Base trade TP %s is below override floor %s.",
                                              FormatUsd(base_tp),
                                              FormatUsd(override_tp_floor_usd)));
            feasible = false;
        }
        return feasible;
    }

    for(int i = 0; i < level_count; i++)
    {
        double deepest = distances[i];
        double stage_tp_price = g_entry_price + (DirectionSign() * (g_final_tp_points - deepest) * _Point);
        double existing_profit = GeneratedBasketUsdAtPrice(distances, out_lots, i, stage_tp_price);
        double new_leg_per_lot = BasketLegUsd(PriceFromAdverseDistance(distances[i]), 1.0, stage_tp_price);
        if(new_leg_per_lot <= 0.0)
        {
            feasible = false;
            AppendLine(out_note, StringFormat("Level %d has non-positive per-lot TP profit.", i + 1));
            out_lots[i] = 0.0;
            continue;
        }

        double required_lot = g_min_lot;
        if(existing_profit + 1e-6 < g_min_final_tp_usd)
            required_lot = (g_min_final_tp_usd - existing_profit) / new_leg_per_lot;
        if(required_lot < g_min_lot)
            required_lot = g_min_lot;

        bool lot_capped_by_max_loss = false;
        double max_lot_for_risk = g_max_lot;
        if(override_min_tp)
        {
            double existing_risk_abs = MathAbs(GeneratedBasketUsdAtPrice(distances, out_lots, i, sl_price));
            double new_leg_risk_per_lot = MathAbs(BasketLegUsd(PriceFromAdverseDistance(distances[i]), 1.0, sl_price));
            if(new_leg_risk_per_lot <= 0.0)
            {
                feasible = false;
                AppendLine(out_note, StringFormat("Level %d has non-positive per-lot final SL risk.", i + 1));
                out_lots[i] = 0.0;
                continue;
            }

            max_lot_for_risk = (max_loss_cap - existing_risk_abs) / new_leg_risk_per_lot;
            if(max_lot_for_risk < g_min_lot - 1e-8)
            {
                feasible = false;
                AppendLine(out_note, StringFormat("Level %d cannot fit the minimum tradable lot inside max loss -$%.2f.",
                                                  i + 1,
                                                  max_loss_cap));
                out_lots[i] = 0.0;
                continue;
            }

            if(required_lot > max_lot_for_risk)
            {
                required_lot = max_lot_for_risk;
                lot_capped_by_max_loss = true;
            }
        }

        double normalized_lot = NormalizeLotUp(required_lot);
        if(max_loss_enabled && override_min_tp && normalized_lot > max_lot_for_risk + 1e-8)
            normalized_lot = NormalizeLotDown(max_lot_for_risk);

        if(normalized_lot <= 0.0)
        {
            feasible = false;
            AppendLine(out_note, StringFormat("Level %d could not be normalized to a tradable lot.", i + 1));
            out_lots[i] = 0.0;
            continue;
        }
        if(normalized_lot > g_max_lot + 1e-8)
        {
            normalized_lot = g_max_lot;
            feasible = false;
            AppendLine(out_note, StringFormat("Level %d hit max lot %.4f.", i + 1, g_max_lot));
        }

        out_lots[i] = normalized_lot;

        double stage_profit = GeneratedBasketUsdAtPrice(distances, out_lots, i + 1, stage_tp_price);
        out_last_stage_tp_usd = stage_profit;
        if(override_min_tp && stage_profit + 1e-6 < override_tp_floor_usd)
        {
            feasible = false;
            AppendLine(out_note, StringFormat("Level %d TP resolves to %s below override floor %s.",
                                              i + 1,
                                              FormatUsd(stage_profit),
                                              FormatUsd(override_tp_floor_usd)));
        }
        if(stage_profit + 1e-6 < g_min_final_tp_usd)
        {
            if(!override_min_tp)
            {
                feasible = false;
            }
            AppendLine(out_note, StringFormat("Level %d TP resolves to %s below target %s.",
                                              i + 1,
                                              FormatUsd(stage_profit),
                                              FormatUsd(g_min_final_tp_usd)));
            if(lot_capped_by_max_loss)
                AppendLine(out_note, StringFormat("Level %d lot was reduced to keep projected final SL within -$%.2f.",
                                                  i + 1,
                                                  max_loss_cap));
        }
    }

    out_risk_abs = MathAbs(GeneratedBasketUsdAtPrice(distances, out_lots, level_count, sl_price));
    if(max_loss_enabled && out_risk_abs > max_loss_cap + 1e-6)
    {
        if(override_min_tp)
        {
            AppendLine(out_note, StringFormat("Projected final SL %s exceeds max loss -$%.2f.",
                                              FormatUsd(-out_risk_abs),
                                              max_loss_cap));
            feasible = false;
        }
    }
    return feasible;
}

void RefreshRuntimeOutputs()
{
    g_current_tp_price = ComputeCurrentTpPrice();
    g_current_sl_price = ComputeCurrentSlPrice();
    g_last_tp_usd = BasketUsdAtPrice(g_current_tp_price, g_consumed_levels);
    g_last_sl_usd = BasketUsdAtPrice(g_current_sl_price, g_consumed_levels);
    g_last_tp_points_from_entry = FavorablePointsBetween(g_entry_price, g_current_tp_price);
    g_last_sl_points_from_entry = FavorablePointsBetween(g_entry_price, g_current_sl_price);
}

void ValidateCurrentPlan()
{
    g_validation_warning = "";

    if(g_consumed_levels > ArraySize(g_levels))
        g_consumed_levels = ArraySize(g_levels);

    if(!g_symbol_specs_ok)
    {
        g_plan_valid = false;
        g_validation_warning = "Unable to derive current symbol specs.";
        RefreshRuntimeOutputs();
        return;
    }

    if(g_final_tp_points <= 0.0 || g_final_sl_points <= 0.0)
    {
        g_plan_valid = false;
        g_validation_warning = "Final TP and final SL points must be greater than zero.";
        RefreshRuntimeOutputs();
        return;
    }

    bool levels_ok = true;
    double prev_distance = 0.0;
    for(int i = 0; i < ArraySize(g_levels); i++)
    {
        if(g_custom_mode)
            g_levels[i].distance_points = RawAdverseDistancePoints(g_levels[i].price);
        else
            g_levels[i].price = PriceFromAdverseDistance(g_levels[i].distance_points);

        double distance = g_levels[i].distance_points;
        if(distance <= 0.0)
        {
            levels_ok = false;
            g_validation_warning = StringFormat("Level %d is not on the adverse side of entry.", i + 1);
            break;
        }
        if(distance >= g_final_sl_points - 1e-6)
        {
            levels_ok = false;
            g_validation_warning = StringFormat("Level %d sits at or beyond the final SL.", i + 1);
            break;
        }
        if(i > 0 && distance <= prev_distance + 1e-6)
        {
            levels_ok = false;
            g_validation_warning = "Levels must remain strictly ordered toward the adverse side.";
            break;
        }
        prev_distance = distance;
    }

    if(levels_ok && MaxLossEnabled())
    {
        double projected_full_sl_abs = MathAbs(BasketUsdAtPrice(ComputeCurrentSlPrice(), ArraySize(g_levels)));
        if(projected_full_sl_abs > MaxLossCapUsd() + 1e-6)
        {
            g_validation_warning = StringFormat("Projected full-ladder SL %s exceeds max loss -$%.2f.",
                                                FormatUsd(-projected_full_sl_abs),
                                                MaxLossCapUsd());
            if(MaxLossOverridesMinTp())
                levels_ok = false;
        }
    }

    if(g_custom_mode)
        g_generated_feasible = false;

    g_plan_valid = levels_ok && (g_custom_mode || g_generated_feasible);
    RefreshRuntimeOutputs();
}

void RebuildGeneratedPlanner()
{
    g_plan_warning = "";
    g_solver_status = "";
    g_generated_feasible = true;
    ArrayResize(g_levels, 0);
    g_consumed_levels = 0;
    g_level_page = 0;

    if(!RefreshSymbolSpecs())
    {
        g_generated_feasible = false;
        g_plan_warning = "Unable to derive current symbol point value / lot constraints.";
        ValidateCurrentPlan();
        return;
    }

    g_initial_lot = NormalizeLotNearest(g_initial_lot);
    if(g_initial_lot <= 0.0)
    {
        g_generated_feasible = false;
        g_plan_warning = "Initial hedge lot must be greater than zero.";
        ValidateCurrentPlan();
        return;
    }

    g_fixed_scale_in_lot = NormalizeLotNearest(g_fixed_scale_in_lot);
    if(g_fixed_scale_in_lot <= 0.0)
        g_fixed_scale_in_lot = g_min_lot;

    if(g_entry_price <= 0.0)
        g_entry_price = AnchorPrice();

    if(g_spacing_points <= 0.0)
    {
        g_generated_feasible = false;
        g_plan_warning = "Spacing points must be greater than zero.";
        ValidateCurrentPlan();
        return;
    }

    if(g_spacing_multiplier < 1.0)
    {
        g_spacing_multiplier = 1.0;
        AppendLine(g_plan_warning, "Spacing multiplier was floored at 1.00.");
    }

    if(g_max_loss_usd < 0.0)
    {
        g_max_loss_usd = MathAbs(g_max_loss_usd);
        AppendLine(g_plan_warning, "Max loss was converted to a positive USD amount.");
    }

    if(g_max_tp_override_percent < 0.0)
    {
        g_max_tp_override_percent = 0.0;
        AppendLine(g_plan_warning, "Max TP override was floored at 0%.");
    }
    if(g_max_tp_override_percent > 100.0)
    {
        g_max_tp_override_percent = 100.0;
        AppendLine(g_plan_warning, "Max TP override was capped at 100%.");
    }

    int max_before_sl = MaxLevelsBeforeSl();
    int chosen_count = 0;
    double distances[];
    double chosen_lots[];
    ArrayResize(distances, 0);
    ArrayResize(chosen_lots, 0);

    if(g_level_count_mode == PlannerLevelCount_AutoFillToFinalSL)
    {
        chosen_count = max_before_sl;
        if(chosen_count == MAX_LEVELS_LIMIT)
            AppendLine(g_plan_warning, StringFormat("Auto-fill was truncated at the internal %d-level limit.", MAX_LEVELS_LIMIT));
    }
    else
    {
        if(g_max_levels < 0)
            g_max_levels = 0;
        if(g_max_levels > MAX_LEVELS_LIMIT)
        {
            g_max_levels = MAX_LEVELS_LIMIT;
            AppendLine(g_plan_warning, StringFormat("Max levels was capped at %d.", MAX_LEVELS_LIMIT));
        }
        chosen_count = MathMin(g_max_levels, max_before_sl);
        if(g_max_levels > max_before_sl)
            AppendLine(g_plan_warning, StringFormat("Only %d levels fit before the final SL.", max_before_sl));
    }

    BuildDistances(chosen_count, distances);

    if(g_lot_mode == PlannerLotMode_AutoMaintainMinTp)
    {
        if(MaxLossEnabled() && MaxLossOverridesMinTp())
        {
            double best_risk = 1e100;
            double best_base_spacing = g_spacing_points;
            int best_count = -1;
            double best_distances[];
            double best_lots[];
            string best_note = "";

            for(int candidate = chosen_count; candidate >= 0; candidate--)
            {
                double candidate_distances[];
                double candidate_lots[];
                double candidate_base_spacing = g_spacing_points;
                double risk_abs = 0.0;
                double last_tp_usd = 0.0;
                string note = "";

                if(!BuildMaxLossCandidateDistances(candidate, candidate_distances, candidate_base_spacing))
                    continue;

                bool feasible = AttemptSolveAutoLots(candidate, candidate_distances, candidate_lots, risk_abs, last_tp_usd, note);
                if(feasible && risk_abs <= MaxLossCapUsd() + 1e-6)
                {
                    best_risk = risk_abs;
                    best_count = candidate;
                    best_base_spacing = candidate_base_spacing;
                    best_note = note;
                    CopyDoubleArray(candidate_distances, best_distances);
                    CopyDoubleArray(candidate_lots, best_lots);
                    break;
                }
            }

            if(best_count >= 0)
            {
                chosen_count = best_count;
                CopyDoubleArray(best_distances, distances);
                CopyDoubleArray(best_lots, chosen_lots);
                g_generated_feasible = true;
                if(chosen_count == 0)
                    g_solver_status = StringFormat("Max loss mode selected base trade only; projected final SL %s / cap -$%.2f.",
                                                   FormatUsd(-best_risk),
                                                   MaxLossCapUsd());
                else
                    g_solver_status = StringFormat("Max loss mode selected %d level(s); projected final SL %s / cap -$%.2f.",
                                                   chosen_count,
                                                   FormatUsd(-best_risk),
                                                   MaxLossCapUsd());
                if(best_base_spacing > g_spacing_points + 1e-6)
                    AppendLine(g_plan_warning, StringFormat("Max loss mode widened base spacing to %.1f pts.", best_base_spacing));
                if(best_note != "")
                    AppendLine(g_plan_warning, best_note);
            }
            else
            {
                double fallback_risk = 0.0;
                double fallback_tp = 0.0;
                string fallback_note = "";
                double fallback_spacing = g_spacing_points;
                BuildMaxLossCandidateDistances(chosen_count, distances, fallback_spacing);
                AttemptSolveAutoLots(chosen_count, distances, chosen_lots, fallback_risk, fallback_tp, fallback_note);
                g_generated_feasible = false;
                g_solver_status = StringFormat("No max-loss compliant auto ladder found up to %d level(s). Showing the %d-level attempt.",
                                               MathMax(1, chosen_count),
                                               chosen_count);
                if(fallback_spacing > g_spacing_points + 1e-6)
                    AppendLine(g_plan_warning, StringFormat("Fallback widened base spacing to %.1f pts.", fallback_spacing));
                if(fallback_note != "")
                    AppendLine(g_plan_warning, fallback_note);
            }
        }
        else if(g_level_count_mode == PlannerLevelCount_UserMaxLevels && chosen_count > 0)
        {
            double best_risk = 1e100;
            int best_count = -1;
            double best_lots[];
            string best_note = "";
            for(int candidate = 1; candidate <= chosen_count; candidate++)
            {
                double candidate_distances[];
                double candidate_lots[];
                double risk_abs = 0.0;
                double last_tp_usd = 0.0;
                string note = "";
                BuildDistances(candidate, candidate_distances);
                bool feasible = AttemptSolveAutoLots(candidate, candidate_distances, candidate_lots, risk_abs, last_tp_usd, note);
                if(feasible && (risk_abs < best_risk - 1e-6 || (MathAbs(risk_abs - best_risk) <= 1e-6 && candidate > best_count)))
                {
                    best_risk = risk_abs;
                    best_count = candidate;
                    best_note = note;
                    ArrayResize(best_lots, ArraySize(candidate_lots));
                    for(int i = 0; i < ArraySize(candidate_lots); i++)
                        best_lots[i] = candidate_lots[i];
                }
            }

            if(best_count >= 0)
            {
                chosen_count = best_count;
                BuildDistances(chosen_count, distances);
                ArrayResize(chosen_lots, ArraySize(best_lots));
                for(int i = 0; i < ArraySize(best_lots); i++)
                    chosen_lots[i] = best_lots[i];
                g_generated_feasible = true;
                g_solver_status = StringFormat("Auto search selected %d level(s) with projected final SL %s.",
                                               chosen_count,
                                               FormatUsd(-best_risk));
                if(best_note != "")
                    AppendLine(g_plan_warning, best_note);
            }
            else
            {
                double fallback_risk = 0.0;
                double fallback_tp = 0.0;
                string fallback_note = "";
                AttemptSolveAutoLots(chosen_count, distances, chosen_lots, fallback_risk, fallback_tp, fallback_note);
                g_generated_feasible = false;
                g_solver_status = StringFormat("No feasible auto ladder found up to %d level(s). Showing the %d-level attempt.",
                                               MathMax(1, chosen_count),
                                               chosen_count);
                if(fallback_note != "")
                    AppendLine(g_plan_warning, fallback_note);
            }
        }
        else
        {
            double risk_abs = 0.0;
            double last_tp_usd = 0.0;
            string note = "";
            g_generated_feasible = AttemptSolveAutoLots(chosen_count, distances, chosen_lots, risk_abs, last_tp_usd, note);
            if(chosen_count == 0)
                g_solver_status = "Auto mode has no levels available before the final SL.";
            else
                g_solver_status = StringFormat("Auto mode built %d level(s); projected final SL %s.",
                                               chosen_count,
                                               FormatUsd(-risk_abs));
            if(note != "")
                AppendLine(g_plan_warning, note);
        }
    }
    else
    {
        ArrayResize(chosen_lots, chosen_count);
        for(int i = 0; i < chosen_count; i++)
            chosen_lots[i] = g_fixed_scale_in_lot;

        g_generated_feasible = true;
        g_solver_status = StringFormat("Fixed lot mode built %d level(s) at %.4f lots each.",
                                       chosen_count,
                                       g_fixed_scale_in_lot);

        if(chosen_count == 0)
        {
            double base_tp = BasketLegUsd(g_entry_price, g_initial_lot, g_entry_price + (DirectionSign() * g_final_tp_points * _Point));
            if(base_tp + 1e-6 < g_min_final_tp_usd)
                AppendLine(g_plan_warning, StringFormat("Base trade only reaches %s at TP, below target %s.",
                                                        FormatUsd(base_tp),
                                                        FormatUsd(g_min_final_tp_usd)));
        }
        else
        {
            int first_below = -1;
            double first_below_value = 0.0;
            for(int i = 0; i < chosen_count; i++)
            {
                double stage_tp_price = g_entry_price + (DirectionSign() * (g_final_tp_points - distances[i]) * _Point);
                double stage_profit = GeneratedBasketUsdAtPrice(distances, chosen_lots, i + 1, stage_tp_price);
                if(stage_profit + 1e-6 < g_min_final_tp_usd)
                {
                    first_below = i + 1;
                    first_below_value = stage_profit;
                    break;
                }
            }
            if(first_below > 0)
                AppendLine(g_plan_warning, StringFormat("Fixed lot mode first falls below min TP at level %d: %s vs target %s.",
                                                        first_below,
                                                        FormatUsd(first_below_value),
                                                        FormatUsd(g_min_final_tp_usd)));
        }

        if(MaxLossEnabled())
        {
            double fixed_risk_abs = MathAbs(GeneratedBasketUsdAtPrice(distances, chosen_lots, chosen_count, ComputeCurrentSlPrice()));
            if(fixed_risk_abs > MaxLossCapUsd() + 1e-6)
            {
                AppendLine(g_plan_warning, StringFormat("Fixed lot mode projects %s at final SL, above max loss -$%.2f.",
                                                        FormatUsd(-fixed_risk_abs),
                                                        MaxLossCapUsd()));
                if(MaxLossOverridesMinTp())
                    g_generated_feasible = false;
            }
        }
    }

    ArrayResize(g_levels, chosen_count);
    for(int i = 0; i < chosen_count; i++)
    {
        g_levels[i].distance_points = distances[i];
        g_levels[i].price = PriceFromAdverseDistance(distances[i]);
        g_levels[i].lot = (i < ArraySize(chosen_lots) ? chosen_lots[i] : 0.0);
        g_levels[i].manual_price = false;
        g_levels[i].manual_lot = false;
    }

    ValidateCurrentPlan();
}

void ResetPlanner(const bool reseed_entry)
{
    g_custom_mode = false;
    if(reseed_entry || g_entry_price <= 0.0)
        g_entry_price = AnchorPrice();
    RebuildGeneratedPlanner();
}

void ApplyInputChangeAndRebuild()
{
    g_custom_mode = false;
    RebuildGeneratedPlanner();
}

void CycleDirection()
{
    g_direction = (g_direction == PlannerDirection_Long ? PlannerDirection_Short : PlannerDirection_Long);
    ApplyInputChangeAndRebuild();
}

void CycleLevelCountMode()
{
    g_level_count_mode = (g_level_count_mode == PlannerLevelCount_AutoFillToFinalSL
        ? PlannerLevelCount_UserMaxLevels
        : PlannerLevelCount_AutoFillToFinalSL);
    ApplyInputChangeAndRebuild();
}

void CycleLotMode()
{
    g_lot_mode = (g_lot_mode == PlannerLotMode_AutoMaintainMinTp
        ? PlannerLotMode_FixedScaleInLots
        : PlannerLotMode_AutoMaintainMinTp);
    ApplyInputChangeAndRebuild();
}

void CycleMaxLossMode()
{
    g_max_loss_mode = (g_max_loss_mode == PlannerMaxLossMode_OverrideMinFinalTp
        ? PlannerMaxLossMode_ShowWarning
        : PlannerMaxLossMode_OverrideMinFinalTp);
    ApplyInputChangeAndRebuild();
}

void AddTradeStep()
{
    if(!g_plan_valid)
        return;
    if(g_consumed_levels >= ArraySize(g_levels))
        return;
    g_consumed_levels++;
    RefreshRuntimeOutputs();
}

void RemoveTradeStep()
{
    if(g_consumed_levels <= 0)
        return;
    g_consumed_levels--;
    RefreshRuntimeOutputs();
}

int PageCount()
{
    int total = ArraySize(g_levels);
    if(total <= 0)
        return 1;
    int pages = total / LEVEL_ROWS_PER_PAGE;
    if((total % LEVEL_ROWS_PER_PAGE) != 0)
        pages++;
    if(pages < 1)
        pages = 1;
    return pages;
}

void EnsureValidPage()
{
    int pages = PageCount();
    if(g_level_page < 0)
        g_level_page = 0;
    if(g_level_page >= pages)
        g_level_page = pages - 1;
    if(g_level_page < 0)
        g_level_page = 0;
}

int PanelViewportW()
{
    return MathMax(1, g_panel_w - (PANEL_CONTENT_MARGIN * 2));
}

int PanelViewportH()
{
    return MathMax(1, g_panel_h - PANEL_CONTENT_TOP - PANEL_CONTENT_MARGIN);
}

int PanelContentOriginX()
{
    return g_panel_x + PANEL_CONTENT_MARGIN;
}

int PanelContentOriginY()
{
    return g_panel_y + PANEL_CONTENT_TOP;
}

int ContentScreenX(const int content_x)
{
    return PanelContentOriginX() + content_x - g_panel_scroll_x;
}

int ContentScreenY(const int content_y)
{
    return PanelContentOriginY() + content_y - g_panel_scroll_y;
}

void ClampPanelScroll()
{
    int max_scroll_x = MathMax(0, g_panel_content_w - PanelViewportW());
    int max_scroll_y = MathMax(0, g_panel_content_h - PanelViewportH());

    if(g_panel_scroll_x < 0)
        g_panel_scroll_x = 0;
    if(g_panel_scroll_y < 0)
        g_panel_scroll_y = 0;
    if(g_panel_scroll_x > max_scroll_x)
        g_panel_scroll_x = max_scroll_x;
    if(g_panel_scroll_y > max_scroll_y)
        g_panel_scroll_y = max_scroll_y;
}

void ClampPanelToChart()
{
    long chart_width_long = 0;
    long chart_height_long = 0;
    if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, chart_width_long))
        return;
    if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, chart_height_long))
        return;

    int chart_width = (int)chart_width_long;
    int chart_height = (int)chart_height_long;

    int max_w = MathMax(120, chart_width - 20);
    int max_h = MathMax(120, chart_height - 20);
    int min_w = 0;
    int min_h = 0;
    if(g_panel_minimized)
    {
        min_w = MathMin(PANEL_MINIMIZED_W, max_w);
        min_h = MathMin(PANEL_MINIMIZED_H, max_h);
        max_h = min_h;
    }
    else
    {
        min_w = MathMin(PANEL_MIN_W, max_w);
        min_h = MathMin(PANEL_MIN_H, max_h);
    }

    if(g_panel_w < min_w)
        g_panel_w = min_w;
    if(g_panel_h < min_h)
        g_panel_h = min_h;
    if(g_panel_w > max_w)
        g_panel_w = max_w;
    if(g_panel_h > max_h)
        g_panel_h = max_h;

    if(g_panel_x < 0)
        g_panel_x = 0;
    if(g_panel_y < 0)
        g_panel_y = 0;
    if(g_panel_x + g_panel_w > chart_width - 4)
        g_panel_x = MathMax(0, chart_width - g_panel_w - 4);
    if(g_panel_y + g_panel_h > chart_height - 4)
        g_panel_y = MathMax(0, chart_height - g_panel_h - 4);
}

bool ContentPointVisible(const int content_x, const int content_y)
{
    int sx = ContentScreenX(content_x);
    int sy = ContentScreenY(content_y);
    return (sx >= PanelContentOriginX() &&
            sx <= PanelContentOriginX() + PanelViewportW() - 2 &&
            sy >= PanelContentOriginY() &&
            sy <= PanelContentOriginY() + PanelViewportH() - 2);
}

bool ContentRectFullyVisible(const int content_x, const int content_y, const int w, const int h)
{
    int sx = ContentScreenX(content_x);
    int sy = ContentScreenY(content_y);
    return (sx >= PanelContentOriginX() &&
            sy >= PanelContentOriginY() &&
            sx + w <= PanelContentOriginX() + PanelViewportW() &&
            sy + h <= PanelContentOriginY() + PanelViewportH());
}

bool ClipContentRect(const int content_x,
                     const int content_y,
                     const int w,
                     const int h,
                     int &out_x,
                     int &out_y,
                     int &out_w,
                     int &out_h)
{
    int sx = ContentScreenX(content_x);
    int sy = ContentScreenY(content_y);
    int left = PanelContentOriginX();
    int top = PanelContentOriginY();
    int right = left + PanelViewportW();
    int bottom = top + PanelViewportH();

    int clipped_left = MathMax(sx, left);
    int clipped_top = MathMax(sy, top);
    int clipped_right = MathMin(sx + w, right);
    int clipped_bottom = MathMin(sy + h, bottom);
    if(clipped_right <= clipped_left || clipped_bottom <= clipped_top)
        return false;

    out_x = clipped_left;
    out_y = clipped_top;
    out_w = clipped_right - clipped_left;
    out_h = clipped_bottom - clipped_top;
    return true;
}

void CreateOrUpdateScrolledPanelBackground(const string name,
                                           const int content_x,
                                           const int content_y,
                                           const int w,
                                           const int h,
                                           const color bg,
                                           const color border)
{
    int sx = 0;
    int sy = 0;
    int sw = 0;
    int sh = 0;
    if(!ClipContentRect(content_x, content_y, w, h, sx, sy, sw, sh))
        return;
    CreateOrUpdatePanelBackground(name, sx, sy, sw, sh, bg, border);
}

void CreateOrUpdateScrolledLabel(const string name,
                                 const int content_x,
                                 const int content_y,
                                 const string text,
                                 const color clr,
                                 const int font_size)
{
    if(!ContentPointVisible(content_x, content_y))
        return;
    CreateOrUpdateLabel(name, ContentScreenX(content_x), ContentScreenY(content_y), text, clr, font_size);
}

void CreateOrUpdateScrolledBoldLabel(const string name,
                                     const int content_x,
                                     const int content_y,
                                     const string text,
                                     const color clr,
                                     const int font_size)
{
    if(!ContentPointVisible(content_x, content_y))
        return;
    CreateOrUpdateBoldLabel(name, ContentScreenX(content_x), ContentScreenY(content_y), text, clr, font_size, 5);
}

void CreateOrUpdateScrolledMultilineLabelBlock(const string base_name,
                                               const int content_x,
                                               const int content_y,
                                               const string text,
                                               const color clr,
                                               const int font_size,
                                               const int line_gap,
                                               const int max_chars)
{
    string wrapped = WrapText(text, max_chars);
    string lines[];
    const ushort newline = 10;
    int count = StringSplit(wrapped, newline, lines);
    if(count <= 0)
    {
        CreateOrUpdateScrolledLabel(base_name, content_x, content_y, wrapped, clr, font_size);
        return;
    }

    for(int i = 0; i < count; i++)
    {
        int line_y = content_y + (i * line_gap);
        if(ContentPointVisible(content_x, line_y))
            CreateOrUpdateLabel(base_name + "_" + IntegerToString(i),
                                ContentScreenX(content_x),
                                ContentScreenY(line_y),
                                lines[i],
                                clr,
                                font_size);
    }
}

void CreateOrUpdateScrolledButton(const string name,
                                  const int content_x,
                                  const int content_y,
                                  const int w,
                                  const int h,
                                  const string text,
                                  const color bg,
                                  const color fg)
{
    if(!ContentRectFullyVisible(content_x, content_y, w, h))
        return;
    CreateOrUpdateButton(name, ContentScreenX(content_x), ContentScreenY(content_y), w, h, text, bg, fg);
}

void CreateOrUpdateScrolledEdit(const string name,
                                const int content_x,
                                const int content_y,
                                const int w,
                                const int h,
                                const string text)
{
    if(!ContentRectFullyVisible(content_x, content_y, w, h))
        return;
    CreateOrUpdateEdit(name, ContentScreenX(content_x), ContentScreenY(content_y), w, h, text);
}

void CreateOrUpdateResizeHandle(const string name, const int x, const int y)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_XSIZE, PANEL_RESIZE_HANDLE);
    ObjectSetInteger(0, name, OBJPROP_YSIZE, PANEL_RESIZE_HANDLE);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, (color)C'110,120,136');
    ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, (color)C'214,220,230');
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 10);
}

void ShiftPanelScroll(const int dx, const int dy)
{
    g_panel_scroll_x += dx;
    g_panel_scroll_y += dy;
    ClampPanelScroll();
}

void ResizePanelByStep(const int direction)
{
    if(g_panel_minimized)
        return;

    g_panel_w += (direction * PANEL_RESIZE_STEP_W);
    g_panel_h += (direction * PANEL_RESIZE_STEP_H);
    ClampPanelToChart();
    g_panel_restore_w = g_panel_w;
    g_panel_restore_h = g_panel_h;
    ClampPanelScroll();
}

void TogglePanelMinimized()
{
    if(!g_panel_minimized)
    {
        g_panel_restore_w = g_panel_w;
        g_panel_restore_h = g_panel_h;
        g_panel_w = PANEL_MINIMIZED_W;
        g_panel_h = PANEL_MINIMIZED_H;
        g_panel_minimized = true;
    }
    else
    {
        g_panel_w = g_panel_restore_w;
        g_panel_h = g_panel_restore_h;
        g_panel_minimized = false;
    }

    ClampPanelToChart();
    ClampPanelScroll();
}

void HandlePanelResizeDrag(const string name)
{
    if(g_panel_minimized)
        return;

    int dragged_x = (int)ObjectGetInteger(0, name, OBJPROP_XDISTANCE);
    int dragged_y = (int)ObjectGetInteger(0, name, OBJPROP_YDISTANCE);
    int right = g_panel_x + g_panel_w;
    int bottom = g_panel_y + g_panel_h;

    if(name == Obj("ResizeTL"))
    {
        g_panel_x = dragged_x;
        g_panel_y = dragged_y;
        g_panel_w = right - g_panel_x;
        g_panel_h = bottom - g_panel_y;
    }
    else if(name == Obj("ResizeTR"))
    {
        g_panel_y = dragged_y;
        g_panel_w = (dragged_x + PANEL_RESIZE_HANDLE) - g_panel_x;
        g_panel_h = bottom - g_panel_y;
    }
    else if(name == Obj("ResizeBL"))
    {
        g_panel_x = dragged_x;
        g_panel_w = right - g_panel_x;
        g_panel_h = (dragged_y + PANEL_RESIZE_HANDLE) - g_panel_y;
    }
    else if(name == Obj("ResizeBR"))
    {
        g_panel_w = (dragged_x + PANEL_RESIZE_HANDLE) - g_panel_x;
        g_panel_h = (dragged_y + PANEL_RESIZE_HANDLE) - g_panel_y;
    }

    ClampPanelToChart();
    g_panel_restore_w = g_panel_w;
    g_panel_restore_h = g_panel_h;
    ClampPanelScroll();
}

void DeletePlannerObjectsByPrefix(const string prefix)
{
    if(prefix == "")
        return;

    ObjectsDeleteAll(0, prefix, -1, -1);

    // Fallback for any objects that were left behind by older builds or a failed queued delete.
    for(int pass = 0; pass < 3; pass++)
    {
        bool deleted = false;
        for(int i = ObjectsTotal(0, -1, -1) - 1; i >= 0; i--)
        {
            string name = ObjectName(0, i, -1, -1);
            if(StartsWith(name, prefix))
            {
                ObjectSetInteger(0, name, OBJPROP_HIDDEN, false);
                ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
                ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
                ObjectDelete(0, name);
                deleted = true;
            }
        }

        if(!deleted)
            break;
    }
}

void DeletePlannerObjects()
{
    if(g_prefix != "")
        DeletePlannerObjectsByPrefix(g_prefix);

    DeletePlannerObjectsByPrefix(PLANNER_OBJECT_PREFIX_ROOT);
}

void CreateOrUpdatePanelBackground(const string name, const int x, const int y, const int w, const int h, const color bg, const color border)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_RECTANGLE_LABEL, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_XSIZE, w);
    ObjectSetInteger(0, name, OBJPROP_YSIZE, h);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, bg);
    ObjectSetInteger(0, name, OBJPROP_COLOR, border);
    ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, border);
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 1);
}

void CreateOrUpdateLabel(const string name, const int x, const int y, const string text, const color clr, const int font_size)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, name, OBJPROP_FONT, PANEL_FONT);
    ObjectSetInteger(0, name, OBJPROP_FONTSIZE, font_size);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 2);
}

void CreateOrUpdateBoldLabel(const string name,
                             const int x,
                             const int y,
                             const string text,
                             const color clr,
                             const int font_size,
                             const int zorder)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_LABEL, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, name, OBJPROP_FONT, PANEL_FONT_BOLD);
    ObjectSetInteger(0, name, OBJPROP_FONTSIZE, font_size);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
}

void CreateOrUpdateMultilineLabelBlock(const string base_name,
                                       const int x,
                                       const int y,
                                       const string text,
                                       const color clr,
                                       const int font_size,
                                       const int line_gap,
                                       const int max_chars)
{
    string wrapped = WrapText(text, max_chars);
    string lines[];
    const ushort newline = 10;
    int count = StringSplit(wrapped, newline, lines);

    if(count <= 0)
    {
        CreateOrUpdateLabel(base_name, x, y, wrapped, clr, font_size);
        return;
    }

    for(int i = 0; i < count; i++)
    {
        CreateOrUpdateLabel(base_name + "_" + IntegerToString(i), x, y + (i * line_gap), lines[i], clr, font_size);
    }
}

void CreateOrUpdateButton(const string name, const int x, const int y, const int w, const int h, const string text, const color bg, const color fg)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_BUTTON, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_XSIZE, w);
    ObjectSetInteger(0, name, OBJPROP_YSIZE, h);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, bg);
    ObjectSetInteger(0, name, OBJPROP_COLOR, fg);
    ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, clrBlack);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, name, OBJPROP_FONT, PANEL_FONT);
    ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 9);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 3);
}

void CreateOrUpdateEdit(const string name, const int x, const int y, const int w, const int h, const string text)
{
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_EDIT, 0, 0, 0);

    ObjectSetInteger(0, name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, name, OBJPROP_XDISTANCE, x);
    ObjectSetInteger(0, name, OBJPROP_YDISTANCE, y);
    ObjectSetInteger(0, name, OBJPROP_XSIZE, w);
    ObjectSetInteger(0, name, OBJPROP_YSIZE, h);
    ObjectSetInteger(0, name, OBJPROP_BGCOLOR, (color)C'246,248,250');
    ObjectSetInteger(0, name, OBJPROP_COLOR, clrBlack);
    ObjectSetInteger(0, name, OBJPROP_BORDER_COLOR, (color)C'76,84,94');
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, name, OBJPROP_FONT, PANEL_FONT);
    ObjectSetInteger(0, name, OBJPROP_FONTSIZE, 10);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 4);
}

bool ResolveVisibleLineSpan(const double price, datetime &out_left_time, datetime &out_right_time)
{
    int price_x = 0;
    int price_y = 0;
    if(!ResolvePriceLabelScreenPoint(price, price_x, price_y))
        return false;

    long chart_width_long = 0;
    long chart_height_long = 0;
    if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, chart_width_long))
        return false;
    if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, chart_height_long))
        return false;

    int chart_width = (int)chart_width_long;
    int chart_height = (int)chart_height_long;
    int left_x = g_panel_x + g_panel_w + 14;
    int right_x = chart_width - 12;
    if(left_x > right_x - 24)
        left_x = MathMax(10, right_x - 24);
    if(price_y < 8)
        price_y = 8;
    if(price_y > chart_height - 8)
        price_y = chart_height - 8;

    int sub_window = 0;
    double left_price = 0.0;
    double right_price = 0.0;
    datetime left_time = 0;
    datetime right_time = 0;
    if(!ChartXYToTimePrice(0, left_x, price_y, sub_window, left_time, left_price))
        return false;
    if(!ChartXYToTimePrice(0, right_x, price_y, sub_window, right_time, right_price))
        return false;

    out_left_time = left_time;
    out_right_time = right_time;
    return true;
}

void CreateOrUpdateHLine(const string name, const double price, const color clr, const ENUM_LINE_STYLE style, const int width)
{
    datetime left_time = 0;
    datetime right_time = 0;
    if(!ResolveVisibleLineSpan(price, left_time, right_time))
        return;

    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_TREND, 0, left_time, price, right_time, price);

    ObjectMove(0, name, 0, left_time, price);
    ObjectMove(0, name, 1, right_time, price);
    ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_STYLE, style);
    ObjectSetInteger(0, name, OBJPROP_WIDTH, width);
    ObjectSetInteger(0, name, OBJPROP_RAY_LEFT, false);
    ObjectSetInteger(0, name, OBJPROP_RAY_RIGHT, false);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
}

bool ResolvePriceLabelScreenPoint(const double price, int &out_x, int &out_y)
{
    long chart_width_long = 0;
    long chart_height_long = 0;
    if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, chart_width_long))
        return false;
    if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, chart_height_long))
        return false;

    int chart_width = (int)chart_width_long;
    int chart_height = (int)chart_height_long;
    int sub_window = 0;
    datetime visible_time = 0;
    double sample_price = 0.0;
    if(!ChartXYToTimePrice(0, chart_width - 12, chart_height / 2, sub_window, visible_time, sample_price))
    {
        int tf_seconds = PeriodSeconds((ENUM_TIMEFRAMES)_Period);
        if(tf_seconds <= 0)
            tf_seconds = 60;
        datetime bar_open = iTime(_Symbol, PERIOD_CURRENT, 0);
        if(bar_open <= 0)
            bar_open = TimeCurrent();
        visible_time = bar_open + tf_seconds;
    }

    return ChartTimePriceToXY(0, 0, visible_time, price, out_x, out_y);
}

bool ResolveDraggedLabelPrice(const string object_name, const int popup_h, double &out_price)
{
    long chart_width_long = 0;
    long chart_height_long = 0;
    if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, chart_width_long))
        return false;
    if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, chart_height_long))
        return false;

    int chart_width = (int)chart_width_long;
    int chart_height = (int)chart_height_long;
    int drag_x = (int)ObjectGetInteger(0, object_name, OBJPROP_XDISTANCE) + (CALLOUT_TEXT_PAD_X + 4);
    int drag_y = (int)ObjectGetInteger(0, object_name, OBJPROP_YDISTANCE) + (popup_h / 2);
    if(drag_x < 8)
        drag_x = 8;
    if(drag_x > chart_width - 8)
        drag_x = chart_width - 8;
    if(drag_y < 8)
        drag_y = 8;
    if(drag_y > chart_height - 8)
        drag_y = chart_height - 8;

    int sub_window = 0;
    datetime when = 0;
    double price = 0.0;
    if(!ChartXYToTimePrice(0, drag_x, drag_y, sub_window, when, price))
        return false;

    out_price = price;
    return true;
}

bool ResolvePriceTextLayout(const double price,
                            const int popup_w,
                            const int popup_h,
                            const int lane_index,
                            const int gap_y,
                            int &out_x,
                            int &out_y)
{
    int price_x = 0;
    int price_y = 0;
    if(!ResolvePriceLabelScreenPoint(price, price_x, price_y))
        return false;

    long chart_width_long = 0;
    long chart_height_long = 0;
    if(!ChartGetInteger(0, CHART_WIDTH_IN_PIXELS, 0, chart_width_long))
        return false;
    if(!ChartGetInteger(0, CHART_HEIGHT_IN_PIXELS, 0, chart_height_long))
        return false;

    int chart_width = (int)chart_width_long;
    int chart_height = (int)chart_height_long;
    int min_x = g_panel_x + g_panel_w + 12;
    int max_x = chart_width - popup_w - 14;
    int desired_x = g_panel_x + g_panel_w + CALLOUT_BASE_X_GAP + (lane_index * CALLOUT_LANE_STEP);
    if(max_x >= min_x)
    {
        if(desired_x > max_x)
            desired_x = max_x;
        if(desired_x < min_x)
            desired_x = min_x;
    }
    else
        desired_x = max_x;
    if(desired_x < 10)
        desired_x = 10;

    int desired_y = price_y - popup_h - gap_y;
    if(desired_y < 8)
        desired_y = price_y + gap_y;
    if(desired_y > chart_height - popup_h - 8)
    {
        int retry_y = price_y - popup_h - gap_y;
        if(retry_y >= 8)
            desired_y = retry_y;
        else
            desired_y = chart_height - popup_h - 8;
    }

    out_x = desired_x;
    out_y = desired_y;
    return true;
}

void CreateOrUpdatePriceText(const string base_name,
                             const double price,
                             const string text,
                             const color text_clr,
                             const color bg_clr,
                             const int popup_w,
                             const int popup_h,
                             const int lane_index,
                             const int gap_y)
{
    int desired_x = 0;
    int desired_y = 0;
    if(!ResolvePriceTextLayout(price, popup_w, popup_h, lane_index, gap_y, desired_x, desired_y))
        return;

    string bg_name = base_name + "_Bg";
    string text_name = base_name + "_Text";

    if(ObjectFind(0, bg_name) < 0)
        ObjectCreate(0, bg_name, OBJ_RECTANGLE_LABEL, 0, 0, 0);
    ObjectSetInteger(0, bg_name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, bg_name, OBJPROP_XDISTANCE, desired_x);
    ObjectSetInteger(0, bg_name, OBJPROP_YDISTANCE, desired_y);
    ObjectSetInteger(0, bg_name, OBJPROP_XSIZE, popup_w);
    ObjectSetInteger(0, bg_name, OBJPROP_YSIZE, popup_h);
    ObjectSetInteger(0, bg_name, OBJPROP_BGCOLOR, bg_clr);
    ObjectSetInteger(0, bg_name, OBJPROP_COLOR, text_clr);
    ObjectSetInteger(0, bg_name, OBJPROP_BORDER_COLOR, text_clr);
    ObjectSetInteger(0, bg_name, OBJPROP_BACK, false);
    ObjectSetInteger(0, bg_name, OBJPROP_SELECTABLE, true);
    ObjectSetInteger(0, bg_name, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, bg_name, OBJPROP_HIDDEN, true);
    ObjectSetInteger(0, bg_name, OBJPROP_ZORDER, 8);

    if(ObjectFind(0, text_name) < 0)
        ObjectCreate(0, text_name, OBJ_LABEL, 0, 0, 0);
    ObjectSetInteger(0, text_name, OBJPROP_CORNER, CORNER_LEFT_UPPER);
    ObjectSetInteger(0, text_name, OBJPROP_XDISTANCE, desired_x + CALLOUT_TEXT_PAD_X);
    ObjectSetInteger(0, text_name, OBJPROP_YDISTANCE, desired_y + CALLOUT_TEXT_PAD_Y);
    ObjectSetInteger(0, text_name, OBJPROP_COLOR, text_clr);
    ObjectSetInteger(0, text_name, OBJPROP_BACK, false);
    ObjectSetInteger(0, text_name, OBJPROP_SELECTABLE, true);
    ObjectSetInteger(0, text_name, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, text_name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, text_name, OBJPROP_FONT, PANEL_FONT);
    ObjectSetInteger(0, text_name, OBJPROP_FONTSIZE, 9);
    ObjectSetString(0, text_name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, text_name, OBJPROP_ZORDER, 9);
}

void CreateOrUpdatePriceDrawdownText(const string name,
                                     const double price,
                                     const string text,
                                     const int popup_w,
                                     const int popup_h,
                                     const int lane_index,
                                     const int gap_y)
{
    if(text == "")
        return;

    int desired_x = 0;
    int desired_y = 0;
    if(!ResolvePriceTextLayout(price, popup_w, popup_h, lane_index, gap_y, desired_x, desired_y))
        return;

    CreateOrUpdateBoldLabel(name,
                            desired_x + CALLOUT_DRAWDOWN_X,
                            desired_y + CALLOUT_TEXT_PAD_Y,
                            text,
                            clrRed,
                            9,
                            11);
}

string SummaryText()
{
    string text = "";
    AppendLine(text, StringFormat("Symbol: %s", _Symbol));
    AppendLine(text, StringFormat("Direction: %s", DirectionName()));
    AppendLine(text, StringFormat("Modes: %s | %s",
                                  (g_level_count_mode == PlannerLevelCount_AutoFillToFinalSL ? "Auto Fill" : "User Max"),
                                  (g_lot_mode == PlannerLotMode_AutoMaintainMinTp ? "Auto Min TP" : "Fixed Lots")));
    if(MaxLossEnabled())
    {
        AppendLine(text, StringFormat("Max Loss: -$%.2f | %s", MaxLossCapUsd(), MaxLossModeButtonText()));
        if(MaxLossOverridesMinTp())
            AppendLine(text, StringFormat("TP Floor: %s", FormatUsd(OverrideTpFloorUsd())));
    }
    AppendLine(text, StringFormat("Entry: %s", FormatPrice(g_entry_price)));
    AppendLine(text, StringFormat("TP: %s | %s", FormatUsd(g_last_tp_usd), FormatPoints(g_last_tp_points_from_entry)));
    AppendLine(text, StringFormat("SL: %s | %s", FormatUsd(g_last_sl_usd), FormatPoints(g_last_sl_points_from_entry)));
    AppendLine(text, StringFormat("Filled: %d / %d", g_consumed_levels, ArraySize(g_levels)));
    if(g_custom_mode)
        AppendLine(text, "State: Custom");
    else if(g_plan_valid)
        AppendLine(text, "State: Generated");
    else
        AppendLine(text, "State: Invalid");
    return text;
}

string WarningText()
{
    string text = "";
    AppendLine(text, CurrentOpenLotsText());
    if(g_solver_status != "")
        AppendLine(text, g_solver_status);
    if(g_plan_warning != "")
        AppendLine(text, g_plan_warning);
    if(g_validation_warning != "")
        AppendLine(text, g_validation_warning);
    if(g_custom_mode)
        AppendLine(text, "Custom mode stays active until Reset or another input / mode change.");
    return text;
}

void RenderPanel()
{
    ClampPanelToChart();
    int header_pad_x = 12;
    int header_btn_gap = 4;
    int header_button_y = g_panel_y + ((PANEL_HEADER_H - PANEL_SCROLL_BUTTON) / 2) + 1;
    int size_down_x = g_panel_x + 8;
    int size_up_x = size_down_x + PANEL_SCROLL_BUTTON + header_btn_gap;
    int min_btn_x = g_panel_x + g_panel_w - header_pad_x - PANEL_SCROLL_BUTTON;
    int scroll_block_w = (PANEL_SCROLL_BUTTON * 4) + (header_btn_gap * 3);
    int scroll_x = min_btn_x - header_btn_gap - scroll_block_w;
    int scroll_y = header_button_y;
    int title_x = size_up_x + PANEL_SCROLL_BUTTON + 12;
    int header_text_right = scroll_x - 12;
    string panel_title = (((header_text_right - title_x) < 250) ? "EHM Planner" : "Elastic Hedge Martingale Planner");

    CreateOrUpdatePanelBackground(Obj("PanelBg"), g_panel_x, g_panel_y, g_panel_w, g_panel_h, (color)C'22,24,28', (color)C'188,194,204');
    CreateOrUpdatePanelBackground(Obj("HeaderBg"), g_panel_x + 1, g_panel_y + 1, g_panel_w - 2, PANEL_HEADER_H, (color)C'54,66,84', (color)C'188,194,204');
    if(!g_panel_minimized)
    {
        CreateOrUpdateButton(Obj("BtnSizeDown"), size_down_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "-", (color)C'68,72,80', clrWhite);
        CreateOrUpdateButton(Obj("BtnSizeUp"), size_up_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "+", (color)C'68,72,80', clrWhite);
    }
    CreateOrUpdateLabel(Obj("PanelTitle"), title_x, g_panel_y + 12, panel_title, clrWhite, 13);
    if(!g_panel_minimized && (header_text_right - title_x) >= 520)
        CreateOrUpdateLabel(Obj("PanelSubtitle"), header_text_right - 244, g_panel_y + 14, "Planner only. No live orders are placed.", clrGainsboro, 9);
    CreateOrUpdateButton(Obj("BtnMinimize"), min_btn_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, (g_panel_minimized ? "+" : "-"), (color)C'68,72,80', clrWhite);

    if(g_panel_minimized)
        return;

    int viewport_w = PanelViewportW();
    int viewport_h = PanelViewportH();
    int content_w = MathMax(260, viewport_w);
    int content_h = MathMax(220, viewport_h);
    bool compact = (content_w < 520);
    bool stacked = (content_w < 720);
    bool split_action_rows = (content_w < 620);
    int section_gap = (compact ? 10 : 14);
    int left_w = 0;
    int right_w = 0;
    if(stacked)
    {
        left_w = content_w;
        right_w = content_w;
    }
    else
    {
        left_w = MathMax(340, (content_w * 47) / 100);
        if(left_w > content_w - section_gap - 260)
            left_w = content_w - section_gap - 260;
        right_w = content_w - left_w - section_gap;
    }

    int row_y = 10;
    int row_h = (compact ? 28 : 30);
    int input_y = row_y + row_h * 3 + 18;
    int input_field_rows = 11;
    int input_h = input_y + (input_field_rows * row_h) + 18;
    int summary_h = (stacked ? (compact ? 190 : 168) : 214);
    int status_h = (compact ? 114 : 94);
    int action_h = (split_action_rows ? 104 : 72);

    int summary_x = (stacked ? 0 : left_w + section_gap);
    int summary_y = (stacked ? input_h + section_gap : 0);
    int status_x = summary_x;
    int status_y = summary_y + summary_h + section_gap;
    int action_y = (stacked ? status_y + status_h + section_gap : MathMax(input_h, status_y + status_h) + section_gap);
    int levels_y = action_y + action_h + section_gap;
    int levels_h = MathMax((compact ? 380 : 240), content_h - levels_y);

    g_panel_content_w = content_w;
    g_panel_content_h = MathMax(levels_y + levels_h, viewport_h);
    ClampPanelScroll();

    int label_x = 8;
    int input_value_w = MathMin((compact ? 132 : 156), MathMax((compact ? 108 : 124), left_w / 3));
    int value_x = left_w - input_value_w - 14;
    if(value_x < (compact ? 170 : 190))
        value_x = (compact ? 170 : 190);
    CreateOrUpdateButton(Obj("BtnScrollLeft"), scroll_x, scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "<", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollRight"), scroll_x + PANEL_SCROLL_BUTTON + header_btn_gap, scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, ">", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollUp"), scroll_x + ((PANEL_SCROLL_BUTTON + header_btn_gap) * 2), scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "^", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollDown"), scroll_x + ((PANEL_SCROLL_BUTTON + header_btn_gap) * 3), scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "v", (color)C'68,72,80', clrWhite);

    CreateOrUpdateScrolledPanelBackground(Obj("InputBg"), 0, 0, left_w, input_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("SummaryBg"), summary_x, summary_y, right_w, summary_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("StatusBg"), status_x, status_y, right_w, status_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("ActionBg"), 0, action_y, content_w, action_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("LevelsBg"), 0, levels_y, content_w, levels_h, (color)C'30,34,40', (color)C'112,122,136');

    CreateOrUpdateScrolledLabel(Obj("InputsTitle"), label_x, 6, "Inputs", clrWhite, 10);
    CreateOrUpdateScrolledLabel(Obj("SummaryTitle"), summary_x + 8, summary_y + 6, "Plan Summary", clrWhite, 10);
    CreateOrUpdateScrolledLabel(Obj("WarningTitle"), status_x + 8, status_y + 6, "Status", clrWhite, 10);
    CreateOrUpdateScrolledLabel(Obj("ActionsTitle"), label_x, action_y + 6, "Simulation", clrWhite, 10);

    CreateOrUpdateScrolledLabel(Obj("LblDir"), label_x, row_y + 20, "Direction", clrGainsboro, 10);
    CreateOrUpdateScrolledButton(Obj("BtnDir"), value_x, row_y + 14, input_value_w, 24, DirectionName(), (g_direction == PlannerDirection_Long ? clrSeaGreen : clrIndianRed), clrWhite);

    CreateOrUpdateScrolledLabel(Obj("LblCountMode"), label_x, row_y + row_h + 20, "Level Count Mode", clrGainsboro, 10);
    CreateOrUpdateScrolledButton(Obj("BtnCountMode"), value_x, row_y + row_h + 14, input_value_w, 24, LevelCountModeButtonText(), clrSteelBlue, clrWhite);

    CreateOrUpdateScrolledLabel(Obj("LblLotMode"), label_x, row_y + row_h * 2 + 20, "Lot Mode", clrGainsboro, 10);
    CreateOrUpdateScrolledButton(Obj("BtnLotMode"), value_x, row_y + row_h * 2 + 14, input_value_w, 24, LotModeButtonText(), clrDarkOrange, clrWhite);

    CreateOrUpdateScrolledMultilineLabelBlock(Obj("SummaryLabel"), summary_x + 8, summary_y + 34, SummaryText(), (g_plan_valid ? clrLightGreen : clrKhaki), 10, 18, MathMax(28, right_w / 10));
    CreateOrUpdateScrolledMultilineLabelBlock(Obj("WarningLabel"), status_x + 8, status_y + 34, WarningText(), (g_plan_valid ? clrLightGray : clrOrange), 9, 15, MathMax(30, right_w / 9));

    CreateOrUpdateScrolledLabel(Obj("LblTp"), label_x, input_y + 6, "Final TP (pts)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditTp"), value_x, input_y, input_value_w, 24, DoubleToString(g_final_tp_points, 1));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblSl"), label_x, input_y + 6, "Final SL (pts)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditSl"), value_x, input_y, input_value_w, 24, DoubleToString(g_final_sl_points, 1));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblMinTpUsd"), label_x, input_y + 6, "Min Final TP ($)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditMinTpUsd"), value_x, input_y, input_value_w, 24, DoubleToString(g_min_final_tp_usd, 2));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblInitLot"), label_x, input_y + 6, "Initial Hedge Lot", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditInitLot"), value_x, input_y, input_value_w, 24, DoubleToString(g_initial_lot, 4));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblSpacing"), label_x, input_y + 6, "Spacing (pts)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditSpacing"), value_x, input_y, input_value_w, 24, DoubleToString(g_spacing_points, 1));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblSpacingMultiplier"), label_x, input_y + 6, "Space Multiplier", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditSpacingMultiplier"), value_x, input_y, input_value_w, 24, DoubleToString(g_spacing_multiplier, 2));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblMaxLevels"), label_x, input_y + 6, "Max Levels", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditMaxLevels"), value_x, input_y, input_value_w, 24, IntegerToString(g_max_levels));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblFixedLot"), label_x, input_y + 6, "Fixed Scale-In Lot", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditFixedLot"), value_x, input_y, input_value_w, 24, DoubleToString(g_fixed_scale_in_lot, 4));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblMaxLossUsd"), label_x, input_y + 6, "Max Loss ($)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditMaxLossUsd"), value_x, input_y, input_value_w, 24, DoubleToString(g_max_loss_usd, 2));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblMaxTpOverride"), label_x, input_y + 6, "Max TP Override (%)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditMaxTpOverride"), value_x, input_y, input_value_w, 24, DoubleToString(g_max_tp_override_percent, 1));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblMaxLossMode"), label_x, input_y + 6, "Max Loss Mode", clrGainsboro, 10);
    CreateOrUpdateScrolledButton(Obj("BtnMaxLossMode"), value_x, input_y, input_value_w, 24, MaxLossModeButtonText(), clrDimGray, clrWhite);

    int action_gap = (compact ? 8 : 12);
    int action_x = 8;
    int action_row_y = action_y + 28;
    if(split_action_rows)
    {
        int row1_btn_w = (content_w - (action_x * 2) - (action_gap * 2)) / 3;
        int row2_btn_w = (content_w - (action_x * 2) - action_gap) / 2;
        int row2_y = action_row_y + 34;
        CreateOrUpdateScrolledButton(Obj("BtnAddTrade"), action_x, action_row_y, row1_btn_w, 26, "Add Trade", clrSeaGreen, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnRemoveTrade"), action_x + row1_btn_w + action_gap, action_row_y, row1_btn_w, 26, "Remove Trade", clrIndianRed, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnReset"), action_x + ((row1_btn_w + action_gap) * 2), action_row_y, row1_btn_w, 26, "Reset", clrMediumPurple, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnAddLevel"), action_x, row2_y, row2_btn_w, 26, "Add Level", clrRoyalBlue, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnRemoveLevel"), action_x + row2_btn_w + action_gap, row2_y, row2_btn_w, 26, "Remove Level", clrFireBrick, clrWhite);
    }
    else
    {
        int action_btn_w = 134;
        CreateOrUpdateScrolledButton(Obj("BtnAddTrade"), action_x, action_row_y, action_btn_w, 26, "Add Trade", clrSeaGreen, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnRemoveTrade"), action_x + action_btn_w + action_gap, action_row_y, 142, 26, "Remove Trade", clrIndianRed, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnReset"), action_x + 288, action_row_y, 104, 26, "Reset", clrMediumPurple, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnAddLevel"), action_x + 404, action_row_y, 126, 26, "Add Level", clrRoyalBlue, clrWhite);
        CreateOrUpdateScrolledButton(Obj("BtnRemoveLevel"), action_x + 540, action_row_y, 138, 26, "Remove Level", clrFireBrick, clrWhite);
    }

    EnsureValidPage();
    int level_header_y = levels_y + 8;
    CreateOrUpdateScrolledLabel(Obj("LevelHeader"), label_x, level_header_y, "Levels", clrWhite, 10);
    int pager_y = level_header_y - 2;
    int pager_x = content_w - 170;
    if(pager_x < 160)
        pager_x = 160;
    CreateOrUpdateScrolledButton(Obj("BtnPrevPage"), pager_x, pager_y, 42, 22, "<", clrDimGray, clrWhite);
    CreateOrUpdateScrolledButton(Obj("BtnNextPage"), pager_x + 48, pager_y, 42, 22, ">", clrDimGray, clrWhite);
    CreateOrUpdateScrolledLabel(Obj("LblPage"), pager_x + 96, level_header_y + 2, StringFormat("Page %d / %d", g_level_page + 1, PageCount()), clrGainsboro, 10);

    int level_top = 0;
    int level_value_w = 0;
    int level_value_x = 0;
    int level_row_h = 26;
    if(compact)
    {
        level_value_w = MathMin(148, content_w - 16);
        level_value_x = label_x;
        CreateOrUpdateScrolledLabel(Obj("LevelSubHeader"), label_x, level_header_y + 24, "Scale-In Lot", clrWhite, 10);
        level_top = levels_y + 56;
        level_row_h = 50;
    }
    else
    {
        level_value_w = MathMin(144, MathMax(120, content_w / 5));
        level_value_x = content_w - 170 - level_value_w - 12;
        if(level_value_x < label_x + 220)
            level_value_x = label_x + 220;
        CreateOrUpdateScrolledLabel(Obj("LevelSubHeader"), level_value_x, level_header_y, "Scale-In Lot", clrWhite, 10);
        level_top = levels_y + 34;
    }
    int start = g_level_page * LEVEL_ROWS_PER_PAGE;
    int total = ArraySize(g_levels);
    int active_level_idx = CurrentMartingaleLevelIndex();
    string active_drawdown_text = CurrentMartingaleDrawdownText();
    for(int slot = 0; slot < LEVEL_ROWS_PER_PAGE; slot++)
    {
        int idx = start + slot;
        int y = level_top + (slot * level_row_h);
        string label_name = Obj(StringFormat("LevelLabel_%d", slot));
        string drawdown_name = Obj(StringFormat("LevelDrawdown_%d", slot));
        string edit_name = Obj(StringFormat("LevelLotEdit_%d", slot));
        if(idx < total)
        {
            string state = (idx < g_consumed_levels ? "[x]" : "[ ]");
            string line = "";
            if(compact)
                line = StringFormat("%s L%d %.0f @ %s", state, idx + 1, g_levels[idx].distance_points, FormatPrice(g_levels[idx].price));
            else
                line = StringFormat("%s L%d %s @ %s",
                                    state,
                                    idx + 1,
                                    FormatPoints(g_levels[idx].distance_points),
                                    FormatPrice(g_levels[idx].price));
            color row_color = (idx < g_consumed_levels ? clrOrange : clrDodgerBlue);
            CreateOrUpdateScrolledLabel(label_name, label_x, y + 4, line, row_color, 10);
            if(idx == active_level_idx && active_drawdown_text != "")
            {
                int drawdown_x = label_x + 300;
                if(compact)
                    drawdown_x = MathMax(label_x + 142, content_w - 118);
                else if(drawdown_x + 118 > level_value_x)
                {
                    if(level_value_x + level_value_w + 126 < content_w)
                        drawdown_x = level_value_x + level_value_w + 8;
                    else
                        drawdown_x = MathMax(label_x + 170, content_w - 126);
                }

                CreateOrUpdateScrolledBoldLabel(drawdown_name, drawdown_x, y + 4, active_drawdown_text, clrRed, 10);
            }
            CreateOrUpdateScrolledEdit(edit_name, level_value_x, (compact ? y + 20 : y), level_value_w, 24, DoubleToString(g_levels[idx].lot, 4));
        }
        else
        {
            CreateOrUpdateScrolledLabel(label_name, label_x, y + 4, "", clrGainsboro, 10);
            CreateOrUpdateScrolledEdit(edit_name, level_value_x, (compact ? y + 20 : y), level_value_w, 24, "");
        }
    }
}

void RenderLines()
{
    CreateOrUpdateHLine(Obj("EntryLine"), g_entry_price, clrWhite, STYLE_SOLID, 3);
    CreateOrUpdateHLine(Obj("TpLine"), g_current_tp_price, clrLimeGreen, STYLE_SOLID, 2);
    CreateOrUpdateHLine(Obj("SlLine"), g_current_sl_price, clrTomato, STYLE_SOLID, 2);

    CreateOrUpdatePriceText(Obj("EntryGrip"), g_entry_price, "Drag Grid", clrWhite, (color)C'70,76,84', 132, 22, 0, 4);
    CreateOrUpdatePriceText(Obj("EntryCallout"), g_entry_price, StringFormat("Entry  %s", FormatPrice(g_entry_price)), clrWhite, (color)C'48,52,58', CALLOUT_MAIN_W, CALLOUT_MAIN_H, 1, CALLOUT_MAIN_GAP_Y);
    CreateOrUpdatePriceText(Obj("TpCallout"), g_current_tp_price, StringFormat("TP  %s  |  %s", FormatUsd(g_last_tp_usd), FormatPoints(g_last_tp_points_from_entry)), clrLimeGreen, (color)C'12,40,18', CALLOUT_MAIN_W, CALLOUT_MAIN_H, 1, CALLOUT_MAIN_GAP_Y);
    CreateOrUpdatePriceText(Obj("SlCallout"), g_current_sl_price, StringFormat("SL  %s  |  %s", FormatUsd(g_last_sl_usd), FormatPoints(g_last_sl_points_from_entry)), clrTomato, (color)C'52,18,18', CALLOUT_MAIN_W, CALLOUT_MAIN_H, 1, CALLOUT_MAIN_GAP_Y);

    int active_level_idx = CurrentMartingaleLevelIndex();
    string active_drawdown_text = CurrentMartingaleDrawdownText();
    for(int i = 0; i < ArraySize(g_levels); i++)
    {
        bool is_active_level = (i == active_level_idx && active_drawdown_text != "");
        color level_color = (i < g_consumed_levels ? clrOrange : clrDodgerBlue);
        int line_width = (i < g_consumed_levels ? 3 : 2);
        int popup_w = (is_active_level ? CALLOUT_LEVEL_ACTIVE_W : CALLOUT_LEVEL_W);
        string line_name = Obj(StringFormat("LevelLine_%d", i));
        string text_name = Obj(StringFormat("LevelCallout_%d", i));
        CreateOrUpdateHLine(line_name, g_levels[i].price, level_color, STYLE_DASH, line_width);
        CreateOrUpdatePriceText(text_name,
                                g_levels[i].price,
                                StringFormat("L%d  %.4f lots", i + 1, g_levels[i].lot),
                                level_color,
                                (i < g_consumed_levels ? (color)C'64,36,14' : (color)C'12,28,52'),
                                popup_w,
                                CALLOUT_LEVEL_H,
                                (i % 3),
                                CALLOUT_LEVEL_GAP_Y);
        if(is_active_level)
            CreateOrUpdatePriceDrawdownText(Obj(StringFormat("LevelDrawdownCallout_%d", i)),
                                            g_levels[i].price,
                                            active_drawdown_text,
                                            popup_w,
                                            CALLOUT_LEVEL_H,
                                            (i % 3),
                                            CALLOUT_LEVEL_GAP_Y);
    }
}

void RenderAll()
{
    EnsureValidPage();
    DeletePlannerObjects();
    RefreshRuntimeOutputs();
    RenderPanel();
    RenderLines();
    ChartRedraw();
}

int ParseTrailingIndex(const string value)
{
    int underscore = StringFind(value, "_", StringLen(g_prefix));
    while(underscore >= 0)
    {
        int next = StringFind(value, "_", underscore + 1);
        if(next < 0)
        {
            string tail = StringSubstr(value, underscore + 1);
            return (int)StringToInteger(tail);
        }
        underscore = next;
    }
    return -1;
}

int ParseLevelCalloutIndex(const string value)
{
    string prefix = Obj("LevelCallout_");
    if(!StartsWith(value, prefix))
        return -1;

    int start = StringLen(prefix);
    int end = StringFind(value, "_Bg", start);
    if(end < 0)
        end = StringFind(value, "_Text", start);
    if(end < 0 || end <= start)
        return -1;

    return (int)StringToInteger(StringSubstr(value, start, end - start));
}

void ShiftPlannerAnchor(const double new_entry_price)
{
    if(new_entry_price <= 0.0)
        return;

    double delta = new_entry_price - g_entry_price;
    if(MathAbs(delta) <= (_Point * 0.1))
        return;

    g_entry_price = new_entry_price;

    // Re-anchor every explicit level price so the whole ladder moves together.
    for(int i = 0; i < ArraySize(g_levels); i++)
        g_levels[i].price += delta;

    ValidateCurrentPlan();
}

void HandleEntryCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    ShiftPlannerAnchor(new_price);
}

void HandleTpCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    double deepest = CurrentDeepestConsumedDistance();
    double current_points = FavorablePointsBetween(g_entry_price, new_price);
    double recomputed_base_points = current_points + deepest;
    if(recomputed_base_points <= 0.0)
        recomputed_base_points = 1.0;
    g_final_tp_points = recomputed_base_points;
    if(!g_custom_mode)
    {
        int preserved_consumed = g_consumed_levels;
        RebuildGeneratedPlanner();
        g_consumed_levels = MathMin(preserved_consumed, ArraySize(g_levels));
        ValidateCurrentPlan();
    }
    else
    {
        ValidateCurrentPlan();
    }
}

void HandleSlCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    double adverse_points = RawAdverseDistancePoints(new_price);
    if(adverse_points <= 0.0)
        adverse_points = 1.0;
    g_final_sl_points = adverse_points;
    if(!g_custom_mode)
    {
        int preserved_consumed = g_consumed_levels;
        RebuildGeneratedPlanner();
        g_consumed_levels = MathMin(preserved_consumed, ArraySize(g_levels));
        ValidateCurrentPlan();
    }
    else
    {
        ValidateCurrentPlan();
    }
}

void HandleLevelCalloutDrag(const int idx, const string object_name)
{
    if(idx < 0 || idx >= ArraySize(g_levels))
        return;

    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_LEVEL_H, new_price) || new_price <= 0.0)
        return;

    g_levels[idx].price = new_price;
    g_levels[idx].distance_points = RawAdverseDistancePoints(new_price);
    g_levels[idx].manual_price = true;
    g_custom_mode = true;
    ValidateCurrentPlan();
}

void HandleEntryLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("EntryLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    ShiftPlannerAnchor(new_price);
}

void HandleTpLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("TpLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    double deepest = CurrentDeepestConsumedDistance();
    double current_points = FavorablePointsBetween(g_entry_price, new_price);
    double recomputed_base_points = current_points + deepest;
    if(recomputed_base_points <= 0.0)
        recomputed_base_points = 1.0;
    g_final_tp_points = recomputed_base_points;
    if(!g_custom_mode)
    {
        int preserved_consumed = g_consumed_levels;
        RebuildGeneratedPlanner();
        g_consumed_levels = MathMin(preserved_consumed, ArraySize(g_levels));
        ValidateCurrentPlan();
    }
    else
    {
        ValidateCurrentPlan();
    }
}

void HandleSlLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("SlLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    double adverse_points = RawAdverseDistancePoints(new_price);
    if(adverse_points <= 0.0)
        adverse_points = 1.0;
    g_final_sl_points = adverse_points;
    if(!g_custom_mode)
    {
        int preserved_consumed = g_consumed_levels;
        RebuildGeneratedPlanner();
        g_consumed_levels = MathMin(preserved_consumed, ArraySize(g_levels));
        ValidateCurrentPlan();
    }
    else
        ValidateCurrentPlan();
}

void HandleLevelLineDrag(const int idx)
{
    if(idx < 0 || idx >= ArraySize(g_levels))
        return;

    double new_price = ObjectGetDouble(0, Obj(StringFormat("LevelLine_%d", idx)), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    g_levels[idx].price = new_price;
    g_levels[idx].distance_points = RawAdverseDistancePoints(new_price);
    g_levels[idx].manual_price = true;
    g_custom_mode = true;
    ValidateCurrentPlan();
}

void ApplyDoubleEdit(const string name, double &target, const bool positive_only, const bool reset_generated)
{
    string raw = "";
    if(!ObjectGetString(0, name, OBJPROP_TEXT, 0, raw))
        raw = ObjectGetString(0, name, OBJPROP_TEXT);
    double parsed = StringToDouble(raw);
    if(positive_only && parsed <= 0.0)
        return;
    target = parsed;
    if(reset_generated)
        ApplyInputChangeAndRebuild();
    else
        ValidateCurrentPlan();
}

void ApplyIntEdit(const string name, int &target, const int min_value, const int max_value, const bool reset_generated)
{
    string raw = "";
    if(!ObjectGetString(0, name, OBJPROP_TEXT, 0, raw))
        raw = ObjectGetString(0, name, OBJPROP_TEXT);
    int parsed = (int)StringToInteger(raw);
    if(parsed < min_value)
        parsed = min_value;
    if(parsed > max_value)
        parsed = max_value;
    target = parsed;
    if(reset_generated)
        ApplyInputChangeAndRebuild();
    else
        ValidateCurrentPlan();
}

void HandleLevelLotEdit(const int slot)
{
    int idx = g_level_page * LEVEL_ROWS_PER_PAGE + slot;
    if(idx < 0 || idx >= ArraySize(g_levels))
        return;

    string name = Obj(StringFormat("LevelLotEdit_%d", slot));
    string raw = "";
    if(!ObjectGetString(0, name, OBJPROP_TEXT, 0, raw))
        raw = ObjectGetString(0, name, OBJPROP_TEXT);
    double parsed = StringToDouble(raw);
    g_levels[idx].lot = NormalizeManualLevelLot(parsed);
    g_levels[idx].manual_lot = true;
    g_custom_mode = true;
    ValidateCurrentPlan();
}

void LoadInputsToRuntime()
{
    g_direction = InpDirection;
    g_level_count_mode = InpLevelCountMode;
    g_lot_mode = InpLotMode;
    g_max_loss_mode = InpMaxLossMode;
    g_final_tp_points = InpFinalTakeProfitPoints;
    g_final_sl_points = InpFinalStopLossPoints;
    g_min_final_tp_usd = InpMinimumFinalTakeProfitUsd;
    g_initial_lot = InpInitialHedgeLot;
    g_spacing_points = InpLevelSpacingPoints;
    g_spacing_multiplier = InpSpacingMultiplier;
    g_max_levels = InpMaxLevels;
    g_fixed_scale_in_lot = InpFixedScaleInLot;
    g_max_loss_usd = InpMaxLossUsd;
    g_max_tp_override_percent = InpMaxTpOverridePercent;
}

int OnInit()
{
    g_prefix = StringFormat("%s%I64d_", PLANNER_OBJECT_PREFIX_ROOT, ChartID());
    LoadInputsToRuntime();
    RefreshSymbolSpecs();
    g_panel_x = PANEL_X;
    g_panel_y = PANEL_Y;
    g_panel_w = PANEL_W;
    g_panel_h = PANEL_H;
    g_panel_restore_w = PANEL_W;
    g_panel_restore_h = PANEL_H;
    g_panel_scroll_x = 0;
    g_panel_scroll_y = 0;
    g_panel_minimized = false;
    g_entry_price = AnchorPrice();
    ResetPlanner(true);
    RenderAll();
    return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
    DeletePlannerObjects();
    ChartRedraw();
}

void OnTick()
{
}

void OnChartEvent(const int id,
                  const long &lparam,
                  const double &dparam,
                  const string &sparam)
{
    if(id == CHARTEVENT_CHART_CHANGE)
    {
        RenderAll();
        return;
    }

    if(id != CHARTEVENT_OBJECT_CLICK &&
       id != CHARTEVENT_OBJECT_DRAG &&
       id != CHARTEVENT_OBJECT_ENDEDIT)
        return;

    if(!StartsWith(sparam, g_prefix))
        return;

    if(id == CHARTEVENT_OBJECT_CLICK)
    {
        bool handled_click = false;
        if(sparam == Obj("BtnSizeDown"))
        {
            ResizePanelByStep(-1);
            handled_click = true;
        }
        else if(sparam == Obj("BtnSizeUp"))
        {
            ResizePanelByStep(1);
            handled_click = true;
        }
        else if(sparam == Obj("BtnMinimize"))
        {
            TogglePanelMinimized();
            handled_click = true;
        }
        else if(sparam == Obj("BtnDir"))
        {
            CycleDirection();
            handled_click = true;
        }
        else if(sparam == Obj("BtnCountMode"))
        {
            CycleLevelCountMode();
            handled_click = true;
        }
        else if(sparam == Obj("BtnLotMode"))
        {
            CycleLotMode();
            handled_click = true;
        }
        else if(sparam == Obj("BtnMaxLossMode"))
        {
            CycleMaxLossMode();
            handled_click = true;
        }
        else if(sparam == Obj("BtnAddTrade"))
        {
            AddTradeStep();
            handled_click = true;
        }
        else if(sparam == Obj("BtnRemoveTrade"))
        {
            RemoveTradeStep();
            handled_click = true;
        }
        else if(sparam == Obj("BtnReset"))
        {
            ResetPlanner(true);
            handled_click = true;
        }
        else if(sparam == Obj("BtnAddLevel"))
        {
            if(g_level_count_mode == PlannerLevelCount_UserMaxLevels)
            {
                g_max_levels = MathMin(MAX_LEVELS_LIMIT, g_max_levels + 1);
                ApplyInputChangeAndRebuild();
                handled_click = true;
            }
        }
        else if(sparam == Obj("BtnRemoveLevel"))
        {
            if(g_level_count_mode == PlannerLevelCount_UserMaxLevels)
            {
                g_max_levels = MathMax(0, g_max_levels - 1);
                ApplyInputChangeAndRebuild();
                handled_click = true;
            }
        }
        else if(sparam == Obj("BtnPrevPage"))
        {
            g_level_page--;
            EnsureValidPage();
            handled_click = true;
        }
        else if(sparam == Obj("BtnNextPage"))
        {
            g_level_page++;
            EnsureValidPage();
            handled_click = true;
        }
        else if(sparam == Obj("BtnScrollLeft"))
        {
            ShiftPanelScroll(-PANEL_SCROLL_STEP, 0);
            handled_click = true;
        }
        else if(sparam == Obj("BtnScrollRight"))
        {
            ShiftPanelScroll(PANEL_SCROLL_STEP, 0);
            handled_click = true;
        }
        else if(sparam == Obj("BtnScrollUp"))
        {
            ShiftPanelScroll(0, -PANEL_SCROLL_STEP);
            handled_click = true;
        }
        else if(sparam == Obj("BtnScrollDown"))
        {
            ShiftPanelScroll(0, PANEL_SCROLL_STEP);
            handled_click = true;
        }

        if(handled_click)
            RenderAll();
        return;
    }

    if(id == CHARTEVENT_OBJECT_DRAG)
    {
        if(sparam == Obj("EntryLine"))
            HandleEntryLineDrag();
        else if(sparam == Obj("EntryGrip_Bg") || sparam == Obj("EntryGrip_Text"))
            HandleEntryCalloutDrag(sparam);
        else if(sparam == Obj("EntryCallout_Bg") || sparam == Obj("EntryCallout_Text"))
            HandleEntryCalloutDrag(sparam);
        else if(sparam == Obj("TpLine"))
            HandleTpLineDrag();
        else if(sparam == Obj("TpCallout_Bg") || sparam == Obj("TpCallout_Text"))
            HandleTpCalloutDrag(sparam);
        else if(sparam == Obj("SlLine"))
            HandleSlLineDrag();
        else if(sparam == Obj("SlCallout_Bg") || sparam == Obj("SlCallout_Text"))
            HandleSlCalloutDrag(sparam);
        else if(StartsWith(sparam, Obj("LevelLine_")))
            HandleLevelLineDrag(ParseTrailingIndex(sparam));
        else if(StartsWith(sparam, Obj("LevelCallout_")))
            HandleLevelCalloutDrag(ParseLevelCalloutIndex(sparam), sparam);

        RenderAll();
        return;
    }

    if(id == CHARTEVENT_OBJECT_ENDEDIT)
    {
        if(sparam == Obj("EditTp"))
            ApplyDoubleEdit(sparam, g_final_tp_points, true, true);
        else if(sparam == Obj("EditSl"))
            ApplyDoubleEdit(sparam, g_final_sl_points, true, true);
        else if(sparam == Obj("EditMinTpUsd"))
            ApplyDoubleEdit(sparam, g_min_final_tp_usd, true, true);
        else if(sparam == Obj("EditInitLot"))
            ApplyDoubleEdit(sparam, g_initial_lot, true, true);
        else if(sparam == Obj("EditSpacing"))
            ApplyDoubleEdit(sparam, g_spacing_points, true, true);
        else if(sparam == Obj("EditSpacingMultiplier"))
            ApplyDoubleEdit(sparam, g_spacing_multiplier, true, true);
        else if(sparam == Obj("EditMaxLevels"))
            ApplyIntEdit(sparam, g_max_levels, 0, MAX_LEVELS_LIMIT, true);
        else if(sparam == Obj("EditFixedLot"))
            ApplyDoubleEdit(sparam, g_fixed_scale_in_lot, true, true);
        else if(sparam == Obj("EditMaxLossUsd"))
            ApplyDoubleEdit(sparam, g_max_loss_usd, false, true);
        else if(sparam == Obj("EditMaxTpOverride"))
            ApplyDoubleEdit(sparam, g_max_tp_override_percent, false, true);
        else if(StartsWith(sparam, Obj("LevelLotEdit_")))
            HandleLevelLotEdit(ParseTrailingIndex(sparam));

        RenderAll();
        return;
    }
}
