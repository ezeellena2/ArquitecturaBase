using System.Linq.Expressions;
using System.Reflection;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Persistence;

// OT1-C: the lists of users and roles sort by every column with a datum, in SQL; these tests build it without a connection.
public sealed class AdministrationListQueryTests
{
    // WhatsApp sorts by the saved number and Verificado by whether the person signed in with it. Without a number there is
    // neither: an ascending sort already leaves it last in Postgres, a descending one adds an «is null» term first. Equal
    // values keep the usual order of the list, the newest account first.
    [Fact]
    public void Users_sort_by_the_saved_number_and_by_whether_it_is_verified_with_accounts_without_one_last()
    {
        using var db = Context();
        using var users = new UserManager<ApplicationUser>(new UserStore<ApplicationUser, ApplicationRole, ApplicationDbContext, Guid>(db),
            null!, null!, null!, null!, null!, null!, null!, null!);
        var reader = new UserReader(users, null!, db, TimeProvider.System);
        string[] OrderBy(string sort) => OrderTerms(ListQuery(reader, new ListUsersRequest { Sort = sort }).ToQueryString());

        var byNumber = OrderBy("whatsapp");
        var byNumberDescending = OrderBy("-whatsapp");
        var byVerified = OrderBy("verified");
        var byVerifiedDescending = OrderBy("-verified");

        Assert.EndsWith("\"PhoneNumber\"", byNumber[0], StringComparison.Ordinal);
        Assert.EndsWith("\"CreatedAtUtc\" DESC", byNumber[1], StringComparison.Ordinal);
        Assert.EndsWith("\"PhoneNumber\" IS NULL", byNumberDescending[0], StringComparison.Ordinal);
        Assert.EndsWith("\"PhoneNumber\" DESC", byNumberDescending[1], StringComparison.Ordinal);
        Assert.EndsWith("\"CreatedAtUtc\" DESC", byNumberDescending[2], StringComparison.Ordinal);
        Assert.Contains("\"PhoneNumberConfirmed\"", byVerified[0], StringComparison.Ordinal);
        Assert.EndsWith("\"CreatedAtUtc\" DESC", byVerified[1], StringComparison.Ordinal);
        Assert.EndsWith("IS NULL", byVerifiedDescending[0], StringComparison.Ordinal);
        Assert.Contains("\"PhoneNumberConfirmed\"", byVerifiedDescending[1], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byVerifiedDescending[1], StringComparison.Ordinal);
        // The page with the roles of each user also translates.
        Assert.Contains("LIMIT", Page(ListQuery(reader, new ListUsersRequest { Sort = "-verified" })).ToQueryString(), StringComparison.Ordinal);
    }

    // The counts are those of the row, as subqueries of the same statement: no query per row. A count has no empty value, so
    // it adds no «is null» term; an empty description goes last. Equal values keep the usual order, the name.
    [Fact]
    public void Roles_sort_by_their_counts_in_the_same_statement_and_an_empty_description_goes_last()
    {
        using var db = Context();
        var reader = new RoleReader(db, null!);
        string[] OrderBy(string sort) => OrderTerms(ListQuery(reader, new ListRolesRequest { Sort = sort }).ToQueryString());

        var byUsers = OrderBy("-userCount");
        var byPermissions = OrderBy("permissions");
        var byDescription = OrderBy("-description");

        Assert.Contains("count(*)", byUsers[0], StringComparison.Ordinal);
        Assert.Contains("\"AspNetUserRoles\"", byUsers[0], StringComparison.Ordinal);
        Assert.EndsWith("DESC", byUsers[0], StringComparison.Ordinal);
        Assert.EndsWith("\"Name\"", byUsers[1], StringComparison.Ordinal);
        Assert.Contains("count(*)", byPermissions[0], StringComparison.Ordinal);
        Assert.Contains("\"AspNetRoleClaims\"", byPermissions[0], StringComparison.Ordinal);
        Assert.EndsWith("\"Name\"", byPermissions[1], StringComparison.Ordinal);
        Assert.EndsWith("\"Description\" IS NULL", byDescription[0], StringComparison.Ordinal);
        Assert.EndsWith("\"Description\" DESC", byDescription[1], StringComparison.Ordinal);
        Assert.EndsWith("\"Name\"", byDescription[2], StringComparison.Ordinal);
        // The page with the permissions of each role also translates.
        Assert.Contains("LIMIT", Page(ListQuery(reader, new ListRolesRequest { Sort = "-userCount" })).ToQueryString(), StringComparison.Ordinal);
    }

    private static IQueryable ListQuery(object reader, object request)
    {
        var method = reader.GetType().GetMethod("ListQuery", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [request]));
    }

    // The second page of ToPagedResultAsync, without running it.
    private static IQueryable Page(IQueryable query)
    {
        var skipped = Expression.Call(typeof(Queryable), nameof(Queryable.Skip), [query.ElementType], query.Expression, Expression.Constant(20));
        return query.Provider.CreateQuery(Expression.Call(typeof(Queryable), nameof(Queryable.Take), [query.ElementType], skipped, Expression.Constant(20)));
    }

    // The terms of the outer ORDER BY (the one that starts a line), split at its own commas, not at those of its subqueries.
    private static string[] OrderTerms(string sql)
    {
        const string start = "\nORDER BY ";
        var clause = sql[(sql.LastIndexOf(start, StringComparison.Ordinal) + start.Length)..];
        var terms = new List<string>();
        var depth = 0;
        var from = 0;
        for (var index = 0; index < clause.Length; index++)
        {
            depth += clause[index] switch { '(' => 1, ')' => -1, _ => 0 };
            if (clause[index] == ',' && depth == 0)
            {
                terms.Add(clause[from..index].Trim());
                from = index + 1;
            }
        }
        terms.Add(clause[from..].Trim());
        return [.. terms];
    }

    private static ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
}
