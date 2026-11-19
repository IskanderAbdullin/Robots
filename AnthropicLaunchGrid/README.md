# AnthropicLaunchGrid — ANTperp launch-order robot

Standalone .NET 10 CLI project inspired by the repository `IskanderAbdullin/Claude` (separate code; no shared dependencies). It watches the T-Invest REST API for `ANTperp`, checks that limit and API trading are permitted and that a live order book exists, then submits a **single** configured buy ladder.

> **WARNING** The product is an SPB Exchange perpetual futures neo-asset, not Nasdaq common stock. Orders can execute immediately when crossed; `$70–90` are price *points* whose contract valuation/margin and tick size must be independently verified in T-Invest before enabling live mode. 395 lots is NOT necessarily 395 US shares. Default live trading is DISABLED.

## Setup

1. Install .NET 10 SDK. Copy `config.example.json` to `config.json`.
2. Supply `TINVEST_TOKEN` in the OS environment (never commit it), and set `accountId` in the local config.
3. Build: `dotnet build AnthropicLaunchGrid.csproj -c Release`
4. Offline grid: `dotnet run -- plan config.json`. Self-tests: `dotnet run -- self-test`.
5. Connect, **without sending real orders**: `dotnet run -- watch config.json`. This waits for a valid instrument and market-data trigger and prints simulated placements.
6. After checking the actual contract details, permitted price ticks, account, margins, risks, and total size, set `liveEnabled:true`, then launch explicitly: `dotnet run -- live config.json --confirm-live`.

## Default ladder

26 BUY LIMIT orders at every whole price from 70 through 95 inclusive; 16 lots at 70–74, 15 lots at 75–95 (total: 395 lots). Override with the `levels` JSON array of objects `{"price":70,"lots":16}`, or change `minPrice`, `maxPrice`, `priceStep`, `totalLots`. If `levels` is non-empty, it takes precedence. Always review the printed order plan.

The order-entry rate gate allows no more than 13 starts in any sliding one-second period, and sequential HTTP requests may be slower. This is NOT a guarantee that 21 orders will reach the venue within two seconds: market-data polling, network RTT, broker processing and dynamic rate limits all matter. No retries after any ambiguous placement. A durable append-only JSONL journal records every live attempt (flushed to disk BEFORE HTTP call), its client idempotency UUID, broker acknowledgement or uncertainty. Relaunch skips previously attempted price levels. Journal files must not be deleted to restart trading without separately reconciling positions and active orders.

## Operational controls & limitations

- Modes: `plan` (offline), `watch` (real data, **no orders**), `live` (two-step enable).
- Only `ANTperp`; ticker is *not* the broker identity. It is resolved to `instrument_uid` by exact ticker comparison. If `instrumentUid` is set manually, operator is responsible for ensuring it matches ANTperp. Never hard-code an unknown UID.
- Starts on a trading-status and nonempty-book **polling** signal. Polling isn't as fast as the streaming market data feed. No proof of first exchange trade and no guarantee of being first in the book. A future integration with MarketDataStream and OrdersStream will lower latency.
- The code checks `apiTradeAvailableFlag` and `limitOrderAvailableFlag` and accepts the normal trading states. If the broker does not expose these flags for this instrument, it will wait rather than submit.
- `priceType` defaults to `PRICE_TYPE_POINT` for futures; this is a preliminary setting, NOT proof of valid quotation convention for ANTperp. Confirm with broker prior to live trading.
- `positionLimitLots` validates planned gross lots but DOES NOT query account positions or available margin and **is not a portfolio-level risk cap**. A live order may be rejected for insufficient margin or price corridors. The engine stops on first rejection or ambiguous result.
- The `allowCrossingSpread` switch is currently descriptive only; it **does not** impose a protection against marketable BUY limit orders. When price is at/below the BUY limit, execution may be immediate, possibly for all 395 lots. Live enabling requires this explicitly understood.
- Uses HTTP REST. No API credentials stored in source. No background task is created by merely pushing code to GitHub. Running it requires an active machine/terminal.
- Never cancels existing orders on Ctrl+C; Ctrl+C only stops the placement process.
- No genuine broker integration test, order submission test, latency benchmark, or production certification has been run.

Reference APIs: [FindInstrument](https://developer.tbank.ru/invest/api/instruments-service-find-instrument), [GetTradingStatus](https://developer.tbank.ru/invest/api/market-data-service-get-trading-status), [GetOrderBook](https://developer.tbank.ru/invest/api/market-data-service-get-order-book), [PostOrder](https://developer.tbank.ru/invest/api/orders-service-post-order).
