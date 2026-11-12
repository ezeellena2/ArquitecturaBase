using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.ValueObjects;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
// DOC7-A: the dashboard groups in the zone the front builds the sheet's filter in, the profile's of whoever reads; then the business
// zone and then UTC. It is read on the server, never from the request.
public sealed class FormDashboardZonePolicyTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task The_zone_is_the_one_of_the_profile_of_whoever_reads()
    {
        var accounts = new InMemoryUserAccounts();
        var ana = await accounts.CreateAsync(Email.Create("ana.tablero@example.com").Value, null, false, "Ana", "es", Ct, "Europe/Madrid");

        var (zone, id) = await Policy(accounts, "America/Argentina/Buenos_Aires").FindAsync(ana.Id, Ct);

        Assert.Equal("Europe/Madrid", id);
        Assert.Equal(TimeSpan.FromHours(1), zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task Without_a_readable_profile_zone_it_is_the_business_one_and_without_it_utc()
    {
        var accounts = new InMemoryUserAccounts();
        var odd = await accounts.CreateAsync(Email.Create("zona.rara@example.com").Value, null, false, "Rara", "es", Ct, "Not/AZone");

        Assert.Equal("America/Argentina/Buenos_Aires", (await Policy(accounts, "America/Argentina/Buenos_Aires").FindAsync(odd.Id, Ct)).Id);
        Assert.Equal("America/Argentina/Buenos_Aires", (await Policy(accounts, "America/Argentina/Buenos_Aires").FindAsync(Guid.NewGuid(), Ct)).Id);
        var (zone, id) = await Policy(accounts, "Not/AZone").FindAsync(Guid.NewGuid(), Ct);
        Assert.Equal(("UTC", TimeZoneInfo.Utc), (id, zone));
    }

    [Fact]
    public async Task A_profile_zone_saved_by_its_windows_name_goes_by_its_iana_name_which_postgresql_reads()
    {
        var accounts = new InMemoryUserAccounts();
        var ana = await accounts.CreateAsync(Email.Create("zona.windows@example.com").Value, null, false, "Ana", "es", Ct, "Argentina Standard Time");

        var (zone, id) = await Policy(accounts, "UTC").FindAsync(ana.Id, Ct);

        Assert.True(TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windows));
        Assert.Equal("Argentina Standard Time", windows);
        Assert.NotEqual("Argentina Standard Time", id);
        Assert.Equal(TimeSpan.FromHours(-3), zone.GetUtcOffset(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)));
    }

    private static FormDashboardZonePolicy Policy(InMemoryUserAccounts accounts, string business) =>
        new(accounts, new FakeSystemSettingsReader { Presentation = SystemPresentationResponse.Defaults with { DefaultTimeZoneId = business } });
}
