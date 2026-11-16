using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Configuration.Auth;

/// <summary>
/// N7: los topes diarios de lo que sale por correo con un código o una invitación, de la sección <c>Email</c>, como
/// <c>WhatsApp:DailyAuthCodeLimit</c> para su canal. Infrastructure lee la misma sección para mandar los correos; acá
/// solo están los topes. Todos cuentan en una ventana móvil de 24 horas y leen la base, así valen para todas las copias.
/// </summary>
public sealed class EmailQuotaOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Cuántos correos con un código o una invitación pueden salir en 24 horas, entre todos. Va por debajo del cupo diario
    /// de la cuenta que manda (Gmail da unos 500 a una cuenta gratuita, despliegue.md), que también gastan los otros
    /// correos del sistema. Los códigos frenan antes, en este tope menos <see cref="DailyInvitationReserve"/>; las
    /// invitaciones llegan hasta acá. Al 80 % y al llegar queda un aviso en el log.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Email:DailyLimit must be greater than 0.")]
    public int DailyLimit { get; init; } = 400;

    /// <summary>
    /// Cuántos lugares de <see cref="DailyLimit"/> quedan sólo para las invitaciones: los códigos frenan en el tope menos
    /// esta reserva (350 con los valores por defecto), así un abuso anónimo de los códigos no deja a los administradores sin
    /// invitar. No dice nada de nadie, porque invitar es sólo del administrador. Va por debajo de <see cref="DailyLimit"/>
    /// (se valida al arrancar); 0 la apaga.
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "Email:DailyInvitationReserve cannot be negative.")]
    public int DailyInvitationReserve { get; init; } = 50;

    /// <summary>
    /// Cuántos códigos por correo se pueden pedir desde una misma dirección (una IPv6, por su /64) en 24 horas: para
    /// entrar o desde el perfil, a cualquier destino, hayan salido o no (en InviteOnly, uno a un correo sin cuenta no sale),
    /// así el tope no dice qué correos tienen cuenta. Sin él, una sola dirección gasta el tope de todos.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Email:DailyCodeLimitPerAddress must be greater than 0.")]
    public int DailyCodeLimitPerAddress { get; init; } = 50;

    /// <summary>Cuántos códigos por correo puede pedir una cuenta desde su perfil en 24 horas: cuenta los que salieron.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Email:DailyProfileCodeLimitPerAccount must be greater than 0.")]
    public int DailyProfileCodeLimitPerAccount { get; init; } = 10;

    /// <summary>Cuántas invitaciones por correo puede mandar un administrador en 24 horas.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Email:DailyInvitationLimitPerAdministrator must be greater than 0.")]
    public int DailyInvitationLimitPerAdministrator { get; init; } = 100;
}
