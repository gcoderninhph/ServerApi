using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using ServerApi.Abstractions;
using ServerApi.Configuration;
using ServerApi.Internal;
using ServerApi.Unity.Abstractions;
using ServerApi.Unity.Configs;
using ServerApi.Unity.Server;

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
        InitLogger();
        KcpServerConfig kcpServerConfig = new();
        string configPortSection = ServerApiOptions.SectionName + ":Kcp:Port";
        ushort port = (configuration.GetValue<int>(configPortSection) is int p && p > 0 && p <= 65535) ? 
            (ushort)p : throw new InvalidOperationException($"{configPortSection} port configuration is missing.");
        kcpServerConfig.Port = port;

        UnityKcpServer server = new(kcpServerConfig);
        server.Start();

        _ = Task.Run(async() => {
            while(server.IsRunning){
                server.DispatchMessages();
                await Task.Delay(10);
            }
        });

        services.AddSingleton(server);

        return services;
    }

    private static void InitLogger()
    {
        if (Log.Logger == Serilog.Core.Logger.None)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()                              // Log ra terminal
                .WriteTo.File("logs/app-.log", rollingInterval: RollingInterval.Day)  // Log ra file theo ngày
                .Enrich.FromLogContext()
                .CreateLogger();
        }
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
