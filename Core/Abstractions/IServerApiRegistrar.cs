using System;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ServerApi.Abstractions;

/// <summary>
/// Provides methods to register message handlers for different transports.
/// </summary>
public interface IServerApiRegistrar
{
    /// <summary>
    /// Register a handler for a WebSocket command.
    /// </summary>
    /// <typeparam name="TRequest">The request message type (must be a protobuf message).</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="handler">The handler function that processes the request.</param>
    void HandleWebSocket<TRequest, TResponse>(
        string commandId,
        Func<IContext, TRequest, IResponder<TResponse>, Task> handler)
        where TRequest : class, Google.Protobuf.IMessage<TRequest>, new()
        where TResponse : class;

    /// <summary>
    /// Register a handler for a TCP Stream command.
    /// </summary>
    /// <typeparam name="TRequest">The request message type (must be a protobuf message).</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="handler">The handler function that processes the request.</param>
    void HandleTcpStream<TRequest, TResponse>(
        string commandId,
        Func<IContext, TRequest, IResponder<TResponse>, Task> handler)
        where TRequest : class, Google.Protobuf.IMessage<TRequest>, new()
        where TResponse : class;

    /// <summary>
    /// Register a handler for both WebSocket and TCP Stream with the same command ID.
    /// </summary>
    /// <typeparam name="TRequest">The request message type (must be a protobuf message).</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="handler">The handler function that processes the request.</param>
    void HandleBoth<TRequest, TResponse>(
        string commandId,
        Func<IContext, TRequest, IResponder<TResponse>, Task> handler)
        where TRequest : class, Google.Protobuf.IMessage<TRequest>, new()
        where TResponse : class;

    /// <summary>
    /// Get a broadcast responder for WebSocket to send messages without listening for responses.
    /// Used for server-initiated messages to specific connections.
    /// </summary>
    /// <typeparam name="TBody">The message body type.</typeparam>
    /// <param name="commandId">The command identifier.</param>
    /// <returns>A broadcast responder that can send messages to specific connections.</returns>
    IBroadcastResponder<TBody> GetWebSocketBroadcaster<TBody>(string commandId)
        where TBody : class, IMessage;

    /// <summary>
    /// Get a broadcast responder for TCP Stream to send messages without listening for responses.
    /// Used for server-initiated messages to specific connections.
    /// </summary>
    /// <typeparam name="TBody">The message body type.</typeparam>
    /// <param name="commandId">The command identifier.</param>
    /// <returns>A broadcast responder that can send messages to specific connections.</returns>
    IBroadcastResponder<TBody> GetTcpStreamBroadcaster<TBody>(string commandId)
        where TBody : class, IMessage;
}
