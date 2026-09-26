using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

[Collection(ApiTestGroup.Name)]
public sealed class MvcResultContractTests(ApiFactory factory)
{
    [Fact]
    public async Task Controller_results_preserve_200_204_and_json_converters()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();

        using var ok = await client.GetAsync("/api/_mvc-probe/success", TestContext.Current.CancellationToken);
        using var jsonString = await client.GetAsync("/api/_mvc-probe/string", TestContext.Current.CancellationToken);
        using var jsonNull = await client.GetAsync("/api/_mvc-probe/null", TestContext.Current.CancellationToken);
        using var noContent = await client.PostAsync("/api/_mvc-probe/no-content", null, TestContext.Current.CancellationToken);
        var body = await ok.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("application/json", ok.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Open", body.GetProperty("registrationMode").GetString());
        Assert.Equal("2026-09-24T12:00:00Z", body.GetProperty("atUtc").GetString());
        Assert.Equal(HttpStatusCode.OK, jsonString.StatusCode);
        Assert.Equal("application/json", jsonString.Content.Headers.ContentType?.MediaType);
        Assert.Equal("\"ready\"", await jsonString.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.OK, jsonNull.StatusCode);
        Assert.Null(jsonNull.Content.Headers.ContentType);
        Assert.Empty(await jsonNull.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NoContent, noContent.StatusCode);
        Assert.Empty(await noContent.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Accepted_results_carry_the_value_when_there_is_one_and_never_a_location()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();

        using var withValue = await client.PostAsync("/api/_mvc-probe/accepted", null, TestContext.Current.CancellationToken);
        using var withoutValue = await client.PostAsync("/api/_mvc-probe/accepted-empty", null, TestContext.Current.CancellationToken);
        using var withNull = await client.PostAsync("/api/_mvc-probe/accepted-null", null, TestContext.Current.CancellationToken);
        using var failed = await client.PostAsync("/api/_mvc-probe/accepted-failure", null, TestContext.Current.CancellationToken);
        var body = await withValue.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, withValue.StatusCode);
        Assert.Equal("application/json", withValue.Content.Headers.ContentType?.MediaType);
        Assert.Equal(30, body.GetProperty("resendAfterSeconds").GetInt32());
        Assert.Null(withValue.Headers.Location);
        foreach (var empty in new[] { withoutValue, withNull })
        {
            Assert.Equal(HttpStatusCode.Accepted, empty.StatusCode);
            Assert.Empty(await empty.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
            Assert.Null(empty.Content.Headers.ContentType);
            Assert.Null(empty.Headers.Location);
        }
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Probe.Conflict", (await failed.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Created_results_point_the_location_to_the_action_and_carry_the_value()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();

        using var created = await client.PostAsync("/api/_mvc-probe/created", null, TestContext.Current.CancellationToken);
        using var failed = await client.PostAsync("/api/_mvc-probe/created-failure", null, TestContext.Current.CancellationToken);
        var id = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("application/json", created.Content.Headers.ContentType?.MediaType);
        Assert.Equal(MvcResultProbeController.CreatedId, id);
        Assert.Equal($"/api/_mvc-probe/items/{id}", created.Headers.Location?.AbsolutePath);
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal("application/problem+json", failed.Content.Headers.ContentType?.MediaType);
        Assert.Null(failed.Headers.Location);
        Assert.Equal("Probe.Conflict", (await failed.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Controller_errors_preserve_problem_status_code_trace_and_metadata()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();
        var cases = new (string Kind, HttpStatusCode Status, string Code)[]
        {
            ("validation", HttpStatusCode.BadRequest, ValidationError.ErrorCode),
            ("unauthorized", HttpStatusCode.Unauthorized, "Probe.Unauthorized"),
            ("forbidden", HttpStatusCode.Forbidden, "Probe.Forbidden"),
            ("not-found", HttpStatusCode.NotFound, "Probe.NotFound"),
            ("conflict", HttpStatusCode.Conflict, "Probe.Conflict"),
            ("rate-limit", HttpStatusCode.TooManyRequests, "Probe.RateLimit"),
            ("failure", HttpStatusCode.InternalServerError, "Probe.Failure"),
        };

        foreach (var (kind, status, code) in cases)
        {
            using var response = await client.GetAsync($"/api/_mvc-probe/error/{kind}", TestContext.Current.CancellationToken);
            var problem = await response.ReadJsonAsync();

            Assert.Equal(status, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
            Assert.Equal(code, problem.GetProperty("code").GetString());
            if (kind == "rate-limit")
            {
                Assert.False(problem.TryGetProperty("type", out _));
            }
            else
            {
                Assert.True(problem.TryGetProperty("type", out var type), $"Missing type for {kind}.");
                Assert.False(string.IsNullOrWhiteSpace(type.GetString()));
            }
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));

            if (kind == "validation")
            {
                Assert.Equal("required", problem.GetProperty("errors").GetProperty("field")[0].GetString());
            }
            else if (kind == "rate-limit")
            {
                Assert.Equal(30, problem.GetProperty("retryAfter").GetInt32());
            }
        }
    }

    [Fact]
    public async Task Controller_problems_use_the_existing_error_translations()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();

        using var spanish = await client.SendAsync(HttpMethod.Get, "/api/_mvc-probe/error/settings-not-found", language: "es");
        using var english = await client.SendAsync(HttpMethod.Get, "/api/_mvc-probe/error/settings-not-found", language: "en");
        using var framework = await client.SendAsync(HttpMethod.Get, "/api/_mvc-probe/missing", language: "es");
        var spanishProblem = await spanish.ReadJsonAsync();
        var englishProblem = await english.ReadJsonAsync();
        var frameworkProblem = await framework.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, spanish.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, english.StatusCode);
        Assert.Equal("Settings.System.NotFound", spanishProblem.GetProperty("code").GetString());
        Assert.Equal("Settings.System.NotFound", englishProblem.GetProperty("code").GetString());
        Assert.Equal("No encontramos la configuración del sistema.", spanishProblem.GetProperty("detail").GetString());
        Assert.Equal("We couldn't find the system settings.", englishProblem.GetProperty("detail").GetString());
        Assert.Equal(frameworkProblem.GetProperty("type").GetString(), spanishProblem.GetProperty("type").GetString());
        Assert.Contains("es", spanish.Content.Headers.ContentLanguage);
        Assert.Contains("en", english.Content.Headers.ContentLanguage);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> ProbeApi()
    {
        _ = factory.Services.GetRequiredService<ApplicationPartManager>();
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(MvcResultProbeController).Assembly)));
    }
}

