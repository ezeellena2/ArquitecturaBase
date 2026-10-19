using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPublicationLanguageTests
{
    [Theory]
    [InlineData("es", "en", false)]
    [InlineData("en", "es", false)]
    [InlineData("es", "en", true)]
    [InlineData("en", "es", true)]
    public void Publication_targets_only_the_original_when_secondary_texts_are_absent_or_partial(string original, string secondary, bool partial)
    {
        var definition = Definition(original, secondary, partial ? "name" : null);

        var snapshot = FormPublicationSnapshotPolicy.ChannelRequest(definition);

        Assert.Equal([original], snapshot.TargetCultures);
        Assert.Empty(FormPublicationPolicy.MissingTranslations(definition));
    }


    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Complete_translation_targets_both_languages_and_reports_no_missing_texts(string original, string secondary)
    {
        var definition = Definition(original, secondary);

        var cultures = FormPublicationPolicy.CheckCultures(definition);

        Assert.Equal(["es", "en"], FormPublicationSnapshotPolicy.ChannelRequest(definition).TargetCultures);
        Assert.All(cultures, culture => { Assert.True(culture.IsComplete); Assert.Empty(culture.Missing); });
        Assert.Equal(original, Assert.Single(cultures, culture => culture.IsOriginal).Culture);
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Missing_secondary_texts_keep_exact_paths_and_stable_identities_without_blocking_the_original(string original, string secondary)
    {
        var definition = Definition(original, secondary, null);

        var report = FormPublicationPolicy.CheckCultures(definition).Single(culture => culture.Culture == secondary);

        Assert.False(report.IsOriginal);
        Assert.False(report.IsComplete);
        Assert.Equal(new[] { "name", "draft.submitLabel", "draft.fields[0].label", "draft.fields[0].help", "draft.fields[0].options[0].label" }
            .Select(path => path + "." + secondary), report.Missing.Select(issue => issue.Path));
        Assert.All(report.Missing, issue => { Assert.Equal(secondary, issue.Culture); Assert.Null(issue.ChannelId); });
        Assert.Equal(definition.Draft.Fields[0].FieldId, report.Missing[2].FieldId);
        Assert.Equal(definition.Draft.Fields[0].Options![0].ChoiceId, report.Missing[4].ChoiceId);
        Assert.Empty(FormPublicationPolicy.MissingTranslations(definition));
    }

    [Theory]
    [InlineData("es", "en", "name")]
    [InlineData("en", "es", "name")]
    [InlineData("es", "en", "submitLabel")]
    [InlineData("en", "es", "submitLabel")]
    [InlineData("es", "en", "label")]
    [InlineData("en", "es", "label")]
    [InlineData("es", "en", "help")]
    [InlineData("en", "es", "help")]
    [InlineData("es", "en", "option")]
    [InlineData("en", "es", "option")]
    public void One_missing_secondary_text_excludes_that_whole_language(string original, string secondary, string part)
    {
        var definition = WithoutText(Definition(original, secondary), secondary, part);

        var report = FormPublicationPolicy.CheckCultures(definition).Single(culture => culture.Culture == secondary);

        Assert.False(report.IsComplete);
        Assert.Single(report.Missing);
        Assert.Equal([original], FormPublicationSnapshotPolicy.ChannelRequest(definition).TargetCultures);
        Assert.Empty(FormPublicationPolicy.MissingTranslations(definition));
    }

    [Theory]
    [InlineData("es", "en", "name")]
    [InlineData("en", "es", "name")]
    [InlineData("es", "en", "submitLabel")]
    [InlineData("en", "es", "submitLabel")]
    [InlineData("es", "en", "label")]
    [InlineData("en", "es", "label")]
    [InlineData("es", "en", "help")]
    [InlineData("en", "es", "help")]
    [InlineData("es", "en", "option")]
    [InlineData("en", "es", "option")]
    public void A_missing_original_text_remains_a_publication_blocker(string original, string secondary, string part)
    {
        var definition = WithoutText(Definition(original, secondary), original, part);

        var missing = Assert.Single(FormPublicationPolicy.MissingTranslations(definition));

        Assert.Equal(original, missing.Culture);
        Assert.False(FormPublicationPolicy.CheckCultures(definition).Single(culture => culture.IsOriginal).IsComplete);
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Absent_optional_help_does_not_require_a_translation(string original, string secondary)
    {
        var definition = Definition(original, secondary);
        definition = definition with { Draft = definition.Draft with { Fields = [definition.Draft.Fields[0] with { Help = null }] } };

        Assert.All(FormPublicationPolicy.CheckCultures(definition), culture => Assert.True(culture.IsComplete));
    }

    private static FormDefinitionResponse WithoutText(FormDefinitionResponse definition, string culture, string part)
    {
        IReadOnlyDictionary<string, string> Without(IReadOnlyDictionary<string, string> texts) =>
            texts.Where(pair => pair.Key != culture).ToDictionary(pair => pair.Key, pair => pair.Value);
        var field = definition.Draft.Fields[0];
        return part switch
        {
            "name" => definition with { Name = Without(definition.Name) },
            "submitLabel" => definition with { Draft = definition.Draft with { SubmitLabel = Without(definition.Draft.SubmitLabel) } },
            "label" => definition with { Draft = definition.Draft with { Fields = [field with { Label = Without(field.Label) }] } },
            "help" => definition with { Draft = definition.Draft with { Fields = [field with { Help = Without(field.Help!) }] } },
            _ => definition with { Draft = definition.Draft with { Fields = [field with { Options = [field.Options![0] with { Label = Without(field.Options[0].Label) }] }] } },
        };
    }
    internal static FormDefinitionResponse Definition(string original, string secondary, string? translated = "all")
    {
        IReadOnlyDictionary<string, string> Text(string part) => translated == "all" || translated == part
            ? new Dictionary<string, string> { [original] = "Original", [secondary] = "Translation" }
            : new Dictionary<string, string> { [original] = "Original" };
        var fieldId = Guid.CreateVersion7();
        return new(Guid.CreateVersion7(), Text("name"), "FORM", false, 1,
            new(1, original, Text("submitLabel"), [new(fieldId, "field_" + fieldId.ToString("N"), "choice",
                Text("label"), Text("help"), true, null, [new(Guid.CreateVersion7(), Text("option"))])]));
    }
}
