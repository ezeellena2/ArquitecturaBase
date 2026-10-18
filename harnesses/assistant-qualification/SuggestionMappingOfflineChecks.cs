using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

internal static class SuggestionMappingOfflineChecks
{
    public static readonly Action[] All=[Suggestion_mapping_accepts_only_the_exact_observed_notes_fact,
        Suggestion_mapping_accepts_the_English_source_fact,Suggestion_mapping_keeps_the_exact_mapped_only_case,
        Suggestion_mapping_rejects_missing_or_changed_mapped_place,Suggestion_mapping_rejects_unknown_or_multiple_extras,
        Suggestion_mapping_rejects_invented_notes,Suggestion_mapping_rejects_wrong_field_types,
        Suggestion_mapping_rejects_duplicate_proposed_fields,Suggestion_mapping_requires_subject_culture_and_complete_scope,
        Suggestion_mapping_requires_the_exact_turn_ordinal_and_capabilities,Suggestion_mapping_requires_source_set_and_target_revisions,
        Suggestion_mapping_requires_the_same_source_alias_type_and_public_fact,Suggestion_mapping_rejects_automatic_loops,
        Suggestion_mapping_does_not_relax_other_cases_or_forms,Suggestion_mapping_rejects_duplicate_observed_rows,
        Suggestion_mapping_reevaluation_changes_only_its_check,Suggestion_mapping_reevaluation_preserves_other_failures,
        Suggestion_mapping_reevaluation_rejects_other_cases_or_ambiguous_checks];

