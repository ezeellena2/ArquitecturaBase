using System.Diagnostics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
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
        AddSemanticChecks(checks,testCase,outcome.Proposal?.Payload,forms,actors,subject.UserId);
        if(testCase.FormAlias=="local-visit" && outcome.Proposal is {} proposal)
        {
            var form=proposal.ObservedReads.Select(read=>read.Payload).OfType<FormAssistantCandidatesPayload>()
                .SelectMany(read=>read.Forms).SingleOrDefault(item=>item.DefinitionId==forms.Form("local-visit").DefinitionId);
            var place= forms.Form("local-visit").Fields["place"];
            checks.Add(new("whole-original-presentation",form?.Name=="Visita local" && form.Questions.SingleOrDefault(question=>question.FieldId==place)?.Label=="Lugar"));
        }
        return new(requestId,outcome,technical,elapsed.Elapsed.TotalSeconds,checks,measurements.EndCase());
    }

    private static void AddSemanticChecks(List<CheckEvidence> checks,ChannelCase testCase,AssistantCapabilityPayload? payload,
        QualificationChannelFixture fixture,QualificationFixture actors,Guid subject)
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
            foreach(var expected in testCase.Values ?? [])
            {
                var value=expected.Key=="color" && form.ChoiceIds.TryGetValue(expected.Value,out var choice) ? choice.ToString("D") : expected.Value;
                checks.Add(new("scalar-"+expected.Key,form.Fields.TryGetValue(expected.Key,out var field) && registration.Values.Any(item=>item.FieldId==field && item.Type==expected.Type && string.Equals(item.Value,value,StringComparison.OrdinalIgnoreCase))));
            }
            checks.Add(new("owner-reason",!string.IsNullOrWhiteSpace(registration.Reason)));
        }
        if(payload is FormAssistantClarificationPayload clarification)
            checks.Add(new("two-or-three-current-forms",clarification.Forms.Count is >=2 and <=3 && clarification.Forms.All(item=>fixture.Forms.Any(form=>form.DefinitionId==item.DefinitionId && form.VersionId==item.VersionId))));
        if(payload is FormCatalogProposalPayload catalog)
        {
            checks.Add(new("active-supervisors-filter",catalog.Filters.Conditions?.Any(item=>item is {Column:"active",Operator:"equals",Value:"true"})==true && catalog.Filters.Conditions.Any(item=>item is {Column:"role",Operator:"has"} && item.Value==actors.SupervisorRoleId.ToString("D"))));
            checks.Add(new("only-closed-filter-columns",catalog.Filters.Conditions?.All(item=>item?.Column is "name" or "role" or "active")==true));
            if(testCase.RequireLimitation)checks.Add(new("unsupported-criterion-explained",catalog.Limitations.Count>0));
        }
        if(payload is MissingFormRequestProposalPayload missing)
            checks.Add(new("generalized-topic",missing.ExistingTopicId is not null || !string.IsNullOrWhiteSpace(missing.Name) && !string.IsNullOrWhiteSpace(missing.Description) && !(missing.Name+missing.Description).Contains('@')));
        if(payload is FormSuggestionAssistantRegistrationPayload suggestion)
            checks.Add(new("observed-source-and-target",fixture.Suggestions.Any(item=>item.SubjectUserId==subject && item.SourceRecordId==suggestion.SourceRecordId && item.SuggestionId==suggestion.SuggestionId && item.TargetDefinitionId==suggestion.DefinitionId)));
        if(payload is WhatsAppAssistantDeclinePayload decline)
            checks.Add(new("declines-real-offer-only",fixture.PriorOffers.Any(item=>item.SubjectUserId==subject && item.RequestId==decline.RequestId && item.DefinitionId==decline.DefinitionId)));
        if(payload is WhatsAppAssistantNoReplyPayload noReply)
            checks.Add(new("no-free-form-reply",noReply.Reason is "not_form" or "operator_needed"));
    }
}
