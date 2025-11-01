using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Serilog;
using ServerApi.Protos;
using ServerApi.Unity.Client;
using ServerApi.Unity.Utils;

namespace ServerApi.Unity.Abstractions
{
    public abstract class ClientAbstract : IClient
    {
        private readonly NetworkThreadPool? threadPool;
        private readonly bool useMainThread;
        private readonly MessageHandlerRegistry handlerRegistry;
        private readonly List<Action> connectHandlers;
        private readonly List<Action> disconnectHandlers;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<MessageEnvelope>> pendingRequests;
        private bool isConnected = false;
        private readonly int requestTimeout;

        private bool autoReconnect = false;
        private int reconnectAttempts = 0;
        private int maxReconnectAttempts = 0;
        private int reconnectDelay = 0;
        private bool isDisposed = false;
        private bool isReTryingReconnect = false;

        public bool IsConnected => isConnected;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClientAbstract"/> class.
        /// </summary>
        /// <param name="useMainThread">Whether to use Unity main thread for message handling.</param>
        /// <param name="workerThreadCount">Number of worker threads if not using main thread.</param>
        /// <param name="maxQueueSize">Maximum task queue size for thread pool.</param>
        /// <param name="autoReconnect">Whether to automatically reconnect on disconnection.</param>
        /// <param name="maxReconnectAttempts">Maximum number of reconnection attempts (0 = unlimited).</param>
        /// <param name="reconnectDelay">Delay between reconnection attempts in seconds.</param>
        /// <returns></returns>
        public ClientAbstract(
            bool useMainThread,
            int workerThreadCount,
            int maxQueueSize,
            bool autoReconnect,
            int maxReconnectAttempts,
            int reconnectDelay,
            int requestTimeout
        )
        {
            this.useMainThread = useMainThread;
            if (!useMainThread)
            {
                threadPool = new(workerThreadCount, maxQueueSize);
            }
            handlerRegistry = new();
            connectHandlers = new();
            disconnectHandlers = new();
            pendingRequests = new();
            this.autoReconnect = autoReconnect;
            this.maxReconnectAttempts = maxReconnectAttempts;
            this.reconnectDelay = reconnectDelay;
            this.requestTimeout = requestTimeout;
        }

        public abstract Task ConnectAsync();
        public abstract Task DisconnectAsync();
        public abstract Task Send(byte[] data);
        public abstract void DispatchMessages();

        protected void OnConnected()
        {
            reconnectAttempts = 0;
            isConnected = true;
            isReTryingReconnect = false;

            _ = RunWithThread(() =>
            {
                foreach (var handler in connectHandlers)
                {
                    try
                    {
                        handler.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in OnConnected handler: {ex.Message}");
                    }
                }
            });
            return;

        }

        protected void OnDisconnected()
        {
            isConnected = false;

            _ = RunWithThread(() =>
            {
                foreach (var handler in disconnectHandlers)
                {
                    try
                    {
                        handler.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Error in OnDisconnected handler: {ex.Message}");
                    }
                }

                ClearPendingRequest();

                if (autoReconnect)
                {
                    _ = AutoReconnectAsync();
                }
            });
        }

        public void OnConnect(Action handler)
        {
            if (handler != null)
                connectHandlers.Add(handler);
        }

        public void OnDisconnect(Action handler)
        {
            if (handler != null)
                disconnectHandlers.Add(handler);
        }

        private async Task AutoReconnectAsync()
        {
            while (autoReconnect &&
                   !isConnected &&
                   !isDisposed &&
                   !isReTryingReconnect &&
                   (maxReconnectAttempts == 0 || reconnectAttempts < maxReconnectAttempts))
            {
                reconnectAttempts++;
                Log.Information($"Reconnection attempt {reconnectAttempts}...");

                await Task.Delay(TimeSpan.FromSeconds(reconnectDelay));

                try
                {
                    await ConnectAsync();
                    return;
                }
                catch (Exception ex)
                {
                    Log.Warning($"Reconnection attempt {reconnectAttempts} failed: {ex.Message}");
                }
            }

            if (!isConnected)
            {
                Log.Error($"Failed to reconnect after {reconnectAttempts} attempts");
            }
        }


        public IUnityBroadcastRequester<TRequest> GetBroadcaster<TRequest>(string commandId)
        where TRequest : class, IMessage<TRequest>, new()
        {
            return new UnityBroadcastRequester<TRequest>(
                commandId,
                SendBroadcastAsync<TRequest>);
        }

        public IUnityRequester<TRequest> Handle<TRequest, TResponse>(string commandId, Action<TResponse> handler, Action<string> errorHandler)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new()
        {
            handlerRegistry.Register<TRequest, TResponse>(commandId, handler, errorHandler);

            return new UnityRequester<TRequest>(
                commandId,
                SendRequestAsync);
        }

        private async Task SendRequestAsync<TRequest>(
            string commandId,
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : class, IMessage<TRequest>, new()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            try
            {
                var messageBytes = await SendMessage(null, commandId, request);
                Log.Debug("[KCP] Sent", "", messageBytes.Length);
                Log.Debug($"Sent request for command: {commandId}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending request: {ex.Message}");
                throw;
            }
        }

