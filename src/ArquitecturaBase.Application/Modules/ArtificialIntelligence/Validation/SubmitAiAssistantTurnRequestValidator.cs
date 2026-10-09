using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;

internal sealed class SubmitAiAssistantTurnRequestValidator : AbstractValidator<SubmitAiAssistantTurnRequest>
{
    public SubmitAiAssistantTurnRequestValidator(AiAssistantLimits limits)
    {
        RuleFor(x => x.RequestId).NotEmpty().WithMessage(_ => ValidationMessages.Required);
        RuleFor(x => x.Question).Cascade(CascadeMode.Stop).Must(q => !string.IsNullOrWhiteSpace(q))
            .WithMessage(_ => ValidationMessages.Required).MaximumLength(limits.QuestionCharacters)
            .WithMessage(_ => ValidationMessages.MaxLength);
        RuleFor(x => x.ExpectedConnectionRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.AiRevisionInvalid);
        RuleFor(x => x).Must(x => x.ConversationId is null ? x.ExpectedRevision is null : x.ConversationId != Guid.Empty && x.ExpectedRevision >= 0)
            .WithMessage(_ => ValidationMessages.AiRevisionInvalid).OverridePropertyName("expectedRevision");
        RuleFor(x => x.Culture).Must(c => c is "es" or "en").WithMessage(_ => ValidationMessages.Required);
    }
}
