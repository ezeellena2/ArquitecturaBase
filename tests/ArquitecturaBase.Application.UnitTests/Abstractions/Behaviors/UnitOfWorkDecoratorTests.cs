using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class UnitOfWorkDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_command_saves_changes_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), unitOfWork);

        await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_does_not_save()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), unitOfWork);

        var result = await decorator.Handle(new PingCommand(PingErrors.FailMessage), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Successful_command_without_response_saves_changes_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingBaseCommand>(new PingBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingBaseCommand("hola"), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_without_response_does_not_save()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingBaseCommand>(new PingBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingBaseCommand(PingErrors.FailMessage), Ct);

        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_that_persists_changes_on_failure_still_saves()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandHandler<PingPersistentCommand, string>(
            new PingPersistentCommandHandler(), unitOfWork);

        var result = await decorator.Handle(new PingPersistentCommand(PingErrors.FailMessage), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Failed_command_without_response_that_persists_changes_on_failure_still_saves()
    {
        var unitOfWork = new FakeUnitOfWork();
        var decorator = new UnitOfWorkDecorator.CommandBaseHandler<PingPersistentBaseCommand>(
            new PingPersistentBaseCommandHandler(), unitOfWork);

        await decorator.Handle(new PingPersistentBaseCommand(PingErrors.FailMessage), Ct);

        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }
}
