using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CM1 to CM6: every folder of the first level is a menu with its icon, its forms and a group per subfolder, each with the active forms
// directly inside it, in alphabetical order. Whoever only opens forms sees what they can open and nothing empty; whoever administers
// forms sees every folder, also the empty ones, every active form and how many catalogs each menu holds.
public sealed class FormMenuServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(P.RecordsRead)]
    [InlineData(P.RecordsCreate)]
    [InlineData(P.ApprovalsDecide)]
    public async Task Whoever_only_opens_forms_sees_the_forms_they_can_open_and_nothing_empty(string permission)
    {
        using var fixture = new FormMenuArrangement(permission);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        Assert.False(menu.CanManage);
        Assert.Equal(["Facturación", "Repartos"], menu.Menus.Select(section => section.Name));
        var deliveries = menu.Menus[1];
        Assert.Equal((fixture.Deliveries.Id, "truck", 1L, 0), (deliveries.FolderId, deliveries.Icon, deliveries.Revision, deliveries.CatalogCount));
        Assert.Equal(["Hoja de ruta"], deliveries.Forms.Select(form => form.Name));
        Assert.Equal(["Norte", "Sur"], deliveries.Groups.Select(group => group.Name));
        Assert.Equal(["Carga en ruta", "Entrega"], deliveries.Groups[0].Forms.Select(form => form.Name));
        Assert.Equal(["Entrega"], deliveries.Groups[1].Forms.Select(form => form.Name));
        Assert.Equal((fixture.North.Id, 1L), (deliveries.Groups[0].FolderId, deliveries.Groups[0].Revision));
        var billing = menu.Menus[0];
        Assert.Equal(("receipt", 0), (billing.Icon, billing.CatalogCount));
        Assert.Empty(billing.Forms);
        Assert.Equal(["Notas"], billing.Groups.Select(group => group.Name));
        Assert.Equal(["Factura", "Nota de crédito"], billing.Groups[0].Forms.Select(form => form.Name));
    }

    [Theory]
    [InlineData(P.Manage)]
    [InlineData(P.Manage, P.RecordsRead)]
    public async Task Whoever_administers_forms_sees_every_folder_every_active_form_and_the_catalogs_of_each_menu(params string[] permissions)
    {
        using var fixture = new FormMenuArrangement(permissions);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        Assert.True(menu.CanManage);
        Assert.Equal(["Compras", "Facturación", "Repartos"], menu.Menus.Select(section => section.Name));
        var purchases = menu.Menus[0];
        Assert.Equal(("package", 0), (purchases.Icon, purchases.CatalogCount));
        Assert.Empty(purchases.Forms);
        Assert.Empty(purchases.Groups);
        var billing = menu.Menus[1];
        Assert.Equal(["Factura"], billing.Forms.Select(form => form.Name));
        Assert.Equal(["Notas"], billing.Groups.Select(group => group.Name));
        var deliveries = menu.Menus[2];
        Assert.Equal(2, deliveries.CatalogCount);
        Assert.Equal(["Hoja de ruta"], deliveries.Forms.Select(form => form.Name));
        Assert.Equal(["Norte", "Sur", "Vacío"], deliveries.Groups.Select(group => group.Name));
        Assert.Empty(deliveries.Groups[2].Forms);
    }

    [Fact]
    public async Task Whoever_cannot_open_or_administer_forms_gets_an_empty_menu()
    {
        using var fixture = new FormMenuArrangement(P.Read, P.CatalogsRead, P.CatalogsManage);
        var menu = (await fixture.Fixture.Menu.GetAsync(Ct)).Value;

        Assert.False(menu.CanManage);
        Assert.Empty(menu.Menus);
    }

    [Fact]
    public async Task An_inactive_account_cannot_read_the_menu()
    {
        using var fixture = new FormMenuArrangement(P.Manage);
        await fixture.Fixture.Users.SetActiveAsync(fixture.Fixture.Actor, false, Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, (await fixture.Fixture.Menu.GetAsync(Ct)).Error);
    }

    // Repartos (truck) holds «Hoja de ruta» and a catalog; its group Norte holds «Entrega» and «Carga en ruta», Sur holds «Entrega»
    // too, and Vacío only a draft and two catalogs (one of them also in Repartos). Facturación (receipt) holds «Factura», which only
    // whoever administers forms sees, and its group Notas holds «Nota de crédito» and «Factura» again. Compras (package) is empty.
    private sealed class FormMenuArrangement : IDisposable
    {
        public FormFolderFixture Fixture { get; }
        public BusinessFormFolder Deliveries { get; }
        public BusinessFormFolder North { get; }

        public FormMenuArrangement(params string[] permissions)
        {
            Fixture = new FormFolderFixture(permissions);
            Deliveries = Fixture.Arrange("Repartos", icon: "truck");
            North = Fixture.Arrange("Norte", Deliveries);
            var south = Fixture.Arrange("Sur", Deliveries);
            var empty = Fixture.Arrange("Vacío", Deliveries);
            var billing = Fixture.Arrange("Facturación", icon: "receipt");
            var notes = Fixture.Arrange("Notas", billing);
            Fixture.Arrange("Compras", icon: "package");
            var delivery = Form("Entrega");
            var invoice = Form("Factura");
            var shared = Guid.NewGuid();
            Fixture.File(Deliveries, FormFolderItemKind.Form, Form("Hoja de ruta"));
            Fixture.File(Deliveries, FormFolderItemKind.Catalog, shared);
            Fixture.File(North, FormFolderItemKind.Form, delivery);
            Fixture.File(North, FormFolderItemKind.Form, Form("Carga en ruta"));
            Fixture.File(south, FormFolderItemKind.Form, delivery);
            Fixture.File(empty, FormFolderItemKind.Form, Guid.NewGuid());
            Fixture.File(empty, FormFolderItemKind.Catalog, shared);
            Fixture.File(empty, FormFolderItemKind.Catalog, Guid.NewGuid());
            Fixture.File(billing, FormFolderItemKind.Form, Form("Factura", openable: false));
            Fixture.File(notes, FormFolderItemKind.Form, Form("Nota de crédito"));
            Fixture.File(notes, FormFolderItemKind.Form, invoice);
        }

        private Guid Form(string name, bool openable = true)
        {
            var id = Guid.NewGuid();
            Fixture.ActiveForms[id] = name;
            if (openable) Fixture.Openable.Add(id);
            return id;
        }

        public void Dispose() => Fixture.Dispose();
    }
}
