using System.Net.WebSockets;
using RFIDPoker.Emulator.Options;
using RFIDPoker.Emulator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<EmulatorConfig>(builder.Configuration.GetSection(EmulatorConfig.SectionName));

builder.Services.AddHttpClient();

// Shared tag store + background broadcaster.
builder.Services.AddSingleton<TagEmitter>();
builder.Services.AddSingleton<ITagEmitter>(sp => sp.GetRequiredService<TagEmitter>());
builder.Services.AddHostedService<EmulatedReaderHost>();

// Hand simulator + one-shot mapping seeder.
builder.Services.AddHostedService<HandSimulator>();
builder.Services.AddHostedService<MappingSeeder>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// Pepper-compatible endpoint. Point RFIDPoker.Api's Rfid:Devices[0]:WebSocketUrl at
// ws://localhost:<port>/wscomm.cgi to replace the physical reader.
app.Map("/wscomm.cgi", async (HttpContext ctx, TagEmitter emitter, ILogger<Program> logger) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsync("Expected a WebSocket request.");
        return;
    }

    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    var id = emitter.Register(socket);
    logger.LogInformation("Client {Id} connected. Total: {Count}", id, emitter.ActiveSockets.Count);

    var buffer = new byte[1024];
    try
    {
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, ctx.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close) break;
        }
    }
    catch (OperationCanceledException) { }
    catch (WebSocketException) { }
    finally
    {
        emitter.Unregister(id);
        logger.LogInformation("Client {Id} disconnected. Total: {Count}", id, emitter.ActiveSockets.Count);
    }
});

// Health/status endpoint useful for debugging.
app.MapGet("/", (TagEmitter emitter) => Results.Ok(new
{
    device = emitter.DeviceName,
    clients = emitter.ActiveSockets.Count,
    tagsByAntenna = emitter.Snapshot()
}));

app.Run();
