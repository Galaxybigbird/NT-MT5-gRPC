#property copyright ""
#property link      ""
#property version   "1.02"
#property strict
#property description "On-chart calculator that proposes evenly spaced partial closes to cap loss"

input group "===== Planner Inputs =====";
input double Planner_EntryLots            = 0.16;   // Starting lot size to analyze
input double Planner_StopDistancePoints   = 4900;   // Stop distance (points from entry)
input double Planner_MaxLoss              = 175.0;  // Desired max total loss (account currency)
input int    Planner_MaxClosures          = 25;     // Maximum closures to search for

input group "===== Advanced Trim Options =====";
input bool   Planner_EnableInitialTrim    = false;  // Kick off scale-out with a custom partial
input double Planner_FirstTrimDistancePts = 800;    // Distance (pts) for the first trim
input double Planner_FirstTrimRatio       = 0.50;   // Portion of position closed on the first trim (0..1)
input bool   Planner_EnableAdaptiveCompression = false; // Apply spacing compression multiplier to remaining tiers
input double Planner_CompressionFactor    = 0.75;   // Multiplier (<1 tightens spacing); ignored if adaptive compression disabled

input group "===== Planner Output Options =====";
input bool   Planner_LogDetails           = true;   // Print the summary to the Experts log
input bool   Planner_ShowOnChart          = true;   // Write the summary to the chart comment
input bool   Planner_DrawLevels           = true;   // Draw horizontal reference lines for closure levels
input int    Planner_LevelPreviewCount    = 6;      // Number of closure levels to list explicitly

//+------------------------------------------------------------------+
//| Helper: append text line to accumulator                          |
//+------------------------------------------------------------------+
void AppendLine(string &dest, const string text)
{
    if(dest == "")
        dest = text;
    else
        dest = dest + "\n" + text;
}

//+------------------------------------------------------------------+
//| Helper: compute per-point value for 1 lot                         |
//+------------------------------------------------------------------+
double PointValuePerLot()
{
    double tickValue = 0.0;
    double tickSize  = 0.0;
    double pointSize = 0.0;

    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_VALUE, tickValue))
        return 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_TRADE_TICK_SIZE, tickSize))
        return 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_POINT, pointSize))
        return 0.0;

    if(tickValue <= 0.0 || tickSize <= 0.0 || pointSize <= 0.0)
        return 0.0;

    return tickValue * (pointSize / tickSize);
}

//+------------------------------------------------------------------+
//| Helper: normalize volume to broker step                           |
//+------------------------------------------------------------------+
double NormalizeVolumeToStep(double volume)
{
    double step = 0.0;
    if(!SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_STEP, step))
        return volume;
    if(step <= 0.0)
        return volume;

    double rounded = MathRound(volume / step) * step;
    // Keep a sane precision (up to 8 decimals)
    return NormalizeDouble(rounded, 8);
}

//+------------------------------------------------------------------+
//| Helper: create closure level preview string                       |
//+------------------------------------------------------------------+
string BuildLevelPreview(int closures, double pointIncrement, double pointSize)
{
    if(closures <= 0 || pointIncrement <= 0.0 || pointSize <= 0.0)
        return "(not applicable)";

    int previewCount = MathMin(closures, Planner_LevelPreviewCount);
    if(previewCount <= 0)
        previewCount = 1;

    string preview = "";
    for(int i = 1; i <= previewCount; i++)
    {
        double distPts = pointIncrement * i;
        double distPrice = distPts * pointSize;
        string chunk = StringFormat("%d: %.1f pts (~%.*f)",
                                    i,
                                    distPts,
                                    (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS),
                                    distPrice);
        if(preview == "")
            preview = chunk;
        else
            preview = preview + ", " + chunk;
    }

    if(previewCount < closures)
        preview = preview + StringFormat(", … %d total levels", closures);

    return preview;
}


double ClampDouble(double value, double minValue, double maxValue)
{
    if(value < minValue)
        return minValue;
    if(value > maxValue)
        return maxValue;
    return value;
}

