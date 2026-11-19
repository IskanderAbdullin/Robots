namespace LiquidityHunter.Core;

public sealed class BudgetLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, decimal> _reservations = new(StringComparer.Ordinal);
    public decimal Budget { get; }
    public decimal InventoryExposure { get; private set; }
    public BudgetLedger(decimal budget)
    {
        if (budget <= 0m) throw new ArgumentOutOfRangeException(nameof(budget));
        Budget = budget;
    }
    public decimal Reserved { get { lock (_gate) return _reservations.Values.Sum(); } }
    public decimal Free { get { lock (_gate) return Budget - InventoryExposure - _reservations.Values.Sum(); } }

    public bool TryReserve(string orderKey, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(orderKey) || amount <= 0m) return false;
        lock (_gate)
        {
            if (_reservations.ContainsKey(orderKey)
                || InventoryExposure + _reservations.Values.Sum() + amount > Budget) return false;
            _reservations.Add(orderKey, amount);
            return true;
        }
    }
    // On fill, reserved amount is transferred to inventory, not released into free capital.
    public void ConfirmEntryFill(string orderKey, decimal filledNotional)
    {
        lock (_gate)
        {
            if (!_reservations.TryGetValue(orderKey, out var remaining)
                || filledNotional <= 0m || filledNotional > remaining)
                throw new InvalidOperationException("Unrecognized or excessive fill");
            var balance = remaining - filledNotional;
            if (balance == 0m) _reservations.Remove(orderKey);
            else _reservations[orderKey] = balance;
            InventoryExposure += filledNotional;
        }
    }
    public void CancelUnfilled(string orderKey)
    {
        lock (_gate) _reservations.Remove(orderKey);
    }
    public void ConfirmExit(decimal releasedNotional)
    {
        lock (_gate)
        {
            if (releasedNotional <= 0m || releasedNotional > InventoryExposure)
                throw new InvalidOperationException("Invalid release");
            InventoryExposure -= releasedNotional;
        }
    }
}
