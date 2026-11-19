using MarketMakerSPBStandalone.Core;
using MarketMakerSPBStandalone.Runtime;

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

var alreadyAtTarget = new[]
{
    new Quote(Side.Buy, 99, 20), new Quote(Side.Sell, 103, 20)
};
r = QuotePlanner.Build(Input(baseBuy, baseSell, alreadyAtTarget));
Check(r.Count == 2 && r[0].Price == 99 && r[1].Price == 103,
    "existing quote at qualifying target stays at its price, no self-chase");

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
    new[] { new Level(100m, 100) }, new[] { new Level(102m, 100) })));
Check(r.Count == 1 && r[0].Side == Side.Buy, "two-tick spread buy-priority self-cross prevention");
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

var rec = new OrderReconciler();
var safe = new MarketSafety(true, true, true, true, true, true);
var cmd = rec.Reconcile(new[] { new Quote(Side.Buy, 99, 10) }, safe);
Check(cmd.Count == 1 && cmd[0].Kind == CommandKind.Place && cmd[0].Lots == 10,
    "initial placement");
Check(rec.Reconcile(new[] { new Quote(Side.Buy, 99, 10) }, safe).Count == 0,
    "pending placement is idempotent");
var id = cmd[0].ClientId;
rec.OnBrokerReport(id, ManagedOrderState.Working, 0);
cmd = rec.Reconcile(new[] { new Quote(Side.Buy, 99, 15) }, safe);
Check(cmd.Count == 1 && cmd[0].Kind == CommandKind.Place && cmd[0].Lots == 5,
    "increase size without cancelling queue");
var second = cmd[0].ClientId;
rec.OnBrokerReport(second, ManagedOrderState.Working, 0);
cmd = rec.Reconcile(new[] { new Quote(Side.Buy, 98, 15) }, safe);
Check(cmd.Count == 2 && cmd.All(x => x.Kind == CommandKind.Cancel),
    "move starts with cancellations");
Check(rec.Reconcile(new[] { new Quote(Side.Buy, 98, 15) }, safe).Count == 0,
    "cancel in-flight blocks replacement");
rec.OnBrokerReport(id, ManagedOrderState.Cancelled, 0);
Check(rec.Reconcile(new[] { new Quote(Side.Buy, 98, 15) }, safe).Count == 0,
    "remaining cancellation blocks replacement");
rec.OnBrokerReport(second, ManagedOrderState.Cancelled, 0);
cmd = rec.Reconcile(new[] { new Quote(Side.Buy, 98, 15) }, safe);
Check(cmd.Count == 1 && cmd[0].Kind == CommandKind.Place && cmd[0].Price == 98,
    "replace only after both terminal states");
Check(rec.Reconcile(new[] { new Quote(Side.Buy, 98, 15) }, safe with { BookFresh = false }).Count == 0,
    "stale book forbids new placement");

var journalFile = Path.Combine(Path.GetTempPath(), "mm-paper-" + Guid.NewGuid().ToString("N") + ".jsonl");
var paper = new PaperBroker();
string paperId;
try
{
    using (var session = new PaperSession(journalFile, paper))
    {
        var placed = session.Tick(new[] { new Quote(Side.Buy, 99, 8) }, safe);
        Check(placed.Count == 1 && placed[0].Kind == CommandKind.Place,
            "paper placement recorded before broker acknowledgement");
        paperId = placed[0].ClientId;
        Check(session.Orders.Find(paperId)?.State == ManagedOrderState.Working,
            "paper broker report updates local state");
        session.Report(paper.Fill(paperId, 3));
        Check(session.Orders.Find(paperId)?.FilledLots == 3,
            "partial fill updates cumulative quantity");
        session.Report(paper.Snapshot(paperId));
        Check(session.Orders.Find(paperId)?.FilledLots == 3,
            "duplicate reports cannot double fill");
    }
    using (var restart = new PaperSession(journalFile, paper))
    {
        Check(restart.Orders.Find(paperId)?.State == ManagedOrderState.Unknown,
            "restart fails closed until broker audit");
        Check(restart.Tick(new[] { new Quote(Side.Buy, 99, 8) }, safe).Count == 0,
            "unknown order blocks new placements");
        restart.Audit();
        Check(restart.Orders.Find(paperId)?.State == ManagedOrderState.Partial,
            "audit restores broker state");
        var outCommands = restart.Tick(Array.Empty<Quote>(), safe);
        Check(outCommands.Count == 1 && outCommands[0].Kind == CommandKind.Cancel &&
            restart.Orders.Find(paperId)?.State == ManagedOrderState.Cancelled,
            "cancel order after audit");
    }
    using (var restart2 = new PaperSession(journalFile, paper))
    {
        Check(restart2.Orders.Find(paperId)?.State == ManagedOrderState.Cancelled &&
            restart2.Orders.Find(paperId)?.FilledLots == 3,
            "journal replays terminal state and fills");
    }
}
finally { File.Delete(journalFile); }


