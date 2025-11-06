package main

import (
	"testing"
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

func TestPendingOnlyWhenNoTickets(t *testing.T) {
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

	// Ticket-only policy: no trade enqueued, just pending recorded
	if _, ok := drainOne(a); ok {
		t.Fatalf("expected no CLOSE_HEDGE enqueued when no tickets known (pending-only)")
	}

	// Pending should be recorded so that later-arriving tickets can complete the close
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses[baseID]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 1 || pend[0].qty != 2 || pend[0].instrument != "NQ" || pend[0].account != "Sim101" {
		t.Fatalf("expected one pending close intent (qty=2, NQ/Sim101); got: %+v", pend)
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

func TestRemainderBecomesPendingOnly(t *testing.T) {
	a := NewApp()
	baseID := "TEST_BASE_4"

	// Pre-populate one known ticket
	a.mt5TicketMux.Lock()
	a.baseIdToTickets[baseID] = []uint64{4444}
	a.mt5TicketMux.Unlock()

	// Ask to close 2 -> one allocated by known ticket, one pending remainder
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

	// verify remainder is pending-only (no additional trade enqueued)
	if _, ok := drainOne(a); ok {
		t.Fatalf("expected no base_id-only remainder enqueued; should be pending-only")
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses[baseID]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 1 || pend[0].qty != 1 || pend[0].instrument != "NQ" || pend[0].account != "Sim101" {
		t.Fatalf("expected one pending remainder (qty=1, NQ/Sim101); got: %+v", pend)
	}
}

// New tests to validate BaseID alignment for base_id-only CLOSE_HEDGE
func TestBaseIdAlignmentAffectsPending_CrossRef(t *testing.T) {
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

	// No trade enqueued; pending should be recorded under aligned BaseID (TRD_B)
	if _, ok := drainOne(a); ok {
		t.Fatalf("expected no base_id-only trade enqueued; pending-only policy")
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses["TRD_A"]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 1 || pend[0].qty != 1 || pend[0].instrument != "NQ" || pend[0].account != "Sim101" {
		t.Fatalf("expected pending recorded under TRD_A with qty=1; got: %+v", pend)
	}
}

func TestBaseIdAlignmentAffectsPending_ByInstrumentAccount(t *testing.T) {
	a := NewApp()

	// Prepopulate instrument/account metadata under a different BaseID
	a.mt5TicketMux.Lock()
	a.baseIdToInstrument["TRD_B"] = "NQ"
	a.baseIdToAccount["TRD_B"] = "Sim101"
	a.mt5TicketMux.Unlock()

	// No tickets known anywhere forces pending-only path
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

	// No trade enqueued; pending should be recorded under aligned BaseID (TRD_B)
	if _, ok := drainOne(a); ok {
		t.Fatalf("expected no base_id-only trade enqueued; pending-only policy")
	}
	a.mt5TicketMux.RLock()
	pend := a.pendingCloses["TRD_A"]
	a.mt5TicketMux.RUnlock()
	if len(pend) != 1 || pend[0].qty != 1 || pend[0].instrument != "NQ" || pend[0].account != "Sim101" {
		t.Fatalf("expected pending recorded under TRD_A with qty=1; got: %+v", pend)
	}
}
