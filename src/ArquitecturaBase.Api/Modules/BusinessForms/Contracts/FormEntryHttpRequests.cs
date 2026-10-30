namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormEntryOptionsQuery(string? Search = null, int Page = 1, int PageSize = 50)
{
    public override string ToString() => "FormEntryOptionsQuery { Search = [REDACTED] }";
}

/// <summary>
/// A send in multipart: <see cref="Answers"/> is a JSON array of <c>{ fieldId, value }</c> or, in a multiple choice,
/// <c>{ fieldId, choiceIds }</c>; each file goes with the question it answers, at the same position of <see cref="FileFieldIds"/>.
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

public sealed record FormEntryAnswerHttpItem(Guid FieldId, string? Value = null, IReadOnlyList<Guid>? ChoiceIds = null)
{
    public override string ToString() => "FormEntryAnswerHttpItem { Content = [REDACTED] }";
}
