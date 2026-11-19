# Eventlab — portable OS Engine order-lifecycle test oracle

Experimental standalone Python event simulator. **No broker, no OS Engine binary, no live orders.** It models an account-level limit of risk capital and position slots across multiple symbols, partial fills, queue-ahead, cancellation delay, expiry, and mandatory stop/time/profit exits.

### Run
```bash
cd projects/liquidity-hunter-osengine/tests/eventlab
python -m unittest -v test_engine.py
```

`test_engine.py` contains 19 regression methods including 200 seeded randomized order sequences. No dependencies beyond Python 3.10+. For full executable portfolio stress and sample JSON outputs, see the separately produced standalone archive linked in the conversation, and the [research report](../../docs/EVENTLAB-ITERATION2-2026-11-20.md).

### Modeling limitations
- This is a reference *test oracle*, not the current C# BotPanel implementation.
- Prices are integer cents, sizes are single shares. No lot sizes or FX.
- Synthetic `sell_at_bid` is separately supplied aggressor volume; can't be inferred from historical L2 snapshots without trades.
- A crossed best ask triggers a hypothetical fill; true queue position, hidden liquidity and execution priority are unknown.
- Market exits are approximated at best bid regardless of actual fillable size.
- Deterministic latencies are artificial; partial fills can occur during pending cancel.
- No out-of-order broker reconciliation, exchange session modeling or permission to submit real orders.
- Compile and native OsTester integration must happen before using any version for live trading.


**Verification distinction:** the complete local downloadable eventlab contains 23 methods (all passed in Python 3.13.5). This GitHub port contains 19 methods and can be run via the added GitHub Actions workflow; a GitHub-hosted successful run is not yet verified. Its stand-alone stress generator is deliberately distinct from the full local research harness, so do not compare their numeric outputs as if from identical data.
