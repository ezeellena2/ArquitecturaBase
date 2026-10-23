using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public enum BusinessFormCatalogProposalState { Pending, Approved, Discarded }

public sealed partial class BusinessFormCatalog
{
    public Guid? ProposalId { get; private set; }
    public string? ProposalEvidenceJson { get; private set; }
    public BusinessFormCatalogProposalState? ProposalState { get; private set; }
    public Guid? ProposalDecidedByUserId { get; private set; }
    public DateTime? ProposalDecidedAtUtc { get; private set; }

    public static BusinessFormCatalog CreateProposedFilters(string originalCulture, IReadOnlyDictionary<string, string> name,
        IReadOnlyDictionary<string, string> description, string filtersJson, Guid proposalId, string evidenceJson)
    {
        if (proposalId == Guid.Empty) throw new ArgumentException("A proposal identity is required.", nameof(proposalId));
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceJson);
        var catalog = CreateFilters(originalCulture, name, description, filtersJson);
        catalog.IsActive = false;
        catalog.ProposalId = proposalId;
        catalog.ProposalEvidenceJson = evidenceJson;
        catalog.ProposalState = BusinessFormCatalogProposalState.Pending;
        return catalog;
    }

    public Result ApproveProposal(long expectedRevision, Guid actorUserId, DateTime decidedAtUtc,
        IReadOnlyDictionary<string, string> name, IReadOnlyDictionary<string, string> description, string filtersJson)
    {
        if (ProposalState != BusinessFormCatalogProposalState.Pending || Revision != expectedRevision || Revision >= 9007199254740991)
            return FormCatalogErrors.RevisionConflict;
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(filtersJson);
        if (!HasOriginalName(name)) return FormCatalogErrors.ContentInvalid;
        NameJson = SerializeTexts(name);
        DescriptionJson = SerializeTexts(description);
        FiltersJson = filtersJson;
        ProposalState = BusinessFormCatalogProposalState.Approved;
        ProposalDecidedByUserId = actorUserId;
        ProposalDecidedAtUtc = decidedAtUtc;
        IsActive = true;
        Revision++;
        return Result.Success();
    }

    public Result DiscardProposal(long expectedRevision, Guid actorUserId, DateTime decidedAtUtc)
    {
        if (ProposalState != BusinessFormCatalogProposalState.Pending || Revision != expectedRevision || Revision >= 9007199254740991)
            return FormCatalogErrors.RevisionConflict;
        ProposalState = BusinessFormCatalogProposalState.Discarded;
        ProposalDecidedByUserId = actorUserId;
        ProposalDecidedAtUtc = decidedAtUtc;
        IsActive = false;
        Revision++;
        return Result.Success();
    }
}
