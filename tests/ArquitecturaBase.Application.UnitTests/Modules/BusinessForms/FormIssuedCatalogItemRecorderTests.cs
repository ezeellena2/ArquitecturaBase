using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// The customer an issuer completed from the CUIT written in the response joins the catalog its standard brings, once.
public sealed class FormIssuedCatalogItemRecorderTests
{
    private const string Key = "test.customers";

    [Fact]
    public async Task An_issued_party_joins_its_catalog_as_an_active_option_with_its_fields_under_the_locks_and_a_new_revision()
    {
        var scene = new Scene();
        var revision = scene.Catalog.Revision;

        await scene.Recorder().AddAsync(Party(), CancellationToken.None);

        var added = Assert.Single(scene.Repository.Items);
        Assert.Equal(("30500010912", "Agro del Sur S.A.", true, 1), (added.Code, added.TextEs, added.IsActive, added.Position));
        Assert.Null(added.DetailEs);
        var values = JsonDocument.Parse(added.FieldValuesJson).RootElement;
        Assert.Equal("30500010912", values.GetProperty(scene.TaxId.Id.ToString("D")).GetString());
        Assert.Equal("Ruta 8 km 225", values.GetProperty(scene.Address.Id.ToString("D")).GetString());
        Assert.Equal(scene.Monotributo.ToString("D"), values.GetProperty(scene.Condition.Id.ToString("D")).GetString());
        Assert.Equal(revision + 1, scene.Catalog.Revision);
        Assert.Equal(new[] { scene.Catalog.Id, scene.Conditions.Id }.Order().ToList(), scene.Repository.Locks);
    }

    [Fact]
    public async Task A_party_already_in_the_catalog_by_its_code_or_its_cuit_is_not_added_again()
    {
        var byCode = new Scene();
        await byCode.Recorder().AddAsync(Party(), CancellationToken.None);
        await byCode.Recorder().AddAsync(Party(), CancellationToken.None);
        Assert.Single(byCode.Repository.Items);

        var byCuit = new Scene();
        var written = byCuit.Catalog.AddManualItem("agro", "Agro", null, null, null, 1).Value;
        Assert.True(byCuit.Catalog.SetManualItemFields(written, JsonSerializer.Serialize(new Dictionary<string, string>
            { [byCuit.TaxId.Id.ToString("D")] = "30-50001091-2" }, JsonSerializerOptions.Web)).IsSuccess);
        byCuit.Repository.Items.Add(written);
        var revision = byCuit.Catalog.Revision;

        await byCuit.Recorder().AddAsync(Party(), CancellationToken.None);

        Assert.Equal(written, Assert.Single(byCuit.Repository.Items));
        Assert.Equal(revision, byCuit.Catalog.Revision);
    }

    [Fact]
    public async Task A_catalog_that_is_missing_or_an_option_that_does_not_fit_leaves_everything_as_it_was()
    {
        var missing = new Scene();
        await missing.Recorder().AddAsync(Party() with { CatalogKey = "test.nowhere" }, CancellationToken.None);
        Assert.Empty(missing.Repository.Items);
        Assert.Empty(missing.Repository.Locks);

        var required = new Scene();
        var email = Field("email", "text");
        Assert.True(required.Catalog.SetFields([required.TaxId, required.Address, required.Condition, email], null).IsSuccess);
        Assert.True(required.Catalog.SetFields([required.TaxId, required.Address, required.Condition, email with { Required = true }], null).IsSuccess);
        required.Reader.Catalogs[required.Catalog.Id] = Row(required.Catalog);
        var revision = required.Catalog.Revision;

        await required.Recorder().AddAsync(Party(), CancellationToken.None);

        Assert.Empty(required.Repository.Items);
        Assert.Equal(revision, required.Catalog.Revision);
    }

