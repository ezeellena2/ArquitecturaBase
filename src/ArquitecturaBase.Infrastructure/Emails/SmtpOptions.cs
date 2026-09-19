using System.ComponentModel.DataAnnotations;
using MailKit.Security;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Cuenta que envía los emails (Email:Smtp, sección 6.8). La contraseña es una contraseña de aplicación de Gmail y
/// va en user-secrets o en variables de entorno. Remitente: FromName y FromAddress, también para los .eml.
/// </summary>
internal sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string Host { get; init; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    public SecureSocketOptions Security { get; init; } = SecureSocketOptions.StartTls;

    [Required]
    public string UserName { get; init; } = string.Empty;

    [Required]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string FromName { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string FromAddress { get; init; } = string.Empty;
}
