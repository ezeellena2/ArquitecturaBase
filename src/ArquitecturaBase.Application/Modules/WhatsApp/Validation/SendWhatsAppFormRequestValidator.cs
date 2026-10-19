using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;
internal sealed class SendWhatsAppFormRequestValidator : AbstractValidator<SendWhatsAppFormRequest>
{
    public SendWhatsAppFormRequestValidator()
    {
        RuleFor(x => x.ContactId).NotEmpty(); RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.DefinitionId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty();
        RuleFor(x => x.ExpectedModeRevision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ExpectedRecipientContextRevision).NotEmpty().MaximumLength(67);
        RuleFor(x => x.Message).NotEmpty().MaximumLength(WhatsAppOutboundMessage.MaxInteractiveBodyLength)
            .Must(value => value is null || WhatsAppOutgoingMessage.IsVisibleBody(value)).WithMessage(_ => ValidationMessages.Required)
            .When(x => x.Message is not null);
    }
}
