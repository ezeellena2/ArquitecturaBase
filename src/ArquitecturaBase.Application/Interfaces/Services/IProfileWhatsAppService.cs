using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>El WhatsApp de la sesión: pedir el código para vincular un número, confirmarlo y desvincularlo.</summary>
public interface IProfileWhatsAppService
{
    Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken);

    Task<Result> ConfirmPhoneLinkAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken);

    Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken);
}
