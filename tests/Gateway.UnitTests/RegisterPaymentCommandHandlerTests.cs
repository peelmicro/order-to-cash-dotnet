using OrderToCash.Gateway.Application.Commands;
using OrderToCash.Gateway.Application.Rpc;
using OrderToCash.Gateway.Domain.Projection;
using OrderToCash.Gateway.UnitTests.TestSupport;
using Xunit;

namespace OrderToCash.Gateway.UnitTests;

/// <summary>Ported from #7's own <c>register-payment.command.ts</c> — the bounded scan of <c>billing.invoice.list</c> that resolves an <c>invoiceId</c> to its <c>orderReference</c>, since no "get invoice by id" RPC subject exists.</summary>
public sealed class RegisterPaymentCommandHandlerTests
{
    private static OrderReadModelDocument OrderDocument(Guid orderId, string orderReference) => new(
        orderId, orderReference, DateTimeOffset.UtcNow,
        new OrderReadModelParty("CarrefourEs", null, "8412345000013"),
        new OrderReadModelParty("IBERFOODS", null, "8412345000020"),
        "invoiced", null, "EUR", new OrderReadModelTotals(100, 0, 100), [],
        new OrderReadModelReferences(null, "INV-000027", null), [], true, DateTimeOffset.UtcNow);

    private static InvoiceViewPayload Invoice(Guid invoiceId, string orderReference) => new(
        invoiceId, "INV-000027", DateTimeOffset.UtcNow, orderReference, "CarrefourEs", "IBERFOODS", "EUR", 100, 0, 100, "issued", null, null);

    /// <summary>The payment.register RPC call's correlationId must be the ORDER id (saga.md's invariant), never the invoice id the caller supplied — the exact seam this feature's own known-risk section is about.</summary>
    [Fact]
    public async Task HandleAsync_UsesTheResolvedOrderIdAsTheCorrelationId_ForBillingPaymentRegister()
    {
        var invoiceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(invoiceId, "ORD-000042")], new InvoicePageInfo(1, 200, 1)));
        rpc.EnqueueReply(GatewaySubjects.PaymentRegister, new PaymentRegisterReplyPayload("accepted", "PAY-1", "INV-000027", "ORD-000042", "paid", DateTimeOffset.UtcNow));
        var readModel = new FakeOrderReadModel();
        readModel.Seed(OrderDocument(orderId, "ORD-000042"));
        var handler = new RegisterPaymentCommandHandler(rpc, readModel);

        var result = await handler.HandleAsync(
            new RegisterPaymentCommand(invoiceId, "PAY-1", 100, "EUR", DateTimeOffset.UtcNow, "operator"), CancellationToken.None);

        Assert.Equal(orderId, result.CorrelationId);
        var paymentCall = rpc.Calls.Single(c => c.Subject == GatewaySubjects.PaymentRegister);
        Assert.Equal(orderId, paymentCall.Meta.CorrelationId);
    }

    [Fact]
    public async Task HandleAsync_ScansMultiplePages_UntilTheInvoiceIdMatches()
    {
        var invoiceId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var rpc = new FakeRpcClient();
        // Page 1: full (200 items), none matching.
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload(
            Enumerable.Range(0, 200).Select(_ => Invoice(Guid.NewGuid(), "ORD-OTHER")).ToList(), new InvoicePageInfo(1, 200, 500)));
        // Page 2: the match.
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(invoiceId, "ORD-000099")], new InvoicePageInfo(2, 200, 500)));
        rpc.EnqueueReply(GatewaySubjects.PaymentRegister, new PaymentRegisterReplyPayload("accepted", "PAY-1", "INV-000099", "ORD-000099", "paid", DateTimeOffset.UtcNow));
        var readModel = new FakeOrderReadModel();
        readModel.Seed(OrderDocument(orderId, "ORD-000099"));
        var handler = new RegisterPaymentCommandHandler(rpc, readModel);

        var result = await handler.HandleAsync(
            new RegisterPaymentCommand(invoiceId, "PAY-1", 100, "EUR", DateTimeOffset.UtcNow, "operator"), CancellationToken.None);

        Assert.Equal("accepted", result.Reply.Outcome);
        Assert.Equal(2, rpc.Calls.Count(c => c.Subject == GatewaySubjects.InvoiceList));
    }

    /// <summary>The scan reached a SHORT page (fewer than the page size) without a match — genuinely exhausted, honestly 404.</summary>
    [Fact]
    public async Task HandleAsync_Throws_InvoiceNotFoundError_WhenTheScanReachesAShortPageWithNoMatch()
    {
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(Guid.NewGuid(), "ORD-OTHER")], new InvoicePageInfo(1, 200, 1)));
        var handler = new RegisterPaymentCommandHandler(rpc, new FakeOrderReadModel());

        await Assert.ThrowsAsync<InvoiceNotFoundError>(
            () => handler.HandleAsync(new RegisterPaymentCommand(Guid.NewGuid(), "PAY-1", 100, "EUR", DateTimeOffset.UtcNow, "operator"), CancellationToken.None));
    }

    /// <summary>The scan hit its page budget while every page fetched was still full — the invoice MAY exist beyond the search window, so this is never claimed as a 404.</summary>
    [Fact]
    public async Task HandleAsync_Throws_InvoiceScanBudgetExceededError_WhenEveryScannedPageWasFull()
    {
        var rpc = new FakeRpcClient();
        for (var page = 1; page <= 5; page++)
        {
            rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload(
                Enumerable.Range(0, 200).Select(_ => Invoice(Guid.NewGuid(), "ORD-OTHER")).ToList(), new InvoicePageInfo(page, 200, 2000)));
        }

        var handler = new RegisterPaymentCommandHandler(rpc, new FakeOrderReadModel());

        await Assert.ThrowsAsync<InvoiceScanBudgetExceededError>(
            () => handler.HandleAsync(new RegisterPaymentCommand(Guid.NewGuid(), "PAY-1", 100, "EUR", DateTimeOffset.UtcNow, "operator"), CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_Throws_OrderNotYetProjectedError_WhenTheInvoiceIsFoundButItsOrderIsNotInTheReadModelYet()
    {
        var invoiceId = Guid.NewGuid();
        var rpc = new FakeRpcClient();
        rpc.EnqueueReply(GatewaySubjects.InvoiceList, new InvoiceListReplyPayload([Invoice(invoiceId, "ORD-000042")], new InvoicePageInfo(1, 200, 1)));
        var handler = new RegisterPaymentCommandHandler(rpc, new FakeOrderReadModel());

        await Assert.ThrowsAsync<OrderNotYetProjectedError>(
            () => handler.HandleAsync(new RegisterPaymentCommand(invoiceId, "PAY-1", 100, "EUR", DateTimeOffset.UtcNow, "operator"), CancellationToken.None));
    }
}
