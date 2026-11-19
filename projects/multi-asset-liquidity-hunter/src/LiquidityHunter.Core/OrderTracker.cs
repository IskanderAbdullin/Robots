namespace LiquidityHunter.Core;

public enum OrderPhase { Idle, SubmissionUnknown, Working, CancelUnknown, ReconcileRequired, Finished }
public sealed class OrderTracker
{
    private readonly object _gate = new();
    public OrderPhase Phase { get; private set; } = OrderPhase.Idle;
    public string? ClientOrderId { get; private set; }
    public string? BrokerOrderId { get; private set; }
    public int OrderedLots { get; private set; }
    public int ConfirmedFilledLots { get; private set; }
    public int RemainingLots { get { lock (_gate) return OrderedLots - ConfirmedFilledLots; } }

    public void BeginSubmit(string clientId, int lots)
    {
        if (string.IsNullOrWhiteSpace(clientId) || lots <= 0) throw new ArgumentException("Bad order");
        lock (_gate)
        {
            if (Phase is not (OrderPhase.Idle or OrderPhase.Finished))
                throw new InvalidOperationException("Unresolved previous order");
            ClientOrderId = clientId; BrokerOrderId = null;
            OrderedLots = lots; ConfirmedFilledLots = 0; Phase = OrderPhase.SubmissionUnknown;
        }
    }
    public void Acknowledge(string clientId, string brokerId)
    {
        lock (_gate)
        {
            if (Phase != OrderPhase.SubmissionUnknown || clientId != ClientOrderId ||
                string.IsNullOrWhiteSpace(brokerId)) throw new InvalidOperationException("Unexpected acknowledgement");
            BrokerOrderId = brokerId; Phase = OrderPhase.Working;
        }
    }
    public void BeginCancel()
    {
        lock (_gate)
        {
            if (Phase != OrderPhase.Working) throw new InvalidOperationException("Order not working");
            Phase = OrderPhase.CancelUnknown;
        }
    }
    public void ReportFill(int cumulativeLots)
    {
        lock (_gate)
        {
            if (Phase is OrderPhase.Idle or OrderPhase.Finished || cumulativeLots < ConfirmedFilledLots
                || cumulativeLots > OrderedLots)
                throw new InvalidOperationException("Out-of-order or excessive fill");
            ConfirmedFilledLots = cumulativeLots;
            if (cumulativeLots == OrderedLots) Phase = OrderPhase.Finished;
        }
    }
    public void MarkUnknown()
    {
        lock (_gate)
        {
            if (Phase is OrderPhase.Idle or OrderPhase.Finished)
                throw new InvalidOperationException("Nothing to reconcile");
            Phase = OrderPhase.ReconcileRequired;
        }
    }
    // This method is only safe after confirming authoritative broker order state.
    public void Reconcile(string? brokerId, int cumulativeLots, bool isActive)
    {
        lock (_gate)
        {
            if (Phase is OrderPhase.Idle or OrderPhase.Finished)
                throw new InvalidOperationException("No outstanding order");
            if (cumulativeLots < ConfirmedFilledLots || cumulativeLots > OrderedLots)
                throw new InvalidOperationException("Bad cumulative lots");
            BrokerOrderId = brokerId;
            ConfirmedFilledLots = cumulativeLots;
            Phase = (cumulativeLots == OrderedLots || !isActive)
                ? OrderPhase.Finished : OrderPhase.Working;
        }
    }
}
