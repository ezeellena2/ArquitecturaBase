using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;
internal sealed class SearchBusinessPlacesRequestValidator : AbstractValidator<SearchBusinessPlacesRequest>
{
    public SearchBusinessPlacesRequestValidator()
    {
        RuleFor(x => x.Input).Must(input => input is not null && input.Trim().Length is >= 3 and <= 200 && !input.Any(char.IsControl))
            .WithMessage(_ => ValidationMessages.AiPlaceSearchInvalid);
    }
}
