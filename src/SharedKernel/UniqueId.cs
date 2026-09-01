using OrderToCash.SharedKernel.Errors;

namespace OrderToCash.SharedKernel;

/// <summary>
/// An opaque, globally unique identifier generated inside the domain, not by
/// the store — used for aggregate identity and for `eventId`
/// (domain-model.md §2.5). Two <see cref="UniqueId"/>s are equal iff their
/// underlying values are equal.
/// </summary>
public readonly record struct UniqueId
{
    private UniqueId(Guid value) => Value = value;

    public Guid Value { get; }

    /// <summary>Generates a fresh identity — the only place a new <see cref="UniqueId"/> is minted.</summary>
    public static UniqueId New() => new(Guid.NewGuid());

    /// <summary>Wraps an existing, non-empty GUID — e.g. one round-tripped from the wire or the store.</summary>
    public static UniqueId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new InvalidUniqueIdError();
        }

        return new UniqueId(value);
    }

    public override string ToString() => Value.ToString();
}
