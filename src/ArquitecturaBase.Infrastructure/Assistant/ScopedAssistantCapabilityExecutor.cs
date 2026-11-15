using System.Data.Common;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Assistant;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Assistant;

internal sealed partial class ScopedAssistantCapabilityExecutor(IServiceScopeFactory scopeFactory,
    ILogger<ScopedAssistantCapabilityExecutor> logger) : IAssistantCapabilityExecutor
{
    public async Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = scopeFactory.CreateAsyncScope();
        var matches = scope.ServiceProvider.GetServices<IAssistantCapabilitySource>()
            .SelectMany(source => source.Definitions.Where(definition => definition.Id == request.Capability.Id
                && definition.Version == request.Capability.Version).Select(_ => source)).ToArray();
        if (matches.Length > 1) throw new InvalidOperationException("Duplicate assistant capability sources are not allowed.");
        if (matches.Length == 0) return SystemQueryErrors.NotFound;
        try
        {
            return await matches[0].ExecuteAsync(request, cancellationToken);
        }
        // I5: a database failure of the source is the source's, never the provider's: a failed result that the engine records as an
        // unavailable read (asked or preloaded) or that ends a proposal with its code. A canceled call stays the engine's to classify.
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsDatabaseFailure(exception))
        {
            // The type and SQLSTATE of the provider's exception, never its message. What is not transient (a column missing after a deploy)
            // leaves every turn without this read until someone fixes it: an Error, apart from the Warning of a transient one.
            var database = DatabaseException(exception);
            var transient = database?.IsTransient ?? false;
            var type = (database ?? exception).GetType().Name;
            LogDatabaseFailure(logger, transient ? LogLevel.Warning : LogLevel.Error, request.Capability.Id, request.Capability.Version,
                request.TurnId, type, database?.SqlState, transient);
            return SystemQueryErrors.Unavailable;
        }
    }

    // Npgsql raises a DbException; EF wraps it in DbUpdateException or in the exception of an exhausted or transient execution.
    private static bool IsDatabaseFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbException or DbUpdateException) return true;
        return false;
    }

    private static DbException? DatabaseException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbException database) return database;
        return null;
    }

    [LoggerMessage(Message = "Assistant capability {CapabilityId}@{Version} of turn {TurnId} failed in the database with {ExceptionType} "
        + "(SQLSTATE {SqlState}, transient {Transient}); it is unavailable.")]
    private static partial void LogDatabaseFailure(ILogger logger, LogLevel level, string capabilityId, int version, Guid turnId, string exceptionType,
        string? sqlState, bool transient);
}
