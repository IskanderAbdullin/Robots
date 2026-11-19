using System.Threading.Channels;
using MarketMakerSPBStandalone.Core;
using MarketMakerSPBStandalone.Runtime;
using Tinkoff.InvestApi;

namespace MarketMakerSPBStandalone.TInvest;

/// <summary>
/// Headless runtime: two data streams, periodic buying power, broker audit and single-writer
/// trading engine. Real orders are permitted ONLY after explicit settings+CLI+environment opt-in.
/// This class is not instantiated by the paper demo.
/// </summary>
public sealed class TInvestLiveHost
{
    private readonly LiveBotSettings _config;
    private readonly bool _cliLive;
    private readonly Action<string> _log;
    private readonly object _snapshotLock = new();
    private MarketSnapshot? _market;
    private AccountCapacity? _capacity;
    private bool _marketConnected;
    private bool _orderConnected;
    private bool _statusKnown;
    private bool _tradingAllowed;
    private bool _needAudit = true;

    public TInvestLiveHost(LiveBotSettings config, bool cliLiveSwitch, Action<string> log)
    {
        config.Validate();
        _config = config;
        _cliLive = cliLiveSwitch;
        _log = log;
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var token = Environment.GetEnvironmentVariable(_config.TokenEnvironment);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("The selected token environment variable is empty.");
        bool armed = _config.LiveAuthorized(_cliLive);
        _log(armed ? "LIVE ROUTING ARMED (real-money risk)" :
            "READ-ONLY: real order commands are disabled.");

        var client = InvestApiClientFactory.Create(token);
        var orders = new TInvestOrdersClient(client, _config.AccountId,
            _config.InstrumentUid, allowWrites: armed);
        var reader = new TInvestStreamReader(client, _config.InstrumentUid,
            _config.AccountId, _config.BookDepth);
        var strategy = new StrategyEvaluator(_config.AccountId, _config.InstrumentUid,
            new PlanSettings(_config.PriceStep, _config.Buy, _config.Sell), _config.Risk);
        using var session = new DurableBrokerSession(_config.StateFile, orders);
        var inbox = Channel.CreateUnbounded<TInvestOrderReport>(new UnboundedChannelOptions
        {
            SingleReader = true, SingleWriter = true
        });

        using var background = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var marketTask = reader.MarketLoopAsync(OnMarket, background.Token);
        var orderTask = reader.OrderLoopAsync(
            x => { if (!inbox.Writer.TryWrite(x)) throw new InvalidOperationException("Broker inbox unavailable."); },
            alive =>
            {
                lock (_snapshotLock)
                {
                    _orderConnected = alive;
                    _needAudit = true;
                    _capacity = null;
                }
                session.RequireAudit();
            }, background.Token);

        DateTimeOffset nextCapacity = DateTimeOffset.MinValue;
        DateTimeOffset nextAudit = DateTimeOffset.MinValue;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                while (inbox.Reader.TryRead(out var evt))
                {
                    await session.OnStreamReportAsync(evt, stop);
                    lock (_snapshotLock) _capacity = null;
                }
                var now = DateTimeOffset.UtcNow;
                bool connected, auditRequested;
                MarketSnapshot? market;
                AccountCapacity? capacity;
                lock (_snapshotLock)
                {
                    connected = _marketConnected && _orderConnected && _statusKnown;
                    auditRequested = _needAudit;
                    market = _market;
                    capacity = _capacity;
                }

                if (connected && (auditRequested || !session.Audited) && now >= nextAudit)
                {
                    try
                    {
                        bool verified = await session.AuditAsync(stop);
                        if (verified)
                        {
                            lock (_snapshotLock) _needAudit = false;
                            _log("Order audit completed.");
                        }
                        else _log("Order audit incomplete / foreign orders: trading blocked.");
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception ex) { _log("Order audit failed: " + ex.Message); }
                    nextAudit = now.AddSeconds(5);
                }

                if (connected && now >= nextCapacity)
                {
                    try
                    {
                        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                        deadline.CancelAfter(TimeSpan.FromSeconds(5));
                        var power = await orders.ReadBuyingPowerAsync(deadline.Token);
                        if (power.BuyLots < 0 || power.SellLots < 0)
                            throw new InvalidOperationException("Negative broker limits.");
                        capacity = new AccountCapacity(_config.AccountId, _config.InstrumentUid,
                            power.BuyLots, power.SellLots, DateTimeOffset.UtcNow, true);
                        lock (_snapshotLock) _capacity = capacity;
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                    catch (Exception ex)
                    {
                        _log("Buying power unavailable: " + ex.Message);
                        capacity = null;
                        lock (_snapshotLock) _capacity = null;
                    }
                    nextCapacity = now.AddSeconds(3);
                }

                var decision = strategy.Evaluate(DateTimeOffset.UtcNow,
                    enabled: armed, connected: connected,
                    ordersSynced: session.Audited && !auditRequested,
                    market: market, capacity: capacity, liveOrders: session.Orders.Orders);
                try
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    deadline.CancelAfter(TimeSpan.FromSeconds(8));
                    var commands = await session.TickAsync(decision.Desired, decision.Safety, deadline.Token);
                    if (commands.Count > 0)
                    {
                        lock (_snapshotLock) _capacity = null;
                        nextCapacity = DateTimeOffset.MinValue;
                        foreach (var cmd in commands)
                            _log($"{cmd.Kind} {cmd.Side} {cmd.Lots} @ {cmd.Price} id={cmd.ClientId}");
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    // RPCs could have reached the exchange despite error; force an audit.
                    session.RequireAudit();
                    lock (_snapshotLock) { _needAudit = true; _capacity = null; }
                    _log("Order transport uncertain, fail closed: " + ex.Message);
                    nextAudit = DateTimeOffset.UtcNow.AddSeconds(3);
                }
                await Task.Delay(100, stop);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            background.Cancel();
            try { await Task.WhenAll(marketTask, orderTask); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _log("Stream shutdown: " + ex.Message); }
            _log("Host stopped. Verify remaining broker orders; shutdown is not proof of cancellation.");
        }
    }

    private void OnMarket(TInvestMarketEvent ev)
    {
        lock (_snapshotLock)
        {
            if (!ev.StreamConnected)
            {
                _marketConnected = false;
                _statusKnown = false;
                _tradingAllowed = false;
                _market = null;
                _capacity = null;
                return;
            }
            _marketConnected = true;
            if (ev.LimitOrdersAllowed.HasValue)
            {
                _statusKnown = true;
                _tradingAllowed = ev.LimitOrdersAllowed.Value;
                if (_market != null)
                    _market = _market with { TradingAllowed = _tradingAllowed };
            }
            if (ev.Depth != null)
                _market = new MarketSnapshot(ev.Depth, ev.ReceivedAt,
                    _statusKnown && _tradingAllowed, ev.MinPrice, ev.MaxPrice);
        }
    }
}
