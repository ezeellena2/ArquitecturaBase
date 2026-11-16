using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// N16: la retención de los datos del ingreso (identidad.md, «Retención de los datos del ingreso»). Cada método es una
/// transacción que toma primero el lock de la retención, sin esperar: dos copias de la Api nunca corren un lote a la vez,
/// y la que lo encuentra tomado no hace nada, porque la otra ya está en eso. Un apagado a mitad de un lote o de un tramo
/// lo deshace entero; lo anterior ya quedó guardado.
/// </summary>
internal sealed partial class IdentityRetentionService(
    IIdentityRetentionRepository retention,
    IOpenIddictPruner sessions,
    IUnitOfWork unitOfWork,
    IOptions<IdentityRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<IdentityRetentionService> logger) : IIdentityRetentionService
{
    public async Task<Result<bool>> PurgeBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var (codes, links, invitations, audits) = (0, 0, 0, 0);

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (await retention.ExistsOtherRunAsync(ct))
            {
                LogOtherRun(logger);
                return Result.Success(false);
            }

            var cutoffs = IdentityRetentionPolicy.CutoffsAt(timeProvider.GetUtcNow().UtcDateTime, settings);

            codes = await retention.RemoveLoginCodesAsync(cutoffs.LoginCodesBeforeUtc, settings.BatchSize, ct);
            links = await retention.RemoveLoginLinksAsync(cutoffs.LoginCodesBeforeUtc, settings.BatchSize, ct);
            invitations = await retention.RemoveUserInvitationsAsync(cutoffs.InvitationsBeforeUtc, settings.BatchSize, ct);

            var withRequestInfo = await retention.ListLoginAuditsWithRequestInfoAsync(
                cutoffs.LoginAuditsBeforeUtc, settings.BatchSize, ct);
            foreach (var audit in withRequestInfo) audit.ForgetRequestInfo();
            audits = withRequestInfo.Count;

            return Result.Success(Math.Max(Math.Max(codes, links), Math.Max(invitations, audits)) >= settings.BatchSize);
        }, CommitPolicy.OnSuccess, cancellationToken);

        // Después del commit: el log dice lo que quedó guardado. Cuántas filas, nunca cuáles.
        if (result.IsSuccess && codes + links + invitations + audits > 0) LogPurged(logger, codes, links, invitations, audits);

        return result;
    }

    public async Task<Result<DateTime?>> PruneSessionsTrancheAsync(DateTime? fromUtc, CancellationToken cancellationToken)
    {
        var pruned = new OpenIddictPruneTranche(0, 0, NextFromUtc: null);

        var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (await retention.ExistsOtherRunAsync(ct))
            {
                LogOtherRun(logger);
                return Result.Success<DateTime?>(null);
            }

            var cutoffs = IdentityRetentionPolicy.CutoffsAt(timeProvider.GetUtcNow().UtcDateTime, options.Value);
            pruned = await sessions.PruneTrancheAsync(cutoffs.SessionsExpiredBeforeUtc, fromUtc, ct);

            return Result.Success(pruned.NextFromUtc);
        }, CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess && pruned.Tokens + pruned.Authorizations > 0) LogPruned(logger, pruned.Tokens, pruned.Authorizations);

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity retention removed {LoginCodes} login codes, {LoginLinks} login links and {Invitations} invitations, and cleared the request info of {LoginAudits} sign-ins")]
    private static partial void LogPurged(ILogger logger, int loginCodes, int loginLinks, int invitations, int loginAudits);

    [LoggerMessage(Level = LogLevel.Information, Message = "Identity retention pruned {Tokens} OpenIddict tokens and {Authorizations} authorizations")]
    private static partial void LogPruned(ILogger logger, long tokens, long authorizations);

    // Otra copia tiene el lock: está corriendo la retención, y esta termina su corrida sin esperar. No es un error.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Identity retention is already running in another copy; this run ends")]
    private static partial void LogOtherRun(ILogger logger);
}
