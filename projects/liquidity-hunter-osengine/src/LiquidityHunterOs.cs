// Place in OS Engine Custom/Robots after validating the OS Engine version.
// Requires OsEngine assemblies and a configured screener with MarketDepth data.
// TEST FIRST in OsTester. Regime defaults to Off. Educational strategy, not verified live.
using System;
using System.Collections.Generic;
using System.Threading;
using OsEngine.Entity;
using OsEngine.Market;
using OsEngine.OsTrader.Panels;
using OsEngine.OsTrader.Panels.Attributes;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Logging;

namespace OsEngine.Robots
{
    [Bot("LiquidityHunterOs")]
    public sealed class LiquidityHunterOs : BotPanel
    {
        private readonly BotTabScreener _screener;
        private readonly StrategyParameterString _regime;
        private readonly StrategyParameterInt _maxPositions;
        private readonly StrategyParameterInt _pollMs;
        private readonly StrategyParameterDecimal _minSpreadBps;
        private readonly StrategyParameterDecimal _maxAskImbalance;
        private readonly StrategyParameterDecimal _budgetPerInstrument;
        private readonly StrategyParameterDecimal _profitBps;
        private readonly StrategyParameterDecimal _roundTripCostBps;
        private readonly StrategyParameterDecimal _maxJumpBps;
        private readonly StrategyParameterDecimal _stopBps;
        private readonly StrategyParameterInt _entryTicksBelowBid;
        private readonly StrategyParameterInt _maxEntryAgeSec;
        private readonly Thread _worker;
        private volatile bool _disposed;
        private readonly Dictionary<string, DateTime> _lastEntryAttempt = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, decimal> _previousMid = new Dictionary<string, decimal>();
        private readonly object _lock = new object();

        public LiquidityHunterOs(string name, StartProgram startProgram) : base(name, startProgram)
        {
            TabCreate(BotTabType.Screener);
            _screener = TabsScreener[0];
            _regime = CreateParameter("Regime", "Off",
                new[] { "Off", "On", "OnlyClosePosition" }, "Base");
            _maxPositions = CreateParameter("Max positions", 5, 0, 100, 1, "Risk");
            _pollMs = CreateParameter("Scan interval ms", 500, 100, 5000, 100, "Base");
            _minSpreadBps = CreateParameter("Min spread bps", 15m, 0m, 1000m, 1m, "Signal");
            _maxAskImbalance = CreateParameter("Max imbalance", -0.10m, -1m, 1m, 0.05m, "Signal");
            _budgetPerInstrument = CreateParameter("Budget per instrument", 10000m, 100m, 10000000m, 100m, "Risk");
            _profitBps = CreateParameter("Profit target bps", 50m, 1m, 5000m, 1m, "Exit");
            _roundTripCostBps = CreateParameter("Expected roundtrip cost bps", 16m, 0m, 500m, 1m, "Risk");
            _maxJumpBps = CreateParameter("Max one-observation jump bps", 50m, 1m, 5000m, 1m, "Signal");
            _stopBps = CreateParameter("Risk stop bps", 100m, 1m, 5000m, 1m, "Exit");
            _entryTicksBelowBid = CreateParameter("Entry ticks below bid", 0, 0, 20, 1, "Signal");
            _maxEntryAgeSec = CreateParameter("Entry TTL seconds", 30, 1, 3600, 1, "Risk");
            DeleteEvent += OnDelete;
            _worker = new Thread(Loop) { IsBackground = true, Name = "LiquidityHunterOs.Scanner" };
            _worker.Start();
        }

        public override string GetNameStrategyType() { return "LiquidityHunterOs"; }
        public override void ShowIndividualSettingsDialog() { }

        private void OnDelete() { _disposed = true; }

        private void Loop()
        {
            while (!_disposed)
            {
                try
                {
                    var tabs = _screener.Tabs;
                    // Manage existing positions even when regime is Off; Off forbids new entries.
                    if (tabs != null)
                    {
                        foreach (var tab in tabs)
                        {
                            if (_disposed) break;
                            Process(tab);
                        }
                    }
                }
                catch (Exception ex)
                {
                    SendNewLogMessage(ex.ToString(), LogMessageType.Error);
                }
                Thread.Sleep(Math.Max(100, _pollMs.ValueInt));
            }
        }

