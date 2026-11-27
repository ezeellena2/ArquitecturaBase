using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>Consumo de WhatsApp (T7): what Meta reports for a business month, read on each request.</summary>
public interface IWhatsAppUsageService
{
    /// <summary>
    /// 400 at <c>month</c> if it is missing, not <c>yyyy-MM</c>, after the current business month or more than eleven before it;
    /// <c>WhatsApp.Usage.Unavailable</c> if Meta does not report it.
    /// </summary>
    Task<Result<WhatsAppUsageResponse>> GetAsync(GetWhatsAppUsageRequest request, CancellationToken cancellationToken);
}
