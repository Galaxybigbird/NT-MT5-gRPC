package grpc

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log"
	"net"
	"regexp"
	"strings"
	"sync"
	"time"

	trading "BridgeApp/internal/grpc/proto"
	blog "BridgeApp/internal/logging"

	"crypto/md5"
	"encoding/hex"

	"google.golang.org/grpc"
	"google.golang.org/grpc/codes"
	"google.golang.org/grpc/keepalive"
	"google.golang.org/grpc/status"
)

// md5 + hex for deterministic correlation id from base_id
// kept local to avoid new package churn
//nolint:gosec // correlation id, not for crypto

// Server represents the gRPC server for the trading bridge
type Server struct {
	trading.UnimplementedTradingServiceServer
	trading.UnimplementedStreamingServiceServer
	trading.UnimplementedLoggingServiceServer
	app              AppInterface
	server           *grpc.Server
	tradeStreams     map[string]chan *trading.Trade
	streamsMux       sync.RWMutex
	healthStreams    map[string]chan *trading.HealthResponse
	healthStreamsMux sync.RWMutex
	// rate-limit noisy logs like health checks
	healthLogMu   sync.Mutex
	lastHealthLog map[string]time.Time

	// recentTradeIDs tracks recently processed Trade.Id to avoid double-processing
	// across SubmitTrade and TradingStream. Entries expire after a short TTL.
	recentTradeIDs  map[string]time.Time
	recentTradesMux sync.Mutex

	// recentlyClosedTickets tracks MT5 tickets that were just closed (via MT5 result/notification)
	// to suppress any stale CLOSE_HEDGE requests still lingering in buffers.
	recentlyClosedTickets map[uint64]time.Time
	rcMux                 sync.Mutex

	// currentMT5StreamID tracks the single active MT5 GetTrades stream. New connections supersede older ones.
	currentMT5StreamID string
	mt5StreamMux       sync.RWMutex
}

// AppInterface defines the interface that the App struct must implement for gRPC integration
type AppInterface interface {
	GetTradeQueue() chan interface{}
	PollTradeFromQueue() interface{} // Non-blocking trade retrieval
	AddToTradeQueue(trade interface{}) error
	GetNetPosition() int
	GetHedgeSize() float64
	GetQueueSize() int
	IsAddonConnected() bool
	IsHedgebotActive() bool
	SetAddonConnected(connected bool)
	SetHedgebotActive(active bool)
	AddToTradeHistory(trade interface{})
	HandleHedgeCloseNotification(notification interface{}) error
	HandleMT5TradeResult(result interface{}) error
	HandleElasticUpdate(update interface{}) error
	HandleTrailingStopUpdate(update interface{}) error
	HandleNTCloseHedgeRequest(request interface{}) error
}

// NewGRPCServer creates a new gRPC server instance
func NewGRPCServer(app AppInterface) *Server {
	return &Server{
		app:                   app,
		tradeStreams:          make(map[string]chan *trading.Trade),
		healthStreams:         make(map[string]chan *trading.HealthResponse),
		lastHealthLog:         make(map[string]time.Time),
		recentTradeIDs:        make(map[string]time.Time),
		recentlyClosedTickets: make(map[uint64]time.Time),
	}
}

// StartGRPCServer starts the gRPC server on the specified port
func (s *Server) StartGRPCServer(port string) error {
	// Hook standard logger to unified JSONL early
	blog.HookStdLogger()

	lis, err := net.Listen("tcp", ":"+port)
	if err != nil {
		return fmt.Errorf("failed to listen on port %s: %v", port, err)
	}

	// Configure server options with keepalive and insecure settings for .NET Framework compatibility
	opts := []grpc.ServerOption{
		grpc.KeepaliveParams(keepalive.ServerParameters{
			Time:    30 * time.Second,
			Timeout: 5 * time.Second,
		}),
		grpc.KeepaliveEnforcementPolicy(keepalive.EnforcementPolicy{
			MinTime:             5 * time.Second,
			PermitWithoutStream: true,
		}),
		grpc.MaxRecvMsgSize(1024 * 1024), // 1MB
		grpc.MaxSendMsgSize(1024 * 1024), // 1MB
	}

	s.server = grpc.NewServer(opts...)

	// Register services
	trading.RegisterTradingServiceServer(s.server, s)
	trading.RegisterStreamingServiceServer(s.server, s)
	trading.RegisterLoggingServiceServer(s.server, s)

	// start unified logger
	blog.L().EnsureStarted("bridge")
	blog.L().SetStateProvider(func() (int, int, float64) { return s.app.GetQueueSize(), s.app.GetNetPosition(), s.app.GetHedgeSize() })

	log.Printf("gRPC server starting on port %s", port)

	// Start monitor for queued trades while MT5 stream absent
	go s.monitorOfflineMT5Queue()

	// Start server in goroutine but wait to ensure it starts successfully
	serverStarted := make(chan error, 1)
	go func() {
		if err := s.server.Serve(lis); err != nil {
			log.Printf("gRPC server error: %v", err)
			serverStarted <- err
		}
	}()

	// Wait a moment to see if the server starts successfully
	select {
	case err := <-serverStarted:
		// Server failed to start immediately
		return fmt.Errorf("gRPC server failed to start: %v", err)
	case <-time.After(100 * time.Millisecond):
		// Server seems to have started successfully
		// Verify the port is actually listening
		testConn, err := net.DialTimeout("tcp", fmt.Sprintf("localhost:%s", port), 1*time.Second)
		if err != nil {
			s.server.Stop() // Stop the server if it's not accessible
			return fmt.Errorf("gRPC server started but port %s is not accessible: %v", port, err)
		}
		testConn.Close()
		log.Printf("gRPC server verified listening on port %s", port)
	}

	return nil
}

