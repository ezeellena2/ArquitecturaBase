using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Contracts.Settings;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Net.Http.Headers;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Tags("Settings")]
public sealed class SettingsController(ISystemSettingsService service, IBrandImageService brandImages) : ControllerBase
{
    // The file plus the multipart envelope and the revision field.
    private const long MaximumBrandImageRequestBytes = BrandImage.MaxBytes + 16 * 1024;
    private const int ImmutableSeconds = 365 * 24 * 60 * 60;

    [HttpGet]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesResponseType<SystemSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpGet("presentation")]
    [AllowAnonymous]
    [ProducesResponseType<SystemPresentationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPresentation(CancellationToken cancellationToken) =>
        (await service.GetPresentationAsync(cancellationToken)).ToActionResult(this);

    [HttpPatch]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateSection(
        [FromBody] UpdateSystemSettingsSectionHttpRequest request,
        CancellationToken cancellationToken) =>
        (await service.UpdateSectionAsync(new UpdateSystemSettingsSectionRequest(
            request.ExpectedRevision, request.DefaultCulture, request.DefaultTimeZoneId,
            request.DefaultPageSize, request.RegistrationMode,
            request.Appearance is { } appearance ? new SystemAppearanceRequest(appearance.PrimaryColor,
                appearance.MenuColor, appearance.HeaderColor, appearance.AlternateRowColor, appearance.MenuAccentColor, appearance.Font) : null,
            request.Branding is { } branding ? new SystemBrandingRequest(branding.AppName, branding.EmailSenderName) : null,
            request.Retention is { } retention ? new IdentityRetentionRequest(retention.LoginAuditMonths, retention.LoginCodeDays,
                retention.InvitationDays) : null), cancellationToken)).ToActionResult(this);

    // ME2d: the sign-in, the emails and the tab show them without a session. With the current version in `v` the image never
    // changes, so it is cached for a year; without it, the browser revalidates with the ETag.
    [HttpGet("brand/logo")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/png", "image/jpeg")]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public Task<IActionResult> OpenLogo([FromQuery(Name = "v")] string? version, CancellationToken cancellationToken) =>
        OpenBrandImageAsync(BrandImageKind.Logo, version, cancellationToken);

    [HttpGet("brand/icon")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/png")]
    [ProducesProblem(StatusCodes.Status404NotFound)]
    public Task<IActionResult> OpenIcon([FromQuery(Name = "v")] string? version, CancellationToken cancellationToken) =>
        OpenBrandImageAsync(BrandImageKind.Icon, version, cancellationToken);

    [HttpPost("brand/logo")]
    [HasPermission(Permissions.Settings.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumBrandImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumBrandImageRequestBytes)]
    [ProducesResponseType<BrandImageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UploadLogo([FromForm] UploadBrandImageHttpRequest request, CancellationToken cancellationToken) =>
        UploadBrandImageAsync(BrandImageKind.Logo, request, cancellationToken);

    [HttpPost("brand/icon")]
    [HasPermission(Permissions.Settings.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumBrandImageRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumBrandImageRequestBytes)]
    [ProducesResponseType<BrandImageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public Task<IActionResult> UploadIcon([FromForm] UploadBrandImageHttpRequest request, CancellationToken cancellationToken) =>
        UploadBrandImageAsync(BrandImageKind.Icon, request, cancellationToken);

    [HttpDelete("brand/logo")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<BrandImageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveLogo([FromQuery] RemoveBrandImageQuery query, CancellationToken cancellationToken) =>
        (await brandImages.RemoveAsync(BrandImageKind.Logo, query.ExpectedRevision, cancellationToken)).ToActionResult(this);

    [HttpDelete("brand/icon")]
    [HasPermission(Permissions.Settings.Manage)]
    [ProducesResponseType<BrandImageResponse>(StatusCodes.Status200OK)]
    [ProducesProblem(StatusCodes.Status400BadRequest)]
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveIcon([FromQuery] RemoveBrandImageQuery query, CancellationToken cancellationToken) =>
        (await brandImages.RemoveAsync(BrandImageKind.Icon, query.ExpectedRevision, cancellationToken)).ToActionResult(this);

    private async Task<IActionResult> OpenBrandImageAsync(BrandImageKind kind, string? version, CancellationToken cancellationToken)
    {
        var result = await brandImages.OpenAsync(kind, cancellationToken);
        if (result.IsFailure) return result.ToActionResult(this);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = string.Equals(version, result.Value.Version, StringComparison.Ordinal)
            ? $"public, max-age={ImmutableSeconds}, immutable"
            : "no-cache";
        return File(result.Value.Content, result.Value.ContentType, lastModified: null,
            entityTag: new EntityTagHeaderValue($"\"{result.Value.Version}\""));
    }

    private async Task<IActionResult> UploadBrandImageAsync(BrandImageKind kind, UploadBrandImageHttpRequest request,
        CancellationToken cancellationToken)
    {
        // The route's limit already bounded the file; the validator says when it passes the image's own limit.
        var content = new byte[request.File.Length];
        await using (var stream = request.File.OpenReadStream())
        {
            await stream.ReadExactlyAsync(content, cancellationToken);
        }

        return (await brandImages.UploadAsync(new UploadBrandImageRequest(kind, content, request.ExpectedRevision), cancellationToken))
            .ToActionResult(this);
    }
}
