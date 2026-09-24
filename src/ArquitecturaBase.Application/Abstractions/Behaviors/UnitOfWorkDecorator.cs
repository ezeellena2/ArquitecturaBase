using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Abstractions.Behaviors;

/// <summary>
/// Guarda los cambios si el comando terminó bien, o siempre si el comando implementa
/// <see cref="IPersistChangesOnFailure"/>. Las consultas no pasan por acá.
/// </summary>
internal static class UnitOfWorkDecorator
{
    internal sealed class CommandHandler<TCommand, TResponse>(
        ICommandHandler<TCommand, TResponse> inner,
        IUnitOfWork unitOfWork)
        : ICommandHandler<TCommand, TResponse>
        where TCommand : ICommand<TResponse>
    {
        public async Task<Result<TResponse>> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess || command is IPersistChangesOnFailure)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    internal sealed class CommandBaseHandler<TCommand>(
        ICommandHandler<TCommand> inner,
        IUnitOfWork unitOfWork)
        : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public async Task<Result> Handle(TCommand command, CancellationToken cancellationToken)
        {
            var result = await inner.Handle(command, cancellationToken);

            if (result.IsSuccess || command is IPersistChangesOnFailure)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }
}
