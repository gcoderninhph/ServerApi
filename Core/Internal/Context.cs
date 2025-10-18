using System;
using System.Collections.Generic;
using System.Security.Claims;
using ServerApi.Abstractions;

namespace ServerApi.Internal;

/// <summary>
/// Internal implementation of IContext.
/// </summary>
internal class Context : ServerApi.Abstractions.IContext
{
    public string ConnectionId { get; }
    public string CommandId { get; set; }
    public ClaimsPrincipal? User { get; set; }
    public IReadOnlyDictionary<string, string>? Headers { get; set; }
    public IReadOnlyDictionary<string, string>? QueryParameters { get; set; }
    public Dictionary<string, object> Attributes { get; }
    public string TransportType { get; }

    public Context(string connectionId, string transportType, string commandId = "")
    {
        ConnectionId = connectionId;
        TransportType = transportType;
        CommandId = commandId;
        Attributes = new Dictionary<string, object>();
    }
}
