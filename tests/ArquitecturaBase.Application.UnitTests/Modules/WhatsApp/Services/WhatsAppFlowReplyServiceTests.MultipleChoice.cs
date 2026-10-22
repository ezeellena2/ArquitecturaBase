using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multiple_choice_arrays_use_the_sealed_own_or_catalog_offer_and_preserve_order(bool catalog)
    {
        var fixture = new Fixture(catalog: catalog);
        var field = fixture.Mapping[5];
        var second = Guid.NewGuid();
        var choices = catalog ? new[] { fixture.OptionId } : [second, fixture.OptionId];
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            ProfileRevision = "flow-static-7.3-r9",
            Mapping = [field with { Type = "multiChoice", Selection = new(1, 2), Choices = catalog ? [] : choices }],
        };
        var message = fixture.AddReplyJson("wamid.multiple-choice", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["flow_token"] = "secret-flow-token", [field.Key] = choices,
        }));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        using var canonical = JsonDocument.Parse(request.Answers[field.Key]);
        Assert.Equal(choices, canonical.RootElement.EnumerateArray().Select(item => item.GetGuid()));
        Assert.Equal(catalog ? fixture.SessionProtector.Payload.Catalogs : null,
            catalog ? request.Catalogs : null);
        Assert.Empty(request.Files!);
        Assert.Equal(0, fixture.Media.DownloadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Optional_multiple_choice_keeps_an_explicit_empty_array_distinct_from_omission(bool present)
    {
        var fixture = new Fixture();
        var field = fixture.Mapping[5];
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            ProfileRevision = "flow-static-7.3-r9",
            Mapping = [field with { Type = "multiChoice", Required = false, Selection = new(0, 2) }],
        };
        var payload = new Dictionary<string, object> { ["flow_token"] = "secret-flow-token" };
        if (present) payload[field.Key] = Array.Empty<string>();
        var message = fixture.AddReplyJson("wamid.multiple-choice-empty", JsonSerializer.Serialize(payload));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        Assert.Equal(present, request.Answers.ContainsKey(field.Key));
        if (present) Assert.Equal("[]", request.Answers[field.Key]);
        Assert.Equal(0, fixture.Media.DownloadCount);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[null]")]
    [InlineData("[{}]")]
    [InlineData("[\"unoffered\"]")]
    [InlineData("\"not-an-array\"")]
    public async Task Invalid_multiple_choices_are_rejected_before_a_valid_file_is_downloaded(string raw)
    {
        var fixture = new Fixture(media: true);
        var photo = fixture.Mapping[0];
        var id = Guid.NewGuid();
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            ProfileRevision = "flow-static-7.3-r9",
            Mapping = [photo, new(id, "field_" + id.ToString("N"), "multiChoice", true, null,
                [fixture.OptionId], Selection: new(1, 2))],
        };
        var multiple = fixture.ProjectionProtector.Projection.Mapping[1];
        using var value = JsonDocument.Parse(raw);
        var message = fixture.AddReplyJson("wamid.multiple-choice-invalid-with-file", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["flow_token"] = "secret-flow-token", [multiple.Key] = value.RootElement,
            [photo.Key] = new[] { new { id = "123", file_name = "photo.png", mime_type = "image/png", sha256 = new string('a', 64) } },
        }));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Media.DownloadCount);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_string_containing_a_valid_array_is_not_a_native_multiple_choice_response()
    {
        var fixture = new Fixture();
        var field = fixture.Mapping[5];
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            ProfileRevision = "flow-static-7.3-r9",
            Mapping = [field with { Type = "multiChoice", Selection = new(1, 2) }],
        };
        var message = fixture.AddReplyJson("wamid.multiple-choice-as-string", JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["flow_token"] = "secret-flow-token", [field.Key] = JsonSerializer.Serialize(new[] { fixture.OptionId }),
        }));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(0, fixture.Media.DownloadCount);
        Assert.Equal(0, fixture.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_multiple_choice_and_a_file_share_the_same_capture_without_counting_choices_as_media()
    {
        var fixture = new Fixture(media: true);
        var photo = fixture.Mapping[0];
        var id = Guid.NewGuid();
        var choices = Enumerable.Range(1, 20).Select(index => Guid.Parse($"019a0000-0000-7000-8000-{index:000000000000}")).ToArray();
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            ProfileRevision = "flow-static-7.3-r9",
            Mapping = [photo, new(id, "field_" + id.ToString("N"), "multiChoice", true, null,
                choices, Selection: new(1, 20))],
        };
        var multiple = fixture.ProjectionProtector.Projection.Mapping[1];
        var message = fixture.AddReplyJson("wamid.multiple-choice-with-file", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["flow_token"] = "secret-flow-token", [multiple.Key] = choices,
            [photo.Key] = new[] { new { id = "123", file_name = "photo.png", mime_type = "image/png", sha256 = new string('a', 64) } },
        }));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Completed, message.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(fixture.Sink.Request);
        using var canonical = JsonDocument.Parse(request.Answers[multiple.Key]);
        Assert.Equal(20, canonical.RootElement.GetArrayLength());
        Assert.Equal(choices, canonical.RootElement.EnumerateArray().Select(option => option.GetGuid()));
        Assert.Single(request.Files!);
        Assert.Equal(1, fixture.Media.DownloadCount);
        Assert.Equal(1, fixture.Sink.CaptureCount);
    }
}
