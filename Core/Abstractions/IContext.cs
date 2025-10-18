using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace ServerApi.Abstractions;

/// <summary>
/// Provides contextual information about a client connection and message.
/// </summary>
public interface IContext
{
    /// <summary>
    /// Unique identifier for this connection.
    /// </summary>
    string ConnectionId { get; }

    /// <summary>
    /// The command identifier for the current message.
    /// </summary>
    string CommandId { get; }

    /// <summary>
    /// The authenticated user's claims principal.
    /// </summary>
    ClaimsPrincipal? User { get; set; }

    /// <summary>
    /// Request headers (for WebSocket) or metadata (for TCP).
    /// </summary>
    IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Query parameters from the connection request.
    /// </summary>
    IReadOnlyDictionary<string, string>? QueryParameters { get; set; }

    /// <summary>
    /// Custom attributes for storing connection-specific data.
    /// </summary>
    Dictionary<string, object> Attributes { get; }

    /// <summary>
    /// The transport type used for this connection.
    /// </summary>
    string TransportType { get; }
}
