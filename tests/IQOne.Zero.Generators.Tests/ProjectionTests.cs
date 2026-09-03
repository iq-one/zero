using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The generated selector, and the members it refuses to guess at.
/// </summary>
/// <remarks>
/// The refusals matter more than the successes. A mapper that fills what it can and leaves
/// the rest empty produces a response with a silently absent field — the failure mode this
/// generator exists to replace — so every test that asserts a diagnostic is asserting that
/// the generator did NOT quietly do something.
/// </remarks>
public class ProjectionTests
{
    private const string Preamble = """
        using System;
        using System.Collections.Generic;
        using IQOne.Zero.Persistence;

        namespace Test;

        public sealed class Invoice : IAggregateRoot
        {
            public int Id { get; set; }
            public string? Number { get; set; }
            public decimal Total { get; set; }
            public short Version { get; set; }
            public int? CustomerId { get; set; }
            public InvoiceState State { get; set; }
            public string? Internal { get; set; }
        }

        public enum InvoiceState : byte { Draft = 0, Sent = 1 }
        """;

    [Fact]
    public void Members_that_match_by_name_are_written()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public string? Number { get; set; }
                public decimal Total { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrorMessages.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("Id = e.Id")
            .And.Contain("Number = e.Number")
            .And.Contain("Total = e.Total");

        // Entity'nin FAZLA alani sorun degil: model daralttigini soyluyor.
        run.GeneratedSource.Should().NotContain("Internal");
    }

    [Fact]
    public void A_member_with_no_source_is_an_ERROR_not_an_empty_field()
    {
        // Bu testin varlik sebebi: tasima sirasinda tam bu hata uc kez oldu (FlowFilters,
        // FoundationSubTypeId, Tags) ve ucunde de alan sessizce null donuyordu.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public string? Tags { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO220");
        run.Diagnostics.Single(d => d.Id == "ZERO220").GetMessage()
            .Should().Contain("Tags").And.Contain("Ignore");

        // Ve HICBIR SEY uretilmiyor: ceyregi eksik bir projeksiyon en kotu sonuc.
        run.GeneratedSource.Should().NotContain("Id = e.Id");
    }

    [Fact]
    public void An_ignored_member_is_left_alone()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public decimal Price { get; set; }
            }

            [Projection(Ignore = [nameof(InvoiceModel.Price)])]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("Id = e.Id").And.NotContain("Price =");
    }

    [Fact]
    public void An_ignore_entry_that_matches_nothing_is_reported()
    {
        // Yanlis yazilmis bir ignore girdisi hicbir seyi susturmaz ama gercek bir bosluk
        // hesaba katilmis gibi okunur.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
            }

            [Projection(Ignore = ["Prise"])]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO221");
    }

    [Fact]
    public void A_widening_conversion_is_written_directly()
    {
        // short -> int, byte -> int: derleyici zaten donusturuyor, tahmin yok.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public int Version { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("Version = e.Version");
    }

    [Fact]
    public void An_enum_and_its_number_are_cast()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public byte State { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrorMessages.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("State = (byte)e.State");
    }

    [Fact]
    public void A_NULLABLE_source_into_a_non_nullable_member_is_REFUSED()
    {
        // Tasimada bu tam olarak oldu: entity'de bool?, modelde bool, ve AutoMapper null'i
        // sessizce false yapiyordu. Yedek deger bir KARAR ve projeksiyonda gorunmeli.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public int CustomerId { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO220");
        run.Diagnostics.Single(d => d.Id == "ZERO220").GetMessage()
            .Should().Contain("nullable");
    }

    [Fact]
    public void A_NARROWING_conversion_is_REFUSED()
    {
        // int -> short bir cast ile derlenir ve SESSIZCE sarar. Uretec bunu yazmaz.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel
            {
                public short Id { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO220");
    }

    [Fact]
    public void A_nested_model_is_REFUSED_in_this_version()
    {
        // Ic ice bir model, ic ice bir sorgu demek ve karari uc noktanin: gezinti
        // yuklenecek mi, hangi kosulla, hangi alanlar. Uretec bunu sormaz, reddeder.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class CustomerModel { public int Id { get; set; } }

            public sealed class InvoiceModel
            {
                public int Id { get; set; }
                public CustomerModel? Customer { get; set; }
            }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO220");
    }

    [Fact]
    public void A_base_layer_between_the_specification_and_the_query_is_walked()
    {
        // Uygulamalar araya kendi katmanini koyuyor — her sorguya sayfalama ve durum
        // kurallarini uygulayan bir taban. O taban da ayni iki tipin projeksiyonu.
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel { public int Id { get; set; } }

            public abstract class AppQuery<T, TResult> : Specification<T, TResult>
                where T : class;

            [Projection]
            public sealed partial class InvoiceQuery : AppQuery<Invoice, InvoiceModel>;
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedSource.Should().Contain("Id = e.Id");
    }

    [Fact]
    public void A_hand_written_Selector_and_the_attribute_together_are_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}
            using System.Linq.Expressions;

            public sealed class InvoiceModel { public int Id { get; set; } }

            [Projection]
            public sealed partial class InvoiceQuery : Specification<Invoice, InvoiceModel>
            {
                public override Expression<Func<Invoice, InvoiceModel>> Selector =>
                    e => new InvoiceModel { Id = e.Id };
            }
            """);

        run.DiagnosticIds.Should().Contain("ZERO224");
    }

    [Fact]
    public void A_non_partial_class_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed class InvoiceModel { public int Id { get; set; } }

            [Projection]
            public sealed class InvoiceQuery : Specification<Invoice, InvoiceModel>;
            """);

        run.DiagnosticIds.Should().Contain("ZERO223");
    }

    [Fact]
    public void The_attribute_on_something_that_is_not_a_specification_is_reported()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            [Projection]
            public sealed partial class NotAQuery;
            """);

        run.DiagnosticIds.Should().Contain("ZERO222");
    }
}
