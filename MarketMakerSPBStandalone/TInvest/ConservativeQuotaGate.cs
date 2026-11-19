namespace MarketMakerSPBStandalone.TInvest;

/// <summary>
/// Sliding-window process-local request guard. It is intentionally more conservative
/// than the published limits; dynamic broker response headers must still be honored
/// by production monitoring. A broker error never automatically increases the budget.
/// </summary>
public sealed class ConservativeQuotaGate
{
    private readonly int _maxPerMinute;
    private readonly int _maxPerSecond;
    private readonly Queue<DateTimeOffset> _minute = new();
    private readonly Queue<DateTimeOffset> _second = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ConservativeQuotaGate(int maxPerMinute, int maxPerSecond)
    {
        if (maxPerMinute <= 0 || maxPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPerMinute));
        _maxPerMinute = maxPerMinute;
        _maxPerSecond = maxPerSecond;
    }

    public async Task EnterAsync(CancellationToken token = default)
    {
        while (true)
        {
            await _lock.WaitAsync(token);
            TimeSpan wait;
            try
            {
                var now = DateTimeOffset.UtcNow;
                while (_minute.Count > 0 && now - _minute.Peek() >= TimeSpan.FromMinutes(1))
                    _minute.Dequeue();
                while (_second.Count > 0 && now - _second.Peek() >= TimeSpan.FromSeconds(1))
                    _second.Dequeue();
                if (_minute.Count < _maxPerMinute && _second.Count < _maxPerSecond)
                {
                    _minute.Enqueue(now);
                    _second.Enqueue(now);
                    return;
                }
                var minuteWait = _minute.Count >= _maxPerMinute
                    ? _minute.Peek().AddMinutes(1) - now : TimeSpan.Zero;
                var secondWait = _second.Count >= _maxPerSecond
                    ? _second.Peek().AddSeconds(1) - now : TimeSpan.Zero;
                wait = minuteWait > secondWait ? minuteWait : secondWait;
                if (wait < TimeSpan.FromMilliseconds(1)) wait = TimeSpan.FromMilliseconds(1);
            }
            finally { _lock.Release(); }
            await Task.Delay(wait, token);
        }
    }
}