[ApiController]
[Route("api/_mvc-probe")]
public sealed class MvcResultProbeController : ControllerBase
{
    public static readonly Guid CreatedId = new("01920000-0000-7000-8000-000000000001");

    [HttpGet("success")]
    public IActionResult Success() => Result.Success(new
    {
        RegistrationMode = RegistrationMode.Open,
        AtUtc = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
    }).ToActionResult(this);

    [HttpGet("string")]
    public IActionResult StringResult() => Result.Success("ready").ToActionResult(this);

    [HttpGet("null")]
    public IActionResult NullResult() => Result.Success<string?>(null).ToActionResult(this);

    [HttpPost("no-content")]
    public IActionResult NoContentResult() => Result.Success().ToActionResult(this);

    [HttpPost("accepted")]
    public IActionResult AcceptedWithValue() => Result.Success(new { ResendAfterSeconds = 30 }).ToAcceptedResult(this);

    [HttpPost("accepted-empty")]
    public IActionResult AcceptedWithoutValue() => Result.Success().ToAcceptedResult(this);

    [HttpPost("accepted-null")]
    public IActionResult AcceptedWithNull() => Result.Success<string?>(null).ToAcceptedResult(this);

    [HttpPost("accepted-failure")]
    public IActionResult AcceptedFailure() =>
        Result.Failure<string>(Error.Conflict("Probe.Conflict", "Conflict")).ToAcceptedResult(this);

    [HttpPost("created")]
    public IActionResult CreatedItem() => Result.Success(CreatedId).ToCreatedResult(this, nameof(Item), id => new { id });

    [HttpPost("created-failure")]
    public IActionResult CreatedFailure() =>
        Result.Failure<Guid>(Error.Conflict("Probe.Conflict", "Conflict")).ToCreatedResult(this, nameof(Item), id => new { id });

    [HttpGet("items/{id:guid}")]
    public IActionResult Item([FromRoute] Guid id) => Result.Success(id).ToActionResult(this);

    [HttpGet("error/{kind}")]
    public IActionResult ErrorResult(string kind)
    {
        Error error = kind switch
        {
            "validation" => new ValidationError(new Dictionary<string, string[]> { ["field"] = ["required"] }),
            "unauthorized" => Error.Unauthorized("Probe.Unauthorized", "Unauthorized"),
            "forbidden" => Error.Forbidden("Probe.Forbidden", "Forbidden"),
            "not-found" => Error.NotFound("Probe.NotFound", "Not found"),
            "conflict" => Error.Conflict("Probe.Conflict", "Conflict"),
            "settings-not-found" => SettingsErrors.NotFound,
            "rate-limit" => Error.TooManyRequests("Probe.RateLimit", "Rate limit", new Dictionary<string, object?>
            {
                ["retryAfter"] = 30,
            }),
            _ => Error.Failure("Probe.Failure", "Failure"),
        };

        return Result.Failure(error).ToActionResult(this);
    }
}
