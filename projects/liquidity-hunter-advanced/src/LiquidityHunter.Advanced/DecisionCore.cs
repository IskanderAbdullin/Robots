namespace LiquidityHunter.Advanced;
public sealed record DepthLevel(decimal Price, decimal Lots);
public sealed record DepthFrame(string Symbol, DateTimeOffset Time,
    IReadOnlyList<DepthLevel> Bids, IReadOnlyList<DepthLevel> Asks,
    decimal TickSize, decimal LotSize, decimal VolatilityBps,
    decimal SellFlowShare, decimal DepthPersistence,
    decimal ReboundProbability, decimal ReboundUncertainty, decimal FillProbability)
{
    public bool Valid(DateTimeOffset now, TimeSpan ttl)
    {
        if(string.IsNullOrWhiteSpace(Symbol)||Time>now||now-Time>ttl||TickSize<=0||
            LotSize<=0||Bids.Count==0||Asks.Count==0||Bids[0].Price>=Asks[0].Price)return false;
        if(Bids.Any(x=>x.Price<=0||x.Lots<=0)||Asks.Any(x=>x.Price<=0||x.Lots<=0))return false;
        for(int i=1;i<Bids.Count;i++)if(Bids[i].Price>=Bids[i-1].Price)return false;
        for(int i=1;i<Asks.Count;i++)if(Asks[i].Price<=Asks[i-1].Price)return false;
        return new[]{SellFlowShare,DepthPersistence,ReboundProbability,
            ReboundUncertainty,FillProbability}.All(x=>x>=0&&x<=1)&&VolatilityBps>=0;
    }
}
public sealed record SearchSettings(int MaxPriceTicks,int ExitTicks,int AdverseTicks,
    decimal FeeBps,decimal SlippageBps,decimal MinSpreadBps,decimal MaxVolatilityBps,
    decimal MinPersistence,decimal MinSellFlow,decimal RiskPenalty,
    decimal UncertaintyMultiplier,decimal CapitalCostBps)
{
    public static SearchSettings Demo=>new(5,4,10,8,10,10,100,0.4m,0.55m,0.25m,1.5m,3m);
}
public sealed record Decision(string Symbol,decimal Entry,decimal Exit,decimal ScorePerLot,
    decimal FillProbability,decimal ConservativeReboundProbability,
    decimal WorstCaseLossPerLot,decimal NotionalPerLot,int TickOffset);
public static class LevelOptimizer
{
    public static Decision? Best(DepthFrame f,SearchSettings s,DateTimeOffset now,TimeSpan ttl)
    {
        if(!f.Valid(now,ttl)||s.MaxPriceTicks<0||s.ExitTicks<1||s.AdverseTicks<1||
            s.FeeBps<0||s.SlippageBps<0||s.RiskPenalty<0||
            s.UncertaintyMultiplier<0||s.CapitalCostBps<0)return null;
        decimal mid=(f.Bids[0].Price+f.Asks[0].Price)/2m;
        decimal spread=10000m*(f.Asks[0].Price-f.Bids[0].Price)/mid;
        if(spread<s.MinSpreadBps||f.VolatilityBps>s.MaxVolatilityBps||
            f.DepthPersistence<s.MinPersistence||f.SellFlowShare<s.MinSellFlow)return null;
        decimal lower=Math.Max(0m,f.ReboundProbability-s.UncertaintyMultiplier*f.ReboundUncertainty);
        Decision? best=null;
        for(int i=0;i<=s.MaxPriceTicks;i++)
        {
            decimal entry=f.Bids[0].Price-i*f.TickSize;
            decimal exit=entry+s.ExitTicks*f.TickSize;
            decimal adverse=entry-s.AdverseTicks*f.TickSize;
            if(entry<=0||adverse<=0)continue;
            decimal pFill=f.FillProbability/(1m+i); // Placeholder: recalibrate from queue-aware observations.
            decimal notional=entry*f.LotSize;
            decimal fee=(entry+exit)*f.LotSize*s.FeeBps/20000m;
            decimal gain=(exit-entry)*f.LotSize-fee;
            decimal loss=(entry-adverse)*f.LotSize+fee+notional*s.SlippageBps/10000m;
            decimal score=pFill*(lower*gain-(1m-lower)*loss-s.RiskPenalty*loss)
                -notional*s.CapitalCostBps/10000m;
            if(score>0&&(best==null||score>best.ScorePerLot))
                best=new(f.Symbol,entry,exit,score,pFill,lower,loss,notional,i);
        }
        return best;
    }
}
public sealed record Allocation(string Symbol,decimal Price,int Lots,decimal Committed,decimal Score);
public sealed record BudgetSettings(decimal Total,decimal MaxSymbolShare,int MaxSymbols,decimal BufferShare);
public static class CapitalOptimizer
{
    public static IReadOnlyList<Allocation> Allocate(IEnumerable<Decision> opportunities,BudgetSettings b)
    {
        if(b.Total<=0||b.MaxSymbolShare<=0||b.MaxSymbolShare>1||b.BufferShare<0||
            b.BufferShare>=1||b.MaxSymbols<0)throw new ArgumentOutOfRangeException(nameof(b));
        decimal free=b.Total*(1m-b.BufferShare),perSymbol=b.Total*b.MaxSymbolShare;
        var items=new List<Allocation>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        foreach(var d in opportunities.Where(x=>x.NotionalPerLot>0&&x.ScorePerLot>0)
                     .OrderByDescending(x=>x.ScorePerLot/x.NotionalPerLot))
        {
            if(items.Count>=b.MaxSymbols)break;
            if(!seen.Add(d.Symbol))continue;
            int lots=(int)Math.Min(int.MaxValue,decimal.Floor(Math.Min(free,perSymbol)/d.NotionalPerLot));
            if(lots<=0)continue;
            decimal amount=lots*d.NotionalPerLot;
            items.Add(new(d.Symbol,d.Entry,lots,amount,d.ScorePerLot));
            free-=amount;
        }
        return items;
    }
}
