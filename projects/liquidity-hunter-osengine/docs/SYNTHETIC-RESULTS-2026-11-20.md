# Synthetic Test Stand — OS Engine Liquidity Hunter
Date: 2026-11-20. Research/testing stage only. **No real trades, no compiled OS Engine binary test.**

## What was actually executed
An independent Python research harness (local execution) generated deterministic L2-like quotes for 24 synthetic instruments in each of 12 regimes over 720 snapshots/instrument; compared two strategies per symbol/regime. 576 instrument/regime/policy combinations per seed. Executed three independent seeds: 20261120, 20261121, 20261201. Aggregate P&L is in **artificial quote-currency share units**, not RUB or portfolio-return percent.

| Seed | Baseline P&L | Filtered P&L | Baseline trades | Filtered trades | Reduction in simulated loss |
|---|---:|---:|---:|---:|---:|
| 20261120 | -1204.46 | -905.02 | 4061 | 3593 | 24.9% |
| 20261121 | -1158.18 | -886.04 | 4068 | 3618 | 23.5% |
| 20261201 | -1183.56 | -854.64 | 4085 | 3619 | 27.8% |

**Interpretation:** filters consistently reduce losses on these constructed scenarios, but do **not** produce profitable strategy. This is not evidence of live market performance. The constructed distribution, not markets, generated these results.

## Tested conditions
Range mean reversion, downtrend, uptrend, flash crash, disappearing liquidity, temporary false wall, wide/tight spread, volatility shock, trading halt, stale feed/reconnect, correlated multi-asset crash. The simulator separates signal decisions, pending passive orders (12 snapshot TTL), fill only on a following snapshot showing price trade-through, position management with profit/time/stop exits. Stop/time exits incur an extra slippage penalty; roundtrip commission charged; no look-ahead in entry signals. This is a **simplified** price-through queue proxy.

## Fee sensitivity (smaller separate sample, 8 symbols x 360 snapshots x 12 regimes)
| Per-side? | Baseline | Filtered |
|---|---:|---:|
| Estimated roundtrip fee = 0 bps | -133.46 | -63.35 |
| Estimated roundtrip fee = 8 bps | -178.15 | -103.09 |
| Estimated roundtrip fee = 16 bps | -222.85 | -142.83 |

The `fee_bps` parameter is roundtrip cost in this benchmark: cost = (entry+exit)*fee_bps/20000. Thus at 8 bps, approx. 8 bps of notional is charged across both legs. These quantities are not verified T-Invest actual fees.

## Six additional local invariants
- 12-case scenario generation count
- all synthetic L1 quote prices positive and uncrossed; displayed quantities positive
- greater fee cannot improve identical-path P&L
- position ages and fill timestamps nonnegative
- extreme fees prevent filtered entries
- fixed seed generates identical streams

All six Python unittest cases passed. The full local benchmark and result JSON files are separate from the compact repository smoke suite at `tests/synthetic/smoke.py`. The compact repository suite reproduces scenario types, not the exact aggregate table above. Its own execution in this session has **not** been verified; run it with Python 3.

## Findings in OS Engine implementation
1. Previously `ManagePosition` returned immediately when a profit close was active; its stop could never override that active order. **Fixed:** on breach, request cancellation, wait until `CloseActive` goes false, then market close. This still needs race/reconnect tests.
2. Previously `Regime=Off` stopped management of already open positions. **Fixed:** scan positions in Off; no new entries.
3. No explicit guard for fee-negative target. **Added:** skip entries when profit target <= configured expected roundtrip fees.
4. Jump detection to block sudden discontinuities. **Added:** reject first observation and jumps beyond configured maximum.
5. To prevent accidental live trading, new entries are blocked in `StartProgram.IsOsTrader`. Tester evaluation only.
6. Remaining critical gaps: pending entry cancellation after TTL, true global reservation across symbols, stale depth timestamp validation, unknown order state, per-symbol synchronized order actions, snapshot-driven deterministic tester processing, venue-specific lot-volume and currency semantics, online volatility/conditional rebound calibration. Do not use this prototype live.

## Next experiments
- Real OS Engine build and adapter integration with the exact user OS Engine revision (not possible in this session; no dotnet SDK or Windows/WPF OS Tester).
- Event-driven synthetic order fill model with configurable latency, queue-ahead, partial fills, cancellation acknowledgment delay and ambiguous broker state.
- Per-symbol allocation across 20–200 instruments with budget reservation; simulate shock-correlated fills and liquidation.
- Actual L2 and trade history from the same venue/session, walk-forward and instrument holdout, optimization only on training and evaluate frozen parameters out-of-sample.
- Estimate conditional return **after hypothetical fill**, not from randomly selected times; score net expectancy and fat-tailed risk.
- Never assume historical snapshots prove where a real queue order would execute.

## Reproduction
```bash
python3 projects/liquidity-hunter-osengine/tests/synthetic/smoke.py
```
This is a portable dependency-free smoke suite; the exhaustive locally executed harness may be downloaded separately as a reproducibility artifact. Do not confuse it with an OS Engine native tester.


## Additional abstract order-FSM regression (14 local invariants total)
Eight further local unit tests of a standalone abstract order-state model passed:
partial fill during pending cancel; complete fill before cancel acknowledgment; duplicated cumulative fill does not double count; cumulative fill rollback rejected; overfill rejected; submit-timeout reconciliation; reconciliation rollback rejected; second cancellation before acknowledgment rejected. See [order_fsm_tests.py](../tests/synthetic/order_fsm_tests.py).

**Boundary:** this proves properties of the small reference state machine, NOT actual OS Engine/Broker order-state reconciliation. A production adapter should reproduce these same cases in a contract/integration test.
OS robot entry TTL handling is now present (request cancel on expired active open order), commit fa986ca. This is not confirmed cancel-state reconciliation.
