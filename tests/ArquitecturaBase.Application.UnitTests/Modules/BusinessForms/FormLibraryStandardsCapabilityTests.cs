using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC6: forms.library.standards@1 shows the library standards that read documents, so propose-form@2 names a similar one by its key;
// only the documents channel offers it, with a document, and Home never does.
public sealed class FormLibraryStandardsCapabilityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AssistantCapabilityReference Standards = new(BusinessFormLibraryAssistantCapabilitySource.StandardsId, 1);

    private sealed class Host
    {
        public InMemoryUserAccounts Users { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public Guid Actor { get; }
        public BusinessFormLibraryAssistantCapabilitySource Source { get; }

        // The module's own standard, which reads receipts, next to another module's that reads nothing.
        public Host(string[]? permissions = null, bool active = true)
        {
            Actor = Users.AddUser("library@example.test", isActive: active).Id;
            Permissions.Permissions[Actor] = permissions ?? [BusinessFormsPermissions.Read];
            var receipt = new BusinessFormStandardSource().Standards.Single();
            Source = new([new BusinessFormStandardSource(), new Fixed(receipt with { Key = "module.invoice", Reading = null })], Users, Permissions);
        }

        public AssistantCapabilityRequest Request(string culture = "es", string json = "{}") => new(Guid.NewGuid(),
            new AssistantChannelContext("forms-documents", Actor, null, culture, "America/Argentina/Buenos_Aires", new([], []), [Standards],
                [new("assistant_proposal", 1)]), Standards, json, []);
    }

    private sealed class Fixed(StructuredFormStandard standard) : IStructuredFormStandardSource
    {
        public IReadOnlyList<StructuredFormStandard> Standards { get; } = [standard];
    }

    [Fact]
    public void It_is_a_read_of_the_documents_channel_that_Home_never_offers()
    {
        var definition = Assert.Single(new Host().Source.Definitions);
        Assert.Equal((Standards.Id, 1, AssistantCapabilityKind.Read, false, AssistantCapabilityAttachment.Document),
            (definition.Id, definition.Version, definition.Kind, definition.OfferedInHome, definition.Attachment));
        Assert.Equal([BusinessFormsPermissions.Read], definition.RequiredPermissions.Actor);
        Assert.Empty(definition.RequiredPermissions.Subject);
    }

    [Theory]
    [InlineData("es", "Comprobante de transferencia", "Monto")]
    [InlineData("en", "Transfer receipt", "Amount")]
    public async Task It_lists_only_the_standards_that_read_documents_with_what_they_read(string culture, string name, string amount)
    {
        var host = new Host();
        var result = await host.Source.ExecuteAsync(host.Request(culture), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormLibraryStandardsPayload>(result.Value.Payload);
        var standard = Assert.Single(payload.Standards);
        Assert.Equal(("complete", true, 1), (result.Value.State, payload.IsComplete, result.Value.ItemCount));
        Assert.Equal(("forms.transfer-receipt", name), (standard.Key, standard.Name));
        // The data it reads, by the names of its questions; the receipt itself is the document, not a datum.
        Assert.Contains(amount, standard.Reads);
        Assert.Equal(14, standard.Reads.Count);
        Assert.DoesNotContain(culture == "es" ? "Comprobante" : "Receipt", standard.Reads);
    }

    [Fact]
    public async Task It_needs_an_active_account_that_reads_forms_and_no_arguments()
    {
        Assert.Equal(FormDefinitionErrors.Forbidden, (await Executed(new Host(permissions: []))).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden, (await Executed(new Host(active: false))).Error);
        var host = new Host();
        Assert.Equal(FormDefinitionErrors.ContentInvalid, (await host.Source.ExecuteAsync(host.Request(json: "{\"key\":\"x\"}"), Ct)).Error);
        Assert.Equal(FormDefinitionErrors.ContentInvalid, (await host.Source.ExecuteAsync(host.Request("fr"), Ct)).Error);
    }

    private static Task<ArquitecturaBase.Domain.Results.Result<AssistantCapabilityResponse>> Executed(Host host) => host.Source.ExecuteAsync(host.Request(), Ct);

    [Fact]
    public async Task The_owner_codec_reads_it_back_exactly()
    {
        var host = new Host();
        var json = JsonSerializer.Serialize((FormLibraryStandardsPayload)(await host.Source.ExecuteAsync(host.Request(), Ct)).Value.Payload, JsonSerializerOptions.Web);
        Assert.IsType<FormLibraryStandardsPayload>(host.Source.ParseOutput(Standards, json).Value);
        Assert.True(host.Source.ParseOutput(Standards, json.Replace("\"reads\"", "\"x\":1,\"reads\"", StringComparison.Ordinal)).IsFailure);
        Assert.True(host.Source.ParseOutput(new(Standards.Id, 2), json).IsFailure);
        var repeated = new FormLibraryStandardsPayload(true, [new("a", "A", "", []), new("a", "B", "", [])]);
        Assert.True(host.Source.ParseOutput(Standards, JsonSerializer.Serialize(repeated, JsonSerializerOptions.Web)).IsFailure);
    }
}
