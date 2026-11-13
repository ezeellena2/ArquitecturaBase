using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Review of task 9 of the documents: a question that went as a list says the label of its button, queued and once it went out, and what
// the assistant sent for a file the person sent says so, with its body (which may carry what was read) only with formrecords.read. Any
// other message carries neither.
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private const string ChooseForm = "Elegir formulario";
    private const string Unrecognized = "No reconocí este documento. ¿Qué querés cargar con él?";
    private const string Summary = "Recibí una visita a Sucursal Norte. ¿La cargo?";
    private static readonly string[] ListTitles = ["Comprobantes de transfe…", "Pagos a proveedores del…", "Rendición de gastos de…", "Facturas de servicios…", "Otro"];

    [Fact]
    public async Task A_question_that_went_as_a_list_says_the_label_of_its_button_queued_and_once_it_went_out()
    {
        var host = new Host();
        host.Reader.Audio = new(Guid.NewGuid(), WhatsAppMessageDirection.Outbound, WhatsAppMessageKind.Interactive, Unrecognized, Now, 0, null, null, null,
            Origin: WhatsAppMessageOrigin.Assistant) { QuickReplyTitlesJson = JsonSerializer.Serialize(ListTitles), ListButton = ChooseForm };
        host.Reader.Outgoing = new(Guid.NewGuid(), Guid.NewGuid(), null, Unrecognized, Now, WhatsAppOutgoingMessageState.Pending, null, WhatsAppOutgoingMessageKind.List,
            Origin: WhatsAppMessageOrigin.Assistant)
        {
            QuickRepliesJson = JsonSerializer.Serialize(ListTitles.Select((title, index) => new WhatsAppOutgoingQuickReply("FORM_DOCUMENT:" + index, title, 0))),
            ListButton = ChooseForm,
        };

        var thread = (await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value;

        var sent = Assert.Single(thread.Messages);
        var queued = Assert.Single(thread.Outgoing);
        Assert.Equal((ChooseForm, ChooseForm), (sent.ListButton, queued.ListButton));
        Assert.Equal(ListTitles, sent.QuickReplies);
        Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies, queued.Kind);
        Assert.Equal(ListTitles, queued.QuickReplies);
        Assert.Contains("\"listButton\":\"Elegir formulario\"", JsonSerializer.Serialize(sent, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Contains("\"listButton\":\"Elegir formulario\"", JsonSerializer.Serialize(queued, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.DoesNotContain("loadsFile", JsonSerializer.Serialize(thread, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task What_was_sent_for_a_file_says_so_and_its_body_needs_formrecords_read(bool canReadRecords)
    {
        var host = new Host();
        host.Source.Allowed = canReadRecords;
        host.Reader.Audio = new(Guid.NewGuid(), WhatsAppMessageDirection.Outbound, WhatsAppMessageKind.Flow, Summary, Now, 0, null, null, null,
            Origin: WhatsAppMessageOrigin.Assistant) { LoadsFile = true };
        host.Reader.Outgoing = new(Guid.NewGuid(), Guid.NewGuid(), null, Summary, Now, WhatsAppOutgoingMessageState.Pending, null, WhatsAppOutgoingMessageKind.Flow,
            Origin: WhatsAppMessageOrigin.Assistant) { LoadsFile = true };

        var thread = (await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value;

        var sent = Assert.Single(thread.Messages);
        var queued = Assert.Single(thread.Outgoing);
        Assert.Equal((true, true), (sent.LoadsFile, queued.LoadsFile));
        Assert.Equal(canReadRecords ? Summary : null, sent.Body);
        Assert.Equal(canReadRecords ? Summary : null, queued.Body);
        Assert.Contains("\"loadsFile\":true", JsonSerializer.Serialize(sent, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Contains("\"loadsFile\":true", JsonSerializer.Serialize(queued, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.DoesNotContain("listButton", JsonSerializer.Serialize(thread, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    // Buttons, a form sent for no file and any other message keep their shape: no list button and no mark.
    [Fact]
    public async Task Buttons_and_any_other_message_carry_neither_a_list_button_nor_the_mark_of_a_file()
    {
        var host = new Host();
        host.Reader.Audio = new(Guid.NewGuid(), WhatsAppMessageDirection.Outbound, WhatsAppMessageKind.Interactive, "¿Qué es este documento?", Now, 0, null, null, null,
            Origin: WhatsAppMessageOrigin.Assistant) { QuickReplyTitlesJson = "[\"Pagos\",\"Gastos\",\"Otro\"]" };
        host.Reader.Outgoing = new(Guid.NewGuid(), Guid.NewGuid(), null, Summary, Now, WhatsAppOutgoingMessageState.Pending, null, WhatsAppOutgoingMessageKind.Flow,
            Origin: WhatsAppMessageOrigin.Assistant);

        var thread = (await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value;

        Assert.Equal((null, false, null, false), (thread.Messages[0].ListButton, thread.Messages[0].LoadsFile, thread.Outgoing[0].ListButton, thread.Outgoing[0].LoadsFile));
        var json = JsonSerializer.Serialize(thread, JsonSerializerOptions.Web);
        Assert.DoesNotContain("listButton", json, StringComparison.Ordinal);
        Assert.DoesNotContain("loadsFile", json, StringComparison.Ordinal);
    }
}
