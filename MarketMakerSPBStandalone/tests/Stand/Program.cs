using MarketMakerSPBStandalone.Core;

var checks = 0;
void Check(bool ok, string name)
{
    checks++;
    if (!ok) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS " + name);
}
var baseBuy = new SideSettings(PlacementMode.AheadOfTarget, SizeMode.Fixed, 12, 100, 100, 0);
var baseSell = new SideSettings(PlacementMode.AheadOfTarget, SizeMode.Fixed, 9, 100, 100, 0);
var book = new Book(new[] { new Level(99m, 150), new Level(98m, 200) },
    new[] { new Level(103m, 150), new Level(104m, 250) });
PlanningInput Input(SideSettings buy, SideSettings sell, IReadOnlyList<Quote>? own = null,
    Book? depth = null) => new(depth ?? book,
    new PlanSettings(1, buy, sell), own ?? Array.Empty<Quote>());
var r = QuotePlanner.Build(Input(baseBuy, baseSell));
Check(r.Count == 2 && r[0] == new Quote(Side.Buy, 100, 12) &&
    r[1] == new Quote(Side.Sell, 102, 9), "both target levels");
var own = new[] { new Quote(Side.Buy, 99, 80), new Quote(Side.Sell, 103, 80) };
r = QuotePlanner.Build(Input(baseBuy, baseSell, own));
Check(r.Count == 2 && r[0].Price == 99 && r[0].Lots == 12 &&
    r[1].Price == 103, "subtract own liquidity before target choice");
var pctBuy = baseBuy with { Placement = PlacementMode.PercentageLadder, Percent = 50, FixedLots = 200 };
var pctSell = baseSell with { Placement = PlacementMode.PercentageLadder, Percent = 50, FixedLots = 200 };
r = QuotePlanner.Build(Input(pctBuy, pctSell));
Check(r.Count == 4 && r[0] == new Quote(Side.Buy, 99, 75) &&
    r[1] == new Quote(Side.Buy, 98, 100) &&
    r[2] == new Quote(Side.Sell, 103, 75) &&
    r[3] == new Quote(Side.Sell, 104, 125), "percentage ladder and total size cap");
r = QuotePlanner.Build(Input(baseBuy with { Sizing = SizeMode.Available, MaxAvailableLots = 0 },
    baseSell with { Sizing = SizeMode.Available, MaxAvailableLots = 0 }));
Check(r.Count == 0, "no brokerage-confirmed capacity -> no orders");
r = QuotePlanner.Build(Input(baseBuy, baseSell, depth: new Book(
    new[] { new Level(100m, 100) }, new[] { new Level(101m, 100) })));
Check(r.Count == 1 && r[0].Side == Side.Buy, "one-tick spread buy-priority self-cross prevention");
r = QuotePlanner.Build(Input(baseBuy, baseSell, depth: new Book(
    new[] { new Level(104m, 100) }, new[] { new Level(103m, 100) })));
Check(r.Count == 0, "crossed market fail closed");
r = QuotePlanner.Build(Input(baseBuy, baseSell, depth: new Book(
    new[] { new Level(99m, 100) }, Array.Empty<Level>())));
Check(r.Count == 0, "empty book fail closed");
try
{
    QuotePlanner.Build(Input(baseBuy with { Percent = -1 }, baseSell));
    throw new Exception("Invalid input silently accepted");
}
catch (ArgumentOutOfRangeException) { Check(true, "invalid config rejected"); }
Console.WriteLine($"All {checks} planning checks passed.");
