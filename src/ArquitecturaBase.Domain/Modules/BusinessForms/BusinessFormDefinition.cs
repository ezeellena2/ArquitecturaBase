using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public sealed class BusinessFormDefinition : Entity, IAuditable, ISoftDeletable
{
    private BusinessFormDefinition() { }
    public string Name { get; private set; } = "";
    public string NameJson { get; private set; } = "{}";
    public string Prefix { get; private set; } = "";
    public string? OfferWhen { get; private set; }
    // «Dónde se completa» of the draft (SA1-A): true is «Sólo en la app». It belongs to the draft and its revision.
    public bool AppOnly { get; private set; }
    // The reading of documents of the draft (DOC1), as Application validated it; null without one. It belongs to the draft too.
    public string? DocumentReadingJson { get; private set; }
    public Guid? ActiveVersionId { get; private set; }
    public DateTime? DeactivatedAtUtc { get; private set; }
    public Guid? DeactivatedBy { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }
    public Guid? DeletedBy { get; private set; }
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
    public Guid? ProposalId { get; private set; }
    public string? ProposalEvidenceJson { get; private set; }
    /// <summary>BI1-A: the standard of the library this form was added from, its version and which question holds each datum it needs.</summary>
    public string? StandardKey { get; private set; }
    public int? StandardVersion { get; private set; }
    public string? StandardBindingsJson { get; private set; }
    public const int MaxStandardKeyLength = 64;
    public static BusinessFormDefinition Create() => new();
    // A draft created from an assistant proposal keeps which one and where it came from, once.
    public void RecordProposal(Guid proposalId, string evidenceJson)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(proposalId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceJson);
        if (ProposalId is not null) throw new InvalidOperationException("A draft records its proposal only once.");
        ProposalId = proposalId;
        ProposalEvidenceJson = evidenceJson;
    }
    // A form added from the library keeps its standard, version and bound questions, once.
    public void RecordStandard(string key, int version, string bindingsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > MaxStandardKeyLength) throw new ArgumentException("The standard key is too long.", nameof(key));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingsJson);
        if (StandardKey is not null) throw new InvalidOperationException("A form records its standard only once.");
        StandardKey = key;
        StandardVersion = version;
        StandardBindingsJson = bindingsJson;
    }
    public Result SetDraft(long expectedRevision, string name, string nameJson, string prefix, string culture,
        string submitLabelJson, string schemaJson, int questionCount, bool creating, string? offerWhen = null, bool appOnly = false,
        string? documentReadingJson = null)
    {
        if (Revision != expectedRevision || (!creating && Revision >= 9007199254740991)) return FormDraftErrors.RevisionConflict;
        if (PrefixLocked && Prefix != prefix) return FormDefinitionErrors.PrefixLocked;
        Name = name; NameJson = nameJson; Prefix = prefix; Culture = culture; SubmitLabelJson = submitLabelJson;
        OfferWhen = offerWhen; AppOnly = appOnly; DocumentReadingJson = documentReadingJson;
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
