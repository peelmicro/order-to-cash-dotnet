namespace OrderToCash.Gateway.Presentation;

/// <summary>Thrown when a resource is genuinely unknown to the Gateway itself, not answered by an RPC responder's own <c>NOT_FOUND</c> — <c>GET /orders/{id}</c>'s "the identifier is unknown to the system" case (R55), which the read model can only answer by absence, never by a thrown error of its own.</summary>
public sealed class GatewayNotFoundError(string message) : Exception(message);
