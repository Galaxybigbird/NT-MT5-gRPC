package main

import (
	"context"
	"crypto/md5"
	"encoding/json"
	"testing"
	"time"

	grpcserver "BridgeApp/internal/grpc"
	trading "BridgeApp/internal/grpc/proto"
	blog "BridgeApp/internal/logging"
	"fmt"
	"log"
	"net"
	"os"
	"path/filepath"
	"strings"

	"google.golang.org/grpc"
	"google.golang.org/grpc/credentials/insecure"
	"google.golang.org/grpc/test/bufconn"
)

// MockApp implements the AppInterface for testing
type MockApp struct {
	trades         []interface{}
	netPos         int
	hedgeSize      float64
	queueSize      int
	addonConn      bool
	hedgebotActive bool
}

func (m *MockApp) GetTradeQueue() chan interface{} {
	ch := make(chan interface{}, 10)
	return ch
}

func (m *MockApp) PollTradeFromQueue() interface{} {
	if len(m.trades) > 0 {
		trade := m.trades[0]
		m.trades = m.trades[1:]
		return trade
	}
	return nil
}

func (m *MockApp) AddToTradeQueue(trade interface{}) error {
	m.trades = append(m.trades, trade)
	m.queueSize = len(m.trades)
	return nil
}

func (m *MockApp) GetNetPosition() int                                         { return m.netPos }
func (m *MockApp) GetHedgeSize() float64                                       { return m.hedgeSize }
func (m *MockApp) GetQueueSize() int                                           { return m.queueSize }
func (m *MockApp) IsAddonConnected() bool                                      { return m.addonConn }
func (m *MockApp) IsHedgebotActive() bool                                      { return m.hedgebotActive }
func (m *MockApp) SetAddonConnected(connected bool)                            { m.addonConn = connected }
func (m *MockApp) SetHedgebotActive(active bool)                               { m.hedgebotActive = active }
func (m *MockApp) AddToTradeHistory(trade interface{})                         {}
func (m *MockApp) HandleHedgeCloseNotification(notification interface{}) error { return nil }
func (m *MockApp) HandleMT5TradeResult(result interface{}) error               { return nil }
func (m *MockApp) HandleElasticUpdate(update interface{}) error                { return nil }
func (m *MockApp) HandleTrailingStopUpdate(update interface{}) error           { return nil }
func (m *MockApp) HandleNTCloseHedgeRequest(request interface{}) error         { return nil }

const bufSize = 1024 * 1024

var lis *bufconn.Listener

func bufDialer(context.Context, string) (net.Conn, error) {
	return lis.Dial()
}