    public static void Suggestion_mapping_accepts_only_the_exact_observed_notes_fact()=>Require(Accepted(Create()));
    public static void Suggestion_mapping_accepts_the_English_source_fact()=>Require(Accepted(Create("en")));
    public static void Suggestion_mapping_keeps_the_exact_mapped_only_case(){var s=Create();Require(Accepted(WithValues(s,[Payload(s).Values[0]])));}
    public static void Suggestion_mapping_rejects_missing_or_changed_mapped_place()
    {
        var s=Create();var values=Payload(s).Values;
        Require(!Accepted(WithValues(s,[values[1]])));
        Require(!Accepted(WithValues(s,[values[0] with {Value="Another place"},values[1]])));
    }
    public static void Suggestion_mapping_rejects_unknown_or_multiple_extras()
    {
        var s=Create();var values=Payload(s).Values;
        Require(!Accepted(WithValues(s,[values[0],new(Guid.NewGuid(),"longText",values[1].Value)])));
        Require(!Accepted(WithValues(s,[..values,new(Guid.NewGuid(),"shortText","Additional fact")])));
    }
    public static void Suggestion_mapping_rejects_invented_notes()
    {var s=Create();Require(!Accepted(WithValues(s,[Payload(s).Values[0],Payload(s).Values[1] with {Value="Unobserved notes"}])));}
    public static void Suggestion_mapping_rejects_wrong_field_types()
    {
        var s=Create();Require(!Accepted(WithValues(s,[Payload(s).Values[0],Payload(s).Values[1] with {Type="shortText"}])));
        Require(!Accepted(WithValues(s,[Payload(s).Values[0] with {Type="longText"},Payload(s).Values[1]])));
    }
    public static void Suggestion_mapping_rejects_duplicate_proposed_fields()
    {var s=Create();Require(!Accepted(WithValues(s,[..Payload(s).Values,Payload(s).Values[1]])));}
    public static void Suggestion_mapping_requires_subject_culture_and_complete_scope()
    {
        var s=Create();s=WithValues(s,[Payload(s).Values[0]]);
        Require(!Accepted(WithRead(s,Read(s) with {RecipientUserId=Guid.NewGuid()})));
        Require(!Accepted(WithRead(s,Read(s) with {Culture="en"})));
        Require(!Accepted(WithRead(s,Read(s) with {IsComplete=false})));
        Require(!Accepted(WithRead(s,Read(s) with {State="partial"})));
    }
    public static void Suggestion_mapping_requires_the_exact_turn_ordinal_and_capabilities()
    {
        var s=Create();s=WithValues(s,[Payload(s).Values[0]]);var read=s.Proposal.ObservedReads[0];
        Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[read with {TurnId=Guid.NewGuid()}]}}));
        Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[read with {Ordinal=2}]}}));
        Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[read with {Capability=new("forms.suggestion-candidates",2)}]}}));
        Require(!Accepted(s with {Proposal=s.Proposal with {Capability=new("forms.propose-suggestion",2)}}));
    }
    public static void Suggestion_mapping_requires_source_set_and_target_revisions()
    {
        var s=Create();var read=Read(s);
        Require(!Accepted(WithRead(s,read with {SourceRecordId=Guid.NewGuid()})));
        Require(!Accepted(WithRead(s,read with {SuggestionSetId=Guid.NewGuid()})));
        Require(!Accepted(WithRead(s,read with {SuggestionRevision=4})));
        Require(!Accepted(WithCandidate(s,Candidate(s) with {Form=Candidate(s).Form with {DefinitionRevision=4}})));
        Require(!Accepted(WithCandidate(s,Candidate(s) with {Form=Candidate(s).Form with {VersionId=Guid.NewGuid()}})));
    }
    public static void Suggestion_mapping_requires_the_same_source_alias_type_and_public_fact()
    {
        var s=Create();var read=Read(s);var sourceNotes=read.SourceValues[1];
        Require(!Accepted(WithRead(s,read with {SourceValues=[read.SourceValues[0],sourceNotes with {FieldId=Guid.NewGuid()}]})));
        Require(!Accepted(WithRead(s,read with {SourceValues=[read.SourceValues[0],sourceNotes with {Type="shortText"}]})));
        Require(!Accepted(WithRead(s,read with {SourceValues=[read.SourceValues[0]]})));
        var invented="Unobserved fixture fact";
        Require(!Accepted(WithRead(WithValues(s,[Payload(s).Values[0],Payload(s).Values[1] with {Value=invented}]),
            read with {SourceValues=[read.SourceValues[0],sourceNotes with {Value=invented}]})));
    }
    public static void Suggestion_mapping_rejects_automatic_loops()
    {var s=Create();Require(!Accepted(WithCandidate(s,Candidate(s) with {CreatesAutomaticLoop=true})));}
    public static void Suggestion_mapping_does_not_relax_other_cases_or_forms()
    {
        var s=Create();Require(!Accepted(s with {Case=s.Case with {Id="channel-visit-es"}}));
        Require(!Accepted(s with {Fixture=s.Fixture with {Forms=[s.Fixture.Form("visit"),s.Fixture.Form("maintenance") with {Alias="other"}]}}));
        Require(!Accepted(s with {Fixture=s.Fixture with {Suggestions=[s.Fixture.Suggestions[0] with {SourceRecordId=Guid.NewGuid()}]}}));
    }
    public static void Suggestion_mapping_rejects_duplicate_observed_rows()
    {
        var s=Create();Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0],s.Proposal.ObservedReads[0]]}}));
        Require(!Accepted(WithRead(s,Read(s) with {Suggestions=[Candidate(s),Candidate(s)]})));
        Require(!Accepted(WithRead(s,Read(s) with {SourceValues=[..Read(s).SourceValues,Read(s).SourceValues[1]]})));
    }
    public static void Suggestion_mapping_reevaluation_changes_only_its_check()
    {
        var original=Original();var updated=QualificationContinuationPolicy.ReevaluateChannelSuggestionMapping(original,true);
        Require(updated.Checks.Single(check=>check.Id=="preserves-exact-observed-mapping").Passed
            && updated with {Checks=original.Checks,Assessment=original.Assessment}==original);
    }
    public static void Suggestion_mapping_reevaluation_preserves_other_failures()
    {
        var original=Original() with {Checks=[new("preserves-exact-observed-mapping",false),new("observed-read-scope",false)]};
        var updated=QualificationContinuationPolicy.ReevaluateChannelSuggestionMapping(original,true);
        Require(updated.Checks.Single(check=>check.Id=="preserves-exact-observed-mapping").Passed
            && !updated.Checks.Single(check=>check.Id=="observed-read-scope").Passed);
    }
    public static void Suggestion_mapping_reevaluation_rejects_other_cases_or_ambiguous_checks()
    {
        foreach(var original in new[]{Original() with {CaseId="channel-visit-es"},Original() with {Checks=[]},
            Original() with {Checks=[new("preserves-exact-observed-mapping",false),new("preserves-exact-observed-mapping",false)]}})
        {
            var refused=false;try{QualificationContinuationPolicy.ReevaluateChannelSuggestionMapping(original,true);}
            catch(InvalidOperationException){refused=true;}Require(refused);
        }
    }
    private static Sample Create(string culture="es")
    {
        var subject=Guid.NewGuid();var turn=Guid.NewGuid();var sourceId=Guid.NewGuid();var set=Guid.NewGuid();var suggestion=Guid.NewGuid();
        var source=new QualificationForm(Guid.NewGuid(),Guid.NewGuid(),"visit",new Dictionary<string,Guid>{{"place",Guid.NewGuid()},{"notes",Guid.NewGuid()}});
        var target=new QualificationForm(Guid.NewGuid(),Guid.NewGuid(),"maintenance",new Dictionary<string,Guid>{{"place",Guid.NewGuid()},{"notes",Guid.NewGuid()}});
        var place=culture=="es"?"Sucursal Norte":"North branch";var notes=culture=="es"?"Hace falta mantenimiento del equipo.":"The equipment needs maintenance.";
        var form=new FormAssistantCandidate(target.DefinitionId,target.VersionId,3,"Maintenance","Fixture instruction.",culture,
            [new(target.Fields["place"],"shortText","Place",80,null,null,[]),new(target.Fields["notes"],"longText","Notes",600,null,null,[])]);
        var mapped=new FormAssistantValue(target.Fields["place"],"shortText",place);
        var candidate=new FormSuggestionAssistantCandidate(suggestion,form,[mapped],false);
        var read=new FormSuggestionAssistantCandidatesPayload("complete",subject,culture,true,sourceId,set,2,"Visit","VISIT-1",
            [new(source.Fields["place"],"shortText","Place",place),new(source.Fields["notes"],"longText","Notes",notes)],[candidate]);
        var payload=new FormSuggestionAssistantRegistrationPayload("proposed",1,sourceId,set,2,suggestion,target.DefinitionId,target.VersionId,3,
            [mapped,new(target.Fields["notes"],"longText",notes)],"Fixture reason.");
        var proposal=new AssistantObservedProposal(turn,Guid.NewGuid(),new("forms.propose-suggestion",1),payload)
            {ObservedReads=[new(turn,1,new("forms.suggestion-candidates",1),read)]};
        return new(new("channel-suggestion-"+culture,culture,"auto","Prepare the configured maintenance suggestion preserving the mapped Place.","suggestion",[]),
            subject,new([source,target],[new(subject,sourceId,"VISIT-1",suggestion,target.DefinitionId)],[]),proposal);
    }
    private static bool Accepted(Sample s)=>ChannelRunner.PreservesObservedSuggestionMapping(s.Case,s.Proposal,s.Fixture,s.Subject);
    private static FormSuggestionAssistantRegistrationPayload Payload(Sample s)=>(FormSuggestionAssistantRegistrationPayload)s.Proposal.Payload;
    private static FormSuggestionAssistantCandidatesPayload Read(Sample s)=>(FormSuggestionAssistantCandidatesPayload)s.Proposal.ObservedReads[0].Payload;
    private static FormSuggestionAssistantCandidate Candidate(Sample s)=>Read(s).Suggestions[0];
    private static Sample WithValues(Sample s,IReadOnlyList<FormAssistantValue> values)=>s with {Proposal=s.Proposal with {Payload=Payload(s) with {Values=values}}};
    private static Sample WithRead(Sample s,FormSuggestionAssistantCandidatesPayload read)=>s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {Payload=read}]}};
    private static Sample WithCandidate(Sample s,FormSuggestionAssistantCandidate candidate)=>WithRead(s,Read(s) with {Suggestions=[candidate]});
    private static CaseEvidence Original()=>new("channel-suggestion-es","whatsapp-assistant","es","completed","candidate","profile","candidateHash",
        "profile",1,"profileHash","capabilities-v1",1,"instructionsHash",[],"fixtureHash",3,2,120,new(111,22,false),12.5,512,256,
        null,[new("preserves-exact-observed-mapping",false)],"original");
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline suggestion mapping check failed.");}
    private sealed record Sample(ChannelCase Case,Guid Subject,QualificationChannelFixture Fixture,AssistantObservedProposal Proposal);
}