// monitorOfflineMT5Queue logs a warning if trades accumulate while no MT5 trade stream is active
func (s *Server) monitorOfflineMT5Queue() {
	ticker := time.NewTicker(2 * time.Second)
	defer ticker.Stop()
	for range ticker.C {
		// If there are active MT5 streams skip
		s.streamsMux.RLock()
		mt5StreamCount := len(s.tradeStreams)
		s.streamsMux.RUnlock()
		if mt5StreamCount > 0 {
			continue
		}
		qSize := s.app.GetQueueSize()
		if qSize > 0 {
			log.Printf("WARN: %d trade(s) buffered with no active MT5 stream. MT5 likely disconnected or timed out. Trades will flush on reconnection.", qSize)
			blog.L().Warn("stream", fmt.Sprintf("buffered trades with no MT5 stream: %d", qSize), nil)
		}
	}
}

// Stop gracefully stops the gRPC server
func (s *Server) Stop() {
	if s.server != nil {
		log.Println("Stopping gRPC server...")
		s.server.GracefulStop()
	}
}

// SubmitTrade handles trade submission from NinjaTrader
func (s *Server) SubmitTrade(ctx context.Context, req *trading.Trade) (*trading.GenericResponse, error) {
	log.Printf("gRPC: Received trade submission - ID: %s, Action: %s, Quantity: %.2f",
		req.Id, req.Action, req.Quantity)
	// Dedup guard: skip if we've very recently processed this trade ID
	if s.wasRecentlyProcessed(req.Id, 3*time.Second) {
		log.Printf("gRPC: Skipping duplicate trade submission ID: %s", req.Id)
		return &trading.GenericResponse{Status: "success", Message: "Duplicate suppressed"}, nil
	}
	s.markProcessed(req.Id)

	// Update addon connection status
	s.app.SetAddonConnected(true)

	// Enqueue with smart splitting for multi-quantity entries
	if err := s.enqueueTradeWithSplit(req); err != nil {
		log.Printf("gRPC: Failed to enqueue trade(s): %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to add trade to queue: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process trade")
	}

	// NOTE: Trades are sent to MT5 via forwardTradesToStream polling mechanism
	// No need to broadcast here to avoid duplication - the queue handles it

	log.Printf("gRPC: Trade processed successfully - ID: %s", req.Id)

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Trade processed successfully",
		Metadata: map[string]string{
			"trade_id":   req.Id,
			"timestamp":  time.Now().Format(time.RFC3339),
			"queue_size": fmt.Sprintf("%d", s.app.GetQueueSize()),
		},
	}, nil
}

// enqueueTradeWithSplit splits multi-quantity Buy/Sell entries into unit trades to create distinct MT5 tickets.
// This aligns MT5 hedges 1:1 with NT contract count and improves ticket-based close reliability.
func (s *Server) enqueueTradeWithSplit(req *trading.Trade) error {
	// Convert to internal once as a base template
	base := convertProtoToInternalTrade(req)

	// Normalize action casing
	act := strings.ToLower(req.Action)
	orderType := strings.TrimSpace(req.OrderType)
	skipSplit := strings.EqualFold(orderType, "ENTRY_AGG")

	// Only split for entry actions (buy/sell). Leave CLOSE/MT5 notifications untouched.
	if act == "buy" || act == "sell" {
		// Split any aggregated multi-quantity entry to ensure 1:1 MT5 tickets per contract.
		// This is robust to varying NT batching patterns (e.g., submissions of Qty=2 remaining of a 3 group).
		if req.Quantity > 1 && !skipSplit {
			n := int(req.Quantity + 1e-9)
			if n > 1 {
				log.Printf("gRPC: Splitting entry (Qty=%.2f → %d units) for BaseID %s", req.Quantity, n, req.BaseId)
				for i := 1; i <= n; i++ {
					t := *base // shallow copy
					// Unique ID per unit contract; keep BaseID same for correlation
					t.ID = fmt.Sprintf("%s_%dof%d", req.Id, i, n)
					t.Quantity = 1
					t.TotalQuantity = 1
					t.ContractNum = i
					// Add to queue
					if err := s.app.AddToTradeQueue(&t); err != nil {
						return err
					}
					// History
					s.app.AddToTradeHistory(&t)
				}
				return nil
			}
		}
	}

	// Default path: no split
	if err := s.app.AddToTradeQueue(base); err != nil {
		return err
	}
	s.app.AddToTradeHistory(base)
	return nil
}

