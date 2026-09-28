using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

internal sealed class CreateWidgetRequestValidator : AbstractValidator<CreateWidgetRequest>
{
    public CreateWidgetRequestValidator() =>
        RuleFor(request => request.Name).Required().MaxLength(Widget.NameMaxLength);
}
