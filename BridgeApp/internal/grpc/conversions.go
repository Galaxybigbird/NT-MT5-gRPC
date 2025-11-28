package grpc

import (
	"strings"
	"time"

	trading "BridgeApp/internal/grpc/proto"
)

// Import the Trade type from main package
type Trade struct {
	ID                   string    `json:"id"`
	BaseID               string    `json:"base_id"`
	Time                 time.Time `json:"time"`
	Action               string    `json:"action"`
	Quantity             float64   `json:"quantity"`
	Price                float64   `json:"price"`
	TotalQuantity        int       `json:"total_quantity"`
	ContractNum          int       `json:"contract_num"`
	OrderType            string    `json:"order_type"`
	MeasurementPips      int       `json:"measurement_pips"`
	RawMeasurement       float64   `json:"raw_measurement"`
	Instrument           string    `json:"instrument"`
	AccountName          string    `json:"account_name"`
	NTBalance            float64   `json:"nt_balance,omitempty"`
	NTDailyPnL           float64   `json:"nt_daily_pnl,omitempty"`
	NTTradeResult        string    `json:"nt_trade_result,omitempty"`
	NTSessionTrades      int       `json:"nt_session_trades,omitempty"`
	MT5Ticket            uint64    `json:"mt5_ticket"`
	NTPointsPer1kLoss    float64   `json:"nt_points_per_1k_loss,omitempty"`
	EventType            string    `json:"event_type,omitempty"`
	ElasticProfitLevel   int32     `json:"elastic_profit_level,omitempty"`
	ElasticCurrentProfit float64   `json:"elastic_current_profit,omitempty"`
	LifecycleEvent       string    `json:"lifecycle_event,omitempty"`
	RemainingQuantity    float64   `json:"remaining_quantity,omitempty"`
	Seq                  uint64    `json:"seq,omitempty"`
	Epoch                uint64    `json:"epoch,omitempty"`
	Direction            string    `json:"direction,omitempty"`
	TargetLots           float64   `json:"target_lots,omitempty"`
}

type MT5CloseNotification struct {
	ID                 string    `json:"id"`
	BaseID             string    `json:"base_id"`
	Time               time.Time `json:"time"`
	Action             string    `json:"action"`
	Quantity           float64   `json:"quantity"`
	Price              float64   `json:"price"`
	TotalQuantity      float64   `json:"total_quantity"`
	ContractNum        int       `json:"contract_num"`
	OrderType          string    `json:"order_type"`
	MeasurementPips    float64   `json:"measurement_pips"`
	RawMeasurement     float64   `json:"raw_measurement"`
	Instrument         string    `json:"instrument"`
	AccountName        string    `json:"account_name"`
	NTBalance          float64   `json:"nt_balance"`
	NTDailyPnL         float64   `json:"nt_daily_pnl"`
	NTTradeResult      string    `json:"nt_trade_result"`
	NTSessionTrades    int       `json:"nt_session_trades"`
	MT5Ticket          uint64    `json:"mt5_ticket"`
	ClosureReason      string    `json:"closure_reason"`
	ElasticProfitLevel int       `json:"elastic_profit_level"`
}

// Internal struct definitions that match the app.go structures
// These should be moved to a common package in a real implementation

type InternalTrade struct {
	ID                string    `json:"id"`
	BaseID            string    `json:"base_id"`
	Time              time.Time `json:"time"`
	Action            string    `json:"action"`
	Quantity          float64   `json:"quantity"`
	Price             float64   `json:"price"`
	TotalQuantity     int       `json:"total_quantity"`
	ContractNum       int       `json:"contract_num"`
	OrderType         string    `json:"order_type,omitempty"`
	MeasurementPips   int       `json:"measurement_pips,omitempty"`
	RawMeasurement    float64   `json:"raw_measurement,omitempty"`
	Instrument        string    `json:"instrument_name,omitempty"`
	AccountName       string    `json:"account_name,omitempty"`
	NTBalance         float64   `json:"nt_balance,omitempty"`
	NTDailyPnL        float64   `json:"nt_daily_pnl,omitempty"`
	NTTradeResult     string    `json:"nt_trade_result,omitempty"`
	NTSessionTrades   int       `json:"nt_session_trades,omitempty"`
	MT5Ticket         uint64    `json:"mt5_ticket,omitempty"`
	NTPointsPer1kLoss float64   `json:"nt_points_per_1k_loss,omitempty"`
	// Event enrichment (optional)
	EventType            string  `json:"event_type,omitempty"`
	ElasticCurrentProfit float64 `json:"elastic_current_profit,omitempty"`
	ElasticProfitLevel   int32   `json:"elastic_profit_level,omitempty"`
	LifecycleEvent       string  `json:"lifecycle_event,omitempty"`
	RemainingQuantity    float64 `json:"remaining_quantity,omitempty"`
	Seq                  uint64  `json:"seq,omitempty"`
	Epoch                uint64  `json:"epoch,omitempty"`
	Direction            string  `json:"direction,omitempty"`
	TargetLots           float64 `json:"target_lots,omitempty"`
}

