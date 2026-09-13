using System.Text.RegularExpressions;
using OrderToCash.Contracts.Rpc;

namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// Hand-rolled validation for `billing.payment.register`, the shape of
/// <see cref="InvoiceRequestValidator"/>/<see cref="CreditRequestValidator"/>.
/// </summary>
/// <remarks>
/// The cross-field check — AT LEAST ONE of <c>invoiceId</c>/<c>invoiceReference</c>
/// — lives HERE, not in the domain and not left to
/// <c>PaymentRegisterService</c> to discover by both being <see langword="null"/>:
/// neither is in `asyncapi.yaml`'s own `required:` list for
/// <c>PaymentRegisterRequestPayload</c>, so the schema alone permits a
/// request naming NEITHER, and refusing it here — before any lock, before
/// any transaction — is <see cref="InvoiceRequestValidator"/>'s own
/// `discount` cross-field placement, applied to a NEW cross-field rule
/// this subject introduces.
/// </remarks>
public static partial class PaymentRegisterRequestValidator
{
    public static void ValidateRegister(PaymentRegisterRequestPayload request)
    {
        var errors = new List<string>();

        if (string.IsNullOrEmpty(request.PaymentReference) || request.PaymentReference.Length > 30)
        {
            errors.Add("paymentReference must be 1-30 characters.");
        }

        var hasInvoiceId = request.InvoiceId is { } id && id != Guid.Empty;
        var hasInvoiceReference = !string.IsNullOrEmpty(request.InvoiceReference);

        if (!hasInvoiceId && !hasInvoiceReference)
        {
            errors.Add("at least one of invoiceId or invoiceReference must be supplied.");
        }

        if (request.InvoiceReference is { } invoiceReference && invoiceReference.Length > 0 && !InvoiceReferencePattern().IsMatch(invoiceReference))
        {
            errors.Add($"invoiceReference '{invoiceReference}' must match ^INV-[0-9]{{6,}}$.");
        }

        if (request.Amount is null)
        {
            errors.Add("amount must be supplied.");
        }
        else
        {
            if (request.Amount.Amount < 0)
            {
                errors.Add($"amount.amount must be a non-negative integer; got {request.Amount.Amount}.");
            }

            if (request.Amount.Currency is null || !CurrencyCodePattern().IsMatch(request.Amount.Currency))
            {
                errors.Add($"amount.currency '{request.Amount.Currency}' must match ^[A-Z]{{3}}$.");
            }
        }

        if (request.Source is not ("operator" or "robot" or "test"))
        {
            errors.Add($"source '{request.Source}' must be one of: operator, robot, test.");
        }

        if (errors.Count > 0)
        {
            throw new InvalidInvoiceRequestError($"billing.payment.register request failed validation: {string.Join(" ", errors)}");
        }
    }

    [GeneratedRegex(@"^INV-[0-9]{6,}$")]
    private static partial Regex InvoiceReferencePattern();

    [GeneratedRegex(@"^[A-Z]{3}$")]
    private static partial Regex CurrencyCodePattern();
}
