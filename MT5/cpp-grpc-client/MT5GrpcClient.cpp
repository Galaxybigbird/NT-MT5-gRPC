#include "MT5GrpcClient.h"
#include "JsonConverter.h"
#include <windows.h>
#include <grpcpp/grpcpp.h>
#include <grpcpp/create_channel.h>
#include <grpcpp/security/credentials.h>
#include "proto/trading.grpc.pb.h"
#include <nlohmann/json.hpp>
#include <thread>
#include <atomic>
#include <queue>
#include <mutex>
#include <condition_variable>
#include <chrono>
#include <memory>
#include <iostream>
#include <fstream>

using grpc::Channel;
using grpc::ClientContext;
using grpc::Status;
using grpc::ClientReader;
using grpc::ClientWriter;
using grpc::ClientReaderWriter;
using trading::TradingService;
using trading::StreamingService;
using trading::LoggingService;
using trading::Trade;
using trading::HealthRequest;
using trading::HealthResponse;
using trading::GenericResponse;
using trading::MT5TradeResult;
using trading::HedgeCloseNotification;
using trading::ElasticHedgeUpdate;
using trading::TrailingStopUpdate;
using trading::LogEvent;
using trading::LogAck;
using nlohmann::json;

// Global state
class GrpcClientState {
public:
    std::shared_ptr<Channel> channel_;
    std::unique_ptr<TradingService::Stub> trading_stub_;
    std::unique_ptr<StreamingService::Stub> streaming_stub_;
    std::unique_ptr<LoggingService::Stub> logging_stub_;
    
    std::atomic<bool> is_initialized_{false};
    std::atomic<bool> is_connected_{false};
    std::atomic<bool> is_streaming_{false};
    
    std::string server_address_;
    int connection_timeout_ms_ = 30000;
    int streaming_timeout_ms_ = 0; // No timeout for streaming
    int max_retries_ = 3;
    
    std::queue<std::string> trade_queue_;
    std::mutex trade_queue_mutex_;
    std::condition_variable trade_queue_cv_;
    
    std::thread streaming_thread_;
    std::atomic<bool> stop_streaming_{false};
    // Pointer to the active streaming context to allow external cancellation
    std::mutex streaming_ctx_mutex_;
    grpc::ClientContext* active_streaming_context_ = nullptr; // non-owning; valid only while stream runs
    
    std::string last_error_;
    std::mutex error_mutex_;
    
    std::chrono::steady_clock::time_point last_health_check_;
    
    ~GrpcClientState() {
        Cleanup();
    }
    
    void Cleanup() {
        is_streaming_ = false;
        stop_streaming_ = true;

        // Proactively cancel the active streaming context to unblock any pending Read/Write
        {
            std::lock_guard<std::mutex> lock(streaming_ctx_mutex_);
            if (active_streaming_context_ != nullptr) {
                active_streaming_context_->TryCancel();
            }
        }
        
        if (streaming_thread_.joinable()) {
            streaming_thread_.join();
        }
        
    trading_stub_.reset();
    streaming_stub_.reset();
    logging_stub_.reset();
        channel_.reset();
        
        is_initialized_ = false;
        is_connected_ = false;
        
        // Clear trade queue
        std::lock_guard<std::mutex> lock(trade_queue_mutex_);
        while (!trade_queue_.empty()) {
            trade_queue_.pop();
        }
    }

    // Non-blocking cancel used from DllMain PROCESS_DETACH to avoid deadlocks
    void QuickCancelForDetach() {
        is_streaming_ = false;
        stop_streaming_ = true;
        {
            std::lock_guard<std::mutex> lock(streaming_ctx_mutex_);
            if (active_streaming_context_ != nullptr) {
                active_streaming_context_->TryCancel();
            }
        }
        // Do NOT join threads or destroy gRPC objects here; defer to explicit GrpcShutdown
    }
    
    void SetLastError(const std::string& error) {
        std::lock_guard<std::mutex> lock(error_mutex_);
        last_error_ = error;
    }
    
    std::string GetLastError() {
        std::lock_guard<std::mutex> lock(error_mutex_);
        return last_error_;
    }
    
    void EnqueueTrade(const std::string& trade_json) {
        std::lock_guard<std::mutex> lock(trade_queue_mutex_);
        trade_queue_.push(trade_json);
        trade_queue_cv_.notify_one();
    }
    
