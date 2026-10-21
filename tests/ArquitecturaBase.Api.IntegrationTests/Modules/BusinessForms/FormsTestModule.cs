using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
internal sealed class FormsTestModule : IApiTestModule
{
    public IReadOnlyCollection<string> Routes => [
        "DELETE /api/forms/definitions/{id:guid}",
        "GET /api/forms/definitions/{id:guid}/deactivation", "POST /api/forms/definitions/{id:guid}/deactivation",
        "GET /api/forms/missing-requests", "POST /api/forms/missing-requests/{id:guid}/discard",
        "GET /api/forms/definitions/{id:guid}/pdf-design", "POST /api/forms/definitions/{id:guid}/pdf-design", "POST /api/forms/definitions/{id:guid}/pdf-design/preview",
        "GET /api/forms/records/{recordid:guid}/documents/{documentid:guid}/content",
        "GET /api/forms/overview", "GET /api/forms/approvals", "GET /api/forms/approvals/count", "GET /api/forms/approvals/options",
        "GET /api/forms/approvals/{id:guid}", "POST /api/forms/approvals/{id:guid}/decisions",
        "GET /api/forms/records/{recordid:guid}/workflow", "POST /api/forms/records/{recordid:guid}/actions/{runid:guid}/retry", "POST /api/forms/records/{recordid:guid}/actions/{runid:guid}/consult", "POST /api/forms/records/{recordid:guid}/actions/{runid:guid}/review",
        "GET /api/forms/definitions/{id:guid}/suggestions", "POST /api/forms/definitions/{id:guid}/suggestions",
        "GET /api/forms/definitions/{id:guid}/suggestions/revisions", "GET /api/forms/definitions/{id:guid}/suggestions/revisions/{revision:long}", "GET /api/forms/definitions/{id:guid}/suggestions/options",
        "GET /api/forms/definitions/{id:guid}/rules", "POST /api/forms/definitions/{id:guid}/rules",
        "GET /api/forms/definitions/{id:guid}/rules/revisions", "GET /api/forms/definitions/{id:guid}/rules/revisions/{revision:long}", "GET /api/forms/definitions/{id:guid}/rules/options",
        "POST /api/forms/definitions/{id:guid}/records/export", "GET /api/forms/navigation", "GET /api/forms/definitions/{id:guid}/records", "POST /api/forms/definitions/{id:guid}/records/search",
        "POST /api/forms/definitions/{id:guid}/records/filter-counts/search", "GET /api/forms/records/{id:guid}","GET /api/forms/definitions", "GET /api/forms/definitions/facets",
        "GET /api/forms/definitions/{id:guid}", "POST /api/forms/definitions", "PUT /api/forms/definitions/{id:guid}/draft", "GET /api/forms/draft-changes/{requestid:guid}", "POST /api/forms/definitions/{id:guid}/publication-checks", "POST /api/forms/definitions/{id:guid}/publications",
        "GET /api/forms/definitions/{id:guid}/publications/{requestid:guid}", "GET /api/forms/definitions/{id:guid}/versions/{versionid:guid}/preview",
        "GET /api/forms/records/{recordid:guid}/files/{fileid:guid}/thumbnail", "GET /api/forms/records/{recordid:guid}/files/{fileid:guid}/content",
        "POST /api/forms/catalogs/{id:guid}/proposal/approve", "POST /api/forms/catalogs/{id:guid}/proposal/discard",
        "GET /api/forms/catalogs", "GET /api/forms/catalogs/{id:guid}", "GET /api/forms/catalogs/{id:guid}/items", "POST /api/forms/catalogs", "PUT /api/forms/catalogs/{id:guid}",
        "GET /api/forms/catalog-filter-options", "POST /api/forms/catalog-previews", "POST /api/forms/catalogs/filters", "PUT /api/forms/catalogs/{id:guid}/filters"];
    public void ConfigureSettings(IWebHostBuilder builder) { }
    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        services.Configure<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormPublicationWorkerOptions>(options => options.ProcessInBackground = false);
        services.Configure<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormActionWorkerOptions>(options => options.ProcessInBackground = false);
        services.AddSingleton<IStructuredFormPublicationDriver, StructuredFormPublicationDriver>();
    }
}
