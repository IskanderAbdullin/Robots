# Event-driven synthetic tester, iteration 2 — 2026-11-20

## Scope
A locally executed **standalone Python test oracle**, developed to stress the OS Engine Liquidity Hunter strategy before deploying to real OS Engine. It does not run the OS Engine binary and never submits broker orders. The full source bundle and raw JSON outputs were generated as conversation artifact `liquidity_hunter_eventlab_v2.zip` (see user-facing attachment).

## Actual tests executed
- Python 3.13.5, 22 unittest methods **passed**; one method repeats 200 randomized order sequences (12,000 simulated clock ticks in that method).
- 12 synthetic market regimes, 12 symbols/regime, 240 frames/symbol; two strategy variants and three commission levels per seed. Thus 34,560 frames per strategy/fee configuration, six configurations per seed.
- Two independent seeds, 20261120 and 20261121.
- Sample test logs: no same-tick fill, queue-ahead consumption, partial fills, fill while cancel pending, no fills after cancel acknowledgment, reservation survives partial fill, stop can override profit intent, halt/stale cancels entries, expired order stays pending until cancel ack, no duplicate instrument entries, monotonic timestamps, no negative inventory, full portfolio budget and slots reservation, seeded deterministic property checks.

## Monetary convention
P&L shown in artificial integer **cents** from a single synthetic currency with one-share lot sizes. This is not RUB, not actual capital return, and not T-Invest fees. Commission `fee_bps` denotes total roundtrip cost (approximately), not a per-leg fee. The price dynamics and displayed depth are simulated; no real exchange data were used.

| Seed | Roundtrip bps | Baseline P&L cents | Filtered P&L cents | Baseline filled shares | Filtered filled shares |
|---|---:|---:|---:|---:|---:|
| 20261120 | 0 | -2883.00 | -1750.00 | 343 | 315 |
| 20261120 | 8 | -5244.39 | -3923.42 | 343 | 315 |
| 20261120 | 16 | -7605.79 | -6096.84 | 343 | 315 |
| 20261121 | 0 | -4532.00 | -2909.00 | 300 | 255 |
| 20261121 | 8 | -6595.89 | -4669.84 | 300 | 255 |
| 20261121 | 16 | -8659.78 | -6430.67 | 300 | 255 |

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
