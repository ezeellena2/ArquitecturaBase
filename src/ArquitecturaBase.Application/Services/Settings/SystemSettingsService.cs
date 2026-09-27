using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Settings;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Settings;

internal sealed partial class SystemSettingsService(
    ISystemSettingsRepository repository,
    ISystemSettingsReader reader,
    IUnitOfWork unitOfWork,
    ServiceRequestValidator<UpdateSystemSettingsRequest> validator,
    ILogger<SystemSettingsService> logger) : ISystemSettingsService
{
    private const string GetOperation = "GetSystemSettings";
    private const string UpdateOperation = "UpdateSystemSettings";

    /// <summary>El panel lee la fila guardada, no la lectura cacheada del camino de ingreso.</summary>
    public async Task<Result<SystemSettingsResponse>> GetAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, GetOperation);
        var settings = await repository.GetAsync(cancellationToken);

        Result<SystemSettingsResponse> result = settings is null
            ? SettingsErrors.NotFound
            : new SystemSettingsResponse(settings.RegistrationMode);

        LogOutcome(logger, GetOperation, result);
        return result;
    }

    public async Task<Result> UpdateAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, UpdateOperation);

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogFailed(logger, UpdateOperation, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        // Después del commit y solo si se confirmó: invalidar antes dejaría que una lectura concurrente vuelva a cachear el
        // modo viejo. Que la fábrica no vea lo no confirmado lo garantiza el lector, que lee en su propio scope.
        if (result.IsSuccess)
        {
            await reader.InvalidateAsync(cancellationToken);
        }

        LogOutcome(logger, UpdateOperation, result);
        return result;
    }

    private async Task<Result> UpdateCoreAsync(UpdateSystemSettingsRequest request, CancellationToken cancellationToken)
    {
        var settings = await repository.GetAsync(cancellationToken);
        if (settings is null)
        {
            return SettingsErrors.NotFound;
        }

        settings.SetRegistrationMode(request.RegistrationMode);
        return Result.Success();
    }

    private static void LogOutcome(ILogger logger, string operation, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, operation);
        }
        else
        {
            LogFailed(logger, operation, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {Operation}")]
    private static partial void LogHandling(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {Operation}")]
    private static partial void LogHandled(ILogger logger, string operation);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Operation} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string operation, string errorCode);
}
