using OrderToCash.Contracts.Facts;

namespace OrderToCash.Contracts.Rpc;

// The request/reply payload records of fulfillment.despatch.create,
// transcribed from specs/shared/asyncapi.yaml. Feature 76
// (application_layer_depends_on_infrastructure_unguarded) moved these here
// from src/Fulfillment/Infrastructure/Messaging/Rpc/DespatchRpcPayloads.cs
// AND unified them with Orders' own caller-side copy in
// src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs, which
// declared the structurally IDENTICAL pair for the same subject — see
// CreditRpcPayloads.cs's own header for the full reasoning.

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateRequestPayload</c>.</summary>
public sealed record DespatchCreateRequestPayload(string OrderReference);

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateReplyPayload</c>. <c>Created</c> is <see langword="false"/> on the idempotent repeat — the existing despatch advice is returned and no second fact is emitted (F8) — still a plain success (SO6), never thrown.</summary>
public sealed record DespatchCreateReplyPayload(
    string OrderReference,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    bool Created,
    IReadOnlyList<DespatchLine>? Lines = null);
