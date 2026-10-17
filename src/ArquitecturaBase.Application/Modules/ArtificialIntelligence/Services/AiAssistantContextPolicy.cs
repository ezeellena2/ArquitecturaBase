using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantContextPolicy(IAiAssistantReader reader, IAiConnectionReader connections, AiAssistantAccessGuard guard, AiAssistantResponsePolicy responsePolicy, AiConnectionCandidatePolicy credentialPolicy, AiAssistantCapabilityCatalog capabilities, AiAssistantLimits limits, TimeProvider timeProvider)
{
    private const string AnswerSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"answer\",\"clarification\",\"unsupported\"]},\"text\":{\"type\":\"string\"},\"evidenceIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"kind\",\"text\",\"evidenceIds\"],\"additionalProperties\":false}";
    public async Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken)
    {
        var turn = await reader.FindTurnAsync(claim.TurnId, cancellationToken);
        if (turn is null || turn.OwnerUserId != claim.OwnerUserId || turn.Fence != claim.Fence || turn.State != AiAssistantTurnState.Running)
            return AiAssistantErrors.FenceRejected;
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        if (profile.IsFailure || !AiAssistantChannelPolicy.Matches(profile.Value, claim.OwnerUserId, turn.ChannelId, turn.Culture, turn.TimeZoneId))
            return AiAssistantErrors.ContentBlocked;
        var actor = await guard.AuthorizeCapturedAsync(profile.Value.ChannelContext, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (!await guard.CanReadAsync(claim.OwnerUserId, turn.RequiredPermissions, cancellationToken)) return AiAssistantErrors.ContentBlocked;
        if (turn.ContentExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || turn.ProtectedQuestion is null) return AiAssistantErrors.Expired;
        var connection = await connections.FindAsync(cancellationToken);
        if (connection.Revision != turn.ConnectionRevision || connection.Provider != turn.Provider || connection.Model != turn.Model) return AiAssistantErrors.ConnectionChanged;
        if (guard.Availability(connection) != "available" || !guard.IsProfileQualified(profile.Value, connection)) return AiAssistantErrors.Unavailable;
        var offered = await capabilities.BuildAsync(profile.Value.ChannelContext, cancellationToken);
        if (offered.IsFailure) return offered.Error;
        if (offered.Value.Capabilities.Count != profile.Value.ChannelContext.AllowedCapabilities.Count) return AiAssistantErrors.ContentBlocked;
        var credential = credentialPolicy.Reveal(connection.Provider, connection.ProtectedApiKey);
        if (credential.IsFailure) return credential.Error;
        var question = responsePolicy.Reveal(turn, "question", turn.ProtectedQuestion);
        if (question is null) return AiAssistantErrors.ContentUnavailable;
        var page = await reader.ListTurnsAsync(claim.OwnerUserId, claim.ConversationId,
            new ListAiAssistantTurnsRequest { PageSize = limits.HistoryTurns + 1, Sort = "-ordinal" }, cancellationToken);
        var messages = new List<LlmConversationMessage>();
        var historyCharacters = 0;
        var expiredThroughOrdinal = page.Items.Where(t => t.Ordinal < turn.Ordinal &&
            (t.ContentExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || t.ProtectedQuestion is null)).Select(t => t.Ordinal).DefaultIfEmpty(0).Max();
        foreach (var previous in page.Items.Where(t => turn.ChannelId == "home" && t.Ordinal < turn.Ordinal && t.Ordinal > expiredThroughOrdinal && t.State == AiAssistantTurnState.Completed).Reverse())
        {
            var mapped = responsePolicy.Map(previous, true, limits.PollingAfterSeconds);
            if (mapped.IsFailure) return mapped.Error;
            if (mapped.Value.ContentAccess != "allowed" || mapped.Value.Answer is null) continue;
            var length = mapped.Value.Question!.Length + mapped.Value.Answer.Text.Length;
            if (historyCharacters + length > limits.HistoryCharacters) continue;
            historyCharacters += length;
            messages.Add(new("user", mapped.Value.Question));
            messages.Add(new("assistant", mapped.Value.Answer.Text));
        }
        messages.Add(new("user", question));
        const string proposalSchema = "{\"type\":\"object\",\"properties\":{\"proposalId\":{\"type\":\"string\"}},\"required\":[\"proposalId\"],\"additionalProperties\":false}";
        var tools = offered.Value.Capabilities.Select(d => new LlmToolDefinition(d.Id, d.Description, d.InputSchemaJson)).ToList();
        foreach (var terminal in offered.Value.Terminals)
            tools.Add(terminal.Id == "assistant_answer"
                ? new("assistant_answer", AssistantTexts.Get("Prompt.TerminalAnswerDescription", turn.Culture), AnswerSchema)
                : new("assistant_proposal", AssistantTexts.Get("Prompt.TerminalProposalDescription", turn.Culture), proposalSchema));
        var system = AssistantTexts.Get(profile.Value.InstructionsId == "home-system" ? "Prompt.SystemV1" : "Prompt.CapabilitiesV1", turn.Culture);
        var request = new LlmConversationRequest(turn.Provider, turn.Model, credential.Value, system, messages, tools, [], null,
            limits.OutputTokens, limits.ProviderResponseBytes, limits.ContinuationBytes);
        if (RequestBytes(request) > limits.ContextBytes) return AiAssistantErrors.LimitExceeded;
        return new AiAssistantExecutionContextResponse(claim, turn, request, [], [], 0, 0) { ExecutionProfile = profile.Value };
    }

    private static int RequestBytes(LlmConversationRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
        new { request.Model, request.SystemPrompt, request.Messages, request.Tools, request.ToolResults, request.Continuation }, AiAssistantResponsePolicy.JsonOptions));
}
