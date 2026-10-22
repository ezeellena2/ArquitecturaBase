using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Seed;

internal sealed class FormVersionPresentationSeeder(ApplicationDbContext dbContext, IFormPublicationProtector protector) : IDatabaseSeedParticipant
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var legacy = await (from version in dbContext.Set<BusinessFormVersion>().Where(value => value.NameJson == null || value.PublishedCultures == null)
                            join operation in dbContext.Set<BusinessFormPublicationOperation>() on version.Id equals operation.VersionId
                            where operation.DefinitionId == version.DefinitionId
                            select new { Version = version, Operation = operation }).ToListAsync(cancellationToken);
        foreach (var row in legacy)
        {
            var operation = row.Operation; var version = row.Version;
            var manifest = protector.ReadManifest(new(operation.Id, operation.OperatorUserId, operation.RequestId,
                operation.DefinitionId, operation.VersionId), operation.ProtectedManifest);
            if (manifest.Format != 1 || manifest.VersionId != version.Id || manifest.DefinitionId != version.DefinitionId
                || manifest.ContentHash != version.ContentHash || (version.Culture is not null && version.Culture != manifest.OriginalCulture))
                throw new InvalidOperationException("The sealed version presentation does not match its publication manifest.");
            if (version.NameJson is null)
            {
                var schema = JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("The sealed publication schema is invalid.");
                version.SealPresentation(JsonSerializer.Serialize(manifest.Name, JsonSerializerOptions.Web),
                    JsonSerializer.Serialize(manifest.SubmitLabel, JsonSerializerOptions.Web), manifest.OriginalCulture, schema.Questions.Count);
            }
            if (version.PublishedCultures is null)
                version.SealPublishedCultures(manifest.Profiles.SelectMany(profile => profile.Cultures).Select(culture => culture.Culture)
                    .Distinct(StringComparer.Ordinal).ToArray());
        }
    }
}
