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

// Completed turns of the actor's own conversation: Home, or a channel's in the assistant mode with the actor as its Subject. Any
// other channel conversation keeps its own "latest" rule.
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
            if (row is null || row.State != AiAssistantTurnState.Completed) return AiAssistantErrors.NotFound;
            var captured = AiAssistantChannelPolicy.Read(row.ExecutionProfileJson);
            if (captured.IsFailure || !AiAssistantChannelPolicy.Matches(captured.Value, row.OwnerUserId, row.ChannelId, row.Culture, row.TimeZoneId))
                return AiAssistantErrors.NotFound;
            var context = captured.Value.ChannelContext;
            var home = row.ChannelId == "home";
            if (!home && (context.InteractionMode != AssistantInteractionMode.Assistant || context.SubjectUserId != context.ActorUserId))
                return AiAssistantErrors.NotFound;
            var access = await guard.AuthorizeCapturedAsync(context, cancellationToken);
            if (access.IsFailure) return access.Error;
            if (!await guard.CanReadAsync(actorUserId, row.RequiredPermissions, cancellationToken)) return AiAssistantErrors.ContentBlocked;
            // Home does not seal the original instant: the turn's own submission is when it was asked. A channel seals its message's.
            DateTime occurredAtUtc;
            if (home) occurredAtUtc = row.SubmittedAtUtc;
            else if (captured.Value.OccurredAtUtc is { Kind: DateTimeKind.Utc } sealedAtUtc) occurredAtUtc = sealedAtUtc;
            else return AiAssistantErrors.ContentUnavailable;
            var mapped = responsePolicy.Map(row, true, 0);
            if (mapped.IsFailure) return mapped.Error;
            // A turn closed with assistant_proposal@2 may hold several: the one with this Id.
            if (mapped.Value.ObservedProposals.SingleOrDefault(item => item.Id == proposalId) is not { } proposal) return AiAssistantErrors.NotFound;
            AssistantAttachmentDescriptor? attachment = null;
            if (row.AttachmentKey is not null && (attachment = await AttachmentAsync(row, cancellationToken)) is null)
                return AiAssistantErrors.ContentUnavailable;
            var provenance = new AssistantOperationProvenance(captured.Value.ModelProfileId, captured.Value.ModelProfileVersion,
                captured.Value.InstructionsId, captured.Value.InstructionsVersion, occurredAtUtc, row.ChannelId);
            // The group replaces only its own members; any other proposal keeps the rule of its own capability.
            IReadOnlyCollection<string> replacing = capabilityGroup.Contains(proposal.Capability.Id, StringComparer.Ordinal)
                ? capabilityGroup : [proposal.Capability.Id];
            return new AssistantProposalLookupResponse(context, proposal, provenance, await IsCurrentAsync(row, replacing, cancellationToken))
                { Attachment = attachment };
        });

    // The file of the proposal's turn, a spreadsheet or a document, its own or inherited. Rows never bring what is sealed: only this turn,
    // and only because it has one, asks for its descriptor (the name and the media type are not in the clear).
    private async Task<AssistantAttachmentDescriptor?> AttachmentAsync(AiAssistantTurnRow row, CancellationToken cancellationToken)
    {
        var sealedAttachments = await reader.ListProtectedAttachmentsAsync(row.OwnerUserId, [row.Id], cancellationToken);
        return sealedAttachments.TryGetValue(row.Id, out var protectedAttachment)
            && AiAssistantAttachmentPolicy.Read(responsePolicy.Reveal(row, AiAssistantAttachmentPolicy.Purpose, protectedAttachment)) is { } stored
            ? new AssistantAttachmentDescriptor(stored.StorageKey, stored.Name, stored.ContentType, stored.Size, stored.Sha256) : null;
    }

    // A later turn of the same engine conversation that closed on a proposal of the same capability, or of its group, replaced this
    // one; asking for a catalog after a form does not replace the form.
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
                if (later.IsSuccess && later.Value.ObservedProposals.Any(replacement => replacing.Contains(replacement.Capability.Id, StringComparer.Ordinal)))
                    return false;
            }
            if (turns.Items.Count < PageSize || page * PageSize >= turns.TotalCount) return true;
        }
    }
}
