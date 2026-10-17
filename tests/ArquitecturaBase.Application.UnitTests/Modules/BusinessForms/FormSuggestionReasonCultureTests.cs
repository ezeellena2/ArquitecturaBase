using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionReasonCultureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_Always_reason_keeps_the_conversation_language_when_the_complete_target_uses_its_original_language(bool forSending)
    {
        var fixture = new FormSuggestionRuntimeFixture();
        var ct = TestContext.Current.CancellationToken;
        await fixture.Assistant.Users.UpdateProfileAsync(fixture.Assistant.UserId, null, "en", "America/Argentina/Buenos_Aires", ct);
        fixture.Assistant.Catalog.Items.Add(BusinessFormCatalogItem.Create(fixture.Assistant.Catalog.Catalog.Id,
            "B", "Sede sin traducción", null, null, null, 1));

        var result = await fixture.Policy.PrepareAsync(fixture.Request with { Culture = "en", ForSending = forSending }, ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Prefill.Culture);
        var field = Assert.Single(result.Value.Prefill.Fields);
        Assert.Equal("Personas", field.Label);
        Assert.Equal("Ana", field.DisplayValue);
        Assert.Equal("Suggested after VISIT-000012.", result.Value.Prefill.Reason);
    }
}