type InternalHedgeCloseNotification struct {
	EventType           string  `json:"event_type"`
	BaseID              string  `json:"base_id"`
	NTInstrumentSymbol  string  `json:"nt_instrument_symbol"`
	NTAccountName       string  `json:"nt_account_name"`
	ClosedHedgeQuantity float64 `json:"closed_hedge_quantity"`
	ClosedHedgeAction   string  `json:"closed_hedge_action"`
	Timestamp           string  `json:"timestamp"`
	ClosureReason       string  `json:"closure_reason"`
	MT5Ticket           uint64  `json:"mt5_ticket,omitempty"`
}

type InternalMT5TradeResult struct {
	Status  string  `json:"status"`
	Ticket  uint64  `json:"ticket"`
	Volume  float64 `json:"volume"`
	IsClose bool    `json:"is_close"`
	ID      string  `json:"id"`
}

type InternalElasticHedgeUpdate struct {
	EventType     string  `json:"event_type"`
	Action        string  `json:"action"`
	BaseID        string  `json:"base_id"`
	CurrentProfit float64 `json:"current_profit"`
	ProfitLevel   int32   `json:"profit_level"`
	Timestamp     string  `json:"timestamp"`
	MT5Ticket     uint64  `json:"mt5_ticket,omitempty"`
}

type InternalTrailingStopUpdate struct {
	EventType    string  `json:"event_type"`
	BaseID       string  `json:"base_id"`
	NewStopPrice float64 `json:"new_stop_price"`
	TrailingType string  `json:"trailing_type"`
	CurrentPrice float64 `json:"current_price"`
	Timestamp    string  `json:"timestamp"`
	MT5Ticket    uint64  `json:"mt5_ticket,omitempty"`
}

// convertProtoToInternalTrade converts a protobuf Trade to internal Trade format
func convertProtoToInternalTrade(proto *trading.Trade) *InternalTrade {
	var tradeTime time.Time
	if proto.Timestamp > 0 {
		tradeTime = time.Unix(proto.Timestamp, 0)
	} else {
		tradeTime = time.Now()
	}

	return &InternalTrade{
		ID:                proto.Id,
		BaseID:            proto.BaseId,
		Time:              tradeTime,
		Action:            proto.Action,
		Quantity:          proto.Quantity,
		Price:             proto.Price,
		TotalQuantity:     int(proto.TotalQuantity),
		ContractNum:       int(proto.ContractNum),
		OrderType:         proto.OrderType,
		MeasurementPips:   int(proto.MeasurementPips),
		RawMeasurement:    proto.RawMeasurement,
		Instrument:        proto.Instrument,
		AccountName:       proto.AccountName,
		NTBalance:         proto.NtBalance,
		NTDailyPnL:        proto.NtDailyPnl,
		NTTradeResult:     proto.NtTradeResult,
		NTSessionTrades:   int(proto.NtSessionTrades),
		MT5Ticket:         proto.Mt5Ticket,
		NTPointsPer1kLoss: proto.GetNtPointsPer_1KLoss(),
		// Event enrichment (if provided on Trade proto, e.g. for Action=EVENT)
		EventType:            proto.GetEventType(),
		ElasticCurrentProfit: proto.GetElasticCurrentProfit(),
		ElasticProfitLevel:   proto.GetElasticProfitLevel(),
		LifecycleEvent:       proto.GetLifecycleEvent(),
		RemainingQuantity:    proto.GetRemainingQuantity(),
		Seq:                  proto.GetSeq(),
		Epoch:                proto.GetEpoch(),
		Direction:            proto.GetDirection(),
		TargetLots:           proto.GetTargetLots(),
	}
}

