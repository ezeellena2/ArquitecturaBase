using System.Text.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldTests
{
    [Fact]
    public void New_fields_start_optional_and_do_not_advance_the_catalog_revision()
    {
        var catalog = Catalog();
        var field = Field();

        Assert.True(catalog.SetFields([field], null).IsSuccess);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(field.Id, JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson!, JsonSerializerOptions.Web)![0].Id);
        var required = catalog.SetFields([field with { Id = Guid.CreateVersion7(), Key = "another", Required = true }], null);
        Assert.Equal("Forms.Catalog.FieldInvalid", required.Error.Code);
    }

    [Fact]
    public void Stored_field_keys_cannot_change_and_inactive_fields_keep_their_names()
    {
        var catalog = Catalog();
        var field = Field();
        Assert.True(catalog.SetFields([field], null).IsSuccess);

        Assert.Equal("Forms.Catalog.FieldInvalid", catalog.SetFields([field with { Key = "changed" }], null).Error.Code);
        Assert.True(catalog.SetFields([field with { IsActive = false, Name = new Dictionary<string, string> { ["es"] = "Changed" } }], null).IsSuccess);
        var stored = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson!, JsonSerializerOptions.Web)![0];
        Assert.Equal("Campo", stored.Name["es"]);
        Assert.False(stored.IsActive);
    }

    [Fact]
    public void Inactive_keys_remain_unique_and_filters_catalogs_cannot_define_fields()
    {
        var catalog = Catalog();
        var field = Field() with { IsActive = false };
        Assert.Equal("Forms.Catalog.FieldKeyDuplicate", catalog.SetFields([field, Field()], null).Error.Code);
        var filters = BusinessFormCatalog.CreateFilters(new Dictionary<string, string> { ["es"] = "People" }, new Dictionary<string, string>(), "{}");
        Assert.Equal(FormCatalogErrors.ManualItemsUnavailable, filters.SetFields([Field()], null).Error);
    }

    [Fact]
    public void Twenty_fields_is_the_limit_including_inactive_fields()
    {
        var catalog = Catalog();
        var fields = Enumerable.Range(0, 21).Select(index => Field() with { Key = $"field_{index}", IsActive = index % 2 == 0 }).ToArray();
        Assert.Equal("Forms.Catalog.TooManyFields", catalog.SetFields(fields, null).Error.Code);
        Assert.True(catalog.SetFields(fields[..20], null).IsSuccess);
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
    private static BusinessFormCatalogField Field() => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, "text", null, false, false, null, null, 300, "data", 0, true);
}
