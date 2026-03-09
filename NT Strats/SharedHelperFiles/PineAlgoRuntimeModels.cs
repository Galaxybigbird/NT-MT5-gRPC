using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Shared
{
    internal sealed class PineTradeRuntimeState
    {
        public string TradeId;
        public string SyncTradeId;
        public string EntrySignal;
        public MarketPosition EntrySide;
        public int OriginalQuantity;
        public int RemainingQuantity;
        public bool OpenPublished;
        public bool IsSynthetic;
        public string InstrumentName;
        public string AccountName;
        public double NtPointsPer1kLoss;
        public double EntryPrice;
        public double LastStopPrice;
        public double LastTargetPrice;
        public bool PendingClosePublish;
        public DateTime EntryTimeUtc;
    }

    internal sealed class PineTradeSyncGroup
    {
        public string TradeId;
        public MarketPosition Side;
        public int TotalQuantity;
        public int LastPublishedRemaining;
        public bool OpenPublished;
        public bool ClosedPublished;
        public DateTime CreatedAtUtc;
        public readonly List<string> MemberTradeIds = new List<string>();
    }
}

