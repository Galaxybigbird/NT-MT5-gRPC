using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Trading.Proto;
using Grpc.Core;
using System.IO;

namespace NTGrpcClient
{
    internal class TradingClient : ITradingClient
    {
        private readonly Channel _channel;
        private readonly TradingService.TradingServiceClient _client;
        private readonly StreamingService.StreamingServiceClient _streamingClient;
        private readonly LoggingService.LoggingServiceClient _loggingClient;
        private CancellationTokenSource _streamCancellation;
        private Task _streamTask;
        private bool _disposed = false;

        public bool IsConnected { get; private set; }
        public string LastError { get; private set; } = "";
        
        public TradingClient(string serverAddress)
        {
            try
            {
                // Extract host and port, normalize localhost to 127.0.0.1 for Grpc.Core
                var address = serverAddress.Replace("http://", "").Replace("https://", "");
                if (address.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
                {
                    address = "127.0.0.1:" + address.Substring("localhost:".Length);
                }
                
                // For .NET Framework 4.8, use Grpc.Core's native implementation
                // Optionally specify a small connect timeout via environment if configured
                // Configure keepalive to reduce intermittent disconnects
                var options = new[]
                {
                    new ChannelOption("grpc.keepalive_time_ms", 15000),           // ping every 15s
                    new ChannelOption("grpc.keepalive_timeout_ms", 5000),        // 5s timeout
                    new ChannelOption("grpc.http2.min_time_between_pings_ms", 10000),
                    new ChannelOption("grpc.keepalive_permit_without_calls", 1),
                    new ChannelOption("grpc.max_reconnect_backoff_ms", 5000)    // speed up reconnects
                };
                _channel = new Channel(address, ChannelCredentials.Insecure, options);
                
                _client = new TradingService.TradingServiceClient(_channel);
                _streamingClient = new StreamingService.StreamingServiceClient(_channel);
                _loggingClient = new LoggingService.LoggingServiceClient(_channel);
                
                LastError = $"Grpc.Core channel created for {address}";
            }
            catch (Exception ex)
            {
                LastError = $"Failed to create Grpc.Core channel: {ex.Message}";
                throw;
            }
        }

    public void LogFireAndForget(string level, string component, string message, string tradeId = "", string errorCode = "", string baseId = "", string correlationId = "")
        {
            try
            {
                var nowNs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;
                var ev = new LogEvent
                {
                    TimestampNs = nowNs,
                    Source = "nt",
                    Level = string.IsNullOrWhiteSpace(level) ? "INFO" : level,
                    Component = component ?? "nt_addon",
                    Message = message ?? string.Empty,
                    TradeId = tradeId ?? string.Empty,
                    ErrorCode = errorCode ?? string.Empty,
                    BaseId = baseId ?? string.Empty
                };
                // Elevate base_id from embedded JSON or plain token if not provided
                if (string.IsNullOrWhiteSpace(ev.BaseId) && !string.IsNullOrWhiteSpace(ev.Message))
                {
                    try
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(ev.Message, "\\\"base_id\\\"\\s*:\\s*\\\"([^\\\"\\\\]+)\\\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (m.Success) ev.BaseId = m.Groups[1].Value.Trim();
                        if (string.IsNullOrWhiteSpace(ev.BaseId))
                        {
                            var m2 = System.Text.RegularExpressions.Regex.Match(ev.Message, "base_id=([A-Za-z0-9_\\-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (m2.Success) ev.BaseId = m2.Groups[1].Value.Trim();
                        }
                    }
                    catch { }
                }
                if (!string.IsNullOrEmpty(correlationId))
                {
                    // Proto in this project may not expose CorrelationId yet; store in tags
                    ev.Tags["correlation_id"] = correlationId;
                }
                else if (!string.IsNullOrWhiteSpace(ev.BaseId))
                {
                    var corr = CorrelationProvider.Get(ev.BaseId);
                    if (!string.IsNullOrEmpty(corr)) { ev.Tags["correlation_id"] = corr; }
                }
                _ = _loggingClient.LogAsync(ev);
            }
            catch
            {
                // swallow logging errors
            }
        }
        
        private async Task TestConnection()
        {
            try
            {
                var request = new HealthRequest { Source = "nt_addon_init" };
                var response = await _client.HealthCheckAsync(request);
                IsConnected = response.Status == "healthy";
            }
            catch (Exception ex)
            {
                IsConnected = false;
                // Store the last connection error for debugging
                LastError = $"Connection test failed: {ex.Message}";
            }
        }
        
        public async Task<OperationResult> SubmitTradeAsync(string tradeJson)
        {
            try
            {
                var trade = JsonToProtoTrade(tradeJson);
                var response = await _client.SubmitTradeAsync(trade);
                
                return new OperationResult
                {
                    Success = response.Status == "success",
                    ResponseJson = JsonSerializer.Serialize(new { status = response.Status, message = response.Message })
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<OperationResult> HealthCheckAsync(string source)
        {
            try
            {
                LastError = $"DEBUG: Starting HealthCheck to {_channel.Target}";
                
                var request = new HealthRequest { Source = source };
                LastError = $"DEBUG: Created request, calling gRPC...";
                
                var response = await _client.HealthCheckAsync(request);
                LastError = $"DEBUG: Got response: Status={response.Status}";
                
                var responseJson = JsonSerializer.Serialize(new
                {
                    status = response.Status,
                    queue_size = response.QueueSize,
                    net_position = response.NetPosition,
                    hedge_size = response.HedgeSize
                });
                
                bool isHealthy = response.Status == "healthy";
                // Update connection state based on health response
                IsConnected = isHealthy;
                
                return new OperationResult
                {
                    Success = isHealthy,
                    ResponseJson = responseJson,
                    ErrorMessage = isHealthy ? "" : $"Server status: {response.Status}"
                };
            }
            catch (Exception ex)
            {
                LastError = $"EXCEPTION: {ex.GetType().Name}: {ex.Message}";
                IsConnected = false;
                
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = $"{ex.GetType().Name}: {ex.Message}"
                };
            }
        }
        
        public async Task<OperationResult> SubmitElasticUpdateAsync(string updateJson)
        {
            try
            {
                var update = JsonToProtoElasticUpdate(updateJson);
                var response = await _client.SubmitElasticUpdateAsync(update);
                
                return new OperationResult
                {
                    Success = response.Status == "success",
                    ResponseJson = JsonSerializer.Serialize(new { status = response.Status })
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<OperationResult> SubmitTrailingUpdateAsync(string updateJson)
        {
            try
            {
                var update = JsonToProtoTrailingUpdate(updateJson);
                var response = await _client.SubmitTrailingUpdateAsync(update);
                
                return new OperationResult
                {
                    Success = response.Status == "success",
                    ResponseJson = JsonSerializer.Serialize(new { status = response.Status })
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<OperationResult> NotifyHedgeCloseAsync(string notificationJson)
        {
            try
            {
                var notification = JsonToProtoHedgeClose(notificationJson);
                var response = await _client.NotifyHedgeCloseAsync(notification);
                
                return new OperationResult
                {
                    Success = response.Status == "success",
                    ResponseJson = JsonSerializer.Serialize(new { status = response.Status })
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public async Task<OperationResult> NTCloseHedgeAsync(string notificationJson)
        {
            try
            {
                var notification = JsonToProtoHedgeClose(notificationJson);
                var response = await _client.NTCloseHedgeAsync(notification);
                
                return new OperationResult
                {
                    Success = response.Status == "success",
                    ResponseJson = JsonSerializer.Serialize(new { status = response.Status })
                };
            }
            catch (Exception ex)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
        
        public void StartTradingStream(Action<string> onTradeReceived)
        {
            _streamCancellation = new CancellationTokenSource();

            _streamTask = Task.Run(async () =>
            {
                try
                {
                    // Reconnect loop with jitter backoff
                    var rand = new Random();
                    int attempt = 0;
                    while (!_streamCancellation.IsCancellationRequested)
                    {
                        try
                        {
                            using (var stream = _streamingClient.TradingStream())
                            {
                                await stream.RequestStream.WriteAsync(new Trade { Id = "init_stream" });
                                // Reset attempt on successful connect
                                attempt = 0;
                                while (await stream.ResponseStream.MoveNext(_streamCancellation.Token))
                                {
                                    var trade = stream.ResponseStream.Current;
                                    var tradeJson = ProtoTradeToJson(trade);
                                    onTradeReceived?.Invoke(tradeJson);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break; // normal shutdown
                        }
                        catch (Exception)
                        {
                            // transient error, fall through to backoff
                        }

                        // Exponential backoff with jitter (max ~5s)
                        attempt++;
                        var delayMs = Math.Min(5000, (int)(Math.Pow(2, Math.Min(6, attempt)) * 100) + rand.Next(0, 250));
                        await Task.Delay(delayMs, _streamCancellation.Token).ContinueWith(_ => { });
                    }
                }
                catch (OperationCanceledException)
                {
                    // Stream was cancelled - normal
                }
                catch (Exception)
                {
                    // swallow; outer loop handles retries
                }
            });
        }

        public void StopTradingStream()
        {
            try { _streamCancellation?.Cancel(); } catch { }
            try { _streamTask?.Wait(2000); } catch { }
            _streamTask = null;
        }
        
        // JSON conversion methods
        private Trade JsonToProtoTrade(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            var instrument = GetStringValue(root, "instrument");
            if (string.IsNullOrWhiteSpace(instrument))
            {
                instrument = GetStringValue(root, "instrument_name");
            }

            return new Trade
            {
                Id = GetStringValue(root, "id"),
                BaseId = GetStringValue(root, "base_id"),
                Timestamp = GetInt64Value(root, "timestamp"),
                Action = GetStringValue(root, "action"),
                Quantity = GetDoubleValue(root, "quantity"),
                Price = GetDoubleValue(root, "price"),
                TotalQuantity = GetInt32Value(root, "total_quantity"),
                ContractNum = GetInt32Value(root, "contract_num"),
                OrderType = GetStringValue(root, "order_type"),
                MeasurementPips = GetInt32Value(root, "measurement_pips"),
                RawMeasurement = GetDoubleValue(root, "raw_measurement"),
                Instrument = instrument,
                AccountName = GetStringValue(root, "account_name"),
                NtBalance = GetDoubleValue(root, "nt_balance"),
                NtDailyPnl = GetDoubleValue(root, "nt_daily_pnl"),
                NtTradeResult = GetStringValue(root, "nt_trade_result"),
                NtSessionTrades = GetInt32Value(root, "nt_session_trades"),
                Mt5Ticket = GetUInt64Value(root, "mt5_ticket"),
                NtPointsPer1KLoss = GetDoubleValue(root, "nt_points_per_1k_loss"),
                EventType = GetStringValue(root, "event_type"),
                ElasticCurrentProfit = GetDoubleValue(root, "elastic_current_profit"),
                ElasticProfitLevel = GetInt32Value(root, "elastic_profit_level")
            };
        }

        private string ProtoTradeToJson(Trade trade)
        {
            var data = new
            {
                id = trade.Id,
                base_id = trade.BaseId,
                timestamp = trade.Timestamp,
                action = trade.Action,
                quantity = trade.Quantity,
                price = trade.Price,
                total_quantity = trade.TotalQuantity,
                contract_num = trade.ContractNum,
                order_type = trade.OrderType,
                measurement_pips = trade.MeasurementPips,
                raw_measurement = trade.RawMeasurement,
                instrument = trade.Instrument,
                instrument_name = trade.Instrument,
                account_name = trade.AccountName,
                nt_balance = trade.NtBalance,
                nt_daily_pnl = trade.NtDailyPnl,
                nt_trade_result = trade.NtTradeResult,
                nt_session_trades = trade.NtSessionTrades,
                mt5_ticket = trade.Mt5Ticket,
                nt_points_per_1k_loss = trade.NtPointsPer1KLoss,
                event_type = trade.EventType,
                elastic_current_profit = trade.ElasticCurrentProfit,
                elastic_profit_level = trade.ElasticProfitLevel
            };

            return JsonSerializer.Serialize(data);
        }
        
        private ElasticHedgeUpdate JsonToProtoElasticUpdate(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            // Accept both schema keys and legacy aliases to be tolerant to callers
            double currentProfit = GetDoubleValue(root, "current_profit");
            if (Math.Abs(currentProfit) < double.Epsilon)
            {
                // legacy alias
                currentProfit = GetDoubleValue(root, "elastic_current_profit");
                if (Math.Abs(currentProfit) < double.Epsilon)
                    currentProfit = GetDoubleValue(root, "price");
            }
            int profitLevel = GetInt32Value(root, "profit_level");
            if (profitLevel == 0)
            {
                // legacy alias
                profitLevel = GetInt32Value(root, "elastic_profit_level");
                if (profitLevel == 0)
                    profitLevel = GetInt32Value(root, "level");
                if (profitLevel == 0)
                    profitLevel = GetInt32Value(root, "volume");
            }

            return new ElasticHedgeUpdate
            {
                EventType = GetStringValue(root, "event_type"),
                Action = GetStringValue(root, "action"),
                BaseId = GetStringValue(root, "base_id"),
                CurrentProfit = currentProfit,
                ProfitLevel = profitLevel,
                Timestamp = GetStringValue(root, "timestamp"),
                Mt5Ticket = GetUInt64Value(root, "mt5_ticket")
            };
        }
        
        private TrailingStopUpdate JsonToProtoTrailingUpdate(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return new TrailingStopUpdate
            {
                EventType = GetStringValue(root, "event_type"),
                BaseId = GetStringValue(root, "base_id"),
                NewStopPrice = GetDoubleValue(root, "new_stop_price"),
                TrailingType = GetStringValue(root, "trailing_type"),
                CurrentPrice = GetDoubleValue(root, "current_price"),
                Timestamp = GetStringValue(root, "timestamp"),
                Mt5Ticket = GetUInt64Value(root, "mt5_ticket")
            };
        }
        
        private HedgeCloseNotification JsonToProtoHedgeClose(string json)
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            
            return new HedgeCloseNotification
            {
                EventType = GetStringValue(root, "event_type"),
                BaseId = GetStringValue(root, "base_id"),
                NtInstrumentSymbol = GetStringValue(root, "nt_instrument_symbol"),
                NtAccountName = GetStringValue(root, "nt_account_name"),
                ClosedHedgeQuantity = GetDoubleValue(root, "closed_hedge_quantity"),
                ClosedHedgeAction = GetStringValue(root, "closed_hedge_action"),
                Timestamp = GetStringValue(root, "timestamp"),
                ClosureReason = GetStringValue(root, "closure_reason"),
                Mt5Ticket = GetUInt64Value(root, "mt5_ticket")
            };
        }
        
        // Helper methods for JSON parsing
        private string GetStringValue(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String 
                ? value.GetString() ?? "" 
                : "";
        }
        
        private double GetDoubleValue(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetDouble();
                if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var result))
                    return result;
            }
            return 0.0;
        }
        
        private int GetInt32Value(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetInt32();
                if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var result))
                    return result;
            }
            return 0;
        }
        
        private long GetInt64Value(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetInt64();
                if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out var result))
                    return result;
            }
            return 0;
        }
        
        private ulong GetUInt64Value(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value))
            {
                if (value.ValueKind == JsonValueKind.Number)
                    return value.GetUInt64();
                if (value.ValueKind == JsonValueKind.String && ulong.TryParse(value.GetString(), out var result))
                    return result;
            }
            return 0;
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _streamCancellation?.Cancel();
                try
                {
                    _channel?.ShutdownAsync().Wait(5000); // Wait max 5 seconds for shutdown
                }
                catch (Exception ex)
                {
                    LastError = $"Channel shutdown error: {ex.Message}";
                }
                _disposed = true;
            }
        }
    }
}
