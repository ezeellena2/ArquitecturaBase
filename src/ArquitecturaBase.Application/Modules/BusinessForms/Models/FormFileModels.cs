namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormFileAccessRow(Guid FileId, bool IsPhoto);
public sealed record FormFileContentResponse(Stream Content, string ContentType, string? DownloadName)
{
    public override string ToString() => nameof(FormFileContentResponse);
}