// ConvertInternalToProtoTrade converts an internal Trade to protobuf Trade format (exported for testing)
func ConvertInternalToProtoTrade(internal *InternalTrade) *trading.Trade {
	return &trading.Trade{
		Id:                   internal.ID,
		BaseId:               internal.BaseID,
		Timestamp:            internal.Time.Unix(),
		Action:               internal.Action,
		Quantity:             internal.Quantity,
		Price:                internal.Price,
		TotalQuantity:        int32(internal.TotalQuantity),
		ContractNum:          int32(internal.ContractNum),
		OrderType:            internal.OrderType,
		MeasurementPips:      int32(internal.MeasurementPips),
		RawMeasurement:       internal.RawMeasurement,
		Instrument:           internal.Instrument,
		AccountName:          internal.AccountName,
		NtBalance:            internal.NTBalance,
		NtDailyPnl:           internal.NTDailyPnL,
		NtTradeResult:        internal.NTTradeResult,
		NtSessionTrades:      int32(internal.NTSessionTrades),
		Mt5Ticket:            internal.MT5Ticket,
		NtPointsPer_1KLoss:   internal.NTPointsPer1kLoss,
		EventType:            internal.EventType,
		ElasticCurrentProfit: internal.ElasticCurrentProfit,
		ElasticProfitLevel:   internal.ElasticProfitLevel,
		LifecycleEvent:       internal.LifecycleEvent,
		RemainingQuantity:    internal.RemainingQuantity,
		Seq:                  internal.Seq,
		Epoch:                internal.Epoch,
		Direction:            internal.Direction,
		TargetLots:           internal.TargetLots,
	}
}

// convertProtoToInternalHedgeClose converts protobuf HedgeCloseNotification to internal format
func convertProtoToInternalHedgeClose(proto *trading.HedgeCloseNotification) *InternalHedgeCloseNotification {
	return &InternalHedgeCloseNotification{
		EventType:           proto.EventType,
		BaseID:              proto.BaseId,
		NTInstrumentSymbol:  proto.NtInstrumentSymbol,
		NTAccountName:       proto.NtAccountName,
		ClosedHedgeQuantity: proto.ClosedHedgeQuantity,
		ClosedHedgeAction:   proto.ClosedHedgeAction,
		Timestamp:           proto.Timestamp,
		ClosureReason:       proto.ClosureReason,
		MT5Ticket:           proto.Mt5Ticket,
	}
}

// convertInternalToProtoHedgeClose converts internal HedgeCloseNotification to protobuf format
func convertInternalToProtoHedgeClose(internal *InternalHedgeCloseNotification) *trading.HedgeCloseNotification {
	return &trading.HedgeCloseNotification{
		EventType:           internal.EventType,
		BaseId:              internal.BaseID,
		NtInstrumentSymbol:  internal.NTInstrumentSymbol,
		NtAccountName:       internal.NTAccountName,
		ClosedHedgeQuantity: internal.ClosedHedgeQuantity,
		ClosedHedgeAction:   internal.ClosedHedgeAction,
		Timestamp:           internal.Timestamp,
		ClosureReason:       internal.ClosureReason,
		Mt5Ticket:           internal.MT5Ticket,
	}
}

// convertProtoToInternalMT5Result converts protobuf MT5TradeResult to internal format
func convertProtoToInternalMT5Result(proto *trading.MT5TradeResult) *InternalMT5TradeResult {
	return &InternalMT5TradeResult{
		Status:  proto.Status,
		Ticket:  proto.Ticket,
		Volume:  proto.Volume,
		IsClose: proto.IsClose,
		ID:      proto.Id,
	}
}

// convertInternalToProtoMT5Result converts internal MT5TradeResult to protobuf format
func convertInternalToProtoMT5Result(internal *InternalMT5TradeResult) *trading.MT5TradeResult {
	return &trading.MT5TradeResult{
		Status:  internal.Status,
		Ticket:  internal.Ticket,
		Volume:  internal.Volume,
		IsClose: internal.IsClose,
		Id:      internal.ID,
	}
}

// convertProtoToInternalElasticUpdate converts protobuf ElasticHedgeUpdate to internal format
func convertProtoToInternalElasticUpdate(proto *trading.ElasticHedgeUpdate) *InternalElasticHedgeUpdate {
	return &InternalElasticHedgeUpdate{
		EventType:     proto.EventType,
		Action:        proto.Action,
		BaseID:        proto.BaseId,
		CurrentProfit: proto.CurrentProfit,
		ProfitLevel:   proto.ProfitLevel,
		Timestamp:     proto.Timestamp,
		MT5Ticket:     proto.Mt5Ticket,
	}
}

