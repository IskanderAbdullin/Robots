using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

return await Runner.MainAsync(args);

public sealed class Settings
{
    public string Ticker { get; set; } = "ANTperp";
    public string AccountId { get; set; } = "";
    public string TokenEnv { get; set; } = "TINVEST_TOKEN";
    public string InstrumentUid { get; set; } = "";
    public string PriceType { get; set; } = "PRICE_TYPE_POINT";
    public decimal MinPrice { get; set; } = 70m;
    public decimal MaxPrice { get; set; } = 95m;
    public decimal PriceStep { get; set; } = 1m;
    public int TotalLots { get; set; } = 395;
    public int MaxPostRequestsPerSecond { get; set; } = 13;
    public int PositionLimitLots { get; set; } = 395;
    public int PollMs { get; set; } = 300;
    public int DiscoveryPollMs { get; set; } = 2000;
    public int BookDepth { get; set; } = 1;
    public bool RequireBook { get; set; } = true;
    public bool LiveEnabled { get; set; }
    public bool AllowCrossingSpread { get; set; } = true;
    public string JournalPath { get; set; } = "state/launch-journal.jsonl";
    public List<Level> Levels { get; set; } = [];
}
public sealed record Level(decimal Price, int Lots);
public static class Grid
{
    public static List<Level> Build(Settings c)
    {
        if(c.MinPrice<=0 || c.PriceStep<=0 || c.MaxPrice<c.MinPrice || c.TotalLots<=0 ||
           (c.MaxPrice-c.MinPrice)/c.PriceStep > 999 || (c.MaxPrice-c.MinPrice)%c.PriceStep!=0)
            throw new ArgumentException("Invalid range/step/volume.");
        var result=c.Levels.Count>0 ? c.Levels.ToList() : new List<Level>();
        if(result.Count==0)
        {
            int n=checked((int)((c.MaxPrice-c.MinPrice)/c.PriceStep)+1);
            if(c.TotalLots<n) throw new ArgumentException("Lots must cover every level.");
            int baseLots=c.TotalLots/n, extra=c.TotalLots%n;
            for(int i=0;i<n;i++) result.Add(new Level(c.MinPrice+i*c.PriceStep,baseLots+(i<extra?1:0)));
        }
        if(result.Count>1000 || result.Any(x=>x.Lots<=0 || x.Price<=0) ||
           result.Select(x=>x.Price).Distinct().Count()!=result.Count ||
           result.Sum(x=>x.Lots)>c.PositionLimitLots)
            throw new ArgumentException("Invalid custom levels or exceeded position limit.");
        if(c.MaxPostRequestsPerSecond is <1 or >13) throw new ArgumentException("MaxPostRequestsPerSecond must be 1..13.");
        if(c.PollMs<100 || c.DiscoveryPollMs<500 || c.BookDepth is <1 or >50)
            throw new ArgumentException("Unsafe polling/depth.");
        if(c.Ticker!="ANTperp") throw new ArgumentException("Ticker identity guard: expected ANTperp.");
        if(c.PriceType is not ("PRICE_TYPE_POINT" or "PRICE_TYPE_CURRENCY"))
            throw new ArgumentException("Unsupported priceType.");
        if(string.IsNullOrWhiteSpace(c.JournalPath)) throw new ArgumentException("Journal path required.");
        return result.OrderBy(x=>x.Price).ToList();
    }
}
public sealed class Api : IDisposable
{
    private readonly HttpClient _http;
    private const string Base="https://invest-public-api.tbank.ru/rest/tinkoff.public.invest.api.contract.v1.";
    public Api(string token)
    {
        _http=new HttpClient{Timeout=TimeSpan.FromSeconds(3)};
        _http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);
    }
    public async Task<JsonDocument> Call(string endpoint,object request,CancellationToken ct)
    {
        using var response=await _http.PostAsJsonAsync(Base+endpoint,request,Runner.Json,ct);
        string body=await response.Content.ReadAsStringAsync(ct);
        if(!response.IsSuccessStatusCode)
            throw new HttpRequestException(endpoint+": HTTP "+(int)response.StatusCode+" "+body[..Math.Min(body.Length,350)],null,response.StatusCode);
        return JsonDocument.Parse(body);
    }
    public void Dispose()=>_http.Dispose();
}
public static class Runner
{
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web) {WriteIndented=false,PropertyNameCaseInsensitive=true};
    static readonly object JournalLock=new();
    public static async Task<int> MainAsync(string[] args)
    {
        try
        {
            string command=args.Length>0?args[0]:"plan";
            string path=args.Length>1?args[1]:"config.json";
            if(command=="self-test") {Tests();return 0;}
            if(!File.Exists(path)) {Console.Error.WriteLine("Config missing: "+path+"; copy config.example.json.");return 2;}
            var c=System.Text.Json.JsonSerializer.Deserialize<Settings>(await File.ReadAllTextAsync(path),Json)??throw new Exception("Invalid config");
            var levels=Grid.Build(c);
            decimal notional=levels.Sum(x=>x.Price*x.Lots);
            Console.WriteLine($"ANTperp | {levels.Count} BUY levels | {levels.Sum(x=>x.Lots)} lots | price*lots {notional} | cap {c.MaxPostRequestsPerSecond}/s");
            foreach(var l in levels) Console.WriteLine($"{l.Price,6:F2} : {l.Lots} lots");
            if(command=="plan") return 0;
            if(command is not ("watch" or "live")) throw new ArgumentException("Commands: plan, watch, live, self-test");
            if(command=="live" && (!c.LiveEnabled || !args.Contains("--confirm-live")))
                throw new ArgumentException("Live requires liveEnabled:true AND --confirm-live.");
            if(command=="live" && string.IsNullOrWhiteSpace(c.AccountId))
                throw new ArgumentException("Live requires explicit accountId.");
            string token=Environment.GetEnvironmentVariable(c.TokenEnv)??"";
            if(string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Missing token env "+c.TokenEnv);
            using var api=new Api(token);
            using var cancel=new CancellationTokenSource();
            Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
            await Run(c,levels,api,command=="live",cancel.Token);
            return 0;
        }
        catch(OperationCanceledException) {Console.WriteLine("Stopped. Existing orders are NOT cancelled.");return 0;}
        catch(Exception ex) {Console.Error.WriteLine("STOP: "+ex.Message);return 1;}
    }
    static string Get(JsonElement e,string key,string fallback="")
        =>e.TryGetProperty(key,out var v)?v.ToString():fallback;
    static JsonElement GetObject(JsonElement e,string key)
        =>e.TryGetProperty(key,out var v)?v:default;
    static bool Flag(JsonElement e,string key)
        =>e.TryGetProperty(key,out var v) && v.ValueKind==JsonValueKind.True;
    static bool ValidStatus(string status)=>status is "SECURITY_TRADING_STATUS_NORMAL_TRADING" or "SECURITY_TRADING_STATUS_DEALER_NORMAL_TRADING";
    static async Task Run(Settings c,List<Level> levels,Api api,bool live,CancellationToken ct)
    {
        string uid=c.InstrumentUid;
        while(string.IsNullOrWhiteSpace(uid))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var d=await api.Call("InstrumentsService/FindInstrument",new {query=c.Ticker},ct);
                var instruments=GetObject(d.RootElement,"instruments");
                if(instruments.ValueKind==JsonValueKind.Array)
                    foreach(var item in instruments.EnumerateArray())
                        if(Get(item,"ticker").Equals(c.Ticker,StringComparison.OrdinalIgnoreCase) &&
                           Get(item,"uid").Length>0)
                        {uid=Get(item,"uid");Console.WriteLine("Discovered "+c.Ticker+" uid="+uid);break;}
            }
            catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.NotFound) {}
            if(uid.Length==0) await Task.Delay(c.DiscoveryPollMs,ct);
        }
        Console.WriteLine("Waiting for trading status + order book. LIVE="+live);
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var s=await api.Call("MarketDataService/GetTradingStatus",new {instrumentId=uid},ct);
                var status=Get(s.RootElement,"tradingStatus");
                bool allowed=Flag(s.RootElement,"limitOrderAvailableFlag") && Flag(s.RootElement,"apiTradeAvailableFlag");
                if(ValidStatus(status)&&allowed)
                {
                    using var b=await api.Call("MarketDataService/GetOrderBook",new {instrumentId=uid,depth=c.BookDepth},ct);
                    var bids=GetObject(b.RootElement,"bids");
                    var asks=GetObject(b.RootElement,"asks");
                    bool hasBook= bids.ValueKind==JsonValueKind.Array && asks.ValueKind==JsonValueKind.Array &&
                        (bids.GetArrayLength()>0 || asks.GetArrayLength()>0);
                    if(hasBook || !c.RequireBook)
                    {
                        Console.WriteLine("Trade trigger: "+status+" book="+hasBook);
                        await Submit(c,levels,uid,api,live,ct);
                        return;
                    }
                }
            }
            catch(HttpRequestException ex) when(ex.StatusCode==HttpStatusCode.NotFound ||
                 ex.StatusCode==HttpStatusCode.ServiceUnavailable)
            {Console.WriteLine("Waiting: "+ex.Message);}
            await Task.Delay(c.PollMs,ct);
        }
    }
    static object Quotation(decimal price)
    {
        long units=decimal.ToInt64(decimal.Truncate(price));
        int nano=decimal.ToInt32((price-units)*1_000_000_000m);
        return new {units=units.ToString(CultureInfo.InvariantCulture),nano};
    }
    static void WriteJournal(string path,object record)
    {
        lock(JournalLock)
        {
            string? dir=Path.GetDirectoryName(Path.GetFullPath(path));
            if(dir!=null) Directory.CreateDirectory(dir);
            using var fs=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read);
            using var writer=new StreamWriter(fs);
            writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(record,Json));
            writer.Flush();fs.Flush(true);
        }
    }
    static HashSet<decimal> PreviouslyAttempted(string path)
    {
        var attempted=new HashSet<decimal>();
        if(!File.Exists(path)) return attempted;
        foreach(var line in File.ReadLines(path))
        {
            try
            {
                using var json=JsonDocument.Parse(line);
                if(Get(json.RootElement,"event")=="attempt" &&
                   decimal.TryParse(Get(json.RootElement,"price"),NumberStyles.Number,CultureInfo.InvariantCulture,out var p))
                    attempted.Add(p);
            }
            catch(JsonException) {throw new InvalidOperationException("Corrupted journal; refusing live orders.");}
        }
        return attempted;
    }
    static async Task Submit(Settings c,List<Level> levels,string uid,Api api,bool live,CancellationToken ct)
    {
        var previous=PreviouslyAttempted(c.JournalPath);
        if(previous.Count>0) Console.WriteLine("Prior attempts in journal: "+previous.Count+"; never automatically retry.");
        // Strict lower bound: >= 1 second between any N+1 submissions.
        var starts=new Queue<long>();
        var clock=Stopwatch.StartNew();
        foreach(var level in levels)
        {
            ct.ThrowIfCancellationRequested();
            if(previous.Contains(level.Price)) {Console.WriteLine("SKIP previous attempt "+level.Price);continue;}
            while(starts.Count>0 && clock.ElapsedMilliseconds-starts.Peek()>=1000) starts.Dequeue();
            if(starts.Count>=c.MaxPostRequestsPerSecond)
                await Task.Delay((int)Math.Max(1,1000-(clock.ElapsedMilliseconds-starts.Peek())),ct);
            while(starts.Count>0 && clock.ElapsedMilliseconds-starts.Peek()>=1000) starts.Dequeue();
            starts.Enqueue(clock.ElapsedMilliseconds);
            if(!live) {Console.WriteLine("SIMULATED BUY "+level.Lots+" @ "+level.Price);continue;}
            var requestId=Guid.NewGuid().ToString();
            WriteJournal(c.JournalPath,new {eventName="unused",@event="attempt",time=DateTimeOffset.UtcNow,uid,price=level.Price,lots=level.Lots,requestId});
            try
            {
                using var result=await api.Call("OrdersService/PostOrder",new {
                    instrumentId=uid,quantity=level.Lots.ToString(CultureInfo.InvariantCulture),
                    price=Quotation(level.Price),direction="ORDER_DIRECTION_BUY",
                    accountId=c.AccountId,orderType="ORDER_TYPE_LIMIT",orderId=requestId,
                    timeInForce="TIME_IN_FORCE_DAY",priceType=c.PriceType,confirmMarginTrade=false
                },ct);
                WriteJournal(c.JournalPath,new {@event="accepted",time=DateTimeOffset.UtcNow,price=level.Price,requestId,
                    brokerOrderId=Get(result.RootElement,"orderId"),status=Get(result.RootElement,"executionReportStatus")});
                Console.WriteLine("POSTED "+level.Lots+" @ "+level.Price);
            }
            catch(Exception ex)
            {
                WriteJournal(c.JournalPath,new {@event="unknown-or-rejected",time=DateTimeOffset.UtcNow,price=level.Price,requestId,
                    error=ex.Message});
                // Never retry uncertain submission automatically, never go to next level after a failure.
                throw new InvalidOperationException("Submission failed/uncertain at "+level.Price+"; STOP; reconcile by requestId="+requestId,ex);
            }
        }
    }
    static void Tests()
    {
        var c=new Settings();
        var levels=Grid.Build(c);
        if(levels.Count!=26 || levels.Sum(x=>x.Lots)!=395 || levels[0]!=new Level(70,16) ||
           levels[4]!=new Level(74,16) || levels[5]!=new Level(75,15) || levels[^1]!=new Level(95,15))
            throw new Exception("Grid test failed");
        c.Levels=[new Level(70,100),new Level(75,100)];
        if(Grid.Build(c).Count!=2) throw new Exception("Custom levels test failed");
        Console.WriteLine("Self tests PASS");
    }
}
