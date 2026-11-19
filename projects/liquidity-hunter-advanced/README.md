# Liquidity Hunter Advanced
Independent .NET 10 decision core: validated depth, regime and risk gates, price-level search, conservative EV, cross-symbol budget allocation.

Not yet a live broker robot. Probabilities and fill-depth decay currently supplied or heuristic, **not calibrated**. No T-Invest adapter or execution yet. Never trade live with this baseline.

Run: `dotnet run --project tests/LiquidityHunter.Advanced.Smoke/LiquidityHunter.Advanced.Smoke.csproj`
See [SPEC](docs/SPEC.md) and [Research](https://github.com/IskanderAbdullin/Info/blob/main/research/liquidity-hunter-second-wave-2026-11-20.md).