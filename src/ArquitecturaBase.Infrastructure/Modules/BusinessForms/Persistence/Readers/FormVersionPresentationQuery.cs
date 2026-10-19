using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal static class FormVersionPresentationQuery
{
    internal static IQueryable<VersionNameRow> Names(ApplicationDbContext db, IEnumerable<FormResponseVersionRow> versions, string culture)
    {
        var rows = versions.Select(version => new VersionNameRow { VersionId = version.Id,
            Name = JsonSerializer.Deserialize<Dictionary<string, string>>(version.NameJson)![version.DisplayCulture(culture)] }).ToArray();
        return db.Database.SqlQueryRaw<VersionNameRow>(
            """SELECT (value->>'versionId')::uuid AS "VersionId", value->>'name' AS "Name" FROM jsonb_array_elements({0}::jsonb) AS value""",
            JsonSerializer.Serialize(rows, JsonSerializerOptions.Web));
    }

    internal sealed class VersionNameRow
    {
        public Guid VersionId { get; init; }
        public string Name { get; init; } = "";
    }
}
