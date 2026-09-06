using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application;

/// <summary>
/// The `invoice.issue` transactional unit as a plain class the command
/// handler delegates to (design.md §7.1/§7.2). Per `domain-model.md` §8 rule
/// 6 — "one transaction mutates exactly one aggregate instance plus its
/// outbox records" — this transaction DELIBERATELY mutates TWO: an
/// <see cref="Invoice"/> and a <see cref="Domain.BuyerCredit"/>. The
/// invariant that justifies it is one no single aggregate owns: an issued
/// invoice's hold is consumed. Splitting the two writes across separate
/// transactions leaves a crash window nothing can detect, because
/// <see cref="Domain.BuyerCredit.Consume"/> raises no event and there is no
/// <c>credit.consumed.v1</c> in the fact catalogue to repair it
/// (design.md §7.4, re-derived against this repository's own code — not
/// inherited on #7's citation alone).
/// </summary>
public sealed class InvoiceIssueService(
    IUnitOfWork unitOfWork,
    IBuyerCreditRepository credits,
    IInvoiceRepository invoices,
    IInvoiceNumberAllocator invoiceNumbers,
    IClock clock)
{
    public async Task<InvoiceIssueReplyPayload> IssueAsync(IssueInvoiceCommand command, CancellationToken cancellationToken)
    {
        var orderReference = OrderNumber.Parse(command.OrderReference);

        // BI9/B7 fast path: a hit returns created:false immediately, NO
        // transaction opened — the sweeper hits this on every retry.
        var existing = await invoices.FindByOrderReferenceAsync(orderReference, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return BuildReply(existing, created: false);
        }

        return await unitOfWork.ExecuteAsync(
            async ct =>
            {
                // BI8: ALWAYS the first lock.
                var credit = await credits.LockForOrderAsync(command.RetailerCode, command.CompanyCode, orderReference, ct).ConfigureAwait(false);
                if (credit is null)
                {
                    throw new CreditLineNotFoundError(command.RetailerCode, command.CompanyCode);
                }

                // BI8: the B7 authority, second lock.
                var lockedInvoice = await invoices.LockByOrderReferenceAsync(orderReference, ct).ConfigureAwait(false);
                if (lockedInvoice is not null)
                {
                    return BuildReply(lockedInvoice, created: false);
                }

                if (!string.Equals(command.Currency, credit.CreditLimit.Currency, StringComparison.Ordinal))
                {
                    throw new InvoiceCurrencyMismatchError(credit.CreditLimit.Currency, command.Currency);
                }

                var activeHold = ActiveHoldOf(credit, orderReference);
                if (activeHold <= 0)
                {
                    throw new NoActiveHoldError(orderReference.Value);
                }

                // BI8: the LAST lock — the global hot spot, held as briefly as possible.
                var invoiceReference = await invoiceNumbers.AllocateNextAsync(ct).ConfigureAwait(false);

                // clock.UtcNow read EXACTLY ONCE — the invoice date, the
                // fact's occurredAt and the ledger entry's date all agree
                // exactly (BI13).
                var ctx = new InvoiceContext(clock.UtcNow, command.RequestId);
                var creditCtx = new CreditContext(ctx.OccurredAt, command.RequestId);

                var input = new IssueInvoiceInput(
                    UniqueId.New(),
                    invoiceReference,
                    orderReference,
                    command.RetailerCode,
                    command.CompanyCode,
                    [.. command.Lines.Select(l => new InvoiceLineInput(l.ProductCode, new Quantity(l.Units), new Money(l.UnitPrice, command.Currency)))],
                    new Money(command.Discount ?? 0, command.Currency),
                    command.CorrelationId);

                var invoice = Invoice.Issue(input, ctx, UniqueId.New);

                // R40: appends ONE consume entry, raises NOTHING.
                credit.Consume(orderReference, creditCtx, UniqueId.New);

                await invoices.SaveAsync(invoice, ct).ConfigureAwait(false);
                await credits.SaveChangesAsync(credit, ct).ConfigureAwait(false);

                return BuildReplyFromInvoice(invoice, created: true);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static long ActiveHoldOf(Domain.BuyerCredit credit, OrderNumber orderReference) =>
        credit.Summary.ByOrder
            .FirstOrDefault(o => string.Equals(o.OrderReference, orderReference.Value, StringComparison.OrdinalIgnoreCase))
            ?.ActiveHold ?? 0;

    private static InvoiceIssueReplyPayload BuildReply(InvoiceSnapshot snapshot, bool created) => new(
        snapshot.OrderReference.Value,
        snapshot.InvoiceReference,
        snapshot.InvoiceDate,
        snapshot.Currency,
        snapshot.TotalAmount.MinorUnits,
        InvoiceStatuses.ToToken(snapshot.State),
        created);

    private static InvoiceIssueReplyPayload BuildReplyFromInvoice(Invoice invoice, bool created) => new(
        invoice.OrderReference.Value,
        invoice.InvoiceReference,
        invoice.InvoiceDate,
        invoice.Currency,
        invoice.TotalAmount.MinorUnits,
        invoice.Status,
        created);
}
