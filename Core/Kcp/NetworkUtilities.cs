using System;
using System.Collections.Generic;
using Serilog;

namespace ServerApi.Unity.Utils
{
    /// <summary>
    /// Thread pool for multi-threading to handle network operations off the main thread.
    /// Supports configurable worker threads and task queue management.
    /// </summary>
    public class NetworkThreadPool : IDisposable
    {
        private readonly System.Threading.Thread[] workers;
        private readonly Queue<Action> taskQueue = new Queue<Action>();
        private readonly object queueLock = new object();
        private readonly int maxQueueSize;
        private bool isRunning = true;

        public NetworkThreadPool(int workerCount, int maxQueueSize)
        {
            this.maxQueueSize = maxQueueSize;
            workers = new System.Threading.Thread[workerCount];

            for (int i = 0; i < workerCount; i++)
            {
                workers[i] = new System.Threading.Thread(WorkerLoop)
                {
                    Name = $"NetworkWorker-{i}",
                    IsBackground = true
                };
                workers[i].Start();
            }

            Log.Debug($"NetworkThreadPool started with {workerCount} workers");
        }

        public bool TryEnqueue(Action task)
        {
            lock (queueLock)
            {
                if (taskQueue.Count >= maxQueueSize)
                {
                    Log.Warning("Thread pool queue is full, task rejected");
                    return false;
                }

                taskQueue.Enqueue(task);
                System.Threading.Monitor.Pulse(queueLock);
                return true;
            }
        }

        private void WorkerLoop()
        {
            while (isRunning)
            {
                Action? task = null;

                lock (queueLock)
                {
                    while (taskQueue.Count == 0 && isRunning)
                    {
                        System.Threading.Monitor.Wait(queueLock);
                    }

                    if (taskQueue.Count > 0)
                    {
                        task = taskQueue.Dequeue();
                    }
                }

                if (task != null)
                {
                    try
                    {
                        task();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Exception occurred in NetworkThreadPool");
                    }
                }
            }
        }

        public void Dispose()
        {
            isRunning = false;

            lock (queueLock)
            {
                System.Threading.Monitor.PulseAll(queueLock);
            }

            foreach (var worker in workers)
            {
                worker?.Join(1000);
            }

            Log.Verbose("NetworkThreadPool disposed");
        }
    }
}
