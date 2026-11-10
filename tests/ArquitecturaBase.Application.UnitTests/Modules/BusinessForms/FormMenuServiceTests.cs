using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CP5-A: an own menu has its forms and a group per subfolder, with what is further below in its group; a folder in a panel is one
// group with everything below it. Only active forms, in alphabetical order, and nothing empty or for someone who cannot open forms.
public sealed class FormMenuServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_own_menu_has_a_group_per_subfolder_and_never_more_than_two_levels()
    {
        using var fixture = new FormMenuArrangement(P.RecordsRead);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        var deliveries = Assert.Single(menu.Menus);
        Assert.Equal(("Repartos", "truck"), (deliveries.Name, deliveries.Icon));
        Assert.Equal(["Hoja de ruta"], deliveries.Forms.Select(form => form.Name));
        Assert.Equal(["Norte", "Sur"], deliveries.Groups.Select(group => group.Name));
        Assert.Equal(["Carga en ruta", "Entrega"], deliveries.Groups[0].Forms.Select(form => form.Name));
        Assert.Equal(["Entrega"], deliveries.Groups[1].Forms.Select(form => form.Name));
    }

    [Fact]
    public async Task A_folder_in_a_panel_is_one_group_with_every_form_below_it_in_alphabetical_order()
    {
        using var fixture = new FormMenuArrangement(P.RecordsCreate);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        Assert.Equal(["Compras", "Facturación"], menu.Administration.Select(group => group.Name));
        Assert.Equal(["Factura", "Nota de crédito"], menu.Administration[1].Forms.Select(form => form.Name));
        Assert.Equal(["Orden de compra"], menu.Administration[0].Forms.Select(form => form.Name));
        Assert.Empty(menu.Forms);
    }

    [Fact]
    public async Task Whoever_cannot_open_forms_gets_an_empty_menu()
    {
        using var fixture = new FormMenuArrangement(P.Read, P.Manage, P.CatalogsManage);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        Assert.Empty(menu.Menus);
        Assert.Empty(menu.Forms);
        Assert.Empty(menu.Administration);
    }

    [Fact]
    public async Task An_inactive_account_cannot_read_the_menu()
    {
        using var fixture = new FormMenuArrangement(P.RecordsRead);
        await fixture.Fixture.Users.SetActiveAsync(fixture.Fixture.Actor, false, Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Fixture.Menu.GetAsync(Ct)).Error);
    }

    // Repartos (own menu, truck) holds «Hoja de ruta»; Norte holds «Entrega» and, below it, Tarde holds «Carga en ruta»; Sur holds
    // «Entrega» too; Vacío holds only a draft. Facturación and Compras go inside Administración; Facturación holds «Factura» and,
    // below, Notas holds «Nota de crédito» and «Factura» again. Archivo is outside the menu.
    private sealed class FormMenuArrangement : IDisposable
    {
        public FormFolderFixture Fixture { get; }

        public FormMenuArrangement(params string[] permissions)
        {
            Fixture = new FormFolderFixture(permissions);
            var deliveries = Place(Fixture.Arrange("Repartos"), FormFolderMenuPlacement.Own, "truck");
            var north = Fixture.Arrange("Norte", deliveries);
            var late = Fixture.Arrange("Tarde", north);
            var south = Fixture.Arrange("Sur", deliveries);
            var empty = Fixture.Arrange("Vacío", deliveries);
            var billing = Place(Fixture.Arrange("Facturación"), FormFolderMenuPlacement.Administration, null);
            var notes = Fixture.Arrange("Notas", billing);
            var purchases = Place(Fixture.Arrange("Compras"), FormFolderMenuPlacement.Administration, null);
            var archive = Fixture.Arrange("Archivo");
            var route = Form("Hoja de ruta");
            var delivery = Form("Entrega");
            var invoice = Form("Factura");
            Fixture.File(deliveries, FormFolderItemKind.Form, route);
            Fixture.File(north, FormFolderItemKind.Form, delivery);
            Fixture.File(late, FormFolderItemKind.Form, Form("Carga en ruta"));
            Fixture.File(south, FormFolderItemKind.Form, delivery);
            Fixture.File(empty, FormFolderItemKind.Form, Guid.NewGuid());
            Fixture.File(empty, FormFolderItemKind.Catalog, Guid.NewGuid());
            Fixture.File(billing, FormFolderItemKind.Form, invoice);
            Fixture.File(notes, FormFolderItemKind.Form, Form("Nota de crédito"));
            Fixture.File(notes, FormFolderItemKind.Form, invoice);
            Fixture.File(purchases, FormFolderItemKind.Form, Form("Orden de compra"));
            Fixture.File(archive, FormFolderItemKind.Form, Form("Remito viejo"));
        }

        private static BusinessFormFolder Place(BusinessFormFolder folder, FormFolderMenuPlacement placement, string? icon)
        {
            Assert.True(folder.SetMenu(folder.Revision, placement, icon).IsSuccess);
            return folder;
        }

        private Guid Form(string name)
        {
            var id = Guid.NewGuid();
            Fixture.ActiveForms[id] = name;
            return id;
        }

        public void Dispose() => Fixture.Dispose();
    }
}
