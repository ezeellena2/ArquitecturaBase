using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Las ediciones con control de revisión (N2): una cuenta, un rol y el perfil se guardan con la revisión que se leyó.
/// Los tests que no prueban ese control la leen como la pantalla, con un GET a la misma ruta, justo antes de guardar.
/// </summary>
internal static class RevisionedEdits
{
    /// <summary>
    /// PUT a <paramref name="url"/> con la revisión que el recurso tiene ahora, salvo que el cuerpo traiga la suya. Si la
    /// lectura no responde 200 (quien pide no puede leer, o el id no existe), manda 1, la revisión con la que nace todo.
    /// Sin cuerpo, manda el PUT tal cual.
    /// </summary>
    public static async Task<HttpResponseMessage> PutWithRevisionAsync(
        this HttpClient client, string url, string accessToken, object? body = null, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (body is null)
        {
            return await client.SendWithTokenAsync(HttpMethod.Put, url, accessToken, body: null, language);
        }

        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        if (!json.ContainsKey("expectedRevision"))
        {
            using var current = await client.GetWithTokenAsync(url, accessToken);
            json["expectedRevision"] = current.IsSuccessStatusCode
                ? (await current.ReadJsonAsync()).GetProperty("revision").GetInt64()
                : 1;
        }

        return await client.SendWithTokenAsync(HttpMethod.Put, url, accessToken, json, language);
    }
}
