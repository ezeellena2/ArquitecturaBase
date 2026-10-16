using ArquitecturaBase.Domain.Common;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormRuleSet : Entity, IAuditable
{
    private BusinessFormRuleSet() { }
    public Guid DefinitionId { get; private set; }
    public long Revision { get; private set; }
    public string OriginalCulture { get; private set; } = "es";
    public string RulesJson { get; private set; } = "[]";
    public string QuestionSchemaJson { get; private set; } = "{}";
    public bool HasApproval { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormRuleSet Create(Guid definitionId, long revision, string originalCulture, string rulesJson, bool hasApproval, string questionSchemaJson)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(definitionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(revision);
        if (originalCulture is not ("es" or "en")) throw new ArgumentException("The original culture is invalid.", nameof(originalCulture));
        ArgumentException.ThrowIfNullOrWhiteSpace(rulesJson);
        return new() { DefinitionId = definitionId, Revision = revision, OriginalCulture = originalCulture, RulesJson = rulesJson, HasApproval = hasApproval, QuestionSchemaJson = questionSchemaJson };
    }
    public override string ToString() => $"BusinessFormRuleSet {{ Id = {Id}, Revision = {Revision}, Content = [REDACTED] }}";
}
