using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `BI5` unit half, `BI8`'s three-lock order, the fast path opening no
/// transaction, reply-only-after-commit, and rollback ⇒ no reply
/// (design.md §7.1/§7.2).
/// </summary>
public sealed class InvoiceIssueServiceTests
{
    private static BuyerCredit ReconstituteLine(long limit, IReadOnlyList<CreditLedgerEntrySnapshot>? entries = null, string currency = "EUR") =>
        BuyerCredit.Reconstitute(new BuyerCreditSnapshot(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(limit, currency), 0, entries ?? []));

    private static IssueInvoiceCommand Command(string orderReference = "ORD-000001", string currency = "EUR") =>
        new(orderReference, "CarrefourEs", "IBERFOODS", currency,
            [new Contracts.Facts.InvoiceLine("SKU-1", 2, 1_000)],
            null,
            UniqueId.New(),
            UniqueId.New());

    [Fact]
    public async Task BI5_RaisesNoActiveHoldWithoutTouchingTheInvoiceRepository_WhenTheOrderHoldsNoActiveHold()
    {
        var credits = new RecordingBuyerCreditRepository { LockResult = ReconstituteLine(10_000) }; // no hold entries at all
        var invoices = new RecordingInvoiceRepository();
        var allocator = new RecordingInvoiceNumberAllocator();
        var service = new InvoiceIssueService(new FakeUnitOfWork(), credits, invoices, allocator, new FakeClock());

        await Assert.ThrowsAsync<NoActiveHoldError>(() => service.IssueAsync(Command(), CancellationToken.None));

        // Zero save calls on BOTH repositories, not just the invoice one (#7's N3).
        Assert.Equal(0, credits.SaveChangesCallCount);
        Assert.Equal(0, invoices.SaveCallCount);
        Assert.Equal(0, allocator.CallCount);
    }

    [Fact]
    public async Task OpensNoTransaction_WhenAnInvoiceAlreadyExists()
    {
        var unitOfWork = new FakeUnitOfWork();
        var invoices = new RecordingInvoiceRepository { FindResult = ExistingSnapshot() };
        var credits = new RecordingBuyerCreditRepository();
        var allocator = new RecordingInvoiceNumberAllocator();
        var service = new InvoiceIssueService(unitOfWork, credits, invoices, allocator, new FakeClock());

        var reply = await service.IssueAsync(Command(), CancellationToken.None);

        Assert.False(reply.Created);
        Assert.Equal(0, unitOfWork.ExecuteCount);
    }

    [Fact]
    public async Task BI8_TakesTheThreeLocksInExactlyTheOrderCreditsThenInvoicesThenTheInvoiceNumberSequence()
    {
        var callLog = new List<string>();
        var credits = new RecordingBuyerCreditRepository
        {
            LockResult = ReconstituteLine(10_000, [new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(2_000, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow)]),
            CallLog = callLog,
        };
        var invoices = new RecordingInvoiceRepository { CallLog = callLog };
        var allocator = new RecordingInvoiceNumberAllocator { CallLog = callLog };
        var service = new InvoiceIssueService(new FakeUnitOfWork(), credits, invoices, allocator, new FakeClock());

        var reply = await service.IssueAsync(Command(), CancellationToken.None);

        Assert.True(reply.Created);
        Assert.Equal(["credits.Lock", "invoices.Lock", "allocator.AllocateNext"], callLog);
    }

    [Fact]
    public async Task ReturnsTheReplyOnlyAfterExecuteAsyncResolves()
    {
        var credits = new RecordingBuyerCreditRepository
        {
            LockResult = ReconstituteLine(10_000, [new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(2_000, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow)]),
        };
        var invoices = new RecordingInvoiceRepository();
        var allocator = new RecordingInvoiceNumberAllocator();
        var executed = false;
        var unitOfWork = new FakeUnitOfWork { BeforeWork = () => { executed = true; return Task.CompletedTask; } };
        var service = new InvoiceIssueService(unitOfWork, credits, invoices, allocator, new FakeClock());

        var reply = await service.IssueAsync(Command(), CancellationToken.None);

        Assert.True(executed);
        Assert.True(reply.Created);
    }

    [Fact]
    public async Task PropagatesTheFailureAndReturnsNoReply_WhenTheTransactionRollsBack()
    {
        var credits = new RecordingBuyerCreditRepository
        {
            LockResult = ReconstituteLine(10_000, [new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(2_000, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow)]),
        };
        var invoices = new RecordingInvoiceRepository();
        var allocator = new RecordingInvoiceNumberAllocator();
        var service = new InvoiceIssueService(new ThrowingAfterWorkUnitOfWork(), credits, invoices, allocator, new FakeClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync(Command(), CancellationToken.None));
    }

    private static InvoiceSnapshot ExistingSnapshot() => new(
        UniqueId.New(),
        "INV-000001",
        DateTimeOffset.UtcNow,
        OrderNumber.Parse("ORD-000001"),
        "CarrefourEs",
        "IBERFOODS",
        "EUR",
        new Money(2_000, "EUR"),
        Money.Zero("EUR"),
        new Money(2_000, "EUR"),
        new InvoiceState.Issued(),
        [new InvoiceLineSnapshot(UniqueId.New(), "SKU-1", new Quantity(2), new Money(1_000, "EUR"))]);

    private sealed class RecordingBuyerCreditRepository : IBuyerCreditRepository
    {
        public BuyerCredit? LockResult { get; set; }

        public List<string>? CallLog { get; set; }

        public int SaveChangesCallCount { get; private set; }

        public Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken cancellationToken)
        {
            CallLog?.Add("credits.Lock");
            return Task.FromResult(LockResult);
        }

        public Task SaveChangesAsync(BuyerCredit credit, CancellationToken cancellationToken)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInvoiceRepository : IInvoiceRepository
    {
        public InvoiceSnapshot? FindResult { get; set; }

        public InvoiceSnapshot? LockResult { get; set; }

        public List<string>? CallLog { get; set; }

        public int SaveCallCount { get; private set; }

        public Task<InvoiceSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken) =>
            Task.FromResult(FindResult);

        public Task<InvoiceSnapshot?> LockByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.Lock");
            return Task.FromResult(LockResult);
        }

        public Task SaveAsync(Invoice invoice, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            return Task.CompletedTask;
        }

        // Feature 22 additions — unused by this file's own tests (they
        // exercise `billing.invoice.issue`, not `billing.payment.register`),
        // present only to keep this fake compiling against the extended
        // port, the SAME mechanical reason `InvoiceIssueHandlerSpec`'s
        // fake gained throwing stubs in #7's counterpart.
        public Task<InvoiceSnapshot?> FindByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");

        public Task<InvoiceSnapshot?> FindByInvoiceReferenceAsync(string invoiceReference, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");

        public Task<PaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");

        public Task<PaymentSnapshot?> FindPaymentByInvoiceIdAsync(UniqueId invoiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");

        public Task<InvoiceSnapshot?> LockByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");

        public Task MarkPaidAsync(Invoice invoice, MarkPaidInput payment, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by InvoiceIssueServiceTests.");
    }

    private sealed class RecordingInvoiceNumberAllocator : IInvoiceNumberAllocator
    {
        public List<string>? CallLog { get; set; }

        public int CallCount { get; private set; }

        public Task<string> AllocateNextAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            CallLog?.Add("allocator.AllocateNext");
            return Task.FromResult("INV-000006");
        }
    }
}
