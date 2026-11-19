namespace LiquidityHunter.Core;

public enum Side { Buy, Sell }
public readonly record struct Level(decimal Price, decimal Quantity);
public sealed record Book(
    string InstrumentId, DateTimeOffset Timestamp,
    IReadOnlyList<Level> Bids, IReadOnlyList<Level> Asks,
    decimal PriceStep, int LotSize)
{
    public bool IsValid(DateTimeOffset now, TimeSpan maxAge)
    {
        if (string.IsNullOrWhiteSpace(InstrumentId) || PriceStep <= 0 || LotSize < 1
            || Timestamp > now || now - Timestamp > maxAge
            || Bids.Count == 0 || Asks.Count == 0) return false;
        if (Bids.Any(x => x.Price <= 0 || x.Quantity <= 0)
            || Asks.Any(x => x.Price <= 0 || x.Quantity <= 0)) return false;
        if (Bids[0].Price >= Asks[0].Price) return false;
        return IsDescending(Bids) && IsAscending(Asks);
    }
    private static bool IsDescending(IReadOnlyList<Level> levels)
    {
        for (var i = 1; i < levels.Count; i++) if (levels[i].Price >= levels[i-1].Price) return false;
        return true;
    }
    private static bool IsAscending(IReadOnlyList<Level> levels)
    {
        for (var i = 1; i < levels.Count; i++) if (levels[i].Price <= levels[i-1].Price) return false;
        return true;
    }
}

public sealed record BookFeatures(decimal Mid, decimal SpreadBps, decimal Imbalance,
    decimal MicroPrice, decimal BidDepth, decimal AskDepth);

public static class Features
{
    public static BookFeatures Calculate(Book book, int depth = 5)
    {
        if (depth < 1 || book.Bids.Count == 0 || book.Asks.Count == 0)
            throw new ArgumentOutOfRangeException(nameof(depth));
        var bid = book.Bids[0]; var ask = book.Asks[0];
        if (bid.Price <= 0 || ask.Price <= bid.Price)
            throw new ArgumentException("Crossed or invalid book");
        var bq = book.Bids.Take(depth).Sum(x => x.Quantity);
        var aq = book.Asks.Take(depth).Sum(x => x.Quantity);
        if (bq <= 0 || aq <= 0) throw new ArgumentException("Missing depth");
        var mid = (bid.Price + ask.Price) / 2m;
        var denom = bq + aq;
        var microTop = (ask.Price * bid.Quantity + bid.Price * ask.Quantity) / (bid.Quantity + ask.Quantity);
        return new(mid, 10000m * (ask.Price - bid.Price) / mid,
            (bq - aq) / denom, microTop, bq, aq);
    }
}

public sealed record Candidate(string InstrumentId, Side Side,
    decimal EntryPrice, decimal ExitPrice, int Lots,
    decimal EstimatedFillProbability, decimal EstimatedWinProbability,
    decimal NetGainPerLot, decimal NetLossPerLot, decimal ExpectedNetPerLot);
public sealed record CandidateConfig(decimal MinSpreadBps, decimal MinExpectedNetPerLot,
    decimal EstimatedRoundTripFeeBps, decimal ReboundProbability,
    decimal EstimatedFillProbability, decimal AdverseExitSlippageBps,
    int Lots, int ExitTicks, int AdverseTicks)
{
    public static CandidateConfig ConservativeDemo => new(8m, 0m, 8m, 0.55m, 0.2m, 10m, 1, 2, 6);
}

public static class CandidateEngine
{
    // Educational baseline: probabilities are uncalibrated placeholders, never production alpha.
    public static Candidate? EvaluateBuy(Book b, CandidateConfig cfg, DateTimeOffset now,
        TimeSpan maxAge)
    {
        if (!b.IsValid(now, maxAge) || cfg.Lots <= 0 || cfg.ExitTicks < 1
            || cfg.AdverseTicks < 1 || cfg.ReboundProbability is < 0m or > 1m
            || cfg.EstimatedFillProbability is < 0m or > 1m) return null;
        var f = Features.Calculate(b);
        if (f.SpreadBps < cfg.MinSpreadBps) return null;
        var entry = b.Bids[0].Price;
        var exit = entry + cfg.ExitTicks * b.PriceStep;
        var adverseExit = entry - cfg.AdverseTicks * b.PriceStep;
        if (adverseExit <= 0m) return null;
        var qty = b.LotSize;
        var fee = (entry + exit) * qty * cfg.EstimatedRoundTripFeeBps / 20000m;
        var adverseSlippage = entry * qty * cfg.AdverseExitSlippageBps / 10000m;
        var gain = (exit - entry) * qty - fee;
        var loss = (entry - adverseExit) * qty + fee + adverseSlippage;
        var ev = cfg.ReboundProbability * gain - (1m-cfg.ReboundProbability) * loss;
        if (ev < cfg.MinExpectedNetPerLot || ev <= 0m) return null;
        return new(b.InstrumentId, Side.Buy, entry, exit, cfg.Lots,
            cfg.EstimatedFillProbability, cfg.ReboundProbability, gain, loss, ev);
    }
}
