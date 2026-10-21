using System.Reflection;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormRetiredRoutesTests
{
    [Theory]
    [InlineData(typeof(FormResponsesController), "navigation")]
    [InlineData(typeof(FormApprovalsController), "options")]
    public void Replaced_form_lists_no_longer_expose_their_get_route(Type controller, string route)
    {
        var getRoutes = controller.GetMethods().SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>());
        Assert.DoesNotContain(getRoutes, attribute => attribute.Template == route);
    }

    [Fact]
    public void Approval_tasks_count_and_anchored_record_navigation_keep_their_routes()
    {
        var approvalRoutes = typeof(FormApprovalsController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>()).Select(attribute => attribute.Template).ToArray();
        Assert.Contains(null, approvalRoutes);
        Assert.Contains("count", approvalRoutes);
        Assert.Contains("{id:guid}", approvalRoutes);
        var responseRoutes = typeof(FormResponsesController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>()).Select(attribute => attribute.Template);
        Assert.Contains("definitions/{definitionId:guid}/records/{recordId:guid}/navigation", responseRoutes);
    }
}