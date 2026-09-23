using System.Buffers;
using ArquitecturaBase.Api.ErrorHandling;
using ArquitecturaBase.Api.RateLimiting;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.WhatsApp.ReceiveWebhook;
using ArquitecturaBase.Domain.Results;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace ArquitecturaBase.Api.Endpoints.Webhooks;

/// <summary>
/// El webhook de WhatsApp (sección 7 del spec del ingreso con WhatsApp). Lo llama Meta, así que es anónimo: lo que
/// prueba que viene de Meta es la palabra de verificación en el GET y la firma en cada POST. Nunca abre una sesión ni
/// llama a Meta (sección 5): guarda lo que llegó, despierta al procesador y responde. Sin WhatsApp o sin sus dos
/// secretos, las rutas no existen y responden el 404 del framework.
/// </summary>
internal sealed partial class WhatsAppWebhookEndpoints : IEndpoint
{
    public const string Route = "/webhooks/whatsapp";

    /// <summary>Meta agrupa hasta 1000 novedades en un webhook: 5 MB alcanzan de sobra.</summary>
    public const int MaxBodyBytes = 5 * 1024 * 1024;

    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string SubscribeMode = "subscribe";
    private const int ReadChunkBytes = 16 * 1024;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        if (!app.ServiceProvider.GetRequiredService<IWhatsAppAvailability>().IsWebhookEnabled)
        {
            return;
        }

        var group = app.MapGroup(Route)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.WhatsAppWebhookPolicy)
            .WithTags("Webhooks");

        group.MapGet("", Verify);
        group.MapPost("", ReceiveAsync);
    }

    /// <summary>
    /// La verificación que hace Meta al configurar el webhook: si el modo es "subscribe" y la palabra coincide, se le
    /// devuelve el challenge tal cual, en texto plano. Si no, 403. La palabra viaja en la query string porque así la
    /// manda Meta: por eso el log de ASP.NET Core, que incluye la query en "Request starting", queda en Warning.
    /// </summary>
    private static IResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IWhatsAppSignatureValidator validator,
        ILogger<WhatsAppWebhookEndpoints> logger)
    {
        if (string.Equals(mode, SubscribeMode, StringComparison.Ordinal)
            && validator.IsValidVerifyToken(verifyToken)
            && !string.IsNullOrEmpty(challenge))
        {
            LogVerified(logger);

            return TypedResults.Text(challenge, "text/plain");
        }

        LogVerificationRejected(logger);

        return TypedResults.StatusCode(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Un aviso de Meta: lee el cuerpo crudo (la firma es sobre esos bytes), valida la firma, lo lee y lo guarda. Todo
    /// lo que tenga la firma correcta responde 200, aunque no traiga nada que guardar: cualquier otra cosa hace que Meta
    /// lo reintente durante días.
    /// </summary>
    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        IWhatsAppSignatureValidator validator,
        IWhatsAppWebhookReader reader,
        ICommandHandler<ReceiveWhatsAppWebhookCommand> handler,
        IWhatsAppInboundSignal inboundSignal,
        IServiceScopeFactory scopes,
        ILogger<WhatsAppWebhookEndpoints> logger,
        CancellationToken cancellationToken)
    {
        if (await ReadBodyAsync(context, cancellationToken) is not { } body)
        {
            LogTooLarge(logger, MaxBodyBytes);

            return TypedResults.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        if (!validator.IsValidSignature(body, context.Request.Headers[SignatureHeader]))
        {
            // Sin el cuerpo ni la firma: solo que se rechazó y cuánto medía.
            LogSignatureRejected(logger, body.Count);

            return TypedResults.StatusCode(StatusCodes.Status401Unauthorized);
        }

        var command = new ReceiveWhatsAppWebhookCommand(reader.Read(body));
        Result result;

        try
        {
            result = await handler.Handle(command, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // Otro pedido guardó lo mismo entre que este miró y guardó: un reintento de Meta que se cruzó con el
            // original sin compartir un lock. No se guardó nada de este pedido y la transacción ya se deshizo, así que
            // se procesa de nuevo, en otro scope con un contexto limpio: lo repetido se saltea y lo nuevo del mismo
            // lote se guarda. Se hace acá y no en el caso de uso porque hace falta otro scope, y el del pedido es este.
            // Si choca otra vez, sale el 500 y Meta reintenta más tarde.
            LogConcurrentDuplicate(logger);

            await using var scope = scopes.CreateAsyncScope();
            result = await scope.ServiceProvider
                .GetRequiredService<ICommandHandler<ReceiveWhatsAppWebhookCommand>>()
                .Handle(command, cancellationToken);
        }

        if (!result.IsSuccess)
        {
            return result.Error.ToProblem();
        }

        // Recién ahora, con los mensajes ya guardados: el procesador los busca en la tabla. Si el aviso se pierde, la
        // revisión periódica los encuentra igual.
        if (command.Batch.Messages.Count > 0)
        {
            inboundSignal.Notify();
        }

        // El caso de uso siempre termina bien: el 200 va sin cuerpo, como lo espera Meta.
        return TypedResults.Ok();
    }

    /// <summary>
    /// El cuerpo crudo, hasta <see cref="MaxBodyBytes"/>, o null si es más largo. Se controla con el Content-Length, con
    /// el límite de Kestrel y mientras se lee: sin Content-Length (por partes) o bajo TestServer, donde el límite de
    /// Kestrel no existe, lo que cuenta es lo leído.
    /// </summary>
    private static async Task<ArraySegment<byte>?> ReadBodyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        if (request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        // Sin capacidad inicial: el Content-Length lo manda el cliente, que todavía no probó nada (la firma se valida
        // después de leer). Reservar lo que dice sería reservar antes de que llegue un byte; así la memoria crece con
        // lo que de verdad llega.
        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ReadChunkBytes);

        try
        {
            int read;

            while ((read = await request.Body.ReadAsync(chunk.AsMemory(), cancellationToken)) > 0)
            {
                if (buffer.Length + read > MaxBodyBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        // Sin copiarlo: los bytes escritos, sobre el mismo arreglo del stream, que sigue vivo en el segmento.
        return buffer.TryGetBuffer(out var written) ? written : buffer.ToArray();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Meta verified the WhatsApp webhook")]
    private static partial void LogVerified(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook verification: wrong mode or verify token")]
    private static partial void LogVerificationRejected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook of {Length} bytes with a missing or invalid signature")]
    private static partial void LogSignatureRejected(ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a WhatsApp webhook larger than {MaxBytes} bytes")]
    private static partial void LogTooLarge(ILogger logger, int maxBytes);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Another request saved part of a WhatsApp webhook at the same time; handling it again, skipping what is already saved")]
    private static partial void LogConcurrentDuplicate(ILogger logger);
}
