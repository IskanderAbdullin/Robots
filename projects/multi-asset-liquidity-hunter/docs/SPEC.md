# Multi-Asset Liquidity Hunter — техническое задание v0.1
## Статус
Исследование: ../Info (отдельный репозиторий). Реализация итерационная. По умолчанию LIVE_TRADING=false. Это экспериментальный проект, НЕ готовая прибыльная стратегия.

## Продукт
Самостоятельный сервис .NET 10 C#, T-Invest API gRPC, Windows/Linux headless, без обязательного GUI. Раздельные Core, TInvestAdapter, Strategy, Execution, Risk, Storage, Backtest, App, Tests. Никаких секретов в Git. Проверить и адаптировать работающий образец Claude, не копировать без аудита.

## Модули
1. UniverseDiscovery: получение списка бумаг, реальных статусов торговли, разрешения на лонг/шорт, лотности, шага цены, валюты; фильтр по обороту/волатильности/спреду и доступным данным; регулярное обновление.
2. MarketData: multiplex streams стаканов и обезличенных сделок, лимит подписок, resubscribe с backoff и jitter, снапшот/sequence validation, UTC timestamps; TTL 2 секунды по умолчанию (конфиг); WAL raw events с ротацией.
3. FeatureEngine: spread/OBI/depth/microprice, OFI по обновлениям, trade imbalance, realized volatility, depth stability, jump score, historical rebound conditional-on-fill, queue proxy.
4. OpportunityScorer: candidate (instrument, side, limitPrice, qty, expectedNetPnl, fillProbability, drawdownRisk, exitLiquidity), конфигурируемая scoring model; фильтр min expected net profit и minimum sample size; hysteresis для предотвращения излишних отмен.
5. PortfolioAllocator: общий budget, open-orders reserved exposure + confirmed positions, per-symbol cap, sector/currency/correlation cap, reserve for fees, max simultaneous positions.
6. OrderStateMachine: Idle -> Placing -> Active -> CancelPending -> Reconcile -> Filled/Closed; idempotency per order, reconcile после сбоев; без следующей заявки пока неясен статус старой.
7. ExitManager: после подтверждённого fill выставляет reduce-only в логике нашего учета (не биржевая гарантия), динамический уровень выхода с учетом qty, depth, net fee, минимальной прибыли; если цель неосуществима — risk/time stop; недопустимо гарантировать выход «всегда с плюсом».
8. TInvestAdapter: actual service API methods, OrdersStream, MarketDataStream, Accounts/Portfolio, paginated history; retry только безопасных операций; rate limiter по методам и аккаунту, уважать retry-after. Поддержка Sandbox по возможности; не считать Sandbox идентичным биржевому исполнению.
9. RiskManager: daily loss, portfolio DD, max inventory exposure, max aged inventory, stale quotes, correlated shock, order-error burst / account-level throttling, emergency shutdown.
10. Telemetry: JSONL event log; schema version, strategy version, order lifecycle, portfolio snapshot, feature snapshot, decision reasons, PnL mark-to-market, api counters, journal summary для OsEngine Lab.
11. Backtest/replay: deterministic event replay, exchange fee, exchange-calendar, queue position uncertainty, latency, partial fills; walk-forward tuning.
12. Config: конфиг в JSON и secrets из environment variables/secret store, validate at startup, no tokens in logs.

## Алгоритм
Каждые N мс на свежих данных оцениваем каждый candidate. Для покупки: уровень в пределах допустимого отклонения от mid/локального reference, наличие достаточной встречной ликвидности при выходе, историческая conditional rebound, положительный EV after fees и risk penalty. Не гонимся за максимальной вероятностью касания, избегаем «ловли падающего ножа». Продажа/короткая позиция зеркально только при доступном шорте. После fill инициируется выход с лимитной прибылью и отдельной аварийной логикой.
Скорость и число репрайсов ограничивать; часто менять заявки только после material signal change, с minimum quote lifetime и cooldown (но рисковые отмены имеют приоритет). Для T-Invest ReplaceOrder отдельно исследовать семантику статуса и квоты до активации.

## Этапы и критерии готовности
P0: репозиторий, ТЗ, контракты, конфиг, типы, чистый core и unit tests.
P1: universe + streaming data, reconnect, data journal, shadow opportunity log; тесты offline fixtures.
P2: account/order reconciliation, actual orders interface + sandbox; FSM tests: duplicate/partial fill/cancel race.
P3: risk engine, capital allocation, exit engine, no unhedged/oversold exit; concurrency tests.
P4: backtest with conservative queue model, costs, walk-forward, stress testing.
P5: paper soak test >=10 trading sessions, monitor error budget and anti-spoof compliance.
P6: live readiness checklist, manual approval, strict capital limit, kill switch and rollback.
Для каждого этапа: тесты, документация, commit/push; не создавать тяжелые GitHub Actions artifacts, запускать targeted tests и хранить только нужные отчеты.

## Safety invariants
- Никаких real orders до explicit LIVE_TRADING=true и отдельной user authorization.
- Sum(worst-case filled entry commitments + open inventory commitments) <= budget (в одной валюте после консервативной FX-конвертации).
- Sell quantity <= confirmed free position; partial fill cannot double-sell.
- Unknown status => quarantine instrument; reconnect => reconcile before orders.
- Outdated market data => do not quote; critical risk => cancel entries, manage exits.
- Server time and exchange trading schedule checked; no trading on pauses, suspensions or closed sessions.
- Idempotency key per new order; no duplicate order after timeout without broker reconciliation.
- Exit positive LIMIT target is a preference, not a profit guarantee; risk stop can realize loss.
- Every decision should be explainable and reproducible with config+feature+data versions.

## Default illustrative parameters (not tuned)
budget=0 for live; paperBudgetRub=100000; perSymbolLimit=0.03 total; maxSymbolsWithOrders=20; maxConcurrentPositions=10; maxDailyLoss=0.01 portfolio; maxDrawdown=0.03; minExpectedNetEdgeBp=20; maxDataAgeMs=2000; minQuoteLifetimeMs=5000; repriceCooldownMs=15000. All require empirical calibration.

## Open questions
Permissions and availability of shorts, account type, NEO market sessions, existing Cloud/Claude reference code patterns, updated T-Invest SDK contracts and quotas, usable full-history L2 feed. Resolve via inspection, never assume.
