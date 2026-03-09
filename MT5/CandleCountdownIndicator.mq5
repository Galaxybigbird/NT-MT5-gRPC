#property indicator_chart_window
#property indicator_buffers 0
#property indicator_plots 0
#property strict

input color CountdownColor = clrWhite;
input int   CountdownFontSize = 12;
input string CountdownFont = "Arial";
input int   CountdownXOffset = 12;

const string CandleCountdownObjName = "ACHM_CandleCountdown_IND";
datetime g_last_candle_countdown_update = 0;

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

    if(ObjectFind(0, CandleCountdownObjName) < 0)
    {
        ObjectCreate(0, CandleCountdownObjName, OBJ_LABEL, 0, 0, 0);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_CORNER, CORNER_LEFT_UPPER);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_ANCHOR, ANCHOR_LEFT);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_FONTSIZE, CountdownFontSize);
        ObjectSetString(0, CandleCountdownObjName, OBJPROP_FONT, CountdownFont);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_COLOR, CountdownColor);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_SELECTABLE, false);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_HIDDEN, true);
        ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_BACK, false);
    }

    ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_XDISTANCE, x + CountdownXOffset);
    ObjectSetInteger(0, CandleCountdownObjName, OBJPROP_YDISTANCE, y);
    ObjectSetString(0, CandleCountdownObjName, OBJPROP_TEXT, text);
}

int OnInit()
{
    EventSetTimer(1);
    UpdateCandleCountdown();
    return INIT_SUCCEEDED;
}

void OnDeinit(const int reason)
{
    EventKillTimer();
    ObjectDelete(0, CandleCountdownObjName);
}

void OnTimer()
{
    UpdateCandleCountdown();
}

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
    UpdateCandleCountdown();
    return rates_total;
}
