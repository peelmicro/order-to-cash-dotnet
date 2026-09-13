using System.Text.Json;
using Xunit;

namespace OrderToCash.Gateway.IntegrationTests;

/// <summary>
/// Direct, non-HTTP proof that <see cref="MoneyFieldSweep"/>'s
/// <c>isCanonicalMoneyAmount</c> clause (<c>MoneyFieldSweep.cs:122</c>) is
/// load-bearing, not decorative. That clause is the ONLY reason the sweep
/// still recognises the documented standalone <c>Money { amount, currency }</c>
/// shape (<c>specs/shared/openapi.yaml</c>'s <c>Money</c> schema) once
/// <c>amount</c> has regressed to a decimal STRING — every other path
/// through <see cref="MoneyFieldSweep"/> requires <c>ValueKind == Number</c>.
///
/// No response <c>MoneyRepresentationHttpTests</c> sweeps over HTTP carries
/// this shape today: the canonical <c>Money</c> object only appears in
/// <c>RegisterPaymentRequest.amount</c> (a REQUEST body), and
/// <c>RegisterPaymentResponse</c> never echoes it back — so without this
/// direct call into the sweep function itself, the clause was provably
/// unexercised by the whole suite (advisory A2,
/// progress/review_test_matrix_rows_that_outlived_their_named_closer.md).
/// This test calls <see cref="MoneyFieldSweep.SweepForMoneyFields"/> the same
/// way <c>MoneyRepresentationHttpTests</c> does, on a body shaped exactly
/// like that request schema's own documented example
/// (<c>specs/shared/openapi.yaml:689-692</c>), just with the string
/// regression the clause exists to catch.
/// </summary>
public sealed class MoneyFieldSweepCanonicalAmountTests
{
    [Fact]
    public void ACanonicalMoneyAmountThatHasRegressedToADecimalStringIsStillDiscovered()
    {
        var body = JsonDocument.Parse(
            """
            {
              "paymentReference": "PAY-2026-08-18-000019",
              "amount": { "amount": "124250", "currency": "EUR" }
            }
            """).RootElement;

        var findings = MoneyFieldSweep.SweepForMoneyFields(body);

        var finding = Assert.Single(findings);
        Assert.Equal("$.amount.amount", finding.Path);
        Assert.Equal(JsonValueKind.String, finding.Amount.ValueKind);
        Assert.Equal("124250", finding.Amount.GetString());
        Assert.NotNull(finding.Currency);
        Assert.Equal("EUR", finding.Currency!.Value.GetString());
    }
}
