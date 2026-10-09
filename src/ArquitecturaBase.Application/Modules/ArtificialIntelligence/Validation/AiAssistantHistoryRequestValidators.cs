using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Resources;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Validation;

internal sealed class ListAiConversationsRequestValidator() : PagedRequestValidator<ListAiConversationsRequest>(ListAiConversationsRequest.SortableFields);
internal sealed class ListAiAssistantTurnsRequestValidator() : PagedRequestValidator<ListAiAssistantTurnsRequest>(ListAiAssistantTurnsRequest.SortableFields);
internal sealed class CancelAiAssistantTurnRequestValidator : AbstractValidator<CancelAiAssistantTurnRequest>
{
    public CancelAiAssistantTurnRequestValidator() => RuleFor(x => x.RequestId).NotEmpty().WithMessage(_ => ValidationMessages.Required);
}