// GetTrades handles streaming trade requests from MT5
func (s *Server) GetTrades(stream trading.TradingService_GetTradesServer) error {
	log.Println("gRPC: New trade stream connection attempt from MT5")

	// Track first activity time to help diagnose client-side idle timeouts
	startTime := time.Now()

	// Mark hedgebot as active when MT5 connects via gRPC streaming
	s.app.SetHedgebotActive(true)
	log.Println("gRPC: Hedgebot marked as active via streaming connection")

	// Create channel for this stream
	streamChan := make(chan *trading.Trade, 100)
	streamID := fmt.Sprintf("stream_%d", time.Now().UnixNano())

	// Supersede any existing MT5 streams to enforce a single active stream
	s.streamsMux.Lock()
	// Remove older MT5 streams (prefix "stream_")
	removed := 0
	for id := range s.tradeStreams {
		if strings.HasPrefix(id, "stream_") {
			// Skip the new one about to be added
			if id == streamID {
				continue
			}
			delete(s.tradeStreams, id)
			removed++
			log.Printf("gRPC: Superseding prior MT5 stream %s (removed)", id)
		}
	}
	// Register this stream as active
	s.tradeStreams[streamID] = streamChan
	s.streamsMux.Unlock()

	// Mark this stream as the current MT5 stream
	s.mt5StreamMux.Lock()
	s.currentMT5StreamID = streamID
	s.mt5StreamMux.Unlock()

	// Log connection with stream context
	s.streamsMux.RLock()
	connCount := len(s.tradeStreams)
	s.streamsMux.RUnlock()
	if removed > 0 {
		log.Printf("gRPC: New trade stream connected from MT5 - id=%s active_streams=%d (superseded %d older MT5 stream(s))", streamID, connCount, removed)
	} else {
		log.Printf("gRPC: New trade stream connected from MT5 - id=%s active_streams=%d", streamID, connCount)
	}

	// Clean up on exit
	defer func() {
		s.streamsMux.Lock()
		delete(s.tradeStreams, streamID)
		close(streamChan)
		streamCount := len(s.tradeStreams)
		s.streamsMux.Unlock()

		log.Printf("gRPC: Trade stream %s disconnected (remaining_streams=%d)", streamID, streamCount)

		// Mark hedgebot as inactive when stream disconnects
		// Only if this was the last active stream
		if streamCount == 0 {
			s.app.SetHedgebotActive(false)
			log.Println("gRPC: Hedgebot marked as inactive - no active streams")
		}
	}()

	// Start goroutine to monitor app trade queue and forward to stream
	go s.forwardTradesToStream(streamChan, streamID)

	// Drain health pings from client to avoid flow-control stalls and update activity
	go func() {
		for {
			req, err := stream.Recv()
			if err != nil {
				if err == io.EOF {
					return
				}
				log.Printf("gRPC: Trade stream %s recv error: %v", streamID, err)
				return
			}
			// Treat any inbound message as proof-of-life from MT5
			s.app.SetHedgebotActive(true)
			// Rate-limit health logs to avoid JSONL spam
			if s.shouldLogHealth(req.GetSource(), 30*time.Second) {
				s.streamsMux.RLock()
				count := len(s.tradeStreams)
				s.streamsMux.RUnlock()
				log.Printf("gRPC: GetTrades ping from source: %s (stream_id=%s active_streams=%d)", req.GetSource(), streamID, count)
			}
		}
	}()

	// Handle incoming health requests and send trades
	for {
		// If this stream has been superseded, close it proactively
		s.mt5StreamMux.RLock()
		currentID := s.currentMT5StreamID
		s.mt5StreamMux.RUnlock()
		if currentID != "" && currentID != streamID {
			log.Printf("gRPC: Trade stream %s superseded by %s; closing stream", streamID, currentID)
			return status.Error(codes.Canceled, "superseded by new MT5 connection")
		}
		select {
		case <-stream.Context().Done():
			uptime := time.Since(startTime)
			log.Printf("gRPC: Trade stream %s context cancelled (uptime=%s)", streamID, uptime.Truncate(time.Millisecond))
			// If the stream repeatedly dies around the same short interval (<15s) without send errors,
			// flag a warning hinting at client-side streaming timeout configuration.
			if uptime < 15*time.Second {
				log.Printf("WARN: MT5 trade stream %s ended after %s (likely client-side idle timeout). Consider increasing/removing MT5 streaming timeout.", streamID, uptime.Truncate(time.Millisecond))
				blog.L().Warn("stream", fmt.Sprintf("mt5 stream ended early: %s", uptime.Truncate(time.Millisecond)), map[string]interface{}{"stream_id": streamID})
			}
			return nil
		case trade := <-streamChan:
			if trade == nil {
				log.Printf("gRPC: Received nil trade in stream %s, continuing", streamID)
				continue
			}

			// Final gate: suppress stale CLOSE_HEDGE right before sending to MT5
			if strings.EqualFold(trade.Action, "CLOSE_HEDGE") && trade.Mt5Ticket > 0 {
				if s.wasTicketRecentlyClosed(trade.Mt5Ticket, 10*time.Second) {
					log.Printf("gRPC: Suppressed stale CLOSE_HEDGE at send for ticket %d (trade %s)", trade.Mt5Ticket, trade.Id)
					blog.L().Info("close_sync", "suppressed stale CLOSE_HEDGE at send", map[string]interface{}{
						"mt5_ticket": trade.Mt5Ticket,
						"trade_id":   trade.Id,
						"action":     trade.Action,
						"stream_id":  streamID,
					})
					continue
				}
			}

			// Emit sizing hint presence for diagnostics
			if trade.GetNtPointsPer_1KLoss() <= 0 {
				extra := map[string]interface{}{
					"trade_id":              trade.Id,
					"base_id":               trade.BaseId,
					"action":                trade.Action,
					"instrument":            trade.Instrument,
					"nt_points_per_1k_loss": trade.GetNtPointsPer_1KLoss(),
				}
				// If this is an EVENT, include event payload details for diagnostics
				if strings.EqualFold(trade.Action, "EVENT") {
					extra["event_type"] = trade.EventType
					extra["elastic_current_profit"] = trade.ElasticCurrentProfit
					extra["elastic_profit_level"] = trade.ElasticProfitLevel
				}
				blog.L().Warn("stream", "sending trade without nt_points_per_1k_loss (EA will fallback)", extra)
			} else {
				extra := map[string]interface{}{
					"trade_id":              trade.Id,
					"base_id":               trade.BaseId,
					"action":                trade.Action,
					"instrument":            trade.Instrument,
					"nt_points_per_1k_loss": trade.GetNtPointsPer_1KLoss(),
				}
				// If this is an EVENT, include event payload details for diagnostics
				if strings.EqualFold(trade.Action, "EVENT") {
					extra["event_type"] = trade.EventType
					extra["elastic_current_profit"] = trade.ElasticCurrentProfit
					extra["elastic_profit_level"] = trade.ElasticProfitLevel
				}
				blog.L().Info("stream", "sending trade with nt_points_per_1k_loss", extra)
			}

			log.Printf("gRPC: Sending trade to MT5 stream - ID: %s, Action: %s", trade.Id, trade.Action)
			if err := s.sendTradeWithTimeout(stream, trade, 2*time.Second); err != nil {
				log.Printf("gRPC: Error sending trade to stream (id=%s): %v", streamID, err)
				return err
			}
		}
	}
}

