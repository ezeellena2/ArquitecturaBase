using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Task 8 with PostgreSQL: the inbox's port tells a channel whether some form published in a channel reads documents (the scenario
// publishes «Pagos» with its reading on), asked in SQL, and whether a person can load into one: an active account that completes forms, as
// the documents capabilities are offered. Without any form that reads, in a base of its own: the shared one keeps other forms.
[Collection(ApiTestGroup.Name)]
public sealed class FormReceivedDocumentLoadingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_channel_learns_that_a_form_reads_documents_and_who_can_load_one()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct);

        var sender = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));
        var reader = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Reader, Ct));
        var unknown = await scenario.PortAsync(port => port.FindLoadingAsync(Guid.Empty, Ct));
        await scenario.SetActiveAsync(scenario.Sender, false, Ct);
        var inactive = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));

        Assert.Equal(new StructuredDocumentLoading(true, true), sender);
        Assert.Equal(new StructuredDocumentLoading(true, false), reader);
        Assert.Equal(new StructuredDocumentLoading(true, false), unknown);
        Assert.Equal(new StructuredDocumentLoading(true, false), inactive);
    }

    [Fact]
    public async Task Only_a_form_published_with_its_reading_on_reads_documents()
    {
        await using var scenario = await FormReceivedDocumentScenario.CreateAsync(factory, Ct, isolated: true);
        var published = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));

        await scenario.DeleteFormAsync(scenario.Form.DefinitionId, Ct);
        await scenario.DeleteFormAsync(scenario.Other.DefinitionId, Ct);
        var deleted = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));
        await scenario.AddFormAsync("Lectura apagada", "Reading off", Ct, reading: false);
        await scenario.AddFormAsync("Sin lectura", "Without reading", Ct, reading: null);
        var off = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));
        await scenario.AddFormAsync("Lectura prendida", "Reading on", Ct);
        var on = await scenario.PortAsync(port => port.FindLoadingAsync(scenario.Sender, Ct));

        Assert.Equal(new StructuredDocumentLoading(true, true), published);
        Assert.Equal((StructuredDocumentLoading.None, StructuredDocumentLoading.None), (deleted, off));
        Assert.Equal(new StructuredDocumentLoading(true, true), on);
    }
}
