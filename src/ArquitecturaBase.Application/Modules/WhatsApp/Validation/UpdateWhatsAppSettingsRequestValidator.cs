using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using FluentValidation;
using static ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppSettings;
using Messages = ArquitecturaBase.Application.Modules.WhatsApp.Validation.WhatsAppAssistantAgentValidationMessages;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

/// <summary>
/// Says which value of Configuración › WhatsApp is wrong, with its path; the ranges are the entity's (<see cref="WhatsAppSettings"/>),
/// which checks them again and has the last word.
/// </summary>
internal sealed class UpdateWhatsAppSettingsRequestValidator : AbstractValidator<UpdateWhatsAppSettingsRequest>
{
    public UpdateWhatsAppSettingsRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.SettingsRevisionInvalid);
        RuleFor(request => request.FlowSessionLifetimeHours).InclusiveBetween(MinFlowSessionLifetimeHours, MaxFlowSessionLifetimeHours)
            .WithMessage(_ => Messages.OutOfRange(MinFlowSessionLifetimeHours, MaxFlowSessionLifetimeHours));
        RuleFor(request => request.MessageRetentionDays).InclusiveBetween(MinMessageRetentionDays, MaxMessageRetentionDays)
            .WithMessage(_ => Messages.OutOfRange(MinMessageRetentionDays, MaxMessageRetentionDays));
        RuleFor(request => request.AllowedCountries).NotEmpty().WithMessage(_ => ValidationMessages.Required);
        RuleFor(request => request.AllowedCountries).Must(countries => countries is null || countries.Count <= MaxAllowedCountries)
            .WithMessage(_ => Messages.TooMany(MaxAllowedCountries));
        RuleForEach(request => request.AllowedCountries).Must(IsCountryCode).WithMessage(_ => ValidationMessages.CountryInvalid);
    }
}