// sendTradeWithTimeout attempts to send a trade on the server stream with a timeout to avoid indefinite blocking
func (s *Server) sendTradeWithTimeout(stream trading.TradingService_GetTradesServer, trade *trading.Trade, timeout time.Duration) error {
	done := make(chan error, 1)
	go func() {
		// Note: stream.Send is blocking; this goroutine will exit when send completes or context cancels
		done <- stream.Send(trade)
	}()

	select {
	case err := <-done:
		return err
	case <-time.After(timeout):
		return status.Error(codes.DeadlineExceeded, fmt.Sprintf("send timeout after %s", timeout))
	case <-stream.Context().Done():
		return stream.Context().Err()
	}
}

// forwardTradesToStream monitors the app trade queue and forwards trades to the stream
func (s *Server) forwardTradesToStream(streamChan chan<- *trading.Trade, streamID string) {
	log.Printf("gRPC: Starting trade forwarding for stream %s", streamID)

	// Lower poll interval to reduce perceived latency from enqueue -> MT5 send
	ticker := time.NewTicker(25 * time.Millisecond)
	defer ticker.Stop()

	for range ticker.C {
		// Check if stream is still active
		s.streamsMux.RLock()
		_, exists := s.tradeStreams[streamID]
		s.streamsMux.RUnlock()

		if !exists {
			log.Printf("gRPC: Stream %s no longer exists, stopping trade forwarding", streamID)
			return
		}

		// MULTI_TRADE_FIX: Drain ALL available trades from queue in each cycle, not just one
	drainLoop:
		for {
			trade := s.pollTradeFromApp()
			if trade == nil {
				break // No more trades in queue
			}

			// STALE_CLOSE_SUPPRESSION: If this is a CLOSE_HEDGE for a ticket we very recently
			// marked as closed, drop it to avoid MT5 "position not found" noise and races.
			if strings.EqualFold(trade.Action, "CLOSE_HEDGE") && trade.Mt5Ticket > 0 {
				if s.wasTicketRecentlyClosed(trade.Mt5Ticket, 10*time.Second) {
					log.Printf("gRPC: Dropping stale CLOSE_HEDGE for recently-closed ticket %d (trade %s)", trade.Mt5Ticket, trade.Id)
					blog.L().Info("close_sync", "dropped stale CLOSE_HEDGE due to prior MT5 close", map[string]interface{}{
						"mt5_ticket": trade.Mt5Ticket,
						"trade_id":   trade.Id,
						"action":     trade.Action,
					})
					continue
				}
			}

			select {
			case streamChan <- trade:
				log.Printf("gRPC: Forwarded trade %s to stream %s", trade.Id, streamID)
			default:
				log.Printf("gRPC: Stream %s buffer full, dropping trade %s", streamID, trade.Id)
				blog.L().Warn("stream", "stream buffer full - dropping trade", map[string]interface{}{"stream_id": streamID, "trade_id": trade.Id})
				break drainLoop // Stop trying if buffer is full
			}
		}
	}
}

// pollTradeFromApp attempts to get a trade from the app's trade queue
func (s *Server) pollTradeFromApp() *trading.Trade {
	tradeInterface := s.app.PollTradeFromQueue()
	if tradeInterface == nil {
		return nil
	}

	// The app.go returns main.Trade, we need to convert it to protobuf format
	// First convert to InternalTrade, then to proto
	internal := &InternalTrade{}

	// Use JSON marshaling to convert from main.Trade to InternalTrade
	jsonBytes, err := json.Marshal(tradeInterface)
	if err != nil {
		log.Printf("gRPC: Failed to marshal trade from queue: %v", err)
		return nil
	}

	if err := json.Unmarshal(jsonBytes, internal); err != nil {
		log.Printf("gRPC: Failed to unmarshal trade to internal format: %v", err)
		return nil
	}

	// Now convert InternalTrade to protobuf Trade
	return ConvertInternalToProtoTrade(internal)
}

// SubmitTradeResult handles trade execution results from MT5
func (s *Server) SubmitTradeResult(ctx context.Context, req *trading.MT5TradeResult) (*trading.GenericResponse, error) {
	log.Printf("gRPC: Received trade result - Ticket: %d, Status: %s", req.Ticket, req.Status)

	// If this was a closure, mark ticket as recently closed to suppress stale CLOSE_HEDGE
	if req.GetIsClose() && req.GetTicket() > 0 {
		s.markTicketClosed(req.GetTicket())
	}

	// Update hedgebot active status
	s.app.SetHedgebotActive(true)

	// Convert protobuf to internal format and handle
	result := convertProtoToInternalMT5Result(req)
	err := s.app.HandleMT5TradeResult(result)
	if err != nil {
		log.Printf("gRPC: Failed to handle MT5 trade result: %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to handle trade result: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process trade result")
	}

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Trade result processed successfully",
	}, nil
}

// markTicketClosed records the given MT5 ticket as recently closed
func (s *Server) markTicketClosed(ticket uint64) {
	s.rcMux.Lock()
	s.recentlyClosedTickets[ticket] = time.Now()
	// Optional pruning: keep map from growing too large
	if len(s.recentlyClosedTickets) > 1000 {
		cutoff := time.Now().Add(-15 * time.Second)
		for tk, t := range s.recentlyClosedTickets {
			if t.Before(cutoff) {
				delete(s.recentlyClosedTickets, tk)
			}
		}
	}
	s.rcMux.Unlock()
}

// wasTicketRecentlyClosed checks if a ticket was marked closed within a TTL
func (s *Server) wasTicketRecentlyClosed(ticket uint64, ttl time.Duration) bool {
	s.rcMux.Lock()
	defer s.rcMux.Unlock()
	t, ok := s.recentlyClosedTickets[ticket]
	if !ok {
		return false
	}
	if time.Since(t) <= ttl {
		return true
	}
	// Expired; cleanup
	delete(s.recentlyClosedTickets, ticket)
	return false
}

