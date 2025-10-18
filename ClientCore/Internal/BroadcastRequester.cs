using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace ClientCore.Internal;

/// <summary>
/// Implementation của IBroadcastRequester để gửi tin nhắn one-way
/// </summary>
internal class BroadcastRequester<TRequest> : IBroadcastRequester<TRequest> where TRequest : class, IMessage
{
    private readonly string _commandId;
    private readonly Func<string, byte[], CancellationToken, Task> _sendFunc;

    public BroadcastRequester(string commandId, Func<string, byte[], CancellationToken, Task> sendFunc)
    {
        _commandId = commandId;
        _sendFunc = sendFunc;
    }

    public async Task SendAsync(TRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        // Serialize request to bytes
        var requestBytes = request.ToByteArray();

        // Send through the provided send function
        await _sendFunc(_commandId, requestBytes, cancellationToken);
    }
}
