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
        var filters = BusinessFormCatalog.CreateFilters("es", new Dictionary<string, string> { ["es"] = "People" }, new Dictionary<string, string>(), "{}");
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

    [Fact]
    public void Blank_name_translations_are_discarded_when_fields_are_saved()
    {
        var catalog = Catalog();
        var empty = Field() with { Name = new Dictionary<string, string> { ["es"] = "Campo", ["en"] = "" } };
        var spaces = Field() with { Key = "other", Name = new Dictionary<string, string> { ["es"] = "Otro", ["en"] = "   " } };

        Assert.True(catalog.SetFields([empty, spaces], null).IsSuccess);
        var stored = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(catalog.FieldsJson!, JsonSerializerOptions.Web)!;
        Assert.All(stored, field => Assert.Equal(["es"], field.Name.Keys));
        Assert.Equal(["Campo", "Otro"], stored.Select(field => field.Name["es"]));
        var blankOriginal = Field() with { Name = new Dictionary<string, string> { ["es"] = "  ", ["en"] = "Field" } };
        Assert.Equal("Forms.Catalog.FieldInvalid", Catalog().SetFields([blankOriginal], null).Error.Code);
    }

    [Fact]
    public void Field_names_require_the_catalog_original_language_instead_of_Spanish()
    {
        var english = BusinessFormCatalog.CreateManual("en", new Dictionary<string, string> { ["en"] = "Suppliers" }, new Dictionary<string, string>());
        var spanishOnly = Field() with { Name = new Dictionary<string, string> { ["es"] = "Campo" } };
        var englishOnly = Field() with { Name = new Dictionary<string, string> { ["en"] = "Field", ["es"] = " " } };

        Assert.Equal("Forms.Catalog.FieldInvalid", english.SetFields([spanishOnly], null).Error.Code);
        Assert.Null(english.FieldsJson);
        Assert.True(english.SetFields([englishOnly], null).IsSuccess);
        var stored = JsonSerializer.Deserialize<BusinessFormCatalogField[]>(english.FieldsJson!, JsonSerializerOptions.Web)!;
        Assert.Equal(["en"], Assert.Single(stored).Name.Keys);
    }

    private static BusinessFormCatalog Catalog() => BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
    private static BusinessFormCatalogField Field() => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, "text", null, false, false, null, null, 300, "data", 0, true);
}
