using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantConversationTitleTests
{
    [Fact]
    public void Title_normalizes_whitespace_and_preserves_Unicode_text_elements()
    {
        Assert.Equal("¿Qué permisos hay?", AiAssistantConversationTitlePolicy.Normalize(" \n¿Qué\t permisos  hay? "));
        var text = string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 81));
        Assert.Equal(string.Concat(Enumerable.Repeat("👨‍👩‍👧‍👦", 80)) + "…", AiAssistantConversationTitlePolicy.Normalize(text));
    }

    [Fact]
    public void Blocked_or_expired_titles_never_decrypt_the_question()
    {
        var clock = new FakeTimeProvider();
        var protector = new Protector();
        var policy = new AiAssistantResponsePolicy(protector, clock, new AiAssistantCapabilityCatalogPolicy([], new FakePermissionService()));
        var row = Row(clock.GetUtcNow().UtcDateTime.AddDays(1));
        Assert.True(policy.MapTitle(row, false).IsSuccess);
        Assert.True(policy.MapTitle(Row(clock.GetUtcNow().UtcDateTime), true).IsSuccess);
        Assert.Equal(0, protector.Reads);
    }

    [Fact]
    public void Authorized_title_uses_the_original_cipher_context_and_reports_corruption()
    {
        var clock = new FakeTimeProvider();
        var protector = new Protector();
        var policy = new AiAssistantResponsePolicy(protector, clock, new AiAssistantCapabilityCatalogPolicy([], new FakePermissionService()));
        var row = Row(clock.GetUtcNow().UtcDateTime.AddDays(1));
        Assert.Equal("Pregunta de prueba", policy.MapTitle(row, true).Value);
        Assert.Equal((row.OwnerUserId, row.Id, row.FirstQuestion!.TurnId, "question", 7L, "ciphertext"), protector.Context);
        protector.Text = null;
        Assert.Equal(AiAssistantErrors.ContentUnavailable, policy.MapTitle(row, true).Error);
        Assert.DoesNotContain("ciphertext", row.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pregunta", new AiConversationSummaryResponse(row.Id, 1, "Pregunta", row.LastActivityAtUtc, "allowed").ToString(), StringComparison.Ordinal);
    }

    private static AiConversationRow Row(DateTime expiresUtc) => new(Guid.NewGuid(), Guid.NewGuid(), AiConversationState.Active,
        1, expiresUtc.AddDays(-1), [], null, new(Guid.NewGuid(), 7, "ciphertext", expiresUtc));

    private sealed class Protector : IAiConversationProtector
    {
        public int Reads { get; private set; }
        public string? Text { get; set; } = "Pregunta de prueba";
        public (Guid Owner, Guid Conversation, Guid Turn, string Purpose, long Revision, string Ciphertext) Context { get; private set; }
        public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext) => throw new NotSupportedException();
        public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext)
        {
            Reads++;
            Context = (ownerUserId, conversationId, turnId, purpose, payloadRevision, ciphertext);
            return Text;
        }
    }
}
