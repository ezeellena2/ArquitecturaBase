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
    private readonly Dictionary<string, int> highest;
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
        highest = Definitions.GroupBy(definition => definition.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(definition => definition.Version), StringComparer.Ordinal);
        OfferedInHome = Definitions.Where(definition => definition.OfferedInHome && definition.RequiredPermissions.Subject.Count == 0)
            .Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToArray();
    }

    public const int MaximumDefinitions = 16;
    public const int MaximumPreloads = 3;
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; }
    public IReadOnlyList<AssistantCapabilityReference> OfferedInHome { get; }

    public AssistantCapabilityDefinition? Find(AssistantCapabilityReference capability) =>
        registry.TryGetValue(capability, out var entry) ? entry.Definition : null;

    // I4: whether this version knows every capability a seal names, offered or preloaded (null). One above the highest version it registers
    // for its Id was sealed by a newer version (UnknownVersion); a lower one it no longer registers, or an Id it does not have, was retired or
    // is damaged (ContentUnavailable), which wins.
    public Error? Classify(AssistantChannelContext context)
    {
        Error? verdict = null;
        foreach (var reference in context.AllowedCapabilities.Concat(context.PreloadedCapabilities ?? []))
        {
            if (reference is not null && registry.ContainsKey(reference)) continue;
            if (reference?.Id is null || !highest.TryGetValue(reference.Id, out var top) || reference.Version <= top) return AiAssistantErrors.ContentUnavailable;
            verdict = AiAssistantErrors.UnknownVersion;
        }
        return verdict;
    }

    // I4: what a channel asks now, with the sealed version of each capability and terminal it names while this version still knows it: a
    // module that raises a version keeps the earlier one registered (and every terminal up to the highest is read), so what was in flight is
    // compared and replayed as it was sealed.
    public AssistantChannelContext AsSealed(AssistantChannelContext requested, AssistantChannelContext captured)
    {
        var sealedVersions = captured.AllowedCapabilities.Concat(captured.PreloadedCapabilities ?? []).Where(registry.ContainsKey).ToArray();
        var sealedTerminals = captured.AllowedTerminals.Where(KnownTerminal).ToArray();
        AssistantCapabilityReference Sealed(AssistantCapabilityReference reference) =>
            sealedVersions.FirstOrDefault(known => known.Id == reference.Id) ?? reference;
        return requested with { AllowedCapabilities = requested.AllowedCapabilities.Select(Sealed).ToArray(),
            PreloadedCapabilities = requested.PreloadedCapabilities?.Select(Sealed).ToArray(),
            AllowedTerminals = requested.AllowedTerminals.Select(terminal => sealedTerminals.FirstOrDefault(known => known.Id == terminal.Id) ?? terminal).ToArray() };
    }

    // I4, review: the fingerprint has every capability the channel asked, also one the seal left out (filtered by permission or file), at
    // the version asked then. For each of those the replay also tries every version its Id registers, first the one asked now; the
    // combinations are few, because a module keeps an earlier version only while work sealed with it lasts, and capped.
    public IEnumerable<AssistantChannelContext> Replays(AssistantChannelContext requested, AssistantChannelContext captured)
    {
        var aligned = AsSealed(requested, captured);
        var sealedIds = captured.AllowedCapabilities.Concat(captured.PreloadedCapabilities ?? []).Select(reference => reference.Id).ToHashSet(StringComparer.Ordinal);
        IEnumerable<AssistantCapabilityReference[]> Options(IReadOnlyList<AssistantCapabilityReference> references) =>
            references.Aggregate<AssistantCapabilityReference, IEnumerable<AssistantCapabilityReference[]>>([[]], (prefixes, reference) =>
                prefixes.SelectMany(prefix => (sealedIds.Contains(reference.Id) ? [reference] : Definitions.Where(definition => definition.Id == reference.Id)
                    .Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).Prepend(reference).Distinct())
                    .Select(option => (AssistantCapabilityReference[])[.. prefix, option])));
        IEnumerable<AssistantCapabilityReference[]?> preloadOptions = [null];
        if (aligned.PreloadedCapabilities is { } preloads) preloadOptions = Options(preloads);
        return (from allowed in Options(aligned.AllowedCapabilities)
                from preloaded in preloadOptions
                select aligned with { AllowedCapabilities = allowed, PreloadedCapabilities = preloaded }).Take(MaximumReplays);
    }

    private const int MaximumReplays = 64;

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
        var preloads = new List<AssistantCapabilityDefinition>();
        foreach (var reference in context.PreloadedCapabilities ?? [])
        {
            if (Find(reference) is not { Kind: AssistantCapabilityKind.Read } definition) return AiAssistantErrors.ToolRejected;
            preloads.Add(definition);
        }
        if (context.SubjectUserId is null && context.RequiredPermissions.Subject.Count != 0) return AiAssistantErrors.Forbidden;
        var actorPermissions = await permissions.GetPermissionsAsync(context.ActorUserId, ct);
        IReadOnlyCollection<string> subjectPermissions = context.SubjectUserId is { } subjectId
            ? await permissions.GetPermissionsAsync(subjectId, ct) : [];
        if (!context.RequiredPermissions.Actor.All(actorPermissions.Contains) || !context.RequiredPermissions.Subject.All(subjectPermissions.Contains))
            return AiAssistantErrors.Forbidden;
        // What the turn's file does not admit is left out before counting the sixteen definitions: a spreadsheet or a document only
        // with one, and what never goes with a document. A preload follows the same rule and never counts.
        bool Admitted(AssistantCapabilityDefinition definition) => definition.OfferedWith(attachment) &&
            definition.RequiredPermissions.Actor.All(actorPermissions.Contains) &&
            definition.RequiredPermissions.Subject.All(subjectPermissions.Contains);
        var offered = selected.Where(Admitted).ToArray();
        var preloaded = preloads.Where(Admitted).ToArray();
        if (offered.Length + context.AllowedTerminals.Count > MaximumDefinitions) return AiAssistantErrors.ToolRejected;
        var reserved = offered.Concat(preloaded).ToArray();
        var required = new AssistantPermissionRequirements(
            context.RequiredPermissions.Actor.Concat(reserved.SelectMany(definition => definition.RequiredPermissions.Actor)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            context.RequiredPermissions.Subject.Concat(reserved.SelectMany(definition => definition.RequiredPermissions.Subject)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        return new AiAssistantCapabilityCatalogResponse(offered, context.AllowedTerminals.ToArray(), required) { Preloaded = preloaded };
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

    // assistant_answer@2 also closes on one observed proposal of the turn; assistant_proposal@1 selects one, and @2 up to three. Every
    // version up to the highest of each terminal is still read.
    private static readonly Dictionary<string, int> HighestTerminals = new(StringComparer.Ordinal) { ["assistant_answer"] = 2, ["assistant_proposal"] = 2 };

    public static bool KnownTerminal(AssistantTerminalReference terminal) => ClassifyTerminal(terminal) is null;

    // I4: a terminal above the highest version of its Id was sealed by a newer version; a null one, an Id this version does not have or a
    // version below 1 is damaged or retired.
    public static Error? ClassifyTerminal(AssistantTerminalReference? terminal) =>
        terminal?.Id is null || !HighestTerminals.TryGetValue(terminal.Id, out var top) || terminal.Version <= 0 ? AiAssistantErrors.ContentUnavailable
        : terminal.Version > top ? AiAssistantErrors.UnknownVersion : null;

    private static bool ValidContext(AssistantChannelContext context)
    {
        if (!Enum.IsDefined(context.InteractionMode) || string.IsNullOrWhiteSpace(context.ChannelId) || context.ChannelId.Length > 80 || context.ActorUserId == Guid.Empty || context.SubjectUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.Culture) || string.IsNullOrWhiteSpace(context.TimeZoneId) ||
            context.RequiredPermissions.Actor.Concat(context.RequiredPermissions.Subject).Any(string.IsNullOrWhiteSpace) ||
            context.AllowedCapabilities.Select(reference => reference.Id).Distinct(StringComparer.Ordinal).Count() != context.AllowedCapabilities.Count ||
            context.AllowedTerminals.Count == 0 || context.AllowedTerminals.Distinct().Count() != context.AllowedTerminals.Count ||
            context.AllowedTerminals.Any(terminal => !KnownTerminal(terminal)) ||
            context.PreloadedCapabilities is { } preloaded && (preloaded.Count > MaximumPreloads ||
                preloaded.Select(reference => reference.Id).Distinct(StringComparer.Ordinal).Count() != preloaded.Count ||
                preloaded.Any(reference => context.AllowedCapabilities.Any(allowed => allowed.Id == reference.Id)))) return false;
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
