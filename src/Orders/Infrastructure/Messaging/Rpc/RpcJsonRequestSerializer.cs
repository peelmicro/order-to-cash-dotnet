using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

/// <summary>
/// The ONE implementation of <see cref="IRpcRequestSerializer"/> — composes
/// the EXISTING, UNMODIFIED <see cref="RpcJson.Serialize{T}"/> verbatim, it
/// wraps it, it does not replace or alter it (feature 76,
/// <c>application_layer_depends_on_infrastructure_unguarded</c>).
/// </summary>
public sealed class RpcJsonRequestSerializer : IRpcRequestSerializer
{
    public byte[] Serialize<T>(T value) => RpcJson.Serialize(value);
}
