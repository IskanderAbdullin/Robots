# MarketMakerSPBStandalone — Technical Specification (v0.1, 2026-11-20)

## 1. Purpose and source of truth
Build an independent .NET 10 C# market-maker in `IskanderAbdullin/Robots/MarketMakerSPBStandalone`, without OsEngine dependency. Preserve the **combined** strategy from `IskanderAbdullin/MarketMaker2_SPB` (branch `codex/market-maker-spb`, file `MarketMakerSPB.cs`), including its buyer and seller behavior, not the earlier buyer-only `MarketMakerSPB` repository. Use `IskanderAbdullin/Claude` as an architectural reference (TInvest broker, host, risk, journal, GUI and stand), not as a copied/automatically trusted implementation.

## 2. Functional parity (mandatory)
- Single account/instrument identity (instrument UID, ticker, class code), one common on/off setting, independent buy/sell parameters, default disabled.
- Buy: first qualifying bid with **foreign** volume threshold, quoting one tick above; or multi-level bids with a configured percentage of foreign volume. Own orders must be subtracted from displayed depth, never allowed to make themselves the target.
- Sell: first qualifying ask with foreign volume threshold, quoting one tick below; or multi-level asks with a configured percentage of foreign volume.
- Both sides: fixed total **remaining live order lots** mode, or buy maximum affordable lots (margin-aware, from broker-confirmed limits) / sell maximum freely available inventory (no short sales).
- At unchanged price, add only uncovered volume without cancelling existing queue priority. When plan prices change, cancel affected **side only**, wait for terminal acknowledgements, fresh portfolio/limit data and book before replacing.
- No new orders after stale depth, absent trading permission, unresolved order state, account/instrument change or unavailable authoritative balances. Track per-side reservations, partial fills, external active orders, order-state versions.
- Self-cross prevention includes pending, unknown and cancellation-in-flight orders. Common off cancels both sides but does not liquidate holdings. Faults fail closed, not fail open.
- Configurable timer / event wake-up, deterministic state engine, persistent journal, startup resynchronization and recovery after missed stream events.

## 3. Software components
`Domain`: decimals, lot and tick rounding, book/quote levels, order identity/state, fills, balances; no external dependencies.
`Planning`: two-side target builder and foreign-liquidity calculation; **pure**, offline-testable.
`Engine`: single-writer actor, desired-vs-actual reconciliation, order FSM, cancel-before-create, unknown-order blocking, connection/auth/market-data safety gates, position/cash reservations.
`Broker`: `IBroker`, T-Invest gRPC+streams, explicit timeouts and throttling, dynamic quota headers, idempotent request IDs, broker-side reconciliation.
`Persistence`: durable write-ahead intents + atomic snapshots; restart repair.
`Risk`: max notional/position/loss, stale data, spread/slippage controls, order-per-minute cap, panic stop, trading windows; all restrictive.
`UI`: CLI first; separate optional Windows WPF GUI after safe core validation. Profiles and secrets must never be committed.
`Tests`: standalone simulation, deterministic seeded scenarios and repeated crash/reconnect/partial-fill/order-race tests.

## 4. Order handling and invariants
Each order has immutable client idempotency key; states NEW_INTENT -> PENDING -> WORKING/PARTIAL -> CANCEL_PENDING -> terminal, with UNKNOWN as safety-blocking. Durable intent precedes placement. Do not infer cancellation from REST timeout. Query broker and list active orders on reconnect. Only broker-confirmed terminal states release outstanding commitments. Derive filled amount from max(status cumulative, deduplicated trades). Never double-book a fill.
1. No crossing own live bid/ask (including pending and unknown).
2. No replacement placement before terminal cancel acknowledgement.
3. No buy without confirmed remaining buying-power quota and reserved pending buys.
4. No sell without confirmed free holdings net of foreign and own reservations.
5. No new orders in OFF or fail-closed mode; OFF cancellation remains possible.
6. An unavailable data source cannot be treated as a zero commitment.

## 5. Broker adapter and constraints
Use the *current* T-Invest Invest API protocol definitions from the pinned version of the existing Claude project, review the generated contracts and API docs before implementing. OrdersService: PostOrder, CancelOrder, GetOrderState, GetOrders, GetMaxLots; OrdersStreamService order/trade streams; MarketDataStreamService order books and trading status; positions/portfolio streams or polling with freshness barrier. Respect rate limits and tariff counters including cancellations and unsuccessful requests; do not assume per-account isolated quota. Bounded timeouts, serialized reconciliation, reconnect backoff and health telemetry. Sandbox/mock must be default; live requires separate explicit opt-in and approved account.

## 6. Risk and production safety
Defaults: live disabled, fail closed, order size 0 until configured, no margin by default, max positions and notional caps, no reverse-side self-cross, explicit instrument status, no trading outside allowed windows. Enforce broker minimum price increment/upper-lower limits. Detect toxic fills / markouts / spread collapses and reduce quotation size or stop, behind opt-in flags. For SPB pro-rata, optimize **displayed size and queue price without futile cancel/replace loops**; do not incorrectly assume FIFO.
Never delete, interfere with, or relabel orders of other robots or human accounts.

## 7. Delivery milestones
- M0 Research and architecture/TZ committed; baseline test inventory and build scripts.
- M1 Pure planner + config validation + deterministic stand (both sides, foreign depth, price tick, percentage ladder).
- M2 Isolated asynchronous FSM and paper broker, incremental orders, partial fills, cancel acknowledgements, self-cross and source freshness checks.
- M3 T-Invest adapter and contract verification; no real trades; replay of broker events and reconnect.
- M4 Journal, restart reconciliation, risk limits, P&L, kill switch.
- M5 Configuration UX, operational logs, metrics, Windows GUI, release packaging.
- M6 Full parity checklist, property/fuzz tests, failure injection and supervised paper-mode burn-in; only then consider live-mode opt-in.

## 8. Test gates and GitHub resources
No workflow auto-trigger for every commit; prefer local `dotnet build` and `dotnet run --project tests/Stand`. If CI is later needed, manual `workflow_dispatch`, short timeout, no artifacts/caches for routine builds. No force push. Commit after coherent stages with SHA. No secrets in config or logs. Never claim live trading validated by simulation.

## 9. Optional independent improvements (not required for parity, initially disabled)
Inventory skew, dynamic risk-aware spreads, order-flow imbalance, post-fill markout/adverse-selection guard, volatility halt, queue/pro-rata-aware hysteresis and replacement budget, shadow quotes, replay/backtesting and P&L attribution. Evaluate them against baseline using identical recorded events rather than assuming higher theoretical returns.
References:
- https://tinkoff.github.io/investAPI/orders/
- https://tinkoff.github.io/investAPI/faq_orders/
- https://tinkoff.github.io/investAPI/deadlines/
- https://github.com/kayasolomon/market-making
- https://github.com/abh-win/optimal-market-making

## 10. Acceptance criteria
Full buyer/seller parity is established only after side-by-side oracle test with original OsEngine logic across matching snapshot/fill/order scenarios. Measured latency and profitable behavior are not asserted without replay or live evidence. Broker implementation remains **not production ready** until post-order reconciliation, API quotas, durable idempotency and failure tests pass.
