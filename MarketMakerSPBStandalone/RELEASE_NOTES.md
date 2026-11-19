# Standalone SPB Market Maker — Windows development preview

Build source: `IskanderAbdullin/Robots/MarketMakerSPBStandalone`. Architecture from `IskanderAbdullin/Claude`, quotation logic compared with `IskanderAbdullin/MarketMaker2_SPB`.

This is a **preview for offline/UI and read-only brokerage data experiments**, not production software. The broker order gateway exists but actual live trading is disabled by default behind multiple separate opt-ins. No real-money end-to-end tests have been completed.

## Included
- Windows WPF interface, SDK-dependent headless host, pure quote strategy for buy and sell, price/size modes, journal, risk gates.
- Paper broker simulation, fake order service, exchange order book and order status stream clients.
- Durable trade ID deduplication, informational marked result, exclusive single-state-instance lock.
- GitHub Actions Ubuntu and Windows builds and tests.

## Validation
See `BUILD_STATUS.md`. The automatic artifact is generated in a Windows CI job; release zip is downloadable from that GitHub Actions run's Artifacts section and expires after 7 days.

Do not embed credentials or tokens in the artifact. For live monitoring, create a local private `config.json` from `config.example.json`. Live activation is NOT recommended; it requires dedicated broker-side validation and explicit operator review.
