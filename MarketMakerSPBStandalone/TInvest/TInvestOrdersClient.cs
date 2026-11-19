using MarketMakerSPBStandalone.Core;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace MarketMakerSPBStandalone.TInvest;

public sealed record TInvestFill(string TradeId, decimal Price, long Lots,
    DateTimeOffset TimeUtc);
public sealed record TInvestOrderReport(
    string ClientId, string ExchangeId, ManagedOrderState State, long FilledLots,
    IReadOnlyList<TInvestFill>? Fills = null);
public sealed record TInvestBuyingPower(long BuyLots, long SellLots);
public sealed record TInvestInventory(long FreeSellLots, long TotalHeldLots);

/// <summary>
/// T-Invest unary order API binding. NOT used by the paper application.
/// Must never treat a timeout or successful cancel HTTP/gRPC response as a terminal order report.
/// </summary>
public sealed class TInvestOrdersClient : IOrderGateway
{
    private readonly InvestApiClient _client;
    private readonly string _accountId;
    private readonly string _instrumentUid;
    private readonly bool _allowWrites;
    private readonly bool _allowMarginBuys;
    // These static gates cover all instances in this process, not just one account.
    private static readonly ConservativeQuotaGate PostGate = new(100, 8);
    private static readonly ConservativeQuotaGate CancelAndStateGate = new(70, 8);
    private static readonly ConservativeQuotaGate ReadGate = new(70, 8);
    private static readonly ConservativeQuotaGate OperationsGate = new(120, 8);

    public TInvestOrdersClient(InvestApiClient client, string accountId, string instrumentUid,
        bool allowWrites = false, bool allowMarginBuys = false)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(instrumentUid))
            throw new ArgumentException("Account and instrument must be explicit.");
        _client = client;
        _accountId = accountId;
        _instrumentUid = instrumentUid;
        _allowWrites = allowWrites;
        _allowMarginBuys = allowMarginBuys;
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
        await PostGate.EnterAsync(cancellationToken);
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
            ConfirmMarginTrade = command.Side == Side.Buy && _allowMarginBuys
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
        await CancelAndStateGate.EnterAsync(cancellationToken);
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
        await CancelAndStateGate.EnterAsync(cancellationToken);
        var response = await _client.Orders.GetOrderStateAsync(request,
            cancellationToken: cancellationToken);
        var fills = response.Stages.Select(stage => new TInvestFill(
            stage.TradeId, TInvestDataClient.ToDecimal(stage.Price), stage.Quantity,
            stage.ExecutionTime?.ToDateTimeOffset() ?? DateTimeOffset.MinValue)).ToArray();
        return new TInvestOrderReport(clientId, response.OrderId,
            Map(response.ExecutionReportStatus), response.LotsExecuted, fills);
    }

    public async Task<IReadOnlyList<TInvestOrderReport>> ReadActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await ReadGate.EnterAsync(cancellationToken);
        var response = await _client.Orders.GetOrdersAsync(
            new GetOrdersRequest { AccountId = _accountId }, cancellationToken: cancellationToken);
        return response.Orders.Where(x => x.InstrumentUid == _instrumentUid)
            .Select(x => new TInvestOrderReport(x.OrderRequestId, x.OrderId,
                Map(x.ExecutionReportStatus), x.LotsExecuted)).ToArray();
    }

    public async Task<TInvestBuyingPower> ReadBuyingPowerAsync(
        CancellationToken cancellationToken = default)
    {
        await ReadGate.EnterAsync(cancellationToken);
        var response = await _client.Orders.GetMaxLotsAsync(new GetMaxLotsRequest
        {
            AccountId = _accountId, InstrumentId = _instrumentUid
        }, cancellationToken: cancellationToken);
        return new TInvestBuyingPower(
            (_allowMarginBuys ? response.BuyMarginLimits : response.BuyLimits)?.BuyMaxLots ?? 0,
            response.SellLimits?.SellMaxLots ?? 0);
    }

    /// <summary>Cash/free securities only. Margin-selling or futures are not supported here.</summary>
    public async Task<long> ReadFreeSellLotsAsync(int lotSize,
        CancellationToken cancellationToken = default) =>
        (await ReadInventoryAsync(lotSize, cancellationToken)).FreeSellLots;

    public async Task<TInvestInventory> ReadInventoryAsync(int lotSize,
        CancellationToken cancellationToken = default)
    {
        if (lotSize <= 0) throw new ArgumentOutOfRangeException(nameof(lotSize));
        await OperationsGate.EnterAsync(cancellationToken);
        var response = await _client.Operations.GetPositionsAsync(
            new PositionsRequest { AccountId = _accountId },
            cancellationToken: cancellationToken);
        if (response.LimitsLoadingInProgress)
            throw new InvalidOperationException("Broker is recalculating position limits.");
        var matches = response.Securities.Where(x => x.InstrumentUid == _instrumentUid).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("Ambiguous securities positions in account.");
        if (matches.Length == 0) return new TInvestInventory(0, 0);
        var position = matches[0];
        long free = Math.Max(0L, position.Balance);
        long total = checked(position.Balance + position.Blocked);
        if (total < 0) throw new InvalidOperationException("Negative holdings: short position not supported.");
        return new TInvestInventory(free / lotSize, (total + lotSize - 1) / lotSize);
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
