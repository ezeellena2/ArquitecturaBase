using ArquitecturaBase.Infrastructure.Modules.Arca.Registry;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Registry;

/// <summary>El cupo del padrón de un tercero, solo y con un reloj controlado: no necesita Docker.</summary>
public sealed class ArcaRegistryQuotaTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 11, 18, 0, 0, TimeSpan.Zero));

    [Fact]
    public void A_person_looks_up_up_to_the_quota_of_new_cuits_and_repeating_one_does_not_count()
    {
        var quota = Quota(2);
        var person = Guid.NewGuid();

        Assert.True(quota.Admit(person, "20000000028"));
        Assert.True(quota.Admit(person, "20000000036"));
        Assert.True(quota.Admit(person, "20000000028"));
        Assert.False(quota.Admit(person, "20000000044"));
    }

    [Fact]
    public void A_cuit_stops_counting_a_minute_after_it_was_last_looked_up()
    {
        var quota = Quota(1);
        var person = Guid.NewGuid();
        Assert.True(quota.Admit(person, "20000000028"));

        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.True(quota.Admit(person, "20000000028"));
        clock.Advance(TimeSpan.FromSeconds(40));
        Assert.False(quota.Admit(person, "20000000036"));

        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.True(quota.Admit(person, "20000000036"));
    }

    [Fact]
    public void Each_person_has_their_own_quota_and_what_runs_without_one_has_none()
    {
        var quota = Quota(1);
        var person = Guid.NewGuid();
        Assert.True(quota.Admit(person, "20000000028"));

        Assert.False(quota.Admit(person, "20000000036"));
        Assert.True(quota.Admit(Guid.NewGuid(), "20000000036"));
        Assert.True(quota.Admit(null, "20000000036"));
        Assert.True(quota.Admit(null, "20000000044"));
    }

    private ArcaRegistryQuota Quota(int lookupsPerMinute) =>
        new(Options.Create(new ArcaRegistryOptions { LookupsPerMinute = lookupsPerMinute }), clock);
}