// NotifyHedgeClose handles hedge closure notifications from MT5
func (s *Server) NotifyHedgeClose(ctx context.Context, req *trading.HedgeCloseNotification) (*trading.GenericResponse, error) {
	log.Printf("gRPC: Received hedge close notification - BaseID: %s, Reason: %s",
		req.BaseId, req.ClosureReason)

	// Update hedgebot active status
	s.app.SetHedgebotActive(true)

	// Mark ticket as recently closed if provided
	if req.GetMt5Ticket() > 0 {
		s.markTicketClosed(req.GetMt5Ticket())
	}

	// Convert and handle notification
	notification := convertProtoToInternalHedgeClose(req)
	err := s.app.HandleHedgeCloseNotification(notification)
	if err != nil {
		log.Printf("gRPC: Failed to handle hedge close notification: %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to handle hedge close notification: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process notification")
	}

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Hedge close notification processed successfully",
	}, nil
}

// SubmitElasticUpdate handles elastic hedge updates
func (s *Server) SubmitElasticUpdate(ctx context.Context, req *trading.ElasticHedgeUpdate) (*trading.GenericResponse, error) {
	log.Printf("gRPC: Received elastic ping - BaseID: %s, ProfitLevel: %d",
		req.BaseId, req.ProfitLevel)

	// Convert and handle update
	update := convertProtoToInternalElasticUpdate(req)
	err := s.app.HandleElasticUpdate(update)
	if err != nil {
		log.Printf("gRPC: Failed to handle elastic ping: %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to handle elastic ping: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process elastic ping")
	}

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Elastic update processed successfully",
	}, nil
}

// SubmitTrailingUpdate handles trailing stop updates
func (s *Server) SubmitTrailingUpdate(ctx context.Context, req *trading.TrailingStopUpdate) (*trading.GenericResponse, error) {
	log.Printf("gRPC: Received trailing update - BaseID: %s, NewStopPrice: %.2f",
		req.BaseId, req.NewStopPrice)

	// Convert and handle update
	update := convertProtoToInternalTrailingUpdate(req)
	err := s.app.HandleTrailingStopUpdate(update)
	if err != nil {
		log.Printf("gRPC: Failed to handle trailing update: %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to handle trailing update: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process trailing update")
	}

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Trailing update processed successfully",
	}, nil
}

// HealthCheck handles health check requests
func (s *Server) HealthCheck(ctx context.Context, req *trading.HealthRequest) (*trading.HealthResponse, error) {
	if s.shouldLogHealth(req.Source, 30*time.Second) {
		log.Printf("gRPC: Health check from source: %s", req.Source)
	}

	// Update connection status based on source
	switch req.Source {
	case "hedgebot", "MT5_EA":
		s.app.SetHedgebotActive(true)
		// noisy; omit per-request log
	case "addon", "NT_ADDON", "nt_addon_init", "NT_ADDON_KEEPALIVE":
		s.app.SetAddonConnected(true)
		// noisy; omit per-request log
	}

	response := &trading.HealthResponse{
		Status:      "healthy",
		QueueSize:   int32(s.app.GetQueueSize()),
		NetPosition: int32(s.app.GetNetPosition()),
		HedgeSize:   s.app.GetHedgeSize(),
	}

	return response, nil
}

// shouldLogHealth returns true if enough time has passed since the last log for this source
func (s *Server) shouldLogHealth(source string, interval time.Duration) bool {
	if source == "" {
		source = "unknown"
	}
	s.healthLogMu.Lock()
	defer s.healthLogMu.Unlock()
	last := s.lastHealthLog[source]
	now := time.Now()
	if now.Sub(last) >= interval {
		s.lastHealthLog[source] = now
		return true
	}
	return false
}

// GetSettings handles settings requests
func (s *Server) GetSettings(ctx context.Context, req *trading.SettingsRequest) (*trading.SettingsResponse, error) {
	log.Printf("gRPC: Settings request for: %s", req.SettingName)

	// Basic settings map - can be extended to read from config file or database
	settings := map[string]string{
		"verbose-mode":          "true",
		"max-queue-size":        "1000",
		"connection-timeout":    "30",
		"retry-attempts":        "3",
		"hedge-ratio":           "1.0",
		"position-size-limit":   "100",
		"daily-loss-limit":      "5000",
		"max-concurrent-trades": "50",
	}

	value, exists := settings[req.SettingName]
	if !exists {
		log.Printf("gRPC: Unknown setting requested: %s", req.SettingName)
		return &trading.SettingsResponse{
			SettingName:  req.SettingName,
			SettingValue: "",
			Success:      false,
		}, nil
	}

	return &trading.SettingsResponse{
		SettingName:  req.SettingName,
		SettingValue: value,
		Success:      true,
	}, nil
}

// SystemHeartbeat handles system heartbeat requests
func (s *Server) SystemHeartbeat(ctx context.Context, req *trading.HeartbeatRequest) (*trading.HeartbeatResponse, error) {
	log.Printf("gRPC: Heartbeat from component: %s, Status: %s", req.Component, req.Status)

	// Treat NT addon heartbeat as proof-of-life
	switch strings.ToUpper(req.GetComponent()) {
	case "ADDON", "NT_ADDON", "NT_ADDON_INIT", "NT_ADDON_KEEPALIVE", "NT", "NINJATRADER":
		s.app.SetAddonConnected(true)
	}

	return &trading.HeartbeatResponse{
		Status:  "acknowledged",
		Message: "Heartbeat received successfully",
	}, nil
}

