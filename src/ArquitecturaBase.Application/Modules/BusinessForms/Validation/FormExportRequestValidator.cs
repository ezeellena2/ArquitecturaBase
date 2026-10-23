using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
internal sealed class FormExportRequestValidator : AbstractValidator<FormExportRequest>
{
    public FormExportRequestValidator()
    {
        RuleFor(request => request.Filter).NotNull();
        RuleFor(request => request.Columns).Must(columns => columns is null || columns.Count <= 16380 && columns.All(id => id != Guid.Empty) && columns.Distinct().Count() == columns.Count);
        RuleFor(request => request.CatalogFields).Must(fields => fields is null || fields.Count <= 16380
            && fields.All(field => field is not null && field.QuestionId != Guid.Empty && field.FieldId != Guid.Empty) && fields.Distinct().Count() == fields.Count);
    }
}
