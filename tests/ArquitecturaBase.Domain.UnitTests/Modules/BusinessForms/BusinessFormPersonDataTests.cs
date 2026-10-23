using System.Text.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormPersonDataTests
{
    [Fact]
    public void A_person_attribute_requires_a_source_only_when_its_type_is_catalog()
    {
        Assert.True(BusinessFormPersonAttribute.Create("zone", "es", Names(), BusinessFormPersonAttributeType.Catalog, null, 0).IsFailure);
        Assert.True(BusinessFormPersonAttribute.Create("zone", "es", Names(), BusinessFormPersonAttributeType.Text, Guid.CreateVersion7(), 0).IsFailure);
        Assert.True(BusinessFormPersonAttribute.Create("zone", "es", Names(), BusinessFormPersonAttributeType.Text, null, 0).IsSuccess);
        Assert.True(BusinessFormPersonAttribute.Create("zone", "es", Names(), BusinessFormPersonAttributeType.Catalog, Guid.CreateVersion7(), 0).IsSuccess);
    }

    [Fact]
    public void Updating_an_attribute_keeps_its_key_and_identity_and_preserves_its_name_when_inactive()
    {
        var names = Names();
        var attribute = BusinessFormPersonAttribute.Create("zone", "es", names, BusinessFormPersonAttributeType.Catalog, Guid.CreateVersion7(), 0).Value;
        var id = attribute.Id;
        names["es"] = "Cambiado afuera";
        Assert.Equal("Zona", JsonDocument.Parse(attribute.NameJson).RootElement.GetProperty("es").GetString());

        var result = attribute.Update(1, Names("Región"), BusinessFormPersonAttributeType.Text, null, 2, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(id, attribute.Id);
        Assert.Equal("zone", attribute.Key);
        Assert.Equal(2, attribute.Revision);
        Assert.Equal(2, attribute.Position);
        Assert.False(attribute.IsActive);
        Assert.Equal("Zona", JsonDocument.Parse(attribute.NameJson).RootElement.GetProperty("es").GetString());
        Assert.Equal(FormPersonDataErrors.AttributeRevisionConflict,
            attribute.Update(1, Names(), BusinessFormPersonAttributeType.Text, null, 0, true).Error);
        Assert.False(attribute.IsActive);
        Assert.Equal(2, attribute.Revision);
    }

    [Fact]
    public void Deactivating_and_editing_an_inactive_attribute_preserves_its_last_active_name()
    {
        var attribute = BusinessFormPersonAttribute.Create("zone", "es", Names(), BusinessFormPersonAttributeType.Text, null, 0).Value;
        Assert.True(attribute.Update(1, Names("Región"), BusinessFormPersonAttributeType.Text, null, 0, true).IsSuccess);
        Assert.True(attribute.Update(2, Names("No reemplazar"), BusinessFormPersonAttributeType.Text, null, 0, false).IsSuccess);
        Assert.True(attribute.Update(3, Names("Tampoco"), BusinessFormPersonAttributeType.Text, null, 0, false).IsSuccess);
        Assert.Equal("Región", JsonDocument.Parse(attribute.NameJson).RootElement.GetProperty("es").GetString());
    }

    [Fact]
    public void A_person_data_row_has_its_own_revision_and_a_stale_write_does_not_change_its_values()
    {
        var userId = Guid.CreateVersion7();
        var values = "{\"zone\":\"central\"}";
        var data = BusinessFormPersonData.Create(userId, values);

        Assert.Equal(userId, data.UserId);
        Assert.Equal(1, data.Revision);
        Assert.Equal(values, data.ValuesJson);
        Assert.True(data.Update(1, "{}").IsSuccess);
        Assert.Equal(2, data.Revision);
        Assert.Equal(FormPersonDataErrors.RevisionConflict, data.Update(1, values).Error);
        Assert.Equal("{}", data.ValuesJson);
        Assert.Equal(2, data.Revision);
    }

    [Fact]
    public void Defining_person_data_has_an_explicit_permission_in_the_module_catalog()
    {
        Assert.Equal("formpersondata.manage", BusinessFormsPermissions.PersonDataManage);
        Assert.Equal(1, BusinessFormsPermissions.All.Count(permission => permission == BusinessFormsPermissions.PersonDataManage));
    }

    [Fact]
    public void A_person_attribute_requires_only_the_name_in_its_original_language()
    {
        var english = BusinessFormPersonAttribute.Create("zone", "en", new Dictionary<string, string> { ["en"] = "Zone" },
            BusinessFormPersonAttributeType.Text, null, 0);

        Assert.True(english.IsSuccess);
        Assert.Equal("en", english.Value.OriginalCulture);
        Assert.Equal("{\"en\":\"Zone\"}", english.Value.NameJson);
        Assert.Equal("es", BusinessFormPersonAttribute.Create("region", "es", Names(), BusinessFormPersonAttributeType.Text, null, 1).Value.OriginalCulture);
        Assert.Equal(FormPersonDataErrors.AttributeInvalid, BusinessFormPersonAttribute.Create("zone", "en",
            new Dictionary<string, string> { ["es"] = "Zona" }, BusinessFormPersonAttributeType.Text, null, 0).Error);
        Assert.Equal(FormPersonDataErrors.AttributeInvalid, BusinessFormPersonAttribute.Create("zone", "fr",
            new Dictionary<string, string> { ["en"] = "Zone" }, BusinessFormPersonAttributeType.Text, null, 0).Error);

        var attribute = english.Value;
        Assert.Equal(FormPersonDataErrors.AttributeInvalid,
            attribute.Update(1, new Dictionary<string, string> { ["es"] = "Región" }, BusinessFormPersonAttributeType.Text, null, 0, true).Error);
        Assert.Equal(1, attribute.Revision);
        Assert.True(attribute.Update(1, new Dictionary<string, string> { ["en"] = "Region" }, BusinessFormPersonAttributeType.Text, null, 0, true).IsSuccess);
        Assert.Equal("{\"en\":\"Region\"}", attribute.NameJson);
        Assert.Equal("en", attribute.OriginalCulture);
    }

    private static Dictionary<string, string> Names(string original = "Zona") => new() { ["es"] = original, ["en"] = "Zone" };
}
