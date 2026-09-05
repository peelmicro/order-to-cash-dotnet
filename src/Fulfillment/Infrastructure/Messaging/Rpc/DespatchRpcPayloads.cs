using OrderToCash.Contracts.Facts;

namespace OrderToCash.Fulfillment.Infrastructure.Messaging.Rpc;

// The request/reply payload records of fulfillment.despatch.create,
// transcribed from specs/shared/asyncapi.yaml — Fulfillment's OWN copy, not
// a reference to Orders' SagaCommandPayloads.cs, per design.md §6.3's rule
// that "RPC payloads live in the service that speaks them".

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateRequestPayload</c>.</summary>
public sealed record DespatchCreateRequestPayload(string OrderReference);

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateReplyPayload</c>. <c>Created</c> is <see langword="false"/> on the idempotent repeat — the existing despatch advice is returned and no second fact is emitted (F8) — still a plain success (SO6), never thrown.</summary>
public sealed record DespatchCreateReplyPayload(
    string OrderReference,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    bool Created,
    IReadOnlyList<DespatchLine>? Lines = null);
