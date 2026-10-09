using ArquitecturaBase.Application.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.ArchitectureTests;

public sealed partial class PermissionAuthorizationTests
{
    [Fact]
    public void The_whatsapp_permission_source_remains_composable()
    {
        var services = new ServiceCollection().AddWhatsAppApplication();
        Assert.Contains(services, x => x.ImplementationType?.Name == "WhatsAppPermissionCatalogSource");
    }
}
