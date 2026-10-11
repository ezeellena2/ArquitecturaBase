using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class UnusedWhatsAppFormService : IWhatsAppFormService
{
    public Task<Result<WhatsAppFormsResponse>> ListAsync(ListWhatsAppFormsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<WhatsAppFormContactResponse?> FindContactAsync(Guid contactId, CancellationToken cancellationToken) => throw new NotSupportedException();
}
