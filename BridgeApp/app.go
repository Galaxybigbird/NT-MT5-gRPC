package main

import (
	"context"
	"encoding/json"
	"fmt"
	"log"
	"os"
	"reflect"
	"strconv"
	"strings"
	"sync"
	"time"

	grpcserver "BridgeApp/internal/grpc"
	blog "BridgeApp/internal/logging"
)

// App struct
type App struct {
	ctx                  context.Context
	tradeQueue           chan Trade
	queueMux             sync.Mutex
	netNT                int
	hedgeLot             float64
	bridgeActive         bool
	addonConnected       bool
	tradeHistory         []Trade
	hedgebotActive       bool
	tradeLogSenderActive bool

	// gRPC server integration
	grpcServer *grpcserver.Server
	grpcPort   string

	// Addon connection tracking
	lastAddonRequestTime time.Time
	addonStatusMux       sync.Mutex
	// HedgeBot connection tracking
	hedgebotStatusMux sync.Mutex // Protects hedgebotActive and hedgebotLastPing
	hedgebotLastPing  time.Time  // Timestamp of the last successful ping from Hedgebot

	// MT5 ticket to BaseID mapping
	mt5TicketMux      sync.RWMutex
	mt5TicketToBaseId map[uint64]string   // MT5 ticket -> BaseID
	baseIdToMT5Ticket map[string]uint64   // BaseID -> last seen MT5 ticket (compat)
	baseIdToTickets   map[string][]uint64 // BaseID -> all MT5 tickets for this base
	baseIdCrossRef    map[string]string   // BaseID -> Related BaseID (for NT inconsistencies)

	// Metadata to aid resolution when BaseID mismatches occur
	baseIdToInstrument map[string]string // BaseID -> instrument symbol
	baseIdToAccount    map[string]string // BaseID -> NT account name

	// Pending NT-initiated CLOSE_HEDGE requests when no MT5 ticket is known yet
	pendingCloses map[string][]pendingClose // BaseID -> queued close intents

	// Track NT-initiated close requests by MT5 ticket to tag subsequent MT5 close results as acks
	ntCloseMux         sync.Mutex
	ntInitiatedTickets map[uint64]time.Time // ticket -> time marked

	// Cache NT sizing hints per BaseID so elastic events can carry them
	baseIdToNtPoints map[string]float64 // BaseID -> nt_points_per_1k_loss
	// Fallback cache per instrument to cover early elastic events before Buy/Sell caching
	instrumentToNtPts map[string]float64 // instrument -> nt_points_per_1k_loss

	// Trade sync state tracking (per base_id)
	tradeSyncMux    sync.RWMutex
	tradeSyncByBase map[string]*tradeSyncState

	// Recent elastic-close context to align/suppress subsequent generic MT5TradeResult closes
	elasticMux            sync.Mutex
	recentElasticByBase   map[string]elasticMark // BaseID -> last elastic close marker
	recentElasticByTicket map[uint64]elasticMark // MT5 ticket -> last elastic close marker

	// Sequence counter for unique elastic event IDs
	elasticSeqMux   sync.Mutex
	elasticSeqCount uint64
}

type tradeSyncState struct {
	BaseID     string
	Direction  string
	NtQuantity int
	Remaining  float64
	Instrument string
	Account    string
	Epoch      uint64
	Seq        uint64
	TargetLots float64
	LastUpdate time.Time
}

// elasticMark captures an elastic close signal context for short-lived correlation
type elasticMark struct {
	reason string
	when   time.Time
	ticket uint64
	qty    float64
}

// markElasticClose records an elastic close marker for correlation windows
func (a *App) markElasticClose(baseID string, ticket uint64, reason string, qty float64) {
	if strings.TrimSpace(baseID) == "" || !strings.HasPrefix(strings.ToLower(reason), "elastic_") {
		return
	}
	a.elasticMux.Lock()
	if a.recentElasticByBase == nil {
		a.recentElasticByBase = make(map[string]elasticMark)
	}
	if a.recentElasticByTicket == nil {
		a.recentElasticByTicket = make(map[uint64]elasticMark)
	}
	mk := elasticMark{reason: reason, when: time.Now(), ticket: ticket, qty: qty}
	a.recentElasticByBase[baseID] = mk
	if ticket != 0 {
		a.recentElasticByTicket[ticket] = mk
	}
	a.elasticMux.Unlock()
	log.Printf("gRPC: Marked recent elastic close context: base_id=%s ticket=%d reason=%s qty=%.4f", baseID, ticket, reason, qty)
}

// recentElasticFor returns an elastic marker if found for base or ticket within a TTL
func (a *App) recentElasticFor(baseID string, ticket uint64, within time.Duration) (elasticMark, bool) {
	a.elasticMux.Lock()
	defer a.elasticMux.Unlock()
	var mk elasticMark
	var ok bool
	// Prefer ticket match when available
	if ticket != 0 {
		if v, exists := a.recentElasticByTicket[ticket]; exists && time.Since(v.when) <= within {
			return v, true
		}
	}
	if v, exists := a.recentElasticByBase[baseID]; exists && time.Since(v.when) <= within {
		mk, ok = v, true
	}
	// Garbage collect stale entries occasionally
	if len(a.recentElasticByBase) > 0 || len(a.recentElasticByTicket) > 0 {
		cutoff := time.Now().Add(-within)
		for k, v := range a.recentElasticByBase {
			if v.when.Before(cutoff) {
				delete(a.recentElasticByBase, k)
			}
		}
		for k, v := range a.recentElasticByTicket {
			if v.when.Before(cutoff) {
				delete(a.recentElasticByTicket, k)
			}
		}
	}
	return mk, ok
}

// bestInstAcctFor returns the best-known instrument and account for a given BaseID.
// It first checks direct mappings, then falls back to cross-referenced BaseIDs if present.
func (a *App) bestInstAcctFor(baseID string) (string, string) {
	a.mt5TicketMux.RLock()
	defer a.mt5TicketMux.RUnlock()

	inst := strings.TrimSpace(a.baseIdToInstrument[baseID])
	acct := strings.TrimSpace(a.baseIdToAccount[baseID])
	if inst != "" && acct != "" {
		return inst, acct
	}

	// Try cross-referenced BaseID (NT inconsistencies)
	if rel, ok := a.baseIdCrossRef[baseID]; ok && rel != "" {
		if inst == "" {
			inst = strings.TrimSpace(a.baseIdToInstrument[rel])
		}
		if acct == "" {
			acct = strings.TrimSpace(a.baseIdToAccount[rel])
		}
	}
	return inst, acct
}

