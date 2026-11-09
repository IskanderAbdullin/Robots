using System.Collections.Concurrent;
namespace OsEngine.Entity
{
    public enum StartProgram { IsOsTrader, IsTester, IsOsOptimizer }
    public enum Side { None, Buy, Sell }
    public enum OrderStateType { None, Pending, Active, Partial, Cancel, Done, Fail }
    public enum PositionStateType { Opening, Open, OpeningFail, Done }
    public enum OrderTypeTime { GTC }
    public enum SecurityType { None, Stock, Futures, CurrencyPair, Bond }
    public class StrategyParameterString { public string ValueString; }
    public class StrategyParameterDecimal { public decimal ValueDecimal; }
    public class StrategyParameterInt { public int ValueInt; }
    public class Security
    {
        public decimal VolumeStep = 1, PriceStep = 1;
        public int DecimalsVolume, Decimals;
        public string Name = "TEST", NameClass = "TQBR";
        public decimal Lot = 1; public SecurityType SecurityType = SecurityType.Stock;
    }
    public class Order
    {
        public int NumberUser;
        public string NumberMarket = "";
        public Side Side;
        public decimal Price, Volume;
        decimal executed;
        public decimal VolumeExecute { get => executed==0 && State==OrderStateType.Done ? Volume : executed; set => executed=value; }
        public OrderStateType State;
        public Order Copy() => (Order)MemberwiseClone();
    }
    public class Position
    {
        public int Number;
        public PositionStateType State = PositionStateType.Opening;
        public List<Order> OpenOrders = new();
    }
    public class PositionOnBoard
    {
        public string SecurityNameCode, SecurityNameClass = "TQBR";
        public decimal ValueCurrent, ValueBlocked;
    }
    public class Portfolio
    {
        public string Number = "OFFLINE";
        public decimal ValueCurrent = 100000, ValueBlocked;
        public List<PositionOnBoard> Positions = new();
        public Action OnRead;
        public int ReadCount;
        public List<PositionOnBoard> GetPositionOnBoard() { ReadCount++; OnRead?.Invoke(); return Positions; }
    }
    public class MarketDepthLevel { public double Price, Bid, Ask; }
    public class MarketDepth
    {
        public List<MarketDepthLevel> Bids = new(), Asks = new();
        public Action OnCopy;
        public int Copies;
        public MarketDepth GetCopy()
        {
            Copies++;
            var copy = new MarketDepth { Bids = Bids?.ConvertAll(Clone), Asks = Asks?.ConvertAll(Clone) };
            OnCopy?.Invoke();
            return copy;
        }
        static MarketDepthLevel Clone(MarketDepthLevel x) => x == null ? null : new() { Price=x.Price, Bid=x.Bid, Ask=x.Ask };
    }
}
namespace OsEngine.Logging { public enum LogMessageType { Trade, Error, System } }
namespace OsEngine.OsTrader.Panels.Attributes { public class BotAttribute : Attribute { public BotAttribute(string name) { } } }
namespace OsEngine.OsTrader.Panels.Tab.Internal { }
namespace OsEngine.OsTrader.Panels
{
    using OsEngine.Entity;
    using OsEngine.Logging;
    using OsEngine.OsTrader.Panels.Tab;
    public enum BotTabType { Simple }
    public abstract class BotPanel : IDisposable
    {
        public StartProgram StartProgram;
        public List<BotTabSimple> TabsSimple = new();
        public List<string> Logs = new();
        public event Action DeleteEvent;
        public event Action ParametrsChangeByUser;
        public bool IsDisposed;
        public void RaiseParametersChanged() => ParametrsChangeByUser?.Invoke();
        public BotPanel(string name, StartProgram program) { StartProgram=program; }
        public void TabCreate(BotTabType type) => TabsSimple.Add(BotTabSimple.Next ?? new());
        public StrategyParameterString CreateParameter(string name, string value, string[] choices) => new() { ValueString=value };
        public StrategyParameterDecimal CreateParameter(string name, decimal value, decimal min, decimal max, decimal step) => new() { ValueDecimal=value };
        public StrategyParameterInt CreateParameter(string name, int value, int min, int max, int step) => new() { ValueInt=value };
        public void SendNewLogMessage(string message, LogMessageType type) { lock (Logs) Logs.Add(type+":"+message); }
        public void Dispose() { if (IsDisposed) return; IsDisposed=true; DeleteEvent?.Invoke(); }
        public abstract string GetNameStrategyType();
        public abstract void ShowIndividualSettingsDialog();
    }
}
namespace OsEngine.OsTrader.Panels.Tab
{
    using OsEngine.Entity;
    public enum PlaceResponse { Normal, Null, NoOrders, MultipleOrders, MismatchedIdentity }
    public record Command(int Sequence, string Kind, int NumberUser, Side Side, decimal Price, decimal Volume, OrderStateType State);
    public class Manual
    {
        public OrderTypeTime OrderTypeTime;
        public bool Disabled, Saved;
        public void DisableManualSupport() => Disabled=true;
        public void Save() => Saved=true;
    }
    public class ConnectorFake { public event Action<Order> OrderChangeEvent; public void Emit(Order order) => OrderChangeEvent?.Invoke(order); public int Subscribers => OrderChangeEvent?.GetInvocationList().Length ?? 0; }
    public class BotTabSimple
    {
        static AsyncLocal<BotTabSimple> next = new();
        public static BotTabSimple Next { get => next.Value; set => next.Value=value; }
        public Security Security = new();
        public Portfolio Portfolio = new();
        public List<Position> PositionsAll = new();
        public Manual ManualPositionSupport = new();
        public bool EmulatorIsOn, ServerIsSupportChangeOrderPrice;
        public bool IsConnected=true, IsReadyToTrade=true;
        public event Action<MarketDepth> MarketDepthUpdateEvent;
        public event Action<Order> OrderUpdateEvent;
        public event Action<Portfolio> PortfolioOnExchangeChangedEvent;
        public ConnectorFake Connector = new();
        public void PortfolioEvent() => PortfolioOnExchangeChangedEvent?.Invoke(Portfolio);
        public int PortfolioSubscribers => PortfolioOnExchangeChangedEvent?.GetInvocationList().Length ?? 0;
        public int DepthSubscribers => MarketDepthUpdateEvent?.GetInvocationList().Length ?? 0;
        public int OrderSubscribers => OrderUpdateEvent?.GetInvocationList().Length ?? 0;
        public List<Command> Commands = new();
        public Action<Order> OnPlace, OnCancel;
        public Action<Order,decimal> OnChange;
        public Action<Command> OnCommand;
        public Exception PlaceException, CancelException, ChangeException;
        public PlaceResponse Response;
        public int NextId=1;
        int sequence;
        public void Depth(MarketDepth depth) => MarketDepthUpdateEvent?.Invoke(depth);
        public void Update(Order order) => OrderUpdateEvent?.Invoke(order);
        public decimal RoundPrice(decimal price, Security security, Side side) =>
            RoundLikeEngine(price,security);
        static decimal RoundLikeEngine(decimal price,Security security)
        {
            if(security.PriceStep==0)return price;
            price=Math.Round(price,security.Decimals);
            decimal minStep=1; for(int i=0;i<security.Decimals;i++)minStep*=0.1m;
            for(int i=0;i<100000;i++){if(price%security.PriceStep==0)return price;price-=minStep;}
            throw new InvalidOperationException("RoundPrice unreachable grid: Decimals/PriceStep mismatch");
        }
        public Position BuyAtLimit(decimal volume, decimal price) => Place(volume,price,Side.Buy);
        public Position SellAtLimit(decimal volume, decimal price) => Place(volume,price,Side.Sell);
        void Record(string kind, Order order, decimal price, decimal volume)
        {
            var command = new Command(++sequence,kind,order.NumberUser,order.Side,price,volume,order.State);
            Commands.Add(command);
            OnCommand?.Invoke(command);
        }
        Position Place(decimal volume, decimal price, Side side)
        {
            var order = new Order { NumberUser=NextId++,Side=side,Price=price,Volume=volume,State=OrderStateType.Pending };
            var position = new Position { Number=order.NumberUser,OpenOrders=new() { order } };
            PositionsAll.Add(position);
            Record("Place",order,price,volume);
            if (PlaceException != null) throw PlaceException;
            OnPlace?.Invoke(order);
            return Response switch {
                PlaceResponse.Null => null,
                PlaceResponse.NoOrders => new Position {Number=position.Number,OpenOrders=new()},
                PlaceResponse.MultipleOrders => new Position {Number=position.Number,OpenOrders=new() {order,order.Copy()}},
                PlaceResponse.MismatchedIdentity => new Position {Number=position.Number,OpenOrders=new() {new Order{NumberUser=order.NumberUser+10000,Side=side,Price=price,Volume=volume,State=OrderStateType.Pending}}},
                _ => position
            };
        }
        public void CloseOrder(Order order)
        {
            Record("Cancel",order,order.Price,Math.Max(0,order.Volume-order.VolumeExecute));
            if (CancelException != null) throw CancelException;
            OnCancel?.Invoke(order);
        }
        public void ChangeOrderPrice(Order order, decimal price)
        {
            Record("Change",order,price,Math.Max(0,order.Volume-order.VolumeExecute));
            if (ChangeException != null) throw ChangeException;
            OnChange?.Invoke(order,price);
        }
    }
}



namespace OsEngine
{
    public static class RobotShutdownCoordinator
    {
        public static event Func<Task> BeforeDisconnectEvent;
        public static int ShutdownSubscribers => BeforeDisconnectEvent?.GetInvocationList().Length ?? 0;
        public static async Task<bool> PrepareAsync(string reason)
        {
            var handlers = BeforeDisconnectEvent?.GetInvocationList().Cast<Func<Task>>().ToArray() ?? Array.Empty<Func<Task>>();
            try { await Task.WhenAll(handlers.Select(handler => handler())); return true; }
            catch { return false; }
        }
    }
}

