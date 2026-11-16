using ArquitecturaBase.Application.Models.Assistant;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed partial class AiAssistantResponsePolicy(IAiConversationProtector protector, TimeProvider timeProvider, AiAssistantCapabilityCatalogPolicy catalog,
    ILogger<AiAssistantResponsePolicy> logger)
{
    public const string UnavailableAccess = "unavailable";
    internal static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web);
    public DateTime ServerNowUtc => timeProvider.GetUtcNow().UtcDateTime;

    // I8: content that cannot be read is a state of its row, not of the page: like a blocked one, the generic title and access
    // «unavailable», so the others are seen and this one can be removed. A page leaves one warning, with how many and the first id (I8,
    // review), never the content.
    public IReadOnlyList<(string Title, string Access)> Summaries(IReadOnlyList<AiConversationRow> rows, Func<AiConversationRow, bool> canRead)
    {
        var summaries = new List<(string Title, string Access)>(rows.Count);
        var unreadable = new List<Guid>();
        foreach (var row in rows)
        {
            var allowed = canRead(row);
            var title = MapTitle(row, allowed);
            if (title.IsSuccess) summaries.Add((title.Value, allowed ? "allowed" : "blocked"));
            else
            {
                unreadable.Add(row.Id);
                summaries.Add((AssistantTexts.Get("Conversation.Title"), UnavailableAccess));
            }
        }
        if (unreadable.Count > 0) LogUnreadableConversations(logger, unreadable.Count, unreadable[0]);
        return summaries;
    }

    // I8: a turn whose content cannot be decrypted keeps its state, failure and usage, without content and with access «unavailable». Only
    // a decryption that gives nothing counts (I8, review): a stored answer that does not decode, or a proposal of a capability this copy
    // does not know, fails as before. A page leaves one warning, with how many and the first id.
    public Result<IReadOnlyList<AiAssistantTurnResponse>> MapReadable(IReadOnlyList<AiAssistantTurnRow> rows, bool canRead, int pollingAfterSeconds)
    {
        var items = new List<AiAssistantTurnResponse>(rows.Count);
        AiAssistantTurnRow? first = null;
        var unreadable = 0;
        foreach (var row in rows)
        {
            var decryption = new Decryption();
            var mapped = Map(row, canRead, pollingAfterSeconds, decryption);
            if (mapped.IsSuccess) { items.Add(mapped.Value); continue; }
            if (!decryption.Failed) return mapped.Error;
            first ??= row;
            unreadable++;
            // Without access nothing is decrypted, so this mapping cannot fail.
            items.Add(Map(row, false, pollingAfterSeconds).Value with { ContentAccess = UnavailableAccess });
        }
        if (first is not null) UnreadableTurns(unreadable, first.Id, first.ConversationId);
        return items;
    }

    public Result<AiAssistantTurnResponse> MapReadable(AiAssistantTurnRow row, bool canRead, int pollingAfterSeconds)
    {
        var mapped = MapReadable([row], canRead, pollingAfterSeconds);
        return mapped.IsFailure ? mapped.Error : mapped.Value[0];
    }

    // Something sealed that cannot be decrypted and is left out like expired content: only how many and the ids go to the log.
    public void UnreadableTurns(int count, Guid firstTurnId, Guid conversationId) => LogUnreadableTurns(logger, count, firstTurnId, conversationId);

    public Result<string> MapTitle(AiConversationRow row, bool canRead)
    {
        var question = row.FirstQuestion;
        if (!canRead || question is null || question.ContentExpiresAtUtc <= ServerNowUtc || question.ProtectedQuestion is null)
            return AssistantTexts.Get("Conversation.Title");
        var plaintext = protector.Unprotect(row.OwnerUserId, row.Id, question.TurnId, "question",
            question.PayloadRevision, question.ProtectedQuestion);
        if (plaintext is null) return AiAssistantErrors.ContentUnavailable;
        // A dictated first turn without typed text is named after what was said.
        if (plaintext.Trim().Length == 0 && question.ProtectedTranscript is { } protectedTranscript)
            plaintext = protector.Unprotect(row.OwnerUserId, row.Id, question.TurnId, "transcript", question.PayloadRevision, protectedTranscript);
        if (plaintext is null) return AiAssistantErrors.ContentUnavailable;
        // A first turn with neither written nor dictated text is named after the file of its spreadsheet (Stage 2).
        if (plaintext.Trim().Length == 0 && question.ProtectedAttachment is { } protectedAttachment)
        {
            plaintext = AiAssistantAttachmentPolicy.Read(protector.Unprotect(row.OwnerUserId, row.Id, question.TurnId,
                AiAssistantAttachmentPolicy.Purpose, question.PayloadRevision, protectedAttachment))?.Name;
            if (plaintext is null) return AiAssistantErrors.ContentUnavailable;
        }
        var title = AiAssistantConversationTitlePolicy.Normalize(plaintext);
        return title.Length == 0 ? AssistantTexts.Get("Conversation.Title") : title;
    }

    // A first turn without written or dictated text is named after its spreadsheet, which the page does not bring: whether to ask for it.
    public bool NeedsSpreadsheetForTitle(AiConversationRow row, bool canRead) =>
        canRead && row.FirstQuestion is { AttachmentKey: not null, ProtectedTranscript: null, ProtectedAttachment: null, ProtectedQuestion: { } protectedQuestion } question
        && question.ContentExpiresAtUtc > ServerNowUtc
        && protector.Unprotect(row.OwnerUserId, row.Id, question.TurnId, "question", question.PayloadRevision, protectedQuestion) is { } plaintext
        && plaintext.Trim().Length == 0;

    public Result<AiAssistantTurnResponse> Map(AiAssistantTurnRow row, bool canRead, int pollingAfterSeconds) =>
        Map(row, canRead, pollingAfterSeconds, new Decryption());

    private Result<AiAssistantTurnResponse> Map(AiAssistantTurnRow row, bool canRead, int pollingAfterSeconds, Decryption decryption)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var access = !canRead ? "blocked" : row.ContentExpiresAtUtc <= now || row.ProtectedQuestion is null ? "expired" : "allowed";
        string? question = null;
        AiAssistantAnswer? answer = null;
        AssistantObservedProposal? proposal = null;
        IReadOnlyList<AssistantObservedProposal> proposals = [];
        AssistantNormalizedAudio? normalizedAudio = null;
        AssistantAudioInput? normalizedRecording = null;
        StructuredDocumentReading? documentReading = null;
        AssistantDocumentInput? readDocument = null;
        AiAssistantTurnAudioResponse? audio = null;
        AiAssistantTurnAttachmentResponse? attachment = null;
        string? attachedDocument = null;
        IReadOnlyList<SystemQueryEvidence> evidences = [];
        if (access == "allowed")
        {
            question = Reveal(row, "question", row.ProtectedQuestion!, decryption);
            if (question is null) return AiAssistantErrors.ContentUnavailable;
            if (row.AudioRequestId is { } audioRequestId && row.ProtectedTranscript is { } protectedTranscript)
            {
                var transcript = Reveal(row, "transcript", protectedTranscript, decryption);
                if (transcript is null) return AiAssistantErrors.ContentUnavailable;
                audio = new AiAssistantTurnAudioResponse(audioRequestId, transcript);
            }
            // Only the turn that brought a file shows it; the next ones inherit it for the model, not for the thread. A Home copy says which
            // turns brought it, and a turn without text or audio, in any channel, always brought its own. A document is also named, so the
            // history does not call it a spreadsheet.
            if (row.ProtectedAttachment is { } protectedAttachment
                && (AiAssistantHomeAttachmentPolicy.IsOwn(row) || question.Trim().Length == 0 && audio is null))
            {
                var stored = AiAssistantAttachmentPolicy.Read(Reveal(row, AiAssistantAttachmentPolicy.Purpose, protectedAttachment, decryption));
                if (stored is null) return AiAssistantErrors.ContentUnavailable;
                attachment = AiAssistantHomeAttachmentPolicy.Response(stored);
                attachedDocument = stored.Document is null ? null : stored.Name;
            }
            if (row.ProtectedAnswer is { } protectedAnswer)
            {
                var json = Reveal(row, "answer", protectedAnswer, decryption);
                if (json is null) return AiAssistantErrors.ContentUnavailable;
                if (IsKind(json, "normalizedAudio"))
                {
                    var decoded = DecodeNormalizedAudio(row, json);
                    if (decoded.IsFailure) return decoded.Error;
                    normalizedAudio = decoded.Value;
                    // A normalization's question is the descriptor of the recording it heard.
                    normalizedRecording = AiAssistantAudioPolicy.ReadDescriptor(question);
                    question = null;
                }
                else if (IsKind(json, "readDocument"))
                {
                    var decoded = DecodeDocumentReading(row, json);
                    if (decoded.IsFailure) return decoded.Error;
                    documentReading = decoded.Value;
                    // A reading's question is the descriptor of the document it read.
                    readDocument = AiAssistantDocumentPolicy.ReadDescriptor(question);
                    if (readDocument is null) return AiAssistantErrors.ContentUnavailable;
                    question = null;
                }
                else if (IsKind(json, "proposal"))
                {
                    var decoded = DecodeProposal(row, json, decryption);
                    if (decoded.IsFailure) return decoded.Error;
                    (proposal, answer) = decoded.Value;
                    if (proposal is not null) proposals = [proposal];
                }
                else if (IsKind(json, "proposals"))
                {
                    var decoded = DecodeProposals(row, json, decryption);
                    if (decoded.IsFailure) return decoded.Error;
                    proposals = decoded.Value;
                    proposal = proposals[0];
                }
                else answer = JsonSerializer.Deserialize<AiAssistantAnswer>(json, JsonOptions);
            }
            if (row.ProtectedEvidences is { } protectedEvidences)
            {
                var json = Reveal(row, "evidences", protectedEvidences, decryption);
                if (json is null) return AiAssistantErrors.ContentUnavailable;
                evidences = JsonSerializer.Deserialize<SystemQueryEvidence[]>(json, JsonOptions) ?? [];
            }
        }
        var failure = row.FailureCode is { } code ? new AiAssistantFailure(code, ErrorMessages.Find(code) ?? AssistantTexts.Get(code),
            row.State == AiAssistantTurnState.Uncertain ? "reviewUncertainty" : "newAttempt") : null;
        var usage = row.AudioSeconds is not null
            ? new LlmConversationUsage(row.UsageUnknown ? null : row.InputTokens, row.UsageUnknown ? null : row.OutputTokens) { AudioSeconds = row.AudioSeconds }
            : row.UsageUnknown || row.InputTokens is null && row.OutputTokens is null ? null : new LlmConversationUsage(row.InputTokens, row.OutputTokens);
        return new AiAssistantTurnResponse(row.Id, row.ConversationId, row.RequestId, row.Ordinal, row.ConversationRevision,
            row.Revision, State(row.State), row.Stage, access, question, answer, evidences, failure, usage,
            row.Culture, row.TimeZoneId, row.SubmittedAtUtc, now, pollingAfterSeconds)
        {
            CancelRequestedAtUtc = row.CancelRequestedAtUtc, ObservedProposal = proposal, ObservedProposals = proposals, NormalizedAudio = normalizedAudio, DocumentReading = documentReading,
            ReadDocument = readDocument, NormalizedRecording = normalizedRecording,
            Audio = audio, Attachment = attachment, AttachedDocument = attachedDocument,
            Proposal = proposal is null ? null : new AiAssistantProposalResponse(proposal.Id, proposal.Capability.Id, proposal.Capability.Version,
                JsonSerializer.SerializeToElement(proposal.Payload, proposal.Payload.GetType(), JsonOptions)),
        };
    }

    private static bool IsKind(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == expected;
    }

    private static Result<AssistantNormalizedAudio> DecodeNormalizedAudio(AiAssistantTurnRow row, string json)
    {
        var profile = AiAssistantChannelPolicy.Read(row.ExecutionProfileJson);
        if (profile.IsFailure || profile.Value.Purpose != AiAssistantPurpose.NormalizeAudio) return AiAssistantErrors.ContentUnavailable;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!Closed(document.RootElement, "kind", "text", "languages")) return AiAssistantErrors.ContentUnavailable;
            var audio = JsonSerializer.Deserialize<AssistantNormalizedAudio>(json, JsonOptions);
            return audio is not null && AiAssistantAudioPolicy.Valid(audio, 4000) ? audio : AiAssistantErrors.ContentUnavailable;
        }
        catch (JsonException) { return AiAssistantErrors.ContentUnavailable; }
    }

    // Only a reading turn stores a reading, and only a closed and whole one reads back.
    private static Result<StructuredDocumentReading> DecodeDocumentReading(AiAssistantTurnRow row, string json)
    {
        var profile = AiAssistantChannelPolicy.Read(row.ExecutionProfileJson);
        if (profile.IsFailure || profile.Value.Purpose != AiAssistantPurpose.ReadDocument) return AiAssistantErrors.ContentUnavailable;
        // N27: one a newer version wrote is left for the copy that knows it.
        return AiAssistantDocumentPolicy.ReadStored(json) is { } reading ? reading : AiAssistantDocumentPolicy.Unread(json);
    }

    // A channel stores only the selected ordinal; Home also keeps the answer that explains the proposal.
    private Result<(AssistantObservedProposal? Proposal, AiAssistantAnswer? Answer)> DecodeProposal(AiAssistantTurnRow row, string json, Decryption decryption)
    {
        try
        {
            using var selected = JsonDocument.Parse(json);
            var root = selected.RootElement;
            var explained = Closed(root, "kind", "ordinal", "text", "evidenceIds");
            if (!explained && !Closed(root, "kind", "ordinal") || root.GetProperty("kind").GetString() != "proposal" ||
                !root.GetProperty("ordinal").TryGetInt32(out var ordinal)) return AiAssistantErrors.ContentUnavailable;
            AiAssistantAnswer? answer = null;
            if (explained)
            {
                var text = root.GetProperty("text");
                var evidence = root.GetProperty("evidenceIds");
                if (text.ValueKind != JsonValueKind.String || evidence.ValueKind != JsonValueKind.Array) return AiAssistantErrors.ContentUnavailable;
                answer = new AiAssistantAnswer("proposal", text.GetString()!, evidence.EnumerateArray().Select(id => id.GetGuid()).ToArray());
            }
            return DecodeSelected(row, ordinal, answer, decryption);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { return AiAssistantErrors.ContentUnavailable; }
    }

    // assistant_proposal@2 (Casos del asistente): two or three selected proposals, in their order; a channel needs each one.
    private Result<IReadOnlyList<AssistantObservedProposal>> DecodeProposals(AiAssistantTurnRow row, string json, Decryption decryption)
    {
        try
        {
            using var selected = JsonDocument.Parse(json);
            var root = selected.RootElement;
            if (!Closed(root, "kind", "ordinals") || root.GetProperty("kind").GetString() != "proposals" ||
                root.GetProperty("ordinals") is not { ValueKind: JsonValueKind.Array } values ||
                values.GetArrayLength() is < 2 or > AiAssistantTerminalPolicy.MaximumSelected) return AiAssistantErrors.ContentUnavailable;
            var proposals = new List<AssistantObservedProposal>();
            var seen = new HashSet<int>();
            foreach (var value in values.EnumerateArray())
            {
                if (!value.TryGetInt32(out var ordinal) || !seen.Add(ordinal)) return AiAssistantErrors.ContentUnavailable;
                var decoded = DecodeSelected(row, ordinal, null, decryption);
                if (decoded.IsFailure) return decoded.Error;
                if (decoded.Value.Proposal is not { } proposal) return AiAssistantErrors.ContentUnavailable;
                proposals.Add(proposal);
            }
            return Result.Success<IReadOnlyList<AssistantObservedProposal>>(proposals);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        { return AiAssistantErrors.ContentUnavailable; }
    }

    // The proposal stored at an ordinal, with the reads it observed.
    private Result<(AssistantObservedProposal? Proposal, AiAssistantAnswer? Answer)> DecodeSelected(AiAssistantTurnRow row, int ordinal, AiAssistantAnswer? answer,
        Decryption decryption)
    {
        try
        {
            var result = row.ToolResults.SingleOrDefault(t => t.Ordinal == ordinal);
            if (result is null || result.ProtectedResult is null) return AiAssistantErrors.ContentUnavailable;
            var reference = new AssistantCapabilityReference(result.ToolId, result.Version);
            // An uninstalled owner leaves Home's explanation readable without its card; a channel needs the proposal itself.
            if (catalog.Find(reference)?.Kind != AssistantCapabilityKind.Proposal)
                return answer is not null ? (null, answer) : AiAssistantErrors.ContentUnavailable;
            var stored = Reveal(row, "tool:" + ordinal, result.ProtectedResult, decryption);
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
                var protectedRead = Reveal(row, "tool:" + readOrdinal, read.ProtectedResult, decryption);
                if (protectedRead is null) return AiAssistantErrors.ContentUnavailable;
                using var readJson = JsonDocument.Parse(protectedRead);
                var readPayload = readJson.RootElement.TryGetProperty("payload", out var content) ? content.GetRawText() : protectedRead;
                var decoded = catalog.ParseOutput(readReference, readPayload);
                if (decoded.IsFailure) return AiAssistantErrors.ContentUnavailable;
                reads.Add(new(row.Id, readOrdinal, readReference, decoded.Value));
            }
            return (new AssistantObservedProposal(row.Id, proposalId, reference, payload.Value) { ObservedReads = reads }, answer);
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

    // I8, review: whether a mapping failed because something did not decrypt, the only failure that makes a row «unavailable».
    private sealed class Decryption { public bool Failed { get; set; } }

    private string? Reveal(AiAssistantTurnRow row, string purpose, string ciphertext, Decryption decryption)
    {
        var plaintext = Reveal(row, purpose, ciphertext);
        if (plaintext is null) decryption.Failed = true;
        return plaintext;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The protected titles of {Count} assistant conversations cannot be read, the first {ConversationId}; they are shown as unavailable")]
    private static partial void LogUnreadableConversations(ILogger logger, int count, Guid conversationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The protected content of {Count} assistant turns cannot be read, the first {TurnId} in conversation {ConversationId}; they are left out as unavailable")]
    private static partial void LogUnreadableTurns(ILogger logger, int count, Guid turnId, Guid conversationId);
}
