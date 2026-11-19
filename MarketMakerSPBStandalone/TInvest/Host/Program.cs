using MarketMakerSPBStandalone.TInvest;

if (args.Length < 2 || args[0] != "--config" || (args.Length > 2 && (args.Length != 3 || args[2] != "--live")))
{
    Console.WriteLine("Usage: dotnet run --project TInvest/Host -- --config ./config.json [--live]");
    Console.WriteLine("Without --live this runs read-only, never sending real orders.");
    Console.WriteLine("To enable real trading also set Enabled, AllowLiveWrites and explicit acknowledgement in config,");
    Console.WriteLine("plus MARKETMAKER_LIVE_ACK environment variable. Do not enable before supervised validation.");
    return;
}
var config = LiveBotSettings.Load(args[1]);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};
var host = new TInvestLiveHost(config, args.Length == 3 && args[2] == "--live",
    msg => Console.WriteLine($"{DateTimeOffset.UtcNow:O} {msg}"));
await host.RunAsync(stop.Token);
