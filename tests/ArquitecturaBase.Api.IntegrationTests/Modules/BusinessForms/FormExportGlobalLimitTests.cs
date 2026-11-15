using System.Net;
using System.Reflection;
using System.Security.Claims;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Api.RequestContext;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormExportGlobalLimitTests
{
    [Fact]
    public async Task Exports_and_imports_of_different_people_are_capped_per_copy_and_free_their_slot()
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            await next(context);
        });
        app.UseRouting();
        app.UseRateLimiter();
        var exportPolicy = typeof(FormExportsController).GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName!;
        app.MapPost("/export", async context =>
        {
            if (context.Request.Query.ContainsKey("hold"))
            {
                Interlocked.Increment(ref entered);
                await release.Task.WaitAsync(context.RequestAborted);
            }
            await context.Response.WriteAsync("export", context.RequestAborted);
        }).RequireRateLimiting(exportPolicy);
        app.MapPost("/read", context => context.Response.WriteAsync("read", context.RequestAborted))
            .RequireRateLimiting(BusinessFormsApiRegistration.ReadPolicy);
        await app.StartAsync(ct);
        using var client = app.GetTestClient();

        var holders = Enumerable.Range(0, BusinessFormsApiRegistration.GlobalExportLimit)
            .Select(_ => client.SendAsync(Request("/export?hold=true", Guid.NewGuid()), ct)).ToArray();
        while (Volatile.Read(ref entered) < holders.Length) await Task.Delay(10, ct);

        using var rejected = await client.SendAsync(Request("/export", Guid.NewGuid()), ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter?.Delta);
        using var read = await client.SendAsync(Request("/read", Guid.NewGuid()), ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        release.SetResult();
        foreach (var holder in holders)
        {
            using var done = await holder;
            Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        }
        using var next = await client.SendAsync(Request("/export", Guid.NewGuid()), ct);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    private static HttpRequestMessage Request(string route, Guid actor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route);
        request.Headers.Add("X-Test-UserId", actor.ToString());
        return request;
    }
}
