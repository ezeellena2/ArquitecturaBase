using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AssistantOperationFingerprintTests
{
    private static HmacAiAssistantRequestFingerprint Fingerprint(string active = "one") => new(new AiKeyRing(Options.Create(new AiSecretProtectionOptions
    {
        InstallationId = "fictitious-installation", ActiveKeyId = active,
        Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]), ["two"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()) }
    })));
    private static AssistantOperationRequest Request() => new(Guid.NewGuid(), new("fixture-manual", Guid.NewGuid(), Guid.NewGuid(), "en", "UTC",
        new(["fixture.send", "fixture.read"], []), [new("fixture.read", 1), new("fixture.propose", 1)], [new("assistant_proposal", 1)]),
        "  Fictitious request\r\nnext  ", new DateTime(2026, 10, 17, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void A_neutral_receipt_replays_normalized_content_and_ordered_permissions_using_its_original_key()
    {
        var request = Request();
        var fingerprint = Fingerprint().Create(request, "one");
        Assert.NotEmpty(fingerprint);
        var reordered = request with { Text = "Fictitious request\nnext", Context = request.Context with
        { RequiredPermissions = new(["fixture.read", "fixture.send"], []), AllowedCapabilities = request.Context.AllowedCapabilities.Reverse().ToArray() } };
        Assert.True(Fingerprint("two").Matches(reordered, "one", fingerprint));
    }

    [Fact]
    public void Existing_unspecified_mode_receipts_keep_the_original_neutral_fingerprint()
    {
        var request = Request();
        var context = request.Context;
        var ring = new AiKeyRing(Options.Create(new AiSecretProtectionOptions
        {
            InstallationId = "fictitious-installation", ActiveKeyId = "one",
            Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]) }
        }));
        // The deployed neutral request format before interaction mode was an explicit server fact.
        object?[] originalValues = ["assistant-operation-request-fingerprint:v1", request.RequestId,
            context.ChannelId, context.ActorUserId, context.SubjectUserId, context.Culture, context.TimeZoneId,
            new[] { "fixture.read", "fixture.send" }, Array.Empty<string>(),
            new object[][] { ["fixture.propose", 1], ["fixture.read", 1] },
            new object[][] { ["assistant_proposal", 1] }, "Fictitious request\nnext", request.OccurredAtUtc, request.ContentExpiresAtUtc];
        var expected = Convert.ToBase64String(System.Security.Cryptography.HMACSHA256.HashData(ring.AssistantFingerprintKey("one"),
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(originalValues)));
        Assert.Equal(expected, Fingerprint().Create(request, "one"));
        Assert.False(Fingerprint().Matches(request with { Context = context with { InteractionMode = AssistantInteractionMode.Manual } }, "one", expected));
    }
    [Theory]
    [InlineData("key")]
    [InlineData("name")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("expiry")]
    public void Audio_receipts_bind_the_original_private_object_and_source_expiry(string changed)
    {
        var request = Request() with { Text = "", Audio = new("private/audio", "audio.ogg", "audio/ogg; codecs=opus", 42,
            new string('a', 64), new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)) };
        var original = Fingerprint();
        var hash = original.Create(request, "one");
        Assert.True(original.Matches(request, "one", hash));
        var audio = request.Audio!;
        var modified = changed switch {
            "key" => audio with { StorageKey = "private/other" },
            "name" => audio with { Name = "other.ogg" },
            "mime" => audio with { ContentType = "audio/wav" },
            "size" => audio with { Size = 43 },
            "hash" => audio with { Sha256 = new string('b', 64) },
            _ => audio with { ExpiresAtUtc = audio.ExpiresAtUtc.AddHours(1) },
        };
        Assert.False(original.Matches(request with { Audio = modified }, "one", hash));
        Assert.False(original.Matches(request with { Audio = null }, "one", hash));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("subject")]
    [InlineData("channel")]
    [InlineData("channelConversation")]
    [InlineData("interactionMode")]
    [InlineData("culture")]
    [InlineData("zone")]
    [InlineData("permission")]
    [InlineData("capability")]
    [InlineData("terminal")]
    [InlineData("text")]
    [InlineData("occurred")]
    [InlineData("expiry")]
    public void A_neutral_receipt_rejects_a_changed_owner_scope_or_original_content(string changed)
    {
        var request = Request();
        var original = Fingerprint();
        var fingerprint = original.Create(request, "one");
        Assert.True(original.Matches(request, "one", fingerprint));
        var modified = changed switch
        {
            "actor" => request with { Context = request.Context with { ActorUserId = Guid.NewGuid() } },
            "subject" => request with { Context = request.Context with { SubjectUserId = Guid.NewGuid() } },
            "channel" => request with { Context = request.Context with { ChannelId = "fixture-other" } },
            "channelConversation" => request with { Context = request.Context with { ChannelConversationId = Guid.NewGuid() } },
            "interactionMode" => request with { Context = request.Context with { InteractionMode = AssistantInteractionMode.Assistant } },
            "culture" => request with { Context = request.Context with { Culture = "es" } },
            "zone" => request with { Context = request.Context with { TimeZoneId = "America/Argentina/Buenos_Aires" } },
            "permission" => request with { Context = request.Context with { RequiredPermissions = new(["fixture.other"], []) } },
            "capability" => request with { Context = request.Context with { AllowedCapabilities = [new("fixture.read", 2)] } },
            "terminal" => request with { Context = request.Context with { AllowedTerminals = [new("assistant_answer", 1)] } },
            "text" => request with { Text = "Different request" },
            "occurred" => request with { OccurredAtUtc = request.OccurredAtUtc.AddMinutes(1) },
            _ => request with { ContentExpiresAtUtc = request.ContentExpiresAtUtc.AddHours(1) }
        };
        Assert.False(original.Matches(modified, "one", fingerprint));
    }

    [Fact]
    public void A_continuation_binds_its_previous_operation_and_a_request_without_one_keeps_its_fingerprint()
    {
        var request = Request();
        var original = Fingerprint();
        var alone = original.Create(request, "one");
        var continued = request with { PreviousRequestId = Guid.NewGuid() };
        var fingerprint = original.Create(continued, "one");
        Assert.NotEqual(alone, fingerprint);
        Assert.True(original.Matches(continued, "one", fingerprint));
        Assert.False(original.Matches(continued with { PreviousRequestId = Guid.NewGuid() }, "one", fingerprint));
        Assert.False(original.Matches(request, "one", fingerprint));
        Assert.True(original.Matches(request with { PreviousRequestId = null }, "one", alone));
    }

    // Documents (task 3): the read documents after the first have no copy of their own, so their SHA-256 bind them, with a marker of
    // their own and only when they exist; a spreadsheet or a lone document keeps the fingerprint it had, and what was read stays out.
    [Fact]
    public void More_read_documents_bind_their_hashes_with_their_own_marker_only_when_they_exist()
    {
        var request = Request() with { Attachment = Attachment() };
        var (context, attachment) = (request.Context, request.Attachment!);
        var ring = new AiKeyRing(Options.Create(new AiSecretProtectionOptions
        {
            InstallationId = "fictitious-installation", ActiveKeyId = "one",
            Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]) }
        }));
        object?[] spreadsheetValues = ["assistant-operation-request-fingerprint:v1", request.RequestId,
            context.ChannelId, context.ActorUserId, context.SubjectUserId, context.Culture, context.TimeZoneId,
            new[] { "fixture.read", "fixture.send" }, Array.Empty<string>(),
            new object[][] { ["fixture.propose", 1], ["fixture.read", 1] },
            new object[][] { ["assistant_proposal", 1] }, "Fictitious request\nnext", request.OccurredAtUtc, request.ContentExpiresAtUtc,
            new object?[] { "attachment:v1", attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc }];
        Assert.Equal(Convert.ToBase64String(HMACSHA256.HashData(ring.AssistantFingerprintKey("one"), JsonSerializer.SerializeToUtf8Bytes(spreadsheetValues))),
            Fingerprint().Create(request, "one"));

        var document = AssistantAttachmentInput.ForDocument("private/documents/comprobante.pdf", "Comprobante.pdf", attachment.ExpiresAtUtc, Read('d'));
        var alone = Fingerprint().Create(request with { Attachment = document }, "one");
        var withMore = request with { Attachment = document with { MoreDocuments = [Read('a'), Read('b')] } };
        var hash = Fingerprint().Create(withMore, "one");
        Assert.NotEqual(alone, hash);
        Assert.False(Fingerprint().Matches(withMore with { Attachment = document with { MoreDocuments = [Read('a'), Read('e')] } }, "one", hash));
        Assert.False(Fingerprint().Matches(withMore with { Attachment = document with { MoreDocuments = [Read('b'), Read('a')] } }, "one", hash));
        Assert.True(Fingerprint().Matches(withMore with { Attachment = document with { MoreDocuments = [Read('a', "Otro texto leído"), Read('b')] } }, "one", hash));

        var owner = Guid.NewGuid();
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), Guid.NewGuid(), 3, 7, "", "es") { Attachment = document };
        Assert.NotEqual(Fingerprint().Create(owner, turn, "one"), Fingerprint().Create(owner, turn with { Attachment = document with { MoreDocuments = [Read('a')] } }, "one"));
    }

    private static StructuredDocument Read(char hash, string text = "Transferencia ficticia") => new("comprobante.pdf", StructuredDocumentKind.Pdf,
        "application/pdf", 4096, new string(hash, 64), 1, new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "es", text, false));

    // Stage 2: a spreadsheet binds its private copy and source expiry; what was prepared comes from those bytes and stays out.
    private static AssistantAttachmentInput Attachment() => new("private/attachments/clientes.xlsx", "Clientes.xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 2048, new string('a', 64),
        new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), Sheet("Hoja 1"));

    // The prepared sheet is opaque here: only its own type reads it.
    private static StructuredSpreadsheet Sheet(string name) =>
        JsonSerializer.Deserialize<StructuredSpreadsheet>(JsonSerializer.Serialize(new { sheetName = name }), JsonSerializerOptions.Web)!;

    private static AssistantAttachmentInput Changed(AssistantAttachmentInput attachment, string changed) => changed switch
    {
        "key" => attachment with { StorageKey = "private/attachments/otra.xlsx" },
        "name" => attachment with { Name = "Otra.xlsx" },
        "mime" => attachment with { ContentType = "text/csv" },
        "size" => attachment with { Size = 2049 },
        "hash" => attachment with { Sha256 = new string('b', 64) },
        _ => attachment with { ExpiresAtUtc = attachment.ExpiresAtUtc.AddHours(1) },
    };

    [Theory]
    [InlineData("key")]
    [InlineData("name")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("expiry")]
    public void A_spreadsheet_binds_its_private_copy_but_not_what_was_prepared(string changed)
    {
        var request = Request() with { Attachment = Attachment() };
        var original = Fingerprint();
        var hash = original.Create(request, "one");
        Assert.NotEqual(original.Create(request with { Attachment = null }, "one"), hash);
        Assert.True(original.Matches(request with { Attachment = request.Attachment! with { Spreadsheet = Sheet("Hoja 2") } }, "one", hash));
        Assert.False(original.Matches(request with { Attachment = Changed(request.Attachment!, changed) }, "one", hash));
        Assert.False(original.Matches(request with { Attachment = null }, "one", hash));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("name")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("hash")]
    public void A_Home_turn_with_a_spreadsheet_binds_what_the_request_sends_but_not_its_expiry_or_what_was_prepared(string changed)
    {
        var owner = Guid.NewGuid();
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), Guid.NewGuid(), 3, 7, "", "es") { Attachment = Attachment() };
        var original = Fingerprint();
        var hash = original.Create(owner, turn, "one");
        Assert.True(original.Matches(owner, turn with { Attachment = turn.Attachment! with { Spreadsheet = Sheet("Hoja 2") } }, "one", hash));
        // The server sets the expiry of a Home copy on each attempt: a retry later still recovers its turn.
        Assert.True(original.Matches(owner, turn with { Attachment = turn.Attachment! with { ExpiresAtUtc = turn.Attachment.ExpiresAtUtc.AddMinutes(5) } }, "one", hash));
        Assert.False(original.Matches(owner, turn with { Attachment = Changed(turn.Attachment!, changed) }, "one", hash));
        Assert.False(original.Matches(owner, turn with { Attachment = null }, "one", hash));
    }

    [Theory]
    [InlineData("key")]
    [InlineData("name")]
    [InlineData("mime")]
    [InlineData("size")]
    [InlineData("hash")]
    [InlineData("pages")]
    [InlineData("expiry")]
    public void A_reading_binds_the_original_private_document_and_its_expiry(string changed)
    {
        var request = Request() with { Text = "", Document = new("private/documents/comprobante.pdf", "Comprobante.pdf", "application/pdf", 2048,
            new string('a', 64), 2, new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc)) };
        var original = Fingerprint();
        var hash = original.Create(request, "one");
        Assert.True(original.Matches(request, "one", hash));
        Assert.NotEqual(original.Create(request with { Document = null }, "one"), hash);
        var document = request.Document!;
        var modified = changed switch
        {
            "key" => document with { StorageKey = "private/documents/otro.pdf" },
            "name" => document with { Name = "Otro.pdf" },
            "mime" => document with { ContentType = "image/png" },
            "size" => document with { Size = 2049 },
            "hash" => document with { Sha256 = new string('b', 64) },
            "pages" => document with { Pages = 3 },
            _ => document with { ExpiresAtUtc = document.ExpiresAtUtc.AddHours(1) },
        };
        Assert.False(original.Matches(request with { Document = modified }, "one", hash));
        Assert.False(original.Matches(request with { Document = null }, "one", hash));
    }

    // Stage 3: a turn with a photo or a PDF binds the reading it names with its own marker, and only when it names one.
    [Fact]
    public void A_Home_turn_with_a_document_binds_the_reading_it_names()
    {
        var owner = Guid.NewGuid();
        var typed = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), Guid.NewGuid(), 3, 7, "Armame un formulario", "es");
        var turn = typed with { DocumentRequestId = Guid.NewGuid() };
        var ring = new AiKeyRing(Options.Create(new AiSecretProtectionOptions
        {
            InstallationId = "fictitious-installation", ActiveKeyId = "one", Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]) }
        }));
        object?[] values = [owner, turn.RequestId, turn.ConversationId, turn.ExpectedRevision, turn.ExpectedConnectionRevision, "Armame un formulario",
            new object?[] { "home-document:v1", turn.DocumentRequestId }];
        var original = Fingerprint();
        var hash = original.Create(owner, turn, "one");

        Assert.Equal(Convert.ToBase64String(HMACSHA256.HashData(ring.AssistantFingerprintKey("one"), JsonSerializer.SerializeToUtf8Bytes(values))), hash);
        Assert.True(original.Matches(owner, turn, "one", hash));
        Assert.False(original.Matches(owner, turn with { DocumentRequestId = Guid.NewGuid() }, "one", hash));
        Assert.False(original.Matches(owner, typed, "one", hash));
    }

    [Fact]
    public void A_typed_Home_turn_keeps_its_original_fingerprint()
    {
        var owner = Guid.NewGuid();
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), Guid.NewGuid(), 3, 7, "  Fictitious question  ", "es");
        var ring = new AiKeyRing(Options.Create(new AiSecretProtectionOptions
        {
            InstallationId = "fictitious-installation", ActiveKeyId = "one", Keys = new() { ["one"] = Convert.ToBase64String(new byte[32]) }
        }));
        object?[] originalValues = [owner, turn.RequestId, turn.ConversationId, turn.ExpectedRevision, turn.ExpectedConnectionRevision, "Fictitious question"];
        var expected = Convert.ToBase64String(HMACSHA256.HashData(ring.AssistantFingerprintKey("one"), JsonSerializer.SerializeToUtf8Bytes(originalValues)));
        Assert.Equal(expected, Fingerprint().Create(owner, turn, "one"));
    }
}