type Trade struct {
	ID              string    `json:"id"`      // Unique trade identifier
	BaseID          string    `json:"base_id"` // Base ID for multi-contract trades
	Time            time.Time `json:"time"`    // Timestamp of trade
	Action          string    `json:"action"`  // "buy" or "sell"
	Quantity        float64   `json:"quantity"`
	Price           float64   `json:"price"`
	TotalQuantity   int       `json:"total_quantity"` // Total contracts in this trade group
	ContractNum     int       `json:"contract_num"`   // Which contract this is (1-based)
	OrderType       string    `json:"order_type"`     // "ENTRY", "TP", "SL", "NT_CLOSE"
	MeasurementPips int       `json:"measurement_pips"`
	RawMeasurement  float64   `json:"raw_measurement"`
	Instrument      string    `json:"instrument"`
	AccountName     string    `json:"account_name"`

	// Enhanced NT Performance Data (omit when not provided to avoid resetting EA state)
	NTBalance       float64 `json:"nt_balance,omitempty"`
	NTDailyPnL      float64 `json:"nt_daily_pnl,omitempty"`
	NTTradeResult   string  `json:"nt_trade_result,omitempty"` // "win", "loss", "pending"
	NTSessionTrades int     `json:"nt_session_trades,omitempty"`

	// MT5 position tracking
	MT5Ticket         uint64  `json:"mt5_ticket"`                      // MT5 position ticket number (always include, even if 0)
	NTPointsPer1kLoss float64 `json:"nt_points_per_1k_loss,omitempty"` // Elastic sizing hint

	// Optional event enrichment (forwarded verbatim to MT5 stream JSON)
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

// pendingClose tracks a queued NT-initiated close request waiting for ticket resolution
type pendingClose struct {
	qty        int
	instrument string
	account    string
}

// addCrossRef safely records a bidirectional BaseID cross-reference.
// It is tolerant to empty/identical inputs and preserves existing mappings.
func (a *App) addCrossRef(aBase, bBase string) {
	if strings.TrimSpace(aBase) == "" || strings.TrimSpace(bBase) == "" || aBase == bBase {
		return
	}
	a.mt5TicketMux.Lock()
	defer a.mt5TicketMux.Unlock()
	if _, exists := a.baseIdCrossRef[aBase]; !exists {
		a.baseIdCrossRef[aBase] = bBase
		log.Printf("gRPC: Created cross-reference mapping: %s -> %s (on alignment)", aBase, bBase)
	}
	if _, exists := a.baseIdCrossRef[bBase]; !exists {
		a.baseIdCrossRef[bBase] = aBase
		log.Printf("gRPC: Created reverse cross-reference mapping: %s -> %s (on alignment)", bBase, aBase)
	}
}

// waitForTickets polls the in-memory ticket map for a BaseID until tickets appear or timeout.
// Returns the discovered tickets (may be empty) and does not mutate state.
func (a *App) waitForTickets(baseID string, maxWait time.Duration, poll time.Duration) []uint64 {
	if maxWait <= 0 || poll <= 0 {
		return nil
	}
	deadline := time.Now().Add(maxWait)
	for {
		a.mt5TicketMux.RLock()
		t := a.baseIdToTickets[baseID]
		a.mt5TicketMux.RUnlock()
		if len(t) > 0 {
			return t
		}
		if time.Now().After(deadline) {
			return nil
		}
		time.Sleep(poll)
	}
}

// resolveBaseIDToTickets attempts to find tickets for a BaseID strictly by direct mapping only.
// Cross-reference based resolution is DISABLED to avoid mis-closing the wrong hedge.
// Returns tickets and the actual BaseID they were found under (same as requested on success).
func (a *App) resolveBaseIDToTickets(requestedBaseID string) ([]uint64, string) {
	a.mt5TicketMux.RLock()
	defer a.mt5TicketMux.RUnlock()

	if tickets, found := a.baseIdToTickets[requestedBaseID]; found && len(tickets) > 0 {
		return tickets, requestedBaseID
	}
	return nil, ""
}

// detectAndStoreCrossReferences no longer performs heuristic mapping because trade_ids are generated by the strategy.
func (a *App) detectAndStoreCrossReferences(newBaseID string) {
	// No-op with strategy-owned trade_ids; cross-reference heuristics removed.
	return
}

// NewApp creates a new App application struct
func NewApp() *App {

	// Read configuration from environment variables
	grpcPort := getEnvString("BRIDGE_GRPC_PORT", "50051")

	log.Printf("Configuration: gRPC=true, gRPCPort=%s", grpcPort)

	app := &App{
		tradeQueue:           make(chan Trade, 100),
		hedgebotActive:       false, // Initialize HedgeBot as inactive
		tradeLogSenderActive: false,
		// gRPC configuration from environment
		grpcPort: grpcPort,
		// Initialize MT5 ticket mappings
		mt5TicketToBaseId:  make(map[uint64]string),
		baseIdToMT5Ticket:  make(map[string]uint64),
		baseIdToTickets:    make(map[string][]uint64),
		baseIdCrossRef:     make(map[string]string),
		baseIdToInstrument: make(map[string]string),
		baseIdToAccount:    make(map[string]string),
		pendingCloses:      make(map[string][]pendingClose),
		ntInitiatedTickets: make(map[uint64]time.Time),
		baseIdToNtPoints:   make(map[string]float64),
		instrumentToNtPts:  make(map[string]float64),
		tradeSyncByBase:    make(map[string]*tradeSyncState),
	}

	// Initialize gRPC server
	app.grpcServer = grpcserver.NewGRPCServer(app)

	// Initialize elastic correlation maps
	app.initElasticMaps()

	return app
}

// Helper functions for environment variable configuration
func getEnvString(key, defaultValue string) string {
	if value := os.Getenv(key); value != "" {
		return value
	}
	return defaultValue
}

// startup is called when the app starts. The context is saved
// so we can call the runtime methods
func (a *App) startup(ctx context.Context) {
	a.ctx = ctx

	// Start background goroutine to monitor addon connection status
	go func() {
		ticker := time.NewTicker(10 * time.Second)
		defer ticker.Stop()
		for {
			<-ticker.C

			a.addonStatusMux.Lock()
			// Do not forcibly mark disconnected on idle; only warn if stale.
			if a.addonConnected && time.Since(a.lastAddonRequestTime) > 2*time.Minute {
				log.Printf("Addon connection appears stale (last request: %s ago) — keeping connected=true until explicit disconnect", time.Since(a.lastAddonRequestTime))
			}
			a.addonStatusMux.Unlock()
		}
	}()

	// Start server initialization
	a.startServer()

	// Start background reconciler to dispatch pending closes by any available tickets
	go func() {
		// Lower cadence to reduce pending->dispatch latency; event-driven kicks still occur on state changes
		ticker := time.NewTicker(50 * time.Millisecond)
		defer ticker.Stop()
		for range ticker.C {
			a.reconcilePendingCloses()
		}
	}()
}

// startServer initializes the gRPC server
func (a *App) startServer() {

	log.Printf("=== Bridge Server Starting (alignment+pclose enabled) ===")
	log.Printf("Initial state:")
	log.Printf("Net position: %d", a.netNT)
	log.Printf("Hedge size: %.2f", a.hedgeLot)
	log.Printf("Queue size: %d", len(a.tradeQueue))
	log.Printf("gRPC enabled: true")

	// Start gRPC server ONLY - no HTTP fallback
	log.Printf("Starting gRPC server on port %s", a.grpcPort)
	if err := a.grpcServer.StartGRPCServer(a.grpcPort); err != nil {
		log.Printf("FATAL: Failed to start gRPC server: %v", err)
		panic("gRPC server failed to start - no fallback available")
	} else {
		log.Printf("gRPC server started successfully on port %s", a.grpcPort)
	}

	a.bridgeActive = true
}

// shutdown is called when the app terminates
func (a *App) shutdown(ctx context.Context) {
	log.Printf("Application shutting down...")
	a.DisableAllProtocols("app shutdown")
	// Flush unified logger + Sentry
	blog.L().Shutdown()
}

// === FRONTEND API FUNCTIONS ===

// AttemptReconnect restarts the gRPC server (for pure gRPC mode)
func (a *App) AttemptReconnect(enableGRPC, enableHTTP, enableDual bool) map[string]interface{} {
	log.Printf("Frontend requested reconnect - pure gRPC mode only")

	// In pure gRPC mode, we ignore the parameters and just ensure gRPC is running
	if !a.bridgeActive {
		log.Printf("Restarting gRPC server...")
		if err := a.grpcServer.StartGRPCServer(a.grpcPort); err != nil {
			log.Printf("ERROR: Failed to restart gRPC server: %v", err)
			return map[string]interface{}{
				"success": false,
				"message": "Failed to restart gRPC server",
				"error":   err.Error(),
			}
		}
		a.bridgeActive = true
	}

	return map[string]interface{}{
		"success": true,
		"message": "gRPC server is active",
		"grpc":    true,
		"http":    false,
	}
}

// GetStatus returns the current bridge status
func (a *App) GetStatus() map[string]interface{} {
	a.queueMux.Lock()
	defer a.queueMux.Unlock()

	a.addonStatusMux.Lock()
	addonConnected := a.addonConnected
	a.addonStatusMux.Unlock()

	a.hedgebotStatusMux.Lock()
	hedgebotActive := a.hedgebotActive
	a.hedgebotStatusMux.Unlock()

	return map[string]interface{}{
		"bridgeActive":         a.bridgeActive,
		"addonConnected":       addonConnected,
		"hedgebotActive":       hedgebotActive,
		"tradeLogSenderActive": a.tradeLogSenderActive,
		"netPosition":          a.netNT,
		"hedgeSize":            a.hedgeLot,
		"queueSize":            len(a.tradeQueue),
	}
}

// GetNetPosition returns the current net position
func (a *App) GetNetPosition() int {
	return a.netNT
}

// GetHedgeSize returns the current hedge size
func (a *App) GetHedgeSize() float64 {
	return a.hedgeLot
}

// GetQueueSize returns the current queue size
func (a *App) GetQueueSize() int {
	return len(a.tradeQueue)
}

// IsAddonConnected returns whether the addon is connected
func (a *App) IsAddonConnected() bool {
	a.addonStatusMux.Lock()
	defer a.addonStatusMux.Unlock()
	return a.addonConnected
}

// SetAddonConnected sets the addon connection status
func (a *App) SetAddonConnected(connected bool) {
	a.addonStatusMux.Lock()
	defer a.addonStatusMux.Unlock()
	a.addonConnected = connected
	if connected {
		a.lastAddonRequestTime = time.Now()
	}
}

// IsHedgebotActive returns whether the hedgebot is active
func (a *App) IsHedgebotActive() bool {
	a.hedgebotStatusMux.Lock()
	defer a.hedgebotStatusMux.Unlock()
	return a.hedgebotActive
}

// SetHedgebotActive sets the hedgebot active status
func (a *App) SetHedgebotActive(active bool) {
	a.hedgebotStatusMux.Lock()
	prev := a.hedgebotActive
	a.hedgebotActive = active
	if active {
		a.hedgebotLastPing = time.Now()
	}
	a.hedgebotStatusMux.Unlock()
	// Only log on state changes to avoid spam
	if prev != active {
		log.Printf("Hedgebot active status changed: %v -> %v", prev, active)
	}
}

// GetTradeQueue returns the current trade queue
func (a *App) GetTradeQueue() chan interface{} {
	// Convert to interface{} channel for gRPC compatibility
	ch := make(chan interface{}, len(a.tradeQueue))

	a.queueMux.Lock()
	defer a.queueMux.Unlock()

	// Copy current trades to the interface channel
	for {
		select {
		case trade := <-a.tradeQueue:
			ch <- trade
		default:
			close(ch)
			return ch
		}
	}
}

// PollTradeFromQueue returns a trade from the queue (non-blocking)
func (a *App) PollTradeFromQueue() interface{} {
	select {
	case trade := <-a.tradeQueue:
		return trade
	default:
		return nil
	}
}

// AddToTradeQueue adds a trade to the queue
func (a *App) AddToTradeQueue(trade interface{}) error {
	// Use reflection to convert the incoming trade to our Trade type
	var t Trade

	log.Printf("AddToTradeQueue: Received trade type: %T", trade)

	// Always use JSON marshaling for consistent conversion regardless of type
	// Support both internal.grpc.InternalTrade and main.Trade shapes
	jsonBytes, err := json.Marshal(trade)
	if err != nil {
		log.Printf("AddToTradeQueue: Failed to marshal trade: %v", err)
		return fmt.Errorf("failed to marshal trade: %v", err)
	}

	// First try to unmarshal directly into our Trade struct
	if err := json.Unmarshal(jsonBytes, &t); err != nil {
		// Fallback: adapt known field name mismatches if any
		// e.g., internal uses 'instrument' json tag, Time as unix seconds via 'timestamp' when coming from proto
		var aux map[string]interface{}
		if err2 := json.Unmarshal(jsonBytes, &aux); err2 == nil {
			// Normalize keys
			if v, ok := aux["instrument_name"]; ok {
				aux["instrument"] = v
			}
			if v, ok := aux["timestamp"]; ok {
				// timestamp (seconds) -> time
				switch tv := v.(type) {
				case float64:
					aux["time"] = time.Unix(int64(tv), 0)
				case int64:
					aux["time"] = time.Unix(tv, 0)
				}
			}
			// Try again after normalization
			if reb, err3 := json.Marshal(aux); err3 == nil {
				if err4 := json.Unmarshal(reb, &t); err4 != nil {
					log.Printf("AddToTradeQueue: Failed to unmarshal normalized trade: %v", err4)
					return fmt.Errorf("failed to unmarshal trade: %v", err4)
				}
			} else {
				log.Printf("AddToTradeQueue: Failed to re-marshal normalized trade: %v", err3)
				return fmt.Errorf("failed to marshal trade: %v", err3)
			}
		} else {
			log.Printf("AddToTradeQueue: Failed to unmarshal trade to map: %v", err2)
			return fmt.Errorf("failed to unmarshal trade: %v", err)
		}
	}

	log.Printf("AddToTradeQueue: Successfully converted trade - ID: %s, Action: %s", t.ID, t.Action)

	// If this is an EVENT trade, emit a concise debug snapshot of the enrichment payload
	if strings.EqualFold(strings.TrimSpace(t.Action), "EVENT") {
		log.Printf("DEBUG: EVENT enqueue base_id=%s event_type=%s elastic_current_profit=%.4f elastic_profit_level=%d nt_points_per_1k_loss=%.4f", strings.TrimSpace(t.BaseID), strings.TrimSpace(t.EventType), t.ElasticCurrentProfit, t.ElasticProfitLevel, t.NTPointsPer1kLoss)
	}

	// Track instrument/account and nt_points_per_1k_loss per BaseID for later use
	if b := strings.TrimSpace(t.BaseID); b != "" {
		actLower := strings.ToLower(strings.TrimSpace(t.Action))
		lifecycleLower := strings.ToLower(strings.TrimSpace(t.LifecycleEvent))
		if actLower == "" && lifecycleLower != "" {
			actLower = lifecycleLower
		}
		if actLower == "buy" || actLower == "sell" || actLower == "open" {
			a.mt5TicketMux.Lock()
			if inst := strings.TrimSpace(t.Instrument); inst != "" {
				a.baseIdToInstrument[b] = inst
			}
			if acct := strings.TrimSpace(t.AccountName); acct != "" {
				a.baseIdToAccount[b] = acct
			}
			// Cache NT sizing hint if present and > 0
			if t.NTPointsPer1kLoss > 0 {
				a.baseIdToNtPoints[b] = t.NTPointsPer1kLoss
				if inst := strings.TrimSpace(t.Instrument); inst != "" {
					// Also cache per instrument as a fallback for early elastic events
					a.instrumentToNtPts[inst] = t.NTPointsPer1kLoss
				}
			}
			a.mt5TicketMux.Unlock()
		}
	}

	// Handle lifecycle control events (OPEN/PARTIAL/CLOSED/HEDGE_SET_TARGET)
	lifecycle := strings.ToUpper(strings.TrimSpace(t.LifecycleEvent))
	if lifecycle == "" {
		lifecycle = strings.ToUpper(strings.TrimSpace(t.Action))
	}

	switch lifecycle {
	case "OPEN", "PARTIAL", "CLOSED":
		a.handleLifecycleEvent(t)
		return nil
	case "HEDGE_SET_TARGET":
		if !a.handleHedgeSetTarget(t) {
			return nil
		}
	}

	select {
	case a.tradeQueue <- t:
		return nil
	default:
		return fmt.Errorf("trade queue is full")
	}
}

// handleLifecycleEvent updates local trade sync state based on OPEN/PARTIAL/CLOSED events from NT.
func (a *App) handleLifecycleEvent(t Trade) {
	baseID := strings.TrimSpace(t.BaseID)
	if baseID == "" {
		log.Printf("gRPC: Ignoring lifecycle event without base_id: action=%s id=%s", t.Action, t.ID)
		return
	}

	event := strings.ToUpper(strings.TrimSpace(t.LifecycleEvent))
	if event == "" {
		event = strings.ToUpper(strings.TrimSpace(t.Action))
	}
	if event == "" {
		return
	}

	a.tradeSyncMux.Lock()
	defer a.tradeSyncMux.Unlock()

	state, exists := a.tradeSyncByBase[baseID]

	incomingEpoch := t.Epoch
	if incomingEpoch == 0 && exists {
		incomingEpoch = state.Epoch
	}

	if exists {
		// Epoch comparison to gate stale messages
		if state.Epoch > 0 && incomingEpoch > 0 {
			if incomingEpoch < state.Epoch {
				log.Printf("gRPC: Dropping stale lifecycle %s for %s (epoch %d < %d)", event, baseID, incomingEpoch, state.Epoch)
				return
			}
			if incomingEpoch > state.Epoch {
				exists = false
				state = nil
			}
		}
		// sequence guard (only when same epoch)
		if exists && incomingEpoch == state.Epoch && t.Seq > 0 && state.Seq > 0 && t.Seq <= state.Seq {
			log.Printf("gRPC: Dropping stale lifecycle %s for %s (seq %d <= %d)", event, baseID, t.Seq, state.Seq)
			return
		}
	}

	if !exists {
		state = &tradeSyncState{BaseID: baseID}
		a.tradeSyncByBase[baseID] = state
	}

	if incomingEpoch > 0 {
		state.Epoch = incomingEpoch
	}
	if t.Seq > 0 {
		state.Seq = t.Seq
	} else {
		state.Seq++
	}

	if dir := strings.TrimSpace(t.Direction); dir != "" {
		state.Direction = strings.ToUpper(dir)
	}
	if state.Direction == "" {
		state.Direction = strings.ToUpper(strings.TrimSpace(t.Action))
	}

	if t.TotalQuantity > 0 {
		state.NtQuantity = int(t.TotalQuantity)
	} else if t.Quantity > 0 {
		state.NtQuantity = int(t.Quantity)
	}

	if t.RemainingQuantity > 0 {
		state.Remaining = t.RemainingQuantity
	} else if strings.EqualFold(event, "OPEN") {
		state.Remaining = float64(state.NtQuantity)
	}

	if inst := strings.TrimSpace(t.Instrument); inst != "" {
		state.Instrument = inst
	}
	if acct := strings.TrimSpace(t.AccountName); acct != "" {
		state.Account = acct
	}

	state.LastUpdate = time.Now()

	log.Printf("gRPC: Lifecycle %s processed for base_id=%s epoch=%d seq=%d remaining=%.2f", event, baseID, state.Epoch, state.Seq, state.Remaining)

	if event == "CLOSED" {
		delete(a.tradeSyncByBase, baseID)
	}
}

// handleHedgeSetTarget updates local state for a HEDGE_SET_TARGET command and returns true if it should be forwarded to MT5.
func (a *App) handleHedgeSetTarget(t Trade) bool {
	baseID := strings.TrimSpace(t.BaseID)
	if baseID == "" {
		log.Printf("gRPC: Ignoring HEDGE_SET_TARGET without base_id: id=%s", t.ID)
		return false
	}

	a.tradeSyncMux.Lock()
	defer a.tradeSyncMux.Unlock()

	state, exists := a.tradeSyncByBase[baseID]
	if !exists {
		state = &tradeSyncState{BaseID: baseID}
		a.tradeSyncByBase[baseID] = state
	}

	incomingEpoch := t.Epoch
	if incomingEpoch == 0 {
		incomingEpoch = state.Epoch
	}

	if state.Epoch > 0 && incomingEpoch > 0 {
		if incomingEpoch < state.Epoch {
			log.Printf("gRPC: Dropping stale HEDGE_SET_TARGET for %s (epoch %d < %d)", baseID, incomingEpoch, state.Epoch)
			return false
		}
	}

	if t.Seq > 0 && state.Seq > 0 && incomingEpoch == state.Epoch && t.Seq <= state.Seq {
		log.Printf("gRPC: Dropping stale HEDGE_SET_TARGET for %s (seq %d <= %d)", baseID, t.Seq, state.Seq)
		return false
	}

	if incomingEpoch > 0 {
		state.Epoch = incomingEpoch
	}
	if t.Seq > 0 {
		state.Seq = t.Seq
	} else {
		state.Seq++
	}

	state.TargetLots = t.TargetLots
	state.LastUpdate = time.Now()
	if inst := strings.TrimSpace(t.Instrument); inst != "" {
		state.Instrument = inst
	}
	if acct := strings.TrimSpace(t.AccountName); acct != "" {
		state.Account = acct
	}

	log.Printf("gRPC: Updated hedge target for base_id=%s target_lots=%.4f epoch=%d seq=%d", baseID, state.TargetLots, state.Epoch, state.Seq)
	return true
}

// AddToTradeHistory adds a trade to the history
func (a *App) AddToTradeHistory(trade interface{}) {
	// Use the same conversion logic as AddToTradeQueue
	var t Trade

	switch v := trade.(type) {
	case Trade:
		t = v
	default:
		// Use JSON marshaling/unmarshaling as a universal converter
		jsonBytes, err := json.Marshal(trade)
		if err != nil {
			log.Printf("Failed to marshal trade for history: %v", err)
			return
		}

		if err := json.Unmarshal(jsonBytes, &t); err != nil {
			log.Printf("Failed to unmarshal trade for history: %v", err)
			return
		}
	}

	a.tradeHistory = append(a.tradeHistory, t)
}

// GetTradeHistory returns the trade history
func (a *App) GetTradeHistory() []Trade {
	return a.tradeHistory
}

// initElasticMaps ensures new elastic correlation maps are initialized
func (a *App) initElasticMaps() {
	a.elasticMux.Lock()
	if a.recentElasticByBase == nil {
		a.recentElasticByBase = make(map[string]elasticMark)
	}
	if a.recentElasticByTicket == nil {
		a.recentElasticByTicket = make(map[uint64]elasticMark)
	}
	a.elasticMux.Unlock()
}

// Placeholder implementations for gRPC interface compliance
func (a *App) HandleHedgeCloseNotification(notification interface{}) error {
	log.Printf("gRPC: Received hedge close notification: %+v", notification)

	// Parse the CLOSE_HEDGE notification
	baseID := getBaseIDFromRequest(notification)
	if baseID == "" {
		log.Printf("gRPC: Warning - hedge close notification missing BaseID")
		return fmt.Errorf("hedge close notification missing BaseID")
	}

	// Determine source of the closure by checking closure_reason
	closureReason := getClosureReasonFromRequest(notification)
	log.Printf("gRPC: Closure reason: %s for BaseID: %s", closureReason, baseID)

	// Treat both native MT5_* reasons and elastic_* reasons as MT5-originated signals.
	// Elastic reasons are intentful EA-side close events (partial or completion) and must NOT
	// be turned into NT-initiated CLOSE_HEDGE requests.
	lowerReason := strings.ToLower(strings.TrimSpace(closureReason))
	isMT5Closure := closureReason == "MT5_position_closed" ||
		closureReason == "MT5_stop_loss" ||
		closureReason == "MT5_manual_close" ||
		closureReason == "MT5_take_profit" ||
		strings.HasPrefix(lowerReason, "elastic_")

	if isMT5Closure {
		// MT5 initiated the closure - need to notify NinjaTrader
		log.Printf("gRPC: MT5-initiated closure detected - notifying NinjaTrader for BaseID: %s", baseID)

		// Create a close notification trade for NinjaTrader
		closeNotification := struct {
			ID              string    `json:"id"`
			BaseID          string    `json:"base_id"`
			Time            time.Time `json:"time"`
			Action          string    `json:"action"`
			Quantity        float64   `json:"quantity"`
			Price           float64   `json:"price"`
			TotalQuantity   float64   `json:"total_quantity"`
			ContractNum     int       `json:"contract_num"`
			OrderType       string    `json:"order_type"`
			MeasurementPips float64   `json:"measurement_pips"`
			RawMeasurement  float64   `json:"raw_measurement"`
			Instrument      string    `json:"instrument"`
			AccountName     string    `json:"account_name"`
			NTBalance       float64   `json:"nt_balance"`
			NTDailyPnL      float64   `json:"nt_daily_pnl"`
			NTTradeResult   string    `json:"nt_trade_result"`
			NTSessionTrades int       `json:"nt_session_trades"`
			MT5Ticket       uint64    `json:"mt5_ticket"`
			ClosureReason   string    `json:"closure_reason"`
		}{
			ID:              fmt.Sprintf("mt5close_%d", time.Now().UnixNano()),
			BaseID:          baseID,
			Time:            time.Now(),
			Action:          "MT5_CLOSE_NOTIFICATION",
			Quantity:        getQuantityFromRequest(notification),
			Price:           0.0,
			TotalQuantity:   getQuantityFromRequest(notification),
			ContractNum:     1,
			OrderType:       "MT5_CLOSE",
			MeasurementPips: 0.0,
			RawMeasurement:  0.0,
			Instrument:      getInstrumentFromRequest(notification),
			AccountName:     getAccountFromRequest(notification),
			NTBalance:       0.0,
			NTDailyPnL:      0.0,
			NTTradeResult:   "mt5_closed",
			NTSessionTrades: 0,
			MT5Ticket:       getMT5TicketFromRequest(notification),
			ClosureReason:   closureReason,
		}

		// If this is an elastic signaled close, mark context for correlation and avoid
		// mutating ticket pools on partials (MT5 position remains open with reduced volume).
		if strings.HasPrefix(lowerReason, "elastic_") {
			a.markElasticClose(baseID, closeNotification.MT5Ticket, closureReason, closeNotification.Quantity)
			// On elastic partials, do not attempt ticket inference (we don't want to pop tickets)
			// and do not prune mappings below. If the EA omitted the ticket, we keep it as 0.
			if strings.EqualFold(lowerReason, "elastic_partial_close") {
				// Clear any stale pending NT CLOSE_HEDGE accidentally recorded for this BaseID
				// to prevent immediate re-closing of newly opened tickets.
				a.mt5TicketMux.Lock()
				if _, had := a.pendingCloses[baseID]; had {
					delete(a.pendingCloses, baseID)
					log.Printf("gRPC: Cleared pending CLOSE_HEDGE for BaseID %s due to elastic_partial_close (prevent premature closures).", baseID)
				}
				a.mt5TicketMux.Unlock()
			}
		} else {
			// Non-elastic MT5 close: if MT5 didn't include a ticket, try to infer one
			if closeNotification.MT5Ticket == 0 {
				inferred := uint64(0)
				a.mt5TicketMux.Lock()
				if list, ok := a.baseIdToTickets[baseID]; ok && len(list) > 0 {
					inferred = list[0]
					remaining := list[1:]
					if len(remaining) == 0 {
						delete(a.baseIdToTickets, baseID)
					} else {
						a.baseIdToTickets[baseID] = remaining
					}
					if cur, ok2 := a.baseIdToMT5Ticket[baseID]; ok2 && cur == inferred {
						delete(a.baseIdToMT5Ticket, baseID)
					}
					delete(a.mt5TicketToBaseId, inferred)
				}
				a.mt5TicketMux.Unlock()
				if inferred != 0 {
					closeNotification.MT5Ticket = inferred
					log.Printf("DEBUG: Inferred MT5 ticket %d for close notification (BaseID: %s) due to missing ticket from MT5; ensures NT processes unique sequential closes.", inferred, baseID)
				} else {
					log.Printf("DEBUG: No MT5 ticket extracted from notification and none inferred; NT may fall back to base_id-only logic")
				}
			} else {
				log.Printf("DEBUG: Extracted MT5 ticket %d from notification (type %T)", closeNotification.MT5Ticket, notification)
			}
		}

		// Broadcast MT5 closure notifications to NT streams only (not MT5 streams to prevent circular trades)
		// This notifies NT to close corresponding positions when MT5 closes a hedge
		log.Printf("gRPC: Broadcasting MT5 closure notification to NT streams for BaseID: %s", baseID)
		a.grpcServer.BroadcastMT5CloseToNTStreams(closeNotification)

		// Prune stored ticket mappings only for non-elastic or elastic completion signals.
		// For elastic_partial_close we keep mappings intact (position remains open).
		if closeNotification.MT5Ticket != 0 && !strings.EqualFold(lowerReason, "elastic_partial_close") {
			a.mt5TicketMux.Lock()
			delete(a.mt5TicketToBaseId, closeNotification.MT5Ticket)
			if cur, ok := a.baseIdToMT5Ticket[baseID]; ok && cur == closeNotification.MT5Ticket {
				delete(a.baseIdToMT5Ticket, baseID)
			}
			if list, ok := a.baseIdToTickets[baseID]; ok {
				nl := make([]uint64, 0, len(list))
				for _, v := range list {
					if v != closeNotification.MT5Ticket {
						nl = append(nl, v)
					}
				}
				if len(nl) == 0 {
					delete(a.baseIdToTickets, baseID)
				} else {
					a.baseIdToTickets[baseID] = nl
				}
			}
			a.mt5TicketMux.Unlock()
			log.Printf("gRPC: Pruned MT5 ticket mapping for closed ticket %d (BaseID: %s)", closeNotification.MT5Ticket, baseID)
		}

		log.Printf("gRPC: Successfully sent MT5 closure notification to NinjaTrader for BaseID: %s", baseID)
		return nil
	}

	// Not an MT5/elastic close notification; ignore here (NT-originated closes are handled via HandleNTCloseHedgeRequest entrypoint)
	log.Printf("gRPC: Non-MT5 close notification ignored in HandleHedgeCloseNotification (BaseID: %s, reason=%s)", baseID, closureReason)
	return nil
}

func (a *App) HandleMT5TradeResult(result interface{}) error {
	log.Printf("gRPC: Received MT5 trade result: %+v", result)

	// Handle different types of results that might be passed
	switch mt5Result := result.(type) {
	case *grpcserver.InternalMT5TradeResult:
		// Direct struct pointer
		if mt5Result.Ticket != 0 && mt5Result.ID != "" {
			if mt5Result.IsClose {
				// Closure result: prune mapping and notify NT so NT won't keep retrying
				base := mt5Result.ID
				ticket := mt5Result.Ticket
				// Before pruning/broadcasting, check for recent elastic context to avoid overriding intent
				if mk, ok := a.recentElasticFor(base, ticket, 3*time.Second); ok {
					// If the elastic reason was a partial, we suppress this generic MT5_position_closed broadcast
					if strings.EqualFold(mk.reason, "elastic_partial_close") {
						log.Printf("gRPC: Suppressing generic MT5TradeResult close for ticket %d (BaseID: %s) due to recent elastic_partial_close context.", ticket, base)
						// Still prune the ticket mapping below to keep state consistent
					} else {
						// Reclassify generic reason to the elastic context (e.g., elastic_completion)
						log.Printf("gRPC: Reclassifying MT5TradeResult close for ticket %d (BaseID: %s) from MT5_position_closed to %s due to recent elastic context.", ticket, base, mk.reason)
					}
				}
				a.mt5TicketMux.Lock()
				// Remove from reverse map and single-ticket map
				delete(a.mt5TicketToBaseId, ticket)
				if cur, ok := a.baseIdToMT5Ticket[base]; ok && cur == ticket {
					delete(a.baseIdToMT5Ticket, base)
				}
				// Remove from ticket pool for this BaseID
				if list, ok := a.baseIdToTickets[base]; ok && len(list) > 0 {
					nl := make([]uint64, 0, len(list))
					for _, v := range list {
						if v != ticket {
							nl = append(nl, v)
						}
					}
					if len(nl) == 0 {
						delete(a.baseIdToTickets, base)
					} else {
						a.baseIdToTickets[base] = nl
					}
				}
				a.mt5TicketMux.Unlock()

				// Determine origin for tagging: if NT recently requested this ticket to close, mark as NT ack
				orderType := "MT5_CLOSE"
				a.ntCloseMux.Lock()
				if t, ok := a.ntInitiatedTickets[ticket]; ok {
					if time.Since(t) <= 5*time.Second { // within TTL
						orderType = "NT_CLOSE_ACK"
					}
					// Clean up tracked ticket regardless (one-shot)
					delete(a.ntInitiatedTickets, ticket)
				}
				a.ntCloseMux.Unlock()

				// Decide closure reason and whether to suppress based on recent elastic context
				closureReason := "MT5_position_closed"
				suppress := false
				if mk, ok := a.recentElasticFor(base, ticket, 3*time.Second); ok {
					if strings.EqualFold(mk.reason, "elastic_partial_close") {
						// Suppress broadcasting this generic close; partial already communicated intent
						suppress = true
					} else {
						closureReason = mk.reason // carry over elastic_completion, etc.
					}
				}

				// Broadcast MT5 close notification to NT streams (idempotent on NT side)
				closeNotification := struct {
					ID              string    `json:"id"`
					BaseID          string    `json:"base_id"`
					Time            time.Time `json:"time"`
					Action          string    `json:"action"`
					Quantity        float64   `json:"quantity"`
					Price           float64   `json:"price"`
					TotalQuantity   float64   `json:"total_quantity"`
					ContractNum     int       `json:"contract_num"`
					OrderType       string    `json:"order_type"`
					MeasurementPips float64   `json:"measurement_pips"`
					RawMeasurement  float64   `json:"raw_measurement"`
					Instrument      string    `json:"instrument"`
					AccountName     string    `json:"account_name"`
					NTBalance       float64   `json:"nt_balance"`
					NTDailyPnL      float64   `json:"nt_daily_pnl"`
					NTTradeResult   string    `json:"nt_trade_result"`
					NTSessionTrades int       `json:"nt_session_trades"`
					MT5Ticket       uint64    `json:"mt5_ticket"`
					ClosureReason   string    `json:"closure_reason"`
				}{
					ID:              fmt.Sprintf("mt5close_result_%d", time.Now().UnixNano()),
					BaseID:          base,
					Time:            time.Now(),
					Action:          "MT5_CLOSE_NOTIFICATION",
					Quantity:        mt5Result.Volume,
					Price:           0,
					TotalQuantity:   mt5Result.Volume,
					ContractNum:     1,
					OrderType:       orderType,
					MeasurementPips: 0,
					RawMeasurement:  0,
					Instrument:      func() string { i, _ := a.bestInstAcctFor(base); return i }(),
					AccountName:     func() string { _, ac := a.bestInstAcctFor(base); return ac }(),
					NTBalance:       0,
					NTDailyPnL:      0,
					NTTradeResult:   "mt5_closed",
					NTSessionTrades: 0,
					MT5Ticket:       ticket,
					ClosureReason:   closureReason,
				}
				if suppress {
					log.Printf("gRPC: Skipped broadcasting MT5 close for ticket %d (BaseID: %s) due to elastic_partial_close context.", ticket, base)
				} else {
					a.grpcServer.BroadcastMT5CloseToNTStreams(closeNotification)
					log.Printf("gRPC: Pruned mapping and broadcasted MT5 close for ticket %d (BaseID: %s) with reason %s", ticket, base, closureReason)
				}
			} else {
				// Open/fill result: record mapping and try to drain pendings
				a.mt5TicketMux.Lock()
				a.mt5TicketToBaseId[mt5Result.Ticket] = mt5Result.ID
				a.baseIdToMT5Ticket[mt5Result.ID] = mt5Result.Ticket
				// append unique ticket to list
				list := a.baseIdToTickets[mt5Result.ID]
				seen := false
				for _, v := range list {
					if v == mt5Result.Ticket {
						seen = true
						break
					}
				}
				if !seen {
					a.baseIdToTickets[mt5Result.ID] = append(list, mt5Result.Ticket)
				}

				// Auto-detect potential cross-references for related base_ids
				a.detectAndStoreCrossReferences(mt5Result.ID)

				// Capture available tickets and any pending closes (including correlated) before unlocking
				available := append([]uint64(nil), a.baseIdToTickets[mt5Result.ID]...)
				pend := append([]pendingClose(nil), a.pendingCloses[mt5Result.ID]...)
				// Also absorb pending closes from correlated BaseIDs (crossref or same inst/acct)
				if len(a.pendingCloses) > 0 {
					base := mt5Result.ID
					instB := a.baseIdToInstrument[base]
					acctB := a.baseIdToAccount[base]
					for pBase, plist := range a.pendingCloses {
						if pBase == base || len(plist) == 0 {
							continue
						}
						// Crossref relation
						rel := a.baseIdCrossRef[pBase]
						rev := a.baseIdCrossRef[base]
						sameRel := (rel == base) || (rev == pBase)
						// Instrument/account correlation (when available for both)
						instP, hasInstP := a.baseIdToInstrument[pBase]
						acctP, hasAcctP := a.baseIdToAccount[pBase]
						sameIA := false
						if instB != "" && acctB != "" && hasInstP && hasAcctP {
							sameIA = (instP == instB && acctP == acctB)
						}
						if sameRel || sameIA {
							pend = append(pend, plist...)
							delete(a.pendingCloses, pBase)
							log.Printf("gRPC: Merged %d pending close(s) from BaseID %s into %s (reason=%s)", len(plist), pBase, base, func() string {
								if sameRel {
									return "crossref"
								} else {
									return "inst_acct"
								}
							}())
						}
					}
				}
				a.mt5TicketMux.Unlock()

				log.Printf("gRPC: Stored MT5 ticket mapping - Ticket: %d -> BaseID: %s (count=%d)", mt5Result.Ticket, mt5Result.ID, len(available))

				// If there are pending NT closes for this BaseID, dispatch them now using explicit tickets
				if len(pend) > 0 && len(available) > 0 {
					// Consume pending closes FIFO, allocating one ticket per pending unit until we run out
					a.mt5TicketMux.Lock()
					pool := a.baseIdToTickets[mt5Result.ID]
					var remainingPend []pendingClose
					for _, pc := range pend {
						alloc := pc.qty
						for alloc > 0 && len(pool) > 0 {
							ticket := pool[0]
							pool = pool[1:]
							ct := Trade{
								ID:              fmt.Sprintf("close_%d", time.Now().UnixNano()),
								BaseID:          mt5Result.ID,
								Time:            time.Now(),
								Action:          "CLOSE_HEDGE",
								Quantity:        1,
								Price:           0,
								TotalQuantity:   1,
								ContractNum:     1,
								OrderType:       "CLOSE",
								MeasurementPips: 0,
								RawMeasurement:  0,
								Instrument:      pc.instrument,
								AccountName:     pc.account,
								NTTradeResult:   "closed",
								MT5Ticket:       ticket,
							}
							if err := a.AddToTradeQueue(ct); err != nil {
								log.Printf("gRPC: Failed to add pending CLOSE_HEDGE (ticket %d) to trade queue: %v", ticket, err)
								// If enqueue fails, re-queue this pending and break to avoid spinning
								remainingPend = append(remainingPend, pendingClose{qty: alloc, instrument: pc.instrument, account: pc.account})
								break
							}
							alloc--
							log.Printf("gRPC: Dispatched pending CLOSE_HEDGE using ticket %d for BaseID %s (%d left in this pending)", ticket, mt5Result.ID, alloc)
						}
						if alloc > 0 {
							// Not enough tickets yet; keep remaining pending
							remainingPend = append(remainingPend, pendingClose{qty: alloc, instrument: pc.instrument, account: pc.account})
						}
					}
					// Update pools and pending list
					a.baseIdToTickets[mt5Result.ID] = pool
					if len(remainingPend) == 0 {
						delete(a.pendingCloses, mt5Result.ID)
					} else {
						a.pendingCloses[mt5Result.ID] = remainingPend
					}
					a.mt5TicketMux.Unlock()
				}

				// Event-driven: after storing a new ticket, attempt a global reconcile to drain any cross-base pendings
				go a.reconcilePendingCloses()
			}
		}
	case map[string]interface{}:
		// Legacy map format (keep for compatibility)
		if ticketFloat, ok := mt5Result["Ticket"].(float64); ok {
			ticket := uint64(ticketFloat)

			if baseId, ok := mt5Result["ID"].(string); ok && baseId != "" {
				isClose := false
				if ic, okIC := mt5Result["IsClose"].(bool); okIC {
					isClose = ic
				}
				if isClose {
					// Prune mappings on close and notify NT
					// Check for recent elastic context before processing
					var baseElastic string
					if mk, ok := a.recentElasticFor(baseId, ticket, 3*time.Second); ok {
						baseElastic = mk.reason
						if strings.EqualFold(baseElastic, "elastic_partial_close") {
							log.Printf("gRPC: Suppressing legacy MT5TradeResult close for ticket %d (BaseID: %s) due to recent elastic_partial_close context.", ticket, baseId)
						} else {
							log.Printf("gRPC: Reclassifying legacy MT5TradeResult close for ticket %d (BaseID: %s) to %s due to recent elastic context.", ticket, baseId, baseElastic)
						}
					}
					a.mt5TicketMux.Lock()
					delete(a.mt5TicketToBaseId, ticket)
					if cur, ok := a.baseIdToMT5Ticket[baseId]; ok && cur == ticket {
						delete(a.baseIdToMT5Ticket, baseId)
					}
					if list, ok := a.baseIdToTickets[baseId]; ok && len(list) > 0 {
						nl := make([]uint64, 0, len(list))
						for _, v := range list {
							if v != ticket {
								nl = append(nl, v)
							}
						}
						if len(nl) == 0 {
							delete(a.baseIdToTickets, baseId)
						} else {
							a.baseIdToTickets[baseId] = nl
						}
					}
					a.mt5TicketMux.Unlock()

					// Determine origin for tagging
					orderType := "MT5_CLOSE"
					a.ntCloseMux.Lock()
					if t, ok := a.ntInitiatedTickets[ticket]; ok {
						if time.Since(t) <= 5*time.Second {
							orderType = "NT_CLOSE_ACK"
						}
						delete(a.ntInitiatedTickets, ticket)
					}
					a.ntCloseMux.Unlock()

					closureReason := "MT5_position_closed"
					suppress := false
					if baseElastic != "" {
						if strings.EqualFold(baseElastic, "elastic_partial_close") {
							suppress = true
						} else {
							closureReason = baseElastic
						}
					}

					closeNotification := struct {
						ID              string    `json:"id"`
						BaseID          string    `json:"base_id"`
						Time            time.Time `json:"time"`
						Action          string    `json:"action"`
						Quantity        float64   `json:"quantity"`
						Price           float64   `json:"price"`
						TotalQuantity   float64   `json:"total_quantity"`
						ContractNum     int       `json:"contract_num"`
						OrderType       string    `json:"order_type"`
						MeasurementPips float64   `json:"measurement_pips"`
						RawMeasurement  float64   `json:"raw_measurement"`
						Instrument      string    `json:"instrument"`
						AccountName     string    `json:"account_name"`
						NTBalance       float64   `json:"nt_balance"`
						NTDailyPnL      float64   `json:"nt_daily_pnl"`
						NTTradeResult   string    `json:"nt_trade_result"`
						NTSessionTrades int       `json:"nt_session_trades"`
						MT5Ticket       uint64    `json:"mt5_ticket"`
						ClosureReason   string    `json:"closure_reason"`
					}{
						ID:              fmt.Sprintf("mt5close_result_%d", time.Now().UnixNano()),
						BaseID:          baseId,
						Time:            time.Now(),
						Action:          "MT5_CLOSE_NOTIFICATION",
						Quantity:        0,
						Price:           0,
						TotalQuantity:   0,
						ContractNum:     1,
						OrderType:       orderType,
						MeasurementPips: 0,
						RawMeasurement:  0,
						Instrument:      func() string { i, _ := a.bestInstAcctFor(baseId); return i }(),
						AccountName:     func() string { _, ac := a.bestInstAcctFor(baseId); return ac }(),
						NTBalance:       0,
						NTDailyPnL:      0,
						NTTradeResult:   "mt5_closed",
						NTSessionTrades: 0,
						MT5Ticket:       ticket,
						ClosureReason:   closureReason,
					}
					if suppress {
						log.Printf("gRPC: Skipped broadcasting MT5 close for ticket %d (BaseID: %s) due to elastic_partial_close context.", ticket, baseId)
					} else {
						a.grpcServer.BroadcastMT5CloseToNTStreams(closeNotification)
						log.Printf("gRPC: Pruned mapping and broadcasted MT5 close for ticket %d (BaseID: %s) with reason %s", ticket, baseId, closureReason)
					}
				} else {
					a.mt5TicketMux.Lock()
					a.mt5TicketToBaseId[ticket] = baseId
					a.baseIdToMT5Ticket[baseId] = ticket
					// append unique ticket to list
					list := a.baseIdToTickets[baseId]
					seen := false
					for _, v := range list {
						if v == ticket {
							seen = true
							break
						}
					}
					if !seen {
						a.baseIdToTickets[baseId] = append(list, ticket)
					}

					// Auto-detect potential cross-references for related base_ids
					a.detectAndStoreCrossReferences(baseId)

					// Capture available tickets and pending (including correlated) before unlock
					available := append([]uint64(nil), a.baseIdToTickets[baseId]...)
					pend := append([]pendingClose(nil), a.pendingCloses[baseId]...)
					if len(a.pendingCloses) > 0 {
						base := baseId
						instB := a.baseIdToInstrument[base]
						acctB := a.baseIdToAccount[base]
						for pBase, plist := range a.pendingCloses {
							if pBase == base || len(plist) == 0 {
								continue
							}
							rel := a.baseIdCrossRef[pBase]
							rev := a.baseIdCrossRef[base]
							sameRel := (rel == base) || (rev == pBase)
							instP, hasInstP := a.baseIdToInstrument[pBase]
							acctP, hasAcctP := a.baseIdToAccount[pBase]
							sameIA := false
							if instB != "" && acctB != "" && hasInstP && hasAcctP {
								sameIA = (instP == instB && acctP == acctB)
							}
							if sameRel || sameIA {
								pend = append(pend, plist...)
								delete(a.pendingCloses, pBase)
								log.Printf("gRPC: Merged %d pending close(s) from BaseID %s into %s (reason=%s)", len(plist), pBase, base, func() string {
									if sameRel {
										return "crossref"
									} else {
										return "inst_acct"
									}
								}())
							}
						}
					}
					a.mt5TicketMux.Unlock()

					log.Printf("gRPC: Stored MT5 ticket mapping - Ticket: %d -> BaseID: %s (count=%d)", ticket, baseId, len(available))

					if len(pend) > 0 && len(available) > 0 {
						// Consume pending closes with the available pool
						a.mt5TicketMux.Lock()
						pool := a.baseIdToTickets[baseId]
						var remainingPend []pendingClose
						for _, pc := range pend {
							alloc := pc.qty
							for alloc > 0 && len(pool) > 0 {
								tk := pool[0]
								pool = pool[1:]
								ct := Trade{
									ID:            fmt.Sprintf("close_%d", time.Now().UnixNano()),
									BaseID:        baseId,
									Time:          time.Now(),
									Action:        "CLOSE_HEDGE",
									Quantity:      1,
									TotalQuantity: 1,
									OrderType:     "CLOSE",
									Instrument:    pc.instrument,
									AccountName:   pc.account,
									MT5Ticket:     tk,
								}
								if err := a.AddToTradeQueue(ct); err != nil {
									log.Printf("gRPC: Failed to add pending CLOSE_HEDGE (ticket %d) to trade queue: %v", tk, err)
									remainingPend = append(remainingPend, pendingClose{qty: alloc, instrument: pc.instrument, account: pc.account})
									break
								}
								alloc--
							}
							if alloc > 0 {
								remainingPend = append(remainingPend, pendingClose{qty: alloc, instrument: pc.instrument, account: pc.account})
							}
						}
						a.baseIdToTickets[baseId] = pool
						if len(remainingPend) == 0 {
							delete(a.pendingCloses, baseId)
						} else {
							a.pendingCloses[baseId] = remainingPend
						}
						a.mt5TicketMux.Unlock()
					}
				}
			}
		}
	default:
		log.Printf("gRPC: WARNING - Unknown MT5 trade result type: %T", result)
	}

	return nil
}

func (a *App) HandleElasticUpdate(update interface{}) error {
	log.Printf("gRPC: Received elastic ping: %+v", update)
	// Extract minimal fields
	baseID := getBaseIDFromRequest(update)
	profitLvl := getElasticProfitLevel(update)
	curProfit := getElasticCurrentProfit(update)
	mt5tk := getMT5TicketFromRequest(update)
	log.Printf("DEBUG: Elastic extract base_id=%s profit_level=%d current_profit=%.4f mt5_ticket=%d", baseID, profitLvl, curProfit, mt5tk)

	// Inject cached NT sizing hint and enrichment where possible
	// Try BaseID first, then instrument fallback, and a brief wait to catch just-enqueued Buy
	a.mt5TicketMux.RLock()
	ntPts := a.baseIdToNtPoints[baseID]
	inst, acct := a.baseIdToInstrument[baseID], a.baseIdToAccount[baseID]
	// Instrument-level fallback if BaseID not yet cached
	if ntPts <= 0 && inst != "" {
		if v, ok := a.instrumentToNtPts[inst]; ok && v > 0 {
			ntPts = v
		}
	}
	// Cross-reference fallback: if this base has a mapped related base, try its cached hints
	if ntPts <= 0 {
		if rel, ok := a.baseIdCrossRef[baseID]; ok && rel != "" {
			if v, ok2 := a.baseIdToNtPoints[rel]; ok2 && v > 0 {
				ntPts = v
			}
			// Backfill instrument/account from related base if missing
			if inst == "" {
				if v, ok2 := a.baseIdToInstrument[rel]; ok2 && strings.TrimSpace(v) != "" {
					inst = v
				}
			}
			if acct == "" {
				if v, ok2 := a.baseIdToAccount[rel]; ok2 && strings.TrimSpace(v) != "" {
					acct = v
				}
			}
		}
	}
	a.mt5TicketMux.RUnlock()

	// If still missing, wait briefly for Buy/Sell caching to land
	if ntPts <= 0 {
		for i := 0; i < 10; i++ { // up to ~150ms
			time.Sleep(15 * time.Millisecond)
			a.mt5TicketMux.RLock()
			ntPts = a.baseIdToNtPoints[baseID]
			if ntPts <= 0 && inst == "" && acct == "" {
				// Re-resolve best instrument/account (could have been cached meanwhile)
				inst, acct = a.bestInstAcctFor(baseID)
			}
			a.mt5TicketMux.RUnlock()
			if ntPts > 0 {
				break
			}
		}
		if ntPts <= 0 && inst != "" {
			a.mt5TicketMux.RLock()
			if v, ok := a.instrumentToNtPts[inst]; ok && v > 0 {
				ntPts = v
			}
			a.mt5TicketMux.RUnlock()
		}
	}

	// Generate unique ID using sequence counter to prevent duplicate rejection
	a.elasticSeqMux.Lock()
	a.elasticSeqCount++
	uniqueID := fmt.Sprintf("elastic_evt_%d_%d", time.Now().UnixNano(), a.elasticSeqCount)
	a.elasticSeqMux.Unlock()

	// Enqueue a lightweight event trade carrying enrichment so EA can branch on event_type
	ct := Trade{
		ID:                   uniqueID,
		BaseID:               baseID,
		Time:                 time.Now(),
		Action:               "EVENT",
		Quantity:             0,
		Price:                0,
		TotalQuantity:        0,
		ContractNum:          0,
		OrderType:            "EVENT",
		MeasurementPips:      0,
		RawMeasurement:       0,
		Instrument:           inst,
		AccountName:          acct,
		MT5Ticket:            mt5tk,
		NTPointsPer1kLoss:    ntPts,
		EventType:            "elastic_ping",
		ElasticCurrentProfit: curProfit,
		ElasticProfitLevel:   int32(profitLvl),
	}
	if err := a.AddToTradeQueue(ct); err != nil {
		return fmt.Errorf("failed to enqueue elastic event: %v", err)
	}
	if ntPts <= 0 {
		log.Printf("WARN: Enqueued elastic event without nt_points_per_1k_loss (base_id=%s, inst=%s)", baseID, inst)
	}
	return nil
}

func (a *App) HandleTrailingStopUpdate(update interface{}) error {
	log.Printf("gRPC: Received trailing stop update: %+v", update)
	// No synthetic mapping; EA may act on elastic pings sent explicitly via SubmitElasticUpdate.
	return nil
}

func (a *App) HandleNTCloseHedgeRequest(request interface{}) error {
	log.Printf("gRPC: Received NT close hedge request: %+v", request)

	// Extract BaseID from request
	baseID := getBaseIDFromRequest(request)

	// If NT provided a specific MT5 ticket, prioritize closing that ticket directly.
	if provided := getMT5TicketFromRequest(request); provided != 0 {
		log.Printf("gRPC: NT close request has explicit MT5 ticket %d; enqueueing targeted CLOSE_HEDGE.", provided)
		ct := Trade{
			ID:              fmt.Sprintf("close_%d", time.Now().UnixNano()),
			BaseID:          baseID,
			Time:            time.Now(),
			Action:          "CLOSE_HEDGE",
			Quantity:        1,
			Price:           0.0,
			TotalQuantity:   1,
			ContractNum:     1,
			OrderType:       "CLOSE",
			MeasurementPips: 0,
			RawMeasurement:  0.0,
			Instrument:      getInstrumentFromRequest(request),
			AccountName:     getAccountFromRequest(request),
			NTBalance:       0.0,
			NTDailyPnL:      0.0,
			NTTradeResult:   "closed",
			NTSessionTrades: 0,
			MT5Ticket:       provided,
		}
		if err := a.AddToTradeQueue(ct); err != nil {
			return fmt.Errorf("failed to add targeted CLOSE_HEDGE (ticket %d) to queue: %v", provided, err)
		}
		// Persist instrument/account for this BaseID
		inst := strings.TrimSpace(getInstrumentFromRequest(request))
		acct := strings.TrimSpace(getAccountFromRequest(request))
		if inst != "" {
			a.baseIdToInstrument[baseID] = inst
		}
		if acct != "" {
			a.baseIdToAccount[baseID] = acct
		}
		// Track NT-initiated close for origin tagging
		a.ntCloseMux.Lock()
		a.ntInitiatedTickets[provided] = time.Now()
		a.ntCloseMux.Unlock()
		// Remove ticket from pools to prevent double-use
		a.mt5TicketMux.Lock()
		if bid, ok := a.mt5TicketToBaseId[provided]; ok {
			delete(a.mt5TicketToBaseId, provided)
			if cur, ok := a.baseIdToMT5Ticket[bid]; ok && cur == provided {
				delete(a.baseIdToMT5Ticket, bid)
			}
			// Also prune from slice pool if present
			if slice, ok := a.baseIdToTickets[bid]; ok {
				pruned := slice[:0]
				for _, t := range slice {
					if t != provided {
						pruned = append(pruned, t)
					}
				}
				if len(pruned) == 0 {
					delete(a.baseIdToTickets, bid)
				} else {
					a.baseIdToTickets[bid] = pruned
				}
			}
		}
		a.mt5TicketMux.Unlock()
		log.Printf("gRPC: Targeted CLOSE_HEDGE enqueued and mappings pruned for ticket %d (BaseID: %s)", provided, baseID)
		return nil
	}

	// Try to resolve tickets using both direct mapping and cross-references
	tickets, actualBaseID := a.resolveBaseIDToTickets(baseID)
	if actualBaseID == "" {
		actualBaseID = baseID // Fallback to original if no mapping found
	}

	// Also check legacy single ticket mapping for backward compatibility
	a.mt5TicketMux.RLock()
	legacyTicket, hasLegacy := a.baseIdToMT5Ticket[baseID]
	a.mt5TicketMux.RUnlock()

	makeCloseTrade := func(qtyForMsg int, ticket uint64) Trade {
		return Trade{
			ID:              fmt.Sprintf("close_%d", time.Now().UnixNano()),
			BaseID:          actualBaseID, // Use the actual BaseID where tickets were found
			Time:            time.Now(),
			Action:          "CLOSE_HEDGE",
			Quantity:        float64(qtyForMsg),
			Price:           0.0,
			TotalQuantity:   qtyForMsg,
			ContractNum:     1,
			OrderType:       "CLOSE",
			MeasurementPips: 0,
			RawMeasurement:  0.0,
			Instrument:      getInstrumentFromRequest(request),
			AccountName:     getAccountFromRequest(request),
			NTBalance:       0.0,
			NTDailyPnL:      0.0,
			NTTradeResult:   "closed",
			NTSessionTrades: 0,
			MT5Ticket:       ticket,
		}
	}

	// Decide how many close trades to enqueue
	if len(tickets) > 0 {
		reqQty := int(getQuantityFromRequest(request))
		if reqQty <= 0 {
			reqQty = len(tickets)
		}
		// If fewer tickets available than requested, only wait a very short time;
		// remainder will be handled by the reconciler as tickets arrive.
		if reqQty > len(tickets) {
			waited := a.waitForTickets(actualBaseID, 100*time.Millisecond, 25*time.Millisecond)
			if len(waited) > len(tickets) {
				tickets = waited
			}
		}
		toAllocate := reqQty
		if toAllocate > len(tickets) {
			toAllocate = len(tickets)
		}
		logMsg := fmt.Sprintf("gRPC: Adding %d/%d CLOSE_HEDGE messages with explicit MT5 tickets for BaseID: %s", toAllocate, len(tickets), baseID)
		if actualBaseID != baseID {
			logMsg += fmt.Sprintf(" (resolved via cross-reference to %s)", actualBaseID)
		}
		log.Print(logMsg)

		// TICKET_ALLOCATION_FIX: Remove tickets from available pool as we allocate them to ensure each closure gets a unique ticket
		a.mt5TicketMux.Lock()
		availableTickets := a.baseIdToTickets[actualBaseID]
		for i := 0; i < toAllocate && len(availableTickets) > 0; i++ {
			// Take the first available ticket and remove it from the pool
			t := availableTickets[0]
			availableTickets = availableTickets[1:]            // Remove allocated ticket from pool
			a.baseIdToTickets[actualBaseID] = availableTickets // Update the pool

			ct := makeCloseTrade(1, t)
			if err := a.AddToTradeQueue(ct); err != nil {
				a.mt5TicketMux.Unlock()
				log.Printf("gRPC: Failed to add CLOSE_HEDGE (ticket %d) to trade queue: %v", t, err)
				return fmt.Errorf("failed to add CLOSE_HEDGE (ticket %d) to trade queue: %v", t, err)
			}
			// Track NT-initiated close for origin tagging
			a.ntCloseMux.Lock()
			a.ntInitiatedTickets[t] = time.Now()
			a.ntCloseMux.Unlock()
			log.Printf("gRPC: Allocated ticket %d for CLOSE_HEDGE (%d remaining for BaseID: %s)", t, len(availableTickets), actualBaseID)
		}

		// Clean up empty ticket pools
		if len(availableTickets) == 0 {
			delete(a.baseIdToTickets, actualBaseID)
		}
		a.mt5TicketMux.Unlock()

		remaining := reqQty - toAllocate
		if remaining > 0 {
			// Ticket-only policy: do not enqueue base_id-only remainder. Record pending and wait for tickets.
			inst := strings.TrimSpace(getInstrumentFromRequest(request))
			acct := strings.TrimSpace(getAccountFromRequest(request))
			useBase := actualBaseID // No alignment; keep original base strictly
			a.mt5TicketMux.Lock()
			// Persist instrument/account metadata for better reconciliation later
			if inst != "" {
				a.baseIdToInstrument[useBase] = inst
			}
			if acct != "" {
				a.baseIdToAccount[useBase] = acct
			}
			pcList := a.pendingCloses[useBase]
			pcList = append(pcList, pendingClose{qty: remaining, instrument: inst, account: acct})
			a.pendingCloses[useBase] = pcList
			a.mt5TicketMux.Unlock()
			log.Printf("gRPC: Ticket-only policy: recorded pending remainder CLOSE_HEDGE (qty=%d) for BaseID: %s; will dispatch by ticket when available.", remaining, useBase)
			// Event-driven: kick reconciler to try dispatching from any available pools immediately
			go a.reconcilePendingCloses()
		}

		log.Printf("gRPC: Successfully queued CLOSE_HEDGE for BaseID: %s (ticketed=%d, base_id_only=%d)", baseID, toAllocate, 0)
		return nil
	}

	// No tickets known yet; very short grace period to let MT5 trade result callback populate mappings.
	// Keep this tight; pendings + reconciler will drain quickly as tickets arrive.
	// Wait up to 75ms, polling every ~25ms. Consider both original and any cross-referenced BaseID.
	waitedTickets := a.waitForTickets(baseID, 75*time.Millisecond, 25*time.Millisecond)
	if len(waitedTickets) == 0 && actualBaseID != "" && actualBaseID != baseID {
		if wt2 := a.waitForTickets(actualBaseID, 75*time.Millisecond, 25*time.Millisecond); len(wt2) > 0 {
			waitedTickets = wt2
			baseID = actualBaseID
		}
	}
	if len(waitedTickets) > 0 {
		reqQty := int(getQuantityFromRequest(request))
		if reqQty <= 0 {
			reqQty = len(waitedTickets)
		}
		toAllocate := reqQty
		if toAllocate > len(waitedTickets) {
			toAllocate = len(waitedTickets)
		}
		log.Printf("gRPC: Resolved %d MT5 tickets after brief wait; enqueueing CLOSE_HEDGE by ticket for BaseID: %s", toAllocate, baseID)

		// TICKET_ALLOCATION_FIX: Remove tickets from available pool as we allocate them (same logic as above)
		a.mt5TicketMux.Lock()
		availableWaitedTickets := a.baseIdToTickets[baseID] // Use original baseID since waitForTickets uses it
		for i := 0; i < toAllocate && len(availableWaitedTickets) > 0; i++ {
			// Take the first available ticket and remove it from the pool
			t := availableWaitedTickets[0]
			availableWaitedTickets = availableWaitedTickets[1:] // Remove allocated ticket from pool
			a.baseIdToTickets[baseID] = availableWaitedTickets  // Update the pool

			ct := makeCloseTrade(1, t)
			if err := a.AddToTradeQueue(ct); err != nil {
				a.mt5TicketMux.Unlock()
				log.Printf("gRPC: Failed to add CLOSE_HEDGE (ticket %d) to trade queue: %v", t, err)
				return fmt.Errorf("failed to add CLOSE_HEDGE (ticket %d) to trade queue: %v", t, err)
			}
			// Track NT-initiated close for origin tagging
			a.ntCloseMux.Lock()
			a.ntInitiatedTickets[t] = time.Now()
			a.ntCloseMux.Unlock()
			log.Printf("gRPC: Allocated ticket %d for CLOSE_HEDGE after wait (%d remaining for BaseID: %s)", t, len(availableWaitedTickets), baseID)
		}

		// Clean up empty ticket pools
		if len(availableWaitedTickets) == 0 {
			delete(a.baseIdToTickets, baseID)
		}
		a.mt5TicketMux.Unlock()

		remaining := reqQty - toAllocate
		if remaining > 0 {
			// Ticket-only policy: do not enqueue base_id-only remainder. Record pending and wait for tickets.
			inst := strings.TrimSpace(getInstrumentFromRequest(request))
			acct := strings.TrimSpace(getAccountFromRequest(request))
			useBase := baseID // No alignment; keep original base strictly
			a.mt5TicketMux.Lock()
			// Persist instrument/account metadata for better reconciliation later
			if inst != "" {
				a.baseIdToInstrument[useBase] = inst
			}
			if acct != "" {
				a.baseIdToAccount[useBase] = acct
			}
			pcList := a.pendingCloses[useBase]
			pcList = append(pcList, pendingClose{qty: remaining, instrument: inst, account: acct})
			a.pendingCloses[useBase] = pcList
			a.mt5TicketMux.Unlock()
			log.Printf("gRPC: Ticket-only policy: recorded pending remainder CLOSE_HEDGE (qty=%d) for BaseID: %s (post-wait); will dispatch by ticket when available.", remaining, useBase)
			// Event-driven: kick reconciler to try dispatching from any available pools immediately
			go a.reconcilePendingCloses()
		}

		log.Printf("gRPC: Successfully queued CLOSE_HEDGE (post-wait) for BaseID: %s (ticketed=%d, base_id_only=%d)", baseID, toAllocate, 0)
		return nil
	}

	// Fallback to single ticket if available
	var ticket uint64
	reqQty := int(getQuantityFromRequest(request))
	if reqQty <= 0 {
		reqQty = 1
	}
	if hasLegacy {
		// Legacy single ticket known – close that one specifically (qty=1)
		ticket = legacyTicket
		log.Printf("gRPC: Using legacy single MT5 ticket %d for BaseID %s", ticket, baseID)
		ct := makeCloseTrade(1, ticket)
		if !a.IsHedgebotActive() {
			log.Printf("WARN: CLOSE_HEDGE for BaseID %s queued while MT5 stream inactive. Will deliver when stream reconnects.", baseID)
		}
		if err := a.AddToTradeQueue(ct); err != nil {
			log.Printf("gRPC: Failed to add CLOSE_HEDGE to trade queue: %v", err)
			return fmt.Errorf("failed to add CLOSE_HEDGE to trade queue: %v", err)
		}
		// Track NT-initiated close for origin tagging
		a.ntCloseMux.Lock()
		a.ntInitiatedTickets[ticket] = time.Now()
		a.ntCloseMux.Unlock()
		log.Printf("gRPC: Successfully queued CLOSE_HEDGE for BaseID: %s", baseID)
		return nil
	}

	// No tickets known – alignment is disabled to avoid mis-routing closures; keep original base strictly

	// intentionally no alignment here

	// Ticket-only policy: do not enqueue base_id-only CLOSE_HEDGE at all. Record pending and return.
	instFinal := strings.TrimSpace(getInstrumentFromRequest(request))
	acctFinal := strings.TrimSpace(getAccountFromRequest(request))
	a.mt5TicketMux.Lock()
	if reqQty > 0 {
		// Persist instrument/account metadata for better reconciliation later
		if instFinal != "" {
			a.baseIdToInstrument[baseID] = instFinal
		}
		if acctFinal != "" {
			a.baseIdToAccount[baseID] = acctFinal
		}
		pcList := a.pendingCloses[baseID]
		pcList = append(pcList, pendingClose{qty: reqQty, instrument: instFinal, account: acctFinal})
		a.pendingCloses[baseID] = pcList
		log.Printf("gRPC: Ticket-only policy: recorded pending CLOSE_HEDGE (qty=%d) for BaseID: %s; will dispatch by ticket when available.", reqQty, baseID)
	}
	a.mt5TicketMux.Unlock()
	// Event-driven: kick reconciler after unlocking to avoid deadlock
	go a.reconcilePendingCloses()
	return nil
}

// reconcilePendingCloses attempts to match and dispatch pending closes ONLY from the exact BaseID's own ticket pool.
// Cross-reference and instrument/account correlation are disabled to prevent closing the wrong hedge.
func (a *App) reconcilePendingCloses() {
	a.mt5TicketMux.Lock()
	defer a.mt5TicketMux.Unlock()

	pendCount := 0
	for _, plist := range a.pendingCloses {
		pendCount += len(plist)
	}
	if pendCount == 0 || len(a.baseIdToTickets) == 0 {
		return
	}
	pools := 0
	for _, t := range a.baseIdToTickets {
		if len(t) > 0 {
			pools++
		}
	}
	log.Printf("gRPC: Reconciler start: pendings=%d baseKeys=%d poolsWithTickets=%d", pendCount, len(a.pendingCloses), pools)

	// Helper to try allocate up to `limit` tickets from a specific pool for this pending
	tryAllocate := func(poolBase string, pc pendingClose, limit int) (allocated int) {
		if limit <= 0 {
			return 0
		}
		tickets := a.baseIdToTickets[poolBase]
		for allocated < limit && len(tickets) > 0 {
			tk := tickets[0]
			tickets = tickets[1:]
			// Update pool immediately to prevent double use
			a.baseIdToTickets[poolBase] = tickets

			// Build and enqueue outside of lock? We hold the lock here; keep critical section small.
			trade := Trade{
				ID:            fmt.Sprintf("close_%d", time.Now().UnixNano()),
				BaseID:        poolBase,
				Time:          time.Now(),
				Action:        "CLOSE_HEDGE",
				Quantity:      1,
				TotalQuantity: 1,
				OrderType:     "CLOSE",
				Instrument:    pc.instrument,
				AccountName:   pc.account,
				MT5Ticket:     tk,
			}
			// Temporarily unlock to enqueue to avoid blocking other state
			a.mt5TicketMux.Unlock()
			err := a.AddToTradeQueue(trade)
			a.mt5TicketMux.Lock()
			if err != nil {
				// Return ticket to pool if enqueue failed
				a.baseIdToTickets[poolBase] = append([]uint64{tk}, a.baseIdToTickets[poolBase]...)
				log.Printf("gRPC: Pending reconciler failed to enqueue CLOSE_HEDGE (ticket %d): %v", tk, err)
				break
			}
			// Persist instrument/account metadata for the pool base
			if inst := strings.TrimSpace(pc.instrument); inst != "" {
				a.baseIdToInstrument[poolBase] = inst
			}
			if acct := strings.TrimSpace(pc.account); acct != "" {
				a.baseIdToAccount[poolBase] = acct
			}
			// Track NT-initiated close for origin tagging
			a.ntCloseMux.Lock()
			a.ntInitiatedTickets[tk] = time.Now()
			a.ntCloseMux.Unlock()
			allocated++
			remNow := limit - allocated
			log.Printf("gRPC: Pending reconciler dispatched CLOSE_HEDGE using ticket %d for BaseID %s (remaining in this pending now=%d)", tk, poolBase, remNow)
		}
		return allocated
	}

	// Iterate over a snapshot of keys to allow deletion while iterating
	for pBase, plist := range a.pendingCloses {
		if len(plist) == 0 {
			continue
		}
		var remainingList []pendingClose
		for _, pc := range plist {
			remainingQty := pc.qty
			if remainingQty <= 0 {
				continue
			}

			// Priority 1: direct pool for pBase
			if pool, ok := a.baseIdToTickets[pBase]; ok && len(pool) > 0 {
				alloc := tryAllocate(pBase, pc, remainingQty)
				remainingQty -= alloc
			}

			if remainingQty > 0 {
				// Keep the remainder
				remainingList = append(remainingList, pendingClose{qty: remainingQty, instrument: pc.instrument, account: pc.account})
			}
		}
		if len(remainingList) == 0 {
			delete(a.pendingCloses, pBase)
		} else {
			a.pendingCloses[pBase] = remainingList
		}
	}
	// Summary at end of reconcile
	remainingPend := 0
	for _, plist := range a.pendingCloses {
		for _, pc := range plist {
			remainingPend += pc.qty
		}
	}
	poolsAfter := 0
	for _, t := range a.baseIdToTickets {
		if len(t) > 0 {
			poolsAfter++
		}
	}
	log.Printf("gRPC: Reconciler end: remainingPendQty=%d pendingBases=%d poolsWithTickets=%d", remainingPend, len(a.pendingCloses), poolsAfter)
}

// Helper functions to extract data from the hedge close request
func getBaseIDFromRequest(request interface{}) string {
	if req, ok := request.(map[string]interface{}); ok {
		// Support common key variants
		if baseID, ok := req["BaseID"].(string); ok {
			return baseID
		}
		if baseID, ok := req["base_id"].(string); ok {
			return baseID
		}
		if baseID, ok := req["BaseId"].(string); ok { // camel variant
			return baseID
		}
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		baseIDField := val.FieldByName("BaseID")
		if baseIDField.IsValid() && baseIDField.Kind() == reflect.String {
			return baseIDField.String()
		}
	}
	return ""
}

func getQuantityFromRequest(request interface{}) float64 {
	if req, ok := request.(map[string]interface{}); ok {
		if quantity, ok := req["ClosedHedgeQuantity"].(float64); ok {
			return quantity
		}
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		quantityField := val.FieldByName("ClosedHedgeQuantity")
		if quantityField.IsValid() && quantityField.Kind() == reflect.Float64 {
			return quantityField.Float()
		}
	}
	return 1.0 // Default
}

func getInstrumentFromRequest(request interface{}) string {
	if req, ok := request.(map[string]interface{}); ok {
		if instrument, ok := req["NTInstrumentSymbol"].(string); ok {
			return instrument
		}
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		instrumentField := val.FieldByName("NTInstrumentSymbol")
		if instrumentField.IsValid() && instrumentField.Kind() == reflect.String {
			return instrumentField.String()
		}
	}
	return ""
}

func getAccountFromRequest(request interface{}) string {
	if req, ok := request.(map[string]interface{}); ok {
		if account, ok := req["NTAccountName"].(string); ok {
			return account
		}
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		accountField := val.FieldByName("NTAccountName")
		if accountField.IsValid() && accountField.Kind() == reflect.String {
			return accountField.String()
		}
	}
	return ""
}

func getClosureReasonFromRequest(request interface{}) string {
	if req, ok := request.(map[string]interface{}); ok {
		if reason, ok := req["ClosureReason"].(string); ok {
			return reason
		}
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		reasonField := val.FieldByName("ClosureReason")
		if reasonField.IsValid() && reasonField.Kind() == reflect.String {
			return reasonField.String()
		}
	}
	return ""
}

func getMT5TicketFromRequest(request interface{}) uint64 {
	log.Printf("DEBUG: getMT5TicketFromRequest called with type %T, value: %+v", request, request)
	if req, ok := request.(map[string]interface{}); ok {
		log.Printf("DEBUG: Processing as map[string]interface{}")
		// Try both field name variants (MT5Ticket and mt5_ticket)
		if ticket, ok := req["MT5Ticket"].(uint64); ok {
			log.Printf("DEBUG: Found MT5Ticket as uint64: %d", ticket)
			return ticket
		}
		if ticket, ok := req["mt5_ticket"].(uint64); ok {
			log.Printf("DEBUG: Found mt5_ticket as uint64: %d", ticket)
			return ticket
		}
		// Handle float64 conversion (common in JSON)
		if ticketFloat, ok := req["MT5Ticket"].(float64); ok {
			log.Printf("DEBUG: Found MT5Ticket as float64: %f", ticketFloat)
			return uint64(ticketFloat)
		}
		if ticketFloat, ok := req["mt5_ticket"].(float64); ok {
			log.Printf("DEBUG: Found mt5_ticket as float64: %f", ticketFloat)
			return uint64(ticketFloat)
		}
		// Handle string representation
		if ticketStr, ok := req["MT5Ticket"].(string); ok {
			if v, err := strconv.ParseUint(ticketStr, 10, 64); err == nil {
				log.Printf("DEBUG: Parsed MT5Ticket from string: %s -> %d", ticketStr, v)
				return v
			}
		}
		if ticketStr, ok := req["mt5_ticket"].(string); ok {
			if v, err := strconv.ParseUint(ticketStr, 10, 64); err == nil {
				log.Printf("DEBUG: Parsed mt5_ticket from string: %s -> %d", ticketStr, v)
				return v
			}
		}
		log.Printf("DEBUG: No ticket field found in map keys: %v", reflect.ValueOf(req).MapKeys())
	}
	// Try reflection for struct fields
	val := reflect.ValueOf(request)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		log.Printf("DEBUG: Processing as struct")
		// List all available fields for debugging
		structType := val.Type()
		log.Printf("DEBUG: Struct has %d fields:", structType.NumField())
		for i := 0; i < structType.NumField(); i++ {
			field := structType.Field(i)
			log.Printf("DEBUG: Field %d: %s (type: %s)", i, field.Name, field.Type)
		}
		// Try both field name variants
		ticketField := val.FieldByName("MT5Ticket")
		if ticketField.IsValid() && ticketField.Kind() == reflect.Uint64 {
			log.Printf("DEBUG: Found MT5Ticket field as uint64: %d", ticketField.Uint())
			return ticketField.Uint()
		}
		ticketField = val.FieldByName("Mt5Ticket")
		if ticketField.IsValid() && ticketField.Kind() == reflect.Uint64 {
			log.Printf("DEBUG: Found Mt5Ticket field as uint64: %d", ticketField.Uint())
			return ticketField.Uint()
		}
	}
	log.Printf("DEBUG: No ticket found, returning 0")
	return 0
}

