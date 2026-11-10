using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class ListFormDefinitionsRequestValidator : PagedRequestValidator<ListFormDefinitionsRequest>
{
    public ListFormDefinitionsRequestValidator() : base(ListFormDefinitionsRequest.SortableFields)
    {
        RuleFor(x => x.PublicationFilter).Must(x => x is "Active" or "Published" or "Drafts" or "Deactivated").WithMessage(_ => ErrorMessages.Get(FormDefinitionErrors.ContentInvalid.Code));
        RuleFor(request => request.FolderId).Must((request, folderId) => folderId != Guid.Empty && !(folderId is not null && request.WithoutFolder))
            .WithMessage(_ => ErrorMessages.Get(FormFolderErrors.RequestInvalid.Code));
    }
}
