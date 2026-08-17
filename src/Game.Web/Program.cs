using System.Text.Json;
using System.Threading.Channels;
using Game.Engine.ECS;
using Game.Engine.ECS.Asteroids;
using Game.Engine.ECS.Breakout;
using Game.Engine.ECS.Pacman;
using Game.Engine.ECS.Racer;
using Game.Engine.ECS.Snake;
using Game.Engine.ECS.Tetris;
using Game.Examples;
using Game.Web.Components;
using Game.UI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();
builder.Services.AddSingleton<EcsSimulation>();
builder.Services.AddSingleton<SnakeSimulation>();
builder.Services.AddSingleton<TetrisSimulation>();
builder.Services.AddSingleton<BreakoutSimulation>();
builder.Services.AddSingleton<PacmanSimulation>();
builder.Services.AddSingleton<AsteroidsSimulation>();
builder.Services.AddSingleton<RacerSimulation>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddAdditionalAssemblies(typeof(GameView).Assembly)
    .AddAdditionalAssemblies(typeof(ExamplesHome).Assembly);

// SSE push of batched ECS render signals. The ECS simulation throttles to one
// signal per second; each signal moves every client sprite. No SignalR involved.
app.MapGet("/api/ecs/stream", (EcsSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<EcsRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: sprite-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// SSE push of batched snake render signals, one per 8 Hz grid step.
app.MapGet("/api/snake/stream", (SnakeSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<SnakeRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: snake-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// Client input channel for the snake scene. The JS layer only suggests a direction;
// the simulation validates and applies it on the next grid step (C# sole authority).
app.MapPost("/api/snake/input", (SnakeSimulation sim, SnakeInputRequest request) =>
{
    sim.QueueDirection(request.Direction);
    return Results.NoContent();
});

// Starts a fresh snake game (start button or Space bar). Restarts if game over.
app.MapPost("/api/snake/start", (SnakeSimulation sim) =>
{
    sim.Start();
    return Results.NoContent();
});

// One-shot final-position report from the presentation physics (Rapier food drop).
// The ECS records the final food cell and accepts no further drop events of this
// type for the current food.
app.MapPost("/api/snake/food-dropped", (SnakeSimulation sim, FoodDroppedRequest request) =>
{
    sim.FoodDropped(request.X, request.Y);
    return Results.NoContent();
});

app.MapPost("/api/snake/restart", (SnakeSimulation sim) =>
{
    sim.Reset();
    return Results.NoContent();
});

// SSE push of batched Pacman render signals, one per authoritative fixed tick.
// The simulation callback only enqueues into a bounded channel, so a slow browser
// cannot block the ECS timer or throw from inside the simulation lock.
app.MapGet("/api/pacman/stream", async (PacmanSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var signals = Channel.CreateBounded<PacmanRenderSignal>(new BoundedChannelOptions(2)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false,
    });

    Action<PacmanRenderSignal> handler = signal => signals.Writer.TryWrite(signal);

    sim.OnRenderSignal += handler;

    try
    {
        await foreach (var signal in signals.Reader.ReadAllAsync(ct))
        {
            var json = JsonSerializer.Serialize(signal, jsonOptions);
            await response.WriteAsync($"event: pacman-move\ndata: {json}\n\n", ct);
            await response.Body.FlushAsync(ct);
        }
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        // Browser disconnected; cleanup below.
    }
    finally
    {
        sim.OnRenderSignal -= handler;
        signals.Writer.TryComplete();
    }
});

// Client only suggests direction; PacmanInputSystem validates it on the next fixed tick.
app.MapPost("/api/pacman/input", (PacmanSimulation sim, PacmanInputRequest request) =>
{
    sim.QueueDirection(request.Direction);
    return Results.NoContent();
});

app.MapPost("/api/pacman/start", (PacmanSimulation sim) =>
{
    sim.Start();
    return Results.NoContent();
});

app.MapPost("/api/pacman/restart", (PacmanSimulation sim) =>
{
    sim.Reset();
    return Results.NoContent();
});

// SSE push of batched tetris render signals, one per board mutation.
app.MapGet("/api/tetris/stream", (TetrisSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<TetrisRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: tetris-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// Client input channel for the tetris scene. The JS layer only suggests a command;
// the simulation validates and applies it (C# sole authority).
app.MapPost("/api/tetris/input", (TetrisSimulation sim, TetrisInputRequest request) =>
{
    sim.QueueInput(request.Command);
    return Results.NoContent();
});

// Starts a fresh tetris game (start button or Space bar). Restarts if game over.
app.MapPost("/api/tetris/start", (TetrisSimulation sim) =>
{
    sim.Start();
    return Results.NoContent();
});

app.MapPost("/api/tetris/restart", (TetrisSimulation sim) =>
{
    sim.Reset();
    return Results.NoContent();
});

// SSE push of batched breakout render signals, one per 60 Hz physics tick while running.
app.MapGet("/api/breakout/stream", (BreakoutSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<BreakoutRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: breakout-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// Client input channel for the breakout scene. The JS layer only suggests the held
// paddle direction + a one-shot launch; the simulation validates and applies it (C# sole authority).
app.MapPost("/api/breakout/input", (BreakoutSimulation sim, BreakoutInputRequest request) =>
{
    sim.QueueInput(request);
    return Results.NoContent();
});

// Starts a fresh breakout game (start button or Space bar). Restarts if game over.
app.MapPost("/api/breakout/start", (BreakoutSimulation sim) =>
{
    sim.Start();
    return Results.NoContent();
});

app.MapPost("/api/breakout/restart", (BreakoutSimulation sim) =>
{
    sim.Reset();
    return Results.NoContent();
});

// SSE push of batched asteroids render signals, one per 60 Hz physics tick while running.
app.MapGet("/api/asteroids/stream", (AsteroidsSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<AsteroidsRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: asteroids-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// Client input channel for the asteroids scene. The JS layer only suggests the held
// controls + one-shot fire/hyperspace; the simulation validates and applies them (C# sole authority).
app.MapPost("/api/asteroids/input", (AsteroidsSimulation sim, AsteroidsInputRequest request) =>
{
    sim.QueueInput(request);
    return Results.NoContent();
});

// Starts a fresh asteroids game (start button or Space bar). Restarts if game over.
app.MapPost("/api/asteroids/start", (AsteroidsSimulation sim) =>
{
    sim.Start();
    return Results.NoContent();
});

app.MapPost("/api/asteroids/restart", (AsteroidsSimulation sim) =>
{
    sim.Reset();
    return Results.NoContent();
});

// SSE push of batched racer snapshots. Static track geometry travels in the SSR payload;
// live signals carry player state and traffic within draw distance.
app.MapGet("/api/racer/stream", (RacerSimulation sim, HttpResponse response, CancellationToken ct) =>
{
    response.ContentType = "text/event-stream";

    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var writeSync = new object();

    Action<RacerRenderSignal> handler = signal =>
    {
        var json = JsonSerializer.Serialize(signal, jsonOptions);
        lock (writeSync)
        {
            response.WriteAsync($"event: racer-move\ndata: {json}\n\n").GetAwaiter().GetResult();
        }
    };

    sim.OnRenderSignal += handler;

    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ct.Register(() =>
    {
        sim.OnRenderSignal -= handler;
        completed.TrySetResult();
    });
    return completed.Task;
});

// Client only suggests held input; ECS consumes it on the next fixed tick.
app.MapPost("/api/racer/input", (RacerSimulation sim, RacerInputRequest request) =>
{
    sim.QueueInput(request);
    return Results.NoContent();
});

app.MapPost("/api/racer/config", (RacerSimulation sim, RacerConfigRequest request) =>
{
    sim.ApplyConfig(request);
    return Results.NoContent();
});

app.MapPost("/api/racer/pause", (RacerSimulation sim) =>
{
    sim.Pause();
    return Results.NoContent();
});

app.MapPost("/api/racer/resume", (RacerSimulation sim) =>
{
    sim.Resume();
    return Results.NoContent();
});

app.MapPost("/api/racer/restart", (RacerSimulation sim) =>
{
    sim.Restart();
    return Results.NoContent();
});

app.Run();
