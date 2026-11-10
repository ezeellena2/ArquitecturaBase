using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// CP1-A, CP3-A and CP5-A: a folder only orders. Its name is unique per parent without telling case apart, it goes up to five
// levels without cycles, and its place in the menu takes an icon only as its own menu.
public sealed class BusinessFormFolderTests
{
    [Fact]
    public void A_folder_keeps_its_trimmed_name_and_compares_it_without_case_or_repeated_spaces()
    {
        var folder = BusinessFormFolder.Create(null, "  Siembra   y cosecha  ").Value;

        Assert.Equal("Siembra   y cosecha", folder.Name);
        Assert.Equal("SIEMBRA Y COSECHA", folder.NormalizedName);
        Assert.Equal(BusinessFormFolder.NormalizeName("siembra y COSECHA"), folder.NormalizedName);
        Assert.Null(folder.ParentId);
        Assert.Null(folder.SystemKey);
        Assert.Equal(FormFolderMenuPlacement.None, folder.MenuPlacement);
        Assert.Null(folder.MenuIcon);
        Assert.Equal(1, folder.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0name")]
    public void An_empty_or_unstorable_name_is_rejected(string? name) =>
        Assert.Equal(FormFolderErrors.NameInvalid, BusinessFormFolder.Create(null, name).Error);

    [Fact]
    public void A_name_longer_than_eighty_characters_is_rejected()
    {
        Assert.True(BusinessFormFolder.Create(null, new string('a', BusinessFormFolder.NameMaxLength)).IsSuccess);
        Assert.Equal(FormFolderErrors.NameInvalid, BusinessFormFolder.Create(null, new string('a', BusinessFormFolder.NameMaxLength + 1)).Error);
    }

    [Fact]
    public void Renaming_or_moving_advances_the_revision_and_an_unchanged_save_does_not()
    {
        var parent = Guid.NewGuid();
        var folder = BusinessFormFolder.Create(null, "Facturación").Value;

        Assert.True(folder.Update(1, "Facturación", null).IsSuccess);
        Assert.Equal(1, folder.Revision);
        Assert.True(folder.Update(1, "Facturas", null).IsSuccess);
        Assert.Equal(("Facturas", 2L), (folder.Name, folder.Revision));
        Assert.True(folder.Update(2, "Facturas", parent).IsSuccess);
        Assert.Equal((parent, 3L), (folder.ParentId, folder.Revision));
    }

    [Fact]
    public void A_stale_revision_changes_nothing()
    {
        var folder = BusinessFormFolder.Create(null, "Facturación").Value;

        Assert.Equal(FormFolderErrors.RevisionConflict, folder.Update(2, "Facturas", null).Error);
        Assert.Equal(FormFolderErrors.RevisionConflict, folder.SetMenu(2, FormFolderMenuPlacement.Own, null).Error);
        Assert.Equal(FormFolderErrors.RevisionConflict, folder.CheckRevision(2).Error);
        Assert.Equal(("Facturación", 1L, FormFolderMenuPlacement.None), (folder.Name, folder.Revision, folder.MenuPlacement));
    }

    [Fact]
    public void An_own_menu_takes_the_folder_icon_by_default_or_one_of_the_list()
    {
        var folder = BusinessFormFolder.Create(null, "Repartos").Value;

        Assert.True(folder.SetMenu(1, FormFolderMenuPlacement.Own, null).IsSuccess);
        Assert.Equal((FormFolderMenuPlacement.Own, "folder", 2L), (folder.MenuPlacement, folder.MenuIcon, folder.Revision));
        Assert.True(folder.SetMenu(2, FormFolderMenuPlacement.Own, "truck").IsSuccess);
        Assert.Equal("truck", folder.MenuIcon);
        Assert.True(folder.SetMenu(3, FormFolderMenuPlacement.Own, "truck").IsSuccess);
        Assert.Equal(3, folder.Revision);
        Assert.True(folder.SetMenu(3, FormFolderMenuPlacement.Administration, null).IsSuccess);
        Assert.Equal((FormFolderMenuPlacement.Administration, (string?)null, 4L), (folder.MenuPlacement, folder.MenuIcon, folder.Revision));
        Assert.Equal(12, BusinessFormFolder.MenuIcons.Count);
    }

    [Theory]
    [InlineData(FormFolderMenuPlacement.Own, "rocket")]
    [InlineData(FormFolderMenuPlacement.Forms, "truck")]
    [InlineData(FormFolderMenuPlacement.None, "folder")]
    public void An_icon_outside_the_list_or_without_an_own_menu_is_rejected(FormFolderMenuPlacement placement, string icon)
    {
        var folder = BusinessFormFolder.Create(null, "Repartos").Value;

        Assert.Equal(FormFolderErrors.IconInvalid, folder.SetMenu(1, placement, icon).Error);
        Assert.Equal((FormFolderMenuPlacement.None, 1L), (folder.MenuPlacement, folder.Revision));
    }
}
