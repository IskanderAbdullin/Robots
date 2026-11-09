using OsEngine.Entity;
using OsEngine.OsTrader.Panels.Tab;
namespace OfflineStand;
public sealed class BrokerLedger
{
    public sealed class Entry
    {
        public int Number;
        public Side Side;
        public decimal Price,Volume,Filled;
        public bool Terminal,CancelRequested;
        public decimal Remaining=>Math.Max(0,Volume-Filled);
    }
    public readonly Dictionary<int,Entry> Orders=new();
    public decimal InitialStock,InitialCash,Sold,CashSpent,Lot=1;
    public int Commands,Fills,Terminals;
    public decimal MaxStockExposure,MaxCashExposure;
    public List<string> Violations=new();
    public decimal ReservedStock=>Orders.Values.Where(x=>!x.Terminal&&x.Side==Side.Sell).Sum(x=>x.Remaining);
    public decimal ReservedCash=>Orders.Values.Where(x=>!x.Terminal&&x.Side==Side.Buy).Sum(x=>x.Remaining*x.Price*Lot);
    public void Command(Command command)
    {
        Commands++;
        if(command.Kind=="Place")
        {
            if(Orders.ContainsKey(command.NumberUser))Violations.Add("duplicate exchange order identity");
            Orders[command.NumberUser]=new(){Number=command.NumberUser,Side=command.Side,Price=command.Price,Volume=command.Volume};
        }
        else if(!Orders.TryGetValue(command.NumberUser,out var order))Violations.Add("command targets unknown broker order");
        else if(order.Terminal)Violations.Add("command targets terminal broker order");
        else if(command.Kind=="Cancel")order.CancelRequested=true;
        else if(command.Kind=="Change")order.Price=command.Price;
        Validate();
    }
    public void Fill(int id,decimal quantity)
    {
        var order=Orders[id];
        if(order.Terminal)throw new InvalidOperationException("test broker filled terminal order");
        if(quantity<0||quantity>order.Remaining)throw new InvalidOperationException("test broker invalid fill");
        order.Filled+=quantity;Fills++;
        if(order.Side==Side.Sell)Sold+=quantity;else CashSpent+=quantity*order.Price*Lot;
        Validate();
    }
    public void Terminal(int id) { Orders[id].Terminal=true;Terminals++;Validate(); }
    public void Validate()
    {
        MaxStockExposure=Math.Max(MaxStockExposure,ReservedStock);MaxCashExposure=Math.Max(MaxCashExposure,ReservedCash);
        if(Sold+ReservedStock>InitialStock)Violations.Add($"stock oversubscription sold={Sold} potential={ReservedStock} initial={InitialStock}");
        if(CashSpent+ReservedCash>InitialCash)Violations.Add($"cash oversubscription spent={CashSpent} potential={ReservedCash} initial={InitialCash}");
        if(Orders.Values.Any(x=>x.Filled<0||x.Filled>x.Volume))Violations.Add("invalid executed quantity");
    }
    public void AssertSafe() { Validate();Assert.That(Violations.Count==0,string.Join("; ",Violations)); }
}
public static partial class Cases
{
    static void BrokerProperties()
    {
        foreach(var side in new[]{Side.Buy,Side.Sell})for(int profile=0;profile<6;profile++)
        {
            var s=side;int p=profile;Add("broker","Independent cash/stock ledger "+s+" profile="+p,c=>RunBroker(c,s,p));
        }
    }
    static void RunBroker(TestContext c,Side side,int profile)
    {
        var random=new Random(unchecked(c.Seed+profile*7919+(side==Side.Buy?171:981)));
        var f=c.New(side==Side.Sell?100000:0);
        var broker=new BrokerLedger{InitialCash=side==Side.Buy?10000000:0,InitialStock=side==Side.Sell?100000:0,Lot=profile%2==0?1:10};
        if(profile<2)
        {
            if(side==Side.Buy)broker.InitialCash=(profile==0?17m:53m)*100m*broker.Lot;
            else { broker.InitialStock=profile==0?17m:53m;f.Stock.ValueCurrent=broker.InitialStock; }
        }
        f.Tab.Security.Lot=broker.Lot;
        if(side==Side.Buy){f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=broker.InitialCash;}
        else f.Decimal("_buyTotalVolume",0);
        f.CommandObserver=broker.Command;
        void Portfolio()
        {
            if(side==Side.Buy)f.Money.ValueCurrent=Math.Max(0,broker.InitialCash-broker.CashSpent-broker.ReservedCash);
            else { f.Stock.ValueCurrent=Math.Max(0,broker.InitialStock-broker.Sold);f.Stock.ValueBlocked=broker.ReservedStock; }
            f.PortfolioEvent();
        }
        MarketDepth Book()
        {
            double bid=100+random.Next(-2,3);var d=Fixture.Book(bid,bid+5,20+profile*5);
            foreach(var entry in broker.Orders.Values.Where(o=>!o.Terminal))
            {
                var level=(entry.Side==Side.Buy?d.Bids:d.Asks).FirstOrDefault(x=>(decimal)x.Price==entry.Price);
                if(level!=null){if(entry.Side==Side.Buy)level.Bid+=(double)entry.Remaining;else level.Ask+=(double)entry.Remaining;}
            }
            return d;
        }
        for(int i=0;i<c.Iterations;i++)
        {
            c.PropertyEventCount++;f.Advance(TimeSpan.FromSeconds(1));Portfolio();int action=random.Next(10);
            bool hadActive=broker.Orders.Values.Any(x=>!x.Terminal);int beforeCommands=broker.Commands,beforeFills=broker.Fills,beforeTerminals=broker.Terminals;
            var pending=f.Outstanding;
            if(pending!=null)
            {
                var order=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).First(x=>x.NumberUser==pending.NumberUser);
                if(pending.Kind=="Place")
                {
                    if(action<7)f.Ack(order,OrderStateType.Active);else f.Tick(Book());
                }
                else if(pending.Kind=="Cancel")
                {
                    var entry=broker.Orders[order.NumberUser];
                    if(action<3&&entry.Remaining>1)
                    {
                        broker.Fill(entry.Number,1);Portfolio();f.Ack(order,OrderStateType.Partial,entry.Filled);
                    }
                    else if(action<8)
                    {
                        broker.Terminal(entry.Number);Portfolio();f.Ack(order,OrderStateType.Cancel,entry.Filled);
                    }
                    else f.Tick(Book());
                }
            }
            else
            {
                var active=broker.Orders.Values.Where(x=>!x.Terminal).ToArray();
                if(action<3&&active.Length>0)
                {
                    var entry=active[random.Next(active.Length)];decimal quantity=Math.Min(entry.Remaining,random.Next(1,5));broker.Fill(entry.Number,quantity);
                    var order=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).First(x=>x.NumberUser==entry.Number);
                    var state=entry.Remaining==0?OrderStateType.Done:OrderStateType.Partial;if(state==OrderStateType.Done)broker.Terminal(entry.Number);
                    Portfolio();f.Ack(order,state,entry.Filled);
                }
                else
                {
                    if(action==3)f.Off();else f.On();f.Tick(Book());
                }
            }
            broker.AssertSafe();f.Verify();
            Assert.That(!f.Error,"benign broker stream unexpectedly entered Error");
            if(hadActive||broker.Commands>beforeCommands||broker.Fills>beforeFills||broker.Terminals>beforeTerminals)c.BrokerActiveSteps++;c.BrokerCommands=broker.Commands+c.BrokerCommandsBase;c.BrokerFills=broker.Fills+c.BrokerFillsBase;
        }
        Assert.That(broker.Commands>0,"broker property did not exercise execution");
        if(c.Iterations>=100)Assert.That(broker.Fills>0&&broker.Terminals>0,"broker trace did not exercise fills/cancels");
        if(profile<2&&c.Iterations>=100)Assert.That(side==Side.Sell?broker.MaxStockExposure>=broker.InitialStock*.8m:broker.MaxCashExposure>=broker.InitialCash*.8m,"boundary profile never approached available balance");
        c.BrokerCommandsBase=c.BrokerCommands;c.BrokerFillsBase=c.BrokerFills;
    }
}

