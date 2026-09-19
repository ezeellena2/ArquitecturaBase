using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.UnitTests.Common;

public sealed class AggregateRootTests
{
    private sealed record SomethingHappened(string What) : IDomainEvent;

    private sealed class Basket : AggregateRoot
    {
        public void DoSomething(string what) => RaiseDomainEvent(new SomethingHappened(what));
    }

    [Fact]
    public void Raised_events_are_accumulated_in_order()
    {
        var basket = new Basket();

        basket.DoSomething("first");
        basket.DoSomething("second");

        Assert.Equal(
            [new SomethingHappened("first"), new SomethingHappened("second")],
            basket.GetDomainEvents());
    }

    [Fact]
    public void Clearing_removes_the_events()
    {
        var basket = new Basket();
        basket.DoSomething("first");

        basket.ClearDomainEvents();

        Assert.Empty(basket.GetDomainEvents());
    }
}