    [Fact]
    public async Task A_long_name_keeps_its_first_thirty_characters_as_the_text_and_goes_whole_in_the_detail_and_values_that_do_not_fit_are_dropped()
    {
        var scene = new Scene();
        const string name = "Cooperativa Agrícola Ganadera del Sudoeste Limitada";

        await scene.Recorder().AddAsync(Party() with
        {
            Text = name,
            Fields = new Dictionary<string, string> { ["tax_id"] = "30500010912", ["address"] = new string('x', 201), ["vat_condition"] = "99", ["unknown"] = "x" },
        }, CancellationToken.None);

        var added = Assert.Single(scene.Repository.Items);
        Assert.Equal((name[..30].TrimEnd(), name), (added.TextEs, added.DetailEs));
        var values = JsonDocument.Parse(added.FieldValuesJson).RootElement;
        Assert.Equal(["30500010912"], values.EnumerateObject().Select(value => value.Value.GetString()));
    }

    private static StructuredIssueCatalogItem Party() => new(Key, "30500010912", "Agro del Sur S.A.",
        new Dictionary<string, string> { ["tax_id"] = "30500010912", ["vat_condition"] = "6", ["address"] = "Ruta 8 km 225" }, "tax_id");

    private static BusinessFormCatalogField Field(string key, string type, Guid? source = null, int? maxLength = null) =>
        new(Guid.CreateVersion7(), key, new Dictionary<string, string> { ["es"] = key }, type, source, false, false, null, null, maxLength, "data", 0, true);

    private static FormCatalogStoredRow Row(BusinessFormCatalog catalog) => new(catalog.Id, catalog.Key, catalog.NameJson, catalog.DescriptionJson,
        catalog.OriginalCulture, catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, default, null, null, null, 0, FieldsJson: catalog.FieldsJson);

    // «Clientes» with its CUIT, address and condition (an option of «Condiciones», by its code) and the catalogs found by key.
    private sealed class Scene
    {
        public BusinessFormCatalog Catalog { get; } = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Clientes" }, new Dictionary<string, string>());
        public BusinessFormCatalog Conditions { get; } = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Condiciones" }, new Dictionary<string, string>());
        public BusinessFormCatalogField TaxId { get; } = Field("tax_id", "text", maxLength: 13);
        public BusinessFormCatalogField Address { get; } = Field("address", "text", maxLength: 200);
        public BusinessFormCatalogField Condition { get; }
        public Guid Monotributo { get; }
        public FormCatalogCapacityRepository Repository { get; }
        public FormCatalogFieldReader Reader { get; } = new();
        private readonly Codes codes = new();
        private readonly Library library = new();

        public Scene()
        {
            Catalog.AssignStandardKey(Key);
            Condition = Field("vat_condition", "catalog", Conditions.Id);
            Assert.True(Catalog.SetFields([TaxId, Address, Condition], null).IsSuccess);
            var monotributo = Conditions.AddManualItem("6", "Monotributo", null, null, null, 1).Value;
            Monotributo = monotributo.Id;
            Repository = new FormCatalogCapacityRepository(Catalog);
            library.Catalogs[Key] = new FormLibraryCatalogRow(Catalog.Id, true);
            Reader.Catalogs[Catalog.Id] = Row(Catalog);
            Reader.Catalogs[Conditions.Id] = Row(Conditions);
            Reader.Items[Conditions.Id] = FormCatalogFieldPresentation.ToRows([monotributo]);
            codes.Active[(Conditions.Id, "6")] = monotributo.Id;
        }

        public FormIssuedCatalogItemRecorder Recorder() => new(library, Reader, Repository, codes, new FormCatalogItemRecorder(Repository));
    }

    private sealed class Library : IFormLibraryReader
    {
        public Dictionary<string, FormLibraryCatalogRow> Catalogs { get; } = new(StringComparer.Ordinal);
        public Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, FormLibraryCatalogRow>> ListCatalogsByKeyAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, FormLibraryCatalogRow>>(Catalogs.Where(pair => catalogKeys.Contains(pair.Key)).ToDictionary());
        public Task<IReadOnlyDictionary<string, Guid>> ListActiveItemIdsByCodeAsync(Guid catalogId, IReadOnlyList<string> codes, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class Codes : IFormCatalogItemCodeReader
    {
        public Dictionary<(Guid, string), Guid> Active { get; } = [];
        public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<Guid?> FindActiveAsync(Guid catalogId, string code, CancellationToken cancellationToken) =>
            Task.FromResult(Active.TryGetValue((catalogId, code), out var id) ? id : (Guid?)null);
    }
}