// Log handles unified logging events from clients (NT addon, MT5 EA, etc.)
func (s *Server) Log(ctx context.Context, req *trading.LogEvent) (*trading.LogAck, error) {
	// Map protobuf LogEvent to internal logging.Event and ingest
	log.Printf("gRPC: LoggingService received event from source=%s level=%s component=%s", req.GetSource(), req.GetLevel(), req.GetComponent())

	// Treat NT addon log traffic as proof-of-life to mark addon connected
	switch strings.ToUpper(req.GetSource()) {
	case "ADDON", "NT_ADDON", "NT_ADDON_INIT", "NT_ADDON_KEEPALIVE", "NT", "NINJATRADER":
		s.app.SetAddonConnected(true)
	}

	baseIDs := extractBaseIDs(req.GetBaseId(), req.GetMessage(), req.GetTags())
	accepted := 0

	if len(baseIDs) == 0 {
		// No base id found – pass through, but do not fabricate correlation
		ev := blog.Event{
			TimestampNS:   req.GetTimestampNs(),
			Source:        req.GetSource(),
			Level:         req.GetLevel(),
			Component:     req.GetComponent(),
			Message:       req.GetMessage(),
			BaseID:        req.GetBaseId(),
			TradeID:       req.GetTradeId(),
			NTOrderID:     req.GetNtOrderId(),
			MT5Ticket:     req.GetMt5Ticket(),
			QueueSize:     int(req.GetQueueSize()),
			NetPosition:   int(req.GetNetPosition()),
			HedgeSize:     req.GetHedgeSize(),
			ErrorCode:     req.GetErrorCode(),
			Stack:         req.GetStack(),
			Tags:          cloneTags(req.GetTags()),
			SchemaVersion: req.GetSchemaVersion(),
			CorrelationID: req.GetCorrelationId(),
		}
		blog.L().Ingest(ev)
		accepted++
		return &trading.LogAck{Accepted: uint32(accepted), Dropped: 0}, nil
	}

	// If any base ids exist, we split into one event per base and canonicalize correlation
	for _, b := range baseIDs {
		corr := md5Hex(strings.TrimSpace(b))
		tags := ensureTags(req.GetTags())
		tags["correlation_id"] = corr
		// If useful, we could also echo base_id tag
		if _, ok := tags["base_id"]; !ok {
			tags["base_id"] = b
		}
		ev := blog.Event{
			TimestampNS:   req.GetTimestampNs(),
			Source:        req.GetSource(),
			Level:         req.GetLevel(),
			Component:     req.GetComponent(),
			Message:       req.GetMessage(),
			BaseID:        b,
			TradeID:       req.GetTradeId(),
			NTOrderID:     req.GetNtOrderId(),
			MT5Ticket:     req.GetMt5Ticket(),
			QueueSize:     int(req.GetQueueSize()),
			NetPosition:   int(req.GetNetPosition()),
			HedgeSize:     req.GetHedgeSize(),
			ErrorCode:     req.GetErrorCode(),
			Stack:         req.GetStack(),
			Tags:          tags,
			SchemaVersion: req.GetSchemaVersion(),
			CorrelationID: corr, // canonical: MD5(base_id)
		}
		blog.L().Ingest(ev)
		accepted++
	}
	return &trading.LogAck{Accepted: uint32(accepted), Dropped: 0}, nil
}

// Helpers
var (
	jsonBaseIDRe  = regexp.MustCompile(`(?i)"base_id"\s*:\s*"([A-Za-z0-9_:\-\.]+)"`)
	plainBaseIDRe = regexp.MustCompile(`(?i)\bbase_id\s*=\s*([A-Za-z0-9_:\-\.]+)\b`)
	// Matches JSON arrays like: "base_ids": ["A","B"] (case-insensitive)
	jsonBaseIDsArrayRe = regexp.MustCompile(`(?i)"base_ids"\s*:\s*\[([^\]]*)\]`)
)

func extractBaseIDs(primary string, message string, tags map[string]string) []string {
	set := map[string]struct{}{}
	add := func(v string) {
		v = strings.TrimSpace(v)
		if v == "" {
			return
		}
		set[v] = struct{}{}
	}
	add(primary)
	if tags != nil {
		if t, ok := tags["base_id"]; ok {
			add(t)
		}
		// Support comma/space-separated list in tags["base_ids"]
		if t, ok := tags["base_ids"]; ok {
			// split on comma and/or whitespace
			for _, part := range strings.FieldsFunc(t, func(r rune) bool { return r == ',' || r == ' ' || r == '\t' || r == ';' }) {
				add(part)
			}
		}
	}
	// Scan message for any base_id occurrences (json/plain)
	for _, m := range jsonBaseIDRe.FindAllStringSubmatch(message, -1) {
		if len(m) > 1 {
			add(m[1])
		}
	}
	for _, m := range plainBaseIDRe.FindAllStringSubmatch(message, -1) {
		if len(m) > 1 {
			add(m[1])
		}
	}
	// Scan for JSON arrays base_ids: ["..."]
	if arrMatches := jsonBaseIDsArrayRe.FindAllStringSubmatch(message, -1); len(arrMatches) > 0 {
		// Extract quoted tokens within the array payload
		quotedTokenRe := regexp.MustCompile(`"([A-Za-z0-9_:\-\.]+)"`)
		for _, am := range arrMatches {
			if len(am) > 1 {
				payload := am[1]
				for _, q := range quotedTokenRe.FindAllStringSubmatch(payload, -1) {
					if len(q) > 1 {
						add(q[1])
					}
				}
			}
		}
	}
	// Materialize in a stable order (insertion order is not preserved in map; we can prefer: primary -> tags -> json -> plain)
	var out []string
	if primary != "" {
		out = append(out, primary)
	}
	if tags != nil {
		if t, ok := tags["base_id"]; ok && (primary == "" || t != primary) {
			out = append(out, t)
		}
		if t, ok := tags["base_ids"]; ok {
			// Append each parsed element if not already present
			seenLocal := map[string]struct{}{}
			for _, part := range strings.FieldsFunc(t, func(r rune) bool { return r == ',' || r == ' ' || r == '\t' || r == ';' }) {
				part = strings.TrimSpace(part)
				if part == "" {
					continue
				}
				if _, ok := seenLocal[part]; ok {
					continue
				}
				seenLocal[part] = struct{}{}
				out = append(out, part)
			}
		}
	}
	// Append uniques from regex matches not already present
	seen := map[string]struct{}{}
	for _, v := range out {
		seen[v] = struct{}{}
	}
	for _, m := range jsonBaseIDRe.FindAllStringSubmatch(message, -1) {
		if len(m) > 1 {
			if _, ok := seen[m[1]]; !ok {
				out = append(out, m[1])
				seen[m[1]] = struct{}{}
			}
		}
	}
	for _, m := range plainBaseIDRe.FindAllStringSubmatch(message, -1) {
		if len(m) > 1 {
			if _, ok := seen[m[1]]; !ok {
				out = append(out, m[1])
				seen[m[1]] = struct{}{}
			}
		}
	}
	// Include any discovered tokens from base_ids arrays
	if arrMatches := jsonBaseIDsArrayRe.FindAllStringSubmatch(message, -1); len(arrMatches) > 0 {
		quotedTokenRe := regexp.MustCompile(`"([A-Za-z0-9_:\-\.]+)"`)
		for _, am := range arrMatches {
			if len(am) > 1 {
				payload := am[1]
				for _, q := range quotedTokenRe.FindAllStringSubmatch(payload, -1) {
					if len(q) > 1 {
						if _, ok := seen[q[1]]; !ok {
							out = append(out, q[1])
							seen[q[1]] = struct{}{}
						}
					}
				}
			}
		}
	}
	return out
}

