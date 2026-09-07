using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// How the shape being produced gets written.
/// </summary>
/// <remarks>
/// Two forms, and which applies is not a preference: a type with a parameterless constructor is
/// written as an initialiser, and one without has to be written positionally. A positional
/// record has no parameterless constructor at all, and it is the commonest destination in the
/// ported code — twelve of twenty-one hand-written selectors in the application this framework
/// was built for. An initialiser over one of those does not compile.
/// </remarks>
public class MapConstructionTests
{
    [Fact]
    public void A_positional_RECORD_is_written_positionally()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Configuration
            {
                public string? Key { get; set; }
                public string? Value { get; set; }
                public byte State { get; set; }
            }

            public sealed record ConfigurationRow(string? Key, string? Value);

            public sealed partial class ConfigurationMap : Map<Configuration, ConfigurationRow>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("new global::Test.ConfigurationRow(");

        // Sira ANLAM tasiyor: kurucunun bildirdigi sira.
        var text = run.GeneratedSource;
        text.IndexOf("source.Key").Should().BeLessThan(text.IndexOf("source.Value"));
    }

    [Fact]
    public void The_order_follows_the_CONSTRUCTOR_not_the_source()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } public int B { get; set; } }

            /// <summary>Kaynaktakinin TERSI sirada.</summary>
            public sealed record RowModel(int B, int A);

            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        var text = run.GeneratedSource;
        text.IndexOf("source.B").Should().BeLessThan(text.IndexOf("source.A"));
    }

    [Fact]
    public void A_parameter_with_no_source_is_an_ERROR()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } }
            public sealed record RowModel(int A, string? Missing);

            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO250");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("Missing"));
    }

    [Fact]
    public void A_parameter_can_be_declared_like_any_other_member()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public enum State : byte { Off = 0, On = 1 }

            public sealed class Row { public int A { get; set; } public byte State { get; set; } }
            public sealed record RowModel(int A, State State);

            public sealed partial class RowMap : Map<Row, RowModel>
            {
                protected override void Configure(IMapBuilder<Row, RowModel> map)
                    => map.Member(m => m.State, e => (State)e.State);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should().Contain("(State)source.State");
    }

    [Fact]
    public void An_IGNORED_parameter_gets_the_default_because_one_must_be_passed()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } }
            public sealed record RowModel(int A, string? Note);

            public sealed partial class RowMap : Map<Row, RowModel>
            {
                protected override void Configure(IMapBuilder<Row, RowModel> map)
                    => map.Ignore(m => m.Note);
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        // FullyQualifiedFormat nullable isaretini tasimiyor; etkisi ayni.
        run.GeneratedSource.Should().Contain("default(string)");
    }

    [Fact]
    public void A_record_with_an_EXTRA_settable_member_gets_both_forms()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row
            {
                public int A { get; set; }
                public string? Note { get; set; }
            }

            public sealed record RowModel(int A)
            {
                public string? Note { get; set; }
            }

            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("new global::Test.RowModel(")
            .And.Contain("Note = source.Note");
    }

    [Fact]
    public void SEVERAL_constructors_and_no_parameterless_one_is_refused()
    {
        // Birini secmek, ureteci cagiranin hangi sekli kastettigine sessizce karar
        // verdirmek olurdu.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int A { get; set; } }

            public sealed class RowModel
            {
                public RowModel(int a) => A = a;
                public RowModel(int a, string? b) { A = a; B = b; }

                public int A { get; }
                public string? B { get; }
            }

            public sealed partial class RowMap : Map<Row, RowModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO260");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("2 constructors"));
    }

    [Fact]
    public void A_TUPLE_destination_is_refused_with_the_reason()
    {
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int Id { get; set; } public string? Tags { get; set; } }

            public sealed partial class RowMap : Map<Row, (int Id, string? Tags)>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO260");
        run.DiagnosticMessages.Should().Contain(m => m.Contains("Item1"));
    }

    [Fact]
    public void A_SCALAR_destination_produces_nothing_and_is_reported()
    {
        // Onceden bu sessizce `new int()` yaziyordu: mumkun olan en bos yanlis cevap.
        var run = GeneratorHarness.Run("""
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Row { public int Id { get; set; } }

            public sealed partial class RowMap : Map<Row, int>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO261");
    }

    [Fact]
    public void A_composed_member_can_be_a_positional_record_too()
    {
        var run = GeneratorHarness.Run("""
            using System.Collections.Generic;
            using System.Linq;
            using IQOne.Zero.Mapping;

            namespace Test;

            public sealed class Role
            {
                public int Id { get; set; }
                public ICollection<Claim> Claims { get; set; } = [];
            }

            public sealed class Claim { public string? Key { get; set; } public string? Value { get; set; } }

            public sealed record ClaimRow(string? Key, string? Value);

            public sealed record RoleClaimGroup(int RoleId, IReadOnlyList<ClaimRow>? Claims);

            public sealed partial class ClaimMap : Map<Claim, ClaimRow>;

            public sealed partial class RoleMap : Map<Role, RoleClaimGroup>
            {
                protected override void Configure(IMapBuilder<Role, RoleClaimGroup> map) => map
                    .Member(m => m.RoleId, e => e.Id)
                    .Member(m => m.Claims, e => e.Claims.To<IReadOnlyList<ClaimRow>>());
            }
            """);

        run.DiagnosticMessages.Should().BeEmpty();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        // ClaimQueries.cs'teki gercek sekil: konumsal kayit artı ic ice konumsal koleksiyon.
        run.GeneratedSource.Should()
            .Contain("new global::Test.RoleClaimGroup(")
            .And.Contain("source.Claims.Select(")
            .And.Contain("new global::Test.ClaimRow(")
            .And.Contain(".ToList()");
    }
}
