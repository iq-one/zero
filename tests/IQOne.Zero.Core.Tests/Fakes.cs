using IQOne.Zero.Modules;

namespace IQOne.Zero.Tests;

internal abstract class FakeModule(params Type[] dependencies) : IModule
{
    public string Name => GetType().Name;

    public IReadOnlyList<Type> Dependencies { get; } = dependencies;
}

internal sealed class CoreModule() : FakeModule;

internal sealed class SharedModule() : FakeModule(typeof(CoreModule));

internal sealed class RadiologyModule() : FakeModule(typeof(CoreModule), typeof(SharedModule));

internal sealed class LaboratoryModule() : FakeModule(typeof(SharedModule));

// A cycle: A -> B -> A
internal sealed class CycleA() : FakeModule(typeof(CycleB));
internal sealed class CycleB() : FakeModule(typeof(CycleA));

/// <summary>Records the order the lifecycle phases ran in.</summary>
internal class RecordingModule(List<string> log, params Type[] dependencies)
    : FakeModule(dependencies),
      IModuleConfigureServicesStep, IModuleInitializeStep, IModulePreRunStep, IModulePostRunStep
{
    public ValueTask OnConfigureServicesAsync(IModuleServiceContext context, CancellationToken cancellationToken)
    {
        log.Add($"{Name}:configure");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnInitializeAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add($"{Name}:initialize");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnPreRunAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add($"{Name}:prerun");
        return ValueTask.CompletedTask;
    }

    public ValueTask OnPostRunAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        log.Add($"{Name}:postrun");
        return ValueTask.CompletedTask;
    }
}

internal sealed class FirstModule(List<string> log) : RecordingModule(log);

internal sealed class SecondModule(List<string> log) : RecordingModule(log, typeof(FirstModule));
