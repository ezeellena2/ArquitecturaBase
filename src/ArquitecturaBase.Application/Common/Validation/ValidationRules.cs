using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Authorization;
using FluentValidation;

namespace ArquitecturaBase.Application.Common.Validation;

/// <summary>Reglas reutilizables con mensajes traducidos desde Validation.resx.</summary>
public static class ValidationRules
{
    public const int EmailMaxLength = 254;

    /// <summary>Tiene que coincidir con ApplicationUser.DisplayNameMaxLength: Application no ve Infrastructure.</summary>
    public const int DisplayNameMaxLength = 100;

    /// <summary>La columna de Identity admite 256; 64 alcanza de sobra para un nombre de rol y se lee mejor.</summary>
    public const int RoleNameMaxLength = 64;

    /// <summary>Tiene que coincidir con ApplicationRole.DescriptionMaxLength.</summary>
    public const int RoleDescriptionMaxLength = 256;

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

    /// <summary>Todos los permisos pedidos tienen que estar en el catálogo de Domain.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyCollection<string>?> ValidPermissions<T>(
        this IRuleBuilder<T, IReadOnlyCollection<string>?> ruleBuilder) =>
        ruleBuilder
            .Must(permissions => permissions is null
                || permissions.All(permission => Permissions.All.Contains(permission, StringComparer.Ordinal)))
            .WithMessage(_ => ValidationMessages.PermissionUnknown);
}
