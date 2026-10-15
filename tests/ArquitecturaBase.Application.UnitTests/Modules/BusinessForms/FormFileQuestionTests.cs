using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormFileQuestionTests
{
    [Theory]
    [InlineData("photo", "cameraOrGallery")]
    [InlineData("photo", "camera")]
    [InlineData("document", null)]
    public void File_settings_survive_normalization_and_identity_resolution(string type, string? source)
    {
        var request = Request(type, new(1, 3, source));
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var resolved = FormDraftPolicy.Resolve(FormDraftPolicy.Normalize(request), null, FormRetiredIdentities.Empty);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(request.Draft.Fields[0].Files, resolved.Value.Draft.Fields[0].Files);
    }

    [Theory]
    [InlineData("photo", 0, 1, "camera", true)]
    [InlineData("photo", 1, 0, "camera", true)]
    [InlineData("photo", 2, 1, "camera", true)]
    [InlineData("photo", 1, 11, "camera", true)]
    [InlineData("photo", 1, 1, "gallery", true)]
    [InlineData("document", 1, 1, "camera", true)]
    [InlineData("shortText", 1, 1, null, false)]
    public void Invalid_file_settings_are_rejected_with_the_question_path(string type, int minimum, int maximum, string? source, bool required)
    {
        var request = Request(type, new(minimum, maximum, source));
        request = request with { Draft = request.Draft with { Fields = [request.Draft.Fields[0] with { Required = required }] } };
        Assert.Contains(new SaveFormDraftRequestValidator().Validate(request).Errors, error => error.PropertyName == "draft.fields[0].files");
    }

    [Fact]
    public void Drafts_above_ten_files_can_be_saved_but_cannot_be_published()
    {
        var request = Request("photo", new(1, 6, "camera"));
        request = request with { Draft = request.Draft with { Fields = [request.Draft.Fields[0], request.Draft.Fields[0] with { LocalId = "other" }] } };
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
        var draft = FormDraftPolicy.Resolve(request, null, FormRetiredIdentities.Empty).Value.Draft;
        var definition = new FormDefinitionResponse(Guid.CreateVersion7(), Texts, "PHOTO", false, 1, draft);
        Assert.Contains(FormPublicationPolicy.ValidateFiles(definition, true), issue => issue.Code == "Forms.Publication.TooManyFiles");
        Assert.Contains(FormPublicationPolicy.ValidateFiles(definition, false), issue => issue.Code == "Forms.Publication.FileStorageUnavailable");
    }

    private static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string> { ["es"] = "Foto", ["en"] = "Photo" };
    private static SaveFormDraftRequest Request(string type, FormFileSettings files) => new(Guid.CreateVersion7(), Texts, "PHOTO",
        new(1, "es", Texts, [new("photo", null, null, type, Texts, null, true, null, null, Files: files)]));
}
