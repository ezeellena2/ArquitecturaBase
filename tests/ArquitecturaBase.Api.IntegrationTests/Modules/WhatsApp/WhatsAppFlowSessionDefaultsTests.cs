using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowSessionDefaultsTests
{
    [Fact]
    public void An_insert_from_the_previous_binary_keeps_the_known_conversation_origin()
    {
        var model = new ModelBuilder();
        model.ApplyConfiguration(new WhatsAppFlowSessionConfiguration());
        var entity = model.Model.FindEntityType(typeof(WhatsAppFlowSession))!;
        var configured = entity.FindProperty(nameof(WhatsAppFlowSession.Origin))!
            .FindAnnotation(RelationalAnnotationNames.DefaultValue);
        Assert.NotNull(configured);
        Assert.Equal(WhatsAppMessageOrigin.Conversation, configured.Value);
    }

    [Fact]
    public void An_insert_from_the_previous_binary_does_not_invent_private_assistant_content()
    {
        var model = new ModelBuilder();
        model.ApplyConfiguration(new WhatsAppFlowSessionConfiguration());
        var entity = model.Model.FindEntityType(typeof(WhatsAppFlowSession))!;
        var configured = entity.FindProperty(nameof(WhatsAppFlowSession.ProtectedAssistantContent))!
            .FindAnnotation(RelationalAnnotationNames.DefaultValue);
        Assert.NotNull(configured);
        Assert.Empty(Assert.IsType<byte[]>(configured.Value));
    }
}
