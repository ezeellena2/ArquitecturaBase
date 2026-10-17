using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantQualification;

internal sealed record ObservedCapability(string Id, int Version, int Bytes, string State);
internal sealed record ExecutionStamps(string ProfileId, int ProfileVersion, string ProfileSha256,
    string InstructionsId, int InstructionsVersion, string ChannelId);
internal sealed record TurnMetadata(int ProviderSteps, int ToolCalls, int ToolBytes,
    long? InputTokens, long? OutputTokens, bool UsageUnknown, decimal? AudioSeconds,
    ExecutionStamps Stamps, IReadOnlyList<ObservedCapability> Capabilities);

internal sealed class SafeTurnMetadata(ApplicationDbContext db)
{
    public async Task<TurnMetadata> FindAsync(Guid turnId, CancellationToken cancellationToken)
    {
        var turn = await db.Set<AiAssistantTurn>().AsNoTracking().Where(row => row.Id == turnId)
            .Select(row => new { row.ProviderSteps, row.ToolCalls, row.ToolBytes, row.InputTokens, row.OutputTokens,
                row.UsageUnknown, row.AudioSeconds, row.ExecutionProfileJson, row.Provider, row.Model, row.ConversationId })
            .SingleAsync(cancellationToken);
        var profile = JsonSerializer.Deserialize<AiAssistantExecutionProfileSnapshot>(turn.ExecutionProfileJson!, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The execution has no sealed profile.");
        var channelId = await db.Set<AiConversation>().AsNoTracking().Where(row => row.Id == turn.ConversationId)
            .Select(row => row.ChannelId).SingleAsync(cancellationToken);
        if (profile.ChannelContext.ChannelId != channelId || profile.ModelProfileVersion < 1 || profile.InstructionsVersion < 1)
            throw new InvalidOperationException("The execution stamp is invalid.");
        var hash = EvidenceWriter.HashPublicText(JsonSerializer.Serialize(new { profile.ModelProfileId,
            profile.ModelProfileVersion, turn.Provider, turn.Model, profile.Purpose }, JsonSerializerOptions.Web));
        var stamps = new ExecutionStamps(profile.ModelProfileId, profile.ModelProfileVersion, hash,
            profile.InstructionsId, profile.InstructionsVersion, channelId);
        var tools = await db.Set<AiAssistantToolExecution>().AsNoTracking().Where(row => row.TurnId == turnId)
            .OrderBy(row => row.Ordinal).Select(row => new ObservedCapability(row.ToolId, row.Version, row.Bytes, row.State))
            .ToArrayAsync(cancellationToken);
        return new(turn.ProviderSteps, turn.ToolCalls, turn.ToolBytes, turn.InputTokens, turn.OutputTokens,
            turn.UsageUnknown, turn.AudioSeconds, stamps, tools);
    }
}