// Elastic update helpers (reflective extract to avoid package coupling)
func getElasticProfitLevel(update interface{}) int {
	// Helper to parse any to int
	parseToInt := func(x interface{}) (int, bool) {
		switch t := x.(type) {
		case int:
			return t, true
		case int32:
			return int(t), true
		case int64:
			return int(t), true
		case float64:
			return int(t), true
		case float32:
			return int(t), true
		case string:
			if i, err := strconv.Atoi(strings.TrimSpace(t)); err == nil {
				return i, true
			}
		}
		return 0, false
	}
	// Map case: try multiple aliases and casings
	if m, ok := update.(map[string]interface{}); ok {
		keys := []string{"ProfitLevel", "profit_level", "profitLevel", "ElasticProfitLevel", "elastic_profit_level", "elasticProfitLevel", "level"}
		for _, k := range keys {
			if v, ok := m[k]; ok {
				if iv, ok2 := parseToInt(v); ok2 {
					return iv
				}
			}
		}
	}
	// Struct case: scan fields case-insensitively
	val := reflect.ValueOf(update)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		cand := map[string]struct{}{"profitlevel": {}, "elasticprofitlevel": {}, "level": {}}
		t := val.Type()
		for i := 0; i < val.NumField(); i++ {
			name := strings.ToLower(t.Field(i).Name)
			if _, ok := cand[name]; ok {
				f := val.Field(i)
				switch f.Kind() {
				case reflect.Int, reflect.Int32, reflect.Int64:
					return int(f.Int())
				case reflect.Float32, reflect.Float64:
					return int(f.Float())
				case reflect.String:
					if i, err := strconv.Atoi(strings.TrimSpace(f.String())); err == nil {
						return i
					}
				}
			}
		}
		// Fallback to exact name
		f := val.FieldByName("ProfitLevel")
		if f.IsValid() && (f.Kind() == reflect.Int || f.Kind() == reflect.Int32 || f.Kind() == reflect.Int64) {
			return int(f.Int())
		}
	}
	return 0
}

