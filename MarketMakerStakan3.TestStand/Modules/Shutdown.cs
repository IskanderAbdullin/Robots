using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Shutdown()
    {
        Add("shutdown","Off parameter cancels Active order immediately without depth or timer",c=>
        {
            var f=LiveActive(c);f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);
        });
        Add("shutdown","Off parameter waits for pending place then cancels on acknowledgment",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader);f.Tick();f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(1,f.Count);f.Ack(f.First,OrderStateType.Active);Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);
        });
        Add("shutdown","Off cancellation uses partial remaining quantity",c=>
        {
            var f=LiveActive(c);f.Ack(f.First,OrderStateType.Partial,3);f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(2,f.Count);Assert.Equal(7m,f.Last.Volume);
        });
        Add("shutdown","Off drains own orders sequentially from terminal callbacks",c=>
        {
            var f=TwoLiveOrders(c);var orders=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).ToArray();f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(3,f.Count);Assert.Equal(orders[0].NumberUser,f.Last.NumberUser);f.Ack(orders[0],OrderStateType.Cancel);Assert.Equal(4,f.Count);Assert.Equal(orders[1].NumberUser,f.Last.NumberUser);f.Ack(orders[1],OrderStateType.Cancel);Assert.That(f.Operation==null,"cancel operation not released");Assert.Equal(4,f.Count);
        });
        Add("shutdown","Repeated Off parameter events do not repeat pending cancellation",c=>
        {
            var f=LiveActive(c);f.Off();for(int i=0;i<20;i++)f.Robot.RaiseParametersChanged();Assert.Equal(2,f.Count);
        });
        Add("shutdown","Parameter event while On does not create new plan",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader);f.Robot.RaiseParametersChanged();Assert.Equal(0,f.Count);
        });
        Add("shutdown","Off does not cancel restored unrelated orders",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader,tab:Restored(OrderStateType.Active));f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(0,f.Count);
        });
        Add("shutdown","Off disconnected order waits for reconnect before sending cancellation",c=>
        {
            var f=LiveActive(c);f.Tab.IsConnected=false;f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(1,f.Count);Assert.That(f.Operation==null,"disconnected Off created phantom cancel operation");f.Tab.IsConnected=true;f.Timer();Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);
        });
        Add("shutdown","Off during pending reprice waits for target acknowledgment then cancels",c=>
        {
            var f=ChangePending(c);f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(2,f.Count);f.Ack(f.First,OrderStateType.Active,price:99);Assert.Equal(3,f.Count);Assert.Equal("Cancel",f.Last.Kind);Assert.Equal(99m,f.Last.Price);
        });
        Add("shutdown","Graceful shutdown without own orders completes and forbids new placement",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader);using var session=new ShutdownSession(f);session.Complete();f.On();f.Robot.RaiseParametersChanged();f.Tick();Assert.Equal(0,f.Count);
        });
        Add("shutdown","Graceful shutdown waits for confirmed cancel before completing",c=>
        {
            var f=LiveActive(c);using var session=new ShutdownSession(f);Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);Assert.That(!session.Task.IsCompleted,"shutdown completed before cancel acknowledgment");f.Ack(f.First,OrderStateType.Cancel);session.Complete();Assert.Equal(2,f.Count);
        });
        Add("shutdown","Graceful shutdown drains multiple owned orders one at a time",c=>
        {
            var f=TwoLiveOrders(c);var orders=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).ToArray();using var session=new ShutdownSession(f);Assert.Equal(3,f.Count);Assert.That(!session.Task.IsCompleted,"shutdown did not wait for first cancel");f.Ack(orders[0],OrderStateType.Cancel);Assert.Equal(4,f.Count);Assert.Equal(orders[1].NumberUser,f.Last.NumberUser);f.Ack(orders[1],OrderStateType.Cancel);session.Complete();Assert.Equal(4,f.Count);
        });
        Add("shutdown","Graceful shutdown waits pending place identity and cancels after Active",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader);f.Tick();using var session=new ShutdownSession(f);Assert.Equal(1,f.Count);Assert.That(!session.Task.IsCompleted,"pending place treated as absent");f.Ack(f.First,OrderStateType.Active);Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);f.Ack(f.First,OrderStateType.Cancel);session.Complete();
        });
        Add("shutdown","Graceful shutdown cancellation preserves executed partial quantity",c=>
        {
            var f=LiveActive(c);f.Ack(f.First,OrderStateType.Partial,4);using var session=new ShutdownSession(f);Assert.Equal(6m,f.Last.Volume);f.Ack(f.First,OrderStateType.Cancel,4);session.Complete();Assert.Equal(4m,f.Registry(f.First.NumberUser).VolumeExecute);
        });
        Add("shutdown","Restored live order is not adopted but blocks successful shutdown",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader,tab:Restored(OrderStateType.Active));using var session=new ShutdownSession(f);f.Advance(TimeSpan.FromSeconds(9));session.Complete(false);Assert.Equal(0,f.Count);Assert.That(f.Robot.Logs.Any(x=>x.Contains("77")),"old unresolved order omitted from diagnosis");
        });
        Add("shutdown","Restored terminal order does not block graceful shutdown",c=>
        {
            var f=c.New(program:StartProgram.IsOsTrader,tab:Restored(OrderStateType.Cancel));using var session=new ShutdownSession(f);session.Complete();Assert.Equal(0,f.Count);
        });        Add("shutdown","Shutdown timeout reports unconfirmed order rather than successful cleanup",c=>
        {
            var f=LiveActive(c);int user=f.First.NumberUser;using var session=new ShutdownSession(f);Assert.That(!session.Task.IsCompleted,"unconfirmed cancellation completed synchronously");f.Advance(TimeSpan.FromSeconds(9));session.Complete(false);Assert.Equal(2,f.Count);var logs=string.Join("\n",f.Robot.Logs);Assert.That(logs.Contains(user.ToString()),"timeout omitted own NumberUser");Assert.That(f.Robot.Logs.Any(IsShutdownWarning),"timeout did not log incomplete cleanup");Assert.That(!f.Robot.Logs.Any(IsShutdownSuccess),"timeout reported successful cleanup");
        });
        Add("shutdown","Disconnected shutdown cannot pretend exchange cancellation is confirmed",c=>
        {
            var f=LiveActive(c);f.Tab.IsConnected=false;using var session=new ShutdownSession(f);Assert.Equal(1,f.Count);Assert.That(!session.Task.IsCompleted,"disconnect treated as confirmation");f.Advance(TimeSpan.FromSeconds(9));session.Complete(false);Assert.Equal(1,f.Count);Assert.That(f.Robot.Logs.Any(IsShutdownWarning),"disconnect timeout not reported");
        });
        Add("shutdown","Repeated shutdown notifications share cancellation and never duplicate request",c=>
        {
            var f=LiveActive(c);using var first=new ShutdownSession(f);using var second=new ShutdownSession(f);Assert.Equal(2,f.Count);f.Ack(f.First,OrderStateType.Cancel);first.Complete();second.Complete();Assert.Equal(2,f.Count);
        });
        Add("shutdown","On selected during graceful shutdown cannot reopen trading",c=>
        {
            var f=LiveActive(c);using var session=new ShutdownSession(f);f.On();f.Robot.RaiseParametersChanged();f.Tick(Fixture.Book(99,102));Assert.Equal(2,f.Count);f.Ack(f.First,OrderStateType.Cancel);session.Complete();f.Advance(TimeSpan.FromSeconds(1));f.PortfolioEvent();f.On();f.Tick();Assert.Equal(2,f.Count);
        });
        Add("shutdown","Delete removes static shutdown and parameter callbacks",c=>
        {
            int before=OsEngine.RobotShutdownCoordinator.ShutdownSubscribers;var f=c.New(program:StartProgram.IsOsTrader);Assert.Equal(before+1,OsEngine.RobotShutdownCoordinator.ShutdownSubscribers,"shutdown callback not registered");f.Dispose();Assert.Equal(before,OsEngine.RobotShutdownCoordinator.ShutdownSubscribers,"static shutdown callback leaked");f.Robot.RaiseParametersChanged();Assert.Equal(0,f.Count);
        });
        Add("shutdown","Explicit Off can cancel another known own Active order after Error",c=>
        {
            var f=TwoLiveOrders(c);var orders=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).ToArray();f.Ack(orders[0],OrderStateType.Fail);Assert.That(f.Error,"setup did not enter Error");f.AllowSafetyCancelAfterError=true;f.Off();f.Robot.RaiseParametersChanged();Assert.Equal(3,f.Count);Assert.Equal("Cancel",f.Last.Kind);Assert.Equal(orders[1].NumberUser,f.Last.NumberUser);f.Ack(orders[1],OrderStateType.Cancel);Assert.That(f.Error,"explicit cleanup cleared Error");f.On();f.Tick();Assert.Equal(3,f.Count);
        });
        Add("shutdown","Graceful shutdown can drain known Active order while preserving Error",c=>
        {
            var f=TwoLiveOrders(c);var orders=f.Tab.PositionsAll.SelectMany(p=>p.OpenOrders).ToArray();f.Ack(orders[0],OrderStateType.Fail);Assert.That(f.Error,"setup did not enter Error");f.AllowSafetyCancelAfterError=true;using var session=new ShutdownSession(f);Assert.Equal(3,f.Count);Assert.Equal("Cancel",f.Last.Kind);Assert.Equal(orders[1].NumberUser,f.Last.NumberUser);f.Ack(orders[1],OrderStateType.Cancel);session.Complete();Assert.That(f.Error,"shutdown cleared Error");Assert.Equal(3,f.Count);
        });        Add("shutdown","Synchronous terminal cancel callback cannot start a replacement",c=>
        {
            var f=LiveActive(c);f.Tab.OnCancel=o=>f.Ack(o,OrderStateType.Cancel);using var session=new ShutdownSession(f);session.Complete();Assert.Equal(2,f.Count);Assert.Equal("Cancel",f.Last.Kind);
        });
    }
    static Fixture LiveActive(TestContext c)
    {
        var f=c.New(program:StartProgram.IsOsTrader);f.Tick();f.Ack(f.First,OrderStateType.Active);return f;
    }
    static Fixture TwoLiveOrders(TestContext c)
    {
        var f=c.New(program:StartProgram.IsOsTrader);f.Decimal("_buyTotalVolume",150);var d=Fixture.Book();f.Tick(d);f.Ack(f.First,OrderStateType.Active);f.PortfolioEvent();f.Advance(TimeSpan.FromSeconds(1));d.Bids[0].Bid=200;f.Tick(d);Assert.Equal(2,f.Count,"second owned order setup");f.Ack(f.LastOrder,OrderStateType.Active);return f;
    }
    static bool IsShutdownWarning(string log) => log.StartsWith("Error:",StringComparison.OrdinalIgnoreCase) &&
        (log.Contains("shutdown",StringComparison.OrdinalIgnoreCase)||log.Contains("выход",StringComparison.OrdinalIgnoreCase)||log.Contains("заверш",StringComparison.OrdinalIgnoreCase)||log.Contains("останов",StringComparison.OrdinalIgnoreCase)||log.Contains("отключ",StringComparison.OrdinalIgnoreCase));
    static bool IsShutdownSuccess(string log) => !log.StartsWith("Error:",StringComparison.OrdinalIgnoreCase) &&
        (log.Contains("подтверждено снятие",StringComparison.OrdinalIgnoreCase)||log.Contains("все собственные заявки сняты",StringComparison.OrdinalIgnoreCase)||log.Contains("shutdown complete",StringComparison.OrdinalIgnoreCase));
    sealed class ShutdownSession : IDisposable
    {
        readonly Fixture fixture;
        public Task<bool> Task {get;}
        public ShutdownSession(Fixture fixture) { this.fixture=fixture;Task=OsEngine.RobotShutdownCoordinator.PrepareAsync("offline test"); }
        public void Complete(bool expectedSuccess=true) { Assert.That(Task.Wait(TimeSpan.FromSeconds(3)),"shutdown task did not finish within bounded wait");Assert.Equal(expectedSuccess,Task.GetAwaiter().GetResult(),"coordinator success result"); }
        public void Dispose()
        {
            if(!Task.IsCompleted)fixture.Advance(TimeSpan.FromSeconds(10)); Assert.That(Task.Wait(TimeSpan.FromSeconds(3)),"shutdown cleanup task leaked");
        }
    }
}





