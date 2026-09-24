using ArquitecturaBase.Application.Common.Validation;
using FluentValidation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record CreateWidgetRequest(string? Name);

internal sealed class CreateWidgetRequestValidator : AbstractValidator<CreateWidgetRequest>
{
    public CreateWidgetRequestValidator() =>
        RuleFor(request => request.Name).Required().MaxLength(Widget.NameMaxLength);
}