func getElasticCurrentProfit(update interface{}) float64 {
	// Helper to parse any to float64
	parseToFloat := func(x interface{}) (float64, bool) {
		switch t := x.(type) {
		case float64:
			return t, true
		case float32:
			return float64(t), true
		case int:
			return float64(t), true
		case int32:
			return float64(t), true
		case int64:
			return float64(t), true
		case string:
			if f, err := strconv.ParseFloat(strings.TrimSpace(t), 64); err == nil {
				return f, true
			}
		}
		return 0, false
	}
	if m, ok := update.(map[string]interface{}); ok {
		keys := []string{"CurrentProfit", "current_profit", "currentProfit", "ElasticCurrentProfit", "elastic_current_profit", "elasticCurrentProfit", "profit"}
		for _, k := range keys {
			if v, ok := m[k]; ok {
				if fv, ok2 := parseToFloat(v); ok2 {
					return fv
				}
			}
		}
	}
	val := reflect.ValueOf(update)
	if val.Kind() == reflect.Ptr {
		val = val.Elem()
	}
	if val.Kind() == reflect.Struct {
		cand := map[string]struct{}{"currentprofit": {}, "elasticcurrentprofit": {}, "profit": {}}
		t := val.Type()
		for i := 0; i < val.NumField(); i++ {
			name := strings.ToLower(t.Field(i).Name)
			if _, ok := cand[name]; ok {
				f := val.Field(i)
				switch f.Kind() {
				case reflect.Float32, reflect.Float64:
					return f.Float()
				case reflect.Int, reflect.Int32, reflect.Int64:
					return float64(f.Int())
				case reflect.String:
					if fl, err := strconv.ParseFloat(strings.TrimSpace(f.String()), 64); err == nil {
						return fl
					}
				}
			}
		}
		// Fallback to exact name
		f := val.FieldByName("CurrentProfit")
		if f.IsValid() && (f.Kind() == reflect.Float64 || f.Kind() == reflect.Float32) {
			return f.Float()
		}
	}
	return 0.0
}

// DisableAllProtocols stops the gRPC server (for shutdown or UI action)
func (a *App) DisableAllProtocols(reason ...string) error {
	r := "unspecified"
	if len(reason) > 0 && reason[0] != "" {
		r = reason[0]
	}
	log.Printf("Disabling gRPC server... (reason=%s)", r)

	// Stop gRPC server
	if a.grpcServer != nil {
		log.Printf("Stopping gRPC server... (reason=%s)", r)
		a.grpcServer.Stop()
	}

	a.bridgeActive = false
	log.Printf("gRPC server disabled (reason=%s)", r)
	return nil
}
