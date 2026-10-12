using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormsPermissionsTests
{
    [Fact]
    public void Catalog_permissions_follow_area_action_and_are_in_the_module_catalog()
    {
        Assert.Contains(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.All);
        Assert.Contains(BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.All);
        Assert.Matches("^[a-z]+\\.[a-z]+$", BusinessFormsPermissions.CatalogsRead);
        Assert.Matches("^[a-z]+\\.[a-z]+$", BusinessFormsPermissions.CatalogsManage);
    }
}
