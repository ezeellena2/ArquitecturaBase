using ArquitecturaBase.Domain.Delivery;

namespace ArquitecturaBase.Domain.UnitTests.Delivery;

public sealed class QueuedDeliveryTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_claimed_delivery_cannot_be_claimed_again()
    {
        var delivery = QueuedDelivery.Create("email", "message", "encrypted", Now, Now.AddMinutes(5));
        delivery.Claim(Now);
        Assert.Equal(Now, delivery.ClaimedAtUtc);
        Assert.Throws<InvalidOperationException>(() => delivery.Claim(Now.AddSeconds(1)));
    }

    [Fact]
    public void A_delivery_cannot_be_claimed_at_its_expiration()
    {
        var delivery = QueuedDelivery.Create("email", "message", "encrypted", Now, Now.AddMinutes(5));
        Assert.Throws<InvalidOperationException>(() => delivery.Claim(Now.AddMinutes(5)));
        Assert.Null(delivery.ClaimedAtUtc);
    }

    [Fact]
    public void Creation_requires_a_future_utc_expiration()
    {
        Assert.Throws<ArgumentException>(() => QueuedDelivery.Create("email", "message", "encrypted", Now, Now));
        Assert.Throws<ArgumentException>(() => QueuedDelivery.Create("email", "message", "encrypted", Now, DateTime.SpecifyKind(Now.AddMinutes(5), DateTimeKind.Unspecified)));
    }
}
