using System.Net;
using System.Security.Cryptography;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «SendCopy»: the copy goes to the receiver the issuer reads from the bound data, in the language of the form, with the document
/// of the issuance of its rule attached; without a receiver or a document it does not apply. Nothing leaves here: the email is
/// queued with the confirmation of the run, so it is queued once.
/// </summary>
internal sealed class FormIssueCopyService(IEnumerable<IStructuredRecordIssuer> issuers, FormIssueRequestPolicy requests, IPrivateFileStore files,
    IFormRecordResultReader results) : IFormIssueCopyService
{
    public async Task<FormActionOutcomeResponse> CopyAsync(FormActionWorkResponse work, CancellationToken cancellationToken)
    {
        if (issuers.FirstOrDefault(issuer => issuer.IssuerId == work.Snapshot.IssuerId) is not { } issuer)
            return new(FormActionRunState.Failed, FormActionErrors.IssuerUnavailable.Code, true);
        if (work.PdfArtifacts.Count == 0) return new(FormActionRunState.NotApplicable);
        var document = work.PdfArtifacts[0];
        if (await requests.RequestAsync(work.RunId, work.RecordId, issuer, cancellationToken) is not var (request, culture))
            return new(FormActionRunState.Failed, FormActionErrors.IssueDataUnavailable.Code);
        if (issuer.CopyRecipient(request.Data) is not { } address) return new(FormActionRunState.NotApplicable);
        var content = await ReadAsync(document, cancellationToken);
        var body = FormActionResultTokenPolicy.Resolve(work.Snapshot.CopyBody ?? throw new InvalidOperationException("The copy text is missing."),
            await results.ListAsync([work.RecordId], cancellationToken), culture);
        return new(FormActionRunState.Completed)
        {
            Email = new EmailMessage(address, Path.GetFileNameWithoutExtension(document.Name),
                "<p>" + WebUtility.HtmlEncode(body).Replace("\n", "<br>", StringComparison.Ordinal) + "</p>", body)
            {
                Attachments = [new EmailAttachment(document.Name, document.ContentType, content)],
            },
        };
    }

    // The document as it was confirmed: its size and its SHA-256.
    private async Task<byte[]> ReadAsync(FormPdfArtifact document, CancellationToken cancellationToken)
    {
        await using var stream = await files.OpenReadAsync(document.StorageKey, cancellationToken)
            ?? throw new InvalidOperationException("The document of the issuance is missing from the private store.");
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        var content = copy.ToArray();
        if (content.LongLength != document.Size || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(content)), document.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The document of the issuance does not match what was confirmed.");
        return content;
    }
}
