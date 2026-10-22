using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogUsageCountTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Saving_a_catalog_returns_its_actual_usage_only_with_form_read(bool filters, bool canReadForms)
    {
        var catalog = Catalog(filters);
        var reader = new FormCatalogUsageReader(3);
        var work = new FakeUnitOfWork();

        var result = await SaveAsync(filters, canReadForms, catalog, reader, work, 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(canReadForms ? 3 : (int?)null, result.Value.Catalog.UsageCount);
        if (canReadForms) Assert.Equal(catalog.Id, Assert.Single(reader.UsageReads));
        else Assert.Empty(reader.UsageReads);
        Assert.Equal(2, result.Value.Catalog.Revision);
        Assert.Equal(1, work.Transactions);
        Assert.Equal(1, work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, work.LastPolicy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_catalog_revision_conflict_does_not_read_usage(bool filters)
    {
        var catalog = Catalog(filters);
        var reader = new FormCatalogUsageReader(3);
        var work = new FakeUnitOfWork();

        var result = await SaveAsync(filters, true, catalog, reader, work, 2);

        Assert.True(result.IsFailure);
        Assert.Equal(FormCatalogErrors.RevisionConflict, result.Error);
        Assert.Empty(reader.UsageReads);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, work.Rollbacks);
    }

    private static BusinessFormCatalog Catalog(bool filters)
    {
        var texts = new Dictionary<string, string> { ["es"] = "Personas", ["en"] = "People" };
        return filters ? BusinessFormCatalog.CreateFilters(texts, texts, "{}") : BusinessFormCatalog.CreateManual(texts, texts);
    }

    private static Task<Result<FormCatalogSaveResponse>> SaveAsync(bool filters, bool canReadForms,
        BusinessFormCatalog catalog, FormCatalogUsageReader reader, FakeUnitOfWork work, long expectedRevision)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = canReadForms
            ? [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.Read]
            : [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        var guard = new FormCatalogAccessGuard(new FakeCurrentUser { UserId = actor }, permissions);
        var repository = new FormCatalogActivationRepository(catalog);
        var texts = new Dictionary<string, string?> { ["es"] = "Personas editadas", ["en"] = "Edited people" };
        var cancellationToken = TestContext.Current.CancellationToken;
        if (filters)
        {
            var service = new FormCatalogFiltersService(repository, null!, work,
                RequestValidators.For(new UpdateFiltersCatalogRequestValidator()), guard, null!, reader);
            return service.UpdateAsync(catalog.Id, new(expectedRevision, texts, texts,
                new("name", null, [], false, "name", false)), cancellationToken);
        }
        var manual = new FormCatalogService(repository, reader, work,
            RequestValidators.For(new UpdateManualCatalogRequestValidator()), guard, null!, new(repository));
        return manual.UpdateManualAsync(catalog.Id, new(expectedRevision, texts, texts, []), cancellationToken);
    }
}
