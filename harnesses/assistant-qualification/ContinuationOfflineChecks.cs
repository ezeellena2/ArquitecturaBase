namespace AssistantQualification;

internal static class ContinuationOfflineChecks
{
    public static void Continuation_submits_only_cases_without_any_ledger()
    {
        var accepted=Entry("known","accepted");
        var plan=QualificationContinuationPolicy.Build(["known","new"],[accepted]);
        Require(!plan[0].ShouldSubmit && plan[0].Existing==accepted && plan[1].ShouldSubmit && plan[1].Existing is null);
    }
    public static void Continuation_before_submit_uncertainty_keeps_its_existing_identity()
    {
        var uncertain=Entry("known","before-submit");
        var step=QualificationContinuationPolicy.Build(["known"],[uncertain]).Single();
        Require(!step.ShouldSubmit && step.Existing==uncertain);
    }
    public static void Continuation_refuses_a_ledger_outside_the_original_inventory()=>Reject(
        ()=>QualificationContinuationPolicy.Build(["known"],[Entry("other","accepted")]));
    public static void Continuation_refuses_duplicate_case_identifiers()=>Reject(
        ()=>QualificationContinuationPolicy.Build(["known","known"],[]));
    public static void Continuation_refuses_multiple_requests_for_the_same_case()=>Reject(
        ()=>QualificationContinuationPolicy.Build(["known"],[Entry("known","accepted"),Entry("known","before-submit")]));
    public static void Continuation_home_reevaluation_changes_only_the_fact_check_and_assessment()
    {
        var original=Original();
        var updated=QualificationContinuationPolicy.ReevaluateHome(original,true);
        Require(updated.Checks.Single(check=>check.Id=="expected-fixture-facts").Passed
            && updated with {Checks=original.Checks,Assessment=original.Assessment}==original);
    }
    public static void Continuation_home_reevaluation_preserves_other_failed_checks()
    {
        var original=Original() with {Checks=[new("completed",false),new("expected-fixture-facts",false)]};
        var updated=QualificationContinuationPolicy.ReevaluateHome(original,true);
        Require(!updated.Checks.Single(check=>check.Id=="completed").Passed
            && updated.Checks.Single(check=>check.Id=="expected-fixture-facts").Passed);
    }
    private static QualificationLedgerEntry Entry(string id,string phase)=>new(id,Guid.NewGuid(),Guid.NewGuid(),null,null,phase);
    private static CaseEvidence Original()=>new("known","home","es","completed","candidate","profile","candidateHash",
        "profile",1,"profileHash","home-system",1,"instructionsHash",[],"fixtureHash",2,1,120,new(111,22,false),12.5,512,256,
        null,[new("expected-fixture-facts",false)],"original");
    private static void Reject(Action action)
    {
        var rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Require(rejected);
    }
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline continuation check failed.");}
}
