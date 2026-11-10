using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Serialization;
using ArquitecturaBase.Api.Authorization;
using ArquitecturaBase.Api.Modules.BusinessForms;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogTransferContractTests
{
    [Theory]
    [InlineData("Preview", "import-previews")]
    [InlineData("Apply", "imports")]
    public void Multipart_endpoints_use_http_contracts_and_the_export_limiter_with_a_bounded_envelope(string action, string path)
    {
        var controller = typeof(FormCatalogTransfersController);
        Assert.Equal(BusinessFormsPermissions.CatalogsRead, controller.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
        Assert.Equal(BusinessFormsApiRegistration.ExportPolicy, controller.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName);
        Assert.Equal("api/forms/catalogs/{id:guid}", controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.True(controller.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        var method = controller.GetMethod(action)!;
        Assert.Equal(path, method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(BusinessFormsPermissions.CatalogsManage, method.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
        Assert.Contains("multipart/form-data", method.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
        Assert.Equal(FormCatalogImportLimits.MaximumFileBytes + 64L * 1024, ((IRequestSizeLimitMetadata)method.GetCustomAttribute<RequestSizeLimitAttribute>()!).MaxRequestBodySize);
        Assert.Equal(FormCatalogImportLimits.MaximumFileBytes + 64L * 1024, method.GetCustomAttribute<RequestFormLimitsAttribute>()!.MultipartBodyLengthLimit);
        var input = Assert.Single(method.GetParameters(), parameter => parameter.IsDefined(typeof(FromFormAttribute)));
        Assert.Equal(typeof(FormCatalogImportHttpRequest), input.ParameterType);
        Assert.Equal(typeof(IFormFile), input.ParameterType.GetProperty("File")!.PropertyType);
        Assert.True(input.ParameterType.GetConstructors().Single().GetParameters().Single(parameter => parameter.Name == "File").IsDefined(typeof(RequiredAttribute)));
    }

    // Stage 2 of the assistant authoring: the same review and import from the copy of a proposal's spreadsheet, with closed JSON bodies.
    [Theory]
    [InlineData("PreviewFromAssistant", "assistant-import-previews", typeof(FormCatalogAssistantImportPreviewHttpRequest),
        "ExpectedRevision,ProposalId,RequestId")]
    [InlineData("ApplyFromAssistant", "assistant-imports", typeof(FormCatalogAssistantImportHttpRequest),
        "ConfirmDeactivations,ConfirmEmpty,ConfirmErrors,ExpectedRevision,ProposalId,RequestId,SheetSha256")]
    public void Assistant_import_endpoints_take_closed_json_contracts_and_require_manage(string action, string path, Type contract, string properties)
    {
        var method = typeof(FormCatalogTransfersController).GetMethod(action)!;
        Assert.Equal(path, method.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(BusinessFormsPermissions.CatalogsManage, method.GetCustomAttribute<HasPermissionAttribute>()!.Permission);
        Assert.Null(method.GetCustomAttribute<ConsumesAttribute>());
        Assert.Equal(contract, Assert.Single(method.GetParameters(), parameter => parameter.IsDefined(typeof(FromBodyAttribute))).ParameterType);
        Assert.Equal(JsonUnmappedMemberHandling.Disallow, contract.GetCustomAttribute<JsonUnmappedMemberHandlingAttribute>()!.UnmappedMemberHandling);
        Assert.Equal(properties.Split(','), contract.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_pending_import_of_a_catalog_requires_only_read()
    {
        var method = typeof(FormCatalogTransfersController).GetMethod("GetAssistantImport")!;
        Assert.Equal("assistant-import", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(method.GetCustomAttribute<HasPermissionAttribute>());
        var response = Assert.Single(method.GetCustomAttributes<ProducesResponseTypeAttribute>(), attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(FormCatalogAssistantImportResponse), response.Type);
    }

    [Fact]
    public void Workbook_export_requires_only_read_and_declares_the_binary_response()
    {
        var method = typeof(FormCatalogTransfersController).GetMethod("Export")!;
        Assert.Equal("export", method.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Null(method.GetCustomAttribute<HasPermissionAttribute>());
        Assert.Equal(typeof(FormCatalogExportQuery), Assert.Single(method.GetParameters(), parameter => parameter.IsDefined(typeof(FromQueryAttribute))).ParameterType);
        var response = Assert.Single(method.GetCustomAttributes<ProducesResponseTypeAttribute>(), attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(FileContentResult), response.Type);
    }
}
