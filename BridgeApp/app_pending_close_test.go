package main

import (
	"fmt"
	"testing"
	"time"
)

// helper to drain a trade from the queue quickly
func drainOne(a *App) (Trade, bool) {
	t := a.PollTradeFromQueue()
	if t == nil {
		return Trade{}, false
	}
	tr, ok := t.(Trade)
	return tr, ok
}

func TestBaseIdOnlyFallbackWhenNoTickets(t *testing.T) {
	a := NewApp()

	baseID := "TEST_BASE_1"
	req := map[string]interface{}{
		"BaseID":              baseID,
		"ClosedHedgeQuantity": 2.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_initiated",
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected base_id-only CLOSE_HEDGE fallback when no tickets are known")
	}
	if tr.Action != "CLOSE_HEDGE" || tr.BaseID != baseID || tr.MT5Ticket != 0 || tr.TotalQuantity != 2 {
		t.Fatalf("unexpected base_id-only fallback trade: %+v", tr)
	}

	a.mt5TicketMux.RLock()
	pend := a.pendingCloses[baseID]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 0 {
		t.Fatalf("expected no pending close intents after base_id-only fallback; got: %+v", pend)
	}
}

// Removed pending-dispatch tests since behavior now dispatches base_id-only immediately when no tickets are known.

func TestTargetedCloseEnqueuesImmediately(t *testing.T) {
	a := NewApp()
	baseID := "TEST_BASE_3"

	// Request includes explicit mt5 ticket
	req := map[string]interface{}{
		"BaseID":              baseID,
		"ClosedHedgeQuantity": 1.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"MT5Ticket":           float64(3333), // simulate JSON number
		"ClosureReason":       "NT_initiated",
	}
	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected one CLOSE_HEDGE enqueued immediately for targeted ticket")
	}
	if tr.MT5Ticket != 3333 || tr.Action != "CLOSE_HEDGE" || tr.TotalQuantity != 1 {
		t.Fatalf("unexpected targeted trade: %+v", tr)
	}
}

func TestRemainderFallsBackToBaseIdOnly(t *testing.T) {
	a := NewApp()
	baseID := "TEST_BASE_4"

	// Pre-populate one known ticket
	a.mt5TicketMux.Lock()
	a.baseIdToTickets[baseID] = []uint64{4444}
	a.mt5TicketMux.Unlock()

	// Ask to close 2 -> one allocated by known ticket, one base_id-only fallback remainder
	req := map[string]interface{}{
		"BaseID":              baseID,
		"ClosedHedgeQuantity": 2.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_initiated",
	}
	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected one CLOSE_HEDGE enqueued for known ticket")
	}
	if tr.MT5Ticket != 4444 || tr.TotalQuantity != 1 {
		t.Fatalf("unexpected allocated trade: %+v", tr)
	}

	remainder, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected base_id-only CLOSE_HEDGE remainder fallback")
	}
	if remainder.Action != "CLOSE_HEDGE" || remainder.BaseID != baseID || remainder.MT5Ticket != 0 || remainder.TotalQuantity != 1 {
		t.Fatalf("unexpected base_id-only remainder trade: %+v", remainder)
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses[baseID]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 0 {
		t.Fatalf("expected no pending remainder after base_id-only fallback; got: %+v", pend)
	}
}

func TestAllocatedTicketIsNotReusedFromLegacyMapping(t *testing.T) {
	a := NewApp()
	baseID := "TEST_BASE_LEGACY_REUSE"

	a.mt5TicketMux.Lock()
	a.mt5TicketToBaseId[5555] = baseID
	a.baseIdToMT5Ticket[baseID] = 5555
	a.baseIdToTickets[baseID] = []uint64{5555}
	a.mt5TicketMux.Unlock()

	req := map[string]interface{}{
		"BaseID":              baseID,
		"ClosedHedgeQuantity": 1.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_initiated",
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("first HandleNTCloseHedgeRequest error: %v", err)
	}

	first, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected first CLOSE_HEDGE")
	}
	if first.MT5Ticket != 5555 {
		t.Fatalf("expected first close to target ticket 5555, got %+v", first)
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("second HandleNTCloseHedgeRequest error: %v", err)
	}

	second, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected second CLOSE_HEDGE fallback")
	}
	if second.MT5Ticket == 5555 {
		t.Fatalf("stale legacy ticket was reused for a second close: %+v", second)
	}
}

