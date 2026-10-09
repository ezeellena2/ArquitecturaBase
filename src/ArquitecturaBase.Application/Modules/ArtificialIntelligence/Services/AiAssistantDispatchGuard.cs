using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantDispatchGuard(IAiAssistantRepository repository, IAiConnectionRepository connections,
    IAiConnectionReader connectionReader, AiAssistantAccessGuard guard, IEnumerable<ISystemQuerySource> sources)
{
    public async Task LockInstallationAsync(CancellationToken ct)
    {
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
    }
    public async Task ReserveSourcesAsync(AiAssistantTurn turn, CancellationToken ct)
    {
        var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
        foreach (var definition in sources.SelectMany(s => s.Definitions))
            if (await guard.CanReadAsync(turn.OwnerUserId, definition.RequiredPermissions, ct))
                conversation!.RequirePermissions(definition.RequiredPermissions);
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
        var actor = await guard.AuthorizeAsync(turn.OwnerUserId, ct);
        if (actor.IsFailure) return actor.Error;
        var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
        if (conversation is null) return AiAssistantErrors.NotFound;
        if (!await guard.CanReadAsync(turn.OwnerUserId, conversation.RequiredPermissions, ct)) return AiAssistantErrors.ContentBlocked;
        var connection = await connectionReader.FindAsync(ct);
        if (connection.Revision != turn.ConnectionRevision || connection.Provider != turn.Provider || connection.Model != turn.Model) return AiAssistantErrors.ConnectionChanged;
        return guard.Availability(connection) == "available" ? null : AiAssistantErrors.Unavailable;
    }

}