func TestGRPCServer(t *testing.T) {
	// Force logs directory to a temp path for deterministic assertions
	tmpDir := filepath.Join(os.TempDir(), "bridgeapp-test-logs")
	_ = os.MkdirAll(tmpDir, 0o755)
	os.Setenv("BRIDGE_LOG_DIR", tmpDir)
	// Clean previous logs in this folder
	if entries, err := os.ReadDir(tmpDir); err == nil {
		for _, e := range entries {
			_ = os.Remove(filepath.Join(tmpDir, e.Name()))
		}
	}
	// Setup
	lis = bufconn.Listen(bufSize)

	mockApp := &MockApp{
		trades:    make([]interface{}, 0),
		netPos:    0,
		hedgeSize: 0.0,
		queueSize: 0,
	}

	server := grpcserver.NewGRPCServer(mockApp)

	s := grpc.NewServer()
	trading.RegisterTradingServiceServer(s, server)
	trading.RegisterStreamingServiceServer(s, server)
	trading.RegisterLoggingServiceServer(s, server)

	go func() {
		if err := s.Serve(lis); err != nil {
			log.Printf("Server exited with error: %v", err)
		}
	}()
	defer s.Stop()

	// Connect
	conn, err := grpc.DialContext(context.Background(), "bufnet",
		grpc.WithContextDialer(bufDialer),
		grpc.WithTransportCredentials(insecure.NewCredentials()))
	if err != nil {
		t.Fatalf("Failed to dial bufnet: %v", err)
	}
	defer conn.Close()

	client := trading.NewTradingServiceClient(conn)

	// Also create a logging client for the logging service
	logClient := trading.NewLoggingServiceClient(conn)

	// Test SubmitTrade
	t.Run("SubmitTrade", func(t *testing.T) {
		trade := &trading.Trade{
			Id:          "test-001",
			BaseId:      "base-001",
			Timestamp:   time.Now().Unix(),
			Action:      "buy",
			Quantity:    1.0,
			Price:       50000.0,
			Instrument:  "NQ",
			AccountName: "TestAccount",
		}

		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		response, err := client.SubmitTrade(ctx, trade)
		if err != nil {
			t.Fatalf("SubmitTrade failed: %v", err)
		}

		if response.Status != "success" {
			t.Errorf("Expected status 'success', got '%s'", response.Status)
		}

		// Verify addon connection was set
		if !mockApp.IsAddonConnected() {
			t.Error("Expected addon to be marked as connected")
		}

		// Verify trade was added to queue
		if mockApp.GetQueueSize() == 0 {
			t.Error("Expected trade to be added to queue")
		}

		log.Printf("✓ gRPC SubmitTrade test passed")
	})

	// Test HealthCheck
	t.Run("HealthCheck", func(t *testing.T) {
		healthReq := &trading.HealthRequest{
			Source:        "test_client",
			OpenPositions: 1,
		}

		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		response, err := client.HealthCheck(ctx, healthReq)
		if err != nil {
			t.Fatalf("HealthCheck failed: %v", err)
		}

		if response.Status != "healthy" {
			t.Errorf("Expected status 'healthy', got '%s'", response.Status)
		}

		if response.QueueSize != int32(mockApp.GetQueueSize()) {
			t.Errorf("Expected queue size %d, got %d", mockApp.GetQueueSize(), response.QueueSize)
		}

		log.Printf("✓ gRPC HealthCheck test passed")
	})

	// Test SubmitTradeResult
	t.Run("SubmitTradeResult", func(t *testing.T) {
		result := &trading.MT5TradeResult{
			Status:  "executed",
			Ticket:  12345,
			Volume:  1.0,
			IsClose: false,
			Id:      "test-001",
		}

		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		response, err := client.SubmitTradeResult(ctx, result)
		if err != nil {
			t.Fatalf("SubmitTradeResult failed: %v", err)
		}

		if response.Status != "success" {
			t.Errorf("Expected status 'success', got '%s'", response.Status)
		}

		// Verify hedgebot was marked as active
		if !mockApp.IsHedgebotActive() {
			t.Error("Expected hedgebot to be marked as active")
		}

		log.Printf("✓ gRPC SubmitTradeResult test passed")
	})

	// Test ElasticUpdate
	t.Run("SubmitElasticUpdate", func(t *testing.T) {
		update := &trading.ElasticHedgeUpdate{
		EventType:     "elastic_ping",
			Action:        "update",
			BaseId:        "base-001",
			CurrentProfit: 500.0,
			ProfitLevel:   2,
			Timestamp:     time.Now().Format(time.RFC3339),
		}

		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		response, err := client.SubmitElasticUpdate(ctx, update)
		if err != nil {
			t.Fatalf("SubmitElasticUpdate failed: %v", err)
		}

		if response.Status != "success" {
			t.Errorf("Expected status 'success', got '%s'", response.Status)
		}

		log.Printf("✓ gRPC SubmitElasticUpdate test passed")
	})

	// Test TrailingUpdate
	t.Run("SubmitTrailingUpdate", func(t *testing.T) {
		update := &trading.TrailingStopUpdate{
			EventType:    "trailing_stop_update",
			BaseId:       "base-001",
			NewStopPrice: 49500.0,
			TrailingType: "percentage",
			CurrentPrice: 50000.0,
			Timestamp:    time.Now().Format(time.RFC3339),
		}

		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()

		response, err := client.SubmitTrailingUpdate(ctx, update)
		if err != nil {
			t.Fatalf("SubmitTrailingUpdate failed: %v", err)
		}

		if response.Status != "success" {
			t.Errorf("Expected status 'success', got '%s'", response.Status)
		}

		log.Printf("✓ gRPC SubmitTrailingUpdate test passed")
	})

	// Test LoggingService.Log writes to logs/unified-*.jsonl
	t.Run("LoggingServiceLogWritesFile", func(t *testing.T) {
		// Ensure logger is started to avoid dropping events
		blog.L().EnsureStarted("test")

		// Send a log event
		evt := &trading.LogEvent{
			TimestampNs: time.Now().UnixNano(),
			Source:      "test_addon",
			Level:       "INFO",
			Component:   "unit_test",
			Message:     "hello from test",
		}

		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		ack, err := logClient.Log(ctx, evt)
		if err != nil {
			t.Fatalf("LoggingService.Log failed: %v", err)
		}
		if ack.GetAccepted() != 1 {
			t.Fatalf("expected accepted=1, got %d", ack.GetAccepted())
		}

		// Wait briefly for async writer to flush
		time.Sleep(500 * time.Millisecond)

		// Verify a unified-*.jsonl exists under temp logs dir
		logsDir := tmpDir
		entries, err := os.ReadDir(logsDir)
		if err != nil {
			t.Fatalf("failed to read logs dir: %v", err)
		}
		found := false
		for _, e := range entries {
			name := e.Name()
			if strings.HasPrefix(name, "unified-") && strings.HasSuffix(name, ".jsonl") {
				// Optionally, read the file to check content contains our message
				b, readErr := os.ReadFile(filepath.Join(logsDir, name))
				if readErr == nil && strings.Contains(string(b), "hello from test") {
					found = true
					break
				}
			}
		}
		if !found {
			t.Fatalf("expected a unified-*.jsonl containing the test message, but none found")
		}
		log.Printf("✓ LoggingService.Log writes to unified JSONL")
	})

	// Test canonicalization: single base_id results in deterministic correlation_id
	t.Run("LoggingServiceCanonicalizesCorrelation", func(t *testing.T) {
		blog.L().EnsureStarted("test")
		msg := "canonicalization-test-" + time.Now().Format("150405.000")
		evt := &trading.LogEvent{
			TimestampNs:   time.Now().UnixNano(),
			Source:        "test_addon",
			Level:         "INFO",
			Component:     "unit_test",
			Message:       msg,
			BaseId:        "TESTBASE1",
			CorrelationId: "should_be_ignored",
		}
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		ack, err := logClient.Log(ctx, evt)
		if err != nil {
			t.Fatalf("Log failed: %v", err)
		}
		if ack.GetAccepted() != 1 {
			t.Fatalf("expected accepted=1, got %d", ack.GetAccepted())
		}
		time.Sleep(500 * time.Millisecond)
		// Find our line and verify correlation_id = md5(base_id)
		logsDir := tmpDir
		entries, err := os.ReadDir(logsDir)
		if err != nil {
			t.Fatalf("failed to read logs dir: %v", err)
		}
		found := false
		expectedCorr := testMd5Hex("TESTBASE1")
		for _, e := range entries {
			name := e.Name()
			if !strings.HasPrefix(name, "unified-") || !strings.HasSuffix(name, ".jsonl") {
				continue
			}
			b, readErr := os.ReadFile(filepath.Join(logsDir, name))
			if readErr != nil {
				continue
			}
			for _, line := range strings.Split(string(b), "\n") {
				if !strings.Contains(line, msg) {
					continue
				}
				var ev struct {
					Message       string `json:"message"`
					BaseID        string `json:"base_id"`
					CorrelationID string `json:"correlation_id"`
				}
				if err := json.Unmarshal([]byte(line), &ev); err == nil {
					if ev.Message == msg && ev.BaseID == "TESTBASE1" && ev.CorrelationID == expectedCorr {
						found = true
						break
					}
				}
			}
		}
		if !found {
			t.Fatalf("expected canonicalized correlation_id for TESTBASE1 not found")
		}
		log.Printf("✓ LoggingService canonicalizes correlation_id from base_id")
	})

	// Test splitting: multiple base_id occurrences yield multiple events
	t.Run("LoggingServiceSplitsMultiBase", func(t *testing.T) {
		blog.L().EnsureStarted("test")
		msg := "split-multi-test-" + time.Now().Format("150405.000") + " base_id=MB1 then base_id=MB2"
		evt := &trading.LogEvent{
			TimestampNs: time.Now().UnixNano(),
			Source:      "test_addon",
			Level:       "INFO",
			Component:   "unit_test",
			Message:     msg,
		}
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		ack, err := logClient.Log(ctx, evt)
		if err != nil {
			t.Fatalf("Log failed: %v", err)
		}
		if ack.GetAccepted() != 2 {
			t.Fatalf("expected accepted=2 for split, got %d", ack.GetAccepted())
		}
		time.Sleep(600 * time.Millisecond)
		logsDir := tmpDir
		entries, err := os.ReadDir(logsDir)
		if err != nil {
			t.Fatalf("failed to read logs dir: %v", err)
		}
		seenMB1, seenMB2 := false, false
		for _, e := range entries {
			name := e.Name()
			if !strings.HasPrefix(name, "unified-") || !strings.HasSuffix(name, ".jsonl") {
				continue
			}
			b, readErr := os.ReadFile(filepath.Join(logsDir, name))
			if readErr != nil {
				continue
			}
			for _, line := range strings.Split(string(b), "\n") {
				if !strings.Contains(line, msg) {
					continue
				}
				var ev struct {
					BaseID  string `json:"base_id"`
					Message string `json:"message"`
				}
				if err := json.Unmarshal([]byte(line), &ev); err == nil && ev.Message == msg {
					if ev.BaseID == "MB1" {
						seenMB1 = true
					}
					if ev.BaseID == "MB2" {
						seenMB2 = true
					}
				}
			}
		}
		if !(seenMB1 && seenMB2) {
			t.Fatalf("expected split events for MB1 and MB2, got MB1=%v MB2=%v", seenMB1, seenMB2)
		}
		log.Printf("✓ LoggingService splits multi-base messages into multiple events")
	})
}

