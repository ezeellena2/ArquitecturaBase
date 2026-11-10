using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Stage 2: the contracts that name the columns of the turn's spreadsheet, propose-catalog@2 and propose-import@1.
internal static partial class FormAuthoringAssistantCodec
{
    public const string MappedCatalogArgumentsSchema = """
        {"type":"object","properties":{"name":{"type":"string","maxLength":120,"description":"Catalog name in the conversation language, not used by an existing catalog."},"description":{"type":"string","maxLength":1000},"valueType":{"type":"string","enum":["text","integer","decimal","date"],"description":"Type of the value of each option; text when the options have no value."},"code":{"type":"string","maxLength":80,"description":"What the code of each option is, for example the tax ID."},"option":{"type":"string","maxLength":80,"description":"What the visible text of each option is, for example the company name."},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string","maxLength":120},"type":{"type":"string","enum":["text","integer","decimal","date","boolean"]}},"required":["name","type"],"additionalProperties":false}},"mapping":{"anyOf":[{"type":"object","description":"Where each part of the options comes from in the conversation's spreadsheet; null without a spreadsheet.","properties":{"hasHeaders":{"type":"boolean","description":"Whether row 1 holds column headers instead of data."},"code":{"type":"string","maxLength":3,"description":"Column letter of the code of each option, as the spreadsheet shows it (A, B…)."},"option":{"type":"string","maxLength":3,"description":"Column letter of the visible text of each option; the same as the code for a simple list."},"value":{"type":["string","null"],"maxLength":3,"description":"Column letter of the value when the value type is not text; null when it is text."},"detail":{"type":["string","null"],"maxLength":3,"description":"Column letter of the detail of each option, or null."},"fields":{"type":"array","maxItems":20,"items":{"type":["string","null"],"maxLength":3},"description":"One column letter for each field, in the order of fields; null for a field that no column fills."}},"required":["hasHeaders","code","option","value","detail","fields"],"additionalProperties":false},{"type":"null"}]}},"required":["name","description","valueType","code","option","fields","mapping"],"additionalProperties":false}
        """;
    public const string MappedCatalogProposalSchema = """
        {"type":"object","properties":{"culture":{"type":"string","enum":["es","en"]},"name":{"type":"string"},"description":{"type":"string"},"valueType":{"type":"string","enum":["text","integer","decimal","date"]},"code":{"type":"string"},"option":{"type":"string"},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string"},"type":{"type":"string","enum":["text","integer","decimal","date","boolean"]}},"required":["name","type"],"additionalProperties":false}},"spreadsheet":{"type":["object","null"],"properties":{"fileName":{"type":"string"},"sheetName":{"type":"string"},"rowCount":{"type":"integer"},"hasHeaders":{"type":"boolean"},"columns":{"type":"array","maxItems":24,"items":{"type":"object","properties":{"target":{"type":"string","enum":["code","option","value","detail","field"]},"field":{"type":["string","null"]},"key":{"type":["string","null"]},"letter":{"type":"string"},"header":{"type":["string","null"]}},"required":["target","field","key","letter","header"],"additionalProperties":false}},"sample":{"type":"array","maxItems":3,"items":{"type":"array","maxItems":24,"items":{"type":"string"}}}},"required":["fileName","sheetName","rowCount","hasHeaders","columns","sample"],"additionalProperties":false}},"required":["culture","name","description","valueType","code","option","fields","spreadsheet"],"additionalProperties":false}
        """;
    public const string ImportArgumentsSchema = """
        {"type":"object","properties":{"catalogId":{"type":"string","description":"The id of an existing manual catalog as the catalogs read shows it."},"hasHeaders":{"type":"boolean","description":"Whether row 1 of the conversation's spreadsheet holds column headers instead of data."},"code":{"type":"string","maxLength":3,"description":"Column letter of the code of each option, as the spreadsheet shows it (A, B…)."},"option":{"type":"string","maxLength":3,"description":"Column letter of the visible text of each option; the same as the code for a simple list."},"value":{"type":["string","null"],"maxLength":3,"description":"Column letter of the value when the catalog's value type is not text; null when it is text."},"detail":{"type":["string","null"],"maxLength":3,"description":"Column letter of the detail of each option, or null."},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string","maxLength":120,"description":"Field name as the catalogs read shows it."},"column":{"type":"string","maxLength":3,"description":"Column letter that fills the field."}},"required":["name","column"],"additionalProperties":false},"description":"Each field of the catalog that a column fills; a required field left out comes back named in the rejection."}},"required":["catalogId","hasHeaders","code","option","value","detail","fields"],"additionalProperties":false}
        """;
    public const string ImportProposalSchema = """
        {"type":"object","properties":{"catalog":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"valueType":{"type":"string","enum":["text","integer","decimal","date"]}},"required":["id","name","valueType"],"additionalProperties":false},"spreadsheet":{"type":"object","properties":{"fileName":{"type":"string"},"sheetName":{"type":"string"},"rowCount":{"type":"integer"},"hasHeaders":{"type":"boolean"},"columns":{"type":"array","maxItems":24,"items":{"type":"object","properties":{"target":{"type":"string","enum":["code","option","value","detail","field"]},"field":{"type":["string","null"]},"key":{"type":["string","null"]},"letter":{"type":"string"},"header":{"type":["string","null"]}},"required":["target","field","key","letter","header"],"additionalProperties":false}},"sample":{"type":"array","maxItems":3,"items":{"type":"array","maxItems":24,"items":{"type":"string"}}}},"required":["fileName","sheetName","rowCount","hasHeaders","columns","sample"],"additionalProperties":false}},"required":["catalog","spreadsheet"],"additionalProperties":false}
        """;

