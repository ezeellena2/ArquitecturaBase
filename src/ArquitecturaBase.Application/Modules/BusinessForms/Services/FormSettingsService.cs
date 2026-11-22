using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Configuración › Formularios (RC4-A, with the received documents' retention of RC1-A). The route asks for «Administrar la configuración»;
/// saving validates every number, takes the lock of the settings and brings the revision it read (409 otherwise), in one transaction. The
/// first save creates the row; until then the settings read today's values.
/// </summary>
internal sealed class FormSettingsService(IFormSettingsReader reader, IFormSettingsRepository repository, IUnitOfWork unitOfWork,
    IRequestValidator validator, ILogger<FormSettingsService> logger) : IFormSettingsService
{
    public Task<Result<FormSettingsResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormSettingsResponse>>(logger, "ReadFormSettings", async () => await reader.FindAsync(cancellationToken));

    public Task<Result<FormSettingsResponse>> UpdateAsync(UpdateFormSettingsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormSettingsResponse>>(logger, "UpdateFormSettings", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await repository.LockAsync(ct);
                var stored = await repository.GetAsync(ct);
                var settings = stored ?? BusinessFormsSettings.Create();
                var updated = settings.Update(request.ExpectedRevision!.Value, request.MaxFileSizeMiB!.Value, request.DailyRereadsPerAsker!.Value,
                    request.SimilarDocumentsToSuggest!.Value, request.ProposedExpiryDays!.Value, request.DiscardedRetentionDays!.Value);
                if (updated.IsFailure) return Result.Failure<FormSettingsResponse>(updated.Error);
                if (stored is null) repository.Add(settings);
                return Result.Success(FormSettingsResponse.From(settings));
            }, CommitPolicy.OnSuccess, cancellationToken);
        });
}
