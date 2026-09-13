using System.Text.RegularExpressions;
using OrderToCash.Contracts.Rpc;

namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal — mirrors <see cref="InvalidCreditRequestError"/>:
/// the request does not even satisfy `asyncapi.yaml`'s schema. Mapped to
/// `VALIDATION_FAILED` by <see cref="BillingErrorMapper"/>.
/// </summary>
public sealed class InvalidInvoiceRequestError(string message) : Exception(message);

/// <summary>
/// Hand-rolled validation, the shape of <see cref="CreditRequestValidator"/>
/// (design.md §4.4) — no `class-validator` equivalent is added.
/// </summary>
/// <remarks>
/// The cross-field check (`discount ≤ Σ(unitPrice × units)`) lives HERE,
/// not only in the domain, and runs BEFORE the responder dispatches the
/// command — #7 was rejected for putting this only in the domain, where the
/// refusal happened after opening the transaction and reaching the
/// invoice-number sequence, the service's hottest row (`BI2`'s placement
/// clause, `design.md` §4.4). The sum is computed inside an explicit
/// `checked` region: C# arithmetic is unchecked by default
/// (`CheckForOverflowUnderflow` is not set), so two lines near
/// <see cref="long.MaxValue"/> would otherwise wrap to a small or negative
/// sum and the cross-field check would then wrongly ACCEPT a payload the
/// domain refuses inside the transaction — reintroducing exactly the defect
/// this check exists to prevent, one level down (`BI25`).
/// </remarks>
public static partial class InvoiceRequestValidator
{
    private const int PartyCodeMaxLength = 20;

    public static void ValidateIssue(InvoiceIssueRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);
        ValidatePartyCode(request.RetailerCode, "retailerCode", errors);
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);
        ValidateCurrencyCode(request.Currency, errors);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            errors.Add("lines must be a non-empty array.");
        }
        else
        {
            foreach (var line in request.Lines)
            {
                if (string.IsNullOrEmpty(line.ProductCode))
                {
                    errors.Add("lines[].productCode must not be empty.");
                }

                if (line.Units < 1)
                {
                    errors.Add($"lines[].units must be >= 1; got {line.Units}.");
                }

                if (line.UnitPrice < 0)
                {
                    errors.Add($"lines[].unitPrice must be a non-negative integer; got {line.UnitPrice}.");
                }
            }
        }

        var discount = request.Discount ?? 0;
        if (discount < 0)
        {
            errors.Add("discount must be a non-negative integer.");
        }

        // The cross-field check — checked, deliberately (BI25).
        if (request.Lines is { Count: > 0 })
        {
            try
            {
                var sum = 0L;
                checked
                {
                    foreach (var line in request.Lines)
                    {
                        sum += (long)line.UnitPrice * line.Units;
                    }
                }

                if (discount > sum)
                {
                    errors.Add($"discount ({discount}) must not exceed the sum of unitPrice x units ({sum}).");
                }
            }
            catch (OverflowException)
            {
                errors.Add("discount cross-field check overflowed while summing lines' unitPrice x units — the payload is refused rather than accepted on a wrapped sum.");
            }
        }

        ThrowIfAny(errors, "billing.invoice.issue");
    }

    public static void ValidateList(InvoiceListRequestPayload request)
    {
        var errors = new List<string>();

        if (request.Page is { } page && page < 1)
        {
            errors.Add("page must be >= 1.");
        }

        if (request.PageSize is { } pageSize && (pageSize < 1 || pageSize > 200))
        {
            errors.Add("pageSize must be between 1 and 200.");
        }

        if (request.Status is { } status && status is not ("issued" or "paid"))
        {
            errors.Add($"status '{status}' must be one of: issued, paid.");
        }

        if (request.RetailerCode is { } retailerCode)
        {
            ValidatePartyCode(retailerCode, "retailerCode", errors);
        }

        if (request.CompanyCode is { } companyCode)
        {
            ValidatePartyCode(companyCode, "companyCode", errors);
        }

        if (request.OrderReference is { } orderReference)
        {
            ValidateOrderReference(orderReference, errors);
        }

        if (request.IssuedBeforeMinutes is { } issuedBeforeMinutes && issuedBeforeMinutes < 0)
        {
            errors.Add("issuedBeforeMinutes must be >= 0.");
        }

        ThrowIfAny(errors, "billing.invoice.list");
    }

    private static void ValidateOrderReference(string? value, List<string> errors)
    {
        if (value is null || !OrderReferencePattern().IsMatch(value))
        {
            errors.Add($"orderReference '{value ?? "<null>"}' must match ^ORD-[0-9]{{6,}}$.");
        }
    }

    private static void ValidatePartyCode(string? value, string field, List<string> errors)
    {
        if (string.IsNullOrEmpty(value) || value.Length > PartyCodeMaxLength)
        {
            errors.Add($"{field} must be 1-{PartyCodeMaxLength} characters.");
            return;
        }

        if (!AsciiPattern().IsMatch(value))
        {
            errors.Add($"{field} '{value}' must contain only printable ASCII characters.");
        }
    }

    private static void ValidateCurrencyCode(string? value, List<string> errors)
    {
        if (value is null || !CurrencyCodePattern().IsMatch(value))
        {
            errors.Add($"currency '{value ?? "<null>"}' must match ^[A-Z]{{3}}$.");
        }
    }

    private static void ThrowIfAny(List<string> errors, string subject)
    {
        if (errors.Count > 0)
        {
            throw new InvalidInvoiceRequestError($"{subject} request failed validation: {string.Join(" ", errors)}");
        }
    }

    [GeneratedRegex(@"^ORD-[0-9]{6,}$")]
    private static partial Regex OrderReferencePattern();

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();

    [GeneratedRegex(@"^[\x20-\x7E]+$")]
    private static partial Regex AsciiPattern();
}
