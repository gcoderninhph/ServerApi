using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ServerApi.Abstractions;
using ServerApi.Configuration;
using ServerApi.Internal;

namespace ServerApi.Extensions;

/// <summary>
/// Extension methods for registering ServerApi services.
/// </summary>
public static class ServerApiServiceCollectionExtensions
{
    /// <summary>
    /// Adds ServerApi WebSocket support with configuration from appsettings.json.
    /// </summary>
    public static IServiceCollection AddServerApiWebSocket(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options from configuration
        services.Configure<ServerApiOptions>(configuration.GetSection(ServerApiOptions.SectionName));

        // Register the registrar as singleton - shared across both transports
        services.AddSingleton<ServerApiRegistrar>();
        services.AddSingleton<IServerApiRegistrar>(sp => sp.GetRequiredService<ServerApiRegistrar>());

        // Register WebSocket gateway
        services.AddSingleton<WebSocket.WebSocketGatewayNew>();

        return services;
    }

    /// <summary>
    /// Adds ServerApi TCP Stream support with configuration from appsettings.json.
    /// </summary>
    public static IServiceCollection AddServerApiTcpStream(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options from configuration (if not already done)
        services.Configure<ServerApiOptions>(configuration.GetSection(ServerApiOptions.SectionName));

        // Register the registrar as singleton - shared across both transports
        // Only register if not already registered
        if (!services.Any(x => x.ServiceType == typeof(ServerApiRegistrar)))
        {
            services.AddSingleton<ServerApiRegistrar>();
            services.AddSingleton<IServerApiRegistrar>(sp => sp.GetRequiredService<ServerApiRegistrar>());
        }

        // Register TCP Stream gateway as hosted service
        services.AddHostedService<TcpStream.TcpGateway>();

        return services;
    }

    /// <summary>
    /// Adds ServerApi KCP support with configuration from appsettings.json.
    /// </summary>
    public static IServiceCollection AddServerApiKcp(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options from configuration (if not already done)
        services.Configure<ServerApiOptions>(configuration.GetSection(ServerApiOptions.SectionName));

        // Register the registrar as singleton - shared across all transports
        // Only register if not already registered
        if (!services.Any(x => x.ServiceType == typeof(ServerApiRegistrar)))
        {
            services.AddSingleton<ServerApiRegistrar>();
            services.AddSingleton<IServerApiRegistrar>(sp => sp.GetRequiredService<ServerApiRegistrar>());
        }

        // Register KCP gateway as hosted service
        services.AddHostedService<Kcp.KcpGateway>();

        return services;
    }

    /// <summary>
    /// Adds all ServerApi transports: WebSocket, TCP Stream, and KCP.
    /// </summary>
    public static IServiceCollection AddServerApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddServerApiWebSocket(configuration);
        services.AddServerApiTcpStream(configuration);
        services.AddServerApiKcp(configuration);
        return services;
    }
}

/// <summary>
/// Extension methods for mapping ServerApi endpoints.
/// </summary>
public static class ServerApiApplicationBuilderExtensions
{
    /// <summary>
    /// Maps WebSocket endpoints based on configuration patterns.
    /// </summary>
    public static IApplicationBuilder UseServerApiWebSocket(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<IOptions<ServerApiOptions>>();
        var gateway = app.ApplicationServices.GetRequiredService<WebSocket.WebSocketGatewayNew>();

        var patterns = options.Value.WebSocket?.Patterns ?? new System.Collections.Generic.List<string> { "/ws" };

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(options.Value.WebSocket?.KeepAliveInterval ?? 30)
        });

        app.Use(async (context, next) =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var path = context.Request.Path.ToString();
                if (patterns.Any(pattern => path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)))
                {
                    await gateway.HandleWebSocketAsync(context);
                    return;
                }
            }

            await next();
        });

        return app;
    }
}
