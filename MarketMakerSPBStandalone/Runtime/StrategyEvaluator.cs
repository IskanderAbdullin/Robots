using MarketMakerSPBStandalone.Core;

namespace MarketMakerSPBStandalone.Runtime;

public sealed record QuoteDecision(
    IReadOnlyList<Quote> Desired, MarketSafety Safety, string Reason);

/// <summary>
/// Broker-independent evaluation. The same constraints feed both paper and live order sessions.
/// This class never talks to an exchange, disk or API and is deterministic under a supplied clock.
/// </summary>
public sealed class StrategyEvaluator
{
    private readonly string _account;
    private readonly string _uid;
    private readonly PlanSettings _settings;
    private readonly SafetyLimits _risk;

    public StrategyEvaluator(string accountId, string instrumentUid,
        PlanSettings settings, SafetyLimits risk)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(instrumentUid))
            throw new ArgumentException("Account and instrument UID must be non-empty.");
        risk.Validate();
        _account = accountId;
        _uid = instrumentUid;
        _settings = settings;
        _risk = risk;
    }

    public QuoteDecision Evaluate(DateTimeOffset now, bool enabled, bool connected,
        bool ordersSynced, MarketSnapshot? market, AccountCapacity? capacity,
        IReadOnlyCollection<ManagedOrder> liveOrders)
    {
        var portfolioFresh = capacity is { Authoritative: true } p &&
            p.AccountId == _account && p.InstrumentUid == _uid &&
            p.Timestamp <= now && now - p.Timestamp <= _risk.MaxPortfolioAge;
        var bookFresh = market is { } m && m.Timestamp <= now &&
            now - m.Timestamp <= _risk.MaxBookAge;
        var safety = new MarketSafety(enabled, connected, market?.TradingAllowed == true,
            bookFresh, portfolioFresh, ordersSynced);
        if (!safety.CanCreate)
        {
            string reason = !enabled ? "off" : !connected ? "disconnected" :
                market?.TradingAllowed != true ? "trading status" :
                !bookFresh ? "stale book" : !portfolioFresh ? "stale or wrong account" : "order audit";
            return new QuoteDecision(Array.Empty<Quote>(), safety, reason);
        }
        var p0 = capacity!;
        var m0 = market!;
        var own = liveOrders.Where(x => !x.Terminal)
            .Select(x => new Quote(x.Side, x.Price, x.Outstanding)).ToArray();
        var buy = _settings.Buy with
        {
            MaxAvailableLots = Math.Min(_risk.MaxBuyLots, p0.ConfirmedBuyLots)
        };
        var sell = _settings.Sell with
        {
            MaxAvailableLots = Math.Min(_risk.MaxSellLots, p0.ConfirmedSellLots)
        };
        if (buy.Sizing == SizeMode.Fixed)
            buy = buy with { FixedLots = Math.Min(buy.FixedLots, buy.MaxAvailableLots) };
        if (sell.Sizing == SizeMode.Fixed)
            sell = sell with { FixedLots = Math.Min(sell.FixedLots, sell.MaxAvailableLots) };
        var plan = QuotePlanner.Build(new PlanningInput(
            m0.Depth, new PlanSettings(_settings.PriceStep, buy, sell),
            own, m0.MinPrice, m0.MaxPrice));
        decimal multiplier = _risk.LotSize * _risk.PointValue;
        decimal buySpent = 0, sellSpent = 0;
        var capped = new List<Quote>();
        foreach (var q in plan)
        {
            var limit = q.Side == Side.Buy ? _risk.MaxBuyNotional : _risk.MaxSellNotional;
            var spent = q.Side == Side.Buy ? buySpent : sellSpent;
            var room = Math.Max(0m, limit - spent);
            long allowedLots = (long)Math.Min(q.Lots, decimal.Floor(room / (q.Price * multiplier)));
            if (allowedLots <= 0) continue;
            capped.Add(q with { Lots = allowedLots });
            if (q.Side == Side.Buy) buySpent += q.Price * allowedLots * multiplier;
            else sellSpent += q.Price * allowedLots * multiplier;
        }
        return new QuoteDecision(capped, safety, "ready");
    }
}
