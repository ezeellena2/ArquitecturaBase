using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Answers a tap on an option of a document question (task 9 of the documents): a candidate's prefilled form, «Otro» for the team, a
/// form chosen when the document was not recognized (the next interpretation continues with it fixed), or that the question changed.
/// </summary>
public interface IWhatsAppAssistantDocumentService
{
    Task<Result> ProcessTapAsync(Guid contactId, CancellationToken ct);
}
