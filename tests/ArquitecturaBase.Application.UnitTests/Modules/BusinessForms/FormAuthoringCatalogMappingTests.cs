using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2: propose-catalog@2 maps the columns of the turn's spreadsheet and shows them on the card with three rows.
public sealed class FormAuthoringCatalogMappingTests
{
    private const string ProposeCatalog = BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId;
    private const string ProposeImport = BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId;
    private const string Decline = BusinessFormAuthoringAssistantCapabilitySource.DeclineId;
    private const int Mapped = BusinessFormAuthoringAssistantCapabilitySource.MappedCatalogVersion;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonSerializerOptions.Web);

    private static string Catalog(object? mapping, string valueType = "text", object[]? fields = null, string name = "Contratistas") =>
        Json(new { name, description = "", valueType, code = "CUIT", option = "Razón social",
            fields = fields ?? [new { name = "Rubro", type = "text" }, new { name = "Zona", type = "text" }], mapping });

    private static object Mapping(string? code = "A", string? option = "B", string? value = null, string? detail = null,
        bool hasHeaders = true, string?[]? fields = null) =>
        new { hasHeaders, code, option, value, detail, fields = fields ?? ["C", "D"] };

    private static async Task<Result<AssistantCapabilityResponse>> ProposeAsync(AuthoringAssistantFixture host, string json, bool withSheet = true) =>
        await host.Source.ExecuteAsync(withSheet ? host.Request(ProposeCatalog, json, Mapped, AuthoringAssistantFixture.Sheet())
            : host.Request(ProposeCatalog, json, Mapped), Ct);

    private static FormAuthoringMappedCatalogProposal Proposed(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("proposed", result.Value.State);
        return Assert.IsType<FormAuthoringMappedCatalogProposal>(result.Value.Payload);
    }

    private static FormAuthoringRejection Rejected(Result<AssistantCapabilityResponse> result)
    {
        Assert.True(result.IsSuccess);
        Assert.Equal("rejected", result.Value.State);
        return Assert.IsType<FormAuthoringRejection>(result.Value.Payload);
    }

    private static string[] Rows(FormAuthoringSpreadsheetSummary sheet) => sheet.Sample.Select(row => string.Join("|", row)).ToArray();

    [Fact]
    public async Task The_card_shows_each_mapped_column_and_three_rows_ordered_as_the_catalog()
    {
        var proposal = Proposed(await ProposeAsync(new AuthoringAssistantFixture(), Catalog(Mapping(code: " a ", detail: "D", fields: ["c", null]))));
        Assert.Equal(("es", "Contratistas", "CUIT", "Razón social"), (proposal.Culture, proposal.Name, proposal.Code, proposal.Option));
        var sheet = proposal.Spreadsheet!;
        Assert.Equal(("contratistas.xlsx", "Hoja1", 86, true), (sheet.FileName, sheet.SheetName, sheet.RowCount, sheet.HasHeaders));
        Assert.Equal([new FormAuthoringMappedColumn("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"),
            new("detail", null, null, "D", "Zona"), new("field", "Rubro", null, "C", "Rubro")], sheet.Columns);
        Assert.Equal(["20-11111111-1|Transportes Sur|Norte|Cereales", "20-22222222-2|Logística Pampa|Sur|", "20-33333333-3|Acopio Centro|Centro|Hacienda"],
            Rows(sheet));
    }

    [Fact]
    public async Task Without_headers_row_one_is_data_and_no_column_has_a_header()
    {
        var sheet = Proposed(await ProposeAsync(new AuthoringAssistantFixture(), Catalog(Mapping(hasHeaders: false)))).Spreadsheet!;
        Assert.Equal((87, false), (sheet.RowCount, sheet.HasHeaders));
        Assert.All(sheet.Columns, column => Assert.Null(column.Header));
        Assert.Equal(["CUIT|Razón social|Rubro|Zona", "20-11111111-1|Transportes Sur|Cereales|Norte", "20-22222222-2|Logística Pampa||Sur"], Rows(sheet));
    }

    // The prepared sheet skips empty rows: its first row with content holds the headers, also below an empty title row.
    [Fact]
    public async Task The_headers_are_the_first_row_with_content_even_below_an_empty_row()
    {
        var host = new AuthoringAssistantFixture();
        var lower = AuthoringAssistantFixture.Sheet() with { Rows = [.. AuthoringAssistantFixture.Sheet().Rows.Select(row => row with { Number = row.Number + 2 })] };
        var sheet = Proposed(await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(Mapping()), Mapped, lower), Ct)).Spreadsheet!;
        Assert.Equal(("CUIT", "Razón social", 86), (sheet.Columns[0].Header, sheet.Columns[1].Header, sheet.RowCount));
        Assert.Equal("20-11111111-1|Transportes Sur|Cereales|Norte", Rows(sheet)[0]);
    }

    // Never trimmed: a summary that does not fit the proposal's budget is rejected, for the model to map less.
    [Fact]
    public async Task A_summary_beyond_the_budget_is_rejected_without_trimming()
    {
        var host = new AuthoringAssistantFixture();
        var wide = new string('x', 3000);
        var sheet = AuthoringAssistantFixture.Sheet(4, [[wide, wide], [wide, wide], [wide, wide], [wide, wide]]);
        var result = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Catalog(Mapping(fields: []), fields: []), Mapped, sheet), Ct);
        Assert.Equal([new FormAuthoringIssue("mapping", "tooLarge")], Rejected(result).Issues);
    }

    [Fact]
    public void A_mapped_column_never_shows_the_spreadsheet_text()
    {
        var column = new FormAuthoringMappedColumn("field", "Razón social", "razon_social", "B", "Razón social");
        Assert.DoesNotContain("Razón", column.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Razón", new FormAuthoringSpreadsheetSummary("contratistas.xlsx", "Hoja1", 1, true, [column], [["Transportes Sur"]]).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_typed_catalog_maps_its_value_next_to_the_option()
    {
        var sheet = Proposed(await ProposeAsync(new AuthoringAssistantFixture(), Catalog(Mapping(value: "C", fields: [null, "D"]), "integer"))).Spreadsheet!;
        Assert.Equal(["code", "option", "value", "field"], sheet.Columns.Select(column => column.Target));
        Assert.Equal(("C", "Zona"), (sheet.Columns[2].Letter, sheet.Columns[3].Field));
    }

    [Fact]
    public async Task A_simple_list_takes_the_code_and_the_option_from_the_same_column()
    {
        var host = new AuthoringAssistantFixture();
        var json = Catalog(Mapping(code: "A", option: "A", fields: []), fields: []);
        var result = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, json, Mapped,
            AuthoringAssistantFixture.Sheet(3, [["Color"], ["Rojo"], ["Verde"]])), Ct);
        var sheet = Proposed(result).Spreadsheet!;
        Assert.Equal([new FormAuthoringMappedColumn("code", null, null, "A", "Color"), new("option", null, null, "A", "Color")], sheet.Columns);
        Assert.Equal(["Rojo|Rojo", "Verde|Verde"], Rows(sheet));
        Assert.Equal(2, sheet.RowCount);
    }

    [Theory]
    [InlineData("code-out-of-range", "mapping.code", "outOfRange")]
    [InlineData("field-out-of-range", "mapping.fields[1]", "outOfRange")]
    [InlineData("not-a-letter", "mapping.option", "invalid")]
    [InlineData("without-option", "mapping.option", "required")]
    [InlineData("typed-without-value", "mapping.value", "required")]
    [InlineData("text-with-value", "mapping.value", "invalid")]
    [InlineData("detail-repeats-the-code", "mapping.detail", "repeated")]
    [InlineData("field-repeats-the-value", "mapping.fields[0]", "repeated")]
    [InlineData("fields-repeat", "mapping.fields[1]", "repeated")]
    [InlineData("one-column-per-field", "mapping.fields", "invalid")]
    public async Task A_mapping_that_does_not_fit_the_spreadsheet_comes_back_rejected_with_its_path(string kind, string path, string code)
    {
        var json = kind switch
        {
            "code-out-of-range" => Catalog(Mapping(code: "E")),
            "field-out-of-range" => Catalog(Mapping(fields: ["C", "AA"])),
            "not-a-letter" => Catalog(Mapping(option: "1")),
            "without-option" => Catalog(Mapping(option: null)),
            "typed-without-value" => Catalog(Mapping(), "integer"),
            "text-with-value" => Catalog(Mapping(value: "D", fields: ["C", null])),
            "detail-repeats-the-code" => Catalog(Mapping(detail: "A")),
            "field-repeats-the-value" => Catalog(Mapping(value: "C"), "decimal"),
            "fields-repeat" => Catalog(Mapping(fields: ["C", "C"])),
            _ => Catalog(Mapping(fields: ["C"])),
        };
        Assert.Equal([new FormAuthoringIssue(path, code)], Rejected(await ProposeAsync(new AuthoringAssistantFixture(), json)).Issues);
    }

    [Fact]
    public async Task The_rules_of_version_one_still_apply_beside_the_mapping()
    {
        var result = await ProposeAsync(new AuthoringAssistantFixture(), Catalog(Mapping(code: "Z"), name: ""));
        Assert.Equal([new FormAuthoringIssue("name", "invalid"), new FormAuthoringIssue("mapping.code", "outOfRange")], Rejected(result).Issues);
    }

    [Fact]
    public async Task A_mapping_needs_the_spreadsheet_but_a_spreadsheet_does_not_need_a_mapping()
    {
        var host = new AuthoringAssistantFixture();
        Assert.Equal([new FormAuthoringIssue("mapping", "noSpreadsheet")], Rejected(await ProposeAsync(host, Catalog(Mapping()), withSheet: false)).Issues);
        Assert.Null(Proposed(await ProposeAsync(host, Catalog(null))).Spreadsheet);
        var plain = Proposed(await ProposeAsync(host, Catalog(null), withSheet: false));
        Assert.Equal((null, "Contratistas", 2), (plain.Spreadsheet, plain.Name, plain.Fields.Count));
    }

    [Fact]
    public async Task Each_version_reads_back_only_its_own_proposal()
    {
        var host = new AuthoringAssistantFixture();
        var mapped = Json(Proposed(await ProposeAsync(host, Catalog(Mapping(detail: "D", fields: ["C", null])))));
        var unmapped = Json(Proposed(await ProposeAsync(host, Catalog(null))));
        var first = await host.Source.ExecuteAsync(host.Request(ProposeCatalog, Json(new { name = "Contratistas", description = "", valueType = "text",
            code = "CUIT", option = "Razón social", fields = Array.Empty<object>() })), Ct);
        var original = Json(Assert.IsType<FormAuthoringCatalogProposal>(first.Value.Payload));
        Assert.IsType<FormAuthoringMappedCatalogProposal>(host.Source.ParseOutput(new(ProposeCatalog, Mapped), mapped).Value);
        Assert.Null(Assert.IsType<FormAuthoringMappedCatalogProposal>(host.Source.ParseOutput(new(ProposeCatalog, Mapped), unmapped).Value).Spreadsheet);
        Assert.IsType<FormAuthoringCatalogProposal>(host.Source.ParseOutput(new(ProposeCatalog, 1), original).Value);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, 1), mapped).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, Mapped), original).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, Mapped), Tamper(mapped, root => root["spreadsheet"]!["sample"]!.AsArray()
            .Add(new JsonArray("x", "y", "z", "w")))).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, Mapped), Tamper(mapped, root => root["spreadsheet"]!["columns"]![2]!["target"] = "field")).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, Mapped), Tamper(mapped, root => root["spreadsheet"]!["columns"]![3]!["field"] = "Color")).IsFailure);
        Assert.True(host.Source.ParseOutput(new(ProposeCatalog, Mapped), Tamper(mapped, root => root["spreadsheet"]!["columns"]![3]!["letter"] = "A")).IsFailure);
        var rejection = Json(Rejected(await ProposeAsync(host, Catalog(Mapping(code: "E")))));
        Assert.IsType<FormAuthoringRejection>(host.Source.ParseOutput(new(ProposeCatalog, Mapped), rejection).Value);
    }

    private static string Tamper(string json, Action<JsonNode> change)
    {
        var root = JsonNode.Parse(json)!;
        change(root);
        return root.ToJsonString(JsonSerializerOptions.Web);
    }

    [Fact]
    public void Home_keeps_offering_version_one_while_version_two_and_the_import_wait_for_their_front()
    {
        var definitions = new AuthoringAssistantFixture().Source.Definitions;
        var first = definitions.Single(definition => definition is { Id: ProposeCatalog, Version: 1 });
        var second = definitions.Single(definition => definition is { Id: ProposeCatalog, Version: Mapped });
        var import = definitions.Single(definition => definition.Id == ProposeImport);
        Assert.Equal((true, false), (first.OfferedInHome, first.RequiresAttachment));
        Assert.Equal((false, false), (second.OfferedInHome, second.RequiresAttachment));
        Assert.Equal((1, AssistantCapabilityKind.Proposal, false, true), (import.Version, import.Kind, import.OfferedInHome, import.RequiresAttachment));
        Assert.All([second, import], definition =>
        {
            Assert.Equal([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], definition.RequiredPermissions.Actor);
            Assert.Empty(definition.RequiredPermissions.Subject);
            Assert.Equal(first.Budget, definition.Budget);
            using var input = JsonDocument.Parse(definition.InputSchemaJson);
            using var output = JsonDocument.Parse(definition.OutputSchemaJson);
            Assert.False(input.RootElement.GetProperty("additionalProperties").GetBoolean());
            Assert.False(output.RootElement.GetProperty("additionalProperties").GetBoolean());
        });
        // Home offers each capability once, so it never carries two versions of the same one.
        Assert.Equal([$"{ProposeCatalog}@1"], definitions.Where(definition => definition.OfferedInHome && definition.Id is ProposeCatalog or ProposeImport)
            .Select(definition => $"{definition.Id}@{definition.Version}"));
        Assert.Contains("never import", import.Description, StringComparison.Ordinal);
        Assert.Contains("propose importing", definitions.Single(definition => definition.Id == Decline).Description, StringComparison.Ordinal);
    }

    [Fact]
    public void The_catalog_and_the_import_replace_each_other() =>
        Assert.Equal([ProposeCatalog, ProposeImport], BusinessFormAuthoringAssistantCapabilitySource.CatalogGroup);
}
