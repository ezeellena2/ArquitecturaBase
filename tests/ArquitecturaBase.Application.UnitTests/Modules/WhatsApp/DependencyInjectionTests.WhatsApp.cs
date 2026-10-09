using ArquitecturaBase.Application.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

/// <summary>
/// La parte del módulo WhatsApp de DependencyInjectionTests: registra el módulo como Program.cs, así las reglas de DI
/// también lo recorren. Lleva el namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la misma
/// clase.
/// </summary>
public sealed partial class DependencyInjectionTests
{
    [Fact]
    public void The_whatsapp_application_registration_contributes_services()
    {
        var services = new ServiceCollection().AddWhatsAppApplication();
        Assert.Contains(services, x => x.ServiceType.Namespace?.Contains(".Modules.WhatsApp.", StringComparison.Ordinal) == true);
    }
}
