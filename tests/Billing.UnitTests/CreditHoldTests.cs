using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Domain.Events;
using OrderToCash.SharedKernel;
using Xunit;

namespace OrderToCash.Billing.UnitTests;

/// <summary>
/// `R38`, `R39` (matrix `credit-hold.spec`), `BC10` domain half, `BC14`
/// domain half, `BC26`.
/// </summary>
public sealed class CreditHoldTests
{
    private static BuyerCreditSnapshot Line(long limit, long committedExposure, IReadOnlyList<CreditLedgerEntrySnapshot>? entries = null, string currency = "EUR") =>
        new(UniqueId.New(), "CR-000001", "CarrefourEs", "IBERFOODS", new Money(limit, currency), committedExposure, entries ?? []);

    private static CreditContext Ctx() => new(DateTimeOffset.UtcNow, UniqueId.New());

    [Fact]
    public void R38_AppendsAHoldEntryAndEmitsExactlyOneCreditApprovedV1CarryingTheHeldAmountAndTheResultingAvailableCredit()
    {
        var credit = BuyerCredit.Reconstitute(Line(limit: 10_000, committedExposure: 0));
        var request = new HoldRequest(OrderNumber.Parse("ORD-000001"), new Money(1_000, "EUR"), UniqueId.New());

        Assert.IsType<HoldEvaluation.Fits>(credit.EvaluateHold(request));

        var entry = credit.Approve(request, Ctx(), UniqueId.New);

        Assert.Equal(CreditEntryType.Hold, entry.Type);
        var appended = Assert.Single(credit.AppendedEntries);
        Assert.Same(entry, appended);

        var fact = Assert.IsType<CreditApproved>(Assert.Single(credit.DomainEvents));
        Assert.Equal(request.Amount, fact.HeldAmount);
        Assert.Equal(9_000, fact.AvailableCreditAfter.MinorUnits);
    }

    [Fact]
    public void R39_AppendsNoLedgerEntryAndEmitsCreditRejectedV1WithAMachineReadableReason_WhenTheAmountExceedsTheAvailableCreditOrTheCreditPortRefuses()
    {
        // Branch 1: the amount exceeds the available credit.
        var overLimitCredit = BuyerCredit.Reconstitute(Line(limit: 1_000, committedExposure: 0));
        var overLimitRequest = new HoldRequest(OrderNumber.Parse("ORD-000001"), new Money(1_500, "EUR"), UniqueId.New());

        Assert.IsType<HoldEvaluation.OverLimit>(overLimitCredit.EvaluateHold(overLimitRequest));
        overLimitCredit.Refuse(overLimitRequest, CreditRejectionReason.OverLimit, Ctx(), UniqueId.New);

        Assert.Empty(overLimitCredit.AppendedEntries);
        var overLimitFact = Assert.IsType<CreditRejected>(Assert.Single(overLimitCredit.DomainEvents));
        Assert.Equal(CreditRejectionReason.OverLimit, overLimitFact.Reason);
        Assert.Equal(1_500, overLimitFact.RequestedAmount.MinorUnits);
        Assert.Equal(1_000, overLimitFact.AvailableCredit.MinorUnits);

        // Branch 2: the amount fits, but the credit-decision port refuses it.
        var portRefusedCredit = BuyerCredit.Reconstitute(Line(limit: 10_000, committedExposure: 0));
        var fittingRequest = new HoldRequest(OrderNumber.Parse("ORD-000002"), new Money(1_500, "EUR"), UniqueId.New());

        Assert.IsType<HoldEvaluation.Fits>(portRefusedCredit.EvaluateHold(fittingRequest));
        portRefusedCredit.Refuse(fittingRequest, CreditRejectionReason.SimulatedCentsRule, Ctx(), UniqueId.New);

        Assert.Empty(portRefusedCredit.AppendedEntries);
        var portRefusedFact = Assert.IsType<CreditRejected>(Assert.Single(portRefusedCredit.DomainEvents));
        Assert.Equal(CreditRejectionReason.SimulatedCentsRule, portRefusedFact.Reason);
        Assert.Equal(10_000, portRefusedFact.AvailableCredit.MinorUnits);
    }

