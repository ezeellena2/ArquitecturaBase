using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>Reglas reutilizables con mensajes traducidos desde Validation.resx.</summary>
public static class ValidationRules
{
    public const int EmailMaxLength = 254;

    /// <summary>Tiene que coincidir con ApplicationUser.DisplayNameMaxLength: Application no ve Infrastructure.</summary>
    public const int DisplayNameMaxLength = 100;

    public static IRuleBuilderOptions<T, TProperty> Required<T, TProperty>(this IRuleBuilder<T, TProperty> ruleBuilder) =>
        ruleBuilder.NotEmpty().WithMessage(_ => ValidationMessages.Required);

    public static IRuleBuilderOptions<T, string?> MaxLength<T>(this IRuleBuilder<T, string?> ruleBuilder, int maxLength) =>
        ruleBuilder.MaximumLength(maxLength).WithMessage(_ => ValidationMessages.MaxLength);

    /// <summary>Obligatorio y con formato de correo. Va primero en la cadena: <c>RuleFor(x => x.Email).ValidEmail()</c>.</summary>
    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilderInitial<T, string?> ruleBuilder) =>
        ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .MaximumLength(EmailMaxLength).WithMessage(_ => ValidationMessages.EmailInvalid)
            .EmailAddress().WithMessage(_ => ValidationMessages.EmailInvalid);
}
