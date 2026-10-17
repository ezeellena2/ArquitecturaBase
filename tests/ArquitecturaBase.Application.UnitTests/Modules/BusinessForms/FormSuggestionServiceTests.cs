using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormSuggestionServiceTests
{
    [Fact]
    public async Task Empty_configuration_is_revision_zero_and_requires_only_suggestion_management()
    {
        var f=new FormSuggestionFixture();var result=await f.Service.GetCurrentAsync(f.Definition.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(0,result.Value.Revision);Assert.Empty(result.Value.Suggestions);Assert.Null(result.Value.Id);
        Assert.Equal(0,f.Drafts.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task Saving_moves_only_the_suggestion_pointer_and_adds_an_immutable_revision()
    {
        var f=new FormSuggestionFixture();var revision=f.Definition.Revision;var version=f.Definition.ActiveVersionId;
        var saved=await f.Service.SaveAsync(f.Definition.Id,new(0,[f.Suggestion()]),TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);Assert.Equal(version,saved.Value.SourceVersionId);Assert.Equal(1,saved.Value.Revision);Assert.Equal(saved.Value.Id,f.Definition.ActiveSuggestionSetId);
        Assert.Equal(version,f.Definition.ActiveVersionId);Assert.Equal(revision,f.Definition.Revision);Assert.Null(f.Definition.ActiveRuleSetId);
        Assert.Equal(CommitPolicy.OnSuccess,f.Drafts.UnitOfWork.LastPolicy);Assert.Equal(1,f.Drafts.UnitOfWork.Commits);
        var first=Assert.Single(f.Sets);var firstJson=first.SuggestionsJson;
        Assert.True((await f.Service.SaveAsync(f.Definition.Id,new(1,[]),TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(firstJson,first.SuggestionsJson);Assert.Equal(2,f.Sets.Count);Assert.NotEqual(first.Id,f.Definition.ActiveSuggestionSetId);
    }
    [Fact]
    public async Task Stale_revision_does_not_add_or_replace_any_configuration()
    {
        var f=new FormSuggestionFixture();Assert.True((await f.Service.SaveAsync(f.Definition.Id,new(0,[]),TestContext.Current.CancellationToken)).IsSuccess);
        var id=f.Definition.ActiveSuggestionSetId;
        var stale=await f.Service.SaveAsync(f.Definition.Id,new(0,[]),TestContext.Current.CancellationToken);
        Assert.Equal(FormSuggestionErrors.RevisionConflict,stale.Error);Assert.Single(f.Sets);Assert.Equal(id,f.Definition.ActiveSuggestionSetId);
        Assert.Equal(1,f.Drafts.UnitOfWork.Rollbacks);
    }
    [Fact]
    public async Task Revocation_while_waiting_for_the_lock_prevents_the_revision()
    {
        var f=new FormSuggestionFixture();f.Drafts.BeforeLock=()=>f.Drafts.Permissions.Permissions.Clear();
        var result=await f.Service.SaveAsync(f.Definition.Id,new(0,[]),TestContext.Current.CancellationToken);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);Assert.Empty(f.Sets);Assert.Null(f.Definition.ActiveSuggestionSetId);
    }
    [Fact]
    public async Task An_unpublished_source_does_not_create_a_suggestion_revision()
    {
        var f=new FormSuggestionFixture();f.Options=null;
        var result=await f.Service.SaveAsync(f.Definition.Id,new(0,[]),TestContext.Current.CancellationToken);
        Assert.Equal(FormSuggestionErrors.SourceNotPublished,result.Error);Assert.Empty(f.Sets);
    }
    [Fact]
    public async Task A_destination_removed_inside_the_write_limit_rejects_the_whole_save()
    {
        var f=new FormSuggestionFixture();var suggestion=f.Suggestion();f.BeforeOptionsReturn=()=>f.Options=f.Options! with {Destinations=[]};
        var result=await f.Service.SaveAsync(f.Definition.Id,new(0,[suggestion]),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Empty(f.Sets);Assert.Null(f.Definition.ActiveSuggestionSetId);
    }
    [Fact]
    public async Task Revocation_during_a_current_read_discards_the_configuration()
    {
        var f=new FormSuggestionFixture();f.BeforeCurrentReturn=()=>f.Drafts.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await f.Service.GetCurrentAsync(f.Definition.Id,TestContext.Current.CancellationToken)).Error);
        Assert.Equal(0,f.Drafts.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task History_compares_the_predecessor_outside_the_page_without_revision_lookups()
    {
        var f=new FormSuggestionFixture();Assert.True((await f.Service.SaveAsync(f.Definition.Id,new(0,[f.Suggestion()]),TestContext.Current.CancellationToken)).IsSuccess);
        Assert.True((await f.Service.SaveAsync(f.Definition.Id,new(1,[]),TestContext.Current.CancellationToken)).IsSuccess);var reads=f.RevisionReads;
        var page=await f.Service.ListRevisionsAsync(f.Definition.Id,new(){PageSize=1},TestContext.Current.CancellationToken);
        Assert.Equal(2,page.Value.TotalCount);Assert.Equal(new FormSuggestionChangesResponse(0,0,1,false),Assert.Single(page.Value.Items).Changes);Assert.Equal(reads,f.RevisionReads);
    }
    [Fact]
    public async Task Revocation_during_history_read_discards_the_page()
    {
        var f=new FormSuggestionFixture();f.BeforeHistoryReturn=()=>f.Drafts.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await f.Service.ListRevisionsAsync(f.Definition.Id,new(),TestContext.Current.CancellationToken)).Error);
        Assert.Equal(0,f.Drafts.UnitOfWork.Transactions);
    }
}
