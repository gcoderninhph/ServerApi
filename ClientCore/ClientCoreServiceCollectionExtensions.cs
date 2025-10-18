using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ClientCore.WebSocket;
using ClientCore.TcpStream;

namespace ClientCore;

/// <summary>
/// Extension methods để đăng ký ClientCore services vào DI container
/// </summary>
public static class ClientCoreServiceCollectionExtensions
{
    /// <summary>
    /// Đăng ký WebSocket client services
    /// </summary>
    public static IServiceCollection AddClientApiWebSocket(this IServiceCollection services)
    {
        // WebSocket Register (Singleton) - cần ILoggerFactory
        services.AddSingleton<IWebSocketClientRegister>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WebSocketClientRegister>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new WebSocketClientRegister(logger, loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Đăng ký TCP Stream client services
    /// </summary>
    public static IServiceCollection AddClientApiTcpStream(this IServiceCollection services)
    {
        // TCP Stream Register (Singleton) - cần ILoggerFactory
        services.AddSingleton<ITcpStreamClientRegister>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TcpStreamClientRegister>>();
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return new TcpStreamClientRegister(logger, loggerFactory);
        });

        return services;
    }
}
