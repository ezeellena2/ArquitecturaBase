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
    [InlineData("expiry")]
    public void A_Home_turn_with_a_spreadsheet_binds_its_private_copy_but_not_what_was_prepared(string changed)
    {
        var owner = Guid.NewGuid();
        var turn = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), Guid.NewGuid(), 3, 7, "", "es") { Attachment = Attachment() };
        var original = Fingerprint();
        var hash = original.Create(owner, turn, "one");
        Assert.True(original.Matches(owner, turn with { Attachment = turn.Attachment! with { Spreadsheet = Sheet("Hoja 2") } }, "one", hash));
        Assert.False(original.Matches(owner, turn with { Attachment = Changed(turn.Attachment!, changed) }, "one", hash));
        Assert.False(original.Matches(owner, turn with { Attachment = null }, "one", hash));
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
