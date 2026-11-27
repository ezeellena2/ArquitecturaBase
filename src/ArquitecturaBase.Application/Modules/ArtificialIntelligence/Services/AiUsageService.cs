using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiUsageService(IAiUsageReader reader, IAiUsageRepository repository, IUnitOfWork unitOfWork, IRequestValidator validator,
    ISystemSettingsReader settings, ILogger<AiUsageService> logger) : IAiUsageService
{
    public Task<Result<AiUsageResponse>> GetMonthAsync(AiUsageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiUsageResponse>>(logger, "GetAiUsageMonth", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (!BusinessMonth.TryParse(request.Month, out var month)) throw new InvalidOperationException("The validator lets only valid months through.");
            // PR16: the month of the business, in the zone of the settings, as its daily quotas.
            var zone = BusinessDay.Zone((await settings.FindPresentationAsync(cancellationToken)).DefaultTimeZoneId);
            var (startUtc, endUtc) = BusinessMonth.Bounds(month, zone);
            var usage = await reader.ListMonthAsync(month, startUtc, endUtc, cancellationToken);
            var prices = (await reader.ListPricesAsync(cancellationToken)).ToDictionary(price => (price.Provider, price.Model));
            List<AiUsageModelResponse> models = [];
            foreach (var row in usage.OrderByDescending(row => row.Requests).ThenBy(row => row.Model, StringComparer.Ordinal))
            {
                var price = prices.GetValueOrDefault((row.Provider, row.Model));
                models.Add(new(row.Provider, row.Model, row.Requests, row.InputTokens, row.OutputTokens, row.AudioSeconds, row.UnknownUsageRequests,
                    price is null ? null : new(price.InputUsdPerMillion, price.OutputUsdPerMillion, price.AudioUsdPerMinute, price.Revision),
                    AiUsagePolicy.EstimateCost(row, price)));
            }
            return new AiUsageResponse(BusinessMonth.Format(month), models, models.Sum(model => model.EstimatedCostUsd ?? 0),
                models.Count(model => model.Price is null), models.Sum(model => model.UnknownUsageRequests));
        });
    }

    public Task<Result<AiModelPriceResponse>> UpdatePriceAsync(UpdateAiModelPriceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiModelPriceResponse>>(logger, "UpdateAiModelPrice", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            return await unitOfWork.ExecuteInTransactionAsync(ct => UpdatePriceCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    private async Task<Result<AiModelPriceResponse>> UpdatePriceCoreAsync(UpdateAiModelPriceRequest request, CancellationToken cancellationToken)
    {
        await repository.LockPricesAsync(cancellationToken);
        var model = request.Model.Trim();
        var price = await repository.GetPriceAsync(request.Provider, model, cancellationToken);
        if (price is null)
        {
            // Loading them the first time carries no revision; one that carries it read prices that someone removed or never existed.
            if (request.Revision is not null) return AiModelPriceErrors.RevisionConflict;
            var created = AiModelPrice.Create(request.Provider, model, request.InputUsdPerMillion, request.OutputUsdPerMillion, request.AudioUsdPerMinute);
            if (created.IsFailure) return created.Error;
            repository.AddPrice(created.Value);
            return AiModelPriceResponse.Of(created.Value);
        }
        if (request.Revision is not { } revision) return AiModelPriceErrors.RevisionConflict;
        var updated = price.Update(revision, request.InputUsdPerMillion, request.OutputUsdPerMillion, request.AudioUsdPerMinute);
        return updated.IsFailure ? updated.Error : AiModelPriceResponse.Of(price);
    }
}
