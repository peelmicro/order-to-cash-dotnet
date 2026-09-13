namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The ONE seam that lets Application build a saga command's (or the
/// synthetic <c>orders.cancel.requested</c> envelope's) wire body without
/// depending on <c>Infrastructure.Messaging.Rpc.RpcJson</c> directly (feature
/// 76, <c>application_layer_depends_on_infrastructure_unguarded</c>) — the
/// ONE implementation, <c>RpcJsonRequestSerializer</c>, is a thin wrapper
/// composing the EXISTING, UNMODIFIED <c>RpcJson.Serialize</c> verbatim
/// (the same shared <c>JsonWire.Options</c> every RPC responder and the
/// outbox already use), never a second serializer.
/// </summary>
public interface IRpcRequestSerializer
{
    byte[] Serialize<T>(T value);
}
