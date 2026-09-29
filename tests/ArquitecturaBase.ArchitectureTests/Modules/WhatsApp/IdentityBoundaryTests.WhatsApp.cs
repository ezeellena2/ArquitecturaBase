using ArquitecturaBase.Application.Interfaces.Integrations.Identity;

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
    public void Whatsapp_services_only_check_the_lockout()
    {
        // La regla de oro de WhatsApp por el lado del contrato: el bot recibe ISignInService solo para mirar el bloqueo. No
        // abre una sesión, no suma ni pone en cero intentos fallidos, no cierra sesiones ni toca la cookie de Google.
        var calls = SignInCallsFrom(WhatsAppServices);

        // Casos de control: el detector ve al bot mirando el bloqueo, y la misma regla sobre los servicios del ingreso
        // encuentra lo que acá estaría prohibido. Si dejara de ver cualquiera de los dos, la regla pasaría en silencio.
        Assert.Contains(calls, call => call.Method == nameof(ISignInService.IsLockedOutAsync));
        Assert.Contains(
            BeyondTheLockout(SignInCallsFrom(AuthServices)),
            call => call.Method == nameof(ISignInService.SignInAsync));

        Assert.Empty(BeyondTheLockout(calls).Select(call => call.Owner + "." + call.Method));
    }
}
