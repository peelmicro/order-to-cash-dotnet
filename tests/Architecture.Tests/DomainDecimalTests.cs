using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// CLAUDE.md — "decimal is likewise banned from domain arithmetic — Money is
/// long minor units, and decimal appears only at presentation boundaries",
/// and domain-model.md §2.1 M1 — "A decimal, floating-point or fixed-point
/// major-unit representation is never used". NetArchTest has no built-in
/// predicate for member/parameter types, so this rule is implemented
/// directly with reflection over every type whose namespace has a
/// *.Domain segment, unioned with every type in SharedKernel (see
/// DomainAssemblies.DomainNamespacePattern).
///
/// Two named tests rather than one widened test — see
/// progress/review_shared_kernel.md defect D1: a rule literally named
/// "decimal" that silently also rejected float/double would be its own
/// small trap for the next reader. Both share <see cref="FindTypeOffences"/>
/// so the two bans cannot drift apart on what counts as a field, property,
/// method, parameter, constructor parameter or conversion operator.
/// </summary>
public sealed partial class DomainDecimalTests
{
    private static readonly HashSet<Type> _decimalTypes = [typeof(decimal)];
    private static readonly HashSet<Type> _floatingPointTypes = [typeof(float), typeof(double)];

    /// <summary>
    /// Exactly one named, reviewed exception to the floating-point ban — a
    /// method PARAMETER (never a field, property, return type or
    /// constructor parameter, and never for <c>decimal</c>) that is a
    /// deliberate unvalidated-input boundary, not a domain representation:
    /// <c>Quantity.From(double value)</c> exists specifically so an
    /// unvalidated upstream number (a parsed EDI field, an inbound JSON
    /// value) can be rejected as fractional before it ever becomes a
    /// <see cref="OrderToCash.SharedKernel.Quantity"/> — see
    /// progress/review_shared_kernel.md: "the `From(double)` overload
    /// exists precisely to guard an unvalidated upstream number", and its
    /// explicit instruction not to re-touch <c>Quantity.cs</c> beyond D4.
    /// Adding it here, rather than exempting every method parameter from
    /// the floating-point ban, keeps the ban blanket everywhere else: a
    /// hypothetical <c>Money.Add(double x)</c> or any other undeclared
    /// float/double parameter on any domain type still fails.
    /// </summary>
    private static readonly HashSet<(string TypeFullName, string MethodName, string ParameterName)>
        _reviewedFloatingPointBoundaryParameters =
        [
            ("OrderToCash.SharedKernel.Quantity", "From", "value"),
        ];

    [Fact]
    public void NoDomainTypeHasADecimalFieldPropertyParameterOrReturnType()
    {
        var offences = FindOffencesAcrossDomainAssemblies(_decimalTypes, exemptParameters: null);

        Assert.True(
            offences.Count == 0,
            $"decimal must not appear in domain arithmetic. Offences: {string.Join("; ", offences)}");
    }

    /// <summary>
    /// CLAUDE.md's Money row: "Never a float, never decimal". Originally
    /// this ban existed only in prose and in MoneyTests.cs's own reflection
    /// helper — no architecture test enforced it, so it never fired for any
    /// domain type including Money itself (progress/review_shared_kernel.md
    /// D1, probe P2: a `double` accessor on `Money` left 31/31 + 11/11
    /// green).
    /// </summary>
    [Fact]
    public void NoDomainTypeHasAFloatingPointFieldPropertyParameterOrReturnType()
    {
        var offences = FindOffencesAcrossDomainAssemblies(_floatingPointTypes, _reviewedFloatingPointBoundaryParameters);

        Assert.True(
            offences.Count == 0,
            $"float/double must not appear in domain arithmetic. Offences: {string.Join("; ", offences)}");
    }

    private static List<string> FindOffencesAcrossDomainAssemblies(
        HashSet<Type> forbiddenTypes,
        HashSet<(string TypeFullName, string MethodName, string ParameterName)>? exemptParameters)
    {
        var offences = new List<string>();

        foreach (var assembly in DomainAssemblies.All)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Namespace is null || !DomainNamespaceRegex().IsMatch(type.Namespace))
                {
                    continue;
                }

                offences.AddRange(FindTypeOffences(type, forbiddenTypes, exemptParameters));
            }
        }

        return offences;
    }

    private static IEnumerable<string> FindTypeOffences(
        Type type,
        HashSet<Type> forbiddenTypes,
        HashSet<(string TypeFullName, string MethodName, string ParameterName)>? exemptParameters)
    {
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(flags))
        {
            if (forbiddenTypes.Contains(field.FieldType))
            {
                yield return $"{type.FullName}.{field.Name} (field: {field.FieldType.Name})";
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (forbiddenTypes.Contains(property.PropertyType))
            {
                yield return $"{type.FullName}.{property.Name} (property: {property.PropertyType.Name})";
            }
        }

        foreach (var method in type.GetMethods(flags))
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
                // is caught here. (The original version of this method
                // skipped every IsSpecialName method, which silently also
                // skipped every operator overload — found and fixed in the
                // same pass as D1, since it is the identical failure shape.)
                continue;
            }

            if (forbiddenTypes.Contains(method.ReturnType))
            {
                yield return $"{type.FullName}.{method.Name} (return type: {method.ReturnType.Name})";
            }

            foreach (var parameter in method.GetParameters())
            {
                if (forbiddenTypes.Contains(parameter.ParameterType)
                    && !IsExemptParameter(exemptParameters, type, method.Name, parameter.Name ?? string.Empty))
                {
                    yield return $"{type.FullName}.{method.Name}({parameter.Name}) (parameter: {parameter.ParameterType.Name})";
                }
            }
        }

        foreach (var ctor in type.GetConstructors(flags))
        {
            foreach (var parameter in ctor.GetParameters())
            {
                if (forbiddenTypes.Contains(parameter.ParameterType))
                {
                    yield return $"{type.FullName}..ctor({parameter.Name}) (parameter: {parameter.ParameterType.Name})";
                }
            }
        }
    }

    private static bool IsExemptParameter(
        HashSet<(string TypeFullName, string MethodName, string ParameterName)>? exemptParameters,
        Type type,
        string methodName,
        string parameterName)
    {
        return exemptParameters is not null
            && type.FullName is not null
            && exemptParameters.Contains((type.FullName, methodName, parameterName));
    }

    [GeneratedRegex(DomainAssemblies.DomainNamespacePattern)]
    private static partial Regex DomainNamespaceRegex();
}
