using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// Un canal por el que un administrador manda una invitación. Uno por valor de <see cref="UserInvitationChannel"/>: el
/// de correo lo trae el núcleo y el de WhatsApp, su módulo. Sin adaptador para el canal pedido, la invitación se rechaza
/// en el campo del canal. No abre ni confirma transacciones.
/// </summary>
public interface IInvitationChannel
{
    UserInvitationChannel Channel { get; }

    /// <summary>Si la invitación guarda quién confirmó el consentimiento de la persona y cuándo (WhatsApp sí, correo no).</summary>
    bool RecordsConsent { get; }

    /// <summary>
    /// Las reglas del canal, antes de guardar o encolar nada: no lee, no escribe ni toma locks. En el alta corre antes de
    /// los locks; en el reenvío, con el lock user-invitation: de la cuenta ya tomado y la cuenta leída. Devuelve el error
    /// ya atado a su campo (<see cref="InvitationCheck"/> trae los nombres de los campos del alta o del reenvío).
    /// </summary>
    Result Check(InvitationCheck check);

    /// <summary>
    /// Encola el mensaje de <paramref name="invitation"/>, ya agregada al repositorio, en el idioma
    /// <paramref name="culture"/> de la cuenta. Si la cola no lo toma, la marca como no enviada (MarkSendFailed) y lo
    /// registra sin datos personales: el alta no se deshace por un envío que falló. Adentro del límite y del lock
    /// user-invitation: que tomó quien llama, antes del commit.
    /// </summary>
    void Enqueue(UserAccount user, UserInvitation invitation, string culture);
}
