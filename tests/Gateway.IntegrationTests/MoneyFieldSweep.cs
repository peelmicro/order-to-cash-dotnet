using System.Text.Json;

namespace OrderToCash.Gateway.IntegrationTests;

// R1 (`specs/shared/requirements.md`) — "THE SYSTEM SHALL represent every
// monetary amount as an integer count of minor units together with an ISO
// 4217 alpha-3 currency code ... in every API response." Ported from #7's
// `apps/gateway/src/test-support/money-field-sweep.ts` (`sweepForMoneyFields`)
// — same recognition rules, same deliberate over-inclusion, retargeted at a
// parsed `JsonElement` response body instead of a plain JS object, because
// this codebase's wire type is `System.Text.Json`, never a plain object
// graph.
//
// Two money shapes exist in `specs/shared/openapi.yaml`:
//
//  1. A standalone `Money` object travelling on its own: `{ amount,
//     currency }` (e.g. `RegisterPaymentRequest.amount`) — `currency` is a
//     DIRECT SIBLING of `amount` inside that same object.
//
//  2. The far more common "enclosing object already declares its currency"
//     pattern (`MinorUnits`'s own schema description): an ancestor object
//     carries a `currency` field once, and every `MinorUnits`-typed field
//     anywhere inside it — including several levels of nested
//     objects/arrays down, with NO `currency` field of their own — is
//     expressed in that currency. Concretely: `OrderDetail.currency`
//     covers `OrderDetail.totals.totalAmount` (one level down, `totals`
//     has no `currency` of its own) AND `OrderDetail.items[].unitPrice`
//     (inside an array, two levels down, each item has no `currency` of
//     its own either) AND `Credit.creditLimit`/`activeHolds`/
//     `openExposure`/`availableCredit` (direct siblings of
//     `Credit.currency`, but NOT named "amount" or "discount" — proving a
//     name-based recogniser would have to enumerate business vocabulary,
//     exactly the per-field style this sweep exists to avoid).
//
// The recogniser is therefore SHAPE-based, not name-based, with exactly one
// deliberate exception tied to the documented `Money` schema itself (see
// `IsCanonicalMoneyAmount` below): as the walk descends through a response
// body it carries an "effective currency" — the nearest `currency` field
// found at or above the current object, however deep — and flags every
// NUMBER-typed sibling field (other than `currency` itself) found under
// that context as a monetary finding, PLUS the `amount` key specifically
// whenever it sits directly beside a `currency` key, regardless of that
// value's own JSON value kind (this is what lets the sweep catch a
// `Money.amount` that regressed to a decimal STRING, which a
// number-typed-only rule would silently miss).
//
// Known, disclosed over-inclusion: a field that is merely an unrelated
// integer sharing an object with a `currency` sibling (e.g.
// `OrderItem.quantity`, which lives beside `unitPrice`/`lineDiscount`
// inside the SAME currency-bearing ancestor) is also flagged. This is
// harmless for R1's claim — a quantity is always a genuine integer, so the
// "must be an integer, must carry a valid currency" assertion holds for it
// trivially — and deliberately preferred over a name-based denylist, which
// would silently stop protecting a future field it did not anticipate.
// See progress/impl_test_matrix_rows_that_outlived_their_named_closer.md
// for the full trade-off record and the assertion-by-assertion enumeration
// against #7's own file.

/// <summary>
/// One discovered monetary field. <see cref="Amount"/>/<see cref="Currency"/>
/// are captured via <see cref="JsonElement.Clone"/> so they remain valid
/// after the source <see cref="JsonDocument"/> is disposed.
/// </summary>
public sealed record MoneyFinding(string Path, JsonElement Amount, JsonElement? Currency);

public static class MoneyFieldSweep
{
    private const string CurrencyKey = "currency";
    private const string CanonicalAmountKey = "amount";

    /// <summary>
    /// Recurses <paramref name="body"/> (an already-parsed JSON response)
    /// and returns every field the shape-based rules above recognise as
    /// carrying a monetary amount, each paired with the currency value it
    /// was matched against. Never itself asserts anything — the caller
    /// decides what "integer, ISO-4217" means for each finding, exactly as
    /// #7's own module does.
    /// </summary>
    public static IReadOnlyList<MoneyFinding> SweepForMoneyFields(JsonElement body)
    {
        var findings = new List<MoneyFinding>();
        Walk(body, "$", inheritedCurrency: null, findings);
        return findings;
    }

    private static void Walk(JsonElement node, string path, JsonElement? inheritedCurrency, List<MoneyFinding> findings)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
            {
                Walk(item, $"{path}[{index}]", inheritedCurrency, findings);
                index++;
            }

            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var hasOwnCurrency = node.TryGetProperty(CurrencyKey, out var ownCurrency);
        var effectiveCurrency = hasOwnCurrency ? ownCurrency : inheritedCurrency;

        foreach (var property in node.EnumerateObject())
        {
            if (property.Name == CurrencyKey)
            {
                continue;
            }

            var value = property.Value;

            // The documented `Money` shape: `amount` travelling directly
            // beside its OWN `currency` — flagged regardless of `value`'s
            // JSON value kind, specifically so a regression to a decimal
            // string is still caught (a pure "is a JSON number" rule would
            // silently stop looking).
            var isCanonicalMoneyAmount = property.Name == CanonicalAmountKey && hasOwnCurrency;
            var isNumberUnderCurrencyContext = value.ValueKind == JsonValueKind.Number && effectiveCurrency is not null;

            if (effectiveCurrency is not null && (isCanonicalMoneyAmount || isNumberUnderCurrencyContext))
            {
                findings.Add(new MoneyFinding($"{path}.{property.Name}", value.Clone(), effectiveCurrency.Value.Clone()));
                continue; // a recognised money field is a leaf — never itself walked into
            }

            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                Walk(value, $"{path}.{property.Name}", effectiveCurrency, findings);
            }
        }
    }
}
