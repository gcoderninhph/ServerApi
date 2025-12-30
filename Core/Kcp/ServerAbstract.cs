using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ServerApi.Unity.Utils;
using Google.Protobuf;
using ServerApi.Unity.Server;
using ServerApi.Protos;
using System.Linq;
using Serilog;
using System.Buffers;

namespace ServerApi.Unity.Abstractions
{
    public abstract class ServerAbstract : IServer
    {
        private readonly bool useMainThread;
        private readonly NetworkThreadPool? threadPool;
        private readonly Dictionary<string, HandlerInfo> handlers;
        private readonly ConcurrentDictionary<Type, (object? Parser, System.Reflection.MethodInfo? ParseMethod)> parserCache;
        private readonly ConcurrentDictionary<string, IConnection> connections;
        private readonly object handlersLock;

        public abstract bool IsRunning { get; }
        public abstract void DispatchMessages();

        // Implementation of IUnityServerApiRegistrar methods would go here
        private readonly List<Action<IConnection>> connectHandlers;
        private readonly List<Action<IConnection>> disconnectHandlers;

        public ServerAbstract(bool useMainThread, int threadPoolSize, int maxQueueSize)
        {
            this.useMainThread = useMainThread;
            if (!useMainThread)
                threadPool = new(threadPoolSize, maxQueueSize);
            handlers = new();
            parserCache = new();
            connections = new();
            handlersLock = new();
            connectHandlers = new();
            disconnectHandlers = new();
        }


        public abstract void Start();
        public bool TryGetConnectionById(string connectionId, out IConnection? connection)
        {
            return connections.TryGetValue(connectionId, out connection);
        }

        public void OnConnected(Action<IConnection> handler)
        {
            if (handler != null)
            {
                connectHandlers.Add(handler);
            }
        }

        public void OnDisconnected(Action<IConnection> handler)
        {
            if (handler != null)
            {
                disconnectHandlers.Add(handler);
            }
        }

        protected void InvokeOnConnected(IConnection connection)
        {
            _ = RunWithThread(() =>
            {
                foreach (var handler in connectHandlers)
                {
                    try
                    {
                        handler.Invoke(connection);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in OnConnected handler: {ex.Message}");
                    }
                }
                return Task.CompletedTask;
            });
            return;
        }

        protected void InvokeOnDisconnected(IConnection connection)
        {
            _ = RunWithThread(() =>
            {
                foreach (var handler in disconnectHandlers)
                {
                    try
                    {
                        handler.Invoke(connection);
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in OnDisconnected handler: {ex.Message}");
                    }
                }
                return Task.CompletedTask;
            });
        }

        protected void ClientConnection(IConnection connection)
        {
            connections.TryAdd(connection.ConnectionId, connection);
            InvokeOnConnected(connection);
        }

        protected void ClientDisconnection(IConnection connection)
        {
            connections.TryRemove(connection.ConnectionId, out _);
            InvokeOnDisconnected(connection);
        }

        public IUnityBroadcastResponder<TBody> GetBroadcaster<TBody>(string commandId)
            where TBody : class, IMessage<TBody>, new()
        {
            return new UnityBroadcastResponder<TBody>(
                commandId,
                SendToClientAsync,
                BroadcastToAllAsync);
        }

        public void Handle<TRequest, TResponse>(
            string commandId,
            Func<IUnityContext, TRequest, IUnityResponder<TResponse>, Task> handler)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new()
        {
            if (string.IsNullOrEmpty(commandId))
                throw new ArgumentException("Command ID cannot be null or empty", nameof(commandId));

            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            // Lock to ensure thread-safe registration
            lock (handlersLock)
            {
                if (handlers.ContainsKey(commandId))
                {
                    Log.Warning($"Handler for command '{commandId}' already registered, overwriting");
                }

                handlers[commandId] = new HandlerInfo(typeof(TRequest), typeof(TResponse), handler);
            }

            Log.Debug($"Registered TCP handler for command: {commandId}");
        }

