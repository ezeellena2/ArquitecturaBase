using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormAuthoringAssistantCodec
{
    public const int MaximumRows = 100;
    // Two reads and a proposal must leave room for history and a correction within the turn context.
    public const int ReadBudget = 8192;
    public const int ResponseBudget = 16384;
    public const string EmptySchema = """{"type":"object","properties":{},"required":[],"additionalProperties":false}""";
    public const string FormsSchema = """
        {"type":"object","properties":{"isComplete":{"type":"boolean"},"forms":{"type":"array","maxItems":100,"items":{"type":"object","properties":{"name":{"type":"string"},"prefix":{"type":"string"},"state":{"type":"string","enum":["draft","published","inactive"]},"questions":{"type":"array","maxItems":40,"items":{"type":"object","properties":{"label":{"type":"string"},"type":{"type":"string"}},"required":["label","type"],"additionalProperties":false}}},"required":["name","prefix","state","questions"],"additionalProperties":false}}},"required":["isComplete","forms"],"additionalProperties":false}
        """;
    public const string CatalogsSchema = """
        {"type":"object","properties":{"isComplete":{"type":"boolean"},"catalogs":{"type":"array","maxItems":100,"items":{"type":"object","properties":{"id":{"type":"string"},"name":{"type":"string"},"kind":{"type":"string","enum":["manual","filters"]},"valueType":{"type":"string","enum":["text","integer","decimal","date"]},"isActive":{"type":"boolean"},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string"},"type":{"type":"string"}},"required":["name","type"],"additionalProperties":false}}},"required":["id","name","kind","valueType","isActive","fields"],"additionalProperties":false}}},"required":["isComplete","catalogs"],"additionalProperties":false}
        """;
    public const string FormArgumentsSchema = """
        {"type":"object","properties":{"name":{"type":"string","maxLength":120,"description":"Form name in the conversation language, not used by an existing form."},"prefix":{"type":"string","maxLength":12,"description":"1 to 12 uppercase letters or digits starting with a letter, not used by an existing form. When the requested prefix does not fit, use the closest one that does (uppercase, without spaces or symbols) instead of asking."},"submitLabel":{"type":"string","maxLength":160,"description":"Text of the final button; empty for the default."},"questions":{"type":"array","minItems":1,"maxItems":40,"items":{"type":"object","properties":{"label":{"type":"string","maxLength":160},"type":{"type":"string","enum":["shortText","longText","number","date","yesNo","choice","multiChoice","catalog","photo","document"]},"required":{"type":"boolean"},"options":{"type":"array","maxItems":50,"items":{"type":"string","maxLength":160},"description":"Only for choice (up to 50) and multiChoice (up to 20); empty otherwise."},"catalogId":{"type":["string","null"],"description":"Only for catalog: the ID of an existing catalog read in this turn; null otherwise."}},"required":["label","type","required","options","catalogId"],"additionalProperties":false}}},"required":["name","prefix","submitLabel","questions"],"additionalProperties":false}
        """;
    public const string FormProposalSchema = """
        {"type":"object","properties":{"culture":{"type":"string","enum":["es","en"]},"name":{"type":"string"},"prefix":{"type":"string"},"submitLabel":{"type":"string"},"questions":{"type":"array","maxItems":40,"items":{"type":"object","properties":{"label":{"type":"string"},"type":{"type":"string","enum":["shortText","longText","number","date","yesNo","choice","multiChoice","catalog","photo","document"]},"required":{"type":"boolean"},"options":{"type":"array","maxItems":50,"items":{"type":"string"}},"catalog":{"type":["object","null"],"properties":{"id":{"type":"string"},"name":{"type":"string"}},"required":["id","name"],"additionalProperties":false}},"required":["label","type","required","options","catalog"],"additionalProperties":false}}},"required":["culture","name","prefix","submitLabel","questions"],"additionalProperties":false}
        """;
    public const string CatalogArgumentsSchema = """
        {"type":"object","properties":{"name":{"type":"string","maxLength":120,"description":"Catalog name in the conversation language, not used by an existing catalog."},"description":{"type":"string","maxLength":1000},"valueType":{"type":"string","enum":["text","integer","decimal","date"],"description":"Type of the value of each option; text when the options have no value."},"code":{"type":"string","maxLength":80,"description":"What the code of each option is, for example the tax ID."},"option":{"type":"string","maxLength":80,"description":"What the visible text of each option is, for example the company name."},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string","maxLength":120},"type":{"type":"string","enum":["text","integer","decimal","date","boolean"]}},"required":["name","type"],"additionalProperties":false}}},"required":["name","description","valueType","code","option","fields"],"additionalProperties":false}
        """;
    public const string CatalogProposalSchema = """
        {"type":"object","properties":{"culture":{"type":"string","enum":["es","en"]},"name":{"type":"string"},"description":{"type":"string"},"valueType":{"type":"string","enum":["text","integer","decimal","date"]},"code":{"type":"string"},"option":{"type":"string"},"fields":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"name":{"type":"string"},"type":{"type":"string","enum":["text","integer","decimal","date","boolean"]}},"required":["name","type"],"additionalProperties":false}}},"required":["culture","name","description","valueType","code","option","fields"],"additionalProperties":false}
        """;
    public const string DeclineArgumentsSchema = """
        {"type":"object","properties":{"reason":{"type":"string","enum":["noPermission","formExists","catalogExists","unclear"],"description":"noPermission when the request needs a proposal tool that is not offered to this person; formExists or catalogExists when the person asks to build one that already exists; unclear when the person asks to build something but does not say enough to propose it."},"prefix":{"type":["string","null"],"maxLength":10,"description":"With formExists, the prefix of the existing form as the forms read shows it; otherwise null."},"catalogId":{"type":["string","null"],"description":"With catalogExists, the id of the existing catalog as the catalogs read shows it; otherwise null."}},"required":["reason","prefix","catalogId"],"additionalProperties":false}
        """;
    public const string DeclineSchema = """
        {"type":"object","properties":{"reason":{"type":"string","enum":["noPermission","formExists","catalogExists","unclear"]},"definitionId":{"type":["string","null"]},"catalogId":{"type":["string","null"]},"name":{"type":"string"}},"required":["reason","definitionId","catalogId","name"],"additionalProperties":false}
        """;
    public static readonly string[] IssueCodes = ["invalid", "catalogUnavailable", "catalogNotReadable", "tooLarge", "permissionGranted", "notReadable", "notFound",
        "permissionMissing"];
    public static readonly string[] DeclineReasons = ["noPermission", "formExists", "catalogExists", "unclear"];
    private static readonly string[] States = ["draft", "published", "inactive"];
    private static readonly string[] Kinds = ["manual", "filters"];
    private static readonly string[] ValueTypes = ["text", "integer", "decimal", "date"];

    public static bool EmptyArguments(string json)
    {
        try { using var document = JsonDocument.Parse(json); return Exact(document.RootElement); }
        catch (JsonException) { return false; }
    }

    public static Result<FormAuthoringFormsPayload> ParseForms(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "isComplete", "forms") || root.GetProperty("forms") is not { ValueKind: JsonValueKind.Array } forms ||
                forms.EnumerateArray().Any(form => !Exact(form, "name", "prefix", "state", "questions") ||
                    form.GetProperty("questions").ValueKind != JsonValueKind.Array ||
                    form.GetProperty("questions").EnumerateArray().Any(question => !Exact(question, "label", "type")))) return FormDefinitionErrors.ContentInvalid;
            var payload = root.Deserialize<FormAuthoringFormsPayload>(JsonSerializerOptions.Web);
            return payload is not null && ValidForms(payload) ? payload : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringCatalogsPayload> ParseCatalogs(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "isComplete", "catalogs") || root.GetProperty("catalogs") is not { ValueKind: JsonValueKind.Array } catalogs ||
                catalogs.EnumerateArray().Any(catalog => !Exact(catalog, "id", "name", "kind", "valueType", "isActive", "fields") ||
                    catalog.GetProperty("fields").ValueKind != JsonValueKind.Array ||
                    catalog.GetProperty("fields").EnumerateArray().Any(field => !Exact(field, "name", "type")))) return FormDefinitionErrors.ContentInvalid;
            var payload = root.Deserialize<FormAuthoringCatalogsPayload>(JsonSerializerOptions.Web);
            return payload is not null && ValidCatalogs(payload) ? payload : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    public static bool ValidForms(FormAuthoringFormsPayload payload) => payload.Forms is { Count: <= MaximumRows } forms &&
        forms.All(form => form is { Name.Length: > 0, Prefix.Length: > 0, Questions.Count: <= 40 } && States.Contains(form.State, StringComparer.Ordinal) &&
            form.Questions.All(question => question is { Label: not null, Type.Length: > 0 }));

    public static bool ValidCatalogs(FormAuthoringCatalogsPayload payload) => payload.Catalogs is { Count: <= MaximumRows } catalogs &&
        catalogs.All(catalog => catalog is { Name.Length: > 0, Fields.Count: <= BusinessFormCatalog.MaxFields } && catalog.Id != Guid.Empty &&
            Kinds.Contains(catalog.Kind, StringComparer.Ordinal) && ValueTypes.Contains(catalog.ValueType, StringComparer.Ordinal) &&
            catalog.Fields.All(field => field is { Name: not null, Type.Length: > 0 })) &&
        catalogs.Select(catalog => catalog.Id).Distinct().Count() == catalogs.Count;

    public static Result<FormAuthoringFormArguments> ParseFormArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "name", "prefix", "submitLabel", "questions") || root.GetProperty("questions") is not { ValueKind: JsonValueKind.Array } questions ||
                questions.EnumerateArray().Any(question => !Exact(question, "label", "type", "required", "options", "catalogId") ||
                    question.GetProperty("options").ValueKind != JsonValueKind.Array)) return FormDefinitionErrors.ContentInvalid;
            var arguments = root.Deserialize<FormAuthoringFormArguments>(JsonSerializerOptions.Web);
            return arguments is { Name: not null, Prefix: not null, SubmitLabel: not null, Questions: not null } &&
                arguments.Questions.All(question => question is { Label: not null, Type: not null, Options: not null } && question.Options.All(option => option is not null))
                ? arguments : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringCatalogProposal> ParseCatalogArguments(string json, string culture)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "name", "description", "valueType", "code", "option", "fields") || !FieldsShape(root)) return FormCatalogErrors.ContentInvalid;
            var arguments = root.Deserialize<FormAuthoringCatalogArguments>(JsonSerializerOptions.Web);
            return arguments is { Name: not null, Description: not null, ValueType: not null, Code: not null, Option: not null, Fields: not null } &&
                arguments.Fields.All(field => field is { Name: not null, Type: not null })
                ? new FormAuthoringCatalogProposal(culture, arguments.Name, arguments.Description, arguments.ValueType, arguments.Code, arguments.Option, arguments.Fields)
                : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringFormProposal> ParseFormProposal(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "culture", "name", "prefix", "submitLabel", "questions") || root.GetProperty("questions") is not { ValueKind: JsonValueKind.Array } questions ||
                questions.EnumerateArray().Any(question => !Exact(question, "label", "type", "required", "options", "catalog") ||
                    question.GetProperty("options").ValueKind != JsonValueKind.Array ||
                    question.GetProperty("catalog") is { ValueKind: not JsonValueKind.Null } catalog && !Exact(catalog, "id", "name"))) return FormDefinitionErrors.ContentInvalid;
            var proposal = root.Deserialize<FormAuthoringFormProposal>(JsonSerializerOptions.Web);
            return proposal is { Name: not null, Prefix: not null, SubmitLabel: not null, Questions: not null } &&
                proposal.Questions.All(question => question is { Label: not null, Type: not null, Options: not null }) &&
                FormAuthoringProposalPolicy.HasValidShape(proposal) ? proposal : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringCatalogProposal> ParseCatalogProposal(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "culture", "name", "description", "valueType", "code", "option", "fields") || !FieldsShape(root)) return FormCatalogErrors.ContentInvalid;
            var proposal = root.Deserialize<FormAuthoringCatalogProposal>(JsonSerializerOptions.Web);
            if (proposal is not { Culture: not null, Name: not null, Description: not null, ValueType: not null, Code: not null, Option: not null, Fields: not null } ||
                proposal.Fields.Any(field => field is not { Name: not null, Type: not null })) return FormCatalogErrors.ContentInvalid;
            var normalized = FormAuthoringProposalPolicy.Normalize(proposal);
            return FormAuthoringProposalPolicy.Issues(normalized).Count == 0 &&
                JsonSerializer.Serialize(normalized, JsonSerializerOptions.Web) == JsonSerializer.Serialize(proposal, JsonSerializerOptions.Web)
                ? proposal : FormCatalogErrors.ContentInvalid;
        }
        catch (JsonException) { return FormCatalogErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringDeclineArguments> ParseDeclineArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "reason", "prefix", "catalogId")) return FormDefinitionErrors.ContentInvalid;
            var arguments = root.Deserialize<FormAuthoringDeclineArguments>(JsonSerializerOptions.Web);
            return arguments is { Reason: not null } && DeclineReasons.Contains(arguments.Reason, StringComparer.Ordinal) ? arguments : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    public static Result<FormAuthoringDecline> ParseDecline(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!Exact(document.RootElement, "reason", "definitionId", "catalogId", "name")) return FormDefinitionErrors.ContentInvalid;
            var decline = document.RootElement.Deserialize<FormAuthoringDecline>(JsonSerializerOptions.Web);
            return decline is { Reason: not null, Name: not null } && ValidDecline(decline) ? decline : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    // Each reason names exactly what exists: a form, a catalog, or nothing when the person cannot build it or did not say what.
    public static bool ValidDecline(FormAuthoringDecline decline) => decline.Reason switch
    {
        "noPermission" or "unclear" => decline is { DefinitionId: null, CatalogId: null, Name.Length: 0 },
        "formExists" => decline is { DefinitionId: not null, CatalogId: null, Name.Length: > 0 },
        "catalogExists" => decline is { DefinitionId: null, CatalogId: not null, Name.Length: > 0 },
        _ => false,
    };

    public static Result<FormAuthoringRejection> ParseRejection(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Exact(root, "issues") || root.GetProperty("issues") is not { ValueKind: JsonValueKind.Array } issues || issues.GetArrayLength() is 0 or > 50 ||
                issues.EnumerateArray().Any(issue => !Exact(issue, "path", "code"))) return FormDefinitionErrors.ContentInvalid;
            var rejection = root.Deserialize<FormAuthoringRejection>(JsonSerializerOptions.Web);
            return rejection is { Issues: not null } && rejection.Issues.All(issue => issue is { Path.Length: > 0 and <= 120, Code: not null } &&
                IssueCodes.Contains(issue.Code, StringComparer.Ordinal)) ? rejection : FormDefinitionErrors.ContentInvalid;
        }
        catch (JsonException) { return FormDefinitionErrors.ContentInvalid; }
    }

    private static bool FieldsShape(JsonElement root) => root.GetProperty("fields") is { ValueKind: JsonValueKind.Array } fields &&
        fields.EnumerateArray().All(field => Exact(field, "name", "type"));

    private static bool Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        return actual.Length == names.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length &&
            actual.All(name => names.Contains(name, StringComparer.Ordinal));
    }
}

internal sealed record FormAuthoringQuestionArguments(string Label, string Type, bool Required, IReadOnlyList<string> Options, Guid? CatalogId)
{ public override string ToString() => "FormAuthoringQuestionArguments { Content = [REDACTED] }"; }
internal sealed record FormAuthoringFormArguments(string Name, string Prefix, string SubmitLabel, IReadOnlyList<FormAuthoringQuestionArguments> Questions)
{ public override string ToString() => "FormAuthoringFormArguments { Content = [REDACTED] }"; }
internal sealed record FormAuthoringCatalogArguments(string Name, string Description, string ValueType, string Code, string Option,
    IReadOnlyList<FormAuthoringProposedField> Fields)
{ public override string ToString() => "FormAuthoringCatalogArguments { Content = [REDACTED] }"; }
internal sealed record FormAuthoringDeclineArguments(string Reason, string? Prefix, string? CatalogId);
