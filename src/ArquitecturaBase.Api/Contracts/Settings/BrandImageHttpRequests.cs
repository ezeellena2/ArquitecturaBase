using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Contracts.Settings;

/// <summary>ME2b: the logo or the icon in multipart, with the revision the screen read; recognized by its bytes.</summary>
public sealed record UploadBrandImageHttpRequest(long ExpectedRevision, [Required] IFormFile File)
{
    public override string ToString() => $"UploadBrandImageHttpRequest {{ ExpectedRevision = {ExpectedRevision}, File = [REDACTED] }}";
}

/// <summary>ME2b: removing the logo or the icon names the revision the screen read.</summary>
public sealed record RemoveBrandImageQuery
{
    public long ExpectedRevision { get; init; }
}
