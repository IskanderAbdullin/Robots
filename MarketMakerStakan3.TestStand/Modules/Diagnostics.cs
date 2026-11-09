using OsEngine.Entity;
namespace OfflineStand;
public static partial class Cases
{
    static void Diagnostics()
    {
        Add("diagnostics","Detailed diagnostic logging defaults Off",c=>{var f=c.New();f.Tick();Assert.Equal(0,f.Robot.Logs.Count(x=>x.Contains("MM3DIAG")));});
        Add("diagnostics","Enabled diagnostics explain plan and outgoing request",c=>{var f=c.New();f.String("_diagnostics","On");f.Tick();var lines=f.Robot.Logs.Where(x=>x.Contains("MM3DIAG")).ToArray();Assert.That(lines.Any(x=>x.Contains("MarketDepth")&&x.Contains("bid=")&&x.Contains("ask=")&&x.Contains("plan=")),"missing input/plan record");Assert.That(lines.Any(x=>x.Contains("Place request")),"missing request reason");Assert.That(lines.Any(x=>x.Contains("Place call returned")&&x.Contains("user=")),"missing returned order identity");});
        Add("diagnostics","Acknowledgments include state fill and remaining identity",c=>{var f=c.New();f.String("_diagnostics","On");f.Tick();f.Ack(f.First,OrderStateType.Partial,3);Assert.That(f.Robot.Logs.Any(x=>x.Contains("OrderUpdate")&&x.Contains("executed=3")&&x.Contains("rest=7")),"missing confirmation fields");});
        Add("diagnostics","Diagnostic window expires after five virtual minutes",c=>{var f=c.New();f.String("_diagnostics","On");f.Tick();f.Advance(TimeSpan.FromMinutes(6));int prior=f.Robot.Logs.Count(x=>x.Contains("MM3DIAG"));f.Tick();f.Ack(f.First,OrderStateType.Active);Assert.Equal(prior,f.Robot.Logs.Count(x=>x.Contains("MM3DIAG")));});
        Add("diagnostics","Off then On explicitly starts new diagnostic window",c=>{var f=c.New();f.String("_diagnostics","On");f.Tick();f.Advance(TimeSpan.FromMinutes(6));f.String("_diagnostics","Off");f.Ack(f.First,OrderStateType.Active);f.String("_diagnostics","On");int prior=f.Robot.Logs.Count(x=>x.Contains("MM3DIAG"));f.Ack(f.First,OrderStateType.Partial,1);Assert.That(f.Robot.Logs.Count(x=>x.Contains("MM3DIAG"))>prior,"diagnostic window did not restart");});
        Add("diagnostics","Repeated errors log once while requests remain blocked",c=>{var f=c.New();f.Tick();f.Ack(f.First,OrderStateType.Fail);for(int i=0;i<20;i++)f.Tick();Assert.Equal(1,f.Robot.Logs.Count(x=>x.Contains("Execution Error")));Assert.Equal(1,f.Count);});
        Add("diagnostics","Portfolio unavailable warnings are rate limited",c=>{var f=c.New();f.String("_buyVolumeMode",Fixture.Percent);f.Tab.Portfolio=null;for(int i=0;i<10;i++)f.Tick();Assert.Equal(1,f.Robot.Logs.Count(x=>x.Contains("Портфель недоступен")));f.Advance(TimeSpan.FromSeconds(61));f.Tick();Assert.Equal(2,f.Robot.Logs.Count(x=>x.Contains("Портфель недоступен")));});
    }
}
