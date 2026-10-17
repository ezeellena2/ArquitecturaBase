using ArquitecturaBase.Application.Models.Assistant;
using System.Text.Json;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantResponsePolicy(IAiConversationProtector protector, TimeProvider timeProvider, AiAssistantCapabilityCatalog catalog)
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
        AssistantObservedProposal? proposal = null;
        IReadOnlyList<SystemQueryEvidence> evidences = [];
        if (access == "allowed")
        {
            question = Reveal(row, "question", row.ProtectedQuestion!);
            if (question is null) return AiAssistantErrors.ContentUnavailable;
            if (row.ProtectedAnswer is { } protectedAnswer)
            {
                var json = Reveal(row, "answer", protectedAnswer);
                if (json is null) return AiAssistantErrors.ContentUnavailable;
                if (IsProposal(json))
                {
                    var decoded = DecodeProposal(row, json);
                    if (decoded.IsFailure) return decoded.Error;
                    proposal = decoded.Value;
                }
                else answer = JsonSerializer.Deserialize<AiAssistantAnswer>(json, JsonOptions);
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
            row.Culture, row.TimeZoneId, row.SubmittedAtUtc, now, pollingAfterSeconds) { CancelRequestedAtUtc = row.CancelRequestedAtUtc, Proposal = proposal };
    }

    private static bool IsProposal(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "proposal";
    }

    private Result<AssistantObservedProposal> DecodeProposal(AiAssistantTurnRow row, string json)
    {
        try
        {
            using var selected = JsonDocument.Parse(json);
            var root = selected.RootElement;
            if (!Closed(root, "kind", "ordinal") || root.GetProperty("kind").GetString() != "proposal" ||
                !root.GetProperty("ordinal").TryGetInt32(out var ordinal)) return AiAssistantErrors.ContentUnavailable;
            var result = row.ToolResults.SingleOrDefault(t => t.Ordinal == ordinal);
            if (result is null || result.ProtectedResult is null) return AiAssistantErrors.ContentUnavailable;
            var reference = new AssistantCapabilityReference(result.ToolId, result.Version);
            if (catalog.Find(reference)?.Kind != AssistantCapabilityKind.Proposal) return AiAssistantErrors.ContentUnavailable;
            var stored = Reveal(row, "tool:" + ordinal, result.ProtectedResult);
            if (stored is null) return AiAssistantErrors.ContentUnavailable;
            using var document = JsonDocument.Parse(stored);
            var envelope = document.RootElement;
            if (!Closed(envelope, "output", "readOrdinals")) return AiAssistantErrors.ContentUnavailable;
            var output = envelope.GetProperty("output");
            if (!Closed(output, "state", "proposalId", "payload") || !output.GetProperty("proposalId").TryGetGuid(out var proposalId) ||
                proposalId == Guid.Empty || envelope.GetProperty("readOrdinals").ValueKind != JsonValueKind.Array) return AiAssistantErrors.ContentUnavailable;
            var payload = catalog.ParseOutput(reference, output.GetProperty("payload").GetRawText());
            if (payload.IsFailure) return AiAssistantErrors.ContentUnavailable;
            var reads = new List<AssistantObservedRead>();
            foreach (var value in envelope.GetProperty("readOrdinals").EnumerateArray())
            {
                if (!value.TryGetInt32(out var readOrdinal) || readOrdinal <= 0 || readOrdinal >= ordinal || reads.Any(r => r.Ordinal == readOrdinal)) return AiAssistantErrors.ContentUnavailable;
                var read = row.ToolResults.SingleOrDefault(t => t.Ordinal == readOrdinal);
                if (read?.ProtectedResult is null) return AiAssistantErrors.ContentUnavailable;
                var readReference = new AssistantCapabilityReference(read.ToolId, read.Version);
                if (catalog.Find(readReference)?.Kind != AssistantCapabilityKind.Read) return AiAssistantErrors.ContentUnavailable;
                var protectedRead = Reveal(row, "tool:" + readOrdinal, read.ProtectedResult);
                if (protectedRead is null) return AiAssistantErrors.ContentUnavailable;
                using var readJson = JsonDocument.Parse(protectedRead);
                var readPayload = readJson.RootElement.TryGetProperty("payload", out var content) ? content.GetRawText() : protectedRead;
                var decoded = catalog.ParseOutput(readReference, readPayload);
                if (decoded.IsFailure) return AiAssistantErrors.ContentUnavailable;
                reads.Add(new(row.Id, readOrdinal, readReference, decoded.Value));
            }
            return new AssistantObservedProposal(row.Id, proposalId, reference, payload.Value) { ObservedReads = reads };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { return AiAssistantErrors.ContentUnavailable; }
    }

    private static bool Closed(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var properties = value.EnumerateObject().Select(p => p.Name).ToArray();
        return properties.Length == names.Length && properties.Distinct(StringComparer.Ordinal).Count() == properties.Length &&
            properties.All(p => names.Contains(p, StringComparer.Ordinal));
    }

    public string? Reveal(AiAssistantTurnRow row, string purpose, string ciphertext) =>
        protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, purpose, row.PayloadRevision, ciphertext);
    public static string State(AiAssistantTurnState state) => state.ToString().ToLowerInvariant();
}
