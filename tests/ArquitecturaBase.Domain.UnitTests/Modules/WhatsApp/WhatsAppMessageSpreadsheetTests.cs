using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring: a spreadsheet that arrives by WhatsApp keeps Meta's descriptor and its caption, and is
// stored, rejected with its reason or unavailable once.
public sealed class WhatsAppMessageSpreadsheetTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', 64);

    private static WhatsAppMessage Message(string? caption = null) =>
        WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.spreadsheet", WhatsAppMessageKind.Spreadsheet, caption, null, Now);

    private static WhatsAppMessage Described()
    {
        var message = Message("Armame un catálogo con esta planilla");
        Assert.True(message.SetSpreadsheetDescriptor("1234", "text/csv", Hash, "clientes.csv"));
        return message;
    }

    [Fact]
    public void A_new_spreadsheet_keeps_its_descriptor_and_its_caption_and_waits_to_be_downloaded()
    {
        var message = Described();

        Assert.Equal(WhatsAppSpreadsheetState.Pending, message.SpreadsheetState);
        Assert.Equal(("1234", "text/csv", Hash, "clientes.csv"),
            (message.SpreadsheetMediaId, message.SpreadsheetContentType, message.SpreadsheetSha256, message.SpreadsheetFileName));
        Assert.Equal("Armame un catálogo con esta planilla", message.Body);
        Assert.Null(message.SpreadsheetStorageKey);
        Assert.Null(message.SpreadsheetPreparedJson);
        Assert.False(message.SetSpreadsheetDescriptor("9999", "text/csv", new string('b', 64), "otra.csv"));
        Assert.Equal("1234", message.SpreadsheetMediaId);
        Assert.True(message.SetSpreadsheetDescriptor("1234", "text/csv", Hash, "clientes.csv"));
    }

    [Fact]
    public void Meta_hash_is_kept_in_lowercase()
    {
        var message = Message();

        Assert.True(message.SetSpreadsheetDescriptor("1234", "text/csv", new string('A', 64), "clientes.csv"));

        Assert.Equal(new string('a', 64), message.SpreadsheetSha256);
    }

    [Theory]
    [InlineData(WhatsAppMessageKind.Media)]
    [InlineData(WhatsAppMessageKind.Text)]
    [InlineData(WhatsAppMessageKind.Audio)]
    public void Only_an_inbound_spreadsheet_takes_a_descriptor(WhatsAppMessageKind kind)
    {
        var message = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.other", kind, null, null, Now);

        Assert.False(message.SetSpreadsheetDescriptor("1234", "text/csv", Hash, "clientes.csv"));

        Assert.Null(message.SpreadsheetState);
        Assert.Null(message.SpreadsheetFileName);
    }

    [Theory]
    [InlineData(null, "text/csv", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("id-not-digits", "text/csv", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", null, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", "text/\ncsv", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("1234", "text/csv", null)]
    [InlineData("1234", "text/csv", "bad")]
    public void A_malformed_descriptor_stays_visible_with_its_name_but_unavailable(string? id, string? type, string? hash)
    {
        var message = Message("Mirá esta planilla");

        Assert.False(message.SetSpreadsheetDescriptor(id, type, hash, "clientes.csv"));

        Assert.Equal(WhatsAppSpreadsheetState.Unavailable, message.SpreadsheetState);
        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.Null(message.SpreadsheetMediaId);
        Assert.Equal("clientes.csv", message.SpreadsheetFileName);
        Assert.Equal("Mirá esta planilla", message.Body);
    }

    [Fact]
    public void The_file_name_is_kept_without_control_characters_and_bounded()
    {
        var message = Message();

        Assert.True(message.SetSpreadsheetDescriptor("1234", "text/csv", Hash, "cli\nentes" + new string('x', 300) + ".csv"));

        Assert.StartsWith("clientesxxx", message.SpreadsheetFileName, StringComparison.Ordinal);
        Assert.Equal(WhatsAppMessage.MaxSpreadsheetFileNameLength, message.SpreadsheetFileName!.Length);
        var unnamed = Message();
        Assert.True(unnamed.SetSpreadsheetDescriptor("1234", "text/csv", Hash, " \t "));
        Assert.Null(unnamed.SpreadsheetFileName);
    }

    [Fact]
    public void Storing_the_copy_keeps_what_was_prepared_once_and_clears_the_failures()
    {
        var message = Described();
        message.RegisterSpreadsheetFailure(Now, "Files.Content.Unavailable");

        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, new string('b', 64), "{\"rowCount\":3}").IsSuccess);

        Assert.Equal(WhatsAppSpreadsheetState.Stored, message.SpreadsheetState);
        Assert.Equal(("whatsapp/documents/a.csv", (long?)42, new string('b', 64), "{\"rowCount\":3}"),
            (message.SpreadsheetStorageKey, message.SpreadsheetSize, message.SpreadsheetCopySha256, message.SpreadsheetPreparedJson));
        Assert.Equal((0, (DateTime?)null, (string?)null),
            (message.SpreadsheetFailureCount, message.SpreadsheetFirstFailureAtUtc, message.SpreadsheetFailureCode));
        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, new string('b', 64), "{\"rowCount\":3}").IsSuccess);
        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 43, new string('b', 64), "{\"rowCount\":3}").IsFailure);
        Assert.Equal(42, message.SpreadsheetSize);
    }

    [Theory]
    [InlineData("", 42, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "{}")]
    [InlineData("whatsapp/documents/a.csv", 0, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "{}")]
    [InlineData("whatsapp/documents/a.csv", 10_485_761, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "{}")]
    [InlineData("whatsapp/documents/a.csv", 42, "bad", "{}")]
    [InlineData("whatsapp/documents/a.csv", 42, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", " ")]
    public void An_invalid_copy_is_not_stored(string key, long size, string hash, string prepared)
    {
        var message = Described();

        Assert.True(message.StoreSpreadsheet(key, size, hash, prepared).IsFailure);

        Assert.Equal(WhatsAppSpreadsheetState.Pending, message.SpreadsheetState);
        Assert.Null(message.SpreadsheetStorageKey);
    }

    [Fact]
    public void What_was_prepared_has_a_bound()
    {
        var message = Described();

        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, Hash,
            new string('x', WhatsAppMessage.MaxSpreadsheetPreparedLength + 1)).IsFailure);
        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, Hash,
            new string('x', WhatsAppMessage.MaxSpreadsheetPreparedLength)).IsSuccess);
    }

    [Fact]
    public void A_rejection_keeps_its_reason_and_happens_once()
    {
        var message = Described();

        Assert.True(message.RejectSpreadsheet("legacy"));

        Assert.Equal((WhatsAppSpreadsheetState.Rejected, "legacy"), (message.SpreadsheetState, message.SpreadsheetFailureCode));
        Assert.False(message.RejectSpreadsheet("legacy"));
        Assert.False(message.RejectSpreadsheet("empty"));
        Assert.Equal("legacy", message.SpreadsheetFailureCode);
        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, Hash, "{}").IsFailure);
        Assert.Null(message.SpreadsheetStorageKey);
    }

    [Fact]
    public void An_unavailable_spreadsheet_is_not_stored_nor_rejected_later()
    {
        var message = Described();

        message.SetSpreadsheetUnavailable("WhatsApp.Assistant.Unavailable");

        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, "WhatsApp.Assistant.Unavailable"), (message.SpreadsheetState, message.SpreadsheetFailureCode));
        Assert.False(message.RejectSpreadsheet("legacy"));
        Assert.True(message.StoreSpreadsheet("whatsapp/documents/a.csv", 42, Hash, "{}").IsFailure);
        message.SetSpreadsheetUnavailable("Other");
        Assert.Equal("WhatsApp.Assistant.Unavailable", message.SpreadsheetFailureCode);
        var stored = Described();
        Assert.True(stored.StoreSpreadsheet("whatsapp/documents/a.csv", 42, Hash, "{}").IsSuccess);
        stored.SetSpreadsheetUnavailable("Late");
        Assert.Equal(WhatsAppSpreadsheetState.Stored, stored.SpreadsheetState);
    }

    [Fact]
    public void Download_failures_saturate_at_five_and_end_only_after_fifteen_minutes()
    {
        var message = Described();

        for (var attempt = 0; attempt < 6; attempt++) Assert.False(message.RegisterSpreadsheetFailure(Now.AddMinutes(attempt), "Files.Content.Unavailable"));

        Assert.Equal((5, (DateTime?)Now, WhatsAppSpreadsheetState.Pending),
            (message.SpreadsheetFailureCount, message.SpreadsheetFirstFailureAtUtc, message.SpreadsheetState));
        Assert.True(message.RegisterSpreadsheetFailure(Now.AddMinutes(15), "Files.Content.Unavailable"));
        Assert.Equal((WhatsAppSpreadsheetState.Unavailable, "Files.Content.Unavailable"), (message.SpreadsheetState, message.SpreadsheetFailureCode));
        Assert.False(message.RegisterSpreadsheetFailure(Now.AddMinutes(16), "Files.Content.Unavailable"));
    }
}
