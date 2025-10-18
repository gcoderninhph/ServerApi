using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using ServerApi.Abstractions;

namespace ServerApi.Internal;

/// <summary>
/// Unified responder that can send messages at any time.
/// No more DirectResponder hack - this is clean and simple!
/// </summary>
/// <typeparam name="TResponse">The response message type.</typeparam>
internal class Responder<TResponse> : ServerApi.Abstractions.IResponder<TResponse> where TResponse : class
{
    private readonly Func<Protos.MessageEnvelope, Task> _sendFunc;
    private readonly string _commandId;

    public Responder(Func<Protos.MessageEnvelope, Task> sendFunc, string commandId)
    {
        _sendFunc = sendFunc;
        _commandId = commandId;
    }

    public async Task SendAsync(TResponse message, CancellationToken cancellationToken = default)
    {
        var envelope = new Protos.MessageEnvelope
        {
            Id = _commandId,
            Data = (message as IMessage)?.ToByteString() ?? ByteString.Empty,
            Type = Protos.MessageType.Response
        };

        await _sendFunc(envelope);
    }

    public async Task SendErrorAsync(string errorMessage, CancellationToken cancellationToken = default)
    {
        var envelope = new Protos.MessageEnvelope
        {
            Id = _commandId,
            Data = ByteString.CopyFromUtf8(errorMessage ?? string.Empty),
            Type = Protos.MessageType.Error
        };

        await _sendFunc(envelope);
    }
}
