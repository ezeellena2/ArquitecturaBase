using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationAcceptanceGuard(FormPublicationAccessGuard accessGuard, IRequestValidator validator)
{
    public Task<Result<Guid>> CheckAccessAsync(CancellationToken cancellationToken) => accessGuard.CheckAsync(cancellationToken);

    public async Task<Result<FormPublicationAcceptanceInput>> CheckAsync(AcceptFormPublicationRequest request,
        CancellationToken cancellationToken)
    {
        var access = await accessGuard.CheckAsync(cancellationToken);
        if (access.IsFailure) return access.Error;
        var input = new SaveFormDraftRequest(request.RequestId, FormDraftPolicy.NormalizeName(request.Name)!,
            request.Prefix?.Trim().ToUpperInvariant()!, request.Draft, request.ExpectedRevision);
        if (await validator.ValidateAsync(input, cancellationToken) is { } invalid) return invalid;
        return new FormPublicationAcceptanceInput(access.Value, FormDraftPolicy.Normalize(input));
    }
}

internal sealed record FormPublicationAcceptanceInput(Guid Actor, SaveFormDraftRequest Draft);
