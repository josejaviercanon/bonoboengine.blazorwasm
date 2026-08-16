using System.Text.Json;
using Game.Engine.ECS;
using Game.Examples;
using Game.Web.Components;
using Game.UI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();
builder.Services.AddSingleton<EcsSimulation>();
builder.Services.AddSingleton<SnakeSimulation>();
builder.Services.AddSingleton<TetrisSimulation>();

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

app.Run();
