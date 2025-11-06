#include "JsonConverter.h"
#include "proto/trading.pb.h"
#include <chrono>
#include <iomanip>
#include <sstream>
#include <algorithm>
#include <cctype>
#include <ctime>

using nlohmann::json;

std::string JsonConverter::TradeToJson(const trading::Trade& trade) {
    try {
        json trade_json = {
            {"id", trade.id()},
            {"base_id", trade.base_id()},
            {"timestamp", trade.timestamp()},
            {"action", trade.action()},
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
            // Important for CLOSE_HEDGE targeting in EA
            {"mt5_ticket", trade.mt5_ticket()}
        };
        
        return trade_json.dump();
    } catch (const std::exception& e) {
        return CreateErrorResponse("Failed to convert trade to JSON: " + std::string(e.what()));
    }
}

trading::Trade JsonConverter::JsonToTrade(const std::string& json_str) {
    trading::Trade trade;
    
    try {
        json trade_json = json::parse(json_str);
        
        trade.set_id(JsonConverter::GetStringField(trade_json, "id"));
        trade.set_base_id(JsonConverter::GetStringField(trade_json, "base_id"));
        trade.set_timestamp(JsonConverter::GetIntField(trade_json, "timestamp"));
        trade.set_action(JsonConverter::GetStringField(trade_json, "action"));
        trade.set_quantity(JsonConverter::GetDoubleField(trade_json, "quantity"));
        trade.set_price(JsonConverter::GetDoubleField(trade_json, "price"));
        trade.set_total_quantity(JsonConverter::GetIntField(trade_json, "total_quantity"));
        trade.set_contract_num(JsonConverter::GetIntField(trade_json, "contract_num"));
        trade.set_order_type(JsonConverter::GetStringField(trade_json, "order_type"));
        trade.set_measurement_pips(JsonConverter::GetIntField(trade_json, "measurement_pips"));
        trade.set_raw_measurement(JsonConverter::GetDoubleField(trade_json, "raw_measurement"));
        trade.set_instrument(JsonConverter::GetStringField(trade_json, "instrument"));
        trade.set_account_name(JsonConverter::GetStringField(trade_json, "account_name"));
        trade.set_nt_balance(JsonConverter::GetDoubleField(trade_json, "nt_balance"));
        trade.set_nt_daily_pnl(JsonConverter::GetDoubleField(trade_json, "nt_daily_pnl"));
        trade.set_nt_trade_result(JsonConverter::GetStringField(trade_json, "nt_trade_result"));
        trade.set_nt_session_trades(JsonConverter::GetIntField(trade_json, "nt_session_trades"));
        trade.set_mt5_ticket(JsonConverter::GetUInt64Field(trade_json, "mt5_ticket"));
        
    } catch (const std::exception&) {
        // Return empty trade on error
        // Error handling should be done at caller level
    }
    
    return trade;
}

bool JsonConverter::IsValidJson(const std::string& json_str) {
    return nlohmann::json::accept(json_str);
}

std::string JsonConverter::GetStringField(const nlohmann::json& json_obj, const std::string& field, const std::string& default_val) {
    try {
        if (json_obj.contains(field) && json_obj[field].is_string()) {
            return json_obj[field].get<std::string>();
        }
    } catch (const std::exception&) {
        // Return default on error
    }
    return default_val;
}

double JsonConverter::GetDoubleField(const nlohmann::json& json_obj, const std::string& field, double default_val) {
    try {
        if (json_obj.contains(field)) {
            if (json_obj[field].is_number()) {
                return json_obj[field].get<double>();
            } else if (json_obj[field].is_string()) {
                // Try to parse string as double
                std::string str_val = json_obj[field].get<std::string>();
                return std::stod(str_val);
            }
        }
    } catch (const std::exception&) {
        // Return default on error
    }
    return default_val;
}

