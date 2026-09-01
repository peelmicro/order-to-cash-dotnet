using NetArchTest.Rules;
using Xunit;

namespace OrderToCash.Architecture.Tests;

/// <summary>
/// CLAUDE.md — "Domain purity": no type in any *.Domain namespace, in any
/// service, may depend on EF Core, Kafka, NATS, MongoDB, ASP.NET Core or
/// System.Text.Json. Each rule below is its own named test so a violation
/// in any one service points at the exact forbidden dependency.
/// </summary>
public sealed class DomainPurityTests
{
    [Fact]
    public void DomainMustNotDependOnEntityFrameworkCore()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on Microsoft.EntityFrameworkCore. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainMustNotDependOnConfluentKafka()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("Confluent.Kafka")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on Confluent.Kafka. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainMustNotDependOnNats()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("NATS")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on any NATS.* type. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainMustNotDependOnMongoDb()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("MongoDB")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on any MongoDB.* type. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainMustNotDependOnAspNetCore()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on Microsoft.AspNetCore.*. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void DomainMustNotDependOnSystemTextJson()
    {
        var result = Types.InAssemblies(DomainAssemblies.All)
            .That().ResideInNamespaceMatching(DomainAssemblies.DomainNamespacePattern)
            .ShouldNot().HaveDependencyOn("System.Text.Json")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"Domain types must not depend on System.Text.Json. Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
