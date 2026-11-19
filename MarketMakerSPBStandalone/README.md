# MarketMaker SPB — самостоятельный робот (.NET 10)

Independent C# market maker based on the combined [OsEngine MarketMaker2_SPB](https://github.com/IskanderAbdullin/MarketMaker2_SPB/tree/codex/market-maker-spb) and the architectural reference [Claude](https://github.com/IskanderAbdullin/Claude). Both buy and sell sides are implemented with their own settings. This is an independent implementation, NOT an OsEngine plugin.

## Current validation and limitations

CI compiles the .NET core, T-Invest SDK adapter, console hosts, Windows WPF interface and runs deterministic planner, reconciliation, mock-broker and paper broker tests, including 20,000 seeded book scenarios. The Windows interface is **compile-tested** but has not yet been interactively tested on a user's Windows PC.

No real-money brokerage validation has been performed. This is an **experimental build**. Live trading is OFF by default; it must not be enabled until full end-to-end verification, API quota/header audit, external-order conflict reconciliation, production credential/security review and supervised paper burn-in. Successful builds do NOT prove parity or profitability.

## Projects

| Folder | Purpose |
|---|---|
| `Core/` | Pure quote planner, order FSM and trade ledger |
| `Runtime/` | Shared strategy evaluator, journal, paper broker, fail-closed limits |
| `tests/Stand/` | Offline deterministic and seeded fuzz tests |
| `App/` | Local paper-only executable demo |
| `TInvest/` | T-Invest C# SDK transport, order streams, audit/session, conservative rate gate |
| `TInvest/Stand/` | Fake SDK gateway tests, no brokerage token required |
| `TInvest/Host/` | Headless broker-data host, read-only by default |
| `Gui/` | Windows WPF console, read-only by default |

## Testing

Requirements: .NET SDK 10. To build/run without any brokerage access:

```powershell
dotnet build MarketMakerSPBStandalone/MarketMakerSPBStandalone.csproj -c Release
dotnet run --project MarketMakerSPBStandalone/tests/Stand/Stand.csproj -c Release
dotnet run --project MarketMakerSPBStandalone/TInvest/Stand/Stand.csproj -c Release
dotnet run --project MarketMakerSPBStandalone/App/App.csproj -c Release -- --demo
dotnet build MarketMakerSPBStandalone/TInvest/Host/Host.csproj -c Release
dotnet build MarketMakerSPBStandalone/Gui/Gui.csproj -c Release
```

A GitHub Action [MarketMaker standalone checks](../../actions/workflows/marketmaker-standalone.yml) compiles both Linux and Windows variants and tests the paper and fake-broker modes. A GUI release is created only if the commit message contains `[gui-release]`. GUI release artifacts expire in 7 days.

## T-Invest read-only host

1. Copy `config.example.json` to a private `config.json`, never commit this personal file or API token.
2. Set the exact brokerage account ID, instrument UID, price step and actual lot size.
3. Supply T-Invest token through the environment variable configured by `tokenEnvironment`; do not paste it into JSON or chat.
4. Start the host without `--live`:

```powershell
dotnet run --project MarketMakerSPBStandalone/TInvest/Host/Host.csproj -- --config config.json
```

This **subscribes to T-Invest** data and reads account/order state but never sends order placements or cancellations when the live switch is absent. When watching sensitive account output, keep local logs private.

### Real-money safety

Live requires all of the following simultaneously: `--live` CLI switch (or explicit GUI confirmation), `enabled=true`, `allowLiveWrites=true`, `liveAcknowledgement="I_UNDERSTAND_REAL_MONEY_ORDERS"`, and `MARKETMAKER_LIVE_ACK` with that exact same value. Default risk caps are **zero**, so both buy and sell are blocked until configured. Margin buys are also disabled by default, and selling is bounded by confirmed free inventory. These controls exist for safety only; their presence does not make the build production-ready. **Do not enable live on your actual account yet.**

The host tries to cancel its own known live orders during graceful shutdown if still connected, without liquidating positions. A crash or loss of network may leave orders on exchange; always verify terminal statuses directly with the broker.

## Functional semantics

- Two independent sides: quote one tick inside the best qualifying foreign bid/ask, or place a percentage ladder of available foreign book volume.
- Existing orders count toward total target lots, so when the brokerage only reports buying power available for **new** orders the robot does not needlessly cancel its own existing reservations.
- Same-price incremental additions preserve existing order priority; repricing is cancel-before-create, with broker terminal confirmation.
- Pending, ambiguous or cancel-pending orders block replacement; failed order responses stop new orders.
- Checked instrument UID/account, market trading status, fresh consistent order book, capacity/positions, stream silence watchdog, periodic broker reconciliation and owned-order self-cross guard.
- Executions are journaled by trade ID; marked result is **incremental and informational**, not a verified after-fee tax or account-level result. Actual commissions are not yet reconciled.
- Risk caps include lot size and point value; seller never requests a margin short.

## Unfinished acceptance gates

- True live-market paper burn-in against real NEO order book and positions, without order submission.
- Replay/side-by-side oracle parity against original OsEngine buy and sell on identical broker/account snapshots, including broker reserved funds across multiple concurrent bots.
- Confirm T-Invest account/global rate-limit headers, independent gate allocations, error quotas, resubscribe rules and all edge cases for unknown/partially cancelled orders.
- Commissions, post-fill P&L reconciliation, exchange time/market-schedule user controls, live failure injection and GUI usability validation.
- Operational audit (disk-full, concurrent instances, corrupted recovery log) and explicit human confirmation for actual trading.

See [SPEC.md](SPEC.md) for complete milestone definitions and risk requirements. CI correctness is necessary but not sufficient for actual trading.
