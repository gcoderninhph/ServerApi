using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;
using Serilog;

namespace ServerApi.Unity.Utils
{
    /// <summary>
    /// Registry for message handlers with type-safe registration.
    /// </summary>
    public class MessageHandlerRegistry
    {
        private class HandlerInfo(Type requestType, Type responseType, Delegate handler, Delegate errorHandler)
        {
            public Type RequestType { get; set; } = requestType;
            public Type ResponseType { get; set; } = responseType;
            public Delegate Handler { get; set; } = handler;
            public Delegate ErrorHandler { get; set; } = errorHandler;
        }

        private readonly Dictionary<string, HandlerInfo> handlers = new Dictionary<string, HandlerInfo>();

        /// <summary>
        /// Register a handler for a specific command.
        /// </summary>
        public void Register<TRequest, TResponse>(
            string commandId,
            Action<TResponse> handler,
            Action<string> errorHandler)
            where TRequest : class, IMessage<TRequest>, new()
            where TResponse : class, IMessage<TResponse>, new()
        {
            if (handlers.ContainsKey(commandId))
            {
                Log.Warning($"Handler for command '{commandId}' already registered, overwriting");
            }

            handlers[commandId] = new HandlerInfo(typeof(TRequest), typeof(TResponse), handler, errorHandler);

            Log.Debug($"Registered handler for command: {commandId}");
        }

        /// <summary>
        /// Unregister a handler for a specific command.
        /// </summary>
        public void Unregister(string commandId)
        {
            if (handlers.Remove(commandId))
            {
                Log.Debug($"Unregistered handler for command: {commandId}");
            }
        }

        /// <summary>
        /// Try to invoke a handler for a response message.
        /// </summary>
        public bool TryInvokeHandler<TResponse>(string commandId, TResponse response)
            where TResponse : class, IMessage<TResponse>, new()
        {
            if (!handlers.TryGetValue(commandId, out var handlerInfo))
            {
                Log.Warning($"No handler registered for command: {commandId}");
                return false;
            }

            if (handlerInfo.ResponseType != typeof(TResponse))
            {
                Log.Error($"Response type mismatch for command '{commandId}': expected {handlerInfo.ResponseType.Name}, got {typeof(TResponse).Name}");
                return false;
            }

            try
            {
                var handler = handlerInfo.Handler as Action<TResponse>;
                handler?.Invoke(response);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception occurred while invoking handler");
                return false;
            }
        }

        /// <summary>
        /// Try to invoke an error handler.
        /// </summary>
        public bool TryInvokeErrorHandler(string commandId, string errorMessage)
        {
            if (!handlers.TryGetValue(commandId, out var handlerInfo))
            {
                return false;
            }

            try
            {
                var errorHandler = handlerInfo.ErrorHandler as Action<string>;
                errorHandler?.Invoke(errorMessage);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception occurred while invoking error handler");
                return false;
            }
        }

        /// <summary>
        /// Get request type for a command.
        /// </summary>
        public Type? GetRequestType(string commandId)
        {
            return handlers.TryGetValue(commandId, out var info) ? info.RequestType : null;
        }

        /// <summary>
        /// Get response type for a command.
        /// </summary>
        public Type? GetResponseType(string commandId)
        {
            return handlers.TryGetValue(commandId, out var info) ? info.ResponseType : null;
        }

        /// <summary>
        /// Check if a handler is registered for a command.
        /// </summary>
        public bool IsRegistered(string commandId)
        {
            return handlers.ContainsKey(commandId);
        }

        /// <summary>
        /// Clear all handlers.
        /// </summary>
        public void Clear()
        {
            handlers.Clear();
            Log.Debug("All handlers cleared");
        }
    }
}
