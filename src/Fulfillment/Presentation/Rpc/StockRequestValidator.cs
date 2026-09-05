using System.Text.RegularExpressions;
using OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Fulfillment.Presentation.Rpc;

/// <summary>
/// A wire-shape refusal — mirrors Orders' <c>InvalidOrdersCreateRequestError</c>:
/// the request does not even satisfy <c>asyncapi.yaml</c>'s schema. Mapped to
/// <c>VALIDATION_FAILED</c> by <see cref="StockErrorMapper"/>.
/// </summary>
public sealed class InvalidStockRequestError(string message) : Exception(message);

/// <summary>
/// Hand-rolled validation, the shape of <c>OrdersCreateRequestValidator</c>
/// (design.md §6.4, ledger L11) — no <c>class-validator</c> equivalent is
/// added. Also the place §4.3's accent-collation residual is closed: every
/// business code is required to be printable ASCII, so two callers spelling
/// a code with an accent the CI collation folds differently can never derive
/// two different lock orders for what the database treats as the same row.
/// </summary>
public static partial class StockRequestValidator
{
    private const int PartyCodeMaxLength = 20;
    private const int ProductCodeMaxLength = 30;

    private static readonly string[] _validReleaseReasons = ["credit_rejected", "order_cancelled"];

    public static void ValidateCheck(StockCheckRequestPayload request)
    {
        var errors = new List<string>();
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            errors.Add("lines must contain at least one item.");
        }
        else
        {
            for (var i = 0; i < request.Lines.Count; i++)
            {
                ValidateProductCode(request.Lines[i].ProductCode, $"lines[{i}].productCode", errors);
                ValidatePositive(request.Lines[i].Quantity, $"lines[{i}].quantity", errors);
            }
        }

        ThrowIfAny(errors, "fulfillment.stock.check");
    }

    public static void ValidateReserve(StockReserveRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);
        ValidatePartyCode(request.RetailerCode, "retailerCode", errors);
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            errors.Add("lines must contain at least one item.");
        }
        else
        {
            for (var i = 0; i < request.Lines.Count; i++)
            {
                ValidateProductCode(request.Lines[i].ProductCode, $"lines[{i}].productCode", errors);
                ValidatePositive(request.Lines[i].Units, $"lines[{i}].units", errors);
            }
        }

        ThrowIfAny(errors, "fulfillment.stock.reserve");
    }

    public static void ValidateRelease(StockReleaseRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);

        if (!_validReleaseReasons.Contains(request.Reason, StringComparer.Ordinal))
        {
            errors.Add($"reason '{request.Reason}' must be one of: {string.Join(", ", _validReleaseReasons)}.");
        }

        ThrowIfAny(errors, "fulfillment.stock.release");
    }

    public static void ValidateList(StockListRequestPayload request)
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

        if (request.CompanyCode is { } companyCode)
        {
            ValidatePartyCode(companyCode, "companyCode", errors);
        }

        if (request.ProductCode is { } productCode)
        {
            ValidateProductCode(productCode, "productCode", errors);
        }

        ThrowIfAny(errors, "fulfillment.stock.list");
    }

    public static void ValidateReplenish(StockReplenishRequestPayload request)
    {
        var errors = new List<string>();
        ValidatePartyCode(request.CompanyCode, "companyCode", errors);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            errors.Add("lines must contain at least one item.");
        }
        else
        {
            for (var i = 0; i < request.Lines.Count; i++)
            {
                ValidateProductCode(request.Lines[i].ProductCode, $"lines[{i}].productCode", errors);
                ValidatePositive(request.Lines[i].Units, $"lines[{i}].units", errors);
            }
        }

        ThrowIfAny(errors, "fulfillment.stock.replenish");
    }

    public static void ValidateDespatchCreate(DespatchCreateRequestPayload request)
    {
        var errors = new List<string>();
        ValidateOrderReference(request.OrderReference, errors);

        ThrowIfAny(errors, "fulfillment.despatch.create");
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

    private static void ValidateProductCode(string? value, string field, List<string> errors)
    {
        if (string.IsNullOrEmpty(value) || value.Length > ProductCodeMaxLength)
        {
            errors.Add($"{field} must be 1-{ProductCodeMaxLength} characters.");
            return;
        }

        ValidateAsciiAlphabet(value, field, errors);
    }

    /// <summary>
    /// design.md §4.3's residual, closed here: a code containing a non-ASCII
    /// character could be resolved to the SAME row by an accent-insensitive
    /// collation while sorting differently under an ordinal comparison,
    /// letting two callers derive different lock orders for the same rows.
    /// </summary>
    private static void ValidateAsciiAlphabet(string value, string field, List<string> errors)
    {
        if (!AsciiPattern().IsMatch(value))
        {
            errors.Add($"{field} '{value}' must contain only printable ASCII characters.");
        }
    }

    private static void ValidatePositive(int value, string field, List<string> errors)
    {
        if (value <= 0)
        {
            errors.Add($"{field} must be a strictly positive integer.");
        }
    }

    private static void ThrowIfAny(List<string> errors, string subject)
    {
        if (errors.Count > 0)
        {
            throw new InvalidStockRequestError($"{subject} request failed validation: {string.Join(" ", errors)}");
        }
    }

    [GeneratedRegex(@"^ORD-[0-9]{6,}$")]
    private static partial Regex OrderReferencePattern();

    [GeneratedRegex(@"^[\x20-\x7E]+$")]
    private static partial Regex AsciiPattern();
}
