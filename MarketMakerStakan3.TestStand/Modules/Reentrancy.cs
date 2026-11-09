using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Reentrancy()
    {
        Add("reentrancy","Depth reentered inside place cannot send second request",c=>{var f=c.New(100);f.Tab.OnPlace=o=>f.Tick(Fixture.Book(101,103));f.Tick();Assert.Equal(1,f.Count);});
        Add("reentrancy","Synchronous Active ack inside place keeps exactly one command",c=>{var f=c.New(100);f.Tab.OnPlace=o=>{f.Ack(o,OrderStateType.Active);f.Tick();};f.Tick();Assert.Equal(1,f.Count);Assert.That(f.Operation==null,"sync ack lost");});
        Add("reentrancy","Synchronous Fail inside place blocks next side",c=>{var f=c.New(100);f.Tab.OnPlace=o=>f.Ack(o,OrderStateType.Fail);f.Tick();Assert.That(f.Error,"sync failure not Error");Assert.Equal(1,f.Count);});
        Add("reentrancy","Off selected inside place prevents same-event sell",c=>{var f=c.New(100);f.Tab.OnPlace=o=>{f.Off();f.Ack(o,OrderStateType.Active);};f.Tick();Assert.Equal(1,f.Count);f.Tick();Assert.Equal("Cancel",f.Last.Kind);});
        Add("reentrancy","Synchronous cancel ack cannot place replacement in same event",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);f.Tab.OnCancel=o=>f.Ack(o,OrderStateType.Cancel);f.Tick(Fixture.Book(99,102));Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);});
        Add("reentrancy","Depth reentered inside cancel cannot send another request",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);f.Tab.OnCancel=o=>f.Tick(Fixture.Book(98,102));f.Tick(Fixture.Book(99,102));Assert.Equal(2,f.Count);});
        Add("reentrancy","Depth reentered inside reprice cannot cancel",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live();f.Tab.ServerIsSupportChangeOrderPrice=true;f.Tab.OnChange=(o,p)=>f.Tick(Fixture.Book(98,102));f.Tick(Fixture.Book(99,102));Assert.Equal(2,f.Count);Assert.Equal("Change",f.Last.Kind);});
        Add("concurrency","Parallel depth callbacks serialize one initial placement",c=>{var f=c.New(100);Parallel.For(0,64,_=>f.Tab.Depth(Fixture.Book()));f.Verify();Assert.Equal(1,f.Count);});
        Add("concurrency","Parallel Off callbacks serialize one cancellation",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);f.Off();Parallel.For(0,64,_=>f.Tab.Depth(Fixture.Book()));f.Verify();Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);});
        Add("concurrency","Parallel duplicate Active callbacks do not create commands",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Active);var snapshot=f.First.Copy();Parallel.For(0,64,_=>f.Tab.Update(snapshot.Copy()));f.Verify();Assert.Equal(1,f.Count);Assert.That(f.Operation==null,"ack not observed");});
        Add("concurrency","Parallel timeout callbacks log one error and never retry",c=>{var f=c.New();f.Tick();f.Live();f.Advance(TimeSpan.FromSeconds(61));Parallel.For(0,64,_=>Reflect.Call(f.Robot,"CheckOperationTimer",new object[]{null}));f.Verify();Assert.Equal(1,f.Count);Assert.Equal(1,f.Robot.Logs.Count(x=>x.Contains("Execution Error")));});
        Add("concurrency","Delete concurrent with depth leaves detached lifecycle",c=>{var f=c.New();Parallel.Invoke(()=>f.Tab.Depth(Fixture.Book()),()=>f.Robot.Dispose());int count=f.Count;f.Tab.Depth(Fixture.Book());Assert.Equal(count,f.Count);Assert.Equal(0,f.Tab.DepthSubscribers);Assert.Equal(0,f.Tab.OrderSubscribers);});
    }
}

