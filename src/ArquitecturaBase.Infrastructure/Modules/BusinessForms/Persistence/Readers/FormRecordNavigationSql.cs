namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

// Accepts only the single-Id projection EF produces for the sheet's filter (NavigationIds, and the dashboard's matching responses),
// not arbitrary SQL.
internal static class FormRecordNavigationSql
{
    public static string Create(string sql)
    {
        var tokens = OuterTokens(sql);
        var (id, from) = Projection(sql, tokens);
        var order = tokens.FindIndex(token => token.Text == "ORDER");
        if (order <= from || order + 1 >= tokens.Count || tokens[order + 1].Text != "BY" || tokens.Count(token => token.Text == "ORDER") != 1)
            throw UnexpectedShape();
        var source = sql[tokens[from].Start..tokens[order].Start].TrimEnd();
        var ordering = sql[tokens[order + 1].End..].Trim();
        if (ordering.Length == 0) throw UnexpectedShape();
        return $"""
            SELECT "RecordId", "PreviousId", "NextId", "Position", "TotalCount"
            FROM (
                SELECT {id} AS "RecordId",
                    LAG({id}) OVER record_order AS "PreviousId",
                    LEAD({id}) OVER record_order AS "NextId",
                    (ROW_NUMBER() OVER record_order)::integer AS "Position",
                    (COUNT(*) OVER ())::integer AS "TotalCount"
                {source}
                WINDOW record_order AS (ORDER BY {ordering})
            ) AS navigation
            WHERE "RecordId" = @navigation_record_id
            """;
    }

    /// <summary>The dashboard's matching responses (DOC7-A): the same projection without an order, kept as it is to embed it.</summary>
    public static string Ids(string sql)
    {
        var tokens = OuterTokens(sql);
        _ = Projection(sql, tokens);
        if (tokens.Any(token => token.Text == "ORDER")) throw UnexpectedShape();
        return sql.Trim();
    }

    // SELECT alias."Id" FROM …: one outer SELECT and one FROM, and nothing that changes which rows come back or how many.
    private static (string Id, int From) Projection(string sql, List<Token> tokens)
    {
        var from = tokens.FindIndex(token => token.Text == "FROM");
        if (tokens.Count == 0 || tokens[0].Text != "SELECT" || from < 1
            || tokens.Count(token => token.Text == "SELECT") != 1 || tokens.Count(token => token.Text == "FROM") != 1
            || tokens.Any(token => token.Text is "DISTINCT" or "GROUP" or "HAVING" or "UNION" or "INTERSECT"
                or "EXCEPT" or "LIMIT" or "OFFSET" or "FETCH" or "FOR" or "WINDOW"))
            throw UnexpectedShape();
        var id = sql[tokens[0].End..tokens[from].Start].Trim();
        var alias = id.EndsWith(".\"Id\"", StringComparison.Ordinal) ? id[..^5] : "";
        if (alias.Length == 0 || !IdentifierStart(alias[0]) || alias.Any(character => !IdentifierPart(character)))
            throw UnexpectedShape();
        return (id, from);
    }

    private static List<Token> OuterTokens(string sql)
    {
        var tokens = new List<Token>();
        var depth = 0;
        for (var index = 0; index < sql.Length;)
        {
            var character = sql[index];
            if (character is '\'' or '"')
            {
                var escaped = character == '\'' && index > 0 && sql[index - 1] is 'e' or 'E'
                    && (index < 2 || !IdentifierPart(sql[index - 2]));
                index = QuotedEnd(sql, index, escaped);
            }
            else if (character == '(') { depth++; index++; }
            else if (character == ')') { if (--depth < 0) throw UnexpectedShape(); index++; }
            else if (character is ';' or '$' || (index + 1 < sql.Length &&
                ((character == '-' && sql[index + 1] == '-') || (character == '/' && sql[index + 1] == '*'))))
                throw UnexpectedShape();
            else if (character == '@')
            {
                index++;
                while (index < sql.Length && IdentifierPart(sql[index])) index++;
            }
            else if (IdentifierStart(character))
            {
                var start = index++;
                while (index < sql.Length && IdentifierPart(sql[index])) index++;
                if (depth == 0) tokens.Add(new(sql[start..index].ToUpperInvariant(), start, index));
            }
            else index++;
        }
        if (depth != 0) throw UnexpectedShape();
        return tokens;
    }

    private static int QuotedEnd(string sql, int start, bool escaped)
    {
        var quote = sql[start];
        for (var index = start + 1; index < sql.Length; index++)
        {
            if (escaped && sql[index] == '\\') { index++; continue; }
            if (sql[index] != quote) continue;
            if (index + 1 < sql.Length && sql[index + 1] == quote) { index++; continue; }
            return index + 1;
        }
        throw UnexpectedShape();
    }

    private static bool IdentifierStart(char character) => char.IsAsciiLetter(character) || character == '_';
    private static bool IdentifierPart(char character) => IdentifierStart(character) || char.IsAsciiDigit(character);
    private static InvalidOperationException UnexpectedShape() => new("The record query has an unexpected SQL shape.");
    private readonly record struct Token(string Text, int Start, int End);
}
