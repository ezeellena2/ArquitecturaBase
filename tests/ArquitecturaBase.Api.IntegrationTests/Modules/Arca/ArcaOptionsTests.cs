using ArquitecturaBase.Infrastructure.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca;

public sealed class ArcaOptionsTests
{
    [Fact]
    public void Both_environments_with_https_addresses_an_issuer_and_a_retry_window_are_valid()
    {
        Assert.True(Options().IsValid());
    }

    [Fact]
    public void An_address_that_is_not_absolute_https_makes_the_options_invalid()
    {
        Assert.False((Options() with { Testing = Environment() with { Wsaa = new Uri("http://wsaahomo.afip.gov.ar/ws/services/LoginCms") } }).IsValid());
        Assert.False((Options() with { Testing = Environment() with { Wsaa = null } }).IsValid());
        Assert.False((Options() with { Testing = Environment() with { Wsaa = new Uri("/ws/services/LoginCms", UriKind.Relative) } }).IsValid());

        var services = new Dictionary<string, Uri>(StringComparer.Ordinal) { ["wsfe"] = new Uri("http://wswhomo.afip.gov.ar/wsfev1/service.asmx") };
        Assert.False((Options() with { Production = Environment() with { Services = services } }).IsValid());
    }

    [Fact]
    public void An_environment_needs_its_certificate_issuer_and_its_ticket_retry_window()
    {
        Assert.False((Options() with { Testing = Environment() with { IssuerCommonName = " " } }).IsValid());
        Assert.False((Options() with { Production = Environment() with { TicketRetryWindow = TimeSpan.Zero } }).IsValid());
    }

    [Fact]
    public void Timeouts_sizes_and_ticket_margins_must_be_positive()
    {
        Assert.False((Options() with { RequestTimeout = TimeSpan.Zero }).IsValid());
        Assert.False((Options() with { MaxResponseBytes = 0 }).IsValid());
        Assert.False((Options() with { TicketMargin = TimeSpan.Zero }).IsValid());
        Assert.False((Options() with { TicketLease = TimeSpan.Zero }).IsValid());
    }

    [Fact]
    public void A_key_ring_needs_an_installation_an_active_key_and_external_256_bit_keys()
    {
        Assert.True(Ring().IsValid());
        Assert.False((Ring() with { InstallationId = "" }).IsValid());
        Assert.False((Ring() with { ActiveKeyId = "missing" }).IsValid());
        Assert.False((Ring() with { Keys = new(StringComparer.Ordinal) { ["one"] = Convert.ToBase64String(new byte[16]) } }).IsValid());
        Assert.False((Ring() with { Keys = new(StringComparer.Ordinal) { ["one key"] = Convert.ToBase64String(new byte[32]) } }).IsValid());
    }

    [Fact]
    public void A_key_ring_never_prints_its_keys()
    {
        var ring = Ring();
        Assert.DoesNotContain(ring.Keys["one"], ring.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("arca-installation", ring.ToString(), StringComparison.Ordinal);
    }

    private static ArcaOptions Options() => new() { Testing = Environment(), Production = Environment() };

    private static ArcaEnvironmentOptions Environment() => new()
    {
        Wsaa = new Uri("https://wsaahomo.afip.gov.ar/ws/services/LoginCms"),
        Services = new(StringComparer.Ordinal) { ["wsfe"] = new Uri("https://wswhomo.afip.gov.ar/wsfev1/service.asmx") },
        IssuerCommonName = "Computadores Test",
        TicketRetryWindow = TimeSpan.FromMinutes(10),
    };

    private static ArcaSecretProtectionOptions Ring() => new()
    {
        InstallationId = "arca-installation",
        ActiveKeyId = "one",
        Keys = new(StringComparer.Ordinal) { ["one"] = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()) },
    };
}
