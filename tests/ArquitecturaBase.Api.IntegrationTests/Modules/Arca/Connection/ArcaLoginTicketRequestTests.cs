using System.Text;
using System.Xml.Linq;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

public sealed class ArcaLoginTicketRequestTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 11, 3, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_request_asks_for_the_service_with_a_window_in_argentine_time()
    {
        var root = Parse(ArcaLoginTicketRequest.Build("wsfe", NowUtc));

        Assert.Equal("loginTicketRequest", root.Name.LocalName);
        Assert.Equal("1.0", (string?)root.Attribute("version"));
        Assert.Equal("2026-11-03T09:50:00-03:00", (string?)root.Element("header")?.Element("generationTime"));
        Assert.Equal("2026-11-03T10:10:00-03:00", (string?)root.Element("header")?.Element("expirationTime"));
        Assert.Equal("wsfe", (string?)root.Element("service"));
    }

    [Fact]
    public void Two_requests_in_a_row_have_different_unique_ids()
    {
        static string UniqueId() => Parse(ArcaLoginTicketRequest.Build("wsfe", NowUtc)).Element("header")!.Element("uniqueId")!.Value;

        Assert.NotEqual(UniqueId(), UniqueId());
    }

    private static XElement Parse(byte[] request) => XDocument.Parse(Encoding.UTF8.GetString(request)).Root!;
}
