using ArquitecturaBase.Application.Models.Assistant;
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
}
