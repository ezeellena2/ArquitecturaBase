using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantHomeMutationScenario
{
    public ObservedReadAssistantStorePorts Store { get; }
    public AiAssistantService Service { get; }

    public AiAssistantHomeMutationScenario(Func<Guid, DateTime, string, ObservedReadAssistantStorePorts> createStore)
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("neutral-owner-home-permission@example.test");
        var permissions = new FakePermissionService();
        permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.AssistantUse];
        Store = createStore(actor.Id, clock.GetUtcNow().UtcDateTime, "fixture-neutral");
        var catalog = new AiAssistantCapabilityCatalogPolicy([], permissions);
        var guard = new AiAssistantAccessGuard(users, permissions, [], new AiAssistantLimits(),
            new FakeCurrentUser { UserId = actor.Id }, catalog, new([], [], new MemoryPrivateFileStore()));
        Service = new(Store.Repository, new FakeUnitOfWork(), RequestValidators.For(new CancelAiAssistantTurnRequestValidator()),
            guard, new AiAssistantReadPolicy(new MissingReader(), null!, permissions, guard, null!, new AiAssistantLimits(), null!),
            null!, clock, NullLogger<AiAssistantService>.Instance);
    }
    private sealed class MissingReader : IAiAssistantReader
    {
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken ct) => Task.FromResult<AiAssistantTurnRow?>(null);
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken ct) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken ct) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken ct) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<IReadOnlyList<Guid>> ListRetainedAudioRequestIdsAsync(IReadOnlyCollection<Guid> audioRequestIds, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<IReadOnlyList<Guid>> ListRetainedRequestIdsAsync(string channelId, IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<IReadOnlyDictionary<Guid, string>> ListProtectedAttachmentsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> turnIds, CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected lookup.");
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int graceSeconds, CancellationToken ct) => throw new InvalidOperationException("Unexpected lookup.");
    }
}
