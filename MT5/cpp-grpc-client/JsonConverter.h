#pragma once

#include <string>
#include <cstdint>
#include <nlohmann/json.hpp>

// Forward declaration
namespace trading {
    class Trade;
}

// JSON utility functions for C++ gRPC client
class JsonConverter {
public:
    // Convert protobuf Trade to JSON string
    static std::string TradeToJson(const trading::Trade& trade);
    
    // Convert JSON string to protobuf Trade
    static trading::Trade JsonToTrade(const std::string& json_str);
    
    // Validate JSON string format
    static bool IsValidJson(const std::string& json_str);
    
    // Extract specific fields from JSON
    static std::string GetStringField(const nlohmann::json& json_obj, const std::string& field, const std::string& default_val = "");
    static double GetDoubleField(const nlohmann::json& json_obj, const std::string& field, double default_val = 0.0);
    static int GetIntField(const nlohmann::json& json_obj, const std::string& field, int default_val = 0);
    static uint64_t GetUInt64Field(const nlohmann::json& json_obj, const std::string& field, uint64_t default_val = 0);
    static bool GetBoolField(const nlohmann::json& json_obj, const std::string& field, bool default_val = false);
    
    // Create standardized error responses
    static std::string CreateErrorResponse(const std::string& error_message, int error_code = -1);
    static std::string CreateSuccessResponse(const std::string& message = "Success");
    
    // Format timestamps
    static std::string GetCurrentTimestamp();
    static std::string FormatTimestamp(int64_t timestamp);
    
    // Helper for MT5 trade result formatting
    static std::string FormatTradeResult(const std::string& status, uint64_t ticket, double volume, bool is_close, const std::string& id);
    
    // Helper for health check responses
    static std::string FormatHealthResponse(const std::string& status, int queue_size = 0, int net_position = 0, double hedge_size = 0.0);
};
