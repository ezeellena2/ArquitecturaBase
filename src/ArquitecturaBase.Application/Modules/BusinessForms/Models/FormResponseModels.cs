using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormResponseVersionRow(Guid Id, long Number, string NameJson, string SchemaJson,
    string OriginalCulture, IReadOnlyList<string> PublishedCultures)
{
    public string DisplayCulture(string requestedCulture) => PublishedCultures.Contains(requestedCulture, StringComparer.Ordinal)
        ? requestedCulture : OriginalCulture;
}
public sealed record FormRecordReadBatch(IReadOnlyList<FormRecordStoredRow> Rows, IReadOnlyDictionary<Guid, FormResponseVersionRow> Versions);
public sealed record FormResponseDefinitionRow(Guid Id, string Prefix, Guid ActiveVersionId, IReadOnlyList<FormResponseVersionRow> Versions, bool HasApproval = false, IReadOnlyDictionary<Guid, string>? CatalogValueTypes = null,
    IReadOnlyDictionary<Guid, IReadOnlyList<FormResponseCatalogFieldRow>>? CatalogFields = null)
{
    public IReadOnlyDictionary<Guid, FormResponseVersionRow> VersionsById { get; } = Versions.ToDictionary(version => version.Id);
    public IReadOnlyDictionary<Guid, FormVersionSchema> Schemas { get; } = Versions.ToDictionary(version => version.Id,
        version => JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The stored form schema is invalid."));
}
public sealed record FormResponseOption(Guid Id, string Label);
public sealed record FormResponseColumn(Guid FieldId, string Type, string Label, long IntroducedInVersion, IReadOnlyList<FormResponseOption> Options,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ValueType = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormResponseCatalogField>? Fields = null);
public sealed record FormResponseCell(Guid FieldId, string Type, string State, object? Value)
{
    public override string ToString() => "FormResponseCell { Value = [REDACTED] }";
}
public sealed record FormResponseRow(Guid Id, Guid VersionId, long VersionNumber, string Number, DateTime SubmittedAtUtc,
    FormAuthorResponse Actor, string Channel, IReadOnlyList<FormResponseCell> Cells, string State = "Received", bool HasPdf = false, bool Corrected = false)
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
    /// <summary>One state; «Voided» is the only way to list voided responses, apart from <see cref="IncludeVoided"/>.</summary>
    public string? State { get; init; }
    /// <summary>Voided responses too, when no state is chosen: the Excel's «Incluir las anuladas».</summary>
    public bool IncludeVoided { get; init; }
    /// <summary>Only responses loaded in the app (true) or only those received through a channel (false).</summary>
    public bool? EnteredInApp { get; init; }
    public Guid? SortFieldId { get; init; }
    public IReadOnlyList<Guid> VersionIds { get; init; } = [];
    public IReadOnlyList<Guid> ActorIds { get; init; } = [];
    /// <summary>Only this person's responses, also in the options of every count: whoever only loads (CW6-C). Never from the client.</summary>
    public Guid? OwnerUserId { get; init; }
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
public sealed record FormResponseStoredRow(FormRecordStoredRow Record, DateTime SubmittedAtUtc, FormAuthorResponse Actor, DateTime CreatedAtUtc = default, string State = "Received", bool HasPdf = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public FormResponseVersionRow? Version { get; init; }
    public string? CaptureOriginJson { get; init; }
    public FormAuthorResponse? CaptureOperator { get; init; }
    /// <summary>The first version's receipt when this is a correction: the channel and who answered come from it.</summary>
    public Guid? OriginalReceiptId { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Guid OriginReceiptId => OriginalReceiptId ?? Record.SourceReceiptId;
    /// <summary>The version this correction replaced; null for a response as it arrived.</summary>
    public Guid? ReplacesRecordId { get; init; }
    /// <summary>The correction that replaced this version; null while it is current.</summary>
    public Guid? ReplacedByRecordId { get; init; }
    public override string ToString() => "FormResponseStoredRow { Content = [REDACTED] }";
}

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
    IReadOnlyList<StructuredRecordReadValue> Values, IReadOnlyList<FormResponseHistoryResponse> History)
{
    public FormResponseAssistanceResponse? Assistance { get; init; }
    public IReadOnlyList<FormResponseChainRecordResponse> Chain { get; init; } = [];
    public string State { get; init; } = "Received";
    public FormResponseCorrectionResponse? Correction { get; init; }
    public FormResponseReplacementResponse? ReplacedBy { get; init; }
    public FormResponseVoidResponse? Void { get; init; }
    /// <summary>Whether the person reading it may correct or void it now (decision 8); the server checks again.</summary>
    public bool CanAmend { get; init; }
}
public sealed record FormResponseHistoryResponse(string Kind, DateTime AtUtc, FormAuthorResponse? Actor);

/// <summary>A correction: who and why, and how the version it replaced had ended (its decision and its PDF).</summary>
public sealed record FormResponseCorrectionResponse(Guid PreviousRecordId, DateTime AtUtc, FormAuthorResponse By, string Reason,
    string PreviousState, DateTime? PreviousDecidedAtUtc, FormAuthorResponse? PreviousDecidedBy, DateTime? PreviousPdfAtUtc)
{
    public override string ToString() => "FormResponseCorrectionResponse { Reason = [REDACTED] }";
}
/// <summary>A replaced version: the correction that stands for it now.</summary>
public sealed record FormResponseReplacementResponse(Guid RecordId, DateTime AtUtc);
/// <summary>A voided response: who, when and why.</summary>
public sealed record FormResponseVoidResponse(DateTime AtUtc, FormAuthorResponse By, string Reason)
{
    public override string ToString() => "FormResponseVoidResponse { Reason = [REDACTED] }";
}
public sealed record FormResponseAmendmentRow(FormResponseCorrectionResponse? Correction, FormResponseReplacementResponse? ReplacedBy,
    FormResponseVoidResponse? Void);

public sealed record FormResponseCatalogFieldRow(Guid Id, string Key, IReadOnlyDictionary<string, string> Name, string Type, string Role);
public sealed record FormResponseCatalogField(Guid FieldId, string Key, string Label, string Type, string Role);
