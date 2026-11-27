using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;

internal sealed class AiUsageRequestValidator : AbstractValidator<AiUsageRequest>
{
    public AiUsageRequestValidator()
    {
        // A year past 9998 has no next month to end it.
        RuleFor(x => x.Month).Must(month => BusinessMonth.TryParse(month, out var value) && value.Year is >= 2000 and <= 9998)
            .WithMessage(_ => ValidationMessages.AiUsageMonthInvalid);
    }
}

internal sealed class UpdateAiModelPriceRequestValidator : AbstractValidator<UpdateAiModelPriceRequest>
{
    public UpdateAiModelPriceRequestValidator()
    {
        RuleFor(x => x.Provider).Must(AiProviders.IsSupported).WithMessage(_ => ValidationMessages.AiProviderInvalid);
        RuleFor(x => x.Model).Must(AiModelPrice.IsValidModel).WithMessage(_ => ValidationMessages.AiModelInvalid);
        RuleFor(x => x.InputUsdPerMillion).Must(AiModelPrice.IsValidTokenPrice).WithMessage(_ => ValidationMessages.AiModelTokenPriceInvalid);
        RuleFor(x => x.OutputUsdPerMillion).Must(AiModelPrice.IsValidTokenPrice).WithMessage(_ => ValidationMessages.AiModelTokenPriceInvalid);
        RuleFor(x => x.AudioUsdPerMinute).Must(AiModelPrice.IsValidAudioPrice).WithMessage(_ => ValidationMessages.AiModelAudioPriceInvalid);
        RuleFor(x => x.Revision).Must(revision => revision is null or > 0).WithMessage(_ => ValidationMessages.AiRevisionInvalid);
    }
}
