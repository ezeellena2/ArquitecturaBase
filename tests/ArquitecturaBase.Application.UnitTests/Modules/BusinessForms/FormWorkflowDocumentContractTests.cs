using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormWorkflowDocumentContractTests
{
    [Fact]
    public void A_fixed_document_exposes_the_durable_run_that_generated_it()
    {
        var runId = Guid.NewGuid();
        var document = new FormWorkflowDocumentResponse(Guid.NewGuid(), "es", "Visita.pdf", 100,
            new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
        var runProperty = typeof(FormWorkflowDocumentResponse).GetProperty("RunId");
        Assert.NotNull(runProperty);
        runProperty.SetValue(document, runId);
        var json = JsonSerializer.SerializeToElement(document, JsonSerializerOptions.Web);
        Assert.Equal(runId, json.GetProperty("runId").GetGuid());
    }
}