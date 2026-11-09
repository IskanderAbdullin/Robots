using System.Collections.Concurrent;
namespace OsEngine.Logging { public enum LogMessageType { Error, System, Trade } }
namespace OsEngine.Market
{
    public static class ServerMaster
    {
        public static readonly ConcurrentQueue<string> Logs = new();
        public static void SendNewLogMessage(string message, OsEngine.Logging.LogMessageType type) => Logs.Enqueue(type+":"+message);
    }
}
