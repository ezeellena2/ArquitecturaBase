using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
internal static class LlmModelCatalog
{
    public const int MaxModels = 2000;
    public static Result<IReadOnlyList<AiModelResponse>> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > MaxModels)
            return LlmErrors.ResponseInvalid;
        var models = new List<AiModelResponse>();
        foreach (var item in data.EnumerateArray())
        {
            var id = LlmHttpTransport.Text(item, "id");
            if (id is not { Length: > 0 and <= 150 } || !char.IsAsciiLetterOrDigit(id[0])
                || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or ':' or '/' or '-')) return LlmErrors.ResponseInvalid;
            var name = LlmHttpTransport.Text(item, "display_name");
            models.Add(new(id, name is { Length: > 0 and <= 200 } ? name : id));
        }
        return Result.Success(Sort(models));
    }
    public static IReadOnlyList<AiModelResponse> Sort(IEnumerable<AiModelResponse> models) =>
        models.DistinctBy(x => x.Id, StringComparer.Ordinal).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
}
