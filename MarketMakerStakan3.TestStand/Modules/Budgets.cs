using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Budgets()
    {
        Add("budgets","Fixed buy quantity is independent of portfolio valuation",c=>{var f=c.New();f.Tab.Portfolio.ValueCurrent=1;f.Tick();Assert.Equal(10m,f.Last.Volume);});
        Add("budgets","Portfolio percentage buy quantity uses value and bid",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Percent);f.Decimal("_buyTotalVolume",1);f.Tab.Portfolio.ValueCurrent=100000;f.Tick();Assert.Equal(10m,f.Last.Volume);});
        Add("budgets","Missing portfolio blocks valuation-based orders",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Percent);f.Tab.Portfolio=null;f.Tick();Assert.Equal(0,f.Count);});
        Add("budgets","Cash-only budget uses rub row before aggregate portfolio",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=700;f.Tick();Assert.Equal(7m,f.Last.Volume);});
        Add("budgets","Portfolio equity is not usable cash when rub row is absent",c=>{var f=c.New(100);f.String("_buyVolumeMode",Fixture.Cash);f.Tab.Portfolio.ValueCurrent=100000;f.Decimal("_sellPercent",0);f.Tick();Assert.Equal(0,f.Count);});
        Add("budgets","Negative rub cash balance cannot fund a buy",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=-200;f.Tick();Assert.Equal(0,f.Count);});
        Add("budgets","Cash-only preserves reserved order when free money is zero",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=1000;f.Tick();f.Ack(f.First,OrderStateType.Active);f.Money.ValueCurrent=0;f.Tick(Fixture.Book(qty:110));Assert.Equal(1,f.Count);});
        Add("budgets","Cash-only second placement is capped by actual target price",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=1000;var d=Fixture.Book();d.Bids[0].Bid=5;f.Tick(d);f.Ack(f.First,OrderStateType.Active);f.Money.ValueCurrent=500;d.Bids[0].Bid=10;f.Tick(d);Assert.Equal(2,f.Count);Assert.That(f.Last.Volume*f.Last.Price<=500,"cash budget exceeded");});
        Add("budgets","Sell free stock subtracts ValueBlocked exactly once",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Stock.ValueBlocked=30;f.Tick();Assert.Equal(70m,f.Last.Volume);});
        Add("budgets","Sell percentage scales free stock",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Decimal("_sellPercent",25);f.Tick();Assert.Equal(25m,f.Last.Volume);});
        Add("budgets","Sell plan restores reserved quantity before percentage",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Decimal("_sellPercent",50);f.Stock.ValueBlocked=80;var own=new Order{Side=Side.Sell,Price=102,Volume=80,State=OrderStateType.Active};var p=f.Plan(Fixture.Book(qty:200),new(){own});Assert.Equal(50m,p.Sum(x=>x.Volume));});
        Add("budgets","Sell free twenty can top up reserved eighty",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);var d=Fixture.Book();d.Asks[0].Ask=80;d.Asks[1].Ask=20;f.Tick(d);f.Ack(f.First,OrderStateType.Active);f.Stock.ValueBlocked=80;d.Asks[0].Ask=160;f.Tick(d);Assert.Equal(2,f.Count);Assert.Equal(20m,f.Last.Volume);});
        Add("budgets","Sell budget growth waits confirmation window",c=>{var f=c.New(20);f.Decimal("_buyTotalVolume",0);f.Int("_confirmTicks",3);Assert.Equal(20m,f.Plan(Fixture.Book()).Sum(x=>x.Volume));f.Stock.ValueCurrent=40;Assert.Equal(20m,f.Plan(Fixture.Book()).Sum(x=>x.Volume));Assert.Equal(20m,f.Plan(Fixture.Book()).Sum(x=>x.Volume));Assert.Equal(40m,f.Plan(Fixture.Book()).Sum(x=>x.Volume));});
        Add("budgets","Sell budget shrink applies immediately",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Plan(Fixture.Book());f.Stock.ValueCurrent=30;Assert.Equal(30m,f.Plan(Fixture.Book()).Sum(x=>x.Volume));});
        Add("budgets","Sell portfolio-value mode uses best ask",c=>{var f=c.New();f.Decimal("_buyTotalVolume",0);f.String("_sellVolumeMode","% от всего портфеля");f.Tab.Portfolio.ValueCurrent=1020;f.Decimal("_sellPercent",10);var p=f.Plan(Fixture.Book());Assert.Equal(1m,p.Sum(x=>x.Volume));});
        Add("budgets","Negative blocked stock cannot create fictitious free quantity",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Stock.ValueBlocked=-20;f.Tick();Assert.That(f.Count==0||f.Last.Volume<=100,"negative blocked inflated sell budget");});
        Add("budgets","Duplicate stock balance rows are ambiguous and block trading",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Tab.Portfolio.Positions.Add(new(){SecurityNameCode="TEST",ValueCurrent=100});f.Tick();Assert.Equal(0,f.Count);Assert.That(f.Error,"duplicate stock rows not error");});
        Add("budgets","Duplicate rub balance rows block cash-only trading",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Cash);f.Money.ValueCurrent=1000;f.Tab.Portfolio.Positions.Add(new(){SecurityNameCode="rub",ValueCurrent=1000});f.Tick();Assert.Equal(0,f.Count);Assert.That(f.Error,"duplicate cash rows not error");});
        Add("budgets","Sell portfolio blocks before book includes own acknowledged order",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live(false);f.Stock.ValueBlocked=100;f.PortfolioEvent();f.Tick(Fixture.Book());Assert.Equal(1,f.Count,"cancelled own order while its book inclusion lagged");});
        Add("budgets","Sell book includes acknowledged own order before portfolio update",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Tick();f.Ack(f.First,OrderStateType.Active);f.Live(false);var d=Fixture.Book();d.Asks[0].Ask=200;f.Tick(d);Assert.Equal(1,f.Count);f.Stock.ValueBlocked=100;f.PortfolioEvent();f.Tick(d);Assert.Equal(1,f.Count);});
    }
}

