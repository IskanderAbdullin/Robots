using LiquidityHunter.Advanced;
static void Require(bool b,string reason){if(!b)throw new Exception(reason);}
var now=DateTimeOffset.UtcNow;
var bids=new[]{new DepthLevel(99m,100m),new DepthLevel(98m,50m)};
var asks=new[]{new DepthLevel(101m,100m),new DepthLevel(102m,50m)};
var f=new DepthFrame("TEST",now,bids,asks,1m,1m,20m,0.7m,0.9m,0.95m,0.01m,0.5m);
Require(f.Valid(now,TimeSpan.FromSeconds(2)),"valid");
Require(!(f with {Time=now.AddSeconds(-10)}).Valid(now,TimeSpan.FromSeconds(2)),"stale");
var s=SearchSettings.Demo with {FeeBps=0m,SlippageBps=0m,RiskPenalty=0m,
    UncertaintyMultiplier=0m,CapitalCostBps=0m,ExitTicks=10,AdverseTicks=2};
var c=LevelOptimizer.Best(f,s,now,TimeSpan.FromSeconds(2));
Require(c!=null&&c.ScorePerLot>0m,"decision");
Require(LevelOptimizer.Best(f with {DepthPersistence=0.1m},s,now,TimeSpan.FromSeconds(2))==null,"gate");
var allocated=CapitalOptimizer.Allocate(new[]{c!,c! with {Symbol="OTHER"}},
    new BudgetSettings(1000m,0.2m,2,0.1m));
Require(allocated.Count==2&&allocated.Sum(x=>x.Committed)<=900m,"budget");
Require(allocated.All(x=>x.Committed<=200m),"symbol cap");
Console.WriteLine("PASS: advanced gating, price search, budget caps");
