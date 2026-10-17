using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantMessageTests
{
    private static readonly DateTime Now=new(2026,10,17,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void An_automatic_message_preserves_its_explicit_origin_and_readonly_button_titles()
    {
        var message=WhatsAppMessage.Outbound(Guid.NewGuid(),"wamid.assistant",WhatsAppMessageKind.Interactive,"Elegí un formulario.",Now);
        message.AttributeAssistant(["Visita","Gasto"]);
        Assert.Equal(WhatsAppMessageOrigin.Assistant,message.Origin);Assert.Null(message.OperatorUserId);
        Assert.Equal(WhatsAppMessagePurpose.Business,message.Purpose);
        Assert.Equal(new[]{"Visita","Gasto"},JsonSerializer.Deserialize<string[]>(message.QuickReplyTitlesJson!));
    }
    [Fact]
    public void An_inbound_message_cannot_be_reclassified_as_an_automatic_outbound()
    {
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.in",WhatsAppMessageKind.Text,"Visita",null,Now);
        Assert.Throws<InvalidOperationException>(()=>message.AttributeAssistant());
    }
    [Fact]
    public void A_real_operator_is_never_replaced_with_an_automatic_author()
    {
        var message=WhatsAppMessage.Outbound(Guid.NewGuid(),"wamid.operator",WhatsAppMessageKind.Text,"Mensaje",Now);
        var actor=Guid.NewGuid();message.AttributeOperator(actor);
        Assert.Throws<InvalidOperationException>(()=>message.AttributeAssistant());Assert.Equal(actor,message.OperatorUserId);
    }
    [Fact]
    public void Interpretation_processing_preserves_access_processing_and_its_first_durable_marker()
    {
        var message=WhatsAppMessage.Inbound(Guid.NewGuid(),"wamid.marker",WhatsAppMessageKind.Text,"Visita",null,Now);
        message.MarkProcessed(Now);
        Assert.True(message.MarkAssistantProcessed(Now.AddSeconds(1)));Assert.False(message.MarkAssistantProcessed(Now.AddSeconds(2)));
        Assert.Equal(Now,message.ProcessedAtUtc);Assert.Equal(Now.AddSeconds(1),message.AssistantProcessedAtUtc);
    }
    [Fact]
    public void An_outbound_message_cannot_acquire_an_inbound_interpretation_marker()
    {
        var message=WhatsAppMessage.Outbound(Guid.NewGuid(),"wamid.out-marker",WhatsAppMessageKind.Text,"Mensaje",Now);
        Assert.Throws<InvalidOperationException>(()=>message.MarkAssistantProcessed(Now));
    }
}
