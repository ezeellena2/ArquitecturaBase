using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormPdfDesignRequestValidator : AbstractValidator<SaveFormPdfDesignRequest>
{
    public SaveFormPdfDesignRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().WithMessage(_ => ErrorMessages.Get(FormPdfErrors.ContentInvalid.Code))
            .InclusiveBetween(0, 9007199254740990).WithMessage(_ => ErrorMessages.Get(FormPdfErrors.ContentInvalid.Code));
        RuleFor(request => request.Design).NotNull().WithMessage(_ => ErrorMessages.Get(FormPdfErrors.ContentInvalid.Code));
    }
}
internal sealed class PreviewFormPdfDesignRequestValidator : AbstractValidator<PreviewFormPdfDesignRequest>
{
    public PreviewFormPdfDesignRequestValidator()
    {
        RuleFor(request => request.Design).NotNull().WithMessage(_ => ErrorMessages.Get(FormPdfErrors.ContentInvalid.Code));
        RuleFor(request => request.Culture).Must(culture => culture is "es" or "en")
            .WithMessage(_ => ErrorMessages.Get(FormPreviewErrors.CultureInvalid.Code));
    }
}
