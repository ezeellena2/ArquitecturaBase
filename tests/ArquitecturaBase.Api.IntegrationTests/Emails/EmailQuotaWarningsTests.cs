using ArquitecturaBase.Infrastructure.Emails;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

/// <summary>
/// N7, sin Docker: los avisos del tope diario del correo salen una vez por día en cada copia, no uno por pedido. Cada aviso
/// cuenta su día por separado.
/// </summary>
public sealed class EmailQuotaWarningsTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Each_warning_goes_out_once_a_day_and_again_a_day_after_the_last_one()
    {
        var warnings = new EmailQuotaWarnings();

        Assert.True(warnings.TryWarnNear(Now));
        Assert.False(warnings.TryWarnNear(Now.AddHours(23)));
        Assert.True(warnings.TryWarnReached(Now.AddHours(1)));
        Assert.False(warnings.TryWarnReached(Now.AddHours(2)));
        Assert.True(warnings.TryWarnNear(Now.AddDays(1)));
        Assert.False(warnings.TryWarnNear(Now.AddDays(1).AddMinutes(1)));
    }

    [Fact]
    public void Of_many_requests_at_once_only_one_warns()
    {
        var warnings = new EmailQuotaWarnings();

        var warned = 0;
        Parallel.For(0, 64, _ =>
        {
            if (warnings.TryWarnNear(Now))
            {
                Interlocked.Increment(ref warned);
            }
        });

        Assert.Equal(1, warned);
    }
}
