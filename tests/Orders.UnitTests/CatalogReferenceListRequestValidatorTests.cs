using OrderToCash.Orders.Presentation.Rpc;
using Xunit;

namespace OrderToCash.Orders.UnitTests;

/// <summary>
/// <c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds</c>: an
/// optional array, and when present at least one item (<c>minItems: 1</c>),
/// each drawn from the closed four-value enum. The same "run BEFORE the
/// request reaches the query" discipline as <c>OrdersCreateRequestValidator</c>
/// (review A2).
/// </summary>
public sealed class CatalogReferenceListRequestValidatorTests
{
    [Fact]
    public void Validate_KindsOmitted_ThrowsNothing()
    {
        var exception = Record.Exception(() => CatalogReferenceListRequestValidator.Validate(new CatalogReferenceListRequestPayload(Kinds: null, IncludeDisabled: null)));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_KindsCarriesEveryDeclaredValue_ThrowsNothing()
    {
        var request = new CatalogReferenceListRequestPayload(["products", "retailers", "companies", "currencies"], IncludeDisabled: true);

        var exception = Record.Exception(() => CatalogReferenceListRequestValidator.Validate(request));
        Assert.Null(exception);
    }

    [Fact]
    public void Validate_KindsIsAnEmptyArray_Refuses()
    {
        var request = new CatalogReferenceListRequestPayload(Kinds: [], IncludeDisabled: null);

        var error = Assert.Throws<InvalidCatalogReferenceListRequestError>(() => CatalogReferenceListRequestValidator.Validate(request));
        Assert.Contains("kinds", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_KindsCarriesAValueOutsideTheDeclaredEnum_Refuses()
    {
        var request = new CatalogReferenceListRequestPayload(["products", "widgets"], IncludeDisabled: null);

        var error = Assert.Throws<InvalidCatalogReferenceListRequestError>(() => CatalogReferenceListRequestValidator.Validate(request));
        Assert.Contains("widgets", error.Message, StringComparison.Ordinal);
    }
}
