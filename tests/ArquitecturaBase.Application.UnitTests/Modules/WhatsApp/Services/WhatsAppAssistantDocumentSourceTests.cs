using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Task 9 of the documents: a read document is a source like a spreadsheet. Its caption is the request; without one it pairs with the
// contiguous text or transcribed audio, before or after it, when no assistant reply came in between and they arrived less than ten
// minutes apart. A text waits while its document is downloaded or read, and a document that was rejected already answered.
public sealed class WhatsAppAssistantDocumentSourceTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ContactId = Guid.NewGuid();
    private static readonly StructuredDocumentReading Reading = new(StructuredDocumentReadability.Readable, null, "es", "Transferencia $ 150.000", false);

    private static WhatsAppAssistantDocumentRow Read(Guid messageId, string extension = ".pdf") => new(messageId,
        $"whatsapp/documents/{messageId:N}{extension}", "comprobante" + extension, 2048, new string('d', 64), extension == ".pdf" ? 2 : null, Now.AddDays(90),
        WhatsAppReceivedDocumentReadingPolicy.SerializeReading(Reading));

    private static WhatsAppAssistantSourceRow Text(string body = "Cargalo en pagos", DateTime? at = null) =>
        new(Guid.NewGuid(), ContactId, body, at ?? Now, (at ?? Now).AddDays(90)) { InboundSequence = 8 };

    private static WhatsAppAssistantSourceRow Document(string caption = "", DateTime? at = null)
    {
        var id = Guid.NewGuid();
        return new(id, ContactId, caption, at ?? Now, (at ?? Now).AddDays(90)) { Kind = WhatsAppMessageKind.ReceivedDocument, InboundSequence = 8, Document = Read(id) };
    }

    private static WhatsAppAssistantNeighborRow PreviousText(string body = "Cargalo en pagos", DateTime? at = null, bool answered = false) =>
        new(Guid.NewGuid(), WhatsAppMessageKind.Text, at ?? Now.AddMinutes(-2), (at ?? Now.AddMinutes(-2)).AddDays(30)) { Body = body, AnsweredSince = answered };

    private static WhatsAppAssistantNeighborRow PreviousDocument(WhatsAppDocumentState state, string? caption = null, DateTime? at = null, bool answered = false)
    {
        var id = Guid.NewGuid();
        return new(id, WhatsAppMessageKind.ReceivedDocument, at ?? Now.AddMinutes(-2), (at ?? Now.AddMinutes(-2)).AddDays(90))
        {
            Body = caption, DocumentState = state, AnsweredSince = answered,
            Document = state == WhatsAppDocumentState.Read ? Read(id) with { ExpiresAtUtc = Now.AddDays(20) } : null,
        };
    }

    [Fact]
    public void A_read_document_with_a_caption_is_its_own_request_and_does_not_pair()
    {
        var source = Document("Cargalo en pagos");
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(source, PreviousText("otra cosa"));
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Cargalo en pagos", source.Id), (resolved.State, resolved.Body, resolved.Document!.MessageId));
    }

    [Fact]
    public void A_document_after_its_text_takes_that_text_as_its_request()
    {
        var previous = PreviousText("Te paso la transferencia");
        var source = Document();
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(source, previous);
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Te paso la transferencia", source.Id), (resolved.State, resolved.Body, resolved.Document!.MessageId));
        Assert.Equal(previous.ContentExpiresAtUtc, resolved.ContentExpiresAtUtc);
    }

    [Fact]
    public void A_text_after_its_read_document_carries_that_document()
    {
        var previous = PreviousDocument(WhatsAppDocumentState.Read);
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text("Armame un formulario con esto"), previous);
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Armame un formulario con esto", previous.Id), (resolved.State, resolved.Body, resolved.Document!.MessageId));
        Assert.Equal(previous.Document!.ExpiresAtUtc, resolved.ContentExpiresAtUtc);
        Assert.Null(resolved.Spreadsheet);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(9, true)]
    public void Ten_minutes_apart_they_are_not_a_pair_in_either_order(int minutes, bool paired)
    {
        var text = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousDocument(WhatsAppDocumentState.Read, at: Now.AddMinutes(-minutes)));
        Assert.Equal(paired, text.Document is not null);
        var document = WhatsAppAssistantSourcePolicy.Resolve(Document(), PreviousText(at: Now.AddMinutes(-minutes)));
        Assert.Equal(paired ? WhatsAppAssistantSourceState.Ready : WhatsAppAssistantSourceState.Alone, document.State);
    }

    [Fact]
    public void An_assistant_reply_in_between_keeps_them_apart_in_either_order()
    {
        Assert.Null(WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousDocument(WhatsAppDocumentState.Read, answered: true)).Document);
        var alone = WhatsAppAssistantSourcePolicy.Resolve(Document(), PreviousText(answered: true));
        Assert.Equal((WhatsAppAssistantSourceState.Alone, ""), (alone.State, alone.Body));
        Assert.NotNull(alone.Document);
    }

    // While its document is downloaded or read the text waits for it; one that was rejected was answered with its reason.
    [Theory]
    [InlineData(WhatsAppDocumentState.Pending, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppDocumentState.Reading, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppDocumentState.Rejected, WhatsAppAssistantSourceState.Answered)]
    [InlineData(WhatsAppDocumentState.Stored, WhatsAppAssistantSourceState.Ready)]
    [InlineData(WhatsAppDocumentState.Unavailable, WhatsAppAssistantSourceState.Ready)]
    [InlineData(WhatsAppDocumentState.Failed, WhatsAppAssistantSourceState.Ready)]
    public void A_text_after_a_document_that_is_not_read_waits_is_answered_or_goes_alone(WhatsAppDocumentState state, WhatsAppAssistantSourceState expected)
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousDocument(state));
        Assert.Equal((expected, (WhatsAppAssistantDocumentRow?)null), (resolved.State, resolved.Document));
    }

    // Review of task 9: a document over the day's reading quota was answered with its fixed text, so the text paired with it is not
    // interpreted alone after it; another unavailable one leaves the text alone, as before.
    [Theory]
    [InlineData(WhatsAppReceivedDocumentPreparationPolicy.QuotaCode, WhatsAppAssistantSourceState.Answered)]
    [InlineData(WhatsAppReceivedDocumentPreparationPolicy.NotLinkedCode, WhatsAppAssistantSourceState.Ready)]
    public void A_text_after_a_document_over_the_reading_quota_was_answered_by_its_quota_text(string failure, WhatsAppAssistantSourceState expected)
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousDocument(WhatsAppDocumentState.Unavailable) with { DocumentFailureCode = failure });
        Assert.Equal(expected, resolved.State);
    }

    [Fact]
    public void A_document_with_a_caption_before_a_text_does_not_pair_with_it()
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousDocument(WhatsAppDocumentState.Read, caption: "Cargalo"));
        Assert.Equal((WhatsAppAssistantSourceState.Ready, (WhatsAppAssistantDocumentRow?)null), (resolved.State, resolved.Document));
    }

    [Theory]
    [InlineData(WhatsAppAudioState.Pending, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppAudioState.Transcribing, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppAudioState.Failed, WhatsAppAssistantSourceState.Alone)]
    public void A_document_after_an_audio_waits_for_its_transcription(WhatsAppAudioState state, WhatsAppAssistantSourceState expected)
    {
        var audio = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.Audio, Now.AddMinutes(-1), Now.AddDays(30)) { AudioState = state };
        Assert.Equal(expected, WhatsAppAssistantSourcePolicy.Resolve(Document(), audio).State);
    }

    [Fact]
    public void A_document_after_a_transcribed_audio_takes_its_transcription()
    {
        var audio = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.Audio, Now.AddMinutes(-1), Now.AddDays(10))
            { AudioState = WhatsAppAudioState.Completed, Body = "Cargá este comprobante" };
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Document(), audio);
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Cargá este comprobante", Now.AddDays(10)), (resolved.State, resolved.Body, resolved.ContentExpiresAtUtc));
    }

    // The engine takes the private copy and its reading: the descriptor of the copy, the kind by its key and the pages of a PDF.
    [Theory]
    [InlineData(".pdf", StructuredDocumentKind.Pdf, "application/pdf", 2)]
    [InlineData(".jpg", StructuredDocumentKind.Jpeg, "image/jpeg", null)]
    public void The_attachment_is_the_copy_with_its_reading(string extension, StructuredDocumentKind kind, string mediaType, int? pages)
    {
        var row = Read(Guid.NewGuid(), extension);
        var attachment = WhatsAppAssistantSourcePolicy.Attachment(row);
        Assert.Equal((row.StorageKey, row.FileName, mediaType, row.Size, row.Sha256, row.ExpiresAtUtc),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc));
        Assert.Equal(AssistantAttachmentKind.Document, attachment.Kind);
        Assert.Equal((kind, mediaType, pages, Reading), (attachment.Document!.Kind, attachment.Document.MediaType, attachment.Document.Pages, attachment.Document.Reading));
    }

    [Fact]
    public void A_reading_this_module_did_not_serialize_is_a_fault()
    {
        var row = Read(Guid.NewGuid()) with { ReadingJson = "{\"readability\":3}" };
        Assert.Throws<InvalidOperationException>(() => WhatsAppAssistantSourcePolicy.Attachment(row));
    }
}
