using LiquidityHunter.Core;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
var now = DateTimeOffset.UtcNow;
var b = new Book("TEST", now, new [] {new Level(99m, 100m), new Level(98m, 80m)},
    new [] {new Level(101m, 50m), new Level(102m, 60m)}, 1m, 1);
Check(b.IsValid(now, TimeSpan.FromSeconds(2)), "book invalid");
var f = Features.Calculate(b);
Check(f.Mid == 100m && f.SpreadBps == 200m, "spread");
Check(f.Imbalance > 0m && f.MicroPrice > 100m, "microprice/imbalance");
var stale = b with { Timestamp = now.AddSeconds(-5) };
Check(!stale.IsValid(now, TimeSpan.FromSeconds(2)), "stale data accepted");
var crossed = b with { Bids = new [] {new Level(102m, 100m)} };
Check(!crossed.IsValid(now, TimeSpan.FromSeconds(2)), "crossed book accepted");
var config = CandidateConfig.ConservativeDemo with {
    ReboundProbability = 0.9m, EstimatedRoundTripFeeBps = 0m,
    AdverseExitSlippageBps = 0m, ExitTicks = 3, AdverseTicks = 1
};
var c = CandidateEngine.EvaluateBuy(b, config, now, TimeSpan.FromSeconds(2));
Check(c is not null && c.ExpectedNetPerLot > 0m, "candidate not produced");
var ledger = new BudgetLedger(1000m);
Check(ledger.TryReserve("a", 600m), "reserve failed");
Check(!ledger.TryReserve("b", 500m), "oversubscription");
Check(!ledger.TryReserve("a", 1m), "duplicate key");
ledger.ConfirmEntryFill("a", 250m);
Check(ledger.InventoryExposure == 250m && ledger.Reserved == 350m && ledger.Free == 400m,
    "partial fill transfer");
ledger.CancelUnfilled("a");
Check(ledger.Free == 750m, "cancel reservation");
ledger.ConfirmExit(250m);
Check(ledger.Free == 1000m, "exit accounting");
Console.WriteLine("PASS: deterministic core smoke checks");
