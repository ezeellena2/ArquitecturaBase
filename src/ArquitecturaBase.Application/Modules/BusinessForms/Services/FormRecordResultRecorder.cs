using System.Globalization;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Keeps in the response what an external action returned (RE1-A). The action that confirms its run calls it inside the same
/// transaction, under the fence in force; it never saves by itself. A malformed result is a bug of the module that produced it:
/// it throws and nothing is added.
/// </summary>
internal sealed partial class FormRecordResultRecorder(IFormRecordResultRepository results, TimeProvider timeProvider)
{
    private const int MaxLabelLength = 80;
    private const int MaxTextLength = 500;

    public void Add(Guid definitionId, Guid recordId, Guid runId, StructuredRecordResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        CheckNames(result.SourceName, nameof(result.SourceName));
        if (result.Values is not { Count: > 0 and <= StructuredRecordResult.MaxValues } values)
            throw new ArgumentException("A result has between one and twenty values.", nameof(result));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value?.Key is not { } key || !KeyPattern().IsMatch(key) || !keys.Add(key))
                throw new ArgumentException("Each result value has its own camelCase key.", nameof(result));
            CheckNames(value.Label, nameof(value.Label));
            if (!IsValid(value.Kind, value.Value)) throw new ArgumentException("A result value does not match its kind.", nameof(result));
        }
        results.Add(BusinessFormRecordResult.Create(definitionId, recordId, runId, result.Source,
            FormDraftPolicy.Serialize(Sorted(result.SourceName)),
            FormDraftPolicy.Serialize(values.Select(value => value with { Label = Sorted(value.Label) }).ToArray()),
            timeProvider.GetUtcNow().UtcDateTime));
    }

    /// <summary>The sealed result as it was kept.</summary>
    public static StructuredRecordResult Read(BusinessFormRecordResult stored) => Read(stored.Source, stored.SourceNameJson, stored.ValuesJson);

    public static StructuredRecordResult Read(string source, string sourceNameJson, string valuesJson) => new(source,
        FormDraftPolicy.Read<Dictionary<string, string>>(sourceNameJson), FormDraftPolicy.Read<StructuredRecordResultValue[]>(valuesJson));

    private static bool IsValid(string? kind, string? value) => kind switch
    {
        StructuredRecordResultField.Text => value is null || (value.Length <= MaxTextLength && StorableText.IsStorable(value.AsSpan())),
        StructuredRecordResultField.Date => value is null || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
        StructuredRecordResultField.Number => value is null || (decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var number) && number.ToString("0.############################", CultureInfo.InvariantCulture) == value),
        _ => false,
    };

    private static void CheckNames(IReadOnlyDictionary<string, string>? names, string parameter)
    {
        if (names is null || names.Count == 0 || names.Any(pair => pair.Key is not ("es" or "en") || string.IsNullOrWhiteSpace(pair.Value)
            || pair.Value.Length > MaxLabelLength || !StorableText.IsStorable(pair.Value.AsSpan())))
            throw new ArgumentException("Names go in es or en, each visible and up to 80 characters.", parameter);
    }

    private static SortedDictionary<string, string> Sorted(IReadOnlyDictionary<string, string> names) => new(names.ToDictionary(), StringComparer.Ordinal);

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,39}\\z", RegexOptions.CultureInvariant)] private static partial Regex KeyPattern();
}
