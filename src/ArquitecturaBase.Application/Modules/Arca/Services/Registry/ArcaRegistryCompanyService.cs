using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Registry;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Registry;

internal sealed class ArcaRegistryCompanyService(ArcaServiceSessionIssuer access, IArcaConnectionRepository connections, IArcaRegistryRepository repository,
    IArcaRegistryReader reader, IUnitOfWork unitOfWork, ILogger<ArcaRegistryCompanyService> logger) : IArcaRegistryCompanyService
{
    public Task<Result<ArcaCompanyResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<ArcaCompanyResponse>>(logger, "GetArcaRegistryCompany", async () =>
        {
            if (await access.FindConnectionIdAsync(cancellationToken) is not { } connectionId) return ArcaConnectionErrors.NotConnected;
            var company = await reader.FindCompanyAsync(connectionId, cancellationToken);
            return new ArcaCompanyResponse(company?.LegalName, company?.VatCondition, company?.FiscalAddress);
        });

    public Task<Result> SaveAsync(Guid connectionId, ArcaRegistryPerson person, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "SaveArcaRegistryCompany", () => unitOfWork.ExecuteInTransactionAsync(
            token => SaveCoreAsync(connectionId, person, token), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> SaveCoreAsync(Guid connectionId, ArcaRegistryPerson person, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await connections.GetConnectionAsync(cancellationToken) is not { } connection || connection.Id != connectionId)
            return ArcaConnectionErrors.NotConnected;

        var (legalName, vatCondition, fiscalAddress) =
            (ArcaCompanyDataPolicy.LegalName(person), ArcaCompanyDataPolicy.VatCondition(person), ArcaCompanyDataPolicy.FiscalAddress(person));
        if (await repository.GetCompanyAsync(connectionId, cancellationToken) is { } company)
            company.Update(legalName, vatCondition, fiscalAddress);
        else
            repository.Add(ArcaRegistryCompany.Create(connectionId, legalName, vatCondition, fiscalAddress));
        return Result.Success();
    }
}
