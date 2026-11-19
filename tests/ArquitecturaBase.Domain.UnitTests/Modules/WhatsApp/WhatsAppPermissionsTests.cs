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

    /// <summary>Se responde dentro del chat, que pide «Ver chats» (2026-11-19).</summary>
    [Fact]
    public void Sending_includes_seeing_the_chats()
    {
        Assert.Equal(
            [(WhatsAppPermissions.Send, WhatsAppPermissions.Read)],
            WhatsAppPermissions.Includes.SelectMany(pair => pair.Value.Select(included => (pair.Key, included))));
    }
}
