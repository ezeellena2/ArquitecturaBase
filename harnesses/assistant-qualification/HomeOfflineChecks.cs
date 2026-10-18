using ArquitecturaBase.Application.Models.SystemQueries;
namespace AssistantQualification;
internal static class HomeOfflineChecks
{
    private static HomeCase Counts(string culture)=>new("home-users-"+culture,culture,"public fixture",["users.summary"],ExpectedAnswerFragments:["14","1"]);
    private static SystemQueryEvidence[] Evidence(string culture)=>[new(Guid.NewGuid(),"users.summary",1,DateTime.UnixEpoch,null,null,[],
        [new(null,[new(culture=="es"?"Cuentas activas":"Active accounts","14"),new(culture=="es"?"Cuentas inactivas":"Inactive accounts","1")])])];
    public static void Home_count_labels_cannot_borrow_one_from_fourteen()=>Require(!HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts.",Evidence("en")));
    public static void Home_counts_cannot_swap_active_and_inactive_labels()=>Require(!HomeRunner.FixedFactsMatch(Counts("es"),"Hay 1 cuenta activa y 14 cuentas inactivas.",Evidence("es")));
    public static void Home_counts_cannot_negate_the_observed_fact()=>Require(!HomeRunner.FixedFactsMatch(Counts("en"),"There are not 14 active accounts and there is 1 inactive account.",Evidence("en")));
    public static void Home_counts_match_both_supported_label_orders()
    {
        Require(HomeRunner.FixedFactsMatch(Counts("es"),"Hay 14 cuentas activas y 1 cuenta inactiva.",Evidence("es")));
        Require(HomeRunner.FixedFactsMatch(Counts("en"),"Active accounts: 14. Inactive accounts: 1.",Evidence("en")));
    }
    public static void Home_hours_do_not_reverse_opening_and_closing()=>Require(!HomeRunner.FixedFactsMatch(
        new("home-business-es","es","public fixture",["business.information"],ExpectedAnswerFragments:["09:00","17:00"]),"Atiende de 17:00 a 09:00.",[]));
    public static void Home_identical_observed_counts_can_be_repeated()
    {
        var evidence=Evidence("en");
        Require(HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts and 1 inactive account.",[..evidence,..evidence]));
    }
    public static void Home_conflicting_observed_counts_fail_without_throwing()
    {
        var evidence=Evidence("en");
        var conflicting=evidence[0] with {Rows=[new(null,[new("Active accounts","13"),new("Inactive accounts","1")])]};
        var threw=false;var valid=false;
        try{valid=HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts and 1 inactive account.",[..evidence,conflicting]);}
        catch(InvalidOperationException){threw=true;}
        Require(!threw && !valid);
    }
    public static void Home_counts_allow_negation_outside_the_observed_claims()
    {
        Require(HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts and 1 inactive account. No deleted accounts are included.",Evidence("en")));
        Require(HomeRunner.FixedFactsMatch(Counts("es"),"Hay 14 cuentas activas y 1 cuenta inactiva. No se incluyen cuentas borradas.",Evidence("es")));
    }
    public static void Home_counts_reject_a_numeric_contradiction_after_a_correct_claim()
    {
        Require(!HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts and 1 inactive account. Active accounts: 13.",Evidence("en")));
        Require(!HomeRunner.FixedFactsMatch(Counts("es"),"Hay 14 cuentas activas y 1 cuenta inactiva. Cuentas activas: 13.",Evidence("es")));
    }
    public static void Home_counts_still_reject_negation_in_a_repeated_claim()
    {
        Require(!HomeRunner.FixedFactsMatch(Counts("en"),"14 active accounts and 1 inactive account. There are not 14 active accounts.",Evidence("en")));
        Require(!HomeRunner.FixedFactsMatch(Counts("es"),"Hay 14 cuentas activas y 1 cuenta inactiva. No hay 1 cuenta inactiva.",Evidence("es")));
    }
    public static void Home_counts_accept_Markdown_in_number_first_claims()
    {
        foreach(var marker in new[]{"*","_","~",new string((char)96,1)})
            Require(HomeRunner.FixedFactsMatch(Counts("es"),$"Hay {marker}14{marker} cuentas {marker}activas{marker} y {marker}1{marker} cuenta {marker}inactiva{marker}.",Evidence("es")));
    }
    public static void Home_counts_accept_Markdown_in_label_first_claims()
    {
        Require(HomeRunner.FixedFactsMatch(Counts("es"),"**Cuentas activas**: **14**. _Cuentas inactivas_: ~1~.",Evidence("es")));
        var tick=new string((char)96,1);
        Require(HomeRunner.FixedFactsMatch(Counts("en"),$"{tick}Active accounts{tick}: _14_. **Inactive accounts**: **1**.",Evidence("en")));
    }
    public static void Home_Markdown_does_not_hide_wrong_negated_or_missing_facts()
    {
        Require(!HomeRunner.FixedFactsMatch(Counts("es"),"No hay **14** cuentas activas y **1** cuenta inactiva.",Evidence("es")));
        Require(!HomeRunner.FixedFactsMatch(Counts("en"),"**14** active accounts and _1_ inactive account. **Active accounts**: **13**.",Evidence("en")));
        Require(!HomeRunner.FixedFactsMatch(Counts("es"),"Hay **1** cuenta activa y **14** cuentas inactivas.",Evidence("es")));
        Require(!HomeRunner.FixedFactsMatch(Counts("es") with {ExpectedAnswerFragments=["14","1","Supervisor"]},
            "Hay **14** cuentas activas y **1** cuenta inactiva.",Evidence("es")));
        Require(!HomeRunner.FixedFactsMatch(Counts("en"),"**14** active accounts.",Evidence("en")));
    }
    private static void Require(bool valid){if(!valid)throw new InvalidOperationException("Offline fixture facts check failed.");}
}
