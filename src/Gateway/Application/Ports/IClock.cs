namespace OrderToCash.Gateway.Application.Ports;

/// <summary>The one seam for "now" — every other service in this repository defines its own copy of this port rather than sharing one (SharedKernel carries none).</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
