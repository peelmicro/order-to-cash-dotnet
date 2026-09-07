using OrderToCash.Billing.Application;
using OrderToCash.Billing.Application.Commands;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Errors;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `R47` – `R49`, `BI8`'s lock order extended to this subject, the fast
/// path opening no transaction, and N11's fix (a `paymentReference`
/// resolved by the fast path must identify the SAME invoice the request
/// named, or the reply is a conflict, never a success-shaped duplicate) —
/// inherited as prevention from #7's review finding of the same name,
/// design.md §14's own anticipation.
/// </summary>
public sealed class PaymentRegisterServiceTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static RegisterPaymentCommand Command(
        Guid? invoiceId = null,
        string? invoiceReference = "INV-000001",
        string paymentReference = "PAY-000001",
        long amount = 5_000,
        string currency = "EUR",
        string source = "test") =>
        new(invoiceId, invoiceReference, paymentReference, amount, currency, _fixedNow, source, UniqueId.New(), UniqueId.New());

    private static InvoiceSnapshot IssuedSnapshot(UniqueId id, string invoiceReference, string orderReference, long totalAmount, string currency = "EUR") => new(
        id,
        invoiceReference,
        DateTimeOffset.UtcNow,
        OrderNumber.Parse(orderReference),
        "CarrefourEs",
        "IBERFOODS",
        currency,
        new Money(totalAmount, currency),
        Money.Zero(currency),
        new Money(totalAmount, currency),
        new InvoiceState.Issued(),
        [new InvoiceLineSnapshot(UniqueId.New(), "SKU-1", new Quantity(1), new Money(totalAmount, currency))]);

    private static InvoiceSnapshot PaidSnapshot(UniqueId id, string invoiceReference, string orderReference, long totalAmount, DateTimeOffset paidAt, string currency = "EUR") => new(
        id,
        invoiceReference,
        DateTimeOffset.UtcNow,
        OrderNumber.Parse(orderReference),
        "CarrefourEs",
        "IBERFOODS",
        currency,
        new Money(totalAmount, currency),
        Money.Zero(currency),
        new Money(totalAmount, currency),
        new InvoiceState.Paid(paidAt),
        [new InvoiceLineSnapshot(UniqueId.New(), "SKU-1", new Quantity(1), new Money(totalAmount, currency))]);

    private static BuyerCredit CreditWithHold(long limit, string orderReference, long holdAmount, string currency = "EUR") =>
        BuyerCredit.Reconstitute(new BuyerCreditSnapshot(
            UniqueId.New(),
            "CR-000001",
            "CarrefourEs",
            "IBERFOODS",
            new Money(limit, currency),
            holdAmount,
            [new CreditLedgerEntrySnapshot(UniqueId.New(), orderReference, new Money(holdAmount, currency), CreditEntryType.Hold, DateTimeOffset.UtcNow)]));

    [Fact]
    public async Task R47_LocksTheCreditLineBeforeTheInvoiceRow_CallsMarkPaidAndRelease_PersistsTheInvoiceBeforeTheCreditLine_AndRepliesAccepted()
    {
        var callLog = new List<string>();
        const string orderReference = "ORD-000001";
        const long total = 5_000;
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = IssuedSnapshot(invoiceId, "INV-000001", orderReference, total);

        var invoices = new RecordingInvoiceRepository
        {
            FindByInvoiceReferenceResult = invoiceSnapshot,
            LockByIdResult = invoiceSnapshot,
            CallLog = callLog,
        };
        var credits = new RecordingBuyerCreditRepository
        {
            LockResult = CreditWithHold(50_000, orderReference, total),
            CallLog = callLog,
        };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        var reply = await service.RegisterAsync(Command(amount: total), CancellationToken.None);

        Assert.Equal("accepted", reply.Outcome);
        Assert.Equal("paid", reply.InvoiceStatus);
        Assert.Equal(orderReference, reply.OrderReference);
        Assert.Equal("INV-000001", reply.InvoiceReference);
        Assert.NotNull(reply.PaidAt);

        // BI8 extended: the credit line is ALWAYS the first lock.
        var creditsLockIdx = callLog.IndexOf("credits.Lock");
        var invoicesLockIdx = callLog.IndexOf("invoices.LockById");
        Assert.True(creditsLockIdx >= 0 && invoicesLockIdx >= 0);
        Assert.True(creditsLockIdx < invoicesLockIdx, "BI8: the credit line must be locked BEFORE the invoice row.");

        // R47's ordering — structural: invoices.MarkPaid persisted BEFORE
        // credits.SaveChanges, so payment.received.v1's outbox row is
        // inserted (and assigned its seq) strictly before
        // credit.released.v1's.
        var markPaidIdx = callLog.IndexOf("invoices.MarkPaid");
        var creditsSaveIdx = callLog.IndexOf("credits.SaveChanges");
        Assert.True(markPaidIdx >= 0 && creditsSaveIdx >= 0);
        Assert.True(creditsSaveIdx > markPaidIdx, "R47: invoices.MarkPaid must run (and its outbox row insert) BEFORE credits.SaveChanges.");

        Assert.Equal(1, invoices.MarkPaidCallCount);
        Assert.Equal(1, credits.SaveChangesCallCount);

        // The fact-emission guard: MarkPaid must actually have raised
        // payment.received.v1, and Release must actually have raised
        // credit.released.v1 — not merely that the call counters moved.
        var paymentReceived = Assert.Single(invoices.MarkPaidInvoice!.DomainEvents);
        Assert.IsType<PaymentReceived>(paymentReceived);

        var creditReleased = Assert.Single(credits.Saved!.DomainEvents);
        Assert.IsType<CreditReleased>(creditReleased);
    }

    /// <summary>
    /// Backlog id 57 (ported from #7's `bf59af9`) — `credit.released.v1`'s
    /// `causationId` must be `payment.received.v1`'s OWN `eventId`, not
    /// `command.RequestId` (which both facts shared before this fix, making
    /// them siblings a consumer reading only the two envelopes could not
    /// causally order). This reads the ACTUAL `eventId` the domain assigned
    /// to `PaymentReceived` and asserts `CreditReleased.CausationId` equals
    /// THAT value — never merely that the two ids differ from each other
    /// (`Assert.NotEqual` proves non-collision, never provenance — the
    /// distinct assertion below is corroborating, not the guard itself).
    /// </summary>
    [Fact]
    public async Task Backlog57_CreditReleasedCausationIdIsPaymentReceivedsOwnEventId_NotTheRequestIdBothFactsUsedToShare()
    {
        const string orderReference = "ORD-000401";
        const long total = 9_900;
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = IssuedSnapshot(invoiceId, "INV-000401", orderReference, total);

        var invoices = new RecordingInvoiceRepository
        {
            FindByInvoiceReferenceResult = invoiceSnapshot,
            LockByIdResult = invoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, total) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        var command = Command(amount: total);
        var reply = await service.RegisterAsync(command, CancellationToken.None);

        Assert.Equal("accepted", reply.Outcome);

        var paymentReceived = Assert.IsType<PaymentReceived>(Assert.Single(invoices.MarkPaidInvoice!.DomainEvents));
        var creditReleased = Assert.IsType<CreditReleased>(Assert.Single(credits.Saved!.DomainEvents));

        // The provenance assertion — proves the WHICH, not merely a
        // difference.
        Assert.Equal(paymentReceived.EventId, creditReleased.CausationId);

        // Corroborating: the OLD (defective) behaviour reused the
        // request's own id for both facts — this must no longer hold.
        Assert.NotEqual(command.RequestId, creditReleased.CausationId);

        // payment.received.v1 itself is unaffected — it still carries the
        // request's id, per R47/BI13 (only the SIBLING relationship
        // between the two facts changes).
        Assert.Equal(command.RequestId, paymentReceived.CausationId);
    }

    [Fact]
    public async Task R47_ResolvesTheInvoiceByInvoiceIdWhenBothIdentifiersAreOmittedButInvoiceIdIsSupplied()
    {
        const string orderReference = "ORD-000002";
        const long total = 3_000;
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = IssuedSnapshot(invoiceId, "INV-000002", orderReference, total);

        var invoices = new RecordingInvoiceRepository
        {
            FindByIdResult = invoiceSnapshot,
            LockByIdResult = invoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, total) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        var reply = await service.RegisterAsync(Command(invoiceId: invoiceId.Value, invoiceReference: null, amount: total), CancellationToken.None);

        Assert.Equal("accepted", reply.Outcome);
        Assert.Equal("INV-000002", reply.InvoiceReference);
    }

    [Fact]
    public async Task R48_TheFastPathAnswersDuplicateWithoutOpeningATransaction_WhenThePaymentReferenceIsAlreadyRecordedAgainstTheSameInvoice()
    {
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = PaidSnapshot(invoiceId, "INV-000001", "ORD-000001", 5_000, _fixedNow);
        var existingPayment = new PaymentSnapshot(UniqueId.New(), "PAY-000001", invoiceId, new Money(5_000, "EUR"), _fixedNow, "test");

        var invoices = new RecordingInvoiceRepository
        {
            FindPaymentByReferenceResult = existingPayment,
            FindByIdResult = invoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PaymentRegisterService(unitOfWork, credits, invoices, new FakeClock());

        var reply = await service.RegisterAsync(Command(paymentReference: "PAY-000001"), CancellationToken.None);

        Assert.Equal("duplicate", reply.Outcome);
        Assert.Equal("paid", reply.InvoiceStatus);
        Assert.Equal(0, unitOfWork.ExecuteCount);
        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task R48_TheAuthorityReReadUnderTheInvoiceLockAnswersDuplicateAndWritesNothingNew_ClosingTheRaceTheFastPathLeavesOpen()
    {
        const string orderReference = "ORD-000001";
        var invoiceId = UniqueId.New();
        var authorityPayment = new PaymentSnapshot(UniqueId.New(), "PAY-000001", invoiceId, new Money(5_000, "EUR"), _fixedNow, "test");

        var invoices = new RecordingInvoiceRepository
        {
            // Fast path MISSES (no payment yet from this repository's point
            // of view before the transaction opens)...
            FindByInvoiceReferenceResult = IssuedSnapshot(invoiceId, "INV-000001", orderReference, 5_000),
            // ...but by the time the lock is granted, the SAME reference is
            // already recorded for this SAME invoice — a genuinely
            // concurrent duplicate the fast path alone cannot see.
            LockByIdResult = PaidSnapshot(invoiceId, "INV-000001", orderReference, 5_000, _fixedNow),
            FindPaymentByInvoiceIdResult = authorityPayment,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, 5_000) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        var reply = await service.RegisterAsync(Command(paymentReference: "PAY-000001"), CancellationToken.None);

        Assert.Equal("duplicate", reply.Outcome);
        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task R49_AnAmountMismatchRaisesInvoicePaymentAmountMismatchError_AndWritesNothing()
    {
        const string orderReference = "ORD-000001";
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = IssuedSnapshot(invoiceId, "INV-000001", orderReference, 5_000);
        var invoices = new RecordingInvoiceRepository
        {
            FindByInvoiceReferenceResult = invoiceSnapshot,
            LockByIdResult = invoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, 5_000) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        await Assert.ThrowsAsync<InvoicePaymentAmountMismatchError>(
            () => service.RegisterAsync(Command(amount: 4_000), CancellationToken.None));

        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task R49_ACurrencyMismatchRaisesInvoicePaymentCurrencyMismatchError_AndWritesNothing()
    {
        const string orderReference = "ORD-000001";
        var invoiceId = UniqueId.New();
        var invoiceSnapshot = IssuedSnapshot(invoiceId, "INV-000001", orderReference, 5_000);
        var invoices = new RecordingInvoiceRepository
        {
            FindByInvoiceReferenceResult = invoiceSnapshot,
            LockByIdResult = invoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, 5_000) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        await Assert.ThrowsAsync<InvoicePaymentCurrencyMismatchError>(
            () => service.RegisterAsync(Command(amount: 5_000, currency: "GBP"), CancellationToken.None));

        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task R49_ADifferentPaymentReferenceAgainstAnAlreadyPaidInvoiceRaisesInvoiceAlreadyPaidError_AndNeverTouchesTheCreditLedger()
    {
        const string orderReference = "ORD-000001";
        var invoiceId = UniqueId.New();
        var paidSnapshot = PaidSnapshot(invoiceId, "INV-000001", orderReference, 5_000, _fixedNow);
        var existingPayment = new PaymentSnapshot(UniqueId.New(), "PAY-OLD", invoiceId, new Money(5_000, "EUR"), _fixedNow, "test");

        var invoices = new RecordingInvoiceRepository
        {
            FindByInvoiceReferenceResult = paidSnapshot,
            LockByIdResult = paidSnapshot,
            FindPaymentByInvoiceIdResult = existingPayment,
        };
        var credits = new RecordingBuyerCreditRepository { LockResult = CreditWithHold(50_000, orderReference, 5_000) };
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        await Assert.ThrowsAsync<InvoiceAlreadyPaidError>(
            () => service.RegisterAsync(Command(paymentReference: "PAY-NEW"), CancellationToken.None));

        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task N11_TheFastPathRaisesPaymentReferenceConflict_OpeningNoTransaction_WhenTheRecordedPaymentReferenceBelongsToADifferentInvoiceThanTheOneNamed()
    {
        var otherInvoiceId = UniqueId.New();
        var otherInvoiceSnapshot = PaidSnapshot(otherInvoiceId, "INV-000001", "ORD-000001", 5_000, _fixedNow);
        var existingPayment = new PaymentSnapshot(UniqueId.New(), "PAY-X", otherInvoiceId, new Money(5_000, "EUR"), _fixedNow, "test");

        var invoices = new RecordingInvoiceRepository
        {
            FindPaymentByReferenceResult = existingPayment,
            FindByIdResult = otherInvoiceSnapshot,
        };
        var credits = new RecordingBuyerCreditRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PaymentRegisterService(unitOfWork, credits, invoices, new FakeClock());

        // The request names a DIFFERENT invoice than the one PAY-X is
        // actually recorded against.
        await Assert.ThrowsAsync<PaymentReferenceConflictError>(
            () => service.RegisterAsync(Command(invoiceReference: "INV-000002", paymentReference: "PAY-X"), CancellationToken.None));

        Assert.Equal(0, unitOfWork.ExecuteCount);
        Assert.Equal(0, invoices.MarkPaidCallCount);
        Assert.Equal(0, credits.SaveChangesCallCount);
    }

    [Fact]
    public async Task RaisesInvoiceNotFound_OpeningNoTransaction_WhenNoInvoiceResolvesForTheNamedIdentifier()
    {
        var invoices = new RecordingInvoiceRepository();
        var credits = new RecordingBuyerCreditRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PaymentRegisterService(unitOfWork, credits, invoices, new FakeClock());

        await Assert.ThrowsAsync<InvoiceNotFoundError>(
            () => service.RegisterAsync(Command(invoiceReference: "INV-999999"), CancellationToken.None));

        Assert.Equal(0, unitOfWork.ExecuteCount);
    }

    [Fact]
    public async Task RaisesCreditLineNotFound_WhenNoCreditLineExistsForTheInvoicesRetailerAndCompany()
    {
        var invoiceSnapshot = IssuedSnapshot(UniqueId.New(), "INV-000001", "ORD-000001", 5_000);
        var invoices = new RecordingInvoiceRepository { FindByInvoiceReferenceResult = invoiceSnapshot };
        var credits = new RecordingBuyerCreditRepository();
        var service = new PaymentRegisterService(new FakeUnitOfWork(), credits, invoices, new FakeClock());

        await Assert.ThrowsAsync<CreditLineNotFoundError>(
            () => service.RegisterAsync(Command(), CancellationToken.None));

        Assert.Equal(0, invoices.MarkPaidCallCount);
    }

    private sealed class RecordingBuyerCreditRepository : IBuyerCreditRepository
    {
        public BuyerCredit? LockResult { get; set; }

        public List<string>? CallLog { get; set; }

        public int SaveChangesCallCount { get; private set; }

        public BuyerCredit? Saved { get; private set; }

        public Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken cancellationToken)
        {
            CallLog?.Add("credits.Lock");
            return Task.FromResult(LockResult);
        }

        public Task SaveChangesAsync(BuyerCredit credit, CancellationToken cancellationToken)
        {
            CallLog?.Add("credits.SaveChanges");
            SaveChangesCallCount++;
            Saved = credit;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingInvoiceRepository : IInvoiceRepository
    {
        public InvoiceSnapshot? FindByOrderReferenceResult { get; set; }

        public InvoiceSnapshot? LockByOrderReferenceResult { get; set; }

        public InvoiceSnapshot? FindByIdResult { get; set; }

        public InvoiceSnapshot? FindByInvoiceReferenceResult { get; set; }

        public PaymentSnapshot? FindPaymentByReferenceResult { get; set; }

        public PaymentSnapshot? FindPaymentByInvoiceIdResult { get; set; }

        public InvoiceSnapshot? LockByIdResult { get; set; }

        public List<string>? CallLog { get; set; }

        public int MarkPaidCallCount { get; private set; }

        public Invoice? MarkPaidInvoice { get; private set; }

        public MarkPaidInput? MarkPaidPayment { get; private set; }

        public Task<InvoiceSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken) =>
            Task.FromResult(FindByOrderReferenceResult);

        public Task<InvoiceSnapshot?> LockByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken) =>
            Task.FromResult(LockByOrderReferenceResult);

        public Task SaveAsync(Invoice invoice, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by PaymentRegisterServiceTests.");

        public Task<InvoiceSnapshot?> FindByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.FindById");
            return Task.FromResult(FindByIdResult);
        }

        public Task<InvoiceSnapshot?> FindByInvoiceReferenceAsync(string invoiceReference, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.FindByInvoiceReference");
            return Task.FromResult(FindByInvoiceReferenceResult);
        }

        public Task<PaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.FindPaymentByReference");
            return Task.FromResult(FindPaymentByReferenceResult);
        }

        public Task<PaymentSnapshot?> FindPaymentByInvoiceIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.FindPaymentByInvoiceId");
            return Task.FromResult(FindPaymentByInvoiceIdResult);
        }

        public Task<InvoiceSnapshot?> LockByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.LockById");
            return Task.FromResult(LockByIdResult);
        }

        public Task MarkPaidAsync(Invoice invoice, MarkPaidInput payment, CancellationToken cancellationToken)
        {
            CallLog?.Add("invoices.MarkPaid");
            MarkPaidCallCount++;
            MarkPaidInvoice = invoice;
            MarkPaidPayment = payment;
            return Task.CompletedTask;
        }
    }
}
