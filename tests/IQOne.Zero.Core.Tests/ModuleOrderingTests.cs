using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Tests;

/// <summary>
/// The order is never written by hand; it is derived from what each module depends on.
/// These tests pin that derivation.
/// </summary>
public class ModuleOrderingTests
{
    private static IReadOnlyList<IModule> Resolve(params IModule[] modules)
    {
        var services = new ServiceCollection();
        services.AddModules(modules);

        return services.BuildServiceProvider().GetRequiredService<IReadOnlyList<IModule>>();
    }

    [Fact]
    public void A_dependency_is_configured_before_its_dependent()
    {
        var ordered = Resolve(new RadiologyModule(), new SharedModule(), new CoreModule());

        ordered.Select(m => m.Name).Should().Equal("CoreModule", "SharedModule", "RadiologyModule");
    }

    [Fact]
    public void Input_order_does_not_change_the_result()
    {
        var a = Resolve(new CoreModule(), new SharedModule(), new RadiologyModule());
        var b = Resolve(new RadiologyModule(), new CoreModule(), new SharedModule());

        a.Select(m => m.Name).Should().Equal(b.Select(m => m.Name));
    }

    [Fact]
    public void Independent_modules_are_ordered_by_name_so_the_result_is_deterministic()
    {
        var ordered = Resolve(new RadiologyModule(), new LaboratoryModule(), new SharedModule(), new CoreModule());

        // Laboratory and Radiology both depend on Shared; between the two, name decides.
        ordered.Select(m => m.Name).Should()
            .Equal("CoreModule", "SharedModule", "LaboratoryModule", "RadiologyModule");
    }

    [Fact]
    public void A_cycle_throws_and_names_the_modules_involved()
    {
        var act = () => Resolve(new CycleA(), new CycleB());

        act.Should().Throw<ModuleDependencyCycleException>()
            .Which.Cycle.Should().Contain("CycleA").And.Contain("CycleB");
    }

    [Fact]
    public void A_cycle_report_names_the_participants_and_nothing_else()
    {
        // DownstreamOfCycle cannot be ordered either, but it is not part of the cycle and a
        // reader who changes it fixes nothing.
        var act = () => Resolve(new CycleA(), new CycleB(), new DownstreamOfCycle());

        act.Should().Throw<ModuleDependencyCycleException>()
            .Which.Cycle.Should().NotContain(nameof(DownstreamOfCycle));
    }

    [Fact]
    public void A_dependency_on_a_module_that_was_not_added_fails_and_names_both()
    {
        // Dropping it silently degraded the ordering, which is worse than not starting.
        var act = () => Resolve(new SharedModule());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SharedModule*")
            .WithMessage("*CoreModule*");
    }

    [Fact]
    public void The_resolved_order_is_rendered_readably()
    {
        var services = new ServiceCollection();
        services.AddModules(new RadiologyModule(), new SharedModule(), new CoreModule());

        var text = services.BuildServiceProvider().DescribeModuleGraph();

        text.Should().Contain("Module order")
            .And.Contain("1. CoreModule")
            .And.Contain("3. RadiologyModule");
    }
}
