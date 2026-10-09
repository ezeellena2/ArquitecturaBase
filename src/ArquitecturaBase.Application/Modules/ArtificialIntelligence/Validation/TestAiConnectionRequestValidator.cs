using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
internal sealed class TestAiConnectionRequestValidator : AbstractValidator<TestAiConnectionRequest>
{
    public TestAiConnectionRequestValidator()
    {
        RuleFor(x => x.Provider).IsInEnum().WithMessage(_ => ValidationMessages.AiProviderInvalid);
        RuleFor(x => x.Model).Cascade(CascadeMode.Stop).Required().Matches(@"^[A-Za-z0-9][A-Za-z0-9._:/-]{0,149}$")
            .WithMessage(_ => ValidationMessages.AiModelInvalid);
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.AiRevisionInvalid);
        RuleFor(x => x.CredentialChange).Must(x => x is AiCredentialChange.Keep or AiCredentialChange.Replace)
            .WithMessage(_ => ValidationMessages.AiCredentialInvalid);
        RuleFor(x => x.ApiKey).Must((x, key) => x.CredentialChange == AiCredentialChange.Replace
            ? key is { Length: > 0 and <= 4096 } && key.All(c => c is >= '!' and <= '~') : key is null)
            .WithMessage(_ => ValidationMessages.AiCredentialInvalid);
    }
}
