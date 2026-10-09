using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantProtectionTests
{
    private static AiKeyRing Ring(string active = "one", string installation = "installation", bool includeOld = true) => new(Options.Create(new AiSecretProtectionOptions
    {
        InstallationId = installation, ActiveKeyId = active,
        Keys = includeOld ? new() { ["one"] = Convert.ToBase64String(new byte[32]), ["two"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()) }
            : new() { ["two"] = Convert.ToBase64String(Enumerable.Repeat((byte)2, 32).ToArray()) },
    }));

    private static IAiConversationProtector Protector(AiKeyRing ring)
    {
        var type = typeof(AiKeyRing).Assembly.GetType("ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security.AesGcmAiAssistantContentProtector");
        Assert.NotNull(type);
        return Assert.IsAssignableFrom<IAiConversationProtector>(Activator.CreateInstance(type, ring));
    }

    [Fact]
    public void Content_authenticates_owner_conversation_turn_purpose_revision_and_installation()
    {
        var owner = Guid.NewGuid(); var conversation = Guid.NewGuid(); var turn = Guid.NewGuid();
        var protector = Protector(Ring());
        var encrypted = protector.Protect(owner, conversation, turn, "question", 1, "private question");
        Assert.DoesNotContain("private question", encrypted, StringComparison.Ordinal);
        Assert.Equal("private question", protector.Unprotect(owner, conversation, turn, "question", 1, encrypted));
        Assert.Null(protector.Unprotect(Guid.NewGuid(), conversation, turn, "question", 1, encrypted));
        Assert.Null(protector.Unprotect(owner, Guid.NewGuid(), turn, "question", 1, encrypted));
        Assert.Null(protector.Unprotect(owner, conversation, Guid.NewGuid(), "question", 1, encrypted));
        Assert.Null(protector.Unprotect(owner, conversation, turn, "answer", 1, encrypted));
        Assert.Null(protector.Unprotect(owner, conversation, turn, "question", 2, encrypted));
        Assert.Null(Protector(Ring(installation: "other")).Unprotect(owner, conversation, turn, "question", 1, encrypted));
        Assert.Equal("private question", Protector(Ring("two")).Unprotect(owner, conversation, turn, "question", 1, encrypted));
        Assert.Null(Protector(Ring("two", includeOld: false)).Unprotect(owner, conversation, turn, "question", 1, encrypted));
    }

    [Fact]
    public void Fingerprints_preserve_the_original_key_and_ignore_later_UI_language()
    {
        var type = typeof(AiKeyRing).Assembly.GetType("ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security.HmacAiAssistantRequestFingerprint");
        Assert.NotNull(type);
        var original = Assert.IsAssignableFrom<IAiAssistantRequestFingerprint>(Activator.CreateInstance(type, Ring()));
        var rotated = Assert.IsAssignableFrom<IAiAssistantRequestFingerprint>(Activator.CreateInstance(type, Ring("two")));
        var owner = Guid.NewGuid();
        var request = new SubmitAiAssistantTurnRequest(Guid.NewGuid(), null, null, 7, "  question\r\nnext  ", "es");
        var fingerprint = original.Create(owner, request, "one");
        Assert.True(rotated.Matches(owner, request with { Question = "question\nnext", Culture = "en" }, "one", fingerprint));
        Assert.False(rotated.Matches(Guid.NewGuid(), request, "one", fingerprint));
        Assert.False(rotated.Matches(owner, request with { ExpectedConnectionRevision = 8 }, "one", fingerprint));
        Assert.False(rotated.Matches(owner, request with { Question = "changed" }, "one", fingerprint));
    }
}
