using System.Reflection;
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