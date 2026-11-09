using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void BuyOffset()
    {
        foreach(string mode in new[]{Fixture.Distributed,Fixture.Single})
        {
            foreach(int steps in new[]{0,1,2,3})
            {
                string selected=mode;int gap=steps;
                Add("offset",selected+" empty ticks="+gap,c=>
                {
                    var f=c.New();f.String("_buyMode",selected);f.Int("_buyOffsetSteps",gap);
                    var p=f.Plan(Fixture.Book(100,101));
                    if(gap==3)Assert.Equal(0,p.Count);
                    else Assert.Equal(100m-gap,p[0].Price);
                });
            }
            string captured=mode;
            Add("offset",captured+" existing wide spread is not shifted",c=>
            {
                var f=c.New();f.String("_buyMode",captured);f.Int("_buyOffsetSteps",2);
                Assert.Equal(100m,f.Plan(Fixture.Book(100,105))[0].Price);
            });
        }
        Add("offset","Fractional price step leaves two empty ticks",c=>
        {
            var f=c.New();f.Tab.Security.PriceStep=.25m;f.Tab.Security.Decimals=2;f.Int("_buyOffsetSteps",2);
            var d=Fixture.Book(100,100.25);d.Bids[1].Price=99.75;d.Bids[2].Price=99.5;
            Assert.Equal(99.5m,f.Plan(d)[0].Price);
        });
        Add("offset","Single mode chooses maximum among eligible levels",c=>
        {
            var f=c.New();f.String("_buyMode",Fixture.Single);f.Int("_buyOffsetSteps",1);
            var d=Fixture.Book(100,101);d.Bids[0].Bid=1000;d.Bids[2].Bid=500;
            Assert.Equal(98m,f.Plan(d)[0].Price);
        });
        Add("offset","Sell plan unaffected by buy offset",c=>
        {
            var f=c.New(100);var d=Fixture.Book(100,101);
            var before=f.Plan(d).Where(x=>x.Side==Side.Sell).ToArray();f.Int("_buyOffsetSteps",2);
            Assert.That(before.SequenceEqual(f.Plan(d).Where(x=>x.Side==Side.Sell)),"sell changed");
        });
        Add("offset","Specified-volume mode ignores offset",c=>
        {
            var f=c.New();f.String("_buyMode",Fixture.Specified);f.Decimal("_specifiedOrderVolume",50);f.Int("_buyOffsetSteps",10000);
            Assert.Equal(101m,f.Plan(Fixture.Book(100,105))[0].Price);
        });
        Add("offset","Wall mode ignores offset",c=>
        {
            var f=WallFixture(c);f.Int("_confirmTicks",1);f.Int("_buyOffsetSteps",10000);
            Assert.Equal(101m,f.Plan(WallBook())[0].Price);
        });
    }
}
