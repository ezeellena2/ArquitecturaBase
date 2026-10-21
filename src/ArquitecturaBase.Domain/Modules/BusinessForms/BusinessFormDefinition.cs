using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormDefinition : Entity, IAuditable
{
    private BusinessFormDefinition() { }
    public string Name { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string Prefix { get; private set; } = "";
    public string? OfferWhen { get; private set; }
    public Guid? ActiveVersionId { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }
    public Guid? DeactivatedBy { get; private set; }
    public Guid? ActiveRuleSetId { get; private set; }
    public Guid? ActivePdfDesignId { get; private set; }
    public Guid? ActiveSuggestionSetId { get; private set; }
    public bool PrefixLocked { get; private set; }
    public long Revision { get; private set; } = 1;
    public string Culture { get; private set; } = "es";
    public string SubmitLabelJson { get; private set; } = "{}";
    public string SchemaJson { get; private set; } = "{}";
    public int QuestionCount { get; private set; }
    public long LastRecordNumber { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static BusinessFormDefinition Create() => new();
    public Result SetDraft(long expectedRevision, string name, string nameJson, string prefix, string culture,
        string submitLabelJson, string schemaJson, int questionCount, bool creating, string? offerWhen = null)
    {
        if (Revision != expectedRevision || (!creating && Revision >= 9007199254740991)) return FormDraftErrors.RevisionConflict;
        if (PrefixLocked && Prefix != prefix) return FormDefinitionErrors.PrefixLocked;
        Name = name; NameJson = nameJson; Prefix = prefix; Culture = culture; SubmitLabelJson = submitLabelJson;
        OfferWhen = offerWhen;
        SchemaJson = schemaJson; QuestionCount = questionCount;
        if (!creating) Revision++;
        return Result.Success();
    }
    public void SetActiveRuleSetId(Guid ruleSetId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(ruleSetId, Guid.Empty);
        ActiveRuleSetId = ruleSetId;
    }
    public void SetActiveSuggestionSetId(Guid suggestionSetId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(suggestionSetId, Guid.Empty);
        ActiveSuggestionSetId = suggestionSetId;
    }
    public void SetActivePdfDesignId(Guid designId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(designId, Guid.Empty);
        ActivePdfDesignId = designId;
    }
    public void LockPrefix() => PrefixLocked = true;
    public long AllocateRecordNumber()
    {
        LastRecordNumber = checked(LastRecordNumber + 1);
        return LastRecordNumber;
    }
    public Result Deactivate(long expectedRevision, Guid actorUserId, DateTime nowUtc)
    {
        if (Revision != expectedRevision || Revision >= 9007199254740991) return FormDraftErrors.RevisionConflict;
        if (ActiveVersionId is null || DeactivatedAtUtc is not null) return FormDefinitionErrors.DeactivationUnavailable;
        if (actorUserId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc) return FormDefinitionErrors.ContentInvalid;
        DeactivatedAtUtc = nowUtc; DeactivatedBy = actorUserId; Revision++;
        return Result.Success();
    }
    public Result ActivateVersion(Guid? baseVersionId, Guid versionId)
    {
        if (versionId == Guid.Empty || versionId == ActiveVersionId || ActiveVersionId != baseVersionId) return FormPublicationErrors.StateConflict;
        ActiveVersionId = versionId;
        DeactivatedAtUtc = null; DeactivatedBy = null;
        return Result.Success();
    }
    public override string ToString() => $"BusinessFormDefinition {{ Id = {Id}, Revision = {Revision}, Content = [REDACTED] }}";
}
