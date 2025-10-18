using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using ServerApi.Abstractions;
using ServerApi.Protos;

namespace ServerApi.Internal;

/// <summary>
/// Implementation of IBroadcastResponder for sending messages to specific connections.
/// </summary>
internal class BroadcastResponder<TBody> : IBroadcastResponder<TBody> where TBody : class, IMessage
{
    private readonly string _commandId;
    private readonly IConnectionRegistry _registry;
    private readonly Func<string, byte[], CancellationToken, Task<bool>> _sendFunc;

    public BroadcastResponder(
        string commandId, 
        IConnectionRegistry registry,
        Func<string, byte[], CancellationToken, Task<bool>> sendFunc)
    {
        _commandId = commandId;
        _registry = registry;
        _sendFunc = sendFunc;
    }

    public async Task SendAsync(string connectionId, TBody body, CancellationToken cancellationToken = default)
    {
        // Create message envelope
        var envelope = MessageEnvelopeFactory.CreateResponse(_commandId, body);
        var envelopeBytes = envelope.ToByteArray();

        // Send through registry
        var sent = await _sendFunc(connectionId, envelopeBytes, cancellationToken);
        
        if (!sent)
        {
            throw new InvalidOperationException($"Connection {connectionId} not found or disconnected.");
        }
    }

    public async Task SendErrorAsync(string connectionId, string errorMessage, CancellationToken cancellationToken = default)
    {
        // Create error envelope
        var envelope = MessageEnvelopeFactory.CreateError(null, errorMessage);
        var envelopeBytes = envelope.ToByteArray();

        // Send through registry
        var sent = await _sendFunc(connectionId, envelopeBytes, cancellationToken);
        
        if (!sent)
        {
            throw new InvalidOperationException($"Connection {connectionId} not found or disconnected.");
        }
    }
}
