using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Stage 3: a photo or a PDF in Home is read before its turn, like a recording, and the turn names the reading; a Word goes with its turn
// by file-turns, read on the server. The owner says who may attach a document and prepares it through the neutral port; the engine
// reads the copy and the turn takes the reading from the server, never from the browser.
public sealed class AiAssistantHomeDocumentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Pdf = "%PDF-1.7 comprobante ficticio"u8.ToArray();
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Word = "DOCX comprobante ficticio"u8.ToArray();
    private static readonly byte[] Csv = "Código,Cliente\nA1,Cliente ficticio\n"u8.ToArray();
    private const string WordText = "| Fecha | Monto |\n| 11/11/2026 | $ 1.000 |";
    private static readonly StructuredDocumentReading Read = new(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Cut, "es",
        "| Monto | $ 1.000 |\n| Fecha | 11/11/2026 |", false);

    [Fact]
    public void The_private_key_names_its_request_and_type_and_only_those_keys_are_recognized()
    {
        var requestId = Guid.NewGuid();
        Assert.Equal($"ai/assistant/documents/{requestId:N}.pdf", AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf"));
        Assert.Equal($"ai/assistant/documents/{requestId:N}.jpg", AiAssistantHomeDocumentPolicy.Key(requestId, "image/jpeg"));
        Assert.Equal($"ai/assistant/documents/{requestId:N}.png", AiAssistantHomeDocumentPolicy.Key(requestId, "image/png"));
        foreach (var type in AiAssistantDocumentPolicy.MediaTypes)
        {
            Assert.True(AiAssistantHomeDocumentPolicy.TryParseKey(AiAssistantHomeDocumentPolicy.Key(requestId, type), out var parsed));
            Assert.Equal(requestId, parsed);
        }
        Assert.False(AiAssistantHomeDocumentPolicy.TryParseKey($"ai/assistant/documents/{requestId:N}.docx", out _));
        Assert.False(AiAssistantHomeDocumentPolicy.TryParseKey($"ai/assistant/documents/{requestId:N}.pdf.tmp", out _));
        Assert.False(AiAssistantHomeDocumentPolicy.TryParseKey($"ai/assistant/documents/{Guid.Empty:N}.pdf", out _));
        Assert.False(AiAssistantHomeDocumentPolicy.TryParseKey($"ai/assistant/attachments/{requestId:N}.pdf", out _));
        Assert.False(AiAssistantHomeDocumentPolicy.TryParseKey(null, out _));

        // Both kinds of Home copies name their request; a Word a turn brought keeps the prefix of the files of file-turns.
        Assert.Equal($"ai/assistant/attachments/{requestId:N}.docx", AiAssistantHomeAttachmentPolicy.WordKey(requestId));
        Assert.True(AiAssistantHomeAttachmentPolicy.TryParseFileKey(AiAssistantHomeAttachmentPolicy.WordKey(requestId), out var word));
        Assert.Equal(requestId, word);
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseFileKey(AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf"), out _));
        Assert.True(AiAssistantHomeAttachmentPolicy.TryParseKey(AiAssistantHomeDocumentPolicy.Key(requestId, "image/png"), out var home));
        Assert.Equal(requestId, home);
        Assert.True(AiAssistantHomeAttachmentPolicy.TryParseKey(AiAssistantHomeAttachmentPolicy.WordKey(requestId), out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey($"whatsapp/documents/{requestId:N}.pdf", out _));
    }

    [Fact]
    public async Task Without_the_assistant_permission_nothing_is_read_stored_or_submitted()
    {
        var host = new Host();
        host.Fixture.Permissions.Permissions[host.ActorId] = [];
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.UploadAsync()).Error);
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.Documents.GetAsync(Guid.NewGuid(), "es", Ct)).Error);
        Assert.Equal((0, 0, 0), (host.Reader.Asked.Count, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    // Reading documents needs the installation to read them (a document profile and private storage); without it the owner is never asked.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Without_reading_or_private_storage_documents_are_not_available_in_the_installation(bool storage, bool reading)
    {
        var host = new Host(storage, reading);
        var error = Assert.IsType<ValidationError>((await host.UploadAsync()).Error);
        Assert.Equal(AiAssistantErrors.AttachmentDocumentUnavailable.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((0, 0, 0, 0), (host.Reader.Asked.Count, host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task Who_cannot_attach_documents_gets_its_own_forbidden_code_before_the_file_is_read()
    {
        var host = new Host();
        host.Reader.Documents = false;
        Assert.Equal(AiAssistantErrors.AttachmentDocumentForbidden, (await host.UploadAsync()).Error);
        Assert.Equal([host.ActorId], host.Reader.Asked);
        Assert.Equal((0, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    // A spreadsheet or a Word goes with its turn by file-turns: here they are a request error, before anything is kept.
    [Theory]
    [InlineData("spreadsheet")]
    [InlineData("word")]
    public async Task A_spreadsheet_or_a_word_is_not_read_here(string kind)
    {
        var host = new Host();
        var error = Assert.IsType<ValidationError>((await host.UploadAsync(kind == "word" ? Word : Csv, kind == "word" ? "acta.docx" : "clientes.csv")).Error);
        Assert.Equal(AiAssistantErrors.Invalid.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((1, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Theory]
    [InlineData(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf, "Ai.Assistant.AttachmentProtectedPdf")]
    [InlineData(StructuredAttachmentRejection.TooManyPages, StructuredAttachmentFileKind.Pdf, "Ai.Assistant.AttachmentTooManyPages")]
    [InlineData(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf, "Ai.Assistant.AttachmentUnreadablePdf")]
    [InlineData(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Image, "Ai.Assistant.AttachmentTooLargeImage")]
    [InlineData(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Image, "Ai.Assistant.AttachmentUnreadableImage")]
    [InlineData(StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Word, "Ai.Assistant.Invalid")]
    [InlineData(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown, "Ai.Assistant.Invalid")]
    [InlineData(StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Spreadsheet, "Ai.Assistant.Invalid")]
    public async Task Each_rejection_of_a_photo_or_a_pdf_names_it_under_the_file(StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind kind, string code)
    {
        var host = new Host();
        host.Reader.Preparation = StructuredAttachmentPreparation.Rejected(rejection, kind);
        var error = Assert.IsType<ValidationError>((await host.UploadAsync()).Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((0, 0), (host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task A_file_over_the_limit_or_empty_is_rejected_before_it_is_read()
    {
        var host = new Host();
        foreach (var content in new[] { new byte[StructuredAttachmentLimits.MaximumBytes + 1], Array.Empty<byte>() })
        {
            var error = Assert.IsType<ValidationError>((await host.UploadAsync(content)).Error);
            Assert.Equal(["file"], error.Errors.Keys);
        }
        Assert.Equal((0, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task A_new_pdf_is_stored_once_and_read_in_its_own_channel_without_capabilities()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();

        var result = await host.UploadAsync(requestId: requestId, fileName: @"C:\fakepath\comprobante.pdf");

        Assert.Equal(new AiAssistantDocumentResponse(requestId, "pending", null, null, new AiAssistantLimits().PollingAfterSeconds), result.Value);
        var key = AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf");
        Assert.Equal(1, host.Files.WriteCount);
        Assert.Equal(Pdf, await ReadAsync(host.Files, key));
        var turn = host.Turn(requestId);
        Assert.Equal(AiAssistantChannelPolicy.HomeDocumentChannelId, turn.ChannelId);
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson).Value;
        Assert.Equal(AiAssistantPurpose.ReadDocument, profile.Purpose);
        Assert.Equal((host.ActorId, (Guid?)null, "es"), (profile.ChannelContext.ActorUserId, profile.ChannelContext.SubjectUserId, profile.ChannelContext.Culture));
        Assert.Empty(profile.ChannelContext.AllowedCapabilities);
        Assert.Empty(profile.ChannelContext.AllowedTerminals);
        var descriptor = AiAssistantDocumentPolicy.ReadDescriptor(host.Reveal(turn, "question"))!;
        Assert.Equal((key, "comprobante.pdf", "application/pdf", Pdf.LongLength, Sha(Pdf), 2),
            (descriptor.StorageKey, descriptor.Name, descriptor.ContentType, descriptor.Size, descriptor.Sha256, descriptor.Pages));
        Assert.Equal(host.Now.AddDays(new AiAssistantLimits().RetentionDays), descriptor.ExpiresAtUtc);
        Assert.True(Assert.Single(host.Fixture.Store.Receipts).IsDocument);
        Assert.Equal(["comprobante.pdf"], host.Reader.Names);
    }

    [Fact]
    public async Task A_photo_is_one_page()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Jpeg, "foto.jpg", requestId)).IsSuccess);
        var descriptor = AiAssistantDocumentPolicy.ReadDescriptor(host.Reveal(host.Turn(requestId), "question"))!;
        Assert.Equal((AiAssistantHomeDocumentPolicy.Key(requestId, "image/jpeg"), "image/jpeg", 1), (descriptor.StorageKey, descriptor.ContentType, descriptor.Pages));
    }

    [Fact]
    public async Task Repeating_the_request_returns_its_reading_without_reading_storing_or_submitting_again()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        host.CompleteReading(requestId, Read);
        host.Reader.Documents = false;

        var again = await host.UploadAsync(requestId: requestId);

        Assert.Equal("completed", again.Value.State);
        Assert.Equal("partial", again.Value.Document?.Readability);
        Assert.Equal((1, 1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    // A retry is the same file again: another file, or another name, under the same request is a conflict, whatever the state of its
    // reading, and nothing is read, kept or submitted again.
    [Theory]
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task Repeating_the_request_with_another_file_is_a_conflict_whatever_the_state_of_its_reading(string state)
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        if (state == "completed") host.CompleteReading(requestId, Read);
        if (state == "failed")
        {
            var turn = host.Turn(requestId);
            Assert.True(turn.Claim(Guid.NewGuid(), host.Now.AddSeconds(90)).IsSuccess);
            Assert.True(turn.Fail(turn.Fence, AiAssistantErrors.InvalidOutput.Code, false, host.Now).IsSuccess);
        }

        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync([.. Pdf, (byte)'x'], requestId: requestId)).Error);
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync(fileName: "otro.pdf", requestId: requestId)).Error);
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        Assert.Equal((1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    // Two uploads of the same request at once: the second does not see the first's reading yet, keeps the same copy, and its acceptance
    // meets the first's, sealed at another instant: it answers with that reading, as a retry would.
    [Fact]
    public async Task Two_overlapping_uploads_of_the_same_request_end_in_one_reading()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Fixture.Store.HiddenAcceptedLookups = 1;

        var second = await host.UploadAsync(requestId: requestId);

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
        Assert.Equal((requestId, "pending"), (second.Value.RequestId, second.Value.State));
        Assert.Equal((2, 1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    [Fact]
    public async Task A_completed_reading_says_the_document_and_how_readable_it_was_only_to_its_person()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        Assert.Equal(("pending", (AiAssistantReadDocumentResponse?)null), ((await host.Documents.GetAsync(requestId, "es", Ct)).Value.State,
            (await host.Documents.GetAsync(requestId, "es", Ct)).Value.Document));
        host.CompleteReading(requestId, Read);

        var completed = await host.Documents.GetAsync(requestId, "es", Ct);

        Assert.Equal(new AiAssistantDocumentResponse(requestId, "completed", new AiAssistantReadDocumentResponse("comprobante.pdf", "pdf", 2, Pdf.LongLength,
            "partial", "cut"), null, new AiAssistantLimits().PollingAfterSeconds), completed.Value);
        Assert.DoesNotContain("comprobante", completed.Value.ToString(), StringComparison.Ordinal);
        Assert.Contains("\"document\":{\"name\":\"comprobante.pdf\",\"kind\":\"pdf\",\"pages\":2,\"size\":", JsonSerializer.Serialize(completed.Value,
            AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
        host.Current.UserId = host.OtherId;
        host.Fixture.Permissions.Permissions[host.OtherId] = [ArtificialIntelligencePermissions.AssistantUse];
        Assert.Equal(AiAssistantErrors.NotFound, (await host.Documents.GetAsync(requestId, "es", Ct)).Error);
    }

    [Fact]
    public async Task A_failed_reading_says_why_and_offers_a_new_attempt()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(requestId: requestId)).IsSuccess);
        var turn = host.Turn(requestId);
        Assert.True(turn.Claim(Guid.NewGuid(), host.Now.AddSeconds(90)).IsSuccess);
        Assert.True(turn.Fail(turn.Fence, AiAssistantErrors.InvalidOutput.Code, false, host.Now).IsSuccess);

        var failed = await host.Documents.GetAsync(requestId, "es", Ct);

        Assert.Equal(("failed", (AiAssistantReadDocumentResponse?)null), (failed.Value.State, failed.Value.Document));
        Assert.Equal((AiAssistantErrors.InvalidOutput.Code, "newAttempt"), (failed.Value.Failure?.Code, failed.Value.Failure?.Recovery));
        Assert.False(string.IsNullOrWhiteSpace(failed.Value.Failure?.Detail));
    }

    [Fact]
    public async Task A_copy_already_stored_with_other_content_is_a_conflict_and_nothing_is_read()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        await using (var other = new MemoryStream("%PDF-1.7 otro documento"u8.ToArray(), false))
            await host.Files.WriteOnceAsync(AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf"), other, "application/pdf", Ct);
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync(requestId: requestId)).Error);
        Assert.Empty(host.Fixture.Store.Turns);
    }

    [Fact]
    public async Task A_copy_that_cannot_be_read_back_says_so_under_the_file_in_neutral_words()
    {
        var host = new Host();
        var requestId = Guid.NewGuid();
        host.Files.BeforeRead = () => host.Files.DeleteAsync(AiAssistantHomeDocumentPolicy.Key(requestId, "application/pdf"), Ct);
        var error = Assert.IsType<ValidationError>((await host.UploadAsync(requestId: requestId)).Error);
        Assert.Equal(AiAssistantErrors.AttachmentDocumentNotStored.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Empty(host.Fixture.Store.Turns);
    }

    [Fact]
    public async Task A_turn_takes_the_reading_of_its_document_from_the_server_and_seals_it_as_its_own_file()
    {
        var host = new Host();
        var documentRequestId = await host.ReadAsync();
        var request = host.Turn("", documentRequestId);

        var accepted = await host.AcceptAsync(request);

        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        var turn = host.Turn(request.RequestId);
        var key = AiAssistantHomeDocumentPolicy.Key(documentRequestId, "application/pdf");
        Assert.Equal((key, Sha(Pdf), Pdf.LongLength), (turn.AttachmentKey, turn.AttachmentSha256, turn.AttachmentSize));
        var attachment = host.Attachment(turn);
        Assert.Equal((key, "comprobante.pdf", "application/pdf", Pdf.LongLength), (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size));
        Assert.Null(attachment.Spreadsheet);
        var document = attachment.Document!;
        Assert.Equal(("comprobante.pdf", StructuredDocumentKind.Pdf, 2, Read), (document.FileName, document.Kind, document.Pages, document.Reading));
        Assert.Equal(host.Now.AddDays(new AiAssistantLimits().RetentionDays), attachment.ExpiresAtUtc);
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson).Value;
        Assert.Equal((AssistantAttachmentKind.Document, (Guid?)documentRequestId), (profile.Attachment, profile.DocumentRequestId));
        Assert.Equal("", host.Reveal(turn, "question"));
    }

    [Theory]
    [InlineData("unknown", "Ai.Assistant.NotFound")]
    [InlineData("foreign", "Ai.Assistant.NotFound")]
    [InlineData("pending", "Ai.Assistant.ContentUnavailable")]
    [InlineData("purged", "Ai.Assistant.Expired")]
    [InlineData("audio", "Ai.Assistant.NotFound")]
    public async Task A_turn_names_only_a_completed_reading_of_its_own_person(string state, string code)
    {
        var host = new Host();
        var documentRequestId = state switch
        {
            "unknown" => Guid.NewGuid(),
            "foreign" => await host.ReadAsync(owner: host.OtherId),
            "pending" => await host.ReadAsync(complete: false),
            "audio" => host.SeedNormalization("Armame un formulario"),
            _ => await host.ReadAsync(),
        };
        if (state == "purged") host.Turn(documentRequestId).PurgeContent();
        var turns = host.Fixture.Store.Turns.Count;

        var result = await host.AcceptAsync(host.Turn("Armame un formulario con esto", documentRequestId));

        Assert.Equal(code, result.Error.Code);
        Assert.Equal(turns, host.Fixture.Store.Turns.Count);
    }

    [Fact]
    public async Task Repeating_the_turn_recovers_it_and_another_reading_under_the_same_id_is_a_conflict()
    {
        var host = new Host();
        var documentRequestId = await host.ReadAsync();
        var request = host.Turn("Armame un formulario con esto", documentRequestId);
        var first = await host.AcceptAsync(request);
        Assert.True(first.IsSuccess);

        var again = await host.AcceptAsync(request);
        var other = await host.AcceptAsync(request with { DocumentRequestId = await host.ReadAsync() });

        Assert.Equal((first.Value.ConversationId, first.Value.TurnRevision), (again.Value.ConversationId, again.Value.TurnRevision));
        Assert.Equal(AiAssistantErrors.RequestReused, other.Error);
    }

    [Fact]
    public async Task The_turn_that_brought_the_document_shows_it_also_with_text_and_the_next_one_inherits_it_for_the_model()
    {
        var host = new Host();
        var documentRequestId = await host.ReadAsync();
        var request = host.Turn("Armame un formulario con esto", documentRequestId);
        Assert.True((await host.AcceptAsync(request)).IsSuccess);
        var first = host.Turn(request.RequestId);
        host.Complete(first);
        var conversation = host.Fixture.Store.Conversations.Single(item => item.Id == first.ConversationId);
        host.Fixture.Clock.Advance(TimeSpan.FromDays(1));
        var dayOneExpiry = host.Now.AddDays(new AiAssistantLimits().RetentionDays);
        var next = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), conversation.Id, conversation.Revision, host.Fixture.Store.Connection.Revision,
            "Sumale la fecha", "es");
        Assert.True((await host.AcceptAsync(next)).IsSuccess);
        var inheriting = host.Turn(next.RequestId);
        Assert.Equal(first.AttachmentKey, inheriting.AttachmentKey);
        // A Home copy stays while a turn has it, so the inherited one lasts as the new turn's content, from day one, not from the first turn.
        Assert.Equal((dayOneExpiry, dayOneExpiry), (host.Attachment(inheriting).ExpiresAtUtc, inheriting.ContentExpiresAtUtc));
        Assert.Null(AiAssistantChannelPolicy.Read(inheriting.ExecutionProfileJson).Value.DocumentRequestId);

        var own = host.Responses.Map(await host.ShownAsync(request.RequestId), true, 2).Value;
        Assert.Equal(new AiAssistantTurnAttachmentResponse("comprobante.pdf", "pdf", null, Pdf.LongLength) { Pages = 2 }, own.Attachment);
        Assert.Contains("\"attachment\":{\"name\":\"comprobante.pdf\",\"kind\":\"pdf\",\"size\":" + Pdf.LongLength + ",\"pages\":2}",
            JsonSerializer.Serialize(own, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
        var inherited = host.Responses.Map(await host.ShownAsync(next.RequestId), true, 2).Value;
        Assert.Null(inherited.Attachment);
        host.Fixture.Store.AttachmentReads.Clear();
        var page = await host.Reads.ListTurnsAsync(conversation.Id, new ListAiAssistantTurnsRequest(), Ct);
        Assert.Equal(["comprobante.pdf", null], page.Value.Items.OrderBy(turn => turn.Ordinal).Select(turn => turn.Attachment?.Name));
        Assert.Equal([first.Id], host.Fixture.Store.AttachmentReads);
    }

    // The same reading named again in the conversation is brought again by that turn: each one that named it shows it.
    [Fact]
    public async Task Each_turn_that_names_the_reading_brings_its_document()
    {
        var host = new Host();
        var documentRequestId = await host.ReadAsync();
        var request = host.Turn("Armame un formulario con esto", documentRequestId);
        Assert.True((await host.AcceptAsync(request)).IsSuccess);
        var first = host.Turn(request.RequestId);
        host.Complete(first);
        var conversation = host.Fixture.Store.Conversations.Single(item => item.Id == first.ConversationId);
        var again = host.Turn("Otra vez, con la fecha", documentRequestId) with { ConversationId = conversation.Id, ExpectedRevision = conversation.Revision };
        Assert.True((await host.AcceptAsync(again)).IsSuccess);

        Assert.Equal("comprobante.pdf", host.Responses.Map(await host.ShownAsync(again.RequestId), true, 2).Value.Attachment?.Name);
    }

    [Fact]
    public async Task A_conversation_started_with_only_a_document_is_named_after_it_and_the_history_names_the_document()
    {
        var host = new Host();
        var request = host.Turn("", await host.ReadAsync());
        Assert.True((await host.AcceptAsync(request)).IsSuccess);
        var first = host.Turn(request.RequestId);
        host.Complete(first);

        Assert.Equal("comprobante.pdf", (await host.Reads.GetConversationAsync(first.ConversationId, Ct)).Value.Title);
        var shown = host.Responses.Map(await host.ShownAsync(request.RequestId), true, 2).Value;
        Assert.Equal("(Adjuntó el documento \"comprobante.pdf\".)", AiAssistantHistoryPolicy.Messages([shown], 24000, "es")[0].Text);
    }

    [Fact]
    public async Task A_word_goes_by_file_turns_for_who_designs_forms_and_is_read_on_the_server()
    {
        var host = new Host();
        host.Reader.Spreadsheets = false;
        var request = host.FileTurn(Word, "acta.docx");

        var result = await host.FileTurns.SubmitAsync(request, Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var turn = host.Turn(request.Turn.RequestId);
        var key = AiAssistantHomeAttachmentPolicy.WordKey(request.Turn.RequestId);
        Assert.Equal(key, turn.AttachmentKey);
        Assert.Equal(Word, await ReadAsync(host.Files, key));
        var attachment = host.Attachment(turn);
        Assert.Equal((key, "acta.docx", Word.LongLength, Sha(Word)), (attachment.StorageKey, attachment.Name, attachment.Size, attachment.Sha256));
        Assert.Equal((StructuredDocumentKind.Docx, WordText, (int?)null), (attachment.Document?.Kind, attachment.Document?.Reading?.Text, attachment.Document?.Pages));
        Assert.Equal(AssistantAttachmentKind.Document, AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson).Value.Attachment);
        host.Complete(turn);
        var shown = host.Responses.Map(await host.ShownAsync(request.Turn.RequestId), true, 2).Value;
        Assert.Equal(new AiAssistantTurnAttachmentResponse("acta.docx", "docx", null, Word.LongLength), shown.Attachment);
        Assert.Contains("\"attachment\":{\"name\":\"acta.docx\",\"kind\":\"docx\",\"size\":" + Word.LongLength + "}",
            JsonSerializer.Serialize(shown, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
    }

    // Each kind is for whom the owner allows it: the bytes say which it is, so the refusal comes after reading, before keeping a copy.
    [Theory]
    [InlineData("word", false, true, "Ai.Assistant.AttachmentDocumentForbidden")]
    [InlineData("spreadsheet", true, false, "Ai.Assistant.AttachmentForbidden")]
    public async Task Who_may_attach_only_one_kind_cannot_attach_the_other(string kind, bool documents, bool spreadsheets, string code)
    {
        var host = new Host();
        (host.Reader.Documents, host.Reader.Spreadsheets) = (documents, spreadsheets);
        var result = await host.FileTurns.SubmitAsync(kind == "word" ? host.FileTurn(Word, "acta.docx") : host.FileTurn(Csv, "clientes.csv"), Ct);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal((1, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    // A photo or a PDF here is a request error before any permission: even who may attach only spreadsheets gets it, not a 403.
    [Theory]
    [InlineData("pdf")]
    [InlineData("photo")]
    [InlineData("protected")]
    public async Task A_photo_or_a_pdf_is_read_before_its_turn_and_never_goes_by_file_turns(string kind)
    {
        var host = new Host();
        host.Reader.Documents = false;
        if (kind == "protected")
            host.Reader.Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf);
        var error = Assert.IsType<ValidationError>((await host.FileTurns.SubmitAsync(kind == "photo" ? host.FileTurn(Jpeg, "foto.jpg")
            : host.FileTurn(Pdf, "comprobante.pdf"), Ct)).Error);
        Assert.Equal(AiAssistantErrors.Invalid.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((0, 0), (host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Theory]
    [InlineData(StructuredAttachmentRejection.Legacy, "Ai.Assistant.AttachmentLegacyWord")]
    [InlineData(StructuredAttachmentRejection.Protected, "Ai.Assistant.AttachmentProtectedWord")]
    [InlineData(StructuredAttachmentRejection.MacroEnabled, "Ai.Assistant.AttachmentMacroEnabledWord")]
    [InlineData(StructuredAttachmentRejection.Empty, "Ai.Assistant.AttachmentEmptyWord")]
    [InlineData(StructuredAttachmentRejection.Unreadable, "Ai.Assistant.AttachmentUnreadableWord")]
    public async Task Each_rejection_of_a_word_names_it_under_the_file(StructuredAttachmentRejection rejection, string code)
    {
        var host = new Host();
        host.Reader.Preparation = StructuredAttachmentPreparation.Rejected(rejection, StructuredAttachmentFileKind.Word);
        var error = Assert.IsType<ValidationError>((await host.FileTurns.SubmitAsync(host.FileTurn(Word, "acta.docx"), Ct)).Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((0, 0), (host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    // A Word that is not read asks only for what every installation takes: most read no photo or PDF (DocumentModelProfiles is empty by
    // default), so the text never offers one.
    [Theory]
    [InlineData("Ai.Assistant.AttachmentLegacyWord", "es", "Guardalo como .docx.")]
    [InlineData("Ai.Assistant.AttachmentLegacyWord", "en", "Save it as .docx.")]
    [InlineData("Ai.Assistant.AttachmentMacroEnabledWord", "es", "Guardalo como .docx.")]
    [InlineData("Ai.Assistant.AttachmentMacroEnabledWord", "en", "Save it as .docx.")]
    [InlineData("Ai.Assistant.AttachmentUnreadableWord", "es", "Guardalo de nuevo como .docx.")]
    [InlineData("Ai.Assistant.AttachmentUnreadableWord", "en", "Save it again as .docx.")]
    [InlineData("Ai.Assistant.AttachmentProtectedWord", "es", "Guardalo sin contraseña.")]
    [InlineData("Ai.Assistant.AttachmentProtectedWord", "en", "Save it without a password.")]
    public void A_word_that_is_not_read_asks_only_for_what_every_installation_takes(string code, string culture, string remedy)
    {
        var text = ErrorMessages.ResourceManager.GetString(code, CultureInfo.GetCultureInfo(culture));

        Assert.NotNull(text);
        Assert.EndsWith(remedy, text, StringComparison.Ordinal);
        Assert.DoesNotContain("PDF", text, StringComparison.OrdinalIgnoreCase);
    }

    // The bootstrap publishes, per person, what the clip takes: spreadsheets, a Word and, where the installation reads them, photos and PDFs.
    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(false, true, true, false, true, true)]
    [InlineData(false, true, false, false, true, false)]
    public async Task The_bootstrap_publishes_what_this_person_may_attach(bool spreadsheets, bool documents, bool reading,
        bool expectedSpreadsheets, bool expectedDocuments, bool expectedReading)
    {
        var host = new Host(reading: reading);
        (host.Reader.Spreadsheets, host.Reader.Documents) = (spreadsheets, documents);

        var bootstrap = await host.Bootstrap.BootstrapAsync(Ct);

        Assert.Equal((expectedSpreadsheets, expectedDocuments, expectedReading), (bootstrap.Value.Attachments, bootstrap.Value.Documents, bootstrap.Value.DocumentReading));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(bootstrap.Value, AiAssistantResponsePolicy.JsonOptions));
        Assert.Equal((expectedSpreadsheets, expectedDocuments, expectedReading), (json.RootElement.GetProperty("attachments").GetBoolean(),
            json.RootElement.GetProperty("documents").GetBoolean(), json.RootElement.GetProperty("documentReading").GetBoolean()));
    }

    [Fact]
    public async Task Without_private_storage_the_bootstrap_offers_no_document()
    {
        var host = new Host(storage: false);
        var bootstrap = await host.Bootstrap.BootstrapAsync(Ct);
        Assert.Equal((false, false, false), (bootstrap.Value.Attachments, bootstrap.Value.Documents, bootstrap.Value.DocumentReading));
        Assert.Empty(host.Reader.Asked);
    }

    // A reading that can still be taken (pending, running or completed) keeps its copy; one that failed, was uncertain, canceled or
    // expired releases it, unless a turn already holds it.
    [Fact]
    public async Task A_document_copy_stays_while_its_reading_can_be_taken_or_a_turn_that_holds_it_keeps_its_content()
    {
        var host = new Host();
        var read = await host.ReadAsync();
        var pending = await host.ReadAsync(complete: false);
        var held = await host.ReadAsync();
        var gone = await host.ReadAsync();
        var failed = await host.ReadAsync(complete: false);
        var uncertain = await host.ReadAsync(complete: false);
        var canceled = await host.ReadAsync(complete: false);
        var request = host.Turn("", held);
        Assert.True((await host.AcceptAsync(request)).IsSuccess);
        host.Turn(held).PurgeContent();
        host.Turn(gone).PurgeContent();
        foreach (var (requestId, isUncertain) in new[] { (failed, false), (uncertain, true) })
        {
            var turn = host.Turn(requestId);
            Assert.True(turn.Claim(Guid.NewGuid(), host.Now.AddSeconds(90)).IsSuccess);
            Assert.True(turn.Fail(turn.Fence, AiAssistantErrors.ProviderUncertain.Code, isUncertain, host.Now).IsSuccess);
        }
        host.Turn(canceled).RequestCancellation(host.Now);
        Assert.Equal(AiAssistantTurnState.Canceled, host.Turn(canceled).State);
        var keys = new[] { read, pending, held, gone, failed, uncertain, canceled }.Select(id => AiAssistantHomeDocumentPolicy.Key(id, "application/pdf")).ToArray();
        var source = new AiAssistantDocumentRetentionSource(host.Fixture.Store);

        Assert.Equal(keys[..3].Order(), (await source.ListReferencedKeysAsync([.. keys, AiAssistantHomeAttachmentPolicy.WordKey(Guid.NewGuid()),
            $"whatsapp/documents/{Guid.NewGuid():N}.pdf"], Ct)).Order());
        Assert.Empty(await source.ListReferencedKeysAsync(["forms/pdf/fictional.pdf"], Ct));
        // The attachment source keeps what file-turns kept, a Word included, and leaves the document copies to theirs.
        var word = host.FileTurn(Word, "acta.docx");
        Assert.True((await host.FileTurns.SubmitAsync(word, Ct)).IsSuccess);
        var wordKey = AiAssistantHomeAttachmentPolicy.WordKey(word.Turn.RequestId);
        Assert.Equal([wordKey], await new AiAssistantAttachmentRetentionSource(host.Fixture.Store).ListReferencedKeysAsync([wordKey, .. keys], Ct));
    }

    [Fact]
    public async Task The_written_question_is_optional_with_a_document_reading()
    {
        var validator = RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(new AiAssistantLimits()));
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, 1, "", "es");
        Assert.Contains("question", (await validator.ValidateAsync(turn, Ct))!.Errors.Keys);
        Assert.Null(await validator.ValidateAsync(turn with { DocumentRequestId = Guid.NewGuid() }, Ct));
        Assert.Null(await validator.ValidateAsync(turn with { DocumentRequestId = Guid.NewGuid(), AudioRequestId = Guid.NewGuid() }, Ct));
        Assert.Contains("documentRequestId", (await validator.ValidateAsync(turn with { DocumentRequestId = Guid.Empty }, Ct))!.Errors.Keys);
    }

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    private static async Task<byte[]?> ReadAsync(MemoryPrivateFileStore files, string key)
    {
        await using var stream = await files.OpenReadAsync(key, Ct);
        if (stream is null) return null;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, Ct);
        return copy.ToArray();
    }

    // Home on the real acceptance and engine adapter, with the five core queries as metadata only, a fictional owner and private storage
    // in memory; the reading is completed by hand, as the worker would leave it.
    private sealed class Host
    {
        private readonly AiAssistantSubmissionPolicy submissions;
        public AssistantOperationsFixture Fixture { get; }
        public MemoryPrivateFileStore Files { get; }
        public DocumentReader Reader { get; } = new();
        public FakeCurrentUser Current { get; }
        public AiAssistantResponsePolicy Responses { get; }
        public AiAssistantDocumentService Documents { get; }
        public AiAssistantFileTurnService FileTurns { get; }
        public AiAssistantReadPolicy Reads { get; }
        public AiAssistantBootstrapPolicy Bootstrap { get; }
        public Guid ActorId => Fixture.ActorId;
        public Guid OtherId => Fixture.SubjectId;
        public DateTime Now => Fixture.Clock.GetUtcNow().UtcDateTime;

        public Host(bool storage = true, bool reading = true)
        {
            Fixture = new(documentQualified: reading);
            Files = new MemoryPrivateFileStore(storage);
            Fixture.Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            Current = new FakeCurrentUser { UserId = ActorId };
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource(
                new[] { "users.summary", "roles.list", "roles.permissions", "system.help", "business.information" }
                    .Select(id => PreparedAssistantCapabilitySource.Definition(id)).ToArray())], Fixture.Permissions);
            var guard = new AiAssistantAccessGuard(Fixture.Users, Fixture.Permissions, Fixture.Profiles, limits, Current, catalog,
                new(Fixture.AudioProfiles, Fixture.DocumentProfiles, Files));
            submissions = new(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector, new OperationsFingerprint(), limits, Fixture.Clock);
            Responses = new(Fixture.Protector, Fixture.Clock, catalog);
            var operations = new AiAssistantOperations(Fixture.Store, guard, submissions, Fixture.Work, Fixture.Store, Responses,
                NullLogger<AiAssistantOperations>.Instance);
            Documents = new(operations, new AiAssistantHomeDocumentPolicy(Files, Reader, guard, Fixture.Store), guard, limits, Fixture.Clock,
                NullLogger<AiAssistantDocumentService>.Instance);
            var attachments = new AiAssistantHomeAttachmentPolicy(Files, Reader, Fixture.Store, Fixture.Protector);
            Bootstrap = new(Fixture.Store, Fixture.Store, Fixture.Permissions, guard, Responses, catalog, limits, attachments);
            Reads = new(Fixture.Store, RequestValidators.For(new ListAiAssistantTurnsRequestValidator()), Fixture.Permissions, guard, Responses, limits, Bootstrap);
            FileTurns = new(Fixture.Work, RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(limits)), guard, submissions, attachments, limits,
                Fixture.Clock, NullLogger<AiAssistantFileTurnService>.Instance);
        }

        public Task<Result<AiAssistantDocumentResponse>> UploadAsync(byte[]? content = null, string fileName = "comprobante.pdf", Guid? requestId = null) =>
            Documents.SubmitAsync(new(requestId ?? Guid.NewGuid(), "es", content ?? Pdf, fileName), Ct);

        // A PDF of this person (or another one), uploaded and, unless asked otherwise, read as the worker would leave it.
        public async Task<Guid> ReadAsync(Guid? owner = null, bool complete = true)
        {
            var requestId = Guid.NewGuid();
            Current.UserId = owner ?? ActorId;
            Fixture.Permissions.Permissions[Current.UserId.Value] = [ArtificialIntelligencePermissions.AssistantUse];
            var uploaded = await UploadAsync(requestId: requestId);
            Current.UserId = ActorId;
            Assert.True(uploaded.IsSuccess, uploaded.IsFailure ? uploaded.Error.Code : null);
            if (complete) CompleteReading(requestId, Read);
            return requestId;
        }

        public void CompleteReading(Guid requestId, StructuredDocumentReading reading)
        {
            var turn = Turn(requestId);
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "answer", turn.PayloadRevision,
                AiAssistantDocumentPolicy.SerializeReading(reading)), Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "evidences",
                turn.PayloadRevision, "[]"), Now).IsSuccess);
        }

        public SubmitAiAssistantTurnRequest Turn(string question, Guid documentRequestId) =>
            new(Guid.NewGuid(), null, null, Fixture.Store.Connection.Revision, question, "es") { DocumentRequestId = documentRequestId };

        public SubmitAiAssistantFileTurnRequest FileTurn(byte[] content, string fileName) =>
            new(new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, Fixture.Store.Connection.Revision, "", "es"), content, fileName);

        public Task<Result<AiAssistantTurnAcceptedResponse>> AcceptAsync(SubmitAiAssistantTurnRequest request) =>
            Fixture.Work.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(ActorId, request, ct), CommitPolicy.OnSuccess, Ct);

        public AiAssistantTurn Turn(Guid requestId) => Fixture.Store.Turns.Single(turn => turn.RequestId == requestId);

        // The turn as Home shows it: its row, with the sealed file of the turn that brought it.
        public async Task<AiAssistantTurnRow> ShownAsync(Guid requestId) => (await AiAssistantAttachmentPolicy.WithOwnAttachmentsAsync(Fixture.Store, ActorId,
            [(await Fixture.Store.FindTurnByRequestAsync(ActorId, requestId, Ct))!], Ct))[0];

        public string? Reveal(AiAssistantTurn turn, string purpose) => Fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id,
            purpose, turn.PayloadRevision, turn.ProtectedQuestion!);

        public AssistantAttachmentInput Attachment(AiAssistantTurn turn) => JsonSerializer.Deserialize<AssistantAttachmentInput>(
            Fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id, "attachment", turn.PayloadRevision, turn.ProtectedAttachment!)!,
            JsonSerializerOptions.Web)!;

        public void Complete(AiAssistantTurn turn)
        {
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            var answer = JsonSerializer.Serialize(new AiAssistantAnswer("clarification", "¿Qué querés armar con el documento?", []), JsonSerializerOptions.Web);
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "answer", turn.PayloadRevision, answer),
                Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "evidences", turn.PayloadRevision, "[]"), Now).IsSuccess);
        }

        // A completed Home normalization of this person: a reading request id that names an audio is not a document.
        public Guid SeedNormalization(string text)
        {
            var conversation = AiConversation.Create(ActorId, Now, AiAssistantChannelPolicy.HomeAudioChannelId);
            var requestId = Guid.NewGuid();
            var turn = AiAssistantTurn.Create(ActorId, conversation.Id, requestId, 1, "", "es", "UTC", AiProvider.OpenAI, "audio-model", 1,
                Now, Now.AddDays(30), AiAssistantChannelPolicy.HomeAudioChannelId);
            var profile = new AiAssistantExecutionProfileSnapshot("fixture-audio", 2, "audio-none-v1", 1,
                AiAssistantChannelPolicy.HomeAudio(ActorId, "es", "UTC")) { Purpose = AiAssistantPurpose.NormalizeAudio };
            Assert.True(turn.SealExecutionProfile(JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions)).IsSuccess);
            turn.ProtectQuestion(Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "question", turn.PayloadRevision, "{}"));
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            var answer = JsonSerializer.Serialize(new { kind = "normalizedAudio", text, languages = new[] { "es" } }, AiAssistantResponsePolicy.JsonOptions);
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "answer", turn.PayloadRevision, answer),
                Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "evidences", turn.PayloadRevision, "[]"), Now).IsSuccess);
            Fixture.Store.Conversations.Add(conversation);
            Fixture.Store.Turns.Add(turn);
            Fixture.Store.Receipts.Add(AiAssistantRequestReceipt.Create(ActorId, requestId, "fixture-fingerprint", "test", conversation.Id, turn.Id,
                Now, Now.AddDays(30), AiAssistantChannelPolicy.HomeAudioChannelId, true));
            return requestId;
        }
    }

    // The owner's neutral port, fictional: whom it allows for each kind, what it was asked and, unless it is told what to answer, a file
    // recognized by its first bytes (a PDF of two pages, a JPEG, a Word with its text, or else a CSV sheet).
    private sealed class DocumentReader : IStructuredAttachmentReader
    {
        public bool Spreadsheets { get; set; } = true;
        public bool Documents { get; set; } = true;
        public StructuredAttachmentPreparation? Preparation { get; set; }
        public List<Guid> Asked { get; } = [];
        public List<string> Names { get; } = [];
        public int Prepared { get; private set; }

        public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken)
        {
            Asked.Add(actorUserId);
            return Task.FromResult(new StructuredAttachmentAccess(Spreadsheets, Documents));
        }

        public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        {
            Prepared++;
            Names.Add(fileName);
            return Task.FromResult(Preparation ?? content switch
            {
                [(byte)'%', (byte)'P', (byte)'D', (byte)'F', ..] => Document(content, fileName, StructuredDocumentKind.Pdf, "application/pdf", 2, null),
                [0xFF, 0xD8, 0xFF, ..] => Document(content, fileName, StructuredDocumentKind.Jpeg, "image/jpeg", null, null),
                [(byte)'D', (byte)'O', (byte)'C', (byte)'X', ..] => Document(content, fileName, StructuredDocumentKind.Docx,
                    "application/vnd.openxmlformats-officedocument.wordprocessingml.document", null,
                    new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, null, WordText, false)),
                _ => StructuredAttachmentPreparation.Prepared(FictionalAttachment.Spreadsheet() with
                    { FileName = fileName, Kind = StructuredSpreadsheetKind.Csv, MediaType = "text/csv", Size = content.LongLength, Sha256 = Sha(content) }),
            });
        }

        private static StructuredAttachmentPreparation Document(byte[] content, string fileName, StructuredDocumentKind kind, string mediaType, int? pages,
            StructuredDocumentReading? reading) => StructuredAttachmentPreparation.Prepared(new StructuredDocument(fileName, kind, mediaType, content.LongLength,
            Sha(content), pages, reading));
    }
}