    [Fact]
    public void BC10_ReportsAvailableCreditAfterRecomputedWithTheAppendedHold_NeverThePreHoldValue()
    {
        var credit = BuyerCredit.Reconstitute(Line(limit: 50_000, committedExposure: 10_000));
        var preHoldAvailable = credit.AvailableCredit.MinorUnits;
        var request = new HoldRequest(OrderNumber.Parse("ORD-000001"), new Money(5_000, "EUR"), UniqueId.New());

        credit.Approve(request, Ctx(), UniqueId.New);

        var fact = Assert.IsType<CreditApproved>(Assert.Single(credit.DomainEvents));

        Assert.NotEqual(preHoldAvailable, fact.AvailableCreditAfter.MinorUnits);
        Assert.Equal(preHoldAvailable - 5_000, fact.AvailableCreditAfter.MinorUnits);
        Assert.Equal(credit.AvailableCredit.MinorUnits, fact.AvailableCreditAfter.MinorUnits);
    }

    [Fact]
    public void BC14_EmitsACreditRejectedFactForAnAdapterRefusalThatDiffersFromTheOverLimitRefusalInTheReasonFieldAndInNothingElse()
    {
        // ONE fixture: an over-limit request against a line with no existing
        // holds. Genuine over_limit is legitimate here because the amount
        // truly exceeds AvailableCredit; the adapter refusal is built from
        // the SAME request/context/aggregate, because Refuse's mismatch
        // guard only fires for reason == over_limit.
        var credit = BuyerCredit.Reconstitute(Line(limit: 100_000, committedExposure: 0));
        var request = new HoldRequest(OrderNumber.Parse("ORD-000001"), new Money(150_000, "EUR"), UniqueId.New());
        var ctx = Ctx();

        credit.Refuse(request, CreditRejectionReason.OverLimit, ctx, UniqueId.New);
        credit.Refuse(request, CreditRejectionReason.SimulatedCentsRule, ctx, UniqueId.New);

        Assert.Equal(2, credit.DomainEvents.Count);
        var overLimitFact = Assert.IsType<CreditRejected>(credit.DomainEvents[0]);
        var adapterFact = Assert.IsType<CreditRejected>(credit.DomainEvents[1]);

        Assert.Equal(CreditRejectionReason.OverLimit, overLimitFact.Reason);
        Assert.Equal(CreditRejectionReason.SimulatedCentsRule, adapterFact.Reason);
        Assert.NotEqual(overLimitFact.EventId, adapterFact.EventId);

        // Positive assertion of sameness: normalise EventId, OccurredAt and
        // Reason, and everything else must be byte-for-byte equal.
        var normalisedOverLimit = overLimitFact with { EventId = default, OccurredAt = default, Reason = default };
        var normalisedAdapter = adapterFact with { EventId = default, OccurredAt = default, Reason = default };
        Assert.Equal(normalisedOverLimit, normalisedAdapter);
    }

    [Fact]
    public void BC26_RanksAlreadyHeldAboveCurrencyMismatchAndCurrencyMismatchAboveOverLimit_WhenMoreThanOneApplies()
    {
        // AlreadyHeld vs. CurrencyMismatch AND OverLimit at once: the order
        // already holds, the request's currency is wrong, AND the amount
        // would exceed the limit even taken at face value.
        var alreadyHeldEntry = new CreditLedgerEntrySnapshot(UniqueId.New(), "ORD-000001", new Money(500, "EUR"), CreditEntryType.Hold, DateTimeOffset.UtcNow);
        var alreadyHeldCredit = BuyerCredit.Reconstitute(Line(limit: 1_000, committedExposure: 500, entries: [alreadyHeldEntry]));
        var alreadyHeldRequest = new HoldRequest(OrderNumber.Parse("ORD-000001"), new Money(999_999, "GBP"), UniqueId.New());

        Assert.IsType<HoldEvaluation.AlreadyHeld>(alreadyHeldCredit.EvaluateHold(alreadyHeldRequest));

        // CurrencyMismatch vs. OverLimit, with no hold in the way.
        var noHoldCredit = BuyerCredit.Reconstitute(Line(limit: 1_000, committedExposure: 0));
        var mismatchedAndOverLimitRequest = new HoldRequest(OrderNumber.Parse("ORD-000002"), new Money(999_999, "GBP"), UniqueId.New());

        Assert.IsType<HoldEvaluation.CurrencyMismatch>(noHoldCredit.EvaluateHold(mismatchedAndOverLimitRequest));
    }
}
