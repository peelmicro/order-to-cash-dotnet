using OrderToCash.Contracts.Rpc;
using OrderToCash.Cqrs;
using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Application.Commands;

/// <summary>The <c>billing.payment.register</c> command — carries <see cref="CorrelationId"/>/<see cref="RequestId"/> as <see cref="UniqueId"/>, extracted from the request's headers by the responder, never from the payload (mirrors <c>IssueInvoiceCommand</c>). Exactly one of <see cref="InvoiceId"/>/<see cref="InvoiceReference"/> is guaranteed non-null by <c>PaymentRegisterRequestValidator</c> before this command is ever built.</summary>
public sealed record RegisterPaymentCommand(
    Guid? InvoiceId,
    string? InvoiceReference,
    string PaymentReference,
    long Amount,
    string Currency,
    DateTimeOffset ValueDate,
    string Source,
    UniqueId CorrelationId,
    UniqueId RequestId) : ICommand<PaymentRegisterReplyPayload>;

/// <summary>Thin delegation to <see cref="PaymentRegisterService.RegisterAsync"/> — the split that keeps the transactional unit a plain class a unit test can <c>new</c> with fakes (mirrors <c>IssueInvoiceCommandHandler</c>).</summary>
public sealed class RegisterPaymentCommandHandler(PaymentRegisterService service)
    : ICommandHandler<RegisterPaymentCommand, PaymentRegisterReplyPayload>
{
    public Task<PaymentRegisterReplyPayload> HandleAsync(RegisterPaymentCommand command, CancellationToken cancellationToken) =>
        service.RegisterAsync(command, cancellationToken);
}
