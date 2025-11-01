using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Serilog;
using ServerApi.Protos;
using ServerApi.Unity.Abstractions;
using ServerApi.Unity.Utils;

namespace ServerApi.Unity.Server
{
    /// <summary>
    /// Responder implementation for sending messages back to a specific client.
    /// </summary>
    internal class UnityResponder<TResponse> : IUnityResponder<TResponse>
        where TResponse : class, IMessage<TResponse>, new()
    {
        private readonly IConnection client;
        private readonly string commandId;
        private readonly string requestId;

        public UnityResponder(
            IConnection client,
            string commandId,
            string requestId)
        {
            this.client = client;
            this.commandId = commandId;
            this.requestId = requestId;
        }

        public async Task SendAsync(TResponse message, CancellationToken cancellationToken = default)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = message.ToByteString(),
                    Type = MessageType.Response,
                    RequestId = requestId // Trả lại requestId để client có thể khớp với pending request
                };

                var bytes = envelope.ToByteArray();
                await client.SendAsync(bytes);

                Log.Debug($"Sent response for command '{commandId}' to connection '{client.ConnectionId}', RequestId='{requestId}'");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send response: {ex.Message}");
                throw;
            }
        }

        public async Task SendErrorAsync(string errorMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = ByteString.CopyFromUtf8(errorMessage),
                    Type = MessageType.Error,
                    RequestId = requestId // Trả lại requestId ngay cả khi error
                };

                var bytes = envelope.ToByteArray();
                await client.SendAsync(bytes);

                Log.Debug($"Sent error for command '{commandId}' to connection '{client.ConnectionId}', RequestId='{requestId}': {errorMessage}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send error: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Broadcast responder implementation for sending messages to specific clients or all clients.
    /// </summary>
    internal class UnityBroadcastResponder<TBody> : IUnityBroadcastResponder<TBody>
        where TBody : class, IMessage<TBody>, new()
    {
        private readonly string commandId;
        private readonly Func<string, byte[], Task> sendFunc;
        private readonly Func<byte[], MessageType, Task> broadcastFunc;

        public UnityBroadcastResponder(
            string commandId,
            Func<string, byte[], Task> sendFunc,
            Func<byte[], MessageType, Task> broadcastFunc)
        {
            this.commandId = commandId;
            this.sendFunc = sendFunc;
            this.broadcastFunc = broadcastFunc;
        }

        public async Task SendAsync(string connectionId, TBody body, CancellationToken cancellationToken = default)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = body.ToByteString(),
                    Type = MessageType.Request
                };

                var bytes = envelope.ToByteArray();
                await sendFunc(connectionId, bytes);

                Log.Debug($"Sent broadcast message for command '{commandId}' to connection '{connectionId}'");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send broadcast message: {ex.Message}");
                throw;
            }
        }

        public async Task SendErrorAsync(string connectionId, string errorMessage, CancellationToken cancellationToken = default)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = ByteString.CopyFromUtf8(errorMessage),
                    Type = MessageType.Error
                };

                var bytes = envelope.ToByteArray();
                await sendFunc(connectionId, bytes);

                Log.Debug($"Sent broadcast error for command '{commandId}' to connection '{connectionId}': {errorMessage}");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to send broadcast error: {ex.Message}");
                throw;
            }
        }

        public async Task BroadcastAsync(TBody body, CancellationToken cancellationToken = default)
        {
            try
            {
                var envelope = new MessageEnvelope
                {
                    Id = commandId,
                    Data = body.ToByteString(),
                    Type = MessageType.Request
                };

                var bytes = envelope.ToByteArray();
                await broadcastFunc(bytes, MessageType.Request);

                Log.Debug($"Broadcasted message for command '{commandId}' to all clients");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to broadcast message: {ex.Message}");
                throw;
            }
        }
    }
}
