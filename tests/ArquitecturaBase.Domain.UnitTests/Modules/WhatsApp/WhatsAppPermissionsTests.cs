using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppPermissionsTests
{
    [Fact]
    public void The_module_catalog_exposes_the_distinct_permission_to_configure_the_assistant()
    {
        Assert.Contains("whatsapp.configureassistant", WhatsAppPermissions.All);
        Assert.Equal(WhatsAppPermissions.All.Count, WhatsAppPermissions.All.Distinct(StringComparer.Ordinal).Count());
    }
}
