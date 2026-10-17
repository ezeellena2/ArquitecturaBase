using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace AssistantQualification;

internal sealed record CapabilityStamp(string Id, int Version, string InputSchemaSha256, string? OutputSchemaSha256);
internal sealed record CheckEvidence(string Id, bool Passed);
internal sealed record UsageEvidence(long? InputTokens, long? OutputTokens, bool Unknown);
internal sealed record CaseEvidence(string CaseId, string ChannelId, string Culture, string State,
    string Model, string CandidateProfileId, string CandidateProfileSha256,
    string? ExecutionProfileId, int? ExecutionProfileVersion, string? ExecutionProfileSha256,
    string? InstructionsId, int? InstructionsVersion, string InstructionsSha256,
    IReadOnlyList<CapabilityStamp> Capabilities, string FixtureSha256,
    int ProviderSteps, int ToolCalls, int ToolBytes, UsageEvidence Usage,
    double ElapsedSeconds, int? MaximumContextBytes, int? MaximumContinuationBytes,
    string? FailureCode, IReadOnlyList<CheckEvidence> Checks, string Assessment);

internal static class EvidenceWriter
{
    public static string HashPublicBytes(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static string HashPublicText(string value) => HashPublicBytes(Encoding.UTF8.GetBytes(value));

    // Only this closed allowlist reaches JSON. Never serialize a service result, snapshot or exception.
    public static Task WriteAsync(string outputPath, CaseEvidence evidence, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web) + "\n", cancellationToken);
}
