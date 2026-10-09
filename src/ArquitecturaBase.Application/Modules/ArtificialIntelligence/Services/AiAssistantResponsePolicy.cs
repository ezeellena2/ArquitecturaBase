using System.Text.Json;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantResponsePolicy(IAiConversationProtector protector, TimeProvider timeProvider)
{
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public DateTime ServerNowUtc => timeProvider.GetUtcNow().UtcDateTime;

    public Result<string> MapTitle(AiConversationRow row, bool canRead)
    {
        var question = row.FirstQuestion;
        if (!canRead || question is null || question.ContentExpiresAtUtc <= ServerNowUtc || question.ProtectedQuestion is null)
            return AssistantTexts.Get("Conversation.Title");
        var plaintext = protector.Unprotect(row.OwnerUserId, row.Id, question.TurnId, "question",
            question.PayloadRevision, question.ProtectedQuestion);
        if (plaintext is null) return AiAssistantErrors.ContentUnavailable;
        var title = AiAssistantConversationTitlePolicy.Normalize(plaintext);
        return title.Length == 0 ? AssistantTexts.Get("Conversation.Title") : title;
    }

    public Result<AiAssistantTurnResponse> Map(AiAssistantTurnRow row, bool canRead, int pollingAfterSeconds)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var access = !canRead ? "blocked" : row.ContentExpiresAtUtc <= now || row.ProtectedQuestion is null ? "expired" : "allowed";
        string? question = null;
        AiAssistantAnswer? answer = null;
        IReadOnlyList<SystemQueryEvidence> evidences = [];
        if (access == "allowed")
        {
            question = Reveal(row, "question", row.ProtectedQuestion!);
            if (question is null) return AiAssistantErrors.ContentUnavailable;
            if (row.ProtectedAnswer is { } protectedAnswer)
            {
                var json = Reveal(row, "answer", protectedAnswer);
                if (json is null) return AiAssistantErrors.ContentUnavailable;
                answer = JsonSerializer.Deserialize<AiAssistantAnswer>(json, JsonOptions);
            }
            if (row.ProtectedEvidences is { } protectedEvidences)
            {
                var json = Reveal(row, "evidences", protectedEvidences);
                if (json is null) return AiAssistantErrors.ContentUnavailable;
                evidences = JsonSerializer.Deserialize<SystemQueryEvidence[]>(json, JsonOptions) ?? [];
            }
        }
        var failure = row.FailureCode is { } code ? new AiAssistantFailure(code, ErrorMessages.Find(code) ?? AssistantTexts.Get(code),
            row.State == AiAssistantTurnState.Uncertain ? "reviewUncertainty" : "newAttempt") : null;
        var usage = row.UsageUnknown || row.InputTokens is null && row.OutputTokens is null ? null : new LlmConversationUsage(row.InputTokens, row.OutputTokens);
        return new AiAssistantTurnResponse(row.Id, row.ConversationId, row.RequestId, row.Ordinal, row.ConversationRevision,
            row.Revision, State(row.State), row.Stage, access, question, answer, evidences, failure, usage,
            row.Culture, row.TimeZoneId, row.SubmittedAtUtc, now, pollingAfterSeconds) { CancelRequestedAtUtc = row.CancelRequestedAtUtc };
    }

    public string? Reveal(AiAssistantTurnRow row, string purpose, string ciphertext) =>
        protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, purpose, row.PayloadRevision, ciphertext);
    public static string State(AiAssistantTurnState state) => state.ToString().ToLowerInvariant();
}