string BuildAdvancedPreview(double &distances[], double &lots[])
{
    int total = ArraySize(distances);
    if(total <= 0)
        return "(not applicable)";

    int previewCount = MathMin(total, Planner_LevelPreviewCount);
    if(previewCount <= 0)
        previewCount = 1;

    string preview = "";
    int digits = (int)SymbolInfoInteger(_Symbol, SYMBOL_DIGITS);
    for(int i = 0; i < previewCount; i++)
    {
        double distPts = distances[i];
        double priceLevel = distPts * _Point;
        double lotChunk = (i < ArraySize(lots)) ? lots[i] : 0.0;
        string chunk = StringFormat("%d: %.1f pts (~%.*f) close %.4f lots",
                                    i + 1,
                                    distPts,
                                    digits,
                                    priceLevel,
                                    lotChunk);
        if(preview == "")
            preview = chunk;
        else
            preview = preview + ", " + chunk;
    }

    if(previewCount < total)
        preview = preview + StringFormat(", … %d total closures", total);

    return preview;
}

double g_lastPlanDistances[];
double g_lastPlanLots[];
double g_lastAnchorPrice = 0.0;
bool   g_hasPlan = false;
string g_drawnObjects[];

void ClearPlannerLines()
{
    int count = ArraySize(g_drawnObjects);
    for(int i = 0; i < count; i++)
    {
        if(g_drawnObjects[i] != "")
            ObjectDelete(0, g_drawnObjects[i]);
    }
    ArrayResize(g_drawnObjects, 0);
}

void RegisterPlannerObject(const string name)
{
    int idx = ArraySize(g_drawnObjects);
    ArrayResize(g_drawnObjects, idx + 1);
    g_drawnObjects[idx] = name;
}

void StorePlanArrays(double anchorPrice, double &distances[], double &lots[])
{
    ClearPlannerLines();
    g_hasPlan = false;
    g_lastAnchorPrice = anchorPrice;
    ArrayResize(g_lastPlanDistances, 0);
    ArrayResize(g_lastPlanLots, 0);

    int total = ArraySize(distances);
    if(total <= 0)
        return;

    ArrayResize(g_lastPlanDistances, total);
    ArrayResize(g_lastPlanLots, total);
    for(int i = 0; i < total; i++)
    {
        g_lastPlanDistances[i] = distances[i];
        g_lastPlanLots[i] = (i < ArraySize(lots)) ? lots[i] : 0.0;
    }
    g_hasPlan = true;
}

void DrawPlannerLines()
{
    ClearPlannerLines();
    if(!Planner_DrawLevels || !g_hasPlan)
        return;

    double anchorPrice = g_lastAnchorPrice;
    if(anchorPrice <= 0.0)
        anchorPrice = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    if(anchorPrice <= 0.0)
        anchorPrice = iClose(_Symbol, PERIOD_CURRENT, 0);
    if(anchorPrice <= 0.0)
        return;

    int total = ArraySize(g_lastPlanDistances);
    if(total <= 0)
        return;

    int tfSeconds = PeriodSeconds((ENUM_TIMEFRAMES)Period());
    if(tfSeconds <= 0)
        tfSeconds = 60;

    datetime baseTime = TimeCurrent();
    if(baseTime <= 0)
        baseTime = iTime(_Symbol, PERIOD_CURRENT, 0);

    for(int i = 0; i < total; i++)
    {
        double levelPrice = anchorPrice - g_lastPlanDistances[i] * _Point;
        string lineName = StringFormat("PlannerLine_%d", i);
        ObjectCreate(0, lineName, OBJ_HLINE, 0, 0, levelPrice);
        ObjectSetInteger(0, lineName, OBJPROP_COLOR, clrDodgerBlue);
        ObjectSetInteger(0, lineName, OBJPROP_STYLE, STYLE_DOT);
        ObjectSetInteger(0, lineName, OBJPROP_WIDTH, 1);
        RegisterPlannerObject(lineName);

        string labelName = StringFormat("PlannerLabel_%d", i);
        datetime labelTime = baseTime + (i + 1) * tfSeconds;
        ObjectCreate(0, labelName, OBJ_TEXT, 0, labelTime, levelPrice);
        string labelText = StringFormat("#%d: close %.4f", i + 1, g_lastPlanLots[i]);
        ObjectSetString(0, labelName, OBJPROP_TEXT, labelText);
        ObjectSetInteger(0, labelName, OBJPROP_COLOR, clrDodgerBlue);
        ObjectSetInteger(0, labelName, OBJPROP_ANCHOR, ANCHOR_LEFT);
        RegisterPlannerObject(labelName);
    }
}

