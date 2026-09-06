using System.Reflection;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI23` — the closed hierarchy is structurally exhaustive: abstract,
/// exactly two nested sealed cases, no instance constructor reachable
/// outside the domain assembly. `BI27` — `InvoiceStatuses` parses and
/// writes the closed token set loudly.
/// </summary>
public sealed class InvoiceStateTests
{
    [Fact]
    public void BI23_InvoiceStateIsAnAbstractClosedHierarchyWithExactlyTwoCases_AndNoConstructorAccessibleOutsideTheDomainAssembly()
    {
        var type = typeof(InvoiceState);

        Assert.True(type.IsAbstract, "InvoiceState must be abstract.");

        var nestedSubtypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(t => t.IsSubclassOf(type))
            .ToList();
        Assert.Equal(2, nestedSubtypes.Count);
        Assert.Contains(nestedSubtypes, t => t == typeof(InvoiceState.Issued));
        Assert.Contains(nestedSubtypes, t => t == typeof(InvoiceState.Paid));

        var baseConstructors = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.All(baseConstructors, ctor => Assert.True(ctor.IsFamilyAndAssembly, $"constructor {ctor} must be private protected (IsFamilyAndAssembly)."));
    }

    [Fact]
    public void Issued_HasNoPaidAt()
    {
        InvoiceState state = new InvoiceState.Issued();
        Assert.Null(state.PaidAtOrNull);
    }

    [Fact]
    public void Paid_CannotBeConstructedWithoutItsInstant_AndCarriesIt()
    {
        var instant = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        InvoiceState state = new InvoiceState.Paid(instant);
        Assert.Equal(instant, state.PaidAtOrNull);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("PAID")]
    [InlineData("settled")]
    [InlineData("")]
    public void BI27_ParsesTheClosedStatusTokenSetAndRaisesOnAnythingElse_AndWritesOnlyTheLowerCaseContractTokens(string invalidToken)
    {
        Assert.Throws<UnknownInvoiceStatusError>(() => InvoiceStatuses.Parse(invalidToken, null));
    }

    [Fact]
    public void ToToken_ReturnsOnlyTheLowerCaseContractTokens()
    {
        Assert.Equal("issued", InvoiceStatuses.ToToken(new InvoiceState.Issued()));
        Assert.Equal("paid", InvoiceStatuses.ToToken(new InvoiceState.Paid(DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void Parse_RoundTripsIssuedAndPaid()
    {
        var issued = InvoiceStatuses.Parse("issued", null);
        Assert.IsType<InvoiceState.Issued>(issued);

        var instant = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var paid = InvoiceStatuses.Parse("paid", instant);
        var paidState = Assert.IsType<InvoiceState.Paid>(paid);
        Assert.Equal(instant, paidState.PaidAt);
    }
}
