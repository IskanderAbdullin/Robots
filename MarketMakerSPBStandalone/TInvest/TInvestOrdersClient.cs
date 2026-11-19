using MarketMakerSPBStandalone.Core;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace MarketMakerSPBStandalone.TInvest;

public sealed record TInvestOrderReport(
    string ClientId, string ExchangeId, ManagedOrderState State, long FilledLots);
public sealed record TInvestBuyingPower(long BuyLots, long SellLots);

/// <summary>
/// T-Invest unary order API binding. NOT used by the paper application.
/// Must never treat a timeout or successful cancel HTTP/gRPC response as a terminal order report.
/// </summary>
public sealed class TInvestOrdersClient
{
    private readonly InvestApiClient _client;
    private readonly string _accountId;
    private readonly string _instrumentUid;
    private readonly bool _allowWrites;

    public TInvestOrdersClient(InvestApiClient client, string accountId, string instrumentUid,
        bool allowWrites = false)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(instrumentUid))
            throw new ArgumentException("Account and instrument must be explicit.");
        _client = client;
        _accountId = accountId;
        _instrumentUid = instrumentUid;
        _allowWrites = allowWrites;
    }

    private void RequireWrites()
    {
        if (!_allowWrites) throw new InvalidOperationException(
            "Real order routing disabled: approved live host and exhaustive safety checks required.");
    }

    public async Task<TInvestOrderReport> PostAsync(OrderCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireWrites();
        if (command.Kind != CommandKind.Place || command.Lots <= 0 || command.Price <= 0 ||
            !Guid.TryParse(command.ClientId, out _))
            throw new ArgumentException("Invalid order or client UUID.", nameof(command));
        var response = await _client.Orders.PostOrderAsync(new PostOrderRequest
        {
            InstrumentId = _instrumentUid,
            AccountId = _accountId,
            Quantity = command.Lots,
            Price = FromDecimal(command.Price),
            Direction = command.Side == Side.Buy ? OrderDirection.Buy : OrderDirection.Sell,
            OrderType = OrderType.Limit,
            OrderId = command.ClientId,
            TimeInForce = TimeInForceType.TimeInForceDay,
            ConfirmMarginTrade = false
        }, cancellationToken: cancellationToken);
        return new TInvestOrderReport(command.ClientId, response.OrderId,
            Map(response.ExecutionReportStatus), response.LotsExecuted);
    }

    /// <summary>A successful RPC only acknowledges the cancel request, not final cancellation.</summary>
    public async Task RequestCancelAsync(string exchangeId, string clientId,
        CancellationToken cancellationToken = default)
    {
        RequireWrites();
        if (string.IsNullOrWhiteSpace(exchangeId) && string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Order identity missing.");
        var request = new CancelOrderRequest { AccountId = _accountId };
        if (!string.IsNullOrWhiteSpace(exchangeId)) request.OrderId = exchangeId;
        else
        {
            request.OrderId = clientId;
            request.OrderIdType = OrderIdType.Request;
        }
        await _client.Orders.CancelOrderAsync(request, cancellationToken: cancellationToken);
    }

    public async Task<TInvestOrderReport> ReadStateAsync(string exchangeId, string clientId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(exchangeId) && string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Order identity missing.");
        var request = new GetOrderStateRequest { AccountId = _accountId };
        if (!string.IsNullOrWhiteSpace(exchangeId)) request.OrderId = exchangeId;
        else
        {
            request.OrderId = clientId;
            request.OrderIdType = OrderIdType.Request;
        }
        var response = await _client.Orders.GetOrderStateAsync(request,
            cancellationToken: cancellationToken);
        return new TInvestOrderReport(clientId, response.OrderId,
            Map(response.ExecutionReportStatus), response.LotsExecuted);
    }

    public async Task<IReadOnlyList<TInvestOrderReport>> ReadActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Orders.GetOrdersAsync(
            new GetOrdersRequest { AccountId = _accountId }, cancellationToken: cancellationToken);
        return response.Orders.Where(x => x.InstrumentUid == _instrumentUid)
            .Select(x => new TInvestOrderReport(x.OrderRequestId, x.OrderId,
                Map(x.ExecutionReportStatus), x.LotsExecuted)).ToArray();
    }

    public async Task<TInvestBuyingPower> ReadBuyingPowerAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Orders.GetMaxLotsAsync(new GetMaxLotsRequest
        {
            AccountId = _accountId, InstrumentId = _instrumentUid
        }, cancellationToken: cancellationToken);
        return new TInvestBuyingPower(
            response.BuyLimits?.BuyMaxLots ?? 0,
            response.SellLimits?.SellMaxLots ?? 0);
    }

    public static ManagedOrderState Map(OrderExecutionReportStatus status) => status switch
    {
        OrderExecutionReportStatus.ExecutionReportStatusFill => ManagedOrderState.Done,
        OrderExecutionReportStatus.ExecutionReportStatusCancelled => ManagedOrderState.Cancelled,
        OrderExecutionReportStatus.ExecutionReportStatusRejected => ManagedOrderState.Rejected,
        OrderExecutionReportStatus.ExecutionReportStatusNew => ManagedOrderState.Working,
        OrderExecutionReportStatus.ExecutionReportStatusPartiallyfill => ManagedOrderState.Partial,
        _ => ManagedOrderState.Unknown
    };

    public static Quotation FromDecimal(decimal price)
    {
        long units = (long)decimal.Truncate(price);
        int nano = (int)decimal.Round((price - units) * 1_000_000_000m, 0,
            MidpointRounding.AwayFromZero);
        return new Quotation { Units = units, Nano = nano };
    }
}
