using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring: who gets a spreadsheet downloaded, the limit, each reason answered once, the copy, the
// retries and what the assistant still does not take.
public sealed class WhatsAppSpreadsheetDispatchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_account_that_may_attach_gets_it_downloaded_prepared_and_copied_outside_the_transaction()
    {
        var f = new WhatsAppSpreadsheetFixture();

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([AssistantAttachmentInput.MaximumBytes], f.DownloadLimits);
        Assert.Equal(["clientes.csv"], f.PreparedNames);
        Assert.Equal(["contact", "message"], f.Locks);
        Assert.Equal((1, 1), (f.Host.UnitOfWork.Transactions, f.Host.UnitOfWork.Commits));
        Assert.Equal(WhatsAppSpreadsheetState.Stored, f.Message.SpreadsheetState);
        Assert.Equal((f.Key, (long?)f.Content.Length, WhatsAppSpreadsheetFixture.Hash(f.Content)),
            (f.Message.SpreadsheetStorageKey, f.Message.SpreadsheetSize, f.Message.SpreadsheetCopySha256));
        Assert.Equal(f.Content, await f.StoredAsync(f.Key));
        var prepared = JsonSerializer.Deserialize<StructuredSpreadsheet>(f.Message.SpreadsheetPreparedJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(("clientes.csv", StructuredSpreadsheetKind.Csv, 3, 2), (prepared.FileName, prepared.Kind, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal("Uno", prepared.Rows[1].Cells["B"]);
        Assert.Empty(f.Replies);
        Assert.Empty(f.Host.Requests.Rows);
    }

    [Fact]
    public async Task An_excel_workbook_is_copied_with_its_extension()
    {
        var f = new WhatsAppSpreadsheetFixture { Kind = StructuredSpreadsheetKind.Xlsx };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.EndsWith(".xlsx", f.Message.SpreadsheetStorageKey, StringComparison.Ordinal);
        Assert.Equal(f.Content, await f.StoredAsync(f.Key));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("unlinked")]
    [InlineData("inactive")]
    [InlineData("forbidden")]
    [InlineData("withoutStorage")]
    public async Task Anyone_else_keeps_it_as_today_without_downloading_it(string who)
    {
        var f = new WhatsAppSpreadsheetFixture(storage: who != "withoutStorage");
        if (who == "manual") f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        if (who == "unlinked") f.Contact.UnlinkUser();
        if (who == "inactive") await f.Host.Context.Users.ArrangeAsync(users => users.SetActiveAsync(f.Host.Context.Subject.Id, false, Ct));
        if (who == "forbidden") f.MayAttach = false;

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((0, 0, 0), (f.Downloads, f.PreparedNames.Count, f.Files.WriteCount));
        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code),
            (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Empty(f.Replies);
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((0, 1), (f.Downloads, f.Host.UnitOfWork.Transactions));
    }

    [Fact]
    public async Task Losing_the_permission_while_downloading_leaves_it_unavailable()
    {
        var f = new WhatsAppSpreadsheetFixture();
        f.BeforeDownload = () => f.MayAttach = false;

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(1, f.Downloads);
        Assert.Equal(WhatsAppSpreadsheetState.Unavailable, f.Message.SpreadsheetState);
        Assert.Null(f.Message.SpreadsheetStorageKey);
        Assert.Empty(f.Replies);
    }

    [Theory]
    [InlineData(StructuredAttachmentRejection.Legacy, "legacy",
        "No puedo leer un Excel viejo (.xls). Guardalo como Libro de Excel (.xlsx) o como CSV UTF-8 y mandalo de nuevo.")]
    [InlineData(StructuredAttachmentRejection.Protected, "protected",
        "No puedo leer una planilla con contraseña. Guardala sin contraseña y mandala de nuevo.")]
    [InlineData(StructuredAttachmentRejection.MacroEnabled, "macroEnabled",
        "No puedo leer un Excel con macros o binario (.xlsm o .xlsb). Guardalo como Libro de Excel (.xlsx) y mandalo de nuevo.")]
    [InlineData(StructuredAttachmentRejection.TooLarge, "tooLarge",
        "La planilla pasa el tope de 10 MB, 10.000 filas y 64 columnas. Achicala o dividila y mandala de nuevo.")]
    [InlineData(StructuredAttachmentRejection.Unreadable, "unreadable",
        "No puedo leer la planilla. Guardala como Libro de Excel (.xlsx), o un CSV como CSV UTF-8, y mandala de nuevo.")]
    [InlineData(StructuredAttachmentRejection.Empty, "empty",
        "La planilla no tiene filas con datos. Revisá que su primera hoja tenga lo que querés usar y mandala de nuevo.")]
    public async Task Each_reason_is_kept_without_a_copy_and_answered_once_with_its_remedy(StructuredAttachmentRejection rejection, string reason, string text)
    {
        var f = new WhatsAppSpreadsheetFixture { Preparation = StructuredAttachmentPreparation.Rejected(rejection) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppSpreadsheetState.Rejected, reason), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Equal((0, (string?)null), (f.Files.WriteCount, f.Message.SpreadsheetStorageKey));
        var reply = Assert.Single(f.Replies);
        Assert.Equal((WhatsAppMessageOrigin.Assistant, WhatsAppOutgoingMessageKind.Text, text), (reply.Origin, reply.Kind, reply.Body));
        Assert.Equal(f.Host.Context.Subject.Id, reply.RecipientUserId);
        var reference = Assert.Single(f.Host.Requests.Rows);
        Assert.Equal((f.Message.Id, WhatsAppAssistantRequestKind.Automatic, WhatsAppAssistantRequestState.Sent, (Guid?)reply.Id),
            (reference.SourceMessageId, reference.Kind, reference.State, reference.OutgoingId));
        Assert.Equal((f.Host.Context.Subject.Id, f.Host.Context.Subject.Id), (reference.ActorUserId, reference.SubjectUserId));
        Assert.Equal(1, f.Downloads);
    }

    [Fact]
    public async Task The_reason_is_answered_in_the_language_of_the_person()
    {
        var f = new WhatsAppSpreadsheetFixture { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected) };
        await f.Host.Context.Users.UpdateProfileAsync(f.Host.Context.Subject.Id, "Fictitious subject", "en", "America/Argentina/Buenos_Aires", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("I cannot read a password-protected spreadsheet. Save it without the password and send it again.", Assert.Single(f.Replies).Body);
    }

    [Fact]
    public async Task A_size_over_the_limit_declared_by_Meta_is_answered_as_too_large_without_reading_the_file()
    {
        var f = new WhatsAppSpreadsheetFixture { DownloadError = FileErrors.TooLarge };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal((WhatsAppSpreadsheetState.Rejected, "tooLarge"), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.StartsWith("La planilla pasa el tope de 10 MB", Assert.Single(f.Replies).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bytes_over_the_limit_are_too_large_even_when_Meta_declared_less()
    {
        var f = new WhatsAppSpreadsheetFixture(new byte[AssistantAttachmentInput.MaximumBytes + 1]);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal(0, f.Files.WriteCount);
        Assert.Equal("tooLarge", f.Message.SpreadsheetFailureCode);
        Assert.Single(f.Replies);
    }

    [Fact]
    public async Task Bytes_that_do_not_match_the_hash_Meta_sent_are_unavailable_without_being_read()
    {
        var f = new WhatsAppSpreadsheetFixture(metaHash: new string('c', 64));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, FileErrors.HashMismatch.Code), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task A_download_that_fails_is_retried_and_ends_unavailable_after_five_failures_and_fifteen_minutes()
    {
        var f = new WhatsAppSpreadsheetFixture { DownloadError = FileErrors.Unavailable };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
            Assert.Equal(WhatsAppSpreadsheetState.Pending, f.Message.SpreadsheetState);
            f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(3));
        }
        Assert.Equal((5, FileErrors.Unavailable.Code), (f.Message.SpreadsheetFailureCount, f.Message.SpreadsheetFailureCode));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(WhatsAppSpreadsheetState.Unavailable, f.Message.SpreadsheetState);
        Assert.Equal(6, f.Downloads);
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task A_copy_left_by_an_attempt_that_did_not_commit_is_read_back_and_kept()
    {
        var f = new WhatsAppSpreadsheetFixture();
        await f.Files.WriteOnceAsync(f.Key, new MemoryStream(f.Content), "text/csv", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(1, f.Files.WriteCount);
        Assert.Equal(WhatsAppSpreadsheetState.Stored, f.Message.SpreadsheetState);
    }

    [Fact]
    public async Task A_different_copy_under_the_same_key_is_never_taken_as_ours()
    {
        var f = new WhatsAppSpreadsheetFixture();
        await f.Files.WriteOnceAsync(f.Key, new MemoryStream([1, 2, 3]), "text/csv", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, FileErrors.HashMismatch.Code), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Null(f.Message.SpreadsheetStorageKey);
    }

    [Fact]
    public async Task A_reason_outside_the_reply_window_is_kept_without_answering()
    {
        var f = new WhatsAppSpreadsheetFixture { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty) };
        f.Host.Context.Clock.Advance(TimeSpan.FromHours(25));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppSpreadsheetState.Rejected, "empty"), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Host.Requests.Rows);
    }

    // The reader leaves the rejection out of the conversation (proven with PostgreSQL): it names no change of its own.
    [Fact]
    public async Task A_rejection_answers_with_its_own_request_as_the_proposal_and_takes_no_change()
    {
        var f = new WhatsAppSpreadsheetFixture { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Legacy) };
        f.Host.Reader.ChangeRequest = Guid.NewGuid();

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        var reference = Assert.Single(f.Host.Requests.Rows);
        Assert.Equal((reference.RequestId, (Guid?)null, (Guid?)null), (reference.ProposalId!.Value, reference.ChangeRequestId, reference.PreviousRequestId));
    }

    [Fact]
    public async Task A_fault_downloading_reading_or_copying_counts_as_a_failure_with_the_usual_scale()
    {
        var f = new WhatsAppSpreadsheetFixture { BeforeDownload = () => throw new InvalidOperationException("Fictitious fault.") };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
            Assert.Equal(WhatsAppSpreadsheetState.Pending, f.Message.SpreadsheetState);
            f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(3));
        }
        Assert.Equal((5, FileErrors.Unavailable.Code), (f.Message.SpreadsheetFailureCount, f.Message.SpreadsheetFailureCode));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(WhatsAppSpreadsheetState.Unavailable, f.Message.SpreadsheetState);
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task An_account_whose_confirmed_number_is_not_the_chat_one_keeps_it_without_downloading_it()
    {
        var f = new WhatsAppSpreadsheetFixture();
        await f.Host.Context.Users.ArrangeAsync(users => users.SetPhoneAsync(f.Host.Context.Subject.Id,
            Domain.ValueObjects.PhoneNumber.Create("+5493410000000").Value, true, Ct));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, WhatsAppSpreadsheetState.Unavailable), (f.Downloads, f.Message.SpreadsheetState));
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("link")]
    public async Task A_change_of_mode_or_link_while_downloading_leaves_it_unavailable(string change)
    {
        var f = new WhatsAppSpreadsheetFixture();
        f.BeforeDownload = () =>
        {
            if (change == "mode") f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
            else { f.Contact.UnlinkUser(); f.Contact.LinkUser(f.Host.Context.Subject.Id); }
        };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(1, f.Downloads);
        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, (string?)null), (f.Message.SpreadsheetState, f.Message.SpreadsheetStorageKey));
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task An_owner_that_is_not_installed_leaves_it_unavailable_without_answering()
    {
        var f = new WhatsAppSpreadsheetFixture { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Unavailable) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, "unavailable"), (f.Message.SpreadsheetState, f.Message.SpreadsheetFailureCode));
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task Without_a_pending_spreadsheet_nothing_is_opened()
    {
        var f = new WhatsAppSpreadsheetFixture();
        f.Message.SetSpreadsheetUnavailable("Other");

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0), (f.Downloads, f.Host.UnitOfWork.Transactions));
    }

    [Fact]
    public void The_limit_of_a_spreadsheet_is_the_engine_one_in_every_layer() =>
        Assert.Equal(AssistantAttachmentInput.MaximumBytes, WhatsAppMessage.MaxSpreadsheetBytes);
}
