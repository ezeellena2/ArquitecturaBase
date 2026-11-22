using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
internal sealed class UpdateAiConnectionRequestValidator : AbstractValidator<UpdateAiConnectionRequest>
{
    public UpdateAiConnectionRequestValidator(AiAssistantLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        // RC1-A: from one day to the installation's ceiling, with the range in the request's language.
        RuleFor(x => x.ConversationRetentionDays).InclusiveBetween(1, limits.RetentionDays).When(x => x.ConversationRetentionDays.HasValue)
            .WithMessage(_ => ValidationMessages.NumberBetween);
        RuleFor(x => x.Provider).IsInEnum().WithMessage(_ => ValidationMessages.AiProviderInvalid);
        RuleFor(x => x.Model).Cascade(CascadeMode.Stop).Required().Matches(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,149}$")
            .WithMessage(_ => ValidationMessages.AiModelInvalid);
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.AiRevisionInvalid);
        RuleFor(x => x.RequestId).NotEmpty().WithMessage(_ => ValidationMessages.Required);
        RuleFor(x => x.CredentialChange).IsInEnum().WithMessage(_ => ValidationMessages.AiCredentialInvalid);
        RuleFor(x => x.ApiKey).Must((x, key) => x.CredentialChange == AiCredentialChange.Replace
            ? key is { Length: > 0 and <= 4096 } && key.All(c => c is >= '!' and <= '~') : key is null)
            .WithMessage(_ => ValidationMessages.AiCredentialInvalid);
    }
}