func TestProtocolBufferConversions(t *testing.T) {
	// Test internal trade to proto conversion
	t.Run("InternalToProtoTrade", func(t *testing.T) {
		internal := &grpcserver.InternalTrade{
			ID:              "test-123",
			BaseID:          "base-123",
			Time:            time.Now(),
			Action:          "buy",
			Quantity:        2.0,
			Price:           51000.0,
			TotalQuantity:   2,
			ContractNum:     1,
			OrderType:       "ENTRY",
			MeasurementPips: 50,
			RawMeasurement:  0.5,
			Instrument:      "NQ",
			AccountName:     "TestAccount",
			NTBalance:       100000.0,
			NTDailyPnL:      1500.0,
			NTTradeResult:   "pending",
			NTSessionTrades: 5,
		}

		proto := grpcserver.ConvertInternalToProtoTrade(internal)

		if proto.Id != internal.ID {
			t.Errorf("ID conversion failed: expected %s, got %s", internal.ID, proto.Id)
		}

		if proto.Action != internal.Action {
			t.Errorf("Action conversion failed: expected %s, got %s", internal.Action, proto.Action)
		}

		if proto.Quantity != internal.Quantity {
			t.Errorf("Quantity conversion failed: expected %f, got %f", internal.Quantity, proto.Quantity)
		}

		log.Printf("✓ InternalToProtoTrade conversion test passed")
	})
}

