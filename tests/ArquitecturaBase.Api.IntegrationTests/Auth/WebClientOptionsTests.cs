using System.ComponentModel.DataAnnotations;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Sin al menos una redirect URI y una post-logout redirect URI, el cliente "web" se siembra incompleto y el
/// logout del SPA falla recién cuando alguien lo usa. Sin Docker: valida las data annotations directamente.
/// </summary>
public sealed class WebClientOptionsTests
{
    [Fact]
    public void Options_without_post_logout_redirect_uris_are_rejected()
    {
        var options = new WebClientOptions
        {
            RedirectUris = [new Uri("https://localhost:5173/callback")],
            PostLogoutRedirectUris = [],
        };

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }

    [Fact]
    public void Options_with_a_redirect_and_a_post_logout_redirect_uri_are_valid()
    {
        var options = new WebClientOptions
        {
            RedirectUris = [new Uri("https://localhost:5173/callback")],
            PostLogoutRedirectUris = [new Uri("https://localhost:5173/login")],
        };

        Assert.True(Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true));
    }
}
