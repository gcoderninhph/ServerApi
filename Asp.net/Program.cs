using ServerApi.Extensions;
using Asp.net.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to listen on all interfaces (0.0.0.0)
builder.WebHost.UseUrls("http://0.0.0.0:5000", "http://0.0.0.0:5001");

// Clear default logging providers và add custom providers
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
// Add file logger
var logPath = Path.Combine(Directory.GetCurrentDirectory(), "Logs", $"server-{DateTime.Now:yyyyMMdd-HHmmss}.log");
builder.Logging.AddProvider(new FileLoggerProvider(logPath));

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR for Web UI
builder.Services.AddSignalR();

// Add CORS for SignalR
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()  // Cho phép mọi nguồn
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add MessageSnapshotStore (in-memory)
builder.Services.AddSingleton<MessageSnapshotStore>();

// Configure ServerApi services từ appsettings.json
builder.Services.AddServerApiWebSocket(builder.Configuration);  // WebSocket - CHỈ 1 DÒNG!
builder.Services.AddServerApiTcpStream(builder.Configuration);  // TCP Stream - CHỈ 1 DÒNG!
builder.Services.AddServerApiKcp(builder.Configuration);        // KCP - CHỈ 1 DÒNG!

// Add RegisterServices to handle commands
builder.Services.AddSingleton<RegisterServices>();

var app = builder.Build();

// Initialize RegisterServices to register command handlers
var registerServices = app.Services.GetRequiredService<RegisterServices>();
Console.WriteLine("✅ Command handlers registered");

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// app.UseHttpsRedirection(); // Disabled for development

// Enable CORS
app.UseCors();

// Serve static files (server-ui.html)
app.UseStaticFiles();

// Map SignalR Hub for Web UI
app.MapHub<UiHub>("/hubs/ui");

// Map WebSocket endpoints - Tự động config từ appsettings.json - CHỈ 1 DÒNG!
app.UseServerApiWebSocket();

// TCP Stream Gateway tự động start như IHostedService

app.MapControllers();

// Lấy địa chỉ IP local để hiển thị
var localIp = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName())
    .AddressList
    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
    ?.ToString() ?? "localhost";

Console.WriteLine("=== Asp.net Server (Listening on 0.0.0.0) ===");
Console.WriteLine($"WebSocket: ws://0.0.0.0:5000/ws (or ws://{localIp}:5000/ws)");
Console.WriteLine($"HTTP API:  http://0.0.0.0:5001 (or http://{localIp}:5001)");
Console.WriteLine($"TCP Stream: tcp://0.0.0.0:5003 (or tcp://{localIp}:5003)");
Console.WriteLine($"Web UI:    http://{localIp}:5001/server-ui.html");
Console.WriteLine();

app.Run();
