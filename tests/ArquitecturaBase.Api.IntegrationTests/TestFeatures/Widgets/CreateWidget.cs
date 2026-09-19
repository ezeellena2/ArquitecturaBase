using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Persistence;
using FluentValidation;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Widgets;

public sealed record CreateWidgetCommand(string? Name) : ICommand<Guid>;

internal sealed class CreateWidgetCommandValidator : AbstractValidator<CreateWidgetCommand>
{
    public CreateWidgetCommandValidator() =>
        RuleFor(command => command.Name).Required().MaxLength(Widget.NameMaxLength);
}

internal sealed class CreateWidgetCommandHandler(ApplicationDbContext dbContext)
    : ICommandHandler<CreateWidgetCommand, Guid>
{
    public Task<Result<Guid>> Handle(CreateWidgetCommand command, CancellationToken cancellationToken)
    {
        var widget = new Widget(command.Name!);
        dbContext.Set<Widget>().Add(widget);

        // Guarda el UnitOfWorkDecorator.
        return Task.FromResult(Result.Success(widget.Id));
    }
}
