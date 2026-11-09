using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using OsEngine.Entity;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        string engine = typeof(Order).Assembly.Location;
        Console.WriteLine("OsEngine entity contracts; no BotPanel instance or broker connection");
        Console.WriteLine("Loaded DLL: " + engine);
        Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(engine))));
        foreach (AssemblyMetadataAttribute metadata in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (metadata.Key == "EngineBin") Console.WriteLine("Configured EngineBin: " + metadata.Value);
        }

        Test("MarketDepth.GetCopy isolates price and quantity", () =>
        {
            MarketDepth depth = Depth();
            MarketDepth copy = depth.GetCopy();
            copy.Bids[0].Price = 90;
            copy.Asks[0].Ask = 999;
            Require(depth.Bids[0].Price == 100 && depth.Asks[0].Ask == 20, "Depth copy aliases levels");
        });
        Test("MarketDepth.GetCopy isolates collection membership", () =>
        {
            MarketDepth depth = Depth();
            MarketDepth copy = depth.GetCopy();
            copy.Bids.Clear();
            Require(depth.Bids.Count == 1, "Depth copy aliases list");
        });
        Test("Order partial execution exposes remaining volume", () =>
        {
            Order order = Order();
            order.VolumeExecute = 3;
            order.State = OrderStateType.Partial;
            Require(order.Volume - order.VolumeExecute == 7, "Unexpected partial remainder");
        });
        Test("Done without trades exposes full execution", () =>
        {
            Order order = Order();
            order.State = OrderStateType.Done;
            Require(order.VolumeExecute == 10, "Done must reserve zero remainder");
        });
        Test("Order.SetTrade ignores wrong parent", () =>
        {
            Order order = Order();
            MyTrade trade = Trade("t1", 3);
            trade.NumberOrderParent = "other";
            order.SetTrade(trade);
            Require(order.VolumeExecute == 0, "Foreign trade counted");
        });
        Test("Order.SetTrade deduplicates NumberTrade", () =>
        {
            Order order = Order();
            order.SetTrade(Trade("t1", 3));
            order.SetTrade(Trade("t1", 3));
            Require(order.VolumeExecute == 3 && order.MyTrades.Count == 1, "Duplicate counted twice");
        });
        Test("Order.SetTrade aggregates distinct executions", () =>
        {
            Order order = Order();
            order.SetTrade(Trade("t1", 3));
            order.SetTrade(Trade("t2", 2));
            Require(order.VolumeExecute == 5, "Distinct fills lost");
        });
        Test("Order.SetTrade marks complete execution Done", () =>
        {
            Order order = Order();
            order.SetTrade(Trade("t1", 3));
            order.SetTrade(Trade("t2", 7));
            Require(order.State == OrderStateType.Done && order.VolumeExecute == 10, "Full fill not terminal");
        });
        Test("Order terminal state resists stale Active", () =>
        {
            Order order = Order();
            order.State = OrderStateType.Cancel;
            order.State = OrderStateType.Active;
            Require(order.State == OrderStateType.Cancel, "Stale Active resurrected cancelled order");
        });
        Test("Position.SetOrder: raw Cancel fill is not copied before MyTrade", () =>
        {
            Position position = Position();
            Order raw = Order();
            raw.VolumeExecute = 3;
            raw.State = OrderStateType.Cancel;
            position.SetOrder(raw);
            Order journal = position.OpenOrders[0];
            Console.WriteLine("  CHARACTERIZATION rawExecute=" + raw.VolumeExecute + " journalExecute=" + journal.VolumeExecute);
            Require(journal.State == OrderStateType.Cancel && journal.VolumeExecute == 0,
                "Installed journal semantics changed; reassess raw-report replay assumptions");
        });
        Test("Position.SetTrade closes journal execution gap", () =>
        {
            Position position = Position();
            Order raw = Order();
            raw.VolumeExecute = 3;
            raw.State = OrderStateType.Cancel;
            position.SetOrder(raw);
            position.SetTrade(Trade("t1", 3));
            Require(position.OpenOrders[0].VolumeExecute == 3, "MyTrade did not update journal execution");
        });
        Test("Position.SetTrade duplicate remains idempotent", () =>
        {
            Position position = Position();
            position.SetTrade(Trade("t1", 3));
            position.SetTrade(Trade("t1", 3));
            Require(position.OpenOrders[0].VolumeExecute == 3, "Position duplicate fill counted twice");
        });

        Console.WriteLine("RESULT passed=" + _passed + " failed=" + _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static void Test(string name, Action action)
    {
        try { action(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { _failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static Order Order()
    {
        return new Order { NumberUser = 123, NumberMarket = "broker123", SecurityNameCode = "TEST", Side = Side.Sell, State = OrderStateType.Active, Volume = 10, Price = 101 };
    }

    private static Position Position()
    {
        Position position = new Position { Number = 1, Direction = Side.Sell };
        position.AddNewOpenOrder(Order());
        return position;
    }

    private static MyTrade Trade(string number, decimal volume)
    {
        return new MyTrade { NumberOrderParent = "broker123", NumberTrade = number, SecurityNameCode = "TEST", Side = Side.Sell, Volume = volume, Price = 101, Time = new DateTime(2026, 11, 9, 12, 0, 0) };
    }

    private static MarketDepth Depth()
    {
        MarketDepth depth = new MarketDepth();
        depth.Bids.Add(new MarketDepthLevel { Price = 100, Bid = 10 });
        depth.Asks.Add(new MarketDepthLevel { Price = 101, Ask = 20 });
        return depth;
    }
}
