using System.Reflection;
using OsEngine.Entity;
using OsEngine.OsTrader.Panels.Tab;
using OsEngine.Robots.MarketMaker;
namespace OfflineStand;
public record Scenario(string Module,string Name,Action<TestContext> Run);
public static class Assert
{
    public static void That(bool value,string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Equal<T>(T expected,T actual,string message="") { if (!EqualityComparer<T>.Default.Equals(expected,actual)) throw new InvalidOperationException($"{message} expected={expected}, actual={actual}"); }
}
public static class Reflect
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Get(object target,string name) => target.GetType().GetField(name,Flags)?.GetValue(target) ?? throw new MissingFieldException(target.GetType().Name,name);
    public static object Maybe(object target,string name) => target.GetType().GetField(name,Flags)?.GetValue(target);
    public static void Set(object target,string name,object value) => (target.GetType().GetField(name,Flags) ?? throw new MissingFieldException(name)).SetValue(target,value);
    public static object Call(object target,string name,params object[] args)
    {
        try { return (target.GetType().GetMethod(name,Flags) ?? throw new MissingMethodException(name)).Invoke(target,args); }
        catch(TargetInvocationException e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException ?? e).Throw(); throw; }
    }
}
public record PlanRow(Side Side,decimal Price,decimal Volume);
public sealed class TestContext : IDisposable
{
    public int Seed {get;}
    public int Iterations {get;}
    public long PropertyEventCount;
    public long BrokerActiveSteps; public int BrokerCommands,BrokerFills,BrokerCommandsBase,BrokerFillsBase;
    readonly List<Fixture> fixtures=new();
    public TestContext(int seed,int iterations) { Seed=seed;Iterations=iterations; }
    public Fixture New(decimal stock=0,StartProgram program=StartProgram.IsTester,BotTabSimple tab=null)
    {
        var fixture=new Fixture(stock,program,tab); fixtures.Add(fixture); return fixture;
    }
    public string Trace => string.Join("\n",fixtures.SelectMany((f,i)=>f.Trace.Select(x=>$"fixture={i} {x}")));
    public void Verify() { foreach(var f in fixtures) f.Verify(); }
    public void Dispose() { foreach(var f in fixtures) f.Dispose(); }
}
public sealed class Fixture : IDisposable
{
    public const string Distributed="Распределение сверху вниз", Single="Один уровень (макс. объём)", Wall="Перед большой заявкой", Specified="Перед заявкой заданного объёма";
    public const string Fixed="Фиксированный", Percent="% от стоимости портфеля", Cash="Максимум без маржи";
    public MarketMakerStakan3 Robot {get;}
    public BotTabSimple Tab {get;}
    public CappedTrace Trace=new();
    public List<string> Violations=new();
    public Command Outstanding;
    public Action<Command> CommandObserver;
    public bool Disposed;
    public bool AllowSafetyCancelAfterError;
    public Fixture(decimal stock=0,StartProgram program=StartProgram.IsTester,BotTabSimple tab=null)
    {
        Tab=tab ?? new();
        if(stock>0) Tab.Portfolio.Positions.Add(new(){SecurityNameCode="TEST",ValueCurrent=stock});
        BotTabSimple.Next=Tab;
        try { Robot=new MarketMakerStakan3("offline-test",program); }
        finally { BotTabSimple.Next=null; }
        var timer=Reflect.Maybe(Robot,"_operationTimer") as System.Threading.Timer;
        timer?.Change(Timeout.Infinite,Timeout.Infinite);
        String("_regime","On"); String("_buyVolumeMode",Fixed); Decimal("_buyTotalVolume",10);
        Tab.OnCommand=OnCommand;
    }
    public void String(string name,string value) => ((StrategyParameterString)Reflect.Get(Robot,name)).ValueString=value;
    public string String(string name) => ((StrategyParameterString)Reflect.Get(Robot,name)).ValueString;
    public void Decimal(string name,decimal value) => ((StrategyParameterDecimal)Reflect.Get(Robot,name)).ValueDecimal=value;
    public void Int(string name,int value) => ((StrategyParameterInt)Reflect.Get(Robot,name)).ValueInt=value;
    public bool Error => (bool)Reflect.Get(Robot,"_executionError");
    public object Operation => Reflect.Maybe(Robot,"_operation");
    public int Count => Tab.Commands.Count;
    public Command Last => Tab.Commands[^1];
    public Order First => Tab.PositionsAll.SelectMany(p=>p.OpenOrders??new()).First();
    public Order LastOrder => Tab.PositionsAll.SelectMany(p=>p.OpenOrders??new()).Last();
    public Order Registry(int number) => ((Dictionary<int,Order>)Reflect.Get(Robot,"_ownOrders"))[number];
    public List<Order> Active() => (List<Order>)Reflect.Call(Robot,"GetMyActiveOrders");
    public PositionOnBoard Stock => Tab.Portfolio.Positions.First(x=>x.SecurityNameCode==Tab.Security.Name);
    public PositionOnBoard Money
    {
        get { var row=Tab.Portfolio.Positions.FirstOrDefault(x=>x.SecurityNameCode=="rub");if(row==null){row=new(){SecurityNameCode="rub"};Tab.Portfolio.Positions.Add(row);} return row; }
    }
    public void Off() { Trace.Add("regime Off");String("_regime","Off"); }
    public void On() { Trace.Add("regime On");String("_regime","On"); }
    public void Live(bool settle = true) { if (settle) Advance(TimeSpan.FromSeconds(1)); Robot.StartProgram=StartProgram.IsOsTrader; Reflect.Set(Robot,"_lastProcessTime",DateTime.MinValue); }
    public void Tick(MarketDepth depth=null,bool bypassThrottle=true)
    {
        if(bypassThrottle) Reflect.Set(Robot,"_lastProcessTime",DateTime.MinValue);
        Trace.Add($"depth bid={depth?.Bids?.FirstOrDefault()?.Price} ask={depth?.Asks?.FirstOrDefault()?.Price}");
        Tab.Depth(depth ?? Book());
        Verify();
    }
    public void RawTick(MarketDepth depth) { Trace.Add("raw depth");Tab.Depth(depth);Verify(); }
    public void Ack(Order order,OrderStateType state,decimal? fill=null,decimal? price=null,bool updateJournal=true)
    {
        var snapshot=order.Copy();snapshot.State=state;if(fill.HasValue)snapshot.VolumeExecute=fill.Value;if(price.HasValue)snapshot.Price=price.Value;
        Trace.Add($"ack user={snapshot.NumberUser} state={state} fill={snapshot.VolumeExecute} price={snapshot.Price}");
        Release(snapshot);
        if(updateJournal) { order.State=snapshot.State;order.VolumeExecute=snapshot.VolumeExecute;order.Price=snapshot.Price;order.NumberMarket=snapshot.NumberMarket; }
        Tab.Update(snapshot);Verify();
    }
    public void PortfolioEvent() { Trace.Add("portfolio event");Tab.PortfolioEvent(); }
    public void RawOrder(Order snapshot) { Trace.Add($"connector raw user={snapshot?.NumberUser} state={snapshot?.State}");if(snapshot!=null)Release(snapshot);Tab.Connector.Emit(snapshot);Verify(); }
    public void Update(Order snapshot) { Trace.Add($"raw ack user={snapshot?.NumberUser} state={snapshot?.State}"); if(snapshot!=null)Release(snapshot);Tab.Update(snapshot);Verify(); }
    void Release(Order order)
    {
        if(Outstanding==null || Outstanding.NumberUser!=order.NumberUser)return;
        bool terminal=Terminal(order.State);
        if(terminal || (Outstanding.Kind=="Place" && order.State is OrderStateType.Active or OrderStateType.Partial) ||
           (Outstanding.Kind=="Change" && order.Price==Outstanding.Price && order.State is OrderStateType.Active or OrderStateType.Partial))Outstanding=null;
    }
    void OnCommand(Command command)
    {
        Trace.Add($"command {command}");
        if(Outstanding!=null)Violations.Add($"parallel command {command.Kind}, outstanding={Outstanding}");
        if(Error && !(AllowSafetyCancelAfterError && command.Kind=="Cancel" && (String("_regime")=="Off" || (Reflect.Maybe(Robot,"_shutdownStopping") is bool stopping && stopping)) && ((Dictionary<int,Order>)Reflect.Get(Robot,"_ownOrders")).ContainsKey(command.NumberUser)))Violations.Add("command after Error");
        if(command.Kind=="Place" && String("_regime")!="On")Violations.Add("placement while Off");
        if(command.Price<=0 || command.Volume<=0)Violations.Add("nonpositive command price/volume");
        if(Disposed)Violations.Add("command after Delete");
        Outstanding=command;
        CommandObserver?.Invoke(command);
    }
    public void Timer() { Trace.Add("manual timer callback");Reflect.Call(Robot,"CheckOperationTimer",new object[]{null});Verify(); }
    public void Advance(TimeSpan elapsed)
    {
        Trace.Add($"advance {elapsed.TotalMilliseconds}ms (shift deadlines, no sleeping)");
        foreach(var field in Robot.GetType().GetFields(BindingFlags.Instance|BindingFlags.NonPublic))
        {
            if(field.FieldType!=typeof(DateTime))continue;
            var value=(DateTime)field.GetValue(Robot);
            if(value!=DateTime.MinValue && value.Ticks>elapsed.Ticks)field.SetValue(Robot,value-elapsed);
        }
        if(Operation is object op)
        {
            var field=op.GetType().GetField("Time");var value=(DateTime)field.GetValue(op);field.SetValue(op,value-elapsed);
        }
    }
    public List<PlanRow> Plan(MarketDepth depth,List<Order> active=null)
    {
        Reflect.Call(Robot,"CaptureBalances");
        var plan=(System.Collections.IEnumerable)Reflect.Call(Robot,"BuildPlan",depth,active??Active());
        return plan.Cast<object>().Select(p=>new PlanRow((Side)p.GetType().GetField("Side").GetValue(p),(decimal)p.GetType().GetField("Price").GetValue(p),(decimal)p.GetType().GetField("Volume").GetValue(p))).ToList();
    }
    public bool VolumesMatch(decimal plan,decimal actual,Side side) => (bool)Reflect.Call(Robot,"VolumesMatch",plan,actual,side);
    public void Verify() { Assert.That(Violations.Count==0,string.Join("; ",Violations)); }
    public void Dispose()
    {
        if(Disposed)return;Disposed=true;
        Robot.Dispose();
        Assert.Equal(0,Tab.DepthSubscribers,"depth handler leak");Assert.Equal(0,Tab.OrderSubscribers,"order handler leak");Assert.Equal(0,Tab.PortfolioSubscribers,"portfolio handler leak");Assert.Equal(0,Tab.Connector.Subscribers,"connector handler leak");
    }
    public static bool Terminal(OrderStateType state) => state is OrderStateType.Cancel or OrderStateType.Done or OrderStateType.Fail;
    public static MarketDepth Book(double bid=100,double ask=102,double qty=100) => new()
    {
        Bids=new(){new(){Price=bid,Bid=qty},new(){Price=bid-1,Bid=qty},new(){Price=bid-2,Bid=qty}},
        Asks=new(){new(){Price=ask,Ask=qty},new(){Price=ask+1,Ask=qty},new(){Price=ask+2,Ask=qty}}
    };
}

public sealed class CappedTrace : IEnumerable<string>
{
    readonly Queue<string> lines=new();
    public void Add(string line) { lock(lines){lines.Enqueue(line);if(lines.Count>2000)lines.Dequeue();} }
    public IEnumerator<string> GetEnumerator() { lock(lines)return ((IEnumerable<string>)lines.ToArray()).GetEnumerator(); }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
}



