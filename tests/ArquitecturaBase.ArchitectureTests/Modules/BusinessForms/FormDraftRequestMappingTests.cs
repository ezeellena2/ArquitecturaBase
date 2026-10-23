using System.Reflection;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
using ArquitecturaBase.Api.Modules.BusinessForms.Controllers;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;

public sealed class FormDraftRequestMappingTests
{
    private const string Mapper = "ArquitecturaBase.Api.Modules.BusinessForms.Contracts.FormDraftRequestMapping";
    private static readonly string[] DraftModels =
    [
        typeof(FormDraftInput).FullName!, typeof(FormFieldInput).FullName!, typeof(FormChoiceInput).FullName!,
        typeof(FormQuestionSource).FullName!, typeof(FormFileSettings).FullName!, typeof(FormSelectionSettings).FullName!,
    ];

    [Fact]
    public void Saving_checking_and_accepting_share_the_entire_draft_mapping()
    {
        var calls = CallSites.Calls(typeof(FormDefinitionsController).Assembly);
        foreach (var controller in new[] { typeof(FormDefinitionsController), typeof(FormPublicationChecksController), typeof(FormPublicationsController) })
            Assert.Contains(calls, call => call.Owner == controller.FullName && call.DeclaringType == Mapper && call.Method == "Map");
        Assert.Empty(DuplicatedDraftMappingOwners(calls));
    }

    [Fact]
    public void Mapping_rule_accepts_the_shared_mapper_and_rejects_a_controller_that_constructs_nested_draft_models()
    {
        var controller = typeof(FormDefinitionsController).FullName!;
        Assert.Empty(DuplicatedDraftMappingOwners([new(Mapper, typeof(FormFieldInput).FullName!, ".ctor")]));
        Assert.Equal([controller], DuplicatedDraftMappingOwners([new(controller, typeof(FormFieldInput).FullName!, ".ctor")]));
    }

    [Fact]
    public void The_shared_mapper_preserves_identities_localized_content_and_all_existing_question_settings()
    {
        var fieldId = Guid.CreateVersion7();
        var replacesFieldId = Guid.CreateVersion7();
        var choiceId = Guid.CreateVersion7();
        var catalogId = Guid.CreateVersion7();
        var labels = new Dictionary<string, string> { ["es"] = "Pregunta", ["en"] = "Question" };
        var help = new Dictionary<string, string?> { ["es"] = "Ayuda", ["en"] = null };
        var submit = new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" };
        var input = new FormDraftHttpRequest(1, "es", submit,
        [
            new("local-field", fieldId, replacesFieldId, "multiChoice", labels, help, true, 123,
                [new("local-choice", choiceId, labels)], new(catalogId), new(1, 4, "camera"), new(1, 3)),
        ]);

        var mapped = Map(input);

        Assert.Equal(1, mapped.Format);
        Assert.Equal("es", mapped.Culture);
        Assert.Equal(submit, mapped.SubmitLabel);
        var field = Assert.Single(mapped.Fields);
        Assert.Equal("local-field", field.LocalId);
        Assert.Equal(fieldId, field.FieldId);
        Assert.Equal(replacesFieldId, field.ReplacesFieldId);
        Assert.Equal("multiChoice", field.Type);
        Assert.Equal(labels, field.Label);
        Assert.Equal(help, field.Help);
        Assert.True(field.Required);
        Assert.Equal(123, field.MaxLength);
        var option = Assert.Single(field.Options!);
        Assert.Equal("local-choice", option.LocalId);
        Assert.Equal(choiceId, option.ChoiceId);
        Assert.Equal(labels, option.Label);
        Assert.Equal(catalogId, field.Source?.Catalog);
        Assert.Equal(new FormFileSettings(1, 4, "camera"), field.Files);
        Assert.Equal(new FormSelectionSettings(1, 3), field.Selection);
    }

    [Fact]
    public void The_shared_mapper_preserves_missing_values_for_the_application_validator()
    {
        Assert.Null(Map(null));
        Assert.Null(Map(new(1, "es", null, null!)).Fields);
        Assert.Null(Assert.Single(Map(new(1, "es", null, [null!])).Fields));
        var field = Assert.Single(Map(new(1, "es", null,
            [new("question", null, null, "choice", new Dictionary<string, string>(), null, false, null, [null!])])).Fields);
        Assert.Null(Assert.Single(field.Options!));
        Assert.Null(field.Source);
        Assert.Null(field.Files);
        Assert.Null(field.Selection);
    }

    private static string[] DuplicatedDraftMappingOwners(IEnumerable<CallSites.Call> calls) => calls
        .Where(call => call.Method == ".ctor" && DraftModels.Contains(call.DeclaringType, StringComparer.Ordinal)
            && call.Owner != Mapper && call.Owner.StartsWith("ArquitecturaBase.Api.Modules.BusinessForms.", StringComparison.Ordinal))
        .Select(call => call.Owner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static FormDraftInput Map(FormDraftHttpRequest? draft)
    {
        var mapper = typeof(FormDefinitionsController).Assembly.GetType(Mapper);
        Assert.NotNull(mapper);
        var method = mapper.GetMethod("Map", BindingFlags.Public | BindingFlags.Static, [typeof(FormDraftHttpRequest)]);
        Assert.NotNull(method);
        return (FormDraftInput)method.Invoke(null, [draft])!;
    }
}
