# Liquidity Hunter Advanced — SPEC v0.1
## Independent project
A new autonomous decision architecture that improves first generation MALH; keeps first generation untouched. C# .NET 10 core with strictly optional broker T-Invest adapter. Default shadow-only. Based on secondary Info research.

## Core architecture
FeatureFrame (fresh L2, trade flow, realized vol, OFI, microprice, queue depth, session regime); Strategy ensemble (mean reversion, liquidity sweep, passive spread capture), RegimeGate, ExpectedValueScorer, OrderLevelOptimizer, CapitalAllocator, RiskGate, ExecutionActor per instrument, BrokerReconciler, ReplayEngine, EventStore and Monitoring.

## Decision formula
For every feasible price level and size:
score = P(fill) * [P(rebound|fill)*netGain - (1-P(rebound|fill))*netLoss - tailPenalty]
      - capitalLockCost - cancelRatePenalty - modelUncertaintyPenalty.
Trade only if conservative lower-bound score > 0, no stale feed, no correlated shock, enough opposite-side exit depth, and all portfolio limits respected.
P(fill), P(rebound) require out-of-sample calibration. Demonstration defaults not production signals.

## Specific improvements
- Enumerate entry ticks rather than hardcoding best bid.
- Treat fill probability, post-fill expected edge, uncertainty and risk as separate quantities.
- EWMA vol, trade-flow and OFI features, session-dependent regimes, shock embargo.
- Liquidity stability and displayed/realized volume disagreement detection.
- Correlation/stress-group exposure; cross-asset worst-case simultaneous fills.
- Scored candidate ranking and constrained capital assignment across many symbols.
- Adaptive exit price based on net fees, tick and available depth; hard timeout/stop overrides.
- Queue-uncertain replay with conservative/neutral/optimistic versions; no fair-value leakage.
- Deterministic decisions, typed configs, metrics, reason codes, versioned event journal.
- Deadline/market-closed/unknown order state halt and portfolio reconciliation.
- T-Invest rate-limit manager shared across robots on same account; separate close priority.

## Phases
A0 compile and smoke test deterministic math, feature validation, level search, portfolio allocation.
A1 data recorder and reproducible L2 replay.
A2 calibrated fill/rebound estimation with walk-forward tests.
A3 order lifecycle and true T-Invest SDK integration, shadow mode.
A4 historical stress and multiple-asset portfolio simulation.
A5 paper trading, confidence-band gating, kill switch, manual live release.

## Acceptance
No real orders by default. All calculations decimal-safe / overflow-tested. Account-aware budget and currency normalization. Historical snapshots may miss executions. Duplicate/partial fill reconciliation is mandatory. No automated claim of profitability.
