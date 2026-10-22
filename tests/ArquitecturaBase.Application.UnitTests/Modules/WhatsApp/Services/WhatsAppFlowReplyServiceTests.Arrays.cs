using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    [Theory]
    [InlineData(0, "[\"choice\"]")]
    [InlineData(0, "[{\"id\":\"123\"}]")]
    [InlineData(5, "[\"choice\"]")]
    [InlineData(5, "[]")]
    public async Task Arrays_on_optional_scalar_or_single_choice_mappings_are_invalid_values_before_any_effect(int fieldIndex, string json)
    {
        var fixture = new Fixture();
        var field = fixture.Mapping[fieldIndex];
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            Mapping = fixture.Mapping.Select(mapped => mapped.FieldId == field.FieldId
                ? mapped with { Required = false } : mapped).ToArray(),
        };
        var payload = fixture.ValidAnswers("Stored text").ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        payload["flow_token"] = "secret-flow-token";
        using var array = JsonDocument.Parse(json);
        payload[field.Key] = array.RootElement;
        var message = fixture.AddReplyJson("wamid.array-scalar", JsonSerializer.Serialize(payload));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(FlowErrors.InvalidValue.Code, fixture.Session.CaptureErrorCode);
        Assert.Null(message.FlowRecordNumber);
        Assert.Null(fixture.Sink.Request);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(0, fixture.Media.DownloadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Arrays_without_a_sealed_mapping_are_invalid_values_before_any_effect(bool fieldPrefix)
    {
        var fixture = new Fixture();
        var payload = fixture.ValidAnswers("Stored text").ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        payload["flow_token"] = "secret-flow-token";
        var key = fieldPrefix ? "field_" + Guid.NewGuid().ToString("N") : "unmapped";
        payload[key] = new[] { "choice" };
        var message = fixture.AddReplyJson("wamid.array-unmapped", JsonSerializer.Serialize(payload));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(FlowErrors.InvalidValue.Code, fixture.Session.CaptureErrorCode);
        Assert.Null(message.FlowRecordNumber);
        Assert.Null(fixture.Sink.Request);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(0, fixture.Media.DownloadCount);
    }

    [Theory]
    [InlineData("photo", "[\"123\"]")]
    [InlineData("document", "[\"123\"]")]
    [InlineData("photo", "[{\"id\":\"123\",\"file_name\":\"photo.png\",\"mime_type\":\"image/png\",\"sha256\":\"hash\"},\"123\"]")]
    [InlineData("document", "[{\"id\":\"123\",\"file_name\":\"photo.png\",\"mime_type\":\"image/png\",\"sha256\":\"hash\"},\"123\"]")]
    public async Task File_mappings_reject_string_or_mixed_arrays_before_downloading_or_calling_the_sink(string type, string json)
    {
        var fixture = new Fixture(media: true);
        fixture.ProjectionProtector.Projection = fixture.Projection with
        {
            Mapping = [fixture.Mapping[0] with { Type = type }],
        };
        using var array = JsonDocument.Parse(json);
        var payload = new Dictionary<string, object>
        {
            ["flow_token"] = "secret-flow-token",
            [fixture.Mapping[0].Key] = array.RootElement,
        };
        var message = fixture.AddReplyJson("wamid.array-wrong-file-type", JsonSerializer.Serialize(payload));

        await fixture.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);

        Assert.Equal(WhatsAppFlowCaptureState.Rejected, message.FlowCaptureState);
        Assert.Equal(FlowErrors.InvalidValue.Code, message.FlowCaptureErrorCode);
        Assert.Equal(FlowErrors.InvalidValue.Code, fixture.Session.CaptureErrorCode);
        Assert.Null(message.FlowRecordNumber);
        Assert.Null(fixture.Sink.Request);
        Assert.Equal(0, fixture.Sink.CaptureCount);
        Assert.Equal(0, fixture.Media.DownloadCount);
    }
}