        private async Task BroadcastToAllAsync(ArraySegment<byte> messageBytes, MessageType messageType)
        {
            var tasks = new List<Task>();

            // Snapshot of current clients to avoid collection modification during iteration
            var clientsList = connections.Values.ToList();

            foreach (var connection in clientsList)
            {
                // Wrap each send in a task that won't fail the entire broadcast
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await connection.SendAsync(messageBytes);
                    }
                    catch (Exception ex)
                    {
                        // Client may have disconnected - log and continue
                        Log.Debug($"Failed to broadcast to client {connection.ConnectionId}: {ex.Message}");
                    }
                }));
            }

            await Task.WhenAll(tasks);

            Log.Debug($"Broadcasted message to {clientsList.Count} clients");
        }

        protected async Task OnMessageReceivedAsync(IConnection client, ArraySegment<byte> messageBytes)
        {
            try
            {
                // Parse directly from ArraySegment without allocating new array
                var envelope = MessageEnvelope.Parser.ParseFrom(new ReadOnlySpan<byte>(messageBytes.Array, messageBytes.Offset, messageBytes.Count));
                await ProcessEnvelopeAsync(client, envelope);
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing message from {client.ConnectionId}: {ex.Message}");
            }
        }

        private async Task ProcessEnvelopeAsync(IConnection client, MessageEnvelope envelope)
        {
            var commandId = envelope.Id;

            Log.Debug($"Received message: Command={commandId}, Type={envelope.Type}, From={client.ConnectionId}");

            // Thread-safe handler lookup
            HandlerInfo? handlerInfo;
            lock (handlersLock)
            {
                if (!handlers.TryGetValue(commandId, out handlerInfo) && handlerInfo == null)
                {
                    Log.Warning($"No handler registered for command: {commandId}");
                }
            }

            if (handlerInfo == null)
            {
                await SendErrorToClientAsync(client, commandId, $"Unknown command: {commandId}");
                return;
            }

            await RunWithThread(async () => await ProcessMessageAsync(client, envelope, handlerInfo));
        }

        private async Task ProcessMessageAsync(
            IConnection client,
            MessageEnvelope envelope,
            HandlerInfo handlerInfo)
        {
            try
            {
                // Get or create cached parser (avoid reflection overhead)
                var (parser, parseMethod) = parserCache.GetOrAdd(handlerInfo.RequestType, type =>
                {
                    var p = type.GetProperty("Parser")?.GetValue(null);
                    var m = p?.GetType().GetMethod("ParseFrom", new[] { typeof(byte[]) });
                    return (p, m);
                });

                var length = envelope.Data.Length;
                var data = ArrayPool<byte>.Shared.Rent(length);
                envelope.Data.CopyTo(data, 0);
                var dataSegment = new ArraySegment<byte>(data, 0, length);

                // Parse request using cached parser
                var request = parseMethod?.Invoke(parser, new object[] { dataSegment });

                ArrayPool<byte>.Shared.Return(data);

                if (request == null)
                {
                    await SendErrorToClientAsync(client, envelope.Id, "Failed to parse request", envelope.RequestId);
                    return;
                }

                // Create responder
                var responderType = typeof(UnityResponder<>).MakeGenericType(handlerInfo.ResponseType);
                var responder = Activator.CreateInstance(
                    responderType,
                    client,
                    envelope.Id,
                    envelope.RequestId);

                // Invoke handler
                var handlerDelegate = handlerInfo.Handler;
                var handlerTask = (Task?)handlerDelegate.DynamicInvoke(client.Context, request, responder);
                if (handlerTask != null)
                {
                    await handlerTask;
                }

                Log.Debug($"Successfully processed command '{envelope.Id}' from {client.ConnectionId}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error in handler for command '{envelope.Id}': {ex.Message}");
                await SendErrorToClientAsync(client, envelope.Id, ex.Message, envelope.RequestId);
            }
        }

        public async Task SendToClientAsync(string connectionId, ArraySegment<byte> messageBytes)
        {
            if (connections.TryGetValue(connectionId, out var client))
            {
                try
                {
                    await client.SendAsync(messageBytes);
                }
                catch (Exception ex)
                {
                    // Client may have disconnected during send - log and ignore
                    Log.Debug($"Failed to send to client {connectionId}: {ex.Message}");
                }
            }
            else
            {
                Log.Warning($"Cannot send to disconnected client: {connectionId}");
            }
        }

        // public async Task SendToClientAsync(IConnection client, byte[] messageBytes)
        // {
        //     if (client == null)
        //     {
        //         Log.Warning($"Cannot send to disconnected client");
        //         return;
        //     }
        //     try
        //     {
        //         await client.SendAsync(messageBytes);
        //     }
        //     catch (Exception ex)
        //     {
        //         // Client may have disconnected during send - log and ignore
        //         Log.Debug($"Failed to send to client: {ex.Message}");
        //     }
        // }

        public async Task SendErrorToClientAsync(IConnection client, string commandId, string errorMessage, string? requestId = null)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = ByteString.CopyFromUtf8(errorMessage),
                    Type = MessageType.Error,
                    RequestId = requestId ?? string.Empty // Gửi requestId nếu có
                };

                await client.SendAsync(envelope.ToByteArray());
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send error to client: {ex.Message}");
            }
        }

        protected void DisposeAllClient()
        {
            foreach (var client in connections.Values)
            {
                client.Dispose();
            }
            connections.Clear();
        }

        protected void DisposeThreadPool()
        {
            threadPool?.Dispose();
        }

        protected void ClearEventHandlers()
        {
            connectHandlers.Clear();
            disconnectHandlers.Clear();
        }

        private async Task RunWithThread(Func<Task> action)
        {
            try
            {
                if (useMainThread)
                {
                    await action();
                }
                else
                {
                    TaskCompletionSource<bool> tcs = new();
                    threadPool?.TryEnqueue(async () =>
                    {
                        try
                        {
                            await action();
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    });
                    await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error in RunWithThread: {ex.Message}");
            }
        }

        public virtual void Dispose()
        {
            DisposeAllClient();
            ClearEventHandlers();
            DisposeThreadPool();
        }

        private class HandlerInfo
        {
            public Type RequestType { get; set; }
            public Type ResponseType { get; set; }
            public Delegate Handler { get; set; }

            public HandlerInfo(Type requestType, Type responseType, Delegate handler)
            {
                RequestType = requestType;
                ResponseType = responseType;
                Handler = handler;
            }
        }

    }


}