using System.Net;
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
public sealed class FormFileRateLimitTests
{
    [Fact]
    public async Task File_traffic_has_its_own_bounded_quota_and_cannot_exhaust_form_reads()
    {
        foreach (var method in new[] { "Thumbnail", "Content" })
            Assert.Equal("forms-files-read", Assert.Single(typeof(FormFilesController).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)!
                .GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Cast<EnableRateLimitingAttribute>()).PolicyName);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        builder.Services.AddBusinessFormsApi();
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/files", context => context.Response.WriteAsync("ok")).RequireRateLimiting("forms-files-read");
        app.MapGet("/forms", context => context.Response.WriteAsync("ok")).RequireRateLimiting(BusinessFormsApiRegistration.ReadPolicy);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        for (var index = 0; index < 600; index++)
        {
            using var response = await client.GetAsync("/files", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await client.GetAsync("/files", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var forms = await client.GetAsync("/forms", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, forms.StatusCode);
    }
}
