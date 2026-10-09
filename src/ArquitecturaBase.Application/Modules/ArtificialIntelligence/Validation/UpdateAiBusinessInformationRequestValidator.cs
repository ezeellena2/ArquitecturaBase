using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using FluentValidation;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
internal sealed class UpdateAiBusinessInformationRequestValidator : AbstractValidator<UpdateAiBusinessInformationRequest>
{
    public UpdateAiBusinessInformationRequestValidator()
    {
        RuleFor(x => x.Name).Required().MaximumLength(120).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Description).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(1000).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Hours).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(500).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Location).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(500).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Services).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(2000).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Policies).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(2000).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Faq).NotNull().WithMessage(_ => ValidationMessages.Required).MaximumLength(4000).WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.Description).Must((x, _) => new[] { x.Description, x.Hours, x.Location, x.Services, x.Policies, x.Faq }.Any(v => !string.IsNullOrWhiteSpace(v)))
            .When(x => x.Details?.Normalize() is not { IsSuccess: true, Value: var details } || !details.HasConfirmedFacts())
            .WithMessage(_ => AiBusinessInformationValidationMessages.ContentRequired);
        RuleFor(x => x.Details).Custom((details, context) => {
            if (details is null) return;
            var result = details.Normalize();
            if (result.IsSuccess) return;
            var field = result.Error == AiBusinessInformationErrors.InvalidLocation ? "Details.Location"
                : result.Error == AiBusinessInformationErrors.InvalidHours ? "Details.OpeningHours" : "Details.Faq";
            context.AddFailure(field, AiBusinessInformationValidationMessages.DetailsInvalid);
        });
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.AiRevisionInvalid);
        RuleFor(x => x.RequestId).NotEmpty().WithMessage(_ => ValidationMessages.Required);
    }
}
