using MarketMakerSPBStandalone.Core;
using MarketMakerSPBStandalone.TInvest;

var passed = 0;
void Assert(bool ok, string name)
{
    if (!ok) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS " + name);
    passed++;
}
var temp = Path.Combine(Path.GetTempPath(), "mm-orders-" + Guid.NewGuid().ToString("N") + ".jsonl");
var safe = new MarketSafety(true, true, true, true, true, true);
var fake = new FakeGateway();
try
{
    string id;
    using (var session = new DurableBrokerSession(temp, fake))
    {
        Assert((await session.TickAsync(new[] { new Quote(Side.Buy, 99, 5) }, safe)).Count == 0,
            "no live commands before audit");
        Assert(await session.AuditAsync(), "empty broker audit");
        var placed = await session.TickAsync(new[] { new Quote(Side.Buy, 99, 5) }, safe);
        Assert(placed.Count == 1 && placed[0].Kind == CommandKind.Place, "post after audit");
        id = placed[0].ClientId;
        var tradeTime = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var fillUpdate = new TInvestOrderReport(id, "paper-exchange-" + id,
            ManagedOrderState.Partial, 2,
            new[] { new TInvestFill("trade-01", 99m, 2, tradeTime) });
        await session.OnStreamReportAsync(fillUpdate);
        await session.OnStreamReportAsync(fillUpdate);
        Assert(session.Fills.Fills.Count == 1 && session.Fills.NetLots == 2,
            "duplicate broker trade reports are accounted only once");
        var cancel = await session.TickAsync(new[] { new Quote(Side.Buy, 98, 5) }, safe);
        Assert(cancel.Count == 1 && cancel[0].Kind == CommandKind.Cancel, "cancel old price first");
        Assert((await session.TickAsync(new[] { new Quote(Side.Buy, 98, 5) }, safe)).Count == 0,
            "cancel acceptance is not final confirmation");
        fake.CompleteCancel(id);
        await session.OnStreamReportAsync(await fake.ReadStateAsync("", id));
        var newPlacement = await session.TickAsync(new[] { new Quote(Side.Buy, 98, 5) }, safe);
        Assert(newPlacement.Count == 1 && newPlacement[0].Price == 98,
            "replace after terminal cancel report");
    }
    using (var recovered = new DurableBrokerSession(temp, fake))
    {
        Assert(recovered.Fills.Fills.Count == 1 && recovered.Fills.NetLots == 2,
            "broker fills persisted after process restart");
        Assert(recovered.Orders.Orders.Any(o => !o.Terminal &&
            o.State == ManagedOrderState.Unknown), "unknown after process restart");
        Assert((await recovered.TickAsync(new[] { new Quote(Side.Buy, 98, 5) }, safe)).Count == 0,
            "restart cannot place before audit");
        Assert(await recovered.AuditAsync(), "broker idempotency-key state audit after restart");
        Assert((await recovered.TickAsync(new[] { new Quote(Side.Buy, 98, 5) }, safe)).Count == 0,
            "no duplicate after recovery");
        fake.InjectForeignOrder();
        Assert(!await recovered.AuditAsync(), "foreign active order blocks new trading");
    }
}
finally { File.Delete(temp); }
Console.WriteLine($"All {passed} T-Invest transport-mock checks passed.");

sealed class FakeGateway : IOrderGateway
{
    private readonly Dictionary<string, TInvestOrderReport> _orders = new();
    private bool _foreign;
    public Task<TInvestOrderReport> PostAsync(OrderCommand cmd, CancellationToken token = default)
    {
        var report = new TInvestOrderReport(cmd.ClientId, "paper-exchange-" + cmd.ClientId,
            ManagedOrderState.Working, 0);
        _orders.Add(cmd.ClientId, report);
        return Task.FromResult(report);
    }
    public Task RequestCancelAsync(string exchangeId, string clientId, CancellationToken token = default)
    {
        if (!_orders.ContainsKey(clientId)) throw new Exception("No order.");
        return Task.CompletedTask; // acceptance, NOT terminal confirmation
    }
    public void CompleteCancel(string id)
    {
        _orders[id] = _orders[id] with { State = ManagedOrderState.Cancelled };
    }
    public void InjectForeignOrder() => _foreign = true;
    public Task<TInvestOrderReport> ReadStateAsync(string exchangeId, string clientId,
        CancellationToken token = default) => Task.FromResult(_orders[clientId]);
    public Task<IReadOnlyList<TInvestOrderReport>> ReadActiveAsync(CancellationToken token = default)
    {
        IReadOnlyList<TInvestOrderReport> active = _orders.Values
            .Where(x => x.State != ManagedOrderState.Cancelled).ToList();
        if (_foreign) active = active.Append(new TInvestOrderReport(
            "another-robot", "external", ManagedOrderState.Working, 0)).ToList();
        return Task.FromResult(active);
    }
    public Task<long> ReadFreeSellLotsAsync(int lotSize, CancellationToken token = default) =>
        Task.FromResult(100L);
    public Task<TInvestInventory> ReadInventoryAsync(int lotSize, CancellationToken token = default) =>
        Task.FromResult(new TInvestInventory(100, 100));
    public Task<TInvestBuyingPower> ReadBuyingPowerAsync(CancellationToken token = default) =>
        Task.FromResult(new TInvestBuyingPower(100, 100));
}