func TestBaseIdOnlyFallbackUsesRequestedBaseIDWhenCrossRefExists(t *testing.T) {
	a := NewApp()

	// Prepopulate cross-reference: requested -> related
	a.mt5TicketMux.Lock()
	a.baseIdCrossRef["TRD_A"] = "TRD_B"
	a.mt5TicketMux.Unlock()

	req := map[string]interface{}{
		"BaseID":              "TRD_A",
		"ClosedHedgeQuantity": 1.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_initiated",
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected base_id-only fallback trade for requested BaseID")
	}
	if tr.BaseID != "TRD_A" || tr.MT5Ticket != 0 || tr.TotalQuantity != 1 {
		t.Fatalf("expected fallback to use requested BaseID TRD_A; got: %+v", tr)
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses["TRD_A"]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 0 {
		t.Fatalf("expected no pending close after requested-base fallback; got: %+v", pend)
	}
}

func TestBaseIdOnlyFallbackUsesRequestedBaseIDWhenMetadataMatchesOtherTrade(t *testing.T) {
	a := NewApp()

	// Prepopulate instrument/account metadata under a different BaseID
	a.mt5TicketMux.Lock()
	a.baseIdToInstrument["TRD_B"] = "NQ"
	a.baseIdToAccount["TRD_B"] = "Sim101"
	a.mt5TicketMux.Unlock()

	// No tickets known anywhere forces base_id-only fallback using the requested BaseID.
	req := map[string]interface{}{
		"BaseID":              "TRD_A",
		"ClosedHedgeQuantity": 1.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_initiated",
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected base_id-only fallback trade for requested BaseID")
	}
	if tr.BaseID != "TRD_A" || tr.MT5Ticket != 0 || tr.TotalQuantity != 1 {
		t.Fatalf("expected fallback to use requested BaseID TRD_A; got: %+v", tr)
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses["TRD_A"]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 0 {
		t.Fatalf("expected no pending close after requested-base fallback; got: %+v", pend)
	}
}

func TestBaseIdOnlyFallbackMarksNTCloseAckByBaseID(t *testing.T) {
	a := NewApp()

	baseID := "TEST_BASE_ACK"
	req := map[string]interface{}{
		"BaseID":              baseID,
		"ClosedHedgeQuantity": 1.0,
		"NTInstrumentSymbol":  "NQ",
		"NTAccountName":       "Sim101",
		"ClosureReason":       "NT_stop_close",
	}

	if err := a.HandleNTCloseHedgeRequest(req); err != nil {
		t.Fatalf("HandleNTCloseHedgeRequest error: %v", err)
	}

	tr, ok := drainOne(a)
	if !ok {
		t.Fatalf("expected base_id-only CLOSE_HEDGE fallback when no tickets are known")
	}
	if tr.BaseID != baseID || tr.MT5Ticket != 0 || tr.Action != "CLOSE_HEDGE" {
		t.Fatalf("unexpected fallback trade: %+v", tr)
	}

	if origin := a.consumeNTCloseOrigin(baseID, 987654, 5*time.Second); origin != "NT_CLOSE_ACK" {
		t.Fatalf("expected base-id close intent to classify later MT5 close as NT_CLOSE_ACK, got %s", origin)
	}
	if origin := a.consumeNTCloseOrigin(baseID, 987654, 5*time.Second); origin != "MT5_CLOSE" {
		t.Fatalf("expected NT close intent to be consumed after one ack, got %s", origin)
	}
}

func TestNTCloseHedgePreemptsFullEventQueue(t *testing.T) {
	tests := []struct {
		name          string
		closureReason string
	}{
		{name: "full_close", closureReason: "NT_full_close"},
		{name: "daily_loss_flatten", closureReason: "NT_DAILY_LOSS_LIMIT"},
	}

	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			a := NewApp()

			for i := 0; i < cap(a.tradeQueue); i++ {
				a.tradeQueue <- Trade{
					ID:        fmt.Sprintf("event_%d", i),
					BaseID:    "NOISE",
					Action:    "EVENT",
					EventType: "elastic_update",
				}
			}

			baseID := "TEST_FULL_QUEUE_CLOSE"
			req := map[string]interface{}{
				"BaseID":              baseID,
				"ClosedHedgeQuantity": 1.0,
				"NTInstrumentSymbol":  "NQ",
				"NTAccountName":       "Sim101",
				"ClosureReason":       tt.closureReason,
			}

			if err := a.HandleNTCloseHedgeRequest(req); err != nil {
				t.Fatalf("HandleNTCloseHedgeRequest should not fail when queue is full of events: %v", err)
			}

			foundClose := false
			for {
				tr, ok := drainOne(a)
				if !ok {
					break
				}
				if tr.Action == "CLOSE_HEDGE" && tr.BaseID == baseID && tr.TotalQuantity == 1 {
					foundClose = true
				}
			}
			if !foundClose {
				t.Fatalf("expected CLOSE_HEDGE for %s to be present after preempting full event queue", baseID)
			}
		})
	}
}
