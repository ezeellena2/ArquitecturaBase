using ArquitecturaBase.Application.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

/// <summary>
/// La parte del módulo WhatsApp de ApplicationHelpersTests: registra el módulo como Program.cs, así la convención de
/// helpers también mira los suyos. Lleva el namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de
/// la misma clase.
/// </summary>
public sealed partial class ApplicationHelpersTests
{
    [Fact]
    public void The_whatsapp_helpers_are_scoped()
    {
        var services = new ServiceCollection().AddWhatsAppApplication();
        var helpers = services.Where(x => x.ImplementationType?.Namespace?.StartsWith("ArquitecturaBase.Application.Modules.WhatsApp.Services", StringComparison.Ordinal) == true);
        Assert.NotEmpty(helpers);
        Assert.All(helpers, x => Assert.Equal(ServiceLifetime.Scoped, x.Lifetime));
    }
}
