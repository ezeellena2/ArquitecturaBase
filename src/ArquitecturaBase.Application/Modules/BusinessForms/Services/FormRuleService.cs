using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRuleService(IFormDraftRepository drafts, IFormRuleRepository repository, IFormRuleReader reader,
    FormRuleAccessGuard access, IUnitOfWork unitOfWork, IRequestValidator validator, FormRuleOptionsPolicy optionsBuilder, ILogger<FormRuleService> logger) : IFormRuleService
{
    public Task<Result<FormRuleSetResponse>> GetCurrentAsync(Guid definitionId, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormRuleSetResponse>>(logger, "ReadFormRules", () => GetCurrentCoreAsync(definitionId,cancellationToken));
    public Task<Result<PagedResult<FormRuleRevisionResponse>>> ListRevisionsAsync(Guid definitionId, ListFormRuleRevisionsRequest request, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<PagedResult<FormRuleRevisionResponse>>>(logger, "ListFormRuleRevisions", () => ListRevisionsCoreAsync(definitionId,request,cancellationToken));
    public Task<Result<FormRuleSetResponse>> GetRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormRuleSetResponse>>(logger, "ReadFormRuleRevision", () => GetRevisionCoreAsync(definitionId,revision,cancellationToken));
    public Task<Result<FormRuleOptionsResponse>> GetOptionsAsync(Guid definitionId, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormRuleOptionsResponse>>(logger, "ReadFormRuleOptions", () => GetOptionsCoreAsync(definitionId,cancellationToken));
    public Task<Result<FormRuleSetResponse>> SaveAsync(Guid definitionId, SaveFormRulesRequest request, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormRuleSetResponse>>(logger, "SaveFormRules", () => SaveCoreAsync(definitionId,request,cancellationToken));

    private async Task<Result<FormRuleSetResponse>> GetCurrentCoreAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
        if (definition is null) return FormDefinitionErrors.NotFound;
        var row = await reader.FindCurrentAsync(definitionId, cancellationToken);
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return row is null ? new FormRuleSetResponse(null, definitionId, 0, definition.OriginalCulture, [], null, null) : Map(row);
    }
    private async Task<Result<PagedResult<FormRuleRevisionResponse>>> ListRevisionsCoreAsync(Guid definitionId, ListFormRuleRevisionsRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (await reader.FindDefinitionAsync(definitionId, cancellationToken) is null) return FormDefinitionErrors.NotFound;
        var result = await reader.ListRevisionsAsync(definitionId, request, cancellationToken);
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return new PagedResult<FormRuleRevisionResponse>(result.Items.Select(MapHistory).ToArray(), result.Page, result.PageSize, result.TotalCount);
    }
    private async Task<Result<FormRuleSetResponse>> GetRevisionCoreAsync(Guid definitionId, long revision, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        var row = await reader.FindRevisionAsync(definitionId, revision, cancellationToken);
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return row is null ? FormRuleErrors.RevisionNotFound : Map(row);
    }
    private async Task<Result<FormRuleOptionsResponse>> GetOptionsCoreAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        var options = await optionsBuilder.ReadAsync(actor.Value, definitionId, cancellationToken);
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return options is null ? FormDefinitionErrors.NotFound : options;
    }
    private async Task<Result<FormRuleSetResponse>> SaveCoreAsync(Guid definitionId, SaveFormRulesRequest request, CancellationToken cancellationToken)
    {
        var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var saved = await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await drafts.LockDefinitionsAsync(ct);
            if (!await access.StillAllowedAsync(actor.Value, ct)) return Result.Failure<long>(FormDefinitionErrors.Forbidden);
            var definition = await drafts.GetAsync(definitionId, ct);
            if (definition is null) return Result.Failure<long>(FormDefinitionErrors.NotFound);
            var current = definition.ActiveRuleSetId is { } currentId ? await repository.GetAsync(currentId, ct) : null;
            if ((current?.Revision ?? 0) != request.ExpectedRevision) return Result.Failure<long>(FormRuleErrors.RevisionConflict);
            var options = await optionsBuilder.ReadAsync(actor.Value, definitionId, ct) ?? throw new InvalidOperationException("The tracked form definition is missing from its reader.");
            var rules = FormRulePolicy.Normalize(request, options); if (rules.IsFailure) return Result.Failure<long>(rules.Error);
            var ruleSet = BusinessFormRuleSet.Create(definitionId, (current?.Revision ?? 0) + 1, options.OriginalCulture,
                FormDraftPolicy.Serialize(rules.Value), rules.Value.SelectMany(rule => rule.Actions).Any(action => action.Kind == "RequestApproval"),
                FormDraftPolicy.Serialize(new FormVersionSchema(1, options.Questions)));
            repository.Add(ruleSet);
            definition.SetActiveRuleSetId(ruleSet.Id);
            return Result.Success(ruleSet.Revision);
        }, CommitPolicy.OnSuccess, cancellationToken);
        if (saved.IsFailure) return saved.Error;
        var row = await reader.FindRevisionAsync(definitionId, saved.Value, cancellationToken)
            ?? throw new InvalidOperationException("The confirmed rule revision is missing.");
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return Map(row);
    }
    private static FormRuleRevisionResponse MapHistory(FormRuleRevisionStoredRow row) => new(row.Id, row.Revision, row.CreatedAtUtc, row.Author, row.HasApproval,
        FormRuleHistoryPolicy.Compare(FormDraftPolicy.Read<FormRule[]>(row.RulesJson),
            row.PreviousRulesJson is null ? [] : FormDraftPolicy.Read<FormRule[]>(row.PreviousRulesJson)));
    private static FormRuleSetResponse Map(FormRuleSetStoredRow row) => new(row.Id, row.DefinitionId, row.Revision, row.OriginalCulture,
        FormDraftPolicy.Read<FormRule[]>(row.RulesJson), row.CreatedAtUtc, row.Author);
}
