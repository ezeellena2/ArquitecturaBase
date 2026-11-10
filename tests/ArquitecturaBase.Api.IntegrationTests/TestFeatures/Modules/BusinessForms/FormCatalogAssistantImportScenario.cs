using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// Stage 2 of the assistant authoring: a host of its own for importing from the copy of a proposal's spreadsheet. The proposal port
// answers as a completed Home turn would, with the descriptor of its copy, and the copies sit in memory; the rest is real.
internal sealed class FormCatalogAssistantImportScenario(WebApplicationFactory<Program> host, ScriptedAssistantProposalLookup lookup,
    AssistantCopyFiles files, Guid authorId, Guid managerId, Guid readerId, Guid outsiderId, string suffix, DateTime occurredAtUtc) : IAsyncDisposable
{
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public ScriptedAssistantProposalLookup Lookup => lookup;
    public AssistantCopyFiles Files => files;
    public Guid AuthorId => authorId;
    // Another person who manages catalogs: the author's proposals are not theirs.
    public Guid ManagerId => managerId;
    public Guid ReaderId => readerId;
    public Guid OutsiderId => outsiderId;
    public string Suffix => suffix;

    public static async Task<FormCatalogAssistantImportScenario> CreateAsync(ApiFactory factory, CancellationToken ct)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var people = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync("import-manager-" + suffix, null, [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage], ct);
            await roles.CreateAsync("import-reader-" + suffix, null, [BusinessFormsPermissions.CatalogsRead], ct);
            var users = services.GetRequiredService<IUserRepository>();
            async Task<Guid> PersonAsync(string name, string? role)
            {
                var account = await users.CreateAsync(Email.Create(TestEmails.Unique(name)).Value, null, false, name + " " + suffix, "es", ct);
                if (role is not null) await users.SetRolesAsync(account.Id, [role + "-" + suffix], ct);
                return account.Id;
            }
            return (Author: await PersonAsync("import-author", "import-manager"), Manager: await PersonAsync("import-other", "import-manager"),
                Reader: await PersonAsync("import-reader", "import-reader"), Outsider: await PersonAsync("import-outsider", null));
        });
        var occurred = await factory.ExecuteScopeAsync(services => Task.FromResult(services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime));
        var lookup = new ScriptedAssistantProposalLookup();
        var files = new AssistantCopyFiles();
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IAssistantProposalLookup>(lookup));
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));
        return new(host, lookup, files, people.Author, people.Manager, people.Reader, people.Outsider, suffix, occurred);
    }

    public HttpClient Client(Guid actor)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    public HttpClient AnonymousClient() => host.CreateClient();

    // propose-catalog@2 with the copy of its turn's spreadsheet: the code from A, the option from B and «Teléfono» from C, with headers.
    public CreateFromAssistantProposalRequest MappedCatalog(string name, byte[] copy, string fileName, int rows, bool store = true) => Hand(
        new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, BusinessFormAuthoringAssistantCapabilitySource.MappedCatalogVersion),
        new FormAuthoringMappedCatalogProposal("es", name, "Empresas de transporte ficticias", "text", "CUIT", "Razón social", [new("Teléfono", "text")],
            Summary(fileName, rows, new FormAuthoringMappedColumn("field", "Teléfono", null, "C", "Teléfono"))), true, copy, fileName, store);

    // propose-catalog@1, without a spreadsheet.
    public CreateFromAssistantProposalRequest Catalog(string name) => Hand(new(BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, 1),
        new FormAuthoringCatalogProposal("es", name, "Empresas de transporte ficticias", "text", "CUIT", "Razón social", []), true, null, null, false);

    // propose-import@1 into a catalog, with the copy of its turn's spreadsheet: the code from A and the option from B.
    public CreateFromAssistantProposalRequest Import(Guid catalogId, string catalogName, byte[] copy, int rows, bool current = true, bool store = true) =>
        Hand(new(BusinessFormAuthoringAssistantCapabilitySource.ProposeImportId, 1), new FormAuthoringImportProposal(new(catalogId, catalogName, "text"),
            Summary("transportistas.csv", rows)), current, copy, "transportistas.csv", store);

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    public ValueTask DisposeAsync() => host.DisposeAsync();

    // The sample only goes to the card: the import reads the copy.
    private static FormAuthoringSpreadsheetSummary Summary(string fileName, int rows, params FormAuthoringMappedColumn[] fields) =>
        new(fileName, fileName.EndsWith(".csv", StringComparison.Ordinal) ? "" : "Options", rows, true,
            [new("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"), .. fields], []);

    private CreateFromAssistantProposalRequest Hand(AssistantCapabilityReference capability, AssistantCapabilityPayload payload, bool current,
        byte[]? copy, string? fileName, bool store)
    {
        var requestId = Guid.NewGuid();
        AssistantAttachmentDescriptor? attachment = null;
        if (copy is not null && fileName is not null)
        {
            var csv = fileName.EndsWith(".csv", StringComparison.Ordinal);
            attachment = new($"ai/assistant/attachments/{requestId:N}{(csv ? ".csv" : ".xlsx")}", fileName, csv ? "text/csv" : Xlsx, copy.LongLength, Sha256(copy));
            if (store) files.Put(attachment.StorageKey, copy);
        }
        var context = new AssistantChannelContext("home", authorId, null, "es", "America/Argentina/Buenos_Aires", new([], []), [capability],
            [new("assistant_answer", 2)]);
        var proposal = new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), capability, payload);
        lookup.Add(authorId, requestId, new(context, proposal, new("fictitious-profile", 3, "home-system", 2, occurredAtUtc, "home"), current)
            { Attachment = attachment });
        return new(requestId, proposal.Id);
    }
}
