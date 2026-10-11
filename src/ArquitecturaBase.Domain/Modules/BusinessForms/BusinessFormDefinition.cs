using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormDefinition : Entity, IAuditable
{
    private BusinessFormDefinition() { }
    public string Name { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string Prefix { get; private set; } = "";
    public Guid? ActiveVersionId { get; private set; }
    public bool PrefixLocked { get; private set; }
    public long Revision { get; private set; } = 1;
    public string Culture { get; private set; } = "es";
    public string SubmitLabelJson { get; private set; } = "{}";
    public string SchemaJson { get; private set; } = "{}";
    public int QuestionCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormDefinition Create() => new();
    public Result SetDraft(long expectedRevision, string name, string nameJson, string prefix, string culture,
        string submitLabelJson, string schemaJson, int questionCount, bool creating)
    {
        if (Revision != expectedRevision || (!creating && Revision >= 9007199254740991)) return FormDraftErrors.RevisionConflict;
        if (PrefixLocked && Prefix != prefix) return FormDefinitionErrors.PrefixLocked;
        Name = name; NameJson = nameJson; Prefix = prefix; Culture = culture; SubmitLabelJson = submitLabelJson;
        SchemaJson = schemaJson; QuestionCount = questionCount;
        if (!creating) Revision++;
        return Result.Success();
    }
    public void LockPrefix() => PrefixLocked = true;
    public Result ActivateVersion(Guid? baseVersionId, Guid versionId)
    {
        if (versionId == Guid.Empty || ActiveVersionId != baseVersionId) return FormPublicationErrors.StateConflict;
        ActiveVersionId = versionId;
        return Result.Success();
    }
    public override string ToString() => $"BusinessFormDefinition {{ Id = {Id}, Revision = {Revision}, Content = [REDACTED] }}";
}
