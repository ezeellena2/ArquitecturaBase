using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class FormExportRequestValidator : AbstractValidator<FormExportRequest>
{
    public FormExportRequestValidator()
    {
        RuleFor(request => request.Filter).NotNull().WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.Columns).Must(columns => columns is null || columns.Count <= 16380 && columns.All(id => id != Guid.Empty) && columns.Distinct().Count() == columns.Count)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.CatalogFields).Must(fields => fields is null || fields.Count <= 16380
            && fields.All(field => field is not null && field.QuestionId != Guid.Empty && field.FieldId != Guid.Empty) && fields.Distinct().Count() == fields.Count)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
        RuleFor(request => request.ResultColumns).Must(ids => ids is null || ids.Count <= 16380
            && ids.All(id => !string.IsNullOrWhiteSpace(id)) && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count)
            .WithMessage(_ => ErrorMessages.Get(FormRecordErrors.FilterInvalid.Code));
    }
}
