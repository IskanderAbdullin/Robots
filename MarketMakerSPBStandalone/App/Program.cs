using MarketMakerSPBStandalone.Core;
using MarketMakerSPBStandalone.Runtime;

if (args.Length != 1 || args[0] != "--demo")
{
    Console.WriteLine("MarketMakerSPBStandalone: paper-only prototype. Usage: dotnet run --project App -- --demo");
    Console.WriteLine("No live broker connection is available. Read SPEC.md before integration.");
    return;
}
var file = Path.Combine(Path.GetTempPath(), "mm-demo-" + Guid.NewGuid().ToString("N") + ".jsonl");
try
{
    var paper = new PaperBroker();
    using var session = new PaperSession(file, paper);
    var buy = new SideSettings(PlacementMode.AheadOfTarget, SizeMode.Fixed, 10, 100, 100, 0);
    var sell = new SideSettings(PlacementMode.AheadOfTarget, SizeMode.Fixed, 10, 100, 100, 0);
    var config = new PlanSettings(0.01m, buy, sell);
    var limits = new SafetyLimits(10, 10, 20000, 20000,
        TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), LotSize: 1);
    var controller = new MarketMakerController("DEMO", "PAPER_UID", config, limits, session)
    {
        Enabled = true,
        Connected = true,
        OrdersSynchronized = true
    };
    var now = DateTimeOffset.UtcNow;
    controller.Capacity = new AccountCapacity("DEMO", "PAPER_UID", 10, 10, now, true);
    controller.Market = new MarketSnapshot(new Book(
        new[] { new Level(99.00m, 150), new Level(98.99m, 150) },
        new[] { new Level(99.10m, 200), new Level(99.11m, 200) }), now, true);
    foreach (var cmd in controller.Tick(now))
        Console.WriteLine($"PAPER {cmd.Kind} {cmd.Side} {cmd.Lots} lots @ {cmd.Price} client={cmd.ClientId}");
    controller.Market = controller.Market with { Timestamp = now.AddSeconds(-10) };
    foreach (var cmd in controller.Tick(now))
        Console.WriteLine($"PAPER STALE-DATA {cmd.Kind} {cmd.Side} client={cmd.ClientId}");
    Console.WriteLine("Demo completed. No broker connections or real orders were made.");
}
finally { File.Delete(file); }
