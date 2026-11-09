using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Planning()
    {
        Add("planning","Distributed buy consumes best bid before lower levels",c=>{var f=c.New();f.Decimal("_buyTotalVolume",25);var d=Fixture.Book();d.Bids[0].Bid=10;d.Bids[1].Bid=20;var p=f.Plan(d);Assert.Equal(2,p.Count);Assert.Equal(new PlanRow(Side.Buy,100,10),p[0]);Assert.Equal(new PlanRow(Side.Buy,99,15),p[1]);});
        Add("planning","Distributed buy respects percentage of foreign level",c=>{var f=c.New();f.Decimal("_buyTotalVolume",100);f.Decimal("_levelVolumePercent",25);var p=f.Plan(Fixture.Book(qty:40));Assert.Equal(3,p.Count);Assert.That(p.All(x=>x.Volume==10),"percentage distribution incorrect");});
        Add("planning","Zero-volume bid levels do not consume buy budget",c=>{var f=c.New();var d=Fixture.Book();d.Bids[0].Bid=0;var p=f.Plan(d);Assert.Equal(99m,p[0].Price);Assert.Equal(10m,p[0].Volume);});
        Add("planning","One-level mode selects maximum foreign volume",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);var d=Fixture.Book();d.Bids[1].Bid=200;var p=f.Plan(d);Assert.Equal(1,p.Count);Assert.Equal(99m,p[0].Price);});
        Add("planning","One-level mode tie chooses highest bid",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);var p=f.Plan(Fixture.Book());Assert.Equal(100m,p[0].Price);});
        Add("planning","One-level own volume is excluded from level selection",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);var d=Fixture.Book();d.Bids[0].Bid=105;var own=new Order{Side=Side.Buy,Price=100,Volume=10,State=OrderStateType.Active};var p=f.Plan(d,new(){own});Assert.Equal(99m,p[0].Price);});
        Add("planning","Specified-volume mode uses first qualifying bid",c=>{var f=c.New();f.String("_buyMode",Fixture.Specified);f.Decimal("_specifiedOrderVolume",150);var d=Fixture.Book(100,105);d.Bids[1].Bid=200;d.Bids[2].Bid=300;var p=f.Plan(d);Assert.Equal(1,p.Count);Assert.Equal(100m,p[0].Price);});
        Add("planning","Specified-volume mode rejects crossing target",c=>{var f=c.New();f.String("_buyMode",Fixture.Specified);f.Decimal("_specifiedOrderVolume",50);Assert.Equal(0,f.Plan(Fixture.Book(100,101)).Count);});
        Add("planning","Specified-volume threshold absent produces no buy",c=>{var f=c.New();f.String("_buyMode",Fixture.Specified);f.Decimal("_specifiedOrderVolume",101);Assert.Equal(0,f.Plan(Fixture.Book()).Count);});
        Add("planning","Specified-volume sets own quantity from normal budget",c=>{var f=c.New();f.String("_buyMode",Fixture.Specified);f.Decimal("_specifiedOrderVolume",50);f.Decimal("_buyTotalVolume",7);var p=f.Plan(Fixture.Book(100,105));Assert.Equal(7m,p[0].Volume);});
        Add("planning","Wall requires consecutive confirmations",c=>{var f=WallFixture(c);var d=WallBook();Assert.Equal(0,f.Plan(d).Count);Assert.Equal(0,f.Plan(d).Count);var p=f.Plan(d);Assert.Equal(1,p.Count);Assert.Equal(101m,p[0].Price);});
        Add("planning","Wall candidate price change resets confirmation count",c=>{var f=WallFixture(c);f.Plan(WallBook());var d=WallBook();d.Bids[0].Bid=10;d.Bids[1].Bid=1000;Assert.Equal(0,f.Plan(d).Count);Assert.Equal(0,f.Plan(d).Count);Assert.Equal(1,f.Plan(d).Count);});
        Add("planning","Wall disappearance resets confirmation count",c=>{var f=WallFixture(c);f.Plan(WallBook());f.Plan(Fixture.Book());Assert.Equal(0,f.Plan(WallBook()).Count);Assert.Equal(0,f.Plan(WallBook()).Count);Assert.Equal(1,f.Plan(WallBook()).Count);});
        Add("planning","Wall enforces both minimum and median factor",c=>{var f=WallFixture(c);f.Int("_confirmTicks",1);var d=Fixture.Book(100,105);d.Bids[0].Bid=90;Assert.Equal(0,f.Plan(d).Count);d.Bids[0].Bid=1000;f.Decimal("_wallMedianFactor",200);Assert.Equal(0,f.Plan(d).Count);});
        Add("planning","Wall target equal to best ask is rejected",c=>{var f=WallFixture(c);f.Int("_confirmTicks",1);var d=WallBook();d.Asks[0].Price=101;Assert.Equal(0,f.Plan(d).Count);});
        Add("planning","Sell always distributes best ask upward",c=>{var f=c.New(25);f.Decimal("_buyTotalVolume",0);var d=Fixture.Book();d.Asks[0].Ask=10;d.Asks[1].Ask=20;var p=f.Plan(d);Assert.Equal(2,p.Count);Assert.Equal(new PlanRow(Side.Sell,102,10),p[0]);Assert.Equal(new PlanRow(Side.Sell,103,15),p[1]);});
        Add("planning","Sell foreign-volume increase does not raise basis",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);var d=Fixture.Book(qty:10);var first=f.Plan(d);var next=f.Plan(Fixture.Book(qty:50));Assert.Equal(first.Sum(x=>x.Volume),next.Sum(x=>x.Volume));});
        Add("planning","Sell foreign-volume decrease lowers basis",c=>{var f=c.New(100);f.Decimal("_buyTotalVolume",0);f.Plan(Fixture.Book(qty:20));var next=f.Plan(Fixture.Book(qty:10));Assert.Equal(30m,next.Sum(x=>x.Volume));});
        Add("planning","Repeated unchanged planner input produces identical plan",c=>{var f=c.New(100);var d=Fixture.Book();var a=f.Plan(d);var b=f.Plan(d);Assert.That(a.SequenceEqual(b),"identical input changed target");});
        Add("planning","Market depth is copied before planning",c=>{var f=c.New();var d=Fixture.Book();d.OnCopy=()=>d.Bids[0].Price=90;f.Tick(d);Assert.Equal(1,d.Copies);Assert.Equal(100m,f.Last.Price);});
        Add("planning","Standing-order floor expires after own settle window",c=>{var f=c.New();f.Decimal("_buyTotalVolume",10);f.Tick();f.Ack(f.First,OrderStateType.Active);var d=Fixture.Book();d.Bids[0].Bid=15;var during=f.Plan(d);Assert.Equal(10m,during[0].Volume);f.Advance(TimeSpan.FromSeconds(1));var after=f.Plan(d);Assert.Equal(5m,after[0].Volume);});
    }
    static Fixture WallFixture(TestContext c) { var f=c.New();f.String("_buyMode",Fixture.Wall);f.Decimal("_wallMinVolume",100);f.Decimal("_wallMedianFactor",3);f.Int("_confirmTicks",3);return f; }
    static MarketDepth WallBook() { var d=Fixture.Book(100,105,10);d.Bids[0].Bid=1000;return d; }
}
