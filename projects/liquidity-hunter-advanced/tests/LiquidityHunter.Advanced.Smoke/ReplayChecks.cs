// Independent smoke assertions invoked by a separate top-level entry point only if copied to another project.
// This file is intentionally a static class to avoid multiple top-level programs.
using LiquidityHunter.Advanced;
internal static class ReplayChecks
{
    public static void Run()
    {
        var now=DateTimeOffset.UtcNow;
        var book=new DepthFrame("X",now,
            new[]{new DepthLevel(99m,100m)},new[]{new DepthLevel(101m,20m)},
            1m,1m,10m,0.8m,0.8m,0.8m,0.1m,0.7m);
        var q=new ReplayQuote("X",now,100m,5,20m,now.AddSeconds(30));
        var touch=book with {Time=now.AddSeconds(1),Asks=new[]{new DepthLevel(100m,10m)}};
        var cross=book with {Time=now.AddSeconds(2),Asks=new[]{new DepthLevel(99m,100m)},
            Bids=new[]{new DepthLevel(98m,100m)}};
        if(DepthReplay.SimulateBuy(q,new[]{touch},FillAssumption.Pessimistic)!=null)
            throw new Exception("Unjustified conservative fill");
        if(DepthReplay.SimulateBuy(q,new[]{touch},FillAssumption.Optimistic)==null)
            throw new Exception("Touch fill should exist only optimistic");
        if(DepthReplay.SimulateBuy(q,new[]{touch},FillAssumption.Midpoint)!=null)
            throw new Exception("Queue volume insufficient");
        if(DepthReplay.SimulateBuy(q,new[]{touch,cross},FillAssumption.Pessimistic)==null)
            throw new Exception("Deep trade-through expected");
        Console.WriteLine("PASS: replay fill assumptions");
    }
}
