using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// IA, Stage 2: a stored spreadsheet is a source with its caption as the request; without a caption it pairs with the contiguous text
// or transcribed audio, before or after, if no assistant reply came in between and they arrived less than ten minutes apart.
public sealed class WhatsAppAssistantSourcePolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ContactId = Guid.NewGuid();

    private static WhatsAppAssistantSpreadsheetRow Sheet(Guid messageId, string fileName = "clientes.csv") => new(messageId,
        $"whatsapp/documents/{messageId:N}.csv", fileName, 27, new string('c', 64), Now.AddDays(90),
        JsonSerializer.Serialize(new StructuredSpreadsheet(fileName, StructuredSpreadsheetKind.Csv, "text/csv", 27, new string('c', 64), "", 3, 2,
            [new(1, new Dictionary<string, string> { ["A"] = "Código", ["B"] = "Texto" })]), JsonSerializerOptions.Web));

    private static WhatsAppAssistantSourceRow Text(string body = "Armame un catálogo con esta planilla", DateTime? at = null) =>
        new(Guid.NewGuid(), ContactId, body, at ?? Now, (at ?? Now).AddDays(90)) { InboundSequence = 8 };

    private static WhatsAppAssistantSourceRow Spreadsheet(string caption = "", DateTime? at = null)
    {
        var id = Guid.NewGuid();
        return new(id, ContactId, caption, at ?? Now, (at ?? Now).AddDays(90)) { Kind = WhatsAppMessageKind.Spreadsheet, InboundSequence = 8, Spreadsheet = Sheet(id) };
    }

    private static WhatsAppAssistantNeighborRow PreviousText(string body = "Armame un catálogo con esta planilla", DateTime? at = null, bool answered = false) =>
        new(Guid.NewGuid(), WhatsAppMessageKind.Text, at ?? Now.AddMinutes(-2), (at ?? Now.AddMinutes(-2)).AddDays(30)) { Body = body, AnsweredSince = answered };

    private static WhatsAppAssistantNeighborRow PreviousSpreadsheet(WhatsAppSpreadsheetState state, string? caption = null, DateTime? at = null, bool answered = false)
    {
        var id = Guid.NewGuid();
        return new(id, WhatsAppMessageKind.Spreadsheet, at ?? Now.AddMinutes(-2), (at ?? Now.AddMinutes(-2)).AddDays(90))
            { Body = caption, SpreadsheetState = state, Spreadsheet = state == WhatsAppSpreadsheetState.Stored ? Sheet(id) with { ExpiresAtUtc = Now.AddDays(20) } : null, AnsweredSince = answered };
    }

    [Fact]
    public void A_stored_spreadsheet_with_a_caption_is_its_own_request_and_does_not_pair()
    {
        var source = Spreadsheet("Importala en Clientes");
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(source, PreviousText());
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Importala en Clientes", source.Id), (resolved.State, resolved.Body, resolved.Spreadsheet!.MessageId));
        Assert.Equal(source.ContentExpiresAtUtc, resolved.ContentExpiresAtUtc);
    }

    [Fact]
    public void A_spreadsheet_after_its_text_takes_that_text_as_its_request()
    {
        var previous = PreviousText("Armame un catálogo de clientes");
        var source = Spreadsheet();
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(source, previous);
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Armame un catálogo de clientes", source.Id), (resolved.State, resolved.Body, resolved.Spreadsheet!.MessageId));
        Assert.Equal(previous.ContentExpiresAtUtc, resolved.ContentExpiresAtUtc);
    }

    [Fact]
    public void A_text_after_its_spreadsheet_carries_that_spreadsheet()
    {
        var previous = PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored);
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text("Armame un catálogo de clientes"), previous);
        Assert.Equal((WhatsAppAssistantSourceState.Ready, "Armame un catálogo de clientes", previous.Id), (resolved.State, resolved.Body, resolved.Spreadsheet!.MessageId));
        Assert.Equal(previous.Spreadsheet!.ExpiresAtUtc, resolved.ContentExpiresAtUtc);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(9, true)]
    public void Ten_minutes_apart_they_are_not_a_pair_in_either_order(int minutes, bool paired)
    {
        var text = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored, at: Now.AddMinutes(-minutes)));
        Assert.Equal(paired, text.Spreadsheet is not null);
        var spreadsheet = WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), PreviousText(at: Now.AddMinutes(-minutes)));
        Assert.Equal(paired ? WhatsAppAssistantSourceState.Ready : WhatsAppAssistantSourceState.Alone, spreadsheet.State);
    }

    [Fact]
    public void An_assistant_reply_in_between_keeps_them_apart_in_either_order()
    {
        var text = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored, answered: true));
        Assert.Equal((WhatsAppAssistantSourceState.Ready, (WhatsAppAssistantSpreadsheetRow?)null), (text.State, text.Spreadsheet));
        var spreadsheet = WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), PreviousText(answered: true));
        Assert.Equal((WhatsAppAssistantSourceState.Alone, ""), (spreadsheet.State, spreadsheet.Body));
    }

    [Fact]
    public void A_spreadsheet_with_a_caption_before_a_text_is_not_the_text_pair()
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored, "Mirá esta"));
        Assert.Equal((WhatsAppAssistantSourceState.Ready, (WhatsAppAssistantSpreadsheetRow?)null), (resolved.State, resolved.Spreadsheet));
    }

    [Fact]
    public void A_text_waits_for_its_spreadsheet_while_it_is_downloaded()
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousSpreadsheet(WhatsAppSpreadsheetState.Pending));
        Assert.Equal((WhatsAppAssistantSourceState.Waiting, (WhatsAppAssistantSpreadsheetRow?)null), (resolved.State, resolved.Spreadsheet));
    }

    // H11: a download that keeps failing retries for a day, but past the usual window (five failures and fifteen minutes) it no longer
    // holds back the text paired with it, nor a spreadsheet after a pending audio: they go on as with a file that is not available.
    [Fact]
    public void A_text_stops_waiting_for_a_spreadsheet_whose_download_keeps_failing()
    {
        var source = Text();
        Assert.Equal(source, WhatsAppAssistantSourcePolicy.Resolve(source, PreviousSpreadsheet(WhatsAppSpreadsheetState.Pending) with { AcquisitionStalled = true }));
    }

    [Fact]
    public void A_text_stops_waiting_for_a_received_document_whose_download_keeps_failing()
    {
        var source = Text();
        var document = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.ReceivedDocument, Now.AddMinutes(-2), Now.AddDays(90))
            { DocumentState = WhatsAppDocumentState.Pending, AcquisitionStalled = true };
        Assert.Equal(source, WhatsAppAssistantSourcePolicy.Resolve(source, document));
        Assert.Equal(WhatsAppAssistantSourceState.Waiting, WhatsAppAssistantSourcePolicy.Resolve(source, document with { AcquisitionStalled = false }).State);
        Assert.Equal(WhatsAppAssistantSourceState.Waiting, WhatsAppAssistantSourcePolicy.Resolve(source, document with { DocumentState = WhatsAppDocumentState.Reading }).State);
    }

    [Fact]
    public void A_spreadsheet_stops_waiting_for_an_audio_whose_download_keeps_failing()
    {
        var audio = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.Audio, Now.AddMinutes(-1), Now.AddDays(1))
            { AudioState = WhatsAppAudioState.Pending, AcquisitionStalled = true };
        Assert.Equal(WhatsAppAssistantSourceState.Alone, WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), audio).State);
    }

    [Fact]
    public void A_text_whose_spreadsheet_was_rejected_was_answered_already()
    {
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Text(), PreviousSpreadsheet(WhatsAppSpreadsheetState.Rejected));
        Assert.Equal(WhatsAppAssistantSourceState.Answered, resolved.State);
    }

    [Fact]
    public void A_text_whose_spreadsheet_is_not_available_goes_on_alone_as_today()
    {
        var source = Text();
        Assert.Equal(source, WhatsAppAssistantSourcePolicy.Resolve(source, PreviousSpreadsheet(WhatsAppSpreadsheetState.Unavailable)));
        Assert.Equal(source, WhatsAppAssistantSourcePolicy.Resolve(source, PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored) with { Spreadsheet = null }));
        Assert.Equal(source, WhatsAppAssistantSourcePolicy.Resolve(source, null));
    }

    [Theory]
    [InlineData(WhatsAppAudioState.Completed, "Armame un catálogo de clientes", WhatsAppAssistantSourceState.Ready)]
    [InlineData(WhatsAppAudioState.Pending, null, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppAudioState.Transcribing, null, WhatsAppAssistantSourceState.Waiting)]
    [InlineData(WhatsAppAudioState.Unavailable, null, WhatsAppAssistantSourceState.Alone)]
    [InlineData(WhatsAppAudioState.Completed, null, WhatsAppAssistantSourceState.Alone)]
    public void An_audio_before_a_spreadsheet_counts_only_with_its_transcription(WhatsAppAudioState state, string? transcription, WhatsAppAssistantSourceState expected)
    {
        var audio = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.Audio, Now.AddMinutes(-1), Now.AddDays(1)) { AudioState = state, Body = transcription };
        var resolved = WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), audio);
        Assert.Equal((expected, transcription ?? ""), (resolved.State, resolved.Body));
    }

    [Fact]
    public void A_spreadsheet_alone_has_no_request_of_its_own()
    {
        Assert.Equal(WhatsAppAssistantSourceState.Alone, WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), null).State);
        var button = new WhatsAppAssistantNeighborRow(Guid.NewGuid(), WhatsAppMessageKind.ButtonReply, Now.AddMinutes(-1), Now.AddDays(90)) { Body = "Crear catálogo" };
        Assert.Equal(WhatsAppAssistantSourceState.Alone, WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), button).State);
        var other = PreviousSpreadsheet(WhatsAppSpreadsheetState.Stored);
        Assert.Equal(WhatsAppAssistantSourceState.Alone, WhatsAppAssistantSourcePolicy.Resolve(Spreadsheet(), other).State);
    }

    [Fact]
    public void A_captured_form_response_never_pairs()
    {
        var capture = Text("") with { Kind = WhatsAppMessageKind.FlowReply, CapturedRecordId = Guid.NewGuid() };
        Assert.Equal(capture, WhatsAppAssistantSourcePolicy.Resolve(capture, PreviousSpreadsheet(WhatsAppSpreadsheetState.Pending)));
    }

    [Theory]
    [InlineData("clientes.csv", "clientes.csv")]
    [InlineData("", "spreadsheet.csv")]
    public void The_attachment_names_the_copy_its_size_its_hash_and_the_expiry_of_the_message_content(string fileName, string expected)
    {
        var sheet = Sheet(Guid.NewGuid(), fileName);
        var attachment = WhatsAppAssistantSourcePolicy.Attachment(sheet);
        Assert.Equal((sheet.StorageKey, expected, "text/csv", 27L, new string('c', 64), sheet.ExpiresAtUtc),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc));
        Assert.Equal((3, "Código"), (attachment.Spreadsheet!.RowCount, attachment.Spreadsheet.Rows[0].Cells["A"]));
    }
}
