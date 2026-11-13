using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// VC1-A (task 29): the footer of the Flow of a load, «Por qué lo propuso» of the notice of the loads and each load as a reference of its own.
public sealed class WhatsAppSpreadsheetLoadTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Only_a_pending_flow_of_the_assistant_takes_a_footer_once_within_metas_limit()
    {
        var flow = WhatsAppOutgoingMessage.CreateAssistantFlow(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "1 de 3 · $ 95.000", Guid.NewGuid(), Now, 1, "5493410000000", 1);
        var text = WhatsAppOutgoingMessage.CreateAssistantText(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Recibí una planilla.", Now, 1, "5493410000000", 1);

        Assert.False(flow.AttachFooter(new string('a', 61)));
        Assert.False(flow.AttachFooter("Cobranzas\n· prellenado"));
        Assert.True(flow.AttachFooter("Cobranzas · prellenado"));
        Assert.False(flow.AttachFooter("Gastos · prellenado"));
        Assert.False(text.AttachFooter("Cobranzas · prellenado"));
        Assert.False(flow.ExplainSpreadsheetRows("{}"));
        Assert.False(text.ExplainSpreadsheetRows("[]"));
        Assert.True(text.ExplainSpreadsheetRows("""{"sent":[]}"""));
        Assert.Equal(("Cobranzas · prellenado", """{"sent":[]}"""), (flow.Footer, text.SpreadsheetRowsJson));
    }

    [Fact]
    public void A_load_is_a_reference_of_its_own_that_carries_the_spreadsheet_of_the_first_one()
    {
        var first = WhatsAppAssistantRequest.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), WhatsAppAssistantRequestKind.Automatic,
            1, 1, "{\"channelId\":\"whatsapp-assistant\"}", Now, Now.AddDays(1));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateLoad(first, Guid.NewGuid(), 1, Now));
        var sheet = Guid.NewGuid();
        Assert.True(first.CarryAttachment(sheet));

        var load = WhatsAppAssistantRequest.CreateLoad(first, Guid.NewGuid(), WhatsAppAssistantRequest.MaxLoads, Now);

        Assert.Equal((sheet, 3, first.RequestId, first.SourceMessageId), (load.AttachmentMessageId, load.ActionOrdinal, load.OperationRequestId, load.SourceMessageId));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateLoad(first, Guid.NewGuid(), 4, Now));
        Assert.Throws<ArgumentException>(() => WhatsAppAssistantRequest.CreateLoad(load, Guid.NewGuid(), 1, Now));
    }
}
