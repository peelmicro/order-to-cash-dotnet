namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal — the request does not even satisfy
/// <c>asyncapi.yaml</c>'s <c>OrdersCreateRequestPayload</c> schema (a
/// required field missing or empty). Distinct from every
/// <c>PlaceOrderError</c>/<c>DomainError</c>: those refuse a well-formed
/// request that violates a business rule; this refuses a request that is
/// not even well-formed enough to become a command. Mapped to
/// <c>VALIDATION_FAILED</c> by <see cref="OrdersCreateErrorMapper"/>, the
/// same code a business refusal collapses to — a caller sees one client-
/// caused-refusal code either way, and <c>message</c> says which.
/// </summary>
public sealed class InvalidOrdersCreateRequestError(string message) : Exception(message);

/// <summary>
/// review A2: <c>OrdersCreateResponder</c> deserialised the wire payload and
/// handed it straight to <c>PlaceOrderCommand</c> with no check that the
/// required fields <c>asyncapi.yaml</c>'s <c>OrdersCreateRequestPayload</c>
/// names (<c>retailerCode</c>, <c>companyCode</c>, <c>currency</c>,
/// <c>lines</c> with <c>minItems: 1</c>) were actually present — a request
/// omitting <c>lines</c> deserialised it to <see langword="null"/> and threw
/// <see cref="NullReferenceException"/> inside <c>ToCommand</c>, which
/// <c>OrdersCreateErrorMapper</c>'s catch-all turns into
/// <c>INTERNAL_ERROR</c>: a client-caused refusal disguised as a server
/// fault. CLAUDE.md puts "DTOs, validation" in <c>Presentation/</c> — this
/// is that validation, run BEFORE <c>ToCommand</c> so a malformed request
/// never reaches it.
/// </summary>
public static class OrdersCreateRequestValidator
{
    public static void Validate(OrdersCreateRequestPayload request)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(request.RetailerCode))
        {
            missing.Add("retailerCode");
        }

        if (string.IsNullOrWhiteSpace(request.CompanyCode))
        {
            missing.Add("companyCode");
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            missing.Add("currency");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            missing.Add("lines");
        }
        else
        {
            for (var index = 0; index < request.Lines.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(request.Lines[index].ProductCode))
                {
                    missing.Add($"lines[{index}].productCode");
                }
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOrdersCreateRequestError(
                $"orders.create request is missing or has an empty required field: {string.Join(", ", missing)}.");
        }
    }
}
