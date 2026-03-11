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
        public bool IsManualEntry;
        public string InstrumentName;
        public string AccountName;
        public double NtPointsPer1kLoss;
        public double EntryPrice;
        public double LastStopPrice;
        public double InitialStopPrice;
        public double ActiveStopPrice;
        public double LastTrailingStopPrice;
        public double LastTargetPrice;
        public bool HasWorkingStop;
        public bool TrailingActive;
        public string LastStopSource;
        public DateTime LastStopUpdatedUtc;
        public bool PendingClosePublish;
        public DateTime EntryTimeUtc;
        public string EntryContext;
        public string EntryReason;
        public DateTime EntrySignalTime;
        public double EntryConditionPrev;
        public double EntryConditionNow;
        public bool EntryTrendAllowed;
        public bool EntryRawBuySignal;
        public bool EntryRawSellSignal;
        public double EntryRsiValue;
        public double EntryAtrFilterValue;
        public double EntryAtrMaValue;
        public double EntryLine;
        public double EntrySlLine;
        public double EntryTp1Line;
        public double EntryTp2Line;
        public double EntryTp3Line;
        public string LastExitReason;
        public double MaxFavorablePrice;
        public double MaxAdversePrice;
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

