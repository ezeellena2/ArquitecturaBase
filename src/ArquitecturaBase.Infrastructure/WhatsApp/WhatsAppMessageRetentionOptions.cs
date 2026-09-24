using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// La retención de los mensajes de WhatsApp (sección 6.5 del spec), de la sección <c>WhatsApp</c>. Va aparte de
/// <see cref="WhatsAppOptions"/> a propósito: esas se registran y se validan solo con WhatsApp prendido, y la retención
/// corre siempre, porque la tabla puede tener mensajes de antes de apagarlo. Se valida al arrancar, esté WhatsApp prendido
/// o no, como <c>WhatsAppLoginOptions</c>: los valores por defecto son válidos.
/// </summary>
internal sealed class WhatsAppMessageRetentionOptions
{
    public const string SectionName = WhatsAppOptions.SectionName;

    /// <summary>
    /// A los cuántos días se borra el texto de un mensaje, entrante o saliente. La política de privacidad promete 90:
    /// cambiar este valor pide cambiar antes la política. Como mucho, 10 años.
    /// </summary>
    [Range(1, 3650, ErrorMessage = "WhatsApp:MessageRetentionDays must be between {1} and {2}.")]
    public int MessageRetentionDays { get; init; } = 90;

    /// <summary>
    /// Si la retención corre sola, en segundo plano. Apagada, los textos esperan a que alguien llame a
    /// <see cref="WhatsAppMessageRetentionService.ClearExpiredTextsAsync"/>: la apagan los tests de integración, que
    /// comparten la base y la llaman cuando quieren. Fuera de los tests queda prendida, porque es lo que cumple la política.
    /// </summary>
    public bool ApplyMessageRetentionInBackground { get; init; } = true;
}
