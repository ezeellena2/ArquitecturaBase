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
    /// <summary>The title of the section that starts at this question (CW15-B), in the content language: the app opens a zone with it.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Section { get; init; }
    /// <summary>The block of rows (type «group»): how many items it takes; its questions follow it, marked <see cref="Repeated"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormRowSettings? Rows { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? Repeated { get; init; }
    /// <summary>CA1-A: how the server computes this read-only decimal; the app shows a preview with the same rounding and never sends it.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormCalculation? Calculation { get; init; }
    /// <summary>CO1-A: the earlier question and answers that show this one; hidden, it is neither required nor sent.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormShowWhen? ShowWhen { get; init; }
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
    /// <summary>LD1-A: the field whose value is chosen first; the options are then read with that value as their group.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public FormEntryGroupByResponse? GroupBy { get; init; }
    public override string ToString() => "FormEntryCatalogResponse { Content = [REDACTED] }";
}

/// <summary>The field of the catalog whose value is chosen first (LD1-A), with its name in the content language.</summary>
public sealed record FormEntryGroupByResponse(Guid FieldId, string Name)
{
    public override string ToString() => "FormEntryGroupByResponse { Content = [REDACTED] }";
}

/// <summary>
/// The values to choose first, in order. <see cref="FormEntryGroupResponse.Id"/> is the value the options take as their group: a text, or the id of the
/// source option of a catalog field; null groups the options without a value, which are read with «withoutGroup».
/// </summary>
public sealed record FormEntryGroupsResponse(IReadOnlyList<FormEntryGroupResponse> Groups)
{
    public override string ToString() => "FormEntryGroupsResponse { Content = [REDACTED] }";
}

public sealed record FormEntryGroupResponse(string? Id, string Text)
{
    public override string ToString() => "FormEntryGroupResponse { Content = [REDACTED] }";
}

/// <summary>The values of a question that chooses first by a field (LD1-A), with the same answers and correction as its options.</summary>
public sealed record ListFormEntryGroupsRequest
{
    public IReadOnlyDictionary<Guid, Guid>? Answers { get; init; }
    public Guid? RecordId { get; init; }
    public override string ToString() => "ListFormEntryGroupsRequest";
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
    /// <summary>LD1-A: the value chosen first, as the values give it; <see cref="WithoutGroup"/> asks for the options without one.</summary>
    public string? Group { get; init; }
    public bool WithoutGroup { get; init; }
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

/// <summary>
/// A question's answer: <see cref="Value"/> in its canonical form, the chosen <see cref="ChoiceIds"/> of a multiple choice, or the
/// <see cref="Rows"/> of the block, each item with the values of its questions.
/// </summary>
public sealed record FormEntryAnswer(Guid FieldId, string? Value, IReadOnlyList<Guid>? ChoiceIds,
    IReadOnlyList<IReadOnlyList<FormEntryAnswer>>? Rows = null)
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

/// <summary><see cref="WithoutField"/> keeps only the options without a value in that field (LD1-A), by its id.</summary>
public sealed record FormEntryItemsQuery(Guid CatalogId, string? Containment, string? Search, IReadOnlyCollection<Guid>? Ids,
    int Page, int PageSize, string Culture, string? WithoutField = null)
{
    public override string ToString() => "FormEntryItemsQuery { Search = [REDACTED] }";
}

/// <summary>A value of a field among the options, with how many have it; null for those without a value.</summary>
public sealed record FormEntryGroupRow(string? Value, int Count)
{
    public override string ToString() => "FormEntryGroupRow { Content = [REDACTED] }";
}

public sealed record FormEntryOutcomeRow(DateTime SubmittedAtUtc, string State, bool ApprovalRequested);

public sealed record FormEntryOriginRow(Guid SourceReceiptId, Guid ActorUserId, string? ActorName, DateTime SubmittedAtUtc);
