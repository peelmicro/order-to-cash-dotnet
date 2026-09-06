using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application;

/// <summary>
/// The `billing.payment.register` transactional unit (design.md §14's own
/// anticipation, `InvoiceIssueService`'s exact shape) — feature 22, the sole
/// LIVE caller of <see cref="Invoice.MarkPaid"/> and <c>BuyerCredit.Release</c>,
/// both delivered uncalled by features 21/19.
/// </summary>
/// <remarks>
/// <para>
/// <b>R48 fast path</b> — a hit on <see cref="IInvoiceRepository.FindPaymentByReferenceAsync"/>
/// answers immediately, NO transaction opened. Reviewer finding N11 in #7's
/// counterpart (a sequential cross-invoice reuse of the same
/// <c>paymentReference</c> returned a success-shaped <c>duplicate</c>
/// naming a DIFFERENT invoice than the one the caller asked about, while
/// the concurrent form of the identical condition correctly answered
/// <c>CONFLICT</c>) is inherited as PREVENTION here, not rediscovered:
/// <see cref="IdentityMatches"/> compares whichever identifier the caller
/// supplied against the resolved invoice BEFORE returning a duplicate reply.
/// </para>
/// <para>
/// <b>R47's ordering is structural, not asserted</b> — exactly #7's own
/// property, ported deliberately (`CLAUDE.md`'s ordering note). Inside the
/// ONE transaction: the credit line is locked first (`BI8`, extended to
/// this subject), the target invoice's own row locked second, an authority
/// re-read closes the race the fast path leaves open, <c>Invoice.MarkPaid</c>
/// raises <c>payment.received.v1</c> and refuses all three of R49's cases
/// itself, <c>BuyerCredit.Release</c> raises <c>credit.released.v1</c> —
/// then <see cref="IInvoiceRepository.MarkPaidAsync"/> is AWAITED before
/// <see cref="IBuyerCreditRepository.SaveChangesAsync"/>, so
/// <c>payment.received.v1</c>'s outbox row is inserted (and assigned its
/// `seq`) strictly before <c>credit.released.v1</c>'s — the SAME
/// one-awaited-INSERT-at-a-time / IDENTITY-column mechanism
/// <c>InvoiceIssueService</c>'s own design.md ledger (`L16`) already
/// establishes, exercised here for the first time by a transaction that
/// writes TWO facts.
/// </para>
/// </remarks>
public sealed class PaymentRegisterService(
    IUnitOfWork unitOfWork,
    IBuyerCreditRepository credits,
    IInvoiceRepository invoices,
    IClock clock)
{
    public async Task<PaymentRegisterReplyPayload> RegisterAsync(RegisterPaymentCommand command, CancellationToken cancellationToken)
    {
        // R48 fast path: a hit answers duplicate immediately, NO
        // transaction opened.
        var existingPayment = await invoices.FindPaymentByReferenceAsync(command.PaymentReference, cancellationToken).ConfigureAwait(false);
        if (existingPayment is not null)
        {
            var paidInvoice = await invoices.FindByIdAsync(existingPayment.InvoiceId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"payment '{command.PaymentReference}' references invoice id '{existingPayment.InvoiceId}', which no longer exists.");

            if (!IdentityMatches(command, paidInvoice))
            {
                // N11's fix, inherited as prevention: the SAME
                // paymentReference already recorded against a DIFFERENT
                // invoice than the one THIS request names is a conflict,
                // never a success-shaped duplicate.
                throw new PaymentReferenceConflictError(command.PaymentReference);
            }

            return BuildDuplicateReply(paidInvoice, existingPayment);
        }

        // Identity resolved BEFORE any transaction opens — BI8's credit
        // lock needs (retailerCode, companyCode, orderReference), and those
        // live on the invoice row, not on the request.
        var target = await ResolveInvoiceAsync(command, cancellationToken).ConfigureAwait(false)
            ?? throw new InvoiceNotFoundError(command.InvoiceId, command.InvoiceReference);

        return await unitOfWork.ExecuteAsync(
            async ct =>
            {
                // BI8: ALWAYS the first lock, extended to this subject.
                var credit = await credits.LockForOrderAsync(target.RetailerCode, target.CompanyCode, target.OrderReference, ct).ConfigureAwait(false);
                if (credit is null)
                {
                    throw new CreditLineNotFoundError(target.RetailerCode, target.CompanyCode);
                }

                // BI8: the B8 authority, second lock.
                var lockedInvoice = await invoices.LockByIdAsync(target.Id, ct).ConfigureAwait(false)
                    ?? throw new InvoiceNotFoundError(command.InvoiceId, command.InvoiceReference);

                // The authority re-read: does THIS invoice already carry
                // a payment? Closes the race the fast path leaves open —
                // two requests for the SAME invoice, same reference,
                // arriving concurrently.
                var authorityPayment = await invoices.FindPaymentByInvoiceIdAsync(lockedInvoice.Id, ct).ConfigureAwait(false);
                if (authorityPayment is not null && string.Equals(authorityPayment.PaymentReference, command.PaymentReference, StringComparison.Ordinal))
                {
                    return BuildDuplicateReply(lockedInvoice, authorityPayment);
                }

                var invoice = Invoice.Reconstitute(lockedInvoice);

                // clock.UtcNow read EXACTLY ONCE — the fact's occurredAt
                // and the release ledger entry's date agree exactly
                // (mirrors `BI13`).
                var ctx = new InvoiceContext(clock.UtcNow, command.RequestId);
                var creditCtx = new CreditContext(ctx.OccurredAt, command.RequestId);

                var markPaidInput = new MarkPaidInput(
                    command.PaymentReference,
                    new Money(command.Amount, command.Currency),
                    command.ValueDate,
                    command.Source,
                    command.CorrelationId);

                // Raises payment.received.v1. Refuses B8 (already paid),
                // B10 (amount/currency mismatch) itself — R49's three
                // cases, all inside the domain, all before either
                // repository is touched.
                invoice.MarkPaid(markPaidInput, ctx, UniqueId.New);

                // Raises credit.released.v1 for the order's outstanding
                // exposure, or returns null (no fact, no write) if none
                // remains — BC11/B5, unchanged from CreditReleaseService's
                // own precedent.
                credit.Release(target.OrderReference, CreditReleaseReason.InvoicePaid, command.CorrelationId, creditCtx, UniqueId.New);

                // R47's ordering — structural, not asserted: invoices
                // persisted BEFORE credits, so payment.received.v1's
                // outbox row is inserted (and assigned its seq) strictly
                // before credit.released.v1's.
                await invoices.MarkPaidAsync(invoice, markPaidInput, ct).ConfigureAwait(false);
                await credits.SaveChangesAsync(credit, ct).ConfigureAwait(false);

                return BuildAcceptedReply(invoice, command.PaymentReference, target.OrderReference);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>N11's guard: compares whichever of <see cref="RegisterPaymentCommand.InvoiceId"/>/<see cref="RegisterPaymentCommand.InvoiceReference"/> the caller actually supplied against the invoice a `paymentReference` lookup resolved. Returns <see langword="true"/> when the caller supplied neither (never reachable in production — the validator requires at least one — but kept permissive rather than throwing on a state the validator already refuses).</summary>
    private static bool IdentityMatches(RegisterPaymentCommand command, InvoiceSnapshot invoice)
    {
        if (command.InvoiceId is { } invoiceId && invoiceId != invoice.Id.Value)
        {
            return false;
        }

        if (command.InvoiceReference is { } invoiceReference && !string.Equals(invoiceReference, invoice.InvoiceReference, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private Task<InvoiceSnapshot?> ResolveInvoiceAsync(RegisterPaymentCommand command, CancellationToken cancellationToken) =>
        command.InvoiceId is { } invoiceId
            ? invoices.FindByIdAsync(UniqueId.From(invoiceId), cancellationToken)
            : invoices.FindByInvoiceReferenceAsync(command.InvoiceReference!, cancellationToken);

    private static PaymentRegisterReplyPayload BuildDuplicateReply(InvoiceSnapshot invoice, PaymentSnapshot payment) => new(
        "duplicate",
        payment.PaymentReference,
        invoice.InvoiceReference,
        invoice.OrderReference.Value,
        InvoiceStatuses.ToToken(invoice.State),
        invoice.State.PaidAtOrNull);

    private static PaymentRegisterReplyPayload BuildAcceptedReply(Invoice invoice, string paymentReference, OrderNumber orderReference) => new(
        "accepted",
        paymentReference,
        invoice.InvoiceReference,
        orderReference.Value,
        invoice.Status,
        invoice.PaidAt);
}
