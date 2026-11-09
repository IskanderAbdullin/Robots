using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Properties()
    {
        foreach(var mode in new[]{Fixture.Distributed,Fixture.Single,Fixture.Specified,Fixture.Wall})
        {
            var selected=mode;Add("property","Seeded event trace invariants: "+selected,c=>RunTrace(c,selected));
        }
        Add("property","Seeded planner budgets and volume grids",c=>
        {
            var random=new Random(c.Seed^0x661120);var f=c.New();
            for(int i=0;i<c.Iterations;i++)
            {
                c.PropertyEventCount++;decimal budget=random.Next(0,1000)/4m;decimal step=new[]{1m,.5m,.25m}[random.Next(3)];
                f.Tab.Security.VolumeStep=step;f.Tab.Security.DecimalsVolume=2;f.Decimal("_buyTotalVolume",budget);f.Decimal("_levelVolumePercent",random.Next(1,200));
                var d=Fixture.Book();foreach(var level in d.Bids)level.Bid=random.Next(0,500)/4d;
                var p=f.Plan(d);Assert.That(p.All(x=>x.Volume>0&&x.Price>0&&x.Volume%step==0),"invalid grid or nonpositive plan");Assert.That(p.Sum(x=>x.Volume)<=budget,"plan exceeds configured quantity");
            }
        });
        Add("property","Seeded duplicate snapshots preserve canonical plan",c=>
        {
            var random=new Random(c.Seed^0x200671);var f=c.New();f.String("_buyMode",Fixture.Single);
            for(int i=0;i<c.Iterations;i++)
            {
                c.PropertyEventCount++;var d=Fixture.Book(random.Next(100,10000),20000);foreach(var l in d.Bids)l.Bid=random.Next(1,5000);
                var a=f.Plan(d);var b=f.Plan(d);Assert.That(a.SequenceEqual(b),"unchanged external input changed plan");
            }
        });
    }
    static void RunTrace(TestContext c,string mode)
    {
        int modeKey=Array.IndexOf(new[]{Fixture.Distributed,Fixture.Single,Fixture.Specified,Fixture.Wall},mode);
        var random=new Random(unchecked(c.Seed+modeKey*100003));var f=c.New(100);f.AllowSafetyCancelAfterError=true;f.String("_buyMode",mode);f.Decimal("_specifiedOrderVolume",50);f.Decimal("_wallMinVolume",100);f.Decimal("_wallMedianFactor",2);f.Int("_confirmTicks",2);
        int errorCommands=-1;
        for(int i=0;i<c.Iterations;i++)
        {
            c.PropertyEventCount++;int action=random.Next(10);
            if(f.Error && errorCommands<0)errorCommands=f.Count;
            switch(action)
            {
                case 0: f.Off();break;
                case 1: f.On();break;
                case 2: f.Advance(TimeSpan.FromSeconds(random.Next(0,4)==0?61:1));break;
                case 3:
                case 4:
                case 5:
                    var orders=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders??new()).Where(o=>!Fixture.Terminal(o.State)).ToArray();
                    if(orders.Length==0)break;var order=orders[random.Next(orders.Length)];
                    if(f.Outstanding?.Kind=="Cancel" && action==3)f.Ack(order,OrderStateType.Cancel,order.VolumeExecute);
                    else if(action==4 && order.State is OrderStateType.Active or OrderStateType.Partial)
                    {
                        decimal fill=Math.Min(order.Volume,order.VolumeExecute+1);f.Ack(order,fill==order.Volume?OrderStateType.Done:OrderStateType.Partial,fill);
                    }
                    else f.Ack(order,OrderStateType.Active,order.VolumeExecute);
                    break;
                case 6:
                    f.Tab.Portfolio.ValueCurrent=100000+random.Next(1000);f.Stock.ValueBlocked=random.Next(101);f.PortfolioEvent();break;
                default:
                    var d=Fixture.Book(random.Next(98,103),106,random.Next(20,200));
                    foreach(var live in f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders??new()).Where(o=>!Fixture.Terminal(o.State)))
                    {
                        var level=(live.Side==Side.Buy?d.Bids:d.Asks).FirstOrDefault(l=>(decimal)l.Price==live.Price);
                        if(level!=null){if(live.Side==Side.Buy)level.Bid+=(double)Math.Max(0,live.Volume-live.VolumeExecute);else level.Ask+=(double)Math.Max(0,live.Volume-live.VolumeExecute);}
                    }
                    f.Tick(d);if(random.Next(3)==0){c.PropertyEventCount++;f.Tick(d);}break;
            }
            f.Verify();
            if(errorCommands>=0)Assert.That(f.Tab.Commands.Skip(errorCommands).All(x=>x.Kind=="Cancel"),"Place or Change request continued after Error");
        }
    }
}

