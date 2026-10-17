using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantContextPolicyTests
{
    [Theory]
    [InlineData("es", "en", "Política del asistente versión 1.", "Respondé en español rioplatense con voseo.")]
    [InlineData("en", "es", "Assistant policy version 1.", "Reply in English.")]
    public async Task Prompt_uses_the_captured_turn_language_after_the_request_scope_has_changed(string capturedCulture, string ambientCulture, string prefix, string instruction)
    {
        var host = new Host(capturedCulture);
        var previousCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ambientCulture);
            var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
            Assert.True(result.IsSuccess);
            Assert.StartsWith(prefix, result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains(instruction, result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("assistant_answer", result.Value.ProviderRequest.SystemPrompt, StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [Fact]
    public async Task An_expired_history_gap_starts_fresh_context_without_decrypting_older_turns()
    {
        var host = new Host("es");
        var current = host.Reader.Row;
        host.Reader.History = [
            current,
            current with { Id = Guid.NewGuid(), Ordinal = 2, State = AiAssistantTurnState.Expired, ProtectedQuestion = null },
            current with { Id = Guid.NewGuid(), Ordinal = 1, State = AiAssistantTurnState.Completed, ProtectedQuestion = "older-content" }
        ];
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var message = Assert.Single(result.Value.ProviderRequest.Messages);
        Assert.Equal("Current question", message.Text);
        Assert.Equal(1, host.Protector.Decryptions);
    }

    private sealed class Host
    {
        public InMemoryUserAccounts Users { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public AiAssistantAccessGuard Guard { get; }
        public Guid UserId { get; }
        public Reader Reader { get; }
        public Protector Protector { get; } = new();
        public AiAssistantClaimResponse Claim { get; }
        public AiAssistantContextPolicy Policy { get; }
        public Host(string culture, int[]? profileVersions = null)
        {
            var users = Users;
            var actor = users.AddUser("fictional@example.test");
            UserId = actor.Id;
            var permissions = Permissions;
            permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.AssistantUse];
            var clock = new FakeTimeProvider();
            var limits = new AiAssistantLimits();
            var now = clock.GetUtcNow().UtcDateTime;
            Reader = new Reader(new(Guid.NewGuid(), actor.Id, Guid.NewGuid(), Guid.NewGuid(), 3, AiAssistantTurnState.Running,
                "preparing", 2, 1, [], "current-content", null, null, 1, culture, "UTC", AiProvider.OpenAI, "model", 1,
                Guid.NewGuid(), 1, now, now.AddDays(30), now.AddSeconds(90), null, 0, 0, 0, null, null, false, null));
            var profile = new AiAssistantExecutionProfileSnapshot("home", 1, "home-system", 1,
                new("home", actor.Id, null, culture, "UTC", new([ArtificialIntelligencePermissions.AssistantUse], []), [], [new("assistant_answer", 1)]));
            Reader.Row = Reader.Row with { ExecutionProfileJson = JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions) };
            var row = Reader.Row;
            Claim = new(row.Id, actor.Id, row.ConversationId, row.RequestId, row.Fence, row.AttemptId!.Value, row.DeadlineAtUtc!.Value, culture);
            var profiles = (profileVersions ?? [1]).Select(version => new AiAssistantModelProfile(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "home", Version = version }).ToArray();
            var catalog = new AiAssistantCapabilityCatalog([], permissions);
            var guard = new AiAssistantAccessGuard(users, permissions, profiles, limits,
                new FakeCurrentUser { UserId = actor.Id }, catalog, []);
            Guard = guard;
            Policy = new(Reader, new ConnectionReader(), guard, new AiAssistantResponsePolicy(Protector, clock, catalog),
                new AiConnectionCandidatePolicy(new SecretProtector()), catalog, limits, clock);
        }
    }
    private sealed class Protector : IAiConversationProtector
    {
        public int Decryptions { get; private set; }
        public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext) => throw new NotSupportedException();
        public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext)
        {
            Decryptions++;
            return ciphertext == "current-content" ? "Current question" : throw new InvalidOperationException("Older history must not be decrypted.");
        }
    }
    private sealed class SecretProtector : IAiSecretProtector
    {
        public string Protect(AiProvider provider, string apiKey) => throw new NotSupportedException();
        public string? Unprotect(AiProvider provider, string protectedApiKey) => "fictional-key";
    }
    private sealed class ConnectionReader : IAiConnectionReader
    {
        public Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken) => Task.FromResult(new AiConnectionSnapshot(AiProvider.OpenAI, "model", "encrypted-key", true, 1));
        public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Reader(AiAssistantTurnRow row) : IAiAssistantReader
    {
        public AiAssistantTurnRow Row { get; set; } = row;
        public IReadOnlyList<AiAssistantTurnRow> History { get; set; } = [];
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => Task.FromResult<AiAssistantTurnRow?>(Row);
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<AiAssistantTurnRow>(History, 1, request.PageSize, History.Count));
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => Task.FromResult(new AiAssistantWorkloadRow(0, 0, 0, null, null));
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
