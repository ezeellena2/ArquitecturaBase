using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("account/login-methods")]
[Tags("Account")]
public sealed class LoginMethodsController(ILoginMethodsService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<LoginMethodsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetLoginMethodsAsync(cancellationToken)).ToActionResult(this);
}
