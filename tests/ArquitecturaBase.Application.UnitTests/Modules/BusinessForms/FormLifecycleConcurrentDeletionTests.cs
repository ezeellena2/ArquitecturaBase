using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormLifecycleConcurrentDeletionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Detail_returns_not_found_when_the_definition_disappears_before_lifecycle_is_read()
    {
        var f = new FormDefinitionLifecycleReadFixture(); f.MissingFactsIds.Add(f.Id);
        var result = await f.Policy.FindAsync(f.Id, f.User.UserId!.Value, Ct);
        Assert.Equal(FormDefinitionErrors.NotFound.Code, result.Error.Code);
    }
    [Fact]
    public async Task A_page_omits_only_the_disappearing_row_and_keeps_the_observed_sql_page()
    {
        var f = new FormDefinitionLifecycleReadFixture(); f.MissingFactsIds.Add(f.Id);
        var result = await f.Policy.ListAsync(new(), f.User.UserId!.Value, Ct);
        Assert.True(result.IsSuccess); Assert.Equal(f.OtherId, Assert.Single(result.Value.Items).Id);
        Assert.Equal((3, 2, 7), (result.Value.Page, result.Value.PageSize, result.Value.TotalCount)); Assert.Equal(1, f.FactReads);
    }
    [Fact]
    public async Task A_deactivation_returns_not_found_when_a_later_delete_hides_its_metadata()
    {
        var f = new FormLifecycleFixture { BeforeBatchRead = null };
        f.BeforeBatchRead = () => f.HideFacts = true;
        var result = await f.Service.DeactivateAsync(f.Definition.Id, new(1), Ct);
        Assert.Equal(FormDefinitionErrors.NotFound.Code, result.Error.Code); Assert.Equal(1, f.UnitOfWork.Commits);
    }
}
