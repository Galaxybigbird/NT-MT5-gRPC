#property copyright ""
#property link      ""
#property version   "1.00"
#property strict
#property description "Offline single-trade hedge calculator with draggable TP/SL lines and live chart dashboard"

input group "===== Offline Hedge Calculator =====";
enum OHC_DIRECTION
{
    OhcDirection_Long = 0,
    OhcDirection_Short = 1
};
input OHC_DIRECTION InpDirection        = OhcDirection_Short;
input double        InpLotSize          = 0.06;
input double        InpTakeProfitPoints = 10000.0;
input double        InpStopLossPoints   = 25000.0;

const int    PANEL_X = 10;
const int    PANEL_Y = 40;
const int    PANEL_W = 560;
const int    PANEL_H = 340;
const int    PANEL_HEADER_H = 42;
const int    PANEL_CONTENT_MARGIN = 14;
const int    PANEL_CONTENT_TOP = 56;
const int    PANEL_MIN_W = 340;
const int    PANEL_MIN_H = 220;
const int    PANEL_RESIZE_HANDLE = 18;
const int    PANEL_SCROLL_BUTTON = 18;
const int    PANEL_SCROLL_STEP = 48;
const int    PANEL_RESIZE_STEP_W = 60;
const int    PANEL_RESIZE_STEP_H = 40;
const int    PANEL_MINIMIZED_W = 340;
const int    PANEL_MINIMIZED_H = PANEL_HEADER_H + 2;
const int    CALLOUT_BASE_X_GAP = 28;
const int    CALLOUT_LANE_STEP = 188;
const int    CALLOUT_MAIN_GAP_Y = 18;
const int    CALLOUT_MAIN_W = 252;
const int    CALLOUT_MAIN_H = 24;
const int    CALLOUT_TEXT_PAD_X = 8;
const int    CALLOUT_TEXT_PAD_Y = 3;
const string PANEL_FONT = "Segoe UI";
const string PANEL_FONT_BOLD = "Segoe UI Semibold";
const string CALCULATOR_OBJECT_PREFIX_ROOT = "OHC_";

string        g_prefix = "";
OHC_DIRECTION g_direction;
double        g_lot_size = 0.0;
double        g_tp_points = 0.0;
double        g_sl_points = 0.0;

double g_entry_price = 0.0;
double g_current_tp_price = 0.0;
double g_current_sl_price = 0.0;
double g_last_tp_usd = 0.0;
double g_last_sl_usd = 0.0;
double g_last_tp_points_from_entry = 0.0;
double g_last_sl_points_from_entry = 0.0;

bool   g_symbol_specs_ok = false;
bool   g_calculator_valid = false;
double g_point_value_per_lot = 0.0;
double g_min_lot = 0.0;
double g_max_lot = 0.0;
double g_lot_step = 0.0;
int    g_digits = 5;
string g_status_warning = "";

int  g_panel_x = PANEL_X;
int  g_panel_y = PANEL_Y;
int  g_panel_w = PANEL_W;
int  g_panel_h = PANEL_H;
int  g_panel_scroll_x = 0;
int  g_panel_scroll_y = 0;
int  g_panel_content_w = PANEL_W - (PANEL_CONTENT_MARGIN * 2);
int  g_panel_content_h = PANEL_H - PANEL_CONTENT_TOP - PANEL_CONTENT_MARGIN;
bool g_panel_minimized = false;
int  g_panel_restore_w = PANEL_W;
int  g_panel_restore_h = PANEL_H;

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
    if(line == "")
        return;

    if(dest == "")
        dest = line;
    else
        dest += "\n" + line;
}

string DirectionName()
{
    return (g_direction == OhcDirection_Long ? "Long" : "Short");
}

