using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Una invitación que mandó un administrador (sección 6.6 del spec del ingreso con WhatsApp): por correo, un botón a
/// /login; por WhatsApp, la plantilla con «Quiero entrar». No lleva nada que sirva para entrar, así que no se guarda
/// ningún token: solo quién la mandó, cuándo y por dónde. Si el canal lo pide (WhatsApp), hace falta el consentimiento de
/// la persona, y queda guardado quién lo confirmó y cuándo. Una invitación que salió guarda además el id de su mensaje: por
/// WhatsApp, el que le dio Meta, para leer su estado (la plantilla es de marketing, y Meta limita cuántas recibe cada
/// persona, así que puede no llegar); por correo, el Message-ID, que dice que el servidor la aceptó.
/// </summary>
public sealed class UserInvitation : Entity
{
    /// <summary>
    /// La espera mínima entre dos invitaciones a la misma cuenta: protege a quien las recibe de que le lleguen varias
    /// seguidas, y a la cuenta de WhatsApp del costo de cada plantilla.
    /// </summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    /// <summary>El largo del id que da el proveedor al mensaje (con WhatsApp, el de Meta, que tiene el mismo tope).</summary>
    public const int MaxProviderMessageIdLength = 256;

    // Para EF Core.
    private UserInvitation()
    {
    }

    private UserInvitation(
        Guid userId,
        UserInvitationChannel channel,
        Guid sentBy,
        DateTime sentAtUtc,
        Guid? consentConfirmedBy,
        DateTime? consentConfirmedAtUtc)
    {
        UserId = userId;
        Channel = channel;
        SentBy = sentBy;
        SentAtUtc = sentAtUtc;
        ConsentConfirmedBy = consentConfirmedBy;
        ConsentConfirmedAtUtc = consentConfirmedAtUtc;
    }

    /// <summary>La cuenta invitada. Como en los enlaces y los códigos, sin clave foránea: las cuentas no se borran de verdad.</summary>
    public Guid UserId { get; private set; }

    public UserInvitationChannel Channel { get; private set; }

    /// <summary>Cuándo la mandó el administrador. Es la hora de la que se cuenta la espera hasta la próxima.</summary>
    public DateTime SentAtUtc { get; private set; }

    /// <summary>El administrador que la mandó.</summary>
    public Guid SentBy { get; private set; }

    /// <summary>
    /// Quién confirmó que la persona aceptó recibir mensajes por el canal. Solo si el canal lo pide (WhatsApp sí, correo no).
    /// </summary>
    public Guid? ConsentConfirmedBy { get; private set; }

    /// <summary>Cuándo lo confirmó. Solo si el canal lo pide.</summary>
    public DateTime? ConsentConfirmedAtUtc { get; private set; }

    /// <summary>
    /// El id del mensaje, cuando la cola lo mandó: con WhatsApp, el <c>wamid.…</c> que le dio Meta, con el que la fuente del
    /// estado de entrega del canal busca lo que avisó el proveedor (enviado, entregado, leído o falló); por correo, el
    /// Message-ID con el que el servidor lo aceptó (N5), que dice que salió. Null mientras no salió.
    /// </summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>
    /// Si no se pudo mandar: la cola no la tomó, el proveedor la rechazó al recibirla (Meta, o el servidor de correo con un
    /// 5xx) o venció en la cola sin salir (N5). A la persona no le llegó nada, así que no hace esperar a la próxima.
    /// </summary>
    public bool SendFailed { get; private set; }

    /// <summary>
    /// H7 y N8: no se sabe si salió (el proveedor pudo haberla recibido: un timeout, una respuesta o una conexión cortada
    /// mientras salía). No figura como fallida: el administrador revisa con la persona antes de mandar otra; como pudo haber
    /// llegado, la próxima espera el minuto.
    /// </summary>
    public bool SendUncertain { get; private set; }

    /// <summary>
    /// La invitación que manda <paramref name="sentBy"/> por <paramref name="channel"/>. Si el canal pide el consentimiento
    /// de la persona (<paramref name="consentConfirmed"/>), quien la manda es quien lo confirma: sin él no se puede invitar
    /// por ese canal, y eso lo controla el canal antes de llegar acá.
    /// </summary>
    public static UserInvitation Send(Guid userId, UserInvitationChannel channel, Guid sentBy, DateTime nowUtc, bool consentConfirmed)
    {
        Require(userId, sentBy);

        return consentConfirmed
            ? new UserInvitation(userId, channel, sentBy, nowUtc, sentBy, nowUtc)
            : new UserInvitation(userId, channel, sentBy, nowUtc, consentConfirmedBy: null, consentConfirmedAtUtc: null);
    }

    /// <summary>Por correo, que no guarda consentimiento.</summary>
    public static UserInvitation ByEmail(Guid userId, Guid sentBy, DateTime nowUtc) =>
        Send(userId, UserInvitationChannel.Email, sentBy, nowUtc, consentConfirmed: false);

    /// <summary>
    /// Le pone el id del mensaje con el que salió. Conserva el primero: una invitación sale una sola vez, y un id distinto
    /// sería de otro mensaje.
    /// </summary>
    public void AttachProviderMessage(string providerMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerMessageId);

        if (providerMessageId.Length > MaxProviderMessageIdLength)
        {
            throw new ArgumentException(
                $"The value cannot be longer than {MaxProviderMessageIdLength} characters.", nameof(providerMessageId));
        }

        ProviderMessageId ??= providerMessageId;
    }

    public void MarkSendFailed() => SendFailed = true;

    public void MarkSendUncertain() => SendUncertain = true;

    /// <summary>
    /// Cuánto falta para poder mandarle otra a la misma cuenta, contando desde esta: cero si ya se puede. Una que no se
    /// pudo mandar no hace esperar; una sin confirmar sí, porque pudo haber llegado (H7).
    /// </summary>
    public TimeSpan WaitBeforeAnother(DateTime nowUtc)
    {
        if (SendFailed)
        {
            return TimeSpan.Zero;
        }

        var wait = SentAtUtc + ResendCooldown - nowUtc;

        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    private static void Require(Guid userId, Guid sentBy)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("An invitation needs the account it invites.", nameof(userId));
        }

        if (sentBy == Guid.Empty)
        {
            throw new ArgumentException("An invitation needs the administrator who sends it.", nameof(sentBy));
        }
    }
}
