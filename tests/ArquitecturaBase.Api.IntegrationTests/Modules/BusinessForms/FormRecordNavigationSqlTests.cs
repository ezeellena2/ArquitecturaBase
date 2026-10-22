using System.Reflection;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormRecordNavigationSqlTests
{
    [Fact]
    public void Navigation_keeps_nested_ordering_and_quoted_keywords_inside_their_original_scope()
    {
        const string sql = """
            SELECT b."Id"
            FROM "BusinessFormRecords" AS b
            WHERE EXISTS (SELECT 1 FROM "Values" AS v WHERE v."Text" = 'FROM ORDER BY it''s (text)' ORDER BY v."Id")
              AND b."State" <> E'ORDER BY \' quoted'
            ORDER BY (SELECT v."FROM" FROM "Values" AS v WHERE v."Id" = b."Id" ORDER BY v."ORDER BY" LIMIT 1) DESC NULLS LAST, b."Number"
            """;
        var rewritten = Rewrite(sql);
        Assert.Contains("WHERE EXISTS (SELECT 1 FROM \"Values\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("ORDER BY v.\"Id\")", rewritten, StringComparison.Ordinal);
        Assert.Contains("'FROM ORDER BY it''s (text)'", rewritten, StringComparison.Ordinal);
        Assert.Contains("WINDOW record_order AS (ORDER BY (SELECT v.\"FROM\"", rewritten, StringComparison.Ordinal);
        Assert.Contains("ORDER BY v.\"ORDER BY\" LIMIT 1) DESC NULLS LAST, b.\"Number\")", rewritten, StringComparison.Ordinal);
        Assert.Contains("WHERE \"RecordId\" = @navigation_record_id", rewritten, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SELECT b.\"Id\" FROM \"Rows\" AS b")]
    [InlineData("SELECT DISTINCT b.\"Id\" FROM \"Rows\" AS b ORDER BY b.\"Id\"")]
    [InlineData("SELECT b.\"Id\", b.\"Number\" FROM \"Rows\" AS b ORDER BY b.\"Id\"")]
    [InlineData("SELECT b.\"Id\" FROM \"Rows\" AS b ORDER BY b.\"Id\" LIMIT 1")]
    [InlineData("SELECT b.\"Id\" FROM \"Rows\" AS b ORDER BY b.\"Id\"; SELECT 1")]
    [InlineData("SELECT b.\"Id\" FROM \"Rows\" AS b WHERE b.\"Name\" = 'unclosed ORDER BY b.\"Id\"")]
    [InlineData("WITH rows AS (SELECT 1) SELECT b.\"Id\" FROM rows b ORDER BY b.\"Id\"")]
    public void Navigation_rejects_a_query_shape_outside_its_ef_projection(string sql)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => Rewrite(sql));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    private static string Rewrite(string sql)
    {
        var type = typeof(FormResponseReader).Assembly.GetType("ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers.FormRecordNavigationSql");
        Assert.NotNull(type);
        var method = type.GetMethod("Create", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method.Invoke(null, [sql]));
    }
}
