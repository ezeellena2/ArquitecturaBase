using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.TestDoubles;

internal sealed record PingCommand(string? Message) : ICommand<string>;

internal sealed record PingBaseCommand(string? Message) : ICommand;

internal sealed record PingQuery(string? Message) : IQuery<string>;

internal static class PingErrors
{
    public const string FailMessage = "fail";

    public static readonly Error Failed = Error.Conflict("Test.Ping.Failed", "Ping failed.");
}

internal sealed class PingCommandValidator : AbstractValidator<PingCommand>
{
    public PingCommandValidator() => RuleFor(command => command.Message).Required();
}

internal sealed class PingBaseCommandValidator : AbstractValidator<PingBaseCommand>
{
    public PingBaseCommandValidator() => RuleFor(command => command.Message).Required();
}

internal sealed class PingQueryValidator : AbstractValidator<PingQuery>
{
    public PingQueryValidator() => RuleFor(query => query.Message).Required();
}

internal sealed class PingCommandHandler : ICommandHandler<PingCommand, string>
{
    public int Calls { get; private set; }

    public Task<Result<string>> Handle(PingCommand command, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult<Result<string>>(
            command.Message == PingErrors.FailMessage ? PingErrors.Failed : "pong: " + command.Message);
    }
}

internal sealed class PingBaseCommandHandler : ICommandHandler<PingBaseCommand>
{
    public int Calls { get; private set; }

    public Task<Result> Handle(PingBaseCommand command, CancellationToken cancellationToken)
    {
        Calls++;

        return Task.FromResult(
            command.Message == PingErrors.FailMessage ? Result.Failure(PingErrors.Failed) : Result.Success());
    }
}

internal sealed class PingQueryHandler : IQueryHandler<PingQuery, string>
{
    public Task<Result<string>> Handle(PingQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success("pong: " + query.Message));
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveChangesCalls++;
        return Task.FromResult(1);
    }
}

internal sealed record PingPersistentCommand(string? Message) : ICommand<string>, IPersistChangesOnFailure;

internal sealed record PingPersistentBaseCommand(string? Message) : ICommand, IPersistChangesOnFailure;

internal sealed class PingPersistentCommandHandler : ICommandHandler<PingPersistentCommand, string>
{
    public Task<Result<string>> Handle(PingPersistentCommand command, CancellationToken cancellationToken) =>
        Task.FromResult<Result<string>>(
            command.Message == PingErrors.FailMessage ? PingErrors.Failed : "pong: " + command.Message);
}

internal sealed class PingPersistentBaseCommandHandler : ICommandHandler<PingPersistentBaseCommand>
{
    public Task<Result> Handle(PingPersistentBaseCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Message == PingErrors.FailMessage ? Result.Failure(PingErrors.Failed) : Result.Success());
}
