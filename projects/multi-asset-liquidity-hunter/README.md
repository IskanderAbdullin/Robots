# Multi-Asset Liquidity Hunter
Experimental, multi-symbol limit-order liquidity capture bot for T-Invest; independent of OS Engine.
Status: **P0 core prototype**; NO T-Invest broker adapter and NO live order submission yet.

See [SPEC](docs/SPEC.md), and research in [Info](https://github.com/IskanderAbdullin/Info/blob/main/research/multi-asset-liquidity-hunter-2026-11-20.md).

## Run core checks
```bash
dotnet run --project tests/LiquidityHunter.Smoke/LiquidityHunter.Smoke.csproj
```
The smoke program is self-contained and uses no external testing packages or GitHub Actions minutes.
Prices are in instrument currency; portfolio accounting must convert and haircut FX before production.
The demo candidate's rebound/fill probabilities are **hypothetical** and MUST NOT drive real orders.
No credentials or live trading in this commit.
