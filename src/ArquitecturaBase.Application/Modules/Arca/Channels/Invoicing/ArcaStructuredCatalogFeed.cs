using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// ARCA as a feed of the forms catalogs: it reads a table of WSFEv1 with the ticket of the connection. Reading has no effect
/// outside the system; without a connection, a ticket or an answer it returns the stable code of why, and forms keeps what it had.
/// </summary>
internal sealed class ArcaStructuredCatalogFeed(ArcaServiceSessionIssuer access, IArcaInvoicingClient client, TimeProvider timeProvider) : IStructuredCatalogFeed
{
    private static readonly ArcaServiceCode Wsfe = ArcaServiceCode.Create("wsfe")!.Value;

    public IReadOnlyList<StructuredFeedCatalog> Catalogs => ArcaCatalogFeedPolicy.Catalogs;

    public async Task<StructuredFeedReadResponse> ReadAsync(string catalogKey, CancellationToken cancellationToken)
    {
        if (ArcaCatalogFeedPolicy.Table(catalogKey) is not { } table) return StructuredFeedReadResponse.Failed(ArcaInvoicingErrors.CatalogUnknown.Code);
        var session = await access.OpenAsync(Wsfe, cancellationToken);
        if (session.IsFailure) return StructuredFeedReadResponse.Failed(session.Error.Code);
        var reply = await client.ListParametersAsync(session.Value.Certificate, session.Value.Ticket, table, cancellationToken);
        return reply is { Status: ArcaServiceCheckStatus.Ready, Value: { } rows }
            ? new StructuredFeedReadResponse(ArcaCatalogFeedPolicy.Items(catalogKey, rows, ArcaIssueDateWindow.Today(timeProvider.GetUtcNow().UtcDateTime)))
            : StructuredFeedReadResponse.Failed(ArcaServiceSessionIssuer.Failure(reply.Status).Code);
    }
}
