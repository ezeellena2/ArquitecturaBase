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
        // RC6-B: each field says its own problem; the domain repeats the rules before changing anything.
        When(request => request.Branding is not null, () =>
        {
            RuleFor(request => request.Branding!.AppName).Cascade(CascadeMode.Stop)
                .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage(_ => ValidationMessages.Required)
                .MaximumLength(SystemBranding.MaxNameLength).WithMessage(_ => ValidationMessages.MaxLength)
                .Must(SystemBranding.IsValidName).WithMessage(_ => ValidationMessages.SettingsNameInvalid);
            RuleFor(request => request.Branding!.EmailSenderName).Cascade(CascadeMode.Stop)
                .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage(_ => ValidationMessages.Required)
                .MaximumLength(SystemBranding.MaxNameLength).WithMessage(_ => ValidationMessages.MaxLength)
                .Must(SystemBranding.IsValidName).WithMessage(_ => ValidationMessages.SettingsNameInvalid);
            RuleFor(request => request.Branding!.EmailLogoUrl).Cascade(CascadeMode.Stop)
                .MaximumLength(SystemBranding.MaxLogoUrlLength).WithMessage(_ => ValidationMessages.MaxLength)
                .Must(SystemBranding.IsValidLogoUrl).WithMessage(_ => ValidationMessages.SettingsLogoUrlInvalid);
        });
        // RC1-A: the message names the range of each period, in the request's language.
        When(request => request.Retention is not null, () =>
        {
            RuleFor(request => request.Retention!.LoginAuditMonths)
                .InclusiveBetween(IdentityRetentionPeriods.MinLoginAuditMonths, IdentityRetentionPeriods.MaxLoginAuditMonths)
                .WithMessage(_ => ValidationMessages.NumberBetween);
            RuleFor(request => request.Retention!.LoginCodeDays)
                .InclusiveBetween(IdentityRetentionPeriods.MinLoginCodeDays, IdentityRetentionPeriods.MaxLoginCodeDays)
                .WithMessage(_ => ValidationMessages.NumberBetween);
            RuleFor(request => request.Retention!.InvitationDays)
                .InclusiveBetween(IdentityRetentionPeriods.MinInvitationDays, IdentityRetentionPeriods.MaxInvitationDays)
                .WithMessage(_ => ValidationMessages.NumberBetween);
        });
    }

    private static bool HasSettings(UpdateSystemSettingsSectionRequest request) =>
        request.DefaultCulture is not null || request.DefaultTimeZoneId is not null
        || request.DefaultPageSize.HasValue || request.RegistrationMode.HasValue || request.Appearance is not null
        || request.Branding is not null || request.Retention is not null;

    private static bool IsKnownIanaZone(string? zone) => zone is not null
        && (zone == "UTC" || TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out _))
        && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _);
}
