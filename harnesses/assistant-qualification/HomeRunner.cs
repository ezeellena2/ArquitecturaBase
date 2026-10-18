using System.Diagnostics;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.SystemQueries;
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
        Console.WriteLine("qualification-home-bootstrap-start");
        var bootstrap = await assistant.BootstrapAsync(cancellationToken);
        Console.WriteLine("qualification-home-bootstrap-finished: "+bootstrap.IsSuccess);
        if(bootstrap.IsSuccess)Console.WriteLine("qualification-home-bootstrap-availability: "+bootstrap.Value.Availability);
        else Console.WriteLine("qualification-home-bootstrap-error-code: "+bootstrap.Error.Code);
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
            new("expected-fixture-facts", FixedFactsMatch(testCase,result.Answer?.Text,result.Evidences)),
        };
        // Text is examined in memory and immediately discarded after checks. Never serialize this observation itself.
        return new(requestId, result.ConversationId, result.ConversationRevision, result,
            technical, elapsed.Elapsed.TotalSeconds, checks, measurements.EndCase());
    }
    internal static bool FixedFactsMatch(HomeCase testCase,string? answer,IReadOnlyList<SystemQueryEvidence> evidence)
    {
        if(string.IsNullOrWhiteSpace(answer))return false;
        answer=Regex.Replace(answer,@"[*_~\x60]",string.Empty,RegexOptions.CultureInvariant);
        if(!(testCase.ExpectedAnswerFragments ?? []).All(fragment => answer.Contains(fragment,StringComparison.OrdinalIgnoreCase)))return false;
        if(testCase.Id.StartsWith("home-users-",StringComparison.Ordinal) || testCase.Id.StartsWith("home-mixed-",StringComparison.Ordinal))
        {
            var values=evidence.Where(item=>item.SourceId=="users.summary" && item.IsComplete).SelectMany(item=>item.Rows).SelectMany(row=>row.Values).ToArray();
            var active=values.Where(value=>value.Label==(testCase.Culture=="es"?"Cuentas activas":"Active accounts")).Select(value=>value.Value).Distinct(StringComparer.Ordinal).ToArray();
            var inactive=values.Where(value=>value.Label==(testCase.Culture=="es"?"Cuentas inactivas":"Inactive accounts")).Select(value=>value.Value).Distinct(StringComparer.Ordinal).ToArray();
            return active.Length==1 && inactive.Length==1
                && CountFact(answer,active[0],testCase.Culture=="es"?"activ[oa]s?":"active",testCase.Culture)
                && CountFact(answer,inactive[0],testCase.Culture=="es"?"inactiv[oa]s?":"inactive",testCase.Culture);
        }
        if(testCase.Id.StartsWith("home-business-",StringComparison.Ordinal) || testCase.Id.StartsWith("home-history-start-",StringComparison.Ordinal))
        {
            var opening=answer.IndexOf("09:00",StringComparison.Ordinal);
            return !Negated(answer) && opening>=0 && answer.IndexOf("17:00",StringComparison.Ordinal)>opening;
        }
        return true;
    }
    private static bool Negated(string value)=>Regex.IsMatch(value,@"\b(no|not|never|nunca)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static bool CountFact(string answer,string value,string label,string culture)
    {
        var noun=culture=="es"?@"(?:cuentas?|usuarios?)":@"(?:accounts?|users?)";
        var forward=@"\b(?<count>[0-9]+)\b\s+(?:"+noun+@"\s+)?"+label+@"\b";
        var reverse=@"\b"+label+@"\b(?:\s+"+noun+@")?\s*(?::|=|\bson\b|\bare\b|\bis\b)?\s*\b(?<count>[0-9]+)\b";
        var claims=Regex.Matches(answer,forward+"|"+reverse,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant).Cast<Match>().ToArray();
        return claims.Length>0 && claims.All(claim=>claim.Groups["count"].Value==value && !NegatedCountClaim(answer,claim));
    }
    private static bool NegatedCountClaim(string answer,Match claim)
    {
        var start=answer.LastIndexOfAny(['.',';',',','!','?','\n'],Math.Max(0,claim.Index-1))+1;
        var end=answer.IndexOfAny(['.',';',',','!','?','\n'],claim.Index+claim.Length);
        var before=answer[start..claim.Index];
        var after=answer[(claim.Index+claim.Length)..(end<0?answer.Length:end)];
        var separators=@"\b(?:and|y|but|pero)\b";
        var prefix=Regex.Split(before,separators,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)[^1];
        var suffix=Regex.Split(after,separators,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)[0];
        return Negated(prefix+claim.Value+suffix);
    }
}
