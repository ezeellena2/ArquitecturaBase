using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantCapabilityCatalogPolicy
{
    private readonly Dictionary<AssistantCapabilityReference, (AssistantCapabilityDefinition Definition, IAssistantCapabilitySource Source)> registry = [];
    private readonly IPermissionService permissions;

    public AiAssistantCapabilityCatalogPolicy(IEnumerable<IAssistantCapabilitySource> sources, IPermissionService permissions)
    {
        this.permissions = permissions;
        foreach (var source in sources)
            foreach (var definition in source.Definitions)
            {
                if (!ValidDefinition(definition)) throw new InvalidOperationException("Assistant capabilities must declare a valid closed contract and a positive budget.");
                if (!registry.TryAdd(new(definition.Id, definition.Version), (definition, source)))
                    throw new InvalidOperationException("Assistant capability IDs and versions must be unique.");
            }
        Definitions = registry.Values.Select(entry => entry.Definition).OrderBy(definition => definition.Id, StringComparer.Ordinal)
            .ThenBy(definition => definition.Version).ToArray();
        OfferedInHome = Definitions.Where(definition => definition.OfferedInHome && definition.RequiredPermissions.Subject.Count == 0)
            .Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToArray();
    }

    public const int MaximumDefinitions = 16;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; }
    public IReadOnlyList<AssistantCapabilityReference> OfferedInHome { get; }

    public AssistantCapabilityDefinition? Find(AssistantCapabilityReference capability) =>
        registry.TryGetValue(capability, out var entry) ? entry.Definition : null;

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        registry.TryGetValue(capability, out var entry) ? entry.Source.ParseOutput(capability, outputJson) : AiAssistantErrors.ToolRejected;

    public Task<Result<AiAssistantCapabilityCatalogResponse>> BuildAsync(AssistantChannelContext context, CancellationToken ct) =>
        BuildAsync(context, AssistantAttachmentKind.None, ct);

    public async Task<Result<AiAssistantCapabilityCatalogResponse>> BuildAsync(AssistantChannelContext context, AssistantAttachmentKind attachment,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!ValidContext(context) || !Enum.IsDefined(attachment)) return AiAssistantErrors.ToolRejected;
        var selected = new List<AssistantCapabilityDefinition>();
        foreach (var reference in context.AllowedCapabilities)
        {
            if (Find(reference) is not { } definition) return AiAssistantErrors.ToolRejected;
            selected.Add(definition);
        }
        if (context.SubjectUserId is null && context.RequiredPermissions.Subject.Count != 0) return AiAssistantErrors.Forbidden;
        var actorPermissions = await permissions.GetPermissionsAsync(context.ActorUserId, ct);
        IReadOnlyCollection<string> subjectPermissions = context.SubjectUserId is { } subjectId
            ? await permissions.GetPermissionsAsync(subjectId, ct) : [];
        if (!context.RequiredPermissions.Actor.All(actorPermissions.Contains) || !context.RequiredPermissions.Subject.All(subjectPermissions.Contains))
            return AiAssistantErrors.Forbidden;
        // What the turn's file does not admit is left out before counting the sixteen definitions: a spreadsheet or a document only
        // with one, and what never goes with a document.
        var offered = selected.Where(definition => definition.OfferedWith(attachment) &&
            definition.RequiredPermissions.Actor.All(actorPermissions.Contains) &&
            definition.RequiredPermissions.Subject.All(subjectPermissions.Contains)).ToArray();
        if (offered.Length + context.AllowedTerminals.Count > MaximumDefinitions) return AiAssistantErrors.ToolRejected;
        var required = new AssistantPermissionRequirements(
            context.RequiredPermissions.Actor.Concat(offered.SelectMany(definition => definition.RequiredPermissions.Actor)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            context.RequiredPermissions.Subject.Concat(offered.SelectMany(definition => definition.RequiredPermissions.Subject)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        return new AiAssistantCapabilityCatalogResponse(offered, context.AllowedTerminals.ToArray(), required);
    }

    private static bool ValidDefinition(AssistantCapabilityDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.Id) && definition.Version > 0 && Enum.IsDefined(definition.Kind) && Enum.IsDefined(definition.Attachment) &&
        !string.IsNullOrWhiteSpace(definition.Description) && ValidSchema(definition.InputSchemaJson) && ValidSchema(definition.OutputSchemaJson) &&
        definition.Budget is { InputBytes: > 0, OutputBytes: > 0, MaximumItems: > 0, TimeoutSeconds: > 0 } &&
        definition.RequiredPermissions.Actor.Concat(definition.RequiredPermissions.Subject).All(permission => !string.IsNullOrWhiteSpace(permission));

    private static bool ValidSchema(string schemaJson)
    {
        try
        {
            using var json = JsonDocument.Parse(schemaJson);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "object" ||
                !root.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False ||
                !root.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array) return false;
            var propertyNames = properties.EnumerateObject().Select(property => property.Name).ToArray();
            var requiredNames = required.EnumerateArray().ToArray();
            return root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == root.EnumerateObject().Count() &&
                propertyNames.Distinct(StringComparer.Ordinal).Count() == propertyNames.Length &&
                requiredNames.All(name => name.ValueKind == JsonValueKind.String && properties.TryGetProperty(name.GetString()!, out _)) &&
                requiredNames.Select(name => name.GetString()).Distinct(StringComparer.Ordinal).Count() == requiredNames.Length;
        }
        catch (JsonException) { return false; }
    }

    // assistant_answer@2 also closes on one observed proposal of the turn; assistant_proposal only selects one.
    public static bool KnownTerminal(AssistantTerminalReference terminal) =>
        terminal is { Id: "assistant_answer", Version: 1 or 2 } or { Id: "assistant_proposal", Version: 1 };

    private static bool ValidContext(AssistantChannelContext context)
    {
        if (!Enum.IsDefined(context.InteractionMode) || string.IsNullOrWhiteSpace(context.ChannelId) || context.ChannelId.Length > 80 || context.ActorUserId == Guid.Empty || context.SubjectUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.Culture) || string.IsNullOrWhiteSpace(context.TimeZoneId) ||
            context.RequiredPermissions.Actor.Concat(context.RequiredPermissions.Subject).Any(string.IsNullOrWhiteSpace) ||
            context.AllowedCapabilities.Select(reference => reference.Id).Distinct(StringComparer.Ordinal).Count() != context.AllowedCapabilities.Count ||
            context.AllowedTerminals.Count == 0 || context.AllowedTerminals.Distinct().Count() != context.AllowedTerminals.Count ||
            context.AllowedTerminals.Any(terminal => !KnownTerminal(terminal))) return false;
        try
        {
            _ = CultureInfo.GetCultureInfo(context.Culture);
            _ = TimeZoneInfo.FindSystemTimeZoneById(context.TimeZoneId);
            return true;
        }
        catch (CultureNotFoundException) { return false; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }
}
