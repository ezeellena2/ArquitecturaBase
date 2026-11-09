using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaRetryScheduleTests
{
    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 300)]
    [InlineData(5, 600)]
    [InlineData(6, 1800)]
    [InlineData(7, 3600)]
    [InlineData(40, 3600)]
    public void The_wait_grows_until_an_hour_and_stays_there(int failures, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), ArcaRetrySchedule.After(failures));
    }

    [Fact]
    public void Without_failures_there_is_no_wait()
    {
        Assert.Equal(TimeSpan.Zero, ArcaRetrySchedule.After(0));
    }
}
