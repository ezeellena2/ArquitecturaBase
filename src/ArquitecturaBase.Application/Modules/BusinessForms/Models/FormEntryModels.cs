using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>The origin channel of a response loaded in the app, next to a channel's own (WhatsApp is «phone»).</summary>
public static class FormEntryChannel
{
    public const string Id = "app";
}

/// <summary>The active published version, ready to be loaded in the app in its content language.</summary>
public sealed record FormEntryResponse(Guid DefinitionId, Guid VersionId, long VersionNumber, string Prefix, string Name,
    string Culture, string SubmitLabel, int MaxFileSizeMiB, IReadOnlyList<FormEntryQuestionResponse> Questions)
{
    public override string ToString() => "FormEntryResponse { Content = [REDACTED] }";
}

public sealed record FormEntryQuestionResponse(Guid FieldId, string Type, string Label, string? Help, bool Required, int? MaxLength,
    IReadOnlyList<FormEntryChoiceResponse>? Options, FormSelectionSettings? Selection, FormFileSettings? Files,
    FormEntryCatalogResponse? Catalog)
{
    public override string ToString() => "FormEntryQuestionResponse { Content = [REDACTED] }";
}

public sealed record FormEntryChoiceResponse(Guid Id, string Label)
{
    public override string ToString() => "FormEntryChoiceResponse { Content = [REDACTED] }";
}

/// <summary>
/// What the person who loads can choose from a catalog question: the only option the filters leave, already chosen,
/// or the issue that blocks the question (with the name of the missing person datum). A list that depends on other
/// answers (CW7) names those questions in <see cref="DependsOn"/> and waits for them, without an option chosen beforehand.
/// </summary>
public sealed record FormEntryCatalogResponse(FormEntryOptionResponse? OnlyOption, string? Issue, string? MissingData)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<Guid>? DependsOn { get; init; }
    public override string ToString() => "FormEntryCatalogResponse { Content = [REDACTED] }";
}

public sealed record FormEntryOptionResponse(Guid Id, string Title, string? Description)
{
    public override string ToString() => "FormEntryOptionResponse { Content = [REDACTED] }";
}

public sealed record ListFormEntryOptionsRequest : PagedRequest
{
    public const int MaximumPageSize = 50;
    public static readonly IReadOnlyCollection<string> SortableFields = [];
    /// <summary>The option chosen in each question a dependent list waits for, by the question's FieldId.</summary>
    public IReadOnlyDictionary<Guid, Guid>? Answers { get; init; }
    /// <summary>While correcting this response: the options are those of whoever answered it, for whoever may correct it.</summary>
    public Guid? RecordId { get; init; }
    public override string ToString() => "ListFormEntryOptionsRequest { Search = [REDACTED] }";
}

/// <summary>
/// A response loaded in the app. <see cref="RequestId"/> identifies the send: repeating it with the same answers returns
/// the first result without another number.
/// </summary>
public sealed record SubmitFormEntryRequest(Guid RequestId, Guid VersionId, IReadOnlyList<FormEntryAnswer>? Answers,
    IReadOnlyList<FormEntryFileInput>? Files)
{
    public override string ToString() => "SubmitFormEntryRequest { Content = [REDACTED] }";
}

/// <summary>A question's answer: <see cref="Value"/> in its canonical form, or the chosen <see cref="ChoiceIds"/> of a multiple choice.</summary>
public sealed record FormEntryAnswer(Guid FieldId, string? Value, IReadOnlyList<Guid>? ChoiceIds)
{
    public override string ToString() => "FormEntryAnswer { Content = [REDACTED] }";
}

public sealed record FormEntryFileInput(Guid FieldId, string Name, string ContentType, byte[] Content)
{
    public override string ToString() => "FormEntryFileInput { Content = [REDACTED] }";
}

/// <summary>The saved response: its number and its state, which is already «PendingApproval» when a rule asks for approval.</summary>
public sealed record FormEntrySubmitResponse(Guid RecordId, string Number, string State, DateTime SubmittedAtUtc);

public sealed record FormEntryDefinitionRow(Guid DefinitionId, string Prefix, Guid? ActiveVersionId, bool Deactivated, FormEntryVersionRow? Version);

public sealed record FormEntryVersionRow(Guid VersionId, long Number, string NameJson, string SubmitLabelJson, string Culture, string SchemaJson)
{
    public override string ToString() => "FormEntryVersionRow { Content = [REDACTED] }";
}

public sealed record FormEntryItemsQuery(Guid CatalogId, string? Containment, string? Search, IReadOnlyCollection<Guid>? Ids,
    int Page, int PageSize, string Culture)
{
    public override string ToString() => "FormEntryItemsQuery { Search = [REDACTED] }";
}

public sealed record FormEntryOutcomeRow(DateTime SubmittedAtUtc, string State, bool ApprovalRequested);

public sealed record FormEntryOriginRow(Guid SourceReceiptId, Guid ActorUserId, string? ActorName, DateTime SubmittedAtUtc);
