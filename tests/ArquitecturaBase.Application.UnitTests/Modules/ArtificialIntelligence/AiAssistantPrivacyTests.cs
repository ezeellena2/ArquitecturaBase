using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantPrivacyTests
{
    [Fact]
    public async Task Revoked_source_permission_returns_metadata_without_decrypting_any_content()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("fictional@example.test");
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.AssistantUse];
        var current = new FakeCurrentUser { UserId = actor.Id };
        var clock = new FakeTimeProvider();
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(users, permissions, [], limits, current);
        var reader = new Reader(actor.Id, clock.GetUtcNow().UtcDateTime);
        var policy = new AiAssistantReadPolicy(reader, null!, RequestValidators.For(), permissions, guard,
            new AiAssistantResponsePolicy(new NeverDecrypt(), clock), [], limits);
        var result = await policy.GetTurnAsync(reader.Row.RequestId, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("blocked", result.Value.ContentAccess);
        Assert.Null(result.Value.Question);
        Assert.Null(result.Value.Answer);
        Assert.Empty(result.Value.Evidences);
    }

    [Fact]
    public async Task Losing_the_assistant_use_permission_returns_forbidden_before_any_history_read()
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("fictional@example.test");
        var permissions = new FakePermissionService();
        var clock = new FakeTimeProvider();
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(users, permissions, [], limits, new FakeCurrentUser { UserId = actor.Id });
        var reader = new Reader(actor.Id, clock.GetUtcNow().UtcDateTime);
        var policy = new AiAssistantReadPolicy(reader, null!, RequestValidators.For(), permissions, guard,
            new AiAssistantResponsePolicy(new NeverDecrypt(), clock), [], limits);
        var result = await policy.GetTurnAsync(reader.Row.RequestId, TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.Forbidden, result.Error);
        Assert.Equal(0, reader.Reads);
    }

    private sealed class NeverDecrypt : IAiConversationProtector
    {
        public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext) =>
            throw new InvalidOperationException("Content must not be accessed.");
        public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext) =>
            throw new InvalidOperationException("Content must not be accessed.");
    }

    private sealed class Reader : IAiAssistantReader
    {
        public int Reads { get; private set; }
        public AiAssistantTurnRow Row { get; }
        public Reader(Guid owner, DateTime nowUtc) => Row = new(Guid.NewGuid(), owner, Guid.NewGuid(), Guid.NewGuid(), 1,
            AiAssistantTurnState.Completed, "finished", 4, 2, ["users.read"], "encrypted-question", "encrypted-answer", "encrypted-sources",
            1, "es", "UTC", AiProvider.OpenAI, "model", 1, Guid.NewGuid(), 1, nowUtc, nowUtc.AddDays(30), nowUtc.AddMinutes(1),
            null, 1, 1, 10, 1, 1, false, null);
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<AiAssistantTurnRow?>(ownerUserId == Row.OwnerUserId && requestId == Row.RequestId ? Row : null);
        }
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
