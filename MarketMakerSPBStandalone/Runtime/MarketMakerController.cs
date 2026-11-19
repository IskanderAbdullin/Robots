using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.Runtime;

public sealed record AccountCapacity(
    string AccountId, string InstrumentUid, long ConfirmedBuyLots, long ConfirmedSellLots,
    DateTimeOffset Timestamp, bool Authoritative);
public sealed record MarketSnapshot(
    Book Depth, DateTimeOffset Timestamp, bool TradingAllowed, decimal MinPrice = 0,
    decimal MaxPrice = 0);
public sealed record SafetyLimits(
    long MaxBuyLots, long MaxSellLots, decimal MaxBuyNotional,
    decimal MaxSellNotional, TimeSpan MaxBookAge, TimeSpan MaxPortfolioAge)
{
    public void Validate()
    {
        if (MaxBuyLots < 0 || MaxSellLots < 0 || MaxBuyNotional < 0 ||
            MaxSellNotional < 0 || MaxBookAge <= TimeSpan.Zero || MaxPortfolioAge <= TimeSpan.Zero)
            throw new ArgumentException("Invalid risk limits.");
    }
}

/// <summary>
/// Broker-agnostic coordinator for paper tests. All time and account facts are supplied
/// externally and can be replayed. Passing an unknown or stale account fails closed.
/// </summary>
public sealed class MarketMakerController
{
    private readonly string _account;
    private readonly string _instrumentUid;
    private readonly PlanSettings _settings;
    private readonly SafetyLimits _limits;
    private readonly PaperSession _session;
    public bool Enabled { get; set; }
    public bool Connected { get; set; }
    public bool OrdersSynchronized { get; set; }
    public MarketSnapshot? Market { get; set; }
    public AccountCapacity? Capacity { get; set; }

    public MarketMakerController(string accountId, string instrumentUid,
        PlanSettings settings, SafetyLimits limits, PaperSession session)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(instrumentUid))
            throw new ArgumentException("Account and instrument UID must be set.");
        limits.Validate();
        _account = accountId;
        _instrumentUid = instrumentUid;
        _settings = settings;
        _limits = limits;
        _session = session;
    }

    public IReadOnlyList<OrderCommand> Tick(DateTimeOffset now)
    {
        var authenticated = Capacity is { Authoritative: true } c &&
            c.AccountId == _account && c.InstrumentUid == _instrumentUid &&
            c.Timestamp <= now && now - c.Timestamp <= _limits.MaxPortfolioAge;
        var bookFresh = Market is { } market && market.Timestamp <= now &&
            now - market.Timestamp <= _limits.MaxBookAge;
        var safety = new MarketSafety(Enabled, Connected,
            Market?.TradingAllowed == true, bookFresh, authenticated, OrdersSynchronized);
        if (!safety.CanCreate)
            return _session.Tick(Array.Empty<Quote>(), safety);

        var portfolio = Capacity!;
        var depth = Market!;
        var own = _session.Orders.Orders.Where(x => !x.Terminal)
            .Select(x => new Quote(x.Side, x.Price, x.Outstanding)).ToArray();
        var buyLots = Math.Min(portfolio.ConfirmedBuyLots, _limits.MaxBuyLots);
        var sellLots = Math.Min(portfolio.ConfirmedSellLots, _limits.MaxSellLots);
        var buyCfg = _settings.Buy with { MaxAvailableLots = buyLots };
        var sellCfg = _settings.Sell with { MaxAvailableLots = sellLots };
        if (buyCfg.Sizing == SizeMode.Fixed)
            buyCfg = buyCfg with { FixedLots = Math.Min(buyCfg.FixedLots, buyLots) };
        if (sellCfg.Sizing == SizeMode.Fixed)
            sellCfg = sellCfg with { FixedLots = Math.Min(sellCfg.FixedLots, sellLots) };
        var plan = QuotePlanner.Build(new PlanningInput(depth.Depth,
            new PlanSettings(_settings.PriceStep, buyCfg, sellCfg), own,
            depth.MinPrice, depth.MaxPrice));

        // Notional cap is conservative for each side: apply to the full desired quote set.
        var capped = CapNotional(plan, _limits.MaxBuyNotional, _limits.MaxSellNotional);
        return _session.Tick(capped, safety);
    }

    private static IReadOnlyList<Quote> CapNotional(IReadOnlyList<Quote> quotes,
        decimal maxBuyNotional, decimal maxSellNotional)
    {
        var result = new List<Quote>();
        decimal buySpent = 0, sellSpent = 0;
        foreach (var quote in quotes)
        {
            if (quote.Price <= 0 || quote.Lots <= 0) continue;
            bool buy = quote.Side == Side.Buy;
            decimal limit = buy ? maxBuyNotional : maxSellNotional;
            decimal spent = buy ? buySpent : sellSpent;
            long eligible = limit <= spent ? 0 :
                (long)Math.Min(quote.Lots, decimal.Floor((limit - spent) / quote.Price));
            if (eligible <= 0) continue;
            result.Add(quote with { Lots = eligible });
            if (buy) buySpent += quote.Price * eligible;
            else sellSpent += quote.Price * eligible;
        }
        return result;
    }
}