var controllerJournal = Path.Combine(Path.GetTempPath(), "mm-controller-" + Guid.NewGuid().ToString("N") + ".jsonl");
try
{
    using var ps = new PaperSession(controllerJournal, new PaperBroker());
    var controller = new MarketMakerController("paper-account", "paper-instrument",
        new PlanSettings(1, baseBuy, baseSell),
        new SafetyLimits(20, 20, 2000m, 2000m, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)),
        ps);
    var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
    controller.Enabled = true;
    controller.Connected = true;
    controller.OrdersSynchronized = true;
    controller.Market = new MarketSnapshot(book, now, true);
    controller.Capacity = new AccountCapacity("paper-account", "paper-instrument", 20, 20, now, true);
    var valid = controller.Tick(now);
    Check(valid.Count == 2 && valid[0].Lots == 12 && valid[1].Lots == 9,
        "paper controller respects confirmed capacities");
    Check(controller.Tick(now).Count == 0, "controller idle no duplicate orders");
    controller.Market = new MarketSnapshot(book, now.AddSeconds(-10), true);
    var cancelled = controller.Tick(now);
    Check(cancelled.Count == 2 && cancelled.All(x => x.Kind == CommandKind.Cancel),
        "stale order book cancels known live quotes");
    controller.Market = new MarketSnapshot(book, now, true);
    controller.Capacity = controller.Capacity with { AccountId = "unexpected-account" };
    Check(controller.Tick(now).Count == 0,
        "account identity mismatch blocks order creation");
}
finally { File.Delete(controllerJournal); }


var rejected = new OrderReconciler();
var firstRejected = rejected.Reconcile(new[] { new Quote(Side.Buy, 99, 1) }, safe);
rejected.OnBrokerReport(firstRejected[0].ClientId, ManagedOrderState.Rejected, 0);
Check(rejected.FailureStop && rejected.Reconcile(
    new[] { new Quote(Side.Buy, 99, 1) }, safe).Count == 0,
    "definitive broker rejection is a sticky fail-closed stop");


var rng = new Random(20261120);
for (int iteration = 0; iteration < 20_000; iteration++)
{
    int bid = rng.Next(20, 200);
    int ask = bid + rng.Next(1, 15);
    var fuzzBook = new Book(new[]
    {
        new Level(bid, rng.Next(1, 300)),
        new Level(bid - 1, rng.Next(1, 300)),
    }, new[]
    {
        new Level(ask, rng.Next(1, 300)),
        new Level(ask + 1, rng.Next(1, 300)),
    });
    var fuzzBuy = baseBuy with
    {
        Placement = (PlacementMode)rng.Next(2),
        Sizing = SizeMode.Available,
        MaxAvailableLots = rng.Next(0, 250),
        ThresholdLots = rng.Next(0, 400),
        Percent = rng.Next(0, 101)
    };
    var fuzzSell = baseSell with
    {
        Placement = (PlacementMode)rng.Next(2),
        Sizing = SizeMode.Available,
        MaxAvailableLots = rng.Next(0, 250),
        ThresholdLots = rng.Next(0, 400),
        Percent = rng.Next(0, 101)
    };
    var quotes = QuotePlanner.Build(new PlanningInput(fuzzBook,
        new PlanSettings(1m, fuzzBuy, fuzzSell), Array.Empty<Quote>()));
    var buyTotal = quotes.Where(x => x.Side == Side.Buy).Sum(x => x.Lots);
    var sellTotal = quotes.Where(x => x.Side == Side.Sell).Sum(x => x.Lots);
    if (buyTotal > fuzzBuy.MaxAvailableLots || sellTotal > fuzzSell.MaxAvailableLots ||
        quotes.Any(x => x.Lots <= 0 || x.Price <= 0 ||
            (x.Side == Side.Buy && x.Price >= ask) ||
            (x.Side == Side.Sell && x.Price <= bid)) ||
        (quotes.Any(x => x.Side == Side.Buy) && quotes.Any(x => x.Side == Side.Sell) &&
         quotes.Where(x => x.Side == Side.Buy).Max(x => x.Price) >=
         quotes.Where(x => x.Side == Side.Sell).Min(x => x.Price)))
        throw new Exception("Property failure at iteration " + iteration);
}
Check(true, "20,000 randomized dual-sided book/size/crossing invariants");

Console.WriteLine($"All {checks} offline checks passed.");
