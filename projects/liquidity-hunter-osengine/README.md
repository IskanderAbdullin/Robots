# Liquidity Hunter — OS Engine
Independent OS Engine `BotPanel` robot (C#) for multi-symbol `BotTabScreener`. Uses `MarketDepth` and can be tested with historical order-book snapshots available through OsData and OsTester, **provided a data source actually supplies such history**.

## Install / first run
1. Use OS Engine version matching the API in `src/LiquidityHunterOs.cs`. The code has NOT been compiled against the user's local OS Engine version.
2. Add source to the OS Engine Custom Robots build/integration flow and compile against installed OsEngine libraries.
3. In OsData collect "Market depth" (not just candles) for selected instruments and date range; select that data in tester.
4. Create `LiquidityHunterOs`, configure Screener securities and Portfolio. Choose `Regime=Off` initially, inspect history and output before `On`.
5. Set limits, spread, fees and target. Note fee costs are **not automatically inferred** and simple signal is a baseline, not statistically calibrated.

## Safety caveats
This is an initial historical-testable implementation, not ready for live execution. Position creation/close mechanics and partial fills depend on OS Engine version and tester settings. Entry TTL now also requests cancellation of expired active opening orders (not yet fully broker-reconciled); exit placement is a simplistic limit/stop baseline and can remain stale. Active close orders need explicit cancellation/reconciliation before stop overrides. A global portfolio budget and session-regime risk engine are not implemented. Keep live mode Off until these blockers are resolved.


## Synthetic test evidence (2026-11-20)
Portable deterministic Python regression: [tests/synthetic/smoke.py](tests/synthetic/smoke.py).
Detailed three-seed local research and breakdown: [Synthetic results](docs/SYNTHETIC-RESULTS-2026-11-20.md).
The filter strategy remained unprofitable on synthetic scenarios; do not use backtest P&L as a profitability claim.
The current version blocks new entries in live OsTrader (historical testing only) and keeps managing confirmed positions when entry mode is Off.
The tester-side entry cancellation and stop/cancel races require real OS Engine integration verification.
