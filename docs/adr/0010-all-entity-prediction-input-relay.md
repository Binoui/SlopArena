# ADR-0010: All-entity prediction via input relay

Online play at internet RTT exposes 1–6 ticks of input lag under the Phase-1 raw-state display; rollback fixes it, but re-simulating opponents requires their inputs, which the client has no source for — the server→client protocol is state-only. Decision: every client predicts **all entities** (self + opponents, capped at 10) and the server relays each entity's consumed input — or an explicit no-input marker — alongside its state packet (+20B/entity max, ~57KB/s @ 60Hz×10). Clients re-simulate from a confirmed base on every state batch, replaying exact relayed inputs for opponents and their own input buffer for themselves; corrections snap.

Self-only prediction was rejected because opponent hit reactions would land one round-trip late — wrong for a platform fighter where being hit is a feel event. Delay-based buffering was rejected because it adds latency instead of removing it. The ≤10-entity cap keeps re-simulation cost trivial, and the existing `_serverTick` echo already anchors reconciliation in client-tick space (verified in `MatchInstance.cs`), so the server broadcast changes only by the relay.

Consequences: determinism becomes a hard contract — same Shared code, `MathF` only, no RNG, identical registration order, enforced by golden-tick tests with loss/gap/elimination cases; both sides ship together (no wire versioning); paused clients stop sending and the server stalls on all-empty queues, so the client's re-sim window must absorb gap ticks (cap 30 as a desync guard).

Status: accepted (grilling session 2026-08-02, implementation pending — see `docs/plans/2026-08-02-rollback-netcode.md`).
