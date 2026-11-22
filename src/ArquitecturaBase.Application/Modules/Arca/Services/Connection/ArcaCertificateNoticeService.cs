using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// RC7-B: el aviso de vencimiento del certificado activo. Con el lock de la conexión, otro host que corre el mismo aviso o una
/// renovación que guarda en ese momento esperan; los correos van por la cola transaccional en la misma transacción que marca el
/// certificado, así que salen todos o ninguno, una vez por certificado.
/// </summary>
internal sealed partial class ArcaCertificateNoticeService(IArcaConnectionRepository repository, IArcaNoticeRecipientReader recipients,
    IEmailQueue emailQueue, IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<ArcaCertificateNoticeService> logger)
    : IArcaCertificateNoticeService
{
    public Task<Result> NotifyAsync(CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(NotifyCoreAsync, CommitPolicy.OnSuccess, cancellationToken);

    private async Task<Result> NotifyCoreAsync(CancellationToken cancellationToken)
    {
        await repository.LockConnectionAsync(cancellationToken);
        if (await repository.GetConnectionAsync(cancellationToken) is not { } connection) return Result.Success();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (await repository.GetCertificateAsync(connection.Id, ArcaCertificateRole.Active, cancellationToken) is not { } certificate
            || !certificate.NeedsExpiryNotice(TimeSpan.FromDays(connection.CertificateNoticeDays), nowUtc)) return Result.Success();

        var people = await recipients.ListManagersAsync(cancellationToken);
        if (people.Count == 0)
        {
            LogNobodyToTell();
            return Result.Success();
        }

        foreach (var person in people)
        {
            if (emailQueue.TryEnqueue(ArcaCertificateNoticePolicy.Compose(person, certificate.NotAfterUtc!.Value))) continue;
            LogNotQueued(people.Count);
            return ArcaConnectionErrors.ExpiryNoticeNotQueued;
        }

        certificate.ExpiryNoticeSent(nowUtc);
        LogQueued(people.Count);
        return Result.Success();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The ARCA certificate expiry notice was queued for {Count} people.")]
    private partial void LogQueued(int count);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The ARCA certificate is about to expire and nobody who manages the connection has a confirmed email; the next run tries again.")]
    private partial void LogNobodyToTell();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The ARCA certificate expiry notice for {Count} people was not queued; the next run tries again.")]
    private partial void LogNotQueued(int count);
}
