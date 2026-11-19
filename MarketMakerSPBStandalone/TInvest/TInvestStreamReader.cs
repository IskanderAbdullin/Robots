using Grpc.Core;
using MarketMakerSPBStandalone.Core;
using Tinkoff.InvestApi;
using Tinkoff.InvestApi.V1;

namespace MarketMakerSPBStandalone.TInvest;

public sealed record TInvestMarketEvent(Book? Depth, bool? LimitOrdersAllowed,
    DateTimeOffset ReceivedAt, bool StreamConnected,
    decimal MinPrice = 0, decimal MaxPrice = 0);
/// <summary>
/// Two independent read-only gRPC streams. Reconnects do not imply authorization to trade;
/// the owner must separately audit all broker orders and refresh capacity snapshots.
/// </summary>
public sealed class TInvestStreamReader
{
    private readonly InvestApiClient _client;
    private readonly string _instrumentUid;
    private readonly string _account;
    private readonly int _depth;

    public TInvestStreamReader(InvestApiClient client, string instrumentUid,
        string accountId, int depth = 20)
    {
        if (string.IsNullOrWhiteSpace(instrumentUid) || string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("UID and account are mandatory.");
        if (depth < 1 || depth > 50) throw new ArgumentOutOfRangeException(nameof(depth));
        _client = client;
        _instrumentUid = instrumentUid;
        _account = accountId;
        _depth = depth;
    }

    public async Task MarketLoopAsync(Action<TInvestMarketEvent> onEvent, CancellationToken stop)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var stream = _client.MarketDataStream.MarketDataStream(cancellationToken: stop);
                await stream.RequestStream.WriteAsync(new MarketDataRequest
                {
                    SubscribeOrderBookRequest = new SubscribeOrderBookRequest
                    {
                        SubscriptionAction = SubscriptionAction.Subscribe,
                        Instruments =
                        {
                            new OrderBookInstrument
                            {
                                InstrumentId = _instrumentUid, Depth = _depth,
                                OrderBookType = OrderBookType.Exchange
                            }
                        }
                    }
                });
                await stream.RequestStream.WriteAsync(new MarketDataRequest
                {
                    SubscribeInfoRequest = new SubscribeInfoRequest
                    {
                        SubscriptionAction = SubscriptionAction.Subscribe,
                        Instruments = { new InfoInstrument { InstrumentId = _instrumentUid } }
                    }
                });
                failures = 0;
                await foreach (var message in stream.ResponseStream.ReadAllAsync(stop))
                {
                    var now = DateTimeOffset.UtcNow;
                    switch (message.PayloadCase)
                    {
                        case MarketDataResponse.PayloadOneofCase.Orderbook:
                            var ob = message.Orderbook;
                            if (ob.InstrumentUid != _instrumentUid || !ob.IsConsistent) continue;
                            var book = new Book(
                                ob.Bids.Select(x => new Level(TInvestDataClient.ToDecimal(x.Price), x.Quantity)).ToArray(),
                                ob.Asks.Select(x => new Level(TInvestDataClient.ToDecimal(x.Price), x.Quantity)).ToArray());
                            onEvent(new TInvestMarketEvent(book, null, now, true,
                                TInvestDataClient.ToDecimal(ob.LimitDown),
                                TInvestDataClient.ToDecimal(ob.LimitUp)));
                            break;
                        case MarketDataResponse.PayloadOneofCase.TradingStatus:
                            var status = message.TradingStatus;
                            if (status.InstrumentUid != _instrumentUid) continue;
                            onEvent(new TInvestMarketEvent(null, status.LimitOrderAvailableFlag, now, true));
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (RpcException) when (!stop.IsCancellationRequested) { }
            finally { onEvent(new TInvestMarketEvent(null, false, DateTimeOffset.UtcNow, false)); }
            failures++;
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 1 << Math.Min(failures, 5))), stop);
        }
    }

    public async Task OrderLoopAsync(Action<TInvestOrderReport> onOrder,
        Action<bool> onConnection, CancellationToken stop)
    {
        int failures = 0;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var request = new OrderStateStreamRequest { PingDelayMillis = 15000 };
                request.Accounts.Add(_account);
                using var stream = _client.OrdersStream.OrderStateStream(request,
                    cancellationToken: stop);
                failures = 0;
                bool announced = false;
                await foreach (var message in stream.ResponseStream.ReadAllAsync(stop))
                {
                    if (!announced)
                    {
                        // Every reconnect invalidates previous account/order snapshots.
                        announced = true;
                        onConnection(true);
                    }
                    if (message.PayloadCase != OrderStateStreamResponse.PayloadOneofCase.OrderState)
                        continue;
                    var state = message.OrderState;
                    if (!state.HasOrderRequestId || string.IsNullOrWhiteSpace(state.OrderRequestId))
                        continue; // not an API order created by this robot
                    var mapped = TInvestOrdersClient.Map(state.ExecutionReportStatus);
                    if (mapped == ManagedOrderState.Partial && state.CompletionTime != null)
                        mapped = ManagedOrderState.Cancelled;
                    onOrder(new TInvestOrderReport(state.OrderRequestId, state.OrderId,
                        mapped, state.LotsExecuted));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (RpcException) when (!stop.IsCancellationRequested) { }
            finally { onConnection(false); }
            failures++;
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 1 << Math.Min(failures, 5))), stop);
        }
    }
}
