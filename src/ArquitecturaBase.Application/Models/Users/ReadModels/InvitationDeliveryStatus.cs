namespace ArquitecturaBase.Application.Models.Users.ReadModels;

/// <summary>
/// Cómo va una invitación por WhatsApp, para que el admin vea si llegó (sección 12 del spec del ingreso con WhatsApp): la
/// plantilla es de marketing, y Meta limita cuántas de esas recibe cada persona. Viaja por su nombre.
/// </summary>
public enum InvitationDeliveryStatus
{
    /// <summary>Todavía no salió, o Meta la aceptó y no avisó nada más.</summary>
    Pending = 1,

    /// <summary>Salió de los servidores de Meta (un tilde).</summary>
    Sent = 2,

    /// <summary>Llegó al celular (dos tildes).</summary>
    Delivered = 3,

    /// <summary>La persona la vio (dos tildes azules).</summary>
    Read = 4,

    /// <summary>No salió (la cola no la tomó, o Meta la rechazó) o Meta avisó que no la pudo entregar.</summary>
    Failed = 5,
}
