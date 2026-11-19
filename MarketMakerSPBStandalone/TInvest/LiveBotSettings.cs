using System.Text.Json;
using MarketMakerSPBStandalone.Core;
using MarketMakerSPBStandalone.Runtime;

namespace MarketMakerSPBStandalone.TInvest;

public sealed class LiveBotSettings
{
    public string AccountId { get; set; } = "";
    public string InstrumentUid { get; set; } = "";
    public string TokenEnvironment { get; set; } = "TINVEST_TOKEN";
    public string StateFile { get; set; } = "./state/marketmaker-orders.jsonl";
    public int BookDepth { get; set; } = 20;
    public bool Enabled { get; set; } = false;
    public bool AllowLiveWrites { get; set; } = false;
    public string LiveAcknowledgement { get; set; } = "";
    public decimal PriceStep { get; set; } = 0.01m;
    public SideSettings Buy { get; set; } = new(
        PlacementMode.AheadOfTarget, SizeMode.Fixed, 0, 100, 100, 0);
    public SideSettings Sell { get; set; } = new(
        PlacementMode.AheadOfTarget, SizeMode.Fixed, 0, 100, 100, 0);
    public SafetyLimits Risk { get; set; } = new(
        0, 0, 0, 0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountId) || string.IsNullOrWhiteSpace(InstrumentUid))
            throw new InvalidOperationException("AccountId and InstrumentUid are required.");
        if (string.IsNullOrWhiteSpace(TokenEnvironment) || string.IsNullOrWhiteSpace(StateFile))
            throw new InvalidOperationException("Token environment variable and state path required.");
        if (BookDepth < 1 || BookDepth > 50 || PriceStep <= 0)
            throw new InvalidOperationException("BookDepth or PriceStep invalid.");
        Risk.Validate();
    }

    public bool LiveAuthorized(bool cliLiveSwitch) =>
        cliLiveSwitch && Enabled && AllowLiveWrites &&
        LiveAcknowledgement == "I_UNDERSTAND_REAL_MONEY_ORDERS" &&
        Environment.GetEnvironmentVariable("MARKETMAKER_LIVE_ACK") ==
            "I_UNDERSTAND_REAL_MONEY_ORDERS";

    public static LiveBotSettings Load(string path)
    {
        var cfg = JsonSerializer.Deserialize<LiveBotSettings>(File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidDataException("Missing JSON configuration.");
        cfg.Validate();
        return cfg;
    }
}
