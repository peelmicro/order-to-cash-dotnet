namespace OrderToCash.Billing.Presentation.Rpc;

/// <summary>
/// The three subjects this responder speaks — <c>specs/shared/asyncapi.yaml</c>
/// channels' own <c>address</c>. Guarded by
/// <c>tests/Billing.UnitTests/CreditSubjectsTests.cs</c>, which reads the
/// spec as text rather than retyping the subjects — the discipline
/// <c>OrdersFactTopic</c>/<c>RpcSubjects</c>/<c>StockSubjects</c> already
/// establish.
/// </summary>
public static class CreditSubjects
{
    public const string CreditHold = "billing.credit.hold";

    public const string CreditRelease = "billing.credit.release";

    public const string CreditList = "billing.credit.list";
}
