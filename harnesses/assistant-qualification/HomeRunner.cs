using System.Diagnostics;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace AssistantQualification;

internal sealed record HomeCase(string Id, string Culture, string Question, string[] RequiredTools,
    string ExpectedKind = "answer", int MinimumProviderSteps = 2, string[]? ExpectedAnswerFragments = null,
    string? ConversationKey = null, bool RevokeBeforeWorkerStarts = false);
internal sealed record HomeObservation(Guid RequestId, Guid ConversationId, long ConversationRevision,
    AiAssistantTurnResponse Turn, TurnMetadata Metadata, double ElapsedSeconds, IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);

internal sealed class HomeRunner(QualificationCurrentUser currentUser, IAiAssistantService assistant,
    IAiConnectionReader connections, SafeTurnMetadata metadata, SafeRunLedger ledger, TimeProvider clock, QualificationMeasurements measurements)
{
    public async Task<HomeObservation> RunAsync(HomeCase testCase, QualificationActor actor,
        (Guid Id, long Revision)? conversation, CancellationToken cancellationToken)
    {
        currentUser.Set(actor.UserId);
        var bootstrap = await assistant.BootstrapAsync(cancellationToken);
        if (bootstrap.IsFailure || bootstrap.Value.Availability != "available")
            throw new InvalidOperationException("The real assistant entry point is unavailable for this candidate.");
        var connection = await connections.FindAsync(cancellationToken);
        // Only inspect safe availability metadata. Never access/export ProtectedApiKey.
        if (!connection.Enabled || connection.Provider.ToString() != "OpenAI" || connection.Model != "gpt-6.1-sol")
            throw new InvalidOperationException("The existing connection is not eligible for this exact model probe.");
        var requestId = Guid.CreateVersion7();
        await ledger.SaveAsync(new(testCase.Id, requestId, actor.UserId, conversation?.Id, conversation?.Revision, "before-submit"), cancellationToken);
        measurements.BeginCase();
        var elapsed = Stopwatch.StartNew();
        var accepted = await assistant.SubmitAsync(new SubmitAiAssistantTurnRequest(requestId,
            conversation?.Id, conversation?.Revision, connection.Revision, testCase.Question, testCase.Culture), cancellationToken);
        if (accepted.IsFailure) throw new InvalidOperationException("Qualification submission was rejected.");
        await ledger.SaveAsync(new(testCase.Id, requestId, actor.UserId, accepted.Value.ConversationId,
            accepted.Value.ConversationRevision, "accepted"), cancellationToken);
        AiAssistantTurnResponse? result = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var observed = await assistant.GetTurnAsync(requestId, cancellationToken);
            if (observed.IsFailure) throw new InvalidOperationException("Qualification status read was rejected.");
            result = observed.Value;
            if (result.State is not ("pending" or "running")) break;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(result.PollingAfterSeconds, 1, 30)), clock, cancellationToken);
        }
        if (result is null || result.State is "pending" or "running")
            throw new TimeoutException("Qualification polling deadline elapsed; retain the request ledger.");
        var technical = await metadata.FindAsync(result.TurnId, cancellationToken);
        var observedTools = technical.Capabilities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var evidenceIds = result.Evidences.Select(item => item.Id).ToHashSet();
        var checks = new List<CheckEvidence>
        {
            new("completed", result.State == "completed"),
            new("terminal-kind", result.Answer?.Kind == testCase.ExpectedKind),
            new("required-tools-observed", testCase.RequiredTools.All(observedTools.Contains)),
            new("continuation-then-terminal", technical.ProviderSteps >= testCase.MinimumProviderSteps),
            new("evidence-identities-observed", result.Answer is not null && result.Answer.EvidenceIds.All(evidenceIds.Contains)),
            new("bounded-provider-steps", technical.ProviderSteps <= 4),
            new("bounded-tool-calls", technical.ToolCalls <= 6),
            new("bounded-tool-bytes", technical.ToolBytes <= 65536 && technical.Capabilities.All(item => item.Bytes <= 16384)),
            new("culture-context-matches", result.Culture == testCase.Culture),
            new("expected-fixture-facts", (testCase.ExpectedAnswerFragments ?? []).All(fragment =>
                result.Answer?.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true)),
        };
        // Text is examined in memory and immediately discarded after checks. Never serialize this observation itself.
        return new(requestId, result.ConversationId, result.ConversationRevision, result,
            technical, elapsed.Elapsed.TotalSeconds, checks, measurements.EndCase());
    }
}
