using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class PaginationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_requested_page_sorted_by_newest_first()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(25);

        using var response = await client.SendAsync(
            HttpMethod.Get, $"/test/widgets?search={prefix}&page=2&pageSize=10&sort=-createdAtUtc");
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Enumerable.Range(6, 10).Reverse().Select(i => Name(prefix, i)), Names(page));
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.Equal(10, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(25, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.GetProperty("totalPages").GetInt32());
        Assert.True(page.GetProperty("hasPrevious").GetBoolean());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Sorts_by_name_ascending()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(7);

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets?search={prefix}&pageSize=5&sort=name");
        var page = await response.ReadJsonAsync();

        Assert.Equal(Enumerable.Range(1, 5).Select(i => Name(prefix, i)), Names(page));
        Assert.False(page.GetProperty("hasPrevious").GetBoolean());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Default_sort_is_newest_first()
    {
        using var client = factory.CreateClient();
        var prefix = await SeedWidgetsAsync(3);

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets?search={prefix}");
        var page = await response.ReadJsonAsync();

        Assert.Equal([Name(prefix, 3), Name(prefix, 2), Name(prefix, 1)], Names(page));
    }

    [Fact]
    public async Task Sort_outside_the_whitelist_returns_a_validation_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/widgets?sort=secret", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No se puede ordenar por ese campo.", problem.GetProperty("errors").GetProperty("sort")[0].GetString());
    }

    [Fact]
    public async Task Page_size_above_the_limit_returns_a_validation_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/widgets?pageSize=101", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "La cantidad por página debe estar entre 1 y 100.",
            problem.GetProperty("errors").GetProperty("pageSize")[0].GetString());
    }

    [Fact]
    public async Task Huge_page_returns_a_validation_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/widgets?page=21474838&pageSize=100", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "La página debe estar entre 1 y 1000000.",
            problem.GetProperty("errors").GetProperty("page")[0].GetString());
    }

    // Cada test usa su propio prefijo: comparten la base con otros tests.
    private async Task<string> SeedWidgetsAsync(int count)
    {
        var prefix = "pag-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];

        for (var i = 1; i <= count; i++)
        {
            // Un minuto entre cada uno para que el orden por fecha sea determinista.
            factory.Clock.Advance(TimeSpan.FromMinutes(1));
            var name = Name(prefix, i);

            await factory.ExecuteDbContextAsync(dbContext =>
            {
                dbContext.Add(new Widget(name));
                return dbContext.SaveChangesAsync(Ct);
            });
        }

        return prefix;
    }

    private static string Name(string prefix, int index) =>
        prefix + "-" + index.ToString("D2", CultureInfo.InvariantCulture);

    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("name").GetString()!).ToArray();
}