func ensureTags(in map[string]string) map[string]string {
	if in == nil {
		return map[string]string{}
	}
	// clone to avoid mutating incoming proto map (defensive)
	out := make(map[string]string, len(in)+2)
	for k, v := range in {
		out[k] = v
	}
	return out
}

func cloneTags(in map[string]string) map[string]string { return ensureTags(in) }

func md5Hex(s string) string {
	sum := md5.Sum([]byte(s))
	return hex.EncodeToString(sum[:])
}

// NTCloseHedge handles hedge closure requests from NinjaTrader
func (s *Server) NTCloseHedge(ctx context.Context, req *trading.HedgeCloseNotification) (*trading.GenericResponse, error) {
	log.Printf("gRPC: NT close hedge request - BaseID: %s", req.BaseId)

	// Convert and handle request
	request := convertProtoToInternalHedgeClose(req)
	err := s.app.HandleNTCloseHedgeRequest(request)
	if err != nil {
		log.Printf("gRPC: Failed to handle NT close hedge request: %v", err)
		return &trading.GenericResponse{
			Status:  "error",
			Message: "Failed to handle close hedge request: " + err.Error(),
		}, status.Error(codes.Internal, "Failed to process close hedge request")
	}

	return &trading.GenericResponse{
		Status:  "success",
		Message: "Close hedge request processed successfully",
	}, nil
}

// broadcastTradeToStreams sends a trade to all active streams
func (s *Server) broadcastTradeToStreams(trade *trading.Trade) {
	s.streamsMux.RLock()
	defer s.streamsMux.RUnlock()

	for streamID, streamChan := range s.tradeStreams {
		select {
		case streamChan <- trade:
			log.Printf("gRPC: Trade broadcasted to stream %s", streamID)
		default:
			log.Printf("gRPC: Stream %s buffer full, skipping trade", streamID)
		}
	}
}

// BroadcastMT5CloseNotification sends an MT5 closure notification to NinjaTrader via streams
func (s *Server) BroadcastMT5CloseNotification(notification interface{}) {
	log.Printf("gRPC: Broadcasting MT5 close notification: %+v", notification)

	// Convert the notification to a trading.Trade message
	protoTrade := convertMT5CloseNotificationToProtoTrade(notification)

	// Broadcast to all active streams (primarily NinjaTrader)
	s.broadcastTradeToStreams(protoTrade)
}

// BroadcastMT5CloseToNTStreams sends MT5 closure notifications only to NT streams (not MT5 streams)
// This prevents circular trades while still notifying NT to close original positions
func (s *Server) BroadcastMT5CloseToNTStreams(notification interface{}) {
	log.Printf("gRPC: Broadcasting MT5 close notification to NT streams only: %+v", notification)

	// Convert the notification to a trading.Trade message
	protoTrade := convertMT5CloseNotificationToProtoTrade(notification)

	if protoTrade != nil && protoTrade.OrderType == "NT_CLOSE_ACK" {
		log.Printf("gRPC: Tagging close as NT_CLOSE_ACK; NT should treat as acknowledgement only")
	}

	// Send only to NT bidirectional streams (not MT5 GetTrades streams)
	s.streamsMux.RLock()
	defer s.streamsMux.RUnlock()

	for streamID, streamChan := range s.tradeStreams {
		// Only send to NT bidirectional streams (streamID starts with "bidir_stream_")
		// Do NOT send to MT5 streams (streamID starts with "stream_") to prevent circular trades
		if strings.HasPrefix(streamID, "bidir_stream_") {
			select {
			case streamChan <- protoTrade:
				log.Printf("gRPC: MT5 closure notification sent to NT stream %s", streamID)
			default:
				log.Printf("gRPC: NT stream %s buffer full, skipping MT5 closure notification", streamID)
			}
		} else {
			log.Printf("gRPC: Skipping MT5 stream %s to prevent circular trades", streamID)
		}
	}
}

