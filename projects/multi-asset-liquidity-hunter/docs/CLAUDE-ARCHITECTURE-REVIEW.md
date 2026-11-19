# Реализация на основе анализа Claude/Babid01
Дата: 2026-11-20. Прочитаны:
- IskanderAbdullin/Claude: src/Babid01/Trading/Engine.Orders.cs
- IskanderAbdullin/Claude: src/Babid01/Trading/Engine.Risk.cs
- Последний коммит 1a2006672b8c629eccff3134d3abb827ab437525

## Практики для переноса (с переписыванием под мультиинструментный движок)
1. `_synced`, `_auditPending`, сверка активных ордеров и позиций с брокером, особенно после reconnect.
2. Отдельные backoff для отказов на открытие и закрытие: закрытие имеет приоритет; 30042 должен замораживать новые входы и запускать повторную сверку buying power.
3. Общая квота операций по аккаунту для нескольких роботов; адаптивное замедление при ограничении.
4. Таймауты/повторы запросов состояния заявки, quarantine после недостоверных данных и неизвестных исходов.
5. Восстановление после отрицательной/неконсистентной учетной позиции, без самопроизвольной торговли до сверки.
6. Ежедневный сброс PnL/лимитов согласно торговому календарю, регулярный reconcile позиций.
7. Логирование latency, health, rejection bursts и order lifecycle.

## Отличия MALH
- На каждый инструмент отдельный actor/FSM, но общая account-level budget/risk/rate-limit.
- Протокол сообщений: MarketSnapshot, OrderStateEvent, FillEvent, ReconcileSnapshot, RiskEvent.
- Добавить global coordinator с atomically reserved capital, и распределитель subscribe slots до 300 суммарных на 1 поток (см. официальный T-Invest API).
- Признак `stop opening, keep managing exits` — отдельный от глобального отключения.
- Исключать сообщения собственных сделок из входного сигнала, где источник позволяет корректно определить.
- Синхронизировать отмены и сделки без ожидания FIFO; неизвестный статус => запрос состояния, а не повторная заявка.

## Проверенные ограничения на момент исследования
- https://tinkoff.github.io/investAPI/limits/ — до 300 суммарных подписок на стаканы, сделки и свечи на одно MarketData stream соединение; число соединений зависит от грейда.
- https://tinkoff.github.io/investAPI/marketdata/ — в API quantity стакана в лотах; необходимо отдельно привести к денежной стоимости через размер лота и валюту.
- https://tinkoff.github.io/investAPI/orders/ — PostOrder, CancelOrder, GetOrderState, GetOrders, ReplaceOrder, TradesStream.
- https://tinkoff.github.io/investAPI/sandbox/ — Sandbox не является полным воспроизведением реальной очереди исполнения.

## Вывод
Реализацию брокерского адаптера вести после проверки актуальных contract types/SDK; не заменять настоящую T-Invest интеграцию заглушкой с ложным статусом готовности.
