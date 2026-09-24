using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

public sealed record FakeExternalLogin(string ProviderKey, string Email, string Name, bool EmailVerified);

/// <summary>
/// Simula la vuelta de Google: deja la misma cookie externa que dejaría el middleware de Google después de
/// /signin-google, con los claims que mapea (email_verified llega como "True"/"False") y el proveedor en
/// "LoginProvider".
/// </summary>
[ApiController]
[Route("test/external-login")]
public sealed class ExternalLoginTestController : ControllerBase
{
    [HttpPost]
    public IActionResult SignIn([FromBody] FakeExternalLogin login)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, login.ProviderKey),
                new Claim(ClaimTypes.Email, login.Email),
                new Claim(ClaimTypes.Name, login.Name),
                new Claim("email_verified", login.EmailVerified ? "True" : "False"),
            ],
            GoogleDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties();
        properties.Items["LoginProvider"] = GoogleDefaults.AuthenticationScheme;

        return SignIn(new ClaimsPrincipal(identity), properties, IdentityConstants.ExternalScheme);
    }
}
