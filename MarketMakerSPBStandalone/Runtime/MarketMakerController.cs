using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.Runtime;

public sealed record AccountCapacity(
    string AccountId, string InstrumentUid, long ConfirmedBuyLots, long ConfirmedSellLots,
    DateTimeOffset Timestamp, bool Authoritative,
    long TotalHeldLots = 0, bool PositionAuthoritative = false);
public sealed record MarketSnapshot(
    Book Depth, DateTimeOffset Timestamp, bool TradingAllowed, decimal MinPrice = 0,
    decimal MaxPrice = 0);
public sealed record SafetyLimits(
    long MaxBuyLots, long MaxSellLots, decimal MaxBuyNotional,
    decimal MaxSellNotional, TimeSpan MaxBookAge, TimeSpan MaxPortfolioAge,
    int LotSize = 1, decimal PointValue = 1m,
    long MaxLongPositionLots = long.MaxValue)
{
    public void Validate()
    {
        if (MaxBuyLots < 0 || MaxSellLots < 0 || MaxBuyNotional < 0 ||
            MaxSellNotional < 0 || MaxBookAge <= TimeSpan.Zero || MaxPortfolioAge <= TimeSpan.Zero ||
            LotSize <= 0 || PointValue <= 0 || MaxLongPositionLots < 0)
            throw new ArgumentException("Invalid risk limits.");
    }
}

public sealed class MarketMakerController
{
    private readonly StrategyEvaluator _strategy;
    private readonly PaperSession _session;
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public bool OrdersSynchronized { get; set; }
    public MarketSnapshot? Market { get; set; }
    public AccountCapacity? Capacity { get; set; }

    public MarketMakerController(string accountId, string instrumentUid,
        PlanSettings settings, SafetyLimits limits, PaperSession session)
    {
        _strategy = new StrategyEvaluator(accountId, instrumentUid, settings, limits);
        _session = session;
    }

    public IReadOnlyList<OrderCommand> Tick(DateTimeOffset now)
    {
        var decision = _strategy.Evaluate(now, Enabled, Connected,
            OrdersSynchronized, Market, Capacity, _session.Orders.Orders);
        return _session.Tick(decision.Desired, decision.Safety);
    }
}
