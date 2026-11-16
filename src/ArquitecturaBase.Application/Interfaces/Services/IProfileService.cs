using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// Los datos del perfil de la sesión y su correo: editarlos, pedir y confirmar el código de un correo, y pedir al
/// correo verificado el código que confirma vincular o desvincular el número (cada medio confirma los cambios del otro).
/// </summary>
public interface IProfileService
{
    Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken);

    Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken);

    Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    Task<Result<RequestEmailCodeResponse>> RequestEmailConfirmationCodeAsync(
        RequestEmailConfirmationCodeRequest request, CancellationToken cancellationToken);
}
