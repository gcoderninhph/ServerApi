using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using ServerApi.Unity.Abstractions;

namespace ServerApi.Unity.Client
{
    /// <summary>
    /// Requester implementation for sending requests and receiving responses.
    /// </summary>
    internal class UnityRequester<TRequest> : IUnityRequester<TRequest>
        where TRequest : class, IMessage<TRequest>, new()
    {
        private readonly string commandId;
        private readonly Func<string, TRequest, CancellationToken, Task> sendFunc;

        public UnityRequester(
            string commandId,
            Func<string, TRequest, CancellationToken, Task> sendFunc)
        {
            this.commandId = commandId;
            this.sendFunc = sendFunc;
        }

        public async Task SendAsync(TRequest requestBody, CancellationToken cancellationToken = default)
        {
            await sendFunc(commandId, requestBody, cancellationToken);
        }
    }

    /// <summary>
    /// Broadcast requester implementation for one-way messages.
    /// </summary>
    internal class UnityBroadcastRequester<TRequest> : IUnityBroadcastRequester<TRequest>
        where TRequest : class, IMessage<TRequest>, new()
    {
        private readonly string commandId;
        private readonly Action<string, TRequest, CancellationToken> sendFunc;

        public UnityBroadcastRequester(
            string commandId,
            Action<string, TRequest, CancellationToken> sendFunc)
        {
            this.commandId = commandId;
            this.sendFunc = sendFunc;
        }

        public void SendAsync(TRequest request, CancellationToken cancellationToken = default)
        {
            sendFunc(commandId, request, cancellationToken);
        }
    }
}
