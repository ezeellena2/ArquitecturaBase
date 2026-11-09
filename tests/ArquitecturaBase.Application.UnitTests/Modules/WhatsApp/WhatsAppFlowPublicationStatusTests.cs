using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// What a failed publication says when Meta reports problems (2026-11-09). A problem on a question names it. One that the sealed
// mapping cannot place on any question is about what the app built, so it says that once instead of promising marked fields.
public sealed class WhatsAppFlowPublicationStatusTests
{
    private static readonly WhatsAppFlowStoredManifest Manifest = new(1, "request", "publication", new("waba", "phone", "v25.0", "configuration"), []);

    private static WhatsAppFlowOperationRow Failed(params WhatsAppFlowDiagnostic[] diagnostics) => new(Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), [], null,
        [new(Guid.CreateVersion7(), "es", "Remito", [], "1", "Failed", "Upload", "WhatsApp.Flow.ValidationFailed",
            JsonSerializer.Serialize(diagnostics, JsonSerializerOptions.Web))]);

    [Fact]
    public void Problems_that_Meta_reports_outside_the_questions_say_once_that_the_app_built_that_part()
    {
        var status = WhatsAppFlowPublicationPolicy.Status(Failed(new(null, null, "channel", "es"), new(null, null, "channel", "es")), Manifest);

        Assert.Equal("Failed", status.State);
        var issue = Assert.Single(status.Issues);
        Assert.Equal("channel", issue.Path);
        Assert.Null(issue.FieldId);
        Assert.Equal("WhatsApp.Flow.ValidationUnlocated", issue.Error.Code);
        Assert.Equal("es", issue.Culture);
    }

    [Fact]
    public void Problems_on_a_question_keep_naming_it()
    {
        var field = Guid.CreateVersion7();

        var status = WhatsAppFlowPublicationPolicy.Status(Failed(new(field, null, "draft.fields[0].label.es", "es"), new(null, null, "channel", "es")), Manifest);

        Assert.Equal(2, status.Issues.Count);
        Assert.Equal(field, status.Issues[0].FieldId);
        Assert.All(status.Issues, issue => Assert.Equal("WhatsApp.Flow.ValidationFailed", issue.Error.Code));
    }
}
