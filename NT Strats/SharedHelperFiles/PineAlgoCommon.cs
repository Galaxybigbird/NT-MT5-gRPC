using System;
using System.Collections.Generic;

namespace NinjaTrader.NinjaScript.Shared
{
    public enum PineTpSType
    {
        Atr = 0,
        Trailing = 1,
        Options = 2
    }

    public enum PineSetupType
    {
        OpenClose = 0,
        Renko = 1
    }

    public enum PineSidewaysFilterType
    {
        Atr = 0,
        Rsi = 1,
        AtrOrRsi = 2,
        AtrAndRsi = 3,
        NoFilter = 4,
        SidewaysAtrOrRsi = 5,
        SidewaysAtrAndRsi = 6
    }

    public enum PineTrailingMode
    {
        Atr = 0,
        Ticks = 1,
        Dollars = 2
    }

    public enum PineAtrTrailBehavior
    {
        Intrabar = 0,
        BarClose = 1
    }

    public enum PineAtrTrailSource
    {
        Traditional = 0,
        Dema = 1
    }

    public enum PineExternalActivationType
    {
        Ticks = 0,
        Dollars = 1
    }

    public enum PineEntryStopLossType
    {
        Atr = 0,
        DemaAtr = 1,
        MarketStructure = 2
    }

    public enum PineStructureStopModel
    {
        ChartSwingPivot = 0,
        BosChoch = 1,
        SignalTimeframeSwing = 2
    }

    public enum PineBosChochEngine
    {
        SimplifiedMql = 0,
        ClosePineParity = 1
    }

    public enum PineStructureBufferType
    {
        Ticks = 0,
        Atr = 1
    }

    public sealed class PineEvalState
    {
        public bool Valid;
        public DateTime BarTime;
        public double BarOpen;
        public double BarHigh;
        public double BarLow;
        public double BarClose;
        public bool TradeDateAllowed;
        public bool BuyEntry;
        public bool SellEntry;
        public bool LeTrigger;
        public bool SeTrigger;
        public bool BuyColor;
        public bool RibbonValid;
        public double RibbonTop;
        public double RibbonBottom;
        public double ConditionPrev;
        public double ConditionNow;
        public double EntryLine;
        public double SlLine;
        public double Tp1Line;
        public double Tp2Line;
        public double Tp3Line;
        public bool LongE;
        public bool ShortE;
        public bool LongX;
        public bool ShortX;
        public bool LongSL;
        public bool ShortSL;
        public bool LongTP1;
        public bool ShortTP1;
        public bool LongTP2;
        public bool ShortTP2;
        public bool LongTP3;
        public bool ShortTP3;
    }

    public sealed class PineSyntheticRenkoResult
    {
        public bool BuySignal;
        public bool SellSignal;
        public bool BuyColor;
        public double Open;
        public double Close;
    }

    public sealed class PinePriceBar
    {
        public DateTime Time;
        public double Open;
        public double High;
        public double Low;
        public double Close;
        public double Volume;
    }

    public static class PineAlgoMath
    {
        public static bool EqCond(double left, double right)
        {
            return Math.Abs(left - right) <= 1e-9;
        }

        public static double Truncate2(double value)
        {
            return Math.Truncate(value * 100.0) / 100.0;
        }

        public static bool CrossSeries(double srcCur, double lvlCur, double srcPrev, double lvlPrev, bool over)
        {
            if (over)
                return srcCur > lvlCur && srcPrev < lvlPrev;
            return srcCur < lvlCur && srcPrev > lvlPrev;
        }

        public static double SimpleMovingAverage(IList<double> values)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            double total = 0.0;
            for (int i = 0; i < values.Count; i++)
                total += values[i];
            return total / values.Count;
        }

        public static double ExponentialMovingAverage(IList<double> values, int length)
        {
            if (values == null || values.Count == 0)
                return 0.0;

            if (length <= 1)
                return values[0];

            double alpha = 2.0 / (length + 1.0);
            double ema = values[values.Count - 1];
            for (int i = values.Count - 2; i >= 0; i--)
                ema = alpha * values[i] + (1.0 - alpha) * ema;
            return ema;
        }

        public static List<double> BuildEmaSeries(IList<double> values, int length)
        {
            var result = new List<double>();
            if (values == null || values.Count == 0)
                return result;

            if (length <= 1)
            {
                result.AddRange(values);
                return result;
            }

            double alpha = 2.0 / (length + 1.0);
            result = new List<double>(new double[values.Count]);
            result[values.Count - 1] = values[values.Count - 1];
            for (int i = values.Count - 2; i >= 0; i--)
                result[i] = alpha * values[i] + (1.0 - alpha) * result[i + 1];

            return result;
        }

        public static double CalculateDema(IList<double> values, int length, int index)
        {
            if (values == null || values.Count == 0 || index < 0 || index >= values.Count)
                return 0.0;

            var ema1 = BuildEmaSeries(values, length);
            var ema2 = BuildEmaSeries(ema1, length);
            return (2.0 * ema1[index]) - ema2[index];
        }
    }
}
