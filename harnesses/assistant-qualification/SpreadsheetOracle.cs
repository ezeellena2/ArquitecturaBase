using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

// Stage 2 oracles over public fixture facts, in memory: a catalog that maps the turn's spreadsheet (propose-catalog@2), an import into
// the fixture's Manual catalog (propose-import@1) and the question a vague request gets. Only fixed check IDs and booleans leave this.
internal static class SpreadsheetOracle
{
    public const string ProposeImport = "forms.authoring.propose-import";
    public const int MappedCatalogVersion = 2;

    public static IReadOnlyList<CheckEvidence> PayloadChecks(AuthoringExpectation expected, string culture, AssistantCapabilityPayload? payload,
        QualificationChannelFixture fixture, StructuredSpreadsheet sheet, AssistantCapabilityPayload? previous) => expected switch
    {
        { Capability: AuthoringOracle.ProposeCatalog, Version: MappedCatalogVersion } =>
            CatalogChecks(expected, culture, payload as FormAuthoringMappedCatalogProposal, fixture, sheet, previous),
        { Capability: ProposeImport, Version: 1 } => ImportChecks(expected, payload as FormAuthoringImportProposal, fixture, sheet),
        { Capability: AuthoringOracle.Decline, Version: 1 } => AuthoringOracle.DeclineChecks(expected, payload as FormAuthoringDecline, fixture),
        { Capability: null } => [],
        _ => throw new InvalidOperationException("Unknown spreadsheet expectation."),
    };

    // What the sealed context offered, by Id and version, after the engine filtered it by permissions and by the spreadsheet.
    public static bool Offered(IReadOnlyCollection<AssistantCapabilityReference> offered, IEnumerable<AssistantCapabilityReference> required,
        IEnumerable<AssistantCapabilityReference> forbidden) => required.All(offered.Contains) && !forbidden.Any(offered.Contains);

    private static List<CheckEvidence> CatalogChecks(AuthoringExpectation expected, string culture, FormAuthoringMappedCatalogProposal? proposal,
        QualificationChannelFixture fixture, StructuredSpreadsheet sheet, AssistantCapabilityPayload? previous)
    {
        var fields = expected.Fields ?? [];
        var checks = new List<CheckEvidence>
        {
            new("authoring-payload-type", proposal is not null),
            new("authoring-culture", proposal?.Culture == culture),
            new("authoring-requested-name", proposal is not null && AuthoringOracle.SameText(expected.Name, proposal.Name)),
            new("authoring-name-not-in-use", proposal is not null && !fixture.Catalogs.SelectMany(catalog => catalog.Names.Values)
                .Any(name => AuthoringOracle.SameText(name, proposal.Name))),
            new("authoring-value-type", proposal is not null && string.Equals(expected.ValueType, proposal.ValueType, StringComparison.Ordinal)),
            new("authoring-code-and-option", proposal is not null && CodeAndOption(expected, proposal.Code, proposal.Option)),
            new("authoring-exact-fields", proposal is not null && fields.Length == proposal.Fields.Count
                && fields.Select((field, index) => AuthoringOracle.SameText(field.Name, proposal.Fields[index].Name)
                    && string.Equals(field.Type, proposal.Fields[index].Type, StringComparison.Ordinal)).All(same => same)),
        };
        checks.AddRange(MappingChecks(expected.Mapping, sheet, proposal?.Spreadsheet, proposal?.ValueType, keyed: false));
        // An adjustment proposes the whole catalog again with its name; the answer to «what should I do with it?» proposes for the first time.
        if (expected.Continues is not null)
            checks.Add(previous is FormAuthoringDecline asked
                ? new("spreadsheet-answers-the-question", proposal is not null && asked is { Reason: "unclear", DefinitionId: null, CatalogId: null })
                : new("spreadsheet-keeps-previous-name", proposal is not null && previous is FormAuthoringMappedCatalogProposal shown
                    && AuthoringOracle.SameText(shown.Name, proposal.Name)));
        return checks;
    }

