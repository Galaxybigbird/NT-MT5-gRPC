using System;
using System.Collections.Generic;
using System.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;

namespace NinjaTrader.NinjaScript.AddOns
{
    /// <summary>
    /// Strategies that participate in bridge-driven synchronization implement this interface so the AddOn
    /// can route remote partial / close instructions back into NinjaTrader managed exits.
    /// </summary>
    public interface ITradeSyncParticipant
    {
        void HandleTradeSyncPartial(string tradeId, int quantityToExit);

        void HandleTradeSyncClose(string tradeId);
    }

    /// <summary>
    /// Central coordinator that maps strategy generated trade_id values to their owning strategies,
    /// tracks per-trade state, and acts as the single ingress / egress point for bridge lifecycle messages.
    /// </summary>
    public class TradeSyncService
    {
        public class TradeRecord
        {
            public string TradeId;
            public StrategyBase Strategy;
            public string Instrument;
            public MarketPosition Side;
            public int NtQuantity;
            public int RemainingQuantity;
            public string AccountName;
            public DateTime OpenedAtUtc;
            public DateTime LastUpdateUtc;
            public long LastSeq;
            public long Epoch;
            public double NtPointsPer1kLoss;
            public double EntryPrice;
            public int LastDeltaQuantity;
        }

        private readonly MultiStratManager owner;
        private readonly object gate = new object();
        private readonly Dictionary<string, TradeRecord> tradesById = new Dictionary<string, TradeRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<StrategyBase, HashSet<string>> tradesByStrategy = new Dictionary<StrategyBase, HashSet<string>>();
        private readonly long epoch;

