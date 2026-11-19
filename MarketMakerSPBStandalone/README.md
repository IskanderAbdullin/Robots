# MarketMakerSPBStandalone

Independent .NET 10 C# Market Maker SPB. Original parity target: `IskanderAbdullin/MarketMaker2_SPB`, branch `codex/market-maker-spb`. Architectural comparison: `IskanderAbdullin/Claude`.

**Current stage: M1 pure quotation planning. Not a broker-connected robot and NOT safe for live trading.** Do not pass a brokerage token; this stage has no network trading code.

```bash
dotnet run --project MarketMakerSPBStandalone/tests/Stand/Stand.csproj
```

Read [SPEC.md](SPEC.md) for full requirements and remaining acceptance gates. This project intentionally contains no automatic GitHub Actions builds or artifacts.
