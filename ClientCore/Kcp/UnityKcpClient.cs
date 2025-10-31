using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using kcp2k;
using ServerApi.Unity.Abstractions;
using ServerApi.Unity.Configs;
using ServerApi.Unity.Utils;
using Log = Serilog.Log;

namespace ServerApi.Unity.Client
{
    /// <summary>
    /// Unity TCP Client implementation with IUnityTcpClientRegister pattern.
    /// Supports request/response pattern and broadcast messages.
    /// </summary>
    public class UnityKcpClient : ClientAbstract, IDisposable
    {
        // Configuration
        private readonly KcpClientConfig config;
        // Connection state
        private KcpClient? kcpClient;


        public UnityKcpClient(KcpClientConfig config) : base(config.useMainThread, config.workerThreadCount, config.maxQueueSize, config.autoReconnect, config.maxReconnectAttempts, config.reconnectDelay, config.requestTimeout)
        {
            // Initialize logging
            // NetworkLogger.Initialize(config.verboseLogging, config.logNetworkTraffic);
            this.config = config;
        }

        #region IUnityTcpClientRegister Implementation

        #endregion

        #region Connection Management

        public override async Task ConnectAsync()
        {
            if (IsConnected)
            {
                Log.Warning("Already connected");
                return;
            }

            try
            {
                var ctsTimeOut = new CancellationTokenSource();
                kcpClient = new KcpClient(
                    OnConnected,
                    OnData,
                    OnDisconnected,
                    OnError,
                    config
                );

                Log.Information($"Connecting to {config.host}:{config.port}...");
                kcpClient.Connect(config.host, config.port);

                var connectTask = WaitForConnectionAsync();

                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(config.connectionTimeout), ctsTimeOut.Token);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    throw new TimeoutException($"Connection timeout after {config.connectionTimeout} seconds");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to connect: {ex.Message}");
                await DisconnectAsync();
                throw;
            }
        }
        private async Task WaitForConnectionAsync()
        {
            while (!IsConnected)
            {
                await Task.Delay(50);
            }
        }

        private void OnData(ArraySegment<byte> data, KcpChannel kcpChannel)
        {
            if (data.Array != null)
            {
                OnMessage(data.Array);
            }
            else
            {
                Log.Warning("Received null data array");
            }
        }


        private void OnError(ErrorCode errorCode, string errorMessage)
        {
            Log.Error($"KCP Error ({errorCode}): {errorMessage}");
        }

        public override Task DisconnectAsync()
        {
            if (!IsConnected)
            {
                return Task.CompletedTask;
            }
            Log.Information("Disconnecting...");
            kcpClient?.Disconnect();
            OnDisconnected();
            Log.Information("Disconnected");
            return Task.CompletedTask;
        }

        #endregion

        #region Sending Messages

        public override Task Send(byte[] messageBytes)
        {
            kcpClient?.Send(new ArraySegment<byte>(messageBytes), KcpChannel.Reliable);
            return Task.CompletedTask;
        }

        #endregion

        public override void Dispose()
        {
            base.Dispose();
            _ = DisconnectAsync();
            Log.Error("UnityKcpClient disposed");
        }

        public override void DispatchMessages()
        {
            kcpClient?.Tick();
        }
    }
}
