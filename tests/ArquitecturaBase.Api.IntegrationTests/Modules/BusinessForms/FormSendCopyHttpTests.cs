using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// «SendCopy» with PostgreSQL, the worker run by hand and the app's durable mail queue: after the issuance, the copy goes by email to
// the receiver its issuer reads from the response, with the fiscal PDF attached and the datum of the result in its text; without a
// receiver it does not apply and nothing is queued.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_copy_goes_by_the_mail_queue_to_the_receiver_of_the_issuance_with_its_pdf_and_without_one_it_does_not_apply()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RecordsRead]);
        var issuer = new TestRecordIssuer();
        var files = new AssistantCopyFiles();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IStructuredRecordIssuer>();
            services.AddSingleton<IStructuredRecordIssuer>(issuer);
            services.RemoveAll<IPrivateFileStore>();
            services.AddSingleton<IPrivateFileStore>(files);
        }));
        using var client = InHost(host, author);
        var worker = host.Services.GetRequiredService<FormActionBackgroundService>();
        var (definitionId, versionId, amountId, addressId) = await SeedCopyFormAsync(author);
        byte[] pdf = [37, 80, 68, 70, 45, 49, 46, 55];
        // Each issuance leaves its own fiscal PDF in the private store.
        StructuredIssueResponse Issued()
        {
            var storageKey = $"test-issue/{Guid.NewGuid():N}.pdf";
            files.Put(storageKey, pdf);
            return new(StructuredIssueState.Issued, Result: new StructuredRecordResult(TestRecordIssuer.Key,
                    new Dictionary<string, string> { ["es"] = "Comprobante de prueba", ["en"] = "Test voucher" },
                    [new("number", new Dictionary<string, string> { ["es"] = "Número", ["en"] = "Number" }, StructuredRecordResultField.Text, "00004-00000012")]),
                Document: new StructuredRecordActionDocument(storageKey, "Factura C 00004-00000012.pdf", "application/pdf", pdf.Length,
                    Convert.ToHexStringLower(SHA256.HashData(pdf))));
        }
        issuer.Next = Issued();
        var address = $"cliente-{Guid.NewGuid():N}@example.com";

        var (issueRun, copyRun) = await SendAsync(client, definitionId, versionId, new object[] { new { fieldId = amountId, value = "10" }, new { fieldId = addressId, value = address } });
        await worker.ProcessNextAsync(issueRun, Ct);
        Assert.Equal(FormActionRunState.Completed, (await RunAsync(issueRun)).State);
        await worker.ProcessNextAsync(copyRun, Ct);

        Assert.Equal(FormActionRunState.Completed, (await RunAsync(copyRun)).State);
        var email = await factory.EmailSender.WaitForAsync(address);
        Assert.Equal(("Factura C 00004-00000012", "Te mandamos el comprobante 00004-00000012."), (email.Subject, email.TextBody));
        var attachment = Assert.Single(email.Attachments!);
        Assert.Equal(("Factura C 00004-00000012.pdf", "application/pdf"), (attachment.Name, attachment.ContentType));
        Assert.Equal(pdf, attachment.Content);
        await worker.ProcessNextAsync(copyRun, Ct);
        Assert.Equal(1, factory.EmailSender.CountFor(address));

        var (otherIssue, otherCopy) = await SendAsync(client, definitionId, versionId, new object[] { new { fieldId = amountId, value = "20" } });
        issuer.Next = Issued();
        await worker.ProcessNextAsync(otherIssue, Ct);
        Assert.Equal(FormActionRunState.Completed, (await RunAsync(otherIssue)).State);
        await worker.ProcessNextAsync(otherCopy, Ct);
        Assert.Equal(FormActionRunState.NotApplicable, (await RunAsync(otherCopy)).State);
    }

    // A response loaded in the app: its runs are the issuance and, after it, the copy.
    private async Task<(Guid IssueRun, Guid CopyRun)> SendAsync(HttpClient client, Guid definitionId, Guid versionId, object[] answers)
    {
        using var content = new MultipartFormDataContent
        {
            { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
            { new StringContent(versionId.ToString("D")), "versionId" },
            { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
        };
        using var sent = await client.PostAsync($"/api/forms/entries/{definitionId}", content, Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var runs = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().AsNoTracking().Where(run => run.RecordId == recordId)
            .OrderBy(run => run.Order).Select(run => new { run.Id, run.Kind }).ToListAsync(Ct));
        Assert.Equal(["IssueDocument", "SendCopy"], runs.Select(run => run.Kind));
        return (runs[0].Id, runs[1].Id);
    }

    // A form copied from the standard of the test issuer: the amount holds «first» and the text, «copyTo», where the issuer sends the
    // copy; its rule issues and then sends the copy with the number of the result.
    private async Task<(Guid DefinitionId, Guid VersionId, Guid AmountId, Guid AddressId)> SeedCopyFormAsync(HttpClient actorClient)
    {
        var sheet = await SeedSheetAsync(actorClient, 0);
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            definition.RecordStandard(TestRecordIssuer.Key, 1, FormDraftPolicy.Serialize(new Dictionary<string, Guid>
            {
                ["first"] = sheet.Fields[0].FieldId, ["copyTo"] = sheet.Fields[1].FieldId,
            }));
            var rule = new FormRule(Guid.NewGuid(), "Received", [],
            [
                new FormRuleAction(Guid.NewGuid(), "IssueDocument", null, null, false, TestRecordIssuer.Key),
                new FormRuleAction(Guid.NewGuid(), "SendCopy", null, new Dictionary<string, string> { ["es"] = "Te mandamos el comprobante {result:test.issue.number}." }, false),
            ]);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(ruleSet.Id);
            await db.SaveChangesAsync(Ct);
            return (definition.Id, version.Id, sheet.Fields[0].FieldId, sheet.Fields[1].FieldId);
        });
    }
}