// convertInternalToProtoElasticUpdate converts internal ElasticHedgeUpdate to protobuf format
func convertInternalToProtoElasticUpdate(internal *InternalElasticHedgeUpdate) *trading.ElasticHedgeUpdate {
	return &trading.ElasticHedgeUpdate{
		EventType:     internal.EventType,
		Action:        internal.Action,
		BaseId:        internal.BaseID,
		CurrentProfit: internal.CurrentProfit,
		ProfitLevel:   internal.ProfitLevel,
		Timestamp:     internal.Timestamp,
		Mt5Ticket:     internal.MT5Ticket,
	}
}

// convertProtoToInternalTrailingUpdate converts protobuf TrailingStopUpdate to internal format
func convertProtoToInternalTrailingUpdate(proto *trading.TrailingStopUpdate) *InternalTrailingStopUpdate {
	return &InternalTrailingStopUpdate{
		EventType:    proto.EventType,
		BaseID:       proto.BaseId,
		NewStopPrice: proto.NewStopPrice,
		TrailingType: proto.TrailingType,
		CurrentPrice: proto.CurrentPrice,
		Timestamp:    proto.Timestamp,
		MT5Ticket:    proto.Mt5Ticket,
	}
}

// convertInternalToProtoTrailingUpdate converts internal TrailingStopUpdate to protobuf format
func convertInternalToProtoTrailingUpdate(internal *InternalTrailingStopUpdate) *trading.TrailingStopUpdate {
	return &trading.TrailingStopUpdate{
		EventType:    internal.EventType,
		BaseId:       internal.BaseID,
		NewStopPrice: internal.NewStopPrice,
		TrailingType: internal.TrailingType,
		CurrentPrice: internal.CurrentPrice,
		Timestamp:    internal.Timestamp,
		Mt5Ticket:    internal.MT5Ticket,
	}
}

// convertInternalToMainTrade converts an InternalTrade to a struct that main can use
func convertInternalToMainTrade(internal *InternalTrade) interface{} {
	// Return as interface{} to let AddToTradeQueue handle JSON conversion
	return Trade{
		ID:                internal.ID,
		BaseID:            internal.BaseID,
		Time:              internal.Time,
		Action:            internal.Action,
		Quantity:          internal.Quantity,
		Price:             internal.Price,
		TotalQuantity:     internal.TotalQuantity,
		ContractNum:       internal.ContractNum,
		OrderType:         internal.OrderType,
		MeasurementPips:   internal.MeasurementPips,
		RawMeasurement:    internal.RawMeasurement,
		Instrument:        internal.Instrument,
		AccountName:       internal.AccountName,
		NTBalance:         internal.NTBalance,
		NTDailyPnL:        internal.NTDailyPnL,
		NTTradeResult:     internal.NTTradeResult,
		NTSessionTrades:   internal.NTSessionTrades,
		MT5Ticket:         internal.MT5Ticket,
		NTPointsPer1kLoss: internal.NTPointsPer1kLoss,
	}
}

// convertMT5CloseNotificationToProtoTrade converts an MT5 closure notification to a trading.Trade for streaming
func convertMT5CloseNotificationToProtoTrade(notification interface{}) *trading.Trade {
	switch n := notification.(type) {
	case MT5CloseNotification:
		return buildMT5CloseProto(n)
	case *MT5CloseNotification:
		if n == nil {
			return nil
		}
		return buildMT5CloseProto(*n)
	}

	// Fallback - try to extract what we can from the interface{}
	return &trading.Trade{
		Id:        "mt5_close_notification",
		Action:    "MT5_CLOSE_NOTIFICATION",
		Timestamp: time.Now().Unix(),
	}
}

func buildMT5CloseProto(n MT5CloseNotification) *trading.Trade {
	ntResult := n.NTTradeResult
	if strings.TrimSpace(n.ClosureReason) != "" {
		ntResult = n.ClosureReason
	}

	return &trading.Trade{
		Id:                 n.ID,
		BaseId:             n.BaseID,
		Timestamp:          n.Time.Unix(),
		Action:             n.Action,
		Quantity:           n.Quantity,
		Price:              n.Price,
		TotalQuantity:      int32(n.TotalQuantity),
		ContractNum:        int32(n.ContractNum),
		OrderType:          n.OrderType,
		MeasurementPips:    int32(n.MeasurementPips),
		RawMeasurement:     n.RawMeasurement,
		Instrument:         n.Instrument,
		AccountName:        n.AccountName,
		NtBalance:          n.NTBalance,
		NtDailyPnl:         n.NTDailyPnL,
		NtTradeResult:      ntResult,
		NtSessionTrades:    int32(n.NTSessionTrades),
		Mt5Ticket:          n.MT5Ticket,
		ElasticProfitLevel: int32(n.ElasticProfitLevel),
	}
}
