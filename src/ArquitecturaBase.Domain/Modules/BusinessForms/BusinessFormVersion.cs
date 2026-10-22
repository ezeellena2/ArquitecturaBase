using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed class BusinessFormVersion : Entity, IAuditable
{
    private BusinessFormVersion() { }
    public Guid DefinitionId { get; private set; }
    public long Number { get; private set; }
    public int SchemaFormatVersion { get; private set; } = 1;
    public string SchemaJson { get; private set; } = "";
    public string ContentHash { get; private set; } = "";
    public string? NameJson { get; private set; }
    public string? SubmitLabelJson { get; private set; }
    public string? Culture { get; private set; }
    public int? QuestionCount { get; private set; }
    public string[]? PublishedCultures { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormVersion Create(Guid definitionId, long number, string contentHash) => new()
        { DefinitionId = definitionId, Number = number, ContentHash = contentHash };
    public void SealSchema(string schemaJson)
    {
        if (SchemaJson.Length != 0) throw new InvalidOperationException("A form version schema is immutable.");
        SchemaJson = schemaJson;
    }
    public void SealPresentation(string nameJson, string submitLabelJson, string culture, int questionCount)
    {
        if (NameJson is not null || SubmitLabelJson is not null || Culture is not null || QuestionCount.HasValue)
            throw new InvalidOperationException("A form version presentation is immutable.");
        ArgumentException.ThrowIfNullOrWhiteSpace(nameJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(submitLabelJson);
        if (culture is not ("es" or "en")) throw new ArgumentException("The form culture is unsupported.", nameof(culture));
        if (questionCount is < 0 or > 40) throw new ArgumentOutOfRangeException(nameof(questionCount));
        NameJson = nameJson;
        SubmitLabelJson = submitLabelJson;
        Culture = culture;
        QuestionCount = questionCount;
    }
    public void SealPublishedCultures(IReadOnlyCollection<string> cultures)
    {
        ArgumentNullException.ThrowIfNull(cultures);
        if (PublishedCultures is not null) throw new InvalidOperationException("The publication cultures are immutable.");
        if (Culture is null) throw new InvalidOperationException("The original presentation must be sealed first.");
        if (cultures.Count is < 1 or > 2 || cultures.Any(culture => culture is not ("es" or "en"))
            || cultures.Distinct(StringComparer.Ordinal).Count() != cultures.Count || !cultures.Contains(Culture, StringComparer.Ordinal))
            throw new ArgumentException("The publication cultures must include the original and unique supported languages.", nameof(cultures));
        PublishedCultures = cultures.ToArray();
    }
    public override string ToString() => $"BusinessFormVersion {{ Id = {Id}, Number = {Number}, Content = [REDACTED] }}";
}
