using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Models;

public sealed class WhatsAppFormPreparationTests
{
    [Fact]
    public void Operator_preparation_contains_counts_and_issues_without_recipient_options()
    {
        var fieldId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var response = new WhatsAppFormPreparationResponse("es", "recipient-revision", new([
            new(fieldId, "field_key", "Persona", catalogId, 2, "Filters", "People", [
                new(optionId, "Private person", "Private role")])], []));
        var json = JsonSerializer.Serialize(response, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        var catalog = document.RootElement.GetProperty("preparation").GetProperty("catalogs")[0];
        Assert.Equal(1, catalog.GetProperty("count").GetInt32());
        Assert.Equal(3, catalog.EnumerateObject().Count());
        Assert.DoesNotContain("Private", json);
        Assert.DoesNotContain(catalogId.ToString(), json);
        Assert.DoesNotContain(optionId.ToString(), json);
    }
}
