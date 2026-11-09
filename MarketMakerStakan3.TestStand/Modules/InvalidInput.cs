using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void InvalidInput()
    {
        Add("invalid","Null depth is ignored and later valid depth recovers",c=>{var f=c.New();f.RawTick(null);Assert.Equal(0,f.Count);Assert.That(!f.Error,"null caused Error");f.Tick();Assert.Equal(1,f.Count);});
        Add("invalid","Empty and one-sided books cannot place",c=>{foreach(var d in new[]{new MarketDepth(),new MarketDepth{Bids=null},new MarketDepth{Asks=null},new MarketDepth{Bids=Fixture.Book().Bids},new MarketDepth{Asks=Fixture.Book().Asks}}){var f=c.New();f.RawTick(d);Assert.Equal(0,f.Count);}});
        Add("invalid","Null level is skipped without permanent Error",c=>{var f=c.New();var d=Fixture.Book();d.Bids[0]=null;f.Tick(d);Assert.Equal(0,f.Count);Assert.That(!f.Error,"null level disabled robot");f.Tick();Assert.Equal(1,f.Count);});
        Add("invalid","NaN and infinite prices are ignored",c=>{foreach(var value in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}){var f=c.New();f.Tick(Fixture.Book(value,102));Assert.Equal(0,f.Count);Assert.That(!f.Error,"non-finite price caused Error");}});
        Add("invalid","Nonrepresentable finite decimal prices are ignored",c=>{var f=c.New();f.Tick(Fixture.Book(100,double.MaxValue));Assert.Equal(0,f.Count);Assert.That(!f.Error,"decimal overflow caused permanent Error");f.Tick();Assert.Equal(1,f.Count);});
        Add("invalid","Zero and negative prices cannot place",c=>{foreach(var value in new[]{0d,-1d}){var f=c.New();f.Tick(Fixture.Book(value,102));Assert.Equal(0,f.Count);}});
        Add("invalid","NaN infinite and negative level quantities cannot place",c=>{foreach(var value in new[]{double.NaN,double.PositiveInfinity,-1d}){var f=c.New();var d=Fixture.Book();d.Asks[0].Ask=value;f.Tick(d);Assert.Equal(0,f.Count);Assert.That(!f.Error,"bad volume caused Error");}});
        Add("invalid","Nonrepresentable finite decimal quantity is ignored",c=>{var f=c.New();var d=Fixture.Book();d.Bids[0].Bid=double.MaxValue;f.Tick(d);Assert.Equal(0,f.Count);Assert.That(!f.Error,"volume overflow caused Error");});
        Add("invalid","Crossed and locked raw books cannot place",c=>{foreach(var bid in new[]{102d,103d}){var f=c.New();f.Tick(Fixture.Book(bid,102));Assert.Equal(0,f.Count);}});
        Add("invalid","Unsorted and duplicate raw levels cannot place",c=>{foreach(var side in new[]{Side.Buy,Side.Sell}){var f=c.New();var d=Fixture.Book();if(side==Side.Buy)d.Bids[1].Price=100;else d.Asks[1].Price=102;f.Tick(d);Assert.Equal(0,f.Count);}});
        Add("invalid","Missing security blocks planning",c=>{var f=c.New();f.Tab.Security=null;f.Tick();Assert.Equal(0,f.Count);Assert.That(!f.Error,"missing security caused Error");});
        Add("invalid","Nonpositive volume step cannot send",c=>{foreach(var step in new[]{0m,-1m}){var f=c.New();f.Tab.Security.VolumeStep=step;f.Tick();Assert.Equal(0,f.Count);}});
        Add("invalid","Nonpositive price step cannot send",c=>{foreach(var step in new[]{0m,-1m}){var f=c.New();f.Tab.Security.PriceStep=step;f.Tick();Assert.Equal(0,f.Count);}});
        Add("invalid","Disconnected connector cannot send and reconnect recovers",c=>{var f=c.New();f.Live();f.Tab.IsConnected=false;f.Tick();Assert.Equal(0,f.Count);f.Tab.IsConnected=true;f.Tick();Assert.Equal(1,f.Count);});
        Add("invalid","Not-ready tab cannot send",c=>{var f=c.New();f.Live();f.Tab.IsReadyToTrade=false;f.Tick();Assert.Equal(0,f.Count);});
        Add("invalid","Single-level canonical rounded buy cannot cross best ask",c=>{var f=c.New();f.String("_buyMode",Fixture.Single);f.Tick(Fixture.Book(100.6,100.9));Assert.Equal(0,f.Count);});
        Add("invalid","Distributed canonical rounded buy cannot cross best ask",c=>{var f=c.New();f.Tick(Fixture.Book(100.6,100.9));Assert.That(f.Tab.Commands.All(x=>x.Kind!="Place"||x.Side!=Side.Buy||x.Price<100.9m),"rounded passive order crossed ask");});
        Add("invalid","Canonical zero price is never sent",c=>{var f=c.New();f.Tick(Fixture.Book(.1,1.1));Assert.That(f.Tab.Commands.All(x=>x.Price>0),"zero price sent");});
        Add("invalid","Unknown or malformed regime cannot place",c=>{var f=c.New();f.String("_regime","Unknown");f.Tick();Assert.Equal(0,f.Count);});
    }
}

