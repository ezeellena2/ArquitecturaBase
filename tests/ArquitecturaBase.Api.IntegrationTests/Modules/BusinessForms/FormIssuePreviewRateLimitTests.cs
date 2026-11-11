using System.Reflection;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?» and the itemized total of IV1-A ask for the issue preview while loading: it saves nothing and goes to no provider,
/// so it takes the read quota and never leaves «Enviar» without its write quota.
/// </summary>
public sealed class FormIssuePreviewRateLimitTests
{
    [Fact]
    public void The_issue_preview_takes_the_read_quota_and_not_the_one_of_sending()
    {
        var method = typeof(FormEntriesController).GetMethod("PreviewIssue")!;

        Assert.Equal(BusinessFormsApiRegistration.ReadPolicy, method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }
}
