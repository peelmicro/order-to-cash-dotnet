using OrderToCash.Contracts.Facts;
using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Commands;

/// <summary>The <c>billing.invoice.issue</c> command — carries <see cref="CorrelationId"/>/<see cref="RequestId"/> as <see cref="UniqueId"/> (`BI2`), extracted from the request's headers by the responder, never from the payload.</summary>
public sealed record IssueInvoiceCommand(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<InvoiceLine> Lines,
    long? Discount,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<Infrastructure.Messaging.Rpc.InvoiceIssueReplyPayload>;

/// <summary>Thin delegation to <see cref="InvoiceIssueService.IssueAsync"/> — the split that keeps the transactional unit a plain class a unit test can <c>new</c> with fakes.</summary>
public sealed class IssueInvoiceCommandHandler(InvoiceIssueService service)
    : ICommandHandler<IssueInvoiceCommand, Infrastructure.Messaging.Rpc.InvoiceIssueReplyPayload>
{
    public Task<Infrastructure.Messaging.Rpc.InvoiceIssueReplyPayload> HandleAsync(IssueInvoiceCommand command, CancellationToken cancellationToken) =>
        service.IssueAsync(command, cancellationToken);
}
