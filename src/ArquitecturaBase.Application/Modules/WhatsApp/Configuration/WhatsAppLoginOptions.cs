using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

/// <summary>
/// Lo que el ingreso con WhatsApp necesita de la sección <c>WhatsApp</c> (secciones 13 y 14 del spec del ingreso con
/// WhatsApp). Infrastructure lee la misma sección para mandar los mensajes; acá solo están los topes y lo que se le
/// muestra a la persona. Se valida al arrancar, esté WhatsApp prendido o no: los valores por defecto son válidos. Los países
/// que reciben códigos ya no están acá: son de Configuración › WhatsApp (RC3-A, 2026-11-22), leídos de la base en cada uso.
/// </summary>
public sealed class WhatsAppLoginOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// Cuántos códigos pueden salir por WhatsApp en 24 horas (una ventana móvil), entre todos los números: acota el
    /// costo si alguien abusa del formulario con números ajenos.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "WhatsApp:DailyAuthCodeLimit must be greater than 0.")]
    public int DailyAuthCodeLimit { get; init; } = 100;

    /// <summary>
    /// Cuántos códigos para vincular un número puede pedir una cuenta desde su perfil en 24 horas (una ventana móvil): sin él, una
    /// sola cuenta gasta el tope de todos con números ajenos (H10). Cuenta los que salieron, como el tope de todos.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "WhatsApp:DailyLinkCodeLimitPerAccount must be greater than 0.")]
    public int DailyLinkCodeLimitPerAccount { get; init; } = 10;

    /// <summary>
    /// Con el registro abierto, cuántos códigos de ingreso se pueden pedir desde una misma dirección en 24 horas (H10): cualquiera
    /// pide un código para cualquier número, y sin él una sola dirección gasta el tope de todos. Vive en la memoria de cada copia,
    /// como los otros límites por dirección (H26).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "WhatsApp:DailySignInCodeLimitPerAddress must be greater than 0.")]
    public int DailySignInCodeLimitPerAddress { get; init; } = 20;

    /// <summary>
    /// El número del bot con su código de país y solo con dígitos ("15551632662"), para abrir el chat desde el perfil
    /// (<c>https://wa.me/…</c>). Sin él, el perfil no ofrece abrirlo.
    /// </summary>
    [RegularExpression(
        "^[1-9][0-9]{7,14}$",
        ErrorMessage = "WhatsApp:DisplayPhoneNumber must be the number of the bot with its country code and only digits, like 15551632662.")]
    public string? DisplayPhoneNumber { get; init; }
}
