namespace OrderToCash.Projector.Domain;

/// <summary>The human-readable rendering of one fact (PR16) — <see cref="Detail"/> is <see langword="null"/> for the nine facts that carry none.</summary>
public sealed record SummaryResult(string Summary, IReadOnlyDictionary<string, object>? Detail);
