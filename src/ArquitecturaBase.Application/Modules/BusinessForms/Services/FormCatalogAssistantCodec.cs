using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogAssistantCodec
{
    public const string ProposalSchema = """
        {"type":"object","properties":{"metadataReadOrdinal":{"type":"integer","minimum":1},"name":{"type":"string","maxLength":120},"description":{"type":"string","maxLength":1000},"filters":{"type":"object","properties":{"textColumn":{"type":"string","enum":["name"]},"detailColumn":{"type":["string","null"],"enum":["name","role","active",null]},"conditions":{"type":"array","maxItems":10,"items":{"type":"object","properties":{"column":{"type":"string","enum":["name","role","active"]},"operator":{"type":"string","enum":["contains","equals","has"]},"value":{"type":"string","maxLength":256}},"required":["column","operator","value"],"additionalProperties":false}},"excludeRecipient":{"type":"boolean"},"sortColumn":{"type":"string","enum":["name","role","active"]},"sortDescending":{"type":"boolean"}},"required":["textColumn","detailColumn","conditions","excludeRecipient","sortColumn","sortDescending"],"additionalProperties":false},"limitations":{"type":"array","maxItems":5,"items":{"type":"object","properties":{"code":{"type":"string","enum":["UnsupportedColumn"]},"criterion":{"type":"string","maxLength":80}},"required":["code","criterion"],"additionalProperties":false}}},"required":["metadataReadOrdinal","name","description","filters","limitations"],"additionalProperties":false}
        """;
    public const string ArgumentsSchema = """
        {"type":"object","properties":{"name":{"type":"string","maxLength":120},"description":{"type":"string","maxLength":1000},"filters":{"type":"object","properties":{"textColumn":{"type":"string","enum":["name"]},"detailColumn":{"type":["string","null"],"enum":["name","role","active",null]},"conditions":{"type":"array","maxItems":10,"items":{"type":"object","properties":{"column":{"type":"string","enum":["name","role","active"]},"operator":{"type":"string","enum":["contains","equals","has"]},"value":{"type":"string","maxLength":256}},"required":["column","operator","value"],"additionalProperties":false}},"excludeRecipient":{"type":"boolean"},"sortColumn":{"type":"string","enum":["name","role","active"]},"sortDescending":{"type":"boolean"}},"required":["textColumn","detailColumn","conditions","excludeRecipient","sortColumn","sortDescending"],"additionalProperties":false},"limitations":{"type":"array","maxItems":5,"items":{"type":"object","properties":{"code":{"type":"string","enum":["UnsupportedColumn"]},"criterion":{"type":"string","maxLength":80}},"required":["code","criterion"],"additionalProperties":false}}},"required":["name","description","filters","limitations"],"additionalProperties":false}
        """;
    public const string MetadataSchema = """
        {"type":"object","properties":{"source":{"type":"string","enum":["users"]},"columns":{"type":"array","maxItems":3,"items":{"type":"object","properties":{"key":{"type":"string"},"dataType":{"type":"string"},"canDisplay":{"type":"boolean"},"canFilter":{"type":"boolean"},"canSort":{"type":"boolean"},"operators":{"type":"array","items":{"type":"string"}},"choices":{"type":["array","null"],"maxItems":200,"items":{"type":"object","properties":{"key":{"type":"string"},"label":{"type":"string"}},"required":["key","label"],"additionalProperties":false}}},"required":["key","dataType","canDisplay","canFilter","canSort","operators","choices"],"additionalProperties":false}}},"required":["source","columns"],"additionalProperties":false}
        """;

    public static bool EmptyArguments(string json)
    {
        try { using var document = JsonDocument.Parse(json); return Exact(document.RootElement); }
        catch (JsonException) { return false; }
    }

    public static Result<FormCatalogProposalPayload> ParseProposal(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "metadataReadOrdinal", "name", "description", "filters", "limitations") || !ContentShape(root)) return FormCatalogErrors.ContentInvalid;
            var value = root.Deserialize<FormCatalogProposalPayload>(JsonSerializerOptions.Web);
            return value is null || FormCatalogProposalPolicy.ValidateContent(value).IsFailure ? FormCatalogErrors.ContentInvalid : value;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static Result<FormCatalogProposalArguments> ParseArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "name", "description", "filters", "limitations") || !ContentShape(root)) return FormCatalogErrors.ContentInvalid;
            var value = root.Deserialize<FormCatalogProposalArguments>(JsonSerializerOptions.Web);
            return value is null || FormCatalogProposalPolicy.ValidateContent(value).IsFailure ? FormCatalogErrors.ContentInvalid : value;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    private static bool ContentShape(JsonElement root)
    {
        var filters = root.GetProperty("filters");
            if (!Exact(filters, "textColumn", "detailColumn", "conditions", "excludeRecipient", "sortColumn", "sortDescending") ||
                filters.GetProperty("conditions").ValueKind != JsonValueKind.Array ||
                filters.GetProperty("conditions").EnumerateArray().Any(condition => !Exact(condition, "column", "operator", "value")) ||
                root.GetProperty("limitations").ValueKind != JsonValueKind.Array ||
                root.GetProperty("limitations").EnumerateArray().Any(item => !Exact(item, "code", "criterion"))) return false;
        return true;
    }

    public static Result<FormCatalogMetadataPayload> ParseMetadata(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "source", "columns") || root.GetProperty("columns").ValueKind != JsonValueKind.Array) return FormCatalogErrors.ContentInvalid;
            foreach (var column in root.GetProperty("columns").EnumerateArray())
            {
                if (!Exact(column, "key", "dataType", "canDisplay", "canFilter", "canSort", "operators", "choices") ||
                    column.GetProperty("operators").ValueKind != JsonValueKind.Array) return FormCatalogErrors.ContentInvalid;
                var choices = column.GetProperty("choices");
                if (choices.ValueKind != JsonValueKind.Null && (choices.ValueKind != JsonValueKind.Array ||
                    choices.EnumerateArray().Any(choice => !Exact(choice, "key", "label")))) return FormCatalogErrors.ContentInvalid;
            }
            var metadata = root.Deserialize<FormCatalogMetadataPayload>(JsonSerializerOptions.Web);
            return metadata is null || !ValidMetadata(metadata) ? FormCatalogErrors.ContentInvalid : metadata;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static bool ValidMetadata(FormCatalogMetadataPayload metadata) => metadata.Source == "users" && metadata.Columns is { Count: 3 } &&
        metadata.Columns.Select(column => column.Key).SequenceEqual(["name", "role", "active"]) &&
        metadata.Columns.All(column => column.CanDisplay && column.CanFilter && column.CanSort) &&
        metadata.Columns[0] is { DataType: "text", Choices: null } && metadata.Columns[0].Operators.SequenceEqual(["contains", "equals"]) &&
        metadata.Columns[1] is { DataType: "text", Choices: { Count: <= 200 } } role && role.Operators.SequenceEqual(["has"]) &&
        role.Choices!.All(choice => Guid.TryParse(choice.Key, out var id) && id != Guid.Empty && !string.IsNullOrWhiteSpace(choice.Label)) &&
        role.Choices.Select(choice => choice.Key).Distinct(StringComparer.Ordinal).Count() == role.Choices.Count &&
        metadata.Columns[2] is { DataType: "boolean", Choices: { Count: 2 } } active && active.Operators.SequenceEqual(["equals"]) &&
        active.Choices!.Select(choice => choice.Key).SequenceEqual(["true", "false"]) && active.Choices.All(choice => !string.IsNullOrWhiteSpace(choice.Label));

    private static bool Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == names.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
            actual.All(name => names.Contains(name, StringComparer.Ordinal));
    }
}
