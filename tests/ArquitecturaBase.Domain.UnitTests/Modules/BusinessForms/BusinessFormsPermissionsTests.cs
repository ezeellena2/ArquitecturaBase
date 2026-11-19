using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormsPermissionsTests
{
    [Fact]
    public void Approval_decision_is_an_explicit_permission_in_the_module_catalog()
    {
        Assert.Contains("formapprovals.decide", BusinessFormsPermissions.All);
        Assert.DoesNotContain("formapprovals.manage", BusinessFormsPermissions.All);
    }

    [Fact]
    public void Correcting_and_voiding_responses_is_its_own_permission_next_to_the_other_response_permissions()
    {
        Assert.Equal("formrecords.amend", BusinessFormsPermissions.RecordsAmend);
        var all = BusinessFormsPermissions.All.ToList();
        Assert.Equal(all.IndexOf(BusinessFormsPermissions.RecordsExport) + 1, all.IndexOf(BusinessFormsPermissions.RecordsAmend));
        Assert.Single(all, BusinessFormsPermissions.RecordsAmend);
    }

    /// <summary>Lo que no sirve sin el «Ver» de su área lo incluye (2026-11-19); nada incluye a otra área.</summary>
    [Fact]
    public void What_does_not_work_without_seeing_its_area_includes_seeing_it()
    {
        Assert.Equal(
            [
                (BusinessFormsPermissions.Manage, BusinessFormsPermissions.Read),
                (BusinessFormsPermissions.Publish, BusinessFormsPermissions.Read),
                (BusinessFormsPermissions.RecordsAmend, BusinessFormsPermissions.RecordsRead),
                (BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.CatalogsRead),
                (BusinessFormsPermissions.DocumentsReview, BusinessFormsPermissions.DocumentsRead),
            ],
            BusinessFormsPermissions.Includes.SelectMany(pair => pair.Value.Select(included => (pair.Key, included))));
    }

    [Fact]
    public void Catalog_permissions_follow_area_action_and_are_in_the_module_catalog()
    {
        Assert.Contains(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.All);
        Assert.Contains(BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.All);
        Assert.Matches("^[a-z]+\\.[a-z]+$", BusinessFormsPermissions.CatalogsRead);
        Assert.Matches("^[a-z]+\\.[a-z]+$", BusinessFormsPermissions.CatalogsManage);
    }
}