        protected void OnMessage(byte[] data)
        {
            try
            {
                Log.Debug("Received", "", data.Length);

                var envelope = MessageEnvelope.Parser.ParseFrom(data);
                var commandId = envelope.Id;

                Log.Debug($"Received message: Command={commandId}, Type={envelope.Type}, RequestId={envelope.RequestId}");

                // Xử lý RESPONSE hoặc ERROR cho pending request
                if (!string.IsNullOrEmpty(envelope.RequestId))
                {
                    if (pendingRequests.TryRemove(envelope.RequestId, out var tcs))
                    {
                        if (envelope.Type == MessageType.Error)
                        {
                            var errorMessage = envelope.Data.ToStringUtf8();
                            Log.Error($"Received error response for RequestId={envelope.RequestId}: {errorMessage}");
                            tcs.TrySetException(new Exception($"Server returned error: {errorMessage}"));
                        }
                        else if (envelope.Type == MessageType.Response)
                        {
                            tcs.TrySetResult(envelope);
                        }
                        return;
                    }
                }

                _ = RunWithThread(async () => await ProcessMessage(envelope));
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing WebSocket message: {ex.Message}");
            }
        }

        private Task ProcessMessage(MessageEnvelope envelope)
        {
            try
            {
                var commandId = envelope.Id;

                if (envelope.Type == MessageType.Error)
                {
                    var errorMessage = envelope.Data.ToStringUtf8();
                    handlerRegistry.TryInvokeErrorHandler(commandId, errorMessage);
                    return Task.CompletedTask;
                }

                // Get response type from registry
                var responseType = handlerRegistry.GetResponseType(commandId);
                if (responseType == null)
                {
                    Log.Warning($"No handler registered for command: {commandId}");
                    return Task.CompletedTask;
                }

                // Parse response
                var responseParser = responseType.GetProperty("Parser")?.GetValue(null);
                var parseMethod = responseParser?.GetType().GetMethod("ParseFrom", [typeof(byte[])]);
                var response = parseMethod?.Invoke(responseParser, [envelope.Data.ToByteArray()]);

                if (response == null)
                {
                    Log.Error($"Failed to parse response for command: {commandId}");
                    return Task.CompletedTask;
                }

                // Invoke handler
                var invokeMethod = typeof(MessageHandlerRegistry).GetMethod("TryInvokeHandler");
                var genericMethod = invokeMethod?.MakeGenericMethod(responseType);
                genericMethod?.Invoke(handlerRegistry, [commandId, response]);

                Log.Debug($"Successfully processed response for command: {commandId}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing message: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            // Tạo requestId ngẫu nhiên
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<MessageEnvelope>();

            // Đăng ký pending request
            if (!pendingRequests.TryAdd(requestId, tcs))
            {
                throw new InvalidOperationException($"Failed to register pending request: {requestId}");
            }

            try
            {
                var messageBytes = await SendMessage(requestId, id, request);
                Log.Debug("Sent", "", messageBytes.Length);
                Log.Debug($"Sent request: Command={id}, RequestId={requestId}");
                // Chờ response với timeout
                var timeout = TimeSpan.FromSeconds(requestTimeout);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);

                try
                {
                    // Chờ response với timeout
                    var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeout, timeoutCts.Token));

                    if (completedTask != tcs.Task)
                    {
                        throw new TimeoutException($"Request timeout after {timeout.TotalSeconds}s: Command={id}, RequestId={requestId}");
                    }

                    var responseEnvelope = await tcs.Task;

                    // Parse response
                    var response = new TResponse();
                    response.MergeFrom(responseEnvelope.Data);

                    Log.Debug($"Received response: Command={id}, RequestId={requestId}");
                    return response;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Request timeout after {timeout.TotalSeconds}s: Command={id}, RequestId={requestId}");
                }
            }
            finally
            {
                // Cleanup pending request
                pendingRequests.TryRemove(requestId, out _);
            }
        }

        private async Task SendBroadcastAsync<TRequest>(
            string commandId,
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : class, IMessage<TRequest>, new()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected to server");
            }

            try
            {
                byte[] messageBytes = await SendMessage(null, commandId, request);

                Log.Debug("Sent", "", messageBytes.Length);
                Log.Debug($"Sent broadcast message for command: {commandId}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending broadcast: {ex.Message}");
                throw;
            }
        }

        private async Task<byte[]> SendMessage<TRequest>(string? requestId, string commandId, TRequest request)
            where TRequest : class, IMessage<TRequest>, new()
        {
            var envelope = new MessageEnvelope
            {
                Id = commandId,
                Data = request.ToByteString(),
                Type = MessageType.Request,
            };

            if (!string.IsNullOrEmpty(requestId))
            {
                envelope.RequestId = requestId;
            }

            var messageBytes = envelope.ToByteArray();
            await Send(messageBytes);
            return messageBytes;
        }

        private void ClearPendingRequest()
        {
            foreach (var kvp in pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            pendingRequests.Clear();
        }

        protected void DisposeThreadPool() => threadPool?.Dispose();

        protected void ClearEventHandlers()
        {
            connectHandlers.Clear();
            disconnectHandlers.Clear();
        }

        public virtual void Dispose()
        {
            DisposeThreadPool();
            ClearEventHandlers();
            isDisposed = true;

            Log.Debug("ClientAbstract disposed");
        }

        private Task RunWithThread(Action action)
        {
            TaskCompletionSource<bool> tcs = new();
            try
            {
                if (useMainThread)
                {
                    action();
                    tcs.SetResult(true);
                }
                else
                {
                    threadPool?.TryEnqueue(() =>
                    {
                        try
                        {
                            action();
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }

                    });
                }
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }
    }
}