// local helper for expected correlation
func testMd5Hex(s string) string {
	h := md5.New()
	_, _ = h.Write([]byte(s))
	return fmt.Sprintf("%x", h.Sum(nil))
}

// Benchmark gRPC performance
func BenchmarkGRPCSubmitTrade(b *testing.B) {
	// Setup
	lis = bufconn.Listen(bufSize)

	mockApp := &MockApp{trades: make([]interface{}, 0)}
	server := grpcserver.NewGRPCServer(mockApp)

	s := grpc.NewServer()
	trading.RegisterTradingServiceServer(s, server)

	go func() {
		s.Serve(lis)
	}()
	defer s.Stop()

	conn, _ := grpc.DialContext(context.Background(), "bufnet",
		grpc.WithContextDialer(bufDialer),
		grpc.WithTransportCredentials(insecure.NewCredentials()))
	defer conn.Close()

	client := trading.NewTradingServiceClient(conn)

	trade := &trading.Trade{
		Id:       "bench-trade",
		BaseId:   "bench-base",
		Action:   "buy",
		Quantity: 1.0,
		Price:    50000.0,
	}

	// Benchmark
	b.ResetTimer()
	for i := 0; i < b.N; i++ {
		ctx, cancel := context.WithTimeout(context.Background(), 1*time.Second)
		client.SubmitTrade(ctx, trade)
		cancel()
	}
}
