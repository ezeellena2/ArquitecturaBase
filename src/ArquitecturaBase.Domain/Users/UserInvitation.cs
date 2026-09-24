using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.WhatsApp;

namespace ArquitecturaBase.Domain.Users;

/// <summary>
/// Una invitación que mandó un administrador (sección 6.6 del spec del ingreso con WhatsApp): por correo, un botón a
/// /login; por WhatsApp, la plantilla con «Quiero entrar». No lleva nada que sirva para entrar, así que no se guarda
/// ningún token: solo quién la mandó, cuándo y por dónde. Por WhatsApp hace falta el consentimiento de la persona, y queda
/// guardado quién lo confirmó y cuándo. Una invitación por WhatsApp guarda además el id que le dio Meta, para leer su
/// estado de entrega: la plantilla es de marketing, y Meta limita cuántas recibe cada persona, así que puede no llegar.
/// </summary>
public sealed class UserInvitation : AggregateRoot
{
    /// <summary>
    /// La espera mínima entre dos invitaciones a la misma cuenta: protege a quien las recibe de que le lleguen varias
    /// seguidas, y a la cuenta de WhatsApp del costo de cada plantilla.
    /// </summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    /// <summary>El largo del id de Meta, el mismo que el de los mensajes guardados.</summary>
    public const int MaxWaMessageIdLength = WhatsAppMessage.MaxWaMessageIdLength;

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

    /// <summary>Quién confirmó que la persona aceptó recibir mensajes por WhatsApp. Solo en una invitación por WhatsApp.</summary>
    public Guid? ConsentConfirmedBy { get; private set; }

    /// <summary>Cuándo lo confirmó. Solo en una invitación por WhatsApp.</summary>
    public DateTime? ConsentConfirmedAtUtc { get; private set; }

    /// <summary>
    /// El id que le dio Meta al mensaje (<c>wamid.…</c>), cuando la cola lo mandó. Con él se busca el mensaje guardado, que
    /// tiene el estado que avisa el webhook (enviado, entregado, leído o falló). Null mientras no salió.
    /// </summary>
    public string? WaMessageId { get; private set; }

    /// <summary>
    /// Si no se pudo mandar: la cola no la tomó, o Meta la rechazó al recibirla. A la persona no le llegó nada, así que no
    /// hace esperar a la próxima.
    /// </summary>
    public bool SendFailed { get; private set; }

    public static UserInvitation ByEmail(Guid userId, Guid sentBy, DateTime nowUtc)
    {
        Require(userId, sentBy);

        return new UserInvitation(userId, UserInvitationChannel.Email, sentBy, nowUtc, consentConfirmedBy: null, consentConfirmedAtUtc: null);
    }

    /// <summary>
    /// Por WhatsApp, quien la manda es quien confirma el consentimiento: sin él no se puede invitar (lo controla el caso
    /// de uso antes de llegar acá).
    /// </summary>
    public static UserInvitation ByWhatsApp(Guid userId, Guid sentBy, DateTime nowUtc)
    {
        Require(userId, sentBy);

        return new UserInvitation(userId, UserInvitationChannel.WhatsApp, sentBy, nowUtc, sentBy, nowUtc);
    }

    /// <summary>
    /// Le pone el id que devolvió Meta al mandarla. Conserva el primero: una invitación sale una sola vez, y un id
    /// distinto sería de otro mensaje.
    /// </summary>
    public void AttachWhatsAppMessage(string waMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waMessageId);

        if (Channel is not UserInvitationChannel.WhatsApp)
        {
            throw new InvalidOperationException("Only an invitation by WhatsApp has a WhatsApp message.");
        }

        if (waMessageId.Length > MaxWaMessageIdLength)
        {
            throw new ArgumentException($"The value cannot be longer than {MaxWaMessageIdLength} characters.", nameof(waMessageId));
        }

        WaMessageId ??= waMessageId;
    }

    public void MarkSendFailed() => SendFailed = true;

    /// <summary>
    /// Cuánto falta para poder mandarle otra a la misma cuenta, contando desde esta: cero si ya se puede. Una que no se
    /// pudo mandar no hace esperar.
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
