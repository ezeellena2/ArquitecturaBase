namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>
/// <see cref="Answers"/> carries, as <c>answers[questionId]=optionId</c>, the option chosen in each question that a dependent
/// list waits for.
/// </summary>
public sealed record FormEntryOptionsQuery(string? Search = null, int Page = 1, int PageSize = 50, Dictionary<Guid, Guid>? Answers = null,
    string? Group = null, bool WithoutGroup = false)
{
    public override string ToString() => "FormEntryOptionsQuery { Search = [REDACTED] }";
}

/// <summary>The values to choose first (LD1-A), with the same <see cref="Answers"/> as the options of a dependent list.</summary>
public sealed record FormEntryGroupsQuery(Dictionary<Guid, Guid>? Answers = null)
{
    public override string ToString() => "FormEntryGroupsQuery";
}

/// <summary>
/// A send in multipart: <see cref="Answers"/> is a JSON array of <c>{ fieldId, value }</c>, in a multiple choice
/// <c>{ fieldId, choiceIds }</c> and in the block <c>{ fieldId, rows: [[{ fieldId, value }, …], …] }</c>; each file goes with the
/// question it answers, at the same position of <see cref="FileFieldIds"/>.
/// </summary>
public sealed record SubmitFormEntryHttpRequest
{
    public Guid RequestId { get; init; }
    public Guid VersionId { get; init; }
    public string Answers { get; init; } = "";
    public List<IFormFile> Files { get; init; } = [];
    public List<Guid> FileFieldIds { get; init; } = [];
    public override string ToString() => "SubmitFormEntryHttpRequest { Content = [REDACTED] }";
}

/// <summary>
/// «¿La emito?» before «Enviar»: the answers not sent yet, as the JSON array of a send (<see cref="SubmitFormEntryHttpRequest.Answers"/>),
/// without files.
/// </summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record PreviewFormIssueHttpRequest(Guid VersionId, IReadOnlyList<FormEntryAnswerHttpItem>? Answers)
{
    public override string ToString() => "PreviewFormIssueHttpRequest { Content = [REDACTED] }";
}

public sealed record FormEntryAnswerHttpItem(Guid FieldId, string? Value = null, IReadOnlyList<Guid>? ChoiceIds = null,
    IReadOnlyList<IReadOnlyList<FormEntryAnswerHttpItem>>? Rows = null)
{
    public override string ToString() => "FormEntryAnswerHttpItem { Content = [REDACTED] }";
}
