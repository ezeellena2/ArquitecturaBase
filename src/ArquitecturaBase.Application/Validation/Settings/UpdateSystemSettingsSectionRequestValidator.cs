using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Settings;
using FluentValidation;

namespace ArquitecturaBase.Application.Validation.Settings;

internal sealed class UpdateSystemSettingsSectionRequestValidator : AbstractValidator<UpdateSystemSettingsSectionRequest>
{
    public UpdateSystemSettingsSectionRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThan(0).WithMessage(_ => ValidationMessages.SettingsRevisionInvalid);
        RuleFor(request => request).Must(HasSettings)
            .WithMessage(_ => ValidationMessages.SettingsRequired).OverridePropertyName("setting");
        RuleFor(request => request.DefaultCulture).Must(SystemSettings.IsSupportedCulture)
            .When(request => request.DefaultCulture is not null).WithMessage(_ => ValidationMessages.CultureInvalid);
        RuleFor(request => request.DefaultPageSize).Must(size => size.HasValue && SystemSettings.IsSupportedPageSize(size.Value))
            .When(request => request.DefaultPageSize.HasValue).WithMessage(_ => ValidationMessages.SettingsPageSizeInvalid);
        RuleFor(request => request.DefaultTimeZoneId).Must(IsKnownIanaZone)
            .When(request => request.DefaultTimeZoneId is not null).WithMessage(_ => ValidationMessages.TimeZoneInvalid);
        RuleFor(request => request.RegistrationMode).IsInEnum()
            .When(request => request.RegistrationMode.HasValue).WithMessage(_ => ValidationMessages.RegistrationModeInvalid);
        When(request => request.Appearance is not null, () =>
        {
            RuleFor(request => request.Appearance!.PrimaryColor).Must(AppearancePalette.IsValidColor)
                .WithMessage(_ => ValidationMessages.SettingsColorInvalid);
            RuleFor(request => request.Appearance!.MenuColor).Must(AppearancePalette.IsValidColor)
                .WithMessage(_ => ValidationMessages.SettingsColorInvalid);
            RuleFor(request => request.Appearance!.HeaderColor).Must(AppearancePalette.IsValidColor)
                .WithMessage(_ => ValidationMessages.SettingsColorInvalid);
            RuleFor(request => request.Appearance!.AlternateRowColor).Must(AppearancePalette.IsValidColor)
                .WithMessage(_ => ValidationMessages.SettingsColorInvalid);
        });
    }

    private static bool HasSettings(UpdateSystemSettingsSectionRequest request) =>
        request.DefaultCulture is not null || request.DefaultTimeZoneId is not null
        || request.DefaultPageSize.HasValue || request.RegistrationMode.HasValue || request.Appearance is not null;

    private static bool IsKnownIanaZone(string? zone) => zone is not null
        && (zone == "UTC" || TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out _))
        && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _);
}
