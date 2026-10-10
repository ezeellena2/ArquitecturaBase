using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormVersionPresentationTests
{
    [Fact]
    public void Published_presentation_is_sealed_once_without_changing_schema_hash_or_version_identity()
    {
        var definition = Guid.NewGuid();
        var version = BusinessFormVersion.Create(definition, 2, "synthetic-content-hash");
        var id = version.Id;
        version.SealSchema("{\"format\":1,\"questions\":[]}");
        Assert.Null(version.NameJson); Assert.Null(version.SubmitLabelJson); Assert.Null(version.Culture); Assert.Null(version.QuestionCount);
        version.SealPresentation("{\"es\":\"Original\",\"en\":\"Original\"}", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", 0);
        Assert.Equal("es", version.Culture); Assert.Equal(0, version.QuestionCount);
        Assert.Contains("Original", version.NameJson, StringComparison.Ordinal); Assert.Contains("Submit", version.SubmitLabelJson, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => version.SealPresentation("{}", "{}", "en", 1));
        Assert.Equal(id, version.Id); Assert.Equal(definition, version.DefinitionId); Assert.Equal(2, version.Number);
        Assert.Equal("synthetic-content-hash", version.ContentHash); Assert.Equal("{\"format\":1,\"questions\":[]}", version.SchemaJson);
        Assert.DoesNotContain("Original", version.ToString(), StringComparison.Ordinal);
    }
}
