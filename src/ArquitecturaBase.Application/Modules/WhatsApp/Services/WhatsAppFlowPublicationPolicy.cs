using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFlowPublicationPolicy
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] CurrentTargetCultureOrder = ["en", "es"];
    public static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Options)));
    public static string RequestFingerprint(StructuredFormPublicationRequest request)
    {
        var snapshot = request.Snapshot;
        return Hash(new
        {
            fingerprintFormat = 2,
            request.OperationId,
            request.DefinitionId,
            request.VersionId,
            request.ActorUserId,
            requiredPermissions = request.RequiredPermissions.Order(StringComparer.Ordinal).ToArray(),
            request.PublicationFingerprint,
            request.ProfileRevision,
            cultures = request.Cultures.OrderBy(value => value.Culture, StringComparer.Ordinal).ToArray(),
            snapshot = new
            {
                snapshot.Format,
                snapshot.OriginalCulture,
                name = CanonicalMap(snapshot.Name),
                submitLabel = CanonicalMap(snapshot.SubmitLabel),
                questions = snapshot.Questions.Select(question => new FingerprintQuestion(
                    question.FieldId, question.Key, question.Type, CanonicalMap(question.Label),
                    question.Help is null ? null : CanonicalMap(question.Help), question.Required, question.MaxLength,
                    question.Options?.Select(option => new FingerprintOption(option.ChoiceId, CanonicalMap(option.Label))).ToArray())
                    { Selection = question.Selection }).ToArray(),
                targetCultures = snapshot.TargetCultures.Order(StringComparer.Ordinal).ToArray(),
            },
        });
    }

    public static bool MatchesRequestFingerprint(WhatsAppFlowStoredManifest manifest, StructuredFormPublicationRequest request) =>
        manifest.Format switch
        {
            1 => manifest.RequestFingerprint == Hash(request) || MatchesOriginalTargetCultureOrder(manifest, request),
            2 => manifest.RequestFingerprint == RequestFingerprint(request),
            _ => false,
        };

    private static bool MatchesOriginalTargetCultureOrder(WhatsAppFlowStoredManifest manifest, StructuredFormPublicationRequest request)
    {
        // The original dispatcher emitted es/en. Its legacy hash includes array order;
        // the current dispatcher derives en/es from the sealed profiles.
        if (!request.Snapshot.TargetCultures.SequenceEqual(CurrentTargetCultureOrder, StringComparer.Ordinal)) return false;
        return manifest.RequestFingerprint == Hash(request with
        {
            Snapshot = request.Snapshot with { TargetCultures = ["es", "en"] },
        });
    }

    public static string CompilationFingerprint(string wireHash) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{WhatsAppFlowCompiler.ProfileRevision}\n{WhatsAppFlowCompiler.GraphApiVersion}\n{WhatsAppFlowCompiler.CodecVersion}\n{wireHash}")));
    public static StructuredFormPublicationResponse Status(WhatsAppFlowOperationRow row, WhatsAppFlowStoredManifest manifest)
    {
        var state = row.Bindings.Any(binding => binding.State == "Uncertain") ? "Uncertain"
            : row.Bindings.Any(binding => binding.State == "Failed") ? "Failed"
            : row.Bindings.Count > 0 && row.Bindings.All(binding => binding.State == "Published") ? "Published"
            : row.Bindings.All(binding => binding.State == "Accepted") ? "Accepted" : "Publishing";
        var stage = row.Bindings.FirstOrDefault(binding => binding.State is not ("Published" or "Failed"))?.Step
            ?? row.Bindings.FirstOrDefault(binding => binding.State == "Failed")?.Step ?? "Verify";
        var issues = new List<StructuredFormIssueResponse>();
        foreach (var binding in row.Bindings.Where(binding => binding.FailureCode is not null))
        {
            var error = ErrorFor(binding.FailureCode!);
            var diagnostics = JsonSerializer.Deserialize<WhatsAppFlowDiagnostic[]>(binding.DiagnosticsJson, Options) ?? [];
            if (diagnostics.Length == 0) issues.Add(new(null, null, "channel", error, Culture: binding.Culture));
            else issues.AddRange(diagnostics.Select(value => new StructuredFormIssueResponse(value.FieldId, value.ChoiceId, value.Path, error, Culture: value.Culture)));
        }
        return new(row.NeutralOperationId, row.DefinitionId, row.VersionId, manifest.PublicationFingerprint, state, stage,
            issues.AsReadOnly(), row.NextCheckAtUtc, state == "Uncertain");
    }
    public static Error ErrorFor(string code) => code switch
    {
        "WhatsApp.Flow.ValidationFailed" => FlowErrors.ValidationFailed,
        "WhatsApp.Flow.ProviderRejected" => FlowErrors.ProviderRejected,
        "WhatsApp.Flow.AuthorizationChanged" => FlowErrors.AuthorizationChanged,
        "WhatsApp.Flow.AccountMismatch" => FlowErrors.AccountMismatch,
        "WhatsApp.Flow.AccountUnavailable" => FlowErrors.AccountUnavailable,
        _ => throw new InvalidOperationException("The stored Flow error is unsupported."),
    };

    // Preserve the order and nulls of the original fingerprint; only the new setting is omitted when absent.
    private sealed record FingerprintQuestion(Guid FieldId, string Key, string Type,
        KeyValuePair<string, string>[] Label, KeyValuePair<string, string>[]? Help, bool Required, int? MaxLength,
        FingerprintOption[]? Options)
    {
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [System.Text.Json.Serialization.JsonPropertyOrder(1)]
        public StructuredFormSelectionSettings? Selection { get; init; }
        public override string ToString() => "FingerprintQuestion { Content = [REDACTED] }";
    }
    private sealed record FingerprintOption(Guid ChoiceId, KeyValuePair<string, string>[] Label)
    { public override string ToString() => "FingerprintOption { Content = [REDACTED] }"; }

    private static KeyValuePair<string, string>[] CanonicalMap(IReadOnlyDictionary<string, string> values) =>
        values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
}