int JsonConverter::GetIntField(const nlohmann::json& json_obj, const std::string& field, int default_val) {
    try {
        if (json_obj.contains(field)) {
            if (json_obj[field].is_number_integer()) {
                return json_obj[field].get<int>();
            } else if (json_obj[field].is_number()) {
                return static_cast<int>(json_obj[field].get<double>());
            } else if (json_obj[field].is_string()) {
                // Try to parse string as int
                std::string str_val = json_obj[field].get<std::string>();
                return std::stoi(str_val);
            }
        }
    } catch (const std::exception&) {
        // Return default on error
    }
    return default_val;
}

uint64_t JsonConverter::GetUInt64Field(const nlohmann::json& json_obj, const std::string& field, uint64_t default_val) {
    try {
        if (json_obj.contains(field)) {
            if (json_obj[field].is_number_unsigned()) {
                return json_obj[field].get<uint64_t>();
            } else if (json_obj[field].is_number_integer()) {
                auto val = json_obj[field].get<int64_t>();
                return val < 0 ? default_val : static_cast<uint64_t>(val);
            } else if (json_obj[field].is_string()) {
                auto str_val = json_obj[field].get<std::string>();
                return static_cast<uint64_t>(std::stoull(str_val));
            }
        }
    } catch (const std::exception&) {
        // Return default on error
    }
    return default_val;
}

bool JsonConverter::GetBoolField(const nlohmann::json& json_obj, const std::string& field, bool default_val) {
    try {
        if (json_obj.contains(field)) {
            if (json_obj[field].is_boolean()) {
                return json_obj[field].get<bool>();
            } else if (json_obj[field].is_string()) {
                std::string str_val = json_obj[field].get<std::string>();
                (void)std::transform(str_val.begin(), str_val.end(), str_val.begin(),
                              [](unsigned char c){ return std::tolower(c); });
                return str_val == "true" || str_val == "1" || str_val == "yes";
            } else if (json_obj[field].is_number()) {
                return json_obj[field].get<double>() != 0.0;
            }
        }
    } catch (const std::exception&) {
        // Return default on error
    }
    return default_val;
}

std::string JsonConverter::CreateErrorResponse(const std::string& error_message, int error_code) {
    json error_response = {
        {"status", "error"},
        {"message", error_message},
        {"error_code", error_code},
        {"timestamp", GetCurrentTimestamp()}
    };
    return error_response.dump();
}

std::string JsonConverter::CreateSuccessResponse(const std::string& message) {
    json success_response = {
        {"status", "success"},
        {"message", message},
        {"timestamp", GetCurrentTimestamp()}
    };
    return success_response.dump();
}

std::string JsonConverter::GetCurrentTimestamp() {
    auto now = std::chrono::system_clock::now();
    auto time_t = std::chrono::system_clock::to_time_t(now);
    
    std::tm tm_buf{};
    gmtime_s(&tm_buf, &time_t);
    std::stringstream ss;
    ss << std::put_time(&tm_buf, "%Y-%m-%dT%H:%M:%SZ");
    return ss.str();
}

std::string JsonConverter::FormatTimestamp(int64_t timestamp) {
    auto time_point = std::chrono::system_clock::from_time_t(timestamp);
    auto time_t = std::chrono::system_clock::to_time_t(time_point);
    
    std::tm tm_buf{};
    gmtime_s(&tm_buf, &time_t);
    std::stringstream ss;
    ss << std::put_time(&tm_buf, "%Y-%m-%d %H:%M:%S");
    return ss.str();
}

std::string JsonConverter::FormatTradeResult(const std::string& status, uint64_t ticket, double volume, bool is_close, const std::string& id) {
    json result = {
        {"status", status},
        {"ticket", ticket},
        {"volume", volume},
        {"is_close", is_close},
        {"id", id},
        {"timestamp", GetCurrentTimestamp()}
    };
    return result.dump();
}

std::string JsonConverter::FormatHealthResponse(const std::string& status, int queue_size, int net_position, double hedge_size) {
    json response = {
        {"status", status},
        {"queue_size", queue_size},
        {"net_position", net_position},
        {"hedge_size", hedge_size},
        {"timestamp", GetCurrentTimestamp()}
    };
    return response.dump();
}
