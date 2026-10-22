using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_catalog_can_be_deactivated_and_reactivated_in_one_revision_per_save(bool filters)
    {
        var texts = new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" };
        var input = texts.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        var catalog = filters ? BusinessFormCatalog.CreateFilters(texts, texts, "{}") : BusinessFormCatalog.CreateManual(texts, texts);
        var repository = new FormCatalogActivationRepository(catalog);
        var work = new FakeUnitOfWork();
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var guard = new FormCatalogAccessGuard(new FakeCurrentUser { UserId = actor }, permissions);
        var manual = new FormCatalogService(repository, null!, work, RequestValidators.For(new UpdateManualCatalogRequestValidator()), guard, null!, new(repository));
        var filtered = new FormCatalogFiltersService(repository, null!, work, RequestValidators.For(new UpdateFiltersCatalogRequestValidator()), guard, null!, null!);
        var definition = new FormCatalogFilterDefinition("name", null, [], false, "name", false);
        async Task<Result<FormCatalogSaveResponse>> Save(long revision, bool active) => filters
            ? await filtered.UpdateAsync(catalog.Id, new(revision, input, input, definition, active), TestContext.Current.CancellationToken)
            : await manual.UpdateManualAsync(catalog.Id, new(revision, input, input, [], IsActive: active), TestContext.Current.CancellationToken);
        var inactive = await Save(1, false);
        Assert.True(inactive.IsSuccess);
        Assert.False(inactive.Value.Catalog.IsActive);
        Assert.Equal(2, inactive.Value.Catalog.Revision);
        var stale = await Save(1, true);
        Assert.True(stale.IsFailure);
        Assert.Equal(FormCatalogErrors.RevisionConflict, stale.Error);
        Assert.False(catalog.IsActive);
        var active = await Save(2, true);
        Assert.True(active.IsSuccess);
        Assert.True(active.Value.Catalog.IsActive);
        Assert.Equal(3, active.Value.Catalog.Revision);
        Assert.Equal(3, work.Transactions);
    }
}
