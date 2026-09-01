using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>specs/shared/requirements.md R3. specs/shared/test-matrix.md row R3.</summary>
public sealed class QuantityTests
{
    [Fact]
    public void R3_Quantity_RefusesZeroNegativeAndFractionalValuesAndCreatesNoValueObject()
    {
        var zeroError = Assert.Throws<QuantityMustBePositiveError>(() => new Quantity(0));
        var negativeError = Assert.Throws<QuantityMustBePositiveError>(() => new Quantity(-5));
        var fractionalError = Assert.Throws<QuantityMustBePositiveError>(() => Quantity.From(2.5));

        Assert.Equal("quantity.must_be_strictly_positive_integer", zeroError.Code);
        Assert.Equal("quantity.must_be_strictly_positive_integer", negativeError.Code);
        Assert.Equal("quantity.must_be_strictly_positive_integer", fractionalError.Code);
    }

    [Fact]
    public void Quantity_AcceptsAStrictlyPositiveIntegerAndExposesItsValue()
    {
        var quantity = new Quantity(3);

        Assert.Equal(3, quantity.Value);
    }

    [Fact]
    public void Quantity_FromWholeNumberDoubleProducesTheEquivalentQuantity()
    {
        var quantity = Quantity.From(4.0);

        Assert.Equal(4, quantity.Value);
    }

    /// <summary>
    /// progress/review_shared_kernel.md defect D4: Quantity.From(1e18) used
    /// to leak System.OverflowException — a framework exception with no
    /// stable Code — from `checked((int)value)`, instead of the domain
    /// error every other refusal on this type raises. 1e18 is integral,
    /// positive and finite, so it passes every other guard in From and
    /// previously reached the unchecked cast.
    /// </summary>
    [Fact]
    public void Quantity_FromAnOutOfRangeButOtherwiseWellFormedDoubleRaisesADomainErrorNotAnOverflowException()
    {
        var error = Assert.Throws<QuantityMustBePositiveError>(() => Quantity.From(1e18));

        Assert.Equal("quantity.must_be_strictly_positive_integer", error.Code);
        Assert.IsAssignableFrom<DomainError>(error);
    }
}
