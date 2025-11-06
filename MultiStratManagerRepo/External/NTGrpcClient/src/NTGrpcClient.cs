using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Trading.Proto;
using Grpc.Core;

namespace NTGrpcClient
{
    /// <summary>
    /// Simple gRPC client interface for NinjaTrader addon
    /// Handles all gRPC complexity internally
    /// </summary>
    public static class TradingGrpcClient
    {
        private static ITradingClient _client;
        private static TextWriter _originalConsoleOut;
        private static UnifiedLogWriter _unifiedLogWriter;
        // Correlation tracking (base_id -> correlation_id)
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> _corrByBaseId = new System.Collections.Concurrent.ConcurrentDictionary<string,string>();
        private static string GetOrCreateCorrelation(string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId)) return Guid.NewGuid().ToString("N");
            return _corrByBaseId.GetOrAdd(baseId, _ => Guid.NewGuid().ToString("N"));
        }
        public static void ReleaseCorrelation(string baseId)
        {
            if (!string.IsNullOrWhiteSpace(baseId)) _corrByBaseId.TryRemove(baseId, out _);
        }
        private struct PendingLogEntry
        {
            public string Level;
            public string Component;
            public string Message;
            public string TradeId;
            public string ErrorCode;
            public string BaseId;
        }

        private static readonly System.Collections.Concurrent.ConcurrentQueue<PendingLogEntry> _pendingLogs = new System.Collections.Concurrent.ConcurrentQueue<PendingLogEntry>();
        private static volatile bool _initialized = false;
        private const int MaxBufferedLogs = 2048;
        private static void EnqueuePendingLog(string level, string component, string message, string tradeId, string errorCode, string baseId)
        {
            // Simple backpressure: drop oldest when buffer grows too large
            while (_pendingLogs.Count >= MaxBufferedLogs && _pendingLogs.TryDequeue(out _)) { }
            _pendingLogs.Enqueue(new PendingLogEntry
            {
                Level = level,
                Component = component,
                Message = message,
                TradeId = tradeId,
                ErrorCode = errorCode,
                BaseId = baseId
            });
        }

        private static void FlushPendingLogs()
        {
            if (!_initialized || !(_client is TradingClient impl))
                return;

            PendingLogEntry entry;
            int drained = 0;
            while (drained < 512 && _pendingLogs.TryDequeue(out entry))
            {
                drained++;
                try
                {
                    string corr = GetOrCreateCorrelation(entry.BaseId);
                    impl.LogFireAndForget(entry.Level, entry.Component, entry.Message, entry.TradeId, entry.ErrorCode, entry.BaseId, corr);
                }
                catch
                {
                    // If sending fails, requeue and bail to avoid tight failure loop
                    EnqueuePendingLog(entry.Level, entry.Component, entry.Message, entry.TradeId, entry.ErrorCode, entry.BaseId);
                    break;
                }
            }

            // If more logs remain, schedule another pass soon
            if (!_pendingLogs.IsEmpty)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(200);
                    FlushPendingLogs();
                });
            }
        }
        
        /// <summary>
        /// Initialize the gRPC client
        /// </summary>
        /// <param name="serverAddress">gRPC server address (e.g., "localhost:50051")</param>
        /// <returns>True if successful</returns>
        public static bool Initialize(string serverAddress)
        {
            try
            {
                _client = new TradingClient(serverAddress);
                // Redirect all Console output to Bridge logging, preserving original output
                try
                {
                    _originalConsoleOut = Console.Out;
                    _unifiedLogWriter = new Trading.Proto.UnifiedLogWriter(_originalConsoleOut, serverAddress, source: "nt", component: "nt_addon");
                    Console.SetOut(_unifiedLogWriter);
                    // Emit a quick test line to verify console redirection + LoggingService path
                    Console.WriteLine("[NT_ADDON][INFO][GRPC] Unified logging initialized.");
                }
                catch { /* non-fatal */ }
                // Perform a quick blocking health check to verify server is reachable
                try
                {
                    var health = _client.HealthCheckAsync("NT_ADDON_INIT");
                    if (!health.Wait(TimeSpan.FromSeconds(2)))
                    {
                        LastError = "HealthCheck timeout";
                        _initialized = false;
                        return false;
                    }
                    if (!health.Result.Success)
                    {
                        LastError = string.IsNullOrWhiteSpace(health.Result.ErrorMessage) ? "HealthCheck failed" : health.Result.ErrorMessage;
                        _initialized = false;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    _initialized = false;
                    return false;
                }
                _initialized = true;
                FlushPendingLogs();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        // Lightweight log forwarding helpers
    public static void Log(string level, string component, string message, string tradeId = "", string errorCode = "", string baseId = "")
        {
            if (_initialized && _client is TradingClient impl)
            {
                FlushPendingLogs();
                string corr = GetOrCreateCorrelation(baseId);
                impl.LogFireAndForget(level, component, message, tradeId, errorCode, baseId, corr);
            }
            else
            {
                EnqueuePendingLog(level, component, message, tradeId, errorCode, baseId);
            }
        }

    public static void LogDebug(string component, string message, string tradeId = "", string baseId = "") => Log("DEBUG", component, message, tradeId, "", baseId);
    public static void LogInfo(string component, string message, string tradeId = "", string baseId = "") => Log("INFO", component, message, tradeId, "", baseId);
    public static void LogWarn(string component, string message, string tradeId = "", string baseId = "") => Log("WARN", component, message, tradeId, "", baseId);
    public static void LogError(string component, string message, string tradeId = "", string errorCode = "", string baseId = "") => Log("ERROR", component, message, tradeId, errorCode, baseId);
        
        /// <summary>
        /// Submit a trade to the bridge server
        /// </summary>
        /// <param name="tradeJson">JSON representation of trade</param>
        /// <returns>Success status</returns>
        public static async Task<bool> SubmitTradeAsync(string tradeJson)
        {
            if (!_initialized) 
            {
                LastError = "gRPC client not initialized";
                return false;
            }
            
            try
            {
                var result = await _client.SubmitTradeAsync(tradeJson);
                return result.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Perform health check
        /// </summary>
        /// <param name="source">Source identifier</param>
        /// <returns>Operation result with health status and response JSON</returns>
        public static async Task<OperationResult> HealthCheckAsync(string source)
        {
            if (!_initialized) 
            {
                LastError = "gRPC client not initialized";
                return new OperationResult { Success = false, ErrorMessage = "gRPC client not initialized" };
            }
            
            try
            {
                var result = await _client.HealthCheckAsync(source);
                
                if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    LastError = result.ErrorMessage;
                }
                
                return result;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return new OperationResult { Success = false, ErrorMessage = ex.Message };
            }
        }
        
        /// <summary>
        /// Submit elastic hedge update
        /// </summary>
        /// <param name="updateJson">JSON representation of elastic update</param>
        /// <returns>Success status</returns>
        public static async Task<bool> SubmitElasticUpdateAsync(string updateJson)
        {
            if (!_initialized) return false;
            
            try
            {
                var result = await _client.SubmitElasticUpdateAsync(updateJson);
                return result.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Submit trailing stop update
        /// </summary>
        /// <param name="updateJson">JSON representation of trailing update</param>
        /// <returns>Success status</returns>
        public static async Task<bool> SubmitTrailingUpdateAsync(string updateJson)
        {
            if (!_initialized) return false;
            
            try
            {
                var result = await _client.SubmitTrailingUpdateAsync(updateJson);
                return result.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Notify hedge closure
        /// </summary>
        /// <param name="notificationJson">JSON representation of hedge closure</param>
        /// <returns>Success status</returns>
        public static async Task<bool> NotifyHedgeCloseAsync(string notificationJson)
        {
            if (!_initialized) return false;
            
            try
            {
                var result = await _client.NotifyHedgeCloseAsync(notificationJson);
                return result.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Submit NT close hedge request
        /// </summary>
        /// <param name="notificationJson">JSON representation of close hedge request</param>
        /// <returns>Success status</returns>
        public static async Task<bool> NTCloseHedgeAsync(string notificationJson)
        {
            if (!_initialized) return false;
            
            try
            {
                var result = await _client.NTCloseHedgeAsync(notificationJson);
                return result.Success;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Start streaming connection for real-time updates
        /// </summary>
        /// <param name="onTradeReceived">Callback for received trades (JSON)</param>
        /// <returns>True if stream started</returns>
        public static bool StartTradingStream(Action<string> onTradeReceived)
        {
            if (!_initialized) return false;
            
            try
            {
                _client.StartTradingStream(onTradeReceived);
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
        
        /// <summary>
        /// Stop trading stream
        /// </summary>
        public static void StopTradingStream()
        {
            _client?.StopTradingStream();
        }
        
        // Synchronous wrapper methods for backward compatibility
        // These are temporary until all callers are converted to async
        
        /// <summary>
        /// Submit a trade to the bridge server (synchronous wrapper)
        /// </summary>
        public static bool SubmitTrade(string tradeJson)
        {
            return SubmitTradeAsync(tradeJson).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Perform health check (synchronous wrapper)
        /// </summary>
        public static bool HealthCheck(string source, out string responseJson)
        {
            var result = HealthCheckAsync(source).GetAwaiter().GetResult();
            responseJson = result.ResponseJson;
            return result.Success;
        }
        
        /// <summary>
        /// Submit elastic hedge update (synchronous wrapper)
        /// </summary>
        public static bool SubmitElasticUpdate(string updateJson)
        {
            return SubmitElasticUpdateAsync(updateJson).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Submit trailing stop update (synchronous wrapper)
        /// </summary>
        public static bool SubmitTrailingUpdate(string updateJson)
        {
            return SubmitTrailingUpdateAsync(updateJson).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Notify hedge closure (synchronous wrapper)
        /// </summary>
        public static bool NotifyHedgeClose(string notificationJson)
        {
            return NotifyHedgeCloseAsync(notificationJson).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Submit NT close hedge request (synchronous wrapper)
        /// </summary>
        public static bool NTCloseHedge(string notificationJson)
        {
            return NTCloseHedgeAsync(notificationJson).GetAwaiter().GetResult();
        }
        
        /// <summary>
        /// Check if client is connected
        /// </summary>
        public static bool IsConnected => _initialized && _client?.IsConnected == true;
        
        /// <summary>
        /// Last error message
        /// </summary>
        public static string LastError { get; private set; } = "";
        
        /// <summary>
        /// Version for debugging
        /// </summary>
    public static string Version => "1.0.3-CONNECTIVITY";
        
        /// <summary>
        /// Cleanup and dispose
        /// </summary>
        public static void Dispose()
        {
            try { _client?.StopTradingStream(); } catch { /* ignore */ }
            try { _client?.Dispose(); } catch { /* ignore */ }
            _client = null;
            _initialized = false;

            // Restore Console.Out and dispose unified log writer to stop background thread
            try
            {
                if (_unifiedLogWriter != null)
                {
                    Console.SetOut(_originalConsoleOut ?? Console.Out);
                    _unifiedLogWriter.Dispose();
                }
            }
            catch { /* ignore */ }
            finally
            {
                _unifiedLogWriter = null;
                _originalConsoleOut = null;
            }
        }
    }
    
    /// <summary>
    /// Result wrapper for operations
    /// </summary>
    public class OperationResult
    {
        public bool Success { get; set; }
        public string ResponseJson { get; set; } = "";
        public string ErrorMessage { get; set; } = "";
    }
}
