using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Features.Auth.GetLoginMethods;

/// <summary>
/// Los medios de ingreso activos (sección 10 del spec del ingreso con WhatsApp). El correo no figura: está siempre.
/// Los nombres del JSON son los del contrato ("whatsapp" y no "whatsApp", que es lo que daría el camelCase de estas
/// propiedades).
/// </summary>
/// <param name="Google">Si está configurado el ingreso con Google.</param>
/// <param name="WhatsApp">Si está configurado WhatsApp. Apagado, no hay países ni número.</param>
/// <param name="WhatsAppCountries">Los países a los que se mandan códigos, en ISO 3166-1 alfa-2.</param>
/// <param name="WhatsAppNumber">El número del bot, solo con dígitos, para el enlace "Volver a WhatsApp", o null.</param>
public sealed record LoginMethodsResponse(
    bool Google,
    [property: JsonPropertyName("whatsapp")] bool WhatsApp,
    [property: JsonPropertyName("whatsappCountries")] IReadOnlyList<string> WhatsAppCountries,
    [property: JsonPropertyName("whatsappNumber")] string? WhatsAppNumber);
