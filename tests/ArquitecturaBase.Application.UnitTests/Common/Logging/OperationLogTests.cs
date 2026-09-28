using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Common.Logging;

/// <summary>
/// El único lugar que registra el inicio, el fin y el fallo de una operación (Etapa 3, tarea 2). No atrapa
/// excepciones: si el trabajo lanza, el log queda solo con "Handling".
/// </summary>
public sealed class OperationLogTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Success_logs_handling_and_handled()
    {
        var logger = new FakeLogger<OperationLogTests>();

        var result = await OperationLog.RunAsync(logger, "DoSomething", () => Task.FromResult(Result.Success()));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Handling DoSomething", "Handled DoSomething"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
        Assert.All(logger.Collector.GetSnapshot(), record => Assert.Equal(LogLevel.Information, record.Level));
    }

    [Fact]
    public async Task Failure_logs_handling_and_failed_with_the_error_code_at_warning_level()
    {
        var logger = new FakeLogger<OperationLogTests>();
        var error = Error.Failure("Some.Error", "Algo salió mal.");

        var result = await OperationLog.RunAsync(logger, "DoSomething", () => Task.FromResult(Result.Failure(error)));

        Assert.True(result.IsFailure);
        var snapshot = logger.Collector.GetSnapshot();
        Assert.Equal(["Handling DoSomething", "DoSomething failed with Some.Error"], snapshot.Select(record => record.Message));
        Assert.Equal(LogLevel.Information, snapshot[0].Level);
        Assert.Equal(LogLevel.Warning, snapshot[1].Level);
    }

    /// <summary>Una excepción no se atrapa: no queda ni un Handled ni un Failed, solo el Handling de antes.</summary>
    [Fact]
    public async Task An_exception_leaves_only_the_handling_log()
    {
        var logger = new FakeLogger<OperationLogTests>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OperationLog.RunAsync<Result>(logger, "DoSomething", () => throw new InvalidOperationException("boom")));

        Assert.Equal(["Handling DoSomething"], logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Works_for_result_without_a_value()
    {
        var logger = new FakeLogger<OperationLogTests>();

        var result = await OperationLog.RunAsync(logger, "DoSomething", () => Task.FromResult(Result.Success()));

        Assert.IsType<Result>(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Works_for_result_with_a_value()
    {
        var logger = new FakeLogger<OperationLogTests>();

        var result = await OperationLog.RunAsync(logger, "DoSomething", () => Task.FromResult(Result.Success(42)));

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Equal(
            ["Handling DoSomething", "Handled DoSomething"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    /// <summary>
    /// El caso de los servicios: un lambda que a veces devuelve un <see cref="Error"/> suelto (convertido a
    /// <c>Result&lt;T&gt;</c>) y a veces un valor. El fallo se registra con su código y el tipo sigue siendo el genérico.
    /// </summary>
    [Fact]
    public async Task A_result_with_a_value_that_fails_logs_the_error_code()
    {
        var logger = new FakeLogger<OperationLogTests>();
        var error = Error.NotFound("Some.Thing.NotFound", "No está.");

        var result = await OperationLog.RunAsync<Result<int>>(logger, "DoSomething", async () =>
        {
            await Task.Yield();
            return error;
        });

        Assert.IsType<Result<int>>(result);
        Assert.Equal("Some.Thing.NotFound", result.Error.Code);
        var snapshot = logger.Collector.GetSnapshot();
        Assert.Equal(["Handling DoSomething", "DoSomething failed with Some.Thing.NotFound"], snapshot.Select(record => record.Message));
        Assert.Equal(LogLevel.Warning, snapshot[1].Level);
    }

    /// <summary>Como un servicio que lanza después de un await (la cookie, el caché): la tarea falla y no hay Handled.</summary>
    [Fact]
    public async Task An_exception_after_an_await_leaves_only_the_handling_log()
    {
        var logger = new FakeLogger<OperationLogTests>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => OperationLog.RunAsync<Result>(logger, "DoSomething", async () =>
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal(["Handling DoSomething"], logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public void Handling_and_handled_are_reusable_directly_by_a_caller_that_does_not_return_a_result()
    {
        var logger = new FakeLogger<OperationLogTests>();

        OperationLog.Handling(logger, "ReceiveWhatsAppWebhook");
        OperationLog.Handled(logger, "ReceiveWhatsAppWebhook");

        Assert.Equal(
            ["Handling ReceiveWhatsAppWebhook", "Handled ReceiveWhatsAppWebhook"],
            logger.Collector.GetSnapshot().Select(record => record.Message));
    }
}
