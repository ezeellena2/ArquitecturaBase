using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;

/// <summary>Lo que respondió un servicio SOAP de ARCA: el contenido del cuerpo, una falla con su código, o nada útil.</summary>
internal abstract record SoapResult
{
    private SoapResult() { }

    public sealed record Success(XElement Content) : SoapResult;

    /// <summary>El código sin el prefijo de su espacio de nombres (<c>coe.alreadyAuthenticated</c>) y el texto de ARCA.</summary>
    public sealed record Fault(string Code, string Message) : SoapResult;

    public sealed record Unavailable : SoapResult;
}

/// <summary>
/// La pieza común de los servicios SOAP de ARCA, que usan el WSAA y las familias. Manda el sobre con el plazo de
/// <c>ArcaOptions.RequestTimeout</c>, lee como mucho <c>MaxResponseBytes</c>, parsea sin DTD ni resolver externo y nunca
/// deja la respuesta en un log. Un corte, un estado inesperado o un XML ilegible dan <see cref="SoapResult.Unavailable"/>.
/// </summary>
internal static class SoapEnvelope
{
    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public static async Task<SoapResult> SendAsync(HttpClient client, Uri endpoint, string soapAction, XElement operation,
        ArcaOptions options, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var envelope = new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soapenv", Soap),
            new XElement(Soap + "Header"), new XElement(Soap + "Body", operation));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
        };
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");
        using var timeout = new CancellationTokenSource(options.RequestTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
            // ARCA responde las fallas SOAP con un 500; cualquier otro estado que no sea éxito no trae nada que leer.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.InternalServerError) return new SoapResult.Unavailable();
            var body = await ReadAsync(response.Content, options.MaxResponseBytes, linked.Token);
            if (body is null || Parse(body) is not { } document) return new SoapResult.Unavailable();
            var content = document.Root?.Element(Soap + "Body")?.Elements().FirstOrDefault();
            if (content?.Name == Soap + "Fault") return Fault(content);
            return response.IsSuccessStatusCode && content is not null ? new SoapResult.Success(content) : new SoapResult.Unavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SoapResult.Unavailable();
        }
        catch (HttpRequestException)
        {
            return new SoapResult.Unavailable();
        }
    }

    /// <summary>Parsea XML de ARCA, también el que llega escapado adentro de un campo; null si no es XML válido.</summary>
    public static XDocument? Parse(string? xml) => xml is null ? null : Parse(Encoding.UTF8.GetBytes(xml));

    private static XDocument? Parse(byte[] xml)
    {
        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static SoapResult.Fault Fault(XElement fault)
    {
        var code = fault.Element("faultcode")?.Value.Trim() ?? string.Empty;
        return new SoapResult.Fault(code[(code.IndexOf(':', StringComparison.Ordinal) + 1)..], fault.Element("faultstring")?.Value.Trim() ?? string.Empty);
    }

    private static async Task<byte[]?> ReadAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
