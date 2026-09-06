using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI13` — asserts PROVENANCE, not shape: the id source and the clock are
/// injected delegates returning KNOWN values, and the stamped fields are
/// asserted EQUAL to those known values. `Assert.NotEqual` proves
/// non-collision and can never prove provenance (feature 18's defect,
/// `CLAUDE.md`).
/// </summary>
public sealed class InvoiceFactTests
{
    [Fact]
    public void BI13_StampsInvoiceIssuedWithTheInvoiceAsAggregateIdTheOrderAsCorrelationIdAndTheRequestAsCausationId_ReadFromTheInjectedIdSourceAndClock()
    {
        var knownInstant = new DateTimeOffset(2026, 3, 14, 9, 26, 0, TimeSpan.Zero);
        var knownCausationId = UniqueId.New();
        var knownCorrelationId = UniqueId.New();

        var invoiceId = UniqueId.New();
        var lineId = UniqueId.New();
        var eventId = UniqueId.New();
        var idQueue = new Queue<UniqueId>([lineId, eventId]);
        UniqueId NewId() => idQueue.Count > 0 ? idQueue.Dequeue() : UniqueId.New();

        var input = new IssueInvoiceInput(
            invoiceId,
            "INV-000001",
            OrderNumber.Parse("ORD-000001"),
            "CarrefourEs",
            "IBERFOODS",
            [new InvoiceLineInput("SKU-1", new Quantity(1), new Money(1_000, "EUR"))],
            Money.Zero("EUR"),
            knownCorrelationId);

        var ctx = new InvoiceContext(knownInstant, knownCausationId);

        var invoice = Invoice.Issue(input, ctx, NewId);
        var fact = Assert.IsType<InvoiceIssued>(Assert.Single(invoice.DomainEvents));

        Assert.Equal(invoiceId, fact.AggregateId);
        Assert.Equal(knownCorrelationId, fact.CorrelationId);
        Assert.Equal(knownCausationId, fact.CausationId);
        Assert.Equal(knownInstant, fact.OccurredAt);

        // Same instant for the invoice's own date and the fact's occurredAt.
        Assert.Equal(invoice.InvoiceDate, fact.OccurredAt);
        Assert.Equal(knownInstant, invoice.InvoiceDate);
    }
}
