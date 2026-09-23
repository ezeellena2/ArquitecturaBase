using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>
/// Lo que el ingreso con WhatsApp necesita de la sección <c>WhatsApp</c> (secciones 13 y 14 del spec del ingreso con
/// WhatsApp). Infrastructure lee la misma sección para mandar los mensajes; acá solo están los topes y lo que se le
/// muestra a la persona. Se valida al arrancar, esté WhatsApp prendido o no: los valores por defecto son válidos.
/// </summary>
public sealed class WhatsAppLoginOptions
{
    public const string SectionName = "WhatsApp";

    public const string AllowedCountriesError =
        "WhatsApp:AllowedCountries must list ISO 3166-1 alpha-2 country codes in upper case, like AR.";

    /// <summary>Sin <see cref="AllowedCountries"/>, solo Argentina.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedCountries = ["AR"];

    /// <summary>
    /// A qué países se mandan códigos, en ISO 3166-1 alfa-2 y en mayúsculas ("AR"), tal como vienen de la
    /// configuración. Cada código se paga, con una tarifa por país (sección 17 del spec). No tiene un valor inicial a
    /// propósito: el binder de la configuración suma los elementos de la sección a los que la lista ya tiene, y con
    /// "AR" de entrada, configurar "AR" y "UY" daría "AR", "AR", "UY". El valor por defecto lo pone
    /// <see cref="Countries"/>, que es lo que se usa.
    /// </summary>
    public IReadOnlyList<string>? AllowedCountries { get; init; }

    /// <summary>Los países permitidos: los de <see cref="AllowedCountries"/> o, si no hay ninguno, solo Argentina.</summary>
    public IReadOnlyList<string> Countries => AllowedCountries is { Count: > 0 } ? AllowedCountries : DefaultAllowedCountries;

    /// <summary>
    /// Cuántos códigos pueden salir por WhatsApp en 24 horas (una ventana móvil), entre todos los números: acota el
    /// costo si alguien abusa del formulario con números ajenos.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "WhatsApp:DailyAuthCodeLimit must be greater than 0.")]
    public int DailyAuthCodeLimit { get; init; } = 100;

    /// <summary>
    /// El número del bot con su código de país y solo con dígitos ("15551632662"), para el enlace "Volver a WhatsApp"
    /// (<c>https://wa.me/…</c>). Sin él, la pantalla no ofrece el enlace.
    /// </summary>
    [RegularExpression(
        "^[1-9][0-9]{7,14}$",
        ErrorMessage = "WhatsApp:DisplayPhoneNumber must be the number of the bot with its country code and only digits, like 15551632662.")]
    public string? DisplayPhoneNumber { get; init; }

    internal bool HasValidCountries() =>
        Countries.All(country => country is { Length: 2 } && char.IsAsciiLetterUpper(country[0]) && char.IsAsciiLetterUpper(country[1]));
}
