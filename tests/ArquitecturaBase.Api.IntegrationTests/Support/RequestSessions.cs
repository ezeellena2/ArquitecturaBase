using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Para llamar un caso de uso directo, sin HTTP, como si lo pidiera una cuenta: le pone una petición con su "sub" al
/// accessor, que la guarda en un AsyncLocal, así vale sólo para el flujo que la llama. Lo necesitan los casos de uso que
/// deciden con los permisos de quien pide (nadie da lo que no tiene, N1).
/// </summary>
internal static class RequestSessions
{
    public static void ActAs(this IServiceProvider services, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", userId.ToString("D", CultureInfo.InvariantCulture))], "Test")),
        };
    }

    /// <summary>El id del administrador del seed, que entra por código si todavía no tenía cuenta.</summary>
    public static async Task<Guid> SignInSeedAdminAsync(ApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);

        return (await me.ReadJsonAsync()).GetProperty("id").GetGuid();
    }
}
