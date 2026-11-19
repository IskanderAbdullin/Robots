using MarketMakerSPBStandalone.Core;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace MarketMakerSPBStandalone.TInvest;

/// <summary>
/// Read-only T-Invest adapter. This class contains NO trading methods.
/// All order writes will be in a separate, explicitly enabled and audited component.
/// </summary>
public sealed class TInvestDataClient
{
    private readonly InvestApiClient _client;

    public TInvestDataClient(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("Access token is missing.", nameof(accessToken));
        _client = InvestApiClientFactory.Create(accessToken);
    }

    public async Task<Book> ReadBookAsync(string instrumentUid, int depth = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instrumentUid)) throw new ArgumentException("UID missing.", nameof(instrumentUid));
        if (depth < 1 || depth > 50) throw new ArgumentOutOfRangeException(nameof(depth));
        var result = await _client.MarketData.GetOrderBookAsync(
            new GetOrderBookRequest { InstrumentId = instrumentUid, Depth = depth },
            cancellationToken: cancellationToken);
        return new Book(
            result.Bids.Select(x => new Level(ToDecimal(x.Price), x.Quantity)).ToArray(),
            result.Asks.Select(x => new Level(ToDecimal(x.Price), x.Quantity)).ToArray());
    }

    public async Task<IReadOnlyList<string>> ReadAccountIdsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.Users.GetAccountsAsync(new GetAccountsRequest(),
            cancellationToken: cancellationToken);
        return result.Accounts.Select(x => x.Id).ToArray();
    }

    public static decimal ToDecimal(MoneyValue? money)
    {
        if (money is null) return 0m;
        return money.Units + money.Nano / 1_000_000_000m;
    }

    public static decimal ToDecimal(Quotation? quote)
    {
        if (quote is null) return 0m;
        return quote.Units + quote.Nano / 1_000_000_000m;
    }
}
