using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Reflection;
using System.Security.Claims;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using ArquitecturaBase.Api.RequestContext;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormExportRateLimitTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("knownFailure")]
    [InlineData("exception")]
    [InlineData("canceled")]
    public async Task One_export_per_user_releases_its_slot_after_every_request_outcome(string outcome)
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipelineExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddProblemDetails();
        builder.Services.AddRateLimitingPolicies();
        builder.Services.AddBusinessFormsApi();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", context.Request.Headers["X-Test-UserId"].ToString())], "Test"));
            try
            {
                await next(context);
            }
            finally
            {
                // This runs after the downstream rate limiter has disposed the acquired lease.
                if (context.Request.Query.ContainsKey("hold")) pipelineExited.TrySetResult();
            }
        });
        app.UseRouting();
        app.UseRateLimiter();
        var exportPolicy = typeof(FormExportsController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName!;
        app.MapPost("/export", async context =>
        {
            if (context.Request.Query.ContainsKey("hold"))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                if (outcome == "exception") throw new InvalidOperationException("Synthetic export failure.");
                context.Response.StatusCode = outcome == "knownFailure" ? StatusCodes.Status400BadRequest : StatusCodes.Status200OK;
            }
            await context.Response.WriteAsync("export", context.RequestAborted);
        }).RequireRateLimiting(exportPolicy);
        app.MapPost("/read", context => context.Response.WriteAsync("read", context.RequestAborted))
            .RequireRateLimiting(BusinessFormsApiRegistration.ReadPolicy);
        await app.StartAsync(ct);
        using var client = app.GetTestClient();
        var actor = Guid.NewGuid();
        using var firstRequest = Request("/export?hold=true", actor);
        var first = client.SendAsync(firstRequest, firstCancellation.Token);
        await entered.Task.WaitAsync(ct);
        try
        {
            using var competingRequest = Request("/export", actor);
            using var competing = await client.SendAsync(competingRequest, ct);
            Assert.Equal(HttpStatusCode.TooManyRequests, competing.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(1), competing.Headers.RetryAfter?.Delta);
            var problem=await competing.Content.ReadFromJsonAsync<JsonElement>(ct);
            Assert.Equal(1,problem.GetProperty("retryAfter").GetInt32());
            using var readRequest = Request("/read", actor);
            using var read = await client.SendAsync(readRequest, ct);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var otherRequest = Request("/export", Guid.NewGuid());
            using var other = await client.SendAsync(otherRequest, ct);
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }
        finally
        {
            if (outcome == "canceled") firstCancellation.Cancel();
            else release.TrySetResult();
            if (outcome == "exception")
                await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var unexpected = await first; });
            else if (outcome == "canceled")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { using var unexpected = await first; });
            else
            {
                using var completed = await first;
                Assert.Equal(outcome == "knownFailure" ? HttpStatusCode.BadRequest : HttpStatusCode.OK, completed.StatusCode);
            }
            await pipelineExited.Task.WaitAsync(ct);
        }
        using var nextRequest = Request("/export", actor);
        using var next = await client.SendAsync(nextRequest, ct);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    private static HttpRequestMessage Request(string route, Guid actor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add("X-Test-UserId", actor.ToString());
        return request;
    }
}