        private void Process(BotTabSimple tab)
        {
            if (tab == null || !tab.IsConnected || !tab.IsReadyToTrade || tab.Security == null)
                return;
            List<Position> positions = tab.PositionsOpenAll;
            if (positions != null && positions.Count > 0)
            {
                foreach (var position in positions) ManagePosition(tab, position);
                return;
            }

            if (_regime.ValueString != "On" || StartProgram == StartProgram.IsOsTrader ||
                _screener.PositionsOpenAll.Count >= _maxPositions.ValueInt)
                return;

            MarketDepth source = tab.MarketDepth;
            if (source == null) return;
            MarketDepth depth = source.GetCopy();
            if (depth.Bids == null || depth.Asks == null || depth.Bids.Count < 2 || depth.Asks.Count < 2)
                return;
            decimal bid = depth.Bids[0].Price;
            decimal ask = depth.Asks[0].Price;
            decimal step = tab.Security.PriceStep;
            decimal lot = tab.Security.Lot;
            if (bid <= 0m || ask <= bid || step <= 0m || lot <= 0m) return;
            decimal spread = 20000m * (ask - bid) / (ask + bid);
            if (spread < _minSpreadBps.ValueDecimal || _profitBps.ValueDecimal <= _roundTripCostBps.ValueDecimal) return;
            // Snapshot-to-snapshot jumps are a warning; use tester history to calibrate threshold.
            string priceKey = tab.Security.Name;
            decimal mid = (bid + ask) / 2m;
            lock (_lock)
            {
                decimal previous;
                _previousMid.TryGetValue(priceKey, out previous);
                _previousMid[priceKey] = mid;
                if (previous <= 0m || 10000m * Math.Abs(mid - previous) / previous > _maxJumpBps.ValueDecimal)
                    return;
            }
            decimal bidQty = 0, askQty = 0;
            int top = Math.Min(5, Math.Min(depth.Bids.Count, depth.Asks.Count));
            for (int i = 0; i < top; i++)
            {
                if (depth.Bids[i].Bid <= 0 || depth.Asks[i].Ask <= 0) return;
                bidQty += depth.Bids[i].Bid;
                askQty += depth.Asks[i].Ask;
            }
            if (bidQty + askQty <= 0) return;
            decimal imbalance = (bidQty - askQty) / (bidQty + askQty);
            if (imbalance > _maxAskImbalance.ValueDecimal) return;

            decimal entry = bid - _entryTicksBelowBid.ValueInt * step;
            if (entry <= 0) return;
            // Only passive buy orders. Require exit target to exceed configurable fees externally.
            decimal volume = Math.Floor(_budgetPerInstrument.ValueDecimal / (entry * lot));
            if (volume < 1m) return;
            // Throttle repeats even if broker lags in reporting a pending open position.
            string key = tab.Security.Name;
            DateTime now = tab.TimeServerCurrent;
            lock (_lock)
            {
                DateTime last;
                if (_lastEntryAttempt.TryGetValue(key, out last) && now <= last.AddSeconds(_maxEntryAgeSec.ValueInt))
                    return;
                _lastEntryAttempt[key] = now;
            }
            tab.BuyAtLimit(volume, entry);
        }

        private void ManagePosition(BotTabSimple tab, Position position)
        {
            if (position == null) return;
            // Entry order can be active while OpenVolume is zero or partially filled.
            // Cancel expired residual entry before considering another position.
            if (position.OpenOrders != null && position.OpenOrders.Count > 0)
            {
                Order opening = position.OpenOrders[0];
                if (opening != null && opening.State == OrderStateType.Active &&
                    tab.TimeServerCurrent >= opening.TimeCreate.AddSeconds(_maxEntryAgeSec.ValueInt))
                {
                    tab.CloseAllOrderToPosition(position);
                    return; // Don't assume cancel succeeded; reconcile in subsequent ticks.
                }
            }
            if (position.OpenVolume <= 0m) return;
            // Stop must take priority even when a resting profit-close order is active.
            decimal riskPrice = position.EntryPrice * (1m - _stopBps.ValueDecimal / 10000m);
            if (position.EntryPrice > 0m && tab.PriceBestBid > 0m &&
                (tab.PriceBestBid <= riskPrice || position.SignalTypeClose == "Stop"))
            {
                position.SignalTypeClose = "Stop";
                if (position.CloseActive)
                {
                    tab.CloseAllOrderToPosition(position);
                    return; // Wait for cancellation confirmation; never double-sell.
                }
                tab.CloseAtMarket(position, position.OpenVolume);
                return;
            }
            // Only manage confirmed volume and never duplicate an active close.
            if (position.CloseActive) return;
            decimal step = tab.Security.PriceStep;
            if (step <= 0m || position.EntryPrice <= 0m) return;
            decimal entry = position.EntryPrice;
            decimal profitPrice = RoundUpToTick(entry * (1m + _profitBps.ValueDecimal / 10000m), step);
            tab.CloseAtLimit(position, profitPrice, position.OpenVolume);
        }

        private static decimal RoundUpToTick(decimal price, decimal step)
        {
            return Math.Ceiling(price / step) * step;
        }
    }
}