    bool DequeueTrade(std::string& trade_json) {
        std::lock_guard<std::mutex> lock(trade_queue_mutex_);
        if (trade_queue_.empty()) {
            return false;
        }
        trade_json = trade_queue_.front();
        trade_queue_.pop();
        return true;
    }
    
    size_t GetTradeQueueSize() {
        std::lock_guard<std::mutex> lock(trade_queue_mutex_);
        return trade_queue_.size();
    }
};

static GrpcClientState g_client_state;

// Helper function to convert wide string to UTF-8
std::string WideStringToUTF8(const wchar_t* wide_str) {
    if (!wide_str) return "";
    
    int size = WideCharToMultiByte(CP_UTF8, 0, wide_str, -1, nullptr, 0, nullptr, nullptr);
    if (size <= 0) return "";
    
    std::string result(size - 1, '\0');
    WideCharToMultiByte(CP_UTF8, 0, wide_str, -1, &result[0], size - 1, nullptr, nullptr);
    return result;
}

// Helper function to convert UTF-8 to wide string
void UTF8ToWideString(const std::string& utf8_str, wchar_t* buffer, int buffer_size) {
    if (!buffer || buffer_size <= 0) return;
    
    int chars_written = MultiByteToWideChar(CP_UTF8, 0, utf8_str.c_str(), -1, buffer, buffer_size - 1);
    if (chars_written > 0) {
        buffer[chars_written] = L'\0';
    } else {
        buffer[0] = L'\0';
    }
}

// Streaming thread function
void StreamingThreadFunction() {
    while (!g_client_state.stop_streaming_ && g_client_state.is_initialized_) {
        try {
            ClientContext context;
            // Expose context for external cancellation while this stream is active
            {
                std::lock_guard<std::mutex> lock(g_client_state.streaming_ctx_mutex_);
                g_client_state.active_streaming_context_ = &context;
            }
            // Only set deadline if timeout is specified (> 0)
            if (g_client_state.streaming_timeout_ms_ > 0) {
                auto deadline = std::chrono::system_clock::now() + 
                              std::chrono::milliseconds(g_client_state.streaming_timeout_ms_);
                context.set_deadline(deadline);
            }
            
            auto stream = g_client_state.trading_stub_->GetTrades(&context);
            
            // Send periodic health requests
            std::thread heartbeat_thread([&stream, &context]() {
                try {
                    while (!g_client_state.stop_streaming_) {
                        HealthRequest request;
                        request.set_source("MT5_EA");
                        request.set_open_positions(0);
                        
                        if (!stream->Write(request)) {
                            break;
                        }
                        
                        std::this_thread::sleep_for(std::chrono::seconds(1));
                    }
                } catch (...) {
                    // Heartbeat thread error
                }
            });
            
            // Read trades from stream
            Trade trade;
            while (stream->Read(&trade) && !g_client_state.stop_streaming_) {
                // Convert trade to JSON
                json trade_json = {
                    {"id", trade.id()},
                    {"base_id", trade.base_id()},
                    {"timestamp", trade.timestamp()},
                    {"action", trade.action()},
                    // Ensure EA can branch on elastic/trailing events
                    {"event_type", trade.event_type()},
                    {"quantity", trade.quantity()},
                    {"price", trade.price()},
                    {"total_quantity", trade.total_quantity()},
                    {"contract_num", trade.contract_num()},
                    {"order_type", trade.order_type()},
                    {"measurement_pips", trade.measurement_pips()},
                    {"raw_measurement", trade.raw_measurement()},
                    {"instrument", trade.instrument()},
                    {"account_name", trade.account_name()},
                    {"nt_balance", trade.nt_balance()},
                    {"nt_daily_pnl", trade.nt_daily_pnl()},
                    {"nt_trade_result", trade.nt_trade_result()},
                    {"nt_session_trades", trade.nt_session_trades()},
                    // Elastic sizing hint propagated from NT via Bridge
                    {"nt_points_per_1k_loss", trade.nt_points_per_1k_loss()},
                    // Forward elastic metrics used by EA for partial-close gating
                    {"elastic_current_profit", trade.elastic_current_profit()},
                    {"elastic_profit_level", trade.elastic_profit_level()},
                    // Critical for deterministic CLOSE_HEDGE when multiple hedges exist
                    {"mt5_ticket", trade.mt5_ticket()}
                };

                // SAFEGUARD: Omit nt_daily_pnl from JSON for non-entry actions when the value is zero (proto default)
                try {
                    std::string act = trade.action();
                    std::transform(act.begin(), act.end(), act.begin(), ::tolower);
                    if (act != "buy" && act != "sell" && trade.nt_daily_pnl() == 0.0) {
                        trade_json.erase("nt_daily_pnl");
                    }
                } catch (...) {
                    // Non-fatal: if anything goes wrong, proceed with full JSON
                }

                g_client_state.EnqueueTrade(trade_json.dump());
            }
            
            // Clean up heartbeat thread
            if (heartbeat_thread.joinable()) {
                heartbeat_thread.join();
            }
            
            Status status = stream->Finish();
            if (!status.ok() && !g_client_state.stop_streaming_) {
                g_client_state.SetLastError("Stream error: " + status.error_message());
                g_client_state.is_connected_ = false;
            }
            // Clear exposed context pointer once stream ends
            {
                std::lock_guard<std::mutex> lock(g_client_state.streaming_ctx_mutex_);
                g_client_state.active_streaming_context_ = nullptr;
            }
            
        } catch (const std::exception& e) {
            g_client_state.SetLastError("Streaming exception: " + std::string(e.what()));
            g_client_state.is_connected_ = false;
            // Ensure exposed context pointer is cleared on exceptions too
            {
                std::lock_guard<std::mutex> lock(g_client_state.streaming_ctx_mutex_);
                g_client_state.active_streaming_context_ = nullptr;
            }
        }
        
        // Reconnection delay
        if (!g_client_state.stop_streaming_) {
            std::this_thread::sleep_for(std::chrono::seconds(5));
        }
    }
}

