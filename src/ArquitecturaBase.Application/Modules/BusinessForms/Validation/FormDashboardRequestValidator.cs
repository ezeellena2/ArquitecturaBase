using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;
/// <summary>The dashboard needs the sheet's filter, validated as the sheet's; a period other than day, week or month is its own error.</summary>
internal sealed class FormDashboardRequestValidator : AbstractValidator<FormDashboardRequest>
{
    public FormDashboardRequestValidator() => RuleFor(request => request.Filter).NotNull();
}
