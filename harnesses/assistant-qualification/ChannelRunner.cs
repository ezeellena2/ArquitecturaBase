using System.Diagnostics;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace AssistantQualification;

internal sealed record ScalarExpectation(string Key, string Type, string Value);
internal sealed record ChannelCase(string Id, string Culture, string ActorKind, string Question, string ExpectedPayload,
    string[] RequiredTools, string? FormAlias = null, ScalarExpectation[]? Values = null, bool RequireLimitation = false);
internal sealed record ChannelObservation(Guid RequestId, AssistantOperationResponse Result, TurnMetadata Metadata,
    double ElapsedSeconds, IReadOnlyList<CheckEvidence> Checks, MeasurementSnapshot Measurements);

internal static class QualificationChannelContexts
{
    public static readonly string[] Capabilities = ["whatsapp.offered-forms", "whatsapp.no-reply", "whatsapp.decline-form",
        "forms.assistant-candidates", "forms.propose-registration", "forms.clarify-registration",
        "forms.suggestion-candidates", "forms.propose-suggestion", "forms.catalog-filters.metadata", "forms.catalog-filters.propose",
        "forms.missing-request.propose"];
    public static AssistantChannelContext Create(QualificationActor actor, Guid subject, string kind)
    {
        if(kind is not ("auto" or "manual" or "catalog" or "no-forms")) throw new ArgumentException("Unknown qualification channel kind.",nameof(kind));
        return new(kind == "manual" ? "whatsapp-manual" : "whatsapp-assistant", actor.UserId, subject, actor.Culture, "America/Argentina/Buenos_Aires",
            new(kind == "manual" ? ["whatsapp.read", "whatsapp.send"] : [], []),
            Capabilities.Select(id => new AssistantCapabilityReference(id,1)).ToArray(), [new("assistant_proposal",1)]);
    }

    public static AssistantChannelContext CreateAudio(Guid actorUserId,string culture) => new("whatsapp-manual",actorUserId,null,culture,
        "America/Argentina/Buenos_Aires",new(["whatsapp.read"],[]),[],[]);

    public static AssistantChannelContext Resume(QualificationLedgerEntry entry)
    {
        if(entry.Culture is not ("es" or "en")) throw new InvalidOperationException("Invalid ledger culture.");
        if(entry.OperationKind=="audio")
        {
            if(entry.ChannelId!="whatsapp-manual" || entry.SubjectUserId is not null) throw new InvalidOperationException("Invalid audio ledger scope.");
            return CreateAudio(entry.ActorUserId,entry.Culture);
        }
        if(entry.OperationKind!="channel" || entry.ChannelKind is not ("auto" or "manual" or "catalog" or "no-forms") || entry.SubjectUserId is not {} subject)
            throw new InvalidOperationException("Invalid channel ledger scope.");
        var context=Create(new(entry.ActorUserId,"resumed",entry.Culture,"fictitious"),subject,entry.ChannelKind);
        if(context.ChannelId!=entry.ChannelId) throw new InvalidOperationException("Ledger channel identity changed.");
        return context;
    }
}

