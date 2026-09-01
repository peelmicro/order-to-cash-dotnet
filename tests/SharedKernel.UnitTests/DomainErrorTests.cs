using Xunit;

namespace OrderToCash.SharedKernel.UnitTests;

/// <summary>CLAUDE.md — "Domain errors extend DomainError and carry a stable Code." Not R-numbered; general coverage.</summary>
public sealed class DomainErrorTests
{
    private sealed class TestDomainError(string code, string message) : DomainError(code, message);

    [Fact]
    public void DomainError_CarriesTheSuppliedCodeAndMessage()
    {
        var error = new TestDomainError("test.code", "something was refused");

        Assert.Equal("test.code", error.Code);
        Assert.Equal("something was refused", error.Message);
        Assert.IsAssignableFrom<Exception>(error);
    }

    [Fact]
    public void DomainError_RejectsAnEmptyOrWhitespaceCode()
    {
        Assert.Throws<ArgumentException>(() => new TestDomainError("", "message"));
        Assert.Throws<ArgumentException>(() => new TestDomainError("   ", "message"));
    }
}
