using ArquitecturaBase.Api.Contracts.Users;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.OpenApi;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("api/me")]
[Tags("Users")]
[Authorize]
// Todas leen la cuenta de la sesión: si se borró con el token todavía vigente, responden 404.
[ProducesProblem(StatusCodes.Status404NotFound)]
public sealed class MeController(
    IProfileQueryService queries, IProfileService profile, IProfilePhoneService phone) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await queries.GetAsync(cancellationToken)).ToActionResult(this);

    [HttpPut]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Una revisión vieja: un administrador u otra pestaña cambiaron la cuenta (N2).
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        [FromBody] UpdateProfileHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.UpdateAsync(
            new UpdateProfileRequest(request.DisplayName, request.Culture, request.TimeZoneId, request.ExpectedRevision),
            cancellationToken))
            .ToActionResult(this);

    [HttpPost("email/code")]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestEmailCodeResponse>(StatusCodes.Status202Accepted)]
    // La cuenta ya tiene un correo verificado, que desde el perfil no se cambia, o el correo es de otra cuenta (en
    // errors.email, antes de mandar el código).
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestEmailCode(
        [FromBody] RequestEmailCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.RequestEmailCodeAsync(new RequestEmailCodeRequest(request.Email), cancellationToken))
            .ToAcceptedResult(this);

    [HttpPut("email")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Otra cuenta tomó el correo después de pedir el código: se dice después de un código correcto.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ConfirmEmail(
        [FromBody] ConfirmEmailHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.ConfirmEmailAsync(
            new ConfirmEmailRequest(request.Email, request.Code, request.PhoneCode), cancellationToken))
            .ToActionResult(this);

    /// <summary>
    /// El código, al correo verificado de la cuenta, que confirma vincular o desvincular el número (cada medio confirma los
    /// cambios del otro).
    /// </summary>
    [HttpPost("email/confirmation-code")]
    [EnableRateLimiting(RateLimitingExtensions.LoginCodePolicy)]
    [ProducesResponseType<RequestEmailCodeResponse>(StatusCodes.Status202Accepted)]
    // Sin correo verificado; sin número para desvincular; o con un número verificado, que no se reemplaza.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RequestEmailConfirmationCode(
        [FromBody] RequestEmailConfirmationCodeHttpRequest request,
        CancellationToken cancellationToken) =>
        (await profile.RequestEmailConfirmationCodeAsync(
            new RequestEmailConfirmationCodeRequest(request.Change), cancellationToken)).ToAcceptedResult(this);

    /// <summary>
    /// Con un correo verificado, lleva el código que confirma el cambio, pedido con <c>UnlinkPhone</c> a
    /// <c>email/confirmation-code</c>; sin él, el cuerpo puede faltar.
    /// </summary>
    [HttpDelete("whatsapp")]
    [EnableRateLimiting(RateLimitingExtensions.LoginVerifyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    // Es el único medio de ingreso de la cuenta.
    [ProducesProblem(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnlinkOwnPhone(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] UnlinkOwnPhoneHttpRequest? request,
        CancellationToken cancellationToken) =>
        (await phone.UnlinkOwnPhoneAsync(new UnlinkOwnPhoneRequest(request?.Code), cancellationToken)).ToActionResult(this);
}
