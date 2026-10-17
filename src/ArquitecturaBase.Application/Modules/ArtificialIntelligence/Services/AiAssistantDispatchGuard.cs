using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantDispatchGuard(IAiAssistantRepository repository, IAiConnectionRepository connections,
    IAiConnectionReader connectionReader, AiAssistantAccessGuard guard, AiAssistantCapabilityCatalog catalog)
{
    public async Task LockInstallationAsync(CancellationToken ct)
    {
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
    }
    public async Task ReserveSourcesAsync(AiAssistantTurn turn, CancellationToken ct)
    {
        var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        if (profile.IsFailure || conversation is null) return;
        foreach (var reference in profile.Value.ChannelContext.AllowedCapabilities)
            if (catalog.Find(reference) is { } definition)
                conversation.RequirePermissions(definition.RequiredPermissions.Actor);
    }
    public async Task LockAsync(AiAssistantClaimResponse claim, CancellationToken ct)
    {
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
        await repository.LockActorAsync(claim.OwnerUserId, ct);
        await repository.LockConversationAsync(claim.ConversationId, ct);
    }

    public async Task<Error?> ValidateAsync(AiAssistantTurn turn, CancellationToken ct)
    {
        var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
        if (conversation is null) return AiAssistantErrors.NotFound;
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        if (profile.IsFailure || !AiAssistantChannelPolicy.Matches(profile.Value, turn.OwnerUserId, conversation.ChannelId, turn.Culture, turn.TimeZoneId))
            return AiAssistantErrors.ContentBlocked;
        var actor = await guard.AuthorizeCapturedAsync(profile.Value.ChannelContext, ct);
        if (actor.IsFailure) return actor.Error;
        if (!await guard.CanReadAsync(turn.OwnerUserId, conversation.RequiredPermissions, ct)) return AiAssistantErrors.ContentBlocked;
        if (profile.Value.Purpose == AiAssistantPurpose.Interpret)
        {
            var offered = await catalog.BuildAsync(profile.Value.ChannelContext, ct);
            if (offered.IsFailure) return offered.Error;
            if (offered.Value.Capabilities.Count != profile.Value.ChannelContext.AllowedCapabilities.Count) return AiAssistantErrors.ContentBlocked;
        }
        var connection = await connectionReader.FindAsync(ct);
        if (connection.Revision != turn.ConnectionRevision || connection.Provider != turn.Provider ||
            profile.Value.Purpose == AiAssistantPurpose.Interpret && connection.Model != turn.Model) return AiAssistantErrors.ConnectionChanged;
        return connection.Enabled && connection.ProtectedApiKey is not null && guard.IsProfileQualified(profile.Value, connection) &&
            guard.Model(profile.Value, connection) == turn.Model ? null : AiAssistantErrors.Unavailable;
    }

}
