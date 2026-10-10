using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class ListFormDefinitionsRequestValidator : PagedRequestValidator<ListFormDefinitionsRequest>
{
    public ListFormDefinitionsRequestValidator() : base(ListFormDefinitionsRequest.SortableFields) =>
        RuleFor(x => x.PublicationFilter).Must(x => x is "All" or "Published" or "Drafts").WithMessage(_ => ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
}
