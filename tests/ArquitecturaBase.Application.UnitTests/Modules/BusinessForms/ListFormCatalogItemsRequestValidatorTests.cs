using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class ListFormCatalogItemsRequestValidatorTests
{
    [Fact]
    public async Task Item_ids_filter_rejects_more_than_one_hundred_or_empty_identities()
    {
        var validator = new ListFormCatalogItemsRequestValidator();
        var hundred = Enumerable.Range(0, 100).Select(_ => Guid.CreateVersion7()).ToArray();

        Assert.True((await validator.ValidateAsync(new ListFormCatalogItemsRequest(), TestContext.Current.CancellationToken)).IsValid);
        Assert.True((await validator.ValidateAsync(new ListFormCatalogItemsRequest { Ids = hundred }, TestContext.Current.CancellationToken)).IsValid);

        var tooMany = await validator.ValidateAsync(new ListFormCatalogItemsRequest { Ids = [.. hundred, Guid.CreateVersion7()] },
            TestContext.Current.CancellationToken);
        var empty = await validator.ValidateAsync(new ListFormCatalogItemsRequest { Ids = [Guid.CreateVersion7(), Guid.Empty] },
            TestContext.Current.CancellationToken);

        Assert.Contains(tooMany.Errors, error => error.PropertyName == nameof(ListFormCatalogItemsRequest.Ids));
        Assert.Contains(empty.Errors, error => error.PropertyName == nameof(ListFormCatalogItemsRequest.Ids));
    }
}
