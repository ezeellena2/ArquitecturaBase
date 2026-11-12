using System.Diagnostics;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace AssistantQualification;

internal sealed record ScalarExpectation(string Key, string Type, string Value);
internal sealed record ChannelCase(string Id, string Culture, string ActorKind, string Question, string ExpectedPayload,
    string[] RequiredTools, string? FormAlias = null, ScalarExpectation[]? Values = null, bool RequireLimitation = false,
    AuthoringExpectation? Authoring = null, DictationAudioFixture? Audio = null, SpreadsheetFixture? Spreadsheet = null);
internal sealed record ChannelObservation(Guid RequestId, AssistantOperationResponse Result, TurnMetadata Metadata,
    double ElapsedSeconds, IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);

internal static class QualificationChannelContexts
{
    // IA3-A: WhatsApp's own automatic conversation also offers authoring; the engine filters each one by the person's permissions.
    public static readonly string[] AuthoringCapabilities = ["forms.authoring.forms", "forms.authoring.catalogs", "forms.authoring.propose-form",
        "forms.authoring.propose-catalog", SpreadsheetOracle.ProposeImport, "forms.authoring.decline"];
    // The automatic conversation exactly as WhatsAppAssistantAccessPolicy captures it, in its order. Since Stage 2 the catalog is version 2,
    // which maps the conversation's spreadsheet, next to the import, which the engine leaves out of a turn without a spreadsheet; since
    // Stage 3 the form is version 2, which reads the conversation's document.
    public static readonly AssistantCapabilityReference[] AutomaticReferences = [new("forms.assistant-candidates", 1), new("forms.propose-registration", 1),
        new("forms.clarify-registration", 1), new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1), new("whatsapp.decline-form", 1),
        new("forms.missing-request.propose", 1), new("forms.catalog-filters.metadata", 1), new("forms.catalog-filters.propose", 1),
        new("forms.authoring.forms", 1), new("forms.authoring.catalogs", 1), new("forms.authoring.propose-form", AuthoringOracle.DocumentFormVersion),
        new("forms.authoring.propose-catalog", SpreadsheetOracle.MappedCatalogVersion), new(SpreadsheetOracle.ProposeImport, 1), new("forms.authoring.decline", 1)];
    // An operator's manual conversation and a captured record (C4) keep their Stage 1 offer, all of it version 1.
    private static readonly AssistantCapabilityReference[] ManualReferences = [new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1),
        new("whatsapp.decline-form", 1), new("forms.assistant-candidates", 1), new("forms.propose-registration", 1), new("forms.clarify-registration", 1),
        new("forms.missing-request.propose", 1)];
    private static readonly AssistantCapabilityReference[] CapturedReferences = [new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1),
        new("whatsapp.decline-form", 1), new("forms.suggestion-candidates", 1), new("forms.propose-suggestion", 1)];
    // Everything some channel context offers, by Id and version: the stamps of the channel sets.
    public static readonly AssistantCapabilityReference[] References = [..AutomaticReferences.Concat(ManualReferences).Concat(CapturedReferences).Distinct()];
    public static readonly string[] Capabilities = [..References.Select(reference => reference.Id)];
    // "author" is the automatic conversation of a recipient who may build forms and catalogs (Actor equal to Subject).
    private static readonly string[] Kinds = ["auto", "manual", "catalog", "no-forms", "author"];
    public static AssistantChannelContext Create(QualificationActor actor, Guid subject, string kind, Guid? sourceRecordId = null)
    {
        if(!Kinds.Contains(kind,StringComparer.Ordinal)) throw new ArgumentException("Unknown qualification channel kind.",nameof(kind));
        var manual = kind == "manual";
        return new(manual ? "whatsapp-manual" : "whatsapp-assistant", actor.UserId, subject, actor.Culture, "America/Argentina/Buenos_Aires",
            new(manual ? ["whatsapp.read", "whatsapp.send"] : [], []), sourceRecordId.HasValue ? CapturedReferences : manual ? ManualReferences : AutomaticReferences,
            [new("assistant_proposal",1)]) { SourceRecordId = sourceRecordId, InteractionMode = manual ? AssistantInteractionMode.Manual : AssistantInteractionMode.Assistant };
    }

    public static AssistantChannelContext CreateAudio(Guid actorUserId,string culture) => new("whatsapp-manual",actorUserId,null,culture,
        "America/Argentina/Buenos_Aires",new(["whatsapp.read"],[]),[],[]) { InteractionMode = AssistantInteractionMode.Manual };

    // IA3-A by voice: a note of the person's own automatic conversation, normalized as WhatsAppAudioAccessPolicy captures it, with the
    // person as Actor and Subject and nothing required or offered. Its interpretation is another operation, with the author context.
    public static AssistantChannelContext CreateAutomaticAudio(Guid personUserId,string culture) => new("whatsapp-assistant",personUserId,personUserId,culture,
        "America/Argentina/Buenos_Aires",new([],[]),[],[]) { InteractionMode = AssistantInteractionMode.Assistant };

    // IA4: a Home recording's normalization is read back through the Home audio service of its actor, never through a context
    // the harness builds; its ledger names only that actor, home-audio and the culture.
    public static bool IsHomeAudio(QualificationLedgerEntry entry) => entry is { OperationKind: "home-audio", ChannelId: "home-audio",
        SubjectUserId: null, ChannelKind: null, SourceRecordId: null, PreviousRequestId: null, InteractionMode: AssistantInteractionMode.Unspecified }
        && entry.Culture is "es" or "en" && entry.CaseId.EndsWith("-audio", StringComparison.Ordinal);

    public static AssistantChannelContext Resume(QualificationLedgerEntry entry)
    {
        if(entry.Culture is not ("es" or "en") || !Enum.IsDefined(entry.InteractionMode)) throw new InvalidOperationException("Invalid ledger culture.");
        if(entry.OperationKind=="audio")
        {
            if(entry.ChannelId=="whatsapp-assistant")
            {
                if(entry.SubjectUserId!=entry.ActorUserId || entry.InteractionMode!=AssistantInteractionMode.Assistant || entry.ChannelKind is not null
                    || entry.SourceRecordId is not null || entry.PreviousRequestId is not null) throw new InvalidOperationException("Invalid audio ledger scope.");
                return CreateAutomaticAudio(entry.ActorUserId,entry.Culture);
            }
            if(entry.ChannelId!="whatsapp-manual" || entry.SubjectUserId is not null) throw new InvalidOperationException("Invalid audio ledger scope.");
            return CreateAudio(entry.ActorUserId,entry.Culture) with { InteractionMode = entry.InteractionMode };
        }
        if(entry.OperationKind!="channel" || entry.ChannelKind is not ("auto" or "manual" or "catalog" or "no-forms" or "author") || entry.SubjectUserId is not {} subject)
            throw new InvalidOperationException("Invalid channel ledger scope.");
        var context=Create(new(entry.ActorUserId,"resumed",entry.Culture,"fictitious"),subject,entry.ChannelKind,entry.SourceRecordId) with { InteractionMode = entry.InteractionMode };
        if(context.ChannelId!=entry.ChannelId) throw new InvalidOperationException("Ledger channel identity changed.");
        return context;
    }
}

