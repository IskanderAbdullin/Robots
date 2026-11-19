# Liquidity Hunter OS Engine — SPEC v0.1
## Goal
Independent OS Engine BotPanel using BotTabScreener for selection of many instruments and historical MarketDepth replay in OsTester/OsData. Maintain separate source from broker-direct Liquidity Hunter; avoid editing installed OS Engine or original projects.

## Build/deploy
- Target OS Engine version checked by user: custom Bot compiled against OsEngine project/reference. Copy robot .cs to Custom/Robots *only after* building against the exact OS Engine version, or integrate source into local OS Engine solution. We do not promise single-file compile across upstream versions without verification.
- No passwords or direct broker tokens; OS Engine connector handles T-Invest access. Test on history before live.
- Disabled by default ("Off").

## Modules
1. Screener universe from configured tabs, not blind subscriptions; require valid security, lot, step, session, margin/permissions.
2. SnapshotFeatures from each tab.MarketDepth.GetCopy(); validate sorting, positive spread, volumes; stale snapshot guard from tab.TimeServerCurrent; OBI, spread bps, microprice, depth cliffs.
3. CandidateEngine: choose passive bid level by max adverse risk and required net edge; configurable depth tick offset, minimum spread, imbalance bound, min available ask depth for exit. Historical volatility and rebound model later.
4. PortfolioCoordinator: max positions across tabs and capital reservation incl pending open orders; limitation: OS Engine screener's portfolio and position state can lag; reconcile.
5. PositionSupervisor: one active entry per symbol, time to live, cancel pending states; never place another entry until position/order status clear; partial fill -> exit of confirmed OpenVolume, dynamic profit target and time/risk stop.
6. Tester/Optimizer: handle historical market depth stream and deterministically replay decisions using OS Engine tester; exact queue execution not inferred from snapshot; compare multiple fill rules.
7. Risk controls: symbol/session max budget, stale feed, max positions, daily loss/kill switch, trading periods, broker rejects, stop opening while keep closing.
8. Debug logs, JSON decision reports with features and reasons; no secrets.

## Test requirements
- Test on history with at least ten symbols and varied sessions where available, no look-ahead.
- Verify accuracy of OS Engine lot/contract volume for each market; unknown currency accounts prohibited pending FX normalization.
- Test partial fills, cancellation races, disable/re-enable, reconnect, bid collapse, empty book, market halt, spread compression, stale snapshots.
- Compare with baseline fixed bid entry under same fill assumptions and expenses.

## Stages
OS0 source skeleton, no live entry; OS1 passive multi-symbol entry and protective exit with guards; OS2 history replay/optimizer; OS3 global portfolio control and realistic execution; OS4 instrument-dependent fitting, paper, controlled live manual unlock.

## Safety
"Off" by default; On must be explicitly selected in terminal. Never guarantee profitable close. Stop loss/time risk overrides profit target. Do not infer execution from visible book alone. No self-crossing, spoofing or excessively frequent cancel/replace; economic intent on every quote.
