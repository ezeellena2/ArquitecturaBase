using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormSuggestionSet : Entity, IAuditable
{
    private BusinessFormSuggestionSet() { }
    public Guid DefinitionId { get; private set; }
    public Guid SourceVersionId { get; private set; }
    public long Revision { get; private set; }
    public string OriginalCulture { get; private set; } = "es";
    public string SuggestionsJson { get; private set; } = "[]";
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormSuggestionSet Create(Guid definitionId, Guid sourceVersionId, long revision,
        string originalCulture, string suggestionsJson)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(definitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(sourceVersionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (originalCulture is not ("es" or "en")) throw new ArgumentException("The original culture is invalid.", nameof(originalCulture));
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestionsJson);
        return new() { DefinitionId = definitionId, SourceVersionId = sourceVersionId, Revision = revision,
            OriginalCulture = originalCulture, SuggestionsJson = suggestionsJson };
    }
    public override string ToString() => $"BusinessFormSuggestionSet {{ Id = {Id}, Revision = {Revision}, Content = [REDACTED] }}";
}
