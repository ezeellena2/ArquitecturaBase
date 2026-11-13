using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// H10, sin Docker: el tope diario por dirección de los códigos con el registro abierto. Cada dirección tiene su ventana de un día, en la
/// memoria de la copia, y una IPv6 cuenta por su /64; la que se llena responde el largo de la ventana como espera.
/// </summary>
public sealed class WhatsAppCodeAddressLimiterTests
{
    [Fact]
    public void An_address_takes_its_daily_places_and_then_waits_without_touching_another()
    {
        using var limiter = new WhatsAppCodeAddressLimiter(Options.Create(new WhatsAppLoginOptions { DailySignInCodeLimitPerAddress = 2 }));

        Assert.Null(limiter.TryTake("203.0.113.10"));
        Assert.Null(limiter.TryTake("203.0.113.10"));
        var retryAfter = limiter.TryTake("203.0.113.10");
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromHours(23), TimeSpan.FromDays(1));
        Assert.Null(limiter.TryTake("203.0.113.11"));
    }

    [Theory]
    [InlineData("2001:db8:1:2::10", "2001:db8:1:2::ffff", true)]
    [InlineData("2001:db8:1:2::10", "2001:db8:1:3::10", false)]
    [InlineData("::ffff:203.0.113.10", "203.0.113.10", true)]
    public void An_ipv6_address_counts_by_its_network_of_64(string first, string second, bool shared)
    {
        using var limiter = new WhatsAppCodeAddressLimiter(Options.Create(new WhatsAppLoginOptions { DailySignInCodeLimitPerAddress = 1 }));

        Assert.Null(limiter.TryTake(first));

        Assert.Equal(shared, limiter.TryTake(second) is not null);
    }
}
