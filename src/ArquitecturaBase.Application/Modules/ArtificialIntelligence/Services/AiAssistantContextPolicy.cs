using System.Globalization;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantContextPolicy(IAiAssistantReader reader, IAiConnectionReader connections, AiAssistantAccessGuard guard, AiAssistantResponsePolicy responsePolicy, AiConnectionCandidatePolicy credentialPolicy, AiAssistantCapabilityCatalogPolicy capabilities, AiAssistantLimits limits, TimeProvider timeProvider)
{
    private const string AnswerSchema = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"answer\",\"clarification\",\"unsupported\"]},\"text\":{\"type\":\"string\"},\"evidenceIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}},\"required\":[\"kind\",\"text\",\"evidenceIds\"],\"additionalProperties\":false}";
    private const string AnswerSchemaV2 = "{\"type\":\"object\",\"properties\":{\"kind\":{\"type\":\"string\",\"enum\":[\"answer\",\"clarification\",\"unsupported\",\"proposal\"]},\"text\":{\"type\":\"string\"},\"evidenceIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"proposalId\":{\"type\":[\"string\",\"null\"]}},\"required\":[\"kind\",\"text\",\"evidenceIds\",\"proposalId\"],\"additionalProperties\":false}";
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
        if (connection.Revision != turn.ConnectionRevision || connection.Provider != turn.Provider ||
            profile.Value.Purpose == AiAssistantPurpose.Interpret && connection.Model != turn.Model) return AiAssistantErrors.ConnectionChanged;
        if (!connection.Enabled || connection.ProtectedApiKey is null || !guard.IsProfileQualified(profile.Value, connection) ||
            guard.Model(profile.Value, connection) != turn.Model) return AiAssistantErrors.Unavailable;
        Result<AiAssistantCapabilityCatalogResponse> offered = profile.Value.Purpose == AiAssistantPurpose.NormalizeAudio
            ? new AiAssistantCapabilityCatalogResponse([], [], profile.Value.ChannelContext.RequiredPermissions)
            : await capabilities.BuildAsync(profile.Value.ChannelContext, turn.AttachmentKey is not null, cancellationToken);
        if (offered.IsFailure) return offered.Error;
        if (offered.Value.Capabilities.Count != profile.Value.ChannelContext.AllowedCapabilities.Count) return AiAssistantErrors.ContentBlocked;
        var credential = credentialPolicy.Reveal(connection.Provider, connection.ProtectedApiKey);
        if (credential.IsFailure) return credential.Error;
        var question = responsePolicy.Reveal(turn, "question", turn.ProtectedQuestion);
        if (question is null) return AiAssistantErrors.ContentUnavailable;
        if (turn.ProtectedTranscript is { } protectedTranscript)
        {
            var transcript = responsePolicy.Reveal(turn, "transcript", protectedTranscript);
            if (transcript is null) return AiAssistantErrors.ContentUnavailable;
            question = AiAssistantAudioPolicy.Spoken(question, transcript);
        }
        if (profile.Value.Purpose == AiAssistantPurpose.NormalizeAudio)
        {
            try
            {
                var audio = JsonSerializer.Deserialize<ArquitecturaBase.Application.Models.Assistant.AssistantAudioInput>(question, AiAssistantResponsePolicy.JsonOptions);
                var qualified = guard.AudioProfile(profile.Value, connection);
                if (audio is null || AiAssistantAudioPolicy.Validate(audio) is not null || qualified is null || audio.Size > qualified.MaximumAudioBytes ||
                    !qualified.ContentTypes.Contains(AiAssistantAudioPolicy.MediaType(audio), StringComparer.Ordinal)) return AiAssistantErrors.ContentUnavailable;
                if (audio.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || turn.ContentExpiresAtUtc > audio.ExpiresAtUtc) return AiAssistantErrors.Expired;
                var normalized = new LlmConversationRequest(turn.Provider, turn.Model, credential.Value, "", [], [], [], null,
                    0, limits.ProviderResponseBytes, limits.ContinuationBytes) { Audio = audio, AudioMaximumBytes = qualified.MaximumAudioBytes };
                return new AiAssistantExecutionContextResponse(claim, turn, normalized, [], [], 0, 0) { ExecutionProfile = profile.Value };
            }
            catch (JsonException) { return AiAssistantErrors.ContentUnavailable; }
        }
        // The conversation's spreadsheet goes as data at the end of the current message: outside the history's characters, inside
        // the context budget.
        StructuredSpreadsheet? spreadsheet = null;
        if (turn.AttachmentKey is not null)
        {
            var sealedAttachments = await reader.ListProtectedAttachmentsAsync(turn.OwnerUserId, [turn.Id], cancellationToken);
            var attachment = sealedAttachments.TryGetValue(turn.Id, out var protectedAttachment)
                ? AiAssistantAttachmentPolicy.Read(responsePolicy.Reveal(turn, AiAssistantAttachmentPolicy.Purpose, protectedAttachment)) : null;
            if (attachment is null) return AiAssistantErrors.ContentUnavailable;
            spreadsheet = attachment.Spreadsheet;
            question = AiAssistantAttachmentPolicy.Message(question, spreadsheet, turn.Culture);
        }
        var page = await reader.ListTurnsAsync(claim.OwnerUserId, claim.ConversationId,
            new ListAiAssistantTurnsRequest { PageSize = limits.HistoryTurns + 1, Sort = "-ordinal" }, cancellationToken);
        var expiredThroughOrdinal = page.Items.Where(t => t.Ordinal < turn.Ordinal &&
            (t.ContentExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || t.ProtectedQuestion is null)).Select(t => t.Ordinal).DefaultIfEmpty(0).Max();
        var history = new List<AiAssistantTurnResponse>();
        // A channel conversation has earlier turns only when a message continued its previous operation. Of their spreadsheets, only
        // the one of the turn that brought each comes, to name it when it came without text.
        var earlier = await AiAssistantAttachmentPolicy.WithOwnAttachmentsAsync(reader, claim.OwnerUserId,
            [.. page.Items.Where(t => t.Ordinal < turn.Ordinal && t.Ordinal > expiredThroughOrdinal && t.State == AiAssistantTurnState.Completed)], cancellationToken);
        foreach (var previous in earlier)
        {
            var mapped = responsePolicy.Map(previous, true, limits.PollingAfterSeconds);
            if (mapped.IsFailure) return mapped.Error;
            if (mapped.Value.ContentAccess == "allowed" && (mapped.Value.Answer is not null || mapped.Value.ObservedProposal is not null)) history.Add(mapped.Value);
        }
        var messages = AiAssistantHistoryPolicy.Messages(history, limits.HistoryCharacters, turn.Culture).ToList();
        messages.Add(new("user", question));
        const string proposalSchema = "{\"type\":\"object\",\"properties\":{\"proposalId\":{\"type\":\"string\"}},\"required\":[\"proposalId\"],\"additionalProperties\":false}";
        var tools = offered.Value.Capabilities.Select(d => new LlmToolDefinition(d.Id, d.Description, d.InputSchemaJson)).ToList();
        foreach (var terminal in offered.Value.Terminals)
            tools.Add(terminal switch
            {
                { Id: "assistant_answer", Version: 2 } => new("assistant_answer", AssistantTexts.Get("Prompt.TerminalAnswerV2Description", turn.Culture), AnswerSchemaV2),
                { Id: "assistant_answer" } => new("assistant_answer", AssistantTexts.Get("Prompt.TerminalAnswerDescription", turn.Culture), AnswerSchema),
                _ => new("assistant_proposal", AssistantTexts.Get("Prompt.TerminalProposalDescription", turn.Culture), proposalSchema),
            });
        var system = AssistantTexts.Get(profile.Value.InstructionsId != "home-system" ? "Prompt.CapabilitiesV1"
            : profile.Value.InstructionsVersion == AiAssistantChannelPolicy.HomeInstructionsVersion ? "Prompt.SystemV2" : "Prompt.SystemV1", turn.Culture);
        if (profile.Value.OccurredAtUtc is { } occurredAtUtc)
            system += "\n" + string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.SourceTime", turn.Culture),
                occurredAtUtc.ToString("O", CultureInfo.InvariantCulture), turn.TimeZoneId);
        var request = new LlmConversationRequest(turn.Provider, turn.Model, credential.Value, system, messages, tools, [], null,
            limits.OutputTokens, limits.ProviderResponseBytes, limits.ContinuationBytes);
        if (RequestBytes(request) > limits.ContextBytes) return AiAssistantErrors.LimitExceeded;
        return new AiAssistantExecutionContextResponse(claim, turn, request, [], [], 0, 0) { ExecutionProfile = profile.Value, Attachment = spreadsheet };
    }

    private static int RequestBytes(LlmConversationRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
        new { request.Model, request.SystemPrompt, request.Messages, request.Tools, request.ToolResults, request.Continuation }, AiAssistantResponsePolicy.JsonOptions));
}
