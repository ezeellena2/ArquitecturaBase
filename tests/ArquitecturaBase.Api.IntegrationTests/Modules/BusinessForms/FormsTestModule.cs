using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
internal sealed class FormsTestModule : IApiTestModule
{
    public IReadOnlyCollection<string> Routes => [
        "POST /api/forms/definitions/{id:guid}/records/export", "GET /api/forms/navigation", "GET /api/forms/definitions/{id:guid}/records", "POST /api/forms/definitions/{id:guid}/records/search",
        "POST /api/forms/definitions/{id:guid}/records/filter-counts/search", "GET /api/forms/records/{id:guid}","GET /api/forms/definitions", "GET /api/forms/definitions/facets",
        "GET /api/forms/definitions/{id:guid}", "POST /api/forms/definitions", "PUT /api/forms/definitions/{id:guid}/draft", "GET /api/forms/draft-changes/{requestid:guid}", "POST /api/forms/definitions/{id:guid}/publication-checks", "POST /api/forms/definitions/{id:guid}/publications",
        "GET /api/forms/definitions/{id:guid}/publications/{requestid:guid}", "GET /api/forms/definitions/{id:guid}/versions/{versionid:guid}/preview",
        "GET /api/forms/records/{recordid:guid}/files/{fileid:guid}/thumbnail", "GET /api/forms/records/{recordid:guid}/files/{fileid:guid}/content",
        "GET /api/forms/catalogs", "GET /api/forms/catalogs/{id:guid}", "GET /api/forms/catalogs/{id:guid}/items", "POST /api/forms/catalogs", "PUT /api/forms/catalogs/{id:guid}",
        "GET /api/forms/catalog-filter-options", "POST /api/forms/catalog-previews", "POST /api/forms/catalogs/filters", "PUT /api/forms/catalogs/{id:guid}/filters"];
    public void ConfigureSettings(IWebHostBuilder builder) { }
    public void ConfigureServices(ApiFactory factory, IServiceCollection services)
    {
        services.Configure<ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers.FormPublicationWorkerOptions>(options => options.ProcessInBackground = false);
        services.AddSingleton<IStructuredFormPublicationDriver, StructuredFormPublicationDriver>();
    }
}
