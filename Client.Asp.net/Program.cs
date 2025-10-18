using ClientCore;
using Client.Asp.net.Services;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add File Logging
var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", $"client-{DateTime.Now:yyyyMMdd-HHmmss}.log");
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddProvider(new FileLoggerProvider(logFilePath));
builder.Logging.SetMinimumLevel(LogLevel.Information);

Console.WriteLine($"📝 Log file: {logFilePath}");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add SignalR for Web UI
builder.Services.AddSignalR();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5002", "http://127.0.0.1:5002")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add MessageSnapshotStore (in-memory)
builder.Services.AddSingleton<MessageSnapshotStore>();

// Add ClientCore services
builder.Services.AddClientApiWebSocket();
builder.Services.AddClientApiTcpStream();

// Add RegisterServices
builder.Services.AddSingleton<RegisterServices>();

// Add AutoConnect HostedService
builder.Services.AddHostedService<AutoConnectHostedService>();

// Add ConnectionMonitor for background testing
builder.Services.AddHostedService<ConnectionMonitor>();

var app = builder.Build();

// Configure the HTTP request pipeline
app.UseCors();
app.UseStaticFiles();

// Map SignalR Hub for Web UI
app.MapHub<UiHub>("/hubs/ui");

app.MapControllers();

Console.WriteLine("=== Client.Asp.net Web API ===");
Console.WriteLine("API URL: http://localhost:5002");
Console.WriteLine("Web UI: http://localhost:5002/client-ui.html");
Console.WriteLine();

app.Run("http://localhost:5002");
