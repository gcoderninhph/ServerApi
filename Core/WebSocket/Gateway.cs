using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServerApi.Configuration;
using ServerApi.Internal;

namespace ServerApi.WebSocket;

/// <summary>
/// WebSocket gateway that accepts WebSocket connections and manages them.
/// </summary>
public class WebSocketGatewayNew
{
    private readonly ServerApiRegistrar _registrar;
    private readonly ILogger<WebSocketGatewayNew> _logger;
    private readonly WebSocketOptions _options;
    private readonly SecurityConfig _securityConfig;

    public WebSocketGatewayNew(
        ServerApiRegistrar registrar,
        ILogger<WebSocketGatewayNew> logger,
        IOptions<ServerApiOptions> options)
    {
        _registrar = registrar;
        _logger = logger;
        _options = options.Value.WebSocket ?? new WebSocketOptions();
        _securityConfig = options.Value.Security ?? new SecurityConfig();
    }

    public async Task HandleWebSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Extract user from HttpContext if authentication is enabled
        ClaimsPrincipal? user = null;
        if (_securityConfig.EnableAuthentication)
        {
            user = context.User;
            
            // Check if authenticated user is required
            if (_securityConfig.RequireAuthenticatedUser)
            {
                if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
                {
                    _logger.LogWarning("WebSocket connection rejected: User is not authenticated");
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required");
                    return;
                }
                
                _logger.LogInformation("WebSocket connection authenticated for user: {UserName}", 
                    user.Identity.Name ?? "Unknown");
            }
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = Guid.NewGuid().ToString("N");

        _logger.LogInformation("WebSocket connection {ConnectionId} established.", connectionId);

        // Extract headers and query parameters
        var headers = context.Request.Headers.ToDictionary(
            h => h.Key,
            h => string.Join(", ", h.Value.ToArray()));

        var queryParams = context.Request.Query.ToDictionary(
            q => q.Key,
            q => string.Join(", ", q.Value.ToArray()));

        var connection = new WebSocketConnection(
            connectionId,
            socket,
            _registrar,
            _logger,
            _options.BufferSize,
            user,
            headers,
            queryParams);

        await connection.RunAsync(context.RequestAborted);

        _logger.LogInformation("WebSocket connection {ConnectionId} closed.", connectionId);
    }
}
