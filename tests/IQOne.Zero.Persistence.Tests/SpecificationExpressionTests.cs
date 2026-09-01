using System.Linq.Expressions;
using IQOne.Zero.Persistence;

namespace IQOne.Zero.Persistence.Tests;

/// <summary>
/// Combining two lambdas naively produces a tree with two different parameters, which every
/// query provider rejects at translation time — not at compile time. These pin the rebinding
/// that makes the combined tree usable.
/// </summary>
public class SpecificationExpressionTests
{
    private static readonly Expression<Func<Invoice, bool>> Unpaid = i => !i.IsPaid;
    private static readonly Expression<Func<Invoice, bool>> Customer7 = i => i.CustomerId == 7;

    [Fact]
    public void AndAlso_produces_a_tree_with_a_single_parameter()
    {
        var combined = Unpaid.AndAlso(Customer7);

        combined.Parameters.Should().ContainSingle();

        // Every parameter reference inside the body must be that one parameter; a stray
        // second parameter is what makes a provider throw "could not be translated".
        new ParameterCounter(combined.Parameters[0]).Count(combined.Body).Should().Be(0);
    }

    [Fact]
    public void AndAlso_requires_both_predicates()
    {
        var combined = Unpaid.AndAlso(Customer7).Compile();

        combined(new Invoice { IsPaid = false, CustomerId = 7 }).Should().BeTrue();
        combined(new Invoice { IsPaid = true, CustomerId = 7 }).Should().BeFalse();
        combined(new Invoice { IsPaid = false, CustomerId = 9 }).Should().BeFalse();
    }

    [Fact]
    public void OrElse_requires_either_predicate()
    {
        var combined = Unpaid.OrElse(Customer7).Compile();

        combined(new Invoice { IsPaid = true, CustomerId = 7 }).Should().BeTrue();
        combined(new Invoice { IsPaid = false, CustomerId = 9 }).Should().BeTrue();
        combined(new Invoice { IsPaid = true, CustomerId = 9 }).Should().BeFalse();
    }

    [Fact]
    public void A_combined_predicate_still_works_as_a_queryable_filter()
    {
        Invoice[] invoices =
        [
            new() { Id = 1, IsPaid = false, CustomerId = 7 },
            new() { Id = 2, IsPaid = true, CustomerId = 7 }
        ];

        invoices.AsQueryable().Where(Unpaid.AndAlso(Customer7)).Select(i => i.Id).Should().Equal(1);
    }

    /// <summary>Counts parameter references that are not the expected one.</summary>
    private sealed class ParameterCounter(ParameterExpression expected) : ExpressionVisitor
    {
        private int _foreign;

        public int Count(Expression expression)
        {
            _foreign = 0;
            Visit(expression);

            return _foreign;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node != expected) _foreign++;

            return base.VisitParameter(node);
        }
    }
}