    // The import goes into exactly the fixture's catalog (never another one a cell names), with its fields by their stable keys.
    private static List<CheckEvidence> ImportChecks(AuthoringExpectation expected, FormAuthoringImportProposal? proposal,
        QualificationChannelFixture fixture, StructuredSpreadsheet sheet)
    {
        var target = expected.ExistingCatalog is { } alias ? fixture.Catalogs.SingleOrDefault(catalog => catalog.Alias == alias) : null;
        var checks = new List<CheckEvidence>
        {
            new("authoring-payload-type", proposal is not null),
            new("import-target-catalog", proposal is not null && target is not null && proposal.Catalog.Id == target.Id
                && string.Equals(proposal.Catalog.ValueType, expected.ValueType, StringComparison.Ordinal)
                && target.Names.Values.Any(name => AuthoringOracle.SameText(name, proposal.Catalog.Name))),
        };
        checks.AddRange(MappingChecks(expected.Mapping, sheet, proposal?.Spreadsheet, proposal?.Catalog.ValueType, keyed: true));
        return checks;
    }

    // Code and option describe what each option carries and do not name each other's concept, except in a simple list, which takes
    // both from one column and may describe them alike.
    private static bool CodeAndOption(AuthoringExpectation expected, string code, string option) => expected is { Code: { } wantedCode, Option: { } wantedOption }
        && AuthoringOracle.ContainsPhrase(code, wantedCode) && AuthoringOracle.ContainsPhrase(option, wantedOption)
        && (AuthoringOracle.SameText(wantedCode, wantedOption)
            || !AuthoringOracle.ContainsPhrase(code, wantedOption) && !AuthoringOracle.ContainsPhrase(option, wantedCode));

    // The exact mapping: the turn's own spreadsheet, whether its first row holds headers, the code and option columns, the value only
    // as its type takes it, the detail and each field's column, in the catalog's order and nothing else.
    private static IEnumerable<CheckEvidence> MappingChecks(SpreadsheetMappingExpectation? expected, StructuredSpreadsheet sheet,
        FormAuthoringSpreadsheetSummary? summary, string? valueType, bool keyed)
    {
        var columns = summary?.Columns ?? [];
        string? Letter(string target) => columns.Count(column => column.Target == target) == 1 ? columns.Single(column => column.Target == target).Letter : null;
        var fields = columns.Where(column => column.Target == "field").ToArray();
        var wanted = expected?.Fields ?? [];
        var typed = valueType is "integer" or "decimal" or "date";
        yield return new("spreadsheet-of-the-turn", summary is not null && expected is not null && summary.FileName == sheet.FileName
            && summary.SheetName == sheet.SheetName && summary.RowCount == sheet.RowCount - (expected.HasHeaders ? 1 : 0));
        yield return new("mapping-has-headers", summary is not null && expected is not null && summary.HasHeaders == expected.HasHeaders);
        yield return new("mapping-code-and-option-columns", expected is not null && Letter("code") == expected.Code && Letter("option") == expected.Option);
        yield return new("mapping-value-by-type", expected is not null && valueType is not null && typed == expected.Value is not null
            && (expected.Value is null ? columns.All(column => column.Target != "value") : Letter("value") == expected.Value));
        yield return new("mapping-detail-column", expected is not null
            && (expected.Detail is null ? columns.All(column => column.Target != "detail") : Letter("detail") == expected.Detail));
        yield return new("mapping-field-columns", expected is not null && fields.Length == wanted.Length && fields.Select((column, index) =>
            column.Letter == wanted[index].Column && AuthoringOracle.SameText(wanted[index].Name, column.Field)
            && (keyed ? column.Key is not null && column.Key == wanted[index].Key : column.Key is null)).All(same => same));
        yield return new("mapping-exact-columns", expected is not null && summary is not null && columns.Count >= 2
            && columns[0].Target == "code" && columns[1].Target == "option"
            && columns.Count == 2 + (expected.Value is null ? 0 : 1) + (expected.Detail is null ? 0 : 1) + wanted.Length);
    }
}
