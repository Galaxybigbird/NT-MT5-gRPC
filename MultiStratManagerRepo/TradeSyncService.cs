using System;
using System.Collections.Generic;
using System.Linq;
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

    public enum RunUpUnits
    {
        Ticks,
        Dollars
    }

    public class RunUpConfig
    {
        public bool Enabled { get; set; }
        public RunUpUnits DistanceUnits { get; set; }
        public double DistanceValue { get; set; }
        public RunUpUnits IncrementUnits { get; set; }
        public double IncrementValue { get; set; }
    }

    public interface IRunUpParticipant
    {
        void HandleRunUpStart(string tradeId, double anchorPrice, RunUpConfig config);
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
            public bool ManualStopOverride;
            public bool ManualTargetOverride;
            public bool AggregateEntry;
            public bool IsScaleInTrade;
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

        public bool IsReady
        {
            get { return owner != null && owner.IsGrpcReady; }
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

        public int ClearTradesForAccount(string accountName, string reason)
        {
            if (string.IsNullOrWhiteSpace(accountName))
                return 0;

            string acct = accountName.Trim();
            var removed = new List<TradeRecord>();

            lock (gate)
            {
                var idsToRemove = tradesById.Values
                    .Where(r => r != null &&
                                !string.IsNullOrWhiteSpace(r.TradeId) &&
                                string.Equals((r.AccountName ?? string.Empty).Trim(), acct, StringComparison.OrdinalIgnoreCase))
                    .Select(r => r.TradeId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var tradeId in idsToRemove)
                {
                    TradeRecord record;
                    if (!tradesById.TryGetValue(tradeId, out record))
                        continue;

                    removed.Add(CloneRecord(record));
                    tradesById.Remove(tradeId);

                    HashSet<string> ids;
                    if (record.Strategy != null && tradesByStrategy.TryGetValue(record.Strategy, out ids))
                    {
                        ids.Remove(tradeId);
                        if (ids.Count == 0)
                            tradesByStrategy.Remove(record.Strategy);
                    }
                }
            }

            foreach (var rec in removed)
            {
                if (rec == null)
                    continue;

                double deltaQty = rec.RemainingQuantity > 0 ? rec.RemainingQuantity : rec.NtQuantity;
                if (Math.Abs(deltaQty) < 1e-9)
                    continue;

                owner?.AdjustExposure(rec.Strategy, rec.AccountName, rec.Instrument, -GetSignedQuantity(rec.Side, deltaQty));
            }

            if (removed.Count > 0)
            {
                owner.LogInfo("TRADE_SYNC", $"Cleared {removed.Count} trade(s) for acct={acct} ({reason})");
            }

            return removed.Count;
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

            owner?.ClearExposureForStrategy(strategy);
        }

        public void PublishOpen(StrategyBase strategy, string tradeId, string instrument, MarketPosition side, int quantity, string accountName, double pointsPer1kLoss, double entryPrice, bool aggregateEntry = false, bool isScaleInTrade = false)
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
                EntryPrice = entryPrice,
                AggregateEntry = aggregateEntry,
                IsScaleInTrade = isScaleInTrade
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

            owner?.AdjustExposure(strategy, accountName, instrument, GetSignedQuantity(side, quantity));

            owner.LogInfo("TRADE_SYNC", $"OPEN published for trade_id={tradeId} qty={quantity} side={side} instrument={instrument}", tradeId, tradeId);

            owner.PublishLifecycleToBridge(snapshot, "OPEN");
            owner.OnStrategyTradeOpened(snapshot);
        }

        public void PublishPartial(StrategyBase strategy, string tradeId, int remainingQuantity)
        {
            if (string.IsNullOrWhiteSpace(tradeId))
                return;

            TradeRecord snapshot = null;
            TradeRecord record = null;
            int delta = 0;
            lock (gate)
            {
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

            if (record != null && delta > 0)
                owner?.AdjustExposure(strategy, record.AccountName, record.Instrument, -GetSignedQuantity(record.Side, delta));

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
            TradeRecord record = null;
            int delta = 0;
            lock (gate)
            {
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

            if (record != null)
            {
                double deltaQty = delta > 0 ? delta : record.NtQuantity;
                owner?.AdjustExposure(strategy, record.AccountName, record.Instrument, -GetSignedQuantity(record.Side, deltaQty));
            }

            owner.LogInfo("TRADE_SYNC", $"CLOSED published for trade_id={tradeId}", tradeId, tradeId);

            if (snapshot != null)
            {
                owner.PublishLifecycleToBridge(snapshot, "CLOSED");
                owner.OnStrategyTradeClosed(snapshot, delta);
            }
        }

        public void PublishManualOverride(StrategyBase strategy, string tradeId, bool? stopLocked, bool? targetLocked)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || (!stopLocked.HasValue && !targetLocked.HasValue))
                return;

            bool notify = false;
            lock (gate)
            {
                TradeRecord record;
                if (!tradesById.TryGetValue(tradeId, out record))
                    return;

                if (stopLocked.HasValue && record.ManualStopOverride != stopLocked.Value)
                {
                    record.ManualStopOverride = stopLocked.Value;
                    notify = true;
                }

                if (targetLocked.HasValue && record.ManualTargetOverride != targetLocked.Value)
                {
                    record.ManualTargetOverride = targetLocked.Value;
                    notify = true;
                }
            }

            if (!notify)
                return;

            string stopText = stopLocked.HasValue ? stopLocked.Value.ToString() : "<no-change>";
            string targetText = targetLocked.HasValue ? targetLocked.Value.ToString() : "<no-change>";
            owner.LogInfo("TRAILING_MANUAL_OVERRIDE", $"trade_id={tradeId} stopLocked={stopText} targetLocked={targetText}", tradeId, tradeId);
            owner.NotifyManualOverride(tradeId, stopLocked, targetLocked);
        }

        public bool TryGetTrade(string tradeId, out TradeRecord record)
        {
            lock (gate)
            {
                return tradesById.TryGetValue(tradeId, out record);
            }
        }

        public List<TradeRecord> GetOpenTradesSnapshot()
        {
            lock (gate)
            {
                var snapshot = new List<TradeRecord>(tradesById.Count);
                foreach (var kvp in tradesById)
                {
                    var clone = CloneRecord(kvp.Value);
                    if (clone != null)
                        snapshot.Add(clone);
                }
                return snapshot;
            }
        }

        private static double GetSignedQuantity(MarketPosition side, double quantity)
        {
            double absQty = Math.Abs(quantity);
            if (absQty < 1e-9)
                return 0;
            return side == MarketPosition.Short ? -absQty : absQty;
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
                LastDeltaQuantity = source.LastDeltaQuantity,
                ManualStopOverride = source.ManualStopOverride,
                ManualTargetOverride = source.ManualTargetOverride,
                AggregateEntry = source.AggregateEntry,
                IsScaleInTrade = source.IsScaleInTrade
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

        public bool StartRunUpTrailing(string tradeId, double anchorPrice, RunUpConfig config)
        {
            if (string.IsNullOrWhiteSpace(tradeId) || config == null || !config.Enabled)
                return false;

            TradeRecord record;
            lock (gate)
            {
                if (!tradesById.TryGetValue(tradeId, out record))
                    return false;
            }

            if (record.Strategy is IRunUpParticipant participant)
            {
                participant.HandleRunUpStart(tradeId, anchorPrice, config);
                owner.LogInfo("RUN_UP", $"Activated NT Run-Up trailing for trade_id {tradeId} at anchor {anchorPrice:F2}", tradeId, tradeId);
                return true;
            }

            string strategyName = "<unknown>";
            if (record.Strategy != null && !string.IsNullOrEmpty(record.Strategy.Name))
                strategyName = record.Strategy.Name;
            owner.LogWarn("RUN_UP", string.Format("Strategy {0} missing IRunUpParticipant implementation for trade_id {1}", strategyName, tradeId), tradeId, tradeId);
            return false;
        }
    }
}
