using System.Net;
using System.Reflection;
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
// F17: the official preview and the status of a publication make several calls to Meta per request.
public sealed class FormMetaRateLimitTests
{
    [Fact]
    public async Task The_official_preview_and_the_publication_status_have_their_own_quota_of_ten_a_minute_and_cannot_exhaust_form_reads()
    {
        Assert.Equal("forms-meta-read", PolicyOf(typeof(FormPreviewsController), nameof(FormPreviewsController.Get)));
        Assert.Equal("forms-meta-read", PolicyOf(typeof(FormPublicationsController), nameof(FormPublicationsController.Get)));
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        builder.Services.AddBusinessFormsApi();
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.MapGet("/meta", context => context.Response.WriteAsync("ok")).RequireRateLimiting(BusinessFormsApiRegistration.MetaReadPolicy);
        app.MapGet("/forms", context => context.Response.WriteAsync("ok")).RequireRateLimiting(BusinessFormsApiRegistration.ReadPolicy);
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();
        for (var index = 0; index < 10; index++)
        {
            using var response = await client.GetAsync("/meta", TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var limited = await client.GetAsync("/meta", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var forms = await client.GetAsync("/forms", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, forms.StatusCode);
    }

    private static string? PolicyOf(Type controller, string method) =>
        Assert.Single(controller.GetMethod(method)!.GetCustomAttributes<EnableRateLimitingAttribute>()).PolicyName;
}
