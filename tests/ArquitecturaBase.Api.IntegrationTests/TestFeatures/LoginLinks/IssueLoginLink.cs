using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;

/// <summary>
/// Emite un enlace de ingreso para una cuenta con <see cref="LoginLinkIssuer"/>, el mismo que va a usar el bot
/// (Tarea 11). Solo existe en los tests: así los del enlace no dependen del webhook ni del procesador.
/// </summary>
public sealed record IssueLoginLinkCommand(Guid UserId) : ICommand<IssueLoginLinkResponse>;

public sealed record IssueLoginLinkResponse(string Url, DateTime ExpiresAtUtc);

/// <summary>Pasa por los decoradores como cualquier comando: la unidad de trabajo guarda el enlace y suelta el lock.</summary>
internal sealed class IssueLoginLinkCommandHandler(LoginLinkIssuer issuer)
    : ICommandHandler<IssueLoginLinkCommand, IssueLoginLinkResponse>
{
    public async Task<Result<IssueLoginLinkResponse>> Handle(IssueLoginLinkCommand command, CancellationToken cancellationToken)
    {
        var issued = await issuer.IssueAsync(command.UserId, cancellationToken);

        return issued.IsSuccess ? new IssueLoginLinkResponse(issued.Value.Url, issued.Value.ExpiresAtUtc) : issued.Error;
    }
}
