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
    public static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Options)));
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
}
