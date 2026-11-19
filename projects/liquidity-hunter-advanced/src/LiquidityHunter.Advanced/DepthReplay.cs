namespace LiquidityHunter.Advanced;
public enum FillAssumption { Pessimistic, Midpoint, Optimistic }
public sealed record ReplayQuote(string Symbol,DateTimeOffset PlacedAt,decimal Price,int Lots,
    decimal QueueAheadLots,DateTimeOffset ExpiresAt);
public sealed record ReplayFill(string Symbol,DateTimeOffset Time,int Lots,decimal Price,
    FillAssumption Assumption);
public static class DepthReplay
{
    // Snapshot-only execution *bounds*, NOT actual market-by-order execution.
    // Pessimistic: require ask crossing BELOW order bid; optimistic: merely touch at best ask.
    // Midpoint: require best ask <= bid AND enough displayed offer volume to consume queue.
    public static ReplayFill? SimulateBuy(ReplayQuote order,IEnumerable<DepthFrame> snapshots,
        FillAssumption assumption)
    {
        if(order.Lots<1||order.Price<=0||order.QueueAheadLots<0||
            order.ExpiresAt<=order.PlacedAt)throw new ArgumentException("Invalid quote");
        foreach(var f in snapshots.OrderBy(x=>x.Time))
        {
            if(f.Symbol!=order.Symbol||f.Time<order.PlacedAt||f.Time>order.ExpiresAt||
                f.Asks.Count==0||f.Bids.Count==0)continue;
            decimal bestAsk=f.Asks[0].Price;
            bool touched=bestAsk<=order.Price;
            bool filled=assumption switch
            {
                FillAssumption.Pessimistic=>bestAsk<order.Price,
                FillAssumption.Midpoint=>touched && f.Asks[0].Lots>=order.QueueAheadLots+order.Lots,
                FillAssumption.Optimistic=>touched,
                _=>false
            };
            if(filled)return new(order.Symbol,f.Time,order.Lots,order.Price,assumption);
        }
        return null;
    }
}
