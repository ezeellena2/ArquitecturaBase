using System.Globalization;
using System.Security.Claims;
using ArquitecturaBase.Application.Abstractions.Identity;

namespace ArquitecturaBase.Api.Services;

/// <summary>Usuario de la petición, leído de los claims: "sub" (tokens OIDC) o NameIdentifier (cookie).</summary>
internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            var value = user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);

            return Guid.TryParse(value, CultureInfo.InvariantCulture, out var userId) ? userId : null;
        }
    }

    public bool IsAuthenticated => httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