        public TradeSyncService(MultiStratManager owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            // Monotonic epoch so reconnects can reject stale bridge commands.
            epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public long Epoch
        {
            get { return epoch; }
        }

        public void Shutdown(string reason)
        {
            lock (gate)
            {
                tradesById.Clear();
                tradesByStrategy.Clear();
            }
            if (!string.IsNullOrWhiteSpace(reason))
            {
                owner.LogInfo("TRADE_SYNC", $"TradeSyncService shutdown: {reason}");
            }
        }

        public void RegisterStrategy(StrategyBase strategy)
        {
            if (strategy == null)
                return;

            lock (gate)
            {
                if (!tradesByStrategy.ContainsKey(strategy))
                    tradesByStrategy[strategy] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public void UnregisterStrategy(StrategyBase strategy)
        {
            if (strategy == null)
                return;

            lock (gate)
            {
                HashSet<string> ids;
                if (tradesByStrategy.TryGetValue(strategy, out ids))
                {
                    foreach (var tradeId in ids)
                        tradesById.Remove(tradeId);
                }
                tradesByStrategy.Remove(strategy);
            }
        }

        public void PublishOpen(StrategyBase strategy, string tradeId, string instrument, MarketPosition side, int quantity, string accountName, double pointsPer1kLoss, double entryPrice)
        {
            if (strategy == null || string.IsNullOrWhiteSpace(tradeId) || quantity <= 0)
                return;

            TradeRecord openRecord = new TradeRecord
            {
                TradeId = tradeId,
                Strategy = strategy,
                Instrument = instrument ?? string.Empty,
                Side = side,
                NtQuantity = quantity,
                RemainingQuantity = quantity,
                AccountName = accountName ?? string.Empty,
                OpenedAtUtc = DateTime.UtcNow,
                LastUpdateUtc = DateTime.UtcNow,
                LastSeq = 0,
                Epoch = epoch,
                NtPointsPer1kLoss = pointsPer1kLoss,
                EntryPrice = entryPrice
            };

            TradeRecord snapshot;
            lock (gate)
            {
                HashSet<string> ids;
                if (!tradesByStrategy.TryGetValue(strategy, out ids))
                {
                    ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    tradesByStrategy[strategy] = ids;
                }
                ids.Add(tradeId);
                tradesById[tradeId] = openRecord;
                openRecord.LastSeq++;
                snapshot = CloneRecord(openRecord);
            }

            owner.LogInfo("TRADE_SYNC", $"OPEN published for trade_id={tradeId} qty={quantity} side={side} instrument={instrument}", tradeId, tradeId);

            owner.PublishLifecycleToBridge(snapshot, "OPEN");
            owner.OnStrategyTradeOpened(snapshot);
        }

        public void PublishPartial(StrategyBase strategy, string tradeId, int remainingQuantity)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            TradeRecord snapshot = null;
            int delta = 0;
            lock (gate)
            {
                TradeRecord record;
                if (tradesById.TryGetValue(tradeId, out record))
                {
                    int previous = record.RemainingQuantity;
                    record.RemainingQuantity = Math.Max(remainingQuantity, 0);
                    record.LastUpdateUtc = DateTime.UtcNow;
                    record.LastSeq++;
                    delta = Math.Max(0, previous - record.RemainingQuantity);
                    snapshot = CloneRecord(record);
                    snapshot.LastDeltaQuantity = delta;
                }
            }

            owner.LogInfo("TRADE_SYNC", $"PARTIAL published for trade_id={tradeId} remaining={remainingQuantity}", tradeId, tradeId);

            if (snapshot != null)
            {
                owner.PublishLifecycleToBridge(snapshot, "PARTIAL");
                if (delta > 0)
                    owner.OnStrategyTradePartiallyClosed(snapshot, delta);
            }
        }

        public void PublishClosed(StrategyBase strategy, string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            TradeRecord snapshot = null;
            int delta = 0;
            lock (gate)
            {
                TradeRecord record;
                if (tradesById.TryGetValue(tradeId, out record))
                {
                    int previous = record.RemainingQuantity;
                    record.RemainingQuantity = 0;
                    record.LastUpdateUtc = DateTime.UtcNow;
                    record.LastSeq++;
                    delta = Math.Max(0, previous);
                    snapshot = CloneRecord(record);
                    snapshot.LastDeltaQuantity = delta;

                    HashSet<string> ids;
                    if (record.Strategy != null && tradesByStrategy.TryGetValue(record.Strategy, out ids))
                        ids.Remove(tradeId);
                }
                tradesById.Remove(tradeId);
            }

            owner.LogInfo("TRADE_SYNC", $"CLOSED published for trade_id={tradeId}", tradeId, tradeId);

            if (snapshot != null)
            {
                owner.PublishLifecycleToBridge(snapshot, "CLOSED");
                owner.OnStrategyTradeClosed(snapshot, delta);
            }
        }

        public bool TryGetTrade(string tradeId, out TradeRecord record)
        {
            lock (gate)
            {
                return tradesById.TryGetValue(tradeId, out record);
            }
        }

        private static TradeRecord CloneRecord(TradeRecord source)
        {
            if (source == null)
                return null;

            return new TradeRecord
            {
                TradeId = source.TradeId,
                Strategy = source.Strategy,
                Instrument = source.Instrument,
                Side = source.Side,
                NtQuantity = source.NtQuantity,
                RemainingQuantity = source.RemainingQuantity,
                AccountName = source.AccountName,
                OpenedAtUtc = source.OpenedAtUtc,
                LastUpdateUtc = source.LastUpdateUtc,
                LastSeq = source.LastSeq,
                Epoch = source.Epoch,
                NtPointsPer1kLoss = source.NtPointsPer1kLoss,
                EntryPrice = source.EntryPrice,
                LastDeltaQuantity = source.LastDeltaQuantity
            };
        }

        public void HandleBridgePartial(string tradeId, int quantityToExit)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || quantityToExit <= 0)
                return;

            TradeRecord record;
            lock (gate)
            {
                if (!tradesById.TryGetValue(tradeId, out record))
                    return;
            }

            if (record.Strategy is ITradeSyncParticipant participant)
            {
                participant.HandleTradeSyncPartial(tradeId, quantityToExit);
            }
            else
            {
                string strategyName = "<unknown>";
                if (record.Strategy != null && !string.IsNullOrEmpty(record.Strategy.Name))
                    strategyName = record.Strategy.Name;
                owner.LogWarn("TRADE_SYNC", string.Format("Strategy {0} missing ITradeSyncParticipant implementation for trade_id {1}", strategyName, tradeId), tradeId, tradeId);
            }
        }

        public void HandleBridgeClosed(string tradeId)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            TradeRecord record;
            lock (gate)
            {
                if (!tradesById.TryGetValue(tradeId, out record))
                    return;
            }

            if (record.Strategy is ITradeSyncParticipant participant)
            {
                participant.HandleTradeSyncClose(tradeId);
            }
            else
            {
                string strategyName = "<unknown>";
                if (record.Strategy != null && !string.IsNullOrEmpty(record.Strategy.Name))
                    strategyName = record.Strategy.Name;
                owner.LogWarn("TRADE_SYNC", string.Format("Strategy {0} missing ITradeSyncParticipant implementation for CLOSE of trade_id {1}", strategyName, tradeId), tradeId, tradeId);
            }
        }
    }
}
