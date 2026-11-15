using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Connection;

/// <summary>
/// Desconectar ARCA (H26): con el lock y la revisión, como cualquier cambio de la conexión. Después del commit deja un log con
/// la conexión y quién la desconectó; la fila se borra, así que su auditoría no lo cuenta.
/// </summary>
internal sealed partial class ArcaDisconnectionService(IArcaConnectionRepository repository, IUnitOfWork unitOfWork, ICurrentUser currentUser,
    ILogger<ArcaDisconnectionService> logger) : IArcaDisconnectionService
{
    public Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "DisconnectArca", async () =>
        {
            Guid? removed = null;
            var result = await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await repository.LockConnectionAsync(token);
                if (await repository.GetConnectionAsync(token) is not { } connection) return ArcaConnectionErrors.NotConnected;
                if (connection.Revision != expectedRevision) return ArcaConnectionErrors.RevisionConflict;
                repository.Remove(connection);
                removed = connection.Id;
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (result.IsSuccess && removed is { } connectionId) LogDisconnected(connectionId, currentUser.UserId);
            return result;
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "The ARCA connection {ConnectionId} was disconnected by user {UserId}.")]
    private partial void LogDisconnected(Guid connectionId, Guid? userId);
}