bool SimulateAdvancedPlan(int totalClosures,
                          double pointValue,
                          double brokerMinLot,
                          double &lossOut,
                          double &firstDistanceOut,
                          double &firstLotsOut,
                          double &restSegmentDistanceOut,
                          double &restLotsOut,
                          double &finalDistanceOut,
                          double &distances[],
                          double &lots[])
{
    ArrayResize(distances, 0);
    ArrayResize(lots, 0);

    if(totalClosures <= 0)
        return false;

    lossOut = 0.0;
    firstDistanceOut = 0.0;
    firstLotsOut = 0.0;
    restSegmentDistanceOut = 0.0;
    restLotsOut = 0.0;
    finalDistanceOut = 0.0;

    double currentLots = Planner_EntryLots;
    double distanceCovered = 0.0;

    int closureSlots = totalClosures;
    ArrayResize(distances, closureSlots);
    ArrayResize(lots, closureSlots);
    int idx = 0;

    int restClosures = totalClosures;

    if(Planner_EnableInitialTrim)
    {
        if(restClosures <= 0)
            return false;

        double ratio = ClampDouble(Planner_FirstTrimRatio, 0.0, 1.0);
        if(ratio > 1.0 - 1e-6)
            ratio = 1.0;
        double firstDistance = Planner_FirstTrimDistancePts;
        if(firstDistance <= 0.0 || firstDistance >= Planner_StopDistancePoints - 1e-6)
            return false;

        lossOut += currentLots * pointValue * firstDistance;
        distanceCovered += firstDistance;
        double closedLots = currentLots * ratio;
        currentLots -= closedLots;
        firstDistanceOut = firstDistance;
        firstLotsOut = closedLots;

        distances[idx] = distanceCovered;
        lots[idx] = closedLots;
        idx++;
        restClosures--;
    }

    if(restClosures < 0)
        restClosures = 0;

    double remainingDistance = Planner_StopDistancePoints - distanceCovered;
    if(remainingDistance < -1e-6)
        return false;

    double effectiveDistance = remainingDistance;
    if(Planner_EnableAdaptiveCompression)
    {
        double factor = ClampDouble(Planner_CompressionFactor, 0.05, 1.0);
        effectiveDistance = remainingDistance * factor;
    }

    if(restClosures > 0)
    {
        if(currentLots <= 0.0)
        {
            ArrayResize(distances, idx);
            ArrayResize(lots, idx);
            finalDistanceOut = distanceCovered;
            return true;
        }

        double minLot = (brokerMinLot > 0.0) ? brokerMinLot : 0.0;
        if(minLot > 0.0)
        {
            int maxFeasible = (int)MathFloor((currentLots + 1e-8) / minLot);
            if(maxFeasible <= 0)
                return false;
            if(restClosures > maxFeasible)
                restClosures = maxFeasible;
        }

        if(restClosures <= 0)
        {
            ArrayResize(distances, idx);
            ArrayResize(lots, idx);
            finalDistanceOut = distanceCovered;
            return true;
        }

        double segment = effectiveDistance / restClosures;
        restSegmentDistanceOut = segment;
        double totalRestLots = currentLots;
        double uniformLots = totalRestLots / restClosures;
        restLotsOut = (minLot > 0.0 ? MathMax(uniformLots, minLot) : uniformLots);

        for(int i = 0; i < restClosures; i++)
        {
            lossOut += currentLots * pointValue * segment;
            distanceCovered += segment;
            double toClose = uniformLots;
            if(minLot > 0.0)
                toClose = MathMax(toClose, minLot);
            int slotsLeft = restClosures - i;
            if(minLot > 0.0 && slotsLeft > 1)
            {
                double minReserved = minLot * (slotsLeft - 1);
                double maxAvailable = currentLots - minReserved;
                if(toClose > maxAvailable)
                    toClose = maxAvailable;
            }
            if(toClose <= 0.0 || toClose > currentLots)
                toClose = currentLots;
            if(i == restClosures - 1)
                toClose = currentLots;
            currentLots -= toClose;
            distances[idx] = distanceCovered;
            lots[idx] = toClose;
            idx++;
        }
    }

    ArrayResize(distances, idx);
    ArrayResize(lots, idx);
    finalDistanceOut = distanceCovered;

    if(currentLots > 1e-6)
        return false; // not fully flattened

    return true;
}

