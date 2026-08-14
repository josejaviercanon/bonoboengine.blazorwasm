using System.Text.Json;
using Game.Engine.ECS;
using Game.Examples;
using Game.Web.Components;
using Game.UI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents();
builder.Services.AddSingleton<EcsSimulation>();

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

app.Run();
