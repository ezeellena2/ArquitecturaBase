using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// CP1-A, CP3-A and CM1 to CM6: a folder only orders. Its name is unique per parent without telling case apart, and a folder of the
// first level is a menu with one of the twelve icons while a subfolder has none.
public sealed class BusinessFormFolderTests
{
    [Fact]
    public void A_folder_keeps_its_trimmed_name_and_compares_it_without_case_or_repeated_spaces()
    {
        var folder = BusinessFormFolder.Create(null, "  Siembra   y cosecha  ", "leaf").Value;

        Assert.Equal("Siembra   y cosecha", folder.Name);
        Assert.Equal("SIEMBRA Y COSECHA", folder.NormalizedName);
        Assert.Equal(BusinessFormFolder.NormalizeName("siembra y COSECHA"), folder.NormalizedName);
        Assert.Null(folder.ParentId);
        Assert.Null(folder.SystemKey);
        Assert.Equal("leaf", folder.MenuIcon);
        Assert.Equal(1, folder.Revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0name")]
    public void An_empty_or_unstorable_name_is_rejected(string? name) =>
        Assert.Equal(FormFolderErrors.NameInvalid, BusinessFormFolder.Create(null, name, "folder").Error);

    [Fact]
    public void A_name_longer_than_eighty_characters_is_rejected()
    {
        Assert.True(BusinessFormFolder.Create(null, new string('a', BusinessFormFolder.NameMaxLength), "folder").IsSuccess);
        Assert.Equal(FormFolderErrors.NameInvalid, BusinessFormFolder.Create(null, new string('a', BusinessFormFolder.NameMaxLength + 1), "folder").Error);
    }

    [Fact]
    public void A_folder_of_the_first_level_takes_one_of_the_twelve_icons_and_a_subfolder_none()
    {
        var parent = Guid.NewGuid();

        Assert.Equal("truck", BusinessFormFolder.Create(null, "Repartos", "truck").Value.MenuIcon);
        Assert.Null(BusinessFormFolder.Create(parent, "Zona norte", null).Value.MenuIcon);
        Assert.Equal(FormFolderErrors.IconInvalid, BusinessFormFolder.Create(null, "Repartos", null).Error);
        Assert.Equal(FormFolderErrors.IconInvalid, BusinessFormFolder.Create(null, "Repartos", "rocket").Error);
        Assert.Equal(FormFolderErrors.IconInvalid, BusinessFormFolder.Create(parent, "Zona norte", "truck").Error);
        Assert.Equal(12, BusinessFormFolder.MenuIcons.Count);
        Assert.Equal(["folder", "truck", "package", "receipt", "users", "calendar", "clipboard", "chart", "wrench", "store", "leaf", "file"],
            BusinessFormFolder.MenuIcons);
    }

    [Fact]
    public void Renaming_moving_or_changing_the_icon_advances_the_revision_and_an_unchanged_save_does_not()
    {
        var parent = Guid.NewGuid();
        var folder = BusinessFormFolder.Create(null, "Facturación", "receipt").Value;

        Assert.True(folder.Update(1, "Facturación", null, "receipt").IsSuccess);
        Assert.Equal(1, folder.Revision);
        Assert.True(folder.Update(1, "Facturas", null, "receipt").IsSuccess);
        Assert.Equal(("Facturas", 2L), (folder.Name, folder.Revision));
        Assert.True(folder.Update(2, "Facturas", null, "chart").IsSuccess);
        Assert.Equal(("chart", 3L), (folder.MenuIcon, folder.Revision));
        Assert.True(folder.Update(3, "Facturas", parent, null).IsSuccess);
        Assert.Equal((parent, (string?)null, 4L), (folder.ParentId, folder.MenuIcon, folder.Revision));
        Assert.True(folder.Update(4, "Facturas", null, "store").IsSuccess);
        Assert.Equal(((Guid?)null, "store", 5L), (folder.ParentId, folder.MenuIcon, folder.Revision));
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "rocket")]
    [InlineData(true, "truck")]
    public void A_move_with_an_icon_that_does_not_fit_its_level_changes_nothing(bool intoAnother, string? icon)
    {
        var folder = BusinessFormFolder.Create(null, "Repartos", "truck").Value;

        Assert.Equal(FormFolderErrors.IconInvalid, folder.Update(1, "Rutas", intoAnother ? Guid.NewGuid() : null, icon).Error);
        Assert.Equal(("Repartos", (Guid?)null, "truck", 1L), (folder.Name, folder.ParentId, folder.MenuIcon, folder.Revision));
    }

    [Fact]
    public void A_stale_revision_changes_nothing()
    {
        var folder = BusinessFormFolder.Create(null, "Facturación", "receipt").Value;

        Assert.Equal(FormFolderErrors.RevisionConflict, folder.Update(2, "Facturas", null, "receipt").Error);
        Assert.Equal(FormFolderErrors.RevisionConflict, folder.CheckRevision(2).Error);
        Assert.Equal(("Facturación", 1L), (folder.Name, folder.Revision));
    }

    [Fact]
    public void The_first_free_icon_is_the_first_of_the_list_no_menu_uses_and_the_folder_once_all_are_taken()
    {
        var folder = BusinessFormFolder.Create(null, "Repartos", "folder").Value;
        var truck = BusinessFormFolder.Create(null, "Camiones", "truck").Value;
        var receipt = BusinessFormFolder.Create(null, "Facturación", "receipt").Value;

        Assert.Equal("folder", BusinessFormFolder.FirstFreeIcon([]));
        Assert.Equal("package", BusinessFormFolder.FirstFreeIcon([folder, truck, receipt]));
        Assert.Equal("folder", BusinessFormFolder.FirstFreeIcon([truck, receipt]));
        var all = BusinessFormFolder.MenuIcons.Select((icon, index) => BusinessFormFolder.Create(null, "Menú " + index, icon).Value).ToArray();
        Assert.Equal("folder", BusinessFormFolder.FirstFreeIcon(all));
    }

    // Until the second deployment of FoldersAsMenus the previous version may still write a menu without an icon, a subfolder with one
    // and folders below the second level: they are read and written as the two levels they show.
    [Fact]
    public void The_two_levels_leave_out_what_is_below_the_second_and_each_folder_shows_the_icon_of_its_level()
    {
        var menu = WithStoredIcon(BusinessFormFolder.Create(null, "Archivo", "receipt").Value, null);
        var group = WithStoredIcon(BusinessFormFolder.Create(menu.Id, "Viejos", null).Value, "truck");
        var hidden = BusinessFormFolder.Create(group.Id, "Muy viejos", null).Value;
        var below = BusinessFormFolder.Create(hidden.Id, "Antiguos", null).Value;
        var other = BusinessFormFolder.Create(null, "Repartos", "leaf").Value;

        Assert.Equal([menu, group, other], BusinessFormFolder.TwoLevels([menu, group, hidden, below, other]));
        Assert.Equal(["folder", null, null, null, "leaf"], new[] { menu, group, hidden, below, other }.Select(folder => folder.ShownIcon));
        // The menu without an icon takes the folder and the icon the subfolder hides is free.
        Assert.Equal("truck", BusinessFormFolder.FirstFreeIcon([menu, group, other]));
    }

    [Fact]
    public void Saving_what_a_folder_shows_changes_nothing_and_any_other_change_stores_the_icon_of_its_level()
    {
        var menu = WithStoredIcon(BusinessFormFolder.Create(null, "Archivo", "receipt").Value, null);
        var group = WithStoredIcon(BusinessFormFolder.Create(menu.Id, "Viejos", null).Value, "truck");

        Assert.True(menu.Update(1, "Archivo", null, "folder").IsSuccess);
        Assert.True(group.Update(1, "Viejos", menu.Id, null).IsSuccess);
        Assert.Equal((null, 1L, "truck", 1L), (menu.MenuIcon, menu.Revision, group.MenuIcon, group.Revision));

        Assert.True(menu.Update(1, "Archivo viejo", null, "folder").IsSuccess);
        Assert.True(group.Update(1, "Muy viejos", menu.Id, null).IsSuccess);
        Assert.Equal(("folder", 2L, (string?)null, 2L), (menu.MenuIcon, menu.Revision, group.MenuIcon, group.Revision));
    }

    // What only the previous version writes: this one never leaves a menu without an icon or a subfolder with one.
    private static BusinessFormFolder WithStoredIcon(BusinessFormFolder folder, string? icon)
    {
        typeof(BusinessFormFolder).GetProperty(nameof(BusinessFormFolder.MenuIcon))!.SetValue(folder, icon);
        return folder;
    }
}
