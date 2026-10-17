using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormNavigationRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["name"];
    public string DisplayCulture { get; init; } = "es";
}
public sealed record FormNavigationResponse(Guid Id, string Name, string Prefix, long VersionNumber);
public sealed record FormResponseVersionRow(Guid Id, long Number, string NameJson, string SchemaJson);
public sealed record FormResponseDefinitionRow(Guid Id, string Prefix, Guid ActiveVersionId, IReadOnlyList<FormResponseVersionRow> Versions, bool HasApproval = false)
{
    public IReadOnlyDictionary<Guid, FormResponseVersionRow> VersionsById { get; } = Versions.ToDictionary(version => version.Id);
    public IReadOnlyDictionary<Guid, FormVersionSchema> Schemas { get; } = Versions.ToDictionary(version => version.Id,
        version => JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored form schema is invalid."));
}
public sealed record FormResponseOption(Guid Id, string Label);
public sealed record FormResponseColumn(Guid FieldId, string Type, string Label, long IntroducedInVersion, IReadOnlyList<FormResponseOption> Options);
public sealed record FormResponseCell(Guid FieldId, string Type, string State, object? Value)
{
    public override string ToString() => "FormResponseCell { Value = [REDACTED] }";
}
public sealed record FormResponseRow(Guid Id, Guid VersionId, long VersionNumber, string Number, DateTime SubmittedAtUtc,
    FormAuthorResponse Actor, string Channel, IReadOnlyList<FormResponseCell> Cells, string State = "Received", bool HasPdf = false)
{
    public override string ToString() => "FormResponseRow { Content = [REDACTED] }";
}
public sealed record FormResponseSheetResponse(Guid DefinitionId, string Name, string Prefix, long ActiveVersionNumber,
    IReadOnlyList<FormResponseColumn> Columns, IReadOnlyList<FormResponseVersionResponse> Versions, PagedResult<FormResponseRow> Records, bool HasApproval = false);
public sealed record FormResponseVersionResponse(Guid Id, long Number);
public sealed record ListFormRecordsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["number", "submittedAtUtc", "actor", "field"];
    public long? UpToNumber { get; init; }
    public string? State { get; init; }
    public Guid? SortFieldId { get; init; }
    public IReadOnlyList<Guid> VersionIds { get; init; } = [];
    public IReadOnlyList<Guid> ActorIds { get; init; } = [];
    public DateTime? ReceivedFromUtc { get; init; }
    /// <summary>Exclusive upper bound of the received interval.</summary>
    public DateTime? ReceivedToUtc { get; init; }
    public IReadOnlyList<FormRecordCondition> Conditions { get; init; } = [];
    public string DisplayCulture { get; init; } = "es";
    public override string ToString() => "ListFormRecordsRequest { Content = [REDACTED] }";
}
public sealed record FormRecordCondition(Guid FieldId, string Operator, IReadOnlyList<string>? Values)
{
    public override string ToString() => "FormRecordCondition { Values = [REDACTED] }";
}
public sealed record FormResponseStoredRow(FormRecordStoredRow Record, DateTime SubmittedAtUtc, FormAuthorResponse Actor, DateTime CreatedAtUtc = default, string State = "Received", bool HasPdf = false);

public sealed record FormRecordFacetRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["label"];
    public ListFormRecordsRequest Filter { get; init; } = new();
    public string Dimension { get; init; } = "version";
    public Guid? FieldId { get; init; }
    public override string ToString() => "FormRecordFacetRequest { Content = [REDACTED] }";
}
public sealed record FormResponseFacetOptionResponse(string Id, string Label, int Count);

public sealed record FormResponseDetailResponse(Guid Id, Guid DefinitionId, Guid VersionId, long VersionNumber, string Number, string Name,
    DateTime SubmittedAtUtc, FormAuthorResponse Actor, string Channel, StructuredRecordOrigin? Origin,
    IReadOnlyList<StructuredRecordReadValue> Values, IReadOnlyList<FormResponseHistoryResponse> History);
public sealed record FormResponseHistoryResponse(string Kind, DateTime AtUtc, FormAuthorResponse? Actor);