int DirectionSign()
{
    return (g_direction == OhcDirection_Long ? 1 : -1);
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

string FormatPoints(const double value)
{
    return DoubleToString(value, 0) + " pts";
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

double FavorablePointsBetween(const double entry_price, const double exit_price)
{
    if(_Point <= 0.0)
        return 0.0;
    return (DirectionSign() * (exit_price - entry_price) / _Point);
}

double RawAdverseDistancePoints(const double price)
{
    return -FavorablePointsBetween(g_entry_price, price);
}

double ComputeTpPrice()
{
    return g_entry_price + (DirectionSign() * g_tp_points * _Point);
}

double ComputeSlPrice()
{
    return g_entry_price - (DirectionSign() * g_sl_points * _Point);
}

double TradeUsdAtPrice(const double exit_price)
{
    if(g_lot_size <= 0.0 || g_point_value_per_lot <= 0.0)
        return 0.0;

    double favorable_points = FavorablePointsBetween(g_entry_price, exit_price);
    return favorable_points * g_point_value_per_lot * g_lot_size;
}

void RefreshRuntimeOutputs()
{
    g_current_tp_price = ComputeTpPrice();
    g_current_sl_price = ComputeSlPrice();
    g_last_tp_usd = TradeUsdAtPrice(g_current_tp_price);
    g_last_sl_usd = TradeUsdAtPrice(g_current_sl_price);
    g_last_tp_points_from_entry = FavorablePointsBetween(g_entry_price, g_current_tp_price);
    g_last_sl_points_from_entry = FavorablePointsBetween(g_entry_price, g_current_sl_price);
}

void ValidateCalculator()
{
    g_status_warning = "";

    if(!RefreshSymbolSpecs())
    {
        g_calculator_valid = false;
        g_status_warning = "Unable to derive current symbol point value / lot constraints.";
        RefreshRuntimeOutputs();
        return;
    }

    double original_lot = g_lot_size;
    g_lot_size = NormalizeLotNearest(g_lot_size);
    g_lot_size = NormalizeDouble(g_lot_size, 2);
    if(g_lot_size <= 0.0)
    {
        g_calculator_valid = false;
        g_status_warning = "Lot size must be greater than zero.";
        RefreshRuntimeOutputs();
        return;
    }
    if(MathAbs(original_lot - g_lot_size) > 1e-8)
        AppendLine(g_status_warning, StringFormat("Lot normalized to %.4f by symbol volume rules.", g_lot_size));

    if(g_tp_points <= 0.0 || g_sl_points <= 0.0)
    {
        g_calculator_valid = false;
        AppendLine(g_status_warning, "TP and SL points must be greater than zero.");
        RefreshRuntimeOutputs();
        return;
    }

    if(g_entry_price <= 0.0)
        g_entry_price = AnchorPrice();

    g_calculator_valid = true;
    RefreshRuntimeOutputs();
}

void ResetCalculator(const bool reseed_entry)
{
    if(reseed_entry || g_entry_price <= 0.0)
        g_entry_price = AnchorPrice();
    ValidateCalculator();
}

void CycleDirection()
{
    g_direction = (g_direction == OhcDirection_Long ? OhcDirection_Short : OhcDirection_Long);
    ValidateCalculator();
}

void ShiftCalculatorAnchor(const double new_entry_price)
{
    if(new_entry_price <= 0.0)
        return;

    if(MathAbs(new_entry_price - g_entry_price) <= (_Point * 0.1))
        return;

    g_entry_price = new_entry_price;
    ValidateCalculator();
}

void ApplyDoubleEdit(const string name, double &target, const bool positive_only)
{
    string raw = "";
    if(!ObjectGetString(0, name, OBJPROP_TEXT, 0, raw))
        raw = ObjectGetString(0, name, OBJPROP_TEXT);

    double parsed = StringToDouble(raw);
    if(positive_only && parsed <= 0.0)
        return;

    target = parsed;
    ValidateCalculator();
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
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
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
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
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
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetString(0, name, OBJPROP_FONT, PANEL_FONT_BOLD);
    ObjectSetInteger(0, name, OBJPROP_FONTSIZE, font_size);
    ObjectSetString(0, name, OBJPROP_TEXT, text);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, zorder);
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
    ObjectSetInteger(0, name, OBJPROP_SELECTED, false);
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

void DeleteCalculatorObjects()
{
    if(g_prefix == "")
        return;

    ObjectsDeleteAll(0, g_prefix, -1, -1);

    for(int pass = 0; pass < 3; pass++)
    {
        bool deleted = false;
        for(int i = ObjectsTotal(0, -1, -1) - 1; i >= 0; i--)
        {
            string name = ObjectName(0, i, -1, -1);
            if(StartsWith(name, g_prefix))
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
    if(ObjectFind(0, name) < 0)
        ObjectCreate(0, name, OBJ_HLINE, 0, 0, price);

    ObjectSetDouble(0, name, OBJPROP_PRICE, price);
    ObjectSetInteger(0, name, OBJPROP_COLOR, clr);
    ObjectSetInteger(0, name, OBJPROP_STYLE, style);
    ObjectSetInteger(0, name, OBJPROP_WIDTH, width);
    ObjectSetInteger(0, name, OBJPROP_SELECTABLE, true);
    ObjectSetInteger(0, name, OBJPROP_SELECTED, true);
    ObjectSetInteger(0, name, OBJPROP_HIDDEN, true);
    ObjectSetInteger(0, name, OBJPROP_BACK, false);
    ObjectSetInteger(0, name, OBJPROP_ZORDER, 7);
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

string SummaryText()
{
    string text = "";
    AppendLine(text, StringFormat("Symbol: %s", _Symbol));
    AppendLine(text, StringFormat("Direction: %s", DirectionName()));
    AppendLine(text, StringFormat("Entry: %s", FormatPrice(g_entry_price)));
    AppendLine(text, StringFormat("Lot Size: %.2f", g_lot_size));
    AppendLine(text, StringFormat("TP: %s | %s @ %s",
                                  FormatUsd(g_last_tp_usd),
                                  FormatPoints(g_last_tp_points_from_entry),
                                  FormatPrice(g_current_tp_price)));
    AppendLine(text, StringFormat("SL: %s | %s @ %s",
                                  FormatUsd(g_last_sl_usd),
                                  FormatPoints(g_last_sl_points_from_entry),
                                  FormatPrice(g_current_sl_price)));
    AppendLine(text, StringFormat("Point Value / Lot: $%.6f", g_point_value_per_lot));
    return text;
}

string StatusText()
{
    string text = "";
    AppendLine(text, "Offline calculator only. No live orders are placed.");
    if(g_status_warning != "")
        AppendLine(text, g_status_warning);
    else
        AppendLine(text, "Ready.");
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
    string panel_title = (((header_text_right - title_x) < 230) ? "OHC" : "Offline Hedge Calculator");

    CreateOrUpdatePanelBackground(Obj("PanelBg"), g_panel_x, g_panel_y, g_panel_w, g_panel_h, (color)C'22,24,28', (color)C'188,194,204');
    CreateOrUpdatePanelBackground(Obj("HeaderBg"), g_panel_x + 1, g_panel_y + 1, g_panel_w - 2, PANEL_HEADER_H, (color)C'54,66,84', (color)C'188,194,204');
    if(!g_panel_minimized)
    {
        CreateOrUpdateButton(Obj("BtnSizeDown"), size_down_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "-", (color)C'68,72,80', clrWhite);
        CreateOrUpdateButton(Obj("BtnSizeUp"), size_up_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "+", (color)C'68,72,80', clrWhite);
    }
    CreateOrUpdateLabel(Obj("PanelTitle"), title_x, g_panel_y + 12, panel_title, clrWhite, 13);
    if(!g_panel_minimized && (header_text_right - title_x) >= 450)
        CreateOrUpdateLabel(Obj("PanelSubtitle"), header_text_right - 226, g_panel_y + 14, "Calculator only. No live orders.", clrGainsboro, 9);
    CreateOrUpdateButton(Obj("BtnMinimize"), min_btn_x, header_button_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, (g_panel_minimized ? "+" : "-"), (color)C'68,72,80', clrWhite);

    if(g_panel_minimized)
        return;

    int viewport_w = PanelViewportW();
    int viewport_h = PanelViewportH();
    int content_w = MathMax(320, viewport_w);
    int content_h = MathMax(240, viewport_h);
    bool stacked = (content_w < 520);
    bool compact = (content_w < 500);
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
        left_w = MathMax(300, (content_w * 52) / 100);
        if(left_w > 320)
            left_w = 320;
        if(left_w > content_w - section_gap - 210)
            left_w = content_w - section_gap - 210;
        right_w = content_w - left_w - section_gap;
    }

    int row_h = 32;
    int input_h = 196;
    int summary_h = 150;
    int status_h = 78;
    int summary_x = (stacked ? 0 : left_w + section_gap);
    int summary_y = (stacked ? input_h + section_gap : 0);
    int status_x = summary_x;
    int status_y = summary_y + summary_h + section_gap;

    g_panel_content_w = content_w;
    g_panel_content_h = MathMax(MathMax(input_h, status_y + status_h), content_h);
    ClampPanelScroll();

    int label_x = 8;
    int value_w = MathMin((compact ? 132 : 156), MathMax(116, left_w / 3));
    int value_x = left_w - value_w - 14;
    if(value_x < 162)
        value_x = 162;

    CreateOrUpdateButton(Obj("BtnScrollLeft"), scroll_x, scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "<", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollRight"), scroll_x + PANEL_SCROLL_BUTTON + header_btn_gap, scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, ">", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollUp"), scroll_x + ((PANEL_SCROLL_BUTTON + header_btn_gap) * 2), scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "^", (color)C'68,72,80', clrWhite);
    CreateOrUpdateButton(Obj("BtnScrollDown"), scroll_x + ((PANEL_SCROLL_BUTTON + header_btn_gap) * 3), scroll_y, PANEL_SCROLL_BUTTON, PANEL_SCROLL_BUTTON, "v", (color)C'68,72,80', clrWhite);

    CreateOrUpdateScrolledPanelBackground(Obj("InputBg"), 0, 0, left_w, input_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("SummaryBg"), summary_x, summary_y, right_w, summary_h, (color)C'38,43,50', (color)C'112,122,136');
    CreateOrUpdateScrolledPanelBackground(Obj("StatusBg"), status_x, status_y, right_w, status_h, (color)C'38,43,50', (color)C'112,122,136');

    CreateOrUpdateScrolledLabel(Obj("InputsTitle"), label_x, 6, "Inputs", clrWhite, 10);
    CreateOrUpdateScrolledLabel(Obj("SummaryTitle"), summary_x + 8, summary_y + 6, "Trade Summary", clrWhite, 10);
    CreateOrUpdateScrolledLabel(Obj("StatusTitle"), status_x + 8, status_y + 6, "Status", clrWhite, 10);

    int input_y = 32;
    CreateOrUpdateScrolledLabel(Obj("LblDir"), label_x, input_y + 6, "Direction", clrGainsboro, 10);
    CreateOrUpdateScrolledButton(Obj("BtnDir"), value_x, input_y, value_w, 24, DirectionName(), (g_direction == OhcDirection_Long ? clrSeaGreen : clrIndianRed), clrWhite);
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblLot"), label_x, input_y + 6, "Lot Size", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditLot"), value_x, input_y, value_w, 24, DoubleToString(g_lot_size, 2));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblTp"), label_x, input_y + 6, "Take Profit (pts)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditTp"), value_x, input_y, value_w, 24, DoubleToString(g_tp_points, 0));
    input_y += row_h;

    CreateOrUpdateScrolledLabel(Obj("LblSl"), label_x, input_y + 6, "Stop Loss (pts)", clrGainsboro, 10);
    CreateOrUpdateScrolledEdit(Obj("EditSl"), value_x, input_y, value_w, 24, DoubleToString(g_sl_points, 0));
    input_y += row_h;

    CreateOrUpdateScrolledButton(Obj("BtnReset"), value_x, input_y, value_w, 24, "Reset", clrMediumPurple, clrWhite);

    int summary_chars = MathMax(28, right_w / 10);
    int status_chars = MathMax(30, right_w / 9);
    CreateOrUpdateScrolledMultilineLabelBlock(Obj("SummaryLabel"), summary_x + 8, summary_y + 30, SummaryText(), (g_calculator_valid ? clrLightGreen : clrKhaki), 10, 16, summary_chars);
    CreateOrUpdateScrolledMultilineLabelBlock(Obj("StatusLabel"), status_x + 8, status_y + 30, StatusText(), (g_calculator_valid ? clrLightGray : clrOrange), 9, 14, status_chars);

    CreateOrUpdateResizeHandle(Obj("ResizeTL"), g_panel_x, g_panel_y);
    CreateOrUpdateResizeHandle(Obj("ResizeTR"), g_panel_x + g_panel_w - PANEL_RESIZE_HANDLE, g_panel_y);
    CreateOrUpdateResizeHandle(Obj("ResizeBL"), g_panel_x, g_panel_y + g_panel_h - PANEL_RESIZE_HANDLE);
    CreateOrUpdateResizeHandle(Obj("ResizeBR"), g_panel_x + g_panel_w - PANEL_RESIZE_HANDLE, g_panel_y + g_panel_h - PANEL_RESIZE_HANDLE);
}

void RenderLines()
{
    if(g_entry_price <= 0.0)
        return;

    CreateOrUpdateHLine(Obj("EntryLine"), g_entry_price, clrWhite, STYLE_SOLID, 3);
    CreateOrUpdateHLine(Obj("TpLine"), g_current_tp_price, clrLimeGreen, STYLE_SOLID, 2);
    CreateOrUpdateHLine(Obj("SlLine"), g_current_sl_price, clrTomato, STYLE_SOLID, 2);

    CreateOrUpdatePriceText(Obj("EntryGrip"),
                            g_entry_price,
                            "Entry",
                            clrWhite,
                            (color)C'70,76,84',
                            132,
                            22,
                            0,
                            4);
    CreateOrUpdatePriceText(Obj("EntryCallout"),
                            g_entry_price,
                            StringFormat("Entry  %s", FormatPrice(g_entry_price)),
                            clrWhite,
                            (color)C'48,52,58',
                            CALLOUT_MAIN_W,
                            CALLOUT_MAIN_H,
                            1,
                            CALLOUT_MAIN_GAP_Y);
    CreateOrUpdatePriceText(Obj("TpCallout"),
                            g_current_tp_price,
                            StringFormat("TP  %s  |  %s", FormatUsd(g_last_tp_usd), FormatPoints(g_last_tp_points_from_entry)),
                            clrLimeGreen,
                            (color)C'12,40,18',
                            CALLOUT_MAIN_W,
                            CALLOUT_MAIN_H,
                            1,
                            CALLOUT_MAIN_GAP_Y);
    CreateOrUpdatePriceText(Obj("SlCallout"),
                            g_current_sl_price,
                            StringFormat("SL  %s  |  %s", FormatUsd(g_last_sl_usd), FormatPoints(g_last_sl_points_from_entry)),
                            clrTomato,
                            (color)C'52,18,18',
                            CALLOUT_MAIN_W,
                            CALLOUT_MAIN_H,
                            1,
                            CALLOUT_MAIN_GAP_Y);
}

void RenderAll()
{
    DeleteCalculatorObjects();
    ValidateCalculator();
    RenderPanel();
    RenderLines();
    ChartRedraw();
}

void HandleEntryCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    ShiftCalculatorAnchor(new_price);
}

void HandleTpCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    double points = FavorablePointsBetween(g_entry_price, new_price);
    if(points <= 0.0)
        points = 1.0;
    g_tp_points = points;
    ValidateCalculator();
}

void HandleSlCalloutDrag(const string object_name)
{
    double new_price = 0.0;
    if(!ResolveDraggedLabelPrice(object_name, CALLOUT_MAIN_H, new_price) || new_price <= 0.0)
        return;

    double adverse_points = RawAdverseDistancePoints(new_price);
    if(adverse_points <= 0.0)
        adverse_points = 1.0;
    g_sl_points = adverse_points;
    ValidateCalculator();
}

void HandleEntryLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("EntryLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    ShiftCalculatorAnchor(new_price);
}

void HandleTpLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("TpLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    double points = FavorablePointsBetween(g_entry_price, new_price);
    if(points <= 0.0)
        points = 1.0;
    g_tp_points = points;
    ValidateCalculator();
}

void HandleSlLineDrag()
{
    double new_price = ObjectGetDouble(0, Obj("SlLine"), OBJPROP_PRICE, 0);
    if(new_price <= 0.0)
        return;

    double adverse_points = RawAdverseDistancePoints(new_price);
    if(adverse_points <= 0.0)
        adverse_points = 1.0;
    g_sl_points = adverse_points;
    ValidateCalculator();
}

