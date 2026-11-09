using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
namespace OfflineStand;
public static partial class Cases
{
    public static readonly List<Scenario> All=new();
    static void Add(string module,string name,Action<TestContext> run) => All.Add(new(module,name,run));
    public static void Register() { Lifecycle();Execution();Reentrancy();Planning();Budgets();Rounding();InvalidInput();Registry();Diagnostics();Properties();BrokerProperties();BuyOffset();Shutdown(); }
}
public sealed record CaseResult(string Module,string Name,bool Passed,double DurationMs,string Error,string Trace,long PropertyEvents,long BrokerActiveSteps,int BrokerCommands,int BrokerFills);
public static class Program
{
    public static int Main(string[] args)
    {
        try { return Run(args); }
        catch(Exception e) { Console.Error.WriteLine("ERROR "+e.Message);return 2; }
    }
    static int Run(string[] args)
    {
        string modules="all",json=null,source=null;int seed=20261109,iterations=200;bool list=false,quiet=false;
        for(int i=0;i<args.Length;i++)
        {
            string Next() { if(++i>=args.Length)throw new ArgumentException("Missing value for "+args[i-1]);return args[i]; }
            switch(args[i])
            {
                case "--module":modules=Next();break;
                case "--seed":seed=int.Parse(Next());break;
                case "--iterations":iterations=int.Parse(Next());if(iterations<1||iterations>1000000)throw new ArgumentOutOfRangeException("iterations");break;
                case "--json":json=Next();break;
                case "--source":source=Next();break;
                case "--list":list=true;break;
                case "--quiet":quiet=true;break;
                case "--help":Console.WriteLine("--module all|execution,planning|name-substring --seed N --iterations N --json path --source path --list --quiet\n--source must match the source compiled via -p:RobotSource=path (run.ps1 handles this). Exit: 0 all pass; 1 test failures; 2 invalid command/source.");return 0;
                default:throw new ArgumentException("Unknown option: "+args[i]);
            }
        }
        if(source!=null)
        {
            string actual=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
            if(!actual.Equals(RobotIdentity.Hash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("--source hash differs from compiled robot. Rebuild with -p:RobotSource=path. compiled="+RobotIdentity.Hash+" actual="+actual);
        }
        Cases.Register();
        var filters=modules.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var selected=Cases.All.Where(x=>filters.Any(f=>f.Equals("all",StringComparison.OrdinalIgnoreCase)||x.Module.Contains(f,StringComparison.OrdinalIgnoreCase)||x.Name.Contains(f,StringComparison.OrdinalIgnoreCase))).ToList();
        if(selected.Count==0)throw new ArgumentException("No tests match --module "+modules);
        if(list) { foreach(var g in selected.GroupBy(x=>x.Module)){Console.WriteLine(g.Key+" ("+g.Count()+")");foreach(var t in g)Console.WriteLine("  "+t.Name);}return 0; }
        Console.WriteLine($"MarketMakerStakan3 OFFLINE stand | source SHA256={RobotIdentity.Hash} | seed={seed} iterations={iterations}");
        var results=new List<CaseResult>();var total=Stopwatch.StartNew();
        foreach(var test in selected)
        {
            using var context=new TestContext(seed,iterations);var watch=Stopwatch.StartNew();Exception error=null;
            try { test.Run(context);context.Verify(); }
            catch(Exception e) { error=e; }
            try { context.Dispose(); }catch(Exception cleanup){error=error==null?cleanup:new AggregateException(error,cleanup);}
            watch.Stop();
            var result=new CaseResult(test.Module,test.Name,error==null,watch.Elapsed.TotalMilliseconds,error?.ToString(),error==null?null:context.Trace,context.PropertyEventCount,context.BrokerActiveSteps,context.BrokerCommands,context.BrokerFills);
            results.Add(result);
            if(!quiet||error!=null)Console.WriteLine($"{(error==null?"PASS":"FAIL")} [{test.Module}] {test.Name}"+(error==null?"":"\n  "+error.Message));
        }
        var groups=results.GroupBy(x=>x.Module).Select(g=>new{module=g.Key,passed=g.Count(x=>x.Passed),failed=g.Count(x=>!x.Passed)}).ToArray();
        foreach(var g in groups)Console.WriteLine($"MODULE {g.module}: passed={g.passed} failed={g.failed}");
        int failed=results.Count(x=>!x.Passed);
        Console.WriteLine($"RESULT passed={results.Count-failed} failed={failed} total={results.Count} durationMs={total.Elapsed.TotalMilliseconds:F1} propertyEvents={results.Sum(x=>x.PropertyEvents)} brokerActiveSteps={results.Sum(x=>x.BrokerActiveSteps)} brokerCommands={results.Sum(x=>x.BrokerCommands)} brokerFills={results.Sum(x=>x.BrokerFills)}");
        if(json!=null)
        {
            var full=Path.GetFullPath(json);Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full,JsonSerializer.Serialize(new {schemaVersion=1,sourceSha256=RobotIdentity.Hash,seed,iterations,passed=results.Count-failed,failed,total=results.Count,durationMs=total.Elapsed.TotalMilliseconds,propertyEvents=results.Sum(x=>x.PropertyEvents),brokerActiveSteps=results.Sum(x=>x.BrokerActiveSteps),brokerCommands=results.Sum(x=>x.BrokerCommands),brokerFills=results.Sum(x=>x.BrokerFills),modules=groups,results},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("JSON "+full);
        }
        return failed==0?0:1;
    }
}