// TradingStream handles bidirectional streaming for real-time updates
func (s *Server) TradingStream(stream trading.StreamingService_TradingStreamServer) error {
	log.Println("gRPC: New bidirectional trading stream connected")

	// Create channel for this stream
	streamChan := make(chan *trading.Trade, 100)
	streamID := fmt.Sprintf("bidir_stream_%d", time.Now().UnixNano())

	s.streamsMux.Lock()
	s.tradeStreams[streamID] = streamChan
	s.streamsMux.Unlock()

	// Clean up on exit
	defer func() {
		s.streamsMux.Lock()
		delete(s.tradeStreams, streamID)
		close(streamChan)
		s.streamsMux.Unlock()
		log.Printf("gRPC: Bidirectional trading stream %s disconnected", streamID)
	}()

	// Mark addon as connected on stream establishment
	s.app.SetAddonConnected(true)

	// Handle the stream
	errChan := make(chan error, 2)

	// Goroutine for receiving trades from client
	go func() {
		for {
			trade, err := stream.Recv()
			if err != nil {
				errChan <- err
				return
			}

			// Any inbound message from NT proves life; refresh addon connection
			s.app.SetAddonConnected(true)

			// Process incoming trade (similar to SubmitTrade)
			log.Printf("gRPC: Received trade via bidirectional stream - ID: %s", trade.Id)

			// Dedup guard: skip if we've very recently processed this trade ID
			if s.wasRecentlyProcessed(trade.Id, 3*time.Second) {
				log.Printf("gRPC: Skipping duplicate streamed trade ID: %s", trade.Id)
				continue
			}
			s.markProcessed(trade.Id)

			// Enqueue with smart splitting to align MT5 tickets with NT contract count
			if err := s.enqueueTradeWithSplit(trade); err != nil {
				log.Printf("gRPC: Failed to add streamed trade(s) to queue: %v", err)
			}
		}
	}()

	// Goroutine for sending trades to client
	go func() {
		for {
			select {
			case <-stream.Context().Done():
				errChan <- stream.Context().Err()
				return
			case trade := <-streamChan:
				if err := stream.Send(trade); err != nil {
					errChan <- err
					return
				}
			}
		}
	}()

	// Wait for error or context cancellation
	return <-errChan
}

// wasRecentlyProcessed checks and expires recentTradeIDs using ttl.
func (s *Server) wasRecentlyProcessed(id string, ttl time.Duration) bool {
	if id == "" {
		return false
	}
	now := time.Now()
	cutoff := now.Add(-ttl)
	s.recentTradesMux.Lock()
	defer s.recentTradesMux.Unlock()
	// Clean old entries opportunistically
	for k, t := range s.recentTradeIDs {
		if t.Before(cutoff) {
			delete(s.recentTradeIDs, k)
		}
	}
	if t, ok := s.recentTradeIDs[id]; ok && t.After(cutoff) {
		return true
	}
	return false
}

// markProcessed records a trade id with current timestamp
func (s *Server) markProcessed(id string) {
	if id == "" {
		return
	}
	s.recentTradesMux.Lock()
	s.recentTradeIDs[id] = time.Now()
	s.recentTradesMux.Unlock()
}

// StatusStream handles streaming status updates
func (s *Server) StatusStream(stream trading.StreamingService_StatusStreamServer) error {
	log.Println("gRPC: New status stream connected")

	// Create channel for this stream
	streamChan := make(chan *trading.HealthResponse, 100)
	streamID := fmt.Sprintf("status_stream_%d", time.Now().UnixNano())

	s.healthStreamsMux.Lock()
	s.healthStreams[streamID] = streamChan
	s.healthStreamsMux.Unlock()

	// Clean up on exit
	defer func() {
		s.healthStreamsMux.Lock()
		delete(s.healthStreams, streamID)
		close(streamChan)
		s.healthStreamsMux.Unlock()
		log.Printf("gRPC: Status stream %s disconnected", streamID)
	}()

	// Send periodic status updates
	ticker := time.NewTicker(5 * time.Second)
	defer ticker.Stop()

	for {
		select {
		case <-stream.Context().Done():
			return nil
		case <-ticker.C:
			// Send status update
			status := &trading.HealthResponse{
				Status:      "healthy",
				QueueSize:   int32(s.app.GetQueueSize()),
				NetPosition: int32(s.app.GetNetPosition()),
				HedgeSize:   s.app.GetHedgeSize(),
			}

			if err := stream.Send(status); err != nil {
				log.Printf("gRPC: Error sending status update: %v", err)
				return err
			}
		case status := <-streamChan:
			if err := stream.Send(status); err != nil {
				log.Printf("gRPC: Error sending status update: %v", err)
				return err
			}
		}
	}
}

// ElasticUpdatesStream handles streaming elastic hedge updates
func (s *Server) ElasticUpdatesStream(stream trading.StreamingService_ElasticUpdatesStreamServer) error {
	log.Println("gRPC: New elastic ping stream connected")

	for {
		update, err := stream.Recv()
		if err != nil {
			log.Printf("gRPC: Elastic updates stream error: %v", err)
			return err
		}

		// Process elastic ping
		internalUpdate := convertProtoToInternalElasticUpdate(update)
		if err := s.app.HandleElasticUpdate(internalUpdate); err != nil {
			response := &trading.GenericResponse{
				Status:  "error",
				Message: "Failed to process elastic ping: " + err.Error(),
			}
			if err := stream.Send(response); err != nil {
				return err
			}
		} else {
			response := &trading.GenericResponse{
				Status:  "success",
				Message: "Elastic update processed successfully",
			}
			if err := stream.Send(response); err != nil {
				return err
			}
		}
	}
}

// TrailingUpdatesStream handles streaming trailing stop updates
func (s *Server) TrailingUpdatesStream(stream trading.StreamingService_TrailingUpdatesStreamServer) error {
	log.Println("gRPC: New trailing updates stream connected")

	for {
		update, err := stream.Recv()
		if err != nil {
			log.Printf("gRPC: Trailing updates stream error: %v", err)
			return err
		}

		// Process trailing update
		internalUpdate := convertProtoToInternalTrailingUpdate(update)
		if err := s.app.HandleTrailingStopUpdate(internalUpdate); err != nil {
			response := &trading.GenericResponse{
				Status:  "error",
				Message: "Failed to process trailing update: " + err.Error(),
			}
			if err := stream.Send(response); err != nil {
				return err
			}
		} else {
			response := &trading.GenericResponse{
				Status:  "success",
				Message: "Trailing update processed successfully",
			}
			if err := stream.Send(response); err != nil {
				return err
			}
		}
	}
}
