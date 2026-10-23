using System.Text.Json;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogTests
{
    [Fact]
    public void Manual_catalog_starts_active_with_revision_one_and_a_server_key()
    {
        var catalog = BusinessFormCatalog.CreateManual("es", Texts("Personas", "People"), Texts("Supervisores disponibles", "Available supervisors"));

        Assert.Equal(BusinessFormCatalogKind.Manual, catalog.Kind);
        Assert.True(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.NotEqual(Guid.Empty, catalog.Id);
        Assert.Equal(catalog.Id.ToString("N"), catalog.Key);
        Assert.Equal("People", Read(catalog.NameJson, "en"));
        Assert.Equal("Available supervisors", Read(catalog.DescriptionJson, "en"));
        Assert.Null(catalog.FiltersJson);
    }

    [Fact]
    public void Filters_catalog_stores_its_definition_and_updates_it_in_one_revision()
    {
        const string definition = "{\"textColumn\":\"name\",\"detailColumn\":\"role\"}";
        const string updatedDefinition = "{\"textColumn\":\"role\",\"detailColumn\":null}";
        var catalog = BusinessFormCatalog.CreateFilters("es", Texts("Supervisores", "Supervisors"),
            Texts("Usuarios activos", "Active users"), definition);

        Assert.Equal(BusinessFormCatalogKind.Filters, catalog.Kind);
        Assert.True(catalog.IsActive);
        Assert.Equal(1, catalog.Revision);
        Assert.Equal(definition, catalog.FiltersJson);

        var updated = catalog.UpdateFilters(1, Texts("Supervisores", "Supervisors"),
            Texts("Personas activas", "Active people"), updatedDefinition);

        Assert.True(updated.IsSuccess);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(updatedDefinition, catalog.FiltersJson);
        Assert.Equal("Active people", Read(catalog.DescriptionJson, "en"));

        var stale = catalog.UpdateFilters(1, Texts("No guardar", "Do not save"),
            Texts("No guardar", "Do not save"), definition);

        Assert.True(stale.IsFailure);
        Assert.Equal(FormCatalogErrors.RevisionConflict, stale.Error);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal(updatedDefinition, catalog.FiltersJson);
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
        var catalog = BusinessFormCatalog.CreateManual("es", Texts("Clientes", "Customers"), Texts("A quién se visita", "Who to visit"));

        var result = catalog.UpdateManual(1, Texts("Proveedores", "Suppliers"), Texts("A quién se compra", "Who we buy from"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, catalog.Revision);
        Assert.Equal("Suppliers", Read(catalog.NameJson, "en"));
        Assert.Equal("Who we buy from", Read(catalog.DescriptionJson, "en"));
    }

    [Fact]
    public void An_English_original_catalog_keeps_its_original_culture_and_requires_only_its_English_name()
    {
        var manual = BusinessFormCatalog.CreateManual("en", Only("en", "People"), new Dictionary<string, string>());
        var filters = BusinessFormCatalog.CreateFilters("en", Only("en", "Supervisors"), Only("en", "Active users"), "{}");
        var proposal = BusinessFormCatalog.CreateProposedFilters("en", Only("en", "Leads"), Only("en", "Team leads"), "{}",
            Guid.CreateVersion7(), "{\"originalCulture\":\"en\"}");

        Assert.Equal(["en", "en", "en"], new[] { manual.OriginalCulture, filters.OriginalCulture, proposal.OriginalCulture });
        Assert.Equal("{\"en\":\"People\"}", manual.NameJson);
        Assert.Equal("{}", manual.DescriptionJson);
        Assert.Throws<ArgumentException>(() => BusinessFormCatalog.CreateManual("en", Only("es", "Personas"), new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => BusinessFormCatalog.CreateFilters("en", Texts("Supervisores", "  "), Only("en", "Users"), "{}"));
        Assert.Throws<ArgumentException>(() => BusinessFormCatalog.CreateManual("fr", Only("en", "People"), new Dictionary<string, string>()));

        Assert.Equal(FormCatalogErrors.ContentInvalid, manual.UpdateManual(1, Only("es", "Personas"), new Dictionary<string, string>()).Error);
        Assert.Equal(FormCatalogErrors.ContentInvalid, filters.UpdateFilters(1, Only("es", "Supervisores"), Only("en", "Users"), "{}").Error);
        Assert.Equal(FormCatalogErrors.ContentInvalid,
            proposal.ApproveProposal(1, Guid.CreateVersion7(), new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc), Only("es", "Líderes"), Only("en", "Leads"), "{}").Error);
        Assert.Equal([1L, 1L, 1L], new[] { manual.Revision, filters.Revision, proposal.Revision });

        Assert.True(manual.UpdateManual(1, Texts(" ", "Staff"), Texts("", "Directory")).IsSuccess);
        Assert.Equal("{\"en\":\"Staff\"}", manual.NameJson);
        Assert.Equal("{\"en\":\"Directory\"}", manual.DescriptionJson);
        Assert.Equal("en", manual.OriginalCulture);
    }

    private static Dictionary<string, string> Only(string culture, string text) => new(StringComparer.Ordinal) { [culture] = text };

    private static BusinessFormCatalog CreateCatalog() =>
        BusinessFormCatalog.CreateManual("es", Texts("Personas", "People"), Texts("Directorio", "Directory"));

    private static Dictionary<string, string> Texts(string spanish, string english) =>
        new Dictionary<string, string>(StringComparer.Ordinal) { ["es"] = spanish, ["en"] = english };

    private static string Read(string json, string culture) =>
        JsonDocument.Parse(json).RootElement.GetProperty(culture).GetString()!;
}
