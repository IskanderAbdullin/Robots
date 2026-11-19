namespace MarketMakerSPBStandalone.Core;

public enum Side { Buy, Sell }
public enum PlacementMode { AheadOfTarget, PercentageLadder }
public enum SizeMode { Fixed, Available }
public readonly record struct Level(decimal Price, long Lots);
public readonly record struct Quote(Side Side, decimal Price, long Lots);
public sealed record Book(IReadOnlyList<Level> Bids, IReadOnlyList<Level> Asks);
public sealed record SideSettings(
    PlacementMode Placement,
    SizeMode Sizing,
    long FixedLots,
    decimal ThresholdLots,
    decimal Percent,
    long MaxAvailableLots);
public sealed record PlanSettings(decimal PriceStep, SideSettings Buy, SideSettings Sell);
public sealed record PlanningInput(
    Book Book,
    PlanSettings Settings,
    IReadOnlyList<Quote> OwnLiveOrders,
    decimal MinPrice = 0,
    decimal MaxPrice = 0);

/// <summary>Pure deterministic desired quote builder; all quantities are integer lots.</summary>
public static class QuotePlanner
{
    public static IReadOnlyList<Quote> Build(PlanningInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var step = input.Settings.PriceStep;
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(input), "PriceStep must be positive.");
        Validate(input.Settings.Buy);
        Validate(input.Settings.Sell);
        if (input.MinPrice < 0 || input.MaxPrice < 0 ||
            (input.MaxPrice > 0 && input.MinPrice > input.MaxPrice))
            throw new ArgumentException("Invalid price boundaries.");
        var bids = Clean(input.Book.Bids, true, step, input.OwnLiveOrders);
        var asks = Clean(input.Book.Asks, false, step, input.OwnLiveOrders);
        if (bids.Count == 0 || asks.Count == 0 || bids[0].Price >= asks[0].Price)
            return Array.Empty<Quote>(); // fail closed on crossed, empty or invalid book
        var result = new List<Quote>();
        BuildSide(Side.Buy, bids, input.Settings.Buy, step, bids[0].Price,
            asks[0].Price, input.MinPrice, input.MaxPrice, input.OwnLiveOrders, result);
        BuildSide(Side.Sell, asks, input.Settings.Sell, step, bids[0].Price,
            asks[0].Price, input.MinPrice, input.MaxPrice, result);
        // Both sides can target the same price in a one-tick spread.
        // The legacy combined strategy gives BUY priority.
        var buyMax = result.Where(x => x.Side == Side.Buy).Select(x => x.Price).DefaultIfEmpty(0).Max();
        result.RemoveAll(x => x.Side == Side.Sell && x.Price <= buyMax);
        return result;
    }

    private static void Validate(SideSettings side)
    {
        if (side.FixedLots < 0 || side.MaxAvailableLots < 0 ||
            side.ThresholdLots < 0 || side.Percent < 0 || side.Percent > 100)
            throw new ArgumentOutOfRangeException(nameof(side), "Negative size/threshold or percentage outside [0, 100].");
    }

    private static List<Level> Clean(IReadOnlyList<Level> source, bool bids, decimal step,
        IReadOnlyList<Quote> own)
    {
        var ownByPrice = own.Where(x => x.Side == (bids ? Side.Buy : Side.Sell))
            .GroupBy(x => x.Price).ToDictionary(x => x.Key, x => x.Sum(y => Math.Max(0, y.Lots)));
        return source.Where(x => x.Price > 0 && x.Lots > 0 && x.Price % step == 0)
            .GroupBy(x => x.Price)
            .Select(x => new Level(x.Key,
                Math.Max(0, x.Sum(y => y.Lots) - ownByPrice.GetValueOrDefault(x.Key))))
            .Where(x => x.Lots > 0)
            .OrderBy(x => bids ? -x.Price : x.Price).ToList();
    }

    private static void BuildSide(Side side, IReadOnlyList<Level> levels,
        SideSettings cfg, decimal step, decimal bestBid, decimal bestAsk,
        decimal minPrice, decimal maxPrice, IReadOnlyList<Quote> ownOrders,
        List<Quote> result)
    {
        var remaining = cfg.Sizing == SizeMode.Fixed ? cfg.FixedLots : cfg.MaxAvailableLots;
        if (remaining <= 0) return;
        if (cfg.Placement == PlacementMode.AheadOfTarget)
        {
            foreach (var level in levels)
            {
                if (level.Lots < cfg.ThresholdLots) continue;
                // Legacy OsEngine behavior: if this target is our existing level,
                // keep its price instead of endlessly stepping ahead of ourselves.
                var price = ownOrders.Any(o => o.Side == side && o.Price == level.Price && o.Lots > 0)
                    ? level.Price : level.Price + (side == Side.Buy ? step : -step);
                if (Allowed(price, side, bestBid, bestAsk, minPrice, maxPrice))
                    result.Add(new Quote(side, price, remaining));
                break;
            }
            return;
        }
        foreach (var level in levels)
        {
            if (remaining <= 0) break;
            var lots = (long)decimal.Floor(level.Lots * cfg.Percent / 100m);
            lots = Math.Min(lots, remaining);
            if (lots <= 0) continue;
            if (!Allowed(level.Price, side, bestBid, bestAsk, minPrice, maxPrice)) continue;
            result.Add(new Quote(side, level.Price, lots));
            remaining -= lots;
        }
    }

    private static bool Allowed(decimal price, Side side, decimal bestBid, decimal bestAsk,
        decimal minPrice, decimal maxPrice) =>
        price > 0 && (minPrice <= 0 || price >= minPrice) &&
        (maxPrice <= 0 || price <= maxPrice) &&
        (side == Side.Buy ? price < bestAsk : price > bestBid);
}
