namespace OrderToCash.Gateway.Domain.Sse;

/// <summary>The result of <see cref="ReplayBuffer{T}.ReplayAfter"/> — ported from #7's <c>domain/sse/replay-buffer.ts</c> <c>ReplayResult&lt;T&gt;</c> interface.</summary>
/// <param name="Resumed">
/// <see langword="true"/> only when the given cursor was found in the
/// buffer. <see langword="false"/> both when no cursor was supplied at all
/// (a fresh connection) and when the cursor was supplied but is no longer
/// (or never was) in the buffer — openapi.yaml's own two honest
/// limitations, and a single boolean is what makes them indistinguishable
/// to the caller by construction: a stale cursor and an unknown one both
/// mean "the buffer genuinely could not find that cursor", never an error.
/// </param>
/// <param name="Missed">Every entry strictly AFTER <paramref name="Resumed"/>'s cursor — never the cursor's own frame again ("resumes after", never "resumes at-or-before", R55 acceptance bullet 1).</param>
public sealed record ReplayResult<T>(bool Resumed, IReadOnlyList<T> Missed);
