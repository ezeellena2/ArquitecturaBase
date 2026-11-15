using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSelectionQueryTests
{
    [Theory]
    [InlineData("Active", true)]
    [InlineData("Published", false)]
    public void Published_selection_requires_sealed_presentation_and_a_confirmed_publication(string method, bool requiresActiveVersion)
    {
        var sql = QuerySql(method);

        Assert.Contains("\"NameJson\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"SubmitLabelJson\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"QuestionCount\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"Culture\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"State\" = 'Published'", sql, StringComparison.Ordinal);
        Assert.Equal(requiresActiveVersion, sql.Contains("\"ActiveVersionId\"", StringComparison.Ordinal));
        Assert.Equal(requiresActiveVersion, sql.Contains("\"DeactivatedAtUtc\" IS NULL", StringComparison.Ordinal));
        Assert.DoesNotContain("\"OfferWhen\"", sql, StringComparison.Ordinal);
    }

    // SA2-A: what channels offer (sending, the assistant and suggestions) leaves out versions completed only in the app; an
    // accepted session belongs to a version published in its channel.
    [Theory]
    [InlineData("Active", true)]
    [InlineData("Assistant", true)]
    [InlineData("Published", false)]
    public void Channel_selection_leaves_out_versions_completed_only_in_the_app(string method, bool filtered)
    {
        Assert.Equal(filtered, QuerySql(method).Contains("\"AppOnly\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("Published")]
    public void Selection_excludes_deleted_definitions_even_for_an_accepted_historical_version(string method)
    {
        Assert.Matches("""NOT \(?\w+\."IsDeleted"\)?""", QuerySql(method));
    }

    [Fact]
    public void Assistant_instructions_are_an_additional_filter_over_offerable_versions()
    {
        var sql = QuerySql("Assistant");

        Assert.Contains("\"ActiveVersionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"State\" = 'Published'", sql, StringComparison.Ordinal);
        Assert.Contains("\"DeactivatedAtUtc\" IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"OfferWhen\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"OfferWhen\" <> ''", sql, StringComparison.Ordinal);
    }

    // F48: the assistant's settings list the first 100 forms by name in the asker's language, cut in SQL after ordering, not the
    // first 101 by id.
    [Theory]
    [InlineData("es", "DisplayNameEs")]
    [InlineData("en", "DisplayNameEn")]
    public void The_assistant_form_list_is_cut_after_ordering_by_name_in_the_askers_language(string culture, string column)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var query = Assert.IsAssignableFrom<IQueryable<FormAssistantCandidateRow>>(typeof(FormSelectionReader)
            .GetMethod("ChannelForms", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(new FormSelectionReader(db), [culture]));

        var sql = query.ToQueryString();

        var order = sql.IndexOf("ORDER BY", StringComparison.Ordinal);
        Assert.True(order >= 0 && order < sql.IndexOf("LIMIT", StringComparison.Ordinal), sql);
        Assert.Contains(column, sql[order..sql.IndexOf("LIMIT", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Equal(100, FormSelectionReader.ChannelFormsLimit);
    }

    private static string QuerySql(string method)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormSelectionReader(db);
        var queryMethod = typeof(FormSelectionReader).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(queryMethod);
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormVersion>>(queryMethod.Invoke(reader, []));
        return query.Select(version => version.Id).ToQueryString();
    }
}