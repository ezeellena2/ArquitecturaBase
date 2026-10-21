using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormSuggestionReader(ApplicationDbContext dbContext) : IFormSuggestionReader
{
    public Task<FormSuggestionSetStoredRow?> FindSetAsync(Guid id,CancellationToken ct)=>Stored(dbContext.Set<BusinessFormSuggestionSet>().AsNoTracking()
        .Where(value=>value.Id==id)).SingleOrDefaultAsync(ct);
    public Task<bool> ExistsDefinitionAsync(Guid id,CancellationToken ct)=>dbContext.Set<BusinessFormDefinition>().AsNoTracking().AnyAsync(value=>value.Id==id,ct);
    public Task<FormSuggestionSetStoredRow?> FindCurrentAsync(Guid id,CancellationToken ct)=>Stored(dbContext.Set<BusinessFormSuggestionSet>().AsNoTracking()
        .Where(value=>value.DefinitionId==id&&dbContext.Set<BusinessFormDefinition>().Any(definition=>definition.Id==id&&definition.ActiveSuggestionSetId==value.Id))).SingleOrDefaultAsync(ct);
    public Task<FormSuggestionSetStoredRow?> FindRevisionAsync(Guid id,long revision,CancellationToken ct)=>Stored(dbContext.Set<BusinessFormSuggestionSet>().AsNoTracking()
        .Where(value=>value.DefinitionId==id&&value.Revision==revision)).SingleOrDefaultAsync(ct);
    public async Task<FormSuggestionOptionsResponse?> FindOptionsAsync(Guid id,CancellationToken ct)
    {
        var rows=await FormOfferableVersionQuery.Versions(dbContext)
            .OrderBy(version=>version.DefinitionId).Select(version=>new{version.DefinitionId,version.Id,version.NameJson,version.Culture,version.SchemaJson}).ToArrayAsync(ct);
        var source=rows.SingleOrDefault(row=>row.DefinitionId==id);if(source is null)return null;
        var forms=rows.Select(row=>new FormSuggestionPublishedForm(row.DefinitionId,row.Id,Read<Dictionary<string,string>>(row.NameJson!),row.Culture!,Read<FormVersionSchema>(row.SchemaJson).Questions)).ToArray();
        return new(forms.Single(form=>form.DefinitionId==id),forms.Where(form=>form.DefinitionId!=id).ToArray());
    }
    public Task<PagedResult<FormSuggestionRevisionStoredRow>> ListRevisionsAsync(Guid id,ListFormSuggestionRevisionsRequest request,CancellationToken ct)
    {
        var sortable=new Dictionary<string,Expression<Func<BusinessFormSuggestionSet,object?>>>(StringComparer.OrdinalIgnoreCase){["revision"]=value=>value.Revision};
        return dbContext.Set<BusinessFormSuggestionSet>().AsNoTracking().Where(value=>value.DefinitionId==id)
            .ApplySort(SortDescriptor.Parse(request.Sort),sortable,new("revision",true),value=>value.Id)
            .Select(value=>new FormSuggestionRevisionStoredRow(value.Id,value.Revision,value.CreatedAtUtc,
                value.CreatedBy==null?null:new FormAuthorResponse(value.CreatedBy.Value,dbContext.Users.Where(user=>user.Id==value.CreatedBy).Select(user=>user.DisplayName).FirstOrDefault()),value.SuggestionsJson,
                dbContext.Set<BusinessFormSuggestionSet>().Where(previous=>previous.DefinitionId==value.DefinitionId&&previous.Revision<value.Revision)
                    .OrderByDescending(previous=>previous.Revision).Select(previous=>previous.SuggestionsJson).FirstOrDefault()))
            .ToPagedResultAsync(request,ct);
    }
    private IQueryable<FormSuggestionSetStoredRow> Stored(IQueryable<BusinessFormSuggestionSet> query)=>query.Select(value=>new FormSuggestionSetStoredRow(value.Id,value.DefinitionId,value.SourceVersionId,value.Revision,
        value.OriginalCulture,value.SuggestionsJson,value.CreatedAtUtc,value.CreatedBy==null?null:new FormAuthorResponse(value.CreatedBy.Value,dbContext.Users.Where(user=>user.Id==value.CreatedBy).Select(user=>user.DisplayName).FirstOrDefault())));
    private static T Read<T>(string json)=>JsonSerializer.Deserialize<T>(json,JsonSerializerOptions.Web)??throw new InvalidOperationException("The stored suggestion metadata is invalid.");
}