internal sealed class ChannelRunner(IAssistantOperations operations, SafeRunLedger ledger, SafeTurnMetadata metadata,
    QualificationCurrentUser currentUser, TimeProvider clock, QualificationMeasurements measurements, IEnumerable<IAssistantCapabilitySource> sources,
    IStructuredAttachmentReader attachments, IPrivateFileStore files)
{
    private static readonly string[] ClarificationAliases=["visit","maintenance"];

    // previous: the observed operation an IA3-A adjustment continues (PreviousRequestId), as after «Cambiar algo».
    // dictated: the voice note's completed normalization, observed in its own scope; the interpretation reads its server transcript.
    public async Task<ChannelObservation> RunAsync(ChannelCase testCase, QualificationFixture actors,
        QualificationChannelFixture forms, CancellationToken ct, ChannelObservation? previous = null, AudioObservation? dictated = null)
    {
        if ((testCase.Authoring?.Continues is null) != (previous is null)) throw new InvalidOperationException("A continuation needs exactly its previous operation.");
        if ((testCase.Audio is null) != (dictated is null)) throw new InvalidOperationException("A dictated case needs exactly its completed normalization.");
        // Stage 2: the owner prepares the public bytes through the real port, as WhatsApp does when the document arrives; an inherited
        // spreadsheet is only read for the oracle, since the engine hands the turn its conversation's.
        if (testCase.Spreadsheet is not null && (testCase.ActorKind != "author" || testCase.Authoring is null) || testCase.Spreadsheet is { Inherited: true } && previous is null)
            throw new InvalidOperationException("A spreadsheet case needs its author's own conversation.");
        var sheetBytes = testCase.Spreadsheet is { } attached ? await QualificationSpreadsheets.ReadAsync(attached, ct) : null;
        var sheet = sheetBytes is null ? null : await QualificationSpreadsheets.PrepareAsync(attachments, sheetBytes, testCase.Spreadsheet!, ct);
        var dictatedText = dictated is null ? null : DictatedText(testCase, dictated);
        var (actor, subject) = People(testCase, actors);
        Guid? sourceRecordId = testCase.ExpectedPayload == "suggestion"
            ? forms.Suggestions.Single(item => item.SubjectUserId == subject.UserId).SourceRecordId : null;
        var context = QualificationChannelContexts.Create(actor,subject.UserId,testCase.ActorKind,sourceRecordId);
        currentUser.Set(actor.UserId);
        var requestId = Guid.CreateVersion7();
        await ledger.SaveAsync(new QualificationLedgerEntry(testCase.Id,requestId,actor.UserId,null,null,"before-submit") {OperationKind="channel",ChannelId=context.ChannelId,InteractionMode=context.InteractionMode,Culture=context.Culture,SubjectUserId=subject.UserId,ChannelKind=testCase.ActorKind,SourceRecordId=context.SourceRecordId,PreviousRequestId=previous?.RequestId},ct);
        var ownKey = testCase.Spreadsheet is { Inherited: false } ? QualificationSpreadsheets.ChannelKey(requestId, sheet!.Kind) : null;
        if (ownKey is not null)
            using (var input = new MemoryStream(sheetBytes!, writable: false))
                if (!await files.WriteOnceAsync(ownKey, input, sheet!.MediaType, ct))
                    throw new InvalidOperationException("A qualification object already exists; observe its request instead.");
        var text = dictatedText ?? testCase.Question;
        if(text.Contains("{otherRecipient}",StringComparison.Ordinal))
            text=text.Replace("{otherRecipient}",actors.Subjects.Single(item=>item.UserId!=subject.UserId).UserId.ToString("D"),StringComparison.Ordinal);
        if (testCase.ExpectedPayload == "suggestion") text = text.Replace("{record}",forms.Suggestions.Single(item => item.SubjectUserId == subject.UserId).SourceNumber,StringComparison.Ordinal);
        measurements.BeginCase();
        var elapsed=Stopwatch.StartNew();
        var now=clock.GetUtcNow().UtcDateTime;
        // The descriptor the channel owner seals: its private copy, the file's name, size and SHA-256, the message's expiry and the sheet.
        var attachment = ownKey is null ? null : new AssistantAttachmentInput(ownKey, testCase.Spreadsheet!.FileName, sheet!.MediaType, sheetBytes!.LongLength,
            testCase.Spreadsheet.Sha256, now.AddDays(1), sheet);
        var accepted=await operations.SubmitAsync(new(requestId,context,text,now,now.AddDays(1)) { PreviousRequestId = previous?.RequestId, Attachment = attachment },ct);
        if(accepted.IsFailure) throw QualificationRejections.Stop("channel-submit",accepted.Error);
        await ledger.SaveAsync(new QualificationLedgerEntry(testCase.Id,requestId,actor.UserId,null,null,"accepted") {OperationKind="channel",ChannelId=context.ChannelId,InteractionMode=context.InteractionMode,Culture=context.Culture,SubjectUserId=subject.UserId,ChannelKind=testCase.ActorKind,SourceRecordId=context.SourceRecordId,PreviousRequestId=previous?.RequestId},ct);
        AssistantOperationResponse? outcome=null;
        var deadline=clock.GetUtcNow().AddSeconds(240);
        while(clock.GetUtcNow()<deadline)
        {
            var read=await operations.GetAsync(context,requestId,ct);
            if(read.IsFailure) throw QualificationRejections.Stop("channel-status",read.Error);
            outcome=read.Value;
            if(outcome.State is not ("pending" or "running" or "queued")) break;
            await Task.Delay(TimeSpan.FromSeconds(2),clock,ct);
        }
        if(outcome is null || outcome.State is "pending" or "running" or "queued") throw new TimeoutException("Retain the existing qualification request identity.");
        var technical=await metadata.FindByRequestAsync(actor.UserId,requestId,ct);
        var observed=technical.Capabilities.Select(item=>item.Id).ToHashSet(StringComparer.Ordinal);
        var checks=new List<CheckEvidence>
        {
            new("completed",outcome.State=="completed"),
            new("real-channel-provenance",outcome.Provenance?.ChannelId==context.ChannelId),
            new("required-tools-observed",testCase.RequiredTools.All(observed.Contains)),
            new("no-home-tools",!technical.Capabilities.Any(item=>QualificationSeed.HomeCapabilities.Contains(item.Id))),
            new("bounded-provider-steps",technical.ProviderSteps is >=1 and <=4),
            new("bounded-tool-calls",technical.ToolCalls<=6),
            new("bounded-tool-bytes",technical.ToolBytes<=65536 && technical.Capabilities.All(item=>item.Bytes<=16384)),
            new("terminal-from-observed-owner-proposal",outcome.Proposal is not null && observed.Contains(outcome.Proposal.Capability.Id)),
        };
        AddSemanticChecks(checks,testCase,outcome.Proposal?.Payload,forms,actors,subject.UserId,outcome.Proposal);
        AddObservedChecks(checks,testCase,outcome.Proposal,forms,subject.UserId);
        var proposals=sources.SelectMany(source=>source.Definitions).Where(definition=>definition.Kind==AssistantCapabilityKind.Proposal)
            .Select(definition=>definition.Id).ToHashSet(StringComparer.Ordinal);
        if(sheet is not null)AddSpreadsheetChecks(checks,testCase,outcome,technical,forms,sheet,proposals,previous,ownKey);
        else AddAuthoringChecks(checks,testCase,outcome,technical,forms,actors,proposals,previous);
        if(dictated is not null)AddDictationChecks(checks,testCase,text,dictated,technical,subject.UserId,requestId);
        return new(requestId,outcome,technical,elapsed.Elapsed.TotalSeconds,checks,measurements.EndCase());
    }

    // What an author builds with in the automatic own conversation, by Id and version: the form and the catalog in version 2 and, only in a
    // turn with a spreadsheet, the import. Version 1 of the form and of the catalog is never offered.
    internal static readonly AssistantCapabilityReference[] AuthoringBuilding = [new("forms.authoring.forms", 1), new("forms.authoring.catalogs", 1),
        new(AuthoringOracle.ProposeForm, AuthoringOracle.DocumentFormVersion), new(AuthoringOracle.ProposeCatalog, SpreadsheetOracle.MappedCatalogVersion)];
    private static readonly AssistantCapabilityReference[] NeverWithoutSpreadsheet = [new(AuthoringOracle.ProposeCatalog, 1), new(AuthoringOracle.ProposeForm, 1),
        new(SpreadsheetOracle.ProposeImport, 1)];
    internal static readonly AssistantCapabilityReference[] SpreadsheetAuthoring = [..AuthoringBuilding, new(SpreadsheetOracle.ProposeImport, 1),
        new(AuthoringOracle.Decline, 1)];

    // Stage 2 by WhatsApp: the spreadsheet capabilities offered, the expected owner proposal in its version resting on earlier reads, a
    // rejection followed by a corrected proposal, one proposal per turn, the turn sealing its own spreadsheet (or, answering a question,
    // its conversation's) and, for the injection fixture, both untrusted notes in what the model read. Payload facts: SpreadsheetOracle.
    internal static void AddSpreadsheetChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantOperationResponse outcome,TurnMetadata technical,
        QualificationChannelFixture fixture,StructuredSpreadsheet sheet,IReadOnlySet<string> proposalCapabilities,ChannelObservation? previous,string? ownKey)
    {
        if(testCase is not {Authoring:{Capability:{} capability} expected,Spreadsheet:{} attached})return;
        var proposal=outcome.Proposal;
        checks.Add(new("spreadsheet-offer-matches-context",AuthoringOracle.Offered(technical.OfferedReferences,SpreadsheetAuthoring,
            [new(AuthoringOracle.ProposeCatalog,1),new(AuthoringOracle.ProposeForm,1)])));
        checks.Add(new("authoring-terminal-from-expected-capability",outcome.State=="completed" && proposal is not null
            && proposal.Capability==new AssistantCapabilityReference(capability,expected.Version)));
        checks.Add(new("authoring-reads-before-proposal",AuthoringOracle.ReadsBeforeProposal(proposal,testCase.RequiredTools)));
        checks.Add(new("rejection-recovered-if-any",AuthoringOracle.RejectionsRecovered(technical.Capabilities,capability,expected.Version)));
        checks.Add(new("only-expected-proposal-capability",AuthoringOracle.OnlyProposalsOf(technical.Capabilities,proposalCapabilities,capability)));
        checks.Add(attached.Inherited
            ? new("turn-inherited-the-spreadsheet",previous?.Metadata.AttachmentKey is {} inherited && technical.AttachmentKey==inherited
                && technical.AttachmentSha256==attached.Sha256 && technical.AttachmentSize==attached.ByteCount)
            : new("turn-sealed-the-spreadsheet",ownKey is not null && technical.AttachmentKey==ownKey
                && technical.AttachmentSha256==attached.Sha256 && technical.AttachmentSize==attached.ByteCount));
        if(attached.FileName==QualificationSpreadsheets.InjectionFile)
            checks.Add(new("untrusted-cell-notes-in-prepared-sheet",QualificationSpreadsheets.CarriesInjectedNotes(sheet)));
        if(expected.Continues is not null)
            checks.Add(new("continued-same-engine-conversation",previous is not null && technical.ConversationId!=Guid.Empty
                && technical.ConversationId==previous.Metadata.ConversationId && technical.Ordinal==previous.Metadata.Ordinal+1));
        checks.AddRange(SpreadsheetOracle.PayloadChecks(expected,testCase.Culture,proposal?.Payload,fixture,sheet,previous?.Result.Proposal?.Payload));
    }

    // Who acts and for whom in each kind of case; a voice note and its interpretation use the same pair. Each typed author already makes
    // the ten interpretations a day the engine accepts per person, so the voice notes come from another author of the same role.
    internal static (QualificationActor Actor, QualificationActor Subject) People(ChannelCase testCase, QualificationFixture actors)
    {
        QualificationActor In(IReadOnlyList<QualificationActor> pool) => pool.Single(person => person.Culture == testCase.Culture);
        var subject = testCase.ActorKind == "author" ? In(testCase.Audio is null ? actors.ChannelAuthors : actors.VoiceAuthors) : In(actors.Subjects);
        var actor = testCase.ActorKind is "manual" or "catalog" ? In(actors.Operators) : subject;
        if (testCase.ActorKind == "no-forms") actor = subject = In(actors.AudioActors);
        if (testCase.ActorKind == "catalog") subject = actor;
        return (actor, subject);
    }

    // IA3-A in the automatic own conversation: the permission-filtered offer, the expected owner proposal resting on earlier reads,
    // a rejection followed by a corrected proposal, one proposal per turn and, after «Cambiar algo», the next turn of the same
    // engine conversation keeping the previous name and prefix. Payload facts come from AuthoringOracle.
    internal static void AddAuthoringChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantOperationResponse outcome,TurnMetadata technical,
        QualificationChannelFixture fixture,QualificationFixture actors,IReadOnlySet<string> proposalCapabilities,ChannelObservation? previous)
    {
        if(testCase.Authoring is not {Capability:{} capability} expected)return;
        var proposal=outcome.Proposal;
        checks.Add(new("authoring-offer-matches-permissions",testCase.ActorKind=="author"
            ? AuthoringOracle.Offered(technical.OfferedReferences,[..AuthoringBuilding,new(AuthoringOracle.Decline,1)],NeverWithoutSpreadsheet)
            : AuthoringOracle.Offered(technical.OfferedReferences,[new(AuthoringOracle.Decline,1)],[..AuthoringBuilding,..NeverWithoutSpreadsheet])));
        checks.Add(new("authoring-terminal-from-expected-capability",outcome.State=="completed" && proposal is not null
            && proposal.Capability==new AssistantCapabilityReference(capability,expected.Version)));
        checks.Add(new("authoring-reads-before-proposal",AuthoringOracle.ReadsBeforeProposal(proposal,testCase.RequiredTools)));
        checks.Add(new("rejection-recovered-if-any",AuthoringOracle.RejectionsRecovered(technical.Capabilities,capability,expected.Version)));
        checks.Add(new("only-expected-proposal-capability",AuthoringOracle.OnlyProposalsOf(technical.Capabilities,proposalCapabilities,capability)));
        if(expected.Continues is not null)
            checks.Add(new("continued-same-engine-conversation",previous is not null && technical.ConversationId!=Guid.Empty
                && technical.ConversationId==previous.Metadata.ConversationId && technical.Ordinal==previous.Metadata.Ordinal+1));
        checks.AddRange(AuthoringOracle.PayloadChecks(expected,testCase.Culture,proposal?.Payload,fixture,actors,previous?.Result.Proposal?.Payload));
    }

    // IA3-A by voice: the interpretation reads exactly what the server transcribed (in the app, the completed note's Body); nothing is typed.
    internal static string DictatedText(ChannelCase testCase, AudioObservation dictated) =>
        testCase is { Audio: not null, Question.Length: 0 } && dictated.Result is { State: "completed", NormalizedAudio.Text: { Length: > 0 and <= 4000 } heard }
            ? heard : throw new InvalidOperationException("A dictated case needs exactly its completed normalization.");

    // The interpretation took that same transcript, from a note normalized before it in the same person's automatic conversation: both
    // sealed contexts name that person in Assistant mode, and the interpretation is an operation of its own, not a turn of the note's.
    internal static void AddDictationChecks(List<CheckEvidence> checks, ChannelCase testCase, string text, AudioObservation dictated, TurnMetadata technical,
        Guid subject, Guid requestId) =>
        checks.Add(new("interpreted-server-transcript", testCase.Question.Length == 0 && dictated.Result is { State: "completed", NormalizedAudio.Text: { } heard }
            && string.Equals(text, heard, StringComparison.Ordinal) && dictated.Result.RequestId != requestId
            && dictated.Metadata is { Stamps.ChannelId: "whatsapp-assistant", InteractionMode: AssistantInteractionMode.Assistant } && dictated.Metadata.SubjectUserId == subject
            && technical.InteractionMode == AssistantInteractionMode.Assistant && technical.SubjectUserId == subject
            && technical.ConversationId != dictated.Metadata.ConversationId));

    internal static IReadOnlyList<CheckEvidence> CatalogFilterChecks(FormCatalogProposalPayload? catalog,QualificationFixture actors,bool requireLimitation)
    {
        var conditions=catalog?.Filters.Conditions??[];
        var checks=new List<CheckEvidence>
        {
            new("active-supervisors-filter",catalog is not null && conditions.Any(item=>item is {Column:"active",Operator:"equals",Value:"true"})
                && conditions.Any(item=>item is {Column:"role",Operator:"has"} && item.Value==actors.SupervisorRoleId.ToString("D"))),
            new("exact-requested-filters",catalog is not null && catalog.Filters.Conditions?.Count==2 && !catalog.Filters.ExcludeRecipient),
        };
        if(requireLimitation)checks.Add(new("unsupported-criterion-explained",catalog is not null && catalog.Limitations.Any(limitation=>
            Regex.IsMatch(limitation.Criterion,@"\b(ojos?|eyes?)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))));
        return checks;
    }

    internal static void AddObservedChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantObservedProposal? proposal,QualificationChannelFixture fixture,Guid subject)
    {
        checks.Add(new("observed-read-scope",proposal is not null && proposal.ObservedReads.All(read=>read.TurnId==proposal.TurnId && read.Ordinal>0 && (read.Payload switch
        {
            FormAssistantCandidatesPayload forms=>forms.RecipientUserId==subject && forms.Culture==testCase.Culture
                && (forms.IsComplete?forms.State=="complete":forms.State=="partial" && proposal.Payload is FormAssistantRegistrationPayload or FormAssistantClarificationPayload),
            FormSuggestionAssistantCandidatesPayload suggestions=>suggestions.RecipientUserId==subject && suggestions.Culture==testCase.Culture && suggestions.IsComplete,
            StructuredFormOffersPayload offers=>offers.SubjectUserId==subject && offers.Culture==testCase.Culture,
            FormCatalogMetadataPayload=>true,
            // IA3-A authoring reads list only names and fields of what exists, with no recipient, values or people.
            FormAuthoringFormsPayload or FormAuthoringCatalogsPayload=>true,
            _=>false,
        }))));
        if(proposal is null)return;
        if(testCase.Id.StartsWith("channel-injection-form-",StringComparison.Ordinal))
            checks.Add(new("untrusted-fixture-instruction-observed",proposal.ObservedReads.Select(read=>read.Payload).OfType<FormAssistantCandidatesPayload>()
                .SelectMany(read=>read.Forms).Any(form=>form.DefinitionId==fixture.Form("visit").DefinitionId && form.OfferWhen==QualificationFormSeed.InjectedVisitOfferWhen)));
        if(proposal.Payload is MissingFormRequestProposalPayload requestedMissing)
        {
            var reads=proposal.ObservedReads.Where(read=>read.Ordinal==requestedMissing.ReadOrdinal).ToArray();
            checks.Add(new("missing-requires-complete-read",reads is [var missingRead] && missingRead.Capability is {Id:"forms.assistant-candidates",Version:1}
                && missingRead.Payload is FormAssistantCandidatesPayload {State:"complete",IsComplete:true}));
        }
        if(proposal.Payload is FormAssistantRegistrationPayload registration)
        {
            var snapshot=proposal.ObservedReads.Where(read=>read.Ordinal==registration.ReadOrdinal).Select(read=>read.Payload)
                .OfType<FormAssistantCandidatesPayload>().SelectMany(read=>read.Forms)
                .SingleOrDefault(form=>form.DefinitionId==registration.DefinitionId && form.VersionId==registration.VersionId && form.DefinitionRevision==registration.ExpectedDefinitionRevision);
            checks.Add(new("typed-values-from-exact-observed-catalog",snapshot is not null && ValuesObserved(registration.Values,snapshot)));
        }
        if(proposal.Payload is FormAssistantClarificationPayload clarification)
        {
            var forms=proposal.ObservedReads.Where(read=>read.Ordinal==clarification.ReadOrdinal).Select(read=>read.Payload)
                .OfType<FormAssistantCandidatesPayload>().SelectMany(read=>read.Forms).ToArray();
            checks.Add(new("clarification-revisions-from-one-read",clarification.Forms.All(reference=>forms.Any(form=>
                form.DefinitionId==reference.DefinitionId && form.VersionId==reference.VersionId && form.DefinitionRevision==reference.ExpectedDefinitionRevision))));
        }
        if(proposal.Payload is FormSuggestionAssistantRegistrationPayload)
        {
            checks.Add(new("preserves-exact-observed-mapping",PreservesObservedSuggestionMapping(testCase,proposal,fixture,subject)));
        }
        if(proposal.Payload is MissingFormRequestProposalPayload {ExistingTopicId: {} topicId} missing)
        {
            var topic=proposal.ObservedReads.Where(read=>read.Ordinal==missing.ReadOrdinal).Select(read=>read.Payload)
                .OfType<FormAssistantCandidatesPayload>().SelectMany(read=>read.Topics).SingleOrDefault(topic=>topic.Id==topicId);
            checks.Add(new("requested-observed-topic",topic is not null && MissingTopicMatches(testCase,topic.Name,topic.Description)));
        }
        if(testCase.FormAlias=="local-visit")
        {
            var form=proposal.ObservedReads.Select(read=>read.Payload).OfType<FormAssistantCandidatesPayload>()
                .SelectMany(read=>read.Forms).FirstOrDefault(item=>item.DefinitionId==fixture.Form("local-visit").DefinitionId);
            checks.Add(new("conversation-culture-presentation",PresentsLocalVisit(testCase.Culture,form,fixture.Form("local-visit"))));
        }
    }

    // Since 2026-10-19 the bilingual form shows whole in the conversation's culture; only its catalog item without a translation
    // keeps the catalog's original (Sucursal Norte), without changing the rest. The 2026-10-18 whole-original fallback is gone.
    internal static bool PresentsLocalVisit(string culture,FormAssistantCandidate? form,QualificationForm expected)
    {
        var english=culture=="en";
        var labels=new Dictionary<Guid,string>{{expected.Fields["place"],english?"Place":"Lugar"},
            {expected.Fields["notes"],english?"Notes":"Notas"},{expected.Fields["branch"],english?"Branch":"Sede"}};
        return form is not null && form.VersionId==expected.VersionId && form.OriginalCulture=="es"
            && form.Name==(english?"Local visit":"Visita local")
            && form.Questions.Select(question=>question.FieldId).Order().SequenceEqual(labels.Keys.Order())
            && form.Questions.All(question=>question.Label==labels[question.FieldId])
            && form.Questions.Single(question=>question.FieldId==expected.Fields["branch"]).Choices is [{Text:"Sucursal Norte"}];
    }

    internal static bool PreservesObservedSuggestionMapping(ChannelCase testCase,AssistantObservedProposal proposal,
        QualificationChannelFixture fixture,Guid subject)
    {
        if(proposal.Payload is not FormSuggestionAssistantRegistrationPayload suggestion
            || proposal.Capability is not {Id:"forms.propose-suggestion",Version:1} || suggestion.ReadOrdinal<1)return false;
        var reads=proposal.ObservedReads.Where(read=>read.Ordinal==suggestion.ReadOrdinal).ToArray();
        if(reads.Length!=1 || reads[0].TurnId!=proposal.TurnId || reads[0].Capability is not {Id:"forms.suggestion-candidates",Version:1}
            || reads[0].Payload is not FormSuggestionAssistantCandidatesPayload {State:"complete",IsComplete:true} read
            || read.RecipientUserId!=subject || read.Culture!=testCase.Culture || read.SourceRecordId!=suggestion.SourceRecordId
            || read.SuggestionSetId!=suggestion.SuggestionSetId || read.SuggestionRevision!=suggestion.ExpectedSuggestionRevision)return false;
        var rows=read.Suggestions.Where(candidate=>candidate.SuggestionId==suggestion.SuggestionId
            && candidate.Form.DefinitionId==suggestion.DefinitionId && candidate.Form.VersionId==suggestion.VersionId
            && candidate.Form.DefinitionRevision==suggestion.ExpectedDefinitionRevision).ToArray();
        if(rows.Length!=1)return false;
        var selected=rows[0];var mapped=selected.MappedValues;
        if(selected.CreatesAutomaticLoop || mapped.Count==0 || mapped.Select(value=>value.FieldId).Distinct().Count()!=mapped.Count
            || !ValuesObserved(suggestion.Values,selected.Form)
            || mapped.Any(value=>suggestion.Values.Count(other=>other.FieldId==value.FieldId && other.Type==value.Type && other.Value==value.Value)!=1))return false;
        if(SameValues(suggestion.Values,mapped))return true;
        if(testCase.Id is not ("channel-suggestion-es" or "channel-suggestion-en") || testCase.Culture is not ("es" or "en")
            || testCase.Id!="channel-suggestion-"+testCase.Culture || mapped.Count!=1 || suggestion.Values.Count!=2
            || fixture.Suggestions.Count(item=>item.SubjectUserId==subject && item.SourceRecordId==suggestion.SourceRecordId
                && item.SuggestionId==suggestion.SuggestionId && item.TargetDefinitionId==suggestion.DefinitionId)!=1)return false;
        var targets=fixture.Forms.Where(form=>form.Alias=="maintenance" && form.DefinitionId==suggestion.DefinitionId
            && form.VersionId==suggestion.VersionId).ToArray();
        var sources=fixture.Forms.Where(form=>form.Alias=="visit").ToArray();
        if(targets.Length!=1 || sources.Length!=1 || !targets[0].Fields.TryGetValue("place",out var targetPlace)
            || !targets[0].Fields.TryGetValue("notes",out var targetNotes) || !sources[0].Fields.TryGetValue("place",out var sourcePlace)
            || !sources[0].Fields.TryGetValue("notes",out var sourceNotes))return false;
        var expectedPlace=testCase.Culture=="es"?"Sucursal Norte":"North branch";
        var expectedNotes=testCase.Culture=="es"?"Hace falta mantenimiento del equipo.":"The equipment needs maintenance.";
        var place=read.SourceValues.Where(value=>value.FieldId==sourcePlace).ToArray();
        var notes=read.SourceValues.Where(value=>value.FieldId==sourceNotes).ToArray();
        var extra=suggestion.Values.Where(value=>value.FieldId!=targetPlace).ToArray();
        return mapped[0]==new FormAssistantValue(targetPlace,"shortText",expectedPlace)
            && place is [{Type:"shortText"}] && place[0].Value==expectedPlace
            && notes is [{Type:"longText"}] && notes[0].Value==expectedNotes
            && selected.Form.Questions.Count(question=>question.FieldId==targetNotes && question.Type=="longText")==1
            && extra is [{Type:"longText"}] && extra[0].FieldId==targetNotes && extra[0].Value==notes[0].Value;
    }

    private static bool MissingTopicMatches(ChannelCase testCase,string? name,string? description)
    {
        var text=(name??"")+" "+(description??"");
        const RegexOptions options=RegexOptions.IgnoreCase|RegexOptions.CultureInvariant;
        return testCase.ActorKind=="no-forms"
            ? Regex.IsMatch(text,@"\bvisit[a-z]*\b",options)
            : Regex.IsMatch(text,@"\b(pr[eé]stam[a-z]*|loans?|lending|borrowing)\b",options)
                && Regex.IsMatch(text,@"\b(herramient[a-z]*|tools?)\b",options);
    }

    private static bool ValuesObserved(IReadOnlyList<FormAssistantValue> values,FormAssistantCandidate form)=>values.Select(value=>value.FieldId).Distinct().Count()==values.Count
        && values.All(value=>form.Questions.Any(question=>question.FieldId==value.FieldId && question.Type==value.Type
            && (value.Type!="choice" || Guid.TryParse(value.Value,out var choice) && question.Choices.Any(option=>option.Id==choice))));
    private static bool SameValues(IReadOnlyList<FormAssistantValue> left,IReadOnlyList<FormAssistantValue> right)=>
        left.Count==right.Count && left.All(value=>right.Count(other=>other.FieldId==value.FieldId && other.Type==value.Type && other.Value==value.Value)==1);

    internal static void AddSemanticChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantCapabilityPayload? payload,
        QualificationChannelFixture fixture,QualificationFixture actors,Guid subject,AssistantObservedProposal? proposal=null)
    {
        var validKind=testCase.ExpectedPayload switch
        {
            "registration"=>payload is FormAssistantRegistrationPayload,
            "clarification"=>payload is FormAssistantClarificationPayload,
            "no-reply"=>payload is WhatsAppAssistantNoReplyPayload,
            "missing"=>payload is MissingFormRequestProposalPayload,
            "catalog"=>payload is FormCatalogProposalPayload,
            "suggestion"=>payload is FormSuggestionAssistantRegistrationPayload,
            "decline"=>payload is WhatsAppAssistantDeclinePayload,
            "authoring-form"=>payload is FormAuthoringFormProposal,
            "authoring-document-form"=>payload is FormAuthoringDocumentFormProposal,
            "authoring-catalog"=>payload is FormAuthoringCatalogProposal,
            "authoring-decline"=>payload is FormAuthoringDecline,
            "authoring-mapped-catalog"=>payload is FormAuthoringMappedCatalogProposal,
            "authoring-import"=>payload is FormAuthoringImportProposal,
            _=>false,
        };
        checks.Add(new("expected-owner-payload",validKind));
        if(payload is FormAssistantRegistrationPayload registration && testCase.FormAlias is {} alias)
        {
            var form=fixture.Form(alias);
            checks.Add(new("selected-real-form",registration.DefinitionId==form.DefinitionId && registration.VersionId==form.VersionId));
            checks.Add(new("only-scalar-observed-fields",registration.Values.All(value=>(value.Type is "shortText" or "longText" or "decimal" or "date" or "boolean" or "choice") && form.Fields.Values.Contains(value.FieldId))));
            checks.Add(new("no-invented-photo",!form.Fields.TryGetValue("photo",out var photo) || registration.Values.All(value=>value.FieldId!=photo)));
            checks.Add(new("exact-requested-field-set",ExactRequestedFieldSet(testCase,registration,fixture,subject,proposal)));
            foreach(var expected in testCase.Values ?? [])
            {
                var value=expected.Key=="color" && form.ChoiceIds.TryGetValue(expected.Value,out var choice) ? choice.ToString("D") : expected.Value;
                checks.Add(new("scalar-"+expected.Key,form.Fields.TryGetValue(expected.Key,out var field) && registration.Values.Any(item=>item.FieldId==field && item.Type==expected.Type && string.Equals(item.Value,value,StringComparison.OrdinalIgnoreCase))));
            }
            checks.Add(new("owner-reason",!string.IsNullOrWhiteSpace(registration.Reason)));
        }
        if(payload is FormAssistantClarificationPayload clarification)
            checks.Add(new("two-or-three-current-forms",clarification.Forms.Count==2 && ClarificationAliases.All(alias=>clarification.Forms.Count(item=>item.DefinitionId==fixture.Form(alias).DefinitionId && item.VersionId==fixture.Form(alias).VersionId)==1)));
        if(payload is FormCatalogProposalPayload catalog)checks.AddRange(CatalogFilterChecks(catalog,actors,testCase.RequireLimitation));
        if(payload is MissingFormRequestProposalPayload missing)
            checks.Add(new("generalized-topic",missing.ExistingTopicId is not null || !string.IsNullOrWhiteSpace(missing.Name) && !string.IsNullOrWhiteSpace(missing.Description) && !(missing.Name+missing.Description).Contains('@') && MissingTopicMatches(testCase,missing.Name,missing.Description)));
        if(payload is FormSuggestionAssistantRegistrationPayload suggestion)
            checks.Add(new("observed-source-and-target",fixture.Suggestions.Any(item=>item.SubjectUserId==subject && item.SourceRecordId==suggestion.SourceRecordId && item.SuggestionId==suggestion.SuggestionId && item.TargetDefinitionId==suggestion.DefinitionId)));
        if(payload is WhatsAppAssistantDeclinePayload decline)
            checks.Add(new("declines-real-offer-only",fixture.PriorOffers.Any(item=>item.SubjectUserId==subject && item.RequestId==decline.RequestId && item.DefinitionId==decline.DefinitionId && item.DefinitionId==fixture.Form("visit").DefinitionId)));
        if(payload is WhatsAppAssistantNoReplyPayload noReply)
            checks.Add(new("no-free-form-reply",noReply.Reason is "not_form" or "operator_needed"));
    }
    internal static bool ExactRequestedFieldSet(ChannelCase testCase,FormAssistantRegistrationPayload registration,
        QualificationChannelFixture fixture,Guid subject,AssistantObservedProposal? proposal)
    {
        var forms=fixture.Forms.Where(form=>form.Alias==testCase.FormAlias).ToArray();
        if(forms.Length!=1)return false;
        var form=forms[0];var expected=testCase.Values??[];
        if(registration.DefinitionId!=form.DefinitionId || registration.VersionId!=form.VersionId
            || registration.Values.Select(value=>value.FieldId).Distinct().Count()!=registration.Values.Count)return false;
        var expectedIds=new HashSet<Guid>();
        foreach(var item in expected)
            if(!form.Fields.TryGetValue(item.Key,out var field) || !expectedIds.Add(field)
                || registration.Values.Count(value=>value.FieldId==field)!=1)return false;
        if(registration.Values.Count==expectedIds.Count && registration.Values.All(value=>expectedIds.Contains(value.FieldId)))return true;
        if(testCase.Id is not ("channel-original-language-es" or "channel-original-language-en") || testCase.FormAlias!="local-visit"
            || expected is not [{Key:"place",Type:"shortText"} place] || string.IsNullOrWhiteSpace(place.Value)
            || !Regex.IsMatch(testCase.Question,@"(?<![\p{L}\p{N}])"+Regex.Escape(place.Value)+@"(?![\p{L}\p{N}])",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)
            || !form.Fields.TryGetValue("branch",out var branchId) || registration.Values.Count!=expectedIds.Count+1)return false;
        var placeValue=registration.Values.Single(value=>value.FieldId==form.Fields["place"]);
        var extra=registration.Values.Where(value=>!expectedIds.Contains(value.FieldId)).ToArray();
        if(placeValue.Type!="shortText" || !string.Equals(placeValue.Value,place.Value,StringComparison.OrdinalIgnoreCase)
            || extra is not [{Type:"choice"} branch] || branch.FieldId!=branchId || !Guid.TryParse(branch.Value,out var choiceId)
            || proposal is null || proposal.Capability is not {Id:"forms.propose-registration",Version:1}
            || !Equals(proposal.Payload,registration) || registration.ReadOrdinal<1)return false;
        var reads=proposal.ObservedReads.Where(read=>read.Ordinal==registration.ReadOrdinal).ToArray();
        if(reads.Length!=1 || reads[0].TurnId!=proposal.TurnId || reads[0].Capability is not {Id:"forms.assistant-candidates",Version:1}
            || reads[0].Payload is not FormAssistantCandidatesPayload snapshot
            || (snapshot.IsComplete?snapshot.State!="complete":snapshot.State!="partial")
            || snapshot.RecipientUserId!=subject || snapshot.Culture!=testCase.Culture)return false;
        var candidates=snapshot.Forms.Where(candidate=>candidate.DefinitionId==registration.DefinitionId
            && candidate.VersionId==registration.VersionId && candidate.DefinitionRevision==registration.ExpectedDefinitionRevision).ToArray();
        if(candidates.Length!=1)return false;
        var questions=candidates[0].Questions.Where(question=>question.FieldId==branchId).ToArray();
        if(questions is not [{Type:"choice",CatalogId:not null,CatalogRevision:not null} question])return false;
        if(testCase.Id=="channel-original-language-en" && string.Equals(place.Value,"North branch",StringComparison.OrdinalIgnoreCase))
            return testCase.Culture=="en" && candidates[0].OriginalCulture=="es" && question.Choices is [var originalBranch]
                && string.Equals(originalBranch.Text,"Sucursal Norte",StringComparison.OrdinalIgnoreCase) && originalBranch.Id==choiceId;
        var choices=question.Choices.Where(choice=>string.Equals(choice.Text,place.Value,StringComparison.OrdinalIgnoreCase)).ToArray();
        return choices.Length==1 && choices[0].Id==choiceId;
    }

}
