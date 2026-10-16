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
internal sealed class FormPdfDesignService(IFormDraftRepository drafts, IFormPdfDesignRepository repository, IFormPdfDesignReader reader,
    FormRuleAccessGuard access, IUnitOfWork unitOfWork, IRequestValidator validator, FormPdfPreviewIssuer preview, ILogger<FormPdfDesignService> logger) : IFormPdfDesignService
{
    public Task<Result<FormPdfDesignResponse>> GetCurrentAsync(Guid definitionId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfDesignResponse>>(logger, "ReadFormPdfDesign", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            var row = await reader.FindCurrentAsync(definitionId, cancellationToken);
            if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            return row is null ? new FormPdfDesignResponse(null, definitionId, 0, definition.OriginalCulture, FormPdfDesignPolicy.Default(definition), null, null) : Map(row);
        });
    public Task<Result<FormPdfDesignResponse>> SaveAsync(Guid definitionId, SaveFormPdfDesignRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfDesignResponse>>(logger, "SaveFormPdfDesign", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var saved = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await drafts.LockDefinitionsAsync(ct);
                if (!await access.StillAllowedAsync(actor.Value, ct)) return Result.Failure<long>(FormDefinitionErrors.Forbidden);
                var definition = await drafts.GetAsync(definitionId, ct);
                if (definition is null) return Result.Failure<long>(FormDefinitionErrors.NotFound);
                var current = definition.ActivePdfDesignId is { } id ? await repository.GetAsync(id, ct) : null;
                if ((current?.Revision ?? 0) != request.ExpectedRevision) return Result.Failure<long>(FormPdfErrors.RevisionConflict);
                var options = await reader.FindDefinitionAsync(definitionId, ct) ?? throw new InvalidOperationException("The PDF definition is missing from its reader.");
                var normalized = FormPdfDesignPolicy.Normalize(request.Design, options.OriginalCulture, FormPdfDesignPolicy.KnownQuestions(options).Concat(current is null ? [] : FormDraftPolicy.Read<FormPdfDesign>(current.DesignJson).QuestionIds).Distinct().ToArray());
                if (normalized.IsFailure) return Result.Failure<long>(normalized.Error);
                var design = BusinessFormPdfDesign.Create(definitionId, (current?.Revision ?? 0) + 1, options.OriginalCulture, FormDraftPolicy.Serialize(normalized.Value));
                repository.Add(design); definition.SetActivePdfDesignId(design.Id);
                return Result.Success(design.Revision);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (saved.IsFailure) return saved.Error;
            var row = await reader.FindRevisionAsync(definitionId, saved.Value, cancellationToken) ?? throw new InvalidOperationException("The confirmed PDF design is missing.");
            return await access.StillAllowedAsync(actor.Value, cancellationToken) ? Map(row) : FormDefinitionErrors.Forbidden;
        });
    public Task<Result<FormPdfContentResponse>> PreviewAsync(Guid definitionId, PreviewFormPdfDesignRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPdfContentResponse>>(logger, "PreviewFormPdfDesign", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            var current = await reader.FindCurrentAsync(definitionId, cancellationToken);
            var known = FormPdfDesignPolicy.KnownQuestions(definition).Concat(current is null ? [] : FormDraftPolicy.Read<FormPdfDesign>(current.DesignJson).QuestionIds).Distinct().ToArray();
            var normalized = FormPdfDesignPolicy.Normalize(request.Design, definition.OriginalCulture, known);
            if (normalized.IsFailure) return normalized.Error;
            var content = await preview.IssueAsync(definition, normalized.Value, request.Culture!, cancellationToken);
            try
            {
                if (await access.StillAllowedAsync(actor.Value, cancellationToken)) return content;
                await content.Content.DisposeAsync(); return FormDefinitionErrors.Forbidden;
            }
            catch { await content.Content.DisposeAsync(); throw; }
        });
    private static FormPdfDesignResponse Map(FormPdfDesignStoredRow row) => new(row.Id, row.DefinitionId, row.Revision, row.OriginalCulture,
        FormDraftPolicy.Read<FormPdfDesign>(row.DesignJson), row.CreatedAtUtc, row.Author);
}
