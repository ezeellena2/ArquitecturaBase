using System.Net;
using System.Reflection;
using System.Security.Claims;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using ArquitecturaBase.Api.RequestContext;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormExportRateLimitTests
{
    [Fact]
    public async Task One_export_per_user_runs_without_blocking_reads_or_other_users_and_releases_its_slot()
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        builder.Services.AddBusinessFormsApi();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", context.Request.Headers["X-Test-UserId"].ToString())], "Test"));
            await next(context);
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
            }
            await context.Response.WriteAsync("export", context.RequestAborted);
        }).RequireRateLimiting(exportPolicy);
        app.MapPost("/read", context => context.Response.WriteAsync("read", context.RequestAborted))
            .RequireRateLimiting(BusinessFormsApiRegistration.ReadPolicy);
        await app.StartAsync(ct);
        using var client = app.GetTestClient();
        var actor = Guid.NewGuid();
        using var firstRequest = Request("/export?hold=true", actor);
        var first = client.SendAsync(firstRequest, ct);
        await entered.Task.WaitAsync(ct);
        try
        {
            using var competingRequest = Request("/export", actor);
            using var competing = await client.SendAsync(competingRequest, ct);
            Assert.Equal(HttpStatusCode.TooManyRequests, competing.StatusCode);
            using var readRequest = Request("/read", actor);
            using var read = await client.SendAsync(readRequest, ct);
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var otherRequest = Request("/export", Guid.NewGuid());
            using var other = await client.SendAsync(otherRequest, ct);
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }
        finally
        {
            release.TrySetResult();
            using var completed = await first;
            Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
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
