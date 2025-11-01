using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using ServerApi.Protos;
using ServerApi.Unity.Utils;

namespace ServerApi.Unity.Abstractions
{
    /// <summary>
    /// Provides methods to send messages to a client in a Unity context.
    /// Similar to ServerApi.Abstractions.ISend but adapted for Unity.
    /// </summary>
    public abstract class IConnection : IDisposable
    {
        /// <summary>
        /// Send a message to the client.
        /// </summary>
        /// <typeparam name="TMessage">The type of the message body.</typeparam>
        /// <param name="commandId">The command identifier.</param>
        /// <param name="message">The message body.</param>
        public abstract Task SendAsync(byte[] messageBytes);
        public abstract string RemoteEndpoint { get; }
        public abstract string ConnectionId { get; }
        public abstract IUnityContext Context { get; }
        public abstract void Dispose();


    }
}