using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Controllers;

[ApiController]
[Route("account/login-methods")]
[Tags("Account")]
public sealed class LoginMethodsController(IAccountService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        (await service.GetLoginMethodsAsync(cancellationToken)).ToActionResult(this);
}
