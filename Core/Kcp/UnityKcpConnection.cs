using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using kcp2k;
using ServerApi.Unity.Abstractions;

namespace ServerApi.Unity.Server
{

    /// <summary>
    /// Manages a single KCP client connection.
    /// </summary>
    internal class KcpConnection : IConnection
    {

        private readonly int connectionId;
        private readonly string remoteEndpoint;
        private readonly IUnityContext context;
        private readonly Action<int, byte[]> sendDataAction;
        private readonly Action<IConnection> onDisconnected;


        public override string RemoteEndpoint => remoteEndpoint;

        public override string ConnectionId => connectionId.ToString();

        public override IUnityContext Context => context;


        public KcpConnection(int connectionId, IPEndPoint remoteEndPoint, Action<int, byte[]> sendDataAction, Action<IConnection> onDisconnected)
        {
            this.connectionId = connectionId;
            remoteEndpoint = remoteEndPoint.ToString();
            context = new UnityContext(connectionId.ToString(), "KCP", remoteEndpoint);
            this.sendDataAction = sendDataAction;
            this.onDisconnected = onDisconnected;
        }


        public override void Dispose()
        {
            onDisconnected?.Invoke(this);
        }

        public override Task SendAsync(byte[] messageBytes)
        {
            sendDataAction(connectionId, messageBytes);
            return Task.CompletedTask;
        }
    }
}
