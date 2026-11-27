using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Settings;

/// <summary>ME2b: the revision the screen read and a file within the size limit; the service checks what the bytes are.</summary>
internal sealed class UploadBrandImageRequestValidator : AbstractValidator<UploadBrandImageRequest>
{
    public UploadBrandImageRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThan(0).WithMessage(_ => ValidationMessages.SettingsRevisionInvalid);
        RuleFor(request => request.Content).Must(content => content is { Length: > 0 and <= (int)BrandImage.MaxBytes })
            .WithMessage(request => ErrorMessages.Get(InvalidFor(request.Kind).Code)).OverridePropertyName("file");
    }

    internal static Error InvalidFor(BrandImageKind kind) =>
        kind == BrandImageKind.Icon ? SettingsErrors.IconInvalid : SettingsErrors.LogoInvalid;
}
