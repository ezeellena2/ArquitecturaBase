using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2: propose-import@1 proposes importing the turn's spreadsheet into a Manual catalog that exists; a person imports it.
public sealed class FormAuthoringImportProposalTests
{
    private const string ProposeImport = BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId;
    private static readonly Guid Contractors = new("0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d71");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    private static string Import(string? catalogId = null, string? code = "A", string? option = "B", string? value = null, string? detail = null,
        (string Name, string? Column)[]? fields = null) =>
        Json(new { catalogId = catalogId ?? Contractors.ToString(), hasHeaders = true, code, option, value, detail,
            fields = (fields ?? [("Zona", "D"), ("Rubro", "C")]).Select(field => new { name = field.Name, column = field.Column }).ToArray() });

    private static AuthoringAssistantFixture Host(string[]? permissions = null, BusinessFormCatalogKind kind = BusinessFormCatalogKind.Manual,
        BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text, bool rubroRequired = false, Dictionary<string, string>? zona = null)
    {
        var host = new AuthoringAssistantFixture(permissions);
        var rubro = AuthoringAssistantFixture.Field("rubro", "Rubro") with { Required = rubroRequired };
        var area = AuthoringAssistantFixture.Field("zona", "Zona", position: 1);
        if (zona is not null) area = area with { Name = zona };
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Contractors, "Contratistas", kind, true, rubro, area,
            AuthoringAssistantFixture.Field("viejo", "Viejo", active: false, position: 2)) with { ValueType = valueType });
        return host;
    }

    private static Task<Result<AssistantCapabilityResponse>> ProposeAsync(AuthoringAssistantFixture host, string json, string culture = "es") =>
        host.Source.ExecuteAsync(host.Request(ProposeImport, json, 1, AuthoringAssistantFixture.Sheet()) with { Context = host.Context with { Culture = culture } }, Ct);

    private static FormAuthoringImportProposal Proposed(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("proposed", result.Value.State);
        return Assert.IsType<FormAuthoringImportProposal>(result.Value.Payload);
    }

    private static FormAuthoringRejection Rejected(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("rejected", result.Value.State);
        return Assert.IsType<FormAuthoringRejection>(result.Value.Payload);
    }

    [Fact]
    public async Task Importing_maps_the_spreadsheet_onto_the_catalog_with_the_key_of_each_field_in_the_catalog_order()
    {
        var proposal = Proposed(await ProposeAsync(Host(), Import()));
        Assert.Equal(new FormAuthoringImportCatalog(Contractors, "Contratistas", "text"), proposal.Catalog);
        var sheet = proposal.Spreadsheet;
        Assert.Equal(("contratistas.xlsx", "Hoja1", 86, true), (sheet.FileName, sheet.SheetName, sheet.RowCount, sheet.HasHeaders));
        Assert.Equal([new FormAuthoringMappedColumn("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"),
            new("field", "Rubro", "rubro", "C", "Rubro"), new("field", "Zona", "zona", "D", "Zona")], sheet.Columns);
        Assert.Equal(["20-11111111-1|Transportes Sur|Cereales|Norte", "20-22222222-2|Logística Pampa||Sur", "20-33333333-3|Acopio Centro|Hacienda|Centro"],
            sheet.Sample.Select(row => string.Join("|", row)));
    }

    [Fact]
    public async Task A_field_is_named_as_the_catalogs_read_names_it_or_in_its_original_without_case()
    {
        var host = Host(zona: new() { ["es"] = "Zona", ["en"] = "Area" });
        var english = Proposed(await ProposeAsync(host, Import(fields: [("AREA", "D"), ("rubro", "C")]), "en"));
        Assert.Equal([("Rubro", "rubro"), ("Area", "zona")], english.Spreadsheet.Columns.Skip(2).Select(column => (column.Field!, column.Key!)));
        var original = Proposed(await ProposeAsync(host, Import(fields: [("zona", "D")]), "en"));
        Assert.Equal(("Area", "D"), (original.Spreadsheet.Columns[2].Field, original.Spreadsheet.Columns[2].Letter));
    }

    [Theory]
    [InlineData("filters", "catalogId", "notManual")]
    [InlineData("unknown-catalog", "catalogId", "notFound")]
    [InlineData("not-an-id", "catalogId", "notFound")]
    [InlineData("unknown-field", "fields[0].name", "notFound")]
    [InlineData("inactive-field", "fields[0].name", "notFound")]
    [InlineData("repeated-field", "fields[1].name", "repeated")]
    [InlineData("typed-without-value", "value", "required")]
    [InlineData("text-with-value", "value", "invalid")]
    [InlineData("field-out-of-range", "fields[0].column", "outOfRange")]
    [InlineData("field-without-column", "fields[0].column", "required")]
    [InlineData("detail-repeats-the-code", "detail", "repeated")]
    public async Task An_import_that_does_not_fit_the_catalog_or_the_spreadsheet_comes_back_rejected_with_its_path(string kind, string path, string code)
    {
        var host = kind switch
        {
            "filters" => Host(kind: BusinessFormCatalogKind.Filters),
            "typed-without-value" => Host(valueType: BusinessFormCatalogValueType.Integer),
            _ => Host(),
        };
        var json = kind switch
        {
            "unknown-catalog" => Import(Guid.NewGuid().ToString()),
            "not-an-id" => Import("Contratistas"),
            "unknown-field" => Import(fields: [("Color", "C")]),
            "inactive-field" => Import(fields: [("Viejo", "C")]),
            "repeated-field" => Import(fields: [("Zona", "D"), ("zona", "C")]),
            "text-with-value" => Import(value: "C", fields: [("Zona", "D")]),
            "field-out-of-range" => Import(fields: [("Zona", "E")]),
            "field-without-column" => Import(fields: [("Zona", null)]),
            "detail-repeats-the-code" => Import(detail: "A"),
            _ => Import(),
        };
        Assert.Equal([new FormAuthoringIssue(path, code)], Rejected(await ProposeAsync(host, json)).Issues);
    }

    [Fact]
    public async Task A_required_field_has_to_be_mapped_and_the_rejection_names_it()
    {
        var host = Host(rubroRequired: true);
        var rejection = Rejected(await ProposeAsync(host, Import(fields: [("Zona", "D")])));
        Assert.Equal([new FormAuthoringIssue("fields", "required") { Name = "Rubro" }], rejection.Issues);
        var json = Json(rejection);
        Assert.Contains("\"name\":\"Rubro\"", json, StringComparison.Ordinal);
        Assert.Equal("Rubro", Assert.Single(Assert.IsType<FormAuthoringRejection>(host.Source.ParseOutput(new(ProposeImport, 1), json).Value).Issues).Name);
        Assert.Contains(Proposed(await ProposeAsync(host, Import())).Spreadsheet.Columns, column => column.Key == "rubro");
    }

    [Fact]
    public async Task Proposing_an_import_needs_managing_catalogs_and_the_spreadsheet()
    {
        var reader = Host([BusinessFormsPermissions.CatalogsRead]);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await ProposeAsync(reader, Import())).Error);
        Assert.Equal(0, reader.Reader.Reads);
        var host = Host();
        Assert.True((await host.Source.ExecuteAsync(host.Request(ProposeImport, Import()), Ct)).IsFailure);
    }

    [Fact]
    public async Task It_reads_back_only_its_own_proposal()
    {
        var host = Host();
        var json = Json(Proposed(await ProposeAsync(host, Import(detail: null))));
        Assert.IsType<FormAuthoringImportProposal>(host.Source.ParseOutput(new(ProposeImport, 1), json).Value);
        Assert.True(host.Source.ParseOutput(new(ProposeImport, 2), json).IsFailure);
        Assert.True(host.Source.ParseOutput(new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 2), json).IsFailure);
        var keyless = JsonNode.Parse(json)!;
        keyless["spreadsheet"]!["columns"]![2]!["key"] = null;
        Assert.True(host.Source.ParseOutput(new(ProposeImport, 1), keyless.ToJsonString(JsonSerializerOptions.Web)).IsFailure);
    }
}
