using System.Diagnostics;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace AssistantQualification;

internal sealed record HomeCase(string Id, string Culture, string Question, string[] RequiredTools,
    string ExpectedKind = "answer", int MinimumProviderSteps = 2, string[]? ExpectedAnswerFragments = null,
    string? ConversationKey = null, bool RevokeBeforeWorkerStarts = false, string ActorKind = "home",
    AuthoringExpectation? Authoring = null, DictationAudioFixture? Audio = null, SpreadsheetFixture? Spreadsheet = null);
internal sealed record HomeObservation(Guid RequestId, Guid ConversationId, long ConversationRevision,
    AiAssistantTurnResponse Turn, TurnMetadata Metadata, double ElapsedSeconds, IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);
internal sealed record HomeAudioObservation(Guid RequestId, AiAssistantAudioResponse Result, TurnMetadata Metadata, double ElapsedSeconds,
    IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);

internal sealed class HomeRunner(QualificationCurrentUser currentUser, IAiAssistantService assistant,
    IAiConnectionReader connections, SafeTurnMetadata metadata, SafeRunLedger ledger, TimeProvider clock, QualificationMeasurements measurements,
    IEnumerable<IAssistantCapabilitySource> sources, IAiAssistantAudioService audioService, IAiAssistantFileTurnService fileTurns,
    IStructuredAttachmentReader attachments)
{
    private static readonly string[] HomeAuthoringOffered = ["forms.authoring.forms", "forms.authoring.catalogs", "forms.authoring.propose-form",
        "forms.authoring.propose-catalog", "forms.catalog-filters.metadata", "forms.catalog-filters.propose"];
    // Stage 2: what Home offers an author in a turn with a spreadsheet once the front draws it (plan task 9), by Id and version; never
    // version 1 of the catalog, nor decline, which only a channel says.
    internal static readonly AssistantCapabilityReference[] SpreadsheetOffered = [new("forms.authoring.forms", 1), new("forms.authoring.catalogs", 1),
        new(AuthoringOracle.ProposeForm, 1), new(AuthoringOracle.ProposeCatalog, SpreadsheetOracle.MappedCatalogVersion), new(SpreadsheetOracle.ProposeImport, 1),
        new("forms.catalog-filters.metadata", 1), new(AuthoringOracle.PeopleCatalog, 1)];
    private static readonly AssistantCapabilityReference[] SpreadsheetNotOffered = [new(AuthoringOracle.ProposeCatalog, 1), new(AuthoringOracle.Decline, 1)];

    // IA4: the real Home audio service stores the public recording privately and normalizes it in home-audio. Its own ledger
    // identity ("<case>-audio") is fixed before the submit; resume only reads it back.
    public async Task<HomeAudioObservation> NormalizeAsync(HomeCase testCase, QualificationActor actor, CancellationToken cancellationToken)
    {
        if (testCase.Audio is not { } audio || !AudioFixtureRules.IsValidBrowser(audio) || actor.Culture != testCase.Culture)
            throw new InvalidOperationException("Invalid public Home audio fixture.");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "audio", audio.FileName), cancellationToken);
        if (bytes.LongLength != audio.ByteCount || EvidenceWriter.HashPublicBytes(bytes) != audio.Sha256 || AudioFixtureRules.DetectBrowser(bytes) != audio.ContentType)
            throw new InvalidOperationException("Public Home audio fixture changed.");
        currentUser.Set(actor.UserId);
        var requestId = Guid.CreateVersion7();
        var entry = new QualificationLedgerEntry(testCase.Id + "-audio", requestId, actor.UserId, null, null, "before-submit")
            { OperationKind = "home-audio", ChannelId = "home-audio", Culture = testCase.Culture };
        await ledger.SaveAsync(entry, cancellationToken);
        measurements.BeginCase();
        var elapsed = Stopwatch.StartNew();
        var accepted = await audioService.SubmitAsync(new SubmitAiAssistantAudioRequest(requestId, testCase.Culture, bytes), cancellationToken);
        if (accepted.IsFailure) throw QualificationRejections.Stop("home-audio-submit", accepted.Error);
        await ledger.SaveAsync(entry with { Phase = "accepted" }, cancellationToken);
        AiAssistantAudioResponse? result = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var observed = await audioService.GetAsync(requestId, testCase.Culture, cancellationToken);
            if (observed.IsFailure) throw QualificationRejections.Stop("home-audio-status", observed.Error);
            result = observed.Value;
            if (result.State is not ("pending" or "running" or "queued")) break;
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(result.PollingAfterSeconds, 1, 30)), clock, cancellationToken);
        }
        if (result is null || result.State is "pending" or "running" or "queued")
            throw new TimeoutException("Retain the accepted Home audio request identity.");
        var technical = await metadata.FindByRequestAsync(actor.UserId, requestId, cancellationToken);
        // The transcript is compared in memory only; the observation is never serialized.
        return new(requestId, result, technical, elapsed.Elapsed.TotalSeconds, AudioChecks(audio, result, technical), measurements.EndCase());
    }

    // Authoring cases (IA1-A) also need the seeded forms/catalogs and, for an adjustment, the proposal its conversation showed last.
    // A dictated case (IA4) names its completed normalization; the typed question stays empty and the server takes the transcript.
    // A spreadsheet case (Stage 2) uploads its file through the real file turn; one that inherits it is a plain turn of the same
    // conversation, whose previous turn sealed the copy under previousAttachmentKey.
    public async Task<HomeObservation> RunAsync(HomeCase testCase, QualificationActor actor,
        (Guid Id, long Revision)? conversation, CancellationToken cancellationToken, QualificationFixture? fixture = null,
        QualificationChannelFixture? forms = null, AssistantObservedProposal? previousProposal = null, HomeAudioObservation? dictated = null,
        string? previousAttachmentKey = null)
    {
        if (testCase.Authoring is not null && (fixture is null || forms is null))
            throw new InvalidOperationException("Authoring cases need the seeded forms and catalogs.");
        if ((testCase.Audio is null) != (dictated is null) || dictated is { Result.Transcript: null })
            throw new InvalidOperationException("A dictated case needs exactly its completed normalization.");
        if (testCase.Spreadsheet is { } attached && (testCase.Authoring is null || attached.Inherited && (conversation is null || previousAttachmentKey is null)))
            throw new InvalidOperationException("A spreadsheet case needs its expectation and, to inherit it, its conversation.");
        // The sheet the model reads, prepared from the public bytes by the real port exactly as the file turn prepares it.
        var sheetBytes = testCase.Spreadsheet is { } fixtureSheet ? await QualificationSpreadsheets.ReadAsync(fixtureSheet, cancellationToken) : null;
        var sheet = sheetBytes is null ? null : await QualificationSpreadsheets.PrepareAsync(attachments, sheetBytes, testCase.Spreadsheet!, cancellationToken);
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
        var turn = new SubmitAiAssistantTurnRequest(requestId, conversation?.Id, conversation?.Revision, connection.Revision, testCase.Question,
            testCase.Culture) { AudioRequestId = dictated?.RequestId };
        var uploads = testCase.Spreadsheet is { Inherited: false };
        var accepted = uploads
            ? await fileTurns.SubmitAsync(new SubmitAiAssistantFileTurnRequest(turn, sheetBytes!, testCase.Spreadsheet!.FileName), cancellationToken)
            : await assistant.SubmitAsync(turn, cancellationToken);
        if (accepted.IsFailure) throw QualificationRejections.Stop(uploads ? "home-file-submit" : "home-submit", accepted.Error);
        await ledger.SaveAsync(new(testCase.Id, requestId, actor.UserId, accepted.Value.ConversationId,
            accepted.Value.ConversationRevision, "accepted"), cancellationToken);
        AiAssistantTurnResponse? result = null;
        var deadline = clock.GetUtcNow().AddSeconds(240);
        while (clock.GetUtcNow() < deadline)
        {
            var observed = await assistant.GetTurnAsync(requestId, cancellationToken);
            if (observed.IsFailure) throw QualificationRejections.Stop("home-status", observed.Error);
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
        var proposalCapabilities = sources.SelectMany(source => source.Definitions).Where(definition => definition.Kind == AssistantCapabilityKind.Proposal)
            .Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        if (sheet is not null)
            AddSpreadsheetChecks(checks, testCase, result, technical, forms!, sheet, proposalCapabilities, conversation, previousProposal, previousAttachmentKey);
        else if (testCase.Authoring is not null)
            AddAuthoringChecks(checks, testCase, result, technical, forms!, fixture!, proposalCapabilities, conversation, previousProposal);
        if (dictated is not null) AddDictationChecks(checks, result, dictated.RequestId, dictated.Result.Transcript!);
        // Text is examined in memory and immediately discarded after checks. Never serialize this observation itself.
        return new(requestId, result.ConversationId, result.ConversationRevision, result,
            technical, elapsed.Elapsed.TotalSeconds, checks, measurements.EndCase());
    }
    // IA1-A: home-system@2, the permission-filtered offer and either no proposal at all, or the expected owner proposal named by
    // assistant_answer@2, resting on earlier reads, recovered after a rejection, alone in its turn and, for an adjustment, in the
    // same conversation with the previous name and prefix. Payload facts come from AuthoringOracle.
    internal static void AddAuthoringChecks(List<CheckEvidence> checks,HomeCase testCase,AiAssistantTurnResponse result,TurnMetadata technical,
        QualificationChannelFixture fixture,QualificationFixture actors,IReadOnlySet<string> proposalCapabilities,
        (Guid Id,long Revision)? conversation,AssistantObservedProposal? previous)
    {
        if (testCase.Authoring is not { } expected) return;
        checks.Add(new("home-instructions-v2", technical.Stamps is { InstructionsId: "home-system", InstructionsVersion: 2 }));
        checks.Add(new("authoring-offer-matches-permissions", testCase.ActorKind == "author"
            ? AuthoringOracle.Offered(technical.OfferedCapabilities, HomeAuthoringOffered, [AuthoringOracle.Decline])
            : AuthoringOracle.Offered(technical.OfferedCapabilities, [], [..HomeAuthoringOffered, AuthoringOracle.Decline])));
        if (expected.Capability is not { } capability)
        {
            checks.Add(new("no-proposal", result.Proposal is null && result.ObservedProposal is null));
            checks.Add(new("no-proposal-attempted", AuthoringOracle.OnlyProposalsOf(technical.Capabilities, proposalCapabilities, null)));
            return;
        }
        var proposal = result.ObservedProposal;
        checks.Add(new("proposal-terminal-matches-observed", result.Answer?.Kind == "proposal" && proposal is not null && result.Proposal is { } shown
            && shown.Id == proposal.Id && proposal.TurnId == result.TurnId && proposal.Capability == new AssistantCapabilityReference(capability, 1)
            && shown.Capability == capability && shown.Version == 1));
        checks.Add(new("authoring-reads-before-proposal", AuthoringOracle.ReadsBeforeProposal(proposal, testCase.RequiredTools)));
        checks.Add(new("rejection-recovered-if-any", AuthoringOracle.RejectionsRecovered(technical.Capabilities, capability)));
        checks.Add(new("only-expected-proposal-capability", AuthoringOracle.OnlyProposalsOf(technical.Capabilities, proposalCapabilities, capability)));
        if (expected.Continues is not null)
            checks.Add(new("continued-same-conversation", conversation is { } current && result.ConversationId == current.Id
                && technical.ConversationId == current.Id));
        checks.AddRange(AuthoringOracle.PayloadChecks(expected, testCase.Culture, proposal?.Payload, fixture, actors, previous?.Payload));
    }

    // Stage 2 in Home: home-system@2, the spreadsheet offer by Id and version, the turn bringing its own file (the bubble and the copy
    // keyed by its request) or inheriting its conversation's, both untrusted notes in what the model read for the injection fixture,
    // and either no proposal at all (a spreadsheet without text asks) or the expected owner proposal in its version, resting on earlier
    // reads, recovered after a rejection, alone in its turn and, for an adjustment, in the same conversation. Payload facts: SpreadsheetOracle.
    internal static void AddSpreadsheetChecks(List<CheckEvidence> checks, HomeCase testCase, AiAssistantTurnResponse result, TurnMetadata technical,
        QualificationChannelFixture fixture, StructuredSpreadsheet sheet, IReadOnlySet<string> proposalCapabilities,
        (Guid Id, long Revision)? conversation, AssistantObservedProposal? previous, string? previousAttachmentKey)
    {
        if (testCase is not { Authoring: { } expected, Spreadsheet: { } attached }) return;
        checks.Add(new("home-instructions-v2", technical.Stamps is { InstructionsId: "home-system", InstructionsVersion: 2 }));
        checks.Add(new("spreadsheet-offer-matches-context", SpreadsheetOracle.Offered(technical.OfferedReferences, SpreadsheetOffered, SpreadsheetNotOffered)));
        checks.Add(attached.Inherited
            ? new("turn-inherited-the-spreadsheet", result.Attachment is null && previousAttachmentKey is not null && technical.AttachmentKey == previousAttachmentKey
                && technical.AttachmentSha256 == attached.Sha256 && technical.AttachmentSize == attached.ByteCount)
            : new("turn-brought-the-spreadsheet", result.Attachment is { } own && own.Name == attached.FileName && own.Kind == QualificationSpreadsheets.Kind(sheet)
                && own.Rows == sheet.RowCount && own.Size == attached.ByteCount && technical.AttachmentKey == QualificationSpreadsheets.HomeKey(result.RequestId, sheet.Kind)
                && technical.AttachmentSha256 == attached.Sha256 && technical.AttachmentSize == attached.ByteCount));
        if (attached.FileName == QualificationSpreadsheets.InjectionFile)
            checks.Add(new("untrusted-cell-notes-in-prepared-sheet", QualificationSpreadsheets.CarriesInjectedNotes(sheet)));
        if (expected.Capability is not { } capability)
        {
            checks.Add(new("no-proposal", result.Proposal is null && result.ObservedProposal is null));
            checks.Add(new("no-proposal-attempted", AuthoringOracle.OnlyProposalsOf(technical.Capabilities, proposalCapabilities, null)));
            return;
        }
        var proposal = result.ObservedProposal;
        checks.Add(new("proposal-terminal-matches-observed", result.Answer?.Kind == "proposal" && proposal is not null && result.Proposal is { } shown
            && shown.Id == proposal.Id && proposal.TurnId == result.TurnId && proposal.Capability == new AssistantCapabilityReference(capability, expected.Version)
            && shown.Capability == capability && shown.Version == expected.Version));
        checks.Add(new("authoring-reads-before-proposal", AuthoringOracle.ReadsBeforeProposal(proposal, testCase.RequiredTools)));
        checks.Add(new("rejection-recovered-if-any", AuthoringOracle.RejectionsRecovered(technical.Capabilities, capability, expected.Version)));
        checks.Add(new("only-expected-proposal-capability", AuthoringOracle.OnlyProposalsOf(technical.Capabilities, proposalCapabilities, capability)));
        if (expected.Continues is not null)
            checks.Add(new("continued-same-conversation", conversation is { } current && result.ConversationId == current.Id
                && technical.ConversationId == current.Id));
        checks.AddRange(SpreadsheetOracle.PayloadChecks(expected, testCase.Culture, proposal?.Payload, fixture, sheet, previous?.Payload));
    }

    // IA4 normalization of a Home recording: the browser STT candidate in home-audio, one model effect, no text tools, and the whole
    // spoken fixture compared in memory (only punctuation, case and accents are ignored). Never exported: fixed IDs and booleans.
    internal static IReadOnlyList<CheckEvidence> AudioChecks(DictationAudioFixture audio,AiAssistantAudioResponse response,TurnMetadata technical) =>
    [
        new("completed", response.State == "completed"),
        new("whole-original-transcript", response.Transcript is { Length: > 0 and <= 4000 } transcript
            && AudioFixtureRules.Matches(audio.ExpectedTranscript, transcript, audio.RequiredWords)),
        new("audio-purpose-stamp", technical.Stamps is { ProfileId: "openai-stt-browser-candidate", ProfileVersion: 1, InstructionsId: "audio-none-v1", InstructionsVersion: 1 }),
        new("real-channel-provenance", technical.Stamps.ChannelId == "home-audio"),
        new("single-model-effect", technical.ProviderSteps == 1),
        new("no-text-tools-or-proposals", technical.ToolCalls == 0 && technical.ToolBytes == 0 && technical.Capabilities.Count == 0
            && technical.OfferedCapabilities.Count == 0),
        new("reported-audio-usage-only", technical.AudioSeconds is null or >= 0),
    ];

    // The dictated turn names its normalization and the server took that same transcript; nothing was typed.
    internal static void AddDictationChecks(List<CheckEvidence> checks,AiAssistantTurnResponse result,Guid audioRequestId,string transcript) =>
        checks.Add(new("turn-took-server-transcript", result.Audio is { } audio && audio.RequestId == audioRequestId
            && string.Equals(audio.Transcript, transcript, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(result.Question)));

    internal static bool FixedFactsMatch(HomeCase testCase,string? answer,IReadOnlyList<SystemQueryEvidence> evidence)
    {
        if(string.IsNullOrWhiteSpace(answer))return false;
        answer=Regex.Replace(answer,@"[*_~\x60]",string.Empty,RegexOptions.CultureInvariant);
        var hoursCase=testCase.Id.StartsWith("home-business-",StringComparison.Ordinal) || testCase.Id.StartsWith("home-history-start-",StringComparison.Ordinal);
        if(hoursCase)
        {
            var rules=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
            answer=Regex.Replace(answer,@"(?<![a-z0-9])0?9(?::00)?\s*a\.?\s*m\.?(?![a-z0-9])","09:00",rules);
            answer=Regex.Replace(answer,@"(?<![a-z0-9])0?5(?::00)?\s*p\.?\s*m\.?(?![a-z0-9])","17:00",rules);
        }
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
        if(hoursCase)
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
