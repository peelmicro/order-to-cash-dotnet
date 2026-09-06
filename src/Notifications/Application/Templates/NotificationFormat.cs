using System.Text;

namespace OrderToCash.Notifications.Application.Templates;

/// <summary>
/// Small, pure formatting helpers shared by every fact template
/// (<c>*Template.cs</c> in this folder) — no framework, no I/O, so each
/// template stays trivially unit-testable and this whole folder can live in
/// <c>Application/</c> rather than <c>Infrastructure/</c> (CLAUDE.md's
/// dependency direction: Application must not depend on Infrastructure, and
/// a template built from <c>Infrastructure/Templates</c> would have forced
/// exactly that on <c>NotifyFactCommandHandlers</c>).
/// </summary>
public static class NotificationFormat
{
    /// <summary>
    /// Renders an integer-minor-units amount (CLAUDE.md's Money rule: "never
    /// a float, never a decimal in domain arithmetic") as a human-readable
    /// major-unit string for an email body — e.g.
    /// <c>FormatMoney(124250, "USD")</c> -&gt; <c>"1242.50 USD"</c>. Display
    /// only: integer division and modulo throughout, no floating-point or
    /// <c>decimal</c> conversion anywhere in this method.
    /// </summary>
    public static string FormatMoney(long amountMinorUnits, string currency)
    {
        var sign = amountMinorUnits < 0 ? "-" : string.Empty;
        var absoluteMinorUnits = amountMinorUnits == long.MinValue ? (ulong)long.MaxValue + 1 : (ulong)Math.Abs(amountMinorUnits);
        var majorUnits = absoluteMinorUnits / 100;
        var minorUnits = absoluteMinorUnits % 100;
        return $"{sign}{majorUnits}.{minorUnits:D2} {currency}";
    }

    /// <summary>
    /// Synthesizes a recipient address from <paramref name="identifier"/> —
    /// normally the retailer's business code, but <c>PaymentReceivedPayload</c>
    /// carries no <c>retailerCode</c> at all (domain-model.md §7.2, row 11:
    /// <c>orderReference, invoiceReference, paymentReference, amount,
    /// currency, valueDate, source</c>), so <c>PaymentReceivedTemplate</c>
    /// passes <c>orderReference</c> instead — the best available identifier
    /// for that one fact (the same choice #7 made, for the same reason).
    ///
    /// The domain model carries no email address for any party anywhere —
    /// this is a demo affordance, not a real address book. Mailpit (the
    /// local SMTP sink) accepts and captures any address without delivering
    /// it externally, so a deterministic, human-readable synthetic address
    /// is sufficient to prove "the party for this order was notified" in its
    /// inbox.
    /// </summary>
    public static string RecipientFor(string identifier) => $"{identifier.ToLowerInvariant()}@retailer.order-to-cash.example";

    /// <summary>The correlation id belongs in the subject line of every template (feature 23's acceptance list) — one shared formatter so the wording cannot drift between the seven templates.</summary>
    public static string SubjectWithCorrelationId(string summary, Guid correlationId) => $"[order-to-cash] {summary} (correlationId: {correlationId})";

    /// <summary>
    /// Every payload-derived string that lands inside an HTML template must
    /// go through this first. Fact payloads are internally produced by the
    /// other three services today, but nothing prevents a future externally
    /// supplied business reference (e.g. a remittance's <c>paymentReference</c>)
    /// from carrying <c>&lt;</c>/<c>&amp;</c>/etc., and it would then be
    /// rendered by an email client — escaping unconditionally, rather than
    /// only for fields judged "reachable today", is what makes this guard
    /// durable against that. The plain-text body never needs this — there is
    /// no markup to inject into.
    /// </summary>
    public static string EscapeHtml(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            builder.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => character.ToString(),
            });
        }

        return builder.ToString();
    }
}
