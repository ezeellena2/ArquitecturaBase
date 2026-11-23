namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// La retención de los mensajes de WhatsApp (sección 6.5 del spec), de la sección <c>WhatsApp</c>. Va aparte de
/// <see cref="WhatsAppOptions"/> a propósito: esas se registran y se validan solo con WhatsApp prendido, y la retención
/// corre siempre, porque la tabla puede tener mensajes de antes de apagarlo. El plazo ya no está acá: es «Conservar el contenido
/// de los mensajes» de Configuración › WhatsApp (RC3-A, 2026-11-22), que se lee de la base en cada corrida.
/// </summary>
internal sealed class WhatsAppMessageRetentionOptions
{
    public const string SectionName = WhatsAppOptions.SectionName;

    /// <summary>
    /// Si la retención corre sola, en segundo plano. Apagada, los textos esperan a que alguien llame a
    /// <see cref="WhatsAppMessageRetentionService.ClearExpiredTextsAsync"/>: la apagan los tests de integración, que
    /// comparten la base y la llaman cuando quieren. Fuera de los tests queda prendida, porque es lo que cumple la política.
    /// </summary>
    public bool ApplyMessageRetentionInBackground { get; init; } = true;
}
