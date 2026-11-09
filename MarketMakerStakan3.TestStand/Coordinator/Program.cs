using OsEngine;
using OsEngine.Market;
static class Program
{
    static int passed,failed;
    static readonly TimeSpan Bound=TimeSpan.FromSeconds(3), Deadline=TimeSpan.FromMilliseconds(30);
    static TaskCompletionSource<bool> Completion()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    static bool Result(Task<bool> task){Check(task.Wait(Bound),"coordinator did not finish within bounded test wait");return task.GetAwaiter().GetResult();}
    static void EnsureIdle()
    {
        var task=typeof(RobotShutdownCoordinator).GetField("_runningCallbacks",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)?.GetValue(null) as Task;
        if(task!=null)Check(task.Wait(Bound),"participant callbacks leaked into later scenario");
    }
    static void Test(string name,Action run)
    {
        while(ServerMaster.Logs.TryDequeue(out _)){}
        try {run();EnsureIdle();Console.WriteLine("PASS "+name);passed++;}
        catch(Exception e){Console.WriteLine("FAIL "+name+": "+e);failed++;}
    }
    static void Main()
    {
        Test("No participants completes successfully",()=>Check(Result(RobotShutdownCoordinator.PrepareAsync("empty",Deadline)),"empty shutdown failed"));
        Test("Completed participant is awaited once",()=>
        {
            int calls=0;Func<Task> handler=()=>{Interlocked.Increment(ref calls);return Task.CompletedTask;};RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(Result(RobotShutdownCoordinator.PrepareAsync("one",Deadline)),"participant failed");Check(calls==1,"wrong invocation count");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Participants start in parallel and all confirmations are awaited",()=>
        {
            var release=Completion();using var started=new CountdownEvent(2);Func<Task> a=()=>{started.Signal();return release.Task;};Func<Task> b=()=>{started.Signal();return release.Task;};
            RobotShutdownCoordinator.BeforeDisconnectEvent+=a;RobotShutdownCoordinator.BeforeDisconnectEvent+=b;
            try{var task=RobotShutdownCoordinator.PrepareAsync("parallel",Bound);Check(started.Wait(Bound),"second participant was not started concurrently");Check(!task.IsCompleted,"returned before confirmations");release.SetResult(true);Check(Result(task),"confirmed participants did not succeed");}
            finally{release.TrySetResult(true);RobotShutdownCoordinator.BeforeDisconnectEvent-=a;RobotShutdownCoordinator.BeforeDisconnectEvent-=b;}
        });
        Test("One synchronous exception cancels close and includes reason",()=>
        {
            Func<Task> handler=()=>throw new InvalidOperationException("synthetic shutdown failure");RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("exception-test",Bound)),"exception reported success");Check(ServerMaster.Logs.Any(x=>x.Contains("exception-test")&&x.Contains("synthetic shutdown failure")),"reason/error missing");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Faulted asynchronous participant cancels close",()=>
        {
            Func<Task> handler=()=>Task.FromException(new IOException("async failure"));RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("faulted",Bound)),"fault reported success");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Canceled participant cancels close",()=>
        {
            Func<Task> handler=()=>Task.FromCanceled(new CancellationToken(true));RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("cancelled",Bound)),"canceled task reported success");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Null completion task is rejected",()=>
        {
            Func<Task> handler=()=>null;RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("null-task",Bound)),"null task reported success");Check(ServerMaster.Logs.Any(x=>x.Contains("no completion task")),"null task diagnosis missing");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Deadline cancels close without pretending participant completion",()=>
        {
            var release=Completion();Func<Task> handler=()=>release.Task;RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("deadline-test",Deadline)),"deadline reported success");Check(!release.Task.IsCompleted,"coordinator fabricated participant confirmation");Check(ServerMaster.Logs.Any(x=>x.Contains("deadline-test")&&x.Contains("connections remain available")),"timeout did not explain retained connections");}
            finally{release.TrySetResult(true);RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Concurrent preparation callers share one execution",()=>
        {
            var release=Completion();int calls=0;Func<Task> handler=()=>{Interlocked.Increment(ref calls);return release.Task;};RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{var a=RobotShutdownCoordinator.PrepareAsync("first",Bound);var b=RobotShutdownCoordinator.PrepareAsync("second",Bound);Check(ReferenceEquals(a,b),"concurrent preparation duplicated task");release.SetResult(true);Check(Result(a)&&Result(b),"shared completion failed");Check(calls==1,"participant invoked twice");}
            finally{release.TrySetResult(true);RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Retry never overlaps unfinished callbacks and can resume after completion",()=>
        {
            var first=Completion();int calls=0;Func<Task> handler=()=>Interlocked.Increment(ref calls)==1?first.Task:Task.CompletedTask;RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("first-attempt",Deadline)),"first attempt should timeout");Check(!Result(RobotShutdownCoordinator.PrepareAsync("while-unfinished",Bound)),"unfinished callbacks should not become successful");Check(calls==1,"retry overlapped unfinished callback");first.SetResult(true);EnsureIdle();Check(Result(RobotShutdownCoordinator.PrepareAsync("retry-after-completion",Bound)),"retry did not reevaluate completed participants");Check(calls==2,"retry missing after previous callback finished");}
            finally{first.TrySetResult(true);RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;}
        });
        Test("Failed participant does not prevent another participant from running",()=>
        {
            int ok=0;Func<Task> bad=()=>throw new Exception("failure");Func<Task> good=()=>{Interlocked.Increment(ref ok);return Task.CompletedTask;};RobotShutdownCoordinator.BeforeDisconnectEvent+=bad;RobotShutdownCoordinator.BeforeDisconnectEvent+=good;
            try{Check(!Result(RobotShutdownCoordinator.PrepareAsync("mixed",Bound)),"mixed failure reported success");Check(ok==1,"other participant skipped");}finally{RobotShutdownCoordinator.BeforeDisconnectEvent-=bad;RobotShutdownCoordinator.BeforeDisconnectEvent-=good;}
        });
        Test("Removed participants are not called on later close",()=>
        {
            int calls=0;Func<Task> handler=()=>{Interlocked.Increment(ref calls);return Task.CompletedTask;};RobotShutdownCoordinator.BeforeDisconnectEvent+=handler;RobotShutdownCoordinator.BeforeDisconnectEvent-=handler;
            Check(Result(RobotShutdownCoordinator.PrepareAsync("removed",Deadline)),"empty retry failed");Check(calls==0,"removed handler invoked");
        });
        Console.WriteLine($"RESULT passed={passed} failed={failed}");Environment.ExitCode=failed==0?0:1;
    }
}

