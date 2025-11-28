using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns;

namespace NinjaTrader.NinjaScript.Shared
{
    /// <summary>
    /// Shared helper that mirrors the AddOn's DEMA-ATR trailing calculation so strategies can reuse the same logic.
    /// </summary>
    internal static class SharedDemaAtrTrailing
    {
        /// <summary>
        /// Calculates the trailing stop price using DEMA + ATR logic. Returns null if indicators are not ready.
        /// </summary>
        /// <param name="quotes">Recent bar data (oldest -> newest)</param>
        /// <param name="period">DEMA and ATR period</param>
        /// <param name="atrMultiplier">ATR multiplier for offset</param>
        /// <param name="isLong">True when trailing a long position, false for short</param>
        /// <param name="lastClose">Most recent close price (fallback if quotes empty)</param>
        /// <returns>Trailing stop price or null when insufficient data.</returns>
        public static double? CalculateTrailingStop(List<Quote> quotes, int period, double atrMultiplier, bool isLong, double lastClose)
        {
            if (quotes == null || quotes.Count == 0)
                return null;

            double? atr = IndicatorCalculator.CalculateAtr(quotes, period);
            double? dema = IndicatorCalculator.CalculateDema(quotes, period);

            if (!atr.HasValue || !dema.HasValue)
                return null;

            double offset = atr.Value * atrMultiplier;
            double basePrice = dema.Value;

            if (isLong)
                return Math.Max(0, basePrice - offset);
            else
                return Math.Max(0, basePrice + offset);
        }

        /// <summary>
        /// Builds a Quote list from the given price inputs. Expects oldest -> newest order.
        /// </summary>
        public static List<Quote> BuildQuotes(IList<double> opens, IList<double> highs, IList<double> lows, IList<double> closes, int maxCount)
        {
            var quotes = new List<Quote>();
            if (opens == null || highs == null || lows == null || closes == null)
                return quotes;

            int count = Math.Min(Math.Min(Math.Min(opens.Count, highs.Count), Math.Min(lows.Count, closes.Count)), maxCount);
            for (int i = count - 1; i >= 0; i--)
            {
                quotes.Add(new Quote
                {
                    Date = DateTime.MinValue,
                    Open = opens[i],
                    High = highs[i],
                    Low = lows[i],
                    Close = closes[i],
                    Volume = 0
                });
            }
            quotes.Reverse();
            return quotes;
        }
    }
}
