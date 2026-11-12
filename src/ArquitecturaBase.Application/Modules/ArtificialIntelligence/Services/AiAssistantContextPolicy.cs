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
        // The catalog is decided again for the kind of file sealed with it, without decrypting the file; a kind sealed without a file is
        // an invalid seal, as in the dispatch guard.
        var file = AiAssistantAttachmentPolicy.Sealed(profile.Value, turn.AttachmentKey);
        if (file is null) return AiAssistantErrors.ContentBlocked;
        Result<AiAssistantCapabilityCatalogResponse> offered = AiAssistantChannelPolicy.IsTechnical(profile.Value.Purpose)
            ? new AiAssistantCapabilityCatalogResponse([], [], profile.Value.ChannelContext.RequiredPermissions)
            : await capabilities.BuildAsync(profile.Value.ChannelContext, file.Value, cancellationToken);
        if (offered.IsFailure) return offered.Error;
        if (offered.Value.Capabilities.Count != profile.Value.ChannelContext.AllowedCapabilities.Count ||
            offered.Value.Preloaded.Count != (profile.Value.ChannelContext.PreloadedCapabilities?.Count ?? 0)) return AiAssistantErrors.ContentBlocked;
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
        // ReadDocument: only the sealed instructions in the turn's culture and the private document, without history, capabilities or
        // terminals; the profile sealed at acceptance still has to read this document.
        if (profile.Value.Purpose == AiAssistantPurpose.ReadDocument)
        {
            var document = AiAssistantDocumentPolicy.ReadDescriptor(question);
            var qualified = guard.DocumentProfile(profile.Value, connection);
            if (document is null || qualified is null || !AiAssistantDocumentPolicy.Fits(document, qualified)) return AiAssistantErrors.ContentUnavailable;
            if (document.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime || turn.ContentExpiresAtUtc > document.ExpiresAtUtc) return AiAssistantErrors.Expired;
            var reading = new LlmConversationRequest(turn.Provider, turn.Model, credential.Value, AssistantTexts.Get("Prompt.DocumentReadV1", turn.Culture),
                [], [], [], null, limits.DocumentOutputTokens, limits.ProviderResponseBytes, limits.ContinuationBytes)
                { Document = document, DocumentMaximumBytes = qualified.MaximumDocumentBytes, LowReasoningEffort = qualified.SupportsReasoningEffort };
            return new AiAssistantExecutionContextResponse(claim, turn, reading, [], [], 0, 0) { ExecutionProfile = profile.Value };
        }
        // The conversation's file, a spreadsheet or a document, goes as data at the end of the current message: outside the history's
        // characters, inside the context budget. It has to be the kind its catalog was decided for.
        StructuredSpreadsheet? spreadsheet = null;
        StructuredDocument? attachedDocument = null;
        if (turn.AttachmentKey is not null)
        {
            var sealedAttachments = await reader.ListProtectedAttachmentsAsync(turn.OwnerUserId, [turn.Id], cancellationToken);
            var attachment = sealedAttachments.TryGetValue(turn.Id, out var protectedAttachment)
                ? AiAssistantAttachmentPolicy.Read(responsePolicy.Reveal(turn, AiAssistantAttachmentPolicy.Purpose, protectedAttachment)) : null;
            if (attachment is null || attachment.Kind != file) return AiAssistantErrors.ContentUnavailable;
            (spreadsheet, attachedDocument) = (attachment.Spreadsheet, attachment.Document);
            question = AiAssistantAttachmentPolicy.Message(question, attachment, turn.Culture);
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
        // The message's time goes at the very end, after any file, so the instructions and what precedes it stay the same between turns.
        if (profile.Value.OccurredAtUtc is { } occurredAtUtc)
            question += "\n\n" + string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.SourceTime", turn.Culture),
                occurredAtUtc.ToString("O", CultureInfo.InvariantCulture), turn.TimeZoneId);
        messages.Add(new("user", question));
        const string proposalSchema = "{\"type\":\"object\",\"properties\":{\"proposalId\":{\"type\":\"string\"}},\"required\":[\"proposalId\"],\"additionalProperties\":false}";
        const string proposalsSchema = "{\"type\":\"object\",\"properties\":{\"proposalIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"minItems\":1,\"maxItems\":3}},\"required\":[\"proposalIds\"],\"additionalProperties\":false}";
        var tools = offered.Value.Capabilities.Select(d => new LlmToolDefinition(d.Id, d.Description, d.InputSchemaJson)).ToList();
        foreach (var terminal in offered.Value.Terminals)
            tools.Add(terminal switch
            {
                { Id: "assistant_answer", Version: 2 } => new("assistant_answer", AssistantTexts.Get("Prompt.TerminalAnswerV2Description", turn.Culture), AnswerSchemaV2),
                { Id: "assistant_answer" } => new("assistant_answer", AssistantTexts.Get("Prompt.TerminalAnswerDescription", turn.Culture), AnswerSchema),
                { Id: "assistant_proposal", Version: 2 } => new("assistant_proposal", AssistantTexts.Get("Prompt.TerminalProposalV2Description", turn.Culture), proposalsSchema),
                _ => new("assistant_proposal", AssistantTexts.Get("Prompt.TerminalProposalDescription", turn.Culture), proposalSchema),
            });
        var system = AssistantTexts.Get(profile.Value.InstructionsId != "home-system" ? "Prompt.CapabilitiesV1"
            : profile.Value.InstructionsVersion == AiAssistantChannelPolicy.HomeInstructionsVersion ? "Prompt.SystemV2" : "Prompt.SystemV1", turn.Culture);
        var request = new LlmConversationRequest(turn.Provider, turn.Model, credential.Value, system, messages, tools, [], null,
            limits.OutputTokens, limits.ProviderResponseBytes, limits.ContinuationBytes);
        if (RequestBytes(request) > limits.ContextBytes) return AiAssistantErrors.LimitExceeded;
        return new AiAssistantExecutionContextResponse(claim, turn, request, [], [], 0, 0)
            { ExecutionProfile = profile.Value, Attachment = spreadsheet, Document = attachedDocument };
    }

    private static int RequestBytes(LlmConversationRequest request) => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
        new { request.Model, request.SystemPrompt, request.Messages, request.Tools, request.ToolResults, request.Continuation }, AiAssistantResponsePolicy.JsonOptions));
}
