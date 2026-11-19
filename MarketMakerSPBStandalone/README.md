# MarketMakerSPBStandalone

Independent .NET 10 C# Market Maker SPB. Parity source: [MarketMaker2_SPB](https://github.com/IskanderAbdullin/MarketMaker2_SPB) branch `codex/market-maker-spb`; standalone reference [Claude](https://github.com/IskanderAbdullin/Claude).

**Status: M1 planning and partial M2 order FSM implemented; unverified by a compiler in this environment. NO T-Invest connection, durable journal, risk integration, Windows GUI or live trading yet. Do not run on a real account.**

Run an offline stand with .NET 10 SDK:

```bash
dotnet run --project MarketMakerSPBStandalone/tests/Stand/Stand.csproj
```

Included core:
- `Core/QuotePlanner.cs` — foreign book volumes, target-level or percentage-ladder buy/sell quotes, price bounds, buy-priority crossing filter, fixed/broker-confirmed availability cap.
- `Core/OrderReconciler.cs` — single-writer order commands, pending and cancellation barrier, incremental increase at the same level, report idempotence and crossing guard.
- `tests/Stand/Program.cs` — deterministic offline scenarios (not yet executed).

**Safety boundary:** The reconciler returns *proposed* commands only; it neither transmits them nor journals intents. Calling code must persist client IDs before sending, use broker-confirmed account/portfolio/order snapshots, hold pending reservations, and reconcile unknown states after timeouts before live trading is possible. Current planner and reconciler are development components, not a complete order management system.

See [SPEC.md](SPEC.md) for the comprehensive parity checklist and gated roadmap. No automatic GitHub Actions added, so routine commits do not consume CI minutes or store artifacts.
