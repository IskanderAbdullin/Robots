/*
 * Your rights to use code governed by this license https://github.com/AlexWan/OsEngine/blob/master/LICENSE
 * Ваши права на использование кода регулируются данной лицензией http://o-s-a.net/doc/license_simple_engine.pdf
*/

using OsEngine.Logging;
using OsEngine.Market;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OsEngine
{
    public static class RobotShutdownCoordinator
    {
        private static readonly object Sync = new object();
        private static Task<bool> _preparingTask;
        private static Task _runningCallbacks = Task.CompletedTask;

        public static event Func<Task> BeforeDisconnectEvent;

        public static Task<bool> PrepareAsync(string reason, TimeSpan? timeout = null)
        {
            lock (Sync)
            {
                if (_preparingTask == null || (_preparingTask.IsCompleted && _runningCallbacks.IsCompleted))
                {
                    _preparingTask = PrepareCoreAsync(reason, timeout ?? TimeSpan.FromSeconds(8));
                }

                return _preparingTask;
            }
        }

        private static async Task<bool> PrepareCoreAsync(string reason, TimeSpan timeout)
        {
            Func<Task> handlers = BeforeDisconnectEvent;
            if (handlers == null)
            {
                return true;
            }

            List<Task<bool>> tasks = new List<Task<bool>>();
            foreach (Delegate subscriber in handlers.GetInvocationList())
            {
                Func<Task> handler = (Func<Task>)subscriber;
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        Task task = handler();
                        if (task == null)
                        {
                            throw new InvalidOperationException("Shutdown participant returned no completion task.");
                        }

                        await task.ConfigureAwait(false);
                        return true;
                    }
                    catch (Exception error)
                    {
                        ServerMaster.SendNewLogMessage("Robot shutdown failed (" + reason + "): " + error, LogMessageType.Error);
                        return false;
                    }
                }));
            }

            Task<bool[]> all = Task.WhenAll(tasks);
            _runningCallbacks = all;
            if (await Task.WhenAny(all, Task.Delay(timeout)).ConfigureAwait(false) != all)
            {
                ServerMaster.SendNewLogMessage("Robot shutdown timed out (" + reason + "). Close cancelled; connections remain available for reconciliation.", LogMessageType.Error);
                return false;
            }

            bool[] results = await all.ConfigureAwait(false);
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i])
                {
                    return false;
                }
            }

            if (!Equals(handlers, BeforeDisconnectEvent))
            {
                ServerMaster.SendNewLogMessage("Robot shutdown participants changed (" + reason + "). Close cancelled; retry after reconciliation.", LogMessageType.Error);
                return false;
            }

            return true;
        }
    }
}