internal sealed class ChannelRunner(IAssistantOperations operations, SafeRunLedger ledger, SafeTurnMetadata metadata,
    QualificationCurrentUser currentUser, TimeProvider clock, QualificationMeasurements measurements)
{
    private static readonly string[] ClarificationAliases=["visit","maintenance"];

    public async Task<ChannelObservation> RunAsync(ChannelCase testCase, QualificationFixture actors,
        QualificationChannelFixture forms, CancellationToken ct)
    {
        var subject = actors.Subjects.Single(actor => actor.Culture == testCase.Culture);
        var actor = testCase.ActorKind is "manual" or "catalog" ? actors.Operators.Single(actor => actor.Culture == testCase.Culture) : subject;
        if (testCase.ActorKind == "no-forms") actor = subject = actors.AudioActors.Single(actor => actor.Culture == testCase.Culture);
        if (testCase.ActorKind == "catalog") subject = actor;
        var context = QualificationChannelContexts.Create(actor,subject.UserId,testCase.ActorKind);
        currentUser.Set(actor.UserId);
        var requestId = Guid.CreateVersion7();
        await ledger.SaveAsync(new QualificationLedgerEntry(testCase.Id,requestId,actor.UserId,null,null,"before-submit") {OperationKind="channel",ChannelId=context.ChannelId,Culture=context.Culture,SubjectUserId=subject.UserId,ChannelKind=testCase.ActorKind},ct);
        var text = testCase.Question;
        if (testCase.ExpectedPayload == "suggestion") text = text.Replace("{record}",forms.Suggestions.Single(item => item.SubjectUserId == subject.UserId).SourceNumber,StringComparison.Ordinal);
        measurements.BeginCase();
        var elapsed=Stopwatch.StartNew();
        var now=clock.GetUtcNow().UtcDateTime;
        var accepted=await operations.SubmitAsync(new(requestId,context,text,now,now.AddDays(1)),ct);
        if(accepted.IsFailure) throw new InvalidOperationException("Qualification channel submission was rejected.");
        await ledger.SaveAsync(new QualificationLedgerEntry(testCase.Id,requestId,actor.UserId,null,null,"accepted") {OperationKind="channel",ChannelId=context.ChannelId,Culture=context.Culture,SubjectUserId=subject.UserId,ChannelKind=testCase.ActorKind},ct);
        AssistantOperationResponse? outcome=null;
        var deadline=clock.GetUtcNow().AddSeconds(240);
        while(clock.GetUtcNow()<deadline)
        {
            var read=await operations.GetAsync(context,requestId,ct);
            if(read.IsFailure) throw new InvalidOperationException("Qualification channel status was rejected.");
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
        return new(requestId,outcome,technical,elapsed.Elapsed.TotalSeconds,checks,measurements.EndCase());
    }

    internal static void AddObservedChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantObservedProposal? proposal,QualificationChannelFixture fixture,Guid subject)
    {
        checks.Add(new("observed-read-scope",proposal is not null && proposal.ObservedReads.All(read=>read.TurnId==proposal.TurnId && read.Ordinal>0 && (read.Payload switch
        {
            FormAssistantCandidatesPayload forms=>forms.RecipientUserId==subject && forms.Culture==testCase.Culture && forms.IsComplete,
            FormSuggestionAssistantCandidatesPayload suggestions=>suggestions.RecipientUserId==subject && suggestions.Culture==testCase.Culture && suggestions.IsComplete,
            StructuredFormOffersPayload offers=>offers.SubjectUserId==subject && offers.Culture==testCase.Culture,
            FormCatalogMetadataPayload=>true,
            _=>false,
        }))));
        if(proposal is null)return;
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
            var expected=fixture.Form("local-visit");
            var labels=new Dictionary<Guid,string>{{expected.Fields["place"],"Lugar"},{expected.Fields["notes"],"Notas"},{expected.Fields["branch"],"Sede"}};
            checks.Add(new("whole-original-presentation",form is not null && form.VersionId==expected.VersionId && form.OriginalCulture=="es"
                && form.Name=="Visita local" && form.Questions.Count==labels.Count && form.Questions.All(question=>
                    labels.TryGetValue(question.FieldId,out var label) && question.Label==label)
                && form.Questions.Single(question=>question.FieldId==expected.Fields["branch"]).Choices is [{Text:"Sucursal Norte"}]));
        }
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
        if(payload is FormCatalogProposalPayload catalog)
        {
            checks.Add(new("active-supervisors-filter",catalog.Filters.Conditions?.Any(item=>item is {Column:"active",Operator:"equals",Value:"true"})==true && catalog.Filters.Conditions.Any(item=>item is {Column:"role",Operator:"has"} && item.Value==actors.SupervisorRoleId.ToString("D"))));
            checks.Add(new("exact-requested-filters",catalog.Filters.Conditions?.Count==2 && !catalog.Filters.ExcludeRecipient));
            if(testCase.RequireLimitation)checks.Add(new("unsupported-criterion-explained",catalog.Limitations.Any(limitation=>Regex.IsMatch(limitation.Criterion,@"\b(ojos?|eyes?)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant))));
        }
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
            || reads[0].Payload is not FormAssistantCandidatesPayload {State:"complete",IsComplete:true} snapshot
            || snapshot.RecipientUserId!=subject || snapshot.Culture!=testCase.Culture)return false;
        var candidates=snapshot.Forms.Where(candidate=>candidate.DefinitionId==registration.DefinitionId
            && candidate.VersionId==registration.VersionId && candidate.DefinitionRevision==registration.ExpectedDefinitionRevision).ToArray();
        if(candidates.Length!=1)return false;
        var questions=candidates[0].Questions.Where(question=>question.FieldId==branchId).ToArray();
        if(questions is not [{Type:"choice",CatalogId:not null,CatalogRevision:not null} question])return false;
        var choices=question.Choices.Where(choice=>string.Equals(choice.Text,place.Value,StringComparison.OrdinalIgnoreCase)).ToArray();
        return choices.Length==1 && choices[0].Id==choiceId;
    }

}
