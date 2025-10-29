using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using ServerApi.Abstractions;

namespace ServerApi.Internal;

/// <summary>
/// Internal registrar that stores handlers for WebSocket and TCP commands.
/// </summary>
public class ServerApiRegistrar : ServerApi.Abstractions.IServerApiRegistrar
{
    private readonly Dictionary<string, HandlerInfo> _webSocketHandlers = new();
    private readonly Dictionary<string, HandlerInfo> _tcpStreamHandlers = new();
    private readonly Dictionary<string, HandlerInfo> _kcpHandlers = new();
    private readonly ConnectionRegistry _connectionRegistry;

    public ServerApiRegistrar()
    {
        _connectionRegistry = new ConnectionRegistry();
    }

    private class HandlerInfo
    {
        public required Func<byte[], IMessage> Parser { get; init; }
        public required Func<ServerApi.Abstractions.IContext, object, ServerApi.Abstractions.IResponder<object>, Task> Handler { get; init; }
    }

    public void HandleWebSocket<TRequest, TResponse>(
        string commandId,
        Func<ServerApi.Abstractions.IContext, TRequest, ServerApi.Abstractions.IResponder<TResponse>, Task> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class
    {
        var parser = new MessageParser<TRequest>(() => new TRequest());
        
        _webSocketHandlers[commandId] = new HandlerInfo
        {
            Parser = bytes => parser.ParseFrom(bytes),
            Handler = async (ctx, req, resp) =>
            {
                var typedResponder = new ResponderAdapter<TResponse>(resp);
                await handler(ctx, (TRequest)req, typedResponder);
            }
        };
    }

    public void HandleTcpStream<TRequest, TResponse>(
        string commandId,
        Func<ServerApi.Abstractions.IContext, TRequest, ServerApi.Abstractions.IResponder<TResponse>, Task> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class
    {
        var parser = new MessageParser<TRequest>(() => new TRequest());
        
        _tcpStreamHandlers[commandId] = new HandlerInfo
        {
            Parser = bytes => parser.ParseFrom(bytes),
            Handler = async (ctx, req, resp) =>
            {
                var typedResponder = new ResponderAdapter<TResponse>(resp);
                await handler(ctx, (TRequest)req, typedResponder);
            }
        };
    }

    public void HandleKcp<TRequest, TResponse>(
        string commandId,
        Func<ServerApi.Abstractions.IContext, TRequest, ServerApi.Abstractions.IResponder<TResponse>, Task> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class
    {
        var parser = new MessageParser<TRequest>(() => new TRequest());
        
        _kcpHandlers[commandId] = new HandlerInfo
        {
            Parser = bytes => parser.ParseFrom(bytes),
            Handler = async (ctx, req, resp) =>
            {
                var typedResponder = new ResponderAdapter<TResponse>(resp);
                await handler(ctx, (TRequest)req, typedResponder);
            }
        };
    }

    public void HandleBoth<TRequest, TResponse>(
        string commandId,
        Func<ServerApi.Abstractions.IContext, TRequest, ServerApi.Abstractions.IResponder<TResponse>, Task> handler)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class
    {
        HandleWebSocket(commandId, handler);
        HandleTcpStream(commandId, handler);
        HandleKcp(commandId, handler);
    }

    internal async Task<bool> TryInvokeWebSocketAsync(string commandId, ServerApi.Abstractions.IContext context, byte[] requestBytes, ServerApi.Abstractions.IResponder<object> responder)
    {
        if (_webSocketHandlers.TryGetValue(commandId, out var handlerInfo))
        {
            var request = handlerInfo.Parser(requestBytes);
            await handlerInfo.Handler(context, request, responder);
            return true;
        }
        return false;
    }

    internal async Task<bool> TryInvokeTcpStreamAsync(string commandId, ServerApi.Abstractions.IContext context, byte[] requestBytes, ServerApi.Abstractions.IResponder<object> responder)
    {
        if (_tcpStreamHandlers.TryGetValue(commandId, out var handlerInfo))
        {
            var request = handlerInfo.Parser(requestBytes);
            await handlerInfo.Handler(context, request, responder);
            return true;
        }
        return false;
    }

    internal async Task<bool> TryInvokeKcpAsync(string commandId, ServerApi.Abstractions.IContext context, byte[] requestBytes, ServerApi.Abstractions.IResponder<object> responder)
    {
        if (_kcpHandlers.TryGetValue(commandId, out var handlerInfo))
        {
            var request = handlerInfo.Parser(requestBytes);
            await handlerInfo.Handler(context, request, responder);
            return true;
        }
        return false;
    }

    // Adapter to convert IResponder<object> to IResponder<TResponse>
    private class ResponderAdapter<TResponse> : ServerApi.Abstractions.IResponder<TResponse> where TResponse : class
    {
        private readonly ServerApi.Abstractions.IResponder<object> _inner;

        public ResponderAdapter(ServerApi.Abstractions.IResponder<object> inner)
        {
            _inner = inner;
        }

        public Task SendAsync(TResponse message, CancellationToken cancellationToken = default)
        {
            return _inner.SendAsync(message, cancellationToken);
        }

        public Task SendErrorAsync(string errorMessage, CancellationToken cancellationToken = default)
        {
            return _inner.SendErrorAsync(errorMessage, cancellationToken);
        }
    }

    public IBroadcastResponder<TBody> GetWebSocketBroadcaster<TBody>(string commandId) where TBody : class, IMessage
    {
        return new BroadcastResponder<TBody>(
            commandId,
            _connectionRegistry,
            (connId, data, ct) => _connectionRegistry.TrySendWebSocketAsync(connId, data, ct)
        );
    }

    public IBroadcastResponder<TBody> GetTcpStreamBroadcaster<TBody>(string commandId) where TBody : class, IMessage
    {
        return new BroadcastResponder<TBody>(
            commandId,
            _connectionRegistry,
            (connId, data, ct) => _connectionRegistry.TrySendTcpStreamAsync(connId, data, ct)
        );
    }

    // Expose connection registry for internal use
    internal IConnectionRegistry ConnectionRegistry => _connectionRegistry;
}
