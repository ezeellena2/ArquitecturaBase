using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// Task 8 of the documents plan: a photo, a PDF or a Word received by WhatsApp keeps Meta's descriptor and its caption; it is stored with
// its copy, rejected with its reason or unavailable once, and read only once it is used: a Word with what was prepared, the rest by the
// engine, with the request fixed once.
public sealed class WhatsAppMessageDocumentTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);
    private const string Reading = "{\"readability\":\"readable\",\"text\":\"Total $ 150.000\",\"truncated\":false}";

    private static WhatsAppMessage Message(string? caption = null) =>
        WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.document", WhatsAppMessageKind.ReceivedDocument, caption, null, Now);

    private static WhatsAppMessage Described(string? caption = "Cargalo en pagos")
    {
        var message = Message(caption);
        Assert.True(message.SetDocumentDescriptor("1234", "application/pdf", Hash, "comprobante.pdf"));
        return message;
    }

    private static WhatsAppMessage Stored(string? reading = null, int? pages = 2)
    {
        var message = Described();
        Assert.True(message.StoreDocument($"whatsapp/documents/{message.Id:N}.pdf", 42, Hash, pages, reading).IsSuccess);
        return message;
    }

    [Fact]
    public void The_received_document_is_a_kind_of_its_own_after_the_location_ones() =>
        Assert.Equal(15, (int)WhatsAppMessageKind.ReceivedDocument);

    [Fact]
    public void A_new_document_keeps_its_descriptor_and_its_caption_and_waits_to_be_downloaded()
    {
        var message = Described();

        Assert.Equal(WhatsAppDocumentState.Pending, message.DocumentState);
        Assert.Equal(("1234", "application/pdf", Hash, "comprobante.pdf"),
            (message.DocumentMediaId, message.DocumentContentType, message.DocumentSha256, message.DocumentFileName));
        Assert.Equal("Cargalo en pagos", message.Body);
        Assert.Null(message.DocumentStorageKey);
        Assert.Null(message.DocumentReadingJson);
        Assert.False(message.SetDocumentDescriptor("9999", "application/pdf", new string('b', 64), "otro.pdf"));
        Assert.Equal("1234", message.DocumentMediaId);
        Assert.True(message.SetDocumentDescriptor("1234", "application/pdf", Hash, "comprobante.pdf"));
    }

    [Fact]
    public void Meta_hash_is_kept_in_lowercase()
    {
        var message = Message();

        Assert.True(message.SetDocumentDescriptor("1234", "image/jpeg", new string('A', 64), null));

        Assert.Equal(new string('a', 64), message.DocumentSha256);
    }

    [Theory]
    [InlineData(WhatsAppMessageKind.Media)]
    [InlineData(WhatsAppMessageKind.Spreadsheet)]
    [InlineData(WhatsAppMessageKind.Text)]
    public void Only_an_inbound_received_document_takes_a_descriptor(WhatsAppMessageKind kind)
    {
        var message = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.other", kind, null, null, Now);

        Assert.False(message.SetDocumentDescriptor("1234", "application/pdf", Hash, "comprobante.pdf"));

        Assert.Null(message.DocumentState);
        Assert.Null(message.DocumentFileName);
    }

    [Theory]
    [InlineData(null, "application/pdf", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("id-not-digits", "application/pdf", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", "application/\npdf", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", "application/pdf", null)]
    [InlineData("1234", "application/pdf", "bad")]
    public void A_malformed_descriptor_stays_visible_with_its_name_but_unavailable(string? id, string? type, string? hash)
    {
        var message = Message("Mirá este comprobante");

        Assert.False(message.SetDocumentDescriptor(id, type, hash, "comprobante.pdf"));

        Assert.Equal(WhatsAppDocumentState.Unavailable, message.DocumentState);
        Assert.Equal(WhatsAppMessageKind.ReceivedDocument, message.Kind);
        Assert.Null(message.DocumentMediaId);
        Assert.Equal("comprobante.pdf", message.DocumentFileName);
        Assert.Equal("Mirá este comprobante", message.Body);
    }

    [Fact]
    public void The_file_name_is_kept_without_folders_or_control_characters_and_bounded_like_a_spreadsheet()
    {
        var message = Message();

        Assert.True(message.SetDocumentDescriptor("1234", "application/pdf", Hash, "compro\nbante" + new string('x', 300) + ".pdf"));

        Assert.StartsWith("comprobantexxx", message.DocumentFileName, StringComparison.Ordinal);
        Assert.Equal(WhatsAppMessage.DocumentNameLength, message.DocumentFileName!.Length);
        Assert.EndsWith("…", message.DocumentFileName, StringComparison.Ordinal);
        Assert.Equal("comprobante.pdf", WhatsAppMessage.DocumentName(@"C:\pagos/2026\comprobante.pdf ", "application/pdf"));
        Assert.Equal(WhatsAppMessage.DocumentName(message.DocumentFileName, "application/pdf"), message.DocumentFileName);
    }

    [Theory]
    [InlineData(null, "image/jpeg", "image.jpg")]
    [InlineData(" \t ", "image/png", "image.png")]
    [InlineData(null, "application/pdf", "document.pdf")]
    [InlineData("pagos/", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "document.docx")]
    [InlineData("..", "application/zip", "document")]
    [InlineData(null, null, "document")]
    public void Without_a_name_it_keeps_one_by_its_type(string? fileName, string? type, string expected)
    {
        var unnamed = Message();
        unnamed.SetDocumentDescriptor("1234", type, Hash, fileName);
        Assert.Equal(expected, unnamed.DocumentFileName);
    }

    [Fact]
    public void Storing_the_copy_keeps_its_size_hash_and_pages_once_and_clears_the_failures()
    {
        var message = Described();
        message.RegisterDocumentFailure(Now, "Files.Content.Unavailable");
        var key = $"whatsapp/documents/{message.Id:N}.pdf";

        Assert.True(message.StoreDocument(key, 42, new string('b', 64), 3, null).IsSuccess);

        Assert.Equal(WhatsAppDocumentState.Stored, message.DocumentState);
        Assert.Equal((key, (long?)42, new string('b', 64), (int?)3, (string?)null),
            (message.DocumentStorageKey, message.DocumentSize, message.DocumentCopySha256, message.DocumentPages, message.DocumentReadingJson));
        Assert.Equal((0, (DateTime?)null, (string?)null), (message.DocumentFailureCount, message.DocumentFirstFailureAtUtc, message.DocumentFailureCode));
        Assert.True(message.StoreDocument(key, 42, new string('b', 64), 3, null).IsSuccess);
        Assert.True(message.StoreDocument(key, 43, new string('b', 64), 3, null).IsFailure);
        Assert.Equal(42, message.DocumentSize);
    }

    [Theory]
    [InlineData("", 42, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1)]
    [InlineData("whatsapp/documents/a.pdf", 0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1)]
    [InlineData("whatsapp/documents/a.pdf", 10_485_761, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1)]
    [InlineData("whatsapp/documents/a.pdf", 42, "bad", 1)]
    [InlineData("whatsapp/documents/a.pdf", 42, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 0)]
    [InlineData("whatsapp/documents/a.pdf", 42, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 11)]
    public void An_invalid_copy_is_not_stored(string key, long size, string hash, int pages)
    {
        var message = Described();

        Assert.True(message.StoreDocument(key, size, hash, pages, null).IsFailure);

        Assert.Equal(WhatsAppDocumentState.Pending, message.DocumentState);
        Assert.Null(message.DocumentStorageKey);
    }

    [Fact]
    public void A_prepared_reading_has_a_bound_and_never_goes_blank()
    {
        Assert.True(Described().StoreDocument("whatsapp/documents/a.docx", 42, Hash, null, " ").IsFailure);
        Assert.True(Described().StoreDocument("whatsapp/documents/a.docx", 42, Hash, null,
            new string('x', WhatsAppMessage.MaxDocumentReadingLength + 1)).IsFailure);
        Assert.True(Described().StoreDocument("whatsapp/documents/a.docx", 42, Hash, null,
            new string('x', WhatsAppMessage.MaxDocumentReadingLength)).IsSuccess);
    }

    [Fact]
    public void A_rejection_keeps_its_reason_and_happens_once()
    {
        var message = Described();

        Assert.True(message.RejectDocument("protected"));

        Assert.Equal((WhatsAppDocumentState.Rejected, "protected"), (message.DocumentState, message.DocumentFailureCode));
        Assert.False(message.RejectDocument("protected"));
        Assert.False(message.RejectDocument("empty"));
        Assert.Equal("protected", message.DocumentFailureCode);
        Assert.True(message.StoreDocument("whatsapp/documents/a.pdf", 42, Hash, 1, null).IsFailure);
        Assert.Null(message.DocumentStorageKey);
    }

    [Fact]
    public void An_unavailable_document_is_not_stored_nor_rejected_later()
    {
        var message = Described();

        message.SetDocumentUnavailable("notLinked");

        Assert.Equal((WhatsAppDocumentState.Unavailable, "notLinked"), (message.DocumentState, message.DocumentFailureCode));
        Assert.False(message.RejectDocument("legacy"));
        Assert.True(message.StoreDocument("whatsapp/documents/a.pdf", 42, Hash, 1, null).IsFailure);
        message.SetDocumentUnavailable("Other");
        Assert.Equal("notLinked", message.DocumentFailureCode);
        var stored = Stored();
        stored.SetDocumentUnavailable("Late");
        Assert.Equal(WhatsAppDocumentState.Stored, stored.DocumentState);
    }

    [Fact]
    public void Download_failures_saturate_at_five_and_end_only_after_fifteen_minutes()
    {
        var message = Described();

        for (var attempt = 0; attempt < 6; attempt++) Assert.False(message.RegisterDocumentFailure(Now.AddMinutes(attempt), "Files.Content.Unavailable"));

        Assert.Equal((5, (DateTime?)Now, WhatsAppDocumentState.Pending),
            (message.DocumentFailureCount, message.DocumentFirstFailureAtUtc, message.DocumentState));
        Assert.True(message.RegisterDocumentFailure(Now.AddMinutes(15), "Files.Content.Unavailable"));
        Assert.Equal((WhatsAppDocumentState.Unavailable, "Files.Content.Unavailable"), (message.DocumentState, message.DocumentFailureCode));
        Assert.False(message.RegisterDocumentFailure(Now.AddMinutes(16), "Files.Content.Unavailable"));
    }

    // A Word is read by its owner while it is prepared: once it is used it is read without the engine, and never asks for a reading.
    [Fact]
    public void A_word_with_its_prepared_reading_is_read_once_it_is_used_without_a_request()
    {
        var message = Stored(Reading, pages: null);

        Assert.Equal((WhatsAppDocumentState.Stored, Reading), (message.DocumentState, message.DocumentReadingJson));
        Assert.True(message.RequestDocumentReading(Guid.NewGuid(), "{\"channelId\":\"whatsapp-assistant\"}", 1, Now.AddDays(90), Now).IsFailure);
        Assert.True(message.UsePreparedDocumentReading().IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Read, (Guid?)null), (message.DocumentState, message.DocumentRequestId));
        Assert.True(message.UsePreparedDocumentReading().IsSuccess);
        Assert.True(Stored().UsePreparedDocumentReading().IsFailure);
    }

    [Fact]
    public void A_reading_is_requested_once_with_its_actor_context_and_expiry_and_completed_once()
    {
        var message = Stored();
        var actor = Guid.NewGuid();

        var requested = message.RequestDocumentReading(actor, "{\"channelId\":\"whatsapp-assistant\"}", 4, Now.AddDays(90), Now);

        Assert.True(requested.IsSuccess);
        Assert.Equal((WhatsAppDocumentState.Reading, (Guid?)requested.Value, (Guid?)actor, (long?)4, (DateTime?)Now.AddDays(90)),
            (message.DocumentState, message.DocumentRequestId, message.DocumentActorUserId, message.DocumentLinkRevision, message.DocumentContentExpiresAtUtc));
        Assert.Equal(requested.Value, message.RequestDocumentReading(Guid.NewGuid(), "{}", 9, Now.AddDays(1), Now).Value);
        Assert.Equal(7, requested.Value.Version);
        Assert.True(message.CompleteDocumentReading(Guid.NewGuid(), Reading, Now).IsFailure);
        Assert.True(message.CompleteDocumentReading(requested.Value, Reading, Now).IsSuccess);

        Assert.Equal((WhatsAppDocumentState.Read, Reading), (message.DocumentState, message.DocumentReadingJson));
        Assert.True(message.CompleteDocumentReading(requested.Value, Reading, Now).IsSuccess);
        Assert.True(message.CompleteDocumentReading(requested.Value, "{\"other\":true}", Now).IsFailure);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("context")]
    [InlineData("revision")]
    [InlineData("expired")]
    [InlineData("pending")]
    public void An_invalid_request_is_not_fixed(string invalid)
    {
        var message = invalid == "pending" ? Described() : Stored();

        var requested = message.RequestDocumentReading(invalid == "actor" ? Guid.Empty : Guid.NewGuid(), invalid == "context" ? " " : "{}",
            invalid == "revision" ? -1 : 1, invalid == "expired" ? Now : Now.AddDays(1), Now);

        Assert.True(requested.IsFailure);
        Assert.Null(message.DocumentRequestId);
    }

    [Fact]
    public void A_reading_whose_content_expired_is_not_completed()
    {
        var message = Stored();
        var requested = message.RequestDocumentReading(Guid.NewGuid(), "{}", 1, Now.AddHours(1), Now).Value;

        Assert.True(message.CompleteDocumentReading(requested, Reading, Now.AddHours(1)).IsFailure);
        Assert.True(message.CompleteDocumentReading(requested, " ", Now).IsFailure);
        Assert.Equal(WhatsAppDocumentState.Reading, message.DocumentState);
    }

    [Theory]
    [InlineData(WhatsAppDocumentState.Failed, "invalidOutput")]
    [InlineData(WhatsAppDocumentState.Uncertain, "providerUncertain")]
    [InlineData(WhatsAppDocumentState.Expired, null)]
    [InlineData(WhatsAppDocumentState.Unavailable, "quota")]
    public void A_reading_ends_once_without_its_text(WhatsAppDocumentState state, string? code)
    {
        var message = Stored();
        var requested = message.RequestDocumentReading(Guid.NewGuid(), "{}", 1, Now.AddDays(1), Now).Value;

        message.SetDocumentReadingState(state, code);

        Assert.Equal((state, code, (string?)null), (message.DocumentState, message.DocumentFailureCode, message.DocumentReadingJson));
        Assert.True(message.CompleteDocumentReading(requested, Reading, Now).IsFailure);
        message.SetDocumentReadingState(WhatsAppDocumentState.Failed, "late");
        Assert.Equal(state, message.DocumentState);
    }

    // Decision 10: the request may be the text or the audio that follows; a pending document waits for it once, still pending.
    [Fact]
    public void A_pending_document_waits_for_its_request_once_until_a_utc_instant()
    {
        var message = Described(caption: null);

        Assert.False(message.AwaitDocumentRequest(Now.AddMinutes(10).ToLocalTime()));
        Assert.True(message.AwaitDocumentRequest(Now.AddMinutes(10)));
        Assert.False(message.AwaitDocumentRequest(Now.AddMinutes(20)));

        Assert.Equal((WhatsAppDocumentState.Pending, (DateTime?)Now.AddMinutes(10)), (message.DocumentState, message.DocumentRequestAwaitedUntilUtc));
        Assert.True(message.StoreDocument($"whatsapp/documents/{message.Id:N}.pdf", 42, Hash, 2, null).IsSuccess);
    }

    [Fact]
    public void Only_a_pending_document_waits_for_its_request()
    {
        var stored = Stored();
        var unavailable = Message();
        unavailable.SetDocumentDescriptor(null, "application/pdf", Hash, "comprobante.pdf");

        Assert.False(stored.AwaitDocumentRequest(Now.AddMinutes(10)));
        Assert.False(unavailable.AwaitDocumentRequest(Now.AddMinutes(10)));
        Assert.False(WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.text", WhatsAppMessageKind.Text, "Hola", null, Now).AwaitDocumentRequest(Now.AddMinutes(10)));
        Assert.Null(stored.DocumentRequestAwaitedUntilUtc);
    }

    [Fact]
    public void Only_a_reading_in_course_changes_its_state_and_never_to_read_without_its_text()
    {
        var stored = Stored();
        stored.SetDocumentReadingState(WhatsAppDocumentState.Failed, "late");
        Assert.Equal(WhatsAppDocumentState.Stored, stored.DocumentState);

        var reading = Stored();
        reading.RequestDocumentReading(Guid.NewGuid(), "{}", 1, Now.AddDays(1), Now);
        reading.SetDocumentReadingState(WhatsAppDocumentState.Read);
        reading.SetDocumentReadingState(WhatsAppDocumentState.Pending);
        Assert.Equal(WhatsAppDocumentState.Reading, reading.DocumentState);
    }
}
