using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormEntryCaptureTests
{
    [Fact]
    public async Task A_capture_entered_in_the_app_is_marked_and_its_replay_compares_what_the_person_sent()
    {
        var catalog = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalog);
        var item = Guid.NewGuid();
        var fieldId = Guid.ParseExact(fixture.Key[6..], "N");
        var offer = new StructuredFormCatalogOffer(fieldId, fixture.Key, "Lote", catalog, 3, "Manual", "Lotes", [new(item, "Lote 7", "Soja")]);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = item.ToString("D") }) with { Catalogs = [offer] };

        var first = await fixture.Sink.CaptureEntryAsync(request, CancellationToken.None);
        // The server rebuilds the offer on every send: a renamed option must not turn the same send into a conflict.
        var renamed = request with { Catalogs = [offer with { Revision = 4, Options = [new(item, "Lote 7 · 48 ha", "Soja")] }] };
        var replay = await fixture.Sink.CaptureEntryAsync(renamed, CancellationToken.None);
        var existing = await fixture.Sink.FindExistingEntryAsync(renamed, CancellationToken.None);

        Assert.Equal("Completed", first.State);
        Assert.Equal(first, replay);
        Assert.Equal(first, existing);
        Assert.True(Assert.Single(fixture.Repository.Records).EnteredInApp);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_channel_capture_is_not_marked_and_cannot_be_recovered_as_an_entry()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "Lluvia" });

        var captured = await fixture.Sink.CaptureAsync(request, CancellationToken.None);

        Assert.Equal("Completed", captured.State);
        Assert.False(Assert.Single(fixture.Repository.Records).EnteredInApp);
        Assert.Null(await fixture.Sink.FindExistingEntryAsync(request, CancellationToken.None));
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, (await fixture.Sink.CaptureEntryAsync(request, CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Changed_answers_with_the_same_request_are_a_conflict_without_another_number()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", true);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "12,5" });

        await fixture.Sink.CaptureEntryAsync(request, CancellationToken.None);
        var changed = await fixture.Sink.CaptureEntryAsync(
            request with { Answers = new Dictionary<string, string> { [fixture.Key] = "13" } }, CancellationToken.None);

        Assert.Equal(FormRecordErrors.ReplayConflict.Code, changed.ErrorCode);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }
}
