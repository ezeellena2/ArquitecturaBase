using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// H11: a file received by WhatsApp keeps retrying for a day; past the usual window it no longer holds back what it pairs with.
public sealed class WhatsAppAcquisitionRetryTests
{
    private static readonly DateTime First = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(5, 6)]
    [InlineData(63, 64)]
    [InlineData(64, 64)]
    public void The_count_keeps_growing_up_to_sixty_four(int failures, int expected) =>
        Assert.Equal(expected, WhatsAppAcquisitionRetry.Count(failures));

    [Theory]
    [InlineData(4, 1440, false)]
    [InlineData(5, 1439, false)]
    [InlineData(5, 1440, true)]
    public void It_gives_up_only_with_five_failures_and_a_day(int failures, int minutes, bool expected) =>
        Assert.Equal(expected, WhatsAppAcquisitionRetry.GivesUp(failures, First, First.AddMinutes(minutes)));

    [Theory]
    [InlineData(4, 15, false)]
    [InlineData(5, 14, false)]
    [InlineData(5, 15, true)]
    public void It_stops_holding_back_what_it_pairs_with_after_five_failures_and_fifteen_minutes(int failures, int minutes, bool expected) =>
        Assert.Equal(expected, WhatsAppAcquisitionRetry.StopsHolding(failures, First, First.AddMinutes(minutes)));

    [Fact]
    public void Without_a_first_failure_it_holds_back() => Assert.False(WhatsAppAcquisitionRetry.StopsHolding(5, null, First));
}
