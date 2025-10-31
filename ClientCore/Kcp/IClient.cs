using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ServerApi.Unity.Abstractions
{
    public interface IClient : IDisposable
    {
        /// <summary>
        /// Register a handler for a specific command.
        /// </summary>
        /// <typeparam name="TRequest">
        /// The request object type to send to server, must be a class created by proto buffer.
        /// </typeparam>
        /// <typeparam name="TResponse">
        /// The response object type received from server, must be a class created by proto buffer.
        /// </typeparam>
        /// <param name="commandId">
        /// The command name to register the handler for.
        /// </param>
        /// <param name="handler">
        /// Handler to process responses received from server (receives TResponse).
        /// </param>
        /// <param name="errorHandler">
        /// Handler to process errors.
        /// </param>
        /// <returns>Requester to send requests (sends TRequest).</returns>
        IUnityRequester<TRequest> Handle<TRequest, TResponse>(string commandId, Action<TResponse> handler, Action<string> errorHandler)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new();


        /// <summary>
        /// Create a broadcast requester to send messages without listening for responses.
        /// </summary>
        /// <typeparam name="TRequest">The request object type, must be a Protobuf message.</typeparam>
        /// <param name="commandId">The command name.</param>
        /// <returns>Broadcast requester to send one-way requests.</returns>
        IUnityBroadcastRequester<TRequest> GetBroadcaster<TRequest>(string commandId)
            where TRequest : class, IMessage<TRequest>, new();

        Task<TResponse> SendRequestAsync<TRequest, TResponse>(string id, TRequest request, CancellationToken cancellationToken = default)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new();

        /// <summary>
        /// Connect to TCP server using configured host and port.
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// Disconnect from TCP server.
        /// </summary>
        Task DisconnectAsync();
        bool IsConnected { get; }
        void OnConnect(Action handler);
        void OnDisconnect(Action handler);
        void DispatchMessages();

    }
}