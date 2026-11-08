using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// IA3-A: a channel only says fixed texts, so the assistant declines with a reason that BusinessForms checks.
public sealed class FormAuthoringDeclineTests
{
    private const string Decline = BusinessFormAuthoringAssistantCapabilitySource.DeclineId;
    private static readonly Guid Rains = new("0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d6f");
    private static readonly Guid Contractors = new("0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d70");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Json(string reason, string? prefix = null, string? catalogId = null) =>
        JsonSerializer.Serialize(new { reason, prefix, catalogId }, JsonSerializerOptions.Web);

    private static AuthoringAssistantFixture Host(string[]? permissions = null)
    {
        var host = new AuthoringAssistantFixture(permissions);
        host.Reader.DefinitionsByPrefix["LLU"] = new(Rains, FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Parte de lluvias" }), "es");
        host.Reader.Catalogs.Add(AuthoringAssistantFixture.Catalog(Contractors, "Contratistas"));
        return host;
    }

    [Fact]
    public async Task Declining_names_a_real_reason_and_what_already_exists()
    {
        var withoutManage = Host([BusinessFormsPermissions.Read, BusinessFormsPermissions.CatalogsRead]);
        var cannot = await withoutManage.Source.ExecuteAsync(withoutManage.Request(Decline, Json("noPermission")), Ct);
        Assert.Equal(("proposed", new FormAuthoringDecline("noPermission", null, null, "")), (cannot.Value.State, (FormAuthoringDecline)cannot.Value.Payload));
        var host = Host();
        var form = await host.Source.ExecuteAsync(host.Request(Decline, Json("formExists", " llu ")), Ct);
        Assert.Equal(new FormAuthoringDecline("formExists", Rains, null, "Parte de lluvias"), form.Value.Payload);
        var catalog = await host.Source.ExecuteAsync(host.Request(Decline, Json("catalogExists", catalogId: Contractors.ToString())), Ct);
        Assert.Equal(new FormAuthoringDecline("catalogExists", null, Contractors, "Contratistas"), catalog.Value.Payload);
    }

    [Fact]
    public async Task What_already_exists_is_named_in_the_conversation_language_or_in_its_original()
    {
        var host = Host();
        host.Reader.DefinitionsByPrefix["LLU"] = new(Rains,
            FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Parte de lluvias", ["en"] = "Rain report" }), "es");
        var english = host.Context with { Culture = "en" };
        var form = await host.Source.ExecuteAsync(host.Request(Decline, Json("formExists", "LLU")) with { Context = english }, Ct);
        var catalog = await host.Source.ExecuteAsync(host.Request(Decline, Json("catalogExists", catalogId: Contractors.ToString())) with { Context = english }, Ct);
        Assert.Equal(new FormAuthoringDecline("formExists", Rains, null, "Rain report"), form.Value.Payload);
        Assert.Equal(new FormAuthoringDecline("catalogExists", null, Contractors, "Contratistas"), catalog.Value.Payload);
    }

    [Theory]
    [InlineData("noPermission", null, null, "reason", "permissionGranted")]
    [InlineData("noPermission", "LLU", null, "prefix", "invalid")]
    [InlineData("formExists", "OTR", null, "prefix", "notFound")]
    [InlineData("formExists", "LLU", "0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d70", "catalogId", "invalid")]
    [InlineData("catalogExists", null, "not-an-id", "catalogId", "notFound")]
    [InlineData("catalogExists", "LLU", null, "prefix", "invalid")]
    [InlineData("unclear", "LLU", null, "prefix", "invalid")]
    [InlineData("unclear", null, "0193f2c4-7a1e-7c10-9d4e-2b1f3a4c5d70", "catalogId", "invalid")]
    public async Task A_wrong_reason_comes_back_rejected_with_the_path_to_fix(string reason, string? prefix, string? catalogId, string path, string code)
    {
        var host = Host();
        var response = await host.Source.ExecuteAsync(host.Request(Decline, Json(reason, prefix, catalogId)), Ct);
        Assert.Equal("rejected", response.Value.State);
        Assert.Equal(new FormAuthoringIssue(path, code), Assert.Single(((FormAuthoringRejection)response.Value.Payload).Issues));
    }

    [Fact]
    public async Task Something_that_exists_is_named_only_to_whoever_can_read_it()
    {
        var host = Host([BusinessFormsPermissions.Manage, BusinessFormsPermissions.CatalogsManage]);
        foreach (var json in new[] { Json("formExists", "LLU"), Json("catalogExists", catalogId: Contractors.ToString()) })
        {
            var response = await host.Source.ExecuteAsync(host.Request(Decline, json), Ct);
            Assert.Equal(new FormAuthoringIssue("reason", "notReadable"), Assert.Single(((FormAuthoringRejection)response.Value.Payload).Issues));
        }
    }

    // «Pregunta y espera»: a request that does not say what to build is asked about, never guessed; asking only makes sense
    // to whoever can build a form or a catalog.
    [Fact]
    public async Task An_unclear_request_is_asked_about_when_the_person_can_build_a_form_or_a_catalog()
    {
        foreach (var permissions in new[] { new[] { BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage },
                     new[] { BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage } })
        {
            var host = Host(permissions);
            var asked = await host.Source.ExecuteAsync(host.Request(Decline, Json("unclear")), Ct);
            Assert.Equal(("proposed", new FormAuthoringDecline("unclear", null, null, "")), (asked.Value.State, (FormAuthoringDecline)asked.Value.Payload));
        }
        var readOnly = Host([BusinessFormsPermissions.Read, BusinessFormsPermissions.CatalogsRead]);
        var cannot = await readOnly.Source.ExecuteAsync(readOnly.Request(Decline, Json("unclear")), Ct);
        Assert.Equal(new FormAuthoringIssue("reason", "permissionMissing"), Assert.Single(((FormAuthoringRejection)cannot.Value.Payload).Issues));
    }

    [Fact]
    public void The_reasons_offered_to_the_model_are_the_ones_it_accepts()
    {
        foreach (var schema in new[] { FormAuthoringAssistantCodec.DeclineArgumentsSchema, FormAuthoringAssistantCodec.DeclineSchema })
        {
            using var document = JsonDocument.Parse(schema);
            Assert.Equal(FormAuthoringAssistantCodec.DeclineReasons, document.RootElement.GetProperty("properties").GetProperty("reason").GetProperty("enum")
                .EnumerateArray().Select(reason => reason.GetString()!).ToArray());
        }
        Assert.Contains("unclear", FormAuthoringAssistantCodec.DeclineReasons);
        Assert.Contains("permissionMissing", FormAuthoringAssistantCodec.IssueCodes);
    }

    [Fact]
    public void It_needs_no_permission_is_not_offered_in_Home_and_its_output_is_closed()
    {
        var source = new AuthoringAssistantFixture().Source;
        var definition = source.Definitions.Single(item => item.Id == Decline);
        Assert.Equal((AssistantCapabilityKind.Proposal, false), (definition.Kind, definition.OfferedInHome));
        Assert.Empty(definition.RequiredPermissions.Actor);
        var stored = JsonSerializer.Serialize(new FormAuthoringDecline("formExists", Rains, null, "Parte de lluvias"), JsonSerializerOptions.Web);
        Assert.Equal(new FormAuthoringDecline("formExists", Rains, null, "Parte de lluvias"), source.ParseOutput(new(Decline, 1), stored).Value);
        var mixed = JsonSerializer.Serialize(new FormAuthoringDecline("noPermission", Rains, null, ""), JsonSerializerOptions.Web);
        Assert.True(source.ParseOutput(new(Decline, 1), mixed).IsFailure);
        var unclear = JsonSerializer.Serialize(new FormAuthoringDecline("unclear", null, null, ""), JsonSerializerOptions.Web);
        Assert.Equal(new FormAuthoringDecline("unclear", null, null, ""), source.ParseOutput(new(Decline, 1), unclear).Value);
        var named = JsonSerializer.Serialize(new FormAuthoringDecline("unclear", null, Contractors, "Contratistas"), JsonSerializerOptions.Web);
        Assert.True(source.ParseOutput(new(Decline, 1), named).IsFailure);
    }

    [Fact]
    public void The_summary_carries_the_reason_and_the_app_path_of_what_exists()
    {
        var form = FormAuthoringSummaryPolicy.Declined(new("formExists", Rains, null, "Parte de lluvias"));
        Assert.Equal((StructuredAuthoringKind.Declined, "Parte de lluvias", "formExists", $"/formularios/{Rains:D}"), (form.Kind, form.Title, form.Reason, form.AppPath));
        Assert.Equal($"/catalogos/{Contractors:D}", FormAuthoringSummaryPolicy.Declined(new("catalogExists", null, Contractors, "Contratistas")).AppPath);
        Assert.Null(FormAuthoringSummaryPolicy.Declined(new("noPermission", null, null, "")).AppPath);
    }
}
