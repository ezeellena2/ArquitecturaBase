using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Review of task 9: «Por qué lo propuso» and «Por qué preguntó» under a received document name the forms in the language of whoever reads
// the chat, from the form ids kept with what was sent and resolved when the chat is read; without the form among those the chat can name
// now, the name it was sent with. How many data were read with doubts goes with the chat, and the values read only with formrecords.read.
public sealed class WhatsAppReceivedDocumentWhyTests
{
    private static readonly WhatsAppReceivedDocumentMessageRow Pdf = new("comprobante.pdf", "application/pdf", "whatsapp/documents/x.pdf", 2,
        WhatsAppDocumentState.Read, null);

    // A prefilled form sent for the document, with what was read (one datum with doubts, another not read) sealed in its session.
    private static WhatsAppAssistantPresentationFixture Sent()
    {
        var host = new WhatsAppAssistantPresentationFixture();
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Assistant, null, "Leyó el documento «comprobante.pdf» que mandó la persona.", null,
            [new(host.FieldId, "decimal", "150000")], [host.FieldId]) { Document = Guid.NewGuid() };
        host.Protector.Content = new(1, origin, [new(host.FieldId, "Monto", "150.000")])
        {
            Server = new(null, [new(host.FieldId, StructuredDocumentMarkKind.Doubtful), new(Guid.NewGuid(), StructuredDocumentMarkKind.NotRead)]),
        };
        return host;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Why_it_proposed_names_the_form_in_the_readers_language_and_shows_the_values_only_with_formrecords_read(bool canReadRecords)
    {
        var host = Sent();
        var definition = Guid.NewGuid();
        var why = new WhatsAppReceivedDocumentWhyRow(WhatsAppOutgoingMessageKind.Flow, null, "{\"es\":\"Pagos\",\"en\":\"Payments\"}", "es", host.Row, null)
            { DefinitionId = definition };

        var view = WhatsAppReceivedDocumentPresentationPolicy.View(WhatsAppMessageKind.ReceivedDocument, Pdf with { Why = why }, host.Policy, canReadRecords,
            new Dictionary<Guid, string> { [definition] = "Customer payments" }, "en")!;

        Assert.Equal(("proposed", (int?)1), (view.Why!.Kind, view.Why.Doubtful));
        Assert.Equal(["Customer payments"], view.Why.Forms);
        if (canReadRecords) Assert.Equal(("Monto", "150.000"), (Assert.Single(view.Why.Fields!).Label, view.Why.Fields![0].DisplayValue));
        else Assert.Null(view.Why.Fields);
    }

    // A form with a location question opens with its location request, which is what the reference keeps: the why is the same.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Why_it_proposed_also_shows_when_the_form_opened_with_its_location_request(bool canReadRecords)
    {
        var host = Sent();
        var definition = Guid.NewGuid();
        var why = new WhatsAppReceivedDocumentWhyRow(WhatsAppOutgoingMessageKind.LocationRequest, null, "{\"es\":\"Pagos\"}", "es", host.Row, null)
            { DefinitionId = definition };

        var view = WhatsAppReceivedDocumentPresentationPolicy.View(WhatsAppMessageKind.ReceivedDocument, Pdf with { Why = why }, host.Policy, canReadRecords,
            new Dictionary<Guid, string> { [definition] = "Pagos de clientes" }, "es")!;

        Assert.Equal(("proposed", (int?)1), (view.Why!.Kind, view.Why.Doubtful));
        Assert.Equal(["Pagos de clientes"], view.Why.Forms);
        Assert.Equal(canReadRecords, view.Why.Fields is { Count: 1 });
    }

    [Theory]
    [InlineData("en", "Payments")]
    [InlineData("es", "Pagos")]
    public void Why_it_proposed_falls_back_on_the_names_the_form_was_sent_with(string culture, string name)
    {
        var host = Sent();
        var why = new WhatsAppReceivedDocumentWhyRow(WhatsAppOutgoingMessageKind.Flow, null, "{\"es\":\"Pagos\",\"en\":\"Payments\"}", "es", host.Row, null)
            { DefinitionId = Guid.NewGuid() };

        var view = WhatsAppReceivedDocumentPresentationPolicy.View(WhatsAppMessageKind.ReceivedDocument, Pdf with { Why = why }, host.Policy, true,
            new Dictionary<Guid, string>(), culture)!;

        Assert.Equal([name], view.Why!.Forms);
    }

    [Fact]
    public void Why_it_asked_names_the_forms_offered_and_the_one_chosen_in_the_readers_language()
    {
        var host = new WhatsAppAssistantPresentationFixture();
        var (requestId, proposalId, payments, retired) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        WhatsAppOutgoingQuickReply[] options =
        [
            new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, payments), "Pagos", 0),
            new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, retired), "Comprobante de transfer…", 0) { Description = "Comprobante de transferencia bancaria" },
            new(WhatsAppAssistantDocumentReplyCodec.Encode(requestId, proposalId, null), "Otro", 0),
        ];
        var names = new Dictionary<Guid, string> { [payments] = "Payments" };

        foreach (var (chosen, expected) in new[] { (options[0].Id, "Payments"), (options[2].Id, "Other") })
        {
            var why = new WhatsAppReceivedDocumentWhyRow(WhatsAppOutgoingMessageKind.List, JsonSerializer.Serialize(options), null, null, null, "Pagos")
                { ChosenReplyId = chosen };
            var view = WhatsAppReceivedDocumentPresentationPolicy.View(WhatsAppMessageKind.ReceivedDocument, Pdf with { Why = why }, host.Policy, false, names, "en")!;
            Assert.Equal(("asked", expected), (view.Why!.Kind, view.Why.Chosen));
            Assert.Equal(["Payments", "Comprobante de transferencia bancaria"], view.Why.Forms);
        }
    }
}
