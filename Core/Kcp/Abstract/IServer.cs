using System;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Provides methods to register message handlers for server (Unity version).
    /// Similar to ServerApi.Abstractions.IServerApiRegistrar but adapted for Unity.
    /// </summary>
    public interface IServer : IDisposable
    {
        /// <summary>
        /// Register a handler for a command.
        /// </summary>
        /// <typeparam name="TRequest">The request message type (must be a protobuf message).</typeparam>
        /// <typeparam name="TResponse">The response message type (must be a protobuf message).</typeparam>
        /// <param name="commandId">The command identifier.</param>
        /// <param name="handler">The handler function that processes the request.</param>
        void Handle<TRequest, TResponse>(
            string commandId,
            Func<IUnityContext, TRequest, IUnityResponder<TResponse>, Task> handler)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new();

        /// <summary>
        /// Get a broadcast responder for to send messages without listening for responses.
        /// Used for server-initiated messages to specific connections.
        /// </summary>
        /// <typeparam name="TBody">The message body type.</typeparam>
        /// <param name="commandId">The command identifier.</param>
        /// <returns>A broadcast responder that can send messages to specific connections.</returns>
        IUnityBroadcastResponder<TBody> GetBroadcaster<TBody>(string commandId)
            where TBody : class, IMessage<TBody>, new();


        /// <summary>
        /// Event triggered when a client connects.
        /// </summary>
        void OnConnected(Action<IConnection> handler);

        /// <summary>
        /// Event triggered when a client disconnects.
        /// </summary>
        void OnDisconnected(Action<IConnection> handler);

        bool IsRunning { get; }
        void DispatchMessages();
        void Start();
    }
}
