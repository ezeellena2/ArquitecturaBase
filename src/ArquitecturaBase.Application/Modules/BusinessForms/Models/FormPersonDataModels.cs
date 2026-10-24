using ArquitecturaBase.Application.Common.Pagination;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record CreateFormPersonAttributeRequest(string? Key, IReadOnlyDictionary<string, string?>? Name, string? Type, Guid? SourceCatalogId)
{ public override string ToString() => "CreateFormPersonAttributeRequest { Content = [REDACTED] }"; }
public sealed record UpdateFormPersonAttributeRequest(long? ExpectedRevision, IReadOnlyDictionary<string, string?>? Name, string? Type, Guid? SourceCatalogId, int Position, bool IsActive,
    bool ConfirmValueRemoval = false)
{ public override string ToString() => "UpdateFormPersonAttributeRequest { Content = [REDACTED] }"; }
public sealed record UpdateFormPersonDataRequest(long? ExpectedRevision, IReadOnlyDictionary<string, string?>? Values)
{ public override string ToString() => "UpdateFormPersonDataRequest { Content = [REDACTED] }"; }
public sealed record ListFormPersonDataRequest : PagedRequest
{ public static readonly IReadOnlyCollection<string> SortableFields = ["displayName"]; public string DisplayCulture { get; init; } = "es";
    public override string ToString() => "ListFormPersonDataRequest { Search = [REDACTED] }"; }
public sealed record ListFormPersonAttributeOptionsRequest : PagedRequest
{ public static readonly IReadOnlyCollection<string> SortableFields = ["text"]; public string DisplayCulture { get; init; } = "es";
    public override string ToString() => "ListFormPersonAttributeOptionsRequest { Search = [REDACTED] }"; }
public sealed record FormPersonAttributeUseResponse(Guid DefinitionId, string Name);
public sealed record FormPersonAttributeResponse(Guid Id, string Key, IReadOnlyDictionary<string, string> Name, string OriginalCulture, string Type, Guid? SourceCatalogId,
    string? SourceCatalogName, int Position, bool IsActive, long Revision, IReadOnlyList<FormPersonAttributeUseResponse> UsedBy);
public sealed record FormPersonAttributesResponse(IReadOnlyList<FormPersonAttributeResponse> Items);
public sealed record FormPersonDataValueResponse(string Value, string Text)
{ public override string ToString() => "FormPersonDataValueResponse { Content = [REDACTED] }"; }
public sealed record FormPersonDataResponse(Guid UserId, long? Revision, IReadOnlyDictionary<string, FormPersonDataValueResponse> Values)
{ public override string ToString() => "FormPersonDataResponse { Content = [REDACTED] }"; }
public sealed record FormPersonDataTableResponse(Guid UserId, string? DisplayName, IReadOnlyDictionary<string, FormPersonDataValueResponse> Values)
{ public override string ToString() => "FormPersonDataTableResponse { Content = [REDACTED] }"; }
// The people table with, for each active attribute, how many people of the whole filtered set (not only this page) lack its value.
public sealed record FormPersonDataPageResponse(IReadOnlyList<FormPersonDataTableResponse> Items, int Page, int PageSize, int TotalCount,
    IReadOnlyList<FormPersonMissingCountResponse> Missing)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
public sealed record FormPersonMissingCountResponse(Guid AttributeId, int Count);
public sealed record FormPersonMissingCounts(IReadOnlyDictionary<Guid, int> ByAttribute);
public sealed record FormPersonAttributeOptionResponse(Guid Id, string Text);
public sealed record FormPersonAttributeStoredRow(Guid Id, string Key, string NameJson, string OriginalCulture, string Type, Guid? SourceCatalogId,
    int Position, bool IsActive, long Revision, string? SourceCatalogNameJson = null);
public sealed record FormPersonAttributeUseRow(Guid AttributeId, Guid DefinitionId, string NameJson);
public sealed record FormPersonOptionTextRow(Guid CatalogId, string Text);
public sealed record FormPersonRecipientDataRow(Guid UserId, long? Revision, string ValuesJson, IReadOnlyList<FormPersonAttributeStoredRow> Attributes,
    IReadOnlyDictionary<Guid, FormPersonOptionTextRow> OptionTexts)
{ public override string ToString() => "FormPersonRecipientDataRow { Content = [REDACTED] }"; }
public sealed record FormPersonDataTableRow(Guid UserId, string? DisplayName, string ValuesJson, IReadOnlyDictionary<Guid, FormPersonOptionTextRow> OptionTexts)
{ public override string ToString() => "FormPersonDataTableRow { Content = [REDACTED] }"; }
public sealed record FormPersonAttributeOptionRow(Guid Id, string Text);

public sealed record FormPersonMissingRecipientsRow(int Count, IReadOnlyList<string> People)
{ public override string ToString() => "FormPersonMissingRecipientsRow { Content = [REDACTED] }"; }
