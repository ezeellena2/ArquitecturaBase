using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;

// Only Home turns of the same actor: a channel conversation is a single operation and keeps its own "latest" rule.
internal sealed class AiAssistantProposalLookup(IAiAssistantReader reader, AiAssistantAccessGuard guard,
    AiAssistantResponsePolicy responsePolicy, ILogger<AiAssistantProposalLookup> logger) : IAssistantProposalLookup
{
    private const int PageSize = 50;

    public Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId,
        IReadOnlyCollection<string> capabilityGroup, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AssistantProposalLookupResponse>>(logger, "FindAssistantProposal", async () =>
        {
            ArgumentNullException.ThrowIfNull(capabilityGroup);
            if (actorUserId == Guid.Empty || requestId == Guid.Empty || proposalId == Guid.Empty) return AiAssistantErrors.NotFound;
            var row = await reader.FindTurnByRequestAsync(actorUserId, requestId, cancellationToken);
            if (row is null || row.ChannelId != "home" || row.State != AiAssistantTurnState.Completed) return AiAssistantErrors.NotFound;
            var captured = AiAssistantChannelPolicy.Read(row.ExecutionProfileJson);
            if (captured.IsFailure || !AiAssistantChannelPolicy.Matches(captured.Value, row.OwnerUserId, row.ChannelId, row.Culture, row.TimeZoneId))
                return AiAssistantErrors.NotFound;
            var access = await guard.AuthorizeCapturedAsync(captured.Value.ChannelContext, cancellationToken);
            if (access.IsFailure) return access.Error;
            if (!await guard.CanReadAsync(actorUserId, row.RequiredPermissions, cancellationToken)) return AiAssistantErrors.ContentBlocked;
            var mapped = responsePolicy.Map(row, true, 0);
            if (mapped.IsFailure) return mapped.Error;
            if (mapped.Value.ObservedProposal is not { } proposal || proposal.Id != proposalId) return AiAssistantErrors.NotFound;
            // Home does not seal the original instant: the turn's own submission is when it was asked.
            var provenance = new AssistantOperationProvenance(captured.Value.ModelProfileId, captured.Value.ModelProfileVersion,
                captured.Value.InstructionsId, captured.Value.InstructionsVersion, row.SubmittedAtUtc, row.ChannelId);
            // The group replaces only its own members; any other proposal keeps the rule of its own capability.
            IReadOnlyCollection<string> replacing = capabilityGroup.Contains(proposal.Capability.Id, StringComparer.Ordinal)
                ? capabilityGroup : [proposal.Capability.Id];
            return new AssistantProposalLookupResponse(captured.Value.ChannelContext, proposal, provenance,
                await IsCurrentAsync(row, replacing, cancellationToken));
        });

    // A later turn of the same conversation that closed on a proposal of the same capability, or of its group, replaced this one;
    // asking for a catalog after a form does not replace the form.
    private async Task<bool> IsCurrentAsync(AiAssistantTurnRow row, IReadOnlyCollection<string> replacing, CancellationToken cancellationToken)
    {
        for (var page = 1; ; page++)
        {
            var turns = await reader.ListTurnsAsync(row.OwnerUserId, row.ConversationId,
                new ListAiAssistantTurnsRequest { Page = page, PageSize = PageSize, Sort = "-ordinal" }, cancellationToken);
            foreach (var turn in turns.Items)
            {
                if (turn.Ordinal <= row.Ordinal) return true;
                if (turn.State != AiAssistantTurnState.Completed) continue;
                var later = responsePolicy.Map(turn, true, 0);
                if (later.IsSuccess && later.Value.ObservedProposal is { } replacement
                    && replacing.Contains(replacement.Capability.Id, StringComparer.Ordinal)) return false;
            }
            if (turns.Items.Count < PageSize || page * PageSize >= turns.TotalCount) return true;
        }
    }
}
