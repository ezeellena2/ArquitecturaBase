using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Abstractions.Behaviors;

/// <summary>Registra el inicio y el final de cada caso de uso, con el código de error si falla.</summary>
internal static partial class LoggingDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        ILogger<TCommand> logger)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var requestName = typeof(TCommand).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(command, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        ILogger<TCommand> logger)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var requestName = typeof(TCommand).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(command, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    internal sealed class QueryHandler<TQuery, TResponse>(
        IQueryHandler<TQuery, TResponse> inner,
        ILogger<TQuery> logger)
        : IQueryHandler<TQuery, TResponse>
        where TQuery : IQuery<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken)
        {
            var requestName = typeof(TQuery).Name;
            LogHandling(logger, requestName);

            var result = await inner.Handle(query, cancellationToken);

            LogOutcome(logger, requestName, result);
            return result;
        }
    }

    private static void LogOutcome(ILogger logger, string requestName, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, requestName);
        }
        else
        {
            LogFailed(logger, requestName, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName}")]
    private static partial void LogHandled(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode);
}
