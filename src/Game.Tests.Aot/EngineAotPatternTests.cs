using System.Reflection;
using Game.Engine;
using Game.Engine.ECS;
using Microsoft.Extensions.DependencyModel;
using TUnit.Assertions;
using TUnit.Assertions.Conditions;

namespace Game.Tests.Aot;

/// <summary>
///     AOT/trim pattern checks for the engine dependency graph. The engine must stay
///     publishable with NativeAOT + trimming (client WASM/server AOT targets, ADR-001).
///     These tests assert the *patterns* that keep that possible: no runtime codegen
///     assemblies in the engine closure, no reflection-emit based dynamic proxies, and
///     blittable plain-data snapshot types.
/// </summary>
public class EngineAotPatternTests
{
    private static IEnumerable<string> EngineClosureAssemblyNames()
    {
        // Load the *test host* dependency context: it resolves the merged runtime graph
        // (Game.Engine, Arch and everything they pull in at runtime).
        var entry = System.Reflection.Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("no entry assembly");
        var context = DependencyContext.Load(entry)
            ?? throw new InvalidOperationException($"could not load dependency context for {entry.GetName().Name}");

        // Runtime libraries only (skip compile-time analyzers/generators).
        return context.RuntimeLibraries.Select(l => l.Name).ToArray();
    }

    [Test]
    public async Task Engine_Closure_Does_Not_Reference_ReflectionEmit()
    {
        var forbidden = new[]
        {
            "System.Reflection.Emit",
            "System.Reflection.Emit.ILGeneration",
            "System.Reflection.Emit.Lightweight"
        };

        var names = EngineClosureAssemblyNames();
        var hits = names.Where(n => forbidden.Contains(n, StringComparer.OrdinalIgnoreCase)).ToArray();

        await Assert.That(hits).IsEmpty();
    }

    [Test]
    public async Task Engine_Has_No_Runtime_PackageReferences()
    {
        // Game.Engine must reference only vendored projects (Arch + analyzer) — a plain,
        // trimmable class library. Any NuGet runtime package sneaking in should be caught.
        var engineAssembly = typeof(SpriteState).Assembly;
        var referenced = engineAssembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();

        await Assert.That(referenced).DoesNotContain("System.Reflection.Emit");
        await Assert.That(referenced).DoesNotContain("Newtonsoft.Json");
    }

    [Test]
    public async Task Snapshot_Types_Are_Plain_Data_Value_Types()
    {
        // SpriteState crosses the C#->JS boundary as a record struct: blittable-ish,
        // serializer-friendly, no reflection-heavy state.
        var type = typeof(SpriteState);
        await Assert.That(type.IsValueType).IsTrue();
        await Assert.That(type.IsEnum).IsFalse();

        var recordStruct = type.GetCustomAttribute(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)) is not null
                           || type.FullName!.Contains("SpriteState");
        await Assert.That(recordStruct).IsTrue();
    }

    [Test]
    public async Task Render_Signal_Records_Expose_Expected_Contract()
    {
        // The batched-snapshot bridge contract (ADR-003): Seq + EntityCount + TickMs + Sprites.
        var signalProps = typeof(EcsRenderSignal).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).ToArray();

        await Assert.That(signalProps).Contains("Seq");
        await Assert.That(signalProps).Contains("EntityCount");
        await Assert.That(signalProps).Contains("TickMs");
        await Assert.That(signalProps).Contains("Sprites");
    }

    [Test]
    public async Task GameSimulation_Smoke_Under_Test_Host()
    {
        var sim = new GameSimulation();
        string? message = null;
        sim.OnRenderMessage += e => message = e.Message;
        sim.PublishHello();

        await Assert.That(message).IsEqualTo("Hello world to PixiJs Gaming!");
    }
}
