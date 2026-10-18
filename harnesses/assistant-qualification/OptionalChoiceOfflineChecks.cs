using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

internal static class OptionalChoiceOfflineChecks
{
    public static readonly Action[] All=[Optional_choice_accepts_only_the_fact_backed_local_branch,
        Optional_choice_keeps_the_same_fact_in_the_English_case,Optional_choice_requires_the_explicit_place_field,
        Optional_choice_rejects_notes_or_another_extra,Optional_choice_rejects_a_foreign_choice,
        Optional_choice_rejects_an_ambiguous_observed_label,Optional_choice_requires_the_fact_in_the_public_question,
        Optional_choice_requires_the_same_turn_and_ordinal,Optional_choice_requires_the_exact_read_capability,
        Optional_choice_requires_the_same_subject_culture_and_complete_read,Optional_choice_requires_the_same_version_and_revision,
        Optional_choice_requires_the_expected_form_and_choice_type,Optional_choice_rejects_duplicate_fields,
        Optional_choice_does_not_relax_another_case,Optional_choice_reevaluation_changes_only_its_check,
        Optional_choice_reevaluation_preserves_another_failed_check];

    public static void Optional_choice_accepts_only_the_fact_backed_local_branch(){var s=Create();Require(Accepted(s));}
    public static void Optional_choice_keeps_the_same_fact_in_the_English_case()
    {
        var s=Create();var read=Read(s);
        Require(Accepted(s with {Case=s.Case with {Id="channel-original-language-en",Culture="en",
            Question="I want Local visit. Place: Sucursal norte."},Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {Payload=read with {Culture="en"}}]}}));
    }
    public static void Optional_choice_requires_the_explicit_place_field()
    {
        var s=Create();Require(!Accepted(WithValues(s,[Payload(s).Values[1]])));
    }
    public static void Optional_choice_rejects_notes_or_another_extra()
    {
        var s=Create();var payload=Payload(s);
        Require(!Accepted(WithValues(s,[payload.Values[0],new(s.Form.Fields["notes"],"longText","Sucursal norte")])));
        Require(!Accepted(WithValues(s,[..payload.Values,new(Guid.NewGuid(),"shortText","Sucursal norte")])));
    }
    public static void Optional_choice_rejects_a_foreign_choice()
    {
        var s=Create();Require(!Accepted(WithValues(s,[Payload(s).Values[0],Payload(s).Values[1] with {Value=Guid.NewGuid().ToString("D")}])));
    }
    public static void Optional_choice_rejects_an_ambiguous_observed_label()
    {
        var s=Create();var read=Read(s);var candidate=read.Forms[0];var branch=candidate.Questions[2];
        Require(!Accepted(WithRead(s,read with {Forms=[candidate with {Questions=[candidate.Questions[0],candidate.Questions[1],
            branch with {Choices=[..branch.Choices,new(Guid.NewGuid(),"Sucursal norte")]}]}]})));
    }
    public static void Optional_choice_requires_the_fact_in_the_public_question()
    {
        var s=Create();Require(!Accepted(s with {Case=s.Case with {Question="Quiero el formulario Visita local."}}));
        Require(!Accepted(s with {Case=s.Case with {Question="Quiero el formulario Visita local. Lugar: Sucursal nortecentro."}}));
    }
    public static void Optional_choice_requires_the_same_turn_and_ordinal()
    {
        var s=Create();Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {TurnId=Guid.NewGuid()}]}}));
        Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {Ordinal=2}]}}));
    }
    public static void Optional_choice_requires_the_exact_read_capability()
    {
        var s=Create();Require(!Accepted(s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {Capability=new("forms.assistant-candidates",2)}]}}));
        Require(!Accepted(s with {Proposal=s.Proposal with {Capability=new("forms.propose-registration",2)}}));
    }
    public static void Optional_choice_requires_the_same_subject_culture_and_complete_read()
    {
        var s=Create();Require(!Accepted(WithRead(s,Read(s) with {RecipientUserId=Guid.NewGuid()})));
        Require(!Accepted(WithRead(s,Read(s) with {Culture="en"})));
        Require(!Accepted(WithRead(s,Read(s) with {IsComplete=false})));
    }
    public static void Optional_choice_requires_the_same_version_and_revision()
    {
        var s=Create();var read=Read(s);Require(!Accepted(WithRead(s,read with {Forms=[read.Forms[0] with {VersionId=Guid.NewGuid()}]})));
        Require(!Accepted(WithRead(s,read with {Forms=[read.Forms[0] with {DefinitionRevision=4}]})));
    }
    public static void Optional_choice_requires_the_expected_form_and_choice_type()
    {
        var s=Create();Require(!Accepted(s with {Proposal=s.Proposal with {Payload=Payload(s) with {DefinitionId=Guid.NewGuid()}}}));
        Require(!Accepted(WithValues(s,[Payload(s).Values[0],Payload(s).Values[1] with {Type="shortText"}])));
    }
    public static void Optional_choice_rejects_duplicate_fields()
    {
        var s=Create();Require(!Accepted(WithValues(s,[..Payload(s).Values,Payload(s).Values[1]])));
    }
    public static void Optional_choice_does_not_relax_another_case()
    {
        var s=Create();Require(!Accepted(s with {Case=s.Case with {Id="channel-visit-es"}}));
        Require(!Accepted(s with {Case=s.Case with {FormAlias="visit"}}));
    }
    public static void Optional_choice_reevaluation_changes_only_its_check()
    {
        var original=Original();var updated=QualificationContinuationPolicy.ReevaluateChannelFieldSet(original,true);
        Require(updated.Checks.Single(c=>c.Id=="exact-requested-field-set").Passed
            && updated with {Checks=original.Checks,Assessment=original.Assessment}==original);
    }
    public static void Optional_choice_reevaluation_preserves_another_failed_check()
    {
        var original=Original() with {Checks=[new("exact-requested-field-set",false),new("typed-values-from-exact-observed-catalog",false)]};
        var updated=QualificationContinuationPolicy.ReevaluateChannelFieldSet(original,true);
        Require(updated.Checks.Single(c=>c.Id=="exact-requested-field-set").Passed
            && !updated.Checks.Single(c=>c.Id=="typed-values-from-exact-observed-catalog").Passed);
    }
    private static Sample Create()
    {
        var subject=Guid.NewGuid();var turn=Guid.NewGuid();var place=Guid.NewGuid();var notes=Guid.NewGuid();var branch=Guid.NewGuid();var choice=Guid.NewGuid();
        var form=new QualificationForm(Guid.NewGuid(),Guid.NewGuid(),"local-visit",new Dictionary<string,Guid>{{"place",place},{"notes",notes},{"branch",branch}});
        var candidate=new FormAssistantCandidate(form.DefinitionId,form.VersionId,3,"Visita local","Public instruction.","es",
            [new(place,"shortText","Lugar",80,null,null,[]),new(notes,"longText","Notas",600,null,null,[]),
             new(branch,"choice","Sede",null,Guid.NewGuid(),1,[new(choice,"Sucursal Norte")])]);
        var payload=new FormAssistantRegistrationPayload("ready",1,form.DefinitionId,form.VersionId,3,
            [new(place,"shortText","Sucursal norte"),new(branch,"choice",choice.ToString("D"))],"Public fixture reason.");
        var proposal=new AssistantObservedProposal(turn,Guid.NewGuid(),new("forms.propose-registration",1),payload)
            {ObservedReads=[new(turn,1,new("forms.assistant-candidates",1),new FormAssistantCandidatesPayload("complete",subject,"es",true,[candidate],[]))]};
        return new(new("channel-original-language-es","es","auto","Quiero el formulario Visita local. Lugar: Sucursal norte.","registration",[],"local-visit",[new("place","shortText","Sucursal norte")]),
            subject,form,new([form,new QualificationForm(Guid.NewGuid(),Guid.NewGuid(),"visit",form.Fields)],[],[]),proposal);
    }
    private static bool Accepted(Sample s)
    {
        var checks=new List<CheckEvidence>();
        ChannelRunner.AddSemanticChecks(checks,s.Case,s.Proposal.Payload,s.Fixture,new([],"Supervisor"),s.Subject,s.Proposal);
        return checks.Single(check=>check.Id=="exact-requested-field-set").Passed;
    }
    private static FormAssistantRegistrationPayload Payload(Sample s)=>(FormAssistantRegistrationPayload)s.Proposal.Payload;
    private static FormAssistantCandidatesPayload Read(Sample s)=>(FormAssistantCandidatesPayload)s.Proposal.ObservedReads[0].Payload;
    private static Sample WithValues(Sample s,IReadOnlyList<FormAssistantValue> values)=>s with {Proposal=s.Proposal with {Payload=Payload(s) with {Values=values}}};
    private static Sample WithRead(Sample s,FormAssistantCandidatesPayload read)=>s with {Proposal=s.Proposal with {ObservedReads=[s.Proposal.ObservedReads[0] with {Payload=read}]}};
    private static CaseEvidence Original()=>new("channel-original-language-es","whatsapp-assistant","es","completed","candidate","profile","candidateHash",
        "profile",1,"profileHash","capabilities-v1",1,"instructionsHash",[],"fixtureHash",3,2,120,new(111,22,false),12.5,512,256,
        null,[new("exact-requested-field-set",false)],"original");
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline optional choice check failed.");}
    private sealed record Sample(ChannelCase Case,Guid Subject,QualificationForm Form,QualificationChannelFixture Fixture,AssistantObservedProposal Proposal);
}
