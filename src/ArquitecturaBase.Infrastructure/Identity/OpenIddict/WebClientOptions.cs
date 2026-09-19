using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>URIs del cliente público "web" (el SPA), en Authentication:Clients:Web.</summary>
internal sealed class WebClientOptions
{
    public const string SectionName = "Authentication:Clients:Web";

    [MinLength(1)]
    public IList<Uri> RedirectUris { get; init; } = [];

    [MinLength(1)]
    public IList<Uri> PostLogoutRedirectUris { get; init; } = [];
}
