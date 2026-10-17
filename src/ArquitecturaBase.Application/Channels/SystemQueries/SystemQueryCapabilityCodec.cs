using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels.SystemQueries;

internal static class SystemQueryCapabilityCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string OutputSchemaJson = """
        {"type":"object","properties":{"state":{"type":"string"},"evidence":{"type":["object","null"],"properties":{"id":{"type":"string"},"sourceId":{"type":"string"},"sourceVersion":{"type":"integer"},"observedAtUtc":{"type":"string"},"revision":{"type":["integer","null"]},"destination":{"type":["string","null"]},"requiredPermissions":{"type":"array","items":{"type":"string"}},"rows":{"type":"array","maxItems":50,"items":{"type":"object","properties":{"id":{"type":["string","null"]},"values":{"type":"array","items":{"type":"object","properties":{"label":{"type":"string"},"value":{"type":"string"}},"required":["label","value"],"additionalProperties":false}}},"required":["id","values"],"additionalProperties":false}},"isComplete":{"type":"boolean"}},"required":["id","sourceId","sourceVersion","observedAtUtc","revision","destination","requiredPermissions","rows","isComplete"],"additionalProperties":false}},"required":["state","evidence"],"additionalProperties":false}
        """;

    public static Result<T> ParseArguments<T>(string argumentsJson, params string[] properties) where T : class
    {
        try
        {
            using var json = JsonDocument.Parse(argumentsJson);
            if (!HasProperties(json.RootElement, properties)) return SystemQueryErrors.InvalidArguments;
            var arguments = json.RootElement.Deserialize<T>(JsonOptions);
            return arguments is null ? SystemQueryErrors.InvalidArguments : arguments;
        }
        catch (JsonException) { return SystemQueryErrors.InvalidArguments; }
    }

    public static AssistantCapabilityResponse Response(string state, SystemQueryEvidence? evidence) =>
        new(state, new AssistantSystemQueryPayload(state, evidence), evidence is null ? [] : [evidence], evidence?.Rows.Count ?? 0);

    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson,
        IReadOnlyList<AssistantCapabilityDefinition> definitions, params string[] states)
    {
        var definition = definitions.SingleOrDefault(candidate => candidate.Id == capability.Id && candidate.Version == capability.Version);
        if (definition is null) return SystemQueryErrors.InvalidArguments;
        try
        {
            using var json = JsonDocument.Parse(outputJson);
            var root = json.RootElement;
            if (!HasProperties(root, "state", "evidence") || root.GetProperty("state").ValueKind != JsonValueKind.String ||
                !states.Contains(root.GetProperty("state").GetString(), StringComparer.Ordinal)) return SystemQueryErrors.InvalidArguments;
            var evidence = root.GetProperty("evidence");
            if (evidence.ValueKind != JsonValueKind.Null && !ValidEvidence(evidence)) return SystemQueryErrors.InvalidArguments;
            var payload = root.Deserialize<AssistantSystemQueryPayload>(JsonOptions);
            if (payload is null || payload.State == "complete" && payload.Evidence is null ||
                (payload.State is "notFound" or "notConnected") && payload.Evidence is not null) return SystemQueryErrors.InvalidArguments;
            if (payload.Evidence is { } value && (value.Id == Guid.Empty || value.SourceId != capability.Id || value.SourceVersion != capability.Version ||
                value.ObservedAtUtc.Kind != DateTimeKind.Utc || value.Rows.Count > definition.Budget.MaximumItems ||
                value.RequiredPermissions.Except(definition.RequiredPermissions.Actor, StringComparer.Ordinal).Any())) return SystemQueryErrors.InvalidArguments;
            return payload;
        }
        catch (JsonException) { return SystemQueryErrors.InvalidArguments; }
    }

    private static bool ValidEvidence(JsonElement evidence)
    {
        if (!HasProperties(evidence, "id", "sourceId", "sourceVersion", "observedAtUtc", "revision", "destination", "requiredPermissions", "rows", "isComplete") ||
            evidence.GetProperty("sourceId").ValueKind != JsonValueKind.String ||
            !Nullable(evidence.GetProperty("destination"), JsonValueKind.String) ||
            !Nullable(evidence.GetProperty("revision"), JsonValueKind.Number) ||
            evidence.GetProperty("requiredPermissions").ValueKind != JsonValueKind.Array ||
            evidence.GetProperty("requiredPermissions").EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String) ||
            evidence.GetProperty("rows").ValueKind != JsonValueKind.Array) return false;
        foreach (var row in evidence.GetProperty("rows").EnumerateArray())
        {
            if (!HasProperties(row, "id", "values") || !Nullable(row.GetProperty("id"), JsonValueKind.String) ||
                row.GetProperty("values").ValueKind != JsonValueKind.Array) return false;
            foreach (var value in row.GetProperty("values").EnumerateArray())
                if (!HasProperties(value, "label", "value") || value.GetProperty("label").ValueKind != JsonValueKind.String ||
                    value.GetProperty("value").ValueKind != JsonValueKind.String) return false;
        }
        return true;
    }

    private static bool Nullable(JsonElement value, JsonValueKind kind) => value.ValueKind == JsonValueKind.Null || value.ValueKind == kind;

    private static bool HasProperties(JsonElement value, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == properties.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length &&
            names.All(name => properties.Contains(name, StringComparer.Ordinal));
    }
}
