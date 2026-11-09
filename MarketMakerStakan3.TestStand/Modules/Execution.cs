using OsEngine.Entity;
using OsEngine.OsTrader.Panels.Tab;
namespace OfflineStand;
public static partial class Cases
{
    static void Execution()
    {
        Add("execution","One command globally across buy and sell",c=>{var f=c.New(100);f.Tick();Assert.Equal(1,f.Count);f.Tick(Fixture.Book(101,103));Assert.Equal(1,f.Count);});
        Add("execution","Repeated depth cannot duplicate pending place",c=>{var f=c.New();for(int i=0;i<50;i++)f.Tick();Assert.Equal(1,f.Count);});
        Add("execution","Place Active acknowledgment releases operation",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);Assert.That(f.Operation==null,"place still pending");});
        Add("execution","Place Partial acknowledgment releases operation",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Partial,2);Assert.That(f.Operation==null,"partial did not acknowledge place");Assert.Equal(2m,f.Registry(f.First.NumberUser).VolumeExecute);});
        Add("execution","Cancel Active update does not release cancellation",c=>{var f=CancelPending(c);f.Ack(f.First,OrderStateType.Active);f.Tick();Assert.Equal(2,f.Count);Assert.That(f.Operation!=null,"active wrongly acknowledged cancellation");});
        Add("execution","Cancel terminal acknowledgment releases operation",c=>{var f=CancelPending(c);f.Ack(f.First,OrderStateType.Cancel);Assert.That(f.Operation==null,"cancel pending after terminal");});
        Add("execution","Partial during cancellation keeps original request pending",c=>{var f=CancelPending(c);f.Ack(f.First,OrderStateType.Partial,3);f.Tick();Assert.Equal(2,f.Count);Assert.Equal(3m,f.Registry(f.First.NumberUser).VolumeExecute);});
        Add("execution","Place timeout fails closed without retry",c=>{var f=c.New();f.Tick();f.Advance(TimeSpan.FromSeconds(61));f.Tick();Assert.That(f.Error,"timeout did not set Error");Assert.Equal(1,f.Count);f.Ack(f.First,OrderStateType.Active);f.Tick();Assert.Equal(1,f.Count);});
        Add("execution","Cancel timeout fails closed without retry",c=>{var f=CancelPending(c);f.Advance(TimeSpan.FromSeconds(61));f.Tick();Assert.That(f.Error,"cancel timeout not Error");Assert.Equal(2,f.Count);});
        Add("execution","Timeout detected by timer with no depth",c=>{var f=c.New();f.Tick();f.Live();f.Advance(TimeSpan.FromSeconds(61));f.Timer();Assert.That(f.Error,"timer missed timeout");Assert.Equal(1,f.Count);});
        Add("execution","No timeout before deadline",c=>{var f=c.New();f.Tick();f.Advance(TimeSpan.FromSeconds(30));f.Tick();Assert.That(!f.Error,"early timeout");Assert.Equal(1,f.Count);});
        Add("execution","Exchange Fail permanently blocks automatic requests",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Fail);f.Tick();Assert.That(f.Error,"Fail not error");Assert.Equal(1,f.Count);f.Off();f.On();f.Tick();Assert.Equal(1,f.Count);});
        Add("execution","Place exception fails closed",c=>{var f=c.New();f.Tab.PlaceException=new IOException("synthetic place failure");f.Tick();f.Tick();Assert.That(f.Error,"exception not Error");Assert.Equal(1,f.Count);});
        Add("execution","Cancel exception fails closed",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);f.Tab.CancelException=new IOException("synthetic cancel failure");f.Off();f.Tick();f.Tick();Assert.That(f.Error,"exception not Error");Assert.Equal(2,f.Count);});
        foreach(var response in new[]{PlaceResponse.Null,PlaceResponse.NoOrders,PlaceResponse.MultipleOrders})
        {
            var captured=response;Add("execution","Ambiguous place response "+captured,c=>{var f=c.New();f.Tab.Response=captured;f.Tick();Assert.That(f.Error,"ambiguous response accepted");f.Tick();Assert.Equal(1,f.Count);});
        }
        Add("execution","Synchronous ack mismatched returned identity fails closed",c=>{var f=c.New();f.Tab.Response=PlaceResponse.MismatchedIdentity;f.Tab.OnPlace=o=>f.Ack(o,OrderStateType.Active);f.Tick();Assert.That(f.Error,"identity mismatch accepted");Assert.Equal(1,f.Count);});
        Add("execution","Unsupported server uses cancel instead of reprice",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live();f.Tick(Fixture.Book(99,102));Assert.Equal("Cancel",f.Last.Kind);});
        Add("execution","Supported reprice waits for exact canonical target",c=>{var f=ChangePending(c);var o=f.First;f.Ack(o,OrderStateType.Active);f.Tick(Fixture.Book(98,102));Assert.Equal(2,f.Count);f.Ack(o,OrderStateType.Active,price:99);Assert.That(f.Operation==null,"target did not acknowledge change");});
        Add("execution","Reprice timeout does not issue a second price command",c=>{var f=ChangePending(c);f.Advance(TimeSpan.FromSeconds(61));f.Tick();Assert.That(f.Error,"change timeout not Error");Assert.Equal(2,f.Count);});
        Add("execution","Reprice exception fails closed",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live();f.Tab.ServerIsSupportChangeOrderPrice=true;f.Tab.ChangeException=new IOException("synthetic change failure");f.Tick(Fixture.Book(99,102));Assert.That(f.Error,"change exception not Error");Assert.Equal(2,f.Count);});
    }
    static Fixture CancelPending(TestContext c)
    {
        var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);f.Off();f.Tick();Assert.Equal("Cancel",f.Last.Kind);return f;
    }
    static Fixture ChangePending(TestContext c)
    {
        var f=c.New();f.String("_buyMode",Fixture.Single);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live();f.Tab.ServerIsSupportChangeOrderPrice=true;f.Tick(Fixture.Book(99,102));Assert.Equal("Change",f.Last.Kind);return f;
    }
}
