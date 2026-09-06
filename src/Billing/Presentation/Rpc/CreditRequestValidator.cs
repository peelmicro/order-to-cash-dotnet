using System.Text.RegularExpressions;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal — mirrors <c>InvalidStockRequestError</c>/
/// <c>InvalidOrdersCreateRequestError</c>: the request does not even
/// satisfy <c>asyncapi.yaml</c>'s schema. Mapped to <c>VALIDATION_FAILED</c>
/// by <see cref="CreditErrorMapper"/>.
/// </summary>
public sealed class InvalidCreditRequestError(string message) : Exception(message);

/// <summary>
/// Hand-rolled validation, the shape of <c>StockRequestValidator</c>
/// (design.md §4.4) — no <c>class-validator</c> equivalent is added. Also
/// the place §4.4's `^ORD-[0-9]{6,}$`/`^[A-Z]{3}$` alphabets close the
/// collation and currency-comparison residuals at the edge (§15 ledger
/// <c>L3</c>, <c>L13</c>, <c>L14</c>).
/// </summary>
public static partial class CreditRequestValidator
{
    private const int PartyCodeMaxLength = 20;

    public static void ValidateHold(CreditHoldRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);
        ValidatePartyCode(request.RetailerCode, "retailerCode", errors);
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);

        if (request.Amount is null)
        {
            errors.Add("amount is required.");
        }
        else
        {
            ValidateAmount(request.Amount.Amount, errors);
            ValidateCurrencyCode(request.Amount.Currency, errors);
        }

        ThrowIfAny(errors, "billing.credit.hold");
    }

    public static void ValidateRelease(CreditReleaseRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);
        ValidatePartyCode(request.RetailerCode, "retailerCode", errors);
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);

        ThrowIfAny(errors, "billing.credit.release");
    }

    public static void ValidateList(CreditListRequestPayload request)
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

        if (request.RetailerCode is { } retailerCode)
        {
            ValidatePartyCode(retailerCode, "retailerCode", errors);
        }

        if (request.CompanyCode is { } companyCode)
        {
            ValidatePartyCode(companyCode, "companyCode", errors);
        }

        ThrowIfAny(errors, "billing.credit.list");
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

        ValidateAsciiAlphabet(value, field, errors);
    }

    /// <summary>A code containing a non-ASCII character could be resolved to the SAME row by an accent-insensitive collation while sorting differently under an ordinal comparison — the residual `FS19`/ledger `L3` closed for product codes, closed here for retailer/company codes.</summary>
    private static void ValidateAsciiAlphabet(string value, string field, List<string> errors)
    {
        if (!AsciiPattern().IsMatch(value))
        {
            errors.Add($"{field} '{value}' must contain only printable ASCII characters.");
        }
    }

    private static void ValidateAmount(long amount, List<string> errors)
    {
        if (amount < 0)
        {
            errors.Add("amount.amount must be a non-negative integer.");
        }
    }

    private static void ValidateCurrencyCode(string? value, List<string> errors)
    {
        if (value is null || !CurrencyCodePattern().IsMatch(value))
        {
            errors.Add($"amount.currency '{value ?? "<null>"}' must match ^[A-Z]{{3}}$.");
        }
    }

    private static void ThrowIfAny(List<string> errors, string subject)
    {
        if (errors.Count > 0)
        {
            throw new InvalidCreditRequestError($"{subject} request failed validation: {string.Join(" ", errors)}");
        }
    }

    [GeneratedRegex(@"^ORD-[0-9]{6,}$")]
    private static partial Regex OrderReferencePattern();

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();

    [GeneratedRegex(@"^[\x20-\x7E]+$")]
    private static partial Regex AsciiPattern();
}
