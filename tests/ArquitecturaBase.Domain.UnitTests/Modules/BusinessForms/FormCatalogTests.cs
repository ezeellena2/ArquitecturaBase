using System.Text.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogTests
{
    [Fact]
    public void Manual_catalog_starts_active_with_revision_one_and_a_server_key()
    {
        var catalog = BusinessFormCatalog.CreateManual(Texts("Personas", "People"), Texts("Supervisores disponibles", "Available supervisors"));

        Assert.Equal(BusinessFormCatalogKind.Manual, catalog.Kind);
        Assert.True(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.NotEqual(Guid.Empty, catalog.Id);
        Assert.Equal(catalog.Id.ToString("N"), catalog.Key);
        Assert.Equal("People", Read(catalog.NameJson, "en"));
        Assert.Equal("Available supervisors", Read(catalog.DescriptionJson, "en"));
    }

    [Fact]
    public void Manual_items_are_saved_under_one_parent_revision()
    {
        var catalog = CreateCatalog();
        var added = catalog.AddManualItem("supervisor", "Supervisora", null, null, null, 0);

        Assert.True(added.IsSuccess);
        Assert.Equal(1, catalog.Revision);

        var secondAdded = catalog.AddManualItem("operador", "Operador", null, null, null, 1);

        Assert.True(secondAdded.IsSuccess);
        Assert.Equal(1, catalog.Revision);

        var saved = catalog.UpdateManual(1, Texts("Personas", "People"), Texts("Directorio actualizado", "Updated directory"));

        Assert.True(saved.IsSuccess);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal("Updated directory", Read(catalog.DescriptionJson, "en"));

        var stale = catalog.UpdateManual(1, Texts("Otra", "Another"), Texts("Otro", "Other"));

        Assert.True(stale.IsFailure);
        Assert.Equal(FormCatalogErrors.RevisionConflict, stale.Error);
        Assert.Equal(2, catalog.Revision);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public void Missing_english_item_text_uses_the_spanish_text(string culture)
    {
        var catalog = CreateCatalog();
        var item = catalog.AddManualItem("supervisor", "Supervisora", null, "Equipo", null, 0).Value;

        Assert.Equal("Supervisora", item.TextFor(culture));
        Assert.Equal("Equipo", item.DetailFor(culture));
    }

    [Fact]
    public void Item_code_can_change_without_changing_item_identity()
    {
        var catalog = CreateCatalog();
        var item = catalog.AddManualItem("supervisor", "Supervisora", null, "Equipo", null, 0).Value;
        var itemId = item.Id;

        Assert.True(catalog.UpdateManual(1, Texts("Personas", "People"), Texts("Directorio", "Directory")).IsSuccess);
        var result = catalog.UpdateManualItem(item, "responsable", "Responsable", "Supervisor", "Equipo", "Team", 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(itemId, item.Id);
        Assert.Equal("responsable", item.Code);
        Assert.Equal("Supervisor", item.TextFor("en"));
        Assert.Equal(1, item.Position);
        Assert.Equal("Team", item.DetailFor("en"));
        Assert.Equal(2, catalog.Revision);
    }

    [Fact]
    public void Deactivating_a_manual_item_keeps_its_identity_and_requires_the_current_revision()
    {
        var catalog = CreateCatalog();
        var item = catalog.AddManualItem("supervisor", "Supervisora", null, null, null, 0).Value;
        var itemId = item.Id;

        Assert.True(catalog.UpdateManual(1, Texts("Personas", "People"), Texts("Directorio", "Directory")).IsSuccess);
        var deactivated = catalog.SetManualItemActive(item, false);

        Assert.True(deactivated.IsSuccess);
        Assert.False(item.IsActive);
        Assert.Equal(2, catalog.Revision);

        var stale = catalog.UpdateManual(1, Texts("Personas", "People"), Texts("Directorio", "Directory"));

        Assert.False(stale.IsSuccess);
        Assert.Equal(FormCatalogErrors.RevisionConflict, stale.Error);
        Assert.Equal(itemId, item.Id);
        Assert.False(item.IsActive);
    }

    [Fact]
    public void Updating_catalog_text_keeps_both_languages_and_advances_revision_once()
    {
        var catalog = BusinessFormCatalog.CreateManual(Texts("Clientes", "Customers"), Texts("A quién se visita", "Who to visit"));

        var result = catalog.UpdateManual(1, Texts("Proveedores", "Suppliers"), Texts("A quién se compra", "Who we buy from"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal("Suppliers", Read(catalog.NameJson, "en"));
        Assert.Equal("Who we buy from", Read(catalog.DescriptionJson, "en"));
    }

    private static BusinessFormCatalog CreateCatalog() =>
        BusinessFormCatalog.CreateManual(Texts("Personas", "People"), Texts("Directorio", "Directory"));

    private static Dictionary<string, string> Texts(string spanish, string english) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["es"] = spanish, ["en"] = english };

    private static string Read(string json, string culture) =>
        JsonDocument.Parse(json).RootElement.GetProperty(culture).GetString()!;
}
