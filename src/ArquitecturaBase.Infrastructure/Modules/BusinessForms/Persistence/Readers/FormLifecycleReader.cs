using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormLifecycleReader(ApplicationDbContext dbContext) : IFormLifecycleReader
{
    public async Task<IReadOnlyList<FormLifecycleStoredRow>> ListFactsAsync(IReadOnlyCollection<Guid> definitionIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => definitionIds.Contains(definition.Id))
            .Select(definition => new FormLifecycleStoredRow(definition.Id, definition.Revision, definition.Prefix,
                definition.ActiveVersionId,
                (dbContext.Set<BusinessFormVersion>().Where(version => version.DefinitionId == definition.Id)
                    .Select(version => (long?)version.Number).Max() ?? 0) + 1,
                definition.LastRecordNumber,
                dbContext.Set<BusinessFormTask>().Count(task => task.DefinitionId == definition.Id && task.State == FormTaskState.Open),
                definition.DeactivatedAtUtc,
                definition.DeactivatedBy == null ? null : new FormAuthorResponse(definition.DeactivatedBy.Value,
                    dbContext.Users.Where(user => user.Id == definition.DeactivatedBy).Select(user => user.DisplayName).FirstOrDefault()),
                dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == definition.Id
                    && operation.State != FormPublicationState.Published && operation.State != FormPublicationState.Failed)))
            .ToArrayAsync(cancellationToken);

    public async Task<IReadOnlyList<FormIncomingSuggestionResponse>> ListIncomingSuggestionsAsync(Guid definitionId,
        CancellationToken cancellationToken)
    {
        var target = JsonSerializer.Serialize(new[] { new { destinationDefinitionId = definitionId, isActive = true } }, JsonSerializerOptions.Web);
        var offerable = FormOfferableVersionQuery.Versions(dbContext);
        var rows = await (from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking()
                          join suggestions in dbContext.Set<BusinessFormSuggestionSet>() on definition.ActiveSuggestionSetId equals suggestions.Id
                          where offerable.Any(version => version.DefinitionId == definition.Id)
                              && EF.Functions.JsonContains(suggestions.SuggestionsJson, target)
                          orderby definition.Name, definition.Id
                          select new { definition.Id, definition.NameJson }).ToArrayAsync(cancellationToken);
        return rows.Select(row => new FormIncomingSuggestionResponse(row.Id,
            JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("The stored form name is invalid."))).ToArray();
    }
}
