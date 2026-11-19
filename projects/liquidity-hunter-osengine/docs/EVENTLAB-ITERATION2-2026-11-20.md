# Event-driven synthetic tester, iteration 2 — 2026-11-20

## Scope
A locally executed **standalone Python test oracle**, developed to stress the OS Engine Liquidity Hunter strategy before deploying to real OS Engine. It does not run the OS Engine binary and never submits broker orders. The full source bundle and raw JSON outputs were generated as conversation artifact `liquidity_hunter_eventlab_v2.zip` (see user-facing attachment).

## Actual tests executed
- Python 3.13.5, 23 unittest methods **passed**; one method repeats 200 randomized order sequences (12,000 simulated clock ticks in that method).
- 12 synthetic market regimes, 12 or 24 symbols/regime, 240 or 360 frames/symbol; two strategy variants and three commission levels per seed. Thus 34,560 or 103,680 quote events per strategy/fee configuration, six configurations per seed.
- Three independent seeds, 20261120, 20261121 and 20261201.
- Sample test logs: no same-tick fill, queue-ahead consumption, partial fills, fill while cancel pending, no fills after cancel acknowledgment, reservation survives partial fill, stop can override profit intent, halt/stale cancels entries, expired order stays pending until cancel ack, no duplicate instrument entries, monotonic timestamps, no negative inventory, full portfolio budget and slots reservation, seeded deterministic property checks.

## Monetary convention
P&L shown in artificial integer **cents** from a single synthetic currency with one-share lot sizes. This is not RUB, not actual capital return, and not T-Invest fees. Commission `fee_bps` denotes total roundtrip cost (approximately), not a per-leg fee. The price dynamics and displayed depth are simulated; no real exchange data were used.

| Seed | Instruments × steps | Roundtrip bps | Baseline P&L cents | Filtered P&L cents | Baseline fills | Filtered fills |
|---|---|---:|---:|---:|---:|---:|
| 20261120 | 12 × 240 | 0 | -66187.00 | -52030.00 | 1672 | 1463 |
| 20261120 | 12 × 240 | 8 | -77464.30 | -61908.47 | 1672 | 1463 |
| 20261120 | 12 × 240 | 16 | -88741.59 | -71786.94 | 1672 | 1463 |
| 20261121 | 12 × 240 | 0 | -69958.00 | -57493.00 | 1662 | 1597 |
| 20261121 | 12 × 240 | 8 | -81132.72 | -68330.82 | 1662 | 1597 |
| 20261121 | 12 × 240 | 16 | -92307.43 | -79168.64 | 1662 | 1597 |
| 20261201 | 24 × 360 | 0 | -163810.00 | -99803.00 | 3169 | 2515 |
| 20261201 | 24 × 360 | 8 | -186987.27 | -118174.40 | 3169 | 2515 |
| 20261201 | 24 × 360 | 16 | -210164.55 | -136545.80 | 3169 | 2515 |

Filtered policy reduces losses but is not profitable. The additional 200 randomized order sequences test invariants, **not statistical alpha**.

## Defects uncovered and corrected in event oracle
1. Initial full-history order scan made portfolio stress impractically slow; changed to an active-order index.
2. Wrong initial scale of per-symbol budget versus cent-denominated quotes yielded zero trades; corrected to a 900000-cent total budget and 120000-cent per-symbol cap, with hard assertions.
3. Max concurrent positions was originally enforced only on *filled* positions; multiple unfilled orders could later exceed it. Fixed by reserving portfolio slots for pending entries; added two regression cases.
4. Small random-test typo (`halt` instead of `halted`) repaired, then 22/22 tests passed.

## Simulator architecture
- `BookEvent` (integer tick, bid/ask, displayed quantity, explicitly synthetic aggressive sell volume, halted/stale flags).
- `Order` states: submitting → active → cancel-pending → done, with fill during pending cancel permitted.
- `Position` maintains confirmed quantity and cost, exit blocked while an entry cancel is unresolved.
- `Engine` keeps account-level committed exposure = all open order residual notional + filled inventory cost. Single pending entry per symbol, reserve slot count, stop/time/take exit.
- `stress.py` replays every symbol in deterministic clock order and compares different commissions and two filters.
- `test_engine.py` and `test_randomized.py` validate invariants.

## Important caveats
- The `sell_at_bid` field is an explicit synthetic aggressor-flow proxy, NOT inferred from actual market depth snapshots.
- If ask crosses below our bid, the model estimates a fill; without market-by-order queue this is still not proof.
- The simulator liquidates at the last synthetic bid without modeling true available exit quantity.
- No short selling, order replacement, FX conversion, financing, holidays, true trading sessions, stochastic latency calibration or OS Engine event callback integration.
- This reference engine is separate from OS Engine robot code. Passing its tests is **not** equivalent to proving that the OS Engine program complies with invariants.
- Current OS Engine project remains disabled for real order submissions until it is compiled, run in native tester, and reconciled against broker state.

## Native OS Engine path
OS Engine's official documentation confirms that OsData can collect order book snapshots via supported connected providers and replay them as `MarketDepthAllCandleState` or `MarketDepthOnlyReadyCandle`, subject to connector data availability:
https://www.os-engine-eng.com/posts/we-download-order-book-snapshots-and-run-tests.html

The next integration deliverable is a typed adapter receiving OS Engine market-depth and trade events, mapping to the same event oracle for paper/replay parity. Tests must include cancellations, partial fills, callbacks that arrive out-of-order, fresh order/position sync after reconnect and switching between test/real executions.

## Reproduce locally
```bash
cd eventlab
python -m unittest discover -p 'test_*.py' -v
python stress.py --symbols 12 --ticks 240 --out stress_results.json
python stress.py --seed 20261121 --symbols 12 --ticks 240 --out stress_results_seed2.json
```
The full eventlab sources and result JSON are available in the downloadable archive from this conversation. GitHub repo may contain a smaller smoke suite from the prior iteration; avoid confusing it with this full standalone benchmark.


## Critical correction: shared event clock
Previous portfolio stress results used `at=t*symbols+i`, accidentally mixing market time with the number of subscribed instruments. This changed fill frequency mechanically when universe size increased. **All earlier eventlab P&L figures are superseded.** We now use `at=t` for every symbol in each market step, and added an invariance test: unrelated symbols cannot change an order's simulated latency. After correction, 23/23 local methods passed. See corrected results above. The separate repository stress runner's clock was fixed in commit 67d5db8.
