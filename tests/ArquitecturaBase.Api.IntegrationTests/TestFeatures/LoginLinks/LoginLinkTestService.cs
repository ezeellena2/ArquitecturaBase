using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.LoginLinks;

public sealed record IssueLoginLinkRequest(Guid UserId);

public sealed record IssueLoginLinkResponse(string Url, DateTime ExpiresAtUtc);

public interface ILoginLinkTestService
{
    Task<Result<IssueLoginLinkResponse>> IssueAsync(IssueLoginLinkRequest request, CancellationToken cancellationToken);
}

/// <summary>Emite el enlace con el mismo emisor del bot, en su propio límite: un TooManyRequests no deja nada.</summary>
internal sealed class LoginLinkTestService(LoginLinkIssuer issuer, IUnitOfWork unitOfWork) : ILoginLinkTestService
{
    public async Task<Result<IssueLoginLinkResponse>> IssueAsync(
        IssueLoginLinkRequest request,
        CancellationToken cancellationToken)
    {
        var issued = await unitOfWork.ExecuteInTransactionAsync(
            ct => issuer.IssueAsync(request.UserId, ct), CommitPolicy.OnSuccess, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        return new IssueLoginLinkResponse(issued.Value.Url, issued.Value.ExpiresAtUtc);
    }
}
