using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;

namespace AssistantQualification;

/// <summary>
/// channel-agent (Casos del asistente): one message of a person to the business with a saved agent, in the person's automatic
/// conversation, exactly as WhatsApp captures it with WithAgent. The engine preloads the agent, the form candidates and the history and
/// closes on up to three proposals; the harness reads them back by Get and checks finite facts in memory, never the text it exports.
/// </summary>
internal sealed class AgentRunner(IAssistantOperations operations, SafeRunLedger ledger, SafeTurnMetadata metadata,
    QualificationCurrentUser currentUser, TimeProvider clock, QualificationMeasurements measurements)
{
    public async Task<ChannelObservation> RunAsync(AgentCase testCase, QualificationAgentFixture agent, QualificationChannelFixture forms, CancellationToken ct)
    {
        var person = agent.Subject(testCase.Culture, testCase.Subject);
        var context = QualificationChannelContexts.Create(person, person.UserId, "agent");
        currentUser.Set(person.UserId);
        var requestId = Guid.CreateVersion7();
        QualificationLedgerEntry Entry(string phase) => new(testCase.Id, requestId, person.UserId, null, null, phase)
        {
            OperationKind = "channel", ChannelId = context.ChannelId, InteractionMode = context.InteractionMode, Culture = context.Culture,
            SubjectUserId = person.UserId, ChannelKind = "agent",
        };
        await ledger.SaveAsync(Entry("before-submit"), ct);
        measurements.BeginCase();
        var elapsed = Stopwatch.StartNew();
        var now = clock.GetUtcNow().UtcDateTime;
        var accepted = await operations.SubmitAsync(new(requestId, context, testCase.Question, now, now.AddDays(1)), ct);
        if (accepted.IsFailure) throw QualificationRejections.Stop("agent-submit", accepted.Error);
        await ledger.SaveAsync(Entry("accepted"), ct);
        AssistantOperationResponse? outcome = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var read = await operations.GetAsync(context, requestId, ct);
            if (read.IsFailure) throw QualificationRejections.Stop("agent-status", read.Error);
            outcome = read.Value;
            if (outcome.State is not ("pending" or "running" or "queued")) break;
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct);
        }
        if (outcome is null || outcome.State is "pending" or "running" or "queued") throw new TimeoutException("Retain the existing qualification request identity.");
        var technical = await metadata.FindByRequestAsync(person.UserId, requestId, ct);
        var observed = technical.Capabilities.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var checks = new List<CheckEvidence>
        {
            new("completed", outcome.State == "completed"),
            new("real-channel-provenance", outcome.Provenance?.ChannelId == context.ChannelId),
            new("required-tools-observed", testCase.RequiredTools.All(observed.Contains)),
            new("no-home-tools", !technical.Capabilities.Any(item => QualificationSeed.HomeCapabilities.Contains(item.Id))),
            new("bounded-provider-steps", technical.ProviderSteps is >= 1 and <= 4),
            new("bounded-tool-calls", technical.ToolCalls <= 6),
            new("bounded-tool-bytes", technical.ToolBytes <= 65536 && technical.Capabilities.All(item => item.Bytes <= 16384)),
        };
        checks.AddRange(AgentOracle.ContextChecks(technical, person.UserId));
        checks.AddRange(AgentOracle.Checks(testCase, new(outcome.Proposal, outcome.Proposals, technical.Capabilities, now), forms, person.UserId));
        return new(requestId, outcome, technical, elapsed.Elapsed.TotalSeconds, checks, measurements.EndCase());
    }
}
