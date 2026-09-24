using ArquitecturaBase.Application.Interfaces.Integrations;
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

    /// <summary>
    /// Obligatorio y con la forma de un código de ingreso: <paramref name="length"/> dígitos. Si es el correcto lo decide
    /// el caso de uso; <paramref name="message"/> le dice a la persona por dónde le llegó.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ValidLoginCode<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder,
        int length,
        Func<T, string> message) =>
        ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .Must(code => code!.Length == length && code.All(char.IsAsciiDigit)).WithMessage(message);

    /// <summary>
    /// Opcional: el país elegido para leer un número que no empieza con "+", en ISO 3166-1 alfa-2 ("AR"). Las
    /// minúsculas las acepta el parser. Si el número es un celular, y de qué país, lo decide el caso de uso.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> OptionalCountry<T>(this IRuleBuilderInitial<T, string?> ruleBuilder) =>
        ruleBuilder
            .Must(country => country is null || (country is { Length: 2 } && country.All(char.IsAsciiLetter)))
            .WithMessage(_ => ValidationMessages.CountryInvalid);

    /// <summary>
    /// Obligatorio y con la forma de un token de <see cref="ISecureTokenGenerator"/>: 43 caracteres de base64url. Uno
    /// con esa forma que no existe no es un error de validación, sino el mismo <c>Auth.LoginLink.Invalid</c> de un
    /// enlace vencido o usado: eso lo decide el caso de uso.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ValidLoginLinkToken<T>(this IRuleBuilderInitial<T, string?> ruleBuilder) =>
        ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage(_ => ValidationMessages.Required)
            .Must(ISecureTokenGenerator.HasTokenFormat).WithMessage(_ => ValidationMessages.LoginLinkTokenFormat);

    /// <summary>Todos los permisos pedidos tienen que estar en el catálogo de Domain.</summary>
    public static IRuleBuilderOptions<T, IReadOnlyCollection<string>?> ValidPermissions<T>(
        this IRuleBuilder<T, IReadOnlyCollection<string>?> ruleBuilder) =>
        ruleBuilder
            .Must(permissions => permissions is null
                || permissions.All(permission => Permissions.All.Contains(permission, StringComparer.Ordinal)))
            .WithMessage(_ => ValidationMessages.PermissionUnknown);
}
