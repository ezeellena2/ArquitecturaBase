using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class ValueObjectTests
{
    private sealed class Money(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;

        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void Value_objects_with_the_same_components_are_equal()
    {
        var first = new Money(10, "ARS");
        var second = new Money(10, "ARS");

        var equalByOperator = first == second;

        Assert.Equal(first, second);
        Assert.True(equalByOperator);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Value_objects_with_different_components_are_not_equal()
    {
        Assert.NotEqual(new Money(10, "ARS"), new Money(10, "USD"));
    }
}
