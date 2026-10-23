using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogFieldUsageQueryTests
{
    [Fact]
    public void Field_usage_checks_match_and_presentation_in_drafts_and_active_versions_without_excluding_deactivated_forms()
    {
        var sql = FieldUsageSql();

        Assert.Contains("definition.\"SchemaJson\" -> 'fields'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"SchemaJson\" -> 'questions'", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"Id\" = definition.\"ActiveVersionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("version.\"DefinitionId\" = definition.\"Id\"", sql, StringComparison.Ordinal);
        Assert.Contains("'match'", sql, StringComparison.Ordinal);
        Assert.Contains("'presentation', 'title'", sql, StringComparison.Ordinal);
        Assert.Contains("'presentation', 'description'", sql, StringComparison.Ordinal);
        Assert.Contains("'Draft'", sql, StringComparison.Ordinal);
        Assert.Contains("'Active'", sql, StringComparison.Ordinal);
        Assert.Contains("UNNEST", sql, StringComparison.Ordinal);
        Assert.Contains("requested.\"FieldId\"", sql, StringComparison.Ordinal);
        Assert.Contains("NOT definition.\"IsDeleted\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DeactivatedAtUtc", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Field_usage_returns_the_question_identity_of_draft_fields_and_active_questions()
    {
        var sql = FieldUsageSql();

        // Una vez en la rama del borrador y otra en la de la versión activa; el orden desempata por la identidad, así dos
        // preguntas con el mismo rótulo no se intercalan.
        Assert.Equal(2, sql.Split("(question.value ->> 'fieldId')::uuid AS \"QuestionId\"", StringSplitOptions.None).Length - 1);
        Assert.Contains("ORDER BY \"FormName\", \"DefinitionId\", \"Question\", \"QuestionId\", \"Stage\"", sql, StringComparison.Ordinal);
    }

    private static string FieldUsageSql()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var method = typeof(FormCatalogReader).GetMethod("FieldUsages", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.Equal(typeof(IReadOnlyCollection<Guid>), method.GetParameters()[1].ParameterType);
        var query = Assert.IsAssignableFrom<IQueryable>(method.Invoke(new FormCatalogReader(db), [Guid.NewGuid(), new[] { Guid.NewGuid(), Guid.NewGuid() }, "en"]));
        return query.ToQueryString();
    }
}
