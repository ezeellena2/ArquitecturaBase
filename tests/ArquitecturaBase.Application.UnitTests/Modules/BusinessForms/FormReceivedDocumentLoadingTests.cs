using System.Text.Json;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Task 8, decision 10: a channel reads documents only while some form published in a channel reads them, and a person loads one only
// with what the documents capabilities are offered by: an active account that completes forms. Without the owner, no form reads them.
public sealed class FormReceivedDocumentLoadingTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task With_a_form_that_reads_documents_a_person_who_completes_forms_can_load_one()
    {
        var f = new Fixture();
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto")]);

        Assert.Equal(new StructuredDocumentLoading(true, true), await f.Guard.FindLoadingAsync(f.Person, Ct));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("inactive")]
    [InlineData("unknown")]
    public async Task Someone_who_cannot_complete_forms_still_learns_that_a_form_reads_documents(string who)
    {
        var f = new Fixture();
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto")]);
        if (who == "permission") f.Permissions.Permissions[f.Person] = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage];
        if (who == "inactive") await f.Users.ArrangeAsync(users => users.SetActiveAsync(f.Person, false, Ct));

        var loading = await f.Guard.FindLoadingAsync(who == "unknown" ? Guid.NewGuid() : f.Person, Ct);

        Assert.Equal(new StructuredDocumentLoading(true, false), loading);
    }

    [Fact]
    public async Task Forms_without_reading_only_in_the_app_or_with_it_off_read_no_document()
    {
        var f = new Fixture();
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto")], reading: false);
        f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto")], appOnly: true);
        var off = f.Documents.AddForm([FormReceivedDocumentFixture.Question("decimal", "Monto")]);
        f.Documents.Forms[f.Documents.Forms.IndexOf(off)] = off with
        {
            DocumentReadingJson = JsonSerializer.Serialize(new FormDocumentReading(false, "", []), JsonSerializerOptions.Web),
        };

        Assert.Equal(StructuredDocumentLoading.None, await f.Guard.FindLoadingAsync(f.Person, Ct));
    }

    [Fact]
    public async Task Without_the_owner_no_form_reads_documents()
    {
        var loading = await new UnavailableStructuredDocumentChannel().FindLoadingAsync(Guid.NewGuid(), Ct);

        Assert.Equal(StructuredDocumentLoading.None, loading);
        Assert.Equal((false, false), (loading.AnyFormReads, loading.CanLoad));
    }

    private sealed class Fixture
    {
        public FormReceivedDocumentFixture Documents { get; } = new();
        public InMemoryUserAccounts Users { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public FormReceivedDocumentAccessGuard Guard { get; }
        public Guid Person { get; }

        public Fixture()
        {
            Person = Users.AddUser("person@example.invalid").Id;
            Permissions.Permissions[Person] = [BusinessFormsPermissions.RecordsCreate];
            Guard = new(new FakeCurrentUser(), Users, Permissions, Documents);
        }
    }
}