//+------------------------------------------------------------------+
//| Core planner routine                                              |
//+------------------------------------------------------------------+
void EvaluatePlanner()
{
    string summary = "";
    AppendLine(summary, "===== Elastic Partial Closure Planner =====");
    AppendLine(summary, StringFormat("Symbol: %s", _Symbol));
    AppendLine(summary, StringFormat("Entry lots: %.4f", Planner_EntryLots));
    AppendLine(summary, StringFormat("Stop distance: %.1f pts (~%.5f price)",
                                     Planner_StopDistancePoints,
                                     Planner_StopDistancePoints * _Point));
    AppendLine(summary, StringFormat("Target max loss: %.2f", Planner_MaxLoss));

    if(Planner_EntryLots <= 0.0)
    {
        AppendLine(summary, "⚠ Entry lots must be greater than zero.");
        PublishSummary(summary);
        return;
    }

    double anchorPrice = SymbolInfoDouble(_Symbol, SYMBOL_BID);
    if(anchorPrice <= 0.0)
        anchorPrice = SymbolInfoDouble(_Symbol, SYMBOL_LAST);
    if(anchorPrice <= 0.0)
        anchorPrice = iClose(_Symbol, PERIOD_CURRENT, 0);
    if(anchorPrice > 0.0)
        AppendLine(summary, StringFormat("Visual anchor price: %.5f (current bid/close)", anchorPrice));

    if(Planner_StopDistancePoints <= 0.0)
    {
        AppendLine(summary, "⚠ Stop distance must be greater than zero.");
        PublishSummary(summary);
        return;
    }

    if(Planner_MaxLoss <= 0.0)
    {
        AppendLine(summary, "⚠ Max loss must be greater than zero.");
        PublishSummary(summary);
        return;
    }

    int maxClosures = Planner_MaxClosures;
    if(maxClosures < 1)
        maxClosures = 1;

    double pointValue = PointValuePerLot();
    if(pointValue <= 0.0)
    {
        AppendLine(summary, "⚠ Unable to derive point value for this symbol.");
        PublishSummary(summary);
        return;
    }

    double brokerMinLot = 0.0;
    SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, brokerMinLot);

    double baseLoss = Planner_EntryLots * pointValue * Planner_StopDistancePoints;
    AppendLine(summary, StringFormat("Base loss w/o scaling: %.2f", baseLoss));

    if(baseLoss <= Planner_MaxLoss + 1e-6)
    {
        AppendLine(summary, "No partial closures required—base loss already <= target.");
        AppendLine(summary, StringFormat("Projected loss: %.2f", baseLoss));
        PublishSummary(summary);
        return;
    }

    bool advancedMode = (Planner_EnableInitialTrim || Planner_EnableAdaptiveCompression);
    if(!advancedMode)
    {
        int searchLimit = maxClosures;
        if(brokerMinLot > 0.0)
        {
            int feasible = (int)MathFloor((Planner_EntryLots + 1e-8) / brokerMinLot);
            if(feasible < 1)
                feasible = 1;
            if(searchLimit > feasible)
                searchLimit = feasible;
        }
        if(searchLimit < 1)
            searchLimit = 1;

        int    bestClosureCount = -1;
        double bestLossEstimate = baseLoss;
        const double tolerance = 1e-6;

        for(int n = searchLimit; n >= 1; n--)
        {
            double candidateLoss = baseLoss * (n + 1.0) / (2.0 * n);
            if(candidateLoss <= Planner_MaxLoss + tolerance)
            {
                bestClosureCount = n;
                bestLossEstimate = candidateLoss;
                break;
            }
        }

        if(bestClosureCount < 0)
        {
            double cappedLoss = baseLoss * (maxClosures + 1.0) / (2.0 * maxClosures);
            AppendLine(summary, StringFormat("Need more than %d closures to reach %.2f.\nWith %d closures the loss would still be %.2f.",
                                             maxClosures,
                                             Planner_MaxLoss,
                                             maxClosures,
                                             cappedLoss));
            PublishSummary(summary);
            return;
        }

        double lotsPerClosure = Planner_EntryLots / bestClosureCount;
        double normalizedLots = NormalizeVolumeToStep(lotsPerClosure);
        double minVolume = 0.0;
        SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, minVolume);
        if(minVolume > 0.0 && normalizedLots < minVolume - 1e-8)
            normalizedLots = minVolume;
        double pointIncrement = Planner_StopDistancePoints / bestClosureCount;
        double priceIncrement = pointIncrement * _Point;

        AppendLine(summary, StringFormat("Closures needed: %d", bestClosureCount));
        AppendLine(summary, StringFormat("Lots per closure: raw %.4f | step-adjusted %.4f",
                                         lotsPerClosure,
                                         normalizedLots));
        AppendLine(summary, StringFormat("Distance between closures: %.1f pts (~%.5f price)",
                                         pointIncrement,
                                         priceIncrement));
        AppendLine(summary, StringFormat("Projected total loss after final close: %.2f", bestLossEstimate));

        string preview = BuildLevelPreview(bestClosureCount, pointIncrement, _Point);
        AppendLine(summary, StringFormat("Closure levels preview: %s", preview));

        double simpleDistances[];
        double simpleLots[];
        ArrayResize(simpleDistances, bestClosureCount);
        ArrayResize(simpleLots, bestClosureCount);
        double remainingLots = Planner_EntryLots;
        for(int i = 0; i < bestClosureCount; i++)
        {
            simpleDistances[i] = pointIncrement * (i + 1);
            double targetLots = lotsPerClosure;
            if(i == bestClosureCount - 1 || targetLots > remainingLots)
                targetLots = remainingLots;

            double chunk = NormalizeVolumeToStep(targetLots);
            if(minVolume > 0.0 && chunk < minVolume - 1e-8)
                chunk = minVolume;
            if(chunk > remainingLots)
                chunk = remainingLots;

            simpleLots[i] = chunk;
            remainingLots -= chunk;
        }
        if(remainingLots > 1e-8 && bestClosureCount > 0)
        {
            int tail = bestClosureCount - 1;
            double adjusted = simpleLots[tail] + remainingLots;
            simpleLots[tail] = NormalizeVolumeToStep(adjusted);
            if(minVolume > 0.0 && simpleLots[tail] < minVolume - 1e-8)
                simpleLots[tail] = minVolume;
            if(simpleLots[tail] > Planner_EntryLots)
                simpleLots[tail] = Planner_EntryLots;
            remainingLots = 0.0;
        }
        StorePlanArrays(anchorPrice, simpleDistances, simpleLots);
        DrawPlannerLines();

        PublishSummary(summary);
        return;
    }

    // Advanced planning with initial trim or adaptive spacing
    if(Planner_EnableInitialTrim)
    {
        if(Planner_FirstTrimDistancePts <= 0.0 || Planner_FirstTrimDistancePts >= Planner_StopDistancePoints - 1e-6)
        {
            AppendLine(summary, StringFormat("⚠ Invalid initial trim distance %.1f pts. It must be >0 and < stop distance %.1f.",
                                             Planner_FirstTrimDistancePts,
                                             Planner_StopDistancePoints));
            PublishSummary(summary);
            return;
        }
    }

    int bestAdvancedClosures = -1;
    double bestAdvancedLoss = baseLoss;
    double bestFirstDistance = 0.0;
    double bestFirstLots = 0.0;
    double bestRestSegment = 0.0;
    double bestRestLots = 0.0;
    double bestFinalDistance = 0.0;
    double bestDistances[];
    double bestLots[];

    const double tolerance = 1e-6;

    for(int n = maxClosures; n >= 1; n--)
    {
        double planLoss = 0.0;
        double firstDistanceOut = 0.0;
        double firstLotsOut = 0.0;
        double restSegmentOut = 0.0;
        double restLotsOut = 0.0;
        double finalDistanceOut = 0.0;
        double simDistances[];
        double simLots[];

        if(!SimulateAdvancedPlan(n,
                                 pointValue,
                                 brokerMinLot,
                                 planLoss,
                                 firstDistanceOut,
                                 firstLotsOut,
                                 restSegmentOut,
                                 restLotsOut,
                                 finalDistanceOut,
                                 simDistances,
                                 simLots))
        {
            continue;
        }

        if(planLoss <= Planner_MaxLoss + tolerance)
        {
            bestAdvancedClosures = n;
            bestAdvancedLoss = planLoss;
            bestFirstDistance = firstDistanceOut;
            bestFirstLots = firstLotsOut;
            bestRestSegment = restSegmentOut;
            bestRestLots = restLotsOut;
            bestFinalDistance = finalDistanceOut;
            ArrayResize(bestDistances, ArraySize(simDistances));
            for(int c = 0; c < ArraySize(simDistances); c++)
                bestDistances[c] = simDistances[c];
            ArrayResize(bestLots, ArraySize(simLots));
            for(int c2 = 0; c2 < ArraySize(simLots); c2++)
                bestLots[c2] = simLots[c2];
            break;
        }
    }

    if(bestAdvancedClosures < 0)
    {
        AppendLine(summary, StringFormat("Advanced plan could not reach %.2f within %d closure slots.",
                                         Planner_MaxLoss,
                                         maxClosures));
        PublishSummary(summary);
        return;
    }

    int executedClosures = ArraySize(bestDistances);
    if(executedClosures <= 0)
        executedClosures = bestAdvancedClosures;

    if(executedClosures == bestAdvancedClosures)
        AppendLine(summary, StringFormat("Advanced closures needed: %d", bestAdvancedClosures));
    else
        AppendLine(summary, StringFormat("Advanced closures executed: %d (slots requested: %d)", executedClosures, bestAdvancedClosures));

    if(Planner_EnableInitialTrim)
    {
        double normalizedFirstLots = NormalizeVolumeToStep(bestFirstLots);
        AppendLine(summary, StringFormat("Kick-off trim: %.1f pts (~%.5f price) closing %.4f lots (step-adjusted %.4f)",
                                         bestFirstDistance,
                                         bestFirstDistance * _Point,
                                         bestFirstLots,
                                         normalizedFirstLots));
        double minVolume = 0.0;
        if(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, minVolume) && minVolume > 0.0 && normalizedFirstLots < minVolume - 1e-8)
        {
            AppendLine(summary, StringFormat("⚠ Warning: first trim %.4f is below broker min lot %.4f.", normalizedFirstLots, minVolume));
        }
    }

    int restCount = executedClosures - (Planner_EnableInitialTrim ? 1 : 0);
    if(restCount < 0)
        restCount = 0;

    if(restCount > 0)
    {
        double normalizedRestLots = NormalizeVolumeToStep(bestRestLots);
        double minVolume = 0.0;
        if(SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, minVolume) && minVolume > 0.0)
        {
            if(normalizedRestLots < minVolume)
                normalizedRestLots = minVolume;
        }
        AppendLine(summary, StringFormat("Remaining %d tiers spaced %.1f pts each (closing %.4f lots | broker-adjusted %.4f).",
                                         restCount,
                                         bestRestSegment,
                                         bestRestLots,
                                         normalizedRestLots));
    }

    if(Planner_EnableAdaptiveCompression)
    {
        double factor = ClampDouble(Planner_CompressionFactor, 0.05, 1.0);
        AppendLine(summary, StringFormat("Adaptive spacing compression factor %.2f → last closure triggers at %.1f pts (stop %.1f).",
                                         factor,
                                         bestFinalDistance,
                                         Planner_StopDistancePoints));
    }
    else
    {
        AppendLine(summary, StringFormat("Last closure triggers at %.1f pts (stop %.1f).",
                                         bestFinalDistance,
                                         Planner_StopDistancePoints));
    }

    AppendLine(summary, StringFormat("Projected total loss after advanced plan: %.2f", bestAdvancedLoss));

    double minLot = 0.0;
    SymbolInfoDouble(_Symbol, SYMBOL_VOLUME_MIN, minLot);
    double displayLots[];
    ArrayResize(displayLots, ArraySize(bestLots));
    for(int i = 0; i < ArraySize(bestLots); i++)
    {
        double normalized = NormalizeVolumeToStep(bestLots[i]);
        if(minLot > 0.0 && normalized < minLot)
            normalized = minLot;
        displayLots[i] = normalized;
    }
    string finalPreview = BuildAdvancedPreview(bestDistances, displayLots);

    if(finalPreview != "")
        AppendLine(summary, StringFormat("Closure levels preview: %s", finalPreview));
    else
        AppendLine(summary, "Closure levels preview: (not available)");

    StorePlanArrays(anchorPrice, bestDistances, displayLots);
    DrawPlannerLines();

    PublishSummary(summary);
}

//+------------------------------------------------------------------+
//| Emit summary via log/comment/alert                               |
//+------------------------------------------------------------------+
void PublishSummary(const string summary)
{
    if(Planner_LogDetails)
        Print(summary);

    if(Planner_ShowOnChart)
        Comment(summary);
    else
        Comment("");
}

//+------------------------------------------------------------------+
//| Expert initialization function                                   |
//+------------------------------------------------------------------+
int OnInit()
{
    EvaluatePlanner();
    return(INIT_SUCCEEDED);
}

//+------------------------------------------------------------------+
//| Expert deinitialization function                                 |
//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
    if(Planner_ShowOnChart)
        Comment("");
    ClearPlannerLines();
}

//+------------------------------------------------------------------+
//| No-op OnTick (calculator EA)                                     |
//+------------------------------------------------------------------+
void OnTick()
{
    // Calculator EA does not trade—intentionally left blank.
}
