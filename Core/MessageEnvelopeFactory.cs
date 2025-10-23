#nullable enable

using System;
using Google.Protobuf;
using ServerApi.Protos;

namespace ServerApi;

public static class MessageEnvelopeFactory
{
    public static MessageEnvelope CreateError(MessageEnvelope? source, string message)
    {
        var sourceId = source?.Id;
        var id = string.IsNullOrWhiteSpace(sourceId)
            ? Guid.NewGuid().ToString("N")
            : sourceId!;

        return new MessageEnvelope
        {
            Id = id,
            RequestId = source?.RequestId ?? string.Empty,  // Preserve requestId
            Type = MessageType.Error,
            Data = ByteString.CopyFromUtf8(message ?? string.Empty)
        };
    }

    public static MessageEnvelope CreateResponse(string id, IMessage payload, MessageType type = MessageType.Response, string? requestId = null)
    {
        if (payload is null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        var envelopeId = string.IsNullOrWhiteSpace(id)
            ? Guid.NewGuid().ToString("N")
            : id;

        return new MessageEnvelope
        {
            Id = envelopeId,
            RequestId = requestId ?? string.Empty,  // Include requestId
            Type = type,
            Data = ByteString.CopyFrom(payload.ToByteArray())
        };
    }
}
