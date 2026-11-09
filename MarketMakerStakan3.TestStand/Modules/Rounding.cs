using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Rounding()
    {
        Add("rounding","Volume rounds down to fractional step",c=>{var f=c.New();f.Tab.Security.VolumeStep=.25m;f.Tab.Security.DecimalsVolume=2;f.Decimal("_buyTotalVolume",1.13m);f.Tick();Assert.Equal(1m,f.Last.Volume);});
        Add("rounding","Distributed sub-step demand skips level without inflating quantity",c=>{var f=c.New();f.Tab.Security.VolumeStep=1;var d=Fixture.Book();d.Bids[0].Bid=.5;d.Bids[1].Bid=10;f.Tick(d);Assert.Equal(99m,f.Last.Price);Assert.Equal(10m,f.Last.Volume);});
        Add("rounding","Single-level sub-step total must not exceed configured budget",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Decimal("_buyTotalVolume",.5m);f.Tick();Assert.Equal(0,f.Count);});
        Add("rounding","Buy target uses engine decimal rounding then price grid",c=>{var f=c.New();f.Tab.Security.Decimals=2;f.Tab.Security.PriceStep=.05m;var d=Fixture.Book(100.079,102);f.Tick(d);Assert.Equal(100.05m,f.Last.Price);});
        Add("rounding","Sell target uses same engine grid rule as buy",c=>{var f=c.New(10);f.Decimal("_buyTotalVolume",0);f.Tab.Security.Decimals=2;f.Tab.Security.PriceStep=.05m;f.Tick(Fixture.Book(100,102.079));Assert.Equal(102.05m,f.Last.Price);});
        Add("rounding","Canonical foreign subtraction excludes own raw-equivalent price",c=>{var f=c.New();f.Tab.Security.Decimals=2;f.Tab.Security.PriceStep=.05m;var d=Fixture.Book(100.079,102);var own=new Order{Side=Side.Buy,Price=100.05m,Volume=10,State=OrderStateType.Active};var foreign=(List<decimal>)Reflect.Call(f.Robot,"GetForeignVolumes",d.Bids,Side.Buy,new List<Order>{own});Assert.Equal(90m,foreign[0]);});
        Add("rounding","Canonical-equivalent target does not trigger replacement",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Tab.Security.Decimals=2;f.Tab.Security.PriceStep=.05m;var d=Fixture.Book(100.079,102);d.Bids[0].Bid=200;f.Tick(d);f.Ack(f.First,OrderStateType.Active);d=Fixture.Book(100.081,102);d.Bids[0].Bid=210;f.Tick(d);Assert.Equal(1,f.Count);});
        Add("rounding","Sell growth never matches a smaller standing quantity",c=>{var f=c.New();Assert.That(!f.VolumesMatch(101,100,Side.Sell),"sell growth hidden by tolerance");});
        Add("rounding","Sell shrink tolerance preserves up to ten percent",c=>{var f=c.New();Assert.That(f.VolumesMatch(90,100,Side.Sell),"10% shrink did not match");Assert.That(!f.VolumesMatch(89,100,Side.Sell),"larger shrink matched");});
        Add("rounding","Buy tolerance permits twenty-percent shortfall only",c=>{var f=c.New();Assert.That(f.VolumesMatch(100,80,Side.Buy),"20% did not match");Assert.That(!f.VolumesMatch(100,79,Side.Buy),"excessive shortfall matched");});
        Add("rounding","Volume precision cannot round quantity above budget",c=>{var f=c.New();f.Tab.Security.VolumeStep=.25m;f.Tab.Security.DecimalsVolume=1;f.Decimal("_buyTotalVolume",.25m);f.Tick();Assert.That(f.Count==0||f.Last.Volume<=.25m,"DecimalsVolume inflated volume");});
    }
}
