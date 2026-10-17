using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormSuggestionHistoryPolicyTests
{
    [Fact]
    public void Add_remove_and_update_are_counted_by_stable_suggestion_id()
    {
        var first=Suggestion();var removed=Suggestion();var added=Suggestion();
        var result=FormSuggestionHistoryPolicy.Compare([first with{When="nuevo texto"},added],[first,removed]);
        Assert.Equal(new FormSuggestionChangesResponse(1,1,1,false),result);
    }
    [Fact]
    public void Moving_an_existing_suggestion_changes_order_without_counting_an_edit()
    {
        var first=Suggestion();var second=Suggestion();
        Assert.Equal(new FormSuggestionChangesResponse(0,0,0,true),FormSuggestionHistoryPolicy.Compare([second,first],[first,second]));
    }
    [Fact]
    public void An_added_suggestion_does_not_claim_existing_rows_were_reordered()
    {
        var first=Suggestion();var second=Suggestion();
        Assert.Equal(new FormSuggestionChangesResponse(1,0,0,false),FormSuggestionHistoryPolicy.Compare([Suggestion(),first,second],[first,second]));
    }
    [Fact]
    public void Activation_mapping_and_condition_changes_are_edits()
    {
        var original=Suggestion();
        Assert.Equal(1,FormSuggestionHistoryPolicy.Compare([original with{IsActive=false}],[original]).UpdatedSuggestions);
        Assert.Equal(1,FormSuggestionHistoryPolicy.Compare([original with{Mappings=[new(new("RecordNumber"),Guid.NewGuid())]}],[original]).UpdatedSuggestions);
        Assert.Equal(1,FormSuggestionHistoryPolicy.Compare([original with{Conditions=[new(Guid.NewGuid(),"answered",[])]}],[original]).UpdatedSuggestions);
    }
    [Fact]
    public void A_newly_published_destination_version_is_metadata_not_a_configuration_edit()
    {
        var original=Suggestion();
        Assert.Equal(new FormSuggestionChangesResponse(0,0,0,false),FormSuggestionHistoryPolicy.Compare([original with{DestinationVersionId=Guid.NewGuid()}],[original]));
    }
    private static FormSuggestion Suggestion()=>new(Guid.NewGuid(),Guid.NewGuid(),"Assistant","cuando lo solicite",true,[],[]){DestinationVersionId=Guid.NewGuid()};
}
