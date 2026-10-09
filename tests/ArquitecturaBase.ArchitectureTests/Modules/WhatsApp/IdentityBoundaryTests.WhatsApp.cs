using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// La parte del módulo WhatsApp de IdentityBoundaryTests: la regla de oro, un mensaje nunca abre una sesión. Lleva el
/// namespace de la clase del núcleo, no el de su carpeta, para usar sus detectores.
/// </summary>
public sealed partial class IdentityBoundaryTests
{
    private const string WhatsAppServices = "ArquitecturaBase.Application.Modules.WhatsApp.Services.";

    [Fact]
    public void Whatsapp_services_never_open_a_session()
    {
        // La regla de oro de WhatsApp: un mensaje nunca abre una sesión.
        Assert.DoesNotContain(
            OwnersOf(nameof(ISignInService.SignInAsync)),
            owner => owner.StartsWith(WhatsAppServices, StringComparison.Ordinal));
    }

    [Fact]
    public void Inbound_processing_has_no_identity_or_secret_link_dependencies()
    {
        string[] owners = [WhatsAppServices + "WhatsAppInboundService", WhatsAppServices + "WhatsAppInboundPolicy", WhatsAppServices + "WhatsAppAccessReplyPolicy",
            WhatsAppServices + "WhatsAppWebhookPersistence"];
        var forbidden = ForbiddenInboundDependencies(TypeUses).ToArray();
        Assert.DoesNotContain(forbidden, use => owners.Contains(use.Owner, StringComparer.Ordinal));
        // A real IL control calls the forbidden account reader, so an empty result cannot silently pass.
        Assert.Contains(ForbiddenInboundDependencies(CallSites.TypeUses(typeof(IdentityBoundaryTests).Assembly)),
            use => use.Owner == typeof(InboundIdentityControl).FullName);
        Assert.Contains(TypeUses, use => use.Owner == AuthServices + "LoginLinkIssuer" && use.Type == typeof(ILoginLinkRepository).FullName);
    }

    private static IEnumerable<CallSites.TypeUse> ForbiddenInboundDependencies(IEnumerable<CallSites.TypeUse> uses) =>
        uses.Where(use => use.Type == typeof(IUserReader).FullName || use.Type == typeof(IUserRepository).FullName
            || use.Type == typeof(ILoginLinkRepository).FullName || use.Type == typeof(ISignInService).FullName
            || use.Type.StartsWith(AuthServices, StringComparison.Ordinal));
}

file static class InboundIdentityControl
{
    public static Task<ArquitecturaBase.Application.Models.Identity.UserAccount?> TouchIdentity(IUserReader users, Guid userId, CancellationToken cancellationToken) =>
        users.FindByIdAsync(userId, cancellationToken);
}
