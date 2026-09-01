using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The module declaration itself: the namespace it lands in, the dependencies it claims, and
/// the guarantee that it is emitted at all. A missing module turns one diagnostic into a
/// second one that points nowhere.
/// </summary>
public class ModuleGenerationTests
{
    [Fact]
    public void DependsOn_states_an_ordering_the_reference_graph_does_not()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Modules;

            namespace Seeding
            {
                public sealed class Module;
            }

            namespace Test.Module
            {
                [DependsOn(typeof(global::Seeding.Module))]
                public sealed partial class Module;
            }
            """);

        // module-ordering.md documents this as the only way to express an ordering without a
        // reference, and nothing read it. The consumer could not work around it either: the
        // generated partial already declares Dependencies.
        run.GeneratedSource.Should().Contain("typeof(global::Seeding.Module),");
    }

    [Fact]
    public void An_assembly_name_needing_sanitisation_becomes_a_namespace_that_compiles()
    {
        var run = GeneratorHarness.Run("""
            namespace Test;

            public sealed class Placeholder;
            """, assemblyName: "Acme.Billing-Core");

        run.GeneratedSource.Should()
            .Contain("namespace Acme.Billing_Core;")
            .And.Contain("Name => \"Acme.Billing-Core\"");
    }

    [Fact]
    public void A_namespace_segment_never_starts_with_a_digit()
    {
        var run = GeneratorHarness.Run("""
            namespace Test;

            public sealed class Placeholder;
            """, assemblyName: "Company.2024.Api");

        // Digits passed through untouched, so the emitted namespace did not compile.
        run.GeneratedSource.Should().Contain("namespace Company._2024.Api;");
        run.GeneratedFileErrorMessages.Should().BeEmpty();
    }

    [Fact]
    public void A_module_in_an_assembly_whose_name_was_sanitised_is_found_as_a_dependency()
    {
        var upstream = GeneratorHarness.Reference("""
            namespace Acme.Billing_Core;

            public sealed class Module : global::IQOne.Zero.Modules.IModule
            {
                public string Name => "Acme.Billing-Core";
            }
            """, "Acme.Billing-Core");

        var run = GeneratorHarness.Run(
            ["""
             namespace Test;

             public sealed class Placeholder;
             """],
            "Test.Module",
            upstream);

        // Discovery looked up '{assembly.Name}.Module' raw while emission sanitised it, so a
        // module in Acme.Billing-Core was emitted into Acme.Billing_Core and never found.
        run.GeneratedSource.Should().Contain("typeof(global::Acme.Billing_Core.Module),");
    }

    [Fact]
    public void The_module_is_emitted_even_when_a_diagnostic_is_reported()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Messaging;
            using IQOne.Zero.Web;

            namespace Test;

            [Get("/things")]
            public sealed record NotARequest(int Id);
            """);

        run.DiagnosticIds.Should().Contain("ZERO300");

        // Withholding the file was decided from the descriptor's default severity, so a team
        // downgrading a rule in .editorconfig lost Module.g.cs entirely and got CS0246
        // instead. The error diagnostics fail the build on their own.
        run.GeneratedSource.Should().Contain("public sealed partial class Module");
    }
}
