using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;

public sealed class FormVersionPresentationTests
{
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Published_cultures_are_copied_once_from_the_verified_publication_instead_of_available_names(string original, string secondary)
    {
        var version = BusinessFormVersion.Create(Guid.NewGuid(), 1, "sealed-hash");
        version.SealSchema("sealed-schema");
        version.SealPresentation("{\"es\":\"Original\",\"en\":\"Translation\"}", "{}", original, 0);
        string[] cultures = [original];

        version.SealPublishedCultures(cultures);
        cultures[0] = secondary;

        Assert.NotNull(version.PublishedCultures);
        Assert.Equal([original], version.PublishedCultures);
        Assert.Throws<InvalidOperationException>(() => version.SealPublishedCultures([original, secondary]));
        Assert.Equal("sealed-hash", version.ContentHash);
        Assert.Equal("sealed-schema", version.SchemaJson);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Published_cultures_preserve_both_verified_languages(string original)
    {
        var version = BusinessFormVersion.Create(Guid.NewGuid(), 1, "sealed-hash");
        version.SealPresentation("{}", "{}", original, 0);

        version.SealPublishedCultures(["en", "es"]);

        Assert.NotNull(version.PublishedCultures);
        Assert.Equal(["en", "es"], version.PublishedCultures);
    }

    [Theory]
    [InlineData("")]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("es,es")]
    [InlineData("es,fr")]
    public void Publication_cultures_require_the_original_and_unique_supported_languages(string input)
    {
        var version = BusinessFormVersion.Create(Guid.NewGuid(), 1, "sealed-hash");
        version.SealPresentation("{}", "{}", "es", 0);
        var cultures = input.Length == 0 ? [] : input.Split(',');

        Assert.Throws<ArgumentException>(() => version.SealPublishedCultures(cultures));

        Assert.Null(version.PublishedCultures);
    }

    [Fact]
    public void Publication_cultures_cannot_precede_the_original_presentation()
    {
        var version = BusinessFormVersion.Create(Guid.NewGuid(), 1, "sealed-hash");

        Assert.Throws<InvalidOperationException>(() => version.SealPublishedCultures(["es"]));
    }

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
