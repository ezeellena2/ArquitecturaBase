namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

/// <summary>Voiding a response: only why. Who and when come from the server.</summary>
public sealed record VoidFormRecordHttpRequest(string? Reason)
{
    public override string ToString() => "VoidFormRecordHttpRequest { Reason = [REDACTED] }";
}

/// <summary>
/// A correction in multipart, like a send (<see cref="SubmitFormEntryHttpRequest"/>), plus <see cref="Reason"/> and the files of
/// the replaced response that stay, in <see cref="KeptFileIds"/>.
/// </summary>
public sealed record CorrectFormEntryHttpRequest
{
    public Guid RequestId { get; init; }
    public Guid VersionId { get; init; }
    public string Answers { get; init; } = "";
    public List<IFormFile> Files { get; init; } = [];
    public List<Guid> FileFieldIds { get; init; } = [];
    public string? Reason { get; init; }
    public List<Guid> KeptFileIds { get; init; } = [];
    public override string ToString() => "CorrectFormEntryHttpRequest { Content = [REDACTED] }";
}
