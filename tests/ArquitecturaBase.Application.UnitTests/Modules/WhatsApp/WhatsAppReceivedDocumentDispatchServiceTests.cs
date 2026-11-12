using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// Task 8 of the documents (decision 10): who gets a received document downloaded and who not, the limit and Meta's hash, the copy, each
// file that cannot be read answered once and registered discarded, the reading accepted only when the document will be used, the
// unlinked number and the account that can neither load nor build forms, the retries and what the engine's reading leaves.
public sealed class WhatsAppReceivedDocumentDispatchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly StructuredDocumentReading Reading = new(StructuredDocumentReadability.Readable, null, "es", "Total $ 150.000", false);

    [Fact]
    public async Task A_person_who_can_load_gets_it_downloaded_prepared_copied_and_read_outside_the_transaction()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal([StructuredAttachmentLimits.MaximumBytes], f.DownloadLimits);
        Assert.Equal(["comprobante.pdf"], f.PreparedNames);
        Assert.Equal(["contact", "message"], f.Locks);
        Assert.Equal((1, 1), (f.Host.UnitOfWork.Transactions, f.Host.UnitOfWork.Commits));
        Assert.Equal((WhatsAppDocumentState.Reading, f.Key(), (long?)f.Content.Length, WhatsAppReceivedDocumentFixture.Hash(f.Content), (int?)2),
            (f.Message.DocumentState, f.Message.DocumentStorageKey, f.Message.DocumentSize, f.Message.DocumentCopySha256, f.Message.DocumentPages));
        Assert.Equal(f.Content, await f.StoredAsync(f.Key()));
        var work = Assert.IsType<Application.Modules.WhatsApp.Models.WhatsAppAssistantWorkResponse>(result.Value);
        Assert.Equal(f.Message.DocumentRequestId, work.RequestId);
        var operation = Assert.IsType<AssistantOperationRequest>(work.Operation);
        Assert.Equal((work.RequestId, "", (AssistantAudioInput?)null, (AssistantAttachmentInput?)null), (operation.RequestId, operation.Text, operation.Audio, operation.Attachment));
        Assert.Equal(new AssistantDocumentInput(f.Key(), "comprobante.pdf", "application/pdf", f.Content.Length, WhatsAppReceivedDocumentFixture.Hash(f.Content), 2,
            f.Message.OccurredAtUtc.AddDays(90)), operation.Document);
        var subject = f.Host.Context.Subject.Id;
        Assert.Equal(("whatsapp-assistant", subject, (Guid?)subject, AssistantInteractionMode.Assistant, 0, 0),
            (operation.Context.ChannelId, operation.Context.ActorUserId, operation.Context.SubjectUserId, operation.Context.InteractionMode,
                operation.Context.AllowedCapabilities.Count, operation.Context.AllowedTerminals.Count));
        Assert.Equal((subject, f.Contact.LinkRevision), (f.Message.DocumentActorUserId, f.Message.DocumentLinkRevision));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Registrations);
        Assert.Empty(f.Host.Requests.Rows);
    }

    // Decision 10: the request may also be the text or the audio that follows. While no form reads documents, one without a request
    // waits for it, still pending and without being downloaded, and then stays in the chat as today.
    [Fact]
    public async Task While_no_form_reads_documents_one_without_a_request_waits_for_it_without_downloading_and_then_stays_as_today()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { Loading = StructuredDocumentLoading.None, MayAuthor = true };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((WhatsAppDocumentState.Pending, (DateTime?)f.Message.OccurredAtUtc.AddMinutes(10)),
            (f.Message.DocumentState, f.Message.DocumentRequestAwaitedUntilUtc));
        f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(9));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((1, WhatsAppDocumentState.Pending), (f.Host.UnitOfWork.Transactions, f.Message.DocumentState));
        f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0), (f.Downloads, f.Files.WriteCount));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Registrations);
    }

    // The other order: a text that follows (or an audio once transcribed) ends the wait before its time. Taking it as the request (the
    // contiguous text or audio, which HasRequest will pair) is the circuit's; here the document stays as today.
    [Fact]
    public async Task A_message_that_follows_ends_the_wait_before_its_time()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { Loading = StructuredDocumentLoading.None, MayAuthor = true };
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(2));
        f.FollowedByText = true;

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((2, 0), (f.Host.UnitOfWork.Transactions, f.Downloads));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
    }

    // Only a linked account in the assistant that could have it read waits; anyone else stays as today at once.
    [Theory]
    [InlineData("cannotBuild")]
    [InlineData("manual")]
    [InlineData("otherNumber")]
    [InlineData("withoutStorage")]
    [InlineData("installationDoesNotRead")]
    public async Task Without_someone_who_could_ask_for_it_a_document_never_waits(string who)
    {
        var f = new WhatsAppReceivedDocumentFixture(storage: who != "withoutStorage", caption: null)
        {
            Loading = StructuredDocumentLoading.None, MayAuthor = who != "cannotBuild", InstallationReadsDocuments = who != "installationDoesNotRead",
        };
        if (who == "manual") f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        if (who == "otherNumber")
            await f.Host.Context.Users.ArrangeAsync(users => users.SetPhoneAsync(f.Host.Context.Subject.Id,
                Domain.ValueObjects.PhoneNumber.Create("+5493410000000").Value, true, Ct));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, (DateTime?)null, 0), (f.Message.DocumentState, f.Message.DocumentRequestAwaitedUntilUtc, f.Downloads));
    }

    [Fact]
    public async Task While_no_form_reads_documents_one_with_a_request_is_read_for_someone_who_builds_forms()
    {
        var f = new WhatsAppReceivedDocumentFixture { Loading = StructuredDocumentLoading.None, MayAuthor = true };

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Equal(1, f.Downloads);
        Assert.Equal(WhatsAppDocumentState.Reading, f.Message.DocumentState);
        Assert.NotNull(result.Value?.Operation?.Document);
    }

    // The request is its caption here; pairing it with the contiguous text or audio is the circuit's.
    [Fact]
    public async Task Someone_who_only_builds_forms_keeps_a_document_without_a_request_stored_in_the_chat_without_spending_a_reading()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { Loading = new(true, false), MayAuthor = true };

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Null(result.Value);
        Assert.Equal((WhatsAppDocumentState.Stored, (Guid?)null), (f.Message.DocumentState, f.Message.DocumentRequestId));
        Assert.Equal(f.Content, await f.StoredAsync(f.Key()));
        var again = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);
        Assert.Equal((1, (Application.Modules.WhatsApp.Models.WhatsAppAssistantWorkResponse?)null), (f.Downloads, again.Value));
    }

    [Fact]
    public async Task A_word_is_read_with_what_its_owner_prepared_without_the_engine()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null, contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            fileName: "informe.docx");
        f.Preparation = WhatsAppReceivedDocumentFixture.Prepared(StructuredDocumentKind.Docx, f.Content, reading: Reading);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Null(result.Value);
        Assert.Equal((WhatsAppDocumentState.Read, f.Key(".docx"), (Guid?)null), (f.Message.DocumentState, f.Message.DocumentStorageKey, f.Message.DocumentRequestId));
        Assert.Equal(Reading, JsonSerializer.Deserialize<StructuredDocumentReading>(f.Message.DocumentReadingJson!, WhatsAppReceivedDocumentReadingPolicy.ReadingJson));
        Assert.Contains("\"readability\":\"readable\"", f.Message.DocumentReadingJson, StringComparison.Ordinal);
    }

    // The installation reads documents where Home would (documentReading): without it, a PDF or a photo is not downloaded to be read.
    [Theory]
    [InlineData("application/pdf", "comprobante.pdf")]
    [InlineData("image/jpeg", null)]
    [InlineData("image/png", null)]
    public async Task Where_the_installation_does_not_read_documents_a_pdf_or_a_photo_is_not_downloaded(string contentType, string? fileName)
    {
        var f = new WhatsAppReceivedDocumentFixture(contentType: contentType, fileName: fileName) { InstallationReadsDocuments = false };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0), (f.Downloads, f.Files.WriteCount));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Registrations);
    }

    [Fact]
    public async Task Where_the_installation_does_not_read_documents_a_word_is_still_read_with_what_its_owner_prepared()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null, contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            fileName: "informe.docx") { InstallationReadsDocuments = false };
        f.Preparation = WhatsAppReceivedDocumentFixture.Prepared(StructuredDocumentKind.Docx, f.Content, reading: Reading);

        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);

        Assert.Equal(WhatsAppDocumentState.Read, f.Message.DocumentState);
    }

    [Fact]
    public async Task Where_the_installation_does_not_read_documents_a_pdf_declared_otherwise_stays_stored_without_a_reading()
    {
        var f = new WhatsAppReceivedDocumentFixture(contentType: "application/octet-stream", fileName: "comprobante") { InstallationReadsDocuments = false };

        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);

        Assert.Equal((WhatsAppDocumentState.Stored, (Guid?)null, 1), (f.Message.DocumentState, f.Message.DocumentRequestId, f.Downloads));
    }

    [Fact]
    public async Task A_photo_is_copied_as_a_jpeg_and_read_as_one_page()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null, contentType: "image/jpeg", fileName: null);
        f.Preparation = WhatsAppReceivedDocumentFixture.Prepared(StructuredDocumentKind.Jpeg, f.Content);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Equal(("image.jpg", f.Key(".jpg"), (int?)null), (f.Message.DocumentFileName, f.Message.DocumentStorageKey, f.Message.DocumentPages));
        Assert.Equal(("image/jpeg", 1, "image.jpg"), (result.Value!.Operation!.Document!.ContentType, result.Value.Operation.Document.Pages, result.Value.Operation.Document.Name));
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("inactive")]
    [InlineData("otherNumber")]
    [InlineData("withoutStorage")]
    public async Task Anyone_else_keeps_it_as_today_without_downloading_it(string who)
    {
        var f = new WhatsAppReceivedDocumentFixture(storage: who != "withoutStorage");
        if (who == "manual") f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        if (who == "inactive") await f.Host.Context.Users.ArrangeAsync(users => users.SetActiveAsync(f.Host.Context.Subject.Id, false, Ct));
        if (who == "otherNumber")
            await f.Host.Context.Users.ArrangeAsync(users => users.SetPhoneAsync(f.Host.Context.Subject.Id,
                Domain.ValueObjects.PhoneNumber.Create("+5493410000000").Value, true, Ct));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0, 0), (f.Downloads, f.PreparedNames.Count, f.Files.WriteCount));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Registrations);
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((0, 1), (f.Downloads, f.Host.UnitOfWork.Transactions));
    }

    // DOC4 draws the line in a conversation an operator attends: an unlinked number is not read in either mode.
    [Theory]
    [InlineData(WhatsAppConversationMode.Assistant)]
    [InlineData(WhatsAppConversationMode.Manual)]
    public async Task An_unlinked_number_is_not_downloaded_answered_nor_registered_and_the_chat_says_why(WhatsAppConversationMode mode)
    {
        var f = new WhatsAppReceivedDocumentFixture();
        f.Contact.UnlinkUser();
        f.Contact.ChangeMode(mode);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0), (f.Downloads, f.Files.WriteCount));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.NotLinkedCode),
            (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
        Assert.Empty(f.Registrations);
        Assert.Empty(f.Host.Requests.Rows);
        Assert.All(f.LoadingAsked, asked => Assert.Equal(Guid.Empty, asked));
    }

    [Fact]
    public async Task An_unlinked_number_keeps_it_as_today_while_no_form_reads_documents()
    {
        var f = new WhatsAppReceivedDocumentFixture { Loading = StructuredDocumentLoading.None };
        f.Contact.UnlinkUser();

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
    }

    [Fact]
    public async Task A_linked_account_that_can_neither_load_nor_build_forms_is_told_once_and_handed_to_a_person()
    {
        var f = new WhatsAppReceivedDocumentFixture { Loading = new(true, false) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((0, 0), (f.Downloads, f.Files.WriteCount));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.NotAllowedCode),
            (f.Message.DocumentState, f.Message.DocumentFailureCode));
        var reply = Assert.Single(f.Replies);
        Assert.Equal((WhatsAppMessageOrigin.Assistant, WhatsAppOutgoingMessageKind.Text, f.Host.Context.Subject.Id), (reply.Origin, reply.Kind, reply.RecipientUserId));
        Assert.Equal("Gracias, recibí tu documento. Tu cuenta no puede cargarlo desde acá: se lo pasé a una persona del equipo.", reply.Body);
        Assert.Equal((WhatsAppConversationMode.Manual, true, WhatsAppReceivedDocumentReplyPolicy.HandoffReasonCode),
            (f.Contact.Mode, f.Contact.NeedsPerson, f.Contact.HandoffReasonCode));
        Assert.Equal("Mandó un documento y su cuenta no puede cargarlo ni armar formularios.", f.Contact.HandoffReason);
        var reference = Assert.Single(f.Host.Requests.Rows);
        Assert.Equal((f.Message.Id, WhatsAppAssistantRequestState.Sent, (Guid?)reply.Id, (Guid?)reference.RequestId),
            (reference.SourceMessageId, reference.State, reference.OutgoingId, reference.ProposalId));
        Assert.Empty(f.Registrations);
    }

    [Fact]
    public async Task The_refusal_is_said_in_the_language_of_the_person()
    {
        var f = new WhatsAppReceivedDocumentFixture { Loading = new(true, false) };
        await f.Host.Context.Users.UpdateProfileAsync(f.Host.Context.Subject.Id, "Fictitious subject", "en", "America/Argentina/Buenos_Aires", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("Thanks, I got your document. Your account can't load it from here: I passed it to someone on the team.", Assert.Single(f.Replies).Body);
        Assert.Equal("Sent a document and their account can't load it or build forms.", f.Contact.HandoffReason);
    }

    [Theory]
    [InlineData(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf, "protected", ".pdf",
        "El PDF tiene contraseña y no lo puedo abrir. Mandame uno sin contraseña o una captura del documento.")]
    [InlineData(StructuredAttachmentRejection.TooManyPages, StructuredAttachmentFileKind.Pdf, "tooManyPages", ".pdf",
        "El PDF tiene más de 10 páginas y no lo puedo leer. Mandame sólo las páginas que importan.")]
    [InlineData(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Pdf, "unreadable", ".pdf",
        "No pude abrir el archivo. Mandámelo de nuevo como PDF o como foto.")]
    [InlineData(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word, "empty", ".docx",
        "El Word está vacío. ¿Me mandás el que tiene el contenido?")]
    public async Task A_file_that_cannot_be_read_keeps_its_reason_is_answered_once_and_goes_to_the_inbox_discarded(StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind kind, string reason, string extension, string text)
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { Preparation = StructuredAttachmentPreparation.Rejected(rejection, kind) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Rejected, reason, (string?)null), (f.Message.DocumentState, f.Message.DocumentFailureCode, f.Message.DocumentStorageKey));
        var reply = Assert.Single(f.Replies);
        Assert.Equal((WhatsAppMessageOrigin.Assistant, text), (reply.Origin, reply.Body));
        var reference = Assert.Single(f.Host.Requests.Rows);
        Assert.Equal((f.Message.Id, WhatsAppAssistantRequestState.Sent, (Guid?)reply.Id), (reference.SourceMessageId, reference.State, reference.OutgoingId));
        var registered = Assert.Single(f.Registrations);
        Assert.Equal((WhatsAppReceivedDocumentPreparationPolicy.ChannelId, f.Message.Id.ToString("N"), f.Host.Context.Subject.Id, "es", f.Message.OccurredAtUtc),
            (registered.ChannelId, registered.SourceId, registered.SenderUserId, registered.Culture, registered.ReceivedAtUtc));
        Assert.Equal((StructuredDocumentNotRead.Rejected, (StructuredAttachmentRejection?)rejection, (StructuredDocumentReading?)null),
            (registered.NotRead, registered.Rejection, registered.Reading));
        Assert.Equal((f.Key(extension), "comprobante.pdf", (long)f.Content.Length, WhatsAppReceivedDocumentFixture.Hash(f.Content)),
            (registered.Copy!.StorageKey, registered.Copy.Name, registered.Copy.Size, registered.Copy.Sha256));
        Assert.Null(registered.File);
        Assert.Equal(f.Content, await f.StoredAsync(f.Key(extension)));
        Assert.Equal(1, f.Downloads);
    }

    // Every file that cannot be opened goes to the inbox discarded; one it must not keep (an old Word, one with macros, a protected one or
    // anything else) goes without its bytes: its name, the type Meta declared, its size and the reason.
    [Theory]
    [InlineData(StructuredAttachmentRejection.Legacy, StructuredAttachmentFileKind.Word, "legacy", "application/msword", "informe.doc",
        "Es un archivo de un formato viejo (.doc) y no lo puedo leer. ¿Me lo mandás en PDF?")]
    [InlineData(StructuredAttachmentRejection.MacroEnabled, StructuredAttachmentFileKind.Word, "macroEnabled",
        "application/vnd.ms-word.document.macroEnabled.12", "informe.docm", "El Word tiene macros y no lo abro. Mandámelo sin macros (.docx) o en PDF.")]
    [InlineData(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Word, "protected",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "informe.docx",
        "El archivo tiene contraseña y no lo puedo abrir. Mandámelo sin contraseña o en PDF.")]
    [InlineData(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown, "unreadable", "application/zip", "fotos.zip",
        "No pude abrir el archivo. Mandámelo de nuevo como PDF o como foto.")]
    public async Task A_file_the_inbox_must_not_keep_goes_to_it_discarded_without_its_bytes(StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind kind, string reason, string contentType, string fileName, string text)
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null, contentType: contentType, fileName: fileName)
            { Preparation = StructuredAttachmentPreparation.Rejected(rejection, kind) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Rejected, reason), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Equal(text, Assert.Single(f.Replies).Body);
        var registered = Assert.Single(f.Registrations);
        Assert.Equal((StructuredDocumentNotRead.Rejected, (StructuredAttachmentRejection?)rejection, (StructuredDocumentCopy?)null),
            (registered.NotRead, registered.Rejection, registered.Copy));
        // The type as Meta declared it, without its parameters and in lowercase, as media types compare.
        Assert.Equal(new StructuredDocumentFile(fileName, contentType.ToLowerInvariant(), f.Content.Length), registered.File);
        Assert.Equal(0, f.Files.WriteCount);
    }

    // A browser would decode a photo over the pixel limit: its copy is never kept.
    [Fact]
    public async Task A_photo_that_is_too_big_is_answered_as_such_and_its_copy_is_never_kept()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var f = new WhatsAppReceivedDocumentFixture(jpeg, caption: null, contentType: "image/jpeg", fileName: null)
        {
            Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Image),
        };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("La foto es demasiado grande y no la puedo leer. Mandame una más chica o una captura.", Assert.Single(f.Replies).Body);
        Assert.Equal(0, f.Files.WriteCount);
        var registered = Assert.Single(f.Registrations);
        Assert.Equal(((StructuredDocumentCopy?)null, new StructuredDocumentFile("image.jpg", "image/jpeg", jpeg.Length)), (registered.Copy, registered.File));
    }

    // A spreadsheet that came as a document, or another file, is never told it is an empty Word or over the size: it could not be opened.
    [Theory]
    [InlineData(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Spreadsheet)]
    [InlineData(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Pdf)]
    [InlineData(StructuredAttachmentRejection.TooLarge, StructuredAttachmentFileKind.Word)]
    public async Task Only_a_word_is_empty_and_only_a_photo_or_an_unknown_file_is_too_big(StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind kind)
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { Preparation = StructuredAttachmentPreparation.Rejected(rejection, kind) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("No pude abrir el archivo. Mandámelo de nuevo como PDF o como foto.", Assert.Single(f.Replies).Body);
    }

    // The inbox takes the documents of someone who can load: when it cannot register a discarded one, it is retried with the usual
    // scale and answered once it is registered.
    [Fact]
    public async Task A_rejected_file_the_inbox_could_not_register_is_retried_for_someone_who_can_load()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null)
        {
            Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf),
            RegistrationReply = StructuredDocumentReplyKind.Unavailable,
        };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((WhatsAppDocumentState.Pending, 1, FileErrors.Unavailable.Code),
            (f.Message.DocumentState, f.Message.DocumentFailureCount, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
        f.RegistrationReply = null;
        f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Rejected, "protected"), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Single(f.Replies);
        Assert.Equal((2, 1), (f.Registrations.Count, f.Files.WriteCount));
    }

    [Fact]
    public async Task A_rejected_file_of_someone_who_only_builds_forms_is_answered_although_the_inbox_does_not_take_it()
    {
        var f = new WhatsAppReceivedDocumentFixture
        {
            Loading = new(true, false), MayAuthor = true, RegistrationReply = StructuredDocumentReplyKind.Unavailable,
            Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf),
        };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(WhatsAppDocumentState.Rejected, f.Message.DocumentState);
        Assert.Single(f.Replies);
        Assert.Single(f.Registrations);
    }

    [Fact]
    public async Task The_reason_is_answered_in_the_language_of_the_person()
    {
        var f = new WhatsAppReceivedDocumentFixture
        {
            Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Protected, StructuredAttachmentFileKind.Pdf),
        };
        await f.Host.Context.Users.UpdateProfileAsync(f.Host.Context.Subject.Id, "Fictitious subject", "en", "America/Argentina/Buenos_Aires", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("The PDF has a password and I can't open it. Send me one without a password or a screenshot of the document.", Assert.Single(f.Replies).Body);
        Assert.Equal("en", Assert.Single(f.Registrations).Culture);
    }

    // Over the limit Meta declares there are no bytes: the inbox gets the name, the type and no size.
    [Fact]
    public async Task A_size_over_the_limit_declared_by_Meta_is_too_large_without_reading_the_file_and_goes_to_the_inbox_without_it()
    {
        var f = new WhatsAppReceivedDocumentFixture { DownloadError = FileErrors.TooLarge };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal((WhatsAppDocumentState.Rejected, "tooLarge"), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Equal("El archivo pasa el tope de 10 MB y no lo puedo leer. Mandame uno más liviano o una captura.", Assert.Single(f.Replies).Body);
        var registered = Assert.Single(f.Registrations);
        Assert.Equal(((StructuredDocumentCopy?)null, new StructuredDocumentFile("comprobante.pdf", "application/pdf", null), (StructuredAttachmentRejection?)StructuredAttachmentRejection.TooLarge),
            (registered.Copy, registered.File, registered.Rejection));
    }

    [Fact]
    public async Task Bytes_over_the_limit_are_too_large_even_when_Meta_declared_less()
    {
        var f = new WhatsAppReceivedDocumentFixture(new byte[StructuredAttachmentLimits.MaximumBytes + 1]);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal((0, "tooLarge"), (f.Files.WriteCount, f.Message.DocumentFailureCode));
        Assert.Equal(StructuredAttachmentLimits.MaximumBytes + 1, Assert.Single(f.Registrations).File!.Size);
    }

    // A registration that could never succeed is not retried as one that may pass: an empty file has no size and a type longer than the
    // port takes goes as a generic one.
    [Fact]
    public async Task An_empty_file_goes_to_the_inbox_without_a_copy_nor_a_size()
    {
        var f = new WhatsAppReceivedDocumentFixture([], caption: null, contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            fileName: "vacio.docx") { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Word) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        var registered = Assert.Single(f.Registrations);
        Assert.Equal(((StructuredDocumentCopy?)null, (long?)null), (registered.Copy, registered.File!.Size));
        Assert.Equal((WhatsAppDocumentState.Rejected, 0), (f.Message.DocumentState, f.Files.WriteCount));
    }

    [Fact]
    public async Task A_declared_type_longer_than_the_port_takes_goes_as_a_generic_one()
    {
        var type = "application/" + new string('x', StructuredDocumentFile.MaxMediaTypeLength);
        var f = new WhatsAppReceivedDocumentFixture(caption: null, contentType: type, fileName: "raro.bin")
            { Preparation = StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown) };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal("application/octet-stream", Assert.Single(f.Registrations).File!.MediaType);
    }

    // A registration without bytes is retried for someone who loads, as one with them.
    [Fact]
    public async Task A_file_without_bytes_the_inbox_could_not_register_is_retried_for_someone_who_can_load()
    {
        var f = new WhatsAppReceivedDocumentFixture { DownloadError = FileErrors.TooLarge, RegistrationReply = StructuredDocumentReplyKind.Unavailable };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
        Assert.Equal((WhatsAppDocumentState.Pending, 1), (f.Message.DocumentState, f.Message.DocumentFailureCount));
        f.RegistrationReply = null;
        f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Rejected, 2, 1), (f.Message.DocumentState, f.Registrations.Count, f.Replies.Count));
    }

    [Fact]
    public async Task Bytes_that_do_not_match_the_hash_Meta_sent_are_unavailable_without_being_read()
    {
        var f = new WhatsAppReceivedDocumentFixture(metaHash: new string('c', 64));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Empty(f.PreparedNames);
        Assert.Equal((WhatsAppDocumentState.Unavailable, FileErrors.HashMismatch.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task A_different_copy_under_the_same_key_is_never_taken_as_ours()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        await f.Files.WriteOnceAsync(f.Key(), new MemoryStream([1, 2, 3]), "application/pdf", Ct);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, FileErrors.HashMismatch.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Null(f.Message.DocumentStorageKey);
    }

    [Fact]
    public async Task A_download_that_fails_is_retried_and_ends_unavailable_after_five_failures_and_fifteen_minutes()
    {
        var f = new WhatsAppReceivedDocumentFixture { DownloadError = FileErrors.Unavailable };

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);
            Assert.Equal(WhatsAppDocumentState.Pending, f.Message.DocumentState);
            f.Host.Context.Clock.Advance(TimeSpan.FromMinutes(3));
        }
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, FileErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Equal(6, f.Downloads);
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task A_fault_downloading_reading_or_copying_counts_as_a_failure_with_the_usual_scale()
    {
        var f = new WhatsAppReceivedDocumentFixture { BeforeDownload = () => throw new InvalidOperationException("Fictitious fault.") };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Pending, 1, FileErrors.Unavailable.Code),
            (f.Message.DocumentState, f.Message.DocumentFailureCount, f.Message.DocumentFailureCode));
    }

    [Theory]
    [InlineData("mode")]
    [InlineData("link")]
    public async Task A_change_of_mode_or_link_while_downloading_leaves_it_unavailable(string change)
    {
        var f = new WhatsAppReceivedDocumentFixture();
        f.BeforeDownload = () =>
        {
            if (change == "mode") f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
            else { f.Contact.UnlinkUser(); f.Contact.LinkUser(f.Host.Context.Subject.Id); }
        };

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal(1, f.Downloads);
        Assert.Equal((WhatsAppDocumentState.Unavailable, (string?)null), (f.Message.DocumentState, f.Message.DocumentStorageKey));
        Assert.Empty(f.Replies);
    }

    // What is decided under the lock applies: a person who stopped loading while it was downloaded is told and handed to a person.
    [Fact]
    public async Task A_permission_lost_while_downloading_applies_the_decision_taken_under_the_lock()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        f.BeforeDownload = () => f.Loading = new(true, false);

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((1, (string?)null), (f.Downloads, f.Message.DocumentStorageKey));
        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.NotAllowedCode),
            (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Single(f.Replies);
    }

    // A change between the two decisions is never lost: one that now needs a download leaves it pending, and the next round downloads it.
    [Fact]
    public async Task A_document_that_qualifies_under_the_lock_stays_pending_and_is_downloaded_in_the_next_round()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { MayAuthor = true };
        f.LoadingAnswers.Enqueue(StructuredDocumentLoading.None);
        f.LoadingAnswers.Enqueue(new(true, true));

        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        Assert.Equal((WhatsAppDocumentState.Pending, (DateTime?)null, 0), (f.Message.DocumentState, f.Message.DocumentRequestAwaitedUntilUtc, f.Downloads));
        var next = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Equal((WhatsAppDocumentState.Reading, 1), (f.Message.DocumentState, f.Downloads));
        Assert.NotNull(next.Value?.Operation?.Document);
    }

    [Fact]
    public async Task A_refusal_decided_under_the_lock_applies_although_the_first_look_kept_it()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null);
        f.LoadingAnswers.Enqueue(StructuredDocumentLoading.None);
        f.LoadingAnswers.Enqueue(new(true, false));

        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.NotAllowedCode),
            (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Single(f.Replies);
    }

    // A download taken with another decision is not used: the next round downloads with the one that holds.
    [Fact]
    public async Task A_download_decided_otherwise_under_the_lock_is_taken_again_in_the_next_round()
    {
        var f = new WhatsAppReceivedDocumentFixture(caption: null) { MayAuthor = true };
        f.LoadingAnswers.Enqueue(new(true, false));
        f.LoadingAnswers.Enqueue(new(true, true));

        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        Assert.Equal((WhatsAppDocumentState.Pending, (string?)null, 1), (f.Message.DocumentState, f.Message.DocumentStorageKey, f.Downloads));
        var next = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.Equal((WhatsAppDocumentState.Reading, 2), (f.Message.DocumentState, f.Downloads));
        Assert.NotNull(next.Value?.Operation?.Document);
    }

    [Fact]
    public async Task A_completed_reading_is_kept_with_the_message()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;

        var completed = await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId, WhatsAppReceivedDocumentFixture.Completed(work.RequestId, Reading), Ct);

        Assert.True(completed.IsSuccess);
        Assert.Equal(WhatsAppDocumentState.Read, f.Message.DocumentState);
        Assert.Equal(Reading, JsonSerializer.Deserialize<StructuredDocumentReading>(f.Message.DocumentReadingJson!, WhatsAppReceivedDocumentReadingPolicy.ReadingJson));
        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
    }

    [Fact]
    public async Task A_reading_in_course_is_polled_with_its_sealed_context_without_downloading_again()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;
        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId,
            new AssistantOperationResponse(work.RequestId, "running", null, null, null), Ct)).IsSuccess);

        var polled = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;

        Assert.Equal(WhatsAppDocumentState.Reading, f.Message.DocumentState);
        Assert.Equal((work.RequestId, work.Operation!.Document), (polled.RequestId, polled.Operation!.Document));
        Assert.Equal((work.Operation.Context.ChannelId, work.Operation.Context.ActorUserId, work.Operation.Context.SubjectUserId, work.Operation.Context.Culture),
            (polled.Operation.Context.ChannelId, polled.Operation.Context.ActorUserId, polled.Operation.Context.SubjectUserId, polled.Operation.Context.Culture));
        Assert.Equal(1, f.Downloads);
    }

    [Theory]
    [InlineData("uncertain", WhatsAppDocumentState.Uncertain)]
    [InlineData("expired", WhatsAppDocumentState.Expired)]
    [InlineData("failed", WhatsAppDocumentState.Failed)]
    public async Task A_reading_that_ends_without_its_text_keeps_why(string state, WhatsAppDocumentState expected)
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;

        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId,
            new AssistantOperationResponse(work.RequestId, state, null, null, state == "expired" ? null : "Ai.Assistant.Failed"), Ct)).IsSuccess);

        Assert.Equal((expected, (string?)null), (f.Message.DocumentState, f.Message.DocumentReadingJson));
        Assert.Empty(f.Registrations);
    }

    [Fact]
    public async Task A_failure_that_may_pass_keeps_it_reading_and_another_ends_it_unavailable()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;
        var capacity = Error.TooManyRequests("Ai.Assistant.CapacityLimited", "Fictitious capacity.", new Dictionary<string, object?> { ["retryable"] = true });

        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId, Result.Failure<AssistantOperationResponse>(capacity), Ct)).IsSuccess);
        Assert.Equal(WhatsAppDocumentState.Reading, f.Message.DocumentState);
        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId, Result.Failure<AssistantOperationResponse>(WhatsAppAssistantErrors.Unavailable), Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        Assert.Empty(f.Registrations);
    }

    // The circuit says it (task 9); here the quota leaves the document registered unclassified, from its copy, and not read.
    [Fact]
    public async Task Over_the_day_reading_quota_it_goes_to_the_inbox_unclassified_without_being_read()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;
        var quota = Error.TooManyRequests("Ai.Assistant.QuotaExceeded", "Fictitious quota.", new Dictionary<string, object?> { ["retryAfter"] = 3600 });

        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId, Result.Failure<AssistantOperationResponse>(quota), Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, WhatsAppReceivedDocumentPreparationPolicy.QuotaCode), (f.Message.DocumentState, f.Message.DocumentFailureCode));
        var registered = Assert.Single(f.Registrations);
        Assert.Equal((StructuredDocumentNotRead.Quota, (StructuredAttachmentRejection?)null, (StructuredDocumentReading?)null, f.Message.Id.ToString("N")),
            (registered.NotRead, registered.Rejection, registered.Reading, registered.SourceId));
        Assert.Equal(new StructuredDocumentCopy(f.Key(), "comprobante.pdf", "application/pdf", f.Content.Length, WhatsAppReceivedDocumentFixture.Hash(f.Content), 2),
            registered.Copy);
        Assert.Null(registered.File);
        Assert.Empty(f.Replies);
    }

    [Fact]
    public async Task Losing_the_link_before_completing_leaves_the_reading_unavailable()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        var work = (await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value!;
        f.Contact.UnlinkUser();
        f.Contact.LinkUser(f.Host.Context.Subject.Id);

        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id, work.RequestId, WhatsAppReceivedDocumentFixture.Completed(work.RequestId, Reading), Ct)).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Unavailable, (string?)null), (f.Message.DocumentState, f.Message.DocumentReadingJson));
    }

    [Fact]
    public async Task A_reading_whose_content_expired_is_not_polled_again()
    {
        var f = new WhatsAppReceivedDocumentFixture();
        Assert.NotNull((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        f.Host.Context.Clock.Advance(TimeSpan.FromDays(91));

        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);

        Assert.Equal(WhatsAppDocumentState.Expired, f.Message.DocumentState);
    }

    [Fact]
    public void The_limits_of_a_document_are_the_attachment_ones_in_every_layer() =>
        Assert.Equal((StructuredAttachmentLimits.MaximumBytes, StructuredAttachmentLimits.MaximumPages),
            ((int)WhatsAppMessage.MaxDocumentBytes, WhatsAppMessage.MaxDocumentPages));
}
