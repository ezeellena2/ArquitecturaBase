using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Validation;

// RC4-A: every number of Configuración › Formularios within its range, with the bounds in the message, in the numbers of the language.
internal sealed class UpdateFormSettingsRequestValidator : AbstractValidator<UpdateFormSettingsRequest>
{
    public UpdateFormSettingsRequestValidator()
    {
        RuleFor(request => request.ExpectedRevision).NotNull().WithMessage(_ => ValidationMessages.Required)
            .GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.Required);
        Range(request => request.MaxFileSizeMiB, BusinessFormsSettings.MaxFileSizeMiBRange);
        Range(request => request.DailyRereadsPerAsker, BusinessFormsSettings.DailyRereadsPerAskerRange);
        Range(request => request.SimilarDocumentsToSuggest, BusinessFormsSettings.SimilarDocumentsRange);
        Range(request => request.ProposedExpiryDays, BusinessFormsSettings.RetentionDaysRange);
        Range(request => request.DiscardedRetentionDays, BusinessFormsSettings.RetentionDaysRange);
    }

    private void Range(System.Linq.Expressions.Expression<Func<UpdateFormSettingsRequest, int?>> value, FormSettingRange range) =>
        RuleFor(value).NotNull().WithMessage(_ => FormSettingsTexts.OutOfRange(range.Minimum, range.Maximum))
            .Must(number => number is null || range.Contains(number.Value)).WithMessage(_ => FormSettingsTexts.OutOfRange(range.Minimum, range.Maximum));
}
