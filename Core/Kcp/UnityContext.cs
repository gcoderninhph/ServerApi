using System;
using System.Collections.Generic;
using ServerApi.Unity.Abstractions;
using ServerApi.Unity.Configs;

namespace ServerApi.Unity.Server
{
    /// <summary>
    /// Context implementation for Unity TCP connections.
    /// </summary>
    public class UnityContext : IUnityContext
    {
        public string ConnectionId { get; }
        public Dictionary<string, object> Attributes { get; }
        public string TransportType { get; }
        public string RemoteEndpoint { get; }


        public UnityContext(string connectionId, string transportType, string remoteEndpoint)
        {
            ConnectionId = connectionId;
            TransportType = transportType;
            RemoteEndpoint = remoteEndpoint;
            Attributes = new Dictionary<string, object>();
        }
    }
}