    // Version 1's arguments and the mapping, null without the spreadsheet; a letter that is not a column comes back rejected.
    public static Result<FormAuthoringMappedCatalogArguments> ParseMappedCatalogArguments(string json, string culture)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "name", "description", "valueType", "code", "option", "fields", "mapping") || !FieldsShape(root) ||
                root.GetProperty("mapping") is { ValueKind: not JsonValueKind.Null } shape && !MappingShape(shape)) return FormCatalogErrors.ContentInvalid;
            var arguments = root.Deserialize<FormAuthoringCatalogArguments>(JsonSerializerOptions.Web);
            var mapping = root.GetProperty("mapping").Deserialize<FormAuthoringCatalogMapping>(JsonSerializerOptions.Web);
            return arguments is { Name: not null, Description: not null, ValueType: not null, Code: not null, Option: not null, Fields: not null } &&
                arguments.Fields.All(field => field is { Name: not null, Type: not null }) && mapping is null or { Fields: not null }
                ? new FormAuthoringMappedCatalogArguments(new(culture, arguments.Name, arguments.Description, arguments.ValueType, arguments.Code,
                    arguments.Option, arguments.Fields), mapping)
                : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringImportArguments> ParseImportArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "catalogId", "hasHeaders", "code", "option", "value", "detail", "fields") ||
                root.GetProperty("fields") is not { ValueKind: JsonValueKind.Array } fields ||
                fields.EnumerateArray().Any(field => !Exact(field, "name", "column"))) return FormCatalogErrors.ContentInvalid;
            var arguments = root.Deserialize<FormAuthoringImportArguments>(JsonSerializerOptions.Web);
            return arguments is { CatalogId: not null, Fields: not null } && arguments.Fields.All(field => field is { Name: not null })
                ? arguments : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    // What the server proposed, read back exactly: version 1's catalog, normalized, and the summary it took from the spreadsheet.
    public static Result<FormAuthoringMappedCatalogProposal> ParseMappedCatalogProposal(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "culture", "name", "description", "valueType", "code", "option", "fields", "spreadsheet") || !FieldsShape(root) ||
                root.GetProperty("spreadsheet") is { ValueKind: not JsonValueKind.Null } shape && !SummaryShape(shape)) return FormCatalogErrors.ContentInvalid;
            var proposal = root.Deserialize<FormAuthoringMappedCatalogProposal>(JsonSerializerOptions.Web);
            if (proposal is not { Culture: not null, Name: not null, Description: not null, ValueType: not null, Code: not null, Option: not null, Fields: not null } ||
                proposal.Fields.Any(field => field is not { Name: not null, Type: not null })) return FormCatalogErrors.ContentInvalid;
            var catalog = proposal.ToCatalog();
            var normalized = FormAuthoringProposalPolicy.Normalize(catalog);
            return FormAuthoringProposalPolicy.Issues(normalized).Count == 0 &&
                JsonSerializer.Serialize(normalized, JsonSerializerOptions.Web) == JsonSerializer.Serialize(catalog, JsonSerializerOptions.Web) &&
                (proposal.Spreadsheet is null || FormAuthoringSpreadsheetPolicy.IsValid(proposal.Spreadsheet, proposal.ValueType, proposal.Fields.Select(field => field.Name).ToArray()))
                ? proposal : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringImportProposal> ParseImportProposal(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "catalog", "spreadsheet") || !Exact(root.GetProperty("catalog"), "id", "name", "valueType") ||
                !SummaryShape(root.GetProperty("spreadsheet"))) return FormCatalogErrors.ContentInvalid;
            var proposal = root.Deserialize<FormAuthoringImportProposal>(JsonSerializerOptions.Web);
            return proposal is { Catalog: { Name.Length: > 0, ValueType: not null } catalog, Spreadsheet: not null } && catalog.Id != Guid.Empty &&
                ValueTypes.Contains(catalog.ValueType, StringComparer.Ordinal) && FormAuthoringSpreadsheetPolicy.IsValid(proposal.Spreadsheet, catalog.ValueType, null)
                ? proposal : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    private static bool MappingShape(JsonElement mapping) => Exact(mapping, "hasHeaders", "code", "option", "value", "detail", "fields") &&
        mapping.GetProperty("fields") is { ValueKind: JsonValueKind.Array } fields &&
        fields.EnumerateArray().All(field => field.ValueKind is JsonValueKind.String or JsonValueKind.Null);

    private static bool SummaryShape(JsonElement sheet) => Exact(sheet, "fileName", "sheetName", "rowCount", "hasHeaders", "columns", "sample") &&
        sheet.GetProperty("columns") is { ValueKind: JsonValueKind.Array } columns &&
        columns.EnumerateArray().All(column => Exact(column, "target", "field", "key", "letter", "header")) &&
        sheet.GetProperty("sample") is { ValueKind: JsonValueKind.Array } sample && sample.EnumerateArray().All(row => row.ValueKind == JsonValueKind.Array);
}

// Where each part of a proposed catalog's options comes from: the letters as the model wrote them, null when nothing fills it.
internal sealed record FormAuthoringCatalogMapping(bool HasHeaders, string? Code, string? Option, string? Value, string? Detail, IReadOnlyList<string?> Fields);
internal sealed record FormAuthoringMappedCatalogArguments(FormAuthoringCatalogProposal Catalog, FormAuthoringCatalogMapping? Mapping)
{ public override string ToString() => "FormAuthoringMappedCatalogArguments { Content = [REDACTED] }"; }
internal sealed record FormAuthoringImportFieldArguments(string Name, string? Column)
{ public override string ToString() => "FormAuthoringImportFieldArguments { Content = [REDACTED] }"; }
internal sealed record FormAuthoringImportArguments(string CatalogId, bool HasHeaders, string? Code, string? Option, string? Value, string? Detail,
    IReadOnlyList<FormAuthoringImportFieldArguments> Fields)
{ public override string ToString() => "FormAuthoringImportArguments { Content = [REDACTED] }"; }
