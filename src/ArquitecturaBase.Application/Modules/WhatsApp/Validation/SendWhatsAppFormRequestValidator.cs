using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using FluentValidation;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;
internal sealed class SendWhatsAppFormRequestValidator : AbstractValidator<SendWhatsAppFormRequest>
{
    public SendWhatsAppFormRequestValidator()
    {
        RuleFor(x => x.ContactId).Required(); RuleFor(x => x.RequestId).Required();
        RuleFor(x => x.DefinitionId).Required(); RuleFor(x => x.VersionId).Required();
        RuleFor(x => x.ExpectedModeRevision).GreaterThanOrEqualTo(0).WithMessage(_ => ValidationMessages.Required);
        RuleFor(x => x.ExpectedRecipientContextRevision).Required().MaxLength(67);
        RuleFor(x => x.Message).Required().MaxLength(WhatsAppOutboundMessage.MaxInteractiveBodyLength)
            .Must(value => value is null || WhatsAppOutgoingMessage.IsVisibleBody(value)).WithMessage(_ => ValidationMessages.Required)
            .When(x => x.Message is not null);
    }
}
