using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

/// <summary>
/// What an external action returned for a response (RE1-A), such as the voucher ARCA authorized: one row per run, with the
/// source's name and the values with their names sealed when it was confirmed, so they read the same after the module is removed.
/// Application checks the content; this keeps its identities, its source key and the time.
/// </summary>
public sealed partial class BusinessFormRecordResult : Entity, IAuditable
{
    public const int MaxSourceLength = 64;
    private BusinessFormRecordResult() { }
    public Guid DefinitionId { get; private set; }
    public Guid RecordId { get; private set; }
    public Guid RunId { get; private set; }
    /// <summary>The stable key of what produced it, such as «arca.invoice»; it groups its columns in the sheet.</summary>
    public string Source { get; private set; } = "";
    public string SourceNameJson { get; private set; } = "{}";
    public string ValuesJson { get; private set; } = "[]";
    public DateTime RecordedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static BusinessFormRecordResult Create(Guid definitionId, Guid recordId, Guid runId, string source, string sourceNameJson,
        string valuesJson, DateTime recordedAtUtc)
    {
        foreach (var id in new[] { definitionId, recordId, runId }) ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        if (source is null || source.Length > MaxSourceLength || !SourcePattern().IsMatch(source))
            throw new ArgumentException("The result source must be a stable lowercase key.", nameof(source));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceNameJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(valuesJson);
        if (recordedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("The result time must be UTC.", nameof(recordedAtUtc));
        return new() { DefinitionId = definitionId, RecordId = recordId, RunId = runId, Source = source,
            SourceNameJson = sourceNameJson, ValuesJson = valuesJson, RecordedAtUtc = recordedAtUtc };
    }
    public override string ToString() => "BusinessFormRecordResult { Content = [REDACTED] }";
    [GeneratedRegex("^[a-z][a-z0-9]*([.-][a-z0-9]+)*\\z", RegexOptions.CultureInvariant)] private static partial Regex SourcePattern();
}