void LoadInputsToRuntime()
{
    g_direction = InpDirection;
    g_lot_size = InpLotSize;
    g_tp_points = InpTakeProfitPoints;
    g_sl_points = InpStopLossPoints;
}

int OnInit()
{
    g_prefix = StringFormat("%s%I64d_", CALCULATOR_OBJECT_PREFIX_ROOT, ChartID());
    LoadInputsToRuntime();
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
    ResetCalculator(true);
    RenderAll();
    return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
    DeleteCalculatorObjects();
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
        else if(sparam == Obj("BtnReset"))
        {
            ResetCalculator(true);
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
        if(sparam == Obj("ResizeTL") ||
           sparam == Obj("ResizeTR") ||
           sparam == Obj("ResizeBL") ||
           sparam == Obj("ResizeBR"))
        {
            HandlePanelResizeDrag(sparam);
        }
        else if(sparam == Obj("EntryLine"))
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

        RenderAll();
        return;
    }

    if(id == CHARTEVENT_OBJECT_ENDEDIT)
    {
        if(sparam == Obj("EditLot"))
            ApplyDoubleEdit(sparam, g_lot_size, true);
        else if(sparam == Obj("EditTp"))
            ApplyDoubleEdit(sparam, g_tp_points, true);
        else if(sparam == Obj("EditSl"))
            ApplyDoubleEdit(sparam, g_sl_points, true);

        RenderAll();
        return;
    }
}
