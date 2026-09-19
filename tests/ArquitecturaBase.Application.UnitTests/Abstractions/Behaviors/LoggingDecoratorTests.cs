using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class LoggingDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Successful_command_logs_start_and_end()
    {
        var logger = new FakeLogger<PingCommand>();
        var decorator = new LoggingDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), logger);

        var result = await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Handling PingCommand", "Handled PingCommand"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Failed_command_logs_a_warning_with_the_error_code()
    {
        var logger = new FakeLogger<PingCommand>();
        var decorator = new LoggingDecorator.CommandHandler<PingCommand, string>(new PingCommandHandler(), logger);

        await decorator.Handle(new PingCommand(PingErrors.FailMessage), Ct);

        Assert.Contains(
            logger.Collector.GetSnapshot(),
            record => record.Level == LogLevel.Warning && record.Message == "PingCommand failed with Test.Ping.Failed");
    }

    [Fact]
    public async Task Query_is_logged()
    {
        var logger = new FakeLogger<PingQuery>();
        var decorator = new LoggingDecorator.QueryHandler<PingQuery, string>(new PingQueryHandler(), logger);

        await decorator.Handle(new PingQuery("hola"), Ct);

        Assert.Equal(2, logger.Collector.Count);
    }
}
