using System;
using kcp2k;
using ServerApi.Unity.Abstractions;
using ServerApi.Unity.Configs;
using Log = Serilog.Log;

namespace ServerApi.Unity.Server
{
    public class UnityKcpServer : ServerAbstract
    {
        private readonly KcpServerConfig _kcpServerConfig;
        private readonly KcpServer kcpServer;
        private bool _isRunning = false;
        public override bool IsRunning => _isRunning;

        public UnityKcpServer(KcpServerConfig kcpServerConfig) : base(kcpServerConfig.UseMainThread, kcpServerConfig.WorkerThreadCount, kcpServerConfig.MaxQueueSize)
        {
            _kcpServerConfig = kcpServerConfig;
            // Initialize logging
            // NetworkLogger.Initialize(kcpServerConfig.verboseLogging, kcpServerConfig.logNetworkTraffic);
            kcpServer = new KcpServer(OnConnected, OnData, OnDisconnected, OnError, kcpServerConfig);
        }

        private void OnConnected(int connectionId)
        {
            var remoteEndPoint = kcpServer.GetClientEndPoint(connectionId);
            IConnection connection = new KcpConnection(connectionId, remoteEndPoint, Send, DisposeClient);
            ClientConnection(connection);
            Log.Information($"KCP Connect: Connection {connectionId} from {remoteEndPoint}");
        }

        private void OnDisconnected(int connectionId)
        {
            if (TryGetConnectionById(connectionId.ToString(), out var connection) && connection != null)
            {
                ClientDisconnection(connection);
                Log.Information($"KCP Disconnect: Connection {connectionId} closed");
            }
            else
            {
                Log.Warning($"KCP Disconnect: Connection {connectionId} not found");
            }
        }

        private void OnData(int connectionId, ArraySegment<byte> data, KcpChannel channel)
        {
            if (TryGetConnectionById(connectionId.ToString(), out var connection) && connection != null)
            {
                _ = OnMessageReceivedAsync(connection, data.ToArray());
                Log.Debug("Received", connection.RemoteEndpoint, data.Count);
            }
            else
            {
                Log.Warning($"KCP Data: Connection {connectionId} not found");
            }
        }
        private void OnError(int connectionId, ErrorCode error, string reason)
        {
            Log.Error($"KCP Error on connection {connectionId}: {error} - {reason}");
        }

        private void Send(int connectionId, byte[] data)
        {
            kcpServer.Send(connectionId, new ArraySegment<byte>(data), KcpChannel.Reliable);
        }

        public override void Start()
        {
            try
            {
                if (_isRunning)
                {
                    Log.Warning("KCP Server is already running");
                    return;
                }

                kcpServer.Start(_kcpServerConfig.Port);
                _isRunning = true;
                Log.Information($"KCP Server started on port {_kcpServerConfig.Port}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to start KCP server: {ex.Message}");
                throw;
            }
        }

        private void DisposeClient(IConnection connection)
        {
            kcpServer.Disconnect(int.Parse(connection.ConnectionId));
        }

        public override void Dispose()
        {
            try
            {
                if (!_isRunning)
                {
                    Log.Warning("KCP Server is not running");
                    return;
                }
                base.Dispose();
                kcpServer.Stop();
                _isRunning = false;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to stop KCP server: {ex.Message}");
                throw;
            }
        }

        public override void DispatchMessages()
        {
            kcpServer.Tick();
        }
    }
}