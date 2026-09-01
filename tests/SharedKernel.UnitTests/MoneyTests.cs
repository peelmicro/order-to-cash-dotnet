using System.Reflection;
using OrderToCash.SharedKernel.Errors;
using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>
/// specs/shared/requirements.md R1 (domain half — the API half,
/// `api/money-representation.spec`, is not this assessment's shared kernel)
/// and R2. specs/shared/test-matrix.md rows R1, R2.
/// </summary>
public sealed class MoneyTests
{
    [Fact]
    public void R1_Money_RepresentsOneThousandTwoHundredFortyTwoPoint50EurosAsOneHundredTwentyFourThousandTwoHundredFiftyMinorUnitsAndOffersNoDecimalOrFloatingPointRepresentation()
    {
        // 1 242,50 € == 124250 minor units (cents).
        var money = new Money(124_250, "EUR");

        Assert.Equal(124_250, money.MinorUnits);
        Assert.Equal("EUR", money.Currency);

        AssertNoDecimalOrFloatingPointSurfaceOnMoney();
    }

    /// <summary>
    /// R1 does not merely ask for a passing constructor call — it asks that
    /// no non-integer major-unit representation exists on the type at all
    /// (domain-model.md §2.1, M1: "A decimal, floating-point or fixed-point
    /// major-unit representation is never used"; CLAUDE.md: "Never a float,
    /// never decimal"). This walks every public field, property, method
    /// return type, method parameter, constructor parameter and conversion
    /// operator of <see cref="Money"/> and fails if any of them is
    /// <see cref="decimal"/>, <see cref="float"/> or <see cref="double"/>,
    /// proving the absence rather than narrating it. Originally checked
    /// <see cref="decimal"/> only and public members only — see
    /// progress/review_shared_kernel.md defect D1: a `double` accessor on
    /// `Money` passed the whole suite green. Fields are included too, even
    /// though `Money`'s own fields happen to be auto-property-backed and
    /// private, so a future field added directly (not through a property)
    /// cannot silently reopen the same gap.
    /// </summary>
    private static void AssertNoDecimalOrFloatingPointSurfaceOnMoney()
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        var forbiddenTypes = new HashSet<Type> { typeof(decimal), typeof(float), typeof(double) };
        var offences = new List<string>();

        foreach (var field in typeof(Money).GetFields(flags))
        {
            if (forbiddenTypes.Contains(field.FieldType))
            {
                offences.Add($"field {field.Name} ({field.FieldType.Name})");
            }
        }

        foreach (var property in typeof(Money).GetProperties(flags))
        {
            if (forbiddenTypes.Contains(property.PropertyType))
            {
                offences.Add($"property {property.Name} ({property.PropertyType.Name})");
            }
        }

        foreach (var method in typeof(Money).GetMethods(flags))
        {
            if (method.IsSpecialName && (method.Name.StartsWith("get_", StringComparison.Ordinal)
                || method.Name.StartsWith("set_", StringComparison.Ordinal)))
            {
                // property accessors only — already covered by the property
                // check above. Deliberately NOT skipping every IsSpecialName
                // method: operator overloads (op_Implicit, op_Explicit,
                // op_Addition, ...) are ALSO IsSpecialName, and this loop is
                // exactly what has to keep seeing them — a conversion
                // operator compiles to a static method, so its return type
                // is caught here.
                continue;
            }

            if (forbiddenTypes.Contains(method.ReturnType))
            {
                offences.Add($"method {method.Name} return type ({method.ReturnType.Name})");
            }

            foreach (var parameter in method.GetParameters())
            {
                if (forbiddenTypes.Contains(parameter.ParameterType))
                {
                    offences.Add($"method {method.Name} parameter {parameter.Name} ({parameter.ParameterType.Name})");
                }
            }
        }

        foreach (var ctor in typeof(Money).GetConstructors(flags))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                if (forbiddenTypes.Contains(parameter.ParameterType))
                {
                    offences.Add($"constructor parameter {parameter.Name} ({parameter.ParameterType.Name})");
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            $"Money exposes a decimal or floating-point surface: {string.Join(", ", offences)}");
    }

    [Fact]
    public void R2_Money_RaisesDomainErrorOnCrossCurrencyAddSubtractAndCompareWithNoImplicitConversion()
    {
        var eur = new Money(1_000, "EUR");
        var gbp = new Money(500, "GBP");

        var addError = Assert.Throws<CurrencyMismatchError>(() => eur.Add(gbp));
        var subtractError = Assert.Throws<CurrencyMismatchError>(() => eur.Subtract(gbp));
        var compareError = Assert.Throws<CurrencyMismatchError>(() => eur.CompareTo(gbp));

        Assert.Equal("money.cross_currency", addError.Code);
        Assert.Equal("money.cross_currency", subtractError.Code);
        Assert.Equal("money.cross_currency", compareError.Code);

        // Operands are untouched by the failed operations — proves there is
        // no partial mutation and, together with the absence of any
        // conversion operator below, that no implicit conversion occurred.
        Assert.Equal(1_000, eur.MinorUnits);
        Assert.Equal("EUR", eur.Currency);
        Assert.Equal(500, gbp.MinorUnits);
        Assert.Equal("GBP", gbp.Currency);
    }

    [Fact]
    public void R2_Money_RelationalOperatorsRaiseDomainErrorAcrossCurrencies()
    {
        var eur = new Money(1_000, "EUR");
        var gbp = new Money(500, "GBP");

        Assert.Throws<CurrencyMismatchError>(() => _ = eur > gbp);
        Assert.Throws<CurrencyMismatchError>(() => _ = eur < gbp);
        Assert.Throws<CurrencyMismatchError>(() => _ = eur >= gbp);
        Assert.Throws<CurrencyMismatchError>(() => _ = eur <= gbp);
    }

    [Fact]
    public void R2_Money_HasNoImplicitOrExplicitCurrencyConversionOperator()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        var conversionOperators = typeof(Money)
            .GetMethods(flags)
            .Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .ToArray();

        Assert.Empty(conversionOperators);
    }

    [Fact]
    public void Money_SameCurrencyArithmeticProducesTheExpectedTotalsAndStaysClosedOverTheSameCurrency()
    {
        var lineTotal = new Money(2_500, "EUR").Add(new Money(1_500, "EUR"));
        Assert.Equal(new Money(4_000, "EUR"), lineTotal);

        var remainder = new Money(4_000, "EUR").Subtract(new Money(1_000, "EUR"));
        Assert.Equal(new Money(3_000, "EUR"), remainder);

        var extended = new Money(500, "EUR").Multiply(new Quantity(3));
        Assert.Equal(new Money(1_500, "EUR"), extended);
    }

    [Fact]
    public void Money_ConstructorRejectsAMalformedCurrencyCode()
    {
        Assert.Throws<InvalidCurrencyCodeError>(() => new Money(100, "eur"));
        Assert.Throws<InvalidCurrencyCodeError>(() => new Money(100, "EU"));
        Assert.Throws<InvalidCurrencyCodeError>(() => new Money(100, "EURO"));
        Assert.Throws<InvalidCurrencyCodeError>(() => new Money(100, ""));
    }

    [Fact]
    public void Money_NegativeAmountsAreRepresentableForDiscountsAndReversals()
    {
        var discount = new Money(-500, "EUR");
        Assert.True(discount.IsNegative);
    }
}
