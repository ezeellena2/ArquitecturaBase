using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

/// <summary>El origen público sale de Authentication:Issuer (sección 14 del spec del ingreso con WhatsApp).</summary>
public sealed class PublicOriginTests
{
    [Theory]
    [InlineData("https://localhost:5173/", "https://localhost:5173/")]
    [InlineData("https://app.test", "https://app.test/")]
    [InlineData("https://app.test/base", "https://app.test/base/")]
    [InlineData("https://app.test/base/", "https://app.test/base/")]
    public void Origin_is_the_issuer_ending_in_a_slash(string issuer, string expected)
    {
        var origin = Create(issuer);

        Assert.Equal(new Uri(expected), origin.Value);
        Assert.EndsWith("/", origin.Value!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Without_an_issuer_there_is_no_origin(string? issuer)
    {
        Assert.Null(Create(issuer).Value);
    }

    private static PublicOrigin Create(string? issuer) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Issuer"] = issuer })
            .Build());
}
