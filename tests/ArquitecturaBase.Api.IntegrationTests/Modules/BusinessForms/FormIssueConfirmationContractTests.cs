using System.Reflection;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?»: the confirmation takes a closed body of two booleans, so it accepts at most 8 KiB, like the decisions of the
/// approvals, and takes the write quota.
/// </summary>
public sealed class FormIssueConfirmationContractTests
{
    [Fact]
    public void The_confirmation_takes_a_small_body_and_the_write_quota()
    {
        var method = typeof(FormWorkflowsController).GetMethod("Confirm")!;

        Assert.Equal(8192, ((IRequestSizeLimitMetadata)method.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
        Assert.Equal(BusinessFormsApiRegistration.WritePolicy, method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
    }
}
