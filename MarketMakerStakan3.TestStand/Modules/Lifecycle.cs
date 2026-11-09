using OsEngine.Entity;
using OsEngine.OsTrader.Panels.Tab;
namespace OfflineStand;
public static partial class Cases
{
    static void Lifecycle()
    {
        Add("lifecycle","Default settings use GTC with manual support disabled",c=>{var f=c.New();Assert.That(f.Tab.ManualPositionSupport.Disabled,"manual support enabled");Assert.That(f.Tab.ManualPositionSupport.Saved,"settings not saved");Assert.Equal(OrderTypeTime.GTC,f.Tab.ManualPositionSupport.OrderTypeTime);});
        Add("lifecycle","Strategy identity and settings dialog",c=>{var f=c.New();Assert.Equal("MarketMakerStakan3",f.Robot.GetNameStrategyType());f.Robot.ShowIndividualSettingsDialog();});
        Add("lifecycle","Off from startup sends no request",c=>{var f=c.New(100);f.Off();f.Tick();Assert.Equal(0,f.Count);});
        Add("lifecycle","Optimizer sends no requests",c=>{var f=c.New(100,StartProgram.IsOsOptimizer);f.Tick();Assert.Equal(0,f.Count);});
        Add("lifecycle","Delete unsubscribes handlers and stops requests",c=>{var f=c.New();f.Tick();var order=f.First;int count=f.Count;f.Dispose();f.Tab.Depth(Fixture.Book(99,102));f.Tab.Update(order);f.Timer();Assert.Equal(count,f.Count);Assert.That((bool)Reflect.Get(f.Robot,"_deleted"),"deleted flag absent");});
        Add("lifecycle","Delete is idempotent",c=>{var f=c.New();f.Dispose();f.Dispose();Assert.Equal(0,f.Tab.DepthSubscribers);Assert.Equal(0,f.Tab.OrderSubscribers);});
        Add("lifecycle","Off waits for pending place before cancellation",c=>{var f=c.New();f.Tick();f.Off();f.Tick();Assert.Equal(1,f.Count);f.Ack(f.First,OrderStateType.Active);f.Tick();Assert.Equal("Cancel",f.Last.Kind);Assert.Equal(2,f.Count);});
        Add("lifecycle","Off timer cancels acknowledged place without depth",c=>{var f=c.New();f.Tick();f.Off();f.Ack(f.First,OrderStateType.Active);f.Live();f.Timer();Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);});
        Add("lifecycle","Off drains multiple orders one acknowledgment at a time",c=>{var f=c.New();f.Decimal("_buyTotalVolume",150);var d=Fixture.Book();f.Tick(d);var a=f.First;f.Ack(a,OrderStateType.Active);d.Bids[0].Bid=200;f.Tick(d);var b=f.LastOrder;f.Ack(b,OrderStateType.Active);Assert.Equal(2,f.Count);f.Off();f.Tick();Assert.Equal(3,f.Count);f.Tick();Assert.Equal(3,f.Count);f.Ack(a,OrderStateType.Cancel);f.Tick();Assert.Equal(4,f.Count);Assert.Equal(b.NumberUser,f.Last.NumberUser);});
        Add("lifecycle","Restored live order blocks On and is not adopted",c=>{var t=Restored(OrderStateType.Active);var f=c.New(tab:t);f.Tick();Assert.Equal(0,f.Count);Assert.That(f.Error,"restored live state not blocked");Assert.Equal(0,((Dictionary<int,Order>)Reflect.Get(f.Robot,"_ownOrders")).Count);});
        Add("lifecycle","Off leaves restored orders untouched",c=>{var f=c.New(tab:Restored(OrderStateType.Active));f.Off();f.Tick();Assert.Equal(0,f.Count);});
        Add("lifecycle","Restored terminal order permits clean new session",c=>{var f=c.New(tab:Restored(OrderStateType.Cancel));f.Tick();Assert.Equal(1,f.Count);Assert.Equal("Place",f.Last.Kind);});
        Add("lifecycle","On timing message emitted once",c=>{var f=c.New();f.Tick();f.Tick();Assert.Equal(1,f.Robot.Logs.Count(x=>x.Contains("Робот включён")));Assert.Equal(1,f.Robot.Logs.Count(x=>x.Contains("Портфель получен")));});
    }
    static BotTabSimple Restored(OrderStateType state)
    {
        var tab=new BotTabSimple();tab.PositionsAll.Add(new(){Number=77,OpenOrders=new(){new(){NumberUser=77,Side=Side.Buy,Price=99,Volume=10,State=state}}});return tab;
    }
}
