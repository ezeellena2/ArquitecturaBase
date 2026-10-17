using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormSuggestionService(IFormDraftRepository drafts,IFormSuggestionRepository repository,IFormSuggestionReader reader,
    FormSuggestionAccessGuard access,IUnitOfWork unitOfWork,IRequestValidator validator,FormConditionChoicePolicy choiceOptions,ILogger<FormSuggestionService> logger) : IFormSuggestionService
{
    public Task<Result<FormSuggestionSetResponse>> GetCurrentAsync(Guid id,CancellationToken ct) => OperationLog.RunAsync<Result<FormSuggestionSetResponse>>(logger,"ReadFormSuggestions",()=>GetCurrentCoreAsync(id,ct));
    public Task<Result<FormSuggestionSetResponse>> SaveAsync(Guid id,SaveFormSuggestionsRequest request,CancellationToken ct) => OperationLog.RunAsync<Result<FormSuggestionSetResponse>>(logger,"SaveFormSuggestions",()=>SaveCoreAsync(id,request,ct));
    public Task<Result<PagedResult<FormSuggestionRevisionResponse>>> ListRevisionsAsync(Guid id,ListFormSuggestionRevisionsRequest request,CancellationToken ct) => OperationLog.RunAsync<Result<PagedResult<FormSuggestionRevisionResponse>>>(logger,"ListFormSuggestionRevisions",()=>ListRevisionsCoreAsync(id,request,ct));
    public Task<Result<FormSuggestionSetResponse>> GetRevisionAsync(Guid id,long revision,CancellationToken ct) => OperationLog.RunAsync<Result<FormSuggestionSetResponse>>(logger,"ReadFormSuggestionRevision",()=>GetRevisionCoreAsync(id,revision,ct));
    public Task<Result<FormSuggestionOptionsResponse>> GetOptionsAsync(Guid id,CancellationToken ct) => OperationLog.RunAsync<Result<FormSuggestionOptionsResponse>>(logger,"ReadFormSuggestionOptions",()=>GetOptionsCoreAsync(id,ct));
    private async Task<Result<FormSuggestionSetResponse>> GetCurrentCoreAsync(Guid id,CancellationToken ct)
    {
        var actor=await access.CheckAsync(ct);if(actor.IsFailure)return actor.Error;
        if(!await reader.ExistsDefinitionAsync(id,ct))return FormDefinitionErrors.NotFound;
        var current=await reader.FindCurrentAsync(id,ct);
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        return current is null ? new FormSuggestionSetResponse(null,id,null,0,[],null,null) : Map(current);
    }
    private async Task<Result<FormSuggestionSetResponse>> SaveCoreAsync(Guid id,SaveFormSuggestionsRequest request,CancellationToken ct)
    {
        var actor=await access.CheckAsync(ct);if(actor.IsFailure)return actor.Error;
        if(await validator.ValidateAsync(request,ct)is{}invalid)return invalid;
        var saved=await unitOfWork.ExecuteInTransactionAsync(async innerCt=>
        {
            await drafts.LockDefinitionsAsync(innerCt);
            if(!await access.StillAllowedAsync(actor.Value,innerCt))return Result.Failure<long>(FormDefinitionErrors.Forbidden);
            var definition=await drafts.GetAsync(id,innerCt);if(definition is null)return Result.Failure<long>(FormDefinitionErrors.NotFound);
            var current=definition.ActiveSuggestionSetId is{}currentId?await repository.GetAsync(currentId,innerCt):null;
            if((current?.Revision??0)!=request.ExpectedRevision)return Result.Failure<long>(FormSuggestionErrors.RevisionConflict);
            var options=await reader.FindOptionsAsync(id,innerCt);
            if(options is null)return Result.Failure<long>(FormSuggestionErrors.SourceNotPublished);
            var normalized=FormSuggestionPolicy.Normalize(request,options);if(normalized.IsFailure)return Result.Failure<long>(normalized.Error);
            if(!await access.StillAllowedAsync(actor.Value,innerCt))return Result.Failure<long>(FormDefinitionErrors.Forbidden);
            var snapshot=BusinessFormSuggestionSet.Create(id,options.Source.VersionId,(current?.Revision??0)+1,options.Source.OriginalCulture,FormDraftPolicy.Serialize(normalized.Value));
            repository.Add(snapshot);definition.SetActiveSuggestionSetId(snapshot.Id);
            return Result.Success(snapshot.Revision);
        },CommitPolicy.OnSuccess,ct);
        if(saved.IsFailure)return saved.Error;
        var row=await reader.FindRevisionAsync(id,saved.Value,ct)??throw new InvalidOperationException("The confirmed suggestion revision is missing.");
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        return Map(row);
    }
    private async Task<Result<PagedResult<FormSuggestionRevisionResponse>>> ListRevisionsCoreAsync(Guid id,ListFormSuggestionRevisionsRequest request,CancellationToken ct)
    {
        var actor=await access.CheckAsync(ct);if(actor.IsFailure)return actor.Error;
        if(await validator.ValidateAsync(request,ct)is{}invalid)return invalid;
        if(!await reader.ExistsDefinitionAsync(id,ct))return FormDefinitionErrors.NotFound;
        var rows=await reader.ListRevisionsAsync(id,request,ct);
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        return new PagedResult<FormSuggestionRevisionResponse>(rows.Items.Select(row=>new FormSuggestionRevisionResponse(row.Id,row.Revision,row.CreatedAtUtc,row.Author,
            FormSuggestionHistoryPolicy.Compare(FormDraftPolicy.Read<FormSuggestion[]>(row.SuggestionsJson),row.PreviousSuggestionsJson is null?[]:FormDraftPolicy.Read<FormSuggestion[]>(row.PreviousSuggestionsJson)))).ToArray(),rows.Page,rows.PageSize,rows.TotalCount);
    }
    private async Task<Result<FormSuggestionSetResponse>> GetRevisionCoreAsync(Guid id,long revision,CancellationToken ct)
    {
        var actor=await access.CheckAsync(ct);if(actor.IsFailure)return actor.Error;
        var row=await reader.FindRevisionAsync(id,revision,ct);
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        return row is null?FormSuggestionErrors.RevisionNotFound:Map(row);
    }
    private async Task<Result<FormSuggestionOptionsResponse>> GetOptionsCoreAsync(Guid id,CancellationToken ct)
    {
        var actor=await access.CheckAsync(ct);if(actor.IsFailure)return actor.Error;
        if(!await reader.ExistsDefinitionAsync(id,ct))return FormDefinitionErrors.NotFound;
        var options=await reader.FindOptionsAsync(id,ct);
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        if(options is null)return FormSuggestionErrors.SourceNotPublished;
        var culture=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="en"?"en":"es";
        var choices=await choiceOptions.ReadAsync(actor.Value,options.Source.Questions,culture,ct);
        if(!await access.StillAllowedAsync(actor.Value,ct))return FormDefinitionErrors.Forbidden;
        return options with {ConditionChoices=choices};
    }
    private static FormSuggestionSetResponse Map(FormSuggestionSetStoredRow row)=>new(row.Id,row.DefinitionId,row.SourceVersionId,row.Revision,
        FormDraftPolicy.Read<FormSuggestion[]>(row.SuggestionsJson),row.CreatedAtUtc,row.Author);
}
