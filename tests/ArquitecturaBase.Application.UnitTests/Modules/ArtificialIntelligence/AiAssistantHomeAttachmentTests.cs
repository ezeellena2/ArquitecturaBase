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
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Stage 2: a Home turn with a spreadsheet keeps a private copy under a key derived from its request and is accepted like any other
// turn. The owner says who may attach and reads the file through the neutral port; the engine never opens the copy.
public sealed class AiAssistantHomeAttachmentTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Csv = "Código,Cliente\nA1,Cliente ficticio\nA2,Otro cliente ficticio\n"u8.ToArray();
    private static readonly DateTime Expires = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void The_private_key_names_its_request_and_kind_and_only_those_keys_are_recognized()
    {
        var requestId = Guid.NewGuid();
        Assert.Equal($"ai/assistant/attachments/{requestId:N}.xlsx", AiAssistantHomeAttachmentPolicy.Key(requestId, StructuredSpreadsheetKind.Xlsx));
        Assert.Equal($"ai/assistant/attachments/{requestId:N}.csv", AiAssistantHomeAttachmentPolicy.Key(requestId, StructuredSpreadsheetKind.Csv));
        foreach (var kind in Enum.GetValues<StructuredSpreadsheetKind>())
        {
            Assert.True(AiAssistantHomeAttachmentPolicy.TryParseKey(AiAssistantHomeAttachmentPolicy.Key(requestId, kind), out var parsed));
            Assert.Equal(requestId, parsed);
        }
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey($"ai/assistant/audio/{requestId:N}.webm", out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey($"ai/assistant/attachments/{requestId:N}.xls", out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey($"ai/assistant/attachments/{requestId:N}.csv.tmp", out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey($"ai/assistant/attachments/{Guid.Empty:N}.csv", out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey("fixture/attachments/clientes.xlsx", out _));
        Assert.False(AiAssistantHomeAttachmentPolicy.TryParseKey(null, out _));
    }

    [Fact]
    public void The_name_keeps_no_folders_or_control_characters_and_is_as_long_as_the_prepared_sheet_name()
    {
        Assert.Equal("clientes.xlsx", AiAssistantHomeAttachmentPolicy.Name("clientes.xlsx"));
        Assert.Equal("clientes.csv", AiAssistantHomeAttachmentPolicy.Name(@"C:\fakepath\clientes.csv"));
        Assert.Equal("clientes.csv", AiAssistantHomeAttachmentPolicy.Name("carpeta/clientes.csv"));
        Assert.Equal("clientes.xlsx", AiAssistantHomeAttachmentPolicy.Name(" clien\ttes\n.xlsx "));
        var name = AiAssistantHomeAttachmentPolicy.Name(new string('a', 200) + ".xlsx");
        Assert.Equal(120, name.Length);
        Assert.EndsWith("…", name, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task Attaching_needs_the_owner_to_allow_the_person_and_private_storage(bool allowed, bool storage, bool expected)
    {
        var reader = new AttachmentReader { Allowed = allowed };
        var policy = new AiAssistantHomeAttachmentPolicy(new MemoryPrivateFileStore(storage), reader, null!, null!);
        var userId = Guid.NewGuid();
        Assert.Equal(expected, (await policy.CanAttachAsync(userId, Ct)).Spreadsheets);
        // The owner decides for the named person, never for whoever is the current user.
        Assert.All(reader.Asked, asked => Assert.Equal(userId, asked));
    }

    // Who may attach only documents gets no spreadsheet clip, and a spreadsheet is refused once its bytes say what it is.
    [Fact]
    public async Task Who_may_attach_only_documents_cannot_attach_a_spreadsheet()
    {
        var host = new Host();
        (host.Reader.Allowed, host.Reader.AllowedDocuments) = (false, true);

        Assert.False((await host.Bootstrap.BootstrapAsync(Ct)).Value.Attachments);
        Assert.Equal(AiAssistantErrors.AttachmentForbidden, (await host.Service.SubmitAsync(host.Request(), Ct)).Error);
        Assert.Equal((1, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    [Fact]
    public async Task A_spreadsheet_or_unknown_file_with_a_password_keeps_its_reason()
    {
        foreach (var kind in new[] { StructuredAttachmentFileKind.Spreadsheet, StructuredAttachmentFileKind.Unknown })
        {
            var reader = new AttachmentReader { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected, kind) };
            var policy = new AiAssistantHomeAttachmentPolicy(new MemoryPrivateFileStore(), reader, null!, null!);

            Assert.Equal("Ai.Assistant.AttachmentProtected", (await policy.PrepareAsync(Csv, "precios.xlsx", new(true, true), Ct)).Error.Code);
        }
    }

    // The bootstrap says it per person, by the same rule as a turn: the front shows the clip and never repeats the permissions.
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public async Task The_bootstrap_says_whether_this_person_may_attach_a_spreadsheet(bool allowed, bool storage, bool expected)
    {
        var host = new Host(storage);
        host.Reader.Allowed = allowed;
        var bootstrap = await host.Bootstrap.BootstrapAsync(Ct);
        Assert.True(bootstrap.IsSuccess);
        Assert.Equal(expected, bootstrap.Value.Attachments);
        Assert.Contains($"\"attachments\":{(expected ? "true" : "false")}", JsonSerializer.Serialize(bootstrap.Value, AiAssistantResponsePolicy.JsonOptions),
            StringComparison.Ordinal);
        // The owner decides for the person who opened Home, and only when there is private storage for the copy.
        Assert.Equal(storage ? [host.ActorId] : [], host.Reader.Asked);
    }

    [Fact]
    public async Task Storing_keeps_one_copy_and_rejects_other_content_under_the_same_request()
    {
        var files = new MemoryPrivateFileStore();
        var policy = new AiAssistantHomeAttachmentPolicy(files, new AttachmentReader(), null!, null!);
        var requestId = Guid.NewGuid();
        var attachment = AiAssistantHomeAttachmentPolicy.Describe(requestId, Csv, "clientes.csv", AttachmentReader.Prepare(Csv, "clientes.csv"), Expires);
        Assert.Equal((AiAssistantHomeAttachmentPolicy.Key(requestId, StructuredSpreadsheetKind.Csv), "clientes.csv", "text/csv", Csv.LongLength, Sha(Csv), Expires),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc));
        Assert.Null(AiAssistantAttachmentPolicy.Validate(attachment));
        Assert.True((await policy.StoreAsync(attachment, Csv, Ct)).IsSuccess);
        Assert.True((await policy.StoreAsync(attachment, Csv, Ct)).IsSuccess);
        byte[] other = [.. Csv, (byte)'x'];
        var replaced = AiAssistantHomeAttachmentPolicy.Describe(requestId, other, "clientes.csv", AttachmentReader.Prepare(other, "clientes.csv"), Expires);
        Assert.Equal(AiAssistantErrors.RequestReused, (await policy.StoreAsync(replaced, other, Ct)).Error);
        Assert.Equal(1, files.WriteCount);
    }

    [Fact]
    public async Task Retention_keeps_only_the_copies_that_a_turn_with_content_still_has()
    {
        var host = new Host();
        var kept = host.SeedTurnWith(AiAssistantHomeAttachmentPolicy.Key(Guid.NewGuid(), StructuredSpreadsheetKind.Xlsx));
        var purged = host.SeedTurnWith(AiAssistantHomeAttachmentPolicy.Key(Guid.NewGuid(), StructuredSpreadsheetKind.Csv));
        host.Fixture.Store.Turns.Single(turn => turn.AttachmentKey == purged).PurgeContent();
        var expired = host.SeedTurnWith(AiAssistantHomeAttachmentPolicy.Key(Guid.NewGuid(), StructuredSpreadsheetKind.Csv), contentHours: 1);
        // Another owner's copy, even with content, belongs to that owner's own retention source.
        var foreign = host.SeedTurnWith("fixture/attachments/clientes.xlsx");
        host.Fixture.Clock.Advance(TimeSpan.FromHours(2));
        var source = new AiAssistantAttachmentRetentionSource(host.Fixture.Store);
        Assert.Equal([kept], await source.ListReferencedKeysAsync([kept, purged, expired, foreign, $"ai/assistant/audio/{Guid.NewGuid():N}.webm"], Ct));
        Assert.Empty(await source.ListReferencedKeysAsync(["forms/pdf/fictional.pdf"], Ct));
    }

    [Fact]
    public async Task Without_the_assistant_permission_nothing_is_read_or_stored()
    {
        var host = new Host();
        host.Fixture.Permissions.Permissions[host.ActorId] = [];
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.Service.SubmitAsync(host.Request(), Ct)).Error);
        Assert.Equal((0, 0, 0, 0), (host.Reader.Asked.Count, host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    [Fact]
    public async Task Who_cannot_attach_gets_its_own_forbidden_code_before_the_file_is_read()
    {
        var host = new Host();
        host.Reader.Allowed = false;
        Assert.Equal(AiAssistantErrors.AttachmentForbidden, (await host.Service.SubmitAsync(host.Request(), Ct)).Error);
        Assert.Equal((0, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    // Without private storage it is the installation, not the account: its own code under the file, and the owner is never asked.
    [Fact]
    public async Task Without_private_storage_spreadsheets_are_not_available_in_the_installation()
    {
        var host = new Host(storage: false);
        var error = Assert.IsType<ValidationError>((await host.Service.SubmitAsync(host.Request(), Ct)).Error);
        Assert.Equal(AiAssistantErrors.AttachmentUnavailable.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((0, 0, 0, 0), (host.Reader.Asked.Count, host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    [Fact]
    public async Task A_copy_that_cannot_be_read_back_answers_under_the_file_in_neutral_words()
    {
        var host = new Host();
        var request = host.Request();
        host.Files.BeforeRead = () => host.Files.DeleteAsync(AiAssistantHomeAttachmentPolicy.Key(request.Turn.RequestId, StructuredSpreadsheetKind.Csv), Ct);
        var error = Assert.IsType<ValidationError>((await host.Service.SubmitAsync(request, Ct)).Error);
        Assert.Equal(AiAssistantErrors.AttachmentNotStored.Code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Empty(host.Fixture.Store.Turns);
    }

    [Fact]
    public async Task The_turn_fields_are_checked_before_the_file_is_read_or_a_retry_is_looked_up()
    {
        var host = new Host();
        var error = Assert.IsType<ValidationError>((await host.Service.SubmitAsync(host.Request(requestId: Guid.Empty), Ct)).Error);
        Assert.Contains("requestId", error.Errors.Keys);
        Assert.Equal((0, (Guid?)null), (host.Reader.Prepared, host.Fixture.Store.LastReadRequest));
    }

    [Theory]
    [InlineData(StructuredAttachmentRejection.Unavailable, "Ai.Assistant.AttachmentUnavailable")]
    [InlineData(StructuredAttachmentRejection.Legacy, "Ai.Assistant.AttachmentLegacy")]
    [InlineData(StructuredAttachmentRejection.Protected, "Ai.Assistant.AttachmentProtected")]
    [InlineData(StructuredAttachmentRejection.MacroEnabled, "Ai.Assistant.AttachmentMacroEnabled")]
    [InlineData(StructuredAttachmentRejection.TooLarge, "Ai.Assistant.AttachmentTooLarge")]
    [InlineData(StructuredAttachmentRejection.Unreadable, "Ai.Assistant.AttachmentUnreadable")]
    [InlineData(StructuredAttachmentRejection.Empty, "Ai.Assistant.AttachmentEmpty")]
    [InlineData(StructuredAttachmentRejection.TooManyPages, "Ai.Assistant.AttachmentTooManyPages")]
    public async Task Each_rejection_answers_under_the_file_with_its_own_code(StructuredAttachmentRejection rejection, string code)
    {
        var host = new Host();
        host.Reader.Rejection = rejection;
        var error = Assert.IsType<ValidationError>((await host.Service.SubmitAsync(host.Request(), Ct)).Error);
        Assert.Equal(code, error.Code);
        Assert.Equal(["file"], error.Errors.Keys);
        Assert.Equal((1, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    [Fact]
    public async Task A_new_spreadsheet_is_stored_once_and_its_turn_accepted_with_it()
    {
        var host = new Host();
        var request = host.Request(fileName: @"C:\fakepath\clientes.csv");
        var result = await host.Service.SubmitAsync(request, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal((request.Turn.RequestId, "pending"), (result.Value.RequestId, result.Value.State));
        Assert.Equal(["clientes.csv"], host.Reader.Names);
        var key = AiAssistantHomeAttachmentPolicy.Key(request.Turn.RequestId, StructuredSpreadsheetKind.Csv);
        var turn = host.Turn(request.Turn.RequestId);
        Assert.Equal(key, turn.AttachmentKey);
        var sealedAttachment = host.Attachment(turn);
        Assert.Equal((key, "clientes.csv", "text/csv", Csv.LongLength, Sha(Csv)),
            (sealedAttachment.StorageKey, sealedAttachment.Name, sealedAttachment.ContentType, sealedAttachment.Size, sealedAttachment.Sha256));
        // The copy lasts as long as the turn's content, like a recording.
        Assert.Equal(host.Now.AddDays(new AiAssistantLimits().RetentionDays), sealedAttachment.ExpiresAtUtc);
        Assert.Equal(sealedAttachment.ExpiresAtUtc, turn.ContentExpiresAtUtc);
        Assert.Equal("", host.Reveal(turn, "question"));
        Assert.Equal((1, 1), (host.Files.WriteCount, host.Fixture.Work.Commits));
    }

    [Fact]
    public async Task Repeating_the_same_upload_recovers_its_turn_without_reading_storing_or_accepting_again()
    {
        var host = new Host();
        var request = host.Request(question: "Armame un catálogo con esta planilla");
        var first = await host.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        // The retry accepts with the descriptor it stored, without reading or storing again: the person may have lost the attachment meanwhile.
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Reader.Allowed = false;
        var again = await host.Service.SubmitAsync(request, Ct);
        Assert.True(again.IsSuccess);
        Assert.Equal((first.Value.ConversationId, first.Value.RequestId, first.Value.TurnRevision),
            (again.Value.ConversationId, again.Value.RequestId, again.Value.TurnRevision));
        Assert.Equal((1, 1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    // Two requests with the same id at once: the second does not find the first's acceptance yet, reads and keeps the same copy, and
    // the acceptance recovers the first turn, because Home's fingerprint does not bind the expiry each attempt gives its copy.
    [Fact]
    public async Task Two_overlapping_uploads_of_the_same_request_end_in_one_turn()
    {
        var host = new Host();
        var request = host.Request(question: "Armame un catálogo con esta planilla");
        var first = await host.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Fixture.Store.HidesAcceptedRequests = true;
        var second = await host.Service.SubmitAsync(request, Ct);
        Assert.True(second.IsSuccess);
        Assert.Equal((first.Value.ConversationId, first.Value.RequestId), (second.Value.ConversationId, second.Value.RequestId));
        Assert.Equal((2, 1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    [Fact]
    public async Task An_acceptance_that_fails_after_keeping_the_copy_is_retried_with_the_same_id()
    {
        var host = new Host();
        var request = host.Request();
        var stale = request with { Turn = request.Turn with { ExpectedConnectionRevision = host.Fixture.Store.Connection.Revision + 1 } };
        Assert.Equal(AiAssistantErrors.ConnectionChanged, (await host.Service.SubmitAsync(stale, Ct)).Error);
        Assert.Equal((1, 0), (host.Files.WriteCount, host.Fixture.Store.Turns.Count));
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal((2, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task A_retry_compares_what_was_sent_before_reading_the_stored_descriptor()
    {
        var host = new Host();
        var request = host.Request();
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var reads = host.Fixture.Protector.Reads;
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.Service.SubmitAsync(request with { Content = [.. Csv, (byte)'x'] }, Ct)).Error);
        Assert.Equal(reads, host.Fixture.Protector.Reads);
    }

    [Theory]
    [InlineData("purged", "Ai.Assistant.NotFound")]
    [InlineData("retired", "Ai.Assistant.NotFound")]
    [InlineData("unreadable", "Ai.Assistant.ContentUnavailable")]
    public async Task A_retry_of_a_turn_whose_copy_is_gone_or_unreadable_answers_like_the_turn(string state, string code)
    {
        var host = new Host();
        var request = host.Request();
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = host.Turn(request.Turn.RequestId);
        if (state == "purged") turn.PurgeContent();
        else if (state == "retired") host.Fixture.Store.Conversations.Single(item => item.Id == turn.ConversationId).Remove(host.Now);
        else turn.Attach(turn.AttachmentKey!, turn.AttachmentSha256!, turn.AttachmentSize!.Value, "fictional-unreadable-ciphertext");
        Assert.Equal(code, (await host.Service.SubmitAsync(request, Ct)).Error.Code);
        Assert.Equal((1, 1), (host.Reader.Prepared, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task A_conversation_is_named_after_its_spreadsheet_only_when_its_first_turn_has_no_text()
    {
        var host = new Host();
        var spreadsheetOnly = host.Request();
        var written = host.Request(question: "Armame un catálogo de clientes");
        Assert.True((await host.Service.SubmitAsync(spreadsheetOnly, Ct)).IsSuccess);
        Assert.True((await host.Service.SubmitAsync(written, Ct)).IsSuccess);
        var unnamed = host.Turn(spreadsheetOnly.Turn.RequestId);
        host.Fixture.Store.AttachmentReads.Clear();
        Assert.Equal("clientes.csv", (await host.Reads.GetConversationAsync(unnamed.ConversationId, Ct)).Value.Title);
        Assert.Equal([unnamed.Id], host.Fixture.Store.AttachmentReads);
        Assert.Equal("Armame un catálogo de clientes", (await host.Reads.GetConversationAsync(host.Turn(written.Turn.RequestId).ConversationId, Ct)).Value.Title);
        Assert.Equal([unnamed.Id], host.Fixture.Store.AttachmentReads);
    }

    [Fact]
    public async Task The_turns_page_shows_the_spreadsheet_on_the_turn_that_brought_it_and_reads_only_that_one()
    {
        var host = new Host();
        var request = host.Request();
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var first = host.Turn(request.Turn.RequestId);
        host.Complete(first);
        var conversation = host.Fixture.Store.Conversations.Single(item => item.Id == first.ConversationId);
        Assert.True((await host.AcceptAsync(new SubmitAiAssistantTurnRequest(Guid.NewGuid(), conversation.Id, conversation.Revision,
            host.Fixture.Store.Connection.Revision, "Usá la columna B como nombre", "es"))).IsSuccess);
        host.Fixture.Store.AttachmentReads.Clear();
        var page = await host.Reads.ListTurnsAsync(conversation.Id, new ListAiAssistantTurnsRequest(), Ct);
        Assert.Equal(["clientes.csv", null], page.Value.Items.OrderBy(turn => turn.Ordinal).Select(turn => turn.Attachment?.Name));
        Assert.Equal([first.Id], host.Fixture.Store.AttachmentReads);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("name")]
    [InlineData("question")]
    public async Task Repeating_the_id_with_another_spreadsheet_or_question_is_a_conflict(string changed)
    {
        var host = new Host();
        var request = host.Request(question: "Armame un catálogo con esta planilla");
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var reused = changed switch
        {
            "content" => request with { Content = [.. Csv, (byte)'x'] },
            "name" => request with { FileName = "otra.csv" },
            _ => request with { Turn = request.Turn with { Question = "Otro pedido" } },
        };
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.Service.SubmitAsync(reused, Ct)).Error);
        Assert.Equal((1, 1, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task An_id_already_used_by_a_turn_without_its_own_spreadsheet_is_a_conflict()
    {
        var host = new Host();
        var typed = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, host.Fixture.Store.Connection.Revision, "Hola", "es");
        Assert.True((await host.AcceptAsync(typed)).IsSuccess);
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.Service.SubmitAsync(host.Request(typed.RequestId), Ct)).Error);
        Assert.Equal((0, 0, 1), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task A_copy_already_stored_with_other_content_is_a_conflict_and_no_turn_is_accepted()
    {
        var host = new Host();
        var request = host.Request();
        await using (var other = new MemoryStream("Otra planilla\n"u8.ToArray(), false))
            await host.Files.WriteOnceAsync(AiAssistantHomeAttachmentPolicy.Key(request.Turn.RequestId, StructuredSpreadsheetKind.Csv), other, "text/csv", Ct);
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.Service.SubmitAsync(request, Ct)).Error);
        Assert.Empty(host.Fixture.Store.Turns);
        Assert.Equal(0, host.Fixture.Work.Transactions);
    }

    [Fact]
    public async Task A_spreadsheet_turn_may_also_bring_a_dictated_request()
    {
        var host = new Host();
        var audioRequestId = host.SeedNormalization("Armame un catálogo de clientes");
        var request = host.Request(audioRequestId: audioRequestId);
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = host.Turn(request.Turn.RequestId);
        Assert.Equal(audioRequestId, turn.AudioRequestId);
        Assert.Equal("Armame un catálogo de clientes", host.Reveal(turn, "transcript"));
        Assert.Equal("clientes.csv", host.Attachment(turn).Name);
    }

    [Fact]
    public async Task An_invalid_turn_is_rejected_before_its_copy_is_stored()
    {
        var host = new Host();
        var request = host.Request();
        var invalid = request with { Turn = request.Turn with { ConversationId = Guid.NewGuid(), ExpectedRevision = null } };
        var error = Assert.IsType<ValidationError>((await host.Service.SubmitAsync(invalid, Ct)).Error);
        Assert.Contains("expectedRevision", error.Errors.Keys);
        Assert.Equal((0, 0, 0), (host.Reader.Prepared, host.Files.WriteCount, host.Fixture.Work.Transactions));
    }

    [Fact]
    public async Task The_written_question_is_optional_with_a_spreadsheet_and_may_come_with_an_audio()
    {
        var validator = RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(new AiAssistantLimits()));
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, 1, "", "es");
        var attachment = FictionalAttachment.Input(Expires);
        Assert.Contains("question", (await validator.ValidateAsync(turn, Ct))!.Errors.Keys);
        Assert.Null(await validator.ValidateAsync(turn with { Attachment = attachment }, Ct));
        Assert.Null(await validator.ValidateAsync(turn with { Attachment = attachment, AudioRequestId = Guid.NewGuid() }, Ct));
        Assert.Contains("question", (await validator.ValidateAsync(turn with { Attachment = attachment, Question = new string('x', 4001) }, Ct))!.Errors.Keys);
    }

    [Fact]
    public async Task A_first_turn_with_only_a_spreadsheet_names_the_conversation_after_its_file()
    {
        var host = new Host();
        var request = host.Request();
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var turn = host.Turn(request.Turn.RequestId);
        var conversation = host.Fixture.Store.Conversations.Single(item => item.Id == turn.ConversationId);
        var question = new AiConversationQuestionRow(turn.Id, turn.PayloadRevision, turn.ProtectedQuestion, turn.ContentExpiresAtUtc)
            { ProtectedAttachment = turn.ProtectedAttachment };
        var row = new AiConversationRow(conversation.Id, host.ActorId, AiConversationState.Active, conversation.Revision, host.Now, [], null, question);
        Assert.Equal("clientes.csv", host.Responses.MapTitle(row, true).Value);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, host.Responses.MapTitle(row with { FirstQuestion = question with { ProtectedAttachment = "corrupt" } }, true).Error);
    }

    [Theory]
    [InlineData("es", "(Adjuntó la planilla \"clientes.xlsx\".)")]
    [InlineData("en", "(Attached the spreadsheet \"clientes.xlsx\".)")]
    public void The_history_names_the_spreadsheet_of_a_turn_without_text_and_never_sends_the_sheet_again(string culture, string named)
    {
        AiAssistantTurnResponse Turn(int ordinal, string question, AiAssistantTurnAttachmentResponse? attachment) =>
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ordinal, 1, 1, "completed", "finished", "allowed", question,
                new AiAssistantAnswer("clarification", "¿Qué querés hacer con la planilla?", []), [], null, null, culture, "UTC",
                DateTime.UnixEpoch, DateTime.UnixEpoch, 2) { Attachment = attachment };
        var attachment = new AiAssistantTurnAttachmentResponse("clientes.xlsx", "xlsx", 12, 2048);
        var messages = AiAssistantHistoryPolicy.Messages([Turn(2, "Armá un catálogo", null), Turn(1, "", attachment)], 24000, culture);
        Assert.Equal([named, "¿Qué querés hacer con la planilla?", "Armá un catálogo", "¿Qué querés hacer con la planilla?"],
            messages.Select(message => message.Text));
        Assert.Equal("Usá la columna B", AiAssistantHistoryPolicy.Messages([Turn(1, "Usá la columna B", attachment)], 24000, culture)[0].Text);
    }

    [Fact]
    public async Task Only_the_turn_that_brought_the_spreadsheet_shows_it()
    {
        var host = new Host();
        var request = host.Request();
        Assert.True((await host.Service.SubmitAsync(request, Ct)).IsSuccess);
        var first = host.Turn(request.Turn.RequestId);
        host.Complete(first);
        var conversation = host.Fixture.Store.Conversations.Single(item => item.Id == first.ConversationId);
        var next = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), conversation.Id, conversation.Revision, host.Fixture.Store.Connection.Revision,
            "Usá la columna B como nombre", "es");
        Assert.True((await host.AcceptAsync(next)).IsSuccess);
        Assert.Equal(first.AttachmentKey, host.Turn(next.RequestId).AttachmentKey);

        var own = host.Responses.Map(await host.ShownAsync(request.Turn.RequestId), true, 2).Value;
        Assert.Equal(new AiAssistantTurnAttachmentResponse("clientes.csv", "csv", 12, Csv.LongLength), own.Attachment);
        Assert.Contains("\"attachment\":{\"name\":\"clientes.csv\",\"kind\":\"csv\",\"rows\":12,\"size\":", JsonSerializer.Serialize(own, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
        var inherited = host.Responses.Map(await host.ShownAsync(next.RequestId), true, 2).Value;
        Assert.Null(inherited.Attachment);
        Assert.DoesNotContain("attachment", JsonSerializer.Serialize(inherited, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
        Assert.Null(host.Responses.Map(await host.ShownAsync(request.Turn.RequestId), false, 2).Value.Attachment);
        Assert.DoesNotContain("clientes", own.Attachment!.ToString(), StringComparison.Ordinal);
    }

    private static string Sha(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    // Home on the real acceptance, with the five core queries as metadata only, a fictional owner and private storage in memory.
    private sealed class Host
    {
        private readonly AiAssistantSubmissionPolicy submissions;
        public AssistantOperationsFixture Fixture { get; } = new();
        public MemoryPrivateFileStore Files { get; }
        public AttachmentReader Reader { get; } = new();
        public AiAssistantResponsePolicy Responses { get; }
        public AiAssistantFileTurnService Service { get; }
        public AiAssistantReadPolicy Reads { get; }
        public AiAssistantBootstrapPolicy Bootstrap { get; }
        public Guid ActorId => Fixture.ActorId;
        public DateTime Now => Fixture.Clock.GetUtcNow().UtcDateTime;

        public Host(bool storage = true)
        {
            Files = new MemoryPrivateFileStore(storage);
            Fixture.Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource(
                new[] { "users.summary", "roles.list", "roles.permissions", "system.help", "business.information" }
                    .Select(id => PreparedAssistantCapabilitySource.Definition(id)).ToArray())], Fixture.Permissions);
            var guard = new AiAssistantAccessGuard(Fixture.Users, Fixture.Permissions, Fixture.Profiles, limits,
                new FakeCurrentUser { UserId = ActorId }, catalog, new([], [], Files));
            submissions = new(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector, new OperationsFingerprint(), limits, Fixture.Clock);
            Responses = new(Fixture.Protector, Fixture.Clock, catalog);
            var attachments = new AiAssistantHomeAttachmentPolicy(Files, Reader, Fixture.Store, Fixture.Protector);
            Bootstrap = new(Fixture.Store, Fixture.Store, Fixture.Permissions, guard, Responses, catalog, limits, attachments);
            Reads = new(Fixture.Store, RequestValidators.For(new ListAiAssistantTurnsRequestValidator()), Fixture.Permissions, guard,
                Responses, limits, Bootstrap);
            Service = new(Fixture.Work, RequestValidators.For(new SubmitAiAssistantTurnRequestValidator(limits)), guard, submissions,
                attachments, limits, Fixture.Clock, NullLogger<AiAssistantFileTurnService>.Instance);
        }

        public SubmitAiAssistantFileTurnRequest Request(Guid? requestId = null, string question = "", string fileName = "clientes.csv",
            Guid? audioRequestId = null) =>
            new(new SubmitAiAssistantTurnRequest(requestId ?? Guid.NewGuid(), null, null, Fixture.Store.Connection.Revision, question, "es")
                { AudioRequestId = audioRequestId }, Csv, fileName);

        public Task<Result<AiAssistantTurnAcceptedResponse>> AcceptAsync(SubmitAiAssistantTurnRequest request) =>
            Fixture.Work.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(ActorId, request, ct), CommitPolicy.OnSuccess, Ct);

        public AiAssistantTurn Turn(Guid requestId) => Fixture.Store.Turns.Single(turn => turn.RequestId == requestId);

        // The turn as Home shows it: its row, with the sealed spreadsheet of the turn that brought it.
        public async Task<AiAssistantTurnRow> ShownAsync(Guid requestId) => (await AiAssistantAttachmentPolicy.WithOwnAttachmentsAsync(Fixture.Store, ActorId,
            [(await Fixture.Store.FindTurnByRequestAsync(ActorId, requestId, Ct))!], Ct))[0];

        public string? Reveal(AiAssistantTurn turn, string purpose) => Fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id,
            purpose, turn.PayloadRevision, purpose == "transcript" ? turn.ProtectedTranscript! : turn.ProtectedQuestion!);

        public AssistantAttachmentInput Attachment(AiAssistantTurn turn) => JsonSerializer.Deserialize<AssistantAttachmentInput>(
            Fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id, "attachment", turn.PayloadRevision, turn.ProtectedAttachment!)!,
            JsonSerializerOptions.Web)!;

        public void Complete(AiAssistantTurn turn)
        {
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            var answer = JsonSerializer.Serialize(new AiAssistantAnswer("clarification", "¿Qué querés hacer con la planilla?", []), JsonSerializerOptions.Web);
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "answer", turn.PayloadRevision, answer),
                Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "evidences", turn.PayloadRevision, "[]"), Now).IsSuccess);
        }

        // A completed turn of another conversation that has a copy under the key, with its content for the hours given.
        public string SeedTurnWith(string key, int contentHours = 48)
        {
            var conversation = AiConversation.Create(ActorId, Now, "home");
            var turn = AiAssistantTurn.Create(ActorId, conversation.Id, Guid.NewGuid(), 1, "", "es", "UTC", AiProvider.OpenAI, "model", 1,
                Now, Now.AddHours(contentHours));
            turn.ProtectQuestion(Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "question", turn.PayloadRevision, "Pedido"));
            var attachment = FictionalAttachment.Input(Now.AddHours(contentHours), key);
            turn.Attach(key, attachment.Sha256, attachment.Size, Fixture.Protector.Protect(ActorId, conversation.Id, turn.Id, "attachment",
                turn.PayloadRevision, FictionalAttachment.Json(attachment)));
            Fixture.Store.Conversations.Add(conversation);
            Fixture.Store.Turns.Add(turn);
            return key;
        }

        // A completed Home normalization of this person, as IA4 leaves it before the turn names it.
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

    // The owner's neutral port: whom it allows, what it was asked and, unless it rejects the file, a sheet prepared from the bytes.
    private sealed class AttachmentReader : IStructuredAttachmentReader
    {
        public bool Allowed { get; set; } = true;
        public bool AllowedDocuments { get; set; }
        public StructuredAttachmentRejection? Rejection { get; set; }
        public StructuredAttachmentPreparation? Preparation { get; set; }
        public List<Guid> Asked { get; } = [];
        public List<string> Names { get; } = [];
        public int Prepared { get; private set; }

        public Task<StructuredAttachmentAccess> CanAttachAsync(Guid actorUserId, CancellationToken cancellationToken)
        {
            Asked.Add(actorUserId);
            return Task.FromResult(new StructuredAttachmentAccess(Allowed, AllowedDocuments));
        }

        public Task<StructuredAttachmentPreparation> PrepareAsync(byte[] content, string fileName, CancellationToken cancellationToken)
        {
            Prepared++;
            Names.Add(fileName);
            return Task.FromResult(Preparation ?? (Rejection is { } rejection ? StructuredAttachmentPreparation.Rejected(rejection)
                : StructuredAttachmentPreparation.Prepared(Prepare(content, fileName))));
        }

        public static StructuredSpreadsheet Prepare(byte[] content, string fileName) => FictionalAttachment.Spreadsheet() with
            { FileName = fileName, Kind = StructuredSpreadsheetKind.Csv, MediaType = "text/csv", Size = content.LongLength, Sha256 = Sha(content) };
    }
}
