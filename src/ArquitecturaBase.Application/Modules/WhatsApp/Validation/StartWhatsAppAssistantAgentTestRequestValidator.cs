using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Common;
using FluentValidation;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

/// <summary>Probar: one to five messages of up to 1000 characters each, as the account would write them.</summary>
internal sealed class StartWhatsAppAssistantAgentTestRequestValidator : AbstractValidator<StartWhatsAppAssistantAgentTestRequest>
{
    public StartWhatsAppAssistantAgentTestRequestValidator()
    {
        RuleFor(request => request.RequestId).Required();
        RuleFor(request => request.ContactId).Required();
        RuleFor(request => request.Messages).Cascade(CascadeMode.Stop).NotNull().WithMessage(_ => ValidationMessages.Required)
            .Must(messages => messages.Count is >= 1 and <= StartWhatsAppAssistantAgentTestRequest.MaxMessages)
            .WithMessage(_ => WhatsAppAssistantAgentValidationMessages.TooMany(StartWhatsAppAssistantAgentTestRequest.MaxMessages));
        RuleForEach(request => request.Messages).Cascade(CascadeMode.Stop).Required()
            .MaxLength(StartWhatsAppAssistantAgentTestRequest.MaxMessageLength)
            .Must(message => !string.IsNullOrWhiteSpace(message) && StorableText.IsStorable(message)).WithMessage(_ => ValidationMessages.Required);
    }
}
