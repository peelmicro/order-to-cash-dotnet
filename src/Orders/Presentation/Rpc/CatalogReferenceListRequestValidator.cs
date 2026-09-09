namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal for <c>catalog.reference.list</c> — mirrors
/// <c>InvalidOrdersCreateRequestError</c> in
/// <c>OrdersCreateRequestValidator.cs</c>: distinct from every domain/
/// application refusal, mapped to <c>VALIDATION_FAILED</c> by
/// <see cref="OrdersCreateErrorMapper"/>.
/// </summary>
public sealed class InvalidCatalogReferenceListRequestError(string message) : Exception(message);

/// <summary>
/// <c>asyncapi.yaml</c> <c>CatalogReferenceListRequestPayload.kinds</c>: an
/// array, when present, of at least one item (<c>minItems: 1</c>), each one
/// of the four closed enum values (<see cref="CatalogReferenceKinds"/>).
/// Run BEFORE the request reaches the query, the same discipline
/// <c>OrdersCreateRequestValidator</c> already follows (review A2).
/// </summary>
public static class CatalogReferenceListRequestValidator
{
    public static void Validate(CatalogReferenceListRequestPayload request)
    {
        if (request.Kinds is null)
        {
            return;
        }

        if (request.Kinds.Count == 0)
        {
            throw new InvalidCatalogReferenceListRequestError(
                "catalog.reference.list request's kinds, when present, must not be empty (asyncapi.yaml: minItems: 1).");
        }

        var unknown = request.Kinds.Where(kind => !CatalogReferenceKinds.All.Contains(kind, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidCatalogReferenceListRequestError(
                $"catalog.reference.list request's kinds carries a value outside the declared enum " +
                $"[{string.Join(", ", CatalogReferenceKinds.All)}]: {string.Join(", ", unknown)}.");
        }
    }
}
