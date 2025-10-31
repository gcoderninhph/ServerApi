using System.Collections.Generic;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Provides contextual information about a client connection and message (Unity version).
    /// Similar to ServerApi.Abstractions.IContext but adapted for Unity.
    /// </summary>
    public interface IUnityContext
    {
        /// <summary>
        /// Unique identifier for this connection.
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Custom attributes for storing connection-specific data.
        /// </summary>
        Dictionary<string, object> Attributes { get; }

        /// <summary>
        /// The transport type used for this connection (TCP or WebSocket).
        /// </summary>
        string TransportType { get; }

        /// <summary>
        /// Remote endpoint information (IP:Port for TCP, URL for WebSocket).
        /// </summary>
        string RemoteEndpoint { get; }
    }
}
