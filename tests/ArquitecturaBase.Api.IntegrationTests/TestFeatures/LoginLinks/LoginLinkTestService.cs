using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;

public sealed record IssueLoginLinkRequest(Guid UserId);

public sealed record IssueLoginLinkResponse(string Url, DateTime ExpiresAtUtc);

public interface ILoginLinkTestService
{
    Task<Result<IssueLoginLinkResponse>> IssueAsync(IssueLoginLinkRequest request, CancellationToken cancellationToken);
}

/// <summary>Emite el enlace con el mismo emisor del bot y confirma la escritura después de un resultado exitoso.</summary>
internal sealed class LoginLinkTestService(LoginLinkIssuer issuer, IUnitOfWork unitOfWork) : ILoginLinkTestService
{
    public async Task<Result<IssueLoginLinkResponse>> IssueAsync(
        IssueLoginLinkRequest request,
        CancellationToken cancellationToken)
    {
        var issued = await issuer.IssueAsync(request.UserId, cancellationToken);
        if (!issued.IsSuccess)
        {
            return issued.Error;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new IssueLoginLinkResponse(issued.Value.Url, issued.Value.ExpiresAtUtc);
    }
}
