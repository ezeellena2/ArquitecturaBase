namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    [Fact]
    public async Task Sent_and_pending_assistant_explanations_use_the_same_captured_private_presentation()
    {
        var host=new Host();host.Reader.AssistantRow=host.Presentation.Row;host.Reader.TitlesJson="[\"Visita\",\"Mantenimiento\"]";
        var observed=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),host.Presentation.Context.Operator.Id,Ct);
        var sent=Assert.Single(observed.Value.Messages);var pending=Assert.Single(observed.Value.Outgoing);
        Assert.NotNull(sent.Assistant);Assert.NotNull(pending.Assistant);
        Assert.Equal("Contó una visita.",sent.Assistant.Reason);Assert.Equal(sent.Assistant,pending.Assistant);
        Assert.Equal(new[]{"Visita","Mantenimiento"},sent.QuickReplies);
    }
    [Fact]
    public async Task Losing_record_read_retires_assistant_explanations_after_reload()
    {
        var host=new Host();host.Reader.AssistantRow=host.Presentation.Row;
        var before=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),host.Presentation.Context.Operator.Id,Ct);
        Assert.NotNull(Assert.Single(before.Value.Messages).Assistant);
        host.Source.Allowed=false;
        var after=await host.Service.GetThreadAsync(new(host.Reader.Contact.Id),host.Presentation.Context.Operator.Id,Ct);
        Assert.Null(Assert.Single(after.Value.Messages).Assistant);Assert.Null(Assert.Single(after.Value.Outgoing).Assistant);
    }
    [Fact]
    public async Task Assistant_list_preview_is_generic_even_when_the_original_body_is_available()
    {
        var host=new Host();host.Reader.AssistantPreview=true;
        var result=await host.Service.ListAsync(new(),host.Presentation.Context.Operator.Id,Ct);
        Assert.Equal("Mensaje del asistente",Assert.Single(result.Value.Page.Items).Preview);
    }
}
