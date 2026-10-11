using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

internal sealed record WhatsAppFormSendContext(WhatsAppFormRecipientRow Recipient, string RecipientRevision,
    StructuredFormSelectionRow Form,
    WhatsAppFormBindingRow Binding, WhatsAppFlowProjection Projection, string ButtonText, string StartScreen,
    WhatsAppFlowAccountContext Account, TimeSpan SessionLifetime);
