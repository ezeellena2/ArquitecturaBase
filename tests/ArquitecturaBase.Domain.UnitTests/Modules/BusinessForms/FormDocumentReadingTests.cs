using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

// DOC1: the reading of documents is a setting of the form, like «Dónde se completa»: the draft keeps it under its revision and a
// version seals it once, at acceptance.
public sealed class FormDocumentReadingTests
{
    private const string Reading = """{"enabled":true,"recognition":"Comprobante de una transferencia.","lookups":[]}""";

    [Fact]
    public void The_draft_keeps_its_reading_under_its_revision()
    {
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Pagos", "{}", "PAG", "es", "{}", "{}", 0, true).IsSuccess);
        Assert.Null(definition.DocumentReadingJson);

        Assert.True(definition.SetDraft(1, "Pagos", "{}", "PAG", "es", "{}", "{}", 0, false, documentReadingJson: Reading).IsSuccess);
        Assert.Equal(Reading, definition.DocumentReadingJson);
        Assert.Equal(2, definition.Revision);

        Assert.True(definition.SetDraft(2, "Pagos", "{}", "PAG", "es", "{}", "{}", 0, false).IsSuccess);
        Assert.Null(definition.DocumentReadingJson);
    }

    [Fact]
    public void A_version_seals_the_reading_it_was_published_with_once()
    {
        var version = BusinessFormVersion.Create(Guid.CreateVersion7(), 1, "hash");
        Assert.Null(version.DocumentReadingJson);

        version.SealDocumentReading(Reading);

        Assert.Equal(Reading, version.DocumentReadingJson);
        Assert.Throws<InvalidOperationException>(() => version.SealDocumentReading(Reading));
        Assert.Throws<ArgumentException>(() => BusinessFormVersion.Create(Guid.CreateVersion7(), 2, "hash").SealDocumentReading(" "));
    }
}
