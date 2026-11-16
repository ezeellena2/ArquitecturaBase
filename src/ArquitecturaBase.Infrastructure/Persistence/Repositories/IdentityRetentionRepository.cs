using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

/// <summary>
/// N16: borra y limpia de a lotes, por el seguimiento de EF y el guardado del límite, sin ExecuteDelete ni ExecuteUpdate.
/// Ninguna de estas filas se modifica después de su plazo: un código o un enlace vencen en minutos, una invitación que no
/// es la última ya no la toca su entrega (vence en un día) y un ingreso no cambia. Los enlaces son la excepción: la
/// revocación del acceso invalida también los vencidos, así que se borran bajo el lock de la cuenta.
/// </summary>
internal sealed class IdentityRetentionRepository(ApplicationDbContext dbContext) : IIdentityRetentionRepository
{
    public async Task<bool> ExistsOtherRunAsync(CancellationToken cancellationToken) =>
        !await dbContext.TryAcquireAdvisoryLockAsync(AdvisoryLockKeys.IdentityRetention, cancellationToken);

    // Sin índice por la fecha de creación a propósito: la retención deja la tabla en 30 días de códigos, y recorrerla una
    // vez por lote cuesta menos que mantener otro índice en cada pedido de un código.
    public async Task<int> RemoveLoginCodesAsync(DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        var codes = await dbContext.LoginCodes
            .Where(code => code.CreatedAtUtc < createdBeforeUtc)
            .OrderBy(code => code.CreatedAtUtc)
            .ThenBy(code => code.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        dbContext.LoginCodes.RemoveRange(codes);

        return codes.Count;
    }

    // Sin índice por la fecha: la tabla quedó sin uso (B4) y solo se achica.
    public async Task<int> RemoveLoginLinksAsync(DateTime createdBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        var links = await dbContext.LoginLinks
            .Where(link => link.CreatedAtUtc < createdBeforeUtc)
            .OrderBy(link => link.CreatedAtUtc)
            .ThenBy(link => link.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        // Se leen antes del lock a propósito: lo que decide el borrado es la fecha de creación, que no cambia. El lock de
        // cada cuenta espera a una revocación en curso, que pudo cargar el enlace para invalidarlo: si se borrara antes, su
        // guardado no encontraría la fila. Y una revocación que llega después espera este lote y ya no lo ve. Nadie toma
        // dos login-link: a la vez, así que pedirlos todos juntos, ordenados, no arma un ciclo.
        await dbContext.AcquireAdvisoryLocksAsync(links.Select(link => AdvisoryLockKeys.LoginLink(link.UserId)), cancellationToken);
        dbContext.LoginLinks.RemoveRange(links);

        return links.Count;
    }

    // La última de cada cuenta es la que muestra «Editar usuario» (UserInvitationReader.FindLatestAsync): la subconsulta la
    // busca con su mismo orden. Las invitaciones son pocas: sin índice por la fecha.
    public async Task<int> RemoveUserInvitationsAsync(DateTime sentBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        var invitations = await dbContext.UserInvitations
            .Where(invitation => invitation.SentAtUtc < sentBeforeUtc
                && invitation.Id != dbContext.UserInvitations
                    .Where(latest => latest.UserId == invitation.UserId)
                    .OrderByDescending(latest => latest.SentAtUtc)
                    .ThenByDescending(latest => latest.Id)
                    .Select(latest => latest.Id)
                    .First())
            .OrderBy(invitation => invitation.SentAtUtc)
            .ThenBy(invitation => invitation.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        dbContext.UserInvitations.RemoveRange(invitations);

        return invitations.Count;
    }

    // Por IX_LoginAudits_OccurredAtUtc_RequestInfo, que tiene solo las filas que todavía guardan algo: las ya limpias no
    // se recorren en cada corrida. El filtro es el del índice (LoginAuditConfiguration): sin el identificador de los
    // fallidos, uno que ya quedó sin IP ni navegador no se volvería a visitar.
    public async Task<IReadOnlyList<LoginAudit>> ListLoginAuditsWithRequestInfoAsync(
        DateTime occurredBeforeUtc, int limit, CancellationToken cancellationToken) =>
        await dbContext.LoginAudits
            .Where(audit => audit.OccurredAtUtc < occurredBeforeUtc
                && (audit.IpAddress != null || audit.UserAgent != null || (!audit.Succeeded && audit.Identifier != null)))
            .OrderBy(audit => audit.OccurredAtUtc)
            .ThenBy(audit => audit.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
