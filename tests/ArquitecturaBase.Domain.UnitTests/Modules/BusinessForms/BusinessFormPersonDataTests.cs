using System.Text.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormPersonDataTests
{
    [Fact]
    public void A_person_attribute_requires_a_source_only_when_its_type_is_catalog()
    {
        Assert.True(BusinessFormPersonAttribute.Create("zone", Names(), BusinessFormPersonAttributeType.Catalog, null, 0).IsFailure);
        Assert.True(BusinessFormPersonAttribute.Create("zone", Names(), BusinessFormPersonAttributeType.Text, Guid.CreateVersion7(), 0).IsFailure);
        Assert.True(BusinessFormPersonAttribute.Create("zone", Names(), BusinessFormPersonAttributeType.Text, null, 0).IsSuccess);
        Assert.True(BusinessFormPersonAttribute.Create("zone", Names(), BusinessFormPersonAttributeType.Catalog, Guid.CreateVersion7(), 0).IsSuccess);
    }

    [Fact]
    public void Updating_an_attribute_keeps_its_key_and_identity_and_preserves_its_name_when_inactive()
    {
        var names = Names();
        var attribute = BusinessFormPersonAttribute.Create("zone", names, BusinessFormPersonAttributeType.Catalog, Guid.CreateVersion7(), 0).Value;
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
        var attribute = BusinessFormPersonAttribute.Create("zone", Names(), BusinessFormPersonAttributeType.Text, null, 0).Value;
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

    private static Dictionary<string, string> Names(string original = "Zona") => new() { ["es"] = original, ["en"] = "Zone" };
}
