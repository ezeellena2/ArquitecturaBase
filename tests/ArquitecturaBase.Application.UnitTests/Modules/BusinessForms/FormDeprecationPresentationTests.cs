using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormDeprecationPresentationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static JsonSerializerOptions WireOptions { get; } = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };
    [Theory]
    [InlineData(null)]
    [InlineData("Pending")]
    [InlineData("Uncertain")]
    [InlineData("Deprecated")]
    public async Task Channel_deprecation_evidence_is_visible_without_record_read_and_keeps_absence_distinct(string? state)
    {
        var at = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc);
        var f = new FormDefinitionLifecycleReadFixture
        { DeprecationState = state is null ? null : Enum.Parse<StructuredFormDeprecationState>(state),
            DeprecatedAtUtc = state == "Deprecated" ? at : null };
        f.Permissions.Permissions[f.User.UserId!.Value] = [BusinessFormsPermissions.Read];
        var result = await f.Policy.FindAsync(f.Id, f.User.UserId.Value, Ct);
        Assert.True(result.IsSuccess);
        var lifecycle = JsonSerializer.SerializeToElement(result.Value.Lifecycle, WireOptions);
        Assert.True(lifecycle.TryGetProperty("deprecationState", out var status));
        Assert.Equal(state, status.GetString());
        Assert.True(lifecycle.TryGetProperty("deprecatedAtUtc", out var date));
        if (state == "Deprecated") Assert.Equal(at, date.GetDateTime());
        else Assert.Equal(JsonValueKind.Null, date.ValueKind);
        Assert.Equal(JsonValueKind.Null, lifecycle.GetProperty("responseCount").ValueKind);
        Assert.Equal(1, f.StateReads);
    }
}