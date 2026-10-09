using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class BusinessPlacesService(IBusinessPlacesClient client, IRequestValidator validator, ILogger<BusinessPlacesService> logger) : IBusinessPlacesService
{
    public Task<Result<BusinessPlacesResponse>> SearchAsync(SearchBusinessPlacesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<BusinessPlacesResponse>>(logger, "SearchBusinessPlaces", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            return await client.SearchAsync(request.Input.Trim(), CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, cancellationToken);
        });
    }
}
