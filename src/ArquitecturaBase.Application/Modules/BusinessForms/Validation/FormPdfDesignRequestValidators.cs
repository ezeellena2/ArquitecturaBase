using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class SaveFormPdfDesignRequestValidator : AbstractValidator<SaveFormPdfDesignRequest>
{
    public SaveFormPdfDesignRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().InclusiveBetween(0, 9007199254740990);
        RuleFor(request => request.Design).NotNull();
    }
}
internal sealed class PreviewFormPdfDesignRequestValidator : AbstractValidator<PreviewFormPdfDesignRequest>
{
    public PreviewFormPdfDesignRequestValidator()
    {
        RuleFor(request => request.Design).NotNull();
        RuleFor(request => request.Culture).Must(culture => culture is "es" or "en");
    }
}
