using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using FluentValidation;

namespace ArquitecturaBase.Application.UnitTests.Abstractions.Behaviors;

public sealed class ValidationDecoratorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invalid_command_returns_a_validation_error_without_calling_the_handler()
    {
        using var culture = new CultureScope("es");
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, [new PingCommandValidator()]);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("Este campo es obligatorio.", Assert.Single(error.Errors["message"]));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Valid_command_reaches_the_handler()
    {
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, [new PingCommandValidator()]);

        var result = await decorator.Handle(new PingCommand("hola"), Ct);

        Assert.Equal("pong: hola", result.Value);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Without_validators_the_handler_runs()
    {
        var handler = new PingCommandHandler();
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(handler, []);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Messages_from_every_validator_are_grouped_by_camel_case_field()
    {
        using var culture = new CultureScope("es");
        var tooShort = new InlineValidator<PingCommand>();
        tooShort.RuleFor(command => command.Message).Must(message => message is { Length: > 3 }).WithMessage("Muy corto.");
        var decorator = new ValidationDecorator.CommandHandler<PingCommand, string>(
            new PingCommandHandler(), [new PingCommandValidator(), tooShort]);

        var result = await decorator.Handle(new PingCommand(""), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(["message"], error.Errors.Keys);
        Assert.Equal(["Este campo es obligatorio.", "Muy corto."], error.Errors["message"]);
    }

    [Fact]
    public async Task Invalid_command_without_response_returns_a_validation_error()
    {
        var handler = new PingBaseCommandHandler();
        var decorator = new ValidationDecorator.CommandBaseHandler<PingBaseCommand>(handler, [new PingBaseCommandValidator()]);

        var result = await decorator.Handle(new PingBaseCommand(null), Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Invalid_query_returns_a_validation_error()
    {
        var decorator = new ValidationDecorator.QueryHandler<PingQuery, string>(new PingQueryHandler(), [new PingQueryValidator()]);

        var result = await decorator.Handle(new PingQuery(" "), Ct);

        Assert.IsType<ValidationError>(result.Error);
    }
}
