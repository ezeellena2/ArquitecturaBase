using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>ME2b: an uploaded logo or icon, with the revision the screen read. The content is checked by its bytes.</summary>
public sealed record UploadBrandImageRequest(BrandImageKind Kind, byte[] Content, long ExpectedRevision)
{
    public override string ToString() => $"UploadBrandImageRequest {{ Kind = {Kind}, Content = [{Content.Length} bytes], ExpectedRevision = {ExpectedRevision} }}";
}

/// <summary>The settings revision after the change and the version of the new image, or null after removing it.</summary>
public sealed record BrandImageResponse(long Revision, string? Version);

/// <summary>The current logo or icon, to serve it.</summary>
public sealed record BrandImageFileResponse(Stream Content, string ContentType, string Version);