// DLL Entry Point
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
        break;
    case DLL_THREAD_ATTACH:
        break;
    case DLL_THREAD_DETACH:
        break;
    case DLL_PROCESS_DETACH:
    // Avoid blocking in DllMain; just cancel to unblock any pending IO
    g_client_state.QuickCancelForDetach();
        break;
    }
    return TRUE;
}

// Exported functions implementation
extern "C" {

MT5_GRPC_API int __stdcall TestFunction() {
    return 42;
}

MT5_GRPC_API int __stdcall GrpcInitialize(const wchar_t* server_address, int port) {
    try {
        if (g_client_state.is_initialized_) {
            g_client_state.Cleanup();
        }
        
        std::string address = WideStringToUTF8(server_address);
        g_client_state.server_address_ = address + ":" + std::to_string(port);
        
        // Create insecure channel
        g_client_state.channel_ = grpc::CreateChannel(
            g_client_state.server_address_, 
            grpc::InsecureChannelCredentials()
        );
        
        if (!g_client_state.channel_) {
            g_client_state.SetLastError("Failed to create gRPC channel");
            return ERROR_INIT_FAILED;
        }
        
    // Create stubs
    g_client_state.trading_stub_ = TradingService::NewStub(g_client_state.channel_);
    g_client_state.streaming_stub_ = StreamingService::NewStub(g_client_state.channel_);
    g_client_state.logging_stub_ = LoggingService::NewStub(g_client_state.channel_);
        
        // Test connection with health check
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        HealthRequest request;
        request.set_source("MT5_EA");
        request.set_open_positions(0);
        
        HealthResponse response;
        Status status = g_client_state.trading_stub_->HealthCheck(&context, request, &response);
        
        if (status.ok() && response.status() == "healthy") {
            g_client_state.is_initialized_ = true;
            g_client_state.is_connected_ = true;
            g_client_state.last_health_check_ = std::chrono::steady_clock::now();
            return ERROR_SUCCESS;
        } else {
            g_client_state.SetLastError("Health check failed: " + status.error_message());
            return ERROR_CONNECTION_FAILED;
        }
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Initialize exception: " + std::string(e.what()));
        return ERROR_INIT_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcShutdown() {
    try {
        g_client_state.Cleanup();
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Shutdown exception: " + std::string(e.what()));
        return ERROR_CLEANUP_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcIsConnected() {
    return g_client_state.is_connected_ ? 1 : 0;
}

MT5_GRPC_API int __stdcall GrpcReconnect() {
    try {
        g_client_state.Cleanup();
        g_client_state.is_connected_ = false;
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Reconnect exception: " + std::string(e.what()));
        return ERROR_CONNECTION_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcStartTradeStream() {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        if (g_client_state.is_streaming_) {
            return ERROR_SUCCESS; // Already streaming
        }
        
        g_client_state.stop_streaming_ = false;
        g_client_state.streaming_thread_ = std::thread(StreamingThreadFunction);
        g_client_state.is_streaming_ = true;
        
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Start stream exception: " + std::string(e.what()));
        return ERROR_STREAM_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcStopTradeStream() {
    try {
        g_client_state.is_streaming_ = false;
        g_client_state.stop_streaming_ = true;
        // Cancel active streaming context to interrupt blocking Read/Write immediately
        {
            std::lock_guard<std::mutex> lock(g_client_state.streaming_ctx_mutex_);
            if (g_client_state.active_streaming_context_ != nullptr) {
                g_client_state.active_streaming_context_->TryCancel();
            }
        }
        
        if (g_client_state.streaming_thread_.joinable()) {
            g_client_state.streaming_thread_.join();
        }
        
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Stop stream exception: " + std::string(e.what()));
        return ERROR_STREAM_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcGetNextTrade(wchar_t* trade_json, int buffer_size) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        if (!trade_json || buffer_size <= 0) {
            return ERROR_INVALID_PARAMS;
        }
        
        std::string trade_str;
        if (g_client_state.DequeueTrade(trade_str)) {
            if (trade_str.length() >= buffer_size - 1) {
                return ERROR_INVALID_PARAMS; // Buffer too small
            }
            
            UTF8ToWideString(trade_str, trade_json, buffer_size);
            return ERROR_SUCCESS;
        } else {
            trade_json[0] = L'\0'; // No trades available
            return ERROR_SUCCESS;
        }
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Get next trade exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcGetTradeQueueSize() {
    return static_cast<int>(g_client_state.GetTradeQueueSize());
}

MT5_GRPC_API int __stdcall GrpcSubmitTradeResult(const wchar_t* result_json) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        std::string json_str = WideStringToUTF8(result_json);
        json result_data = json::parse(json_str);
        
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        MT5TradeResult trade_result;
        trade_result.set_status(result_data.value("status", ""));
        trade_result.set_ticket(result_data.value("ticket", 0ULL));
        trade_result.set_volume(result_data.value("volume", 0.0));
        trade_result.set_is_close(result_data.value("is_close", false));
        trade_result.set_id(result_data.value("id", ""));
        
        GenericResponse response;
        Status status = g_client_state.trading_stub_->SubmitTradeResult(&context, trade_result, &response);
        
        if (status.ok() && response.status() == "success") {
            return ERROR_SUCCESS;
        } else {
            g_client_state.SetLastError("Submit trade result failed: " + status.error_message());
            return ERROR_CONNECTION_FAILED;
        }
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Submit trade result exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcHealthCheck(const wchar_t* request_json, wchar_t* response_json, int buffer_size) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        if (!response_json || buffer_size <= 0) {
            return ERROR_INVALID_PARAMS;
        }
        
        std::string json_str = WideStringToUTF8(request_json);
        json request_data = json::parse(json_str);
        
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        HealthRequest request;
        request.set_source("MT5_EA");
        request.set_open_positions(request_data.value("open_positions", 0));
        
        HealthResponse response;
        Status status = g_client_state.trading_stub_->HealthCheck(&context, request, &response);
        
        json response_json_obj = {
            {"status", response.status()},
            {"queue_size", response.queue_size()},
            {"net_position", response.net_position()},
            {"hedge_size", response.hedge_size()}
        };
        
        std::string response_str = response_json_obj.dump();
        UTF8ToWideString(response_str, response_json, buffer_size);
        
        if (status.ok() && response.status() == "healthy") {
            g_client_state.is_connected_ = true;
            g_client_state.last_health_check_ = std::chrono::steady_clock::now();
            return ERROR_SUCCESS;
        } else {
            g_client_state.is_connected_ = false;
            g_client_state.SetLastError("Health check failed: " + status.error_message());
            return ERROR_CONNECTION_FAILED;
        }
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Health check exception: " + std::string(e.what()));
        return ERROR_CONNECTION_FAILED;
    }
}

MT5_GRPC_API int __stdcall GrpcNotifyHedgeClose(const wchar_t* notification_json) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        std::string json_str = WideStringToUTF8(notification_json);
        json notification_data = json::parse(json_str);
        
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        HedgeCloseNotification notification;
        notification.set_event_type(notification_data.value("event_type", ""));
        notification.set_base_id(notification_data.value("base_id", ""));
        notification.set_nt_instrument_symbol(notification_data.value("nt_instrument_symbol", ""));
        notification.set_nt_account_name(notification_data.value("nt_account_name", ""));
        notification.set_closed_hedge_quantity(notification_data.value("closed_hedge_quantity", 0.0));
        notification.set_closed_hedge_action(notification_data.value("closed_hedge_action", ""));
        notification.set_timestamp(notification_data.value("timestamp", ""));
        notification.set_closure_reason(notification_data.value("closure_reason", ""));
        notification.set_mt5_ticket(notification_data.value("mt5_ticket", static_cast<uint64_t>(0)));
        
        GenericResponse response;
        Status status = g_client_state.trading_stub_->NotifyHedgeClose(&context, notification, &response);
        
        return status.ok() && response.status() == "success" ? ERROR_SUCCESS : ERROR_CONNECTION_FAILED;
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Notify hedge close exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcSubmitElasticUpdate(const wchar_t* update_json) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        std::string json_str = WideStringToUTF8(update_json);
        json update_data = json::parse(json_str);
        
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        ElasticHedgeUpdate update;
        update.set_event_type(update_data.value("event_type", "elastic_update"));
        update.set_action(update_data.value("action", ""));
        update.set_base_id(update_data.value("base_id", ""));
        update.set_current_profit(update_data.value("current_profit", 0.0));
        update.set_profit_level(update_data.value("profit_level", 0));
        update.set_timestamp(update_data.value("timestamp", ""));
        
        GenericResponse response;
        Status status = g_client_state.trading_stub_->SubmitElasticUpdate(&context, update, &response);
        
        return status.ok() && response.status() == "success" ? ERROR_SUCCESS : ERROR_CONNECTION_FAILED;
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Submit elastic ping exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcSubmitTrailingUpdate(const wchar_t* update_json) {
    try {
        if (!g_client_state.is_initialized_) {
            return ERROR_NOT_INITIALIZED;
        }
        
        std::string json_str = WideStringToUTF8(update_json);
        json update_data = json::parse(json_str);
        
        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);
        
        TrailingStopUpdate update;
        update.set_event_type(update_data.value("event_type", "trailing_update"));
        update.set_base_id(update_data.value("base_id", ""));
        update.set_new_stop_price(update_data.value("new_stop_price", 0.0));
        update.set_trailing_type(update_data.value("trailing_type", ""));
        update.set_current_price(update_data.value("current_price", 0.0));
        update.set_timestamp(update_data.value("timestamp", ""));
        
        GenericResponse response;
        Status status = g_client_state.trading_stub_->SubmitTrailingUpdate(&context, update, &response);
        
        return status.ok() && response.status() == "success" ? ERROR_SUCCESS : ERROR_CONNECTION_FAILED;
        
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Submit trailing update exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcGetConnectionStatus(wchar_t* status_json, int buffer_size) {
    try {
        if (!status_json || buffer_size <= 0) {
            return ERROR_INVALID_PARAMS;
        }
        
        auto now = std::chrono::steady_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(
            now - g_client_state.last_health_check_).count();
        
        json status_obj = {
            {"connected", g_client_state.is_connected_.load()},
            {"streaming", g_client_state.is_streaming_.load()},
            {"server_address", g_client_state.server_address_},
            {"queue_size", g_client_state.GetTradeQueueSize()},
            {"last_health_check_seconds_ago", duration}
        };
        
        std::string status_str = status_obj.dump();
        UTF8ToWideString(status_str, status_json, buffer_size);
        
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Get connection status exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcGetStreamingStats(wchar_t* stats_json, int buffer_size) {
    try {
        if (!stats_json || buffer_size <= 0) {
            return ERROR_INVALID_PARAMS;
        }
        
        json stats_obj = {
            {"streaming_active", g_client_state.is_streaming_.load()},
            {"trades_in_queue", g_client_state.GetTradeQueueSize()},
            {"connection_established", g_client_state.is_connected_.load()}
        };
        
        std::string stats_str = stats_obj.dump();
        UTF8ToWideString(stats_str, stats_json, buffer_size);
        
        return ERROR_SUCCESS;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Get streaming stats exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcGetLastError(wchar_t* error_message, int buffer_size) {
    try {
        if (!error_message || buffer_size <= 0) {
            return ERROR_INVALID_PARAMS;
        }
        
        std::string error_str = g_client_state.GetLastError();
        if (error_str.empty()) {
            error_str = "No error";
        }
        
        UTF8ToWideString(error_str, error_message, buffer_size);
        return ERROR_SUCCESS;
    } catch (...) {
        return ERROR_SERIALIZATION;
    }
}

MT5_GRPC_API int __stdcall GrpcSetConnectionTimeout(int timeout_ms) {
    g_client_state.connection_timeout_ms_ = timeout_ms;
    return ERROR_SUCCESS;
}

MT5_GRPC_API int __stdcall GrpcSetStreamingTimeout(int timeout_ms) {
    g_client_state.streaming_timeout_ms_ = timeout_ms;
    return ERROR_SUCCESS;
}

MT5_GRPC_API int __stdcall GrpcSetMaxRetries(int max_retries) {
    g_client_state.max_retries_ = max_retries;
    return ERROR_SUCCESS;
}

MT5_GRPC_API int __stdcall GrpcLog(const wchar_t* log_json) {
    try {
        if (!g_client_state.is_initialized_ || !g_client_state.logging_stub_) {
            return ERROR_NOT_INITIALIZED;
        }

        std::string json_str = WideStringToUTF8(log_json);
        json data = json::parse(json_str, nullptr, false);
        if (data.is_discarded()) {
            g_client_state.SetLastError("Invalid JSON for LogEvent");
            return ERROR_SERIALIZATION;
        }

        ClientContext context;
        auto deadline = std::chrono::system_clock::now() + 
                       std::chrono::milliseconds(g_client_state.connection_timeout_ms_);
        context.set_deadline(deadline);

        LogEvent evt;
        // Minimal required fields with safe defaults
        evt.set_timestamp_ns(data.value("timestamp_ns", 0LL));
        evt.set_source(data.value("source", std::string("mt5")));
        evt.set_level(data.value("level", std::string("INFO")));
        evt.set_component(data.value("component", std::string("EA")));
        evt.set_message(data.value("message", std::string("")));
        evt.set_base_id(data.value("base_id", std::string("")));
        evt.set_trade_id(data.value("trade_id", std::string("")));
        evt.set_nt_order_id(data.value("nt_order_id", std::string("")));
        evt.set_mt5_ticket(data.value("mt5_ticket", 0ULL));
        evt.set_queue_size(data.value("queue_size", 0));
        evt.set_net_position(data.value("net_position", 0));
        evt.set_hedge_size(data.value("hedge_size", 0.0));
        evt.set_error_code(data.value("error_code", std::string("")));
        evt.set_stack(data.value("stack", std::string("")));
        evt.set_schema_version(data.value("schema_version", std::string("mt5-1")));
        evt.set_correlation_id(data.value("correlation_id", std::string("")));

        // Optional tags object
        if (data.contains("tags") && data["tags"].is_object()) {
            for (auto it = data["tags"].begin(); it != data["tags"].end(); ++it) {
                (*evt.mutable_tags())[it.key()] = it.value().is_string() ? it.value().get<std::string>() : it.value().dump();
            }
        }

        LogAck ack;
        auto status = g_client_state.logging_stub_->Log(&context, evt, &ack);
        if (status.ok() && ack.accepted() > 0) {
            return ERROR_SUCCESS;
        }
        g_client_state.SetLastError("Log failed: " + status.error_message());
        return ERROR_CONNECTION_FAILED;
    } catch (const std::exception& e) {
        g_client_state.SetLastError("Log exception: " + std::string(e.what()));
        return ERROR_SERIALIZATION;
    }
}

} // extern "C"
